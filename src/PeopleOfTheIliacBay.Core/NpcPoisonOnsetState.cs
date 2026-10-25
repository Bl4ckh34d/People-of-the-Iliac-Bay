using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcPoisonCaptureDisposition { Rejected = 0, Queued = 1, Pending = 2, Degraded = 3 }

    [Serializable]
    public sealed class NpcPoisonPendingOnset
    {
        public long Sequence;
        public string NativeEffectKey;
        public double ObservedGameTime;

        public static NpcPoisonPendingOnset CopyValidated(NpcPoisonPendingOnset source)
        {
            if (source == null || source.Sequence < 1 || double.IsNaN(source.ObservedGameTime) ||
                double.IsInfinity(source.ObservedGameTime) || source.ObservedGameTime < 0)
                throw new ArgumentException("Invalid pending NPC poison onset.", nameof(source));
            NpcExperienceImpactDetails.ForStatusOnset(NpcExperienceStatusKind.Poison,
                source.NativeEffectKey, source.Sequence);
            return new NpcPoisonPendingOnset { Sequence = source.Sequence,
                NativeEffectKey = source.NativeEffectKey, ObservedGameTime = source.ObservedGameTime };
        }
    }

    [Serializable]
    public sealed class NpcPoisonOnsetState
    {
        public string PersonId;
        public long LastIssuedSequence;
        public long LastProjectedSequence;
        public NpcPoisonPendingOnset Pending;
        // Exact number of numbered, unprojected onsets whose detail is unavailable.
        // Unlike DegradedCount, this resets when a later exact onset is projected.
        public long UnprojectedDegradedCount;
        // Exact missing events cannot be reconstructed. This is a typed source-history gap,
        // never an experience event, mood cause or actor-attribution claim.
        public long DegradedCount;
        public bool DegradedCountSaturated;
        public double DegradedFirstAt;
        public double DegradedLastAt;
        public bool SequenceExhausted;

        public bool HasDegradedHistory { get { return DegradedCount > 0; } }

        public static NpcPoisonOnsetState CopyValidated(NpcPoisonOnsetState source)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.PersonId) || source.PersonId.Length > 240 ||
                source.LastIssuedSequence < 1 || source.LastProjectedSequence < 0 ||
                source.LastProjectedSequence > source.LastIssuedSequence || source.DegradedCount < 0 ||
                source.UnprojectedDegradedCount < 0 ||
                source.DegradedCountSaturated && source.DegradedCount != long.MaxValue ||
                source.SequenceExhausted && source.LastIssuedSequence != long.MaxValue ||
                source.SequenceExhausted && source.DegradedCount == 0 ||
                double.IsNaN(source.DegradedFirstAt) || double.IsInfinity(source.DegradedFirstAt) ||
                double.IsNaN(source.DegradedLastAt) || double.IsInfinity(source.DegradedLastAt) ||
                source.DegradedFirstAt < 0 || source.DegradedLastAt < source.DegradedFirstAt ||
                source.DegradedCount == 0 && (source.DegradedFirstAt != 0 || source.DegradedLastAt != 0 ||
                    source.DegradedCountSaturated) ||
                source.DegradedCount > 0 && source.DegradedLastAt < source.DegradedFirstAt)
                throw new ArgumentException("Invalid NPC poison onset state.", nameof(source));
            var pending = source.Pending == null ? null : NpcPoisonPendingOnset.CopyValidated(source.Pending);
            if (pending != null && (pending.Sequence <= source.LastProjectedSequence ||
                pending.Sequence > source.LastIssuedSequence))
                throw new ArgumentException("Invalid pending NPC poison source sequence.", nameof(source));
            // Cumulative gaps can lie behind LastProjectedSequence. This separate
            // current-suffix count prevents an old gap from masking a lost Pending.
            long unprojected = source.LastIssuedSequence - source.LastProjectedSequence;
            if (pending != null) unprojected--;
            if (unprojected != source.UnprojectedDegradedCount ||
                source.UnprojectedDegradedCount > source.DegradedCount)
                throw new ArgumentException("Unexplained NPC poison source sequence gap.", nameof(source));
            return new NpcPoisonOnsetState { PersonId = source.PersonId,
                LastIssuedSequence = source.LastIssuedSequence, LastProjectedSequence = source.LastProjectedSequence,
                Pending = pending, UnprojectedDegradedCount = source.UnprojectedDegradedCount,
                DegradedCount = source.DegradedCount,
                DegradedCountSaturated = source.DegradedCountSaturated,
                DegradedFirstAt = source.DegradedFirstAt, DegradedLastAt = source.DegradedLastAt,
                SequenceExhausted = source.SequenceExhausted };
        }
    }

    // The object identity is only valid for this store and native binding epoch; it is never serialized.
    public sealed class NpcPoisonAssignmentTicket
    {
        internal readonly WorldStateStore Owner;
        internal readonly object Epoch;
        internal readonly string PersonId;
        internal readonly object NativeBindingOwner;
        internal readonly object NativeBundle;
        internal bool Consumed;
        internal bool Revoked;

        internal NpcPoisonAssignmentTicket(WorldStateStore owner, object epoch, string personId,
            object nativeBindingOwner, object nativeBundle)
        { Owner = owner; Epoch = epoch; PersonId = personId;
            NativeBindingOwner = nativeBindingOwner; NativeBundle = nativeBundle; }
    }
}
