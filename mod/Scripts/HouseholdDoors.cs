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
                new object[] { new Func<BuildingSummary, bool?>(HouseholdDoorAccess),
                    new Func<int, string>(HouseholdGreeting) });
        }
        internal string HouseholdGreeting(int buildingKey)
        {
            var game = GameManager.Instance;
            if (!Ready || population == null || worldStore == null || !game || !game.IsReady ||
                !game.PlayerGPS.HasCurrentLocation ||
                game.PlayerGPS.CurrentLocation.MapTableData.MapId != population.SettlementId ||
                !population.Households.Exists(h => h.BuildingKey == buildingKey)) return null;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var guards = GetComponent<GuardPopulation>();
            if (guards && guards.OccupiesCrimeScene(buildingKey, now)) return null;
            // Consider every local household, including visitors from another home.
            foreach (var home in population.Households)
            {
                foreach (var resident in home.Residents)
                    if (resident.IsAlive && (QuestOwned(resident) || !plans.Exists(p => p.Resident.Id == resident.Id)))
                    {
                        if (home.BuildingKey == buildingKey) return null; // Unknown placement is not proven vacancy.
                    }
                var known = home.Residents.FindAll(r => plans.Exists(p => p.Resident.Id == r.Id));
                if (HouseholdAccess.HasLivingOccupant(buildingKey, known, r =>
                    worldStore.LocalResidentState(r.Id, now,
                        HouseholdAccess.EffectiveState(GetState(r.Id, now), Reaction(r.Id, now), now)))) return null;
            }
            foreach (var person in worldStore.NativePeopleFor(population.SettlementId))
            {
                if (!person.IsAlive) continue;
                var alarm = worldStore.NativeResponse(person.Id);
                var witness = alarm == null ? null : alarm.People.Find(p => p.ResidentId == person.Id);
                if (witness != null && witness.HasPosition)
                {
                    var actual = HouseholdAccess.EffectiveState(new ActivitySnapshot(Activity.Home,
                        person.WorkKey, new RoutePoint(0, 0), 0), witness, now);
                    if (actual.BuildingKey == buildingKey) return null;
                }
                else if (person.WorkKey == buildingKey) return null;
            }
            return "You are greeted with an eerie silence...";
        }
        private bool? HouseholdDoorAccess(BuildingSummary summary)
        {
            var game=GameManager.Instance;
            if (game && game.IsReady && game.PlayerGPS.HasCurrentLocation &&
                HasHouseholdPropertyKey(game.PlayerGPS.CurrentLocation.MapTableData.MapId, summary.buildingKey))
                return true;
            if (!HouseholdLocksEnabled) return null;
            if(Ready && worldStore!=null && population!=null && game && game.IsReady &&
                game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId==population.SettlementId &&
                worldStore.ShelterLocked(population.SettlementId,summary.buildingKey,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()) &&
                !DaggerfallBankManager.IsHouseOwned(summary.buildingKey)) return false;
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
        private static bool HasHouseholdPropertyKey(int settlementId, int buildingKey)
        {
            var items = GameManager.Instance.PlayerEntity.Items;
            for (int i = 0; i < items.Count; i++)
                if (HouseholdPropertyKeyItem.IsMatching(items.GetItem(i), settlementId, buildingKey)) return true;
            return false;
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
                worldStore.LocalResidentState(resident.Id,now,
                    HouseholdAccess.EffectiveState(GetState(resident.Id, now), Reaction(resident.Id, now), now)));
        }
    }
}
