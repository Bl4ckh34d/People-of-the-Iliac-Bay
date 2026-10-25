using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdRecoveryTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Household recovery uses stable tier delays and inclusive bounds", TierDelaysAreStableAndBounded));
            cases.Add(("Household recovery activation rechecks property and waits for due time and cleanup", ActivationWaitsForAllGates));
            cases.Add(("Household recovery requires confirmed extinction and safe eligible property", EligibilityRejectsEveryBlocker));
            cases.Add(("Household recovery rejects malformed inputs", InvalidInputsAreRejected));
        }

        private static void TierDelaysAreStableAndBounded()
        {
            AssertTier(HouseholdRecoveryTier.DenseCity, 3, 7);
            AssertTier(HouseholdRecoveryTier.OtherSettlement, 7, 30);
            // Isolated hamlet timing is a conservative default until settlement data has its own tuning.
            AssertTier(HouseholdRecoveryTier.IsolatedHamlet, 24, 30);

            HouseholdRecoveryDecision first = Evaluate(42, 5, 11, 2, 200000, HouseholdRecoveryTier.DenseCity, Eligible());
            HouseholdRecoveryDecision repeated = Evaluate(42, 5, 11, 2, 200000, HouseholdRecoveryTier.DenseCity, Eligible());
            Check.Equal(first.DelayDays, repeated.DelayDays);
            Check.Equal(first.RecoveryAt, repeated.RecoveryAt);
            Check.Near(200000 + first.DelayDays * 86400.0, first.RecoveryAt);

            HouseholdRecoveryDecision nextGeneration = Evaluate(42, 5, 11, 3, 200000, HouseholdRecoveryTier.DenseCity, Eligible());
            Check.True(nextGeneration.DelayDays >= 3 && nextGeneration.DelayDays <= 7,
                "Generation changes must still produce a delay inside the city tier range.");
        }

        private static void AssertTier(HouseholdRecoveryTier tier, int minimum, int maximum)
        {
            int observedMinimum = int.MaxValue;
            int observedMaximum = int.MinValue;
            for (int generation = 0; generation < 4096; generation++)
            {
                HouseholdRecoveryDecision decision = Evaluate(42, 5, 11, generation, 200000, tier, Eligible());
                Check.True(decision.IsEligible, "Valid extinct homes should receive a recovery decision.");
                Check.True(decision.DelayDays >= minimum && decision.DelayDays <= maximum,
                    "Tier delay escaped its documented inclusive range.");
                observedMinimum = Math.Min(observedMinimum, decision.DelayDays);
                observedMaximum = Math.Max(observedMaximum, decision.DelayDays);
            }
            Check.Equal(minimum, observedMinimum);
            Check.Equal(maximum, observedMaximum);
        }

        private static void ActivationWaitsForAllGates()
        {
            HouseholdRecoveryDecision decision = Evaluate(42, 5, 11, 2, 200000, HouseholdRecoveryTier.DenseCity, Eligible());
            HouseholdRecoveryEligibility current = Eligible();
            Check.True(!HouseholdRecoveryPolicy.CanActivate(decision, decision.RecoveryAt - 1, current, true, true),
                "A home must not recover before its fixed deadline.");
            Check.True(!HouseholdRecoveryPolicy.CanActivate(decision, decision.RecoveryAt, current, false, true),
                "A due home must wait until the player is outside the dwelling.");
            Check.True(!HouseholdRecoveryPolicy.CanActivate(decision, decision.RecoveryAt, current, true, false),
                "A due home must wait until all corpses are cleared.");
            Check.True(HouseholdRecoveryPolicy.CanActivate(decision, decision.RecoveryAt, current, true, true),
                "All execution gates should permit recovery exactly at the deadline.");
            Check.True(HouseholdRecoveryPolicy.CanActivate(decision, decision.RecoveryAt + 86400, current, true, true),
                "An overdue persisted decision should remain due without rerolling its deadline.");

            double fixedDeadline = decision.RecoveryAt;
            HouseholdRecoveryEligibility becamePlayerOwned = Facts(playerOwned: true);
            Check.True(!HouseholdRecoveryPolicy.CanActivate(decision, fixedDeadline, becamePlayerOwned, true, true),
                "A property acquired after scheduling must become ineligible.");
            Check.Equal(fixedDeadline, decision.RecoveryAt);
            HouseholdRecoveryEligibility livingOwnerDiscovered = Facts(livingNativeOwner: true);
            Check.True(!HouseholdRecoveryPolicy.CanActivate(decision, fixedDeadline, livingOwnerDiscovered, true, true),
                "A newly discovered living native owner must prevent recovery.");
            Check.Equal(fixedDeadline, decision.RecoveryAt);
        }

        private static void EligibilityRejectsEveryBlocker()
        {
            AssertBlocked(Facts(allDead: false), HouseholdRecoveryBlockReason.ExtinctionUnconfirmed);
            AssertBlocked(Facts(priorResidents: 0), HouseholdRecoveryBlockReason.NoPriorResidents);
            AssertBlocked(Facts(livingResident: true), HouseholdRecoveryBlockReason.LivingPermanentResident);
            AssertBlocked(Facts(livingNativeOwner: true), HouseholdRecoveryBlockReason.LivingNativeOwner);
            AssertBlocked(Facts(beds: -1), HouseholdRecoveryBlockReason.UnknownBedCount);
            AssertBlocked(Facts(beds: 0), HouseholdRecoveryBlockReason.NoBeds);
            AssertBlocked(Facts(playerOwned: true), HouseholdRecoveryBlockReason.PlayerOwned);
            AssertBlocked(Facts(factioned: true), HouseholdRecoveryBlockReason.Factioned);
            AssertBlocked(Facts(activeQuest: true), HouseholdRecoveryBlockReason.ActiveQuest);
            AssertBlocked(Facts(worksite: true), HouseholdRecoveryBlockReason.Worksite);
            AssertBlocked(Facts(partialSettlement: true), HouseholdRecoveryBlockReason.PartialSettlement);

            HouseholdRecoveryDecision absentButUnconfirmed = Evaluate(42, 5, 11, 2, 200000,
                HouseholdRecoveryTier.DenseCity, Facts(allDead: false));
            Check.True(!absentButUnconfirmed.IsEligible,
                "No visible actor or schedule absence is not evidence that a household died.");
        }

        private static void AssertBlocked(HouseholdRecoveryEligibility facts, HouseholdRecoveryBlockReason reason)
        {
            HouseholdRecoveryDecision decision = Evaluate(42, 5, 11, 2, 200000, HouseholdRecoveryTier.DenseCity, facts);
            Check.True(!decision.IsEligible, "A blocked property must not receive an active recovery decision.");
            Check.Equal(reason, decision.BlockReason);
            Check.True(!HouseholdRecoveryPolicy.CanActivate(decision, double.MaxValue, facts, true, true),
                "Blocked properties must stay blocked even when all execution gates are open.");
        }

        private static void InvalidInputsAreRejected()
        {
            Check.Throws<ArgumentNullException>(() => HouseholdRecoveryPolicy.Evaluate(42, 5, 11, 2, 200000,
                HouseholdRecoveryTier.DenseCity, null));
            Check.Throws<ArgumentOutOfRangeException>(() => Evaluate(42, -1, 11, 2, 200000, HouseholdRecoveryTier.DenseCity, Eligible()));
            Check.Throws<ArgumentOutOfRangeException>(() => Evaluate(42, 5, -1, 2, 200000, HouseholdRecoveryTier.DenseCity, Eligible()));
            Check.Throws<ArgumentOutOfRangeException>(() => Evaluate(42, 5, 11, -1, 200000, HouseholdRecoveryTier.DenseCity, Eligible()));
            Check.Throws<ArgumentOutOfRangeException>(() => Evaluate(42, 5, 11, 2, -1, HouseholdRecoveryTier.DenseCity, Eligible()));
            Check.Throws<ArgumentOutOfRangeException>(() => Evaluate(42, 5, 11, 2, double.NaN, HouseholdRecoveryTier.DenseCity, Eligible()));
            Check.Throws<ArgumentOutOfRangeException>(() => Evaluate(42, 5, 11, 2, double.PositiveInfinity, HouseholdRecoveryTier.DenseCity, Eligible()));
            Check.Throws<ArgumentOutOfRangeException>(() => Evaluate(42, 5, 11, 2, double.MaxValue, HouseholdRecoveryTier.DenseCity, Eligible()));
            Check.Throws<ArgumentOutOfRangeException>(() => Evaluate(42, 5, 11, 2, 200000, (HouseholdRecoveryTier)99, Eligible()));
            Check.Throws<ArgumentOutOfRangeException>(() => Evaluate(42, 5, 11, 2, 200000, HouseholdRecoveryTier.DenseCity,
                Facts(beds: -2)));
            HouseholdRecoveryDecision due = Evaluate(42, 5, 11, 2, 200000, HouseholdRecoveryTier.DenseCity, Eligible());
            Check.Throws<ArgumentOutOfRangeException>(() => HouseholdRecoveryPolicy.CanActivate(due, double.NaN, Eligible(), true, true));
            Check.Throws<ArgumentNullException>(() => HouseholdRecoveryPolicy.CanActivate(due, due.RecoveryAt, null, true, true));
        }

        private static HouseholdRecoveryEligibility Eligible()
        {
            return new HouseholdRecoveryEligibility
            {
                AllPermanentResidentsConfirmedDead = true,
                PriorPermanentResidentCount = 2,
                BedCount = 2
            };
        }

        private static HouseholdRecoveryEligibility Facts(bool allDead = true, int priorResidents = 2,
            bool livingResident = false, bool livingNativeOwner = false, int beds = 2,
            bool playerOwned = false, bool factioned = false, bool activeQuest = false,
            bool worksite = false, bool partialSettlement = false)
        {
            return new HouseholdRecoveryEligibility
            {
                AllPermanentResidentsConfirmedDead = allDead,
                PriorPermanentResidentCount = priorResidents,
                HasLivingPermanentResident = livingResident,
                HasLivingNativeOwner = livingNativeOwner,
                BedCount = beds,
                PlayerOwned = playerOwned,
                Factioned = factioned,
                ActiveQuest = activeQuest,
                Worksite = worksite,
                PartialSettlement = partialSettlement
            };
        }

        private static HouseholdRecoveryDecision Evaluate(uint seed, int settlement, int building, int generation,
            double lastDeathAt, HouseholdRecoveryTier tier, HouseholdRecoveryEligibility facts)
        {
            return HouseholdRecoveryPolicy.Evaluate(seed, settlement, building, generation, lastDeathAt, tier, facts);
        }
    }
}
