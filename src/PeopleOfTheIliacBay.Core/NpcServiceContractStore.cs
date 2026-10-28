using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>One owner-, epoch-, save-scope-, and ledger-revision-bound service write ticket.</summary>
    public sealed class NpcServiceContractWriteTicket
    {
        internal WorldStateStore Owner;
        internal object Epoch;
        internal string ScopeId;
        /// <summary>Immutable source used to derive the candidate committed by this ticket.</summary>
        public NpcServiceContractLedger SourceLedger { get; internal set; }
        public long ExpectedLedgerRevision { get; internal set; }
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>Returns an immutable ledger view bound to this exact save binding.</summary>
        public NpcServiceContractLedger ReadNpcServiceContracts()
        {
            NpcServiceContractWriteTicket ticket = CaptureNpcServiceContractWriteTicket();
            return ticket.SourceLedger;
        }

        /// <summary>Captures the current owner, restore epoch, branch scope and ledger revision.</summary>
        public NpcServiceContractWriteTicket CaptureNpcServiceContractWriteTicket()
        {
            NpcServiceContractLedger ledger;
            if (!NpcServiceContractLedger.TryRestoreHistorical(state.NpcServiceContracts, state.NpcFrameworkScopeId,
                IsKnownNpcExperienceIdentity, state.NpcServiceEvidence, out ledger))
                throw new InvalidOperationException("Saved service-contract evidence is invalid.");
            return new NpcServiceContractWriteTicket { Owner = this, Epoch = bindingEpoch,
                ScopeId = state.NpcFrameworkScopeId, SourceLedger = ledger, ExpectedLedgerRevision = ledger.Revision };
        }

        /// <summary>Checks whether a captured service write ticket still belongs to this current save binding.</summary>
        /// <remarks>This is a read-only preflight for native adapters that must validate before an irreversible
        /// game-side mutation. Commit still repeats every check atomically.</remarks>
        public bool IsCurrentNpcServiceContractWriteTicket(NpcServiceContractWriteTicket ticket)
        {
            return ticket != null && ticket.Owner == this && ReferenceEquals(ticket.Epoch, bindingEpoch) &&
                ticket.ScopeId == state.NpcFrameworkScopeId && ticket.SourceLedger != null &&
                ticket.SourceLedger.Version == NpcServiceContractLedger.VersionCurrent &&
                ticket.SourceLedger.Revision == ticket.ExpectedLedgerRevision &&
                ticket.ExpectedLedgerRevision != long.MaxValue && state.NpcServiceContracts != null &&
                state.NpcServiceContracts.SaveBranchId == ticket.ScopeId &&
                state.NpcServiceContracts.Revision == ticket.ExpectedLedgerRevision;
        }

        /// <summary>
        /// Native game-adapter boundary for the pure service ledger. Atomically publishes a detached ledger candidate
        /// and its newly completed native evidence rows. Call only from the trusted adapter after currency transfer
        /// or permission action actually completes. Model/action parsers do not dispatch receipts here; receipt
        /// factories validate record shape, not native completion.
        /// </summary>
        public bool TryCommitNpcServiceContracts(NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate,
            IList<NpcServiceTransferReceipt> completedNativeTransfers,
            IList<NpcServiceRenewalAuthorization> completedNativeAuthorizations,
            out string error)
        {
            if (completedNativeTransfers == null || completedNativeTransfers.Count != 0)
            { error = "Daily payments and failed-start reversals require the combined wallet commit."; return false; }
            NpcServiceContractLedgerState nextContracts;
            NpcServiceNativeEvidenceState nextEvidence;
            if (!TryPrepareNpcServiceContractCommit(ticket, candidate, completedNativeTransfers,
                completedNativeAuthorizations, out nextContracts, out nextEvidence, out error)) return false;

            state.NpcServiceEvidence = nextEvidence;
            state.NpcServiceContracts = nextContracts;
            error = null;
            return true;
        }

        private bool TryPrepareNpcServiceContractCommit(NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate,
            IList<NpcServiceTransferReceipt> completedNativeTransfers,
            IList<NpcServiceRenewalAuthorization> completedNativeAuthorizations,
            out NpcServiceContractLedgerState nextContracts,
            out NpcServiceNativeEvidenceState nextEvidence, out string error)
        {
            nextContracts = null;
            nextEvidence = null;
            error = "Invalid or stale service-contract write ticket.";
            if (!IsCurrentNpcServiceContractWriteTicket(ticket) || candidate == null || completedNativeTransfers == null ||
                completedNativeAuthorizations == null || state.NpcServiceContracts == null || state.NpcServiceEvidence == null ||
                !candidate.IsDirectlyDerivedFrom(ticket.SourceLedger) ||
                ticket.ExpectedLedgerRevision == long.MaxValue || candidate.Revision != ticket.ExpectedLedgerRevision + 1)
                return false;

            if (!IsSingleContractTransition(ticket.SourceLedger, candidate))
            { error = "Candidate is not one revision-bound service-contract transition."; return false; }

            NpcServiceContractLedgerState candidateState;
            try
            {
                candidateState = candidate.ToSaveState(ticket.ScopeId);
                nextEvidence = state.NpcServiceEvidence.Copy();
                if (!AppendNewNativeEvidence(ticket.SourceLedger, candidate, nextEvidence,
                    completedNativeTransfers, completedNativeAuthorizations))
                { error = "New ledger receipts lack exact, unused native completion evidence."; return false; }
            }
            catch (ArgumentException)
            { error = "Candidate contract ledger or native evidence is malformed."; return false; }
            catch (OverflowException)
            { error = "Service-contract accounting overflowed."; return false; }

            NpcServiceContractLedger validated;
            if (!NpcServiceContractLedger.TryRestoreHistorical(candidateState, ticket.ScopeId,
                IsKnownNpcExperienceIdentity, nextEvidence, out validated))
            { error = "Candidate ledger does not agree with the save-owned native evidence registry."; return false; }

            // Both detached replacements are complete before either save component is published.
            nextContracts = validated.ToSaveState(ticket.ScopeId);
            error = null;
            return true;
        }

        private static bool IsSingleContractTransition(NpcServiceContractLedger source,
            NpcServiceContractLedger candidate)
        {
            if (source == null || candidate == null || source.Version != NpcServiceContractLedger.VersionCurrent ||
                candidate.Version != NpcServiceContractLedger.VersionCurrent || candidate.Revision != source.Revision + 1 ||
                candidate.Contracts.Count < source.Contracts.Count || candidate.Contracts.Count > source.Contracts.Count + 1)
                return false;
            int changed = candidate.Contracts.Count - source.Contracts.Count;
            foreach (var oldContract in source.Contracts)
            {
                var updated = candidate.Read(oldContract.ContractId);
                if (updated == null) return false;
                if (oldContract.HasSameSavedState(updated)) continue;
                if (++changed > 1 || updated.Revision != oldContract.Revision + 1 ||
                    !oldContract.HasSameImmutableTerms(updated) || !PreservesReceiptHistory(oldContract, updated)) return false;
            }
            if (changed != 1) return false;
            foreach (var contract in candidate.Contracts)
                if (source.Read(contract.ContractId) == null && (contract.Revision != 1 ||
                    contract.Status != NpcServiceContractStatus.Reserved || contract.AuthorizationReceiptIdsCount != 0)) return false;
            return true;
        }

        private static bool PreservesReceiptHistory(NpcServiceContract source, NpcServiceContract candidate)
        {
            if (candidate.TransferReceipts.Count < source.TransferReceipts.Count ||
                candidate.AuthorizationReceiptIdsCount < source.AuthorizationReceiptIdsCount) return false;
            for (int i = 0; i < source.TransferReceipts.Count; i++)
                if (!NpcServiceContract.SameReceipt(source.TransferReceipts[i], candidate.TransferReceipts[i])) return false;
            for (int i = 0; i < source.AuthorizationReceiptIdsCount; i++)
                if (source.ReadAuthorizationReceiptId(i) != candidate.ReadAuthorizationReceiptId(i)) return false;
            return true;
        }

        private static bool AppendNewNativeEvidence(NpcServiceContractLedger source,
            NpcServiceContractLedger candidate, NpcServiceNativeEvidenceState evidence,
            IList<NpcServiceTransferReceipt> completedTransfers,
            IList<NpcServiceRenewalAuthorization> completedAuthorizations)
        {
            if (!NpcServiceContractLedger.ValidEvidenceRegistry(evidence) ||
                evidence.Revision > long.MaxValue - completedTransfers.Count - completedAuthorizations.Count) return false;
            var usedTransferProofs = new HashSet<string>(StringComparer.Ordinal);
            var usedAuthorizationProofs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contract in candidate.Contracts)
            {
                var oldContract = source.Read(contract.ContractId);
                int firstNewTransfer = oldContract == null ? 0 : oldContract.TransferReceipts.Count;
                for (int i = firstNewTransfer; i < contract.TransferReceipts.Count; i++)
                {
                    var receipt = contract.TransferReceipts[i];
                    if (source.HasReceiptId(receipt.ReceiptId) ||
                        !MatchAndUseTransferProof(receipt, completedTransfers, usedTransferProofs)) return false;
                    evidence.CompletedTransfers.Add(ToState(receipt));
                    evidence.Revision++;
                }
                int firstNewAuthorization = oldContract == null ? 0 : oldContract.AuthorizationReceiptIdsCount;
                for (int i = firstNewAuthorization; i < contract.AuthorizationReceiptIdsCount; i++)
                {
                    string id = contract.ReadAuthorizationReceiptId(i);
                    if (source.HasReceiptId(id) || !MatchAndUseAuthorizationProof(contract, id,
                        completedAuthorizations, usedAuthorizationProofs, out var authorization)) return false;
                    evidence.Authorizations.Add(new NpcServiceAuthorizationReceiptState
                    { ContractId = authorization.ContractId, ReceiptId = authorization.ReceiptId,
                        PayerId = authorization.PayerId, Permission = authorization.Permission,
                        GameTime = authorization.GameTime });
                    evidence.Revision++;
                }
            }
            return usedTransferProofs.Count == completedTransfers.Count &&
                usedAuthorizationProofs.Count == completedAuthorizations.Count;
        }

        private static bool MatchAndUseTransferProof(NpcServiceTransferReceipt receipt,
            IList<NpcServiceTransferReceipt> proofs, HashSet<string> used)
        {
            if (receipt == null) return false;
            int matches = 0;
            foreach (var proof in proofs)
                if (NpcServiceContract.SameReceipt(receipt, proof))
                { matches++; if (!used.Add(proof.ReceiptId)) return false; }
            return matches == 1;
        }

        private static bool MatchAndUseAuthorizationProof(NpcServiceContract contract, string id,
            IList<NpcServiceRenewalAuthorization> proofs, HashSet<string> used,
            out NpcServiceRenewalAuthorization result)
        {
            result = null;
            int matches = 0;
            foreach (var proof in proofs)
            {
                if (proof != null && proof.ContractId == contract.ContractId && proof.PayerId == contract.PayerId && proof.ReceiptId == id)
                { result = proof; matches++; if (!used.Add(proof.ReceiptId)) return false; }
            }
            if (matches != 1 || result.Permission != contract.StandingRenewalPermission ||
                result.GameTime < contract.AcceptedAt || contract.Status != NpcServiceContractStatus.Reserved &&
                result.GameTime > contract.LastServiceUpdateAt) return false;
            try
            {
                NpcServiceRenewalAuthorization.Create(result.ReceiptId, result.ContractId, result.PayerId,
                    result.Permission, result.GameTime);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static NpcServiceTransferReceiptState ToState(NpcServiceTransferReceipt receipt)
        {
            return new NpcServiceTransferReceiptState { ReceiptId = receipt.ReceiptId, ContractId = receipt.ContractId,
                Kind = receipt.Kind, FromOwnerId = receipt.FromOwnerId, ToOwnerId = receipt.ToOwnerId,
                Amount = receipt.Amount, GameTime = receipt.GameTime };
        }

        /// <summary>WorldState copy hook: retain validated, exact native evidence rows as a detached save DTO.</summary>
        internal static NpcServiceNativeEvidenceState CopyNpcServiceNativeEvidence(WorldState source,
            WorldState validated, Func<string, bool> knownActor)
        {
            if (source == null || validated == null || knownActor == null)
                throw new ArgumentNullException("Service evidence copy requires validated world and actor IDs.");
            if (!NpcServiceContractLedger.ValidEvidenceRegistry(source.NpcServiceEvidence))
                throw new ArgumentException("Invalid save-owned native service evidence registry.");
            foreach (var authorization in source.NpcServiceEvidence.Authorizations)
                if (authorization.PayerId != NpcDirectedRelationship.PlayerTargetId && !knownActor(authorization.PayerId))
                    throw new ArgumentException("Service authorization payer is not known in the save.");
            return source.NpcServiceEvidence.Copy();
        }

        /// <summary>WorldState copy hook: structurally restore historical terms and cross-check every native receipt row.</summary>
        internal static NpcServiceContractLedgerState CopyNpcServiceContracts(WorldState source,
            WorldState validated, NpcServiceNativeEvidenceState evidence, Func<string, bool> knownActor)
        {
            if (source == null || validated == null || evidence == null || knownActor == null)
                throw new ArgumentNullException("Service contract copy requires validated save context.");
            NpcServiceContractLedger ledger;
            if (!NpcServiceContractLedger.TryRestoreHistorical(source.NpcServiceContracts,
                validated.NpcFrameworkScopeId, knownActor, evidence, out ledger))
                throw new ArgumentException("Invalid historical service-contract ledger or evidence crosslink.");
            return ledger.ToSaveState(validated.NpcFrameworkScopeId);
        }
    }
}
