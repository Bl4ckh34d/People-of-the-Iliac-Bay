using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Test)>
        {
            ("schema35/38/48/53 import v1-v4 detail without loss", LegacyMatrix),
            ("legacy source metadata cannot hide inside schema35 normalizer", RejectFutureFieldsBeforeNormalization),
            ("later legacy schemas reject v5 header and row fields", RejectFutureFieldsInLaterLegacy),
            ("legacy revision, duplicates and order cannot be normalized away", RejectLegacyCorruption),
            ("v5 retains absolute ordinals with legal retirement gaps", CurrentAbsoluteGaps),
            ("v5 rejects duplicate and reversed retained ordinals", RejectCurrentOrderAndDuplicates),
            ("v5 rejects counter mismatch and numeric overflow", RejectCurrentCounters),
            ("legacy per-observer overflow is lossless but blocks new admission", GrandfatheredOverflow)
        };
        int failures = 0;
        foreach (var item in cases)
        {
            try { item.Test(); Console.WriteLine("PASS " + item.Name); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + item.Name + ": " + error); }
        }
        Console.WriteLine((cases.Count - failures) + "/" + cases.Count + " passed");
        return failures == 0 ? 0 : 1;
    }

    private static (WorldState State, string Person) Fixture(int schema, int inboxVersion, int rows)
    {
        var store = new WorldStateStore(54);
        string person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        WorldState state = store.Snapshot();
        state.Version = schema;
        state.ConversationCueArchive = null;
        state.NpcMoodCauseHistoryFloors.Clear();
        state.NpcExperienceInbox = new NpcExperienceInbox { Version = inboxVersion, Revision = rows };
        for (int index = 1; index <= rows; index++) state.NpcExperienceInbox.Entries.Add(Row(person, index));
        return (state, person);
    }

    private static NpcExperienceInboxEntry Row(string person, int ordinal)
    {
        string suffix = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var evidence = NpcExperienceEvent.FromNativeOutcome(person, person,
            "receipt:legacy:" + suffix, "episode:legacy:" + suffix, "outcome:legacy:" + suffix,
            NpcExperienceEventKind.Injury, 1000d + ordinal,
            NpcResponsibleActorAttribution.Unknown);
        return NpcExperienceInboxEntry.FromNativeEvent(evidence);
    }

    private static WorldStateStore Restore(WorldState state)
    {
        var store = new WorldStateStore(0);
        store.Restore(state);
        return store;
    }

    private static void LegacyMatrix()
    {
        foreach (var version in new[] { (35, 1), (38, 2), (48, 3), (53, 4) })
        {
            var fixture = Fixture(version.Item1, version.Item2, 3);
            NpcExperienceInbox direct = NpcExperienceInbox.CopyValidated(fixture.State.NpcExperienceInbox,
                id => id == fixture.Person);
            WorldStateStore restored = Restore(fixture.State);
            NpcExperienceInbox saved = restored.Snapshot().NpcExperienceInbox;
            Equal(WorldState.CurrentVersion, restored.Snapshot().Version, "current world schema");
            Equal(NpcExperienceInbox.VersionCurrent, saved.Version, "v5 inbox");
            Equal(3L, saved.Revision, "absolute accepted frontier");
            Equal(0L, saved.RetiredCount, "no invented retirement");
            Equal(0L, saved.DetailRevision, "no invented detail mutation");
            Equal(3L, saved.LegacyImportedRevision, "legacy import frontier");
            Equal(3, saved.Entries.Count, "lossless row count");
            Equal(3, direct.Entries.Count, "public copy parity");
            for (int index = 0; index < 3; index++)
            {
                NpcExperienceInboxEntry original = fixture.State.NpcExperienceInbox.Entries[index];
                NpcExperienceInboxEntry migrated = saved.Entries[index];
                Equal(original.ReceiptId, migrated.ReceiptId, "receipt");
                Equal(original.EpisodeId, migrated.EpisodeId, "episode");
                Equal(original.OutcomeId, migrated.OutcomeId, "outcome");
                Equal(original.DeduplicationKey, migrated.DeduplicationKey, "dedupe key");
                Equal(original.ObservedGameTime, migrated.ObservedGameTime, "observed time");
                Equal(original.EventKind, migrated.EventKind, "event kind");
                Equal(original.Status, migrated.Status, "status");
                Equal((long)index + 1, migrated.EnqueueOrdinal, "imported ordinal");
                Equal(migrated.EnqueueOrdinal, direct.Entries[index].EnqueueOrdinal, "copy ordinal");
            }
            Equal(3, restored.ReadPendingNpcExperiences(fixture.Person, 10).Count, "pending public read");
        }
    }

    private static void RejectFutureFieldsBeforeNormalization()
    {
        var seed = Fixture(35, 1, 1);
        foreach (Action<NpcExperienceInboxEntry> poison in new Action<NpcExperienceInboxEntry>[]
        {
            row => row.EnqueueOrdinal = 1,
            row => row.SourceVitalsRevision = 1,
            row => row.SourceKind = NpcExperienceSourceKind.Poison,
            row => row.ImpactDetailsKind = 1,
            row => row.ObservedTurnText = "future speech"
        })
        {
            var candidate = Fixture(35, 1, 1).State;
            poison(candidate.NpcExperienceInbox.Entries[0]);
            Reject(candidate, "schema35 future row field");
        }
        foreach (Action<NpcExperienceInbox> poison in new Action<NpcExperienceInbox>[]
        {
            inbox => inbox.RetiredCount = 1,
            inbox => inbox.DetailRevision = 1,
            inbox => inbox.LegacyImportedRevision = 1,
            inbox => inbox.Version = 5
        })
        {
            var candidate = Fixture(35, 1, 1).State;
            poison(candidate.NpcExperienceInbox);
            Reject(candidate, "schema35 future header field");
        }
        Equal(1L, Restore(seed.State).Snapshot().NpcExperienceInbox.LegacyImportedRevision,
            "unpoisoned schema35 remains readable");
    }

    private static void RejectFutureFieldsInLaterLegacy()
    {
        foreach (var version in new[] { (38, 2), (48, 3), (53, 4) })
        {
            foreach (Action<NpcExperienceInboxEntry> poison in new Action<NpcExperienceInboxEntry>[]
            {
                row => row.EnqueueOrdinal = 1,
                row => row.SourceVitalsRevision = 1,
                row => row.SourceKind = NpcExperienceSourceKind.Appointment
            })
            {
                var candidate = Fixture(version.Item1, version.Item2, 1).State;
                poison(candidate.NpcExperienceInbox.Entries[0]);
                Reject(candidate, "legacy future source field");
            }
            foreach (Action<NpcExperienceInbox> poison in new Action<NpcExperienceInbox>[]
            {
                inbox => inbox.RetiredCount = 1,
                inbox => inbox.DetailRevision = 1,
                inbox => inbox.LegacyImportedRevision = 1,
                inbox => inbox.Version = 5
            })
            {
                var candidate = Fixture(version.Item1, version.Item2, 1).State;
                poison(candidate.NpcExperienceInbox);
                Reject(candidate, "legacy future retention header");
            }
        }
    }

    private static void RejectLegacyCorruption()
    {
        var badRevision = Fixture(53, 4, 2).State;
        badRevision.NpcExperienceInbox.Revision = 3;
        Reject(badRevision, "legacy revision exceeds rows");
        var duplicate = Fixture(53, 4, 2).State;
        duplicate.NpcExperienceInbox.Entries[1] = NpcExperienceInboxEntry.CopyValidated(
            duplicate.NpcExperienceInbox.Entries[0]);
        Reject(duplicate, "duplicate observer outcome");
        var wrongOrder = Fixture(53, 4, 2).State;
        wrongOrder.NpcExperienceInbox.Entries.Reverse();
        // Legacy order itself supplies ordinals. Reordering valid distinct rows must retain that order.
        NpcExperienceInbox migrated = Restore(wrongOrder).Snapshot().NpcExperienceInbox;
        Equal(wrongOrder.NpcExperienceInbox.Entries[0].OutcomeId,
            migrated.Entries[0].OutcomeId, "legacy persisted order");
        Equal(1L, migrated.Entries[0].EnqueueOrdinal, "first persisted legacy row");
    }

    private static void CurrentAbsoluteGaps()
    {
        var fixture = Fixture(54, 5, 3);
        var inbox = fixture.State.NpcExperienceInbox;
        inbox.Entries[0].EnqueueOrdinal = 1;
        inbox.Entries[1].EnqueueOrdinal = 3;
        inbox.Entries[2].EnqueueOrdinal = 5;
        inbox.Revision = 5;
        inbox.RetiredCount = 2;
        inbox.DetailRevision = 2;
        NpcExperienceInbox direct = NpcExperienceInbox.CopyValidated(inbox, id => id == fixture.Person);
        NpcExperienceInbox restored = Restore(fixture.State).Snapshot().NpcExperienceInbox;
        Equal(5L, restored.Revision, "absolute enqueue frontier");
        Equal(2L, restored.RetiredCount, "retired detail count");
        Equal(3, restored.Entries.Count, "retained count");
        Equal(3L, direct.Entries[1].EnqueueOrdinal, "public copy retains gap");
        Equal(5L, restored.Entries[2].EnqueueOrdinal, "restore retains gap");
        // This row arrangement is a compatibility fixture, not evidence that maintenance retired it.
    }

    private static void RejectCurrentOrderAndDuplicates()
    {
        var duplicate = Fixture(54, 5, 2).State;
        duplicate.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 1;
        duplicate.NpcExperienceInbox.Entries[1].EnqueueOrdinal = 2;
        duplicate.NpcExperienceInbox.Entries[1] = NpcExperienceInboxEntry.CopyValidated(
            duplicate.NpcExperienceInbox.Entries[0]);
        duplicate.NpcExperienceInbox.Entries[1].EnqueueOrdinal = 2;
        Reject(duplicate, "duplicate deduplication key");
        var reversed = Fixture(54, 5, 2).State;
        reversed.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 2;
        reversed.NpcExperienceInbox.Entries[1].EnqueueOrdinal = 1;
        Reject(reversed, "reverse ordinals");
        var repeated = Fixture(54, 5, 2).State;
        repeated.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 1;
        repeated.NpcExperienceInbox.Entries[1].EnqueueOrdinal = 1;
        Reject(repeated, "duplicate ordinal");
        var beyondFrontier = Fixture(54, 5, 1).State;
        beyondFrontier.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 2;
        Reject(beyondFrontier, "ordinal beyond revision");
    }

    private static void RejectCurrentCounters()
    {
        foreach (Action<NpcExperienceInbox> poison in new Action<NpcExperienceInbox>[]
        {
            inbox => inbox.Revision = -1,
            inbox => inbox.Revision = long.MaxValue,
            inbox => inbox.RetiredCount = -1,
            inbox => inbox.RetiredCount = 1,
            inbox => inbox.DetailRevision = -1,
            inbox => inbox.LegacyImportedRevision = -1,
            inbox => inbox.LegacyImportedRevision = long.MaxValue
        })
        {
            var candidate = Fixture(54, 5, 1).State;
            candidate.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 1;
            poison(candidate.NpcExperienceInbox);
            Reject(candidate, "invalid v5 counter");
        }
        var exhausted = Fixture(54, 5, 1).State;
        exhausted.NpcExperienceInbox.Revision = long.MaxValue;
        exhausted.NpcExperienceInbox.RetiredCount = long.MaxValue - 1;
        exhausted.NpcExperienceInbox.Entries[0].EnqueueOrdinal = long.MaxValue;
        Equal(long.MaxValue, Restore(exhausted).Snapshot().NpcExperienceInbox.Revision,
            "valid exhausted absolute frontier does not overflow during restore");
    }

    private static void GrandfatheredOverflow()
    {
        int count = NpcExperienceInbox.MaximumRetainedPerObserver + 1;
        var legacy = Fixture(53, 4, count);
        WorldStateStore restored = Restore(legacy.State);
        NpcExperienceInbox imported = restored.Snapshot().NpcExperienceInbox;
        Equal(count, imported.Entries.Count, "lossless historical overflow");
        Equal((long)count, imported.LegacyImportedRevision, "grandfather frontier");
        Equal((long)count, imported.Revision, "accepted ordinal unchanged");
        var issued = restored.TryIssueNpcPoisonAssignment(restored.CaptureNpcVitalsWriteEpoch(),
            legacy.Person, new object(), new object());
        Check(issued != null, "source ticket issued");
        NpcExperienceEvent projected;
        Equal(NpcPoisonCaptureDisposition.Pending,
            restored.TryRecordNpcPoisonAssignment(issued, "poison:grandfathered", 50000, out projected),
            "new source stays pending at cap");
        Check(projected == null, "no new inbox admission");
        Equal((long)count, restored.Snapshot().NpcExperienceInbox.Revision,
            "cap cannot advance absolute frontier");
        var forgedNewRow = Fixture(54, 5, count + 1).State;
        forgedNewRow.NpcExperienceInbox.LegacyImportedRevision = count;
        for (int index = 0; index < forgedNewRow.NpcExperienceInbox.Entries.Count; index++)
            forgedNewRow.NpcExperienceInbox.Entries[index].EnqueueOrdinal = index + 1;
        Reject(forgedNewRow, "post-import overflow is not grandfathered");
    }

    private static void Reject(WorldState state, string message)
    {
        try { Restore(state); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new Exception("accepted corruption: " + message);
    }

    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }

    private static void Equal<T>(T expected, T actual, string message)
    { if (!Equals(expected, actual)) throw new Exception(message + ": expected " + expected + ", got " + actual); }
}
