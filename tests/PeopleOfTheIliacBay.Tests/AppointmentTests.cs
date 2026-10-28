using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class AppointmentTests
    {
        private static WorldStateStore Store(out Resident person)
        {
            var store = new WorldStateStore(42);
            person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
            return store;
        }

        private static AppointmentRecord Booking(string id, string residentId, double start = 1000,
            int importance = 1)
        {
            return new AppointmentRecord
            {
                Id = id, ResidentId = residentId, Purpose = "meet", StartsAt = start, EndsAt = start + 60,
                GraceSeconds = 30, Importance = importance, Status = "accepted",
                Place = new AppointmentPlace { Kind = "building", SettlementId = 100, BuildingKey = 20 }
            };
        }

        private static void WaitThroughWindow(WorldStateStore store, string id, double start)
        {
            double deadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment(id));
            for (double at = deadline - 40; at < deadline; at += 10)
                store.ObserveAppointment(id, at, true, false, false);
            store.ObserveAppointment(id, deadline, true, false, false);
        }

        private static void PatienceIsStableAcrossCopiesAndUsesCapturedDisposition()
        {
            uint seed = 42;
            int low = AppointmentTiming.CreatePatienceSeconds(seed, "npc", "meeting-a", -100);
            int repeat = AppointmentTiming.CreatePatienceSeconds(seed, "npc", "meeting-a", -100);
            int neutral = AppointmentTiming.CreatePatienceSeconds(seed, "npc", "meeting-a", 0);
            int high = AppointmentTiming.CreatePatienceSeconds(seed, "npc", "meeting-a", 100);
            Check.Equal(600, low);
            Check.Equal(low, repeat);
            Check.True(neutral >= 600 && neutral <= 4800);
            Check.True(high >= 600 && high <= 9000);
            Check.True(low <= neutral && neutral <= high);
            Check.True(AppointmentTiming.CreatePatienceSeconds(seed, "npc", "meeting-a", 0) !=
                AppointmentTiming.CreatePatienceSeconds(seed, "npc", "meeting-b", 0));

            var store = Store(out var person);
            store.SetConversationBaseline(person.Id, -40, 500);
            var proposal = Booking("captured", person.Id);
            Check.True(store.TryReserveAppointment(proposal, 500, out var reason), reason);
            int captured = AppointmentTiming.CreatePatienceSeconds(store.WorldSeed, person.Id, proposal.Id, -40);
            Check.Equal(captured, store.FindAppointment(proposal.Id).PatienceSeconds);
            Check.Equal(captured, store.AppointmentsFor(person.Id)[0].PatienceSeconds);

            store.SetConversationBaseline(person.Id, 100, 600);
            Check.Equal(captured, store.FindAppointment(proposal.Id).PatienceSeconds);
            Check.True(store.TryReserveAppointment(proposal, 600, out reason), reason);
            Check.Equal("existing", reason);
            Check.Equal(captured, store.FindAppointment(proposal.Id).PatienceSeconds);

            var snapshot = store.Snapshot();
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { IncludeFields = true });
            var restored = new WorldStateStore(99);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(json,
                new JsonSerializerOptions { IncludeFields = true }));
            Check.Equal(captured, restored.FindAppointment(proposal.Id).PatienceSeconds);
            Check.Equal(captured, restored.AppointmentsFor(person.Id)[0].PatienceSeconds);
        }

        private static void PatienceExtendsReservation()
        {
            var store = Store(out var person);
            var longWait = Booking("long-wait", person.Id, 1000);
            longWait.PatienceSeconds = 3600;
            Check.True(store.TryReserveAppointment(longWait, 500, out var reason), reason);
            var stored = store.FindAppointment(longWait.Id);
            Check.Near(4600, AppointmentTiming.ArrivalDeadline(stored));
            Check.Near(4600, AppointmentTiming.ReservedUntil(stored));
            var overlap = Booking("overlap-long-wait", person.Id, 4000);
            Check.True(!store.TryReserveAppointment(overlap, 500, out reason));
            Check.Equal("overlap", reason);

        }

        private static void PatienceValidationAndNoShowResolutionUseCapturedDeadline()
        {
            var store = Store(out var person);
            var invalid = Booking("invalid-patience", person.Id);
            invalid.PatienceSeconds = 599;
            Check.True(!store.TryReserveAppointment(invalid, 500, out _));
            invalid.PatienceSeconds = 9001;
            Check.True(!store.TryReserveAppointment(invalid, 500, out _));

            var booking = Booking("patient", person.Id, 1000, 3);
            booking.EndsAt = 4000;
            booking.PatienceSeconds = 600;
            Check.True(store.TryReserveAppointment(booking, 500, out var reason), reason);
            var deadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment(booking.Id));
            Check.Near(1600, deadline);
            Check.Near(4000, AppointmentTiming.ReservedUntil(store.FindAppointment(booking.Id)));
            Check.Equal(1, store.AppointmentRevision);
            for (double at = deadline - 40; at <= deadline; at += 10)
                store.ObserveAppointment(booking.Id, at, true, false, false);
            Check.Equal(1, store.AppointmentRevision);
            store.ObserveAppointment(booking.Id, deadline + 1, false, false, false);
            Check.Equal("player-absent", store.FindAppointment(booking.Id).Status);
            Check.True(store.FindAppointment(booking.Id).NoShowPenalty > 0);
            Check.Near(deadline + 1, AppointmentTiming.PlannedLeaveAt(store.FindAppointment(booking.Id)));
            Check.Equal(2, store.AppointmentRevision);
            store.ObserveAppointment(booking.Id, deadline + 2, true, true, false);
            Check.Equal(2, store.AppointmentRevision);

            var unseen = Booking("unobserved-npc", person.Id, 3000);
            unseen.PatienceSeconds = 600;
            Check.True(store.TryReserveAppointment(unseen, 500, out reason), reason);
            store.ObserveAppointment(unseen.Id, 3601, false, false, false);
            Check.Equal("npc-absent", store.FindAppointment(unseen.Id).Status);
            Check.Equal(0, store.FindAppointment(unseen.Id).NoShowPenalty);
            Check.Equal(4, store.AppointmentRevision);
        }

        private static void PublicAppointmentTimingValidatesInputsAndMapsOutcomeLeaves()
        {
            Check.Throws<ArgumentException>(() => AppointmentTiming.ArrivalDeadline(-1, 10, 0, 600));
            Check.Throws<ArgumentException>(() => AppointmentTiming.ArrivalDeadline(1, double.NaN, 0, 600));
            Check.Throws<ArgumentException>(() => AppointmentTiming.ArrivalDeadline(1, 10, -1, 0));
            Check.Throws<ArgumentException>(() => AppointmentTiming.ArrivalDeadline(1, 10, 0, 599));
            Check.Throws<ArgumentException>(() => AppointmentTiming.ArrivalDeadline(1, 10, 0, 9001));
            Check.Throws<ArgumentException>(() => AppointmentTiming.ArrivalDeadline(1, 10, 86401, 0));
            Check.Throws<ArgumentException>(() => AppointmentTiming.CreatePatienceSeconds(1, null, "id", 0));

            var active = Booking("timing-outcomes", "resident", 1000);
            active.EndsAt = 4000;
            active.PatienceSeconds = 600;
            Check.Near(4000, AppointmentTiming.PlannedLeaveAt(active));

            active.Status = "attended";
            active.OutcomeRecorded = true;
            active.ResolvedAt = 1300;
            Check.Near(4000, AppointmentTiming.PlannedLeaveAt(active));

            active.Status = "player-absent";
            active.NpcArrivedAt = 1000;
            active.ResolvedAt = 1601;
            Check.Near(1601, AppointmentTiming.PlannedLeaveAt(active));

            active.Status = "interrupted";
            active.ResolvedAt = 900;
            Check.Near(1000, AppointmentTiming.PlannedLeaveAt(active));
            active.ResolvedAt = double.NaN;
            Check.Throws<ArgumentException>(() => AppointmentTiming.PlannedLeaveAt(active));

            active.Status = "npc-absent";
            active.NpcArrivedAt = -1;
            Check.Near(4000, AppointmentTiming.PlannedLeaveAt(active));

            active.PatienceSeconds = 0;
            active.Status = "attended";
            Check.Near(4030, AppointmentTiming.PlannedLeaveAt(active));
        }

        private static void ReservationRejectsOverlapAndReturnsCopies()
        {
            var store = Store(out var person);
            var original = Booking("a", person.Id);
            Check.True(store.TryReserveAppointment(original, 500, out var reason), reason);
            original.Place.BuildingKey = 99;
            var listed = store.AppointmentsFor(person.Id);
            listed[0].Purpose = "mutated";
            Check.Equal(20, store.FindAppointment("a").Place.BuildingKey);
            Check.Equal("meet", store.FindAppointment("a").Purpose);
            Check.True(!store.TryReserveAppointment(Booking("b", person.Id, 1080), 500, out reason));
            Check.Equal("overlap", reason);
            Check.True(store.TryReserveAppointment(Booking("a", person.Id), 500, out reason));
            Check.Equal("existing", reason);
            var conflictingId = Booking("a", person.Id, 2000);
            Check.True(!store.TryReserveAppointment(conflictingId, 500, out reason));
            Check.Equal("duplicate", reason);
        }

        private static void ReservationRejectsTravelAndInvalidRecords()
        {
            var store = Store(out var person);
            store.ScheduleResidentTrip(ResidentTrip.Create(person.Id, 100, 200, 900, 600, 100,
                new[] { new RoutePoint(10, 10), new RoutePoint(11, 10) }));
            Check.True(!store.TryReserveAppointment(Booking("trip", person.Id, 1000), 500, out var reason));
            Check.Equal("travel", reason);
            var invalid = Booking("bad", person.Id, 500);
            Check.True(!store.TryReserveAppointment(invalid, 500, out reason));
            invalid = Booking("bad-place", person.Id);
            invalid.Place.Kind = "unknown";
            Check.True(!store.TryReserveAppointment(invalid, 500, out reason));
        }

        private static void AttendanceAndNoShowsRecordOnceAndKeepNpcFailureNeutral()
        {
            var store = Store(out var person);
            Check.True(store.TryReserveAppointment(Booking("attended", person.Id), 500, out _));
            store.ObserveAppointment("attended", 1010, true, false, false);
            store.ObserveAppointment("attended", 1020, true, true, false);
            Check.Equal("attended", store.FindAppointment("attended").Status);
            Check.Equal(2, store.AppointmentRevision);
            store.ObserveAppointment("attended", 1200, false, false, false);
            Check.Equal(0, store.ConversationDisposition(person.Id));

            var npcMiss = Booking("npc-miss", person.Id, 12000);
            npcMiss.PatienceSeconds = 600;
            Check.True(store.TryReserveAppointment(npcMiss, 500, out _));
            store.ObserveAppointment("npc-miss", 12000, true, false, false);
            store.ObserveAppointment("npc-miss", 12005, true, false, false);
            store.ObserveAppointment("npc-miss", 12010, false, false, false);
            store.ObserveAppointment("npc-miss", 12601, false, true, false);
            Check.Equal("interrupted", store.FindAppointment("npc-miss").Status);
            Check.Equal(0, store.FindAppointment("npc-miss").NoShowPenalty);
            Check.Equal(0, store.ConversationDisposition(person.Id));

            var timeJump = Booking("time-jump", person.Id, 24000);
            timeJump.PatienceSeconds = 600;
            Check.True(store.TryReserveAppointment(timeJump, 500, out _));
            store.ObserveAppointment("time-jump", 24010, true, false, false);
            store.ObserveAppointment("time-jump", 24601, false, false, false);
            Check.Equal("interrupted", store.FindAppointment("time-jump").Status);
            Check.Equal(0, store.FindAppointment("time-jump").NoShowPenalty);

            var playerMiss = Booking("player-miss", person.Id, 36000, 3);
            playerMiss.PatienceSeconds = 600;
            Check.True(store.TryReserveAppointment(playerMiss, 500, out _));
            WaitThroughWindow(store, "player-miss", 36000);
            store.ObserveAppointment("player-miss", 36601, false, false, false);
            var resolved = store.FindAppointment("player-miss");
            Check.Equal("player-absent", resolved.Status);
            Check.True(resolved.NoShowPenalty > 0 && resolved.NoShowPenalty <= 25);
            int offset = store.ConversationDisposition(person.Id);
            store.ObserveAppointment("player-miss", 4000, true, true, false);
            Check.Equal(offset, store.ConversationDisposition(person.Id));
            Check.True(store.RecallConversation(person.Id, "appointment", 37000).Contains("missed our"));
        }

        private static void RepeatedImportantNoShowsReduceRebookingTrustAndBranchSafely()
        {
            var store = Store(out var person);
            Check.True(store.AppointmentTrustAllows(person.Id));
            for (int i = 0; i < 3; i++)
            {
                string id = "important-" + i;
                double start = 1000 + i * 12000;
                var booking = Booking(id, person.Id, start, 3);
                booking.PatienceSeconds = 600;
                Check.True(store.TryReserveAppointment(booking, 500, out _));
                WaitThroughWindow(store, id, start);
                store.ObserveAppointment(id, start + 601, false, false, false);
            }
            Check.True(!store.AppointmentTrustAllows(person.Id));
            int before = store.ConversationDisposition(person.Id);
            Check.True(!store.TryReserveAppointment(Booking("future", person.Id, 40000), 500, out var reason));
            Check.Equal("trust", reason);
            var branch = new WorldStateStore(42);
            branch.Restore(store.Snapshot());
            branch.ObserveAppointment("important-2", 35000, true, true, false);
            Check.Equal(before, branch.ConversationDisposition(person.Id));
            Check.Equal("player-absent", store.FindAppointment("important-2").Status);
        }

        private static void NpcAbsenceIsNeutralRememberedOnceAndSurvivesRestore()
        {
            var store=Store(out var person);
            int disposition=store.ConversationDisposition(person.Id);
            Check.True(store.TryReserveAppointment(Booking("npc-absent",person.Id,1000,3),500,out _));
            store.ObserveAppointment("npc-absent",1000,false,false,false);
            store.ObserveAppointment("npc-absent",AppointmentTiming.ArrivalDeadline(store.FindAppointment("npc-absent"))+1,false,false,false);
            var outcome=store.FindAppointment("npc-absent");
            Check.Equal("npc-absent",outcome.Status);
            Check.True(outcome.OutcomeRecorded);
            Check.Equal(0,outcome.NoShowPenalty);
            Check.Equal(disposition,store.ConversationDisposition(person.Id));
            var journal=store.Snapshot().ConversationMemories.Find(value=>value.PersonId==person.Id);
            Check.True(journal!=null);
            Check.Equal(1,journal.Entries.Count(value=>value.Source=="observed" && value.Observation!=null &&
                value.Observation.Contains("did not attend the scheduled meet appointment")));

            store.ObserveAppointment("npc-absent",2000,true,false,false);
            Check.Equal(0,store.FindAppointment("npc-absent").NoShowPenalty);
            Check.Equal(disposition,store.ConversationDisposition(person.Id));
            Check.Equal(1,store.Snapshot().ConversationMemories.Find(value=>value.PersonId==person.Id).Entries.Count(value=>
                value.Source=="observed" && value.Observation!=null && value.Observation.Contains("did not attend the scheduled meet appointment")));

            var restored=new WorldStateStore(99);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(store.Snapshot(),
                new JsonSerializerOptions {IncludeFields=true}),new JsonSerializerOptions {IncludeFields=true}));
            Check.Equal("npc-absent",restored.FindAppointment("npc-absent").Status);
            Check.Equal(0,restored.FindAppointment("npc-absent").NoShowPenalty);
            Check.Equal(disposition,restored.ConversationDisposition(person.Id));
            Check.Equal(1,restored.Snapshot().ConversationMemories.Find(value=>value.PersonId==person.Id).Entries.Count(value=>
                value.Source=="observed" && value.Observation!=null && value.Observation.Contains("did not attend the scheduled meet appointment")));
        }

        private static void CancellationIsNeutralAndPersistedExactlyOnce()
        {
            var store = Store(out var person);
            Check.True(store.TryReserveAppointment(Booking("cancel", person.Id), 500, out _));
            Check.Equal(1, store.AppointmentRevision);
            Check.True(store.CancelAppointment("cancel", 600, "player changed plans"));
            Check.Equal(2, store.AppointmentRevision);
            Check.True(!store.CancelAppointment("cancel", 601, "again"));
            Check.Equal(2, store.AppointmentRevision);
            Check.Equal("cancelled", store.FindAppointment("cancel").Status);
            Check.Equal(0, store.ConversationDisposition(person.Id));
            Check.True(store.RecallConversation(person.Id, "cancelled", 700).Contains("cancelled"));
            var snapshot = store.Snapshot();
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { IncludeFields = true });
            var restored = new WorldStateStore(99);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(json, new JsonSerializerOptions { IncludeFields = true }));
            Check.Equal(1, restored.AppointmentRevision);
            Check.Equal("cancelled", restored.FindAppointment("cancel").Status);
            Check.True(restored.TryReserveAppointment(Booking("other", person.Id, 2000), 500, out _));
        }

        private static void RestoreRejectsNoShowWithoutWaitEvidenceTransactionally()
        {
            var store = Store(out var person);
            Check.True(store.TryReserveAppointment(Booking("evidence", person.Id, 1000, 3), 500, out _));
            WaitThroughWindow(store, "evidence", 1000);
            store.ObserveAppointment("evidence",AppointmentTiming.ArrivalDeadline(store.FindAppointment("evidence"))+1,false,false,false);
            var corrupt = store.Snapshot();
            corrupt.Appointments[0].NpcWaitedSeconds = 0;
            var target = Store(out _);
            Check.Throws<ArgumentException>(() => target.Restore(corrupt));
            Check.Equal(0, target.AppointmentsFor(person.Id).Count);
        }

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Appointment ledger validates reservations, overlap and copy isolation", ReservationRejectsOverlapAndReturnsCopies));
            cases.Add(("Appointment patience is deterministic, disposition-bound and persisted once",PatienceIsStableAcrossCopiesAndUsesCapturedDisposition));
            cases.Add(("Appointment patience extends reservations",PatienceExtendsReservation));
            cases.Add(("Appointment patience validation and observed no-show timing are exact",PatienceValidationAndNoShowResolutionUseCapturedDeadline));
            cases.Add(("Appointment timing validates payloads and maps legacy and resolved leaves",PublicAppointmentTimingValidatesInputsAndMapsOutcomeLeaves));
            cases.Add(("Appointment ledger respects travel conflicts and rejects invalid data", ReservationRejectsTravelAndInvalidRecords));
            cases.Add(("Appointment outcomes distinguish attendance and no-shows exactly once", AttendanceAndNoShowsRecordOnceAndKeepNpcFailureNeutral));
            cases.Add(("Repeated important player no-shows reduce trust across save branches", RepeatedImportantNoShowsReduceRebookingTrustAndBranchSafely));
            cases.Add(("NPC no-show is neutral, remembered once and survives restore",NpcAbsenceIsNeutralRememberedOnceAndSurvivesRestore));
            cases.Add(("Appointment cancellation records neutral durable memory once", CancellationIsNeutralAndPersistedExactlyOnce));
            cases.Add(("Appointment restore rejects player no-show without durable wait evidence", RestoreRejectsNoShowWithoutWaitEvidenceTransactionally));
        }
    }
}
