using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcMotivationEvidenceKind
    {
        NativeObservation = 1, NativeOutcomeReceipt = 2, ConversationReceipt = 3,
        PersonalityProfile = 4, RelationshipLedger = 5, CommitmentLedger = 6,
        ResourceLedger = 7, WorldClock = 8, AuthoredRecord = 9
    }

    public enum NpcMotivationKnowledgeState { Known = 1, Unknown = 2, AttributedClaim = 3 }
    public enum NpcMotivationNeedKind { Rest = 2, Safety = 3, Health = 4, Magicka = 5, Stamina = 6, Money = 7, Equipment = 8, Social = 9, Belonging = 10, Recognition = 11, Shelter = 12, Capacity = 13 }
    public enum NpcMotivationGoalKind { FamilyCare = 1, Recovery = 2, Work = 3, Quest = 4, Faction = 5, PersonalProject = 6, Safety = 7, Profit = 8, Social = 9, Survival = 10, Other = 11 }
    public enum NpcMotivationGoalStatus { Unknown = 1, Active = 2, Suspended = 3, Fulfilled = 4, Abandoned = 5 }
    public enum NpcMotivationPriorityLevel { Routine = 1, Important = 2, Urgent = 3, Emergency = 4 }
    public enum NpcMotivationPriorityKind { Family = 1, Survival = 2, AcceptedCommitment = 3, Schedule = 4, Goal = 5, PersonalValue = 6, Other = 7 }
    public enum NpcMotivationCapabilityKind { MeleeCombat = 1, RangedCombat = 2, Magic = 3, Healing = 4, Navigation = 5, Carrying = 6, Crafting = 7, Social = 8, Travel = 9 }
    public enum NpcMotivationAvailability { Unknown = 1, Available = 2, Unavailable = 3 }
    public enum NpcMotivationResourceKind { Health = 1, Magicka = 2, Stamina = 3, Gold = 4, Food = 5, Equipment = 6, Ammunition = 7, CarryCapacity = 8, Time = 9 }
    public enum NpcMotivationThreatKind { HostileActor = 1, Combat = 2, Terrain = 3, Weather = 4, CrimeWitness = 5, SocialConflict = 6, ResourceFailure = 7, Other = 8 }
    public enum NpcMotivationDutyKind { Family = 1, Appointment = 2, Quest = 3, Faction = 4, Work = 5, Service = 6, Personal = 7 }
    [Flags]
    public enum NpcMotivationDutyScope { None = 0, RoutineWork = 1, Travel = 2, Combat = 4, Assistance = 8, Disclosure = 16, Household = 32, Schedule = 64 }
    public enum NpcMotivationDutyStatus { Planned = 1, Accepted = 2, Active = 3, Fulfilled = 4, Breached = 5, Suspended = 6, Cancelled = 7 }

    public sealed class NpcMotivationEvidenceRef
    {
        public string SaveBranchId { get; private set; }
        public string ObserverId { get; private set; }
        public string EvidenceId { get; private set; }
        public NpcMotivationEvidenceKind Kind { get; private set; }
        public long Revision { get; private set; }
        public double ObservedGameTime { get; private set; }

        public NpcMotivationEvidenceRef(string saveBranchId, string observerId, string evidenceId, NpcMotivationEvidenceKind kind,
            long revision, double observedGameTime)
        {
            if (!ValidId(saveBranchId, 128) || !ValidId(observerId, 240) || !ValidId(evidenceId, 240) || !Enum.IsDefined(typeof(NpcMotivationEvidenceKind), kind) ||
                revision < 0 || !Finite(observedGameTime) || observedGameTime < 0)
                throw new ArgumentException("Invalid observer-bound motivation evidence.");
            SaveBranchId = saveBranchId; ObserverId = observerId; EvidenceId = evidenceId; Kind = kind; Revision = revision; ObservedGameTime = observedGameTime;
        }

        internal static bool ValidId(string value, int max) { return NpcExperienceAppraisalContract.ValidIdentity(value, max); }
        internal static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed class NpcMotivationGameClock
    {
        public double GameTimeSeconds { get; private set; }
        public long Revision { get; private set; }
        public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationGameClock(double gameTimeSeconds, long revision, NpcMotivationEvidenceRef evidence)
        {
            if (!NpcMotivationEvidenceRef.Finite(gameTimeSeconds) || gameTimeSeconds < 0 || revision < 0 || evidence == null ||
                evidence.Kind != NpcMotivationEvidenceKind.WorldClock || evidence.Revision != revision || evidence.ObservedGameTime != gameTimeSeconds)
                throw new ArgumentException("Invalid captured native game clock.");
            GameTimeSeconds = gameTimeSeconds; Revision = revision; Evidence = evidence;
        }
    }

    public sealed class NpcMotivationProfileReference
    {
        public string PersonId { get; private set; }
        public long Revision { get; private set; }
        public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationProfileReference(string personId, long revision, NpcMotivationEvidenceRef evidence)
        {
            if (!NpcMotivationEvidenceRef.ValidId(personId, 240) || revision < 0 || evidence == null ||
                evidence.Kind != NpcMotivationEvidenceKind.PersonalityProfile || evidence.Revision != revision)
                throw new ArgumentException("Invalid stable personality profile reference.");
            PersonId = personId; Revision = revision; Evidence = evidence;
        }
    }

    public sealed class NpcMotivationRelationshipReference
    {
        public string ObserverId { get; private set; }
        public string TargetId { get; private set; }
        public long Revision { get; private set; }
        public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationRelationshipReference(string observerId, string targetId, long revision, NpcMotivationEvidenceRef evidence)
        {
            if (!NpcMotivationEvidenceRef.ValidId(observerId, 240) || !NpcMotivationEvidenceRef.ValidId(targetId, 240) ||
                string.Equals(observerId, targetId, StringComparison.Ordinal) || revision < 0 || evidence == null ||
                evidence.Kind != NpcMotivationEvidenceKind.RelationshipLedger || evidence.Revision != revision)
                throw new ArgumentException("Invalid directed relationship reference.");
            ObserverId = observerId; TargetId = targetId; Revision = revision; Evidence = evidence;
        }
    }

    /// <summary>Common immutable identity/provenance surface for typed motivation facts; it does not imply action authority.</summary>
    public interface INpcMotivationFact
    {
        string Id { get; }
        long Revision { get; }
        NpcMotivationKnowledgeState KnowledgeState { get; }
        NpcMotivationEvidenceRef Evidence { get; }
    }

    internal static class NpcMotivationValidation
    {
        internal static void Fact(string id, long revision, NpcMotivationKnowledgeState state,
            NpcMotivationEvidenceRef evidence, int? value, int min, int max, params NpcMotivationEvidenceKind[] knownEvidenceKinds)
        {
            if (!NpcMotivationEvidenceRef.ValidId(id, 128) || revision < 0 ||
                !Enum.IsDefined(typeof(NpcMotivationKnowledgeState), state) ||
                (value.HasValue && (value.Value < min || value.Value > max))) throw new ArgumentException("Invalid motivation fact.");
            if (state == NpcMotivationKnowledgeState.Unknown)
            {
                if (value.HasValue || evidence != null) throw new ArgumentException("Unknown facts cannot carry a value or source.");
            }
            else if (evidence == null || evidence.Revision != revision ||
                (state == NpcMotivationKnowledgeState.AttributedClaim && evidence.Kind != NpcMotivationEvidenceKind.ConversationReceipt) ||
                (state == NpcMotivationKnowledgeState.Known && (evidence.Kind == NpcMotivationEvidenceKind.ConversationReceipt ||
                    knownEvidenceKinds == null || Array.IndexOf(knownEvidenceKinds, evidence.Kind) < 0)))
                throw new ArgumentException("Facts require revision-matched evidence of a permitted source kind; heard claims cannot verify their content.");
            if (state == NpcMotivationKnowledgeState.Known && !value.HasValue)
                throw new ArgumentException("Known scalar facts require a value.");
        }

        internal static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values, int max, string label) where T : class, INpcMotivationFact
        {
            var copy = new List<T>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (values != null)
                foreach (T item in values)
                {
                    if (item == null || !ids.Add(item.Id)) throw new ArgumentException("Null or duplicate " + label + " fact.");
                    if (copy.Count == max) throw new ArgumentException("Too many " + label + " facts.");
                    copy.Add(item);
                }
            return new ReadOnlyCollection<T>(copy);
        }
    }

    public sealed class NpcMotivationNeed : INpcMotivationFact
    {
        public string Id { get; private set; } public NpcMotivationNeedKind Kind { get; private set; }
        public NpcMotivationKnowledgeState KnowledgeState { get; private set; } public int? Intensity { get; private set; }
        public long Revision { get; private set; } public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationNeed(string id, NpcMotivationNeedKind kind, NpcMotivationKnowledgeState knowledgeState, int? intensity, long revision, NpcMotivationEvidenceRef evidence)
        {
            if (!Enum.IsDefined(typeof(NpcMotivationNeedKind), kind)) throw new ArgumentException("Invalid need kind.");
            NpcMotivationValidation.Fact(id, revision, knowledgeState, evidence, intensity, 0, 100,
                NpcMotivationEvidenceKind.NativeObservation, NpcMotivationEvidenceKind.ResourceLedger, NpcMotivationEvidenceKind.NativeOutcomeReceipt);
            Id = id; Kind = kind; KnowledgeState = knowledgeState; Intensity = intensity; Revision = revision; Evidence = evidence;
        }
    }

    public sealed class NpcMotivationGoal : INpcMotivationFact
    {
        public string Id { get; private set; } public NpcMotivationGoalKind Kind { get; private set; }
        public NpcMotivationGoalStatus Status { get; private set; } public NpcMotivationPriorityLevel Priority { get; private set; }
        public NpcMotivationKnowledgeState KnowledgeState { get; private set; } public long Revision { get; private set; }
        public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationGoal(string id, NpcMotivationGoalKind kind, NpcMotivationGoalStatus status,
            NpcMotivationPriorityLevel priority, NpcMotivationKnowledgeState knowledgeState, long revision, NpcMotivationEvidenceRef evidence)
        {
            if (!Enum.IsDefined(typeof(NpcMotivationGoalKind), kind) || !Enum.IsDefined(typeof(NpcMotivationGoalStatus), status) || !Enum.IsDefined(typeof(NpcMotivationPriorityLevel), priority) ||
                (knowledgeState == NpcMotivationKnowledgeState.Unknown && status != NpcMotivationGoalStatus.Unknown) ||
                (knowledgeState != NpcMotivationKnowledgeState.Unknown && status == NpcMotivationGoalStatus.Unknown) ||
                (knowledgeState == NpcMotivationKnowledgeState.Unknown && priority != NpcMotivationPriorityLevel.Routine))
                throw new ArgumentException("Invalid goal.");
            NpcMotivationValidation.Fact(id, revision, knowledgeState, evidence, knowledgeState == NpcMotivationKnowledgeState.Unknown ? (int?)null : (int)priority, 1, 4,
                NpcMotivationEvidenceKind.AuthoredRecord, NpcMotivationEvidenceKind.CommitmentLedger, NpcMotivationEvidenceKind.NativeObservation);
            Id = id; Kind = kind; Status = status; Priority = priority; KnowledgeState = knowledgeState; Revision = revision; Evidence = evidence;
        }
    }

    public sealed class NpcMotivationPriority : INpcMotivationFact
    {
        public string Id { get; private set; } public string SubjectId { get; private set; } public NpcMotivationPriorityKind Kind { get; private set; }
        public int Rank { get; private set; } public NpcMotivationKnowledgeState KnowledgeState { get; private set; }
        public long Revision { get; private set; } public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationPriority(string id, string subjectId, NpcMotivationPriorityKind kind, int rank,
            NpcMotivationKnowledgeState knowledgeState, long revision, NpcMotivationEvidenceRef evidence)
        {
            if (!NpcMotivationEvidenceRef.ValidId(subjectId, 128) || !Enum.IsDefined(typeof(NpcMotivationPriorityKind), kind)) throw new ArgumentException("Invalid priority.");
            NpcMotivationValidation.Fact(id, revision, knowledgeState, evidence,
                knowledgeState == NpcMotivationKnowledgeState.Unknown ? (int?)null : rank, 0, 100,
                NpcMotivationEvidenceKind.AuthoredRecord, NpcMotivationEvidenceKind.CommitmentLedger, NpcMotivationEvidenceKind.PersonalityProfile);
            if (knowledgeState == NpcMotivationKnowledgeState.Unknown && rank != 0) throw new ArgumentException("Unknown priorities must use rank zero.");
            SubjectId = subjectId; Id = id; Kind = kind; Rank = rank; KnowledgeState = knowledgeState; Revision = revision; Evidence = evidence;
        }
    }

    public sealed class NpcMotivationCapabilityConstraint : INpcMotivationFact
    {
        public string Id { get; private set; } public NpcMotivationCapabilityKind Kind { get; private set; }
        public NpcMotivationAvailability Availability { get; private set; } public NpcMotivationKnowledgeState KnowledgeState { get; private set; }
        public long Revision { get; private set; } public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationCapabilityConstraint(string id, NpcMotivationCapabilityKind kind, NpcMotivationAvailability availability,
            NpcMotivationKnowledgeState knowledgeState, long revision, NpcMotivationEvidenceRef evidence)
        {
            if (!Enum.IsDefined(typeof(NpcMotivationCapabilityKind), kind) || !Enum.IsDefined(typeof(NpcMotivationAvailability), availability) ||
                (knowledgeState == NpcMotivationKnowledgeState.Unknown && availability != NpcMotivationAvailability.Unknown) ||
                (knowledgeState != NpcMotivationKnowledgeState.Unknown && availability == NpcMotivationAvailability.Unknown)) throw new ArgumentException("Invalid capability constraint.");
            NpcMotivationValidation.Fact(id, revision, knowledgeState, evidence,
                knowledgeState == NpcMotivationKnowledgeState.Unknown ? (int?)null : (int)availability, 1, 3,
                NpcMotivationEvidenceKind.NativeObservation, NpcMotivationEvidenceKind.AuthoredRecord);
            Id = id; Kind = kind; Availability = availability; KnowledgeState = knowledgeState; Revision = revision; Evidence = evidence;
        }
    }

    public sealed class NpcMotivationResourceConstraint : INpcMotivationFact
    {
        public string Id { get; private set; } public NpcMotivationResourceKind Kind { get; private set; }
        public NpcMotivationKnowledgeState KnowledgeState { get; private set; } public int? CurrentAmount { get; private set; }
        public int? RequiredAmount { get; private set; } public long Revision { get; private set; } public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationResourceConstraint(string id, NpcMotivationResourceKind kind, NpcMotivationKnowledgeState knowledgeState,
            int? currentAmount, int? requiredAmount, long revision, NpcMotivationEvidenceRef evidence)
        {
            if (!Enum.IsDefined(typeof(NpcMotivationResourceKind), kind) || (currentAmount.HasValue && currentAmount.Value < 0) || (requiredAmount.HasValue && requiredAmount.Value < 0)) throw new ArgumentException("Invalid resource constraint.");
            NpcMotivationValidation.Fact(id, revision, knowledgeState, evidence, currentAmount, 0, int.MaxValue,
                NpcMotivationEvidenceKind.ResourceLedger, NpcMotivationEvidenceKind.NativeObservation, NpcMotivationEvidenceKind.NativeOutcomeReceipt);
            if (knowledgeState == NpcMotivationKnowledgeState.Known && !requiredAmount.HasValue) throw new ArgumentException("Known resource constraints require a required amount.");
            if (knowledgeState == NpcMotivationKnowledgeState.Unknown && (currentAmount.HasValue || requiredAmount.HasValue)) throw new ArgumentException("Unknown resource constraints cannot carry amounts.");
            Id = id; Kind = kind; KnowledgeState = knowledgeState; CurrentAmount = currentAmount; RequiredAmount = requiredAmount; Revision = revision; Evidence = evidence;
        }
    }

    public sealed class NpcMotivationThreatObservation : INpcMotivationFact
    {
        public string Id { get; private set; } public NpcMotivationThreatKind Kind { get; private set; }
        public NpcMotivationKnowledgeState KnowledgeState { get; private set; } public int? Severity { get; private set; }
        public long Revision { get; private set; } public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationThreatObservation(string id, NpcMotivationThreatKind kind, NpcMotivationKnowledgeState knowledgeState,
            int? severity, long revision, NpcMotivationEvidenceRef evidence)
        {
            if (!Enum.IsDefined(typeof(NpcMotivationThreatKind), kind)) throw new ArgumentException("Invalid threat kind.");
            NpcMotivationValidation.Fact(id, revision, knowledgeState, evidence, severity, 0, 100,
                NpcMotivationEvidenceKind.NativeObservation, NpcMotivationEvidenceKind.NativeOutcomeReceipt);
            Id = id; Kind = kind; KnowledgeState = knowledgeState; Severity = severity; Revision = revision; Evidence = evidence;
        }
    }

    public sealed class NpcMotivationDuty : INpcMotivationFact
    {
        public string Id { get; private set; } public NpcMotivationDutyKind Kind { get; private set; }
        public NpcMotivationDutyScope Scope { get; private set; } public NpcMotivationDutyStatus Status { get; private set; }
        public double StartsAtGameTime { get; private set; } public double? EndsAtGameTime { get; private set; }
        public NpcMotivationKnowledgeState KnowledgeState { get; private set; } public long Revision { get; private set; }
        public NpcMotivationEvidenceRef Evidence { get; private set; }
        public NpcMotivationDuty(string id, NpcMotivationDutyKind kind, NpcMotivationDutyScope scope, NpcMotivationDutyStatus status,
            double startsAtGameTime, double? endsAtGameTime, long revision, NpcMotivationEvidenceRef evidence)
        {
            const NpcMotivationDutyScope all = NpcMotivationDutyScope.RoutineWork | NpcMotivationDutyScope.Travel | NpcMotivationDutyScope.Combat | NpcMotivationDutyScope.Assistance | NpcMotivationDutyScope.Disclosure | NpcMotivationDutyScope.Household | NpcMotivationDutyScope.Schedule;
            if (!Enum.IsDefined(typeof(NpcMotivationDutyKind), kind) || !Enum.IsDefined(typeof(NpcMotivationDutyStatus), status) || scope == NpcMotivationDutyScope.None || (scope & ~all) != 0 ||
                !NpcMotivationEvidenceRef.Finite(startsAtGameTime) || startsAtGameTime < 0 || (endsAtGameTime.HasValue && (!NpcMotivationEvidenceRef.Finite(endsAtGameTime.Value) || endsAtGameTime.Value < startsAtGameTime)) || evidence == null || revision < 0)
                throw new ArgumentException("Invalid duty.");
            Id = id; Kind = kind; Scope = scope; Status = status; StartsAtGameTime = startsAtGameTime; EndsAtGameTime = endsAtGameTime;
            Revision = revision; Evidence = evidence; KnowledgeState = NpcMotivationKnowledgeState.Known;
            NpcMotivationValidation.Fact(id, revision, KnowledgeState, evidence, (int)status, 1, 7,
                NpcMotivationEvidenceKind.CommitmentLedger, NpcMotivationEvidenceKind.AuthoredRecord,
                NpcMotivationEvidenceKind.NativeOutcomeReceipt, NpcMotivationEvidenceKind.NativeObservation);
        }
    }

    public sealed class NpcMotivationContextSnapshot
    {
        public const int MaxNeeds = 16, MaxGoals = 16, MaxPriorities = 16, MaxCapabilities = 32, MaxResources = 32, MaxThreats = 16, MaxDuties = 32;
        private NpcMotivationContextSnapshot(string branch, string observer, long revision, NpcMotivationGameClock clock,
            NpcMotivationProfileReference profile, NpcMotivationRelationshipReference relationship,
            ReadOnlyCollection<NpcMotivationNeed> needs, ReadOnlyCollection<NpcMotivationGoal> goals,
            ReadOnlyCollection<NpcMotivationPriority> priorities, ReadOnlyCollection<NpcMotivationCapabilityConstraint> capabilities,
            ReadOnlyCollection<NpcMotivationResourceConstraint> resources, ReadOnlyCollection<NpcMotivationThreatObservation> threats,
            ReadOnlyCollection<NpcMotivationDuty> duties)
        {
            SaveBranchId = branch; ObserverId = observer; Revision = revision; Clock = clock; Profile = profile; Relationship = relationship;
            Needs = needs; Goals = goals; Priorities = priorities; Capabilities = capabilities; Resources = resources; Threats = threats; Duties = duties;
        }
        public string SaveBranchId { get; private set; } public string ObserverId { get; private set; }
        public long Revision { get; private set; } public double CapturedGameTime { get { return Clock.GameTimeSeconds; } }
        public NpcMotivationGameClock Clock { get; private set; } public NpcMotivationProfileReference Profile { get; private set; }
        public NpcMotivationRelationshipReference Relationship { get; private set; }
        public IReadOnlyList<NpcMotivationNeed> Needs { get; private set; } public IReadOnlyList<NpcMotivationGoal> Goals { get; private set; }
        public IReadOnlyList<NpcMotivationPriority> Priorities { get; private set; } public IReadOnlyList<NpcMotivationCapabilityConstraint> Capabilities { get; private set; }
        public IReadOnlyList<NpcMotivationResourceConstraint> Resources { get; private set; } public IReadOnlyList<NpcMotivationThreatObservation> Threats { get; private set; }
        public IReadOnlyList<NpcMotivationDuty> Duties { get; private set; }

        public static NpcMotivationContextSnapshot Capture(string saveBranchId, string observerId, long revision,
            NpcMotivationGameClock clock, NpcMotivationProfileReference profile, NpcMotivationRelationshipReference relationship,
            IEnumerable<NpcMotivationNeed> needs, IEnumerable<NpcMotivationGoal> goals, IEnumerable<NpcMotivationPriority> priorities,
            IEnumerable<NpcMotivationCapabilityConstraint> capabilities, IEnumerable<NpcMotivationResourceConstraint> resources,
            IEnumerable<NpcMotivationThreatObservation> threats, IEnumerable<NpcMotivationDuty> duties)
        {
            if (!NpcMotivationEvidenceRef.ValidId(saveBranchId, 128) || !NpcMotivationEvidenceRef.ValidId(observerId, 240) || revision < 0 || clock == null || profile == null ||
                !string.Equals(profile.PersonId, observerId, StringComparison.Ordinal) ||
                (relationship != null && !string.Equals(relationship.ObserverId, observerId, StringComparison.Ordinal)))
                throw new ArgumentException("Invalid motivation snapshot identity or captured sources.");
            var ns = NpcMotivationValidation.Copy(needs, MaxNeeds, "need"); var gs = NpcMotivationValidation.Copy(goals, MaxGoals, "goal");
            var ps = NpcMotivationValidation.Copy(priorities, MaxPriorities, "priority"); var cs = NpcMotivationValidation.Copy(capabilities, MaxCapabilities, "capability");
            var rs = NpcMotivationValidation.Copy(resources, MaxResources, "resource"); var ts = NpcMotivationValidation.Copy(threats, MaxThreats, "threat");
            var ds = NpcMotivationValidation.Copy(duties, MaxDuties, "duty");
            ValidateSource(saveBranchId, observerId, clock.Evidence, profile.Evidence, relationship == null ? null : relationship.Evidence,
                clock.GameTimeSeconds, ns, gs, ps, cs, rs, ts, ds);
            return new NpcMotivationContextSnapshot(saveBranchId, observerId, revision, clock, profile, relationship, ns, gs, ps, cs, rs, ts, ds);
        }

        private static void ValidateSource(string branch, string observer, NpcMotivationEvidenceRef clock, NpcMotivationEvidenceRef profile,
            NpcMotivationEvidenceRef relationship, double time, params object[] facts)
        {
            var sources = new List<NpcMotivationEvidenceRef> { clock, profile };
            if (relationship != null) sources.Add(relationship);
            foreach (object group in facts)
            {
                var enumerable = group as System.Collections.IEnumerable;
                if (enumerable == null) continue;
                foreach (object value in enumerable)
                {
                    var fact = value as INpcMotivationFact;
                    if (fact != null && fact.Evidence != null) sources.Add(fact.Evidence);
                }
            }
            foreach (var evidence in sources)
                if (!string.Equals(evidence.ObserverId, observer, StringComparison.Ordinal) ||
                    !string.Equals(evidence.SaveBranchId, branch, StringComparison.Ordinal) ||
                    evidence.ObservedGameTime > time)
                    throw new ArgumentException("Motivation evidence must match the observer, save branch and snapshot time.");
        }
    }
}
