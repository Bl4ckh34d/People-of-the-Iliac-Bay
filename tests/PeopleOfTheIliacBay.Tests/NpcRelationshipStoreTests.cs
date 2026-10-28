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
