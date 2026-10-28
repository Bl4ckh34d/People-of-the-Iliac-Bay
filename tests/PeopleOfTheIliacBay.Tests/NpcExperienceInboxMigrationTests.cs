using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceInboxMigrationTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("schema 35 to 37 rejects speech mask and impact-shaped inbox data", LegacyImpossibleFieldsReject));
            cases.Add(("current schema does not reinterpret missing origin as native", CurrentMissingOriginRejects));
            cases.Add(("legacy consumed row without required appraisal journal is rejected", MissingLegacyConsumedJournalRejects));
        }

        private static void LegacyDefaultsMigrate()
        {
            string observer;
            var source = CreateNativeSave(out observer);
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(source, 37);
            var row = source.NpcExperienceInbox.Entries[0];
            row.Origin = 0;
            row.AlreadyAppliedEffectsMask = 0;
            WorldStateStore restored = new WorldStateStore(0);
            restored.Restore(source);
            var migrated = restored.Snapshot().NpcExperienceInbox.Entries[0];
            Equal(NpcExperienceEventOrigin.NativeOutcome, migrated.Origin);
            Equal(NpcExperienceAppliedEffectsMask.None, migrated.AlreadyAppliedEffectsMask);
            Equal("outcome:migration", migrated.ToEvent().OutcomeId);
            Equal<NpcExperienceImpactDetails>(null, migrated.ToEvent().NativeImpactDetails);
        }

        private static void LegacyImpossibleFieldsReject()
        {
            string observer;
            var live = new WorldStateStore(0);
            var original = CreateNativeSave(out observer);
            var speech = original;
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(speech, 37);
            speech.NpcExperienceInbox.Entries[0].UtteranceId = "utterance:impossible";
            Throws<ArgumentException>(() => live.Restore(speech));
            Equal(0, live.Snapshot().NpcExperienceInbox.Entries.Count);

            var mask = CreateNativeSave(out observer);
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(mask, 37);
            mask.NpcExperienceInbox.Entries[0].AlreadyAppliedEffectsMask = NpcExperienceAppliedEffectsMask.Mood;
            Throws<ArgumentException>(() => live.Restore(mask));
            Equal(0, live.Snapshot().NpcExperienceInbox.Entries.Count);

            var impact = CreateNativeSave(out observer);
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(impact, 37);
            impact.NpcExperienceInbox.Entries[0].HealthDamageTotal = 25;
            Throws<ArgumentException>(() => live.Restore(impact));
            Equal(0, live.Snapshot().NpcExperienceInbox.Entries.Count);

            var transferImpact = CreateNativeSave(out observer);
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(transferImpact, 37);
            transferImpact.NpcExperienceInbox.Entries[0].TransferQuantity = 25;
            Throws<ArgumentException>(() => live.Restore(transferImpact));
            Equal(0, live.Snapshot().NpcExperienceInbox.Entries.Count);

            var futureKind = CreateNativeSave(out observer);
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(futureKind, 37);
            futureKind.NpcExperienceInbox.Entries[0].EventKind = NpcExperienceEventKind.GiftReceived;
            Throws<ArgumentException>(() => live.Restore(futureKind));
            Equal(0, live.Snapshot().NpcExperienceInbox.Entries.Count);
        }

        private static void CurrentMissingOriginRejects()
        {
            string observer;
            var source = CreateNativeSave(out observer);
            source.NpcExperienceInbox.Entries[0].Origin = 0;
            var live = new WorldStateStore(0);
            Throws<ArgumentException>(() => live.Restore(source));
            Equal(0, live.Snapshot().NpcExperienceInbox.Entries.Count);
        }

        private static void MissingLegacyConsumedJournalRejects()
        {
            string observer;
            var source = CreateNativeSave(out observer);
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(source, 37);
            var row = source.NpcExperienceInbox.Entries[0];
            row.Origin = 0;
            row.Status = NpcExperienceInboxStatus.Consumed;
            row.Revision = 2;
            row.UpdatedAt = 18;
            row.Appraisal = null;
            var live = new WorldStateStore(0);
            Throws<ArgumentException>(() => live.Restore(source));
            Equal(0, live.Snapshot().NpcExperienceInbox.Entries.Count);
        }

        private static WorldState CreateNativeSave(out string observer)
        {
            var store = NpcExperienceCommitTests.Create(out observer);
            NpcExperienceCommitTests.Enqueue(store, observer, "outcome:migration", 10);
            return store.Snapshot();
        }

        private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
        private static void Equal<T>(T expected, T actual)
        { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
        private static void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }
    }
}
