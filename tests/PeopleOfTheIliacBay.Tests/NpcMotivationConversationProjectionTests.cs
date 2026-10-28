using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcMotivationConversationProjectionTests
    {
        private const string PersonId = "npc:projection-test";
        private const string ArchiveId = "0123456789abcdef0123456789abcdef";
        private const double CapturedTime = 100;

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("conversation motivation projection binds saved sources and only timed native duties", BindsSourcesAndDuties));
            cases.Add(("conversation motivation projection rejects a profile for another person", RejectsForeignProfile));
            cases.Add(("conversation commitments preserve optional native time bounds", CommitmentTimesAreValidatedAndCopied));
            cases.Add(("conversation motivation projection includes persisted agenda and active paid service", IncludesPersistedAgendaAndService));
            cases.Add(("conversation motivation projection excludes foreign and expired paid service", ExcludesForeignAndExpiredService));
            cases.Add(("conversation motivation projection includes only verified current keeper work duty", IncludesVerifiedKeeperWorkDuty));
            cases.Add(("conversation motivation projection excludes absent, stale, and mismatched keeper work duty", ExcludesUnverifiedKeeperWorkDuty));
        }

        private static void IncludesVerifiedKeeperWorkDuty()
        {
            const string keeperId = "npc:129:42:7";
            const string siteId = "building:129:42";
            var snapshot = StaticKeeperSnapshot(keeperId, CapturedTime, ConversationDialogueRole.Shopkeeper);
            var profile = NpcPersonalityGenerator.Generate(keeperId, 771, NpcSocialBackground.Commoner);
            var duty = NativeWorkDuty(snapshot, keeperId, siteId, 90, 130, 7, CapturedTime, keeperId);
            var store = new WorldStateStore(13);
            SeedKnownKeeper(store, keeperId);
            NpcMotivationContextSnapshot result;

            Check.True(NpcMotivationConversationProjection.TryCapture(snapshot, profile, store,
                NpcServiceContractLedger.Create(), duty, siteId, out result));
            Check.Equal(1, result.Duties.Count);
            Check.Equal("work:" + keeperId + ":" + siteId, result.Duties[0].Id);
            Check.Equal(NpcMotivationDutyKind.Work, result.Duties[0].Kind);
            Check.Equal(NpcMotivationDutyScope.RoutineWork | NpcMotivationDutyScope.Schedule, result.Duties[0].Scope);
            Check.Equal(NpcMotivationDutyStatus.Active, result.Duties[0].Status);
            Check.Equal(90d, result.Duties[0].StartsAtGameTime);
            Check.Equal(130d, result.Duties[0].EndsAtGameTime.Value);
            Check.Equal(7L, result.Duties[0].Revision);
            Check.Equal(snapshot.MotivationBranchId, result.Duties[0].Evidence.SaveBranchId);
            Check.Equal(keeperId, result.Duties[0].Evidence.ObserverId);
            Check.Equal("native-work:" + siteId, result.Duties[0].Evidence.EvidenceId);
            Check.Equal(NpcMotivationEvidenceKind.NativeObservation, result.Duties[0].Evidence.Kind);
            Check.Equal(CapturedTime, result.Duties[0].Evidence.ObservedGameTime);
            Check.Equal(0, store.Snapshot().NpcMotivations.Count);
        }

        private static void ExcludesUnverifiedKeeperWorkDuty()
        {
            const string keeperId = "npc:129:42:7";
            const string siteId = "building:129:42";
            var snapshot = StaticKeeperSnapshot(keeperId, CapturedTime, ConversationDialogueRole.Innkeeper);
            var profile = NpcPersonalityGenerator.Generate(keeperId, 771, NpcSocialBackground.Commoner);
            var store = new WorldStateStore(17);
            SeedKnownKeeper(store, keeperId);

            Check.True(NpcMotivationConversationProjection.TryCapture(snapshot, profile, store,
                NpcServiceContractLedger.Create(), null, siteId, out var noDuty));
            Check.Equal(0, noDuty.Duties.Count);

            var validDuty = NativeWorkDuty(snapshot, keeperId, siteId, 90, 130, 8, CapturedTime, keeperId);
            var managedResident = SnapshotFor(keeperId, CapturedTime, null);
            Check.True(NpcMotivationConversationProjection.TryCapture(managedResident, profile, store,
                NpcServiceContractLedger.Create(), validDuty, siteId, out var managedResult));
            Check.Equal(0, managedResult.Duties.Count);

            var patron = StaticKeeperSnapshot(keeperId, CapturedTime, ConversationDialogueRole.Unknown);
            Check.True(NpcMotivationConversationProjection.TryCapture(patron, profile, store,
                NpcServiceContractLedger.Create(), validDuty, siteId, out var patronResult));
            Check.Equal(0, patronResult.Duties.Count);

            var stale = NativeWorkDuty(snapshot, keeperId, siteId, 60, 99, 9, CapturedTime, keeperId);
            Check.True(NpcMotivationConversationProjection.TryCapture(snapshot, profile, store,
                NpcServiceContractLedger.Create(), stale, siteId, out var staleResult));
            Check.Equal(0, staleResult.Duties.Count);

            var foreignObserver = NativeWorkDuty(snapshot, keeperId, siteId, 90, 130, 10, CapturedTime, "npc:other");
            Check.True(NpcMotivationConversationProjection.TryCapture(snapshot, profile, store,
                NpcServiceContractLedger.Create(), foreignObserver, siteId, out var foreignResult));
            Check.Equal(0, foreignResult.Duties.Count);

            var wrongSite = NativeWorkDuty(snapshot, keeperId, "building:129:99", 90, 130,
                11, CapturedTime, keeperId);
            Check.True(NpcMotivationConversationProjection.TryCapture(snapshot, profile, store,
                NpcServiceContractLedger.Create(), wrongSite, siteId, out var wrongSiteResult));
            Check.Equal(0, wrongSiteResult.Duties.Count);

            var staleEvidence = NativeWorkDuty(snapshot, keeperId, siteId, 90, 130, 12, CapturedTime - 1, keeperId);
            Check.True(NpcMotivationConversationProjection.TryCapture(snapshot, profile, store,
                NpcServiceContractLedger.Create(), staleEvidence, siteId, out var staleEvidenceResult));
            Check.Equal(0, staleEvidenceResult.Duties.Count);
            Check.Equal(0, store.Snapshot().NpcMotivations.Count);
        }

        private static NpcMotivationDuty NativeWorkDuty(ConversationContextSnapshot snapshot, string personId,
            string siteId, double startsAt, double endsAt, long revision, double observedAt, string evidenceObserver)
        {
            var evidence = new NpcMotivationEvidenceRef(snapshot.MotivationBranchId, evidenceObserver,
                "native-work:" + siteId, NpcMotivationEvidenceKind.NativeObservation, revision, observedAt);
            return new NpcMotivationDuty("work:" + personId + ":" + siteId, NpcMotivationDutyKind.Work,
                NpcMotivationDutyScope.RoutineWork | NpcMotivationDutyScope.Schedule,
                NpcMotivationDutyStatus.Active, startsAt, endsAt, revision, evidence);
        }

        private static ConversationContextSnapshot StaticKeeperSnapshot(string personId, double time,
            ConversationDialogueRole role)
        {
            const string archive = "0123456789abcdef0123456789abcdef";
            var binding = new ConversationContextBinding(ConversationActorCategory.Static,
                personId, null, "session:projection", 3, 7, archive,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "scene:projection", 1);
            var clock = ConversationContextFact<string>.Verified("clock", ConversationContextFactSource.Save, time);
            var name = ConversationContextFact<string>.Verified("Native speaker", ConversationContextFactSource.NativeActor, time);
            var verifiedRole = role == ConversationDialogueRole.Unknown
                ? ConversationContextFact<ConversationDialogueRole>.Unknown()
                : ConversationContextFact<ConversationDialogueRole>.Verified(role, ConversationContextFactSource.NativeActor, time);
            var scene = ConversationContextFact<string>.Verified("Exterior", ConversationContextFactSource.Save, time);
            var personality = ConversationContextFact<string>.Verified("steady", ConversationContextFactSource.Derived, time);
            return new ConversationContextSnapshot(binding, time, clock, name, verifiedRole,
                scene, scene, ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), personality, 10, 77u,
                null, Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
        }

        private static void SeedKnownKeeper(WorldStateStore store, string expectedId)
        {
            store.GetOrCreate(129, new[] { 42 }, 1);
            NativePerson keeper = store.RegisterNativePerson(129, 42, 7, "Projection Keeper",
                ResidentSex.Female, 1, 0, 182, 2);
            Check.Equal(expectedId, keeper.Id);
        }

        private static void BindsSourcesAndDuties()
        {
            var appointment = new ConversationContextCommitment("appointment:7", "appointment", "accepted", 5,
                null, 75, 140);
            var task = new ConversationContextCommitment("task:2", "action-task", "executing", 9,
                NpcActionKind.Follow, 90, 130);
            var untimed = new ConversationContextCommitment("task:legacy", "task", "ready", 3);
            var source = Snapshot(new[] { appointment, task, untimed });
            var profile = NpcPersonalityGenerator.Generate(PersonId, 771, NpcSocialBackground.Commoner);

            NpcMotivationContextSnapshot result;
            Check.True(NpcMotivationConversationProjection.TryCapture(source, profile, out result));
            Check.True(result != null);
            Check.Equal(source.MotivationBranchId, result.SaveBranchId);
            Check.Equal(PersonId, result.ObserverId);
            Check.Equal(source.CapturedGameTime, result.CapturedGameTime);
            Check.Equal(source.Revision, result.Revision);
            Check.Equal(profile.Version, (int)result.Profile.Revision);
            Check.Equal(source.DirectedRelationship.Revision, result.Relationship.Revision);
            Check.Equal(PersonId, result.Clock.Evidence.ObserverId);
            Check.Equal(source.MotivationBranchId, result.Clock.Evidence.SaveBranchId);
            Check.Equal(CapturedTime, result.Clock.Evidence.ObservedGameTime);
            Check.Equal(0, result.Needs.Count);
            Check.Equal(0, result.Goals.Count);
            Check.Equal(0, result.Priorities.Count);
            Check.Equal(0, result.Capabilities.Count);
            Check.Equal(0, result.Resources.Count);
            Check.Equal(0, result.Threats.Count);
            Check.Equal(2, result.Duties.Count);
            Check.Equal(NpcMotivationDutyKind.Appointment, result.Duties[0].Kind);
            Check.Equal(NpcMotivationDutyStatus.Accepted, result.Duties[0].Status);
            Check.Equal(NpcMotivationDutyScope.Schedule, result.Duties[0].Scope);
            Check.Equal(75d, result.Duties[0].StartsAtGameTime);
            Check.Equal(140d, result.Duties[0].EndsAtGameTime.Value);
            Check.Equal(5L, result.Duties[0].Revision);
            Check.Equal(NpcMotivationDutyKind.Personal, result.Duties[1].Kind);
            Check.Equal(NpcMotivationDutyScope.Travel, result.Duties[1].Scope);
            Check.Equal(NpcMotivationDutyStatus.Active, result.Duties[1].Status);
            Check.Equal(90d, result.Duties[1].StartsAtGameTime);
            Check.Equal(130d, result.Duties[1].EndsAtGameTime.Value);
            Check.Equal(9L, result.Duties[1].Revision);
            Check.Equal(3, source.Commitments.Count);
        }

        private static void RejectsForeignProfile()
        {
            NpcMotivationContextSnapshot result;
            Check.True(!NpcMotivationConversationProjection.TryCapture(Snapshot(null),
                NpcPersonalityGenerator.Generate("npc:other", 12, NpcSocialBackground.Commoner), out result));
            Check.True(result == null);
            Check.True(!NpcMotivationConversationProjection.TryCapture(Snapshot(ephemeral: true),
                NpcPersonalityGenerator.Generate(PersonId, 771, NpcSocialBackground.Commoner), out result));
            Check.True(result == null);
        }

        private static void CommitmentTimesAreValidatedAndCopied()
        {
            Check.Throws<ArgumentException>(() => new ConversationContextCommitment("bad:end-before-start", "appointment",
                "accepted", 1, null, 50, 40));
            Check.Throws<ArgumentException>(() => new ConversationContextCommitment("bad:nan", "appointment",
                "accepted", 1, null, double.NaN, 60));
            var original = new ConversationContextCommitment("appointment:copy", "appointment", "accepted", 2,
                null, 50, 80);
            var source = Snapshot(new[] { original });
            Check.Equal(50d, source.Commitments[0].StartsAtGameTime.Value);
            Check.Equal(80d, source.Commitments[0].EndsAtGameTime.Value);
        }

        private static void IncludesPersistedAgendaAndService()
        {
            var store = new WorldStateStore(19);
            var adult = store.GetOrCreate(10, new[] { 20 }, 1).Households[0].Residents
                .Find(person => person.AgeGroup == ResidentAgeGroup.Adult);
            Check.True(adult != null, "fixture requires an adult resident");
            string owner = adult.Id;
            var baseline = store.ReadNpcVitals(owner, NpcArchetype.Warrior);
            Check.True(store.TryCaptureNpcVitals(owner, NpcArchetype.Warrior,
                Math.Max(1, baseline.MaximumHealth - 7), baseline.CurrentMagicka, 90, baseline.Revision));
            Check.True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Warrior, 90));
            var savedNeed = store.ReadNpcMotivationState(owner).Needs[0];

            var snapshot = SnapshotFor(owner, CapturedTime, null);
            var profile = NpcPersonalityGenerator.Generate(owner, 771, NpcSocialBackground.Commoner);
            NpcServiceContractLedger contracts = ActiveServiceLedger(owner, "contract:projection-active", 50, 75);
            NpcMotivationContextSnapshot result;
            Check.True(NpcMotivationConversationProjection.TryCapture(snapshot, profile, store, contracts, out result));
            Check.Equal(1, result.Needs.Count);
            Check.Equal(savedNeed.Intensity.Value, result.Needs[0].Intensity.Value);
            Check.Equal(owner, result.Needs[0].Evidence.ObserverId);
            Check.Equal(snapshot.MotivationBranchId, result.Needs[0].Evidence.SaveBranchId);
            Check.Equal(savedNeed.EvidenceId, result.Needs[0].Evidence.EvidenceId);
            Check.Equal(90d, result.Needs[0].Evidence.ObservedGameTime);
            Check.Equal(1, result.Goals.Count);
            Check.Equal(NpcHealthNeedLifecycle.RecoveryGoalId, result.Goals[0].Id);
            Check.Equal(0, result.Priorities.Count);
            Check.Equal(1, result.Duties.Count);
            Check.Equal("service:contract:projection-active", result.Duties[0].Id);
            Check.Equal(NpcMotivationDutyKind.Service, result.Duties[0].Kind);
            Check.Equal(NpcMotivationDutyStatus.Active, result.Duties[0].Status);
            Check.Equal(NpcMotivationDutyScope.Travel | NpcMotivationDutyScope.Combat, result.Duties[0].Scope);
            Check.Equal(75d, result.Duties[0].StartsAtGameTime);
            Check.Equal(75d + NpcServiceContract.BillingPeriodSeconds, result.Duties[0].EndsAtGameTime.Value);
            Check.Equal(2L, result.Duties[0].Revision);
            Check.Equal(snapshot.MotivationBranchId, result.Duties[0].Evidence.SaveBranchId);
            Check.Equal(owner, result.Duties[0].Evidence.ObserverId);
            Check.Equal(0, result.Capabilities.Count);
            Check.Equal(0, result.Resources.Count);
            Check.Equal(0, result.Threats.Count);
            Check.Equal(1, store.Snapshot().NpcMotivations.Count); // Capture did not allocate a second owner row.
        }

        private static void ExcludesForeignAndExpiredService()
        {
            var store = new WorldStateStore(29);
            SettlementPopulation population = store.GetOrCreate(10, new[] { 20, 21 }, 1);
            Check.True(population.Households.Count >= 2, "two requested homes should yield two households");
            Check.True(population.Households[0].Residents.Count > 0 && population.Households[1].Residents.Count > 0,
                "both fixture households need a resident");
            string owner = population.Households[0].Residents[0].Id;
            string other = population.Households[1].Residents[0].Id;
            NpcServiceContractLedger contracts = ActiveServiceLedger(owner, "contract:projection-expired", 50, 75);
            contracts = AddActiveService(contracts, other, "contract:projection-foreign", 50, 75);
            var snapshot = SnapshotFor(owner, CapturedTime + NpcServiceContract.BillingPeriodSeconds, null);
            NpcMotivationContextSnapshot result;
            Check.True(NpcMotivationConversationProjection.TryCapture(snapshot,
                NpcPersonalityGenerator.Generate(owner, 771, NpcSocialBackground.Commoner), store, contracts, out result));
            Check.Equal(0, result.Duties.Count);
            Check.Equal(0, store.Snapshot().NpcMotivations.Count);
        }

        private static NpcServiceContractLedger ActiveServiceLedger(string owner, string contractId,
            double acceptedAt, double startAt)
        { return AddActiveService(NpcServiceContractLedger.Create(), owner, contractId, acceptedAt, startAt); }

        private static NpcServiceContractLedger AddActiveService(NpcServiceContractLedger ledger, string owner,
            string contractId, double acceptedAt, double startAt)
        {
            const string payer = NpcDirectedRelationship.PlayerTargetId;
            var termsOk = NpcServiceContractTerms.TryCreate(contractId, payer, owner, "home:projection",
                NpcServiceRole.Fighter, NpcServiceCoverage.Follow | NpcServiceCoverage.Combat,
                120, false, NpcServiceRiskScope.OrdinaryCombat, Array.Empty<NpcServiceProtectedTarget>(),
                NpcServiceRestTreatment.RestNotIncluded, NpcServiceResupplyResponsibility.SeparatelyNegotiated,
                Array.Empty<NpcServiceReservedObligation>(), id => id == payer || id == owner,
                home => home == "home:projection", (id, category, revision) => true,
                (id, revision, kind, start, end) => true, out var terms);
            Check.True(termsOk);
            string recipient = owner;
            var funding = NpcServiceTransferReceipt.Create("receipt:funding:" + contractId, contractId,
                NpcServiceTransferKind.Funding, payer, recipient, 120, acceptedAt);
            Check.True(NpcServiceContractLedger.TryReserve(ledger, ledger.Revision, terms, funding, out var reserved));
            NpcServiceContract active;
            Check.True(reserved.Read(contractId).TryStart(reserved.Read(contractId).Revision, startAt, out active));
            Check.True(NpcServiceContractLedger.TryReplace(reserved, reserved.Revision, active, out var updated));
            return updated;
        }

        private static ConversationContextSnapshot SnapshotFor(string personId, double time,
            IEnumerable<ConversationContextCommitment> commitments)
        {
            const string archive = "0123456789abcdef0123456789abcdef";
            var binding = new ConversationContextBinding(ConversationActorCategory.NativeMobile,
                personId, null, "session:projection", 3, 7, archive,
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "scene:projection", 1);
            var relationship = NpcDirectedRelationship.Create(personId, NpcDirectedRelationship.PlayerTargetId).ToView();
            var text = ConversationContextFact<string>.Verified("clock", ConversationContextFactSource.Save, time);
            var scene = ConversationContextFact<string>.Verified("Exterior", ConversationContextFactSource.Save, time);
            var role = ConversationContextFact<ConversationDialogueRole>.Verified(ConversationDialogueRole.ManagedResident,
                ConversationContextFactSource.Save, time);
            var snapshot = new ConversationContextSnapshot(binding, time, text, text, role, scene, scene,
                ConversationContextFact<ConversationNpcPosition>.Unknown(), ConversationContextFact<ConversationNpcActivity>.Unknown(),
                ConversationContextFact<bool>.Unknown(), ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),
                text, 10, 77u, null, commitments ?? Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
            return snapshot.WithDirectedRelationship(relationship);
        }

        private static ConversationContextSnapshot Snapshot(IEnumerable<ConversationContextCommitment> commitments = null,
            bool ephemeral = false)
        {
            var binding = new ConversationContextBinding(ConversationActorCategory.NativeMobile,
                ephemeral ? null : PersonId, ephemeral ? "speaker:ephemeral" : null, "session:projection", 3, 7,
                ArchiveId, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "scene:projection", 1);
            var row = NpcDirectedRelationship.Create(PersonId, NpcDirectedRelationship.PlayerTargetId);
            row.Revision = 4;
            row.LastGameTime = 80;
            var snapshot = new ConversationContextSnapshot(binding, CapturedTime,
                Fact("clock"), Fact("person"), EnumFact(ConversationDialogueRole.ManagedResident),
                Fact("Exterior"), Fact("Exterior"), ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), Fact("reserved"), 10, 77u,
                null, commitments ?? Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
            return ephemeral ? snapshot : snapshot.WithDirectedRelationship(row.ToView());
        }

        private static ConversationContextFact<string> Fact(string value) =>
            ConversationContextFact<string>.Verified(value, ConversationContextFactSource.Save, CapturedTime);
        private static ConversationContextFact<T> EnumFact<T>(T value) where T : struct =>
            ConversationContextFact<T>.Verified(value, ConversationContextFactSource.Save, CapturedTime);
    }
}
