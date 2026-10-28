using System;
using System.Reflection;
using DaggerfallWorkshop.Game.Entity;
using HarmonyLib;
using UnityEngine;

namespace PeopleOfTheIliacBay.QuestHooks
{
    // The owner must provide real roster actors for single-spawn callers. Vanilla
    // combat immediately dereferences the returned object; null is not suppression.
    public sealed class NativeGuardHooks : IDisposable
    {
        private const string Owner = "local.peopleoftheiliacbay.finite-guards";
        private static NativeGuardHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<bool> ownsTown;
        private readonly Action<bool> report;
        private readonly Func<Vector3, Vector3, GameObject> resolveActor;
        private readonly Action<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour> attacked;

        public NativeGuardHooks(Func<bool> ownsTown, Action<bool> report, Func<Vector3, Vector3, GameObject> resolveActor,
            Action<DaggerfallEntityBehaviour,DaggerfallEntityBehaviour> attacked)
        {
            if (active != null) throw new InvalidOperationException("Guard hooks already installed.");
            this.ownsTown = ownsTown ?? throw new ArgumentNullException(nameof(ownsTown));
            this.report = report ?? throw new ArgumentNullException(nameof(report));
            this.resolveActor = resolveActor ?? throw new ArgumentNullException(nameof(resolveActor));
            this.attacked = attacked ?? throw new ArgumentNullException(nameof(attacked));
            active = this;
            try
            {
                var flags = BindingFlags.Static | BindingFlags.NonPublic;
                harmony.Patch(typeof(PlayerEntity).GetMethod("SpawnCityGuards", new[] { typeof(bool) }),
                    prefix: new HarmonyMethod(typeof(NativeGuardHooks).GetMethod("BeforeBatch", flags)));
                harmony.Patch(typeof(PlayerEntity).GetMethod("SpawnCityGuard", new[] { typeof(Vector3), typeof(Vector3) }),
                    prefix: new HarmonyMethod(typeof(NativeGuardHooks).GetMethod("BeforeSingle", flags)));
                harmony.Patch(typeof(PlayerEntity).GetMethod("MakeNPCGuardsIntoEnemiesIfGuardsSpawned", BindingFlags.Instance | BindingFlags.NonPublic),
                    prefix: new HarmonyMethod(typeof(NativeGuardHooks).GetMethod("BeforePoolConversion", flags)));
                harmony.Patch(typeof(DaggerfallEntityBehaviour).GetMethod("HandleAttackFromSource",new[] {typeof(DaggerfallEntityBehaviour)}),
                    postfix:new HarmonyMethod(typeof(NativeGuardHooks).GetMethod("AfterAttack",flags)));
            }
            catch { Dispose(); throw; }
        }

        private static bool BeforeBatch(bool __0)
        {
            var owner = active;
            if (owner == null || !owner.ownsTown()) return true;
            owner.report(__0);
            return false;
        }

        private static bool BeforePoolConversion()
        { return active == null || !active.ownsTown(); }

        private static void AfterAttack(DaggerfallEntityBehaviour __instance,DaggerfallEntityBehaviour sourceEntityBehaviour)
        {
            var owner=active;
            if(owner!=null && owner.ownsTown())owner.attacked(__instance,sourceEntityBehaviour);
        }

        private static bool BeforeSingle(Vector3 __0, Vector3 __1, ref GameObject __result)
        {
            var owner = active;
            if (owner == null || !owner.ownsTown()) return true;
            var actor = owner.resolveActor(__0, __1);
            if (!actor) throw new InvalidOperationException("Owned guard spawn must resolve a real roster actor.");
            __result = actor;
            return false;
        }

        public void Dispose()
        {
            if (active != this) return;
            harmony.UnpatchAll(Owner);
            active = null;
        }
    }
}
