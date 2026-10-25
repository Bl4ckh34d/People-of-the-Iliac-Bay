using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcRelationshipStoreTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("World relationship reads do not materialize unknown NPC identities", () =>
            {
                var store = Create(out _, out _);
                Check.Equal(0L, store.NpcRelationshipLedgerRevision);
                Check.True(store.ReadNpcRelationship("npc:unknown", "player") == null);
                Check.True(store.ReadNpcRelationship("npc:resident:one", "npc:unknown") == null);
                Check.Equal(0L, store.NpcRelationshipLedgerRevision);
                Check.Equal(0, store.Snapshot().NpcRelationships.Relationships.Count);
            }));

            cases.Add(("Timed relationship reads project saved fear without changing the stored row", () =>
            {
                string observer;
                string target;
                var store = Create(out observer, out target);
                var profile = store.GetOrCreateNpcPersonality(observer, 41, NpcSocialBackground.Commoner);
                var evidence = Native(observer, observer, "player", "outcome:timed-fear", 0);
                Check.True(store.TryCommitNpcRelationship(Proposal(0, observer, "player", 0,
                    evidence, 0, 0, 10), 0));

                var rawBefore = store.ReadNpcRelationship(observer, "player");
                double now = 7 * 24 * 60 * 60d;
                var projected = store.ReadNpcRelationship(observer, "player", now);
                var rawAfter = store.ReadNpcRelationship(observer, "player");
                double expectedHalfLife = NpcFearRecoveryPolicy.ProvisionalDefault
                    .HalfLifeSeconds(profile, NpcFearSeverity.Moderate);

                Check.Equal(rawBefore.Revision, projected.Revision);
                Check.Equal(rawBefore.Revision, rawAfter.Revision);
                Check.Equal(rawBefore.StoredInterpersonalFear, rawAfter.StoredInterpersonalFear);
                Check.Equal(10, projected.StoredInterpersonalFear);
                Check.Equal(expectedHalfLife, projected.FearRecoveryHalfLifeSeconds);
                Check.Equal(NpcFearRecoveryPolicy.ProvisionalDefault.Project(10, 0, now, expectedHalfLife),
                    projected.ProjectedInterpersonalFear);
                Check.Equal(now, projected.ProjectedAtGameTime);
            }));

            cases.Add(("World relationship store commits player and NPC-directed rows with ledger CAS", () =>
            {
                string observer;
                string target;
                var store = Create(out observer, out target);
                var playerEvidence = Native(observer, observer, "player", "outcome:player-help", 1);
                Check.True(store.TryCommitNpcRelationship(Proposal(0, observer, "player", 0,
                    playerEvidence, 2, 1, 0), 0));
                Check.Equal(1L, store.NpcRelationshipLedgerRevision);

                var npcEvidence = Native(observer, observer, target, "outcome:npc-help", 2);
                Check.True(store.TryCommitNpcRelationship(Proposal(1, observer, target, 0,
                    npcEvidence, 0, 0, 3), 1));
                Check.Equal(2L, store.NpcRelationshipLedgerRevision);
                var toPlayer = store.ReadNpcRelationship(observer, "player");
                var toNpc = store.ReadNpcRelationship(observer, target);
                Check.True(toPlayer.TrustKnown);
                Check.Equal(52, toPlayer.ReliabilityTrust);
                Check.Equal(1, toPlayer.Resentment);
                Check.Equal(0, toPlayer.InterpersonalFear);
                Check.Equal(3, toNpc.InterpersonalFear);
                Check.Equal(2, store.Snapshot().NpcRelationships.Relationships.Count);

                Check.True(!store.TryCommitNpcRelationship(Proposal(2, observer, target, 0,
                    Native(observer, observer, target, "outcome:stale-row", 3), 1, 0, 0), 2));
                Check.True(!store.TryCommitNpcRelationship(Proposal(1, observer, target, 1,
                    Native(observer, observer, target, "outcome:stale-ledger", 4), 1, 0, 0), 1));
                Check.Equal(2L, store.NpcRelationshipLedgerRevision);
            }));

            cases.Add(("World relationship restore preserves accepted outcomes across branches and rollback", () =>
            {
                string observer;
                string target;
                var store = Create(out observer, out target);
                var before = store.Snapshot();
                var evidence = Native(observer, observer, "player", "outcome:branch-rollback", 9);
                var proposal = Proposal(0, observer, "player", 0, evidence, 4, 0, 0);
                Check.True(store.TryCommitNpcRelationship(proposal, 0));
                var accepted = store.Snapshot();

                var branch = new WorldStateStore(0);
                branch.Restore(accepted);
                Check.True(!branch.TryCommitNpcRelationship(Proposal(1, observer, "player", 1,
                    evidence, 4, 0, 0), 1));
                Check.Equal(1, branch.Snapshot().NpcRelationships.AcceptedOutcomes.Count);

                branch.Restore(before);
                Check.True(branch.TryCommitNpcRelationship(proposal, 0));
                Check.Equal(1, branch.Snapshot().NpcRelationships.AcceptedOutcomes.Count);
                Check.Equal(0, before.NpcRelationships.AcceptedOutcomes.Count);
            }));

            cases.Add(("Schema 39 migration preserves a provable fear anchor and trust history across reloads", () =>
            {
                string observer;
                string target;
                var store = Create(out observer, out target);
                store.GetOrCreateNpcPersonality(observer, 51, NpcSocialBackground.Commoner);
                Commit(store, observer, "outcome:migration-safe-fear", 10, 2, 3, 10);
                Commit(store, observer, "outcome:migration-safe-trust", 10, 1, 4, 0);

                var schema39 = AsSchema39(store.Snapshot());
                var restored = new WorldStateStore(0);
                restored.Restore(schema39);
                var raw = restored.ReadNpcRelationship(observer, "player");
                var expectedProfile = restored.ReadNpcPersonality(observer);
                var expectedHalfLife = NpcFearRecoveryPolicy.ProvisionalDefault
                    .HalfLifeSeconds(expectedProfile, NpcFearSeverity.Moderate);

                Check.True(raw.HasFearRecoveryAnchor);
                Check.Equal(10d, raw.FearRecoveryAnchorGameTime);
                Check.Equal(expectedHalfLife, raw.FearRecoveryHalfLifeSeconds);
                Check.Equal(10, raw.StoredInterpersonalFear);
                Check.Equal(53, raw.ReliabilityTrust);
                Check.Equal(2, raw.TrustEvidenceCount);
                Check.Equal(7, raw.Resentment);
                Check.Equal(2L, raw.Revision);
                Check.Equal(2, restored.Snapshot().NpcRelationships.AcceptedOutcomes.Count);
                AssertAcceptedOutcomesEqual(schema39.NpcRelationships.AcceptedOutcomes,
                    restored.Snapshot().NpcRelationships.AcceptedOutcomes);
                Check.Equal(5, restored.ReadNpcRelationship(observer, "player", 10 + expectedHalfLife)
                    .ProjectedInterpersonalFear);

                var secondReload = new WorldStateStore(0);
                secondReload.Restore(restored.Snapshot());
                var afterReload = secondReload.ReadNpcRelationship(observer, "player");
                Check.True(afterReload.HasFearRecoveryAnchor);
                Check.Equal(raw.FearRecoveryAnchorGameTime, afterReload.FearRecoveryAnchorGameTime);
                Check.Equal(raw.FearRecoveryHalfLifeSeconds, afterReload.FearRecoveryHalfLifeSeconds);
                Check.Equal(raw.StoredInterpersonalFear, afterReload.StoredInterpersonalFear);
                Check.Equal(raw.ReliabilityTrust, afterReload.ReliabilityTrust);
                Check.Equal(raw.TrustEvidenceCount, afterReload.TrustEvidenceCount);
                Check.Equal(raw.Resentment, afterReload.Resentment);
                Check.Equal(raw.Revision, afterReload.Revision);
                Check.Equal(2, secondReload.Snapshot().NpcRelationships.AcceptedOutcomes.Count);
                AssertAcceptedOutcomesEqual(schema39.NpcRelationships.AcceptedOutcomes,
                    secondReload.Snapshot().NpcRelationships.AcceptedOutcomes);
            }));

            cases.Add(("Schema 39 migration leaves mixed, clipped, zero and unattributed fear timing unknown", () =>
            {
                var store = new WorldStateStore(88);
                var observers = new List<string>();
                for (int i = 0; i < 4; i++)
                    observers.Add(store.GetOrCreate(100 + i, new[] { 100 }, 1).Households[0].Residents[0].Id);

                Commit(store, observers[0], "outcome:mixed-positive", 10, 2, 3, 10);
                Commit(store, observers[0], "outcome:mixed-negative", 10, 0, 0, -5);
                for (int i = 0; i < 10; i++)
                    Commit(store, observers[1], "outcome:clipped-" + i, 10,
                        i == 0 ? 1 : 0, i == 0 ? 2 : 0, 10);
                Commit(store, observers[2], "outcome:zero-fear", 10, 2, 6, 0);
                Commit(store, observers[3], "outcome:unattributed", 10, 1, 4, 0);

                var schema39 = AsSchema39(store.Snapshot());
                var unattributed = schema39.NpcRelationships.Relationships.Find(
                    row => row.ObserverId == observers[3] && row.TargetId == "player");
                unattributed.InterpersonalFear = 17;
                var restored = new WorldStateStore(0);
                restored.Restore(schema39);

                var mixed = restored.ReadNpcRelationship(observers[0], "player");
                var clipped = restored.ReadNpcRelationship(observers[1], "player");
                var zero = restored.ReadNpcRelationship(observers[2], "player");
                var unknown = restored.ReadNpcRelationship(observers[3], "player");
                Check.Equal(5, mixed.InterpersonalFear);
                Check.True(!mixed.HasFearRecoveryAnchor);
                Check.Equal(52, mixed.ReliabilityTrust);
                Check.Equal(3, mixed.Resentment);
                Check.Equal(2L, mixed.Revision);
                Check.Equal(100, clipped.InterpersonalFear);
                Check.True(!clipped.HasFearRecoveryAnchor);
                Check.Equal(51, clipped.ReliabilityTrust);
                Check.Equal(2, clipped.Resentment);
                Check.Equal(10L, clipped.Revision);
                Check.Equal(0, zero.InterpersonalFear);
                Check.True(!zero.HasFearRecoveryAnchor);
                Check.Equal(52, zero.ReliabilityTrust);
                Check.Equal(6, zero.Resentment);
                Check.Equal(17, unknown.InterpersonalFear);
                Check.True(!unknown.HasFearRecoveryAnchor);
                Check.Equal(51, unknown.ReliabilityTrust);
                Check.Equal(4, unknown.Resentment);
                Check.Equal(14, restored.Snapshot().NpcRelationships.AcceptedOutcomes.Count);
                AssertAcceptedOutcomesEqual(schema39.NpcRelationships.AcceptedOutcomes,
                    restored.Snapshot().NpcRelationships.AcceptedOutcomes);

                var secondReload = new WorldStateStore(0);
                secondReload.Restore(restored.Snapshot());
                foreach (string observer in observers)
                {
                    var once = restored.ReadNpcRelationship(observer, "player");
                    var twice = secondReload.ReadNpcRelationship(observer, "player");
                    Check.Equal(once.InterpersonalFear, twice.InterpersonalFear);
                    Check.Equal(once.HasFearRecoveryAnchor, twice.HasFearRecoveryAnchor);
                    Check.Equal(once.FearRecoveryAnchorGameTime, twice.FearRecoveryAnchorGameTime);
                    Check.Equal(once.ReliabilityTrust, twice.ReliabilityTrust);
                    Check.Equal(once.Resentment, twice.Resentment);
                    Check.Equal(once.Revision, twice.Revision);
                }
                Check.Equal(14, secondReload.Snapshot().NpcRelationships.AcceptedOutcomes.Count);
                AssertAcceptedOutcomesEqual(schema39.NpcRelationships.AcceptedOutcomes,
                    secondReload.Snapshot().NpcRelationships.AcceptedOutcomes);
            }));

            cases.Add(("Schema 33 migration preserves mood and personality and starts an empty relationship ledger", () =>
            {
                string observer;
                string target;
                var store = Create(out observer, out target);
                store.GetOrCreateNpcPersonality(observer, 31, NpcSocialBackground.Noble);
                store.TryCommitNpcRelationship(Proposal(0, observer, "player", 0,
                    Native(observer, observer, "player", "outcome:legacy", 1), 1, 0, 0), 0);
                var schema33 = store.Snapshot();
                schema33.Version = 33;
                schema33.ConversationMoods.Add(new ConversationMood
                {
                    PersonId = observer, Value = 19, AcceptedTurnSequence = 4,
                    HasTimeAnchor = true, UpdatedAt = 88, Revision = 3
                });
                var restored = new WorldStateStore(0);
                restored.Restore(schema33);
                Check.Equal(0, restored.Snapshot().NpcRelationships.Relationships.Count);
                Check.Equal(0, restored.Snapshot().NpcRelationships.AcceptedOutcomes.Count);
                Check.Equal(0L, restored.NpcRelationshipLedgerRevision);
                Check.True(restored.ReadNpcPersonality(observer) != null);
                var mood = restored.CaptureConversationMood(observer);
                Check.Equal(19, mood.Value);
                Check.Equal(4L, mood.AcceptedTurnSequence);
                Check.Equal(3L, mood.Revision);
                Check.Equal(88d, mood.UpdatedAt);
            }));

            cases.Add(("Malformed relationship identities and incomplete current-schema ledger reject restore atomically", () =>
            {
                string observer;
                string target;
                var store = Create(out observer, out target);
                var evidence = Native(observer, observer, "player", "outcome:atomic-restore", 1);
                Check.True(store.TryCommitNpcRelationship(Proposal(0, observer, "player", 0,
                    evidence, 2, 0, 0), 0));
                var accepted = store.Snapshot();
                var view = store.ReadNpcRelationship(observer, "player");

                var unknownIdentity = store.Snapshot();
                unknownIdentity.NpcRelationships.Relationships[0].ObserverId = "npc:unknown";
                Check.Throws<ArgumentException>(() => store.Restore(unknownIdentity));
                Check.Equal(view.ReliabilityTrust, store.ReadNpcRelationship(observer, "player").ReliabilityTrust);

                var missing = store.Snapshot();
                missing.NpcRelationships = null;
                Check.Throws<ArgumentException>(() => store.Restore(missing));
                Check.Equal(1, store.Snapshot().NpcRelationships.AcceptedOutcomes.Count);

                var partial = store.Snapshot();
                partial.NpcRelationships.AcceptedOutcomes.Clear();
                Check.Throws<ArgumentException>(() => store.Restore(partial));
                Check.Equal(1, store.Snapshot().NpcRelationships.AcceptedOutcomes.Count);
                Check.Equal(1, accepted.NpcRelationships.AcceptedOutcomes.Count);
            }));
        }

        private static WorldStateStore Create(out string observerId, out string targetId)
        {
            var store = new WorldStateStore(88);
            observerId = store.GetOrCreate(10, new[] { 100 }, 1).Households[0].Residents[0].Id;
            targetId = store.GetOrCreate(11, new[] { 100 }, 1).Households[0].Residents[0].Id;
            return store;
        }

        private static NpcExperienceEvent Native(string observer, string subject, string target,
            string outcomeId, double time)
        {
            return NpcExperienceEvent.FromNativeOutcome(observer, subject, "receipt:" + outcomeId,
                "episode:world-store", outcomeId, NpcExperienceEventKind.ObligationFulfilled, time,
                NpcResponsibleActorAttribution.Known, target);
        }

        private static void Commit(WorldStateStore store, string observer, string outcomeId,
            double time, int trust, int resentment, int fear)
        {
            var current = store.ReadNpcRelationship(observer, "player");
            long relationshipRevision = current == null ? 0 : current.Revision;
            var evidence = Native(observer, observer, "player", outcomeId, time);
            Check.True(store.TryCommitNpcRelationship(Proposal(store.NpcRelationshipLedgerRevision,
                observer, "player", relationshipRevision, evidence, trust, resentment, fear),
                store.NpcRelationshipLedgerRevision));
        }

        private static WorldState AsSchema39(WorldState state)
        {
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(state, 39);
            state.NpcRelationships.Version = 1;
            foreach (var row in state.NpcRelationships.Relationships) row.Version = 1;
            return state;
        }

        private static void AssertAcceptedOutcomesEqual(
            List<NpcAcceptedRelationshipOutcome> expected,
            List<NpcAcceptedRelationshipOutcome> actual)
        {
            Check.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Check.Equal(expected[i].ObserverId, actual[i].ObserverId);
                Check.Equal(expected[i].TargetId, actual[i].TargetId);
                Check.Equal(expected[i].ReceiptId, actual[i].ReceiptId);
                Check.Equal(expected[i].OutcomeId, actual[i].OutcomeId);
                Check.Equal(expected[i].DeduplicationKey, actual[i].DeduplicationKey);
                Check.Equal(expected[i].ObservedGameTime, actual[i].ObservedGameTime);
                Check.Equal(expected[i].TrustDelta, actual[i].TrustDelta);
                Check.Equal(expected[i].ResentmentDelta, actual[i].ResentmentDelta);
                Check.Equal(expected[i].FearDelta, actual[i].FearDelta);
            }
        }

        private static NpcRelationshipLedgerUpdateProposal Proposal(long ledgerRevision, string observer,
            string target, long relationshipRevision, NpcExperienceEvent evidence, int trust, int resentment, int fear)
        {
            return new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = ledgerRevision,
                ObserverId = observer,
                TargetId = target,
                RelationshipUpdate = new NpcRelationshipUpdateProposal
                {
                    ExpectedRevision = relationshipRevision,
                    Evidence = evidence,
                    TrustDelta = trust,
                    ResentmentDelta = resentment,
                    InterpersonalFearDelta = fear
                }
            };
        }
    }
}
