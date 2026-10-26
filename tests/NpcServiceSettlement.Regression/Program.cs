using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

// Core transaction regressions only. Typed receipt fixtures stand in for evidence
// supplied by a trusted native adapter; these tests do not claim native payment.
internal static class Program
{
    private const string ContractId = "settlement-contract";
    private const int Rate = 120;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static int failures;

    private static int Main()
    {
        Run("late expiry publishes ledger evidence and NPC wallet together", LateExpiryPublishesTogether);
        Run("missing or altered wage proof and wrong wallet leave all state unchanged", InvalidProofOrWalletIsAtomic);
        Run("service epoch inventory revision and receipt replay reject without partial write", StaleAndReplayAreAtomic);
        Run("stale service ledger revision rejects without partial write", StaleServiceLedgerRejects);
        Run("generic service commit cannot publish a wage or refund without wallet credit", GenericPayoutBypassRejects);
        Run("refund-only cancellation leaves NPC wallet untouched", RefundOnlyNeedsNoWalletCredit);
        Run("earnings-only release credits exactly earned amount", EarningsOnlyCreditsWallet);
        Run("sibling candidate and foreign ticket cannot cross the save owner", SiblingCandidateRejects);
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action check)
    {
        try { check(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error); }
    }

    private static void LateExpiryPublishesTogether()
    {
        WorldStateStore store = FundedStore(start: true, suspend: true, out string worker);
        PrepareLateSettlement(store, worker, out var ticket, out var candidate, out var wage,
            out var credit);
        string before = StateSignature(store);
        Assert(store.TryCommitNpcServiceSettlement(ticket, candidate, credit,
            new[] { wage }, out string error), "combined late settlement rejected: " + error);
        Assert(StateSignature(store) != before, "successful settlement changed no saved state");
        AssertSettled(store, worker);

        var restored = new WorldStateStore(918);
        restored.Restore(store.Snapshot());
        AssertSettled(restored, worker);
        Assert(StateSignature(restored) == StateSignature(store), "restore changed settlement accounting or item payload");
    }

    private static void InvalidProofOrWalletIsAtomic()
    {
        WorldStateStore store = FundedStore(start: true, suspend: true, out string worker);
        PrepareLateSettlement(store, worker, out var ticket, out var candidate, out var wage,
            out var credit);
        string before = StateSignature(store);
        AssertRejectedUnchanged(store, ticket, candidate, credit, Array.Empty<NpcServiceTransferReceipt>(), before,
            "missing native wage proof");
        var wrongWage = Transfer("settle-wage", NpcServiceTransferKind.Earnings,
            worker, 119, 90000);
        AssertRejectedUnchanged(store, ticket, candidate, credit, new[] { wrongWage }, before,
            "altered native wage proof");
        AssertRejectedUnchanged(store, ticket, candidate, credit, new[] { wage, wage }, before,
            "duplicate native wage proof");

        var wrongAmount = Credit(store, worker, 119);
        AssertRejectedUnchanged(store, ticket, candidate, wrongAmount, new[] { wage }, before,
            "wallet credit different from exact earnings");
        var wrongItems = Credit(store, worker, 120);
        wrongItems.Items[0].NativePayload = "substituted-item-payload";
        AssertRejectedUnchanged(store, ticket, candidate, wrongItems, new[] { wage }, before,
            "wallet update changed owned item payload");
    }

    private static void StaleAndReplayAreAtomic()
    {
        WorldStateStore store = FundedStore(start: true, suspend: true, out string worker);
        PrepareLateSettlement(store, worker, out var ticket, out var candidate, out var wage,
            out var credit);
        NpcInventoryState inventory = store.ReadNpcInventory(worker);
        Assert(store.CommitNpcInventoryTransaction("unrelated-inventory-revision", new[] {
            new NpcInventoryUpdate { PersonId = worker, ExpectedRevision = inventory.Revision,
                WalletGold = inventory.WalletGold, Items = inventory.Items }
        }), "fixture could not advance inventory revision");
        string afterRevision = StateSignature(store);
        AssertRejectedUnchanged(store, ticket, candidate, credit, new[] { wage }, afterRevision,
            "stale NPC inventory revision");

        NpcInventoryUpdate refreshedCredit = Credit(store, worker, 120);
        WorldState saved = store.Snapshot();
        store.Restore(saved);
        string afterRestore = StateSignature(store);
        AssertRejectedUnchanged(store, ticket, candidate, refreshedCredit, new[] { wage }, afterRestore,
            "ticket from previous store epoch");

        PrepareLateSettlement(store, worker, out var freshTicket, out var freshCandidate,
            out var freshWage, out var freshCredit);
        Assert(store.TryCommitNpcServiceSettlement(freshTicket, freshCandidate, freshCredit,
            new[] { freshWage }, out string error), "fresh retry rejected: " + error);
        string committed = StateSignature(store);
        AssertRejectedUnchanged(store, freshTicket, freshCandidate, freshCredit,
            new[] { freshWage }, committed, "replayed committed receipts");
        AssertSettled(store, worker, expectedInventoryRevision: 2);
    }

