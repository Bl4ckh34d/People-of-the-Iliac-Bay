using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private List<HomeProfile> recoveryHomes = new List<HomeProfile>();
        private int recoveryScanIndex;

        private bool AdvanceHouseholdRecoveryOne(BuildingDirectory directory, GameManager game)
        {
            if (directory == null || game == null || current == null || recoveryHomes == null || recoveryHomes.Count == 0)
                return false;
            int index = recoveryScanIndex;
            if (index < 0 || index >= recoveryHomes.Count) index = 0;
            recoveryScanIndex = index + 1 == recoveryHomes.Count ? 0 : index + 1;
            HomeProfile home = recoveryHomes[index];
            if (home == null || !directory.GetBuildingSummary(home.BuildingKey, out BuildingSummary summary) ||
                summary.BuildingType < DFLocation.BuildingTypes.House1 ||
                summary.BuildingType > DFLocation.BuildingTypes.House6)
                return false;

            Household household = current.Households.Find(h => h.BuildingKey == home.BuildingKey);
            if (household == null) return false;

            var facts = new HouseholdRecoveryEligibility
            {
                BedCount = home.BedCount,
                Factioned = summary.FactionId != 0,
                PlayerOwned = PlayerOwnsRecoveryHome(directory.MapID, home.BuildingKey),
                ActiveQuest = game.PlayerActivate.IsActiveQuestBuilding(summary, true)
            };

            foreach (var resident in household.Residents)
                if (questOffers != null && questOffers.OwnsResident(resident.Id))
                    facts.ActiveQuest = true;

            foreach (var native in store.NativePeopleFor(directory.MapID))
            {
                if (native.HomeKey == home.BuildingKey)
                {
                    if (native.IsAlive) facts.HasLivingNativeOwner = true;
                    if (native.BedSlot >= 0) facts.Worksite = true;
                }
                if (native.WorkKey == home.BuildingKey && native.HomeKey >= 0)
                    facts.Worksite = true;
                if (native.IsAlive && native.HomeKey < 0 && native.WorkKey == home.BuildingKey)
                    facts.HasLivingNativeOwner = true;
            }

            var outcome = RecoveryOutcome(directory.MapID, home.BuildingKey, household.Generation);
            HouseholdRecoveryReservation reservation = store.EnsureHouseholdRecovery(directory.MapID,
                home.BuildingKey, RecoveryTier(directory), facts, outcome);
            if (reservation == null) return false;

            bool playerOutside = !game.PlayerEnterExit.IsPlayerInsideBuilding ||
                !game.PlayerEnterExit.Interior || game.PlayerEnterExit.Interior.EntryDoor.buildingKey != home.BuildingKey;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if (!store.AdvanceHouseholdRecovery(directory.MapID, home.BuildingKey, now, home, facts, playerOutside))
                return false;

            current = store.CurrentPopulation(directory.MapID);
            return true;
        }

        private static bool PlayerOwnsRecoveryHome(int settlementId, int buildingKey)
        {
            var deeds = DaggerfallBankManager.Houses;
            if (deeds == null) return true;
            foreach (var deed in deeds)
            {
                if (deed == null) return true;
                if (deed.mapID == settlementId && deed.buildingKey == buildingKey)
                    return true;
            }
            return false;
        }

        private HouseholdVacancyOutcome RecoveryOutcome(int settlementId, int buildingKey, int generation)
        {
            uint roll = StableSeed.Combine(store.WorldSeed, (uint)settlementId, (uint)buildingKey,
                unchecked((uint)generation) ^ 0x42414e4bu);
            return roll % 4 == 0 ? HouseholdVacancyOutcome.BankSale : HouseholdVacancyOutcome.NewFamily;
        }

        private static HouseholdRecoveryTier RecoveryTier(BuildingDirectory directory)
        {
            var type = directory.LocationData.MapTableData.LocationType;
            if (type == DFRegion.LocationTypes.TownCity) return HouseholdRecoveryTier.DenseCity;
            if (type == DFRegion.LocationTypes.HomeFarms && directory.BuildingCount <= 20)
                return HouseholdRecoveryTier.IsolatedHamlet;
            return HouseholdRecoveryTier.OtherSettlement;
        }
    }
}
