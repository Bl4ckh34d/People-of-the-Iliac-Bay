using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static string Signature(WorldState value) { return JsonSerializer.Serialize(value, Json); }
    private static int Main()
    {
        var tests = new List<(string, Action)> {
            ("obsolete and future saves fail before mutation", RejectUnsupported),
            ("current save roundtrip preserves identity and meaningful memory", Roundtrip),
            ("invalid current save fails before mutation", RejectInvalidCurrent),
            ("current route timing is authoritative when loading with the game clock", CurrentRouteTiming),
            ("obsolete inbox payloads are rejected inside a current save", RejectOldInbox)
        };
        int failures = 0;
        foreach (var test in tests)
            try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
            catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Item1 + ": " + error.Message); }
        Console.WriteLine((tests.Count - failures) + "/" + tests.Count + " passed");
        return failures == 0 ? 0 : 1;
    }
    private static WorldStateStore Filled()
    {
        var store = new WorldStateStore(42);
        var person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
        store.RememberConversation(person.Id, 100, "day 1", "the inn", "My horse is Copper.", "I remember Copper.", "model");
        store.RememberSceneEvent(person.Id, "injury:1", NpcSceneEventKind.Injury, 101, 100, 10,
            "I was injured; the cause was unknown.", -3, person.Id);
        return store;
    }
    private static void RejectUnsupported()
    {
        var store = Filled();
        string before = Signature(store.Snapshot());
        for (int version = 0; version < WorldState.CurrentVersion; version++)
        {
            var old = store.Snapshot(); old.Version = version;
            Reject(() => store.Restore(old));
            Reject(() => store.Restore(old, 100));
            Equal(before, Signature(store.Snapshot()));
        }
        var future = store.Snapshot(); future.Version = WorldState.CurrentVersion + 1;
        Reject(() => store.Restore(future));
        Equal(before, Signature(store.Snapshot()));
    }
    private static void Roundtrip()
    {
        var original = Filled().Snapshot();
        string before = Signature(original);
        var restored = new WorldStateStore(999);
        restored.Restore(JsonSerializer.Deserialize<WorldState>(before, Json));
        Equal(before, Signature(restored.Snapshot()));
        var person = original.Settlements[0].Households[0].Residents[0];
        Equal(person.Id, restored.Snapshot().Settlements[0].Households[0].Residents[0].Id);
        if (!restored.RecallConversation(person.Id, "Copper", 102, 2000).Contains("Copper"))
            throw new Exception("Conversation memory lost.");
        Equal("I was injured; the cause was unknown.", restored.Snapshot().SceneMemories[0].Observation);
    }
    private static void RejectInvalidCurrent()
    {
        var store = Filled(); string before = Signature(store.Snapshot());
        var invalid = store.Snapshot(); invalid.NpcFrameworkScopeId = "invalid";
        Reject(() => store.Restore(invalid));
        Equal(before, Signature(store.Snapshot()));
    }
    private static void CurrentRouteTiming()
    {
        var store = Filled();
        var person = store.Snapshot().Settlements[0].Households[0].Residents[0];
        var itinerary = new ResidentDestination {
            Stops = new List<ResidentVisitStop> { new ResidentVisitStop { BuildingKey = 20,
                Purpose = ResidentVisitPurpose.Shop, WalkSeconds = 1200, DwellSeconds = 60,
                Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 130 } } } },
            ExitWalkSeconds = 1200,
            ExitRoute = new List<SavedRoutePoint> { new SavedRoutePoint { X = 130 }, new SavedRoutePoint() }
        };
        store.SetRoutines(new TownRoutineState { SettlementId = 100, ItineraryVersion = 1,
            Residents = new List<ResidentRoutine> { new ResidentRoutine { ResidentId = person.Id,
                Itinerary = itinerary, ItineraryDepartureSeconds = 8 * 3600 } } });
        string before = Signature(store.Snapshot());
        var restored = new WorldStateStore(0); restored.Restore(store.Snapshot(), 7 * 3600);
        Equal(before, Signature(restored.Snapshot()));
    }
    private static void RejectOldInbox()
    {
        var store = Filled(); string before = Signature(store.Snapshot());
        for (int version = 1; version < NpcExperienceInbox.VersionCurrent; version++)
        {
            var old = store.Snapshot(); old.NpcExperienceInbox.Version = version;
            try { store.Restore(old); }
            catch (ArgumentException) { Equal(before, Signature(store.Snapshot())); continue; }
            throw new Exception("Obsolete inbox payload accepted.");
        }
    }
    private static void Reject(Action operation)
    {
        try { operation(); }
        catch (ArgumentException error)
        {
            if (!error.Message.Contains("Unsupported save schema") && !error.Message.Contains("Invalid NPC framework save scope")) throw;
            return;
        }
        throw new Exception("Unsupported or invalid schema accepted.");
    }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("Expected " + expected + "; got " + actual); }
}
