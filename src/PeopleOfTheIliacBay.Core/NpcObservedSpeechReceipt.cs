using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcObservedSpeechAffinityExclusion
    {
        None = 0,
        FamilyThreatDispositionFloor = 1
    }

    /// <summary>Exact speech plus its native receipt identity, ready for queueing or an accepted-turn transaction.</summary>
    public sealed class NpcObservedSpeechReceipt
    {
        public const int NativeAffinityDeltaBound = 300;
        private NpcObservedSpeechReceipt(NpcExperienceEvent evidence, NpcObservedConversationTurn turn,
            int? immediateAffinityDelta, NpcObservedSpeechAffinityExclusion affinityExclusion, bool affinityOutcomeAttached)
        {
            Evidence = evidence; Turn = turn; ImmediateAffinityDelta = immediateAffinityDelta;
            AffinityExclusion = affinityExclusion; this.affinityOutcomeAttached = affinityOutcomeAttached;
        }

        private readonly bool affinityOutcomeAttached;
        public NpcExperienceEvent Evidence { get; private set; }
        public NpcObservedConversationTurn Turn { get; private set; }
        /// <summary>Exact signed native affinity change, or null when provenance is legacy/unknown.</summary>
        public int? ImmediateAffinityDelta { get; private set; }
        public NpcObservedSpeechAffinityExclusion AffinityExclusion { get; private set; }

        public static NpcObservedSpeechReceipt Create(NpcExperienceEvent evidence,
            NpcObservedConversationTurn turn)
        {
            if (evidence == null || turn == null) throw new ArgumentNullException(evidence == null ? nameof(evidence) : nameof(turn));
            var copy = NpcExperienceEvent.CopyValidated(evidence);
            var turnCopy = NpcObservedConversationTurn.Create(turn.ObserverId, turn.EpisodeId, turn.SpeakerId,
                turn.ReceiptId, turn.Text, turn.ObservedGameTime);
            ValidateBinding(copy, turnCopy);
            return new NpcObservedSpeechReceipt(copy, turnCopy, null,
                NpcObservedSpeechAffinityExclusion.None, false);
        }

        /// <summary>Returns a new exact receipt carrying native signed affinity provenance.</summary>
        public NpcObservedSpeechReceipt WithAppliedAffinity(int actualDelta,
            NpcObservedSpeechAffinityExclusion exclusion = NpcObservedSpeechAffinityExclusion.None)
        {
            if (affinityOutcomeAttached || ImmediateAffinityDelta.HasValue || AffinityExclusion != NpcObservedSpeechAffinityExclusion.None)
                throw new InvalidOperationException("Native affinity provenance is already attached to this speech receipt.");
            ValidateAffinityProvenance(actualDelta, exclusion);
            return new NpcObservedSpeechReceipt(NpcExperienceEvent.CopyValidated(Evidence),
                NpcObservedConversationTurn.Create(Turn.ObserverId, Turn.EpisodeId, Turn.SpeakerId,
                    Turn.ReceiptId, Turn.Text, Turn.ObservedGameTime), actualDelta, exclusion, true);
        }

        /// <summary>
        /// Creates one observer-scoped receipt for actual submitted text. Caller supplies a persisted
        /// occurrence number; it must remain stable across retries of this same native turn.
        /// </summary>
        public static NpcObservedSpeechReceipt Create(string observerId, long occurrence, string text, double observedGameTime)
        {
            if (string.IsNullOrWhiteSpace(observerId) || observerId.Length > 240 || occurrence < 1 ||
                string.IsNullOrWhiteSpace(text) || text.Length > 1024 || !Finite(observedGameTime) || observedGameTime < 0)
                throw new ArgumentException("Invalid observed speech receipt.");

            string episodeId = observerId;
            string suffix = occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string receiptId = "native-heard-receipt-" + suffix;
            string utteranceId = "native-heard-utterance-" + suffix;
            string speakerId = NpcDirectedRelationship.PlayerTargetId;
            var evidence = NpcExperienceEvent.FromObservedSpeech(observerId, observerId, receiptId,
                episodeId, utteranceId, observedGameTime, speakerId);
            var turn = NpcObservedConversationTurn.Create(observerId, episodeId, speakerId,
                receiptId, text, observedGameTime);
            return new NpcObservedSpeechReceipt(evidence, turn, null,
                NpcObservedSpeechAffinityExclusion.None, false);
        }

        // Only the issued-speech source calls this factory. The compact digest keeps all three
        // IDs below the experience identity bound even for a maximum-length person ID.
        internal static NpcObservedSpeechReceipt CreateIssued(string scopeId, string observerId,
            long ordinal, string text, double observedGameTime)
        {
            if (string.IsNullOrWhiteSpace(scopeId) || string.IsNullOrWhiteSpace(observerId) ||
                ordinal < 1 || ordinal == long.MaxValue || string.IsNullOrWhiteSpace(text) ||
                text.Length > 1024 || !Finite(observedGameTime) || observedGameTime < 0)
                throw new ArgumentException("Invalid issued observed speech.");
            string address = IssuedAddress(scopeId, observerId);
            string number = ordinal.ToString(CultureInfo.InvariantCulture);
            string episodeId = "native-heard-v2-episode-" + address;
            string receiptId = "native-heard-v2-receipt-" + address + "-" + number;
            string utteranceId = "native-heard-v2-utterance-" + address + "-" + number;
            var evidence = NpcExperienceEvent.FromObservedSpeech(observerId, observerId,
                receiptId, episodeId, utteranceId, observedGameTime, NpcDirectedRelationship.PlayerTargetId);
            var turn = NpcObservedConversationTurn.Create(observerId, episodeId,
                NpcDirectedRelationship.PlayerTargetId, receiptId, text, observedGameTime);
            return new NpcObservedSpeechReceipt(evidence, turn, null,
                NpcObservedSpeechAffinityExclusion.None, false);
        }

        internal static string IssuedReceiptId(string scopeId, string observerId, long ordinal)
        {
            return "native-heard-v2-receipt-" + IssuedAddress(scopeId, observerId) + "-" +
                ordinal.ToString(CultureInfo.InvariantCulture);
        }

        private static string IssuedAddress(string scopeId, string observerId)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(scopeId + "|" + observerId);
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        public static NpcObservedSpeechReceipt CopyValidated(NpcObservedSpeechReceipt source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var evidence = NpcExperienceEvent.CopyValidated(source.Evidence);
            if (evidence.Origin != NpcExperienceEventOrigin.AttributedSpeech || evidence.ClaimedEventKind.HasValue ||
                evidence.ClaimedResponsibleActorId != null || source.Turn == null)
                throw new ArgumentException("Invalid observed speech receipt.", nameof(source));
            var turn = NpcObservedConversationTurn.Create(source.Turn.ObserverId, source.Turn.EpisodeId,
                source.Turn.SpeakerId, source.Turn.ReceiptId, source.Turn.Text, source.Turn.ObservedGameTime);
            ValidateBinding(evidence, turn);
            if (!Enum.IsDefined(typeof(NpcObservedSpeechAffinityExclusion), source.AffinityExclusion) ||
                source.ImmediateAffinityDelta.HasValue && (source.ImmediateAffinityDelta.Value < -NativeAffinityDeltaBound ||
                    source.ImmediateAffinityDelta.Value > NativeAffinityDeltaBound) ||
                source.AffinityExclusion == NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor &&
                    !source.ImmediateAffinityDelta.HasValue ||
                source.affinityOutcomeAttached != (source.ImmediateAffinityDelta.HasValue ||
                    source.AffinityExclusion != NpcObservedSpeechAffinityExclusion.None))
                throw new ArgumentException("Invalid native affinity provenance.", nameof(source));
            return new NpcObservedSpeechReceipt(evidence, turn, source.ImmediateAffinityDelta,
                source.AffinityExclusion, source.affinityOutcomeAttached);
        }

        private static void ValidateBinding(NpcExperienceEvent evidence, NpcObservedConversationTurn turn)
        {
            if (evidence.Origin != NpcExperienceEventOrigin.AttributedSpeech || evidence.ClaimedEventKind.HasValue ||
                evidence.ClaimedResponsibleActorId != null || turn.ObserverId != evidence.ObserverId ||
                turn.EpisodeId != evidence.EpisodeId || turn.SpeakerId != evidence.AttributedSpeakerId ||
                turn.ReceiptId != evidence.ReceiptId || turn.ObservedGameTime != evidence.ObservedGameTime)
                throw new ArgumentException("Observed speech does not match its receipt.");
        }

        internal static void ValidateAffinityProvenance(int actualDelta,
            NpcObservedSpeechAffinityExclusion exclusion)
        {
            if (actualDelta < -NativeAffinityDeltaBound || actualDelta > NativeAffinityDeltaBound ||
                !Enum.IsDefined(typeof(NpcObservedSpeechAffinityExclusion), exclusion))
                throw new ArgumentOutOfRangeException(nameof(actualDelta), "Invalid native affinity provenance.");
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>Reads the inbox counter without copying unrelated world state.</summary>
        public long CaptureNpcExperienceInboxRevision()
        { return state.NpcExperienceInbox.Revision; }
    }
}
