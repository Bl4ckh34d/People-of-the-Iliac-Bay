using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcCovertActionKind { Steal, Search, Investigate }
    public enum NpcCovertEffectKind { ItemTransferred, SearchResultRecorded, ClueRecorded }

    /// <summary>One proposed operation against an exact native object and its observed revisions.</summary>
    public sealed class NpcCovertRequest
    {
        public string OperationId, ActorId, TargetId, OwnerId, ItemId;
        public NpcCovertActionKind Kind;
        public long TargetRevision, CrimeRevision, WitnessRevision;
    }

    /// <summary>Current native facts. Unknown ownership, skill, crime, or witness state must deny admission.</summary>
    public sealed class NpcCovertFacts
    {
        public bool ActorAlive, TargetExists, ItemExists, ItemTransferable;
        public bool AdapterAvailable, DutyAvailable, ActorConsents, OwnerConsents, QuestProtected;
        public string OwnerId;
        public long TargetRevision, CrimeRevision, WitnessRevision;
        public int LockDifficulty, LockSkill, Relationship, MinimumRelationship, Risk, MaximumRisk;
        public string[] WitnessIds;
    }

    public sealed class NpcCovertAuthorization
    {
        public string OperationId { get; private set; }
        public string ActorId { get; private set; }
        public string TargetId { get; private set; }
        public string OwnerId { get; private set; }
        public string ItemId { get; private set; }
        public NpcCovertActionKind Kind { get; private set; }
        public long TargetRevision { get; private set; }
        public long CrimeRevision { get; private set; }
        public long WitnessRevision { get; private set; }
        public bool IllegalAccess { get; private set; }
        public string[] WitnessIds { get { return (string[])witnessIds.Clone(); } }
        private readonly string[] witnessIds;

        internal NpcCovertAuthorization(NpcCovertRequest request, NpcCovertFacts facts)
        {
            OperationId = request.OperationId; ActorId = request.ActorId; TargetId = request.TargetId;
            OwnerId = request.OwnerId; ItemId = request.ItemId; Kind = request.Kind;
            TargetRevision = request.TargetRevision; CrimeRevision = request.CrimeRevision;
            WitnessRevision = request.WitnessRevision;
            IllegalAccess = request.Kind == NpcCovertActionKind.Steal || !facts.OwnerConsents;
            witnessIds = (string[])facts.WitnessIds.Clone();
        }
    }

    /// <summary>Observed native effect and resulting legal state, never an inferred action success flag.</summary>
    public sealed class NpcCovertOutcome
    {
        public string OperationId, ReceiptId, ActorId, TargetId, OwnerId, ItemId;
        public string ItemOwnerBefore, ItemOwnerAfter;
        public NpcCovertActionKind Kind;
        public NpcCovertEffectKind EffectKind;
        public string EffectId, CrimeIncidentId;
        public bool NativeAdapterExecuted;
        public long TargetRevisionBefore, TargetRevisionAfter, CrimeRevisionBefore, CrimeRevisionAfter, WitnessRevision;
        public string[] WitnessIds;
        public int SourceQuantityBefore, SourceQuantityAfter, ActorQuantityBefore, ActorQuantityAfter;
    }

    public static class NpcCovertActionContract
    {
        public static bool TryAuthorize(NpcCovertRequest request, NpcCovertFacts facts,
            out NpcCovertAuthorization authorization)
        {
            authorization = null;
            if (request == null || facts == null || !Id(request.OperationId) || !Id(request.ActorId) ||
                !Id(request.TargetId) || !Id(request.OwnerId) || request.ActorId == request.OwnerId ||
                (int)request.Kind < 0 || (int)request.Kind > 2 ||
                request.TargetRevision < 0 || request.CrimeRevision < 0 || request.WitnessRevision < 0 ||
                !facts.ActorAlive || !facts.TargetExists || !facts.AdapterAvailable || !facts.DutyAvailable ||
                !facts.ActorConsents || facts.QuestProtected || facts.OwnerId != request.OwnerId ||
                facts.TargetRevision != request.TargetRevision || facts.CrimeRevision != request.CrimeRevision ||
                facts.WitnessRevision != request.WitnessRevision ||
                facts.LockDifficulty < 0 || facts.LockSkill < facts.LockDifficulty ||
                facts.Relationship < facts.MinimumRelationship || facts.Risk < 0 ||
                facts.MaximumRisk < 0 || facts.Risk > facts.MaximumRisk || !ValidWitnesses(facts.WitnessIds))
                return false;

            if (request.Kind == NpcCovertActionKind.Steal)
            {
                if (!Id(request.ItemId) || !facts.ItemExists || !facts.ItemTransferable || facts.OwnerConsents)
                    return false;
            }
            else if (request.ItemId != null) return false;
            authorization = new NpcCovertAuthorization(request, facts);
            return true;
        }

        public static bool MatchesObservedOutcome(NpcCovertAuthorization authorization, NpcCovertOutcome outcome)
        {
            if (authorization == null || outcome == null || !outcome.NativeAdapterExecuted ||
                !Id(outcome.ReceiptId) || !Id(outcome.EffectId) ||
                outcome.OperationId != authorization.OperationId || outcome.ActorId != authorization.ActorId ||
                outcome.TargetId != authorization.TargetId || outcome.OwnerId != authorization.OwnerId ||
                outcome.ItemId != authorization.ItemId || outcome.Kind != authorization.Kind ||
                outcome.TargetRevisionBefore != authorization.TargetRevision ||
                outcome.TargetRevisionAfter != authorization.TargetRevision + 1 ||
                outcome.CrimeRevisionBefore != authorization.CrimeRevision ||
                outcome.WitnessRevision != authorization.WitnessRevision ||
                !SameWitnesses(authorization.WitnessIds, outcome.WitnessIds)) return false;

            if (authorization.IllegalAccess)
            {
                if (outcome.CrimeRevisionAfter != authorization.CrimeRevision + 1 ||
                    !Id(outcome.CrimeIncidentId)) return false;
            }
            else if (outcome.CrimeRevisionAfter != authorization.CrimeRevision ||
                     outcome.CrimeIncidentId != null) return false;

            if (authorization.Kind == NpcCovertActionKind.Steal)
                return outcome.EffectKind == NpcCovertEffectKind.ItemTransferred &&
                    outcome.ItemOwnerBefore == authorization.OwnerId &&
                    outcome.ItemOwnerAfter == authorization.ActorId &&
                    outcome.SourceQuantityBefore > 0 && outcome.SourceQuantityAfter >= 0 &&
                    outcome.ActorQuantityBefore >= 0 &&
                    outcome.SourceQuantityBefore - outcome.SourceQuantityAfter == 1 &&
                    outcome.ActorQuantityAfter - outcome.ActorQuantityBefore == 1;

            if (outcome.ItemOwnerBefore != null || outcome.ItemOwnerAfter != null ||
                outcome.SourceQuantityBefore != 0 || outcome.SourceQuantityAfter != 0 ||
                outcome.ActorQuantityBefore != 0 || outcome.ActorQuantityAfter != 0) return false;
            return authorization.Kind == NpcCovertActionKind.Search
                ? outcome.EffectKind == NpcCovertEffectKind.SearchResultRecorded
                : outcome.EffectKind == NpcCovertEffectKind.ClueRecorded;
        }

        private static bool Id(string id) { return !string.IsNullOrWhiteSpace(id) && id.Length <= 240; }
        private static bool ValidWitnesses(string[] witnesses)
        {
            if (witnesses == null || witnesses.Length > 64) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string witness in witnesses) if (!Id(witness) || !seen.Add(witness)) return false;
            return true;
        }
        private static bool SameWitnesses(string[] expected, string[] actual)
        {
            if (!ValidWitnesses(actual) || expected.Length != actual.Length) return false;
            for (int i = 0; i < expected.Length; i++) if (expected[i] != actual[i]) return false;
            return true;
        }
    }

    /// <summary>Caller persists entries with world state; entries refuse operation and receipt reuse after reload.</summary>
    [Serializable]
    public sealed class NpcCovertReceiptLedger
    {
        private readonly List<NpcCovertCompletion> entries;

        public NpcCovertReceiptLedger() { entries = new List<NpcCovertCompletion>(); }

        public NpcCovertReceiptLedger(IEnumerable<NpcCovertCompletion> saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            entries = new List<NpcCovertCompletion>();
            var operations = new HashSet<string>(StringComparer.Ordinal);
            var receipts = new HashSet<string>(StringComparer.Ordinal);
            var effects = new HashSet<string>(StringComparer.Ordinal);
            var crimes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in saved)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.OperationId) ||
                    string.IsNullOrWhiteSpace(entry.ReceiptId) || string.IsNullOrWhiteSpace(entry.EffectId) ||
                    !operations.Add(entry.OperationId) || !receipts.Add(entry.ReceiptId) ||
                    !effects.Add(entry.EffectId) ||
                    entry.CrimeIncidentId != null &&
                    (string.IsNullOrWhiteSpace(entry.CrimeIncidentId) || !crimes.Add(entry.CrimeIncidentId)))
                    throw new ArgumentException("Invalid covert completion ledger.", nameof(saved));
                entries.Add(Copy(entry));
            }
        }

        public List<NpcCovertCompletion> Snapshot()
        {
            var copy = new List<NpcCovertCompletion>(entries.Count);
            foreach (var entry in entries) copy.Add(Copy(entry));
            return copy;
        }

        public bool HasCompleted(string operationId)
        {
            foreach (var entry in entries) if (entry.OperationId == operationId) return true;
            return false;
        }

        public bool TryComplete(NpcCovertAuthorization authorization, NpcCovertOutcome outcome)
        {
            if (!NpcCovertActionContract.MatchesObservedOutcome(authorization, outcome)) return false;
            foreach (var entry in entries)
                if (entry.OperationId == outcome.OperationId ||
                    entry.ReceiptId == outcome.ReceiptId || entry.EffectId == outcome.EffectId ||
                    outcome.CrimeIncidentId != null && entry.CrimeIncidentId == outcome.CrimeIncidentId)
                    return false;
            entries.Add(new NpcCovertCompletion { OperationId = outcome.OperationId, ReceiptId = outcome.ReceiptId,
                EffectId = outcome.EffectId, CrimeIncidentId = outcome.CrimeIncidentId });
            return true;
        }

        private static NpcCovertCompletion Copy(NpcCovertCompletion entry)
        { return new NpcCovertCompletion { OperationId = entry.OperationId, ReceiptId = entry.ReceiptId,
            EffectId = entry.EffectId, CrimeIncidentId = entry.CrimeIncidentId }; }
    }

    [Serializable]
    public sealed class NpcCovertCompletion
    {
        public string OperationId, ReceiptId, EffectId, CrimeIncidentId;
    }
}