    private static void StaleServiceLedgerRejects()
    {
        WorldStateStore store = FundedStore(start: true, suspend: true, out string worker);
        PrepareLateSettlement(store, worker, out var ticket, out var candidate, out var wage,
            out var credit);
        CommitContractTransition(store, current => current.TryResume(current.Revision, 50000, out var next)
            ? next : null);
        string afterRevision = StateSignature(store);
        AssertRejectedUnchanged(store, ticket, candidate, credit, new[] { wage }, afterRevision,
            "stale service ledger revision");
    }

    private static void GenericPayoutBypassRejects()
    {
        WorldStateStore store = FundedStore(start: true, suspend: true, out string worker);
        PrepareLateSettlement(store, worker, out var ticket, out var candidate, out var wage,
            out _);
        string before = StateSignature(store);
        Assert(!store.TryCommitNpcServiceContracts(ticket, candidate, new[] { wage },
            new NpcServiceRenewalAuthorization[0], out _),
            "generic service commit published payout without the NPC wallet");
        Assert(StateSignature(store) == before, "generic payout rejection partly changed saved state");
    }

    private static void RefundOnlyNeedsNoWalletCredit()
    {
        WorldStateStore store = FundedStore(start: false, suspend: false, out string worker);
        var ticket = store.CaptureNpcServiceContractWriteTicket();
        NpcServiceContract reserved = ticket.SourceLedger.Read(ContractId);
        var refund = Transfer("cancel-refund", NpcServiceTransferKind.Refund,
            NpcDirectedRelationship.PlayerTargetId, Rate, 10);
        Assert(reserved.TrySettle(reserved.Revision, 10, NpcServiceEndReason.CancelledBeforeService,
            worker, null, refund, out var closed), "pure refund-only cancellation fixture rejected");
        Assert(NpcServiceContractLedger.TryReplace(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
            closed, out var candidate), "refund-only ledger candidate rejected");
        string before = StateSignature(store);
        AssertRejectedUnchanged(store, ticket, candidate, null,
            new NpcServiceTransferReceipt[0], before, "missing refund proof");
        NpcInventoryState inventoryBefore = store.ReadNpcInventory(worker);
        Assert(store.TryCommitNpcServiceSettlement(ticket, candidate, null, new[] { refund }, out string error),
            "refund-only commit rejected: " + error);
        NpcInventoryState inventoryAfter = store.ReadNpcInventory(worker);
        Assert(inventoryAfter.WalletGold == inventoryBefore.WalletGold &&
            inventoryAfter.Revision == inventoryBefore.Revision &&
            inventoryAfter.LastTransactionId == inventoryBefore.LastTransactionId,
            "refund-only transition changed NPC wallet or inventory revision");
        Assert(store.ReadNpcServiceContracts().Read(ContractId).RefundedGold == Rate &&
            store.Snapshot().NpcServiceEvidence.CompletedTransfers.Count == 2,
            "refund-only ledger/evidence did not publish");
    }

    private static void EarningsOnlyCreditsWallet()
    {
        WorldStateStore store = FundedStore(start: true, suspend: false, out string worker);
        var ticket = store.CaptureNpcServiceContractWriteTicket();
        NpcServiceContract active = ticket.SourceLedger.Read(ContractId);
        var wage = Transfer("daily-wage", NpcServiceTransferKind.Earnings, worker, 120, 43200);
        Assert(active.TryReleaseEarnings(active.Revision, 43200, wage, out var paid),
            "pure daily earnings fixture rejected");
        Assert(NpcServiceContractLedger.TryReplace(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
            paid, out var candidate), "earnings ledger candidate rejected");
        NpcInventoryUpdate credit = Credit(store, worker, 120);
        string before = StateSignature(store);
        AssertRejectedUnchanged(store, ticket, candidate, null, new[] { wage }, before,
            "positive earnings without wallet credit");
        Assert(store.TryCommitNpcServiceSettlement(ticket, candidate, credit, new[] { wage }, out string error),
            "earnings-only commit rejected: " + error);
        var current = store.ReadNpcServiceContracts().Read(ContractId);
        Assert(current.Status == NpcServiceContractStatus.Active && current.ReleasedGold == 120 &&
            current.RefundedGold == 0 && current.EscrowGold == 0 &&
            store.ReadNpcInventory(worker).WalletGold == 127,
            "earnings release did not conserve escrow or credit exact wallet amount");
    }

