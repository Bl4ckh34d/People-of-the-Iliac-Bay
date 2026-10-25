using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>
    /// Save-owned registry of native evidence accepted by the WorldStateStore after a completed transfer or
    /// explicit renewal-authorization action. Public fields are serializer data, never model output or commands.
    /// The contract ledger may only reference exact rows in this registry.
    /// </summary>
    [Serializable]
    public sealed class NpcServiceNativeEvidenceState
    {
        public const int VersionCurrent = 1;
        public int Version = VersionCurrent;
        public long Revision;
        public List<NpcServiceTransferReceiptState> CompletedTransfers = new List<NpcServiceTransferReceiptState>();
        public List<NpcServiceAuthorizationReceiptState> Authorizations = new List<NpcServiceAuthorizationReceiptState>();

        public NpcServiceNativeEvidenceState Copy()
        {
            var copy = new NpcServiceNativeEvidenceState { Version = Version, Revision = Revision };
            copy.CompletedTransfers = CopyList(CompletedTransfers, x => x == null ? null : x.Copy());
            copy.Authorizations = CopyList(Authorizations, x => x == null ? null : x.Copy());
            return copy;
        }

        private static List<TOut> CopyList<TIn, TOut>(List<TIn> list, Func<TIn, TOut> copy)
        {
            if (list == null) return null;
            var result = new List<TOut>(list.Count);
            foreach (var item in list) result.Add(copy(item));
            return result;
        }
    }

    /// <summary>Persisted native renewal permission action, including revocation as an explicit false value.</summary>
    [Serializable]
    public sealed class NpcServiceAuthorizationReceiptState
    {
        public string ReceiptId;
        public string ContractId;
        public string PayerId;
        public bool Permission;
        public double GameTime;

        public NpcServiceAuthorizationReceiptState Copy()
        { return (NpcServiceAuthorizationReceiptState)MemberwiseClone(); }
    }

    /// <summary>
    /// Serializer-facing snapshot for one save branch. Public fields are deliberately data-only;
    /// v1 is the first format and restore rejects other versions until an explicit migration exists.
    /// Restore validates every reference and native transfer receipt before publishing a ledger.
    /// </summary>
    [Serializable]
    public sealed class NpcServiceContractLedgerState
    {
        public const int VersionCurrent = 1;
        public int Version = VersionCurrent;
        public string SaveBranchId;
        public long Revision;
        public List<NpcServiceContractState> Contracts = new List<NpcServiceContractState>();

        public NpcServiceContractLedgerState Copy()
        {
            var copy = new NpcServiceContractLedgerState { Version = Version, SaveBranchId = SaveBranchId, Revision = Revision };
            if (Contracts == null) { copy.Contracts = null; return copy; }
            copy.Contracts = new List<NpcServiceContractState>(Contracts.Count);
            foreach (var contract in Contracts) copy.Contracts.Add(contract == null ? null : contract.Copy());
            return copy;
        }
    }

    /// <summary>Complete scalar and typed-reference snapshot of a single contract's current accounting state.</summary>
    [Serializable]
    public sealed class NpcServiceContractState
    {
        public const int VersionCurrent = 2;
        public int Version = VersionCurrent;
        public string ContractId;
        public string PayerId;
        public string NpcId;
        public string HomeReference;
        public NpcServiceRole Role;
        public NpcServiceCoverage Coverage;
        public int RatePer24GameHours;
        public bool StandingRenewalPermission;
        public NpcServiceRiskScope RiskScope;
        public NpcServiceRestTreatment RestTreatment;
        public NpcServiceResupplyResponsibility ResupplyResponsibility;
        public NpcServiceContractStatus Status;
        public NpcServiceSuspensionOwnerKind SuspensionOwnerKind;
        public string SuspensionOwnerId;
        public bool HasEndReason;
        public NpcServiceEndReason EndReason;
        public long Revision;
        public double AcceptedAt;
        public double BlockStartAt;
        public double BlockEndAt;
        public double LastServiceUpdateAt;
        public double SettlementRecordedAt = -1;
        public double EligibleServiceSeconds;
        public double CurrentBlockEligibleSeconds;
        public int CurrentBlockEarnedGold;
        public int CurrentBlockReleasedGold;
        public int FundedGold;
        public int EscrowGold;
        public int EarnedGold;
        public int ReleasedGold;
        public int RefundedGold;
        public List<string> AuthorizationReceiptIds = new List<string>();
        public List<NpcServiceTransferReceiptState> TransferReceipts = new List<NpcServiceTransferReceiptState>();
        public List<NpcServiceProtectedTargetState> ProtectedTargets = new List<NpcServiceProtectedTargetState>();
        public List<NpcServiceReservedObligationState> ReservedObligations = new List<NpcServiceReservedObligationState>();

        public NpcServiceContractState Copy()
        {
            var copy = (NpcServiceContractState)MemberwiseClone();
            copy.AuthorizationReceiptIds = CopyList(AuthorizationReceiptIds, x => x);
            copy.TransferReceipts = CopyList(TransferReceipts, x => x == null ? null : x.Copy());
            copy.ProtectedTargets = CopyList(ProtectedTargets, x => x == null ? null : x.Copy());
            copy.ReservedObligations = CopyList(ReservedObligations, x => x == null ? null : x.Copy());
            return copy;
        }

        private static List<TOut> CopyList<TIn, TOut>(List<TIn> list, Func<TIn, TOut> copy)
        {
            if (list == null) return null;
            var result = new List<TOut>(list.Count);
            foreach (var item in list) result.Add(copy(item));
            return result;
        }
    }

    /// <summary>Serialized facts of a previously completed native currency transfer, not a transfer command.</summary>
    [Serializable]
    public sealed class NpcServiceTransferReceiptState
    {
        public string ReceiptId;
        public string ContractId;
        public NpcServiceTransferKind Kind;
        public string FromOwnerId;
        public string ToOwnerId;
        public int Amount;
        public double GameTime;

        public NpcServiceTransferReceiptState Copy()
        {
            return (NpcServiceTransferReceiptState)MemberwiseClone();
        }
    }

    [Serializable]
    public sealed class NpcServiceProtectedTargetState
    {
        public string ActorId;
        public NpcServiceProtectedTargetCategory Category;
        public long SourceRevision;
        public NpcServiceProtectedTargetState Copy() { return (NpcServiceProtectedTargetState)MemberwiseClone(); }
    }

    [Serializable]
    public sealed class NpcServiceReservedObligationState
    {
        public string ObligationId;
        public long SourceRevision;
        public NpcServiceReservedObligationKind Kind;
        public NpcServiceObligationTreatment Treatment;
        public double StartsAt;
        public double EndsAt;
        public NpcServiceReservedObligationState Copy() { return (NpcServiceReservedObligationState)MemberwiseClone(); }
    }
}
