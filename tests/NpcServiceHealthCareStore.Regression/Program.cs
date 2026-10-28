using System;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class ServiceHealthCareStoreRegression
{
    private const string ContractId = "health-service-store";
    private const string Escrow = "contract:health-service-store:escrow";
    private const NpcArchetype Archetype = NpcArchetype.Citizen;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static int failures;

    private static int Main()
    {
        Run("same-person urgent care pauses service before task interruption", OwnerBoundPausePublishesOnce);
        Run("stale or foreign care goal and vitals reject without service mutation", StaleAndForeignSourcesReject);
        Run("owned pause is idempotent while partial care lowers priority", OwnedPauseContinuesAtLowerPriority);
        Run("accepted paid rest permits lower-priority treatment switch", AcceptedPaidRestPermitsLaterPause);
        Run("fresh important care without accepted treatment cannot pause service", FreshImportantCareCannotPause);
        Run("two-thirds health resumes only exact contract and episode", RecoveryResumesExactOwner);
        Run("saved physical return must finish before billable service resumes", ReturningActionBlocksResume);
        Run("expired care pause stays suspended for native settlement", ExpiredPauseCannotResume);
        Run("new injury transfers exact old unpaid owner to new care episode", NewInjuryTransfersSavedPause);
        Run("restore invalidates old service write ticket", RestoreInvalidatesTicket);
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    private static void OwnerBoundPausePublishesOnce()
    {
        var fixture = Ready();
        var before = Signature(fixture.Store);
        Check(Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 100,
            out bool newlyPaused, out string error), "urgent pause rejected: " + error);
        Check(newlyPaused, "first pause reported no-op");
        var contract = Contract(fixture.Store);
        Check(contract.Status == NpcServiceContractStatus.Suspended &&
            contract.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare &&
            contract.SuspensionOwnerId == fixture.Care.Id && contract.EligibleServiceSeconds == 0 &&
            contract.EarnedGold == 100 && contract.EscrowGold == 0,
            "service owner, accrual or escrow wrong");
        Check(fixture.Store.Snapshot().NpcServiceEvidence.CompletedTransfers.Count == 1,
            "no-transfer pause appended native evidence");
        Check(Signature(fixture.Store) != before, "successful pause published no state");
        var restored = new WorldStateStore(992);
        restored.Restore(fixture.Store.Snapshot());
        Check(Contract(restored).SuspensionOwnerId == fixture.Care.Id &&
            restored.ReadNpcHealthCare(fixture.Person).Id == fixture.Care.Id,
            "saved owner detached restore failed");
    }

    private static void StaleAndForeignSourcesReject()
    {
        var fixture = Ready();
        var store = fixture.Store;
        var ticket = store.CaptureNpcServiceContractWriteTicket();
        string before = Signature(store);
        Check(!store.TryEnsureNpcServiceHealthCarePause(ticket, ContractId, "another-person", fixture.Care.Id,
            fixture.Care.Revision, Archetype, fixture.Vitals.Revision, fixture.Vitals.CapturedAt,
            fixture.Motivation.Revision, fixture.Goal.GoalStartRevision, 100, out _, out _),
            "foreign person paused owned contract");
        Check(!store.TryEnsureNpcServiceHealthCarePause(ticket, ContractId, fixture.Person,
            "care:v1:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            fixture.Care.Revision, Archetype, fixture.Vitals.Revision, fixture.Vitals.CapturedAt,
            fixture.Motivation.Revision, fixture.Goal.GoalStartRevision, 100, out _, out _),
            "foreign episode paused service");
        Check(!store.TryEnsureNpcServiceHealthCarePause(ticket, ContractId, fixture.Person, fixture.Care.Id,
            fixture.Care.Revision + 1, Archetype, fixture.Vitals.Revision, fixture.Vitals.CapturedAt,
            fixture.Motivation.Revision, fixture.Goal.GoalStartRevision, 100, out _, out _),
            "stale care revision paused service");
        Check(!store.TryEnsureNpcServiceHealthCarePause(ticket, ContractId, fixture.Person, fixture.Care.Id,
            fixture.Care.Revision, Archetype, fixture.Vitals.Revision + 1, fixture.Vitals.CapturedAt,
            fixture.Motivation.Revision, fixture.Goal.GoalStartRevision, 100, out _, out _),
            "stale vitals revision paused service");
        Check(!store.TryEnsureNpcServiceHealthCarePause(ticket, ContractId, fixture.Person, fixture.Care.Id,
            fixture.Care.Revision, Archetype, fixture.Vitals.Revision, fixture.Vitals.CapturedAt + 1,
            fixture.Motivation.Revision, fixture.Goal.GoalStartRevision, 100, out _, out _),
            "stale vitals capture time paused service");
        Check(!store.TryEnsureNpcServiceHealthCarePause(ticket, ContractId, fixture.Person, fixture.Care.Id,
            fixture.Care.Revision, Archetype, fixture.Vitals.Revision, fixture.Vitals.CapturedAt,
            fixture.Motivation.Revision + 1, fixture.Goal.GoalStartRevision, 100, out _, out _),
            "stale motivation revision paused service");
        Check(!store.TryEnsureNpcServiceHealthCarePause(ticket, ContractId, fixture.Person, fixture.Care.Id,
            fixture.Care.Revision, Archetype, fixture.Vitals.Revision, fixture.Vitals.CapturedAt,
            fixture.Motivation.Revision, fixture.Goal.GoalStartRevision + 1, 100, out _, out _),
            "wrong recovery generation paused service");
        Check(Signature(store) == before, "rejected source changed service state");
    }

    private static void OwnedPauseContinuesAtLowerPriority()
    {
        var fixture = Ready();
        Check(Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 100,
            out bool first, out string error) && first, "first pause failed: " + error);
        var partial = fixture.Store.ReadNpcVitals(fixture.Person, Archetype);
        int improved = Math.Max(partial.CurrentHealth + 1, partial.MaximumHealth * 9 / 10);
        Check(improved < partial.MaximumHealth, "fixture has no partial-health state");
        Check(fixture.Store.TryCaptureNpcVitals(fixture.Person, Archetype, improved, partial.CurrentMagicka,
            110, partial.Revision), "partial native healing fixture failed");
        NpcHealthNeedLifecycle.Observe(fixture.Store, fixture.Person, Archetype, 111);
        fixture.Refresh();
        Check(fixture.Goal.Priority < NpcMotivationPriorityLevel.Urgent,
            "fixture did not lower recovery priority");
        string before = Signature(fixture.Store);
        Check(Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 112,
            out bool newlyPaused, out error), "owned low-priority continuation rejected: " + error);
        Check(!newlyPaused && Signature(fixture.Store) == before,
            "idempotent owned pause changed ledger or evidence");
    }

    private static void AcceptedPaidRestPermitsLaterPause()
    {
        var fixture = Ready();
        var task = CareTask(fixture, 101);
        Check(fixture.Store.TryAcceptAndBindNpcHealthCareTask(fixture.Person, fixture.Care.Revision,
            task, 101, out string error), "paid-rest care admission failed: " + error);
        Check(fixture.Store.ReadNpcHealthCare(fixture.Person).HasAcceptedTreatment,
            "accepted task did not persist continuation authority");
        var bound = fixture.Store.ActiveNpcTask(fixture.Person);
        Check(fixture.Store.FailNpcTask(bound.Id, bound.Revision, 102, "Bed became unavailable."),
            "terminal task fixture failed");
        var acting = fixture.Store.ReadNpcHealthCare(fixture.Person);
        Check(fixture.Store.TryBlockNpcHealthCare(fixture.Person, acting.Revision, 103,
            "Bed became unavailable."), "bounded care retry fixture failed");
        var injured = fixture.Store.ReadNpcVitals(fixture.Person, Archetype);
        int improved = Math.Max(injured.CurrentHealth + 1, injured.MaximumHealth * 9 / 10);
        Check(improved < injured.MaximumHealth, "fixture has no partial-health state");
        Check(fixture.Store.TryCaptureNpcVitals(fixture.Person, Archetype, improved,
            injured.CurrentMagicka, 200, injured.Revision), "partial native healing failed");
        NpcHealthNeedLifecycle.Observe(fixture.Store, fixture.Person, Archetype, 201);
        fixture.Refresh();
        Check(fixture.Goal.Priority == NpcMotivationPriorityLevel.Important &&
            fixture.Care.HasAcceptedTreatment && fixture.Care.Phase == NpcHealthCarePhase.Blocked,
            "saved accepted episode did not remain important and blocked");
        Check(!Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 402,
            out _, out _), "care retry bypassed its saved deadline");
        Check(Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 403,
            out bool newlyPaused, out error) && newlyPaused,
            "accepted low-priority episode could not pause before treatment switch: " + error);
        Check(Contract(fixture.Store).SuspensionOwnerId == fixture.Care.Id,
            "treatment switch lost exact care owner");
    }

    private static void FreshImportantCareCannotPause()
    {
        var fixture = Ready();
        var injured = fixture.Store.ReadNpcVitals(fixture.Person, Archetype);
        int improved = Math.Max(injured.CurrentHealth + 1, injured.MaximumHealth * 7 / 10);
        Check(improved < injured.MaximumHealth, "fixture has no important injury state");
        Check(fixture.Store.TryCaptureNpcVitals(fixture.Person, Archetype, improved,
            injured.CurrentMagicka, 110, injured.Revision), "fresh important injury fixture failed");
        NpcHealthNeedLifecycle.Observe(fixture.Store, fixture.Person, Archetype, 111);
        fixture.Refresh();
        Check(fixture.Goal.Priority == NpcMotivationPriorityLevel.Important &&
            !fixture.Care.HasAcceptedTreatment, "fresh care fixture already admitted a treatment");
        string before = Signature(fixture.Store);
        Check(!Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 112,
            out _, out _), "unadmitted important care interrupted billable service");
        Check(Signature(fixture.Store) == before, "deferred fresh care changed service ledger");
    }

    private static void RecoveryResumesExactOwner()
    {
        var fixture = Ready();
        Check(Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 100,
            out _, out string error), "pause failed: " + error);
        var injured = fixture.Store.ReadNpcVitals(fixture.Person, Archetype);
        int target = (int)((2L * injured.MaximumHealth + 2) / 3);
        Check(target < injured.MaximumHealth, "fixture must leave an injury for later rest");
        Check(fixture.Store.TryCaptureNpcVitals(fixture.Person, Archetype, target,
            injured.CurrentMagicka, 200, injured.Revision), "sufficient native healing fixture failed");
        fixture.Refresh();
        var ticket = fixture.Store.CaptureNpcServiceContractWriteTicket();
        string before = Signature(fixture.Store);
        Check(!Resume(fixture, ticket, 201, out _), "service resumed before saved care completion");
        Check(Signature(fixture.Store) == before, "early resume mutated service");
        Check(fixture.Store.TryCompleteNpcHealthCare(fixture.Person, fixture.Care.Revision, Archetype, 201),
            "saved care completion failed");
        fixture.Refresh();
        ticket = fixture.Store.CaptureNpcServiceContractWriteTicket();
        Check(!fixture.Store.TryResumeNpcServiceFromHealthCare(ticket, ContractId, fixture.Person,
            "care:v1:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            fixture.Care.Revision, Archetype, fixture.Vitals.Revision, fixture.Vitals.CapturedAt,
            202, out _), "wrong episode resumed contract");
        Check(Resume(fixture, ticket, 202, out error), "sufficient recovery did not resume service: " + error);
        var resumed = Contract(fixture.Store);
        Check(resumed.Status == NpcServiceContractStatus.Active &&
            resumed.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.None && resumed.SuspensionOwnerId == null &&
            resumed.BlockEndAt == 86400 && resumed.LastServiceUpdateAt == 202 &&
            resumed.EligibleServiceSeconds == 0,
            "resumption billed pause or changed fixed expiry");
        Check(!Resume(fixture, ticket, 203, out _), "stale ticket replayed resumption");
    }

    private static void ReturningActionBlocksResume()
    {
        var fixture = Ready();
        Check(Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 100,
            out _, out string error), "pause failed: " + error);
        var task = CareTask(fixture, 101);
        Check(fixture.Store.TryAcceptAndBindNpcHealthCareTask(fixture.Person, fixture.Care.Revision,
            task, 101, out error), "care admission failed: " + error);
        var bound = fixture.Store.ActiveNpcTask(fixture.Person);
        var position = IndoorPosition();
        var action = new NpcActionJob { Id = bound.Id + ":step:0", ProposalId = bound.Id + ":step:0",
            ResidentId = fixture.Person, Kind = NpcActionKind.MoveTo, Target = bound.Steps[0].Target,
            LastPosition = position, Phase = NpcActionPhase.Accepted, Revision = 1,
            AcceptedAt = 102, UpdatedAt = 102, DeadlineAt = 900 };
        Check(fixture.Store.TryDispatchNpcTaskStep(bound.Id, bound.Revision, 102,
            action, true, true, true, out error), "care action dispatch failed: " + error);
        var injured = fixture.Store.ReadNpcVitals(fixture.Person, Archetype);
        Check(fixture.Store.TryCaptureNpcVitals(fixture.Person, Archetype, injured.MaximumHealth,
            injured.CurrentMagicka, 103, injured.Revision), "external full healing failed");
        var care = fixture.Store.ReadNpcHealthCare(fixture.Person);
        Check(NpcHealthCareRecoveryHandoff.TryComplete(fixture.Store, fixture.Person, care.Revision,
            Archetype, 104, OutdoorPosition()), "physical recovery handoff failed");
        fixture.Refresh();
        var returning = fixture.Store.ActiveNpcAction(fixture.Person);
        Check(returning != null && NpcActionExecutor.IsTerminal(returning) && returning.ReturningToRoutine,
            "fixture did not retain terminal physical return");
        string before = Signature(fixture.Store);
        Check(!Resume(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 105, out _),
            "service resumed before physical return completed");
        Check(Signature(fixture.Store) == before, "early return retry changed contract");
        Check(fixture.Store.SaveNpcActionPosition(returning.Id, returning.Revision,
            returning.LastPosition, false, 106), "physical return release failed");
        fixture.Refresh();
        Check(Resume(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 107, out error),
            "service failed to resume after physical return: " + error);
    }

    private static NpcTaskPlan CareTask(Fixture fixture, double now)
    {
        string id = NpcHealthCare.MakeTaskId(fixture.Care);
        var task = new NpcTaskPlan { Id = id, ProposalId = id, ResidentId = fixture.Person,
            GoalId = fixture.Goal.Id, GoalStartRevision = fixture.Goal.GoalStartRevision,
            AcceptedAt = now, UpdatedAt = now, DeadlineAt = now + 900 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.MoveTo,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint,
                WorldSpaceId = "settlement:11", SettlementId = 11, BuildingKey = -1,
                X = 3, Y = 0, Z = 4 } });
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.RestToHeal, DurationSeconds = 300,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint,
                SettlementId = 11, BuildingKey = 23, ContainerId = "bed:health-care:1",
                X = 1.25, Y = 0, Z = -2.5 } });
        return task;
    }

    private static NpcActionTarget IndoorPosition() => new NpcActionTarget
    { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 11, BuildingKey = 23,
        X = 0, Y = 0, Z = 0 };

    private static NpcActionTarget OutdoorPosition() => new NpcActionTarget
    { Kind = NpcActionTargetKind.WorldPoint, WorldSpaceId = "settlement:11",
        SettlementId = 11, BuildingKey = -1, X = 5, Y = 0, Z = -3 };

    private static void ExpiredPauseCannotResume()
    {
        var fixture = Ready();
        Check(Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 100,
            out _, out string error), "pause failed: " + error);
        var injured = fixture.Store.ReadNpcVitals(fixture.Person, Archetype);
        Check(fixture.Store.TryCaptureNpcVitals(fixture.Person, Archetype, injured.MaximumHealth,
            injured.CurrentMagicka, 89990, injured.Revision), "late native healing fixture failed");
        Check(fixture.Store.TryCompleteNpcHealthCare(fixture.Person, fixture.Care.Revision, Archetype, 90000),
            "late care completion failed");
        fixture.Refresh();
        string before = Signature(fixture.Store);
        Check(!Resume(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 90000, out _),
            "expired contract resumed instead of awaiting settlement");
        Check(Signature(fixture.Store) == before && Contract(fixture.Store).EscrowGold == 0,
            "expired retry changed escrow or saved owner");
    }

    private static void NewInjuryTransfersSavedPause()
    {
        var fixture = Ready();
        Check(Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 100,
            out _, out string error), "first pause failed: " + error);
        string oldEpisode = fixture.Care.Id;
        var injured = fixture.Store.ReadNpcVitals(fixture.Person, Archetype);
        Check(fixture.Store.TryCaptureNpcVitals(fixture.Person, Archetype, injured.MaximumHealth,
            injured.CurrentMagicka, 200, injured.Revision), "first full recovery failed");
        Check(fixture.Store.TryCompleteNpcHealthCare(fixture.Person, fixture.Care.Revision, Archetype, 201),
            "first care completion failed");
        NpcHealthNeedLifecycle.Observe(fixture.Store, fixture.Person, Archetype, 201);
        var full = fixture.Store.ReadNpcVitals(fixture.Person, Archetype);
        Check(fixture.Store.TryCaptureNpcVitals(fixture.Person, Archetype,
            Math.Max(1, full.MaximumHealth / 4), full.CurrentMagicka, 202, full.Revision),
            "second native injury failed");
        Check(NpcHealthNeedLifecycle.Observe(fixture.Store, fixture.Person, Archetype, 203),
            "second recovery goal failed");
        Check(fixture.Store.TryBeginNpcHealthCare(fixture.Person, Archetype, 204),
            "new care episode failed");
        fixture.Refresh();
        Check(fixture.Care.Id != oldEpisode && fixture.Care.Phase == NpcHealthCarePhase.Seeking,
            "new injury retained old episode identity");
        string before = Signature(fixture.Store);
        var stale = fixture.Store.CaptureNpcServiceContractWriteTicket();
        Check(!TransferOwner(fixture, stale, fixture.Care.Id, 205, out _),
            "new owner was accepted as expected old owner");
        Check(Signature(fixture.Store) == before, "wrong-old-owner transfer mutated service");
        Check(TransferOwner(fixture, stale, oldEpisode, 205, out error),
            "new care did not inherit saved unpaid pause: " + error);
        var transferred = Contract(fixture.Store);
        Check(transferred.Status == NpcServiceContractStatus.Suspended &&
            transferred.SuspensionOwnerId == fixture.Care.Id && transferred.EligibleServiceSeconds == 0 &&
            transferred.FundedGold == 100 && transferred.EscrowGold == 0 &&
            fixture.Store.Snapshot().NpcServiceEvidence.CompletedTransfers.Count == 1,
            "owner transfer changed billing or native receipt evidence");
        Check(!TransferOwner(fixture, stale, oldEpisode, 206, out _),
            "old service ticket replayed owner transfer");
        var restored = new WorldStateStore(981);
        restored.Restore(fixture.Store.Snapshot());
        Check(Contract(restored).SuspensionOwnerId == fixture.Care.Id,
            "new owner lost across save restore");
    }

    private static bool TransferOwner(Fixture fixture, NpcServiceContractWriteTicket ticket,
        string oldEpisode, double now, out string error) =>
        fixture.Store.TryTransferNpcServiceHealthPauseToNewCare(ticket, ContractId,
            fixture.Person, oldEpisode, fixture.Care.Id, fixture.Care.Revision,
            Archetype, fixture.Vitals.Revision, fixture.Vitals.CapturedAt,
            fixture.Motivation.Revision, fixture.Goal.GoalStartRevision, now, out error);

    private static void RestoreInvalidatesTicket()
    {
        var fixture = Ready();
        var stale = fixture.Store.CaptureNpcServiceContractWriteTicket();
        fixture.Store.Restore(fixture.Store.Snapshot());
        fixture.Refresh();
        string before = Signature(fixture.Store);
        Check(!Ensure(fixture, stale, 100, out _, out _), "pre-restore ticket paused service");
        Check(Signature(fixture.Store) == before, "stale ticket changed saved service state");
        Check(Ensure(fixture, fixture.Store.CaptureNpcServiceContractWriteTicket(), 100,
            out bool newlyPaused, out string error) && newlyPaused,
            "new binding could not pause service: " + error);
    }

    private static bool Ensure(Fixture fixture, NpcServiceContractWriteTicket ticket, double now,
        out bool newlyPaused, out string error) =>
        fixture.Store.TryEnsureNpcServiceHealthCarePause(ticket, ContractId, fixture.Person,
            fixture.Care.Id, fixture.Care.Revision, Archetype, fixture.Vitals.Revision,
            fixture.Vitals.CapturedAt, fixture.Motivation.Revision, fixture.Goal.GoalStartRevision,
            now, out newlyPaused, out error);

    private static bool Resume(Fixture fixture, NpcServiceContractWriteTicket ticket, double now,
        out string error) =>
        fixture.Store.TryResumeNpcServiceFromHealthCare(ticket, ContractId, fixture.Person,
            fixture.Care.Id, fixture.Care.Revision, Archetype, fixture.Vitals.Revision,
            fixture.Vitals.CapturedAt, now, out error);

    private static Fixture Ready()
    {
        var fixture = new Fixture { Store = new WorldStateStore(451) };
        fixture.Person = fixture.Store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0].Id;
        var baseline = fixture.Store.ReadNpcVitals(fixture.Person, Archetype);
        Check(fixture.Store.TryCaptureNpcVitals(fixture.Person, Archetype,
            Math.Max(1, baseline.MaximumHealth / 4), baseline.CurrentMagicka, 90, baseline.Revision),
            "native injury fixture failed");
        Check(NpcHealthNeedLifecycle.Observe(fixture.Store, fixture.Person, Archetype, 95),
            "native recovery goal fixture failed");
        Check(fixture.Store.TryBeginNpcHealthCare(fixture.Person, Archetype, 100), "care fixture failed");
        fixture.Refresh();
        Check(fixture.Goal.Priority >= NpcMotivationPriorityLevel.Urgent,
            "injured recovery goal lacks urgent priority");
        var home = "home:11:23";
        Check(NpcServiceContractTerms.TryCreate(ContractId, NpcDirectedRelationship.PlayerTargetId,
            fixture.Person, home, NpcServiceRole.Fighter,
            NpcServiceCoverage.Follow | NpcServiceCoverage.Rest, 100, false,
            NpcServiceRiskScope.None, Array.Empty<NpcServiceProtectedTarget>(),
            NpcServiceRestTreatment.CountsAsPaidAvailability,
            NpcServiceResupplyResponsibility.SeparatelyNegotiated,
            Array.Empty<NpcServiceReservedObligation>(),
            id => id == NpcDirectedRelationship.PlayerTargetId || id == fixture.Person,
            id => id == home, (_, _, _) => false, (_, _, _, _, _) => false, out var terms),
            "service terms fixture failed");
        var ticket = fixture.Store.CaptureNpcServiceContractWriteTicket();
        var funding = NpcServiceTransferReceipt.Create("funding-health", ContractId,
            NpcServiceTransferKind.Funding, NpcDirectedRelationship.PlayerTargetId, fixture.Person, 100, 0);
        Check(NpcServiceContractLedger.TryReserve(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
            terms, funding, out var reserved), "funded service candidate failed");
        Check(fixture.Store.InitializeNpcInventory(fixture.Person,0,Array.Empty<NpcInventoryItem>()),"wallet fixture");
        var wallet=fixture.Store.ReadNpcInventory(fixture.Person);
        Check(fixture.Store.TryCommitNpcServiceSettlement(ticket,reserved,new NpcInventoryUpdate {PersonId=fixture.Person,
            ExpectedRevision=wallet.Revision,WalletGold=100,Items=wallet.Items},new[]{funding},out string error),"payment commit failed: "+error);
        ticket = fixture.Store.CaptureNpcServiceContractWriteTicket();
        var current = ticket.SourceLedger.Read(ContractId);
        Check(current.TryStart(current.Revision, 0, out var active), "service start transition failed");
        Check(NpcServiceContractLedger.TryReplace(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
            active, out var activeLedger), "service start candidate failed");
        Check(fixture.Store.TryCommitNpcServiceContracts(ticket, activeLedger,
            Array.Empty<NpcServiceTransferReceipt>(), Array.Empty<NpcServiceRenewalAuthorization>(), out error),
            "service start commit failed: " + error);
        return fixture;
    }

    private static NpcServiceContract Contract(WorldStateStore store) =>
        store.ReadNpcServiceContracts().Read(ContractId);

    private static string Signature(WorldStateStore store)
    {
        var state = store.Snapshot();
        return JsonSerializer.Serialize(new { state.NpcServiceContracts, state.NpcServiceEvidence }, Json);
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture
    {
        public WorldStateStore Store;
        public string Person;
        public NpcHealthCareRecord Care;
        public NpcVitalsView Vitals;
        public NpcMotivationStateView Motivation;
        public NpcMotivationGoalView Goal;

        public void Refresh()
        {
            Care = Store.ReadNpcHealthCare(Person);
            Vitals = Store.ReadNpcVitals(Person, Archetype);
            Motivation = Store.ReadNpcMotivationState(Person);
            Goal = null;
            foreach (var goal in Motivation.Goals)
                if (goal.Id == NpcHealthNeedLifecycle.RecoveryGoalId) Goal = goal;
            Check(Care != null && Vitals != null && Goal != null, "care source fixture vanished");
        }
    }
}
