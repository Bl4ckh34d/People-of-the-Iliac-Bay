using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcAppointmentExperienceTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Recorded appointment attendance creates stable observer-bound native event", RecordedAttendanceCreatesStableEvent));
            cases.Add(("Confirmed player no-show uses continuous wait receipt without mutating legacy outcome", ConfirmedNoShowCreatesEventWithoutMutation));
            cases.Add(("Active, interrupted, NPC-absent and long-gap outcomes create no appointment event", UnsupportedOutcomesCreateNoEvent));
        }

        private static void RecordedAttendanceCreatesStableEvent()
        {
            var store = NewStore(out var person);
            var booking = Book(store, person.Id, "appointment-attended");
            store.ObserveAppointment(booking.Id, 1010, true, false, false);
            store.ObserveAppointment(booking.Id, 1020, true, true, false);
            var outcome = store.FindAppointment(booking.Id);
            Check.Equal("attended", outcome.Status);
            Check.True(outcome.OutcomeRecorded);

            Check.True(NpcAppointmentExperience.TryCreate(outcome, out var evidence));
            Check.Equal(NpcExperienceEventOrigin.NativeOutcome, evidence.Origin);
            Check.Equal(NpcExperienceEventKind.AppointmentAttended, evidence.EventKind.Value);
            Check.Equal(person.Id, evidence.ObserverId);
            Check.Equal(person.Id, evidence.SubjectPersonId);
            Check.Equal(NpcResponsibleActorAttribution.Known, evidence.ResponsibleActorAttribution);
            Check.Equal("player", evidence.ResponsibleActorId);
            Check.Equal(outcome.ResolvedAt, evidence.ObservedGameTime);
            Check.Equal(booking.Id + ":attendance", evidence.OutcomeId);
            Check.True(evidence.ReceiptId != evidence.OutcomeId && evidence.EpisodeId != evidence.OutcomeId);

            var invalidTime = store.FindAppointment(booking.Id);
            invalidTime.StartsAt = double.NaN;
            Check.True(!NpcAppointmentExperience.TryCreate(invalidTime, out var invalidEvidence));
            Check.True(invalidEvidence == null);

            Check.True(NpcAppointmentExperience.TryCreate(store.FindAppointment(booking.Id), out var replay));
            Check.Equal(evidence.OutcomeId, replay.OutcomeId);
            Check.Equal(evidence.DeduplicationKey, replay.DeduplicationKey);
        }

        private static void ConfirmedNoShowCreatesEventWithoutMutation()
        {
            var store = NewStore(out var person);
            var booking = Book(store, person.Id, "appointment-player-absent", 3);
            double deadline = AppointmentTiming.ResolutionDeadline(store.FindAppointment(booking.Id));
            for (double at = deadline - 30; at <= deadline; at += 10)
                store.ObserveAppointment(booking.Id, at, true, false, false);
            store.ObserveAppointment(booking.Id, deadline + 1, false, false, false);

            var outcome = store.FindAppointment(booking.Id);
            Check.Equal("player-absent", outcome.Status);
            Check.True(outcome.OutcomeRecorded);
            Check.True(outcome.NoShowPenalty > 0);
            Check.True(outcome.NpcWaitedSeconds >= Math.Min(30, outcome.EndsAt - outcome.StartsAt));
            Check.True(outcome.NpcLastSeenAt >= 0);
            int dispositionBefore = store.ConversationDisposition(person.Id);
            double resolvedAtBefore = outcome.ResolvedAt;
            double waitedBefore = outcome.NpcWaitedSeconds;
            double lastSeenBefore = outcome.NpcLastSeenAt;
            int penaltyBefore = outcome.NoShowPenalty;

            Check.True(NpcAppointmentExperience.TryCreate(outcome, out var evidence));
            Check.Equal(NpcExperienceEventKind.AppointmentNoShow, evidence.EventKind.Value);
            Check.Equal(person.Id, evidence.ObserverId);
            Check.Equal(person.Id, evidence.SubjectPersonId);
            Check.Equal("player", evidence.ResponsibleActorId);
            Check.Equal(NpcResponsibleActorAttribution.Known, evidence.ResponsibleActorAttribution);
            Check.Equal(outcome.Id + ":attendance", evidence.OutcomeId);
            Check.Equal(dispositionBefore, store.ConversationDisposition(person.Id));

            var after = store.FindAppointment(booking.Id);
            Check.Equal(resolvedAtBefore, after.ResolvedAt);
            Check.Equal(waitedBefore, after.NpcWaitedSeconds);
            Check.Equal(lastSeenBefore, after.NpcLastSeenAt);
            Check.Equal(penaltyBefore, after.NoShowPenalty);

            var invalidWait = store.FindAppointment(booking.Id);
            invalidWait.NpcWaitedSeconds = 0;
            Check.True(!NpcAppointmentExperience.TryCreate(invalidWait, out var invalidEvidence));
            Check.True(invalidEvidence == null);
        }

        private static void UnsupportedOutcomesCreateNoEvent()
        {
            var store = NewStore(out var person);
            var active = Book(store, person.Id, "appointment-active");
            Check.True(!NpcAppointmentExperience.TryCreate(store.FindAppointment(active.Id), out var none));
            Check.True(none == null);

            var interrupted = Book(store, person.Id, "appointment-interrupted", start: 10000);
            store.ObserveAppointment(interrupted.Id, interrupted.StartsAt, true, false, true);
            Check.Equal("interrupted", store.FindAppointment(interrupted.Id).Status);
            Check.True(!NpcAppointmentExperience.TryCreate(store.FindAppointment(interrupted.Id), out none));
            Check.True(none == null);

            var npcAbsent = Book(store, person.Id, "appointment-npc-absent", start: 20000);
            double npcAbsentDeadline = AppointmentTiming.ResolutionDeadline(store.FindAppointment(npcAbsent.Id));
            store.ObserveAppointment(npcAbsent.Id, npcAbsentDeadline + 1, false, false, false);
            Check.Equal("npc-absent", store.FindAppointment(npcAbsent.Id).Status);
            Check.True(!NpcAppointmentExperience.TryCreate(store.FindAppointment(npcAbsent.Id), out none));
            Check.True(none == null);

            var gap = Book(store, person.Id, "appointment-long-gap", start: 30000);
            double gapDeadline = AppointmentTiming.ResolutionDeadline(store.FindAppointment(gap.Id));
            store.ObserveAppointment(gap.Id, gap.StartsAt, true, false, false);
            store.ObserveAppointment(gap.Id, gapDeadline + 1, false, false, false);
            Check.Equal("interrupted", store.FindAppointment(gap.Id).Status);
            Check.True(store.FindAppointment(gap.Id).NpcWaitedSeconds < Math.Min(30, gap.EndsAt - gap.StartsAt));
            Check.True(!NpcAppointmentExperience.TryCreate(store.FindAppointment(gap.Id), out none));
            Check.True(none == null);
        }

        private static WorldStateStore NewStore(out Resident person)
        {
            var store = new WorldStateStore(42);
            person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
            return store;
        }

        private static AppointmentRecord Book(WorldStateStore store, string residentId, string id,
            int importance = 1, double start = 1000)
        {
            var booking = new AppointmentRecord
            {
                Id = id,
                ResidentId = residentId,
                Purpose = "meet",
                StartsAt = start,
                EndsAt = start + 60,
                GraceSeconds = 30,
                PatienceSeconds = AppointmentTiming.MinimumPatienceSeconds,
                Importance = importance,
                Status = "accepted",
                Place = new AppointmentPlace { Kind = "building", SettlementId = 100, BuildingKey = 20 }
            };
            Check.True(store.TryReserveAppointment(booking, 500, out var reason), reason);
            return store.FindAppointment(id);
        }
    }
}
