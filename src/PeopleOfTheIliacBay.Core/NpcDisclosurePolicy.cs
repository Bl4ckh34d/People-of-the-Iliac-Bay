using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcDisclosureEpistemicKind
    {
        VerifiedTruth = 1,
        AttributedBelief = 2,
        Rumor = 3,
        PlayerClaim = 4,
        Unknown = 5,
        DeliberateClaim = 6
    }

    public enum NpcDisclosureProtection
    {
        Public = 1,
        Private = 2,
        Confidential = 3,
        QuestProtected = 4
    }

    public enum NpcDisclosureOutcome
    {
        TruthfulAnswer = 1,
        AttributedUncertainty = 2,
        PartialAnswer = 3,
        Refusal = 4,
        DeliberateDeception = 5
    }

    /// <summary>Renderer-free metadata for choosing one actor-owned, already public topic.</summary>
    public sealed class NpcDisclosurePublicTopic
    {
        internal NpcDisclosurePublicTopic(string propositionId, NpcDisclosureEpistemicKind epistemicKind)
        { PropositionId = propositionId; EpistemicKind = epistemicKind; }
        public string PropositionId { get; private set; }
        public NpcDisclosureEpistemicKind EpistemicKind { get; private set; }
        internal static bool IsModelSelectableId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != ':' && c != '_' && c != '-' && c != '.') return false;
            }
            return true;
        }
    }

    /// <summary>One fixed-audience, public read. Evidence/source receipts are intentionally absent.</summary>
    public sealed class NpcDisclosureToolRead
    {
        internal NpcDisclosureToolRead(string actorId, string audienceId, long snapshotRevision,
            NpcDisclosureStatement statement)
        {
            ActorId = actorId; AudienceId = audienceId; SnapshotRevision = snapshotRevision;
            PropositionId = statement.PropositionId; ValueText = statement.ValueId;
            EpistemicKind = statement.EpistemicKind; AttributionId = statement.AttributionId;
        }
        public string ActorId { get; private set; }
        public string AudienceId { get; private set; }
        public long SnapshotRevision { get; private set; }
        public string PropositionId { get; private set; }
        public string ValueText { get; private set; }
        public NpcDisclosureEpistemicKind EpistemicKind { get; private set; }
        public string AttributionId { get; private set; }
    }

    /// <summary>A typed, actor-bound piece of knowledge or proposed claim. It is not freeform renderer text.</summary>
    public sealed class NpcDisclosureStatement
    {
        private NpcDisclosureStatement() { }
        public string Id { get; private set; }
        public string ActorId { get; private set; }
        public string PropositionId { get; private set; }
        public string ValueId { get; private set; }
        public NpcDisclosureEpistemicKind EpistemicKind { get; private set; }
        public NpcDisclosureProtection Protection { get; private set; }
        public string SourceId { get; private set; }
        public long SourceRevision { get; private set; }
        public double CapturedGameTime { get; private set; }
        public string AttributionId { get; private set; }
        public string EvidenceReceiptId { get; private set; }

        public static NpcDisclosureStatement Create(string id, string actorId, string propositionId, string valueId,
            NpcDisclosureEpistemicKind epistemicKind, NpcDisclosureProtection protection, string sourceId,
            long sourceRevision, double capturedGameTime, string attributionId = null, string evidenceReceiptId = null)
        {
            if (!ValidId(id, 128) || !ValidId(actorId, 240) || !ValidId(propositionId, 128) ||
                !ValidId(valueId, 128) || !ValidId(sourceId, 128) || sourceRevision < 0 ||
                !Finite(capturedGameTime) || capturedGameTime < 0 ||
                !Enum.IsDefined(typeof(NpcDisclosureEpistemicKind), epistemicKind) ||
                !Enum.IsDefined(typeof(NpcDisclosureProtection), protection) ||
                (attributionId != null && !ValidId(attributionId, 240)) ||
                (evidenceReceiptId != null && !ValidId(evidenceReceiptId, 128)))
                throw new ArgumentException("Invalid actor-known disclosure statement.");
            if ((epistemicKind == NpcDisclosureEpistemicKind.AttributedBelief ||
                 epistemicKind == NpcDisclosureEpistemicKind.Rumor ||
                 epistemicKind == NpcDisclosureEpistemicKind.PlayerClaim) && attributionId == null)
                throw new ArgumentException("Attributed knowledge requires an attribution identity.");
            if (epistemicKind == NpcDisclosureEpistemicKind.VerifiedTruth && attributionId != null)
                throw new ArgumentException("Verified truth cannot be relabeled as attributed speech.");
            return new NpcDisclosureStatement { Id = id, ActorId = actorId, PropositionId = propositionId,
                ValueId = valueId, EpistemicKind = epistemicKind, Protection = protection, SourceId = sourceId,
                SourceRevision = sourceRevision, CapturedGameTime = capturedGameTime, AttributionId = attributionId,
                EvidenceReceiptId = evidenceReceiptId };
        }

        internal static NpcDisclosureStatement Copy(NpcDisclosureStatement value)
        {
            if (value == null) throw new ArgumentException("Missing disclosure statement.");
            return Create(value.Id, value.ActorId, value.PropositionId, value.ValueId, value.EpistemicKind,
                value.Protection, value.SourceId, value.SourceRevision, value.CapturedGameTime, value.AttributionId,
                value.EvidenceReceiptId);
        }
        private static bool ValidId(string value, int max)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= max; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    /// <summary>
    /// A native-generated bounded option. NativeEligible and PermittedDeceptionIntent are trusted adapter inputs,
    /// never model-returned authorization flags. This type does not grant either permission.
    /// </summary>
    public sealed class NpcDisclosureChoice
    {
        private NpcDisclosureChoice() { }
        public string Id { get; private set; }
        public string ActorId { get; private set; }
        public NpcDisclosureOutcome Outcome { get; private set; }
        public IReadOnlyList<string> StatementIds { get; private set; }
        public bool NativeEligible { get; private set; }
        public bool PermittedDeceptionIntent { get; private set; }
        public string SourceId { get; private set; }
        public long SourceRevision { get; private set; }
        public double CapturedGameTime { get; private set; }
        public long SnapshotRevision { get; private set; }
        public IReadOnlyList<string> EvidenceStatementIds { get; private set; }
        public string AudienceId { get; private set; }
        public string MotiveId { get; private set; }
        public string UtteranceReceiptId { get; private set; }

        public static NpcDisclosureChoice Create(string id, string actorId, NpcDisclosureOutcome outcome, IEnumerable<string> statementIds,
            bool nativeEligible, bool permittedDeceptionIntent, string sourceId, long sourceRevision,
            double capturedGameTime, long snapshotRevision, IEnumerable<string> evidenceStatementIds,
            string audienceId, string motiveId, string utteranceReceiptId)
        {
            if (!ValidId(id, 128) || !ValidId(actorId, 240) || !Enum.IsDefined(typeof(NpcDisclosureOutcome), outcome) ||
                !ValidId(sourceId, 128) || sourceRevision < 0 || snapshotRevision < 0 ||
                !Finite(capturedGameTime) || capturedGameTime < 0 || statementIds == null || evidenceStatementIds == null ||
                !ValidId(audienceId, 240) || !ValidId(utteranceReceiptId, 128) ||
                (motiveId != null && !ValidId(motiveId, 128)))
                throw new ArgumentException("Invalid native disclosure choice.");
            var copied = new List<string>();
            foreach (string statementId in statementIds)
            {
                if (!ValidId(statementId, 128) || copied.Count >= NpcDisclosurePolicy.MaximumStatements)
                    throw new ArgumentException("Disclosure choice exceeds its statement bound or contains an invalid identity.");
                copied.Add(statementId);
            }
            var evidence = new List<string>();
            var evidenceSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (string evidenceId in evidenceStatementIds)
            {
                if (!ValidId(evidenceId, 128) || evidence.Count >= NpcDisclosurePolicy.MaximumStatements ||
                    !evidenceSet.Add(evidenceId))
                    throw new ArgumentException("Disclosure evidence exceeds its bound or contains an invalid identity.");
                evidence.Add(evidenceId);
            }
            if (outcome == NpcDisclosureOutcome.Refusal && copied.Count != 0)
                throw new ArgumentException("A refusal cannot carry renderer statements.");
            if (outcome != NpcDisclosureOutcome.Refusal && copied.Count == 0)
                throw new ArgumentException("A non-refusal disclosure choice requires a statement.");
            if (outcome != NpcDisclosureOutcome.DeliberateDeception && permittedDeceptionIntent)
                throw new ArgumentException("Deception intent only applies to a deliberate deception choice.");
            if (outcome == NpcDisclosureOutcome.DeliberateDeception && !ValidId(motiveId, 128))
                throw new ArgumentException("Deliberate deception requires a native motive identity.");
            return new NpcDisclosureChoice { Id = id, ActorId = actorId, Outcome = outcome,
                StatementIds = new ReadOnlyCollection<string>(copied), NativeEligible = nativeEligible,
                PermittedDeceptionIntent = permittedDeceptionIntent, SourceId = sourceId,
                SourceRevision = sourceRevision, CapturedGameTime = capturedGameTime, SnapshotRevision = snapshotRevision,
                EvidenceStatementIds = new ReadOnlyCollection<string>(evidence), AudienceId = audienceId,
                MotiveId = motiveId, UtteranceReceiptId = utteranceReceiptId };
        }
        private static bool ValidId(string value, int max)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= max; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    /// <summary>Detached actor-knowledge snapshot supplied by a native adapter.</summary>
    public sealed class NpcDisclosureRequest
    {
        private NpcDisclosureRequest() { }
        public string ActorId { get; private set; }
        public long SnapshotRevision { get; private set; }
        public double CapturedGameTime { get; private set; }
        public IReadOnlyList<NpcDisclosureStatement> Statements { get; private set; }
        public IReadOnlyList<NpcDisclosureChoice> Choices { get; private set; }

        public static NpcDisclosureRequest Create(string actorId, long snapshotRevision, double capturedGameTime,
            IEnumerable<NpcDisclosureStatement> statements, IEnumerable<NpcDisclosureChoice> choices, string audienceId)
        {
            if (!ValidId(actorId, 240) || snapshotRevision < 0 || !Finite(capturedGameTime) || capturedGameTime < 0 ||
                statements == null || choices == null || !ValidId(audienceId, 240))
                throw new ArgumentException("Invalid actor knowledge snapshot.");
            var statementCopies = new List<NpcDisclosureStatement>();
            foreach (NpcDisclosureStatement statement in statements)
            {
                if (statementCopies.Count >= NpcDisclosurePolicy.MaximumStatements)
                    throw new ArgumentException("Actor knowledge exceeds its statement bound.");
                NpcDisclosureStatement copy = NpcDisclosureStatement.Copy(statement);
                if (copy.CapturedGameTime > capturedGameTime)
                    throw new ArgumentException("A statement cannot be captured after its knowledge snapshot.");
                statementCopies.Add(copy);
            }
            var choiceCopies = new List<NpcDisclosureChoice>();
            var receiptIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcDisclosureChoice choice in choices)
            {
                if (choice == null || choiceCopies.Count >= NpcDisclosurePolicy.MaximumChoices)
                    throw new ArgumentException("Invalid or excessive disclosure choices.");
                NpcDisclosureChoice copy = CopyChoice(choice);
                if (copy.CapturedGameTime > capturedGameTime)
                    throw new ArgumentException("A disclosure choice cannot be captured after its knowledge snapshot.");
                if (!receiptIds.Add(copy.UtteranceReceiptId))
                    throw new ArgumentException("Each disclosure choice requires a unique utterance receipt identity.");
                choiceCopies.Add(copy);
            }
            return new NpcDisclosureRequest { ActorId = actorId, SnapshotRevision = snapshotRevision,
                CapturedGameTime = capturedGameTime, AudienceId = audienceId,
                Statements = new ReadOnlyCollection<NpcDisclosureStatement>(statementCopies),
                Choices = new ReadOnlyCollection<NpcDisclosureChoice>(choiceCopies) };
        }

        public string AudienceId { get; private set; }
        private static NpcDisclosureChoice CopyChoice(NpcDisclosureChoice choice)
        {
            return NpcDisclosureChoice.Create(choice.Id, choice.ActorId, choice.Outcome, choice.StatementIds,
                choice.NativeEligible, choice.PermittedDeceptionIntent, choice.SourceId, choice.SourceRevision,
                choice.CapturedGameTime, choice.SnapshotRevision, choice.EvidenceStatementIds,
                choice.AudienceId, choice.MotiveId, choice.UtteranceReceiptId);
        }
        private static bool ValidId(string value, int max)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= max; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    /// <summary>The two outputs of disclosure validation, kept distinct for renderer and audit consumers.</summary>
    public sealed class NpcDisclosureDecision
    {
        internal NpcDisclosureDecision(NpcDisclosureChoice choice,
            IList<NpcDisclosureStatement> statements, IList<NpcDisclosureStatement> evidenceStatements)
        {
            var copies = new List<NpcDisclosureStatement>();
            foreach (NpcDisclosureStatement statement in statements) copies.Add(NpcDisclosureStatement.Copy(statement));
            RendererPayload = new NpcDisclosureRendererPayload(choice.AudienceId, choice.Outcome,
                choice.UtteranceReceiptId, copies);
            Trace = new NpcDisclosureDecisionTrace(choice, evidenceStatements);
        }
        /// <summary>Pass only this selected public payload to a renderer.</summary>
        public NpcDisclosureRendererPayload RendererPayload { get; private set; }
    /// <summary>Retain this native trace for receipt/memory adapters; it contains source-bound references, not fact values.</summary>
        public NpcDisclosureDecisionTrace Trace { get; private set; }
    }

    public sealed class NpcDisclosureRendererStatement
    {
        internal NpcDisclosureRendererStatement(NpcDisclosureStatement statement)
        {
            Id = statement.Id; PropositionId = statement.PropositionId; ValueId = statement.ValueId;
            EpistemicKind = statement.EpistemicKind; AttributionId = statement.AttributionId;
        }
        public string Id { get; private set; }
        public string PropositionId { get; private set; }
        public string ValueId { get; private set; }
        public NpcDisclosureEpistemicKind EpistemicKind { get; private set; }
        public string AttributionId { get; private set; }
    }

    public sealed class NpcDisclosureRendererPayload
    {
        internal NpcDisclosureRendererPayload(string audienceId, NpcDisclosureOutcome outcome,
            string utteranceReceiptId, IList<NpcDisclosureStatement> statements)
        {
            AudienceId = audienceId; Outcome = outcome; UtteranceReceiptId = utteranceReceiptId;
            var copies = new List<NpcDisclosureRendererStatement>();
            foreach (NpcDisclosureStatement statement in statements) copies.Add(new NpcDisclosureRendererStatement(statement));
            Statements = new ReadOnlyCollection<NpcDisclosureRendererStatement>(copies);
        }
        public string AudienceId { get; private set; }
        public NpcDisclosureOutcome Outcome { get; private set; }
        public string UtteranceReceiptId { get; private set; }
        /// <summary>Only selected, public statement facts are present.</summary>
        public IReadOnlyList<NpcDisclosureRendererStatement> Statements { get; private set; }
    }

    public sealed class NpcDisclosureDecisionTrace
    {
        internal NpcDisclosureDecisionTrace(NpcDisclosureChoice choice, IList<NpcDisclosureStatement> evidenceStatements)
        {
            ChoiceId = choice.Id; ActorId = choice.ActorId; AudienceId = choice.AudienceId; Outcome = choice.Outcome;
            DecisionSourceId = choice.SourceId; DecisionSourceRevision = choice.SourceRevision;
            MotiveId = choice.MotiveId; UtteranceReceiptId = choice.UtteranceReceiptId;
            SnapshotRevision = choice.SnapshotRevision; CapturedGameTime = choice.CapturedGameTime;
            var references = new List<NpcDisclosureEvidenceReference>();
            foreach (NpcDisclosureStatement statement in evidenceStatements)
                references.Add(new NpcDisclosureEvidenceReference(statement));
            EvidenceReferences = new ReadOnlyCollection<NpcDisclosureEvidenceReference>(references);
        }
        public string ChoiceId { get; private set; }
        public string ActorId { get; private set; }
        public string AudienceId { get; private set; }
        public NpcDisclosureOutcome Outcome { get; private set; }
        public string DecisionSourceId { get; private set; }
        public long DecisionSourceRevision { get; private set; }
        public string MotiveId { get; private set; }
        /// <summary>Native utterance receipt identity, to bind later speech memory/correction without writing history here.</summary>
        public string UtteranceReceiptId { get; private set; }
        public long SnapshotRevision { get; private set; }
        public double CapturedGameTime { get; private set; }
        /// <summary>Actor-bound, source-versioned references; proposition and value content are not copied into this trace.</summary>
        public IReadOnlyList<NpcDisclosureEvidenceReference> EvidenceReferences { get; private set; }
    }

    public sealed class NpcDisclosureEvidenceReference
    {
        internal NpcDisclosureEvidenceReference(NpcDisclosureStatement statement)
        {
            StatementId = statement.Id; ActorId = statement.ActorId; SourceId = statement.SourceId;
            SourceRevision = statement.SourceRevision; CapturedGameTime = statement.CapturedGameTime;
            EvidenceReceiptId = statement.EvidenceReceiptId;
        }
        public string StatementId { get; private set; }
        public string ActorId { get; private set; }
        public string SourceId { get; private set; }
        public long SourceRevision { get; private set; }
        public double CapturedGameTime { get; private set; }
        public string EvidenceReceiptId { get; private set; }
    }

    /// <summary>Validates disclosure choice and enforces the native fact boundary before rendering.</summary>
    public static class NpcDisclosurePolicy
    {
        public const int MaximumStatements = 64;
        public const int MaximumChoices = 32;
        public const string PlayerAudienceId = "player";

        /// <summary>Reads one exact public topic from the named actor's snapshot; protected and missing topics match.</summary>
        public static bool TryReadPublicToolTopic(NpcKnowledgeSnapshot snapshot, string actorId, string audienceId,
            string propositionId, out NpcDisclosureToolRead read)
        {
            read = null;
            if (snapshot == null || string.IsNullOrWhiteSpace(actorId) ||
                !string.Equals(snapshot.ActorId, actorId, StringComparison.Ordinal) ||
                !string.Equals(audienceId, PlayerAudienceId, StringComparison.Ordinal) ||
                !NpcDisclosurePublicTopic.IsModelSelectableId(propositionId)) return false;
            NpcKnowledgeFactView fact;
            if (!snapshot.TryGet(propositionId, out fact) || fact == null || fact.Statement == null ||
                !string.Equals(fact.Statement.ActorId, actorId, StringComparison.Ordinal) ||
                fact.Statement.Protection != NpcDisclosureProtection.Public ||
                (fact.Statement.EpistemicKind != NpcDisclosureEpistemicKind.VerifiedTruth &&
                 fact.Statement.EpistemicKind != NpcDisclosureEpistemicKind.AttributedBelief &&
                 fact.Statement.EpistemicKind != NpcDisclosureEpistemicKind.Rumor &&
                 fact.Statement.EpistemicKind != NpcDisclosureEpistemicKind.PlayerClaim)) return false;
            read = new NpcDisclosureToolRead(actorId, audienceId, snapshot.Revision, fact.Statement);
            return true;
        }

        public static bool TrySelect(NpcDisclosureRequest request, string choiceId, out NpcDisclosureDecision decision)
        {
            decision = null;
            if (request == null || string.IsNullOrWhiteSpace(choiceId) || choiceId.Length > 128) return false;
            NpcDisclosureChoice choice = null;
            int choiceMatches = 0;
            foreach (NpcDisclosureChoice candidate in request.Choices)
                if (candidate != null && string.Equals(candidate.Id, choiceId, StringComparison.Ordinal))
                { choice = candidate; choiceMatches++; }
            if (choiceMatches != 1 || choice == null || !choice.NativeEligible ||
                !string.Equals(choice.ActorId, request.ActorId, StringComparison.Ordinal) ||
                choice.SnapshotRevision != request.SnapshotRevision || choice.CapturedGameTime > request.CapturedGameTime ||
                !string.Equals(choice.AudienceId, request.AudienceId, StringComparison.Ordinal))
                return false;

            var evidence = new List<NpcDisclosureStatement>();
            var evidenceIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in choice.EvidenceStatementIds)
            {
                if (!evidenceIds.Add(id)) return false;
                NpcDisclosureStatement fact = FindStatement(request, id);
                if (fact == null || !string.Equals(fact.ActorId, request.ActorId, StringComparison.Ordinal) ||
                    fact.CapturedGameTime > choice.CapturedGameTime) return false;
                evidence.Add(fact);
            }
            var selected = new List<NpcDisclosureStatement>();
            var selectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in choice.StatementIds)
            {
                if (!selectedIds.Add(id)) return false;
                NpcDisclosureStatement statement = FindStatement(request, id);
                if (statement == null || !string.Equals(statement.ActorId, request.ActorId, StringComparison.Ordinal) ||
                    statement.Protection != NpcDisclosureProtection.Public || !evidenceIds.Contains(statement.Id)) return false;
                selected.Add(statement);
            }
            if (choice.Outcome == NpcDisclosureOutcome.Refusal)
            {
                if (selected.Count != 0) return false;
            }
            else if (selected.Count == 0) return false;

            switch (choice.Outcome)
            {
                case NpcDisclosureOutcome.TruthfulAnswer:
                    for (int i = 0; i < selected.Count; i++)
                        if (selected[i].EpistemicKind != NpcDisclosureEpistemicKind.VerifiedTruth) return false;
                    break;
                case NpcDisclosureOutcome.AttributedUncertainty:
                    for (int i = 0; i < selected.Count; i++)
                        if (!IsAttributed(selected[i].EpistemicKind)) return false;
                    break;
                case NpcDisclosureOutcome.PartialAnswer:
                    for (int i = 0; i < selected.Count; i++)
                        if (selected[i].EpistemicKind == NpcDisclosureEpistemicKind.Unknown ||
                            selected[i].EpistemicKind == NpcDisclosureEpistemicKind.DeliberateClaim) return false;
                    break;
                case NpcDisclosureOutcome.Refusal:
                    break;
                case NpcDisclosureOutcome.DeliberateDeception:
                    if (!choice.PermittedDeceptionIntent) return false;
                    for (int i = 0; i < selected.Count; i++)
                    {
                        NpcDisclosureStatement claim = selected[i];
                        if (claim.EpistemicKind != NpcDisclosureEpistemicKind.DeliberateClaim ||
                            !HasPublicConflict(request, choice, claim)) return false;
                    }
                    break;
                default:
                    return false;
            }
            decision = new NpcDisclosureDecision(choice, selected, evidence);
            return true;
        }

        private static bool IsAttributed(NpcDisclosureEpistemicKind kind)
        {
            return kind == NpcDisclosureEpistemicKind.AttributedBelief || kind == NpcDisclosureEpistemicKind.Rumor ||
                kind == NpcDisclosureEpistemicKind.PlayerClaim;
        }
        private static NpcDisclosureStatement FindStatement(NpcDisclosureRequest request, string id)
        {
            NpcDisclosureStatement result = null;
            int matches = 0;
            foreach (NpcDisclosureStatement statement in request.Statements)
                if (statement != null && string.Equals(statement.Id, id, StringComparison.Ordinal))
                { result = statement; matches++; }
            return matches == 1 ? result : null;
        }
        private static bool HasPublicConflict(NpcDisclosureRequest request, NpcDisclosureChoice choice, NpcDisclosureStatement claim)
        {
            foreach (string evidenceId in choice.EvidenceStatementIds)
            {
                NpcDisclosureStatement known = FindStatement(request, evidenceId);
                if (known != null && string.Equals(known.ActorId, request.ActorId, StringComparison.Ordinal) &&
                    known.Protection == NpcDisclosureProtection.Public &&
                    known.EpistemicKind == NpcDisclosureEpistemicKind.VerifiedTruth &&
                    string.Equals(known.PropositionId, claim.PropositionId, StringComparison.Ordinal) &&
                    !string.Equals(known.ValueId, claim.ValueId, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}


