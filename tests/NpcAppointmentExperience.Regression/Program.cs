using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("confirmed attendance projects after restore without repeating memory", AttendanceRetry),
            ("confirmed no-show keeps original time and penalty through late retry", NoShowRetry),
            ("conversation claim remains singular while projection retries", ConversationClaimRetry),
            ("bounded flush skips unsupported outcomes and rotates", BoundedFlush),
            ("cancelled and NPC-absent appointments create no experience", UnsupportedOutcomes)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void AttendanceRetry()
    {
        var store = Book("retry-attendance", 1000, out var person);
        store.ObserveAppointment("retry-attendance", 1000, true, false, false);
        var inbox = LiveInbox(store);
        inbox.Revision = long.MaxValue; // Exercise real enqueue rejection without introducing a production test path.
        store.ObserveAppointment("retry-attendance", 1010, true, true, false);
        var failed = store.FindAppointment("retry-attendance");
        True(failed.OutcomeRecorded && failed.ResolvedAt == 1010, "Confirmed attendance was lost.");
        Equal(0, store.ConversationDisposition(person));
        Equal(1, ObservationCount(store, person, "attended our meet appointment"));
        Equal(0, inbox.Entries.Count);
        Equal(0, store.FlushNpcAppointmentExperiences(5000, 1));
        Equal(0, store.FlushNpcAppointmentExperiences(5001, 1));
        Equal(1, ObservationCount(store, person, "attended our meet appointment"));
        inbox.Revision = 0;
        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Equal(1, restored.FlushNpcAppointmentExperiences(100000, 1));
        Equal(1, restored.ReadPendingNpcExperiences(person, 10).Count);
        Equal(1010d, restored.ReadPendingNpcExperiences(person, 10)[0].ObservedGameTime);
        Equal(0, restored.FlushNpcAppointmentExperiences(100001, 1));
        Equal(1, ObservationCount(restored, person, "attended our meet appointment"));
        Equal(0, restored.ConversationDisposition(person));
    }

    private static void NoShowRetry()
    {
        var store = Book("retry-no-show", 2000, out var person, 3);
        double deadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment("retry-no-show"));
        for (double at = deadline - 40; at <= deadline; at += 10)
            store.ObserveAppointment("retry-no-show", at, true, false, false);
        var inbox = LiveInbox(store);
        inbox.Revision = long.MaxValue;
        store.ObserveAppointment("retry-no-show", deadline + 1, false, false, false);
        var failed = store.FindAppointment("retry-no-show");
        True(failed.OutcomeRecorded && failed.ResolvedAt == deadline + 1 && failed.NoShowPenalty > 0,
            "Confirmed no-show or its original time was lost.");
        Equal(-failed.NoShowPenalty, store.ConversationDisposition(person));
        Equal(1, ObservationCount(store, person, "missed our meet appointment"));
        inbox.Revision = 0;
        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Equal(1, restored.FlushNpcAppointmentExperiences(deadline + 86400, 1));
        var accepted = restored.FindAppointment("retry-no-show");
        Equal("player-absent", accepted.Status);
        Equal(deadline + 1, accepted.ResolvedAt);
        Equal(-accepted.NoShowPenalty, restored.ConversationDisposition(person));
        Equal(1, restored.ReadPendingNpcExperiences(person, 10).Count);
        Equal(deadline + 1, restored.ReadPendingNpcExperiences(person, 10)[0].ObservedGameTime);
        Equal(1, ObservationCount(restored, person, "missed our meet appointment"));
        Equal(0, restored.FlushNpcAppointmentExperiences(deadline + 86401, 1));
        Equal(-accepted.NoShowPenalty, restored.ConversationDisposition(person));
    }

    private static void ConversationClaimRetry()
    {
        var store = Book("retry-claim", 1000, out var person);
        store.ObserveAppointment("retry-claim", 1000, true, false, false);
        var inbox = LiveInbox(store);
        inbox.Revision = long.MaxValue;
        True(store.TryClaimAppointmentConversation("retry-claim", person, person, 1010,
            true, true, true, true, out var receipt, out var isNew) && isNew,
            "Confirmed conversation claim was lost.");
        Equal(1010d, receipt.ConversationOpenedAt);
        Equal(1, ObservationCount(store, person, "attended our meet appointment"));
        inbox.Revision = 0;
        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        True(restored.TryClaimAppointmentConversation("retry-claim", person, person, 1011,
            true, true, true, true, out _, out isNew) && !isNew,
            "Replayed conversation claim was not idempotent.");
        Equal(1, restored.FlushNpcAppointmentExperiences(5000, 1));
        Equal(1, restored.ReadPendingNpcExperiences(person, 10).Count);
        Equal(1, ObservationCount(restored, person, "attended our meet appointment"));
    }

    private static void BoundedFlush()
    {
        var store = Book("unsupported-outcome", 1000, out var person);
        store.ObserveAppointment("unsupported-outcome", 1000, false, false, true);
        var second = new AppointmentRecord { Id = "supported-outcome", ResidentId = person,
            Purpose = "meet", StartsAt = 2000, EndsAt = 2060, GraceSeconds = 30,
            Place = new AppointmentPlace { Kind = "building", SettlementId = 100, BuildingKey = 20 } };
        True(store.TryReserveAppointment(second, 500, out var reason), reason);
        store.ObserveAppointment(second.Id, 2000, true, false, false);
        var inbox = LiveInbox(store);
        inbox.Revision = long.MaxValue;
        store.ObserveAppointment(second.Id, 2010, true, true, false);
        inbox.Revision = 0;
        Equal(0, store.FlushNpcAppointmentExperiences(2009, 1));
        Equal(0, store.FlushNpcAppointmentExperiences(2009, 1));
        Equal(0, store.ReadPendingNpcExperiences(person, 10).Count);
        Equal(0, store.FlushNpcAppointmentExperiences(5000, 1));
        var source = LiveWorld(store).Appointments.Find(item => item.Id == second.Id);
        source.Status = "player-absent"; // Invalid source cannot authorize projection.
        Equal(0, store.FlushNpcAppointmentExperiences(5000, 1));
        Equal(0, store.ReadPendingNpcExperiences(person, 10).Count);
        source.Status = "attended";
        Equal(0, store.FlushNpcAppointmentExperiences(5000, 1));
        Equal(1, store.FlushNpcAppointmentExperiences(5000, 1));
        Equal(0, store.FlushNpcAppointmentExperiences(5000, 1));
        Equal(1, store.ReadPendingNpcExperiences(person, 10).Count);
    }

    private static void UnsupportedOutcomes()
    {
        var store = Book("cancelled-outcome", 1000, out var person);
        True(store.CancelAppointment("cancelled-outcome", 600, "cancelled by player"),
            "Cancellation failed.");
        var second = new AppointmentRecord { Id = "npc-absent-outcome", ResidentId = person,
            Purpose = "meet", StartsAt = 2000, EndsAt = 2060, GraceSeconds = 30,
            Place = new AppointmentPlace { Kind = "building", SettlementId = 100, BuildingKey = 20 } };
        True(store.TryReserveAppointment(second, 600, out var reason), reason);
        double deadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment(second.Id));
        store.ObserveAppointment(second.Id, deadline + 1, false, false, false);
        Equal("cancelled", store.FindAppointment("cancelled-outcome").Status);
        Equal("npc-absent", store.FindAppointment(second.Id).Status);
        Equal(0, store.FlushNpcAppointmentExperiences(deadline + 86400, 2));
        Equal(0, store.FlushNpcAppointmentExperiences(deadline + 86401, 2));
        Equal(0, store.ReadPendingNpcExperiences(person, 10).Count);
    }

    private static WorldStateStore Book(string id, double start, out string person, int importance = 1)
    {
        var store = new WorldStateStore(42);
        person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        var booking = new AppointmentRecord { Id = id, ResidentId = person, Purpose = "meet",
            StartsAt = start, EndsAt = start + 60, GraceSeconds = 30, Importance = importance,
            Place = new AppointmentPlace { Kind = "building", SettlementId = 100, BuildingKey = 20 } };
        True(store.TryReserveAppointment(booking, 500, out var reason), reason);
        return store;
    }

    private static WorldState LiveWorld(WorldStateStore store)
    {
        var field = typeof(WorldStateStore).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
        True(field != null, "Store state field unavailable.");
        return (WorldState)field.GetValue(store);
    }

    private static NpcExperienceInbox LiveInbox(WorldStateStore store)
    { return LiveWorld(store).NpcExperienceInbox; }

    private static int ObservationCount(WorldStateStore store, string person, string phrase)
    {
        var memory = LiveWorld(store).ConversationMemories.Find(item => item.PersonId == person);
        return memory == null ? 0 : memory.Entries.Count(item => item.Source == "observed" &&
            item.Observation != null && item.Observation.Contains(phrase));
    }

    private static void True(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
}
