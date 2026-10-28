using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private sealed class NativeReferenceComparer : IEqualityComparer<object>
        {
            public new bool Equals(object left, object right) { return ReferenceEquals(left, right); }
            public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); }
        }

        private readonly Dictionary<object, NpcPoisonAssignmentTicket> poisonAssignments =
            new Dictionary<object, NpcPoisonAssignmentTicket>(new NativeReferenceComparer());
        private int pendingPoisonDrainCursor;

        /// <summary>
        /// The trusted native binding calls this only after proving this exact bundle is an active poison
        /// assignment. Core binds its object identity to one owner and the current restore epoch.
        /// </summary>
        public NpcPoisonAssignmentTicket TryIssueNpcPoisonAssignment(object expectedVitalsEpoch,
            string observerId, object nativeBindingOwner, object nativeBundleIdentity)
        {
            if (nativeBindingOwner == null || nativeBundleIdentity == null ||
                !IsNpcVitalsWriteEpochCurrent(expectedVitalsEpoch) || !EligibleResident(observerId) ||
                poisonAssignments.ContainsKey(nativeBundleIdentity)) return null;
            var ticket = new NpcPoisonAssignmentTicket(this, expectedVitalsEpoch, observerId,
                nativeBindingOwner, nativeBundleIdentity);
            poisonAssignments.Add(nativeBundleIdentity, ticket);
            return ticket;
        }

        public bool ReleaseNpcPoisonAssignment(object expectedVitalsEpoch, string observerId,
            object nativeBindingOwner, object nativeBundleIdentity)
        {
            if (!IsNpcVitalsWriteEpochCurrent(expectedVitalsEpoch) || nativeBindingOwner == null ||
                nativeBundleIdentity == null) return false;
            NpcPoisonAssignmentTicket ticket;
            if (!poisonAssignments.TryGetValue(nativeBundleIdentity, out ticket) ||
                ticket.PersonId != observerId || !ReferenceEquals(ticket.NativeBindingOwner, nativeBindingOwner))
                return false;
            ticket.Revoked = true;
            poisonAssignments.Remove(nativeBundleIdentity);
            return true;
        }

        public int ReleaseAllNpcPoisonAssignments(object expectedVitalsEpoch, string observerId,
            object nativeBindingOwner)
        {
            if (!IsNpcVitalsWriteEpochCurrent(expectedVitalsEpoch) || nativeBindingOwner == null) return 0;
            var keys = new List<object>();
            foreach (var item in poisonAssignments)
                if (item.Value.PersonId == observerId && ReferenceEquals(item.Value.NativeBindingOwner, nativeBindingOwner))
                { item.Value.Revoked = true; keys.Add(item.Key); }
            foreach (var key in keys) poisonAssignments.Remove(key);
            return keys.Count;
        }

        public NpcPoisonCaptureDisposition TryRecordNpcPoisonAssignment(NpcPoisonAssignmentTicket ticket,
            string nativeEffectKey, double observedGameTime, out NpcExperienceEvent evidence)
        {
            evidence = null;
            NpcPoisonAssignmentTicket registered;
            if (ticket == null || ticket.Revoked || ticket.Consumed || !ReferenceEquals(ticket.Owner, this) ||
                !IsNpcVitalsWriteEpochCurrent(ticket.Epoch) || !EligibleResident(ticket.PersonId) ||
                !poisonAssignments.TryGetValue(ticket.NativeBundle, out registered) ||
                !ReferenceEquals(ticket, registered) || !FiniteGameTime(observedGameTime) || observedGameTime < 0)
                return NpcPoisonCaptureDisposition.Rejected;
            try { NpcExperienceImpactDetails.ForStatusOnset(NpcExperienceStatusKind.Poison, nativeEffectKey, 1); }
            catch (ArgumentException) { return NpcPoisonCaptureDisposition.Rejected; }

            WorldState candidate;
            NpcPoisonOnsetState next;
            try
            {
                candidate = state.CandidateForPoisonProjection();
                next = candidate.NpcPoisonOnsets.Find(x => x.PersonId == ticket.PersonId);
                if (next == null)
                { next = new NpcPoisonOnsetState { PersonId = ticket.PersonId }; candidate.NpcPoisonOnsets.Add(next); }
            }
            catch (ArgumentException) { return NpcPoisonCaptureDisposition.Rejected; }

            NpcExperienceInbox preparedInbox = null;
            NpcExperienceEvent preparedEvidence = null;
            NpcPoisonCaptureDisposition disposition;
            if (next.LastIssuedSequence == long.MaxValue)
            {
                next.SequenceExhausted = true;
                AddDegradedPoisonOnset(next, observedGameTime);
                disposition = NpcPoisonCaptureDisposition.Degraded;
            }
            else
            {
                long sequence = next.LastIssuedSequence + 1;
                next.LastIssuedSequence = sequence;
                if (next.Pending != null)
                {
                    next.UnprojectedDegradedCount++;
                    AddDegradedPoisonOnset(next, observedGameTime);
                    disposition = NpcPoisonCaptureDisposition.Degraded;
                }
                else
                {
                    try { preparedEvidence = PoisonOnsetEvidence(ticket.PersonId, nativeEffectKey, observedGameTime, sequence); }
                    catch (ArgumentException) { return NpcPoisonCaptureDisposition.Rejected; }
                    if (TryPrepareVerifiedNativeExperience(preparedEvidence, out preparedInbox))
                    { next.LastProjectedSequence = sequence; next.UnprojectedDegradedCount = 0;
                        candidate.NpcExperienceInbox = preparedInbox;
                        disposition = NpcPoisonCaptureDisposition.Queued; }
                    else
                    { next.Pending = new NpcPoisonPendingOnset { Sequence = sequence,
                        NativeEffectKey = nativeEffectKey, ObservedGameTime = observedGameTime };
                        disposition = NpcPoisonCaptureDisposition.Pending; }
                }
            }
            WorldState validated;
            try { validated = CopyValidated(candidate); }
            catch (ArgumentException) { return NpcPoisonCaptureDisposition.Rejected; }
            catch (InvalidOperationException) { return NpcPoisonCaptureDisposition.Rejected; }
            state.NpcPoisonOnsets = validated.NpcPoisonOnsets;
            if (preparedInbox != null) state.NpcExperienceInbox = validated.NpcExperienceInbox;
            ticket.Consumed = true;
            evidence = disposition == NpcPoisonCaptureDisposition.Queued
                ? NpcExperienceEvent.CopyValidated(preparedEvidence) : null;
            if(evidence!=null)RememberVerifiedExperience(evidence);
            return disposition;
        }

        public bool TryProjectPendingNpcPoisonOnset(string personId, out NpcExperienceEvent evidence)
        {
            evidence = null;
            var current = state.NpcPoisonOnsets.Find(x => x.PersonId == personId);
            if (current == null || current.Pending == null) return false;
            NpcExperienceEvent projected;
            NpcExperienceInbox nextInbox;
            try
            {
                projected = PoisonOnsetEvidence(personId, current.Pending.NativeEffectKey,
                    current.Pending.ObservedGameTime, current.Pending.Sequence);
                if (!TryPrepareVerifiedNativeExperience(projected, out nextInbox)) return false;
                var candidate = state.CandidateForPoisonProjection();
                var next = candidate.NpcPoisonOnsets.Find(x => x.PersonId == personId);
                next.LastProjectedSequence = next.Pending.Sequence;
                next.UnprojectedDegradedCount = next.LastIssuedSequence - next.Pending.Sequence;
                next.Pending = null;
                candidate.NpcExperienceInbox = nextInbox;
                var validated = CopyValidated(candidate);
                state.NpcPoisonOnsets = validated.NpcPoisonOnsets;
                state.NpcExperienceInbox = validated.NpcExperienceInbox;
                evidence = NpcExperienceEvent.CopyValidated(projected);
                RememberVerifiedExperience(evidence);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        public int DrainPendingNpcPoisonOnsets(int maximumExamined)
        {
            if (maximumExamined < 1 || maximumExamined > 32) throw new ArgumentOutOfRangeException(nameof(maximumExamined));
            int count = state.NpcPoisonOnsets.Count;
            if (count == 0) { pendingPoisonDrainCursor = 0; return 0; }
            int projected = 0;
            for (int examined = 0; examined < maximumExamined && examined < count; examined++)
            {
                if (pendingPoisonDrainCursor >= count) pendingPoisonDrainCursor = 0;
                string personId = state.NpcPoisonOnsets[pendingPoisonDrainCursor++].PersonId;
                if (TryProjectPendingNpcPoisonOnset(personId, out _)) projected++;
            }
            return projected;
        }

        public NpcPoisonOnsetState ReadNpcPoisonOnsetState(string personId)
        {
            var current = state.NpcPoisonOnsets.Find(x => x.PersonId == personId);
            return current == null ? null : NpcPoisonOnsetState.CopyValidated(current);
        }

        private static void AddDegradedPoisonOnset(NpcPoisonOnsetState state, double at)
        {
            if (state.DegradedCount == 0) state.DegradedFirstAt = at;
            else if (at < state.DegradedFirstAt) state.DegradedFirstAt = at;
            if (state.DegradedCount < long.MaxValue) state.DegradedCount++;
            else state.DegradedCountSaturated = true;
            if (at > state.DegradedLastAt) state.DegradedLastAt = at;
        }

        private static NpcExperienceEvent PoisonOnsetEvidence(string personId, string nativeEffectKey,
            double observedGameTime, long sequence)
        {
            string suffix = sequence.ToString(CultureInfo.InvariantCulture);
            string token = StableObserverToken(personId) + ":" + suffix;
            return NpcExperienceEvent.FromNativeOutcome(personId, personId,
                "poison-receipt:" + token, "poison-episode:" + token, "poison-outcome:" + token,
                NpcExperienceEventKind.StatusOnset, observedGameTime,
                NpcResponsibleActorAttribution.Unknown, null,
                NpcExperienceImpactDetails.ForStatusOnset(NpcExperienceStatusKind.Poison, nativeEffectKey, sequence));
        }

        private static List<NpcPoisonOnsetState> CopyNpcPoisonOnsets(WorldState source, WorldState validated)
        {
            var result = new List<NpcPoisonOnsetState>();
            var byPerson = new Dictionary<string, NpcPoisonOnsetState>(StringComparer.Ordinal);
            {
                Require(source.NpcPoisonOnsets != null, "Missing NPC poison source states.");
                foreach (var raw in source.NpcPoisonOnsets)
                {
                    var item = NpcPoisonOnsetState.CopyValidated(raw);
                    bool alive;
                    Require(KnownNpcIdentity(validated, item.PersonId, out alive) &&
                        !byPerson.ContainsKey(item.PersonId), "Unknown or duplicate NPC poison source owner.");
                    byPerson.Add(item.PersonId, item); result.Add(item);
                }
            }
            var canonicalSequences = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in validated.NpcExperienceInbox.Entries)
            {
                if (row.Origin != NpcExperienceEventOrigin.NativeOutcome ||
                    row.EventKind != NpcExperienceEventKind.StatusOnset ||
                    row.ImpactDetailsKind != (int)NpcExperienceImpactKind.StatusOnset ||
                    row.StatusKind != (int)NpcExperienceStatusKind.Poison || row.StatusOnsetSequence < 1) continue;
                string suffix = StableObserverToken(row.ObserverId) + ":" +
                    row.StatusOnsetSequence.ToString(CultureInfo.InvariantCulture);
                if (row.ReceiptId != "poison-receipt:" + suffix ||
                    row.EpisodeId != "poison-episode:" + suffix ||
                    row.OutcomeId != "poison-outcome:" + suffix ||
                    row.SubjectPersonId != row.ObserverId ||
                    row.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Unknown ||
                    row.ResponsibleActorId != null) continue; // Historical unclassified detail stays retained.
                Require(canonicalSequences.Add(row.ObserverId + ":" + suffix),
                    "Duplicate canonical NPC poison source sequence.");
                NpcPoisonOnsetState item;
                Require(byPerson.TryGetValue(row.ObserverId, out item), "Canonical poison receipt lacks a source cursor.");
                Require(row.StatusOnsetSequence <= item.LastProjectedSequence,
                    "Retained poison receipt exceeds projected source cursor.");
            }
            // A noncanonical imported row may collide on a pending assignment's key.
            // The key alone never proves that exact physical assignment projected.
            return result;
        }
    }
}