    private static void SiblingCandidateRejects()
    {
        WorldStateStore original = FundedStore(start: true, suspend: true, out string worker);
        WorldState saved = original.Snapshot();
        var first = new WorldStateStore(1);
        var sibling = new WorldStateStore(2);
        first.Restore(saved); sibling.Restore(saved);
        PrepareLateSettlement(first, worker, out var firstTicket, out var firstCandidate,
            out var wage, out var firstCredit);
        var siblingTicket = sibling.CaptureNpcServiceContractWriteTicket();
        string before = StateSignature(sibling);
        AssertRejectedUnchanged(sibling, firstTicket, firstCandidate, firstCredit,
            new[] { wage }, before, "foreign owner ticket");
        AssertRejectedUnchanged(sibling, siblingTicket, firstCandidate, Credit(sibling, worker, 120),
            new[] { wage }, before, "candidate derived from sibling ticket");
    }

    private static WorldStateStore FundedStore(bool start, bool suspend, out string worker)
    {
        var store = new WorldStateStore(654);
        var town = store.GetOrCreateFamilies(3, new[] { new HomeProfile(17, 1, 60) });
        worker = town.Households[0].Residents[0].Id;
        string residentId = worker;
        Assert(store.InitializeNpcInventory(worker, 7, new[] {
            new NpcInventoryItem { Uid = 70001, NativePayload = "opaque-owned-item-fixture" }
        }), "NPC inventory fixture rejected");
        string homeId = "home:3:17";
        Assert(NpcServiceContractTerms.TryCreate(ContractId, NpcDirectedRelationship.PlayerTargetId,
            worker, homeId, NpcServiceRole.Fighter, NpcServiceCoverage.Follow, Rate, false,
            NpcServiceRiskScope.None, new NpcServiceProtectedTarget[0],
            NpcServiceRestTreatment.RestNotIncluded, NpcServiceResupplyResponsibility.SeparatelyNegotiated,
            new NpcServiceReservedObligation[0],
            id => id == NpcDirectedRelationship.PlayerTargetId || id == residentId,
            id => id == homeId, (id, category, revision) => false,
            (id, revision, kind, begins, ends) => false, out var terms), "service terms fixture rejected");
        var ticket = store.CaptureNpcServiceContractWriteTicket();
        var funding = NpcServiceTransferReceipt.Create("initial-funding", ContractId,
            NpcServiceTransferKind.Funding, NpcDirectedRelationship.PlayerTargetId,
            "contract:" + ContractId + ":escrow", Rate, 0);
        Assert(NpcServiceContractLedger.TryReserve(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
            terms, funding, out var funded), "pure funding fixture rejected");
        Assert(store.TryCommitNpcServiceContracts(ticket, funded, new[] { funding },
            new NpcServiceRenewalAuthorization[0], out string error), "funding fixture commit rejected: " + error);
        if (start) CommitContractTransition(store, current => current.TryStart(current.Revision, 0, out var next) ? next : null);
        if (suspend) CommitContractTransition(store, current => current.TrySuspend(current.Revision, 43200, out var next) ? next : null);
        return store;
    }

    private static void CommitContractTransition(WorldStateStore store, Func<NpcServiceContract, NpcServiceContract> derive)
    {
        var ticket = store.CaptureNpcServiceContractWriteTicket();
        var current = ticket.SourceLedger.Read(ContractId);
        NpcServiceContract changed = derive(current);
        Assert(changed != null, "pure contract transition fixture rejected");
        Assert(NpcServiceContractLedger.TryReplace(ticket.SourceLedger,
            ticket.ExpectedLedgerRevision, changed, out var candidate), "pure ledger transition fixture rejected");
        Assert(store.TryCommitNpcServiceContracts(ticket, candidate, new NpcServiceTransferReceipt[0],
            new NpcServiceRenewalAuthorization[0], out string error), "contract fixture commit rejected: " + error);
    }

