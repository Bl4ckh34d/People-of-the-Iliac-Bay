using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var cases = new List<(string Name, Action Body)>();
            ClosedShopLeisureTests.Add(cases);
            NpcSceneMemoryTests.Add(cases);
            NpcInnMemoryTests.Add(cases);
            AgenticDialogueUiPolicyTests.Add(cases);
            NpcAcuteFearTests.Add(cases);
            ConversationContextAcuteFearTests.Add(cases);
            NpcPlayerContactFearPolicyTests.Add(cases);
            NpcExperienceCommitTests.Add(cases);
            PcmPlaybackFrameMapperTests.Add(cases);
            TextWeightedWordProgressTests.Add(cases);
            SpeechPresentationStateTests.Add(cases);
            PortraitVisemeTimelineTests.Add(cases);
            PreparedPresentationTests.Add(cases);
            BreezeTtsStreamingPolicyTests.Add(cases);
            PortraitSpeechOwnershipTests.Add(cases);
            SplitPortraitManifestTests.Add(cases);
            SplitPortraitCompositionTests.Add(cases);
            NpcPresentationStateTests.Add(cases);
            NpcExperienceDutyProjectionTests.Add(cases);
            SplitPortraitPresentationTests.Add(cases);
            NpcExperienceCrossFactorTests.Add(cases);
            NpcExperienceSharedContextTests.Add(cases);
            NpcExperienceDeferralTests.Add(cases);
            NpcTraitRecoveryIntegrationTests.Add(cases);
            ConversationContextMotivationTests.Add(cases);
            NpcMotivationConversationProjectionTests.Add(cases);
            NpcMotivationStoreTests.Add(cases);
            NpcObservedSpeechCommitTests.Add(cases);
            NpcExperienceImpactDetailsTests.Add(cases);
            NpcPoisonExperienceTests.Add(cases);
            NpcExperienceInboxMigrationTests.Add(cases);
            NpcItemReceivedReceiptTests.Add(cases);
            NpcVitalsExperienceTests.Add(cases);
            NpcExperienceObserverPagingTests.Add(cases);
            NpcAcceptedSpeechBridgeTests.Add(cases);
            NpcExperienceAffinityPolicyTests.Add(cases);
            NpcGoalExtractionTests.Add(cases);
            NpcGoalTwoPassTests.Add(cases);
            NpcGoalBindingTests.Add(cases);
            NpcGoalCompletionTests.Add(cases);
            SharedNavigationTests.Add(cases);
            NpcJumpTrajectoryTests.Add(cases);
            PopulationTests.Add(cases);
            PopulationSpawnBudgetTests.Add(cases);
            NpcProfileTests.Add(cases);
            NpcStartingBelongingsTests.Add(cases);
            NpcActorProgressTests.Add(cases);
            NpcVitalsTests.Add(cases);
            NpcVitalsBindingPolicyTests.Add(cases);
            NpcInventoryTests.Add(cases);
            NpcShopCommerceTests.Add(cases);
            NpcRepairServiceTests.Add(cases);
            NpcPurchaseTaskTests.Add(cases);
            NpcNamedAcquiredHandoverTests.Add(cases);
            NpcTaskDependencyAcceptanceTests.Add(cases);
            NpcAcquiredItemTaskTests.Add(cases);
            NpcTaskFailureFeedbackTests.Add(cases);
            NpcTaskIndexTests.Add(cases);
            NpcActionContractTests.Add(cases);
            NpcActionWaypointRecoveryTests.Add(cases);
            NpcActionOfferSemanticTests.Add(cases);
            NpcCompanionCastTests.Add(cases);
            FreeHealingPotionCommandTests.Add(cases);
            NpcKnownSpellConversationContextTests.Add(cases);
            ConversationTerminalActionContextTests.Add(cases);
            NativeNpcCompanionExperienceTests.Add(cases);
            NpcHomeActionPolicyTests.Add(cases);
            NpcRoutineRecoveryTransitionTests.Add(cases);
            NpcActionSequenceChatTests.Add(cases);
            NpcActionDecompositionTests.Add(cases);
            BedroomReplacementTests.Add(cases);
            OvernightSerializationTests.Add(cases);
            ConversationMemoryTests.Add(cases);
            SharedActivityConversationContextTests.Add(cases);
            HauntingContextTests.Add(cases);
            ConversationContextToolsTests.Add(cases);
            ConversationContextSnapshotTests.Add(cases);
            NpcDecisionContractTests.Add(cases);
            NpcDecisionInstructionTests.Add(cases);
            NpcDecisionFreshnessTests.Add(cases);
            ConversationActionReplyTests.Add(cases);
            ConversationActionRenderRequestTests.Add(cases);
            DialogueSocialReplyRepairTests.Add(cases);
            ConversationPendingTextTests.Add(cases);
            FriendshipProgressionTests.Add(cases);
            ConversationMoodTests.Add(cases);
            MoodRecoveryTests.Add(cases);
            TimedConversationMoodTests.Add(cases);
            NpcPersonalityTests.Add(cases);
            NativeSocialSkillContextTests.Add(cases);
            NativeSocialSkillAwardTests.Add(cases);
            NpcPersonalityStoreTests.Add(cases);
            NpcExperienceEventTests.Add(cases);
            NpcDirectedRelationshipTests.Add(cases);
            NpcDirectedRelationshipFearRecoveryTests.Add(cases);
            NpcDirectedRelationshipResentmentRecoveryTests.Add(cases);
            NpcRelationshipTrustPolicyTests.Add(cases);
            NpcRelationshipLedgerTests.Add(cases);
            NpcRelationshipStoreTests.Add(cases);
            ConversationRelationshipContextTests.Add(cases);
            NpcAppointmentExperienceTests.Add(cases);
            NpcExperienceInboxTests.Add(cases);
            NpcAppointmentExperienceInboxTests.Add(cases);
            NpcExperienceAppraisalTests.Add(cases);
            LocalDialogueDispatchTests.Add(cases);
            SocialDialogueResultTests.Add(cases);
            SocialDialogueStreamParserTests.Add(cases);
            ConversationPersistentMoodContextTests.Add(cases);
            NpcGoalChainLengthTests.Add(cases);
            NpcSocialFamiliarityTests.Add(cases);
            ConversationFeatureIndexTests.Add(cases);
            SharedMemoryArchiveTests.Add(cases);
            ShopPhysicalProgressTests.Add(cases);
            CounterServiceSlotsTests.Add(cases);
            SeatOccupancyTests.Add(cases);
            SittingViewTests.Add(cases);
            ObservedVisitTests.Add(cases);
            HouseholdInnVisitTests.Add(cases);
            AppointmentTests.Add(cases);
            AppointmentContactTests.Add(cases);
            CompanionHealthContactTests.Add(cases);
            AppointmentObservationTests.Add(cases);
            AppointmentDayPlannerTests.Add(cases);
            AppointmentPointPlannerTests.Add(cases);
            AppointmentPointRestoreTests.Add(cases);
            AppointmentChatTests.Add(cases);
            HouseholdRecoveryTests.Add(cases);
            HouseholdRecoveryReservationTests.Add(cases);
            HouseholdRecoveryStoreTests.Add(cases);
            HouseholdGenerationTests.Add(cases);
            HouseholdDayPlanTests.Add(cases);
            HouseholdJointMovementTests.Add(cases);
            HouseholdJointStoreTests.Add(cases);
            HouseholdOutingConversationTests.Add(cases);
            HouseholdInnServiceTests.Add(cases);
            HouseholdJointSimulationTests.Add(cases);
            HouseholdJointSimulationStoreTests.Add(cases);
            NpcInnServiceIdentityTests.Add(cases);
            NpcInnServiceCatalogTests.Add(cases);
            HouseholdDayAppointmentTests.Add(cases);
            HouseholdRoutineRecoveryTests.Add(cases);
            HouseholdHistoryTests.Add(cases);
            FamilyTests.Add(cases);
            StreetChatTests.Add(cases);
            BereavementTests.Add(cases);
            FamilySaveTests.Add(cases);
            AlarmTests.Add(cases);
            NoiseTests.Add(cases);
            HearingReportTests.Add(cases);
            EscapeTests.Add(cases);
            ShelterRouteTests.Add(cases);
            PursuitShelterTests.Add(cases);
            ThreatResponseTests.Add(cases);
            CrimeSceneTests.Add(cases);
            OutdoorCorpseTests.Add(cases);
            GuardSearchRecruitmentTests.Add(cases);
            CorpsePropertyKeyTests.Add(cases);
            GuardTests.Add(cases);
            GuardPolicyTests.Add(cases);
            HauntingTests.Add(cases);
            RefugeJourneyTests.Add(cases);
            GuardRefugeTests.Add(cases);
            GuardDefenseTests.Add(cases);
            DefenseIncidentTests.Add(cases);
            CorpseCleanupIntegrationTests.Add(cases);
            PatrolPairTests.Add(cases);
            GuardPatrolTests.Add(cases);
            BedTests.Add(cases);
            LodgingTests.Add(cases);
            InnLodgerHomeTests.Add(cases);
            TravelerJourneyTests.Add(cases);
            TravelerNavigationTargetTests.Add(cases);
            ResidentTripTests.Add(cases);
            ResidentTripNativeDestinationTests.Add(cases);
            ResidentVisitTests.Add(cases);
            ResidentInnJourneyTests.Add(cases);
            ResidentTravelDemandTests.Add(cases);
            ResidentDiscoveryTests.Add(cases);
            ResidentRoadJourneyTests.Add(cases);
            RoadSettlementIndexTests.Add(cases);
            RoadPassagePlanTests.Add(cases);
            ResidentVisitIdentityTests.Add(cases);
            ResidentRoadStopSaveTests.Add(cases);
            VisitorOriginTests.Add(cases);
            TravelerSaveTests.Add(cases);
            TravelerDemandTests.Add(cases);
            RoadJourneyTests.Add(cases);
            SleepViewTests.Add(cases);
            SleepArtCatalogTests.Add(cases);
            HouseholdAccessTests.Add(cases);
            EntryReportTests.Add(cases);
            NativePeopleTests.Add(cases);
            PathTests.Add(cases);
            OutdoorNavigationTests.Add(cases);
            StreetLaneRouteTests.Add(cases);
            ScheduleTests.Add(cases);
            ScheduleRecoveryTests.Add(cases);
            RoutineRecoveryPlannerTests.Add(cases);
            RoutineRecoveryPointTests.Add(cases);
            RoutineRecoveryAcceptanceTests.Add(cases);
            RoutineRecoveryStoreTests.Add(cases);
            RoutineRecoveryLifecycleTests.Add(cases);
            AdultDailyItineraryTests.Add(cases);
            AdultDailyRoutineTests.Add(cases);
            HouseholdChildcareTests.Add(cases);
            CorpseSuspectEvidenceTests.Add(cases);
            DailyOutingDistributionTests.Add(cases);
            ResidentRoutineDayTests.Add(cases);
            GuardArcheryTests.Add(cases);
            DailyItineraryScheduleTests.Add(cases);
            SavedRouteSimplifierTests.Add(cases);
            InteriorRouteSimplifierTests.Add(cases);
            RoutineStoreTests.Add(cases);
            UnresolvedMurderWitnessTests.Add(cases);
            SaveTests.Add(cases);
            QuestBindingTests.Add(cases);
            SettingsTests.Add(cases);
            DialogueTests.Add(cases);
            NpcSocialAvailabilityTests.Add(cases);
            ConversationTests.Add(cases);
            ConversationHostilityTests.Add(cases);
            FamilyThreatTests.Add(cases);
            HouseholdLootConsentTests.Add(cases);
            DispositionPricingTests.Add(cases);
            ClockMigrationTests.Add(cases);
            MovementClockTests.Add(cases);
            MovementVariationTests.Add(cases);
            WalkingHeadingTests.Add(cases);
            OccupiedApproachTests.Add(cases);
            ResidentClockMigrationTests.Add(cases);
            int failures = 0;
            foreach (var test in cases)
            {
                try { test.Body(); Console.WriteLine("PASS " + test.Name); }
                catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
            }
            Console.WriteLine($"{cases.Count - failures}/{cases.Count} passed; {failures} failed");
            return failures == 0 ? 0 : 1;
        }
    }

    internal static class Check
    {
        public static void True(bool value, string message = "Condition was false")
        {
            if (!value) throw new Exception(message);
        }
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"Expected {expected}; got {actual}");
        }
        public static void Near(double expected, double actual, double tolerance = 0.000001)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new Exception($"Expected {expected}; got {actual}");
        }
        public static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
    }
}
