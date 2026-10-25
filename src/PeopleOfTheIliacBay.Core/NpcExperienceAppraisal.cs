using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    [Flags]
    public enum NpcExperienceAppliedEffectsMask { None = 0, Mood = 1, Affinity = 2 }

    public enum NpcExperienceSpeechActKind
    {
        NotApplicable = 0, Other = 1, DirectInsult = 2, DirectThreat = 3,
        QuotedThreat = 4, ReportedThreat = 5, HypotheticalThreat = 6,
        JokingThreat = 7, AmbiguousThreat = 8,
        DirectHarassment = 9, SevereAbuse = 10, PersistentAbuse = 11, RepairAttempt = 12
    }

    public enum NpcExperienceConversationOption
    {
        NoAction = 0, ContinueConversation = 1, RefuseCurrentRequest = 2,
        EndConversation = 3, RequestNearbyGuard = 4, RequestNativeRemoval = 5,
        RequestAccessRestriction = 6
    }

    public enum NpcExperienceCommitmentKind { Appointment = 1, SharedActivity = 2, ActionTask = 3, Task = 4 }
    public enum NpcExperienceCommitmentStatus { Planned = 1, Waiting = 2, Active = 3, Fulfilled = 4, Breached = 5, Cancelled = 6 }

    public sealed class NpcExperienceCommitment
    {
        private NpcExperienceCommitment() { }
        public string Id { get; private set; }
        public NpcExperienceCommitmentKind Kind { get; private set; }
        public NpcExperienceCommitmentStatus Status { get; private set; }
        public long Revision { get; private set; }

        public static NpcExperienceCommitment Create(string id, NpcExperienceCommitmentKind kind,
            NpcExperienceCommitmentStatus status, long revision)
        {
            if (!NpcExperienceAppraisalContract.ValidIdentity(id, 128) ||
                !Enum.IsDefined(typeof(NpcExperienceCommitmentKind), kind) ||
                !Enum.IsDefined(typeof(NpcExperienceCommitmentStatus), status) || revision < 0)
                throw new ArgumentException("Invalid observer-known commitment.");
            return new NpcExperienceCommitment { Id = id, Kind = kind, Status = status, Revision = revision };
        }
    }

    public sealed class NpcObservedConversationTurn
    {
        private NpcObservedConversationTurn() { }
        public string ObserverId { get; private set; }
        public string EpisodeId { get; private set; }
        public string SpeakerId { get; private set; }
        public string ReceiptId { get; private set; }
        public string Text { get; private set; }
        public double ObservedGameTime { get; private set; }

        public static NpcObservedConversationTurn Create(string observerId, string episodeId,
            string speakerId, string receiptId, string text, double observedGameTime)
        {
            if (!NpcExperienceAppraisalContract.ValidIdentity(observerId, 240) ||
                !NpcExperienceAppraisalContract.ValidIdentity(episodeId, 240) ||
                !NpcExperienceAppraisalContract.ValidIdentity(speakerId, 240) ||
                !NpcExperienceAppraisalContract.ValidIdentity(receiptId, 240) ||
                string.IsNullOrWhiteSpace(text) || text.Length > 1024 || !Finite(observedGameTime) || observedGameTime < 0)
                throw new ArgumentException("Invalid observer-known conversation turn.");
            return new NpcObservedConversationTurn { ObserverId = observerId, EpisodeId = episodeId,
                SpeakerId = speakerId, ReceiptId = receiptId, Text = text, ObservedGameTime = observedGameTime };
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed class NpcExperienceAppraisalRequest
    {
        internal NpcExperienceAppraisalRequest(string branch, NpcExperienceEvent evidence,
            NpcPersonalityProfile personality, int mood, long moodRevision, double moodTime,
            int affinity, long affinityRevision, int positiveAffinityCredit, double capturedTime,
            NpcDirectedRelationshipView relationship, IReadOnlyList<NpcExperienceCommitment> commitments,
            bool waited, string utterance, IReadOnlyList<NpcObservedConversationTurn> context,
            IReadOnlyList<string> family, string speakerRank,
            IReadOnlyList<NpcExperienceConversationOption> eligibleOptions,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask,
            NpcMotivationContextSnapshot motivationContext, NpcKnowledgeSnapshot knowledgeSnapshot,
            int? immediateAffinityDelta, NpcObservedSpeechAffinityExclusion affinityExclusion,
            NpcRoleState roleState)
        {
            SaveBranchId = branch;
            Evidence = NpcExperienceEvent.CopyValidated(evidence);
            ObserverId = evidence.ObserverId;
            SubjectPersonId = evidence.SubjectPersonId;
            EvidenceDeduplicationKey = evidence.DeduplicationKey;
            CanonicalEvidenceId = evidence.Origin == NpcExperienceEventOrigin.NativeOutcome ? evidence.OutcomeId : evidence.UtteranceId;
            RelationshipTargetId = evidence.Origin == NpcExperienceEventOrigin.AttributedSpeech ? evidence.AttributedSpeakerId :
                evidence.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Known ? evidence.ResponsibleActorId : null;
            bool playerTarget = string.Equals(RelationshipTargetId, NpcDirectedRelationship.PlayerTargetId,
                StringComparison.Ordinal);
            NpcRoleState role = roleState == null ? null : NpcRoleState.CopyValidated(roleState);
            if (role != null && !string.Equals(role.PersonId, evidence.ObserverId, StringComparison.Ordinal))
                throw new ArgumentException("Role evidence must belong to the appraisal observer.", nameof(roleState));
            ExpectedRoleRevision = !playerTarget ? -1 : role != null ? role.Revision :
                relationship == null ? -1 : relationship.RoleRevision;
            PlayerFearSuppressed = playerTarget && (role == null
                ? relationship != null && relationship.PlayerFearSuppressed
                : NpcRolePolicy.SuppressPlayerFear(role));
            Personality = NpcPersonalityProfile.CopyValidated(personality);
            CurrentMood = mood; ExpectedMoodRevision = moodRevision; CurrentMoodGameTime = moodTime;
            CurrentAffinity = affinity; ExpectedAffinityRevision = affinityRevision;
            PositiveAffinityCredit = positiveAffinityCredit; CapturedGameTime = capturedTime;
            Relationship = relationship; ExpectedRelationshipRevision = relationship == null ? -1 : relationship.Revision;
            Commitments = commitments; WaitedForAppointment = waited; ObservedUtterance = utterance;
            ConversationContext = context; KnownFamilyIds = family; KnownSpeakerRankId = speakerRank;
            EligibleConsequences = eligibleOptions;
            AlreadyAppliedEffectsMask = alreadyAppliedEffectsMask;
            ImmediateAffinityDelta = immediateAffinityDelta; AffinityExclusion = affinityExclusion;
            MotivationContext = motivationContext; KnowledgeSnapshot = knowledgeSnapshot;
            ExpectedMotivationRevision = motivationContext == null ? -1 : motivationContext.Revision;
            ExpectedKnowledgeRevision = knowledgeSnapshot == null ? -1 : knowledgeSnapshot.Revision;
            Prompt = NpcExperienceAppraisalContract.BuildPrompt(this);
        }

        public string SaveBranchId { get; private set; }
        public string ObserverId { get; private set; }
        public string SubjectPersonId { get; private set; }
        public string EvidenceDeduplicationKey { get; private set; }
        public string CanonicalEvidenceId { get; private set; }
        public string RelationshipTargetId { get; private set; }
        public int CurrentMood { get; private set; }
        public int CurrentAffinity { get; private set; }
        public int PositiveAffinityCredit { get; private set; }
        public long ExpectedMoodRevision { get; private set; }
        public long ExpectedAffinityRevision { get; private set; }
        public long ExpectedRelationshipRevision { get; private set; }
        /// <summary>Internal request binding to current role state; this is not a model response field.</summary>
        public long ExpectedRoleRevision { get; private set; }
        public bool PlayerFearSuppressed { get; private set; }
        public long ExpectedMotivationRevision { get; private set; }
        public long ExpectedKnowledgeRevision { get; private set; }
        public double CurrentMoodGameTime { get; private set; }
        public double CapturedGameTime { get; private set; }
        public string Prompt { get; private set; }
        public NpcSpeechActClassification SpeechClassification { get; private set; }
        internal NpcExperienceAppraisalRequest WithClassification(NpcSpeechActClassification classification)
        {
            var copy = (NpcExperienceAppraisalRequest)MemberwiseClone();
            copy.SpeechClassification = classification;
            copy.Prompt = NpcExperienceAppraisalContract.BuildPrompt(copy);
            return copy;
        }
        public bool RequiresSpeechAppraisal { get { return Evidence.Origin == NpcExperienceEventOrigin.AttributedSpeech; } }
        public string ObservedUtterance { get; private set; }
        public IReadOnlyList<NpcExperienceCommitment> Commitments { get; private set; }
        public IReadOnlyList<NpcObservedConversationTurn> ConversationContext { get; private set; }
        public IReadOnlyList<string> KnownFamilyIds { get; private set; }
        public string KnownSpeakerRankId { get; private set; }
        public IReadOnlyList<NpcExperienceConversationOption> EligibleConsequences { get; private set; }
        public NpcMotivationContextSnapshot MotivationContext { get; private set; }
        public NpcKnowledgeSnapshot KnowledgeSnapshot { get; private set; }
        public NpcExperienceAppliedEffectsMask AlreadyAppliedEffectsMask { get; private set; }
        public int? ImmediateAffinityDelta { get; private set; }
        public NpcObservedSpeechAffinityExclusion AffinityExclusion { get; private set; }
        internal NpcExperienceEvent Evidence { get; private set; }
        internal NpcPersonalityProfile Personality { get; private set; }
        internal NpcDirectedRelationshipView Relationship { get; private set; }
        internal bool WaitedForAppointment { get; private set; }
    }

    /// <summary>Bounded proposal only. Caller owns provenance, exactly-once handling and atomic state writes.</summary>
    public sealed class NpcExperienceAppraisal
    {
        internal NpcExperienceAppraisal(NpcExperienceAppraisalRequest request, int mood, int affinity,
            int trust, int resentment, int fear, NpcExperienceSpeechActKind speechAct,
            string speechTarget, NpcExperienceConversationOption conversationOption)
        {
            SaveBranchId = request.SaveBranchId; ObserverId = request.ObserverId; SubjectPersonId = request.SubjectPersonId;
            EvidenceDeduplicationKey = request.EvidenceDeduplicationKey; CanonicalEvidenceId = request.CanonicalEvidenceId;
            RelationshipTargetId = request.RelationshipTargetId; ExpectedMoodRevision = request.ExpectedMoodRevision;
            ExpectedAffinityRevision = request.ExpectedAffinityRevision; ExpectedRelationshipRevision = request.ExpectedRelationshipRevision;
            ObservedGameTime = request.Evidence.ObservedGameTime; CapturedGameTime = request.CapturedGameTime;
            MoodDelta = mood; AffinityDelta = affinity; TrustDelta = trust; ResentmentDelta = resentment;
            FearDelta = fear; SpeechActKind = speechAct; SpeechTargetPersonId = speechTarget;
            ConversationOption = conversationOption;
        }
        public string SaveBranchId { get; private set; }
        public string ObserverId { get; private set; }
        public string SubjectPersonId { get; private set; }
        public string EvidenceDeduplicationKey { get; private set; }
        public string CanonicalEvidenceId { get; private set; }
        public string RelationshipTargetId { get; private set; }
        public long ExpectedMoodRevision { get; private set; }
        public long ExpectedAffinityRevision { get; private set; }
        public long ExpectedRelationshipRevision { get; private set; }
        public double ObservedGameTime { get; private set; }
        public double CapturedGameTime { get; private set; }
        public int MoodDelta { get; private set; }
        /// <summary>Disposition proposal only. Consumer must apply its existing gradual friendship policy.</summary>
        public int AffinityDelta { get; private set; }
        public int TrustDelta { get; private set; }
        public int ResentmentDelta { get; private set; }
        public int FearDelta { get; private set; }
        public NpcExperienceSpeechActKind SpeechActKind { get; private set; }
        public string SpeechTargetPersonId { get; private set; }
        public NpcExperienceConversationOption ConversationOption { get; private set; }
    }

    public static class NpcExperienceAppraisalContract
    {
        public const int SchemaVersion = 2;
        public const int PolicyVersion = 4;
        public static NpcExperienceAppraisalRequest WithSpeechClassification(NpcExperienceAppraisalRequest request,
            NpcSpeechActClassification classification)
        {
            if (request == null || !request.RequiresSpeechAppraisal || request.SpeechClassification != null ||
                classification == null || !ReferenceEquals(classification.Request.Source, request))
                throw new ArgumentException("Speech classification must belong to this exact unclassified request.");
            return request.WithClassification(classification);
        }

        private static bool ProtectedSpeech(NpcExperienceAppraisalRequest request)
        { return request.SpeechClassification != null && !request.SpeechClassification.IsDirectHostility; }
        // Candidate bounds only. Scenario calibration remains required before production use.
        public const int DirectInsultAffinityLossBound = 25;
        public const int DirectThreatAffinityLossBound = 50;
        public const int DirectThreatTrustLossBound = 25;
        public const int SevereResentmentAndFearBound = 25;
        private const int MaxCommitments = 8;
        private const int MaxContextTurns = 6;
        private static readonly string[] ResponseFields = {
            "schemaVersion", "policyVersion", "saveBranchId", "observerId", "subjectPersonId",
            "evidenceDeduplicationKey", "canonicalEvidenceId", "relationshipTargetId",
            "expectedMoodRevision", "expectedAffinityRevision", "expectedRelationshipRevision",
            "moodDelta", "affinityDelta", "trustDelta", "resentmentDelta", "fearDelta",
            "speechActKind", "speechTargetPersonId", "conversationOption"
        };

        private sealed class ResponseBounds
        {
            public int MoodMinimum { get; set; }
            public int MoodMaximum { get; set; }
            public int AffinityMinimum { get; set; }
            public int AffinityMaximum { get; set; }
            public int TrustMinimum { get; set; }
            public int TrustMaximum { get; set; }
            public int ResentmentMinimum { get; set; }
            public int ResentmentMaximum { get; set; }
            public int FearMinimum { get; set; }
            public int FearMaximum { get; set; }
        }

        public static NpcExperienceAppraisalRequest CreateRequest(string saveBranchId, NpcExperienceEvent evidence,
            NpcPersonalityProfile personality, int currentMood, long moodRevision, double currentMoodGameTime,
            int currentAffinity, long affinityRevision, int positiveAffinityCredit, double capturedGameTime,
            NpcDirectedRelationshipView relationship, IEnumerable<NpcExperienceCommitment> commitments,
            bool waitedForAppointment = false, NpcObservedConversationTurn observedUtterance = null,
            IEnumerable<NpcObservedConversationTurn> conversationContext = null, IEnumerable<string> knownFamilyIds = null,
            string knownSpeakerRankId = null, IEnumerable<NpcExperienceConversationOption> eligibleConsequences = null,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask = NpcExperienceAppliedEffectsMask.None,
            NpcMotivationContextSnapshot motivationContext = null, NpcKnowledgeSnapshot knowledgeSnapshot = null,
            int? immediateAffinityDelta = null,
            NpcObservedSpeechAffinityExclusion affinityExclusion = NpcObservedSpeechAffinityExclusion.None,
            NpcRoleState roleState = null)
        {
            if (!ValidIdentity(saveBranchId, 128) || evidence == null || personality == null ||
                currentMood < -100 || currentMood > 100 || currentAffinity < -100 || currentAffinity > 100 ||
                moodRevision < 0 || affinityRevision < 0 || positiveAffinityCredit < 0 || positiveAffinityCredit > 5 ||
                !Finite(currentMoodGameTime) || !Finite(capturedGameTime) || currentMoodGameTime < 0 ||
                capturedGameTime < currentMoodGameTime || capturedGameTime < evidence.ObservedGameTime)
                throw new ArgumentException("Invalid appraisal request state.");
            var copy = NpcExperienceEvent.CopyValidated(evidence);
            if (copy.NativeImpactDetails != null && copy.NativeImpactDetails.Kind == NpcExperienceImpactKind.StatusOnset &&
                (copy.Origin != NpcExperienceEventOrigin.NativeOutcome || copy.SubjectPersonId != copy.ObserverId ||
                 copy.ResponsibleActorAttribution != NpcResponsibleActorAttribution.Unknown || copy.ResponsibleActorId != null))
                throw new ArgumentException("Status onset requires observer-bound evidence with unknown cause.", nameof(evidence));
            var profile = NpcPersonalityProfile.CopyValidated(personality);
            if (profile.PersonId != copy.ObserverId) throw new ArgumentException("Personality must belong to evidence observer.");
            if (motivationContext != null && (motivationContext.ObserverId != copy.ObserverId ||
                motivationContext.SaveBranchId != saveBranchId || motivationContext.CapturedGameTime != capturedGameTime))
                throw new ArgumentException("Motivation context must match the exact appraisal observer, branch and capture time.", nameof(motivationContext));
            if (knowledgeSnapshot != null && knowledgeSnapshot.ActorId != copy.ObserverId)
                throw new ArgumentException("Knowledge snapshot must belong to the appraisal observer.", nameof(knowledgeSnapshot));
            bool speech = copy.Origin == NpcExperienceEventOrigin.AttributedSpeech;
            const NpcExperienceAppliedEffectsMask allowedMask = NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity;
            if ((alreadyAppliedEffectsMask & ~allowedMask) != 0 || (!speech && alreadyAppliedEffectsMask != NpcExperienceAppliedEffectsMask.None))
                throw new ArgumentException("Already-applied effects mask is invalid for this evidence.", nameof(alreadyAppliedEffectsMask));
            bool hasAffinityProvenance = immediateAffinityDelta.HasValue || affinityExclusion != NpcObservedSpeechAffinityExclusion.None;
            if (!Enum.IsDefined(typeof(NpcObservedSpeechAffinityExclusion), affinityExclusion) ||
                immediateAffinityDelta.HasValue && (immediateAffinityDelta.Value < -NpcObservedSpeechReceipt.NativeAffinityDeltaBound ||
                    immediateAffinityDelta.Value > NpcObservedSpeechReceipt.NativeAffinityDeltaBound) ||
                !speech && hasAffinityProvenance || hasAffinityProvenance &&
                    (alreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) == 0 ||
                affinityExclusion == NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor &&
                    !immediateAffinityDelta.HasValue)
                throw new ArgumentException("Native affinity provenance must match an applied speech affinity receipt.");
            if (speech && (observedUtterance == null || observedUtterance.Text.Length > 2048 ||
                observedUtterance.ObserverId != copy.ObserverId || observedUtterance.EpisodeId != copy.EpisodeId ||
                observedUtterance.SpeakerId != copy.AttributedSpeakerId || observedUtterance.ReceiptId != copy.ReceiptId ||
                observedUtterance.ObservedGameTime != copy.ObservedGameTime))
                throw new ArgumentException("Attributed speech must match exact native receipt, observer, speaker, episode and time.", nameof(observedUtterance));
            if (!speech && (observedUtterance != null || HasAny(conversationContext) || HasAny(knownFamilyIds) ||
                !string.IsNullOrEmpty(knownSpeakerRankId)))
                throw new ArgumentException("Speech context requires receipt-bound attributed speech.");
            if (!speech && HasAny(eligibleConsequences))
                throw new ArgumentException("Conversation consequences apply only to receipt-bound speech.", nameof(eligibleConsequences));
            if (knownSpeakerRankId != null && !ValidIdentity(knownSpeakerRankId, 128))
                throw new ArgumentException("Invalid native-known speaker rank.", nameof(knownSpeakerRankId));
            if (copy.EventKind == NpcExperienceEventKind.AppointmentNoShow && !waitedForAppointment)
                throw new ArgumentException("No-show appraisal requires native observed-wait evidence.", nameof(waitedForAppointment));
            if (waitedForAppointment && copy.EventKind != NpcExperienceEventKind.AppointmentNoShow)
                throw new ArgumentException("Observed-wait evidence applies only to no-show.", nameof(waitedForAppointment));
            string target = speech ? copy.AttributedSpeakerId : copy.ResponsibleActorId;
            if (relationship != null && (target == null || relationship.ObserverId != copy.ObserverId ||
                relationship.TargetId != target || relationship.Revision < 0 || relationship.LastGameTime > capturedGameTime))
                throw new ArgumentException("Relationship must match observer, known target and captured time.", nameof(relationship));
            if (relationship != null)
                relationship = relationship.ProjectRecovery(capturedGameTime, profile,
                    NpcFearRecoveryPolicy.ProvisionalDefault,
                    NpcResentmentRecoveryPolicy.ProvisionalDefault);
            return new NpcExperienceAppraisalRequest(saveBranchId, copy, profile, currentMood, moodRevision,
                currentMoodGameTime, currentAffinity, affinityRevision, positiveAffinityCredit, capturedGameTime,
                relationship, CopyCommitments(commitments), waitedForAppointment,
                observedUtterance == null ? null : observedUtterance.Text,
                CopyContext(conversationContext, copy), CopyFamily(knownFamilyIds, copy.ObserverId), knownSpeakerRankId,
                CopyOptions(eligibleConsequences), alreadyAppliedEffectsMask, motivationContext, knowledgeSnapshot,
                immediateAffinityDelta, affinityExclusion, roleState);
        }

        /// <summary>
        /// GBNF constraining a model reply to the exact response fields and request binding.
        /// Semantic ranges and event-specific policy remain enforced by TryParseResponse.
        /// </summary>
        public static string ResponseGrammar(NpcExperienceAppraisalRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ResponseBounds bounds = CalculateResponseBounds(request);
            var grammar = new StringBuilder("root ::= ");
            AppendGrammarLiteral(grammar, "{");
            AppendGrammarField(grammar, "\"schemaVersion\":", SchemaVersion.ToString(CultureInfo.InvariantCulture), false, true);
            AppendGrammarField(grammar, "\"policyVersion\":", PolicyVersion.ToString(CultureInfo.InvariantCulture));
            AppendGrammarStringField(grammar, "\"saveBranchId\":", request.SaveBranchId);
            AppendGrammarStringField(grammar, "\"observerId\":", request.ObserverId);
            AppendGrammarStringField(grammar, "\"subjectPersonId\":", request.SubjectPersonId);
            AppendGrammarStringField(grammar, "\"evidenceDeduplicationKey\":", request.EvidenceDeduplicationKey);
            AppendGrammarStringField(grammar, "\"canonicalEvidenceId\":", request.CanonicalEvidenceId);
            AppendGrammarStringField(grammar, "\"relationshipTargetId\":", request.RelationshipTargetId ?? string.Empty);
            AppendGrammarField(grammar, "\"expectedMoodRevision\":", request.ExpectedMoodRevision.ToString(CultureInfo.InvariantCulture));
            AppendGrammarField(grammar, "\"expectedAffinityRevision\":", request.ExpectedAffinityRevision.ToString(CultureInfo.InvariantCulture));
            AppendGrammarField(grammar, "\"expectedRelationshipRevision\":", request.ExpectedRelationshipRevision.ToString(CultureInfo.InvariantCulture));
            AppendGrammarField(grammar, "\"moodDelta\":", "mood-delta", true);
            AppendGrammarField(grammar, "\"affinityDelta\":", "affinity-delta", true);
            AppendGrammarField(grammar, "\"trustDelta\":", "trust-delta", true);
            AppendGrammarField(grammar, "\"resentmentDelta\":", "resentment-delta", true);
            AppendGrammarField(grammar, "\"fearDelta\":", "fear-delta", true);
            AppendGrammarField(grammar, "\"speechActKind\":", "speech-act", true);
            AppendGrammarField(grammar, "\"speechTargetPersonId\":", "speech-target", true);
            AppendGrammarField(grammar, "\"conversationOption\":", "conversation-option", true);
            AppendGrammarLiteral(grammar, "}");
            grammar.AppendLine();
            AppendIntegerRule(grammar, "mood-delta", bounds.MoodMinimum, bounds.MoodMaximum);
            AppendIntegerRule(grammar, "affinity-delta", bounds.AffinityMinimum, bounds.AffinityMaximum);
            AppendIntegerRule(grammar, "trust-delta", bounds.TrustMinimum, bounds.TrustMaximum);
            AppendIntegerRule(grammar, "resentment-delta", bounds.ResentmentMinimum, bounds.ResentmentMaximum);
            AppendIntegerRule(grammar, "fear-delta", bounds.FearMinimum, bounds.FearMaximum);
            grammar.Append("speech-act ::= ");
            IEnumerable<string> speechActs = Enum.GetNames(typeof(NpcExperienceSpeechActKind))
                .Where(name => request.RequiresSpeechAppraisal ==
                    (name != NpcExperienceSpeechActKind.NotApplicable.ToString()));
            if (request.SpeechClassification != null) speechActs = new[] { request.SpeechClassification.Kind.ToString() };
            AppendEnumAlternatives(grammar, speechActs);
            grammar.AppendLine();
            grammar.Append("conversation-option ::= ");
            var options = new List<string> { NpcExperienceConversationOption.NoAction.ToString() };
            if (!ProtectedSpeech(request)) options.AddRange(request.EligibleConsequences.Select(x => x.ToString()));
            AppendEnumAlternatives(grammar, options);
            grammar.AppendLine();
            grammar.Append("speech-target ::= ");
            var targets = new List<string> { string.Empty };
            if (request.RequiresSpeechAppraisal && !ProtectedSpeech(request))
            {
                targets.Add(request.ObserverId);
                targets.AddRange(request.KnownFamilyIds);
            }
            AppendEnumAlternatives(grammar, targets.Distinct(StringComparer.Ordinal));
            grammar.AppendLine();
            return grammar.ToString();
        }

        private static void AppendIntegerRule(StringBuilder grammar, string name, int minimum, int maximum)
        {
            grammar.Append(name).Append(" ::= ");
            for (int value = minimum; value <= maximum; value++)
            {
                if (value > minimum) grammar.Append(" | ");
                grammar.Append(GrammarTerm(value.ToString(CultureInfo.InvariantCulture)));
            }
            grammar.AppendLine();
        }

        private static ResponseBounds CalculateResponseBounds(NpcExperienceAppraisalRequest request)
        {
            bool moodAlreadyApplied = (request.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Mood) != 0;
            bool affinityAlreadyApplied = (request.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) != 0;
            bool noShow = request.Evidence.EventKind == NpcExperienceEventKind.AppointmentNoShow;
            bool relationshipTargetKnown = request.RelationshipTargetId != null;
            bool relationshipLedgerAvailable = CanChangeRelationship(request);
            bool speech = request.RequiresSpeechAppraisal;
            bool speechClassificationBound = speech && request.SpeechClassification != null;
            bool classifiedThreat = speech && request.SpeechClassification != null &&
                request.SpeechClassification.Kind == NpcExperienceSpeechActKind.DirectThreat;
            bool classifiedInsult = speech && request.SpeechClassification != null &&
                request.SpeechClassification.Kind == NpcExperienceSpeechActKind.DirectInsult;
            bool classifiedOtherHostility = speech && request.SpeechClassification != null &&
                (request.SpeechClassification.Kind == NpcExperienceSpeechActKind.DirectHarassment ||
                 request.SpeechClassification.Kind == NpcExperienceSpeechActKind.SevereAbuse ||
                 request.SpeechClassification.Kind == NpcExperienceSpeechActKind.PersistentAbuse);
            bool classifiedHostility = classifiedThreat || classifiedInsult || classifiedOtherHostility;
            bool severeSupplementAllowed = speech && request.AffinityExclusion == NpcObservedSpeechAffinityExclusion.None &&
                (!speechClassificationBound || classifiedThreat || classifiedInsult);

            int affinityMinimum = 0;
            int affinityMaximum = 0;
            if ((!affinityAlreadyApplied || severeSupplementAllowed) && !noShow && relationshipTargetKnown)
            {
                affinityMinimum = -10;
                if (speech)
                    affinityMinimum = classifiedOtherHostility ? -10 :
                        classifiedInsult ? -DirectInsultAffinityLossBound : -DirectThreatAffinityLossBound;
                affinityMaximum = affinityAlreadyApplied ? 0 : request.PositiveAffinityCredit;
                if (classifiedHostility && affinityMaximum > 0) affinityMaximum = 0;
            }

            int trustMinimum = 0;
            int trustMaximum = 0;
            if (relationshipLedgerAvailable)
            {
                if (speech)
                {
                    trustMinimum = !speechClassificationBound || classifiedThreat ?
                        -DirectThreatTrustLossBound : -10;
                }
                else
                {
                    if (NegativeTrustEvent(request.Evidence.EventKind)) trustMinimum = -10;
                    if (PositiveTrustEvent(request.Evidence.EventKind)) trustMaximum = 10;
                }
            }

            int relationshipMinimum = 0;
            int relationshipMaximum = 0;
            if (relationshipLedgerAvailable)
            {
                relationshipMinimum = classifiedHostility ? 0 : -10;
                relationshipMaximum = speech && (!speechClassificationBound || classifiedThreat) ?
                    SevereResentmentAndFearBound : 10;
            }
            if (ProtectedSpeech(request))
            {
                affinityMinimum = 0;
                trustMinimum = 0;
                relationshipMaximum = 0;
            }
            int fearMinimum = request.PlayerFearSuppressed ? 0 : relationshipMinimum;
            int fearMaximum = request.PlayerFearSuppressed ? 0 : relationshipMaximum;
            return new ResponseBounds
            {
                MoodMinimum = moodAlreadyApplied ? 0 : -10,
                MoodMaximum = moodAlreadyApplied ? 0 : 10,
                AffinityMinimum = affinityMinimum,
                AffinityMaximum = affinityMaximum,
                TrustMinimum = trustMinimum,
                TrustMaximum = trustMaximum,
                ResentmentMinimum = relationshipMinimum,
                ResentmentMaximum = relationshipMaximum,
                FearMinimum = fearMinimum,
                FearMaximum = fearMaximum
            };
        }

        private static void AppendGrammarField(StringBuilder grammar, string key, string value,
            bool nonterminal = false, bool first = false)
        {
            if (!first) AppendGrammarLiteral(grammar, ",");
            AppendGrammarLiteral(grammar, key);
            if (nonterminal) grammar.Append(' ').Append(value);
            else AppendGrammarLiteral(grammar, value);
        }

        private static void AppendGrammarStringField(StringBuilder grammar, string key, string value)
        {
            AppendGrammarLiteral(grammar, ",");
            AppendGrammarLiteral(grammar, key);
            grammar.Append(' ').Append(JsonStringGrammarLiteral(value));
        }

        private static void AppendGrammarLiteral(StringBuilder grammar, string value)
        {
            grammar.Append(' ').Append(GrammarTerm(value));
        }

        private static void AppendEnumAlternatives(StringBuilder grammar, IEnumerable<string> values)
        {
            bool first = true;
            foreach (string value in values)
            {
                if (!first) grammar.Append(" | ");
                grammar.Append(JsonStringGrammarLiteral(value));
                first = false;
            }
        }

        private static string JsonStringGrammarLiteral(string value)
        {
            return GrammarTerm("\"" + LocalDialogueCompletion.EscapeJson(value) + "\"");
        }

        private static string GrammarTerm(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }


        public static bool TryParseResponse(NpcExperienceAppraisalRequest request, string response,
            out NpcExperienceAppraisal appraisal)
        {
            appraisal = null;
            if (request == null || response == null || response.Length > 4096) return false;
            try
            {
                Dictionary<string, object> values = new ScalarJsonReader(response).Read();
                if (values.Count != ResponseFields.Length || ResponseFields.Any(x => !values.ContainsKey(x))) return false;
                if (Integer(values, "schemaVersion") != SchemaVersion || Integer(values, "policyVersion") != PolicyVersion ||
                    String(values, "saveBranchId") != request.SaveBranchId || String(values, "observerId") != request.ObserverId ||
                    String(values, "subjectPersonId") != request.SubjectPersonId || String(values, "evidenceDeduplicationKey") != request.EvidenceDeduplicationKey ||
                    String(values, "canonicalEvidenceId") != request.CanonicalEvidenceId ||
                    String(values, "relationshipTargetId") != (request.RelationshipTargetId ?? "") ||
                    Long(values, "expectedMoodRevision") != request.ExpectedMoodRevision ||
                    Long(values, "expectedAffinityRevision") != request.ExpectedAffinityRevision ||
                    Long(values, "expectedRelationshipRevision") != request.ExpectedRelationshipRevision) return false;
                int mood = Integer(values, "moodDelta"), affinity = Integer(values, "affinityDelta");
                if ((request.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Mood) != 0 && mood != 0) return false;
                // The existing appointment resolver already applies its native no-show affinity penalty.
                if (request.Evidence.EventKind == NpcExperienceEventKind.AppointmentNoShow && affinity != 0) return false;
                int trust = Integer(values, "trustDelta"), resentment = Integer(values, "resentmentDelta"), fear = Integer(values, "fearDelta");
                if (request.PlayerFearSuppressed && fear != 0) return false;
                NpcExperienceSpeechActKind speechAct = EnumValue<NpcExperienceSpeechActKind>(values, "speechActKind");
                string speechTarget = String(values, "speechTargetPersonId");
                NpcExperienceConversationOption option = EnumValue<NpcExperienceConversationOption>(values, "conversationOption");
                if (request.SpeechClassification != null && speechAct != request.SpeechClassification.Kind) return false;
                if (ProtectedSpeech(request) && (affinity < 0 || trust != 0 || resentment > 0 || fear > 0 ||
                    speechTarget.Length != 0 || option != NpcExperienceConversationOption.NoAction)) return false;
                if (request.SpeechClassification != null && request.SpeechClassification.IsDirectHostility &&
                    (resentment < 0 || fear < 0)) return false;
                bool speech = request.RequiresSpeechAppraisal;
                if ((!speech && (speechAct != NpcExperienceSpeechActKind.NotApplicable || speechTarget.Length != 0)) ||
                    (speech && speechAct == NpcExperienceSpeechActKind.NotApplicable)) return false;
                if (option != NpcExperienceConversationOption.NoAction && !request.EligibleConsequences.Contains(option)) return false;
                if (mood < -10 || mood > 10 || affinity > request.PositiveAffinityCredit || trust > 10) return false;
                bool knownTarget = request.RelationshipTargetId != null;
                bool severeTarget = speech && (speechTarget == request.ObserverId || request.KnownFamilyIds.Contains(speechTarget));
                if (speech && speechTarget.Length > 0 && !severeTarget) return false;
                int affinityFloor = speech && severeTarget && speechAct == NpcExperienceSpeechActKind.DirectThreat ?
                    -DirectThreatAffinityLossBound : speech && severeTarget && speechAct == NpcExperienceSpeechActKind.DirectInsult ?
                    -DirectInsultAffinityLossBound : -10;
                bool directSevereAffinity = speech && severeTarget &&
                    (speechAct == NpcExperienceSpeechActKind.DirectThreat || speechAct == NpcExperienceSpeechActKind.DirectInsult);
                bool affinityAlreadyApplied = (request.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) != 0;
                if (affinityAlreadyApplied && (affinity > 0 || affinity < 0 &&
                    (!directSevereAffinity || request.AffinityExclusion != NpcObservedSpeechAffinityExclusion.None)) ||
                    request.AffinityExclusion != NpcObservedSpeechAffinityExclusion.None && affinity != 0)
                    return false;
                int trustFloor = speech && severeTarget && speechAct == NpcExperienceSpeechActKind.DirectThreat ?
                    -DirectThreatTrustLossBound : -10;
                int severeRise = speech && severeTarget && speechAct == NpcExperienceSpeechActKind.DirectThreat ? SevereResentmentAndFearBound : 10;
                if (affinity < affinityFloor || trust < trustFloor || resentment < -10 || resentment > severeRise || fear < -10 || fear > severeRise)
                    return false;
                if (affinity != 0 && request.RelationshipTargetId == null) return false;
                if (!CanChangeRelationship(request) && (trust != 0 || resentment != 0 || fear != 0)) return false;
                if (speech && severeTarget && (speechAct == NpcExperienceSpeechActKind.DirectThreat ||
                    speechAct == NpcExperienceSpeechActKind.DirectInsult ||
                    speechAct == NpcExperienceSpeechActKind.DirectHarassment ||
                    speechAct == NpcExperienceSpeechActKind.SevereAbuse ||
                    speechAct == NpcExperienceSpeechActKind.PersistentAbuse) && affinity > 0) return false;
                if (speech && (trust > 0 || trust < 0 && !severeTarget)) return false;
                if (!speech && trust > 0 && !PositiveTrustEvent(request.Evidence.EventKind)) return false;
                if (!speech && trust < 0 && !NegativeTrustEvent(request.Evidence.EventKind)) return false;
                appraisal = new NpcExperienceAppraisal(request, mood, affinity, trust, resentment, fear, speechAct, speechTarget, option);
                return true;
            }
            catch (FormatException) { return false; }
            catch (OverflowException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private static bool CanChangeRelationship(NpcExperienceAppraisalRequest request)
        { return request.Relationship != null && request.RelationshipTargetId != null; }
        private static bool PositiveTrustEvent(NpcExperienceEventKind? kind)
        { return kind == NpcExperienceEventKind.AppointmentAttended || kind == NpcExperienceEventKind.CombatHelp || kind == NpcExperienceEventKind.Healing || kind == NpcExperienceEventKind.ObligationFulfilled; }
        private static bool NegativeTrustEvent(NpcExperienceEventKind? kind)
        { return kind == NpcExperienceEventKind.Injury || kind == NpcExperienceEventKind.AppointmentNoShow || kind == NpcExperienceEventKind.ObligationBreached; }

        private static IReadOnlyList<NpcExperienceCommitment> CopyCommitments(IEnumerable<NpcExperienceCommitment> source)
        {
            var values = source == null ? new List<NpcExperienceCommitment>() : source.ToList();
            if (values.Count > MaxCommitments || values.Any(x => x == null)) throw new ArgumentException("Commitment projection exceeds bound.", nameof(source));
            var ids = new HashSet<string>(StringComparer.Ordinal); var copy = new List<NpcExperienceCommitment>();
            foreach (var item in values)
            {
                var value = NpcExperienceCommitment.Create(item.Id, item.Kind, item.Status, item.Revision);
                if (!ids.Add(value.Id)) throw new ArgumentException("Duplicate commitment ID.", nameof(source));
                copy.Add(value);
            }
            return new ReadOnlyCollection<NpcExperienceCommitment>(copy);
        }
        private static IReadOnlyList<NpcObservedConversationTurn> CopyContext(IEnumerable<NpcObservedConversationTurn> source, NpcExperienceEvent evidence)
        {
            var values = source == null ? new List<NpcObservedConversationTurn>() : source.ToList();
            if (values.Count > MaxContextTurns) throw new ArgumentException("Conversation context exceeds bound.", nameof(source));
            var result = new List<NpcObservedConversationTurn>(); var receipts = new HashSet<string>(StringComparer.Ordinal); double last = -1;
            foreach (var item in values)
            {
                if (item == null || item.ObserverId != evidence.ObserverId || item.EpisodeId != evidence.EpisodeId ||
                    item.ObservedGameTime < last || item.ObservedGameTime > evidence.ObservedGameTime || !receipts.Add(item.ReceiptId))
                    throw new ArgumentException("Conversation context must be ordered, observer-bound, same-episode and receipt-distinct.", nameof(source));
                result.Add(NpcObservedConversationTurn.Create(item.ObserverId, item.EpisodeId, item.SpeakerId, item.ReceiptId, item.Text, item.ObservedGameTime));
                last = item.ObservedGameTime;
            }
            return new ReadOnlyCollection<NpcObservedConversationTurn>(result);
        }
        private static IReadOnlyList<string> CopyFamily(IEnumerable<string> source, string observer)
        {
            var values = source == null ? new List<string>() : source.ToList();
            if (values.Count > 8) throw new ArgumentException("Known family list exceeds bound.", nameof(source));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in values) if (!ValidIdentity(id, 240) || id == observer || !ids.Add(id)) throw new ArgumentException("Invalid or duplicate family ID.", nameof(source));
            return new ReadOnlyCollection<string>(values);
        }
        private static IReadOnlyList<NpcExperienceConversationOption> CopyOptions(IEnumerable<NpcExperienceConversationOption> source)
        {
            var values = source == null ? new List<NpcExperienceConversationOption>() : source.ToList();
            if (values.Count > 6 || values.Any(x => !Enum.IsDefined(typeof(NpcExperienceConversationOption), x) || x == NpcExperienceConversationOption.NoAction) || values.Distinct().Count() != values.Count)
                throw new ArgumentException("Invalid or duplicate eligible conversation option.", nameof(source));
            return new ReadOnlyCollection<NpcExperienceConversationOption>(values);
        }

        internal static string BuildPrompt(NpcExperienceAppraisalRequest request)
        {
            ResponseBounds bounds = CalculateResponseBounds(request);
            var e = request.Evidence;
            string kind = e.EventKind.HasValue ? e.EventKind.Value.ToString() :
                e.ClaimedEventKind.HasValue ? e.ClaimedEventKind.Value.ToString() : "ObservedSpeech";
            string actor = e.NativeImpactDetails != null && e.NativeImpactDetails.Kind == NpcExperienceImpactKind.StatusOnset
                ? "unknown" : e.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Known
                    ? Quote(e.ResponsibleActorId) : "unknown";
            string relation = request.Relationship == null ? "unknown" : "target=" + Quote(request.Relationship.TargetId) +
                ",trustKnown=" + request.Relationship.TrustKnown.ToString().ToLowerInvariant() +
                ",trust=" + (request.Relationship.TrustKnown ? request.Relationship.ReliabilityTrust.ToString(CultureInfo.InvariantCulture) : "unknown") +
                ",confidence=" + request.Relationship.TrustConfidence.ToString(CultureInfo.InvariantCulture) +
                ",evidenceCount=" + request.Relationship.TrustEvidenceCount.ToString(CultureInfo.InvariantCulture) +
                ",resentment=" + request.Relationship.ProjectedResentment.ToString(CultureInfo.InvariantCulture) +
                ",interpersonalFear=" + request.Relationship.ProjectedInterpersonalFear.ToString(CultureInfo.InvariantCulture);
            var b = new StringBuilder();
            b.AppendLine(NpcPersonalityPolicy.AuthorityInstruction);
            b.AppendLine("Return exactly one JSON object with schemaVersion=" + SchemaVersion.ToString(CultureInfo.InvariantCulture) +
                " and policyVersion=" + PolicyVersion.ToString(CultureInfo.InvariantCulture) + ". No prose or extra fields.");
            b.AppendLine("Appraise only supplied observer-known event, stable personality, mood, separate directed relationship and commitments. Zero is valid.");
            if (request.SpeechClassification != null)
            {
                b.AppendLine("The preceding speech attribution stage is fixed for this request: speechActKind=" + request.SpeechClassification.Kind +
                    ". Do not classify the utterance again. This is a fallible interpretation of words, not proof of a world event.");
                if (ProtectedSpeech(request)) b.AppendLine("No direct hostility was established. Do not punish the speaker: affinityDelta >= 0, trustDelta = 0, resentmentDelta <= 0, fearDelta <= 0, speechTargetPersonId empty, conversationOption NoAction. Mood may react to the subject discussed.");
            }
            b.Append("Numeric grammar ranges: moodDelta ").Append(Range(bounds.MoodMinimum, bounds.MoodMaximum))
                .Append("; affinityDelta ").Append(Range(bounds.AffinityMinimum, bounds.AffinityMaximum))
                .Append("; trustDelta ").Append(Range(bounds.TrustMinimum, bounds.TrustMaximum))
                .Append("; resentmentDelta ").Append(Range(bounds.ResentmentMinimum, bounds.ResentmentMaximum))
                .Append("; fearDelta ").Append(Range(bounds.FearMinimum, bounds.FearMaximum)).AppendLine(".");
            b.AppendLine("Follow each conditional limit below even when grammar permits a wider union of possible values.");
            b.AppendLine("moodDelta must stay from -10 to 10, or be exactly 0 when mood effect was already applied.");
            b.AppendLine("affinityDelta must not exceed supplied positiveAffinityCredit. It must be exactly 0 when relationshipTargetId is empty or for AppointmentNoShow. Native events and ordinary speech cannot reduce affinity below -10. DirectInsult targeted at observer or known family may reduce it to -25; DirectThreat to those targets may reduce it to -50. DirectInsult and DirectThreat cannot increase affinity.");
            if ((request.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Affinity) != 0)
            {
                b.Append("Native immediate affinity already applied; recorded signed delta=")
                    .Append(request.ImmediateAffinityDelta.HasValue ? request.ImmediateAffinityDelta.Value.ToString(CultureInfo.InvariantCulture) : "unknown")
                    .AppendLine(". affinityDelta is an additional severe negative supplement only; require direct insult/threat, target observer/known family, and no native family-threat disposition floor. Keep positive and all other affinity proposals at 0.");
                if (request.AffinityExclusion == NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor)
                    b.AppendLine("Native family-threat disposition floor is active; affinityDelta must be exactly 0. Other dimensions remain eligible.");
            }
            else
                b.AppendLine("When affinity effect was already applied, affinityDelta must be an additional severe negative supplement only for direct insult/threat targeted at observer/known family; never replace or include the native immediate delta.");
            b.AppendLine("When no directed relationship ledger row is supplied, trustDelta, resentmentDelta and fearDelta must all be 0. Native trust can increase by at most 10 only for AppointmentAttended, CombatHelp, Healing or ObligationFulfilled; it can decrease by at most 10 only for Injury, AppointmentNoShow or ObligationBreached. Other native events require trustDelta=0.");
            b.AppendLine("Speech trustDelta can never be positive. It may be negative only for a receipt-bound utterance targeted at the observer or known family: DirectThreat may reduce trust by at most 25; other direct hostility by at most 10. Indirect speech requires trustDelta=0. Resentment and fear stay from -10 to 10, except receipt-bound DirectThreat targeted at observer or known family may raise either by at most 25. DirectHarassment, SevereAbuse and PersistentAbuse may reduce affinity by at most 10 and cannot add a negative supplement when immediate affinity was already applied.");
            b.AppendLine("Unknown attribution stays unknown. Attributed claims are not verified world outcomes.");
            b.AppendLine("MoneyReceived, GiftReceived or ItemReceived may affect mood or affinity, but cannot establish trust. An ItemReceived record proves only that the listed item UID and quantity reached the observer; it does not prove gift intent, debt repayment, gratitude or any other purpose. A received transfer does not prove its purpose. Absence alone does not prove intent.");
            b.AppendLine("For AppointmentNoShow the native affinity penalty was already applied: affinityDelta must be 0. Mood may still change; reliability trust requires a relationship ledger row and an allowlisted event.");
            if (request.AlreadyAppliedEffectsMask != NpcExperienceAppliedEffectsMask.None)
                b.Append("Already-applied conversation effects mask=").Append(request.AlreadyAppliedEffectsMask)
                    .AppendLine("; moodDelta must be 0 when mood is masked. Affinity mask blocks positive and ordinary affinity effects.");
            b.AppendLine("Use the observer's actual trait combination, relationship and known context to appraise effect strength. Emotional resilience may soften an insult; do not treat repeated abuse or direct family threats as harmless by default. Do not invent missing traits.");
            b.AppendLine("Words said do not prove a future plan or action. Classify speech only from receipt-bound utterance and context. Quoted, reported, hypothetical, joking or ambiguous threats are not direct threats.");
            b.AppendLine("Candidate negative bounds are provisional and require scenario calibration. Positive affinity stays within supplied gradual-friendship credit.");
            b.AppendLine("Never invent legal charges, crime facts, quest outcomes, guards, removal authority or access restrictions. Choose only listed eligible consequences. Options are proposals, not world changes.");
            b.Append("event={origin=").Append(e.Origin).Append(",kind=").Append(kind).Append(",observer=").Append(Quote(e.ObserverId))
                .Append(",subject=").Append(Quote(e.SubjectPersonId)).Append(",canonicalId=").Append(Quote(request.CanonicalEvidenceId))
                .Append(",deduplicationKey=").Append(Quote(e.DeduplicationKey)).Append(",observedGameTime=")
                .Append(e.ObservedGameTime.ToString("R", CultureInfo.InvariantCulture)).Append(",responsibleActorAttribution=")
                .Append(e.ResponsibleActorAttribution).Append(",responsibleActor=").Append(actor).Append(",attributedSpeaker=")
                .Append(Quote(e.AttributedSpeakerId ?? "")).Append(",claimedResponsibleActor=").Append(Quote(e.ClaimedResponsibleActorId ?? ""))
                .Append(",observedWait=").Append(request.WaitedForAppointment.ToString().ToLowerInvariant()).AppendLine("}");
            b.Append("stablePersonality=").Append(Quote(request.Personality.Description())).AppendLine();
            if (e.Origin == NpcExperienceEventOrigin.NativeOutcome && e.NativeImpactDetails != null)
            {
                var impact = e.NativeImpactDetails;
                if (impact.Kind == NpcExperienceImpactKind.Health)
                    b.Append("nativeKnownHealthEpisode={initial=").Append(impact.InitialHealth.ToString(CultureInfo.InvariantCulture))
                        .Append(",final=").Append(impact.FinalHealth.ToString(CultureInfo.InvariantCulture))
                        .Append(",maximum=").Append(impact.MaximumHealth.ToString(CultureInfo.InvariantCulture))
                        .Append(",totalDamage=").Append(impact.TotalDamage.ToString(CultureInfo.InvariantCulture))
                        .Append(",totalHealing=").Append(impact.TotalHealing.ToString(CultureInfo.InvariantCulture))
                        .Append(",changeCount=").Append(impact.ChangeCount.ToString(CultureInfo.InvariantCulture)).AppendLine("}");
                else if (impact.Kind == NpcExperienceImpactKind.MonetaryTransfer)
                    b.Append("nativeKnownMonetaryTransfer={quantity=").Append(impact.Quantity.ToString(CultureInfo.InvariantCulture))
                        .Append(",currency=").Append(impact.Currency).AppendLine("}");
                else if (impact.Kind == NpcExperienceImpactKind.ItemTransfer)
                {
                    b.Append( e.EventKind == NpcExperienceEventKind.ItemReceived ? "nativeReceivedItem={retainedItemUid=" + impact.RetainedDestinationItemUid.ToString(CultureInfo.InvariantCulture) + ",quantity=" : "nativeKnownItemTransfer={quantity=")
                        .Append(impact.Quantity.ToString(CultureInfo.InvariantCulture))
                        .Append(",nativeValuation=").Append(impact.NativeValuationStatus == NpcExperienceNativeValuationStatus.Known ? "known" : "unknown");
                    if (impact.NativeValuationStatus == NpcExperienceNativeValuationStatus.Known)
                        b.Append(",nativeGoldValue=").Append(impact.NativeGoldValue.ToString(CultureInfo.InvariantCulture));
                    b.AppendLine("}");
                }
                else if (impact.Kind == NpcExperienceImpactKind.StatusOnset)
                    b.Append("nativeStatus={kind=").Append(impact.Status)
                        .Append(",key=").Append(Quote(impact.NativeEffectKey))
                        .Append(",onsetSequence=").Append(impact.StatusOnsetSequence.ToString(CultureInfo.InvariantCulture))
                        .AppendLine(",responsibleActor=Unknown}");
                b.AppendLine("Native impact metadata is the only allowed quantity/value source; an unknown item valuation stays unknown. Never estimate a price. A transfer alone does not prove a gift, debt repayment or motive.");
            }
            else if (e.Origin == NpcExperienceEventOrigin.NativeOutcome && e.EventKind.HasValue &&
                (e.EventKind.Value == NpcExperienceEventKind.MoneyReceived || e.EventKind.Value == NpcExperienceEventKind.GiftReceived))
                b.AppendLine("nativeTransferMagnitude=unknown; do not infer quantity, currency amount or item price.");
            b.Append("observerBackground=").Append(request.Personality.Background).Append("; statusAttitude=").Append(Quote(request.Personality.StatusAttitude)).AppendLine();
            b.Append("knownSpeakerRankId=").Append(Quote(request.KnownSpeakerRankId ?? "unknown")).AppendLine();
            b.Append("observedUtterance=").Append(Quote(request.ObservedUtterance ?? "")).AppendLine();
            b.Append("conversationContext=").Append(string.Join("; ", request.ConversationContext.Select(x =>
                "receipt=" + Quote(x.ReceiptId) + ",speaker=" + Quote(x.SpeakerId) + ",observedGameTime=" +
                x.ObservedGameTime.ToString("R", CultureInfo.InvariantCulture) + ",text=" + Quote(x.Text)))).AppendLine();
            b.Append("knownFamilyIds=").Append(string.Join(",", request.KnownFamilyIds.Select(Quote))).AppendLine();
            b.Append("currentMood=").Append(request.CurrentMood.ToString(CultureInfo.InvariantCulture)).Append("/100; moodRevision=").Append(request.ExpectedMoodRevision.ToString(CultureInfo.InvariantCulture)).AppendLine();
            b.Append("capturedGameTime=").Append(request.CapturedGameTime.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
            b.Append("affinity=").Append(request.CurrentAffinity.ToString(CultureInfo.InvariantCulture)).Append("/100; affinityRevision=").Append(request.ExpectedAffinityRevision.ToString(CultureInfo.InvariantCulture)).Append("; positiveAffinityCap=").Append(request.PositiveAffinityCredit.ToString(CultureInfo.InvariantCulture)).AppendLine();
            b.Append("relationship=").Append(relation).Append("; relationshipRevision=").Append(request.ExpectedRelationshipRevision.ToString(CultureInfo.InvariantCulture)).AppendLine();
            if (request.PlayerFearSuppressed)
                b.AppendLine("Verified native social rank suppresses player-directed fear: fearDelta must be exactly 0. This does not suppress separate witnessed-danger fear.");
            if (request.MotivationContext == null)
                b.AppendLine("motivationSnapshot=unavailable; do not infer missing needs, priorities, rank or authority.");
            else
                b.Append("motivationSnapshot=").Append(Quote(NpcMotivationContextFormatter.ForModels(request.MotivationContext))).AppendLine();
            AppendKnowledgeSnapshot(b, request.KnowledgeSnapshot);
            b.Append("commitments=").Append(string.Join("; ", request.Commitments.Select(x => Quote(x.Id) + "," + x.Kind + "," + x.Status + ",revision=" + x.Revision.ToString(CultureInfo.InvariantCulture)))).AppendLine();
            b.Append("eligibleConsequences=").Append(string.Join(",", request.EligibleConsequences)).AppendLine();
            b.Append("Bind response to saveBranchId=").Append(Quote(request.SaveBranchId)).Append(", observerId=").Append(Quote(request.ObserverId)).Append(", subjectPersonId=").Append(Quote(request.SubjectPersonId)).Append(", evidenceDeduplicationKey=").Append(Quote(request.EvidenceDeduplicationKey)).Append(", canonicalEvidenceId=").Append(Quote(request.CanonicalEvidenceId)).Append(", relationshipTargetId=").Append(Quote(request.RelationshipTargetId ?? "")).Append(", revisions=").Append(request.ExpectedMoodRevision).Append('/').Append(request.ExpectedAffinityRevision).Append('/').Append(request.ExpectedRelationshipRevision).AppendLine();
            b.Append("Return integer moodDelta, affinityDelta, trustDelta, resentmentDelta, fearDelta; string speechActKind, speechTargetPersonId, conversationOption. Never write state directly.");
            return b.ToString();
        }

        private static void AppendKnowledgeSnapshot(StringBuilder prompt, NpcKnowledgeSnapshot snapshot)
        {
            if (snapshot == null)
            {
                prompt.AppendLine("knowledgeSnapshot=unavailable; unknown stays unknown.");
                return;
            }
            prompt.Append("knowledgeSnapshot actor=").Append(Quote(snapshot.ActorId)).Append(" scope=")
                .Append(Quote(snapshot.SaveEpochId)).Append(" revision=")
                .Append(snapshot.Revision.ToString(CultureInfo.InvariantCulture)).Append(" publicTopics=");
            IList<NpcDisclosurePublicTopic> topics = snapshot.PublicTopicMetadata(64);
            if (topics.Count == 0) prompt.Append("none");
            else prompt.Append(string.Join(",", topics.Select(x => x.PropositionId + ":" + x.EpistemicKind)));
            prompt.AppendLine("; metadata only, not a complete fact list or knowledge-tool read; missing topics remain unknown; topic metadata is not proof of unrelated consequences.");
        }

        internal static bool ValidIdentity(string value, int maximum)
        { if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) return false; for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return false; return true; }
        private static string Range(int minimum, int maximum)
        { return "[" + minimum.ToString(CultureInfo.InvariantCulture) + "," + maximum.ToString(CultureInfo.InvariantCulture) + "]"; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool HasAny<T>(IEnumerable<T> source) { return source != null && source.Any(); }
        private static string Quote(string value)
        {
            if (value == null) return "null";
            var b = new StringBuilder(value.Length + 2).Append('"');
            foreach (char c in value) { if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c == '\n') b.Append("\\n"); else if (c == '\r') b.Append("\\r"); else if (c == '\t') b.Append("\\t"); else if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else b.Append(c); }
            return b.Append('"').ToString();
        }
        private static int Integer(Dictionary<string, object> values, string key) { long n = Long(values, key); if (n < int.MinValue || n > int.MaxValue) throw new FormatException(); return (int)n; }
        private static long Long(Dictionary<string, object> values, string key) { object x; if (!values.TryGetValue(key, out x) || !(x is long)) throw new FormatException(); return (long)x; }
        private static string String(Dictionary<string, object> values, string key) { object x; if (!values.TryGetValue(key, out x) || !(x is string)) throw new FormatException(); return (string)x; }
        private static T EnumValue<T>(Dictionary<string, object> values, string key) where T : struct
        { string s = String(values, key); T value; if (!Enum.TryParse<T>(s, false, out value) || !Enum.IsDefined(typeof(T), value) || Enum.GetName(typeof(T), value) != s) throw new FormatException(); return value; }

        private sealed class ScalarJsonReader
        {
            private readonly string text; private int index;
            public ScalarJsonReader(string text) { this.text = text; }
            public Dictionary<string, object> Read()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal); White(); Need('{'); White();
                if (Take('}')) { End(); return result; }
                while (true)
                {
                    White(); string key = ReadString(); White(); Need(':'); White();
                    if (result.ContainsKey(key)) throw new FormatException("Duplicate JSON property.");
                    object value = Peek() == '"' ? (object)ReadString() : ReadLong(); result.Add(key, value); White();
                    if (Take('}')) break; Need(',');
                }
                White(); End(); return result;
            }
            private string ReadString()
            {
                Need('"'); var b = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++]; if (c == '"') return b.ToString(); if (c < 0x20) throw new FormatException();
                    if (c != '\\') { b.Append(c); continue; }
                    if (index >= text.Length) throw new FormatException(); char e = text[index++];
                    switch (e) { case '"': b.Append('"'); break; case '\\': b.Append('\\'); break; case '/': b.Append('/'); break; case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break; case 'n': b.Append('\n'); break; case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break; case 'u': b.Append(ReadHex()); break; default: throw new FormatException(); }
                }
                throw new FormatException();
            }
            private char ReadHex() { if (index + 4 > text.Length) throw new FormatException(); int n = 0; for (int i = 0; i < 4; i++) { char c = text[index++]; int d = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1; if (d < 0) throw new FormatException(); n = n * 16 + d; } return (char)n; }
            private long ReadLong()
            {
                int start = index; if (Take('-') && index == text.Length) throw new FormatException();
                if (Take('0')) { if (index < text.Length && char.IsDigit(text[index])) throw new FormatException(); }
                else { int count = 0; while (index < text.Length && text[index] >= '0' && text[index] <= '9') { index++; count++; } if (count == 0) throw new FormatException(); }
                if (index < text.Length && (text[index] == '.' || text[index] == 'e' || text[index] == 'E')) throw new FormatException();
                return long.Parse(text.Substring(start, index - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            }
            private char Peek() { if (index >= text.Length) throw new FormatException(); return text[index]; }
            private void White() { while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n')) index++; }
            private bool Take(char c) { if (index < text.Length && text[index] == c) { index++; return true; } return false; }
            private void Need(char c) { if (!Take(c)) throw new FormatException(); }
            private void End() { if (index != text.Length) throw new FormatException(); }
        }
    }
}
