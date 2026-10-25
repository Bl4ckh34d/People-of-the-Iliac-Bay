using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcAppointmentExperienceProjection
    {
        None = 0,
        Pending = 1,
        Projected = 2,
        LegacyUnavailable = 3
    }

    [Serializable]
    public sealed class AppointmentPlace
    {
        public string Kind;
        public int SettlementId;
        public int BuildingKey = -1;
        public double X, Y, Z;
        public string Detail;

        // DFU's native FullSerializer writes doubles with 15 significant digits.
        // Canonicalize world points at their persisted-copy boundary so their
        // exact coordinates and route-proof endpoints survive native save/load.
        public static double CanonicalWorldPointCoordinate(double value)
        {
            if(double.IsNaN(value)||double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));
            return double.Parse(value.ToString("G15",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);
        }

        internal static AppointmentPlace Copy(AppointmentPlace value)
        {
            return new AppointmentPlace { Kind = value.Kind, SettlementId = value.SettlementId,
                BuildingKey = value.BuildingKey,
                X = value.Kind=="world-point"?CanonicalWorldPointCoordinate(value.X):value.X,
                Y = value.Kind=="world-point"?CanonicalWorldPointCoordinate(value.Y):value.Y,
                Z = value.Kind=="world-point"?CanonicalWorldPointCoordinate(value.Z):value.Z,
                Detail = value.Detail };
        }
    }

    [Serializable]
    // Ledger currently represents one managed resident and the player. Runtime can
    // adapt group invitations through separate records without changing this owner.
    public sealed class AppointmentRecord
    {
        public string Id, ResidentId, Purpose;
        public AppointmentPlace Place;
        public double StartsAt, EndsAt, GraceSeconds;
        // Zero is the serialized legacy representation; new reservations capture a value.
        public int PatienceSeconds;
        public int Importance;
        public string Status = "accepted";
        public double NpcArrivedAt = -1, PlayerArrivedAt = -1, ResolvedAt = -1;
        public double NpcLastSeenAt = -1, NpcWaitedSeconds;
        // First witnessed sighting and its short, safe approach window. These are
        // separate from PlayerArrivedAt: sight alone never counts as attendance.
        public double PlayerNoticedAt = -1, ApproachUntil = -1;
        // Durable idempotency marker written only after the real meeting window opens.
        public double ConversationOpenedAt = -1;
        // Venue presence is evidence against a no-show, but does not mean meeting arrival.
        public double PlayerAtVenueAt = -1;
        public double FollowUpNoticedAt = -1, FollowUpApproachUntil = -1, FollowUpConversationOpenedAt = -1;
        public bool OutcomeRecorded;
        public int NoShowPenalty;
        // The resolved appointment remains the source after inbox detail is retired.
        public NpcAppointmentExperienceProjection ExperienceProjection;

        internal static AppointmentRecord Copy(AppointmentRecord value)
        {
            return new AppointmentRecord { Id = value.Id, ResidentId = value.ResidentId, Purpose = value.Purpose,
                Place = AppointmentPlace.Copy(value.Place), StartsAt = value.StartsAt, EndsAt = value.EndsAt,
                GraceSeconds = value.GraceSeconds, PatienceSeconds = value.PatienceSeconds,
                Importance = value.Importance, Status = value.Status,
                NpcArrivedAt = value.NpcArrivedAt, PlayerArrivedAt = value.PlayerArrivedAt,
                ResolvedAt = value.ResolvedAt, NpcLastSeenAt = value.NpcLastSeenAt,
                NpcWaitedSeconds = value.NpcWaitedSeconds, PlayerNoticedAt = value.PlayerNoticedAt,
                ApproachUntil = value.ApproachUntil, ConversationOpenedAt = value.ConversationOpenedAt,
                PlayerAtVenueAt = value.PlayerAtVenueAt, FollowUpNoticedAt=value.FollowUpNoticedAt,
                FollowUpApproachUntil=value.FollowUpApproachUntil,
                FollowUpConversationOpenedAt=value.FollowUpConversationOpenedAt,
                OutcomeRecorded = value.OutcomeRecorded,
                NoShowPenalty = value.NoShowPenalty, ExperienceProjection = value.ExperienceProjection };
        }
    }

    public static class AppointmentTiming
    {
        public const int MinimumPatienceSeconds = 600;
        public const int MaximumPatienceSeconds = 9000;

        // Captured once at booking; the same identity roll expands monotonically as disposition rises.
        public static int CreatePatienceSeconds(uint worldSeed, string residentId, string appointmentId,
            int actualDisposition)
        {
            if (string.IsNullOrWhiteSpace(residentId)) throw new ArgumentException("Resident ID is required.", nameof(residentId));
            if (string.IsNullOrWhiteSpace(appointmentId)) throw new ArgumentException("Appointment ID is required.", nameof(appointmentId));
            int disposition = Math.Max(-100, Math.Min(100, actualDisposition));
            int max = MinimumPatienceSeconds + (int)((long)(disposition + 100) *
                (MaximumPatienceSeconds - MinimumPatienceSeconds) / 200);

            // FNV-1a identity mixing and xorshift are private, stable, and do not touch game RNG.
            uint seed = 2166136261u;
            seed = Mix(seed, worldSeed.ToString("x8", CultureInfo.InvariantCulture));
            seed = Mix(seed, residentId);
            seed = Mix(seed, appointmentId);
            if (seed == 0) seed = 0x9e3779b9u;
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            double fraction = seed / 4294967296.0;
            int span = max - MinimumPatienceSeconds;
            return MinimumPatienceSeconds + (int)Math.Floor(fraction * (span + 1));
        }

        // New records wait from StartsAt; zero remains the legacy EndsAt+GraceSeconds rule.
        public static double ArrivalDeadline(AppointmentRecord appointment)
        {
            if (appointment == null) throw new ArgumentNullException(nameof(appointment));
            return ArrivalDeadline(appointment.StartsAt, appointment.EndsAt,
                appointment.GraceSeconds, appointment.PatienceSeconds);
        }

        public static double ArrivalDeadline(double startsAt, double endsAt, double graceSeconds, int patienceSeconds)
        {
            if (!Finite(startsAt) || startsAt < 0 || !Finite(endsAt) || endsAt <= startsAt ||
                !Finite(graceSeconds) || graceSeconds < 0 || graceSeconds > 86400 ||
                !(patienceSeconds == 0 || patienceSeconds >= MinimumPatienceSeconds &&
                    patienceSeconds <= MaximumPatienceSeconds))
                throw new ArgumentException("Invalid appointment timing.");
            double deadline = patienceSeconds > 0 ? startsAt + patienceSeconds : endsAt + graceSeconds;
            if (!Finite(deadline)) throw new ArgumentException("Appointment deadline is not finite.");
            return deadline;
        }

        // Before the outcome, reserve whichever is later: the agreed end or arrival deadline.
        public static double ReservedUntil(AppointmentRecord appointment)
        {
            if (appointment == null) throw new ArgumentNullException(nameof(appointment));
            return Math.Max(appointment.EndsAt, ResolutionDeadline(appointment));
        }

        // A witnessed approach can extend resolution only through its persisted,
        // two-minute maximum window. Opening the meeting ends that extension.
        public static double ResolutionDeadline(AppointmentRecord appointment)
        {
            if (appointment == null) throw new ArgumentNullException(nameof(appointment));
            double deadline = ArrivalDeadline(appointment);
            if (appointment.PlayerNoticedAt >= 0)
                deadline = Math.Max(deadline, appointment.ApproachUntil);
            return deadline;
        }

        // An already-attended meeting may still open its conversation through
        // the agreed end, even when arrival patience expired after attendance.
        public static double ContactDeadline(AppointmentRecord appointment)
        {
            if (appointment == null) throw new ArgumentNullException(nameof(appointment));
            return appointment.OutcomeRecorded && appointment.Status == "attended"
                ? appointment.EndsAt : ResolutionDeadline(appointment);
        }

        // Preserve legacy holds; new resolved records release witnessed no-shows at their outcome.
        public static double PlannedLeaveAt(AppointmentRecord appointment)
        {
            if (appointment == null) throw new ArgumentNullException(nameof(appointment));
            ArrivalDeadline(appointment);
            if (appointment.PatienceSeconds == 0 && appointment.PlayerNoticedAt < 0)
                return appointment.EndsAt + appointment.GraceSeconds;
            if (appointment.Status == "attended") return appointment.EndsAt;
            if ((appointment.Status == "player-absent" || appointment.Status == "interrupted") &&
                appointment.NpcArrivedAt >= 0)
            {
                if (!Finite(appointment.ResolvedAt) || appointment.ResolvedAt < 0)
                    throw new ArgumentException("Invalid appointment resolution time.");
                return Math.Max(appointment.StartsAt, appointment.ResolvedAt);
            }
            return ReservedUntil(appointment);
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static uint Mix(uint hash, string value)
        {
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619u;
                }
                hash ^= 0xff;
                hash *= 16777619u;
                return hash;
            }
        }
    }

    // Immutable, minimal data needed by the live attendance observer. It avoids
    // copying full appointment payloads or exposing ledger records to callbacks.
    public readonly struct PendingAppointmentObservation
    {
        public readonly string Id,ResidentId,PlaceKind,Detail;
        public readonly int SettlementId,BuildingKey;
        public readonly double StartsAt,EndsAt,GraceSeconds,X,Y,Z;
        public readonly int PatienceSeconds;
        public readonly double PlayerNoticedAt, ApproachUntil, PlayerAtVenueAt;
        internal PendingAppointmentObservation(AppointmentRecord value)
        {
            Id=value.Id;ResidentId=value.ResidentId;PlaceKind=value.Place.Kind;
            SettlementId=value.Place.SettlementId;BuildingKey=value.Place.BuildingKey;
            X=value.Place.X;Y=value.Place.Y;Z=value.Place.Z;Detail=value.Place.Detail;
            StartsAt=value.StartsAt;EndsAt=value.EndsAt;GraceSeconds=value.GraceSeconds;
            PatienceSeconds=value.PatienceSeconds;
            PlayerNoticedAt=value.PlayerNoticedAt;ApproachUntil=value.ApproachUntil;PlayerAtVenueAt=value.PlayerAtVenueAt;
        }
    }

    public readonly struct AppointmentMeetingOffer
    {
        public readonly string AppointmentId, ResidentId, Purpose;
        public readonly AppointmentPlace Place;
        public readonly double StartsAt, EndsAt, ArrivalDeadline, NpcArrivedAt, NpcWaitedSeconds;
        public readonly double PlayerNoticedAt, ApproachUntil;
        public readonly int PatienceSeconds;
        internal AppointmentMeetingOffer(AppointmentRecord value)
        {
            AppointmentId=value.Id;ResidentId=value.ResidentId;Purpose=value.Purpose;
            Place=AppointmentPlace.Copy(value.Place);StartsAt=value.StartsAt;EndsAt=value.EndsAt;
            ArrivalDeadline=AppointmentTiming.ArrivalDeadline(value);NpcArrivedAt=value.NpcArrivedAt;
            NpcWaitedSeconds=value.NpcWaitedSeconds;PatienceSeconds=value.PatienceSeconds;
            PlayerNoticedAt=value.PlayerNoticedAt;ApproachUntil=value.ApproachUntil;
        }
    }

    public readonly struct AppointmentConversationReceipt
    {
        public readonly string AppointmentId, ResidentId, Purpose;
        public readonly AppointmentPlace Place;
        public readonly double StartsAt, EndsAt, ArrivalDeadline, NpcArrivedAt, PlayerArrivedAt, PlayerAtVenueAt;
        public readonly double NpcWaitedSeconds, ConversationOpenedAt;
        public readonly int PatienceSeconds;
        internal AppointmentConversationReceipt(AppointmentRecord value)
        {
            AppointmentId=value.Id;ResidentId=value.ResidentId;Purpose=value.Purpose;
            Place=AppointmentPlace.Copy(value.Place);StartsAt=value.StartsAt;EndsAt=value.EndsAt;
            ArrivalDeadline=AppointmentTiming.ArrivalDeadline(value);NpcArrivedAt=value.NpcArrivedAt;
            PlayerArrivedAt=value.PlayerArrivedAt;PlayerAtVenueAt=value.PlayerAtVenueAt;NpcWaitedSeconds=value.NpcWaitedSeconds;
            ConversationOpenedAt=value.ConversationOpenedAt;PatienceSeconds=value.PatienceSeconds;
        }
    }

    public sealed partial class WorldStateStore
    {
        private const int MaximumAppointments = 10000;
        private List<AppointmentRecord> appointmentExperienceScanOwner;
        private int appointmentExperienceScanCursor;
        public int AppointmentRevision { get; private set; }

        /// <summary>
        /// Replays only the inbox projection of an already confirmed appointment outcome.
        /// Resolution, disposition, and observed memory remain owned by the appointment record.
        /// The cursor resets when Restore replaces the save-owned appointment list.
        /// </summary>
        public int FlushNpcAppointmentExperiences(double now, int maximumExamined)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if (maximumExamined < 1 || maximumExamined > 64)
                throw new ArgumentOutOfRangeException(nameof(maximumExamined));
            var appointments = state.Appointments;
            if (!ReferenceEquals(appointmentExperienceScanOwner, appointments))
            {
                appointmentExperienceScanOwner = appointments;
                appointmentExperienceScanCursor = 0;
            }
            if (appointments.Count == 0) return 0;
            int examined = Math.Min(maximumExamined, appointments.Count);
            int enqueued = 0;
            for (int i = 0; i < examined; i++)
            {
                if (appointmentExperienceScanCursor >= appointments.Count)
                    appointmentExperienceScanCursor = 0;
                int index = appointmentExperienceScanCursor++;
                var appointment = appointments[index];
                if (appointment.ExperienceProjection != NpcAppointmentExperienceProjection.Pending ||
                    appointment.ResolvedAt > now) continue;
                NpcExperienceEvent evidence;
                if (!NpcAppointmentExperience.TryCreate(appointment, out evidence)) continue;
                AppointmentRecord marked;
                if (!TryPrepareProjectedAppointment(appointment, out marked)) continue;
                if (state.NpcExperienceInbox.HasOutcome(evidence.ObserverId, evidence.DeduplicationKey))
                {
                    if (HasExactAppointmentExperience(evidence)) appointments[index] = marked;
                    continue;
                }
                if (TryEnqueueVerifiedNativeExperience(evidence))
                {
                    // The replacement was validated before enqueue; no fallible work remains.
                    appointments[index] = marked;
                    enqueued++;
                }
            }
            return enqueued;
        }

        public bool TryReserveAppointment(AppointmentRecord proposed, double now, out string reason)
        {
            reason = "invalid";
            if (proposed == null || !Finite(now) || now < 0) return false;
            AppointmentRecord copy;
            try { copy = ValidateAndCopyAppointment(state, proposed); }
            catch (ArgumentException) { return false; }

            if (copy.PatienceSeconds == 0)
            {
                int disposition = TryGetActualConversationDisposition(copy.ResidentId, out var actual)
                    ? actual : ConversationDisposition(copy.ResidentId);
                copy.PatienceSeconds = AppointmentTiming.CreatePatienceSeconds(state.WorldSeed,
                    copy.ResidentId, copy.Id, disposition);
            }

            if (copy.StartsAt <= now) return false;
            var resident = ResolveResident(copy.ResidentId);
            if (resident == null || !resident.IsAlive || resident.AgeGroup != ResidentAgeGroup.Adult || resident.IsQuestProtected)
                return false;
            var existing = state.Appointments.Find(item => item.Id == copy.Id);
            if (existing != null)
            {
                // A retry of a legacy-shaped proposal keeps its first captured patience.
                if (proposed.PatienceSeconds == 0) copy.PatienceSeconds = existing.PatienceSeconds;
                if ((existing.Status == "accepted" || existing.Status == "waiting") && SameAppointment(existing, copy))
                { reason = "existing"; return true; }
                reason = "duplicate";
                return false;
            }
            if (!AppointmentTrustAllows(copy.ResidentId)) { reason = "trust"; return false; }
            if (state.Appointments.Count >= MaximumAppointments) { reason = "capacity"; return false; }
            foreach (var booking in state.Appointments)
            {
                if ((booking.Status == "accepted" || booking.Status == "waiting") &&
                    copy.StartsAt < AppointmentTiming.ReservedUntil(booking) &&
                    booking.StartsAt < AppointmentTiming.ReservedUntil(copy))
                { reason = "overlap"; return false; }
            }
            foreach (var trip in state.ResidentTrips)
            {
                if (trip.ResidentId != copy.ResidentId) continue;
                var observed = IndexedObservedVisit(trip.ResidentId);
                double tripEnds = trip.HomeAtFor(copy.StartsAt, observed);
                if (copy.StartsAt < tripEnds && trip.DepartsAt < AppointmentTiming.ReservedUntil(copy))
                { reason = "travel"; return false; }
            }
            state.Appointments.Add(copy);
            BumpAppointmentRevision();
            reason = "accepted";
            return true;
        }

        public List<AppointmentRecord> AppointmentsFor(string residentId)
        {
            return state.Appointments.Where(item => item.ResidentId == residentId)
                .OrderBy(item => item.StartsAt).ThenBy(item => item.Id, StringComparer.Ordinal)
                .Select(AppointmentRecord.Copy).ToList();
        }

        public bool TryNoticeAppointmentPlayer(string appointmentId, double now, double latestSafeApproachAt)
        {
            if (!Finite(now) || now < 0 || !Finite(latestSafeApproachAt) || latestSafeApproachAt < 0 ||
                string.IsNullOrWhiteSpace(appointmentId) || IsNpcProactiveContactSuppressed(now)) return false;
            var appointment=state.Appointments.Find(item=>item.Id==appointmentId);
            if(appointment==null)return false;
            if(appointment.PlayerNoticedAt>=0)
                return appointment.ConversationOpenedAt<0 && now<=AppointmentTiming.ContactDeadline(appointment) &&
                    appointment.ApproachUntil>now;
            bool attended=appointment.OutcomeRecorded && appointment.Status=="attended";
            if(appointment.ConversationOpenedAt>=0 || appointment.OutcomeRecorded && !attended || appointment.Status=="cancelled" ||
                appointment.Status=="interrupted" || appointment.Status=="player-absent" ||
                appointment.Status=="npc-absent" || appointment.NpcArrivedAt<0 ||
                now<appointment.StartsAt || now>AppointmentTiming.ContactDeadline(appointment))return false;

            double safeCutoff=Math.Max(now,latestSafeApproachAt);
            foreach(var other in state.Appointments)
                if(other.Id!=appointment.Id && (other.Status=="accepted"||other.Status=="waiting") &&
                    other.StartsAt>now && other.StartsAt<safeCutoff)
                    safeCutoff=other.StartsAt;
            foreach(var trip in state.ResidentTrips)
                if(trip.ResidentId==appointment.ResidentId && trip.HomeLeavesAt>now && trip.HomeLeavesAt<safeCutoff)
                    safeCutoff=trip.HomeLeavesAt;
            double approachUntil=Math.Min(now+120.0,safeCutoff);
            if(attended)approachUntil=Math.Min(approachUntil,appointment.EndsAt);
            appointment.PlayerNoticedAt=now;
            appointment.ApproachUntil=approachUntil;
            BumpAppointmentRevision();
            return approachUntil>now;
        }

        public bool TryGetAppointmentMeetingOffer(string residentId, double now, out AppointmentMeetingOffer offer)
        {
            offer=default;
            if(string.IsNullOrWhiteSpace(residentId)||!Finite(now)||now<0||IsNpcProactiveContactSuppressed(now))return false;
            AppointmentRecord best=null;
            foreach(var item in state.Appointments)
            {
                if(item.ResidentId!=residentId || item.ConversationOpenedAt>=0 || item.NpcArrivedAt<0 ||
                    now<item.StartsAt || now>AppointmentTiming.ContactDeadline(item))continue;
                if(item.Status!="accepted" && item.Status!="waiting" && item.Status!="attended")continue;
                if(item.OutcomeRecorded && item.Status!="attended")continue;
                if(best==null || item.StartsAt<best.StartsAt)best=item;
            }
            if(best==null)return false;
            offer=new AppointmentMeetingOffer(best);
            return true;
        }

        public bool TryClaimAppointmentConversation(string appointmentId, string expectedResidentId,
            string actualSpeakerId, double now, bool nativeWindowOpen, bool conversationAllowed,
            bool npcNear, bool playerNear, out AppointmentConversationReceipt receipt, out bool isNew)
        {
            receipt=default;isNew=false;
            if(string.IsNullOrWhiteSpace(appointmentId)||string.IsNullOrWhiteSpace(expectedResidentId)||
                !string.Equals(expectedResidentId,actualSpeakerId,StringComparison.Ordinal)||!Finite(now)||now<0)return false;
            var appointment=state.Appointments.Find(item=>item.Id==appointmentId);
            if(appointment==null||appointment.ResidentId!=expectedResidentId)return false;
            if(appointment.ConversationOpenedAt>=0)
            { receipt=new AppointmentConversationReceipt(appointment);return true; }
            if(IsNpcProactiveContactSuppressed(now))return false;
            if(!nativeWindowOpen||!conversationAllowed||!npcNear||!playerNear||appointment.NpcArrivedAt<0||
                now<appointment.StartsAt||now>AppointmentTiming.ContactDeadline(appointment)||
                (appointment.Status!="accepted"&&appointment.Status!="waiting"&&appointment.Status!="attended")||
                (appointment.OutcomeRecorded&&appointment.Status!="attended"))return false;

            appointment.ConversationOpenedAt=now;
            if(appointment.PlayerAtVenueAt<0)appointment.PlayerAtVenueAt=now;
            if(appointment.Status!="attended")
            {
                appointment.PlayerArrivedAt=now;
                ResolveAppointment(appointment,now,"attended",0,
                    "The player and I attended our " + appointment.Purpose + " appointment together.");
            }
            else BumpAppointmentRevision();
            receipt=new AppointmentConversationReceipt(appointment);
            isNew=true;
            return true;
        }

        public void VisitPendingAppointmentObservations(int settlementId,Action<PendingAppointmentObservation> visitor)
        {
            if(visitor==null)throw new ArgumentNullException(nameof(visitor));
            var appointments=state.Appointments;int count=appointments.Count;
            for(int i=0;i<count;i++)
            {
                var appointment=appointments[i];
                if(appointment.OutcomeRecorded || appointment.Place==null || (appointment.Place.Kind!="building" && appointment.Place.Kind!="world-point") ||
                    appointment.Place.SettlementId!=settlementId)continue;
                visitor(new PendingAppointmentObservation(appointment));
            }
        }

        public AppointmentRecord FindAppointment(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var found = state.Appointments.Find(item => item.Id == id);
            return found == null ? null : AppointmentRecord.Copy(found);
        }

        public bool AppointmentTrustAllows(string residentId)
        {
            if (string.IsNullOrWhiteSpace(residentId)) return false;
            return state.Appointments.Count(item => item.ResidentId == residentId &&
                item.OutcomeRecorded && item.Status == "player-absent" && item.Importance >= 2) < 3;
        }

        public void ObserveAppointment(string id, double now, bool npcPresent, bool playerPresent, bool interrupted)
        { ObserveAppointment(id,now,npcPresent,playerPresent,playerPresent,interrupted); }

        public void ObserveAppointment(string id, double now, bool npcPresent, bool playerPresent,
            bool playerAtVenue, bool interrupted)
        {
            Require(Finite(now) && now >= 0, "Invalid appointment observation time.");
            playerAtVenue = playerAtVenue || playerPresent;
            var appointment = state.Appointments.Find(item => item.Id == id);
            if (appointment == null || appointment.OutcomeRecorded) return;

            if (interrupted)
            {
                ResolveAppointment(appointment, now, "interrupted", 0, "The appointment was interrupted before its outcome was confirmed.");
                return;
            }
            double lastArrival = AppointmentTiming.ResolutionDeadline(appointment);
            if (now >= appointment.StartsAt && now <= lastArrival)
            {
                if (npcPresent)
                {
                    if (appointment.NpcArrivedAt < 0) appointment.NpcArrivedAt = now;
                    TrackNpcWait(appointment, now);
                }
                else
                {
                    appointment.NpcLastSeenAt = -1;
                    appointment.NpcWaitedSeconds = 0;
                }
                if (playerAtVenue && appointment.PlayerAtVenueAt < 0) appointment.PlayerAtVenueAt = now;
                if (playerPresent && appointment.PlayerArrivedAt < 0) appointment.PlayerArrivedAt = now;
                if (npcPresent && playerPresent)
                {
                    ResolveAppointment(appointment, now, "attended", 0,
                        "The player and I attended our " + appointment.Purpose + " appointment together.");
                    return;
                }
                appointment.Status = "waiting";
                return;
            }
            if (now <= lastArrival) return;
            bool npcWaited = appointment.NpcWaitedSeconds >= RequiredNpcWait(appointment) &&
                appointment.NpcLastSeenAt >= lastArrival - 15 && appointment.NpcLastSeenAt <= lastArrival &&
                now - appointment.NpcLastSeenAt <= 15;
            if (appointment.NpcArrivedAt >= 0 && appointment.PlayerArrivedAt < 0 &&
                appointment.PlayerAtVenueAt < 0 && npcWaited)
            {
                int penalty = PlayerNoShowPenalty(appointment);
                ResolveAppointment(appointment, now, "player-absent", penalty,
                    "The player missed our " + appointment.Purpose + " appointment after I arrived.");
            }
            else if (appointment.NpcArrivedAt < 0)
                ResolveAppointment(appointment, now, "npc-absent", 0,
                    "I did not attend the scheduled " + appointment.Purpose + " appointment.");
            else
                ResolveAppointment(appointment, now, "interrupted", 0,
                    "We did not meet during our " + appointment.Purpose + " appointment window.");
        }

        public bool CancelAppointment(string id, double now, string reason)
        {
            Require(Finite(now) && now >= 0, "Invalid appointment cancellation time.");
            var appointment = state.Appointments.Find(item => item.Id == id);
            if (appointment == null || appointment.OutcomeRecorded) return false;
            string detail = CleanAppointmentText(reason, 160);
            string observation = "The player cancelled the " + appointment.Purpose + " appointment" +
                (detail.Length == 0 ? "." : ": " + detail + ".");
            ResolveAppointment(appointment, now, "cancelled", 0, observation);
            return true;
        }

        private static void TrackNpcWait(AppointmentRecord appointment, double now)
        {
            if (appointment.NpcLastSeenAt >= 0)
            {
                double gap = now - appointment.NpcLastSeenAt;
                if (gap >= 0 && gap <= 15) appointment.NpcWaitedSeconds += gap;
                else appointment.NpcWaitedSeconds = 0;
            }
            else appointment.NpcWaitedSeconds = 0;
            appointment.NpcLastSeenAt = now;
        }

        private static double RequiredNpcWait(AppointmentRecord appointment)
        { return Math.Min(30, appointment.EndsAt - appointment.StartsAt); }

        private void BumpAppointmentRevision()
        { if (AppointmentRevision < int.MaxValue) AppointmentRevision++; }

        private void ResolveAppointment(AppointmentRecord appointment, double now, string status, int penalty, string observation)
        {
            appointment.Status = status;
            appointment.ResolvedAt = now;
            appointment.OutcomeRecorded = true;
            appointment.NoShowPenalty = penalty;
            if (penalty > 0) ApplyAppointmentDispositionPenalty(appointment.ResidentId, now, penalty);
            RememberObservation(appointment.ResidentId, now,
                "game day " + Math.Floor(now / 86400).ToString("0", CultureInfo.InvariantCulture),
                AppointmentPlaceText(appointment.Place), observation);
            BumpAppointmentRevision();
            NpcExperienceEvent nativeEvidence;
            if (NpcAppointmentExperience.TryCreate(appointment, out nativeEvidence))
            {
                appointment.ExperienceProjection = NpcAppointmentExperienceProjection.Pending;
                AppointmentRecord marked;
                int index = state.Appointments.IndexOf(appointment);
                if (index >= 0 && TryPrepareProjectedAppointment(appointment, out marked) &&
                    TryEnqueueVerifiedNativeExperience(nativeEvidence))
                    state.Appointments[index] = marked;
            }
        }

        private bool TryPrepareProjectedAppointment(AppointmentRecord appointment, out AppointmentRecord marked)
        {
            marked = null;
            try
            {
                var candidate = AppointmentRecord.Copy(appointment);
                candidate.ExperienceProjection = NpcAppointmentExperienceProjection.Projected;
                marked = ValidateAndCopyAppointment(state, candidate);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private bool HasExactAppointmentExperience(NpcExperienceEvent evidence)
        { return HasExactAppointmentExperience(state.NpcExperienceInbox, evidence); }

        private int PlayerNoShowPenalty(AppointmentRecord appointment)
        {
            int prior = state.Appointments.Count(item => item.ResidentId == appointment.ResidentId &&
                item.OutcomeRecorded && item.Status == "player-absent" && item.Importance >= 2);
            return Math.Min(25, 2 + appointment.Importance * 3 + Math.Min(4, prior) * 4);
        }

        private void ApplyAppointmentDispositionPenalty(string residentId, double now, int penalty)
        {
            var attitude = state.ConversationAttitudes.Find(item => item.PersonId == residentId);
            if (attitude == null)
            {
                attitude = new ConversationAttitude { PersonId = residentId, Day = Math.Floor(now / 86400) };
                state.ConversationAttitudes.Add(attitude);
            }
            else
            {
                double day = Math.Floor(now / 86400);
                if (day > attitude.Day)
                {
                    attitude.Day = day;
                    attitude.PositiveToday = 0;
                    attitude.NegativeToday = 0;
                    attitude.Utterances.Clear();
                }
            }
            int previous = attitude.Offset;
            attitude.Offset = Math.Max(-200, attitude.Offset - penalty);
            attitude.NegativeToday = Math.Min(1000000, attitude.NegativeToday + previous - attitude.Offset);
        }

        private static AppointmentRecord ValidateAndCopyAppointment(WorldState world, AppointmentRecord value,
            bool legacyProjection = false)
        {
            Require(value != null && !string.IsNullOrWhiteSpace(value.Id) && value.Id.Length <= 120 &&
                !string.IsNullOrWhiteSpace(value.ResidentId) && value.ResidentId.Length <= 240 &&
                !string.IsNullOrWhiteSpace(value.Purpose) && value.Purpose.Length <= 160 &&
                Finite(value.StartsAt) && value.StartsAt >= 0 && Finite(value.EndsAt) && value.EndsAt > value.StartsAt &&
                Finite(value.GraceSeconds) && value.GraceSeconds >= 0 && value.GraceSeconds <= 86400 &&
                (value.PatienceSeconds == 0 || value.PatienceSeconds >= AppointmentTiming.MinimumPatienceSeconds &&
                    value.PatienceSeconds <= AppointmentTiming.MaximumPatienceSeconds) &&
                value.Importance >= 0 && value.Importance <= 3, "Invalid appointment record.");
            ValidateAppointmentPlace(value.Place);
            Require(value.Status == "accepted" || value.Status == "waiting" || value.Status == "attended" ||
                value.Status == "player-absent" || value.Status == "npc-absent" || value.Status == "cancelled" ||
                value.Status == "interrupted", "Invalid appointment status.");
            double originalDeadline = AppointmentTiming.ArrivalDeadline(value);
            double noticeDeadline=value.OutcomeRecorded && value.Status=="attended"?value.EndsAt:originalDeadline;
            Require(value.PlayerNoticedAt == -1 || Finite(value.PlayerNoticedAt) &&
                value.PlayerNoticedAt >= value.StartsAt && value.PlayerNoticedAt <= noticeDeadline,
                "Invalid player-noticed appointment evidence.");
            Require(value.ApproachUntil == -1 || Finite(value.ApproachUntil) && value.PlayerNoticedAt >= 0 &&
                value.ApproachUntil >= value.PlayerNoticedAt && value.ApproachUntil <= value.PlayerNoticedAt + 120.0,
                "Invalid appointment approach window.");
            Require((value.PlayerNoticedAt == -1) == (value.ApproachUntil == -1),
                "Appointment approach window lacks its first sighting.");
            double lastArrival = AppointmentTiming.ResolutionDeadline(value);
            Require(value.NpcArrivedAt == -1 || Finite(value.NpcArrivedAt) && value.NpcArrivedAt >= value.StartsAt && value.NpcArrivedAt <= originalDeadline,
                "Invalid NPC appointment arrival.");
            Require(value.NpcLastSeenAt == -1 || Finite(value.NpcLastSeenAt) && value.NpcLastSeenAt >= value.StartsAt && value.NpcLastSeenAt <= lastArrival,
                "Invalid NPC appointment presence evidence.");
            Require(value.PlayerArrivedAt == -1 || Finite(value.PlayerArrivedAt) && value.PlayerArrivedAt >= value.StartsAt && value.PlayerArrivedAt <= lastArrival,
                "Invalid player appointment arrival.");
            Require(value.PlayerAtVenueAt == -1 || Finite(value.PlayerAtVenueAt) &&
                value.PlayerAtVenueAt >= value.StartsAt && value.PlayerAtVenueAt <= lastArrival,
                "Invalid player appointment venue observation.");
            Require(value.ConversationOpenedAt == -1 || Finite(value.ConversationOpenedAt) &&
                value.ConversationOpenedAt >= value.StartsAt && value.ConversationOpenedAt <= lastArrival,
                "Invalid appointment conversation receipt.");
            Require(value.FollowUpNoticedAt == -1 || Finite(value.FollowUpNoticedAt) &&
                value.Status == "player-absent" && value.FollowUpNoticedAt >= value.ResolvedAt,
                "Invalid missed-appointment follow-up notice.");
            Require(value.FollowUpApproachUntil == -1 || Finite(value.FollowUpApproachUntil) &&
                value.FollowUpNoticedAt >= 0 && value.FollowUpApproachUntil >= value.FollowUpNoticedAt &&
                value.FollowUpApproachUntil <= value.FollowUpNoticedAt + 120.0,
                "Invalid missed-appointment follow-up approach window.");
            Require((value.FollowUpNoticedAt == -1) == (value.FollowUpApproachUntil == -1),
                "Missed-appointment approach window lacks its first sighting.");
            Require(value.FollowUpConversationOpenedAt == -1 || Finite(value.FollowUpConversationOpenedAt) &&
                value.FollowUpNoticedAt >= 0 && value.FollowUpConversationOpenedAt >= value.FollowUpNoticedAt,
                "Invalid missed-appointment follow-up receipt.");
            Require(Finite(value.NpcWaitedSeconds) && value.NpcWaitedSeconds >= 0 &&
                value.NpcWaitedSeconds <= lastArrival - value.StartsAt &&
                (value.NpcArrivedAt >= 0 || value.NpcLastSeenAt == -1 && value.NpcWaitedSeconds == 0),
                "Invalid NPC waiting duration.");
            bool resolved = value.Status != "accepted" && value.Status != "waiting";
            Require(value.OutcomeRecorded == resolved && (resolved ? Finite(value.ResolvedAt) && value.ResolvedAt >= 0 : value.ResolvedAt == -1),
                "Invalid appointment outcome state.");
            Require(value.NoShowPenalty >= 0 && value.NoShowPenalty <= 25 &&
                (value.Status == "player-absent" ? value.NoShowPenalty > 0 : value.NoShowPenalty == 0),
                "Invalid appointment no-show penalty.");
            if (value.Status == "player-absent")
                Require(value.NpcArrivedAt >= 0 && value.PlayerArrivedAt < 0 && value.PlayerAtVenueAt < 0 &&
                    value.NpcWaitedSeconds >= RequiredNpcWait(value) && value.NpcLastSeenAt >= lastArrival - 15 &&
                    value.NpcLastSeenAt <= lastArrival && value.ResolvedAt > lastArrival &&
                    value.ResolvedAt - value.NpcLastSeenAt <= 15, "Player no-show lacks sufficient NPC wait evidence.");
            if (value.Status == "attended")
                Require(value.NpcArrivedAt >= 0 && value.PlayerArrivedAt >= 0 &&
                    value.ResolvedAt >= value.StartsAt && value.ResolvedAt <= lastArrival,
                    "Attended appointment lacks participant arrival evidence.");
            if (value.ConversationOpenedAt >= 0)
                Require(value.Status == "attended" && value.PlayerArrivedAt >= 0 &&
                    value.ConversationOpenedAt >= value.PlayerArrivedAt,
                    "Opened appointment conversation lacks attended meeting evidence.");
            if (value.Status == "npc-absent")
                Require(value.NpcArrivedAt < 0, "NPC absence conflicts with recorded arrival.");
            bool supportsExperience = value.OutcomeRecorded &&
                (value.Status == "attended" || value.Status == "player-absent");
            if (legacyProjection)
                Require(value.ExperienceProjection == NpcAppointmentExperienceProjection.None,
                    "Legacy appointment contains a future experience projection marker.");
            else if (supportsExperience)
                Require(value.ExperienceProjection == NpcAppointmentExperienceProjection.Pending ||
                    value.ExperienceProjection == NpcAppointmentExperienceProjection.Projected ||
                    value.ExperienceProjection == NpcAppointmentExperienceProjection.LegacyUnavailable,
                    "Resolved appointment lacks a valid experience projection state.");
            else
                Require(value.ExperienceProjection == NpcAppointmentExperienceProjection.None,
                    "Unsupported or unresolved appointment claims an experience projection.");
            if (!legacyProjection && value.ExperienceProjection == NpcAppointmentExperienceProjection.LegacyUnavailable &&
                NpcAppointmentExperience.TryCreate(value, out var legacyEvidence))
                Require(!HasExactAppointmentExperience(world.NpcExperienceInbox, legacyEvidence),
                    "Legacy-unavailable appointment has an exact saved experience outcome.");
            Require(KnownAppointmentResident(world, value.ResidentId) != null, "Appointment resident is not known.");
            return AppointmentRecord.Copy(value);
        }

        private static Resident KnownAppointmentResident(WorldState world, string residentId)
        {
            foreach (var settlement in world.Settlements)
            {
                var resident = ResolveResident(world, settlement.SettlementId, residentId);
                if (resident != null) return resident;
            }
            return null;
        }

        private static List<AppointmentRecord> CopyAppointments(WorldState source, WorldState validatedWorld)
        {
            Require(source.Appointments != null && source.Appointments.Count <= MaximumAppointments, "Missing or oversized appointment ledger.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<AppointmentRecord>();
            foreach (var item in source.Appointments)
            {
                var copy = ValidateAndCopyAppointment(validatedWorld, item, source.Version < 49);
                if (source.Version < 49 && copy.OutcomeRecorded &&
                    (copy.Status == "attended" || copy.Status == "player-absent"))
                {
                    NpcExperienceEvent evidence;
                    copy.ExperienceProjection = NpcAppointmentExperience.TryCreate(copy, out evidence) &&
                        HasExactAppointmentExperience(validatedWorld.NpcExperienceInbox, evidence)
                        ? NpcAppointmentExperienceProjection.Projected
                        : NpcAppointmentExperienceProjection.LegacyUnavailable;
                }
                Require(ids.Add(copy.Id), "Duplicate appointment ID.");
                result.Add(copy);
            }
            for (int i = 0; i < result.Count; i++)
            for (int j = i + 1; j < result.Count; j++)
                if ((result[i].Status == "accepted" || result[i].Status == "waiting") &&
                    (result[j].Status == "accepted" || result[j].Status == "waiting"))
                    Require(result[i].StartsAt >= AppointmentTiming.ReservedUntil(result[j]) ||
                        result[j].StartsAt >= AppointmentTiming.ReservedUntil(result[i]),
                        "Overlapping active appointments.");
            return result;
        }

        private static bool HasExactAppointmentExperience(NpcExperienceInbox inbox, NpcExperienceEvent evidence)
        {
            if (inbox == null || inbox.Entries == null || evidence == null) return false;
            foreach (var entry in inbox.Entries)
                if (entry != null && entry.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                    entry.ObserverId == evidence.ObserverId && entry.SubjectPersonId == evidence.SubjectPersonId &&
                    entry.ReceiptId == evidence.ReceiptId && entry.EpisodeId == evidence.EpisodeId &&
                    entry.OutcomeId == evidence.OutcomeId && entry.EventKind == evidence.EventKind &&
                    entry.ObservedGameTime == evidence.ObservedGameTime &&
                    entry.DeduplicationKey == evidence.DeduplicationKey &&
                    entry.ResponsibleActorAttribution == evidence.ResponsibleActorAttribution &&
                    entry.ResponsibleActorId == evidence.ResponsibleActorId && entry.ImpactDetailsKind == 0)
                    return true;
            return false;
        }

        private static void ValidateAppointmentPlace(AppointmentPlace place)
        {
            Require(place != null && (place.Kind == "building" || place.Kind == "dungeon-entrance" ||
                place.Kind == "world-point" || place.Kind == "indoor-hotspot") && place.SettlementId >= 0 &&
                place.BuildingKey >= -1 && Finite(place.X) && Finite(place.Y) && Finite(place.Z) &&
                Math.Abs(place.X) <= 1000000 && Math.Abs(place.Y) <= 1000000 && Math.Abs(place.Z) <= 1000000 &&
                (place.Detail == null || place.Detail.Length <= 240), "Invalid appointment place.");
            if (place.Kind == "building" || place.Kind == "indoor-hotspot")
                Require(place.BuildingKey >= 0, "Appointment place needs a building key.");
        }

        private static string AppointmentPlaceText(AppointmentPlace place)
        {
            if (!string.IsNullOrWhiteSpace(place.Detail)) return CleanAppointmentText(place.Detail, 240);
            if (place.BuildingKey >= 0) return "settlement " + place.SettlementId + ", building " + place.BuildingKey;
            return place.Kind + " in settlement " + place.SettlementId;
        }

        private static string CleanAppointmentText(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string text = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= maximum ? text : text.Substring(0, maximum);
        }

        private static bool SameAppointment(AppointmentRecord a, AppointmentRecord b)
        {
            return a.ResidentId == b.ResidentId && a.Purpose == b.Purpose && a.StartsAt == b.StartsAt &&
                a.EndsAt == b.EndsAt && a.GraceSeconds == b.GraceSeconds && a.PatienceSeconds == b.PatienceSeconds && a.Importance == b.Importance &&
                a.Place.Kind == b.Place.Kind && a.Place.SettlementId == b.Place.SettlementId &&
                a.Place.BuildingKey == b.Place.BuildingKey && a.Place.X == b.Place.X && a.Place.Y == b.Place.Y &&
                a.Place.Z == b.Place.Z && a.Place.Detail == b.Place.Detail;
        }
    }
}
