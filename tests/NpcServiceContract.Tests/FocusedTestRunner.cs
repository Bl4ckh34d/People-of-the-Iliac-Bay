using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class FocusedTestRunner
{
    private static int Main()
    {
        Run("known actor and persistent home required", InvalidReferencesReject);
        Run("contract scope requires explicit risk and preserves protected actors", ContractRiskAndProtection);
        Run("reserved appointment blocks a conflicting funded service interval", AdmissionConstraintBlocksStart);
        Run("funding receipt reserves exact escrow once", FundingIsReceiptBound);
        Run("ledger replacement is revision bound and receipt ids are global", LedgerReplacementIsAtomic);
        Run("service starts at actual ready time", StartUsesActualServiceTime);
        Run("started day earns its fixed fee once across midnight and dismissal", StartedDayIsNotProrated);
        Run("daily fee releases once without fractional installments", DailyFeeReleasesOnce);
        Run("suspension preserves day fee and fixed expiry", SuspensionKeepsFixedExpiry);
        Run("renewal requires permission and exact boundary", RenewalBoundary);
        Run("renewal permission can be revoked prospectively", RenewalPermissionRevocation);
        Run("late active updates cannot bypass exact expiry settlement", ExactExpiryBoundary);
        Run("pre-service cancellation refunds entire reservation", PreServiceCancellation);
        Run("death settles started day wage to estate once", DeathSettlement);
        Run("versioned save DTO round-trips detached ledger state", SaveStateRoundTrip);
        Run("save DTO rejects branch, version, receipt and accounting corruption", SaveStateRejectsCorruption);
        Run("historical restore keeps valid old references without current authority", HistoricalReferencesRestore);
        Run("historical restore requires exact saved native evidence crosslinks", HistoricalEvidenceCrosslinks);
        Run("world store commits native evidence with its contract ledger and survives restore", StoreCommitAndRestore);
        Run("world store rejects missing, replayed, stale and corrupted evidence atomically", StoreRejectsUnprovenWrites);
        Run("world store rejects a candidate replayed into a sibling save", StoreRejectsSiblingReplay);
        Run("service ticket preflight rejects foreign, stale and previous-binding tickets", ServiceTicketPreflight);
        return failures == 0 ? 0 : 1;
    }

    private static int failures;
    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + exception.Message); }
    }

    private static void InvalidReferencesReject()
    {
        NpcServiceContractTerms terms;
        Assert(!NpcServiceContractTerms.TryCreate("contract-1", "player", "npc-1", "home-1",
            NpcServiceRole.Fighter, NpcServiceCoverage.Follow | NpcServiceCoverage.Combat,
            100, true, NpcServiceRiskScope.OrdinaryCombat, new NpcServiceProtectedTarget[0], NpcServiceRestTreatment.RestNotIncluded,
            NpcServiceResupplyResponsibility.SeparatelyNegotiated, new NpcServiceReservedObligation[0],
            id => id != "npc-1", id => true, (id, category, revision) => true, (id, revision, kind, starts, ends) => true, out terms), "unknown worker accepted");
        Assert(!NpcServiceContractTerms.TryCreate("contract-1", "player", "npc-1", "home-1",
            NpcServiceRole.Fighter, NpcServiceCoverage.Follow | NpcServiceCoverage.Combat,
            100, true, NpcServiceRiskScope.OrdinaryCombat, new NpcServiceProtectedTarget[0], NpcServiceRestTreatment.RestNotIncluded,
            NpcServiceResupplyResponsibility.SeparatelyNegotiated, new NpcServiceReservedObligation[0],
            id => true, id => false, (id, category, revision) => true, (id, revision, kind, starts, ends) => true, out terms), "unknown home accepted");
    }

    private static void ContractRiskAndProtection()
    {
        NpcServiceContractTerms terms;
        Assert(!NpcServiceContractTerms.TryCreate("contract-1", "player", "npc-1", "home-1",
            NpcServiceRole.Fighter, NpcServiceCoverage.Follow | NpcServiceCoverage.Combat, 100, false,
            NpcServiceRiskScope.None, new[] { ProtectedTarget("family-1", NpcServiceProtectedTargetCategory.Family) }, NpcServiceRestTreatment.RestNotIncluded,
            NpcServiceResupplyResponsibility.SeparatelyNegotiated, new NpcServiceReservedObligation[0],
            id => true, id => true, (id, category, revision) => true, (id, revision, kind, starts, ends) => true, out terms), "combat accepted without its explicit risk scope");
        Assert(!NpcServiceContractTerms.TryCreate("contract-1", "player", "npc-1", "home-1",
            NpcServiceRole.Fighter, NpcServiceCoverage.Follow, 100, false, NpcServiceRiskScope.None,
            new[] { ProtectedTarget("family-1", NpcServiceProtectedTargetCategory.Family) },
            NpcServiceRestTreatment.RestNotIncluded, NpcServiceResupplyResponsibility.SeparatelyNegotiated,
            new NpcServiceReservedObligation[0], id => true, id => true, (id, category, revision) => false,
            (id, revision, kind, starts, ends) => true, out terms), "unverified protected-person claim accepted");
        var valid = Terms(100, false);
        NpcServiceContractLedger ledger;
        Assert(NpcServiceContractLedger.TryReserve(NpcServiceContractLedger.Create(), 0, valid,
            Transfer("scope-fund", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", 100, 0), out ledger),
            "contract with explicit risk and protected person rejected");
        Assert(ledger.Read("contract-1").IsProtectedActor("family-1") &&
            ledger.Read("contract-1").ProtectedTargets[0].Category == NpcServiceProtectedTargetCategory.Family,
            "typed protected actor reference was lost from accepted contract");
        Assert(!ledger.Read("contract-1").IsProtectedActor("unlisted-actor"), "contract protected an unlisted actor");
    }

    private static void AdmissionConstraintBlocksStart()
    {
        var appointment = Obligation("appointment-1", NpcServiceReservedObligationKind.Appointment,
            NpcServiceObligationTreatment.AdmissionConstraint, 20000, 25000);
        var terms = Terms(100, false, new[] { appointment });
        NpcServiceContractLedger ledger;
        Assert(NpcServiceContractLedger.TryReserve(NpcServiceContractLedger.Create(), 0, terms,
            Transfer("appointment-fund", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", 100, 0), out ledger),
            "valid obligation reference rejected");
        var reserved = ledger.Read("contract-1"); NpcServiceContract started;
        Assert(!reserved.TryStart(reserved.Revision, 0, out started), "funded service started over accepted appointment");
        Assert(reserved.Status == NpcServiceContractStatus.Reserved && reserved.EscrowGold == 100,
            "blocked start changed reserved escrow");
    }

    private static void FundingIsReceiptBound()
    {
        var terms = Terms(100, true);
        var wrong = Transfer("fund-1", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", 99, 10);
        NpcServiceContractLedger ledger;
        Assert(!NpcServiceContractLedger.TryReserve(NpcServiceContractLedger.Create(), 0, terms, wrong, out ledger), "partial funding accepted");
        var futureDays = Transfer("fund-future", NpcServiceTransferKind.Funding,
            "player", "contract:contract-1:escrow", 200, 10);
        Assert(!NpcServiceContractLedger.TryReserve(NpcServiceContractLedger.Create(), 0, terms, futureDays, out ledger),
            "unsupported future prepaid days were admitted");
        var funding = Transfer("fund-1", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", 100, 10);
        Assert(NpcServiceContractLedger.TryReserve(NpcServiceContractLedger.Create(), 0, terms, funding, out ledger), "valid funding rejected");
        var contract = ledger.Read("contract-1");
        Assert(contract.Status == NpcServiceContractStatus.Reserved && contract.EscrowGold == 100, "reservation state wrong");
        Assert(!NpcServiceContractLedger.TryReserve(ledger, ledger.Revision, Terms(100, true), funding, out ledger), "funding receipt replay accepted");
    }

    private static void StartUsesActualServiceTime()
    {
        var contract = Reserved(100);
        NpcServiceContract started;
        Assert(contract.TryStart(contract.Revision, 3600, out started), "actual start rejected");
        Assert(started.BlockStartAt == 3600 && started.BlockEndAt == 90000, "block did not start at ready time");
        Assert(!started.TryStart(started.Revision, 3600, out contract), "second start accepted");
    }

    private static void LedgerReplacementIsAtomic()
    {
        var ledger = NpcServiceContractLedger.Create();
        NpcServiceContractLedger nextLedger;
        Assert(NpcServiceContractLedger.TryReserve(ledger, 0, Terms(100, true),
            Transfer("global-fund", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", 100, 0), out nextLedger),
            "first contract funding failed");
        var active = nextLedger.Read("contract-1"); NpcServiceContract started;
        Assert(active.TryStart(active.Revision, 0, out started), "first contract start failed");
        Assert(NpcServiceContractLedger.TryReplace(nextLedger, nextLedger.Revision, started, out ledger), "revision-bound start replace failed");
        var current = ledger.Read("contract-1");
        var wrong = Transfer("bad-wage", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "npc-1", 49, 43200);
        NpcServiceContract rejected;
        Assert(!current.TryReleaseEarnings(current.Revision, 43200, wrong, out rejected) && rejected == null,
            "incorrect receipt created a candidate");
        var wage = Transfer("global-wage", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "npc-1", 100, 43200);
        NpcServiceContract paid;
        Assert(current.TryReleaseEarnings(current.Revision, 43200, wage, out paid), "valid wage release failed");
        Assert(!NpcServiceContractLedger.TryReplace(ledger, ledger.Revision - 1, paid, out nextLedger),
            "stale ledger revision accepted replacement");
        Assert(NpcServiceContractLedger.TryReplace(ledger, ledger.Revision, paid, out nextLedger), "valid atomic replacement failed");
        NpcServiceContractTerms otherTerms;
        Assert(NpcServiceContractTerms.TryCreate("contract-2", "player", "npc-2", "home-2", NpcServiceRole.Archer,
            NpcServiceCoverage.Follow, 100, false, NpcServiceRiskScope.None, new NpcServiceProtectedTarget[0],
            NpcServiceRestTreatment.RestNotIncluded, NpcServiceResupplyResponsibility.SeparatelyNegotiated,
            new NpcServiceReservedObligation[0], id => true, id => true, (id, category, revision) => true,
            (id, revision, kind, starts, ends) => true, out otherTerms), "second terms invalid");
        Assert(!NpcServiceContractLedger.TryReserve(nextLedger, nextLedger.Revision, otherTerms,
            Transfer("global-wage", NpcServiceTransferKind.Funding, "player", "contract:contract-2:escrow", 100, 0), out ledger),
            "receipt id reused across contracts");
        Assert(nextLedger.Read("contract-1").ReleasedGold == 100 && nextLedger.Read("contract-2") == null,
            "failed ledger operation changed prior state");
    }

    private static void DailyFeeReleasesOnce()
    {
        var contract = Started(100, 0);
        NpcServiceContract next;
        var fractional = Transfer("fractional-wage", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "npc-1", 33, 28800);
        Assert(!contract.TryReleaseEarnings(contract.Revision, 28800, fractional, out next), "fractional wage accepted");
        var first = Transfer("wage-1", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "npc-1", 100, 28800);
        Assert(contract.TryReleaseEarnings(contract.Revision, 28800, first, out next), "daily release rejected");
        Assert(next.EarnedGold == 100 && next.ReleasedGold == 100 && next.EscrowGold == 0, "daily release amount wrong");
        Assert(!next.TryReleaseEarnings(next.Revision, 43200, first, out contract), "wage receipt replay accepted");
        Assert(next.TryReleaseEarnings(next.Revision, 43200, null, out contract) && contract.ReleasedGold == 100,
            "later observation paid the daily wage twice");
    }

    private static void StartedDayIsNotProrated()
    {
        var active = Started(120, 86399);
        Assert(active.EarnedGold == 120 && active.BlockEndAt == 172799,
            "accepted daily fee must be earned at service start, not prorated by seconds");
        NpcServiceContract afterMidnight;
        Assert(active.TryAccrue(active.Revision, 86401, out afterMidnight) &&
            afterMidnight.EarnedGold == 120 && afterMidnight.FundedGold == 120,
            "calendar midnight charged a second day");
        NpcServiceContract dismissed;
        Assert(afterMidnight.TrySettle(afterMidnight.Revision, 86401,
            NpcServiceEndReason.PlayerDismissal, "npc-1",
            Transfer("daily-dismiss-wage", NpcServiceTransferKind.Earnings,
                "contract:contract-1:escrow", "npc-1", 120, 86401), null, out dismissed),
            "early dismissal did not pay the complete started day");
        Assert(dismissed.ReleasedGold == 120 && dismissed.RefundedGold == 0 && dismissed.EscrowGold == 0,
            "started day was refunded or paid twice");
        Assert(!dismissed.TrySettle(dismissed.Revision, 86402,
            NpcServiceEndReason.PlayerDismissal, "npc-1", null, null, out _),
            "dismissal replay settled twice");
    }

    private static void SuspensionKeepsFixedExpiry()
    {
        var contract = Started(100, 0); NpcServiceContract next;
        Assert(contract.TrySuspend(contract.Revision, 43200, out next), "suspend rejected");
        Assert(next.TryResume(next.Revision, 57600, out contract), "resume rejected");
        Assert(contract.BlockEndAt == 86400, "suspension extended funded expiry");
        Assert(contract.TrySettle(contract.Revision, 86400, NpcServiceEndReason.NaturalExpiry, "npc-1",
            Transfer("wage-final", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "npc-1", 100, 86400),
            null, out next),
            "expiry settlement rejected");
        Assert(next.EligibleServiceSeconds == 72000 && next.EarnedGold == 100 && next.EscrowGold == 0,
            "suspension changed fixed day pay or escrow remained");
    }

    private static void RenewalBoundary()
    {
        var contract = Started(100, 0); NpcServiceContract next;
        Assert(!contract.TryRenew(contract.Revision, 86400 - 1, Transfer("early", NpcServiceTransferKind.Funding,
            "player", "contract:contract-1:escrow", 100, 86399), null, null, out next), "early renewal accepted");
        var noPermission = Reserved(100, false); Assert(noPermission.TryStart(noPermission.Revision, 0, out contract), "start rejected");
        Assert(!contract.TryRenew(contract.Revision, 86400, Transfer("no-permission", NpcServiceTransferKind.Funding,
            "player", "contract:contract-1:escrow", 100, 86400), null, null, out next), "renewal without explicit permission accepted");
        var wage = Transfer("old-wage", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "npc-1", 100, 86400);
        var fund = Transfer("fund-next", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", 100, 86400);
        var started = Started(100, 0);
        Assert(started.TryRenew(started.Revision, 86400, fund, wage, null, out next), "permitted exact-boundary renewal rejected");
        Assert(next.BlockStartAt == 86400 && next.BlockEndAt == 172800 && next.EscrowGold == 100 && next.ReleasedGold == 100,
            "renewal did not close old interval and fund next interval");
    }

    private static void RenewalPermissionRevocation()
    {
        var contract = Started(100, 0);
        var authorization = NpcServiceRenewalAuthorization.Create("revoke-1", "contract-1", "player", false, 1000);
        NpcServiceContract next;
        Assert(contract.TrySetRenewalPermission(contract.Revision, authorization, out next), "native revocation rejected");
        Assert(!next.StandingRenewalPermission, "renewal permission remained enabled");
        Assert(!next.TryRenew(next.Revision, 86400,
            Transfer("revoke-fund", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", 100, 86400),
            Transfer("revoke-wage", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "npc-1", 100, 86400), null, out contract),
            "revoked permission renewed contract");
    }

    private static void ExactExpiryBoundary()
    {
        var contract = Started(100, 0); NpcServiceContract next;
        Assert(!contract.TryAccrue(contract.Revision, 86401, out next), "late update crossed funded expiry");
        Assert(!contract.TrySuspend(contract.Revision, 86401, out next), "late suspension bypassed expiry");
        Assert(contract.TrySettle(contract.Revision, 86400, NpcServiceEndReason.NaturalExpiry, "npc-1",
            Transfer("boundary-wage", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "npc-1", 100, 86400),
            null, out next), "exact-boundary natural expiry rejected");
        Assert(!next.TryAccrue(next.Revision, 86400, out contract), "terminated contract accrued more service");
    }

    private static void PreServiceCancellation()
    {
        NpcServiceContractLedger ledger;
        Assert(NpcServiceContractLedger.TryReserve(NpcServiceContractLedger.Create(), 0, Terms(100, false),
            Transfer("cancel-fund", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", 100, 0), out ledger),
            "pre-service fixture reservation failed");
        var contract = ledger.Read("contract-1"); NpcServiceContract next;
        Assert(contract.TrySettle(contract.Revision, 10, NpcServiceEndReason.CancelledBeforeService, "npc-1", null,
            Transfer("cancel-refund", NpcServiceTransferKind.Refund, "contract:contract-1:escrow", "player", 100, 10), out next),
            "pre-service refund rejected");
        Assert(next.EarnedGold == 0 && next.RefundedGold == 100 && next.EscrowGold == 0,
            "pre-service refund did not conserve reservation");
        NpcServiceContractLedger updatedLedger;
        Assert(NpcServiceContractLedger.TryReplace(ledger, ledger.Revision, next, out updatedLedger),
            "pre-service cancellation could not commit to the ledger");
    }

    private static void DeathSettlement()
    {
        var contract = Started(100, 0); NpcServiceContract next;
        Assert(contract.TrySettle(contract.Revision, 43200, NpcServiceEndReason.Death, "estate:npc-1",
            Transfer("death-wage", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "estate:npc-1", 100, 43200),
            null, out next),
            "death settlement rejected");
        Assert(next.Status == NpcServiceContractStatus.Terminated && next.EscrowGold == 0 && next.ReleasedGold == 100 && next.RefundedGold == 0,
            "death settlement conservation failed");
        Assert(!next.TrySettle(next.Revision, 43200, NpcServiceEndReason.Death, "estate:npc-1", null, null, out contract),
            "terminal settlement replay accepted");
    }

    private static void SaveStateRoundTrip()
    {
        var ledger = StartedLedger(100, 0);
        var state = ledger.ToSaveState("save-branch-a");
        NpcServiceContractLedger restored;
        Assert(NpcServiceContractLedger.TryRestore(state, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "valid save state did not restore");
        Assert(restored.Revision == ledger.Revision && restored.Read("contract-1").BlockEndAt == 86400 &&
            restored.Read("contract-1").EscrowGold == 0 && restored.Read("contract-1").EarnedGold == 100 &&
            restored.Read("contract-1").ReleasedGold == 100, "round-trip changed ledger accounting");
        Assert(restored.Read("contract-1").ReservedObligations.Count == 1 &&
            restored.Read("contract-1").ReservedObligations[0].Treatment == NpcServiceObligationTreatment.MayInterruptService,
            "typed obligation reference was not restored");
        Assert(!ReferenceEquals(ledger, restored) && !ReferenceEquals(ledger.Read("contract-1"), restored.Read("contract-1")),
            "restore reused mutable contract objects");
        state.Contracts[0].RatePer24GameHours = 500;
        Assert(ledger.Read("contract-1").RatePer24GameHours == 100 && restored.Read("contract-1").RatePer24GameHours == 100,
            "DTO mutation changed source or restored ledger");
        var branchB = ledger.ToSaveState("save-branch-b");
        Assert(!NpcServiceContractLedger.TryRestore(branchB, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "independent save branch was restored as branch A");
    }

    private static void SaveStateRejectsCorruption()
    {
        var source = StartedLedger(100, 0);
        var wrongVersion = source.ToSaveState("save-branch-a"); wrongVersion.Version++;
        NpcServiceContractLedger restored;
        Assert(!NpcServiceContractLedger.TryRestore(wrongVersion, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "unknown save DTO version accepted");
        var wrongAccounting = source.ToSaveState("save-branch-a"); wrongAccounting.Contracts[0].EscrowGold++;
        Assert(!NpcServiceContractLedger.TryRestore(wrongAccounting, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "corrupted escrow accounting accepted");
        var hiddenEndReason = source.ToSaveState("save-branch-a");
        hiddenEndReason.Contracts[0].EndReason = NpcServiceEndReason.Death;
        Assert(!NpcServiceContractLedger.TryRestore(hiddenEndReason, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "unflagged terminal reason accepted");
        var duplicate = source.ToSaveState("save-branch-a");
        duplicate.Contracts[0].TransferReceipts.Add(duplicate.Contracts[0].TransferReceipts[0].Copy());
        Assert(!NpcServiceContractLedger.TryRestore(duplicate, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "duplicate native transfer receipt accepted");
        var duplicateContract = source.ToSaveState("save-branch-a");
        duplicateContract.Contracts.Add(duplicateContract.Contracts[0].Copy());
        Assert(!NpcServiceContractLedger.TryRestore(duplicateContract, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "duplicate contract ID accepted");
        var duplicateGlobalReceipt = source.ToSaveState("save-branch-a");
        var secondContract = duplicateGlobalReceipt.Contracts[0].Copy();
        secondContract.ContractId = "contract-2"; secondContract.NpcId = "npc-2"; secondContract.HomeReference = "home-2";
        secondContract.Status = NpcServiceContractStatus.Reserved; secondContract.Revision = 1;
        secondContract.BlockStartAt = -1; secondContract.BlockEndAt = -1; secondContract.LastServiceUpdateAt = -1;
        secondContract.EligibleServiceSeconds = 0; secondContract.CurrentBlockEligibleSeconds = 0;
        secondContract.CurrentBlockEarnedGold = 0; secondContract.CurrentBlockReleasedGold = 0;
        secondContract.FundedGold = 100; secondContract.EscrowGold = 100; secondContract.EarnedGold = 0;
        secondContract.ReleasedGold = 0; secondContract.RefundedGold = 0; secondContract.HasEndReason = false;
        secondContract.AuthorizationReceiptIds.Clear(); secondContract.ProtectedTargets.Clear(); secondContract.ReservedObligations.Clear();
        var duplicateFundingReceipt = duplicateGlobalReceipt.Contracts[0].TransferReceipts[0].Copy();
        duplicateFundingReceipt.ContractId = "contract-2";
        duplicateFundingReceipt.ToOwnerId = "contract:contract-2:escrow";
        secondContract.TransferReceipts = new List<NpcServiceTransferReceiptState> { duplicateFundingReceipt };
        duplicateGlobalReceipt.Contracts.Add(secondContract);
        Assert(!NpcServiceContractLedger.TryRestore(duplicateGlobalReceipt, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, receipt => true, RenewalAuthorizationExists, out restored),
            "duplicate receipt ID across restored contracts accepted");
        var unknownActor = source.ToSaveState("save-branch-a"); unknownActor.Contracts[0].NpcId = "unknown-npc";
        Assert(!NpcServiceContractLedger.TryRestore(unknownActor, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "unknown actor reference accepted during restore");
        var unknownHome = source.ToSaveState("save-branch-a"); unknownHome.Contracts[0].HomeReference = "unknown-home";
        Assert(!NpcServiceContractLedger.TryRestore(unknownHome, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "unknown home reference accepted during restore");
        var unknownProtectedTarget = source.ToSaveState("save-branch-a");
        unknownProtectedTarget.Contracts[0].ProtectedTargets[0].Category = NpcServiceProtectedTargetCategory.QuestProtected;
        Assert(!NpcServiceContractLedger.TryRestore(unknownProtectedTarget, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "unverified protected-target category accepted during restore");
        var unknownObligation = source.ToSaveState("save-branch-a");
        unknownObligation.Contracts[0].ReservedObligations[0].EndsAt++;
        Assert(!NpcServiceContractLedger.TryRestore(unknownObligation, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "tampered obligation interval accepted during restore");
        var fakeAuthorization = source.ToSaveState("save-branch-a");
        fakeAuthorization.Contracts[0].AuthorizationReceiptIds.Add("fabricated-auth");
        Assert(!NpcServiceContractLedger.TryRestore(fakeAuthorization, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, NativeReceiptExists, RenewalAuthorizationExists, out restored),
            "unverified native authorization accepted during restore");
        var fakeTransfer = source.ToSaveState("save-branch-a");
        Assert(!NpcServiceContractLedger.TryRestore(fakeTransfer, "save-branch-a", KnownActor, KnownHome,
            KnownProtectedTarget, KnownObligation, receipt => false, RenewalAuthorizationExists, out restored),
            "unverified native transfer receipt accepted");
    }

    private static void HistoricalReferencesRestore()
    {
        var ledger = StartedLedger(100, 0);
        var state = ledger.ToSaveState("save-branch-a");
        state.Contracts[0].HomeReference = "retired-home-7";
        state.Contracts[0].ReservedObligations[0].SourceRevision = 9;
        state.Contracts[0].ReservedObligations[0].EndsAt = 26000;
        var evidence = EvidenceFor(state);
        NpcServiceContractLedger restored;
        Assert(NpcServiceContractLedger.TryRestoreHistorical(state, "save-branch-a", KnownActor,
            evidence, out restored), "valid closed history required current home/obligation authority");
        Assert(restored.Read("contract-1").HomeReference == "retired-home-7" &&
            restored.Read("contract-1").ReservedObligations[0].SourceRevision == 9 &&
            restored.Read("contract-1").ReservedObligations[0].EndsAt == 26000,
            "historical references were not retained verbatim");
    }

    private static void HistoricalEvidenceCrosslinks()
    {
        var ledger = StartedLedger(100, 0);
        var state = ledger.ToSaveState("save-branch-a");
        var evidence = EvidenceFor(state);
        NpcServiceContractLedger restored;
        Assert(NpcServiceContractLedger.TryRestoreHistorical(state, "save-branch-a", KnownActor,
            evidence, out restored), "complete native evidence registry rejected");
        var tampered = state.Copy(); tampered.Contracts[0].TransferReceipts[0].Amount++;
        Assert(!NpcServiceContractLedger.TryRestoreHistorical(tampered, "save-branch-a", KnownActor,
            evidence, out restored), "transfer record differing from saved native evidence was accepted");
        var unknownActor = state.Copy(); unknownActor.Contracts[0].NpcId = "unknown-npc";
        Assert(!NpcServiceContractLedger.TryRestoreHistorical(unknownActor, "save-branch-a", KnownActor,
            evidence, out restored), "unknown historical actor was accepted");
    }

    private static void StoreCommitAndRestore()
    {
        var store = StoreWithServiceWorker(out string workerId, out string homeId);
        var ticket = store.CaptureNpcServiceContractWriteTicket();
        var receipt = StoreFunding("store-fund-1", "store-contract-1", 100, 0);
        var terms = StoreTerms(workerId, homeId, "store-contract-1");
        NpcServiceContractLedger candidate;
        Assert(NpcServiceContractLedger.TryReserve(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
            terms, receipt, out candidate), "valid service reservation candidate rejected");
        string error;
        Assert(store.TryCommitNpcServiceContracts(ticket, candidate, new[] { receipt },
            new NpcServiceRenewalAuthorization[0], out error), "valid native-backed commit rejected: " + error);

        var saved = store.Snapshot();
        Assert(saved.NpcServiceContracts.Revision == 1 && saved.NpcServiceEvidence.Revision == 1 &&
            saved.NpcServiceEvidence.CompletedTransfers.Count == 1, "ledger and evidence did not publish together");
        var restored = new WorldStateStore(2);
        restored.Restore(saved);
        var ledger = restored.ReadNpcServiceContracts();
        Assert(ledger.Revision == 1 && ledger.Read("store-contract-1").Status == NpcServiceContractStatus.Reserved,
            "saved native-backed contract did not restore");
        Assert(restored.Snapshot().NpcServiceEvidence.CompletedTransfers[0].ReceiptId == receipt.ReceiptId,
            "exact native receipt not retained after restore");
    }

    private static void StoreRejectsUnprovenWrites()
    {
        var store = StoreWithServiceWorker(out string workerId, out string homeId);
        var ticket = store.CaptureNpcServiceContractWriteTicket();
        var receipt = StoreFunding("store-fund-2", "store-contract-2", 100, 0);
        NpcServiceContractLedger candidate;
        Assert(NpcServiceContractLedger.TryReserve(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
            StoreTerms(workerId, homeId, "store-contract-2"), receipt, out candidate), "fixture candidate failed");
        string error;
        Assert(!store.TryCommitNpcServiceContracts(ticket, candidate, new NpcServiceTransferReceipt[0],
            new NpcServiceRenewalAuthorization[0], out error), "unproven candidate committed");
        Assert(store.ReadNpcServiceContracts().Revision == 0 && store.Snapshot().NpcServiceEvidence.Revision == 0,
            "failed candidate partly mutated saved state");

        Assert(store.TryCommitNpcServiceContracts(ticket, candidate, new[] { receipt },
            new NpcServiceRenewalAuthorization[0], out error), "valid retry after rejected candidate failed: " + error);
        Assert(!store.TryCommitNpcServiceContracts(ticket, candidate, new[] { receipt },
            new NpcServiceRenewalAuthorization[0], out error), "stale ticket/replayed native evidence committed twice");
        Assert(store.ReadNpcServiceContracts().Revision == 1 && store.Snapshot().NpcServiceEvidence.Revision == 1,
            "stale retry changed ledger or evidence");

        var corrupted = store.Snapshot();
        corrupted.NpcServiceEvidence.CompletedTransfers[0].Amount++;
        bool rejected = false;
        try { new WorldStateStore(3).Restore(corrupted); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "restore accepted receipt/ledger crosslink corruption");
    }

    private static void StoreRejectsSiblingReplay()
    {
        var original = StoreWithServiceWorker(out string workerId, out string homeId);
        var initialTicket = original.CaptureNpcServiceContractWriteTicket();
        var initialFunding = StoreFunding("sibling-initial-fund", "sibling-contract-1", 100, 0);
        NpcServiceContractLedger initialCandidate;
        Assert(NpcServiceContractLedger.TryReserve(initialTicket.SourceLedger, initialTicket.ExpectedLedgerRevision,
            StoreTerms(workerId, homeId, "sibling-contract-1", true), initialFunding, out initialCandidate),
            "sibling initial reservation candidate failed");
        string initialError;
        Assert(original.TryCommitNpcServiceContracts(initialTicket, initialCandidate, new[] { initialFunding },
            new NpcServiceRenewalAuthorization[0], out initialError), "sibling initial reservation failed: " + initialError);
        var baseline = original.Snapshot();
        var firstBranch = new WorldStateStore(1);
        var siblingBranch = new WorldStateStore(2);
        firstBranch.Restore(baseline);
        siblingBranch.Restore(baseline);
        var firstTicket = firstBranch.CaptureNpcServiceContractWriteTicket();
        var siblingTicket = siblingBranch.CaptureNpcServiceContractWriteTicket();
        Assert(firstBranch.NpcFrameworkScopeId == siblingBranch.NpcFrameworkScopeId,
            "fixture must reproduce sibling copies with equal saved scope");

        var authorization = NpcServiceRenewalAuthorization.Create("sibling-auth-1", "sibling-contract-1",
            NpcDirectedRelationship.PlayerTargetId, false, 1);
        NpcServiceContract changedContract;
        Assert(firstTicket.SourceLedger.Read("sibling-contract-1").TrySetRenewalPermission(
            firstTicket.SourceLedger.Read("sibling-contract-1").Revision, authorization, out changedContract),
            "sibling contract transition fixture failed");
        NpcServiceContractLedger candidate;
        Assert(NpcServiceContractLedger.TryReplace(firstTicket.SourceLedger, firstTicket.ExpectedLedgerRevision,
            changedContract, out candidate), "sibling candidate fixture failed");
        string error;
        Assert(firstBranch.TryCommitNpcServiceContracts(firstTicket, candidate, new NpcServiceTransferReceipt[0],
            new[] { authorization }, out error), "first branch fixture commit failed: " + error);
        Assert(!NpcServiceContractLedger.TryReplace(siblingTicket.SourceLedger, siblingTicket.ExpectedLedgerRevision,
            candidate.Read("sibling-contract-1"), out _),
            "foreign contract transition was rebased into the sibling ledger");
        Assert(!siblingBranch.TryCommitNpcServiceContracts(siblingTicket, candidate, new NpcServiceTransferReceipt[0],
            new[] { authorization }, out error), "candidate/proof from first sibling was accepted by second sibling");
        Assert(siblingBranch.ReadNpcServiceContracts().Revision == 1 &&
            siblingBranch.Snapshot().NpcServiceEvidence.Revision == 1, "rejected sibling replay mutated its branch");
    }

    private static void ServiceTicketPreflight()
    {
        var store = StoreWithServiceWorker(out string workerId, out string homeId);
        var ticket = store.CaptureNpcServiceContractWriteTicket();
        Assert(store.IsCurrentNpcServiceContractWriteTicket(ticket), "fresh ticket failed nonmutating preflight");
        var sibling = new WorldStateStore(store.Snapshot().WorldSeed);
        sibling.Restore(store.Snapshot());
        Assert(!store.IsCurrentNpcServiceContractWriteTicket(sibling.CaptureNpcServiceContractWriteTicket()),
            "same-scope sibling ticket passed owner preflight");
        Assert(!sibling.IsCurrentNpcServiceContractWriteTicket(ticket), "foreign ticket passed sibling owner preflight");

        var receipt = StoreFunding("ticket-preflight-fund", "ticket-preflight-contract", 100, 0);
        NpcServiceContractLedger candidate;
        Assert(NpcServiceContractLedger.TryReserve(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
            StoreTerms(workerId, homeId, "ticket-preflight-contract"), receipt, out candidate),
            "ticket preflight candidate failed");
        string error;
        Assert(store.TryCommitNpcServiceContracts(ticket, candidate, new[] { receipt },
            new NpcServiceRenewalAuthorization[0], out error), "ticket preflight fixture commit failed: " + error);
        Assert(!store.IsCurrentNpcServiceContractWriteTicket(ticket), "committed ticket remained current");

        var beforeRestore = store.CaptureNpcServiceContractWriteTicket();
        store.Restore(store.Snapshot());
        Assert(!store.IsCurrentNpcServiceContractWriteTicket(beforeRestore),
            "ticket from previous save-binding epoch remained current after restore");
        Assert(store.IsCurrentNpcServiceContractWriteTicket(store.CaptureNpcServiceContractWriteTicket()),
            "fresh ticket after restore failed preflight");
    }

    private static WorldStateStore StoreWithServiceWorker(out string workerId, out string homeId)
    {
        var store = new WorldStateStore(654);
        var home = new HomeProfile(17, 1, 60);
        var town = store.GetOrCreateFamilies(3, new[] { home });
        var resident = town.Households[0].Residents[0];
        workerId = resident.Id;
        homeId = "home:3:17";
        return store;
    }

    private static NpcServiceContractTerms StoreTerms(string workerId, string homeId, string contractId, bool renewal = false)
    {
        NpcServiceContractTerms terms;
        Assert(NpcServiceContractTerms.TryCreate(contractId, NpcDirectedRelationship.PlayerTargetId,
            workerId, homeId, NpcServiceRole.Fighter, NpcServiceCoverage.Follow, 100, renewal,
            NpcServiceRiskScope.None, new NpcServiceProtectedTarget[0], NpcServiceRestTreatment.RestNotIncluded,
            NpcServiceResupplyResponsibility.SeparatelyNegotiated, new NpcServiceReservedObligation[0],
            id => id == NpcDirectedRelationship.PlayerTargetId || id == workerId,
            id => id == homeId, (id, category, revision) => false,
            (id, revision, kind, starts, ends) => false, out terms), "service terms fixture rejected");
        return terms;
    }

    private static NpcServiceTransferReceipt StoreFunding(string receiptId, string contractId, int amount, double at)
    { return NpcServiceTransferReceipt.Create(receiptId, contractId, NpcServiceTransferKind.Funding,
        NpcDirectedRelationship.PlayerTargetId, "contract:" + contractId + ":escrow", amount, at); }

    private static NpcServiceNativeEvidenceState EvidenceFor(NpcServiceContractLedgerState state)
    {
        var evidence = new NpcServiceNativeEvidenceState();
        foreach (var contract in state.Contracts)
        {
            foreach (var receipt in contract.TransferReceipts)
                evidence.CompletedTransfers.Add(receipt.Copy());
            foreach (var authorizationId in contract.AuthorizationReceiptIds)
                evidence.Authorizations.Add(new NpcServiceAuthorizationReceiptState
                { ContractId = contract.ContractId, ReceiptId = authorizationId, PayerId = contract.PayerId,
                    Permission = contract.StandingRenewalPermission, GameTime = contract.AcceptedAt });
        }
        evidence.Revision = evidence.CompletedTransfers.Count + evidence.Authorizations.Count;
        return evidence;
    }

    private static NpcServiceContractLedger StartedLedger(int rate, double at)
    {
        NpcServiceContractLedger ledger;
        var obligation = Obligation("appointment-1", NpcServiceReservedObligationKind.Appointment,
            NpcServiceObligationTreatment.MayInterruptService, 20000, 25000);
        Assert(NpcServiceContractLedger.TryReserve(NpcServiceContractLedger.Create(), 0, Terms(rate, true, new[] { obligation }),
            Transfer("fund-initial", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", rate, 0), out ledger),
            "save fixture reservation failed");
        var reserved = ledger.Read("contract-1"); NpcServiceContract started;
        Assert(reserved.TryStart(reserved.Revision, at, out started), "save fixture start failed");
        NpcServiceContractLedger updated;
        Assert(NpcServiceContractLedger.TryReplace(ledger, ledger.Revision, started, out updated), "save fixture ledger update failed");
        var active = updated.Read("contract-1");
        var wage = Transfer("save-wage", NpcServiceTransferKind.Earnings, "contract:contract-1:escrow", "npc-1", rate, at + 43200);
        NpcServiceContract paid;
        Assert(active.TryReleaseEarnings(active.Revision, at + 43200, wage, out paid), "save fixture wage release failed");
        NpcServiceContractLedger settled;
        Assert(NpcServiceContractLedger.TryReplace(updated, updated.Revision, paid, out settled),
            "save fixture wage ledger update failed");
        return settled;
    }

    private static bool KnownActor(string id) { return id == "player" || id == "npc-1" || id == "npc-2" || id == "family-1"; }
    private static bool KnownHome(string id) { return id == "home-1" || id == "home-2"; }
    private static bool KnownProtectedTarget(string id, NpcServiceProtectedTargetCategory category, long revision)
    { return id == "family-1" && category == NpcServiceProtectedTargetCategory.Family && revision == 1; }
    private static bool KnownObligation(string id, long revision, NpcServiceReservedObligationKind kind, double starts, double ends)
    { return id == "appointment-1" && revision == 1 && kind == NpcServiceReservedObligationKind.Appointment && starts == 20000 && ends == 25000; }
    private static bool NativeReceiptExists(NpcServiceTransferReceiptState receipt)
    { return receipt != null && (receipt.ReceiptId == "fund-initial" && receipt.Amount == 100 ||
        receipt.ReceiptId == "save-wage" && receipt.Amount == 100); }
    private static bool RenewalAuthorizationExists(string contractId, string receiptId)
    { return contractId == "contract-1" && receiptId == "revoke-1"; }

    private static NpcServiceContractTerms Terms(int rate, bool renewal)
    { return Terms(rate, renewal, new NpcServiceReservedObligation[0]); }

    private static NpcServiceContractTerms Terms(int rate, bool renewal, IEnumerable<NpcServiceReservedObligation> obligations)
    {
        NpcServiceContractTerms terms;
        Assert(NpcServiceContractTerms.TryCreate("contract-1", "player", "npc-1", "home-1",
            NpcServiceRole.Fighter, NpcServiceCoverage.Follow | NpcServiceCoverage.Combat | NpcServiceCoverage.Expedition |
                NpcServiceCoverage.Rest | NpcServiceCoverage.Resupply,
            rate, renewal, NpcServiceRiskScope.OrdinaryCombat | NpcServiceRiskScope.BoundedExpedition,
            new[] { ProtectedTarget("family-1", NpcServiceProtectedTargetCategory.Family) }, NpcServiceRestTreatment.CountsAsPaidAvailability,
            NpcServiceResupplyResponsibility.PlayerProvides, obligations,
            id => true, id => true, (id, category, revision) => true, (id, revision, kind, starts, ends) => true, out terms), "valid terms rejected");
        return terms;
    }

    private static NpcServiceReservedObligation Obligation(string id, NpcServiceReservedObligationKind kind,
        NpcServiceObligationTreatment treatment, double startsAt, double endsAt)
    {
        NpcServiceReservedObligation obligation;
        Assert(NpcServiceReservedObligation.TryCreate(id, 1, kind, treatment, startsAt, endsAt,
            (knownId, revision, knownKind, starts, ends) => true, out obligation), "valid obligation reference rejected");
        return obligation;
    }
    private static NpcServiceProtectedTarget ProtectedTarget(string id, NpcServiceProtectedTargetCategory category)
    {
        NpcServiceProtectedTarget target;
        Assert(NpcServiceProtectedTarget.TryCreate(id, category, 1,
            (actorId, knownCategory, revision) => true, out target), "valid protected target rejected");
        return target;
    }
    private static NpcServiceContract Reserved(int rate, bool renewal = true)
    {
        NpcServiceContractLedger ledger;
        Assert(NpcServiceContractLedger.TryReserve(NpcServiceContractLedger.Create(), 0, Terms(rate, renewal),
            Transfer("fund-initial", NpcServiceTransferKind.Funding, "player", "contract:contract-1:escrow", rate, 0), out ledger),
            "initial reserve rejected");
        return ledger.Read("contract-1");
    }
    private static NpcServiceContract Started(int rate, double at)
    { NpcServiceContract result; var reserved = Reserved(rate); Assert(reserved.TryStart(reserved.Revision, at, out result), "start failed"); return result; }
    private static NpcServiceTransferReceipt Transfer(string id, NpcServiceTransferKind kind, string from, string to, int amount, double at)
    { return NpcServiceTransferReceipt.Create(id, "contract-1", kind, from, to, amount, at); }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
