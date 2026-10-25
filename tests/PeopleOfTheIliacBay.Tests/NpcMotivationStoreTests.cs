using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcMotivationStoreTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("motivation reads do not allocate and unknown identities stay absent", ReadIsPure));
            cases.Add(("motivation updates require current owner ticket and commit atomically", CommitIsOwnedAndAtomic));
            cases.Add(("motivation tickets expire on restore and cannot cross stores", TicketBindingExpires));
            cases.Add(("store captures persistent agenda with the conversation branch and transient inputs", StoreCaptureUsesBoundConversation));
            cases.Add(("motivation ledger round-trips and malformed restore leaves live state unchanged", RestoreIsStrictAndAtomic));
        }

        private static void ReadIsPure()
        {
            var store = KnownNativeStore(out string owner);
            var missing = store.ReadNpcMotivationState(owner);
            Check.True(missing != null, "known NPC should have an empty agenda view");
            Check.Equal(owner, missing.OwnerPersonId);
            Check.Equal(0L, missing.Revision);
            Check.Equal(0, missing.Needs.Count);
            Check.True(store.ReadNpcMotivationState("npc:unknown") == null);
            Check.Equal(0, store.Snapshot().NpcMotivations.Count);
            Check.True(store.TryCaptureNpcMotivationUpdate(owner, out var ticket));
            Check.Equal(0, store.Snapshot().NpcMotivations.Count);
            Check.Equal(0L, ticket.ExpectedRevision);
        }

        private static void CommitIsOwnedAndAtomic()
        {
            var store = KnownNativeStore(out string owner);
            var vitals = CaptureInjury(store, owner, 12);
            Check.True(store.TryCaptureNpcMotivationUpdate(owner, out var ticket));
            var before = store.Snapshot();
            var need = NativeHealthNeed(owner, ticket.ExpectedRevision, vitals, 12);
            var goal = NativeRecoveryGoal(owner, ticket.ExpectedRevision + 1, vitals, 12);
            Check.True(!store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Warrior,
                need, NativeRecoveryGoal("npc:unknown", ticket.ExpectedRevision + 1, vitals, 12)),
                "invalid second update must roll back the entire pair");
            Check.Equal(0, store.Snapshot().NpcMotivations.Count);
            Check.True(store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Warrior, need, goal));
            Check.Equal(1, store.Snapshot().NpcMotivations.Count);
            var view = store.ReadNpcMotivationState(owner);
            Check.Equal(2L, view.Revision);
            Check.Equal(ExpectedHealthIntensity(vitals), view.Needs[0].Intensity.Value);
            Check.Equal(NpcMotivationNeedKind.Health, view.Needs[0].Kind);
            Check.Equal(1, view.Goals.Count);
            Check.Equal(0, before.NpcMotivations.Count);

            var committed = store.Snapshot();
            Check.True(!store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Warrior, need, goal));
            Check.True(!store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Warrior,
                NativeHealthNeed("npc:unknown", ticket.ExpectedRevision, vitals, 12), goal));
            Check.Equal(2L, store.ReadNpcMotivationState(owner).Revision);
            Check.Equal(2, store.Snapshot().NpcMotivations[0].AcceptedUpdates.Count);
            Check.Equal(committed.NpcMotivations[0].Revision, store.Snapshot().NpcMotivations[0].Revision);
        }

        private static void TicketBindingExpires()
        {
            var store = KnownNativeStore(out string owner);
            var other = KnownNativeStore(out string otherOwner);
            Check.True(store.TryCaptureNpcMotivationUpdate(owner, out var ticket));
            Check.True(!other.TryCommitNpcMotivationUpdate(ticket,
                Need(owner, 0, "operation:cross-store", 1, "evidence:cross-store", 1, 25)));
            Check.Equal(0, other.Snapshot().NpcMotivations.Count);

            var preRestore = store.Snapshot();
            store.Restore(preRestore);
            Check.True(!store.TryCommitNpcMotivationUpdate(ticket,
                Need(owner, 0, "operation:old-epoch", 1, "evidence:old-epoch", 1, 25)));
            Check.Equal(0, store.Snapshot().NpcMotivations.Count);

            // Persisted root scope is stable across an ordinary save/load, but the live owner/epoch still changes.
            Check.True(store.TryCaptureNpcMotivationUpdate(owner, out var currentTicket));
            var restored = new WorldStateStore(0);
            restored.Restore(store.Snapshot());
            Check.Equal(store.NpcFrameworkScopeId, restored.NpcFrameworkScopeId);
            Check.True(!restored.TryCommitNpcMotivationUpdate(currentTicket,
                Need(owner, 0, "operation:loaded-store", 1, "evidence:loaded-store", 1, 25)));
            Check.Equal(0, restored.Snapshot().NpcMotivations.Count);
        }

        private static void StoreCaptureUsesBoundConversation()
        {
            var store = KnownNativeStore(out string owner);
            var vitals = CaptureInjury(store, owner, 8);
            Check.True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Warrior, 8));
            var conversation = Conversation(owner, 100, out string branch);
            var clock = new NpcMotivationGameClock(100, 2, Evidence(branch, owner, "clock:current", NpcMotivationEvidenceKind.WorldClock, 2, 100));
            var profile = new NpcMotivationProfileReference(owner, 3,
                Evidence(branch, owner, "profile:current", NpcMotivationEvidenceKind.PersonalityProfile, 3, 100));
            var transient = new[] { new NpcMotivationCapabilityConstraint("capability:travel", NpcMotivationCapabilityKind.Travel,
                NpcMotivationAvailability.Unavailable, NpcMotivationKnowledgeState.Known, 4,
                Evidence(branch, owner, "native:travel", NpcMotivationEvidenceKind.NativeObservation, 4, 99)) };
            var context = store.CaptureNpcMotivationContext(conversation, clock, profile, null, transient, null, null, null);
            Check.Equal(branch, context.SaveBranchId);
            Check.Equal(owner, context.ObserverId);
            Check.Equal(1, context.Needs.Count);
            Check.Equal(ExpectedHealthIntensity(vitals), context.Needs[0].Intensity.Value);
            Check.Equal(1, context.Goals.Count);
            Check.Equal(1, context.Capabilities.Count);
            Check.Equal(1, store.Snapshot().NpcMotivations.Count);
            Check.Throws<ArgumentException>(() => store.CaptureNpcMotivationContext(conversation,
                new NpcMotivationGameClock(99, 2, Evidence(branch, owner, "clock:wrong-time", NpcMotivationEvidenceKind.WorldClock, 2, 99)),
                profile, null, null, null, null, null));
        }

        private static void RestoreIsStrictAndAtomic()
        {
            var store = KnownNativeStore(out string owner);
            var vitals = CaptureInjury(store, owner, 12);
            Check.True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Warrior, 12));
            int intensity = ExpectedHealthIntensity(vitals);
            var saved = store.Snapshot();
            var restored = new WorldStateStore(0);
            restored.Restore(saved);
            Check.Equal(1, restored.Snapshot().NpcMotivations.Count);
            Check.Equal(intensity, restored.ReadNpcMotivationState(owner).Needs[0].Intensity.Value);
            Check.Equal(store.NpcFrameworkScopeId, restored.NpcFrameworkScopeId);

            var duplicate = store.Snapshot();
            duplicate.NpcMotivations.Add(NpcMotivationState.CopyValidated(duplicate.NpcMotivations[0]));
            Check.Throws<ArgumentException>(() => store.Restore(duplicate));
            Check.Equal(intensity, store.ReadNpcMotivationState(owner).Needs[0].Intensity.Value);

            var missing = store.Snapshot();
            missing.NpcMotivations = null;
            Check.Throws<ArgumentException>(() => store.Restore(missing));
            Check.Equal(1, store.Snapshot().NpcMotivations.Count);

            var legacy = store.Snapshot();
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(legacy, 37);
            legacy.NpcMotivations = null;
            var migrated = new WorldStateStore(0);
            migrated.Restore(legacy);
            Check.Equal(0, migrated.Snapshot().NpcMotivations.Count);
            Check.Equal(0L, migrated.ReadNpcMotivationState(owner).Revision);
        }

        private static WorldStateStore KnownNativeStore(out string owner)
        {
            var store = new WorldStateStore(42);
            var adult = store.GetOrCreate(10, new[] { 20 }, 1).Households[0].Residents
                .Find(person => person.AgeGroup == ResidentAgeGroup.Adult);
            Check.True(adult != null, "fixture requires an adult resident");
            owner = adult.Id;
            return store;
        }

        private static NpcVitalsView CaptureInjury(WorldStateStore store, string owner, double now)
        {
            var baseline = store.ReadNpcVitals(owner, NpcArchetype.Warrior);
            Check.True(store.TryCaptureNpcVitals(owner, NpcArchetype.Warrior,
                Math.Max(1, baseline.MaximumHealth - 7), baseline.CurrentMagicka, now, baseline.Revision));
            return store.ReadNpcVitals(owner, NpcArchetype.Warrior);
        }

        private static int ExpectedHealthIntensity(NpcVitalsView vitals)
        { return (int)Math.Ceiling(100.0 * (vitals.MaximumHealth - vitals.CurrentHealth) / vitals.MaximumHealth); }

        private static NpcMotivationStateUpdateProposal NativeHealthNeed(string owner, long expected,
            NpcVitalsView vitals, double now)
        {
            string revision = vitals.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string evidenceId = "native-health:v1:need:" + revision + ":" + vitals.CurrentHealth + ":" + vitals.MaximumHealth;
            return NpcMotivationStateUpdateProposal.ForNeed(owner, expected, "motivation:native-health:v1:need:" + revision,
                now, NpcHealthNeedLifecycle.HealthNeedId, NpcMotivationNeedKind.Health, NpcMotivationKnowledgeState.Known,
                ExpectedHealthIntensity(vitals), new NpcMotivationStateEvidence(owner, evidenceId,
                    NpcMotivationStateEvidenceKind.NativeObservation, expected + 1, now));
        }

        private static NpcMotivationStateUpdateProposal NativeRecoveryGoal(string owner, long expected,
            NpcVitalsView vitals, double now)
        {
            string revision = vitals.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string evidenceId = "native-health:v1:goal:" + revision + ":" + vitals.CurrentHealth + ":" + vitals.MaximumHealth;
            var priority = 4L * vitals.CurrentHealth <= vitals.MaximumHealth ? NpcMotivationPriorityLevel.Emergency
                : 2L * vitals.CurrentHealth <= vitals.MaximumHealth ? NpcMotivationPriorityLevel.Urgent
                : NpcMotivationPriorityLevel.Important;
            return NpcMotivationStateUpdateProposal.ForGoal(owner, expected, "motivation:native-health:v1:goal:" + revision,
                now, NpcHealthNeedLifecycle.RecoveryGoalId, NpcMotivationGoalKind.Recovery,
                NpcMotivationGoalStatus.Active, priority, NpcMotivationKnowledgeState.Known,
                new NpcMotivationStateEvidence(owner, evidenceId,
                    NpcMotivationStateEvidenceKind.NativeObservation, expected + 1, now));
        }

        private static NpcMotivationStateUpdateProposal Need(string owner, long expected, string operation, double now,
            string evidenceId, long sourceRevision, int intensity)
        {
            return NpcMotivationStateUpdateProposal.ForNeed(owner, expected, operation, now, "need:food",
                NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known, intensity,
                new NpcMotivationStateEvidence(owner, evidenceId, NpcMotivationStateEvidenceKind.NativeObservation, sourceRevision, now));
        }

        private static ConversationContextSnapshot Conversation(string owner, double now, out string branch)
        {
            const string archive = "0123456789abcdef0123456789abcdef";
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, owner, null,
                "session:motivation-store", 1, 7, archive, "", "scene:home", 1);
            branch = binding.SaveEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + archive;
            var text = ConversationContextFact<string>.Verified("clock", ConversationContextFactSource.LiveObservation, now);
            var name = ConversationContextFact<string>.Verified("Mira", ConversationContextFactSource.NativeActor, now);
            var role = ConversationContextFact<ConversationDialogueRole>.Verified(ConversationDialogueRole.ManagedResident,
                ConversationContextFactSource.NativeActor, now);
            var scene = ConversationContextFact<string>.Verified("Outside", ConversationContextFactSource.LiveObservation, now);
            var unknownString = ConversationContextFact<string>.Unknown();
            return new ConversationContextSnapshot(binding, now, text, name, role, scene, unknownString,
                ConversationContextFact<ConversationNpcPosition>.Unknown(), ConversationContextFact<ConversationNpcActivity>.Unknown(),
                ConversationContextFact<bool>.Unknown(), ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),
                unknownString, 0, 1, null, Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
        }

        private static NpcMotivationEvidenceRef Evidence(string branch, string observer, string id,
            NpcMotivationEvidenceKind kind, long revision, double time)
        { return new NpcMotivationEvidenceRef(branch, observer, id, kind, revision, time); }
    }
}
