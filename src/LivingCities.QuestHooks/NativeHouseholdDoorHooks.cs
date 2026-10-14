using System;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using HarmonyLib;

namespace LivingCities.QuestHooks
{
    public sealed class NativeHouseholdDoorHooks : IDisposable
    {
        private const string Owner = "local.livingcities.household-doors";
        private static NativeHouseholdDoorHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<BuildingSummary, bool?> access;
        public NativeHouseholdDoorHooks(Func<BuildingSummary, bool?> access)
        {
            if (active != null) throw new InvalidOperationException("Household door hooks already installed.");
            this.access = access ?? throw new ArgumentNullException(nameof(access));
            active = this;
            try
            {
                harmony.Patch(typeof(PlayerActivate).GetMethod("BuildingIsUnlocked"), prefix:
                    new HarmonyMethod(typeof(NativeHouseholdDoorHooks).GetMethod("BeforeAccess", BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch { Dispose(); throw; }
        }
        private static bool BeforeAccess(BuildingSummary __0, ref bool __result)
        {
            bool? value = active == null ? null : active.access(__0);
            if (!value.HasValue) return true;
            __result = value.Value;
            return false;
        }
        public void Dispose()
        {
            if (active != this) return;
            harmony.UnpatchAll(Owner); active = null;
        }
    }
}
