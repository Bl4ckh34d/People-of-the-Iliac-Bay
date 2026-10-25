using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Converts a confirmed appointment receipt into observer-bound native evidence.</summary>
    public static class NpcAppointmentExperience
    {
        public static bool TryCreate(AppointmentRecord appointment, out NpcExperienceEvent evidence)
        {
            evidence = null;
            if (appointment == null || !appointment.OutcomeRecorded ||
                string.IsNullOrWhiteSpace(appointment.Id) || string.IsNullOrWhiteSpace(appointment.ResidentId) ||
                string.Equals(appointment.ResidentId, "player", StringComparison.Ordinal) ||
                !Finite(appointment.ResolvedAt) || appointment.ResolvedAt < 0)
                return false;

            try { AppointmentTiming.ArrivalDeadline(appointment); }
            catch (ArgumentException) { return false; }

            NpcExperienceEventKind eventKind;
            if (string.Equals(appointment.Status, "attended", StringComparison.Ordinal))
            {
                if (appointment.NoShowPenalty != 0 || !Finite(appointment.NpcArrivedAt) ||
                    !Finite(appointment.PlayerArrivedAt) || appointment.NpcArrivedAt < appointment.StartsAt ||
                    appointment.PlayerArrivedAt < appointment.StartsAt || appointment.NpcArrivedAt > appointment.ResolvedAt ||
                    appointment.PlayerArrivedAt > appointment.ResolvedAt)
                    return false;
                eventKind = NpcExperienceEventKind.AppointmentAttended;
            }
            else if (string.Equals(appointment.Status, "player-absent", StringComparison.Ordinal))
            {
                if (!ValidConfirmedPlayerAbsence(appointment))
                    return false;
                eventKind = NpcExperienceEventKind.AppointmentNoShow;
            }
            else
                return false;

            try
            {
                string receiptId = "appointment-receipt:" + appointment.Id;
                string episodeId = "appointment-episode:" + appointment.Id;
                string outcomeId = appointment.Id + ":attendance";
                evidence = NpcExperienceEvent.FromNativeOutcome(appointment.ResidentId, appointment.ResidentId,
                    receiptId, episodeId, outcomeId, eventKind, appointment.ResolvedAt,
                    NpcResponsibleActorAttribution.Known, "player");
                return true;
            }
            catch (ArgumentException)
            {
                evidence = null;
                return false;
            }
        }

        private static bool ValidConfirmedPlayerAbsence(AppointmentRecord appointment)
        {
            if (appointment.NoShowPenalty <= 0 || !Finite(appointment.StartsAt) || !Finite(appointment.EndsAt) ||
                !Finite(appointment.NpcArrivedAt) || !Finite(appointment.PlayerArrivedAt) ||
                !Finite(appointment.PlayerAtVenueAt) || !Finite(appointment.NpcLastSeenAt) ||
                !Finite(appointment.NpcWaitedSeconds) || appointment.NpcArrivedAt < appointment.StartsAt ||
                appointment.PlayerArrivedAt >= 0 || appointment.PlayerAtVenueAt >= 0 ||
                appointment.NpcLastSeenAt < 0 || appointment.NpcWaitedSeconds < 0)
                return false;

            double deadline;
            try { deadline = AppointmentTiming.ResolutionDeadline(appointment); }
            catch (ArgumentException) { return false; }
            if (!Finite(deadline)) return false;

            double requiredWait = Math.Min(30, appointment.EndsAt - appointment.StartsAt);
            return appointment.NpcWaitedSeconds >= requiredWait && appointment.ResolvedAt > deadline &&
                appointment.NpcLastSeenAt >= deadline - 15 && appointment.NpcLastSeenAt <= deadline &&
                appointment.ResolvedAt - appointment.NpcLastSeenAt <= 15;
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
