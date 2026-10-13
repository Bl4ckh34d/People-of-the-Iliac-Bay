using System;
using System.Reflection;
using DaggerfallWorkshop.Game;
using HarmonyLib;
using UnityEngine;

namespace LivingCities.QuestHooks
{
    public sealed class NativeCivilianHooks : IDisposable
    {
        private const string Owner = "local.livingcities.civilian-witnesses";
        private static NativeCivilianHooks active;
        public static Transform CurrentTarget { get; private set; }
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Action<Transform> hit;
        public NativeCivilianHooks(Action<Transform> hit)
        {
            if (active != null) throw new InvalidOperationException("Civilian hook already installed.");
            this.hit = hit ?? throw new ArgumentNullException(nameof(hit));
            active = this;
            try { harmony.Patch(typeof(WeaponManager).GetMethod("WeaponDamage"), prefix:
                new HarmonyMethod(typeof(NativeCivilianHooks).GetMethod("BeforeDamage", BindingFlags.Static | BindingFlags.NonPublic)),
                finalizer: new HarmonyMethod(typeof(NativeCivilianHooks).GetMethod("AfterDamage", BindingFlags.Static | BindingFlags.NonPublic))); }
            catch { Dispose(); throw; }
        }
        private static void BeforeDamage(Transform hitTransform, out Transform __state)
        { __state = CurrentTarget; CurrentTarget = hitTransform; if (active != null) active.hit(hitTransform); }
        private static void AfterDamage(Transform __state) { CurrentTarget = __state; }
        public void Dispose()
        { if (active != this) return; harmony.UnpatchAll(Owner); active = null; }
    }
}
