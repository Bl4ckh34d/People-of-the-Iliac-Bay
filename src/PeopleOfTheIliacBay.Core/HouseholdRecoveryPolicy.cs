using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum HouseholdRecoveryTier
    {
        DenseCity = 0,
        OtherSettlement = 1,
        IsolatedHamlet = 2
    }

    public enum HouseholdRecoveryBlockReason
    {
        None = 0,
        ExtinctionUnconfirmed = 1,
        NoPriorResidents = 2,
        LivingPermanentResident = 3,
        LivingNativeOwner = 4,
        UnknownBedCount = 5,
        NoBeds = 6,
        PlayerOwned = 7,
        Factioned = 8,
        ActiveQuest = 9,
        Worksite = 10,
        PartialSettlement = 11
    }

    /// <summary>
    /// Facts that must be resolved from persistent world history and the current property.
    /// An empty or merely unobserved household is not an extinct household.
    /// </summary>
    public sealed class HouseholdRecoveryEligibility
    {
        public bool AllPermanentResidentsConfirmedDead { get; set; }
        public int PriorPermanentResidentCount { get; set; }
        public bool HasLivingPermanentResident { get; set; }
        public bool HasLivingNativeOwner { get; set; }
        // -1 means bed capacity is unknown; zero means known to have no beds.
        public int BedCount { get; set; } = -1;
        public bool PlayerOwned { get; set; }
        public bool Factioned { get; set; }
        public bool ActiveQuest { get; set; }
        public bool Worksite { get; set; }
        public bool PartialSettlement { get; set; }
    }

    /// <summary>
    /// Immutable policy result. Persistence and runtime household replacement are not connected yet.
    /// </summary>
    public sealed class HouseholdRecoveryDecision
    {
        internal HouseholdRecoveryDecision(bool isEligible, HouseholdRecoveryBlockReason blockReason,
            uint worldSeed, int settlementId, int buildingKey, int generation,
            double confirmedLastDeathAt, HouseholdRecoveryTier tier, int delayDays, double recoveryAt)
        {
            IsEligible = isEligible;
            BlockReason = blockReason;
            WorldSeed = worldSeed;
            SettlementId = settlementId;
            BuildingKey = buildingKey;
            Generation = generation;
            ConfirmedLastDeathAt = confirmedLastDeathAt;
            Tier = tier;
            DelayDays = delayDays;
            RecoveryAt = recoveryAt;
        }

        public bool IsEligible { get; private set; }
        public HouseholdRecoveryBlockReason BlockReason { get; private set; }
        public uint WorldSeed { get; private set; }
        public int SettlementId { get; private set; }
        public int BuildingKey { get; private set; }
        public int Generation { get; private set; }
        public double ConfirmedLastDeathAt { get; private set; }
        public HouseholdRecoveryTier Tier { get; private set; }
        public int DelayDays { get; private set; }
        public double RecoveryAt { get; private set; }
    }

    public static class HouseholdRecoveryPolicy
    {
        private const double SecondsPerDay = 86400.0;

        /// <summary>Computes a stable, generation-specific recovery deadline.</summary>
        public static HouseholdRecoveryDecision Evaluate(uint worldSeed, int settlementId, int buildingKey,
            int generation, double confirmedLastDeathAt, HouseholdRecoveryTier tier,
            HouseholdRecoveryEligibility eligibility)
        {
            if (settlementId < 0) throw new ArgumentOutOfRangeException(nameof(settlementId));
            if (buildingKey < 0) throw new ArgumentOutOfRangeException(nameof(buildingKey));
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (!IsFinite(confirmedLastDeathAt) || confirmedLastDeathAt < 0)
                throw new ArgumentOutOfRangeException(nameof(confirmedLastDeathAt));
            if (tier < HouseholdRecoveryTier.DenseCity || tier > HouseholdRecoveryTier.IsolatedHamlet)
                throw new ArgumentOutOfRangeException(nameof(tier));
            ValidateEligibility(eligibility);

            HouseholdRecoveryBlockReason blocker = FindBlocker(eligibility);
            int minimumDays;
            int maximumDays;
            GetDelayRange(tier, out minimumDays, out maximumDays);
            uint stableKey = StableSeed.Combine(worldSeed, (uint)settlementId, (uint)buildingKey, (uint)generation);
            int delayDays = minimumDays + (int)(stableKey % (uint)(maximumDays - minimumDays + 1));
            double recoveryAt = confirmedLastDeathAt + delayDays * SecondsPerDay;
            if (!IsFinite(recoveryAt) || recoveryAt <= confirmedLastDeathAt)
                throw new ArgumentOutOfRangeException(nameof(confirmedLastDeathAt));

            return new HouseholdRecoveryDecision(blocker == HouseholdRecoveryBlockReason.None, blocker,
                worldSeed, settlementId, buildingKey, generation, confirmedLastDeathAt, tier, delayDays, recoveryAt);
        }

        /// <summary>
        /// Execution gate only. It never recalculates the decision deadline.
        /// </summary>
        public static bool CanActivate(HouseholdRecoveryDecision decision, double now,
            HouseholdRecoveryEligibility currentEligibility, bool playerOutsideDwelling, bool corpsesCleared)
        {
            if (decision == null) throw new ArgumentNullException(nameof(decision));
            if (!IsFinite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            ValidateEligibility(currentEligibility);
            return decision.IsEligible && FindBlocker(currentEligibility) == HouseholdRecoveryBlockReason.None &&
                now >= decision.RecoveryAt && playerOutsideDwelling && corpsesCleared;
        }

        private static void ValidateEligibility(HouseholdRecoveryEligibility eligibility)
        {
            if (eligibility == null) throw new ArgumentNullException(nameof(eligibility));
            if (eligibility.PriorPermanentResidentCount < 0)
                throw new ArgumentOutOfRangeException(nameof(eligibility.PriorPermanentResidentCount));
            if (eligibility.BedCount < -1)
                throw new ArgumentOutOfRangeException(nameof(eligibility.BedCount));
        }

        private static HouseholdRecoveryBlockReason FindBlocker(HouseholdRecoveryEligibility facts)
        {
            if (!facts.AllPermanentResidentsConfirmedDead) return HouseholdRecoveryBlockReason.ExtinctionUnconfirmed;
            if (facts.PriorPermanentResidentCount == 0) return HouseholdRecoveryBlockReason.NoPriorResidents;
            if (facts.HasLivingPermanentResident) return HouseholdRecoveryBlockReason.LivingPermanentResident;
            if (facts.HasLivingNativeOwner) return HouseholdRecoveryBlockReason.LivingNativeOwner;
            if (facts.BedCount < 0) return HouseholdRecoveryBlockReason.UnknownBedCount;
            if (facts.BedCount == 0) return HouseholdRecoveryBlockReason.NoBeds;
            if (facts.PlayerOwned) return HouseholdRecoveryBlockReason.PlayerOwned;
            if (facts.Factioned) return HouseholdRecoveryBlockReason.Factioned;
            if (facts.ActiveQuest) return HouseholdRecoveryBlockReason.ActiveQuest;
            if (facts.Worksite) return HouseholdRecoveryBlockReason.Worksite;
            if (facts.PartialSettlement) return HouseholdRecoveryBlockReason.PartialSettlement;
            return HouseholdRecoveryBlockReason.None;
        }

        private static void GetDelayRange(HouseholdRecoveryTier tier, out int minimumDays, out int maximumDays)
        {
            switch (tier)
            {
                case HouseholdRecoveryTier.DenseCity:
                    minimumDays = 3;
                    maximumDays = 7;
                    return;
                case HouseholdRecoveryTier.OtherSettlement:
                    minimumDays = 7;
                    maximumDays = 30;
                    return;
                case HouseholdRecoveryTier.IsolatedHamlet:
                    // Conservative default. This is a starting tuning value, not a user-locked rule.
                    minimumDays = 24;
                    maximumDays = 30;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tier));
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
