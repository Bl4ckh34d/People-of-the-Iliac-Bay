using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using LivingCities.Core;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private IDisposable householdDoorHook;
        public bool HouseholdLocksEnabled { get; set; } = true;
        private void InstallHouseholdDoorHook()
        {
            var type = Type.GetType("LivingCities.QuestHooks.NativeHouseholdDoorHooks, LivingCities.QuestHooks", true);
            householdDoorHook = (IDisposable)Activator.CreateInstance(type,
                new object[] { new Func<BuildingSummary, bool?>(HouseholdDoorAccess) });
        }
        private bool? HouseholdDoorAccess(BuildingSummary summary)
        {
            if (!HouseholdLocksEnabled) return null;
            if (summary.BuildingType < DFLocation.BuildingTypes.House1 ||
                summary.BuildingType > DFLocation.BuildingTypes.House6 || summary.FactionId != 0)
                return null;
            var guards = GetComponent<GuardPopulation>();
            if (guards && guards.OccupiesCrimeScene(summary.buildingKey, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()))
                return true; // Entry is governed by the ten-second crime-scene leave warning.
            bool? locked = HouseholdRoutineLocked(summary.buildingKey);
            if (!locked.HasValue || DaggerfallBankManager.IsHouseOwned(summary.buildingKey)) return null;
            return !locked.Value;
        }
        // Null leaves native access intact while a town is unknown or still loading.
        public bool? HouseholdRoutineLocked(int buildingKey)
        {
            var game = GameManager.Instance;
            if (!Ready || population == null || game == null || !game.IsReady ||
                !game.PlayerGPS.HasCurrentLocation ||
                game.PlayerGPS.CurrentLocation.MapTableData.MapId != population.SettlementId) return null;
            var home = population.Households.Find(h => h.BuildingKey == buildingKey);
            if (home == null) return null;
            // Quest-owned actors have an independent placement contract. Do not infer
            // their presence from the generated schedule until that placement is known.
            foreach (var resident in home.Residents)
                if (resident.IsAlive && (QuestOwned(resident) || !plans.Exists(p => p.Resident.Id == resident.Id)))
                    return null;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            return HouseholdAccess.RoutineLocked(home, resident =>
                HouseholdAccess.EffectiveState(GetState(resident.Id, now), Reaction(resident.Id, now), now));
        }
    }
}
