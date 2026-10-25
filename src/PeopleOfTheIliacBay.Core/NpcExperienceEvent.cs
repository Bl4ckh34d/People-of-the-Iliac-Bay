using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcExperienceEventKind
    {
        MoneyReceived = 1,
        Injury = 2,
        Healing = 3,
        StatusOnset = 4,
        AppointmentAttended = 5,
        AppointmentNoShow = 6,
        CombatHelp = 7,
        ObligationFulfilled = 8,
        ObligationBreached = 9,
        GiftReceived = 10,
        ItemReceived = 11
    }

    public enum NpcExperienceEventOrigin
    {
        NativeOutcome = 1,
        AttributedSpeech = 2
    }

    public enum NpcResponsibleActorAttribution
    {
        NotApplicable = 0,
        Unknown = 1,
        Known = 2
    }

    /// <summary>
    /// Immutable evidence that one observer saw a native outcome or received an attributed claim.
    /// Attributed speech never establishes a native outcome or responsible actor.
    /// </summary>
    public sealed class NpcExperienceEvent
    {
        private const int MaximumIdentityLength = 240;

        public string ObserverId { get; private set; }
        public string SubjectPersonId { get; private set; }
        public string ReceiptId { get; private set; }
        public string EpisodeId { get; private set; }
        public string OutcomeId { get; private set; }
        public string UtteranceId { get; private set; }
        public NpcExperienceEventKind? EventKind { get; private set; }
        public NpcExperienceEventKind? ClaimedEventKind { get; private set; }
        public NpcExperienceEventOrigin Origin { get; private set; }
        public double ObservedGameTime { get; private set; }
        public NpcResponsibleActorAttribution ResponsibleActorAttribution { get; private set; }
        public string ResponsibleActorId { get; private set; }
        public string AttributedSpeakerId { get; private set; }
        public string ClaimedResponsibleActorId { get; private set; }
        public NpcExperienceImpactDetails NativeImpactDetails { get; private set; }
        public string DeduplicationKey { get; private set; }

        private NpcExperienceEvent() { }

        /// <summary>
        /// Creates a native-outcome record from trusted native adapter code. Structural validation here does not
        /// prove source provenance; callers must not forward model-generated claims through this factory.
        /// </summary>
        public static NpcExperienceEvent FromNativeOutcome(string observerId, string subjectPersonId,
            string receiptId, string episodeId, string outcomeId, NpcExperienceEventKind eventKind, double observedGameTime,
            NpcResponsibleActorAttribution actorAttribution, string responsibleActorId = null,
            NpcExperienceImpactDetails impactDetails = null)
        {
            ValidateCommon(observerId, subjectPersonId, receiptId, episodeId, observedGameTime);
            ValidateIdentity(outcomeId, nameof(outcomeId));
            ValidateCanonicalOccurrenceId(outcomeId, receiptId, episodeId, nameof(outcomeId));
            ValidateEventKind(eventKind);
            ValidateActorAttribution(actorAttribution, responsibleActorId);
            var details = NpcExperienceImpactDetails.CopyValidated(impactDetails);
            if (details != null) details.ValidateEventKind(eventKind);

            var item = new NpcExperienceEvent
            {
                ObserverId = observerId,
                SubjectPersonId = subjectPersonId,
                ReceiptId = receiptId,
                EpisodeId = episodeId,
                OutcomeId = outcomeId,
                EventKind = eventKind,
                Origin = NpcExperienceEventOrigin.NativeOutcome,
                ObservedGameTime = observedGameTime,
                ResponsibleActorAttribution = actorAttribution,
                ResponsibleActorId = responsibleActorId,
                NativeImpactDetails = details
            };
            item.DeduplicationKey = BuildNativeDeduplicationKey(item);
            return item;
        }

        /// <summary>
        /// Creates an observer's record of a speaker's claim. Claimed fields remain attributed speech;
        /// consumers must not treat them as verified world facts or native actor attribution.
        /// </summary>
        public static NpcExperienceEvent FromAttributedSpeech(string observerId, string claimedSubjectPersonId,
            string speechReceiptId, string episodeId, string utteranceId, NpcExperienceEventKind claimedEventKind,
            double observedGameTime, string speakerId, string claimedResponsibleActorId = null)
        {
            return CreateAttributedSpeech(observerId, claimedSubjectPersonId, speechReceiptId, episodeId,
                utteranceId, observedGameTime, speakerId, claimedEventKind, claimedResponsibleActorId);
        }

        /// <summary>
        /// Creates receipt-bound evidence that a speaker said something, without claiming that it proves
        /// any typed world event. The observed utterance must be supplied separately from this identity record.
        /// </summary>
        public static NpcExperienceEvent FromObservedSpeech(string observerId, string subjectPersonId,
            string speechReceiptId, string episodeId, string utteranceId, double observedGameTime, string speakerId)
        {
            return CreateAttributedSpeech(observerId, subjectPersonId, speechReceiptId, episodeId,
                utteranceId, observedGameTime, speakerId, null, null);
        }

        private static NpcExperienceEvent CreateAttributedSpeech(string observerId, string claimedSubjectPersonId,
            string speechReceiptId, string episodeId, string utteranceId, double observedGameTime, string speakerId,
            NpcExperienceEventKind? claimedEventKind, string claimedResponsibleActorId)
        {
            ValidateCommon(observerId, claimedSubjectPersonId, speechReceiptId, episodeId, observedGameTime);
            ValidateIdentity(utteranceId, nameof(utteranceId));
            ValidateCanonicalOccurrenceId(utteranceId, speechReceiptId, episodeId, nameof(utteranceId));
            if (claimedEventKind.HasValue) ValidateEventKind(claimedEventKind.Value);
            ValidateIdentity(speakerId, nameof(speakerId));
            if (claimedEventKind.HasValue && claimedResponsibleActorId != null)
                ValidateIdentity(claimedResponsibleActorId, nameof(claimedResponsibleActorId));
            if (!claimedEventKind.HasValue && claimedResponsibleActorId != null)
                throw new ArgumentException("A speech-only record cannot include a claimed responsible actor.", nameof(claimedResponsibleActorId));

            var item = new NpcExperienceEvent
            {
                ObserverId = observerId,
                SubjectPersonId = claimedSubjectPersonId,
                ReceiptId = speechReceiptId,
                EpisodeId = episodeId,
                UtteranceId = utteranceId,
                ClaimedEventKind = claimedEventKind,
                Origin = NpcExperienceEventOrigin.AttributedSpeech,
                ObservedGameTime = observedGameTime,
                ResponsibleActorAttribution = NpcResponsibleActorAttribution.Unknown,
                AttributedSpeakerId = speakerId,
                ClaimedResponsibleActorId = claimedResponsibleActorId
            };
            item.DeduplicationKey = BuildSpeechDeduplicationKey(item);
            return item;
        }

        public static NpcExperienceEvent CopyValidated(NpcExperienceEvent source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            NpcExperienceEvent copy;
            if (source.Origin == NpcExperienceEventOrigin.NativeOutcome && source.EventKind.HasValue &&
                !source.ClaimedEventKind.HasValue && source.AttributedSpeakerId == null &&
                source.ClaimedResponsibleActorId == null && source.UtteranceId == null)
            {
                copy = FromNativeOutcome(source.ObserverId, source.SubjectPersonId, source.ReceiptId,
                    source.EpisodeId, source.OutcomeId, source.EventKind.Value, source.ObservedGameTime,
                    source.ResponsibleActorAttribution, source.ResponsibleActorId, source.NativeImpactDetails);
            }
            else if (source.Origin == NpcExperienceEventOrigin.AttributedSpeech && !source.EventKind.HasValue &&
                source.ResponsibleActorId == null &&
                (source.ClaimedEventKind.HasValue || source.ClaimedResponsibleActorId == null) &&
                source.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Unknown &&
                source.OutcomeId == null && source.NativeImpactDetails == null)
            {
                copy = source.ClaimedEventKind.HasValue
                    ? FromAttributedSpeech(source.ObserverId, source.SubjectPersonId, source.ReceiptId,
                        source.EpisodeId, source.UtteranceId, source.ClaimedEventKind.Value, source.ObservedGameTime,
                        source.AttributedSpeakerId, source.ClaimedResponsibleActorId)
                    : FromObservedSpeech(source.ObserverId, source.SubjectPersonId, source.ReceiptId,
                        source.EpisodeId, source.UtteranceId, source.ObservedGameTime, source.AttributedSpeakerId);
            }
            else
                throw new ArgumentException("Invalid NPC experience event origin or evidence fields.", nameof(source));

            if (!string.Equals(source.DeduplicationKey, copy.DeduplicationKey, StringComparison.Ordinal))
                throw new ArgumentException("Invalid NPC experience event deduplication key.", nameof(source));
            return copy;
        }

        private static void ValidateCommon(string observerId, string subjectPersonId, string receiptId,
            string episodeId, double observedGameTime)
        {
            ValidateIdentity(observerId, nameof(observerId));
            ValidateIdentity(subjectPersonId, nameof(subjectPersonId));
            ValidateIdentity(receiptId, nameof(receiptId));
            ValidateIdentity(episodeId, nameof(episodeId));
            if (double.IsNaN(observedGameTime) || double.IsInfinity(observedGameTime) || observedGameTime < 0)
                throw new ArgumentOutOfRangeException(nameof(observedGameTime));
        }

        private static void ValidateIdentity(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumIdentityLength)
                throw new ArgumentException("Invalid NPC experience event identity.", parameterName);
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i]))
                    throw new ArgumentException("Invalid NPC experience event identity.", parameterName);
        }

        private static void ValidateEventKind(NpcExperienceEventKind eventKind)
        {
            if (eventKind < NpcExperienceEventKind.MoneyReceived || eventKind > NpcExperienceEventKind.ItemReceived)
                throw new ArgumentOutOfRangeException(nameof(eventKind));
        }

        private static void ValidateCanonicalOccurrenceId(string occurrenceId, string receiptId,
            string episodeId, string parameterName)
        {
            if (string.Equals(occurrenceId, receiptId, StringComparison.Ordinal) ||
                string.Equals(occurrenceId, episodeId, StringComparison.Ordinal))
                throw new ArgumentException("Canonical occurrence identity must differ from receipt and episode identities.",
                    parameterName);
        }

        private static void ValidateActorAttribution(NpcResponsibleActorAttribution actorAttribution,
            string responsibleActorId)
        {
            if (actorAttribution != NpcResponsibleActorAttribution.NotApplicable &&
                actorAttribution != NpcResponsibleActorAttribution.Unknown &&
                actorAttribution != NpcResponsibleActorAttribution.Known)
                throw new ArgumentOutOfRangeException(nameof(actorAttribution));
            if (actorAttribution == NpcResponsibleActorAttribution.Known)
            {
                ValidateIdentity(responsibleActorId, nameof(responsibleActorId));
                return;
            }
            if (responsibleActorId != null)
                throw new ArgumentException("Unknown or inapplicable responsible actor cannot have an identity.",
                    nameof(responsibleActorId));
        }

        private static string BuildNativeDeduplicationKey(NpcExperienceEvent item)
        {
            // Receipt IDs identify adapter records. Canonical outcome IDs identify
            // world outcomes, so separate outcomes in one episode cannot collide.
            return "npc-experience-native-v1|" + Encode(item.EpisodeId) + "|" + Encode(item.OutcomeId) + "|" +
                ((int)item.EventKind.Value).ToString(CultureInfo.InvariantCulture) + "|" + Encode(item.SubjectPersonId) + "|" +
                Encode(item.ObserverId);
        }

        private static string BuildSpeechDeduplicationKey(NpcExperienceEvent item)
        {
            // Speech receipt IDs may be adapter aliases; canonical utterance IDs keep
            // distinct claims in one broader episode from collapsing together.
            return "npc-experience-speech-v1|" + Encode(item.EpisodeId) + "|" + Encode(item.UtteranceId) + "|" +
                (item.ClaimedEventKind.HasValue
                    ? ((int)item.ClaimedEventKind.Value).ToString(CultureInfo.InvariantCulture)
                    : "observed") + "|" +
                Encode(item.SubjectPersonId) + "|" + Encode(item.ObserverId) + "|" + Encode(item.AttributedSpeakerId);
        }

        private static string Encode(string value)
        { return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value; }
    }
}