    private static void PrepareLateSettlement(WorldStateStore store, string worker,
        out NpcServiceContractWriteTicket ticket, out NpcServiceContractLedger candidate,
        out NpcServiceTransferReceipt wage,
        out NpcInventoryUpdate credit)
    {
        ticket = store.CaptureNpcServiceContractWriteTicket();
        var current = ticket.SourceLedger.Read(ContractId);
        Assert(current.Status == NpcServiceContractStatus.Suspended && current.EarnedGold == 120 &&
            current.EscrowGold == Rate && current.BlockEndAt == 86400,
            "started daily contract fixture has wrong accounting");
        wage = Transfer("settle-wage", NpcServiceTransferKind.Earnings, worker, 120, 90000);
        Assert(current.TrySettle(current.Revision, 90000, NpcServiceEndReason.NaturalExpiry,
            worker, wage, null, out var closed), "pure late-expiry settlement fixture rejected");
        Assert(closed.SettlementRecordedAt == 90000 && closed.LastServiceUpdateAt == 43200,
            "late-expiry fixture backdated receipt or billed the pause");
        Assert(NpcServiceContractLedger.TryReplace(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
            closed, out candidate), "settlement ledger candidate rejected");
        credit = Credit(store, worker, 120);
    }

    private static NpcServiceTransferReceipt Transfer(string id, NpcServiceTransferKind kind,
        string target, int amount, double at)
    {
        return NpcServiceTransferReceipt.Create(id, ContractId, kind,
            "contract:" + ContractId + ":escrow", target, amount, at);
    }

    private static NpcInventoryUpdate Credit(WorldStateStore store, string worker, int gold)
    {
        NpcInventoryState saved = store.ReadNpcInventory(worker);
        Assert(saved != null, "saved NPC wallet missing");
        return new NpcInventoryUpdate { PersonId = worker, ExpectedRevision = saved.Revision,
            WalletGold = checked(saved.WalletGold + gold), Items = saved.Items };
    }

    private static void AssertRejectedUnchanged(WorldStateStore store, NpcServiceContractWriteTicket ticket,
        NpcServiceContractLedger candidate, NpcInventoryUpdate credit,
        IList<NpcServiceTransferReceipt> proofs, string expected, string caseName)
    {
        Assert(!store.TryCommitNpcServiceSettlement(ticket, candidate, credit, proofs, out string error),
            caseName + " unexpectedly committed");
        Assert(StateSignature(store) == expected,
            caseName + " partly changed contract, receipt evidence, or NPC inventory: " + error);
    }

    private static void AssertSettled(WorldStateStore store, string worker, long expectedInventoryRevision = 1)
    {
        var saved = store.Snapshot();
        var contract = store.ReadNpcServiceContracts().Read(ContractId);
        var wallet = store.ReadNpcInventory(worker);
        Assert(contract.Status == NpcServiceContractStatus.Terminated &&
            contract.EndReason == NpcServiceEndReason.NaturalExpiry &&
            contract.FundedGold == Rate && contract.ReleasedGold == 120 &&
            contract.RefundedGold == 0 && contract.EscrowGold == 0 &&
            contract.SettlementRecordedAt == 90000 && contract.LastServiceUpdateAt == 43200,
            "late settlement ledger lost amount or actual transfer time");
        Assert(wallet.WalletGold == 127 && wallet.Revision == expectedInventoryRevision && wallet.Items.Count == 1 &&
            wallet.Items[0].Uid == 70001 && wallet.Items[0].NativePayload == "opaque-owned-item-fixture",
            "settlement wallet credit changed the original owned item");
        Assert(saved.NpcServiceEvidence.CompletedTransfers.Count == 2 &&
            saved.NpcServiceEvidence.CompletedTransfers[1].ReceiptId == "settle-wage" &&
            saved.NpcServiceEvidence.CompletedTransfers[1].Amount == 120,
            "exact daily earnings proof was not saved");
    }

    private static string StateSignature(WorldStateStore store)
    {
        WorldState saved = store.Snapshot();
        return JsonSerializer.Serialize(new { Contracts = saved.NpcServiceContracts,
            Evidence = saved.NpcServiceEvidence, Inventories = saved.NpcInventories }, Json);
    }

    private static void Assert(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
