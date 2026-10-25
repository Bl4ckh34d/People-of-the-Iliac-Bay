using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceInboxTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Inbox deduplicates receipt aliases by canonical outcome and keeps distinct outcomes", () =>
            {
                string observer;
                string other;
                var store = Create(out observer, out other);
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, Native(observer, observer, other, "outcome:one", 1)));
                Check.Throws<ArgumentException>(() => HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(
                    store, Native(observer, observer, other, "outcome:one", 1, "receipt:alias")));
                Check.True(!store.TryEnqueueNpcExperience(Native(observer, observer, other, "outcome:one", 1, "receipt:alias")));
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, Native(observer, observer, other, "outcome:two", 2)));
                Check.Equal(2L, store.Snapshot().NpcExperienceInbox.Revision);
                Check.Equal(2, store.Snapshot().NpcExperienceInbox.Entries.Count);
                Check.Equal("outcome:one", store.ReadPendingNpcExperiences(observer, 1)[0].OutcomeId);
                Check.Equal("outcome:two", store.ReadPendingNpcExperiences(observer, 2)[1].OutcomeId);
            }));

            cases.Add(("Unknown actor attribution can be queued without inventing a responsible identity", () =>
            {
                string observer;
                string subject;
                var store = Create(out observer, out subject);
                var evidence = NpcExperienceEvent.FromNativeOutcome(observer, subject, "receipt:unknown-actor",
                    "episode:inbox", "outcome:unknown-actor", NpcExperienceEventKind.Injury, 9,
                    NpcResponsibleActorAttribution.Unknown);
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence));
                var copy = store.ReadPendingNpcExperiences(observer, 10)[0];
                Check.Equal(NpcResponsibleActorAttribution.Unknown, copy.ResponsibleActorAttribution);
                Check.True(copy.ResponsibleActorId == null);
                var persisted = store.Snapshot().NpcExperienceInbox.Entries[0];
                Check.True(persisted.ResponsibleActorId == null);
            }));

            cases.Add(("Inbox rejects speech claims and unknown NPC observer subject or known actor", () =>
            {
                string observer;
                string target;
                var store = Create(out observer, out target);
                var claim = NpcExperienceEvent.FromAttributedSpeech(observer, observer, "receipt:claim",
                    "episode:inbox", "utterance:claim", NpcExperienceEventKind.ObligationBreached,
                    1, "player", target);
                Check.True(!store.TryEnqueueNpcExperience(claim));
                Check.True(!store.TryEnqueueNpcExperience(Native("npc:unknown", observer, target, "outcome:unknown-observer", 2)));
                Check.True(!store.TryEnqueueNpcExperience(Native(observer, "npc:unknown", target, "outcome:unknown-subject", 3)));
                Check.True(!store.TryEnqueueNpcExperience(Native(observer, observer, "npc:unknown", "outcome:unknown-actor", 4)));
                Check.True(!store.TryEnqueueNpcExperience(null));
                Check.Equal(0L, store.Snapshot().NpcExperienceInbox.Revision);
                Check.Equal(0, store.Snapshot().NpcExperienceInbox.Entries.Count);
            }));

            cases.Add(("Pending inbox reads are bounded, observer-scoped, and detached", () =>
            {
                string observer;
                string other;
                var store = Create(out observer, out other);
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, Native(observer, observer, other, "outcome:pending-a", 1)));
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, Native(other, other, observer, "outcome:pending-b", 2)));
                var beforeRevision = store.Snapshot().NpcExperienceInbox.Revision;
                var firstRead = store.ReadPendingNpcExperiences(observer, 1);
                Check.Equal(1, firstRead.Count);
                firstRead.Clear();
                Check.Equal(1, store.ReadPendingNpcExperiences(observer, 10).Count);
                Check.Equal(1, store.ReadPendingNpcExperiences(other, 10).Count);
                Check.Equal(0, store.ReadPendingNpcExperiences("npc:unknown", 10).Count);
                Check.Equal(beforeRevision, store.Snapshot().NpcExperienceInbox.Revision);
                Check.Throws<ArgumentOutOfRangeException>(() => store.ReadPendingNpcExperiences(observer, 101));
            }));

            cases.Add(("Inbox snapshot branches preserve accepted event IDs and rollback independently", () =>
            {
                string observer;
                string other;
                var store = Create(out observer, out other);
                var before = store.Snapshot();
                var evidence = Native(observer, observer, other, "outcome:branch", 4);
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence));
                var accepted = store.Snapshot();

                var restored = new WorldStateStore(0);
                restored.Restore(accepted);
                Check.True(!restored.TryEnqueueNpcExperience(Native(observer, observer, other,
                    "outcome:branch", 4, "receipt:restored-alias")));
                Check.Equal(1L, restored.Snapshot().NpcExperienceInbox.Revision);

                restored.Restore(before);
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(restored, evidence));
                Check.Equal(1L, restored.Snapshot().NpcExperienceInbox.Revision);
                Check.Equal(0, before.NpcExperienceInbox.Entries.Count);
            }));

            cases.Add(("Schema 34 migration preserves prior domains and initializes an empty inbox", () =>
            {
                string observer;
                string other;
                var store = Create(out observer, out other);
                store.GetOrCreateNpcPersonality(observer, 5, NpcSocialBackground.Noble);
                var relationship = Native(observer, observer, "player", "outcome:older-domain", 1);
                Check.True(store.TryCommitNpcRelationship(Proposal(observer, relationship, 2), 0));
                var schema34 = store.Snapshot();
                schema34.Version = 34;
                // Schema 34 did not persist an experience inbox. Do not manufacture
                // future rows and rely on migration silently discarding them.
                schema34.NpcExperienceInbox = null;
                schema34.ConversationMoods.Add(new ConversationMood
                {
                    PersonId = observer, Value = -12, AcceptedTurnSequence = 5,
                    HasTimeAnchor = true, UpdatedAt = 12, Revision = 2
                });
                var restored = new WorldStateStore(0);
                restored.Restore(schema34);
                Check.Equal(0, restored.Snapshot().NpcExperienceInbox.Entries.Count);
                Check.Equal(0L, restored.Snapshot().NpcExperienceInbox.Revision);
                Check.Equal(1L, restored.NpcRelationshipLedgerRevision);
                Check.True(restored.ReadNpcPersonality(observer) != null);
                Check.Equal(-12, restored.CaptureConversationMood(observer).Value);
                Check.Equal(5L, restored.CaptureConversationMood(observer).AcceptedTurnSequence);
                Check.Equal(2L, restored.CaptureConversationMood(observer).Revision);
            }));

            cases.Add(("Malformed current inbox restore fails atomically", () =>
            {
                string observer;
                string other;
                var store = Create(out observer, out other);
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, Native(observer, observer, other, "outcome:restore", 1)));
                var saved = store.Snapshot();
                var corruptIdentity = store.Snapshot();
                corruptIdentity.NpcExperienceInbox.Entries[0].ObserverId = "npc:unknown";
                Check.Throws<ArgumentException>(() => store.Restore(corruptIdentity));
                Check.Equal(1, store.Snapshot().NpcExperienceInbox.Entries.Count);

                var corruptKey = store.Snapshot();
                corruptKey.NpcExperienceInbox.Entries[0].DeduplicationKey = "forged-key";
                Check.Throws<ArgumentException>(() => store.Restore(corruptKey));
                Check.Equal(1, store.Snapshot().NpcExperienceInbox.Entries.Count);

                var missing = store.Snapshot();
                missing.NpcExperienceInbox = null;
                Check.Throws<ArgumentException>(() => store.Restore(missing));
                Check.Equal(1, store.Snapshot().NpcExperienceInbox.Entries.Count);

                var partial = store.Snapshot();
                partial.NpcExperienceInbox.Entries.Clear();
                Check.Throws<ArgumentException>(() => store.Restore(partial));
                Check.Equal(1, store.Snapshot().NpcExperienceInbox.Entries.Count);

                var duplicate = saved;
                duplicate.NpcExperienceInbox.Entries.Add(NpcExperienceInboxEntry.CopyValidated(
                    duplicate.NpcExperienceInbox.Entries[0]));
                duplicate.NpcExperienceInbox.Revision++;
                Check.Throws<ArgumentException>(() => store.Restore(duplicate));
                Check.Equal(1, store.Snapshot().NpcExperienceInbox.Entries.Count);
            }));
        }

        private static WorldStateStore Create(out string firstId, out string secondId)
        {
            var store = new WorldStateStore(109);
            firstId = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
            secondId = store.GetOrCreate(21, new[] { 200 }, 1).Households[0].Residents[0].Id;
            return store;
        }

        private static NpcExperienceEvent Native(string observer, string subject, string actor,
            string outcomeId, double time, string receipt = null)
        {
            return NpcExperienceEvent.FromNativeOutcome(observer, subject, receipt ?? "receipt:" + outcomeId,
                "episode:inbox", outcomeId, NpcExperienceEventKind.ObligationFulfilled, time,
                NpcResponsibleActorAttribution.Known, actor);
        }

        private static NpcRelationshipLedgerUpdateProposal Proposal(string observer, NpcExperienceEvent evidence,
            int delta)
        {
            return new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = 0,
                ObserverId = observer,
                TargetId = "player",
                RelationshipUpdate = new NpcRelationshipUpdateProposal
                { ExpectedRevision = 0, Evidence = evidence, TrustDelta = delta }
            };
        }
    }
}
