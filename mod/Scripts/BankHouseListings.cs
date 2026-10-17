using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using LivingCities.Core;

namespace LivingCities.DFU
{
    /// <summary>Exposes persisted vacant homes through DFU's native bank list.</summary>
    public sealed class BankHouseListings : IDisposable
    {
        private readonly Func<WorldStateStore> getStore;
        private readonly Func<bool> enabled;
        private IDisposable hooks;

        public BankHouseListings(Func<WorldStateStore> getStore, Func<bool> enabled)
        {
            this.getStore = getStore ?? throw new ArgumentNullException(nameof(getStore));
            this.enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        }

        public void Install()
        {
            if (hooks != null) return;
            var type = Type.GetType("LivingCities.QuestHooks.NativeBankHouseHooks, LivingCities.QuestHooks", true);
            hooks = (IDisposable)Activator.CreateInstance(type, new object[] {
                new Func<bool>(enabled),
                new Func<BuildingDirectory, List<BuildingSummary>, List<BuildingSummary>>(ReconcileListings),
                new Action<BuildingSummary, int, TransactionResult>(OnPurchased)
            });
        }

        public void Dispose()
        {
            if (hooks != null) hooks.Dispose();
            hooks = null;
        }

        private List<BuildingSummary> ReconcileListings(BuildingDirectory directory, List<BuildingSummary> nativeListings)
        {
            if (!directory) return nativeListings ?? new List<BuildingSummary>();
            var result = nativeListings == null ? new List<BuildingSummary>() : new List<BuildingSummary>(nativeListings);
            var store = getStore();
            if (store == null) return result;

            var town = store.CurrentPopulation(directory.MapID);
            var nativePeople = store.NativePeopleFor(directory.MapID);
            var reservations = store.HouseholdRecoveryReservations(directory.MapID);

            // DFU's vanilla bank can randomly list ordinary homes. Keep every
            // Living Cities home off that list unless its saved outcome is active.
            if (town != null)
                result.RemoveAll(summary => town.Households.Exists(home => home.BuildingKey == summary.buildingKey) &&
                    !IsSafeListedBankSale(directory, reservations, summary.buildingKey, town, nativePeople));

            foreach (var reservation in reservations)
            {
                if (reservation == null || reservation.Outcome != HouseholdVacancyOutcome.BankSale ||
                    reservation.Status != HouseholdRecoveryStatus.Listed || reservation.BuildingKey < 0) continue;
                BuildingSummary house;
                if (!directory.GetBuildingSummary(reservation.BuildingKey, out house) ||
                    !CanList(directory, house, town, nativePeople))
                    continue;
                if (!result.Exists(item => item.buildingKey == house.buildingKey)) result.Add(house);
            }
            return result;
        }

        private static bool IsSafeListedBankSale(BuildingDirectory directory,
            List<HouseholdRecoveryReservation> reservations, int buildingKey, SettlementPopulation town,
            List<NativePerson> nativePeople)
        {
            if (reservations == null || !reservations.Exists(item => item != null && item.BuildingKey == buildingKey &&
                item.Outcome == HouseholdVacancyOutcome.BankSale && item.Status == HouseholdRecoveryStatus.Listed)) return false;
            BuildingSummary house;
            return directory.GetBuildingSummary(buildingKey, out house) && CanList(directory, house, town, nativePeople);
        }

        private static bool CanList(BuildingDirectory directory, BuildingSummary house, SettlementPopulation town,
            List<NativePerson> nativePeople)
        {
            try { return CanListUnchecked(directory, house, town, nativePeople); }
            catch { return false; }
        }

        private static bool CanListUnchecked(BuildingDirectory directory, BuildingSummary house, SettlementPopulation town,
            List<NativePerson> nativePeople)
        {
            if (house.buildingKey < 0 || house.FactionId != 0 ||
                house.BuildingType < DFLocation.BuildingTypes.House1 || house.BuildingType > DFLocation.BuildingTypes.House6)
                return false;
            var game = GameManager.Instance;
            if (game == null || game.PlayerActivate == null || game.PlayerActivate.IsActiveQuestBuilding(house, true) ||
                IsPlayerOwned(directory.MapID, house.buildingKey)) return false;

            // Recheck current residents and native household owners at listing time.
            if (town == null || town.IsPartial) return false;
            var home = town.Households.Find(item => item.BuildingKey == house.buildingKey);
            if (home == null || home.Residents == null || home.Residents.Exists(person => person.IsAlive)) return false;
            if (nativePeople != null && nativePeople.Exists(person => person != null &&
                (person.HomeKey == house.buildingKey && person.BedSlot >= 0 ||
                 person.WorkKey == house.buildingKey && person.HomeKey >= 0 ||
                 person.HomeKey < 0 && person.WorkKey == house.buildingKey && person.IsAlive))) return false;
            var dfu = DaggerfallUnity.Instance;
            if (!dfu || dfu.ContentReader == null || !dfu.ContentReader.IsReady) return false;
            var beds = HousingSurvey.Read(dfu.ContentReader, directory.LocationData, new[] { house }, dfu.MeshReader);
            return beds.Count == 1 && beds[0].BedCount > 0;
        }

        private static bool IsPlayerOwned(int settlementId, int buildingKey)
        {
            var deeds = DaggerfallBankManager.Houses;
            if (deeds == null) return true;
            foreach (var deed in deeds)
                if (deed != null && deed.mapID == settlementId && deed.buildingKey == buildingKey) return true;
            return false;
        }

        private void OnPurchased(BuildingSummary house, int regionIndex, TransactionResult result)
        {
            if (result != TransactionResult.PURCHASED_HOUSE || house.buildingKey < 0) return;
            var game = GameManager.Instance;
            if (game == null || !game.PlayerGPS.HasCurrentLocation) return;
            // Native PurchaseHouse has already persisted the deed before postfix runs.
            if (!IsPlayerOwned(game.PlayerGPS.CurrentLocation.MapTableData.MapId, house.buildingKey)) return;
            getStore()?.MarkVacantHouseSold(game.PlayerGPS.CurrentLocation.MapTableData.MapId,
                house.buildingKey, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
        }
    }
}
