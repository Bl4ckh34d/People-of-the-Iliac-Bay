using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcMotivationStateEvidenceKind
    {
        NativeObservation = 1, NativeKnowledge = 2, ConversationReceipt = 3,
        AuthorizedDecisionIntent = 4, AuthoredRecord = 5, CommitmentLedger = 6, PersonalityProfile = 7
    }

    /// <summary>Persistent source identity. Save-branch is deliberately supplied only at runtime capture.</summary>
    [Serializable]
    public sealed class NpcMotivationStateEvidence
    {
        public string ObserverId;
        public string EvidenceId;
        public NpcMotivationStateEvidenceKind Kind;
        public long Revision;
        public double ObservedGameTime;

        public NpcMotivationStateEvidence(string observerId, string evidenceId, NpcMotivationStateEvidenceKind kind,
            long revision, double observedGameTime)
        {
            ObserverId = observerId; EvidenceId = evidenceId; Kind = kind; Revision = revision; ObservedGameTime = observedGameTime;
        }

        internal static NpcMotivationStateEvidence CopyValidated(NpcMotivationStateEvidence source, string owner)
        {
            if (source == null || !NpcMotivationEvidenceRef.ValidId(source.ObserverId, 240) ||
                !string.Equals(source.ObserverId, owner, StringComparison.Ordinal) ||
                !NpcMotivationEvidenceRef.ValidId(source.EvidenceId, 240) ||
                !Enum.IsDefined(typeof(NpcMotivationStateEvidenceKind), source.Kind) || source.Revision < 0 ||
                !NpcMotivationEvidenceRef.Finite(source.ObservedGameTime) || source.ObservedGameTime < 0)
                throw new ArgumentException("Invalid persistent motivation evidence.");
            return new NpcMotivationStateEvidence(source.ObserverId, source.EvidenceId, source.Kind, source.Revision, source.ObservedGameTime);
        }

        internal NpcMotivationEvidenceRef ToContextEvidence(string branch)
        {
            return new NpcMotivationEvidenceRef(branch, ObserverId, EvidenceId, ContextKind(Kind), Revision, ObservedGameTime);
        }

        internal static NpcMotivationEvidenceKind ContextKind(NpcMotivationStateEvidenceKind kind)
        {
            switch (kind)
            {
                case NpcMotivationStateEvidenceKind.NativeObservation:
                case NpcMotivationStateEvidenceKind.NativeKnowledge: return NpcMotivationEvidenceKind.NativeObservation;
                case NpcMotivationStateEvidenceKind.ConversationReceipt: return NpcMotivationEvidenceKind.ConversationReceipt;
                case NpcMotivationStateEvidenceKind.AuthorizedDecisionIntent:
                case NpcMotivationStateEvidenceKind.AuthoredRecord: return NpcMotivationEvidenceKind.AuthoredRecord;
                case NpcMotivationStateEvidenceKind.CommitmentLedger: return NpcMotivationEvidenceKind.CommitmentLedger;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }

    [Serializable]
    public sealed class NpcMotivationNeedState
    {
        public string Id; public NpcMotivationNeedKind Kind; public NpcMotivationKnowledgeState KnowledgeState;
        public int? Intensity; public long Revision; public long StateRevision; public double UpdatedAtGameTime; public NpcMotivationStateEvidence Evidence;
        public NpcMotivationNeedState(string id, NpcMotivationNeedKind kind, NpcMotivationKnowledgeState state,
            int? intensity, long revision, double updatedAtGameTime, NpcMotivationStateEvidence evidence)
        { Id = id; Kind = kind; KnowledgeState = state; Intensity = intensity; Revision = revision; StateRevision = revision; UpdatedAtGameTime = updatedAtGameTime; Evidence = evidence; }
        internal NpcMotivationNeed ToContext(string branch)
        { return new NpcMotivationNeed(Id, Kind, KnowledgeState, Intensity, Revision, Evidence == null ? null : Evidence.ToContextEvidence(branch)); }
        internal static NpcMotivationNeedState CopyValidated(NpcMotivationNeedState row, string owner, long maxRevision)
        {
            if (row == null || !NpcMotivationEvidenceRef.ValidId(row.Id, 128) || !Enum.IsDefined(typeof(NpcMotivationNeedKind), row.Kind) ||
                row.Revision < 0 || row.StateRevision < 1 || row.StateRevision > maxRevision || !NpcMotivationEvidenceRef.Finite(row.UpdatedAtGameTime) || row.UpdatedAtGameTime < 0)
                throw new ArgumentException("Invalid need state row.");
            var evidence = row.Evidence == null ? null : NpcMotivationStateEvidence.CopyValidated(row.Evidence, owner);
            ValidateNeed(row.KnowledgeState, row.Intensity, evidence, row.Revision, row.UpdatedAtGameTime);
            var copy = new NpcMotivationNeedState(row.Id, row.Kind, row.KnowledgeState, row.Intensity, row.Revision, row.UpdatedAtGameTime, evidence);
            copy.StateRevision = row.StateRevision; return copy;
        }
        internal static void ValidateNeed(NpcMotivationKnowledgeState state, int? intensity, NpcMotivationStateEvidence evidence, long revision, double time)
        {
            if (!Enum.IsDefined(typeof(NpcMotivationKnowledgeState), state) || (intensity.HasValue && (intensity.Value < 0 || intensity.Value > 100)))
                throw new ArgumentException("Invalid need knowledge or intensity.");
            if (state == NpcMotivationKnowledgeState.Unknown)
            { if (intensity.HasValue || evidence != null) throw new ArgumentException("Unknown needs carry no value or evidence."); return; }
            if (!intensity.HasValue || evidence == null || evidence.Revision != revision || evidence.ObservedGameTime > time ||
                (state == NpcMotivationKnowledgeState.AttributedClaim && evidence.Kind != NpcMotivationStateEvidenceKind.ConversationReceipt) ||
                (state == NpcMotivationKnowledgeState.Known && evidence.Kind != NpcMotivationStateEvidenceKind.NativeObservation &&
                    evidence.Kind != NpcMotivationStateEvidenceKind.NativeKnowledge))
                throw new ArgumentException("Need provenance does not support its knowledge state.");
        }
    }

    [Serializable]
    public sealed class NpcMotivationGoalState
    {
        public string Id; public NpcMotivationGoalKind Kind; public NpcMotivationGoalStatus Status;
        public NpcMotivationPriorityLevel Priority; public NpcMotivationKnowledgeState KnowledgeState;
        public long Revision; public long StateRevision; public long GoalStartRevision; public double UpdatedAtGameTime; public NpcMotivationStateEvidence Evidence;
        public NpcMotivationGoalState(string id, NpcMotivationGoalKind kind, NpcMotivationGoalStatus status,
            NpcMotivationPriorityLevel priority, NpcMotivationKnowledgeState state, long revision,
            double updatedAtGameTime, NpcMotivationStateEvidence evidence)
        { Id = id; Kind = kind; Status = status; Priority = priority; KnowledgeState = state; Revision = revision; StateRevision = revision; UpdatedAtGameTime = updatedAtGameTime; Evidence = evidence; }
        internal NpcMotivationGoal ToContext(string branch)
        { return new NpcMotivationGoal(Id, Kind, Status, Priority, KnowledgeState, Revision, Evidence == null ? null : Evidence.ToContextEvidence(branch)); }
        internal static NpcMotivationGoalState CopyValidated(NpcMotivationGoalState row, string owner, long maxRevision, bool allowLegacyGeneration = false)
        {
            if (row == null || !NpcMotivationEvidenceRef.ValidId(row.Id, 128) || !Enum.IsDefined(typeof(NpcMotivationGoalKind), row.Kind) ||
                !Enum.IsDefined(typeof(NpcMotivationGoalStatus), row.Status) || !Enum.IsDefined(typeof(NpcMotivationPriorityLevel), row.Priority) ||
                row.Revision < 0 || row.StateRevision < 1 || row.StateRevision > maxRevision || !NpcMotivationEvidenceRef.Finite(row.UpdatedAtGameTime) || row.UpdatedAtGameTime < 0)
                throw new ArgumentException("Invalid goal state row.");
            var evidence = row.Evidence == null ? null : NpcMotivationStateEvidence.CopyValidated(row.Evidence, owner);
            ValidateGoal(row.Status, row.Priority, row.KnowledgeState, evidence, row.Revision, row.UpdatedAtGameTime);
            if (row.GoalStartRevision < 0 || row.GoalStartRevision > row.StateRevision ||
                (!allowLegacyGeneration && row.Status != NpcMotivationGoalStatus.Unknown && row.GoalStartRevision == 0) ||
                (row.Status == NpcMotivationGoalStatus.Unknown && row.GoalStartRevision != 0))
                throw new ArgumentException("Invalid goal generation.");
            var copy = new NpcMotivationGoalState(row.Id, row.Kind, row.Status, row.Priority, row.KnowledgeState, row.Revision, row.UpdatedAtGameTime, evidence);
            copy.StateRevision = row.StateRevision; copy.GoalStartRevision = row.GoalStartRevision; return copy;
        }
        internal static void ValidateGoal(NpcMotivationGoalStatus status, NpcMotivationPriorityLevel priority,
            NpcMotivationKnowledgeState state, NpcMotivationStateEvidence evidence, long revision, double time)
        {
            if (!Enum.IsDefined(typeof(NpcMotivationKnowledgeState), state)) throw new ArgumentException("Invalid goal knowledge state.");
            if (state == NpcMotivationKnowledgeState.Unknown)
            { if (status != NpcMotivationGoalStatus.Unknown || priority != NpcMotivationPriorityLevel.Routine || evidence != null) throw new ArgumentException("Unknown goals carry no inferred priority or evidence."); return; }
            if (status == NpcMotivationGoalStatus.Unknown || evidence == null || evidence.Revision != revision || evidence.ObservedGameTime > time ||
                (state == NpcMotivationKnowledgeState.AttributedClaim) ||
                (evidence.Kind == NpcMotivationStateEvidenceKind.AuthorizedDecisionIntent && status != NpcMotivationGoalStatus.Active && status != NpcMotivationGoalStatus.Suspended) ||
                (evidence.Kind != NpcMotivationStateEvidenceKind.AuthorizedDecisionIntent && evidence.Kind != NpcMotivationStateEvidenceKind.NativeObservation &&
                 evidence.Kind != NpcMotivationStateEvidenceKind.NativeKnowledge && evidence.Kind != NpcMotivationStateEvidenceKind.CommitmentLedger && evidence.Kind != NpcMotivationStateEvidenceKind.AuthoredRecord))
                throw new ArgumentException("Goal evidence cannot authorize this goal state.");
        }
    }

    [Serializable]
    public sealed class NpcMotivationPriorityState
    {
        public string Id; public string SubjectId; public NpcMotivationPriorityKind Kind; public int Rank;
        public NpcMotivationKnowledgeState KnowledgeState; public long Revision; public long StateRevision; public double UpdatedAtGameTime;
        public NpcMotivationStateEvidence Evidence;
        public NpcMotivationPriorityState(string id, string subjectId, NpcMotivationPriorityKind kind, int rank,
            NpcMotivationKnowledgeState state, long revision, double updatedAtGameTime, NpcMotivationStateEvidence evidence)
        { Id = id; SubjectId = subjectId; Kind = kind; Rank = rank; KnowledgeState = state; Revision = revision; StateRevision = revision; UpdatedAtGameTime = updatedAtGameTime; Evidence = evidence; }
        internal NpcMotivationPriority ToContext(string branch)
        { return new NpcMotivationPriority(Id, SubjectId, Kind, Rank, KnowledgeState, Revision, Evidence == null ? null : Evidence.ToContextEvidence(branch)); }
        internal static NpcMotivationPriorityState CopyValidated(NpcMotivationPriorityState row, string owner, long maxRevision)
        {
            if (row == null || !NpcMotivationEvidenceRef.ValidId(row.Id, 128) || !NpcMotivationEvidenceRef.ValidId(row.SubjectId, 128) ||
                !Enum.IsDefined(typeof(NpcMotivationPriorityKind), row.Kind) || row.Revision < 0 || row.StateRevision < 1 || row.StateRevision > maxRevision ||
                !NpcMotivationEvidenceRef.Finite(row.UpdatedAtGameTime) || row.UpdatedAtGameTime < 0 || row.Rank < 0 || row.Rank > 100)
                throw new ArgumentException("Invalid priority state row.");
            var evidence = row.Evidence == null ? null : NpcMotivationStateEvidence.CopyValidated(row.Evidence, owner);
            if (!Enum.IsDefined(typeof(NpcMotivationKnowledgeState), row.KnowledgeState) ||
                (row.KnowledgeState == NpcMotivationKnowledgeState.Unknown && (row.Rank != 0 || evidence != null)) ||
                (row.KnowledgeState == NpcMotivationKnowledgeState.AttributedClaim) ||
                (row.KnowledgeState == NpcMotivationKnowledgeState.Known && (evidence == null || evidence.Revision != row.Revision || evidence.ObservedGameTime > row.UpdatedAtGameTime ||
                    (evidence.Kind != NpcMotivationStateEvidenceKind.AuthorizedDecisionIntent && evidence.Kind != NpcMotivationStateEvidenceKind.AuthoredRecord &&
                     evidence.Kind != NpcMotivationStateEvidenceKind.CommitmentLedger && evidence.Kind != NpcMotivationStateEvidenceKind.PersonalityProfile))))
                throw new ArgumentException("Priority provenance is invalid.");
            var copy = new NpcMotivationPriorityState(row.Id, row.SubjectId, row.Kind, row.Rank, row.KnowledgeState, row.Revision, row.UpdatedAtGameTime, evidence);
            copy.StateRevision = row.StateRevision; return copy;
        }
    }

    [Serializable]
    public sealed class NpcAcceptedMotivationUpdate
    {
        public string OperationId; public long Revision; public double AppliedAtGameTime;
        public string RowId; public string OwnerPersonId; public string EvidenceId; public NpcMotivationStateChangeKind ChangeKind;
        // True only on the accepted update that begins a goal lifetime (or a conservative legacy anchor).
        public bool GoalGenerationOrigin;
        public long GoalStartRevision;
        public NpcAcceptedMotivationUpdate(string operationId, long revision, double appliedAtGameTime, string rowId, string ownerPersonId,
            string evidenceId, NpcMotivationStateChangeKind changeKind, bool goalGenerationOrigin = false, long goalStartRevision = 0)
        { OperationId = operationId; Revision = revision; AppliedAtGameTime = appliedAtGameTime; RowId = rowId; OwnerPersonId = ownerPersonId; EvidenceId = evidenceId; ChangeKind = changeKind; GoalGenerationOrigin = goalGenerationOrigin; GoalStartRevision = goalStartRevision; }
    }

    public enum NpcMotivationStateChangeKind { Need = 1, Goal = 2, Priority = 3 }

    public sealed class NpcMotivationStateUpdateProposal
    {
        public string OwnerPersonId { get; private set; } public long ExpectedRevision { get; private set; }
        public string OperationId { get; private set; } public double GameTime { get; private set; }
        public NpcMotivationStateChangeKind ChangeKind { get; private set; }
        public NpcMotivationNeedState Need { get; private set; } public NpcMotivationGoalState Goal { get; private set; }
        public NpcMotivationPriorityState Priority { get; private set; }
        private NpcMotivationStateUpdateProposal(string owner, long expected, string operationId, double gameTime,
            NpcMotivationStateChangeKind kind, NpcMotivationNeedState need, NpcMotivationGoalState goal, NpcMotivationPriorityState priority)
        { OwnerPersonId = owner; ExpectedRevision = expected; OperationId = operationId; GameTime = gameTime; ChangeKind = kind; Need = need; Goal = goal; Priority = priority; }
        public static NpcMotivationStateUpdateProposal ForNeed(string owner, long expected, string operationId, double gameTime,
            string id, NpcMotivationNeedKind kind, NpcMotivationKnowledgeState state, int? intensity, NpcMotivationStateEvidence evidence)
        { var row = new NpcMotivationNeedState(id, kind, state, intensity, evidence == null ? expected + 1 : evidence.Revision, gameTime, evidence) { StateRevision = expected + 1 }; return New(owner, expected, operationId, gameTime, NpcMotivationStateChangeKind.Need, row, null, null); }
        public static NpcMotivationStateUpdateProposal ForGoal(string owner, long expected, string operationId, double gameTime,
            string id, NpcMotivationGoalKind kind, NpcMotivationGoalStatus status, NpcMotivationPriorityLevel priority,
            NpcMotivationKnowledgeState state, NpcMotivationStateEvidence evidence)
        { var row = new NpcMotivationGoalState(id, kind, status, priority, state, evidence == null ? expected + 1 : evidence.Revision, gameTime, evidence) { StateRevision = expected + 1 }; return New(owner, expected, operationId, gameTime, NpcMotivationStateChangeKind.Goal, null, row, null); }
        public static NpcMotivationStateUpdateProposal ForPriority(string owner, long expected, string operationId, double gameTime,
            string id, string subjectId, NpcMotivationPriorityKind kind, int rank, NpcMotivationKnowledgeState state, NpcMotivationStateEvidence evidence)
        { var row = new NpcMotivationPriorityState(id, subjectId, kind, rank, state, evidence == null ? expected + 1 : evidence.Revision, gameTime, evidence) { StateRevision = expected + 1 }; return New(owner, expected, operationId, gameTime, NpcMotivationStateChangeKind.Priority, null, null, row); }
        private static NpcMotivationStateUpdateProposal New(string owner, long expected, string operationId, double time,
            NpcMotivationStateChangeKind kind, NpcMotivationNeedState need, NpcMotivationGoalState goal, NpcMotivationPriorityState priority)
        {
            if (!NpcMotivationEvidenceRef.ValidId(owner, 240) || expected < 0 || expected == long.MaxValue || !NpcMotivationEvidenceRef.ValidId(operationId, 128) ||
                !NpcMotivationEvidenceRef.Finite(time) || time < 0) throw new ArgumentException("Invalid motivation state update proposal.");
            return new NpcMotivationStateUpdateProposal(owner, expected, operationId, time, kind, need, goal, priority);
        }
    }

    /// <summary>Persistent sparse agenda with a bounded receipt tail and compact row/generation anchors.</summary>
    [Serializable]
    public sealed class NpcMotivationState
    {
        public const int VersionCurrent = 2;
        public const int MaxAcceptedUpdates = 256;
        private const int LegacyMaxAcceptedUpdates = 4096;
        public int Version = VersionCurrent; public string OwnerPersonId; public long Revision; public double LastGameTime;
        public long ReceiptBaseRevision;
        public long LastHealthNeedVitalsRevision;
        public long LastHealthGoalVitalsRevision;
        public long LastStartedSafetySerial;
        public string LastStartedSafetyDigest;
        public string LastClosedSafetyDigest;
        public List<NpcMotivationNeedState> Needs = new List<NpcMotivationNeedState>();
        public List<NpcMotivationGoalState> Goals = new List<NpcMotivationGoalState>();
        public List<NpcMotivationPriorityState> Priorities = new List<NpcMotivationPriorityState>();
        public List<NpcAcceptedMotivationUpdate> AcceptedUpdates = new List<NpcAcceptedMotivationUpdate>();
        public List<NpcAcceptedMotivationUpdate> LatestRowAnchors = new List<NpcAcceptedMotivationUpdate>();
        public List<NpcAcceptedMotivationUpdate> GoalOriginAnchors = new List<NpcAcceptedMotivationUpdate>();

        public static NpcMotivationState Create(string ownerPersonId)
        {
            if (!NpcMotivationEvidenceRef.ValidId(ownerPersonId, 240)) throw new ArgumentException("Invalid owner identity.", nameof(ownerPersonId));
            return new NpcMotivationState { OwnerPersonId = ownerPersonId };
        }

        public static NpcMotivationState CopyValidated(NpcMotivationState source)
        { return CopyValidated(source, false); }

        internal static NpcMotivationState CopyValidated(NpcMotivationState source, bool allowLegacyGoalGeneration)
        {
            if (source == null || (source.Version != 1 && source.Version != VersionCurrent) || !NpcMotivationEvidenceRef.ValidId(source.OwnerPersonId, 240) || source.Revision < 0 ||
                !NpcMotivationEvidenceRef.Finite(source.LastGameTime) || source.LastGameTime < 0 || source.Needs == null || source.Goals == null || source.Priorities == null || source.AcceptedUpdates == null)
                throw new ArgumentException("Invalid motivation state envelope.");
            bool legacy = source.Version == 1;
            long receiptBase = legacy ? 0 : source.ReceiptBaseRevision;
            if (receiptBase < 0 || receiptBase > source.Revision || source.AcceptedUpdates.Count != source.Revision - receiptBase ||
                source.AcceptedUpdates.Count > (legacy ? LegacyMaxAcceptedUpdates : MaxAcceptedUpdates) ||
                (!legacy && (source.LatestRowAnchors == null || source.GoalOriginAnchors == null ||
                    source.LastHealthNeedVitalsRevision < 0 || source.LastHealthGoalVitalsRevision < 0 ||
                    source.LastStartedSafetySerial < 0)))
                throw new ArgumentException("Invalid accepted motivation receipt window.");
            if (!legacy && (!ValidSafetyDigest(source.LastStartedSafetyDigest, true) ||
                !ValidSafetyDigest(source.LastClosedSafetyDigest, true) ||
                source.LastStartedSafetySerial > 0 && source.LastStartedSafetyDigest == null))
                throw new ArgumentException("Invalid native safety phase cursor.");
            var needs = CopyRows(source.Needs, NpcMotivationContextSnapshot.MaxNeeds, x => x.Id, x => NpcMotivationNeedState.CopyValidated(x, source.OwnerPersonId, source.Revision));
            var goals = CopyRows(source.Goals, NpcMotivationContextSnapshot.MaxGoals, x => x.Id, x => NpcMotivationGoalState.CopyValidated(x, source.OwnerPersonId, source.Revision, allowLegacyGoalGeneration));
            var priorities = CopyRows(source.Priorities, NpcMotivationContextSnapshot.MaxPriorities, x => x.Id, x => NpcMotivationPriorityState.CopyValidated(x, source.OwnerPersonId, source.Revision));
            var accepted = new List<NpcAcceptedMotivationUpdate>(source.AcceptedUpdates.Count); var ops = new HashSet<string>(StringComparer.Ordinal);
            var evidenceKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.AcceptedUpdates.Count; i++)
            {
                var a = source.AcceptedUpdates[i];
                if (a == null || !NpcMotivationEvidenceRef.ValidId(a.OperationId, 128) || !ops.Add(a.OperationId) || !NpcMotivationEvidenceRef.ValidId(a.RowId, 128) ||
                    !string.Equals(a.OwnerPersonId, source.OwnerPersonId, StringComparison.Ordinal) || a.Revision != receiptBase + i + 1 ||
                    !Enum.IsDefined(typeof(NpcMotivationStateChangeKind), a.ChangeKind) ||
                    (a.GoalGenerationOrigin && a.ChangeKind != NpcMotivationStateChangeKind.Goal) ||
                    (a.ChangeKind != NpcMotivationStateChangeKind.Goal && a.GoalStartRevision != 0) ||
                    (a.ChangeKind == NpcMotivationStateChangeKind.Goal &&
                     (a.GoalStartRevision < 0 || a.GoalStartRevision > a.Revision ||
                      (a.GoalGenerationOrigin && a.GoalStartRevision != a.Revision) ||
                      (a.GoalStartRevision > 0 && a.GoalStartRevision < a.Revision &&
                       !HasAcceptedGoalOrigin(source, a.RowId, a.GoalStartRevision, a.AppliedAtGameTime)))) ||
                    (a.EvidenceId != null && !NpcMotivationEvidenceRef.ValidId(a.EvidenceId, 240)) ||
                    (a.EvidenceId != null && !evidenceKeys.Add(a.RowId + "\0" + a.EvidenceId)) ||
                    !NpcMotivationEvidenceRef.Finite(a.AppliedAtGameTime) || a.AppliedAtGameTime < 0 || a.AppliedAtGameTime > source.LastGameTime ||
                    (i > 0 && a.AppliedAtGameTime < source.AcceptedUpdates[i - 1].AppliedAtGameTime))
                    throw new ArgumentException("Invalid or unordered accepted motivation update receipt.");
                accepted.Add(CopyReceipt(a));
            }
            var rowAnchors = new List<NpcAcceptedMotivationUpdate>();
            var originAnchors = new List<NpcAcceptedMotivationUpdate>();
            if (!legacy)
            {
                if (source.LatestRowAnchors.Count > source.Needs.Count + source.Goals.Count + source.Priorities.Count ||
                    source.GoalOriginAnchors.Count > MaxAcceptedUpdates + NpcMotivationContextSnapshot.MaxGoals + 1000)
                    throw new ArgumentException("Unbounded motivation anchors.");
                foreach (var anchor in source.LatestRowAnchors)
                {
                    ValidateAnchor(anchor, source, receiptBase, false);
                    if (CurrentRowRevision(needs, goals, priorities, anchor.ChangeKind, anchor.RowId) != anchor.Revision)
                        throw new ArgumentException("Latest-row anchor does not own the current row.");
                    if (rowAnchors.Exists(x => x.ChangeKind == anchor.ChangeKind && x.RowId == anchor.RowId))
                        throw new ArgumentException("Duplicate latest-row anchor.");
                    rowAnchors.Add(CopyReceipt(anchor));
                }
                foreach (var anchor in source.GoalOriginAnchors)
                {
                    ValidateAnchor(anchor, source, receiptBase, true);
                    if (originAnchors.Exists(x => x.RowId == anchor.RowId && x.Revision == anchor.Revision))
                        throw new ArgumentException("Duplicate goal-origin anchor.");
                    originAnchors.Add(CopyReceipt(anchor));
                }
            }
            var result = new NpcMotivationState { Version = source.Version, OwnerPersonId = source.OwnerPersonId, Revision = source.Revision,
                ReceiptBaseRevision = receiptBase, LastHealthNeedVitalsRevision = source.LastHealthNeedVitalsRevision,
                LastHealthGoalVitalsRevision = source.LastHealthGoalVitalsRevision,
                LastStartedSafetySerial = source.LastStartedSafetySerial,
                LastStartedSafetyDigest = source.LastStartedSafetyDigest, LastClosedSafetyDigest = source.LastClosedSafetyDigest,
                LastGameTime = source.LastGameTime, Needs = needs, Goals = goals, Priorities = priorities, AcceptedUpdates = accepted,
                LatestRowAnchors = rowAnchors, GoalOriginAnchors = originAnchors };
            ValidateRowTimes(result);
            ValidateRowReceipts(result, allowLegacyGoalGeneration);
            if (!legacy) ValidateSourceCursors(result);
            return result;
        }

        private static NpcAcceptedMotivationUpdate CopyReceipt(NpcAcceptedMotivationUpdate a)
        { return new NpcAcceptedMotivationUpdate(a.OperationId, a.Revision, a.AppliedAtGameTime, a.RowId, a.OwnerPersonId, a.EvidenceId, a.ChangeKind, a.GoalGenerationOrigin, a.GoalStartRevision); }

        private static void ValidateAnchor(NpcAcceptedMotivationUpdate a, NpcMotivationState state, long receiptBase, bool origin)
        {
            if (a == null || a.Revision < 1 || a.Revision > receiptBase || !NpcMotivationEvidenceRef.ValidId(a.OperationId, 128) ||
                !NpcMotivationEvidenceRef.ValidId(a.RowId, 128) || a.OwnerPersonId != state.OwnerPersonId ||
                !Enum.IsDefined(typeof(NpcMotivationStateChangeKind), a.ChangeKind) ||
                (a.EvidenceId != null && !NpcMotivationEvidenceRef.ValidId(a.EvidenceId, 240)) ||
                !NpcMotivationEvidenceRef.Finite(a.AppliedAtGameTime) || a.AppliedAtGameTime < 0 || a.AppliedAtGameTime > state.LastGameTime ||
                (origin && (a.ChangeKind != NpcMotivationStateChangeKind.Goal || !a.GoalGenerationOrigin ||
                    a.GoalStartRevision != a.Revision || a.EvidenceId == null)) ||
                (!origin && ((a.ChangeKind != NpcMotivationStateChangeKind.Goal && a.GoalStartRevision != 0) ||
                    (a.ChangeKind == NpcMotivationStateChangeKind.Goal && (a.GoalStartRevision < 0 || a.GoalStartRevision > a.Revision)))))
                throw new ArgumentException("Invalid compact motivation anchor.");
        }

        public NpcMotivationStateView ReadOnlyView()
        {
            var copy = CopyValidated(this);
            return new NpcMotivationStateView(copy);
        }

        public static bool TryApply(NpcMotivationState current, NpcMotivationStateUpdateProposal proposal, out NpcMotivationState updated)
        { return TryApply(current, proposal, null, out updated); }

        internal static bool TryApply(NpcMotivationState current, NpcMotivationStateUpdateProposal proposal,
            ICollection<string> pinnedOrigins, out NpcMotivationState updated)
        {
            updated = null;
            if (current == null || proposal == null) return false;
            NpcMotivationState snapshot;
            try { snapshot = CopyValidated(current); }
            catch (ArgumentException) { return false; }
            long nextRevision = snapshot.Revision + 1;
            if (proposal.ExpectedRevision != snapshot.Revision || snapshot.Revision == long.MaxValue || nextRevision <= 0 ||
                !string.Equals(proposal.OwnerPersonId, snapshot.OwnerPersonId, StringComparison.Ordinal) || proposal.GameTime < snapshot.LastGameTime ||
                FindOperation(snapshot.AcceptedUpdates, proposal.OperationId) || proposal.GameTime < 0) return false;
            var needs = new List<NpcMotivationNeedState>(snapshot.Needs); var goals = new List<NpcMotivationGoalState>(snapshot.Goals);
            var priorities = new List<NpcMotivationPriorityState>(snapshot.Priorities); string rowId = null; string evidenceId = null;
            bool goalGenerationOrigin = false; long acceptedGoalStartRevision = 0;
            try
            {
                switch (proposal.ChangeKind)
                {
                    case NpcMotivationStateChangeKind.Need:
                        if (proposal.Need == null || proposal.Goal != null || proposal.Priority != null) return false;
                        var need = NpcMotivationNeedState.CopyValidated(proposal.Need, snapshot.OwnerPersonId, nextRevision); rowId = need.Id;
                        evidenceId = need.Evidence == null ? null : need.Evidence.EvidenceId;
                        if (!CanReplace(Find(snapshot.Needs, need.Id, x => x.Id)?.Evidence, Find(snapshot.Needs, need.Id, x => x.Id)?.UpdatedAtGameTime,
                            need.Evidence, proposal.GameTime)) return false;
                        Upsert(needs, need, x => x.Id, NpcMotivationContextSnapshot.MaxNeeds); break;
                    case NpcMotivationStateChangeKind.Goal:
                        if (proposal.Goal == null || proposal.Need != null || proposal.Priority != null) return false;
                        var goal = NpcMotivationGoalState.CopyValidated(proposal.Goal, snapshot.OwnerPersonId, nextRevision, true); rowId = goal.Id;
                        evidenceId = goal.Evidence == null ? null : goal.Evidence.EvidenceId;
                        var previousGoal = Find(snapshot.Goals, goal.Id, x => x.Id);
                        if (!CanReplace(previousGoal?.Evidence, previousGoal?.UpdatedAtGameTime,
                            goal.Evidence, proposal.GameTime)) return false;
                        if (goal.Status == NpcMotivationGoalStatus.Unknown) goal.GoalStartRevision = 0;
                        else if (previousGoal == null || previousGoal.Status == NpcMotivationGoalStatus.Unknown ||
                            previousGoal.Status == NpcMotivationGoalStatus.Fulfilled || previousGoal.Status == NpcMotivationGoalStatus.Abandoned)
                        {
                            if (goal.Status != NpcMotivationGoalStatus.Active) return false;
                            goal.GoalStartRevision = nextRevision;
                            goalGenerationOrigin = true;
                        }
                        else if (goal.Id == "goal:native-safety" && goal.Status == NpcMotivationGoalStatus.Active &&
                            proposal.OperationId.StartsWith("motivation:native-safety:start:", StringComparison.Ordinal) &&
                            previousGoal.Evidence != null && goal.Evidence != null &&
                            previousGoal.Evidence.EvidenceId != goal.Evidence.EvidenceId)
                        {
                            goal.GoalStartRevision = nextRevision;
                            goalGenerationOrigin = true;
                        }
                        else goal.GoalStartRevision = previousGoal.GoalStartRevision;
                        acceptedGoalStartRevision = goal.GoalStartRevision;
                        Upsert(goals, goal, x => x.Id, NpcMotivationContextSnapshot.MaxGoals); break;
                    case NpcMotivationStateChangeKind.Priority:
                        if (proposal.Priority == null || proposal.Need != null || proposal.Goal != null) return false;
                        var priority = NpcMotivationPriorityState.CopyValidated(proposal.Priority, snapshot.OwnerPersonId, nextRevision); rowId = priority.Id;
                        evidenceId = priority.Evidence == null ? null : priority.Evidence.EvidenceId;
                        if (priority.KnowledgeState != NpcMotivationKnowledgeState.Unknown && !HasSubject(needs, goals, priority.SubjectId)) return false;
                        if (!CanReplace(Find(snapshot.Priorities, priority.Id, x => x.Id)?.Evidence, Find(snapshot.Priorities, priority.Id, x => x.Id)?.UpdatedAtGameTime,
                            priority.Evidence, proposal.GameTime)) return false;
                        Upsert(priorities, priority, x => x.Id, NpcMotivationContextSnapshot.MaxPriorities); break;
                    default: return false;
                }
            }
            catch (ArgumentException) { return false; }
            if (evidenceId != null && FindEvidence(snapshot.AcceptedUpdates, rowId, evidenceId)) return false;
            var receipt = new NpcAcceptedMotivationUpdate(proposal.OperationId, nextRevision, proposal.GameTime, rowId,
                snapshot.OwnerPersonId, evidenceId, proposal.ChangeKind, goalGenerationOrigin, acceptedGoalStartRevision);
            var receipts = new List<NpcAcceptedMotivationUpdate>(snapshot.AcceptedUpdates) { receipt };
            long baseRevision = snapshot.ReceiptBaseRevision;
            var latestAnchors = new List<NpcAcceptedMotivationUpdate>(snapshot.LatestRowAnchors);
            var originAnchors = new List<NpcAcceptedMotivationUpdate>(snapshot.GoalOriginAnchors);
            if (snapshot.Version == 1 && receipts.Count > LegacyMaxAcceptedUpdates) return false;
            if (snapshot.Version == VersionCurrent)
            {
                while (receipts.Count > MaxAcceptedUpdates)
                {
                    var removed = receipts[0]; receipts.RemoveAt(0); baseRevision = removed.Revision;
                    if (CurrentRowRevision(needs, goals, priorities, removed.ChangeKind, removed.RowId) == removed.Revision)
                        UpsertAnchor(latestAnchors, removed);
                    if (removed.GoalGenerationOrigin && (TailReferencesOrigin(receipts, removed.RowId, removed.Revision) ||
                        IsCurrentGoalOrigin(goals, removed.RowId, removed.Revision) ||
                        pinnedOrigins != null && pinnedOrigins.Contains(OriginKey(removed.RowId, removed.Revision))))
                        UpsertOrigin(originAnchors, removed);
                }
                latestAnchors.RemoveAll(x => CurrentRowRevision(needs, goals, priorities, x.ChangeKind, x.RowId) != x.Revision);
                originAnchors.RemoveAll(x => !TailReferencesOrigin(receipts, x.RowId, x.Revision) &&
                    !IsCurrentGoalOrigin(goals, x.RowId, x.Revision) &&
                    (pinnedOrigins == null || !pinnedOrigins.Contains(OriginKey(x.RowId, x.Revision))));
            }
            updated = new NpcMotivationState { Version = snapshot.Version, OwnerPersonId = snapshot.OwnerPersonId, Revision = nextRevision,
                ReceiptBaseRevision = baseRevision, LastHealthNeedVitalsRevision = snapshot.LastHealthNeedVitalsRevision,
                LastHealthGoalVitalsRevision = snapshot.LastHealthGoalVitalsRevision,
                LastStartedSafetySerial = snapshot.LastStartedSafetySerial,
                LastStartedSafetyDigest = snapshot.LastStartedSafetyDigest, LastClosedSafetyDigest = snapshot.LastClosedSafetyDigest,
                LastGameTime = proposal.GameTime, Needs = needs, Goals = goals, Priorities = priorities, AcceptedUpdates = receipts,
                LatestRowAnchors = latestAnchors, GoalOriginAnchors = originAnchors };
            return true;
        }

        private static long CurrentRowRevision(List<NpcMotivationNeedState> needs, List<NpcMotivationGoalState> goals,
            List<NpcMotivationPriorityState> priorities, NpcMotivationStateChangeKind kind, string id)
        {
            if (kind == NpcMotivationStateChangeKind.Need) return Find(needs, id, x => x.Id)?.StateRevision ?? 0;
            if (kind == NpcMotivationStateChangeKind.Goal) return Find(goals, id, x => x.Id)?.StateRevision ?? 0;
            return Find(priorities, id, x => x.Id)?.StateRevision ?? 0;
        }

        private static bool IsCurrentGoalOrigin(List<NpcMotivationGoalState> goals, string id, long revision)
        { var row = Find(goals, id, x => x.Id); return row != null && row.GoalStartRevision == revision; }

        private static bool TailReferencesOrigin(List<NpcAcceptedMotivationUpdate> receipts, string id, long revision)
        { return receipts.Exists(x => x.ChangeKind == NpcMotivationStateChangeKind.Goal && x.RowId == id && x.GoalStartRevision == revision); }

        internal static string OriginKey(string id, long revision)
        { return id + "\0" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture); }

        private static void UpsertAnchor(List<NpcAcceptedMotivationUpdate> anchors, NpcAcceptedMotivationUpdate receipt)
        {
            anchors.RemoveAll(x => x.ChangeKind == receipt.ChangeKind && x.RowId == receipt.RowId);
            anchors.Add(receipt);
        }

        private static void UpsertOrigin(List<NpcAcceptedMotivationUpdate> anchors, NpcAcceptedMotivationUpdate receipt)
        {
            if (!anchors.Exists(x => x.RowId == receipt.RowId && x.Revision == receipt.Revision)) anchors.Add(receipt);
        }

        public static NpcMotivationContextSnapshot CaptureContext(NpcMotivationState state, string activeSaveBranchId,
            NpcMotivationGameClock clock, NpcMotivationProfileReference profile, NpcMotivationRelationshipReference relationship,
            IEnumerable<NpcMotivationCapabilityConstraint> capabilities, IEnumerable<NpcMotivationResourceConstraint> resources,
            IEnumerable<NpcMotivationThreatObservation> threats, IEnumerable<NpcMotivationDuty> duties)
        {
            var snapshot = CopyValidated(state);
            if (clock == null || !NpcMotivationEvidenceRef.ValidId(activeSaveBranchId, 128) || clock.GameTimeSeconds < snapshot.LastGameTime)
                throw new ArgumentException("Capture requires the active branch and a clock no earlier than saved motivation state.");
            return NpcMotivationContextSnapshot.Capture(activeSaveBranchId, snapshot.OwnerPersonId, snapshot.Revision, clock, profile, relationship,
                Convert(snapshot.Needs, activeSaveBranchId), Convert(snapshot.Goals, activeSaveBranchId), Convert(snapshot.Priorities, activeSaveBranchId),
                capabilities, resources, threats, duties);
        }

        private static List<T> CopyRows<T>(List<T> source, int max, Func<T, string> id, Func<T, T> copier) where T : class
        {
            if (source.Count > max) throw new ArgumentException("Too many persisted motivation rows.");
            var result = new List<T>(source.Count); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in source) { var copy = copier(row); if (!ids.Add(id(copy))) throw new ArgumentException("Duplicate persisted motivation row ID."); result.Add(copy); }
            return result;
        }
        private static void Upsert<T>(List<T> rows, T value, Func<T, string> id, int max) where T : class
        { for (int i = 0; i < rows.Count; i++) if (string.Equals(id(rows[i]), id(value), StringComparison.Ordinal)) { rows[i] = value; return; } if (rows.Count >= max) throw new ArgumentException("Motivation state row limit reached."); rows.Add(value); }
        private static bool HasSubject(List<NpcMotivationNeedState> needs, List<NpcMotivationGoalState> goals, string subject)
        { foreach (var x in needs) if (x.Id == subject) return true; foreach (var x in goals) if (x.Id == subject) return true; return false; }
        private static T Find<T>(List<T> rows, string id, Func<T, string> key) where T : class
        { foreach (var row in rows) if (string.Equals(key(row), id, StringComparison.Ordinal)) return row; return null; }
        private static bool CanReplace(NpcMotivationStateEvidence previous, double? previousTime, NpcMotivationStateEvidence next, double updateTime)
        {
            if (!previousTime.HasValue) return true;
            if (next == null) return updateTime > previousTime.Value;
            if (previous == null) return next.ObservedGameTime >= previousTime.Value;
            if (string.Equals(previous.EvidenceId, next.EvidenceId, StringComparison.Ordinal)) return false;
            return next.ObservedGameTime > previous.ObservedGameTime ||
                (next.Kind == previous.Kind && next.Revision > previous.Revision);
        }
        private static bool FindOperation(List<NpcAcceptedMotivationUpdate> accepted, string operation)
        { foreach (var x in accepted) if (string.Equals(x.OperationId, operation, StringComparison.Ordinal)) return true; return false; }
        private static bool FindEvidence(List<NpcAcceptedMotivationUpdate> accepted, string rowId, string evidenceId)
        { foreach (var x in accepted) if (string.Equals(x.RowId, rowId, StringComparison.Ordinal) && string.Equals(x.EvidenceId, evidenceId, StringComparison.Ordinal)) return true; return false; }
        private static List<NpcMotivationNeed> Convert(List<NpcMotivationNeedState> rows, string branch)
        { var result = new List<NpcMotivationNeed>(rows.Count); foreach (var x in rows) result.Add(x.ToContext(branch)); return result; }
        private static List<NpcMotivationGoal> Convert(List<NpcMotivationGoalState> rows, string branch)
        { var result = new List<NpcMotivationGoal>(rows.Count); foreach (var x in rows) result.Add(x.ToContext(branch)); return result; }
        private static List<NpcMotivationPriority> Convert(List<NpcMotivationPriorityState> rows, string branch)
        { var result = new List<NpcMotivationPriority>(rows.Count); foreach (var x in rows) result.Add(x.ToContext(branch)); return result; }
        private static void ValidateRowTimes(NpcMotivationState state)
        {
            foreach (var x in state.Needs) if (x.UpdatedAtGameTime > state.LastGameTime) throw new ArgumentException("Need time exceeds state time.");
            foreach (var x in state.Goals) if (x.UpdatedAtGameTime > state.LastGameTime) throw new ArgumentException("Goal time exceeds state time.");
            foreach (var x in state.Priorities) if (x.UpdatedAtGameTime > state.LastGameTime) throw new ArgumentException("Priority time exceeds state time.");
        }
        private static void ValidateRowReceipts(NpcMotivationState state, bool allowLegacyGoalGeneration)
        {
            foreach (var x in state.Needs) ValidateRowReceipt(state, x.Id, x.StateRevision, x.Evidence == null ? null : x.Evidence.EvidenceId, NpcMotivationStateChangeKind.Need);
            foreach (var x in state.Goals)
            {
                ValidateRowReceipt(state, x.Id, x.StateRevision, x.Evidence == null ? null : x.Evidence.EvidenceId, NpcMotivationStateChangeKind.Goal);
                if (LatestRowReceiptAt(state, x.StateRevision)?.GoalStartRevision != x.GoalStartRevision)
                    throw new ArgumentException("Goal row generation differs from its accepted update.");
                if (x.GoalStartRevision > 0 && !HasAcceptedGoalOrigin(state, x.Id, x.GoalStartRevision, double.PositiveInfinity))
                    throw new ArgumentException("Goal generation has no accepted origin.");
                else if (x.GoalStartRevision == 0 && !allowLegacyGoalGeneration && x.Status != NpcMotivationGoalStatus.Unknown)
                    throw new ArgumentException("Goal generation is missing.");
            }
            foreach (var x in state.Priorities) ValidateRowReceipt(state, x.Id, x.StateRevision, x.Evidence == null ? null : x.Evidence.EvidenceId, NpcMotivationStateChangeKind.Priority);
        }
        internal static bool HasAcceptedGoalOrigin(NpcMotivationState state, string goalId, long startRevision, double admittedAt)
        {
            if (state == null || string.IsNullOrWhiteSpace(goalId) || startRevision < 1 ||
                state.AcceptedUpdates == null || startRevision > state.Revision) return false;
            var receipt = ReceiptAt(state, startRevision);
            return receipt != null && receipt.Revision == startRevision &&
                receipt.ChangeKind == NpcMotivationStateChangeKind.Goal &&
                receipt.GoalGenerationOrigin && receipt.GoalStartRevision == startRevision &&
                string.Equals(receipt.RowId, goalId, StringComparison.Ordinal) &&
                receipt.AppliedAtGameTime <= admittedAt;
        }
        private static void ValidateRowReceipt(NpcMotivationState state, string rowId, long revision, string evidenceId, NpcMotivationStateChangeKind kind)
        {
            // Matching an older genuine receipt is insufficient: replacing the
            // entire row with a previous snapshot must not rewind current state.
            for (int i = state.AcceptedUpdates.Count - 1; i >= 0; i--)
            {
                var later = state.AcceptedUpdates[i];
                if (later.Revision <= revision) break;
                if (later.ChangeKind == kind && string.Equals(later.RowId, rowId, StringComparison.Ordinal))
                    throw new ArgumentException("Persisted row predates its latest accepted update receipt.");
            }
            var receipt = LatestRowReceiptAt(state, revision);
            if (receipt == null || !string.Equals(receipt.RowId, rowId, StringComparison.Ordinal) || receipt.ChangeKind != kind ||
                !string.Equals(receipt.EvidenceId, evidenceId, StringComparison.Ordinal))
                throw new ArgumentException("Persisted row does not match its latest accepted update receipt.");
        }

        private static NpcAcceptedMotivationUpdate ReceiptAt(NpcMotivationState state, long revision)
        {
            if (revision > state.ReceiptBaseRevision && revision <= state.Revision)
                return state.AcceptedUpdates[(int)(revision - state.ReceiptBaseRevision - 1)];
            foreach (var anchor in state.LatestRowAnchors) if (anchor.Revision == revision) return anchor;
            foreach (var anchor in state.GoalOriginAnchors) if (anchor.Revision == revision) return anchor;
            return null;
        }

        private static NpcAcceptedMotivationUpdate LatestRowReceiptAt(NpcMotivationState state, long revision)
        {
            if (revision > state.ReceiptBaseRevision && revision <= state.Revision)
                return state.AcceptedUpdates[(int)(revision - state.ReceiptBaseRevision - 1)];
            foreach (var anchor in state.LatestRowAnchors) if (anchor.Revision == revision) return anchor;
            return null;
        }

        private static void ValidateSourceCursors(NpcMotivationState state)
        {
            if (state.LastHealthNeedVitalsRevision > 0 && !state.Needs.Exists(x => x.Id == "need:native-health") ||
                state.LastHealthGoalVitalsRevision > 0 && !state.Goals.Exists(x => x.Id == "goal:native-recovery") ||
                state.LastStartedSafetySerial > 0 && !state.Goals.Exists(x => x.Id == "goal:native-safety"))
                throw new ArgumentException("Native motivation cursor lacks its source row.");
            foreach (var receipt in state.AcceptedUpdates) { ValidateNativeReceiptCursor(state, receipt); ValidateNativeSafetyReceipt(receipt); }
            foreach (var receipt in state.LatestRowAnchors) { ValidateNativeReceiptCursor(state, receipt); ValidateNativeSafetyReceipt(receipt); }
            foreach (var receipt in state.GoalOriginAnchors) { ValidateNativeReceiptCursor(state, receipt); ValidateNativeSafetyReceipt(receipt); }
            var healthNeed = state.Needs.Find(x => x.Id == "need:native-health");
            var recoveryGoal = state.Goals.Find(x => x.Id == "goal:native-recovery");
            ValidateCurrentHealthRow(state, "need:native-health", "need", healthNeed?.StateRevision ?? 0,
                healthNeed?.Evidence?.EvidenceId, state.LastHealthNeedVitalsRevision);
            ValidateCurrentHealthRow(state, "goal:native-recovery", "goal", recoveryGoal?.StateRevision ?? 0,
                recoveryGoal?.Evidence?.EvidenceId, state.LastHealthGoalVitalsRevision);
            var safetyGoal = state.Goals.Find(x => x.Id == "goal:native-safety");
            string digest, phase;
            bool nativeSafetyRow = TryParseSafetyEvidence(safetyGoal?.Evidence?.EvidenceId, out digest, out phase);
            if (nativeSafetyRow)
            {
                if (state.LastStartedSafetyDigest != digest ||
                    safetyGoal.Status == NpcMotivationGoalStatus.Active &&
                        (phase != "active" || state.LastClosedSafetyDigest == digest) ||
                    safetyGoal.Status == NpcMotivationGoalStatus.Fulfilled &&
                        (phase != "fulfilled" || state.LastClosedSafetyDigest != digest) ||
                    safetyGoal.Status == NpcMotivationGoalStatus.Abandoned &&
                        (phase != "abandoned" || state.LastClosedSafetyDigest != digest) ||
                    safetyGoal.Status != NpcMotivationGoalStatus.Active &&
                        safetyGoal.Status != NpcMotivationGoalStatus.Fulfilled &&
                        safetyGoal.Status != NpcMotivationGoalStatus.Abandoned)
                    throw new ArgumentException("Current native safety row differs from its phase cursor.");
            }
            else if (state.LastStartedSafetyDigest != null || state.LastClosedSafetyDigest != null ||
                safetyGoal?.Evidence?.EvidenceId?.StartsWith("native-safety:v1:", StringComparison.Ordinal) == true)
                throw new ArgumentException("Native safety phase cursor lacks its current row.");
        }

        private static void ValidateCurrentHealthRow(NpcMotivationState state, string rowId, string kind,
            long rowRevision, string evidenceId, long cursor)
        {
            var receipt = rowRevision > 0 ? LatestRowReceiptAt(state, rowRevision) : null;
            bool recognized = cursor > 0 ||
                receipt?.OperationId?.StartsWith("motivation:native-health:v1:", StringComparison.Ordinal) == true ||
                evidenceId?.StartsWith("native-health:v1:", StringComparison.Ordinal) == true;
            if (!recognized) return;
            long sourceRevision;
            if (receipt == null || receipt.RowId != rowId || !TryParseHealthOperation(receipt.OperationId, kind, out sourceRevision) ||
                sourceRevision != cursor || !ValidHealthEvidenceId(evidenceId, kind, sourceRevision))
                throw new ArgumentException("Current native health row differs from its source cursor.");
        }

        private static bool ValidSafetyDigest(string digest, bool allowNull)
        {
            if (digest == null) return allowNull;
            if (digest.Length != 64) return false;
            foreach (char character in digest)
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
            return true;
        }

        private static bool TryParseSafetyEvidence(string evidenceId, out string digest, out string phase)
        {
            digest = null; phase = null;
            const string prefix = "native-safety:v1:";
            if (evidenceId == null || !evidenceId.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string[] parts = evidenceId.Substring(prefix.Length).Split(':');
            if (parts.Length != 2 || !ValidSafetyDigest(parts[0], false) ||
                parts[1] != "active" && parts[1] != "fulfilled" && parts[1] != "abandoned") return false;
            digest = parts[0]; phase = parts[1]; return true;
        }

        private static void ValidateNativeReceiptCursor(NpcMotivationState state, NpcAcceptedMotivationUpdate receipt)
        {
            long sourceRevision;
            bool recognized = receipt.OperationId.StartsWith("motivation:native-health:v1:", StringComparison.Ordinal) ||
                receipt.EvidenceId != null && receipt.EvidenceId.StartsWith("native-health:v1:", StringComparison.Ordinal);
            if (!recognized) return;
            string kind = receipt.ChangeKind == NpcMotivationStateChangeKind.Need && receipt.RowId == "need:native-health" ? "need" :
                receipt.ChangeKind == NpcMotivationStateChangeKind.Goal && receipt.RowId == "goal:native-recovery" ? "goal" : null;
            if (kind == null || !TryParseHealthOperation(receipt.OperationId, kind, out sourceRevision) ||
                !ValidHealthEvidenceId(receipt.EvidenceId, kind, sourceRevision) ||
                sourceRevision > (kind == "need" ? state.LastHealthNeedVitalsRevision : state.LastHealthGoalVitalsRevision))
                throw new ArgumentException("Native health cursor or receipt source is invalid.");
        }

        private static void ValidateNativeSafetyReceipt(NpcAcceptedMotivationUpdate receipt)
        {
            const string prefix = "motivation:native-safety:";
            const string evidencePrefix = "native-safety:v1:";
            bool recognized = receipt.OperationId.StartsWith(prefix, StringComparison.Ordinal) ||
                receipt.EvidenceId != null && receipt.EvidenceId.StartsWith(evidencePrefix, StringComparison.Ordinal);
            if (!recognized) return;
            if (!receipt.OperationId.StartsWith(prefix, StringComparison.Ordinal))
                throw new ArgumentException("Malformed native safety operation.");
            string[] parts = receipt.OperationId.Substring(prefix.Length).Split(':');
            if (receipt.RowId != "goal:native-safety" || receipt.ChangeKind != NpcMotivationStateChangeKind.Goal ||
                parts.Length != 2 || parts[1].Length != 64 ||
                (parts[0] != "start" && parts[0] != "fulfill" && parts[0] != "abandon") ||
                receipt.EvidenceId != evidencePrefix + parts[1] + ":" +
                    (parts[0] == "start" ? "active" : parts[0] == "fulfill" ? "fulfilled" : "abandoned"))
                throw new ArgumentException("Malformed native safety receipt.");
            foreach (char digit in parts[1])
                if (!((digit >= '0' && digit <= '9') || (digit >= 'a' && digit <= 'f')))
                    throw new ArgumentException("Malformed native safety digest.");
        }

        private static bool ValidHealthEvidenceId(string evidenceId, string kind, long revision)
        {
            if (evidenceId == null) return false;
            string prefix = "native-health:v1:" + kind + ":" + revision.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
            if (!evidenceId.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string[] values = evidenceId.Substring(prefix.Length).Split(':');
            int current, maximum;
            return values.Length == 2 && int.TryParse(values[0], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out current) &&
                int.TryParse(values[1], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out maximum) &&
                current >= 1 && maximum >= current;
        }

        private static bool TryParseHealthOperation(string operationId, string kind, out long sourceRevision)
        {
            const string prefix = "motivation:native-health:v1:";
            sourceRevision = 0;
            string expected = prefix + kind + ":";
            return operationId != null && operationId.StartsWith(expected, StringComparison.Ordinal) &&
                long.TryParse(operationId.Substring(expected.Length), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out sourceRevision) && sourceRevision > 0;
        }


        internal static void ValidateOriginAnchors(NpcMotivationState state, ICollection<string> pinnedOrigins)
        {
            foreach (var anchor in state.GoalOriginAnchors)
                if (!TailReferencesOrigin(state.AcceptedUpdates, anchor.RowId, anchor.Revision) &&
                    !IsCurrentGoalOrigin(state.Goals, anchor.RowId, anchor.Revision) &&
                    (pinnedOrigins == null || !pinnedOrigins.Contains(OriginKey(anchor.RowId, anchor.Revision))))
                    throw new ArgumentException("Orphaned goal-origin anchor.");
            if (pinnedOrigins != null)
                foreach (var pin in pinnedOrigins)
                {
                    int separator = pin.LastIndexOf('\0');
                    long revision;
                    if (separator < 1 || !long.TryParse(pin.Substring(separator + 1), out revision) ||
                        !HasAcceptedGoalOrigin(state, pin.Substring(0, separator), revision, double.PositiveInfinity))
                        throw new ArgumentException("Task goal-generation pin lacks its origin.");
                }
        }
    }

    public sealed class NpcMotivationStateView
    {
        public string OwnerPersonId { get; private set; } public long Revision { get; private set; } public double LastGameTime { get; private set; }
        public IReadOnlyList<NpcMotivationNeedView> Needs { get; private set; } public IReadOnlyList<NpcMotivationGoalView> Goals { get; private set; }
        public IReadOnlyList<NpcMotivationPriorityView> Priorities { get; private set; } public IReadOnlyList<NpcAcceptedMotivationUpdateView> AcceptedUpdates { get; private set; }
        internal NpcMotivationStateView(NpcMotivationState state)
        {
            OwnerPersonId = state.OwnerPersonId; Revision = state.Revision; LastGameTime = state.LastGameTime;
            var needs = new List<NpcMotivationNeedView>(); foreach (var x in state.Needs) needs.Add(new NpcMotivationNeedView(x));
            var goals = new List<NpcMotivationGoalView>(); foreach (var x in state.Goals) goals.Add(new NpcMotivationGoalView(x));
            var priorities = new List<NpcMotivationPriorityView>(); foreach (var x in state.Priorities) priorities.Add(new NpcMotivationPriorityView(x));
            var accepted = new List<NpcAcceptedMotivationUpdateView>(); foreach (var x in state.AcceptedUpdates) accepted.Add(new NpcAcceptedMotivationUpdateView(x));
            Needs = new ReadOnlyCollection<NpcMotivationNeedView>(needs); Goals = new ReadOnlyCollection<NpcMotivationGoalView>(goals);
            Priorities = new ReadOnlyCollection<NpcMotivationPriorityView>(priorities); AcceptedUpdates = new ReadOnlyCollection<NpcAcceptedMotivationUpdateView>(accepted);
        }
    }

    public sealed class NpcMotivationNeedView
    {
        public string Id { get; private set; } public NpcMotivationNeedKind Kind { get; private set; }
        public NpcMotivationKnowledgeState KnowledgeState { get; private set; } public int? Intensity { get; private set; }
        public long Revision { get; private set; } public double UpdatedAtGameTime { get; private set; }
        public string EvidenceId { get; private set; } public NpcMotivationStateEvidenceKind? EvidenceKind { get; private set; }
        internal NpcMotivationNeedView(NpcMotivationNeedState x)
        { Id = x.Id; Kind = x.Kind; KnowledgeState = x.KnowledgeState; Intensity = x.Intensity; Revision = x.Revision; UpdatedAtGameTime = x.UpdatedAtGameTime; EvidenceId = x.Evidence == null ? null : x.Evidence.EvidenceId; EvidenceKind = x.Evidence == null ? (NpcMotivationStateEvidenceKind?)null : x.Evidence.Kind; }
    }

    public sealed class NpcMotivationGoalView
    {
        public string Id { get; private set; } public NpcMotivationGoalKind Kind { get; private set; }
        public NpcMotivationGoalStatus Status { get; private set; } public NpcMotivationPriorityLevel Priority { get; private set; }
        public NpcMotivationKnowledgeState KnowledgeState { get; private set; } public long Revision { get; private set; }
        public long GoalStartRevision { get; private set; }
        public double UpdatedAtGameTime { get; private set; } public string EvidenceId { get; private set; }
        public NpcMotivationStateEvidenceKind? EvidenceKind { get; private set; }
        internal NpcMotivationGoalView(NpcMotivationGoalState x)
        { Id = x.Id; Kind = x.Kind; Status = x.Status; Priority = x.Priority; KnowledgeState = x.KnowledgeState; Revision = x.Revision; GoalStartRevision = x.GoalStartRevision; UpdatedAtGameTime = x.UpdatedAtGameTime; EvidenceId = x.Evidence == null ? null : x.Evidence.EvidenceId; EvidenceKind = x.Evidence == null ? (NpcMotivationStateEvidenceKind?)null : x.Evidence.Kind; }
    }

    public sealed class NpcMotivationPriorityView
    {
        public string Id { get; private set; } public string SubjectId { get; private set; } public NpcMotivationPriorityKind Kind { get; private set; }
        public int Rank { get; private set; } public NpcMotivationKnowledgeState KnowledgeState { get; private set; }
        public long Revision { get; private set; } public double UpdatedAtGameTime { get; private set; }
        public string EvidenceId { get; private set; } public NpcMotivationStateEvidenceKind? EvidenceKind { get; private set; }
        internal NpcMotivationPriorityView(NpcMotivationPriorityState x)
        { Id = x.Id; SubjectId = x.SubjectId; Kind = x.Kind; Rank = x.Rank; KnowledgeState = x.KnowledgeState; Revision = x.Revision; UpdatedAtGameTime = x.UpdatedAtGameTime; EvidenceId = x.Evidence == null ? null : x.Evidence.EvidenceId; EvidenceKind = x.Evidence == null ? (NpcMotivationStateEvidenceKind?)null : x.Evidence.Kind; }
    }

    public sealed class NpcAcceptedMotivationUpdateView
    {
        public string OperationId { get; private set; } public long Revision { get; private set; }
        public double AppliedAtGameTime { get; private set; } public string RowId { get; private set; } public string EvidenceId { get; private set; }
        public NpcMotivationStateChangeKind ChangeKind { get; private set; }
        internal NpcAcceptedMotivationUpdateView(NpcAcceptedMotivationUpdate x)
        { OperationId = x.OperationId; Revision = x.Revision; AppliedAtGameTime = x.AppliedAtGameTime; RowId = x.RowId; EvidenceId = x.EvidenceId; ChangeKind = x.ChangeKind; }
    }
}
