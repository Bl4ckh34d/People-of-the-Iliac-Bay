using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceDeferralTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Exhausted appraisal defers oldest event and later event can commit after restore", DeferralUnblocksAndPersists));
            cases.Add(("Appraisal deferral rejects duplicate, stale, foreign-store, and wrong-observer tickets", DeferralRejectsStaleBindings));
            cases.Add(("Inbox version one migration preserves pending and consumed rows", VersionOneMigrationPreservesStatuses));
        }

        private static void DeferralUnblocksAndPersists()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            NpcExperienceCommitTests.Enqueue(store, observer, "first", 10);
            NpcExperienceCommitTests.Enqueue(store, observer, "second", 11);

            NpcExperienceAppraisalTicket first;
            True(store.TryCaptureNpcExperienceAppraisal(observer, 12, out first));
            Equal("first", first.Request.CanonicalEvidenceId);
            True(store.TryDeferNpcExperienceAppraisal(observer, first, 13));
            False(store.TryDeferNpcExperienceAppraisal(observer, first, 13));

            var afterDeferral = store.Snapshot();
            var deferred = afterDeferral.NpcExperienceInbox.Entries.Single(x => x.OutcomeId == "first");
            Equal(NpcExperienceInboxStatus.Deferred, deferred.Status);
            Equal<NpcExperienceAppliedAppraisal>(null, deferred.Appraisal);
            Equal(2L, deferred.Revision);
            Equal(13d, deferred.UpdatedAt);
            Equal(1, store.ReadPendingNpcExperiences(observer, 10).Count);
            Equal("second", store.ReadPendingNpcExperiences(observer, 10)[0].OutcomeId);
            Equal(0, store.CaptureConversationMood(observer).Value);

            var restored = new WorldStateStore(0);
            restored.Restore(afterDeferral);
            Equal(NpcExperienceInboxStatus.Deferred, restored.Snapshot().NpcExperienceInbox.Entries
                .Single(x => x.OutcomeId == "first").Status);
            Equal(1, restored.ReadPendingNpcExperiences(observer, 10).Count);

            NpcExperienceAppraisalTicket second;
            True(restored.TryCaptureNpcExperienceAppraisal(observer, 13, out second));
            Equal("second", second.Request.CanonicalEvidenceId);
            True(restored.TryCommitNpcExperienceAppraisal(second, NpcExperienceCommitTests.Result(second.Request, mood: 0)));
            Equal(0, restored.ReadPendingNpcExperiences(observer, 10).Count);
            var final = restored.Snapshot().NpcExperienceInbox;
            Equal(NpcExperienceInboxStatus.Deferred, final.Entries.Single(x => x.OutcomeId == "first").Status);
            Equal(NpcExperienceInboxStatus.Consumed, final.Entries.Single(x => x.OutcomeId == "second").Status);
            Equal(0, restored.CaptureConversationMood(observer, 13).Value);
        }

        private static void DeferralRejectsStaleBindings()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            NpcExperienceCommitTests.Enqueue(store, observer, "stale", 10);
            NpcExperienceAppraisalTicket stale;
            True(store.TryCaptureNpcExperienceAppraisal(observer, 10, out stale));
            True(store.TryCommitNpcExperienceAppraisal(stale, NpcExperienceCommitTests.Result(stale.Request, mood: 0)));
            False(store.TryDeferNpcExperienceAppraisal(observer, stale, 12));
            Equal(0, store.ReadPendingNpcExperiences(observer, 10).Count);

            var otherStore = new WorldStateStore(0);
            var pendingStore = NpcExperienceCommitTests.Create(out string otherObserver);
            NpcExperienceCommitTests.Enqueue(pendingStore, otherObserver, "foreign", 10);
            NpcExperienceAppraisalTicket foreign;
            True(pendingStore.TryCaptureNpcExperienceAppraisal(otherObserver, 10, out foreign));
            var save = pendingStore.Snapshot();
            pendingStore.Restore(save);
            False(pendingStore.TryDeferNpcExperienceAppraisal(otherObserver, foreign, 12));

            True(pendingStore.TryCaptureNpcExperienceAppraisal(otherObserver, 12, out foreign));
            otherStore.Restore(pendingStore.Snapshot());
            False(otherStore.TryDeferNpcExperienceAppraisal(otherObserver, foreign, 13));
            False(pendingStore.TryDeferNpcExperienceAppraisal("npc:not-the-observer", foreign, 13));
            Equal(1, pendingStore.ReadPendingNpcExperiences(otherObserver, 10).Count);
        }

        private static void VersionOneMigrationPreservesStatuses()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            NpcExperienceCommitTests.Enqueue(store, observer, "pending", 10);
            NpcExperienceCommitTests.Enqueue(store, observer, "consumed", 11);
            NpcExperienceAppraisalTicket ticket;
            True(store.TryCaptureNpcExperienceAppraisal(observer, 11, out ticket));
            True(store.TryCommitNpcExperienceAppraisal(ticket, NpcExperienceCommitTests.Result(ticket.Request, mood: 0)));

            var legacy = store.Snapshot().NpcExperienceInbox;
            HistoricalNpcExperienceFixture.PrepareLegacyInbox(legacy, 1);
            var migrated = NpcExperienceInbox.CopyValidated(legacy, id => id == observer);
            Equal(NpcExperienceInbox.VersionCurrent, migrated.Version);
            Equal(NpcExperienceInboxStatus.Consumed, migrated.Entries.Single(x => x.OutcomeId == "pending").Status);
            Equal(NpcExperienceInboxStatus.Pending, migrated.Entries.Single(x => x.OutcomeId == "consumed").Status);

            NpcExperienceCommitTests.Enqueue(store, observer, "deferred", 12);
            True(store.TryCaptureNpcExperienceAppraisal(observer, 12, out ticket));
            True(store.TryDeferNpcExperienceAppraisal(observer, ticket, 13));
            var impossibleV1 = store.Snapshot().NpcExperienceInbox;
            HistoricalNpcExperienceFixture.PrepareLegacyInbox(impossibleV1, 1);
            Throws<ArgumentException>(() => NpcExperienceInbox.CopyValidated(impossibleV1, id => id == observer));
        }

        private static void True(bool value)
        { if (!value) throw new InvalidOperationException("Expected true."); }

        private static void False(bool value)
        { if (value) throw new InvalidOperationException("Expected false."); }

        private static void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }

        private static void Equal<T>(T expected, T actual)
        { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
    }
}
