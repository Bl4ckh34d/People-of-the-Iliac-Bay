using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Test)>
        {
            ("v5 retains absolute ordinals with legal retirement gaps", CurrentAbsoluteGaps),
            ("v5 rejects duplicate and reversed retained ordinals", RejectCurrentOrderAndDuplicates),
            ("v5 rejects counter mismatch and numeric overflow", RejectCurrentCounters),
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

    private static (WorldState State, string Person) Fixture(int rows)
    {
        var store = new WorldStateStore(54);
        string person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        WorldState state = store.Snapshot();
        state.Version = WorldState.CurrentVersion;
        state.ConversationCueArchive = null;
        state.NpcMoodCauseHistoryFloors.Clear();
        state.NpcExperienceInbox = new NpcExperienceInbox { Version = NpcExperienceInbox.VersionCurrent, Revision = rows };
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

    private static void CurrentAbsoluteGaps()
    {
        var fixture = Fixture(3);
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
        var duplicate = Fixture(2).State;
        duplicate.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 1;
        duplicate.NpcExperienceInbox.Entries[1].EnqueueOrdinal = 2;
        duplicate.NpcExperienceInbox.Entries[1] = NpcExperienceInboxEntry.CopyValidated(
            duplicate.NpcExperienceInbox.Entries[0]);
        duplicate.NpcExperienceInbox.Entries[1].EnqueueOrdinal = 2;
        Reject(duplicate, "duplicate deduplication key");
        var reversed = Fixture(2).State;
        reversed.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 2;
        reversed.NpcExperienceInbox.Entries[1].EnqueueOrdinal = 1;
        Reject(reversed, "reverse ordinals");
        var repeated = Fixture(2).State;
        repeated.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 1;
        repeated.NpcExperienceInbox.Entries[1].EnqueueOrdinal = 1;
        Reject(repeated, "duplicate ordinal");
        var beyondFrontier = Fixture(1).State;
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
            var candidate = Fixture(1).State;
            candidate.NpcExperienceInbox.Entries[0].EnqueueOrdinal = 1;
            poison(candidate.NpcExperienceInbox);
            Reject(candidate, "invalid v5 counter");
        }
        var exhausted = Fixture(1).State;
        exhausted.NpcExperienceInbox.Revision = long.MaxValue;
        exhausted.NpcExperienceInbox.RetiredCount = long.MaxValue - 1;
        exhausted.NpcExperienceInbox.Entries[0].EnqueueOrdinal = long.MaxValue;
        Equal(long.MaxValue, Restore(exhausted).Snapshot().NpcExperienceInbox.Revision,
            "valid exhausted absolute frontier does not overflow during restore");
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
