using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using DaggerfallWorkshop.Game.Entity;
using HarmonyLib;
using UnityEngine;

namespace LivingCities.QuestHooks
{
    // EnemyEntity.Update still runs its base update. Only vanilla's final guard
    // destruction is gated, so posted guards survive without inventing a crime.
    public sealed class NativeGuardLifetimeHooks : IDisposable
    {
        private const string Owner = "local.livingcities.guard-lifetime";
        private static NativeGuardLifetimeHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<GameObject, bool> owns;
        public NativeGuardLifetimeHooks(Func<GameObject, bool> owns)
        {
            if (active != null) throw new InvalidOperationException("Guard lifetime hook already installed.");
            this.owns = owns ?? throw new ArgumentNullException(nameof(owns));
            active = this;
            try
            {
                harmony.Patch(typeof(EnemyEntity).GetMethod("Update"), transpiler: new HarmonyMethod(
                    typeof(NativeGuardLifetimeHooks).GetMethod("Rewrite", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch { Dispose(); throw; }
        }
        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
        {
            var source = typeof(UnityEngine.Object).GetMethod("Destroy", new[] { typeof(UnityEngine.Object) });
            var target = typeof(NativeGuardLifetimeHooks).GetMethod("DestroyUnowned", BindingFlags.Static | BindingFlags.NonPublic);
            int matches = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(source))
                { instruction.opcode = OpCodes.Call; instruction.operand = target; matches++; }
                yield return instruction;
            }
            if (matches != 1) throw new InvalidOperationException("Native guard destruction changed; expected one call.");
        }
        private static void DestroyUnowned(UnityEngine.Object target)
        {
            var body = target as GameObject;
            if (body && active != null && active.owns(body)) return;
            UnityEngine.Object.Destroy(target);
        }
        public void Dispose()
        { if (active != this) return; harmony.UnpatchAll(Owner); active = null; }
    }
}
