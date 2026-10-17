using System;
using System.Collections;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class RosyInteriorCompatibility : MonoBehaviour
    {
        private IDisposable hooks;

        [Invoke(StateManager.StateTypes.Start, 12)]
        public static void Init(InitParams initParams)
        {
            var host = FindObjectOfType<LivingCitiesMod>();
            if (!host) return;
            var component = host.gameObject.AddComponent<RosyInteriorCompatibility>();
            component.hooks = (IDisposable)Activator.CreateInstance(HookType());
        }

        private static Type HookType()
        {
            return Type.GetType("LivingCities.QuestHooks.RosyInteriorCompatibilityHooks, LivingCities.QuestHooks", true);
        }

        public static IEnumerator VerifyTransitionLifetime()
        {
            // The preceding probe deliberately exits shortly after entry. Allow all
            // nine delayed frames and scene-cache restoration to finish before PASS.
            for (int frame = 0; frame < 25; frame++) yield return null;
            var type = HookType();
            if (!(bool)type.GetProperty("Installed").GetValue(null, null)) yield break;
            int completed = (int)type.GetProperty("Completed").GetValue(null, null);
            int cancelled = (int)type.GetProperty("Cancelled").GetValue(null, null);
            if (completed == 0 || cancelled == 0)
                throw new InvalidOperationException("Rosy interior regression needs both normal correction and stale-exit cancellation.");
            Debug.Log("LIVING_CITIES_ROSY_INTERIOR_LIFETIME_PASS: normal=" + completed + "; staleCancelled=" + cancelled);
        }

        private void OnDestroy() { hooks?.Dispose(); hooks = null; }
    }
}
