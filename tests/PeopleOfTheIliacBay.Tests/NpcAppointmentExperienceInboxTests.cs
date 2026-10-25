using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcAppointmentExperienceInboxTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Confirmed appointment attendance queues one native event across continuations and restore", () =>
            {
                var store = Create(out var resident);
                Check.True(store.TryReserveAppointment(Booking("attendance-event", resident.Id, 1000), 500, out var reason), reason);
                store.ObserveAppointment("attendance-event", 1000, true, false, false);
                store.ObserveAppointment("attendance-event", 1010, true, true, false);
                var appointment = store.FindAppointment("attendance-event");
                Check.Equal("attended", appointment.Status);
                Check.Equal(0, appointment.NoShowPenalty);
                var queued = store.ReadPendingNpcExperiences(resident.Id, 10);
                Check.Equal(1, queued.Count);
                Check.Equal(NpcExperienceEventKind.AppointmentAttended, queued[0].EventKind.Value);
                Check.Equal(resident.Id, queued[0].ObserverId);
                Check.Equal("player", queued[0].ResponsibleActorId);

                Check.True(NpcAppointmentExperience.TryCreate(appointment, out var canonical));
                var alias = NpcExperienceEvent.FromNativeOutcome(canonical.ObserverId, canonical.SubjectPersonId,
                    "appointment-adapter-alias", canonical.EpisodeId, canonical.OutcomeId,
                    canonical.EventKind.Value, canonical.ObservedGameTime,
                    NpcResponsibleActorAttribution.Known, canonical.ResponsibleActorId);
                Check.True(!store.TryEnqueueNpcExperience(alias));

                store.ObserveAppointment("attendance-event", 1100, true, true, false);
                Check.True(store.TryClaimAppointmentConversation("attendance-event", resident.Id, resident.Id,
                    1011, true, true, true, true, out _, out _));
                Check.True(store.TryClaimAppointmentConversation("attendance-event", resident.Id, resident.Id,
                    1012, true, true, true, true, out _, out _));
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.Equal(1, ObservedAppointmentMemoryCount(store, resident.Id, "attended our meet appointment"));

                var restored = new WorldStateStore(0);
                restored.Restore(store.Snapshot());
                Check.Equal(1, restored.ReadPendingNpcExperiences(resident.Id, 10).Count);
                restored.ObserveAppointment("attendance-event", 1200, true, true, false);
                Check.Equal(1, restored.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.Equal(1, ObservedAppointmentMemoryCount(restored, resident.Id, "attended our meet appointment"));
            }));

            cases.Add(("Confirmed player absence preserves one existing penalty and queues one event", () =>
            {
                var store = Create(out var resident);
                var booking = Booking("absence-event", resident.Id, 2000, 3);
                Check.True(store.TryReserveAppointment(booking, 500, out var reason), reason);
                double deadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment(booking.Id));
                for (double at = deadline - 40; at < deadline; at += 10)
                    store.ObserveAppointment(booking.Id, at, true, false, false);
                store.ObserveAppointment(booking.Id, deadline, true, false, false);
                store.ObserveAppointment(booking.Id, deadline + 1, false, false, false);
                var result = store.FindAppointment(booking.Id);
                Check.Equal("player-absent", result.Status);
                Check.True(result.NoShowPenalty > 0);
                int dispositionAfterOnePenalty = store.ConversationDisposition(resident.Id);
                Check.Equal(-result.NoShowPenalty, dispositionAfterOnePenalty);
                var queued = store.ReadPendingNpcExperiences(resident.Id, 10);
                Check.Equal(1, queued.Count);
                Check.Equal(NpcExperienceEventKind.AppointmentNoShow, queued[0].EventKind.Value);
                Check.Equal("player", queued[0].ResponsibleActorId);
                Check.Equal(1, ObservedAppointmentMemoryCount(store, resident.Id, "missed our meet appointment"));

                store.ObserveAppointment(booking.Id, deadline + 5, true, true, false);
                Check.Equal(dispositionAfterOnePenalty, store.ConversationDisposition(resident.Id));
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.Equal(1, ObservedAppointmentMemoryCount(store, resident.Id, "missed our meet appointment"));
                var restored = new WorldStateStore(0);
                restored.Restore(store.Snapshot());
                Check.Equal(dispositionAfterOnePenalty, restored.ConversationDisposition(resident.Id));
                Check.Equal(1, restored.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.Equal(1, ObservedAppointmentMemoryCount(restored, resident.Id, "missed our meet appointment"));
            }));

            cases.Add(("Opening the actual appointment conversation resolves and queues attendance once", () =>
            {
                var store = Create(out var resident);
                Check.True(store.TryReserveAppointment(Booking("conversation-attendance", resident.Id, 1000), 500, out var reason), reason);
                store.ObserveAppointment("conversation-attendance", 1000, true, false, false);
                Check.True(store.TryClaimAppointmentConversation("conversation-attendance", resident.Id, resident.Id,
                    1010, true, true, true, true, out _, out var isNew));
                Check.True(isNew);
                Check.Equal("attended", store.FindAppointment("conversation-attendance").Status);
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.Equal(NpcExperienceEventKind.AppointmentAttended,
                    store.ReadPendingNpcExperiences(resident.Id, 10)[0].EventKind.Value);

                Check.True(store.TryClaimAppointmentConversation("conversation-attendance", resident.Id, resident.Id,
                    1011, true, true, true, true, out _, out isNew));
                Check.True(!isNew);
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.Equal(1, ObservedAppointmentMemoryCount(store, resident.Id, "attended our meet appointment"));
            }));

            cases.Add(("NPC absence, interrupted, long-gap and venue-only outcomes queue no blamed event", () =>
            {
                var store = Create(out var resident);
                Check.True(store.TryReserveAppointment(Booking("npc-absent-event", resident.Id, 1000), 500, out var reason), reason);
                double absentDeadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment("npc-absent-event"));
                store.ObserveAppointment("npc-absent-event", absentDeadline + 1, false, false, false);
                Check.Equal("npc-absent", store.FindAppointment("npc-absent-event").Status);

                Check.True(store.TryReserveAppointment(Booking("interrupted-event", resident.Id, 4000), 500, out reason), reason);
                store.ObserveAppointment("interrupted-event", 4000, false, false, true);
                Check.Equal("interrupted", store.FindAppointment("interrupted-event").Status);

                Check.True(store.TryReserveAppointment(Booking("long-gap-event", resident.Id, 7000), 500, out reason), reason);
                double longDeadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment("long-gap-event"));
                store.ObserveAppointment("long-gap-event", 7000, true, false, false);
                store.ObserveAppointment("long-gap-event", longDeadline + 1, false, false, false);
                Check.Equal("interrupted", store.FindAppointment("long-gap-event").Status);

                Check.True(store.TryReserveAppointment(Booking("venue-only-event", resident.Id, 10000), 500, out reason), reason);
                double venueDeadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment("venue-only-event"));
                for (double at = venueDeadline - 40; at < venueDeadline; at += 10)
                    store.ObserveAppointment("venue-only-event", at, true, false, true);
                store.ObserveAppointment("venue-only-event", venueDeadline, true, false, true);
                store.ObserveAppointment("venue-only-event", venueDeadline + 1, false, false, false);
                Check.Equal("interrupted", store.FindAppointment("venue-only-event").Status);

                Check.Equal(0, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.Equal(0, store.Snapshot().NpcExperienceInbox.Entries.Count);
                Check.Equal(0, store.ConversationDisposition(resident.Id));
            }));

            cases.Add(("Attendance appraisal requires a matching saved appointment receipt", () =>
            {
                var store = Create(out var resident);
                var fabricated = NpcExperienceEvent.FromNativeOutcome(resident.Id, resident.Id,
                    "appointment-receipt:missing-attendance", "appointment-episode:missing-attendance",
                    "missing-attendance:attendance", NpcExperienceEventKind.AppointmentAttended, 1010,
                    NpcResponsibleActorAttribution.Known, "player");
                Check.True(!store.TryEnqueueNpcExperience(fabricated));
                Check.Equal(0, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, fabricated));

                Check.True(!store.TryCaptureNpcExperienceAppraisal(resident.Id, 1010, out var ticket));
                Check.True(ticket == null);
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);

                var recorded = AttendedStore("removed-source-attendance", out resident).Snapshot();
                recorded.Appointments.Clear();
                var missingSource = new WorldStateStore(0);
                missingSource.Restore(recorded);
                Check.True(!missingSource.TryCaptureNpcExperienceAppraisal(resident.Id, 1010, out ticket));
                Check.True(ticket == null);
                Check.Equal(1, missingSource.ReadPendingNpcExperiences(resident.Id, 10).Count);
            }));

            cases.Add(("Attendance appraisal rejects altered saved receipt and observation time", () =>
            {
                var receiptStore = AttendedStore("altered-receipt", out var resident);
                var alteredReceipt = receiptStore.Snapshot();
                alteredReceipt.NpcExperienceInbox.Entries[0].ReceiptId = "appointment-receipt:forged";
                var restoredReceipt = new WorldStateStore(0);
                restoredReceipt.Restore(alteredReceipt);
                Check.True(!restoredReceipt.TryCaptureNpcExperienceAppraisal(resident.Id, 1010, out var ticket));
                Check.True(ticket == null);
                Check.Equal(1, restoredReceipt.ReadPendingNpcExperiences(resident.Id, 10).Count);

                var timeStore = AttendedStore("altered-time", out resident);
                var alteredTime = timeStore.Snapshot();
                alteredTime.NpcExperienceInbox.Entries[0].ObservedGameTime += 1;
                alteredTime.NpcExperienceInbox.Entries[0].UpdatedAt += 1;
                var restoredTime = new WorldStateStore(0);
                restoredTime.Restore(alteredTime);
                Check.True(!restoredTime.TryCaptureNpcExperienceAppraisal(resident.Id, 1011, out ticket));
                Check.True(ticket == null);
                Check.Equal(1, restoredTime.ReadPendingNpcExperiences(resident.Id, 10).Count);
            }));

            cases.Add(("Attendance appraisal rejects a different resident or unsupported saved outcome", () =>
            {
                var store = new WorldStateStore(42);
                var residents = store.GetOrCreate(100, Enumerable.Range(10, 12), 3).Households
                    .SelectMany(household => household.Residents).ToList();
                Check.True(residents.Count > 1);
                var resident = residents[0];
                var other = residents[1];
                var appointment = Booking("resident-source", resident.Id, 1000);
                Check.True(store.TryReserveAppointment(appointment, 500, out var reason), reason);
                store.ObserveAppointment(appointment.Id, 1000, true, false, false);
                store.ObserveAppointment(appointment.Id, 1010, true, true, false);
                Check.True(NpcAppointmentExperience.TryCreate(store.FindAppointment(appointment.Id), out var canonical));
                var wrongResidentEvent = NpcExperienceEvent.FromNativeOutcome(other.Id, other.Id,
                    canonical.ReceiptId, canonical.EpisodeId, canonical.OutcomeId, canonical.EventKind.Value,
                    canonical.ObservedGameTime, NpcResponsibleActorAttribution.Known, "player");
                Check.True(!store.TryEnqueueNpcExperience(wrongResidentEvent));
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, wrongResidentEvent));
                Check.True(!store.TryCaptureNpcExperienceAppraisal(other.Id, 1010, out var ticket));
                Check.True(ticket == null);

                var statusStore = AttendedStore("unsupported-source-status", out resident);
                var unsupportedStatus = statusStore.Snapshot();
                unsupportedStatus.Appointments[0].Status = "interrupted";
                var restoredUnsupportedStatus = new WorldStateStore(0);
                Check.Throws<ArgumentException>(() => restoredUnsupportedStatus.Restore(unsupportedStatus));
                unsupportedStatus.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.None;
                restoredUnsupportedStatus.Restore(unsupportedStatus);
                Check.True(!restoredUnsupportedStatus.TryCaptureNpcExperienceAppraisal(resident.Id, 1010, out ticket));
                Check.True(ticket == null);
                Check.Equal(1, restoredUnsupportedStatus.ReadPendingNpcExperiences(resident.Id, 10).Count);
            }));

            cases.Add(("Recorded attendance remains appraisable after save restore", () =>
            {
                var store = AttendedStore("restored-attendance", out var resident);
                var restored = new WorldStateStore(0);
                restored.Restore(store.Snapshot());

                Check.True(restored.TryCaptureNpcExperienceAppraisal(resident.Id, 1010, out var ticket));
                Check.True(ticket != null);
                Check.Equal("restored-attendance:attendance", ticket.Request.CanonicalEvidenceId);
                Check.Equal(resident.Id, ticket.Request.ObserverId);
            }));

            cases.Add(("Confirmed no-show remains appraisable only from its saved wait record", () =>
            {
                var store = Create(out var resident);
                var booking = Booking("no-show-capture", resident.Id, 2000, 3);
                Check.True(store.TryReserveAppointment(booking, 500, out var reason), reason);
                double deadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment(booking.Id));
                for (double at = deadline - 40; at < deadline; at += 10)
                    store.ObserveAppointment(booking.Id, at, true, false, false);
                store.ObserveAppointment(booking.Id, deadline, true, false, false);
                store.ObserveAppointment(booking.Id, deadline + 1, false, false, false);

                Check.True(store.TryCaptureNpcExperienceAppraisal(resident.Id, deadline + 1, out var ticket));
                Check.Equal("no-show-capture:attendance", ticket.Request.CanonicalEvidenceId);
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
            }));
        }

        private static WorldStateStore Create(out Resident resident)
        {
            var store = new WorldStateStore(42);
            resident = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
            return store;
        }

        private static AppointmentRecord Booking(string id, string residentId, double start, int importance = 1)
        {
            return new AppointmentRecord
            {
                Id = id,
                ResidentId = residentId,
                Purpose = "meet",
                StartsAt = start,
                EndsAt = start + 60,
                GraceSeconds = 30,
                Importance = importance,
                Place = new AppointmentPlace { Kind = "building", SettlementId = 100, BuildingKey = 20 }
            };
        }

        private static WorldStateStore AttendedStore(string id, out Resident resident)
        {
            var store = Create(out resident);
            Check.True(store.TryReserveAppointment(Booking(id, resident.Id, 1000), 500, out var reason), reason);
            store.ObserveAppointment(id, 1000, true, false, false);
            store.ObserveAppointment(id, 1010, true, true, false);
            Check.Equal("attended", store.FindAppointment(id).Status);
            Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
            return store;
        }

        private static int ObservedAppointmentMemoryCount(WorldStateStore store, string residentId, string phrase)
        {
            var memory = store.Snapshot().ConversationMemories.Find(item => item.PersonId == residentId);
            return memory == null ? 0 : memory.Entries.Count(item => item.Source == "observed" &&
                item.Observation != null && item.Observation.Contains(phrase));
        }
    }
}
