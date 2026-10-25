using System;
using System.Reflection;
using DaggerfallWorkshop.Game;
using HarmonyLib;
using UnityEngine;

namespace PeopleOfTheIliacBay.QuestHooks
{
    public sealed class NativeCivilianHooks : IDisposable
    {
        private const string Owner = "local.peopleoftheiliacbay.civilian-witnesses";
        private static NativeCivilianHooks active;
        public static Transform CurrentTarget { get; private set; }
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<Transform, Action<Exception>> begin;
        private sealed class HitScope
        {
            public Transform Previous;
            public Action<Exception> Complete;
            public bool Completed;
        }
        public NativeCivilianHooks(Action<Transform> hit) : this(Wrap(hit)) { }
        private static Func<Transform, Action<Exception>> Wrap(Action<Transform> hit)
        {
            if (hit == null) throw new ArgumentNullException(nameof(hit));
            return target => { hit(target); return null; };
        }
        public NativeCivilianHooks(Func<Transform, Action<Exception>> begin)
        {
            if (active != null) throw new InvalidOperationException("Civilian hook already installed.");
            this.begin = begin ?? throw new ArgumentNullException(nameof(begin));
            active = this;
            try { harmony.Patch(typeof(WeaponManager).GetMethod("WeaponDamage"), prefix:
                new HarmonyMethod(typeof(NativeCivilianHooks).GetMethod("BeforeDamage", BindingFlags.Static | BindingFlags.NonPublic)),
                finalizer: new HarmonyMethod(typeof(NativeCivilianHooks).GetMethod("AfterDamage", BindingFlags.Static | BindingFlags.NonPublic))); }
            catch { Dispose(); throw; }
        }
        private static void BeforeDamage(Transform hitTransform, out HitScope __state)
        {
            __state = new HitScope { Previous = CurrentTarget };
            CurrentTarget = hitTransform;
            if (active != null) __state.Complete = active.begin(hitTransform);
        }
        private static Exception AfterDamage(Exception __exception, HitScope __state)
        {
            if (__state == null || __state.Completed) return __exception;
            __state.Completed = true;
            try { if (__state.Complete != null) __state.Complete(__exception); }
            catch (Exception error)
            {
                // Do not replace a native failure with a completion failure.
                if (__exception == null) return error;
                Debug.LogException(error);
            }
            finally { CurrentTarget = __state.Previous; }
            return __exception;
        }
        public void Dispose()
        { if (active != this) return; harmony.UnpatchAll(Owner); active = null; }
    }
}
