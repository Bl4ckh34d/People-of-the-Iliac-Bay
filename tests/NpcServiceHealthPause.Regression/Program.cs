using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const string CareA = "care:v1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string CareB = "care:v1:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Contract = "service-health-pause";
    private const string Escrow = "contract:service-health-pause:escrow";
    private static int failures;

    private static int Main()
    {
        Run("health owner pauses service without prorating day pay and only exact episode resumes", OwnerBoundPauseAndRounding);
        Run("health pause owner survives detached save and renewal permission", OwnerSurvivesCopyAndRestore);
        Run("new care episode transfers an unpaid pause without billing", NewCareTransfersPauseOwner);
        Run("legacy suspended contract cannot be resumed by health care", LegacySuspensionMigration);
        Run("legacy terminal time migrates and malformed future owner fails closed", LegacyTerminalAndOwnerValidation);
        Run("late paused expiry uses actual receipt time and conserved escrow", LatePausedExpiry);
        Run("late active expiry caps earned time at fixed boundary", LateActiveExpiry);
        Run("restore rejects end reasons incompatible with settlement time", EndReasonAndSettlementTimeValidation);
        Run("late non-expiry and backdated receipts fail atomically", LateSettlementRejectsOtherReasonsAndWrongEvidence);
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    private static void OwnerBoundPauseAndRounding()
    {
        var active = Started();
        Check(active.TrySuspendForHealthCare(active.Revision, 43200, CareA, out var paused), "health pause failed");
        Check(paused.Status == NpcServiceContractStatus.Suspended &&
            paused.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare &&
            paused.SuspensionOwnerId == CareA && paused.EligibleServiceSeconds == 43200, "pause ownership or accrual wrong");
        Check(!paused.TryResume(paused.Revision, 57600, out _), "ordinary resume stole health pause");
        Check(!paused.TryResumeFromHealthCare(paused.Revision, 57600, CareB, out _), "foreign episode resumed pause");
        Check(!paused.TryResumeFromHealthCare(paused.Revision - 1, 57600, CareA, out _), "stale revision resumed pause");
        Check(paused.TryResumeFromHealthCare(paused.Revision, 57600, CareA, out var resumed), "owned resume failed");
        Check(resumed.Status == NpcServiceContractStatus.Active &&
            resumed.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.None && resumed.SuspensionOwnerId == null &&
            resumed.BlockEndAt == 86400, "resume changed owner or fixed expiry");
        Check(resumed.TrySettle(resumed.Revision, 86400, NpcServiceEndReason.NaturalExpiry, "npc-1",
            Transfer("earned-round", NpcServiceTransferKind.Earnings, Escrow, "npc-1", 100, 86400),
            null, out var settled),
            "daily settlement failed");
        Check(settled.EligibleServiceSeconds == 72000 && settled.EarnedGold == 100 &&
            settled.ReleasedGold == 100 && settled.RefundedGold == 0 && settled.EscrowGold == 0,
            "paused interval accrued or escrow not conserved");
    }

    private static void OwnerSurvivesCopyAndRestore()
    {
        var ledger = StartedLedger();
        var active = ledger.Read(Contract);
        Check(active.TrySuspendForHealthCare(active.Revision, 43200, CareA, out var pause), "health pause failed");
        ledger = Replace(ledger, pause);
        pause = ledger.Read(Contract);
        var authorization = NpcServiceRenewalAuthorization.Create("revoke-health", Contract, "player", false, 45000);
        Check(pause.TrySetRenewalPermission(pause.Revision, authorization, out var revoked), "permission change failed");
        ledger = Replace(ledger, revoked);
        var saved = ledger.ToSaveState("branch-health");
        Check(saved.Contracts[0].Version == NpcServiceContractState.VersionCurrent &&
            saved.Contracts[0].SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare &&
            saved.Contracts[0].SuspensionOwnerId == CareA, "owner absent from save DTO");
        var detached = saved.Copy();
        Check(Restore(detached, out var restored), "owned contract restore failed");
        var restoredPause = restored.Read(Contract);
        Check(restoredPause.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare &&
            restoredPause.SuspensionOwnerId == CareA && !restoredPause.StandingRenewalPermission,
            "owner or renewal permission lost on restore");
        Check(restoredPause.TryResumeFromHealthCare(restoredPause.Revision, 57600, CareA, out _),
            "same owner could not resume after restore");
    }

    private static void NewCareTransfersPauseOwner()
    {
        var active = Started();
        Check(active.TrySuspendForHealthCare(active.Revision, 43200, CareA, out var paused), "health pause failed");
        Check(!paused.TryTransferHealthCareSuspensionOwner(paused.Revision, CareB, CareA, out _),
            "wrong old owner transferred pause");
        Check(!paused.TryTransferHealthCareSuspensionOwner(paused.Revision, CareA, CareA, out _),
            "same episode replay advanced pause");
        Check(paused.TryTransferHealthCareSuspensionOwner(paused.Revision, CareA, CareB, out var transferred),
            "new episode did not inherit unpaid pause");
        Check(transferred.Status == NpcServiceContractStatus.Suspended &&
            transferred.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare &&
            transferred.SuspensionOwnerId == CareB && transferred.Revision == paused.Revision + 1 &&
            transferred.LastServiceUpdateAt == paused.LastServiceUpdateAt &&
            transferred.EligibleServiceSeconds == paused.EligibleServiceSeconds &&
            transferred.FundedGold == paused.FundedGold && transferred.EscrowGold == paused.EscrowGold &&
            transferred.TransferReceipts.Count == paused.TransferReceipts.Count,
            "owner transfer billed time or changed escrow");
        Check(!transferred.TryTransferHealthCareSuspensionOwner(paused.Revision, CareB, CareA, out _),
            "stale owner transfer replay accepted");
    }

    private static void LegacySuspensionMigration()
    {
        var ledger = StartedLedger();
        var active = ledger.Read(Contract);
        Check(active.TrySuspend(active.Revision, 43200, out var oldPause), "legacy pause fixture failed");
        var oldState = Replace(ledger, oldPause).ToSaveState("branch-health");
        oldState.Contracts[0].Version = 1;
        Check(Restore(oldState, out var restored), "legacy v1 suspended contract failed restore");
        var migrated = restored.Read(Contract);
        Check(migrated.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.LegacyUnknown &&
            migrated.SuspensionOwnerId == null, "legacy pause acquired fabricated health owner");
        Check(!migrated.TryResumeFromHealthCare(migrated.Revision, 50000, CareA, out _),
            "health care resumed legacy unknown pause");
        var forgedOld = oldState.Copy();
        forgedOld.Contracts[0].SuspensionOwnerKind = NpcServiceSuspensionOwnerKind.HealthCare;
        forgedOld.Contracts[0].SuspensionOwnerId = CareA;
        Check(!Restore(forgedOld, out _), "v1 future owner fields accepted");

        var oldActive = ledger.ToSaveState("branch-health");
        oldActive.Contracts[0].Version = 1;
        Check(Restore(oldActive, out var restoredActive) &&
            restoredActive.Read(Contract).SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.None,
            "legacy active contract migration wrong");
    }

    private static void LegacyTerminalAndOwnerValidation()
    {
        var ledger = StartedLedger();
        var active = ledger.Read(Contract);
        Check(active.TrySettle(active.Revision, 86400, NpcServiceEndReason.NaturalExpiry, "npc-1",
            Transfer("legacy-wage", NpcServiceTransferKind.Earnings, Escrow, "npc-1", 100, 86400),
            null, out var ended), "terminal fixture failed");
        var oldState = Replace(ledger, ended).ToSaveState("branch-health");
        oldState.Contracts[0].Version = 1;
        oldState.Contracts[0].SettlementRecordedAt = -1;
        Check(Restore(oldState, out var restored) &&
            restored.Read(Contract).SettlementRecordedAt == 86400,
            "legacy terminal settlement time not migrated");
        var forgedOld = oldState.Copy();
        forgedOld.Contracts[0].SettlementRecordedAt = 90000;
        Check(!Restore(forgedOld, out _), "v1 future settlement field accepted");

        var badOwner = StartedLedger().ToSaveState("branch-health");
        badOwner.Contracts[0].SuspensionOwnerKind = NpcServiceSuspensionOwnerKind.HealthCare;
        badOwner.Contracts[0].SuspensionOwnerId = CareA;
        Check(!Restore(badOwner, out _), "active contract acquired suspended health owner");
    }

    private static void LatePausedExpiry()
    {
        var ledger = StartedLedger();
        var active = ledger.Read(Contract);
        Check(active.TrySuspendForHealthCare(active.Revision, 43200, CareA, out var paused), "pause failed");
        ledger = Replace(ledger, paused);
        paused = ledger.Read(Contract);
        Check(paused.TrySettle(paused.Revision, 90000, NpcServiceEndReason.NaturalExpiry, "npc-1",
            Transfer("late-wage", NpcServiceTransferKind.Earnings, Escrow, "npc-1", 100, 90000),
            null, out var ended),
            "late paused expiry failed");
        Check(ended.LastServiceUpdateAt == 43200 && ended.SettlementRecordedAt == 90000 &&
            ended.BlockEndAt == 86400 && ended.EligibleServiceSeconds == 43200 &&
            ended.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.None && ended.SuspensionOwnerId == null,
            "effective end or actual settlement time wrong");
        Check(ended.FundedGold == ended.ReleasedGold + ended.RefundedGold + ended.EscrowGold &&
            ended.ReleasedGold == 100 && ended.RefundedGold == 0 && ended.EscrowGold == 0,
            "late settlement lost escrow conservation");
        ledger = Replace(ledger, ended);
        Check(Restore(ledger.ToSaveState("branch-health").Copy(), out var restored) &&
            restored.Read(Contract).SettlementRecordedAt == 90000 &&
            restored.Read(Contract).LastServiceUpdateAt == 43200 &&
            restored.Read(Contract).SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.None,
            "late settlement failed detached restore");
        Check(!ended.TrySettle(ended.Revision, 90001, NpcServiceEndReason.NaturalExpiry, "npc-1", null, null, out _),
            "terminal receipt replay accepted");
    }

    private static void LateActiveExpiry()
    {
        var active = Started();
        Check(active.TrySettle(active.Revision, 90000, NpcServiceEndReason.NaturalExpiry, "npc-1",
            Transfer("active-late-wage", NpcServiceTransferKind.Earnings, Escrow, "npc-1", 100, 90000), null,
            out var ended), "late active expiry failed");
        Check(ended.EligibleServiceSeconds == 86400 && ended.LastServiceUpdateAt == 86400 &&
            ended.SettlementRecordedAt == 90000 && ended.ReleasedGold == 100 && ended.EscrowGold == 0,
            "late observation accrued beyond boundary or backdated payment");
    }

    private static void EndReasonAndSettlementTimeValidation()
    {
        var ledger = StartedLedger();
        var active = ledger.Read(Contract);
        Check(active.TrySettle(active.Revision, 90000, NpcServiceEndReason.NaturalExpiry, "npc-1",
            Transfer("late-reason-wage", NpcServiceTransferKind.Earnings, Escrow, "npc-1", 100, 90000),
            null, out var late), "late fixture failed");
        var lateState = Replace(ledger, late).ToSaveState("branch-health");
        lateState.Contracts[0].EndReason = NpcServiceEndReason.PlayerDismissal;
        Check(!Restore(lateState, out _), "late non-expiry forged through save restore");

        Check(active.TrySettle(active.Revision, 43200, NpcServiceEndReason.PlayerDismissal, "npc-1",
            Transfer("early-reason-wage", NpcServiceTransferKind.Earnings, Escrow, "npc-1", 100, 43200),
            null,
            out var early), "early fixture failed");
        var earlyState = Replace(ledger, early).ToSaveState("branch-health");
        earlyState.Contracts[0].EndReason = NpcServiceEndReason.NaturalExpiry;
        Check(!Restore(earlyState, out _), "early NaturalExpiry forged through save restore");
        earlyState.Contracts[0].EndReason = NpcServiceEndReason.RenewalDeclined;
        Check(!Restore(earlyState, out _), "early RenewalDeclined forged through save restore");
    }

    private static void LateSettlementRejectsOtherReasonsAndWrongEvidence()
    {
        var active = Started();
        Check(active.TrySuspendForHealthCare(active.Revision, 43200, CareA, out var paused), "pause failed");
        var wage = Transfer("reject-wage", NpcServiceTransferKind.Earnings, Escrow, "npc-1", 100, 90000);
        Check(!paused.TrySettle(paused.Revision, 90000, NpcServiceEndReason.PlayerDismissal, "npc-1",
            wage, null, out _), "late dismissal accepted");
        Check(!paused.TrySettle(paused.Revision, 90000, NpcServiceEndReason.NaturalExpiry, "npc-1",
            Transfer("backdated-wage", NpcServiceTransferKind.Earnings, Escrow, "npc-1", 100, 86400), null, out _),
            "backdated native receipt accepted");
        Check(paused.Status == NpcServiceContractStatus.Suspended && paused.EscrowGold == 100 &&
            paused.SuspensionOwnerId == CareA, "failed settlement mutated source");
    }

    private static NpcServiceContract Started() => StartedLedger().Read(Contract);

    private static NpcServiceContractLedger StartedLedger()
    {
        Check(NpcServiceContractTerms.TryCreate(Contract, "player", "npc-1", "home-1",
            NpcServiceRole.Fighter, NpcServiceCoverage.Follow, 100, true, NpcServiceRiskScope.None,
            Array.Empty<NpcServiceProtectedTarget>(), NpcServiceRestTreatment.RestNotIncluded,
            NpcServiceResupplyResponsibility.SeparatelyNegotiated, Array.Empty<NpcServiceReservedObligation>(),
            _ => true, _ => true, (_, _, _) => true, (_, _, _, _, _) => true, out var terms), "terms failed");
        Check(NpcServiceContractLedger.TryReserve(NpcServiceContractLedger.Create(), 0, terms,
            Transfer("initial-funding", NpcServiceTransferKind.Funding, "player", Escrow, 100, 0),
            out var reservedLedger), "reserve failed");
        var reserved = reservedLedger.Read(Contract);
        Check(reserved.TryStart(reserved.Revision, 0, out var active), "start failed");
        return Replace(reservedLedger, active);
    }

    private static NpcServiceContractLedger Replace(NpcServiceContractLedger ledger, NpcServiceContract next)
    {
        Check(NpcServiceContractLedger.TryReplace(ledger, ledger.Revision, next, out var replaced), "ledger replace failed");
        return replaced;
    }

    private static bool Restore(NpcServiceContractLedgerState state, out NpcServiceContractLedger ledger) =>
        NpcServiceContractLedger.TryRestore(state, "branch-health", _ => true, _ => true,
            (_, _, _) => true, (_, _, _, _, _) => true, _ => true, (_, _) => true, out ledger);

    private static NpcServiceTransferReceipt Transfer(string receiptId, NpcServiceTransferKind kind,
        string from, string to, int amount, double at) =>
        NpcServiceTransferReceipt.Create(receiptId, Contract, kind, from, to, amount, at);

    private static void Check(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }
}
