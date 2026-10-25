using System;
using System.Collections.Generic;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcExperienceInboxStatus
    {
        Pending = 1,
        Consumed = 2,
        Deferred = 3
    }

    public enum NpcExperienceDeferralReason
    {
        None = 0,
        AppraisalAttemptLimit = 1
    }

    public enum NpcExperienceSourceKind
    {
        LegacyUnknown = 0, Health = 1, Poison = 2, ItemTransfer = 3,
        Appointment = 4, ObservedSpeech = 5
    }

    /// <summary>
    /// Save-owned inbox of observer-bound native outcomes. Enqueue proves that identities are known in the save,
    /// not that an external adapter is truthful or that the event has been authorized for appraisal.
    /// </summary>
    [Serializable]
    public sealed class NpcExperienceInbox
    {
        public const int VersionCurrent = 5;
        public const int MaximumRetainedEntries = 8192;
        public const int MaximumRetainedPerObserver = 256;
        public int Version = VersionCurrent;
        public long Revision;
        public long RetiredCount;
        public long DetailRevision;
        public long LegacyImportedRevision;
        public List<NpcExperienceInboxEntry> Entries = new List<NpcExperienceInboxEntry>();

        public static NpcExperienceInbox Create()
        { return new NpcExperienceInbox(); }

        public static NpcExperienceInbox CopyValidated(NpcExperienceInbox source,
            Func<string, bool> knownNpcIdentity)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Version < 1 || source.Version > VersionCurrent)
                throw new ArgumentOutOfRangeException(nameof(source.Version));
            if (source.Revision < 0 || source.Entries == null ||
                source.Version < 5 && (source.RetiredCount != 0 || source.DetailRevision != 0 ||
                    source.LegacyImportedRevision != 0))
                throw new ArgumentException("Invalid NPC experience inbox header.", nameof(source));

            var copy = new NpcExperienceInbox
            {
                Version = VersionCurrent,
                Revision = source.Revision,
                RetiredCount = source.RetiredCount,
                DetailRevision = source.DetailRevision,
                LegacyImportedRevision = source.LegacyImportedRevision,
                Entries = new List<NpcExperienceInboxEntry>(source.Entries.Count)
            };
            var dedupe = new HashSet<string>(StringComparer.Ordinal);
            var perObserver = new Dictionary<string, int>(StringComparer.Ordinal);
            long previousOrdinal = 0;
            foreach (var raw in source.Entries)
            {
                if (source.Version < 5 && raw != null &&
                    (raw.EnqueueOrdinal != 0 || raw.SourceVitalsRevision != 0 ||
                     raw.SourceKind != NpcExperienceSourceKind.LegacyUnknown))
                    throw new ArgumentException("Legacy inbox contains a future enqueue ordinal.", nameof(source));
                if (source.Version == 1 && raw != null && raw.Status == NpcExperienceInboxStatus.Deferred)
                    throw new ArgumentException("Version one inbox cannot contain deferred rows.", nameof(source));
                if (source.Version < 3 && raw != null &&
                    (raw.StatusKind != 0 || raw.StatusNativeEffectKey != null || raw.StatusOnsetSequence != 0))
                    throw new ArgumentException("Legacy inbox cannot contain typed status fields.", nameof(source));
                if (source.Version < 4 && raw != null &&
                    (raw.HasKnownImmediateAffinityDelta || raw.ImmediateAffinityDelta != 0 ||
                     raw.AffinityExclusion != NpcObservedSpeechAffinityExclusion.None))
                    throw new ArgumentException("Legacy inbox cannot contain native speech affinity provenance.", nameof(source));
                var entry = NpcExperienceInboxEntry.CopyValidated(raw);
                if (source.Version < 5) entry.EnqueueOrdinal = copy.Entries.Count + 1;
                if (entry.EnqueueOrdinal <= previousOrdinal || entry.EnqueueOrdinal > source.Revision)
                    throw new ArgumentException("Invalid retained enqueue ordinal.", nameof(source));
                previousOrdinal = entry.EnqueueOrdinal;
                ValidateKnownIdentity(entry.ObserverId, knownNpcIdentity);
                ValidateKnownTarget(entry.SubjectPersonId, knownNpcIdentity);
                if (entry.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Known)
                    ValidateKnownTarget(entry.ResponsibleActorId, knownNpcIdentity);
                if (!dedupe.Add(ObserverOutcomeKey(entry.ObserverId, entry.DeduplicationKey)))
                    throw new ArgumentException("Duplicate observer outcome in NPC experience inbox.", nameof(source));
                perObserver.TryGetValue(entry.ObserverId, out int ownerCount);
                perObserver[entry.ObserverId] = ownerCount + 1;
                copy.Entries.Add(entry);
            }
            if (source.Version < 5)
            {
                if (source.Revision != source.Entries.Count)
                    throw new ArgumentException("Legacy inbox revision must match canonical event history.", nameof(source));
                copy.RetiredCount = 0;
                copy.DetailRevision = 0;
                copy.LegacyImportedRevision = source.Revision;
            }
            else if (source.RetiredCount < 0 || source.DetailRevision < 0 ||
                source.Revision - source.RetiredCount != source.Entries.Count ||
                source.LegacyImportedRevision < 0 || source.LegacyImportedRevision > source.Revision ||
                (source.Entries.Count > MaximumRetainedEntries ||
                 perObserver.Values.Any(count => count > MaximumRetainedPerObserver)) &&
                    (source.LegacyImportedRevision == 0 || source.LegacyImportedRevision != source.Revision ||
                     source.Entries.Exists(x => x.EnqueueOrdinal > source.LegacyImportedRevision)))
                throw new ArgumentException("Invalid inbox retirement or capacity accounting.", nameof(source));
            return copy;
        }

        public List<NpcExperienceEvent> ReadPending(string observerId, int limit)
        {
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit));
            if (string.IsNullOrWhiteSpace(observerId)) throw new ArgumentException("Observer identity is required.", nameof(observerId));
            var result = new List<NpcExperienceEvent>();
            if (Entries == null) return result;
            var ordered = new List<NpcExperienceInboxEntry>(Entries);
            ordered.Sort((a,b) => { int byTime = a.ObservedGameTime.CompareTo(b.ObservedGameTime);
                return byTime != 0 ? byTime : StringComparer.Ordinal.Compare(a.DeduplicationKey,b.DeduplicationKey); });
            foreach (var entry in ordered)
            {
                if (entry.Status != NpcExperienceInboxStatus.Pending ||
                    !string.Equals(entry.ObserverId, observerId, StringComparison.Ordinal)) continue;
                result.Add(entry.ToEvent());
                if (result.Count == limit) break;
            }
            return result;
        }

        public bool HasOutcome(string observerId, string deduplicationKey)
        {
            if (Entries == null) return false;
            foreach (var entry in Entries)
                if (string.Equals(entry.ObserverId, observerId, StringComparison.Ordinal) &&
                    string.Equals(entry.DeduplicationKey, deduplicationKey, StringComparison.Ordinal)) return true;
            return false;
        }

        internal static bool TryAppendValidated(NpcExperienceInbox current,
            NpcExperienceInboxEntry entry, Func<string, bool> knownNpcIdentity,
            out NpcExperienceInbox prepared)
        {
            prepared = null;
            if (current == null || entry == null || current.Revision == long.MaxValue ||
                current.DetailRevision == long.MaxValue ||
                current.Entries.Count >= MaximumRetainedEntries ||
                current.Entries.Count(row => row.ObserverId == entry.ObserverId) >= MaximumRetainedPerObserver ||
                current.HasOutcome(entry.ObserverId, entry.DeduplicationKey)) return false;
            var rows = new List<NpcExperienceInboxEntry>(current.Entries);
            var appended = NpcExperienceInboxEntry.CopyValidated(entry);
            appended.EnqueueOrdinal = current.Revision + 1;
            if (appended.Origin == NpcExperienceEventOrigin.AttributedSpeech)
                appended.SourceKind = NpcExperienceSourceKind.ObservedSpeech;
            rows.Add(appended);
            try
            {
                prepared = CopyValidated(new NpcExperienceInbox {
                    Version = VersionCurrent, Revision = current.Revision + 1,
                    RetiredCount = current.RetiredCount, DetailRevision = current.DetailRevision + 1,
                    LegacyImportedRevision = current.LegacyImportedRevision,
                    Entries = rows
                }, knownNpcIdentity);
                return true;
            }
            catch (ArgumentException) { prepared = null; return false; }
        }

        private static void ValidateKnownIdentity(string id, Func<string, bool> knownNpcIdentity)
        {
            ValidateIdentity(id);
            if (knownNpcIdentity == null || !knownNpcIdentity(id))
                throw new ArgumentException("NPC experience observer is not known to the current world.", nameof(id));
        }

        private static void ValidateKnownTarget(string id, Func<string, bool> knownNpcIdentity)
        {
            ValidateIdentity(id);
            if (!string.Equals(id, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal))
                ValidateKnownIdentity(id, knownNpcIdentity);
        }

        private static void ValidateIdentity(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 240)
                throw new ArgumentException("Invalid NPC experience inbox identity.", nameof(id));
            for (int i = 0; i < id.Length; i++)
                if (char.IsControl(id[i])) throw new ArgumentException("Invalid NPC experience inbox identity.", nameof(id));
        }

        private static string ObserverOutcomeKey(string observerId, string deduplicationKey)
        { return observerId.Length + ":" + observerId + deduplicationKey.Length + ":" + deduplicationKey; }
    }

    /// <summary>Flat serializable record; it avoids persisting immutable event properties through Unity serializers.</summary>
    [Serializable]
    public sealed class NpcExperienceInboxEntry
    {
        public NpcExperienceEventOrigin Origin = NpcExperienceEventOrigin.NativeOutcome;
        public string ObserverId;
        public string SubjectPersonId;
        public string ReceiptId;
        public string EpisodeId;
        public string OutcomeId;
        public string UtteranceId;
        public string AttributedSpeakerId;
        public NpcExperienceEventKind EventKind;
        public double ObservedGameTime;
        public long Revision = 1;
        public long EnqueueOrdinal;
        public long SourceVitalsRevision;
        public NpcExperienceSourceKind SourceKind;
        public double UpdatedAt;
        public NpcResponsibleActorAttribution ResponsibleActorAttribution;
        public string ResponsibleActorId;
        public string DeduplicationKey;
        public NpcExperienceAppliedEffectsMask AlreadyAppliedEffectsMask;
        public bool HasKnownImmediateAffinityDelta;
        public int ImmediateAffinityDelta;
        public NpcObservedSpeechAffinityExclusion AffinityExclusion;
        public int ImpactDetailsKind;
        public int HealthInitial;
        public int HealthFinal;
        public int HealthMaximum;
        public long HealthDamageTotal;
        public long HealthHealingTotal;
        public int HealthChangeCount;
        public long TransferQuantity;
        public int TransferCurrency;
        public int NativeValuationStatus;
        public long NativeGoldValue;
        public ulong ReceivedItemUid;
        public int StatusKind;
        public string StatusNativeEffectKey;
        public long StatusOnsetSequence;
        public string ObservedTurnObserverId;
        public string ObservedTurnEpisodeId;
        public string ObservedTurnSpeakerId;
        public string ObservedTurnReceiptId;
        public string ObservedTurnText;
        public double ObservedTurnGameTime;
        public NpcExperienceInboxStatus Status = NpcExperienceInboxStatus.Pending;
        public NpcExperienceDeferralReason DeferralReason;
        public int DeferralAttempts;
        public NpcExperienceAppliedAppraisal Appraisal;

        public static NpcExperienceInboxEntry FromNativeEvent(NpcExperienceEvent source)
        {
            var evidence = NpcExperienceEvent.CopyValidated(source);
            if (evidence.Origin != NpcExperienceEventOrigin.NativeOutcome || !evidence.EventKind.HasValue)
                throw new ArgumentException("Only native outcomes enter the native experience inbox.", nameof(source));
            return CopyValidated(new NpcExperienceInboxEntry
            {
                ObserverId = evidence.ObserverId,
                Origin = NpcExperienceEventOrigin.NativeOutcome,
                SubjectPersonId = evidence.SubjectPersonId,
                ReceiptId = evidence.ReceiptId,
                EpisodeId = evidence.EpisodeId,
                OutcomeId = evidence.OutcomeId,
                EventKind = evidence.EventKind.Value,
                ObservedGameTime = evidence.ObservedGameTime,
                Revision = 1,
                UpdatedAt = evidence.ObservedGameTime,
                ResponsibleActorAttribution = evidence.ResponsibleActorAttribution,
                ResponsibleActorId = evidence.ResponsibleActorId,
                DeduplicationKey = evidence.DeduplicationKey,
                ImpactDetailsKind = evidence.NativeImpactDetails == null ? 0 : (int)evidence.NativeImpactDetails.Kind,
                HealthInitial = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.InitialHealth,
                HealthFinal = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.FinalHealth,
                HealthMaximum = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.MaximumHealth,
                HealthDamageTotal = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.TotalDamage,
                HealthHealingTotal = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.TotalHealing,
                HealthChangeCount = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.ChangeCount,
                TransferQuantity = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.Quantity,
                TransferCurrency = evidence.NativeImpactDetails == null ? 0 : (int)evidence.NativeImpactDetails.Currency,
                NativeValuationStatus = evidence.NativeImpactDetails == null ? 0 : (int)evidence.NativeImpactDetails.NativeValuationStatus,
                NativeGoldValue = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.NativeGoldValue,
                ReceivedItemUid = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.RetainedDestinationItemUid,
                StatusKind = evidence.NativeImpactDetails == null ? 0 : (int)evidence.NativeImpactDetails.Status,
                StatusNativeEffectKey = evidence.NativeImpactDetails == null ? null : evidence.NativeImpactDetails.NativeEffectKey,
                StatusOnsetSequence = evidence.NativeImpactDetails == null ? 0 : evidence.NativeImpactDetails.StatusOnsetSequence,
                Status = NpcExperienceInboxStatus.Pending
            });
        }

        public static NpcExperienceInboxEntry FromObservedSpeech(NpcExperienceEvent source,
            NpcObservedConversationTurn observedTurn, NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask)
        {
            return FromObservedSpeech(NpcObservedSpeechReceipt.Create(source, observedTurn), alreadyAppliedEffectsMask);
        }

        public static NpcExperienceInboxEntry FromObservedSpeech(NpcObservedSpeechReceipt source,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask)
        {
            var receipt = NpcObservedSpeechReceipt.CopyValidated(source);
            var evidence = receipt.Evidence;
            var observedTurn = receipt.Turn;
            if (evidence.Origin != NpcExperienceEventOrigin.AttributedSpeech || evidence.ClaimedEventKind.HasValue ||
                evidence.ClaimedResponsibleActorId != null || observedTurn == null)
                throw new ArgumentException("Only receipt-bound observed speech without claimed facts may enter this queue.", nameof(source));
            const NpcExperienceAppliedEffectsMask allowedMask = NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity;
            bool hasAffinityProvenance = receipt.ImmediateAffinityDelta.HasValue ||
                receipt.AffinityExclusion != NpcObservedSpeechAffinityExclusion.None;
            if ((alreadyAppliedEffectsMask & ~allowedMask) != 0 || hasAffinityProvenance &&
                (alreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) == 0 ||
                receipt.AffinityExclusion == NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor &&
                !receipt.ImmediateAffinityDelta.HasValue)
                throw new ArgumentException("Native affinity provenance requires a known applied affinity effect.", nameof(source));
            return CopyValidated(new NpcExperienceInboxEntry
            {
                Origin = NpcExperienceEventOrigin.AttributedSpeech,
                ObserverId = evidence.ObserverId,
                SubjectPersonId = evidence.SubjectPersonId,
                ReceiptId = evidence.ReceiptId,
                EpisodeId = evidence.EpisodeId,
                UtteranceId = evidence.UtteranceId,
                AttributedSpeakerId = evidence.AttributedSpeakerId,
                ObservedGameTime = evidence.ObservedGameTime,
                Revision = 1,
                UpdatedAt = evidence.ObservedGameTime,
                ResponsibleActorAttribution = NpcResponsibleActorAttribution.Unknown,
                DeduplicationKey = evidence.DeduplicationKey,
                AlreadyAppliedEffectsMask = alreadyAppliedEffectsMask,
                HasKnownImmediateAffinityDelta = receipt.ImmediateAffinityDelta.HasValue,
                ImmediateAffinityDelta = receipt.ImmediateAffinityDelta ?? 0,
                AffinityExclusion = receipt.AffinityExclusion,
                ObservedTurnObserverId = observedTurn.ObserverId,
                ObservedTurnEpisodeId = observedTurn.EpisodeId,
                ObservedTurnSpeakerId = observedTurn.SpeakerId,
                ObservedTurnReceiptId = observedTurn.ReceiptId,
                ObservedTurnText = observedTurn.Text,
                ObservedTurnGameTime = observedTurn.ObservedGameTime,
                Status = NpcExperienceInboxStatus.Pending
            });
        }

        public static NpcExperienceInboxEntry CopyValidated(NpcExperienceInboxEntry source)
        {
            if (source == null) throw new ArgumentException("Null NPC experience inbox entry.", nameof(source));
            if (source.Status != NpcExperienceInboxStatus.Pending && source.Status != NpcExperienceInboxStatus.Consumed &&
                source.Status != NpcExperienceInboxStatus.Deferred)
                throw new ArgumentOutOfRangeException(nameof(source.Status));
            if (source.Status == NpcExperienceInboxStatus.Pending && source.Appraisal != null ||
                source.Status == NpcExperienceInboxStatus.Consumed && source.Appraisal == null ||
                source.Status == NpcExperienceInboxStatus.Deferred && source.Appraisal != null)
                throw new ArgumentException("Inbox state must match its applied appraisal journal.", nameof(source));
            if (source.Revision < 1 || source.Revision > 2 ||
                double.IsNaN(source.UpdatedAt) || double.IsInfinity(source.UpdatedAt) ||
                source.UpdatedAt < source.ObservedGameTime ||
                source.Status == NpcExperienceInboxStatus.Pending &&
                    (source.Revision != 1 || source.UpdatedAt != source.ObservedGameTime) ||
                source.Status != NpcExperienceInboxStatus.Pending && source.Revision != 2 ||
                source.Status == NpcExperienceInboxStatus.Deferred &&
                    (source.DeferralReason != NpcExperienceDeferralReason.AppraisalAttemptLimit || source.DeferralAttempts != 5) ||
                source.Status != NpcExperienceInboxStatus.Deferred &&
                    (source.DeferralReason != NpcExperienceDeferralReason.None || source.DeferralAttempts != 0))
                throw new ArgumentException("Invalid inbox entry revision or transition timestamp.", nameof(source));
            if (source.Status == NpcExperienceInboxStatus.Consumed && source.Appraisal != null &&
                source.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                (source.Appraisal.AffinityDelta < -10 || source.Appraisal.TrustDelta < -10 ||
                 source.Appraisal.TrustDelta > 10 || source.Appraisal.ResentmentDelta < -10 ||
                 source.Appraisal.ResentmentDelta > 10 || source.Appraisal.FearDelta < -10 ||
                 source.Appraisal.FearDelta > 10))
                throw new ArgumentException("Native outcome appraisal exceeds native bounds.", nameof(source));
            const NpcExperienceAppliedEffectsMask allowedMask = NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity;
            if ((source.AlreadyAppliedEffectsMask & ~allowedMask) != 0 ||
                source.Status == NpcExperienceInboxStatus.Consumed && source.Appraisal != null &&
                (((source.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Mood) != 0 &&
                    (source.Appraisal.MoodDelta != 0 || source.Appraisal.AppliedMoodDelta != 0)) ||
                 ((source.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) != 0 &&
                    (source.Appraisal.AffinityDelta > 0 || source.Appraisal.AppliedAffinityDelta > 0 ||
                     source.Appraisal.AffinityDelta != 0 &&
                        (source.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                         source.Appraisal.Version != 2 || source.Appraisal.AffinityDelta >= 0 ||
                         source.Appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectInsult &&
                         source.Appraisal.SpeechActKind != NpcExperienceSpeechActKind.DirectThreat ||
                          !NpcExperienceAppraisalContract.ValidIdentity(source.Appraisal.SpeechTargetPersonId, 240) ||
                         source.AffinityExclusion != NpcObservedSpeechAffinityExclusion.None)))))
                throw new ArgumentException("Invalid applied-effects mask or consumed appraisal.", nameof(source));
            if (source.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                (source.HasKnownImmediateAffinityDelta || source.ImmediateAffinityDelta != 0 ||
                 source.AffinityExclusion != NpcObservedSpeechAffinityExclusion.None))
                throw new ArgumentException("Native outcome row contains speech affinity provenance.", nameof(source));
            if (!Enum.IsDefined(typeof(NpcObservedSpeechAffinityExclusion), source.AffinityExclusion) ||
                source.HasKnownImmediateAffinityDelta &&
                    (source.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) == 0 ||
                source.HasKnownImmediateAffinityDelta && (source.ImmediateAffinityDelta < -NpcObservedSpeechReceipt.NativeAffinityDeltaBound ||
                    source.ImmediateAffinityDelta > NpcObservedSpeechReceipt.NativeAffinityDeltaBound) ||
                !source.HasKnownImmediateAffinityDelta && source.ImmediateAffinityDelta != 0 ||
                source.AffinityExclusion != NpcObservedSpeechAffinityExclusion.None &&
                    ((source.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) == 0 ||
                     !source.HasKnownImmediateAffinityDelta))
                throw new ArgumentException("Invalid native speech affinity provenance.", nameof(source));
            var impactDetails = ReadImpactDetails(source);
            NpcExperienceEvent reconstructed;
            if (source.Origin == NpcExperienceEventOrigin.NativeOutcome)
            {
                if (source.AlreadyAppliedEffectsMask != NpcExperienceAppliedEffectsMask.None || source.UtteranceId != null ||
                    source.AttributedSpeakerId != null || source.ObservedTurnText != null || source.ObservedTurnObserverId != null ||
                    source.ObservedTurnEpisodeId != null || source.ObservedTurnSpeakerId != null || source.ObservedTurnReceiptId != null ||
                    source.ObservedTurnGameTime != 0)
                    throw new ArgumentException("Native inbox row contains speech-only fields.", nameof(source));
                if (impactDetails != null && impactDetails.Kind == NpcExperienceImpactKind.StatusOnset &&
                    (source.SubjectPersonId != source.ObserverId ||
                     source.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Unknown || source.ResponsibleActorId != null))
                    throw new ArgumentException("Status onset must be observer-bound and cannot identify a responsible actor.", nameof(source));
                reconstructed = NpcExperienceEvent.FromNativeOutcome(source.ObserverId, source.SubjectPersonId,
                    source.ReceiptId, source.EpisodeId, source.OutcomeId, source.EventKind, source.ObservedGameTime,
                    source.ResponsibleActorAttribution, source.ResponsibleActorId, impactDetails);
            }
            else if (source.Origin == NpcExperienceEventOrigin.AttributedSpeech)
            {
                if (source.EventKind != 0 || source.OutcomeId != null || source.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Unknown ||
                    source.ResponsibleActorId != null || source.AlreadyAppliedEffectsMask == NpcExperienceAppliedEffectsMask.None &&
                    (source.ObservedTurnText == null && source.ObservedTurnObserverId == null) || impactDetails != null)
                    throw new ArgumentException("Invalid observed-speech inbox row.", nameof(source));
                if (source.HasKnownImmediateAffinityDelta &&
                    (source.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) == 0)
                    throw new ArgumentException("Observed-speech affinity delta lacks its applied-effects marker.", nameof(source));
                reconstructed = NpcExperienceEvent.FromObservedSpeech(source.ObserverId, source.SubjectPersonId,
                    source.ReceiptId, source.EpisodeId, source.UtteranceId, source.ObservedGameTime, source.AttributedSpeakerId);
                var turn = ToObservedTurnRaw(source);
                if (turn.ObserverId != source.ObserverId || turn.EpisodeId != source.EpisodeId ||
                    turn.SpeakerId != source.AttributedSpeakerId || turn.ReceiptId != source.ReceiptId ||
                    turn.ObservedGameTime != source.ObservedGameTime)
                    throw new ArgumentException("Observed turn does not match speech evidence.", nameof(source));
            }
            else throw new ArgumentOutOfRangeException(nameof(source.Origin));
            if (!string.Equals(reconstructed.DeduplicationKey, source.DeduplicationKey, StringComparison.Ordinal))
                throw new ArgumentException("NPC experience inbox canonical key mismatch.", nameof(source));
            if (source.SourceVitalsRevision < 0 || source.SourceVitalsRevision != 0 &&
                (source.Origin != NpcExperienceEventOrigin.NativeOutcome ||
                 source.EventKind != NpcExperienceEventKind.Injury &&
                 source.EventKind != NpcExperienceEventKind.Healing))
                throw new ArgumentException("Invalid health source revision on inbox detail.", nameof(source));
            if (!Enum.IsDefined(typeof(NpcExperienceSourceKind), source.SourceKind) ||
                source.SourceKind == NpcExperienceSourceKind.ObservedSpeech &&
                    source.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                source.SourceKind != NpcExperienceSourceKind.ObservedSpeech &&
                    source.SourceKind != NpcExperienceSourceKind.LegacyUnknown &&
                    source.Origin != NpcExperienceEventOrigin.NativeOutcome ||
                source.SourceKind == NpcExperienceSourceKind.Health &&
                    source.EventKind != NpcExperienceEventKind.Injury &&
                    source.EventKind != NpcExperienceEventKind.Healing ||
                source.SourceKind == NpcExperienceSourceKind.Poison &&
                    source.EventKind != NpcExperienceEventKind.StatusOnset ||
                source.SourceKind == NpcExperienceSourceKind.ItemTransfer &&
                    source.EventKind != NpcExperienceEventKind.ItemReceived ||
                source.SourceKind == NpcExperienceSourceKind.Appointment &&
                    source.EventKind != NpcExperienceEventKind.AppointmentAttended &&
                    source.EventKind != NpcExperienceEventKind.AppointmentNoShow)
                throw new ArgumentException("Invalid experience source classification.", nameof(source));
            return new NpcExperienceInboxEntry
            {
                ObserverId = source.ObserverId,
                Origin = source.Origin,
                SubjectPersonId = source.SubjectPersonId,
                ReceiptId = source.ReceiptId,
                EpisodeId = source.EpisodeId,
                OutcomeId = source.OutcomeId,
                UtteranceId = source.UtteranceId,
                AttributedSpeakerId = source.AttributedSpeakerId,
                EventKind = source.EventKind,
                ObservedGameTime = source.ObservedGameTime,
                Revision = source.Revision,
                EnqueueOrdinal = source.EnqueueOrdinal,
                SourceVitalsRevision = source.SourceVitalsRevision,
                SourceKind = source.SourceKind,
                UpdatedAt = source.UpdatedAt,
                ResponsibleActorAttribution = source.ResponsibleActorAttribution,
                ResponsibleActorId = source.ResponsibleActorId,
                DeduplicationKey = reconstructed.DeduplicationKey,
                AlreadyAppliedEffectsMask = source.AlreadyAppliedEffectsMask,
                HasKnownImmediateAffinityDelta = source.HasKnownImmediateAffinityDelta,
                ImmediateAffinityDelta = source.ImmediateAffinityDelta,
                AffinityExclusion = source.AffinityExclusion,
                ImpactDetailsKind = source.ImpactDetailsKind,
                HealthInitial = source.HealthInitial,
                HealthFinal = source.HealthFinal,
                HealthMaximum = source.HealthMaximum,
                HealthDamageTotal = source.HealthDamageTotal,
                HealthHealingTotal = source.HealthHealingTotal,
                HealthChangeCount = source.HealthChangeCount,
                TransferQuantity = source.TransferQuantity,
                TransferCurrency = source.TransferCurrency,
                NativeValuationStatus = source.NativeValuationStatus,
                NativeGoldValue = source.NativeGoldValue,
                ReceivedItemUid = source.ReceivedItemUid,
                StatusKind = source.StatusKind,
                StatusNativeEffectKey = source.StatusNativeEffectKey,
                StatusOnsetSequence = source.StatusOnsetSequence,
                ObservedTurnObserverId = source.ObservedTurnObserverId,
                ObservedTurnEpisodeId = source.ObservedTurnEpisodeId,
                ObservedTurnSpeakerId = source.ObservedTurnSpeakerId,
                ObservedTurnReceiptId = source.ObservedTurnReceiptId,
                ObservedTurnText = source.ObservedTurnText,
                ObservedTurnGameTime = source.ObservedTurnGameTime,
                Status = source.Status,
                DeferralReason = source.DeferralReason,
                DeferralAttempts = source.DeferralAttempts,
                Appraisal = source.Appraisal == null ? null : NpcExperienceAppliedAppraisal.CopyValidated(source.Appraisal)
            };
        }

        public NpcExperienceEvent ToEvent()
        {
            var copy = CopyValidated(this);
            return copy.Origin == NpcExperienceEventOrigin.NativeOutcome
                ? NpcExperienceEvent.FromNativeOutcome(copy.ObserverId, copy.SubjectPersonId,
                    copy.ReceiptId, copy.EpisodeId, copy.OutcomeId, copy.EventKind, copy.ObservedGameTime,
                    copy.ResponsibleActorAttribution, copy.ResponsibleActorId, ReadImpactDetails(copy))
                : NpcExperienceEvent.FromObservedSpeech(copy.ObserverId, copy.SubjectPersonId,
                    copy.ReceiptId, copy.EpisodeId, copy.UtteranceId, copy.ObservedGameTime, copy.AttributedSpeakerId);
        }

        public NpcObservedConversationTurn ToObservedTurn()
        { return CopyValidated(this).Origin == NpcExperienceEventOrigin.AttributedSpeech ? ToObservedTurnRaw(this) : null; }

        private static NpcObservedConversationTurn ToObservedTurnRaw(NpcExperienceInboxEntry source)
        { return NpcObservedConversationTurn.Create(source.ObservedTurnObserverId, source.ObservedTurnEpisodeId,
            source.ObservedTurnSpeakerId, source.ObservedTurnReceiptId, source.ObservedTurnText, source.ObservedTurnGameTime); }

        private static NpcExperienceImpactDetails ReadImpactDetails(NpcExperienceInboxEntry source)
        {
            if (source.ImpactDetailsKind == 0)
            {
                if (source.HealthInitial != 0 || source.HealthFinal != 0 || source.HealthMaximum != 0 ||
                    source.HealthDamageTotal != 0 || source.HealthHealingTotal != 0 || source.HealthChangeCount != 0 ||
                    source.TransferQuantity != 0 || source.TransferCurrency != 0 || source.NativeValuationStatus != 0 ||
                    source.NativeGoldValue != 0 || source.ReceivedItemUid != 0 || source.StatusKind != 0 ||
                    source.StatusNativeEffectKey != null || source.StatusOnsetSequence != 0)
                    throw new ArgumentException("Impact detail scalars lack a detail kind.", nameof(source));
                return null;
            }
            var kind = (NpcExperienceImpactKind)source.ImpactDetailsKind;
            switch (kind)
            {
                case NpcExperienceImpactKind.Health:
                    if (source.TransferQuantity != 0 || source.TransferCurrency != 0 || source.NativeValuationStatus != 0 || source.NativeGoldValue != 0 || source.ReceivedItemUid != 0 ||
                        source.StatusKind != 0 || source.StatusNativeEffectKey != null || source.StatusOnsetSequence != 0)
                        throw new ArgumentException("Health impact save contains transfer-only fields.", nameof(source));
                    return NpcExperienceImpactDetails.ForHealthEpisode(source.HealthInitial, source.HealthFinal,
                        source.HealthMaximum, source.HealthDamageTotal, source.HealthHealingTotal, source.HealthChangeCount);
                case NpcExperienceImpactKind.MonetaryTransfer:
                    if (source.HealthInitial != 0 || source.HealthFinal != 0 || source.HealthMaximum != 0 ||
                        source.HealthDamageTotal != 0 || source.HealthHealingTotal != 0 || source.HealthChangeCount != 0 ||
                        source.TransferCurrency != (int)NpcExperienceCurrency.Gold ||
                        source.NativeValuationStatus != (int)NpcExperienceNativeValuationStatus.NotApplicable || source.NativeGoldValue != 0 || source.ReceivedItemUid != 0 ||
                        source.StatusKind != 0 || source.StatusNativeEffectKey != null || source.StatusOnsetSequence != 0)
                        throw new ArgumentException("Monetary impact save contains health-only fields.", nameof(source));
                    return NpcExperienceImpactDetails.ForGoldTransfer(source.TransferQuantity);
                case NpcExperienceImpactKind.ItemTransfer:
                    if (source.HealthInitial != 0 || source.HealthFinal != 0 || source.HealthMaximum != 0 ||
                        source.HealthDamageTotal != 0 || source.HealthHealingTotal != 0 || source.HealthChangeCount != 0 ||
                        source.TransferCurrency != (int)NpcExperienceCurrency.None || source.StatusKind != 0 ||
                        source.StatusNativeEffectKey != null || source.StatusOnsetSequence != 0)
                        throw new ArgumentException("Item impact save contains health-only fields.", nameof(source));
                    return source.EventKind == NpcExperienceEventKind.ItemReceived
                        ? NpcExperienceImpactDetails.ForReceivedItemTransfer(source.ReceivedItemUid, source.TransferQuantity,
                            (NpcExperienceNativeValuationStatus)source.NativeValuationStatus, source.NativeGoldValue)
                        : source.ReceivedItemUid == 0
                            ? NpcExperienceImpactDetails.ForItemTransfer(source.TransferQuantity,
                                (NpcExperienceNativeValuationStatus)source.NativeValuationStatus, source.NativeGoldValue)
                            : throw new ArgumentException("Legacy gift impact cannot name a retained item UID.", nameof(source));
                case NpcExperienceImpactKind.StatusOnset:
                    if (source.HealthInitial != 0 || source.HealthFinal != 0 || source.HealthMaximum != 0 ||
                        source.HealthDamageTotal != 0 || source.HealthHealingTotal != 0 || source.HealthChangeCount != 0 ||
                        source.TransferQuantity != 0 || source.TransferCurrency != 0 || source.NativeValuationStatus != 0 ||
                        source.NativeGoldValue != 0 || source.ReceivedItemUid != 0)
                        throw new ArgumentException("Status impact save contains health- or transfer-only fields.", nameof(source));
                    return NpcExperienceImpactDetails.ForStatusOnset((NpcExperienceStatusKind)source.StatusKind,
                        source.StatusNativeEffectKey, source.StatusOnsetSequence);
                default:
                    throw new ArgumentOutOfRangeException(nameof(source.ImpactDetailsKind));
            }
        }
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Compatibility boundary: caller-constructed native outcomes are not authoritative.
        /// Native producers project confirmed stored results through the private admission path.
        /// </summary>
        public bool TryEnqueueNpcExperience(NpcExperienceEvent nativeEvent)
        {
            return false;
        }

        private bool TryEnqueueVerifiedNativeExperience(NpcExperienceEvent nativeEvent)
        {
            NpcExperienceInbox prepared;
            if (!TryPrepareVerifiedNativeExperience(nativeEvent, out prepared)) return false;
            state.NpcExperienceInbox = prepared;
            RememberVerifiedExperience(nativeEvent);
            return true;
        }

        private bool TryEnqueueVerifiedNativeExperience(NpcExperienceEvent nativeEvent,
            long sourceVitalsRevision)
        {
            if (sourceVitalsRevision < 1) return false;
            NpcExperienceInbox prepared;
            if (!TryPrepareVerifiedNativeExperience(nativeEvent, out prepared)) return false;
            var appended = prepared.Entries[prepared.Entries.Count - 1];
            appended.SourceVitalsRevision = sourceVitalsRevision;
            appended.SourceKind = NpcExperienceSourceKind.Health;
            try { prepared = NpcExperienceInbox.CopyValidated(prepared, IsKnownNpcExperienceIdentity); }
            catch (ArgumentException) { return false; }
            state.NpcExperienceInbox = prepared;
            RememberVerifiedExperience(nativeEvent);
            return true;
        }

        private bool TryPrepareVerifiedNativeExperience(NpcExperienceEvent nativeEvent,
            out NpcExperienceInbox prepared)
        {
            prepared = null;
            if (nativeEvent == null) return false;
            NpcExperienceInbox current;
            NpcExperienceEvent evidence;
            NpcExperienceInboxEntry entry;
            try
            {
                current = NpcExperienceInbox.CopyValidated(state.NpcExperienceInbox,
                    IsKnownNpcExperienceIdentity);
                evidence = NpcExperienceEvent.CopyValidated(nativeEvent);
                if (evidence.Origin != NpcExperienceEventOrigin.NativeOutcome || !evidence.EventKind.HasValue)
                    return false;
                if (evidence.EventKind == NpcExperienceEventKind.ItemReceived &&
                    (evidence.NativeImpactDetails == null || evidence.NativeImpactDetails.Kind != NpcExperienceImpactKind.ItemTransfer ||
                     evidence.NativeImpactDetails.RetainedDestinationItemUid == 0)) return false;
                ValidateKnownExperienceIdentity(evidence.ObserverId);
                ValidateKnownExperienceTarget(evidence.SubjectPersonId);
                if (evidence.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Known)
                    ValidateKnownExperienceTarget(evidence.ResponsibleActorId);
                entry = NpcExperienceInboxEntry.FromNativeEvent(evidence);
                switch (evidence.EventKind.Value)
                {
                    case NpcExperienceEventKind.StatusOnset:
                        entry.SourceKind = NpcExperienceSourceKind.Poison; break;
                    case NpcExperienceEventKind.ItemReceived:
                        entry.SourceKind = NpcExperienceSourceKind.ItemTransfer; break;
                    case NpcExperienceEventKind.AppointmentAttended:
                    case NpcExperienceEventKind.AppointmentNoShow:
                        entry.SourceKind = NpcExperienceSourceKind.Appointment; break;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }

            return NpcExperienceInbox.TryAppendValidated(current, entry,
                IsKnownNpcExperienceIdentity, out prepared);
        }

        /// <summary>Compatibility boundary: caller-created speech evidence is not source authority.</summary>
        public bool TryEnqueueNpcObservedSpeech(NpcExperienceEvent evidence, NpcObservedConversationTurn observedTurn,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask)
        {
            return false;
        }

        /// <summary>Compatibility boundary: receipt-only speech admission cannot prove an issued source.</summary>
        public bool TryEnqueueNpcObservedSpeech(NpcObservedSpeechReceipt observedSpeech,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask)
        {
            return false;
        }

        /// <summary>Returns at most 100 detached native events for one known observer.</summary>
        public List<NpcExperienceEvent> ReadPendingNpcExperiences(string observerId, int limit)
        {
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit));
            if (!IsKnownNpcExperienceIdentity(observerId)) return new List<NpcExperienceEvent>();
            return state.NpcExperienceInbox.ReadPending(observerId, limit);
        }

        private bool IsKnownNpcExperienceIdentity(string id)
        {
            bool alive;
            return KnownNpcIdentity(state, id, out alive);
        }

        private void ValidateKnownExperienceIdentity(string id)
        {
            if (!IsKnownNpcExperienceIdentity(id))
                throw new ArgumentException("NPC experience observer is unknown to this save.", nameof(id));
        }

        private void ValidateKnownExperienceTarget(string id)
        {
            if (string.Equals(id, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal)) return;
            ValidateKnownExperienceIdentity(id);
        }

        private static NpcExperienceInbox CopyNpcExperienceInbox(WorldState source, WorldState validated)
        {
            if (source.Version < 35)
            {
                // Old serializers may supply a blank initializer for this later
                // field. Never discard actual rows or advanced v5 accounting.
                var future = source.NpcExperienceInbox;
                Require(future == null || future.Revision == 0 && future.RetiredCount == 0 &&
                    future.DetailRevision == 0 && future.LegacyImportedRevision == 0 &&
                    (future.Entries == null || future.Entries.Count == 0),
                    "Pre-inbox save contains NPC experience history.");
                return NpcExperienceInbox.Create();
            }
            Require(source.NpcExperienceInbox != null, "Missing NPC experience inbox.");
            Require(source.Version >= 54 || source.NpcExperienceInbox.Version < 5,
                "Legacy save contains future NPC experience retirement fields.");
            Require(source.Version >= 54 ||
                (source.NpcExperienceInbox.RetiredCount == 0 &&
                 source.NpcExperienceInbox.DetailRevision == 0 &&
                 source.NpcExperienceInbox.LegacyImportedRevision == 0),
                "Legacy save contains future NPC experience retirement fields.");
            var persistedInbox = source.Version < 38
                ? NormalizeLegacyNativeInbox(source.NpcExperienceInbox)
                : source.NpcExperienceInbox;
            return NpcExperienceInbox.CopyValidated(persistedInbox,
                id => KnownNpcIdentity(validated, id, out _));
        }

        private static NpcExperienceInbox NormalizeLegacyNativeInbox(NpcExperienceInbox source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Entries == null) throw new ArgumentException("Missing legacy NPC experience entries.", nameof(source));
            var normalized = new NpcExperienceInbox
            {
                Version = source.Version,
                Revision = source.Revision,
                Entries = new List<NpcExperienceInboxEntry>(source.Entries.Count)
            };
            foreach (var raw in source.Entries)
            {
                if (raw == null) throw new ArgumentException("Null legacy NPC experience entry.", nameof(source));
                if ((raw.Origin != 0 && raw.Origin != NpcExperienceEventOrigin.NativeOutcome) ||
                    raw.EventKind < NpcExperienceEventKind.MoneyReceived ||
                    raw.EventKind > NpcExperienceEventKind.ObligationBreached ||
                    raw.AlreadyAppliedEffectsMask != NpcExperienceAppliedEffectsMask.None || raw.UtteranceId != null ||
                    raw.AttributedSpeakerId != null || raw.ObservedTurnObserverId != null || raw.ObservedTurnEpisodeId != null ||
                    raw.ObservedTurnSpeakerId != null || raw.ObservedTurnReceiptId != null || raw.ObservedTurnText != null ||
                    raw.ObservedTurnGameTime != 0 || raw.ImpactDetailsKind != 0 || raw.HealthInitial != 0 ||
                    raw.HealthFinal != 0 || raw.HealthMaximum != 0 || raw.HealthDamageTotal != 0 ||
                    raw.HealthHealingTotal != 0 || raw.HealthChangeCount != 0 || raw.TransferQuantity != 0 ||
                    raw.TransferCurrency != 0 || raw.NativeValuationStatus != 0 || raw.NativeGoldValue != 0 || raw.ReceivedItemUid != 0 ||
                    raw.StatusKind != 0 || raw.StatusNativeEffectKey != null || raw.StatusOnsetSequence != 0 ||
                    raw.EnqueueOrdinal != 0 || raw.SourceVitalsRevision != 0 ||
                    raw.SourceKind != NpcExperienceSourceKind.LegacyUnknown ||
                    raw.DeferralReason != NpcExperienceDeferralReason.None ||
                    raw.DeferralAttempts != 0)
                    throw new ArgumentException("Legacy NPC experience row contains fields unavailable in schemas 35 to 37.", nameof(source));
                normalized.Entries.Add(new NpcExperienceInboxEntry
                {
                    Origin = NpcExperienceEventOrigin.NativeOutcome,
                    ObserverId = raw.ObserverId,
                    SubjectPersonId = raw.SubjectPersonId,
                    ReceiptId = raw.ReceiptId,
                    EpisodeId = raw.EpisodeId,
                    OutcomeId = raw.OutcomeId,
                    EventKind = raw.EventKind,
                    ObservedGameTime = raw.ObservedGameTime,
                    Revision = raw.Revision,
                    UpdatedAt = raw.UpdatedAt,
                    ResponsibleActorAttribution = raw.ResponsibleActorAttribution,
                    ResponsibleActorId = raw.ResponsibleActorId,
                    DeduplicationKey = raw.DeduplicationKey,
                    Status = raw.Status,
                    Appraisal = raw.Appraisal == null ? null : NpcExperienceAppliedAppraisal.CopyValidated(raw.Appraisal)
                });
            }
            return normalized;
        }
    }
}
