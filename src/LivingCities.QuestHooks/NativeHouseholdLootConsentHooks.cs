using System;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using HarmonyLib;

namespace LivingCities.QuestHooks
{
    /// <summary>Skips the native theft result only when a current home resident consents.</summary>
    public sealed class NativeHouseholdLootConsentHooks : IDisposable
    {
        private const string Owner = "local.livingcities.household-loot-consent";
        private static NativeHouseholdLootConsentHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<DaggerfallInventoryWindow, bool> permits;

        public NativeHouseholdLootConsentHooks(Func<DaggerfallInventoryWindow, bool> permits)
        {
            if (active != null) throw new InvalidOperationException("Household loot consent hook already installed.");
            this.permits = permits ?? throw new ArgumentNullException(nameof(permits));
            active = this;
            try
            {
                var method = AccessTools.Method(typeof(DaggerfallInventoryWindow), "AttemptPrivatePropertyTheft");
                if (method == null) throw new MissingMethodException("Native private-property theft API changed.");
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(NativeHouseholdLootConsentHooks), nameof(BeforeTheft)));
            }
            catch { Dispose(); throw; }
        }

        private static bool BeforeTheft(DaggerfallInventoryWindow __instance)
        {
            var self = active;
            if (self == null || __instance == null) return true;
            try { return !self.permits(__instance); }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("LIVING_CITIES_HOUSEHOLD_CONSENT_FAILED: " + error);
                return true;
            }
        }

        public void Dispose()
        {
            if (active == this) active = null;
            harmony.UnpatchAll(Owner);
        }
    }
}
