using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using FullSerializer;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const int Settlement = 11;
    private const int Home = 23;

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("ordinary home sleep counts only scheduled rest across midnight and bounds long absence", ScheduledHomeSleep),
            ("medical visits retain actual position across native serialization and reject stale scene writes", MedicalVisitSurvivesReload),
            ("native condition payload survives vitals changes and native serialization", NativeConditionsSurviveReload),
            ("full-health illness creates condition state without inventing injury", FullHealthConditionIsRetained),
            ("two-thirds health releases care without demanding full healing", SufficientRecoveryPreservesRemainingInjury),
            ("care begins only from persisted adult injury below two thirds", BeginRequiresObservedAdultInjury),
            ("one care episode persists until sufficient health and later injury gets new identity", EpisodePersistsUntilRecovery),
            ("care task binding requires current ready task owned by resident", BindRequiresOwnedReadyTask),
            ("care task binding accepts only scoped recovery plans and exact care task IDs", BindRequiresScopedCarePlan),
            ("restore rejects an active care row linked to an unrelated task plan", RestoreRejectsUnrelatedCareTask),
            ("blocking keeps physical location and retry task identity survives reload", BlockedRetryAndPositionSurviveReload),
            ("interrupted care saves observed flight position through retry and restore", InterruptedCareSavesFlightPosition),
            ("a failed retry can reblock only at its retry boundary", BlockedRetryCanBeBlockedAgain),
            ("completion requires sufficient health and no nonterminal task", CompletionRequiresHealingAndTerminalTask),
            ("external full healing cancels care work and preserves physical return across reload", ExternalHealingPreservesRoutineReturn),
            ("unavailable actor or stale care cannot discard an active recovery route", UnavailableHandoffKeepsCareActive),
            ("verified partial rest immediately reopens care without failure backoff", PartialTreatmentContinuesImmediately),
            ("native self healing uses bound care task and typed receipt", MagicHealingUsesBoundCareTask),
            ("cancelled treatment cannot fast replan and retains failure backoff", CancelledTreatmentKeepsBackoff),
            ("FullSerializer roundtrip retains care and schema 43 migrates empty", FullSerializerAndLegacyMigration),
            ("restore rejects duplicate residents and dynamic paused targets", RestoreRejectsCorruptCareRows),
            ("paused points reject dynamic item and person metadata", PausedPointsRejectDynamicMetadata)
        };

        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void ScheduledHomeSleep()
    {
        var day=DailySchedule.HomeDay(Home,7*60,22*60);
        Equal(2700d,day.HomeSleepSeconds(21.5*3600,22.75*3600));
        Equal(8d*3600,day.HomeSleepSeconds(23*3600,32*3600));
        Equal(0d,day.HomeSleepSeconds(10*3600,18*3600));
        Equal(9d*3600,day.HomeSleepSeconds(0,260d*86400+8*3600));
        Equal(0d,day.HomeSleepSeconds(12,11));
    }

    private static void MedicalVisitSurvivesReload()
    {
        var store=Create(out string id);
        object epoch=store.CaptureNpcVitalsWriteEpoch();
        True(store.TryCaptureNpcConditions(epoch,id,"[]"),"condition owner");
        var visit=new NpcTreatmentVisit {ProviderKey=Home,ReturnKey=Home,EffectKey="Cure-Poison",
            Position=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=Settlement,
                WorldSpaceId="settlement:"+Settlement,BuildingKey=-1,X=12,Z=34}};
        True(store.SaveNpcTreatmentVisit(epoch,id,visit),"visit saved");
        visit.Position.X=999;
        Equal(12d,store.ReadNpcTreatmentVisit(id).Position.X);
        var read=store.ReadNpcTreatmentVisit(id);read.Position.X=444;
        Equal(12d,store.ReadNpcTreatmentVisit(id).Position.X);
        var restored=new WorldStateStore(0);restored.Restore(FullSerializerRoundtrip(store.Snapshot()));
        Equal(34d,restored.ReadNpcProgress(id).TreatmentVisit.Position.Z);
        True(!restored.SaveNpcTreatmentVisit(epoch,id,null),"stale writer rejected");
        True(restored.SaveNpcTreatmentVisit(restored.CaptureNpcVitalsWriteEpoch(),id,null),"visit finishes");
        Equal(null,restored.ReadNpcProgress(id).TreatmentVisit);
    }

    private static void FullHealthConditionIsRetained()
    {
        WorldStateStore store = Create(out string id);
        var before = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        True(!before.IsPersisted, "healthy fixture starts from native baseline");
        True(store.TryCaptureNpcConditions(store.CaptureNpcVitalsWriteEpoch(), id, "[{\"bundleType\":3}]"),
            "full-health illness must persist");
        var after = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        True(after.IsPersisted, "native condition needs a saved owner");
        Equal(before.CurrentHealth, after.CurrentHealth);
        True(!store.ReadNpcProgress(id).HasPendingHealthEpisode, "condition alone is not injury or healing");
    }

    private static void NativeConditionsSurviveReload()
    {
        WorldStateStore store = Create(out string id);
        Injure(store, id, 100d, 90d);
        object epoch = store.CaptureNpcVitalsWriteEpoch();
        const string payload = "[{\"bundleType\":3,\"liveEffects\":[{\"key\":\"Poison-Nux_Vomica\"}]}]";
        True(store.TryCaptureNpcConditions(epoch, id, payload), "native packet is captured");
        NpcVitalsView before = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        CaptureHealth(store, id, before, before.CurrentHealth + 1, 101d);
        Equal(payload, store.ReadNpcProgress(id).NativeConditions);
        var restored = new WorldStateStore(0);
        restored.Restore(FullSerializerRoundtrip(store.Snapshot()));
        Equal(payload, restored.ReadNpcProgress(id).NativeConditions);
        True(!restored.TryCaptureNpcConditions(epoch, id, null), "old scene lease cannot clear restored illness");
        True(restored.TryCaptureNpcConditions(restored.CaptureNpcVitalsWriteEpoch(), id, null), "native cure clears persisted packet");
        Equal(null, restored.ReadNpcProgress(id).NativeConditions);
    }

    private static void SufficientRecoveryPreservesRemainingInjury()
    {
        WorldStateStore store = Create(out string id);
        NpcVitalsView injured = Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "care starts");
        NpcHealthCareRecord care = store.ReadNpcHealthCare(id);
        int target = (int)((2L * injured.MaximumHealth + 2) / 3);
        CaptureHealth(store, id, store.ReadNpcVitals(id, NpcArchetype.Citizen), target - 1, 101d);
        True(!store.TryCompleteNpcHealthCare(id, care.Revision, NpcArchetype.Citizen, 101d), "below target still needs treatment");
        CaptureHealth(store, id, store.ReadNpcVitals(id, NpcArchetype.Citizen), target, 102d);
        True(NpcHealthCareRecoveryHandoff.TryComplete(store, id, care.Revision, NpcArchetype.Citizen, 102d, IndoorPosition()),
            "two-thirds native health releases the work interruption");
        NpcVitalsView recovered = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        Equal(target, recovered.CurrentHealth);
        True(recovered.CurrentHealth < recovered.MaximumHealth, "missing health remains for later rest");
        True(!NpcHealthNeedLifecycle.ShouldInterruptWork(recovered), "routine does not immediately restart care");
        True(!store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 103d, IndoorPosition()), "no new potion episode at sufficient health");
    }

    private static void BeginRequiresObservedAdultInjury()
    {
        string id; WorldStateStore store = Create(out id);
        NpcVitalsView baseline = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        True(!baseline.IsPersisted && baseline.Revision == 0, "fixture starts from seeded vitals");
        True(!store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d), "seeded baseline cannot create care");
        True(store.ReadNpcHealthCare(id) == null, "seeded baseline allocates no care row");

        CaptureHealth(store, id, baseline, baseline.MaximumHealth - 1, 90d);
        True(!store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d), "health at or above two thirds must not interrupt work");
        True(store.ReadNpcHealthCare(id) == null, "healthy resident allocates no care row");

        NpcVitalsView injured = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        int threshold = (int)((2L * injured.MaximumHealth - 1) / 3);
        CaptureHealth(store, id, injured, Math.Max(1, threshold), 95d);
        NpcActionTarget position = IndoorPosition();
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, position), "observed injury at threshold starts care");
        NpcHealthCareRecord row = store.ReadNpcHealthCare(id);
        Equal(NpcHealthCarePhase.Seeking, row.Phase);
        Equal(id, row.PersonId);
        Equal(position.BuildingKey, row.PausedPosition.BuildingKey);
        long revision = row.Revision;
        True(!store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 101d, position), "duplicate begin cannot create a second episode");
        Equal(revision, store.ReadNpcHealthCare(id).Revision);
        Equal(1, store.Snapshot().NpcHealthCares.Count);

        string childId = id;
        WorldStateStore childStore = Create(out childId);
        childStore.ResolveResident(childId).AgeGroup = ResidentAgeGroup.Child;
        NpcVitalsView childBaseline = childStore.ReadNpcVitals(childId, NpcArchetype.Citizen);
        CaptureHealth(childStore, childId, childBaseline, Math.Max(1, (int)((long)childBaseline.MaximumHealth * 3 / 5)), 90d);
        True(!childStore.TryBeginNpcHealthCare(childId, NpcArchetype.Citizen, 100d, position), "children cannot start this adult care path");

        WorldStateStore protectedStore = Create(out string protectedId);
        protectedStore.ResolveResident(protectedId).IsQuestProtected = true;
        NpcVitalsView protectedBaseline = protectedStore.ReadNpcVitals(protectedId, NpcArchetype.Citizen);
        CaptureHealth(protectedStore, protectedId, protectedBaseline, Math.Max(1, (int)((long)protectedBaseline.MaximumHealth * 3 / 5)), 90d);
        True(!protectedStore.TryBeginNpcHealthCare(protectedId, NpcArchetype.Citizen, 100d, position), "quest-protected residents cannot start care");
    }

    private static void EpisodePersistsUntilRecovery()
    {
        string id; WorldStateStore store = Create(out id);
        NpcVitalsView baseline = Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "injured resident starts care");
        NpcHealthCareRecord first = store.ReadNpcHealthCare(id);

        CaptureHealth(store, id, store.ReadNpcVitals(id, NpcArchetype.Citizen),
            Math.Max(1, (int)((long)baseline.MaximumHealth * 3 / 5) - 1), 105d);
        True(!store.TryCompleteNpcHealthCare(id, first.Revision, NpcArchetype.Citizen, 105d), "partial healing does not complete care");
        Equal(NpcHealthCarePhase.Seeking, store.ReadNpcHealthCare(id).Phase);

        CaptureHealth(store, id, store.ReadNpcVitals(id, NpcArchetype.Citizen), baseline.MaximumHealth, 110d);
        True(store.TryCompleteNpcHealthCare(id, first.Revision, NpcArchetype.Citizen, 110d), "observed full health completes episode");
        Equal(NpcHealthCarePhase.Recovered, store.ReadNpcHealthCare(id).Phase);
        Equal(first.Id, store.ReadNpcHealthCare(id).Id);

        NpcVitalsView full = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        CaptureHealth(store, id, full, Math.Max(1, (int)((long)full.MaximumHealth * 3 / 5)), 120d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 120d, IndoorPosition()), "later injury starts next episode");
        NpcHealthCareRecord second = store.ReadNpcHealthCare(id);
        True(first.Id != second.Id, "new episode uses later initial vitals revision");
        Equal(1, store.Snapshot().NpcHealthCares.Count);
    }

    private static void BindRequiresOwnedReadyTask()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "care starts");
        NpcHealthCareRecord row = store.ReadNpcHealthCare(id);
        string taskId = NpcHealthCare.MakeTaskId(row);
        True(!store.TryBindNpcHealthCareTask(id, row.Revision, taskId, 101d), "missing task cannot be bound");

        string otherId = CreateSecondResident(store);
        NpcTaskPlan wrongOwner = Task(taskId, otherId, 101d);
        True(!store.TryAcceptAndBindNpcHealthCareTask(id, row.Revision, wrongOwner, 101d, out _),
            "task owned by another resident cannot enter care");

        string validId; WorldStateStore validStore = Create(out validId);
        Injure(validStore, validId, 100d, 90d);
        True(validStore.TryBeginNpcHealthCare(validId, NpcArchetype.Citizen, 100d, IndoorPosition()), "second care starts");
        NpcHealthCareRecord validRow = validStore.ReadNpcHealthCare(validId);
        string validTaskId = NpcHealthCare.MakeTaskId(validRow);
        NpcTaskPlan careTask = Task(validTaskId, validId, 104d);
        AcceptAndBindCare(validStore, validId, validRow, careTask, 104d);
        NpcHealthCareRecord acting = validStore.ReadNpcHealthCare(validId);
        Equal(NpcHealthCarePhase.Acting, acting.Phase);
        Equal(validTaskId, acting.TaskId);
        Equal(validRow.Revision + 1, acting.Revision);
        True(!validStore.TryBindNpcHealthCareTask(validId, validRow.Revision, validTaskId, 105d), "stale care revision cannot rebind");
    }

    private static void BindRequiresScopedCarePlan()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "care starts");
        NpcHealthCareRecord row = store.ReadNpcHealthCare(id);
        string taskId = NpcHealthCare.MakeTaskId(row);

        NpcTaskPlan noTreatment = Task(taskId, id, 101d);
        noTreatment.Steps.RemoveAt(1);
        True(!store.TryAcceptAndBindNpcHealthCareTask(id, row.Revision, noTreatment, 101d, out _), "movement alone cannot satisfy care task");

        string waitId; WorldStateStore waitStore = Create(out waitId);
        Injure(waitStore, waitId, 100d, 90d);
        True(waitStore.TryBeginNpcHealthCare(waitId, NpcArchetype.Citizen, 100d), "second care starts");
        NpcHealthCareRecord waitRow = waitStore.ReadNpcHealthCare(waitId);
        string waitTaskId = NpcHealthCare.MakeTaskId(waitRow);
        NpcTaskPlan waitTask = Task(waitTaskId, waitId, 101d);
        waitTask.Steps[1] = new NpcTaskStep { Kind = NpcActionKind.Wait, Target = waitTask.Steps[0].Target,
            DurationSeconds = 60 };
        True(!waitStore.TryAcceptAndBindNpcHealthCareTask(waitId, waitRow.Revision, waitTask, 101d, out _), "wait cannot be hidden inside care plan");

        string followId; WorldStateStore followStore = Create(out followId);
        Injure(followStore, followId, 100d, 90d);
        True(followStore.TryBeginNpcHealthCare(followId, NpcArchetype.Citizen, 100d), "third care starts");
        NpcHealthCareRecord followRow = followStore.ReadNpcHealthCare(followId);
        string followTaskId = NpcHealthCare.MakeTaskId(followRow);
        NpcTaskPlan followTask = Task(followTaskId, followId, 101d);
        followTask.Steps[1] = new NpcTaskStep { Kind = NpcActionKind.Follow,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "npc:other" }, DurationSeconds = 60 };
        True(!followStore.TryAcceptAndBindNpcHealthCareTask(followId, followRow.Revision, followTask, 101d, out _), "follow cannot be hidden inside care plan");

        string buyId; WorldStateStore buyStore = Create(out buyId);
        Injure(buyStore, buyId, 100d, 90d);
        True(buyStore.TryBeginNpcHealthCare(buyId, NpcArchetype.Citizen, 100d), "purchase care starts");
        NpcHealthCareRecord buyRow = buyStore.ReadNpcHealthCare(buyId);
        string buyTaskId = NpcHealthCare.MakeTaskId(buyRow);
        NpcTaskPlan buyAndUse = PurchasedCareTask(buyTaskId, buyId, 101d);
        AcceptAndBindCare(buyStore, buyId, buyRow, buyAndUse, 101d);

        string orphanId; WorldStateStore orphanStore = Create(out orphanId);
        Injure(orphanStore, orphanId, 100d, 90d);
        True(orphanStore.TryBeginNpcHealthCare(orphanId, NpcArchetype.Citizen, 100d), "orphan purchase care starts");
        NpcHealthCareRecord orphanRow = orphanStore.ReadNpcHealthCare(orphanId);
        string orphanTaskId = NpcHealthCare.MakeTaskId(orphanRow);
        NpcTaskPlan orphanPurchase = Task(orphanTaskId, orphanId, 101d);
        orphanPurchase.Steps[1] = new NpcTaskStep { Kind = NpcActionKind.Purchase, Target = new NpcActionTarget
        {
            Kind = NpcActionTargetKind.IndoorPoint, SettlementId = Settlement, BuildingKey = Home,
            PersonId = "npc:care-provider", ContainerId = "700", ItemId = "44", Quantity = 1,
            QuotedGold = 9, X = 1.25, Y = 0, Z = -2.5
        } };
        True(!orphanStore.TryAcceptAndBindNpcHealthCareTask(orphanId, orphanRow.Revision, orphanPurchase, 101d, out _),
            "purchase without exact later use cannot be bound as healthcare");

        WorldState forged = buyStore.Snapshot();
        forged.NpcHealthCares[0].TaskId = "arbitrary-task:1";
        forged.NpcTasks[0].Id = "arbitrary-task:1";
        forged.NpcTasks[0].ProposalId = "arbitrary-task:1";
        Throws<ArgumentException>(() => new WorldStateStore(0).Restore(forged));
    }

    private static void RestoreRejectsUnrelatedCareTask()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d), "care starts");
        NpcHealthCareRecord care = store.ReadNpcHealthCare(id);
        string taskId = NpcHealthCare.MakeTaskId(care);
        NpcTaskPlan task = Task(taskId, id, 101d);
        AcceptAndBindCare(store, id, care, task, 101d);

        WorldState unrelated = store.Snapshot();
        NpcTaskPlan savedTask = unrelated.NpcTasks[0];
        savedTask.Steps[1] = new NpcTaskStep { Kind = NpcActionKind.Wait,
            Target = savedTask.Steps[0].Target, DurationSeconds = 60 };
        Throws<ArgumentException>(() => new WorldStateStore(0).Restore(unrelated));
    }

    private static void BlockedRetryCanBeBlockedAgain()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d), "care starts");
        NpcHealthCareRecord seeking = store.ReadNpcHealthCare(id);
        True(store.TryBlockNpcHealthCare(id, seeking.Revision, 101d, "No bed was available."), "first failure blocks care");
        NpcHealthCareRecord blocked = store.ReadNpcHealthCare(id);
        True(!store.TryBlockNpcHealthCare(id, blocked.Revision, blocked.RetryAt - 1, "Still unavailable."),
            "care cannot retry before backoff expires");
        Equal(blocked.Revision, store.ReadNpcHealthCare(id).Revision);

        True(store.TryBlockNpcHealthCare(id, blocked.Revision, blocked.RetryAt, "Still unavailable."),
            "a failed bounded retry may schedule another bounded retry");
        NpcHealthCareRecord retried = store.ReadNpcHealthCare(id);
        Equal(NpcHealthCarePhase.Blocked, retried.Phase);
        Equal(blocked.RetryAt, retried.UpdatedAt);
        Equal(blocked.RetryAt + NpcHealthCare.RetryDelaySeconds, retried.RetryAt);
        Equal(blocked.Revision + 1, retried.Revision);
    }

    private static void BlockedRetryAndPositionSurviveReload()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        NpcActionTarget initial = IndoorPosition();
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, initial), "care starts");
        NpcHealthCareRecord row = store.ReadNpcHealthCare(id);
        string firstTaskId = NpcHealthCare.MakeTaskId(row);
        True(store.TryBlockNpcHealthCare(id, row.Revision, 101d, "No effective treatment is reachable."), "unavailable provider blocks care");
        NpcHealthCareRecord blocked = store.ReadNpcHealthCare(id);
        Equal(NpcHealthCarePhase.Blocked, blocked.Phase);
        Equal("No effective treatment is reachable.", blocked.LastFailure);
        Equal(401d, blocked.RetryAt);
        True(blocked.TaskId == null, "blocked episode holds no active task");
        string retryTaskId = NpcHealthCare.MakeTaskId(blocked);
        True(firstTaskId != retryTaskId, "persisted revision makes retry task ID unique");
        True(!store.TryBindNpcHealthCareTask(id, blocked.Revision, retryTaskId, 400d), "retry waits for bounded backoff");

        WorldState saved = store.Snapshot();
        WorldState restoredWire = FullSerializerRoundtrip(saved);
        var restored = new WorldStateStore(0);
        restored.Restore(restoredWire);
        NpcHealthCareRecord afterReload = restored.ReadNpcHealthCare(id);
        Equal(blocked.Id, afterReload.Id);
        Equal(retryTaskId, NpcHealthCare.MakeTaskId(afterReload));
        Equal(initial.BuildingKey, afterReload.PausedPosition.BuildingKey);
        Equal(initial.X, afterReload.PausedPosition.X);

        WorldStateStore positionStore = restored;
        NpcTaskPlan retryTask = Task(retryTaskId, id, afterReload.RetryAt);
        AcceptAndBindCare(positionStore, id, afterReload, retryTask, afterReload.RetryAt);
        NpcHealthCareRecord acting = positionStore.ReadNpcHealthCare(id);
        NpcActionTarget moved = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint,
            SettlementId = Settlement, BuildingKey = -1, WorldSpaceId = "settlement:11", X = 7.5, Y = 0, Z = 9 };
        NpcTaskPlan failedTask = positionStore.ReadNpcTask(retryTaskId);
        True(positionStore.FailNpcTask(failedTask.Id, failedTask.Revision, afterReload.RetryAt + 10, "Provider route failed."), "terminalize failed route fixture");
        True(positionStore.TryUpdateNpcHealthCarePosition(id, acting.Revision, afterReload.RetryAt + 10, moved), "task boundary stores actual position");
        long movedRevision = positionStore.ReadNpcHealthCare(id).Revision;
        True(!positionStore.TryUpdateNpcHealthCarePosition(id, movedRevision, afterReload.RetryAt + 11, moved), "same position is no-op");
        Equal(movedRevision, positionStore.ReadNpcHealthCare(id).Revision);
        True(positionStore.TryBlockNpcHealthCare(id, movedRevision, afterReload.RetryAt + 12, "Antidote stock vanished."), "failed retry blocks again");
        Equal(7.5d, positionStore.ReadNpcHealthCare(id).PausedPosition.X);
    }

    private static void InterruptedCareSavesFlightPosition()
    {
        string id; WorldStateStore store = RunningCareAction(out id);
        NpcHealthCareRecord acting = store.ReadNpcHealthCare(id);
        NpcTaskPlan running = store.ActiveNpcTask(id);
        NpcActionJob action = store.ActiveNpcAction(id);
        NpcVitalsView injured = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        True(action != null && action.Phase == NpcActionPhase.Accepted,
            "fixture has an unperformed care action before alarm");
        True(store.ObserveNpcAction(action.Id, action.Revision, 103d,
            NpcActionObservation.Interrupted("Observed native alarm owns movement.")),
            "real interruption terminates action without treatment");
        NpcActionJob interrupted = store.ReadNpcAction(action.Id);
        True(interrupted.TreatmentReceipt == null, "alarm interruption has no fabricated treatment");
        True(store.ReconcileNpcTask(running.Id, running.Revision, 103d),
            "completed interruption receipt reconciles the care task");
        True(store.ActiveNpcTask(id) == null, "failed care task releases its action owner");

        var flight = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint,
            SettlementId = Settlement, BuildingKey = -1, WorldSpaceId = "settlement:11",
            X = 8.5, Y = 0, Z = 11.25 };
        True(store.TryUpdateNpcHealthCarePosition(id, acting.Revision, 104d, flight),
            "only the observed post-interruption position becomes the care anchor");
        NpcHealthCareRecord moved = store.ReadNpcHealthCare(id);
        True(store.TryBlockNpcHealthCare(id, moved.Revision, 105d, "Alarm interrupted treatment."),
            "care enters bounded retry after saving movement");
        NpcHealthCareRecord blocked = store.ReadNpcHealthCare(id);
        Equal(NpcHealthCarePhase.Blocked, blocked.Phase);
        Equal(flight.X, blocked.PausedPosition.X);
        Equal(injured.CurrentHealth, store.ReadNpcVitals(id, NpcArchetype.Citizen).CurrentHealth);

        var restored = new WorldStateStore(0);
        restored.Restore(FullSerializerRoundtrip(store.Snapshot()));
        NpcHealthCareRecord reloaded = restored.ReadNpcHealthCare(id);
        Equal(blocked.RetryAt, reloaded.RetryAt);
        Equal(flight.X, reloaded.PausedPosition.X);
        Equal(flight.Z, reloaded.PausedPosition.Z);
        True(restored.ActiveNpcTask(id) == null && restored.ActiveNpcAction(id) == null,
            "restore does not revive the interrupted movement owner");
    }

    private static void CompletionRequiresHealingAndTerminalTask()
    {
        string id; WorldStateStore store = Create(out id);
        NpcVitalsView baseline = Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "care starts");
        NpcHealthCareRecord row = store.ReadNpcHealthCare(id);
        string taskId = NpcHealthCare.MakeTaskId(row);
        NpcTaskPlan task = Task(taskId, id, 101d);
        AcceptAndBindCare(store, id, row, task, 101d);
        NpcHealthCareRecord acting = store.ReadNpcHealthCare(id);
        True(!store.TryCompleteNpcHealthCare(id, acting.Revision, NpcArchetype.Citizen, 102d), "active task blocks completion");

        NpcVitalsView injured = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        CaptureHealth(store, id, injured, baseline.MaximumHealth, 103d);
        True(!store.TryCompleteNpcHealthCare(id, acting.Revision, NpcArchetype.Citizen, 103d), "full health cannot complete while task remains active");
        task = store.ReadNpcTask(taskId);
        True(store.FailNpcTask(task.Id, task.Revision, 104d, "Provider refused treatment."), "terminalize task fixture");
        True(store.TryCompleteNpcHealthCare(id, acting.Revision, NpcArchetype.Citizen, 104d), "full health and no active task complete episode");
        NpcHealthCareRecord recovered = store.ReadNpcHealthCare(id);
        Equal(NpcHealthCarePhase.Recovered, recovered.Phase);
        True(recovered.TaskId == null, "recovered row releases task ownership");
    }

    private static void ExternalHealingPreservesRoutineReturn()
    {
        string id;
        WorldStateStore store = RunningCareAction(out id);
        NpcVitalsView injured = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        CaptureHealth(store, id, injured, injured.MaximumHealth, 103d);
        NpcHealthCareRecord care = store.ReadNpcHealthCare(id);
        NpcActionTarget actualPosition = OutdoorPosition();

        True(NpcHealthCareRecoveryHandoff.TryComplete(store, id, care.Revision,
            NpcArchetype.Citizen, 104d, actualPosition), "external full healing hands off to physical return");
        Equal(NpcHealthCarePhase.Recovered, store.ReadNpcHealthCare(id).Phase);
        True(store.ActiveNpcTask(id) == null, "care task no longer owns resident");
        NpcActionJob returning = store.ActiveNpcAction(id);
        True(returning != null && returning.ReturningToRoutine && returning.Phase == NpcActionPhase.Cancelled,
            "canceled treatment remains a physical return action");
        Equal(actualPosition.X, returning.LastPosition.X);
        Equal(actualPosition.WorldSpaceId, returning.LastPosition.WorldSpaceId);

        var reloaded = new WorldStateStore(0);
        reloaded.Restore(FullSerializerRoundtrip(store.Snapshot()));
        Equal(NpcHealthCarePhase.Recovered, reloaded.ReadNpcHealthCare(id).Phase);
        True(reloaded.ActiveNpcAction(id).ReturningToRoutine, "reload preserves physical return");
    }

    private static void UnavailableHandoffKeepsCareActive()
    {
        string id;
        WorldStateStore store = RunningCareAction(out id);
        NpcHealthCareRecord care = store.ReadNpcHealthCare(id);
        NpcVitalsView injured = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        True(!NpcHealthCareRecoveryHandoff.TryComplete(store, id, care.Revision,
            NpcArchetype.Citizen, 102d, OutdoorPosition()), "partial health cannot cancel care work");
        Equal(NpcTaskPhase.Running, store.ActiveNpcTask(id).Phase);

        CaptureHealth(store, id, injured, injured.MaximumHealth, 103d);
        True(!NpcHealthCareRecoveryHandoff.TryComplete(store, id, care.Revision - 1,
            NpcArchetype.Citizen, 104d, OutdoorPosition()), "stale care revision cannot cancel task");
        True(!NpcHealthCareRecoveryHandoff.TryComplete(store, id, care.Revision,
            NpcArchetype.Citizen, 104d, null), "no observed actor position cannot create return");
        Equal(NpcHealthCarePhase.Acting, store.ReadNpcHealthCare(id).Phase);
        Equal(NpcTaskPhase.Running, store.ActiveNpcTask(id).Phase);
        True(!store.ActiveNpcAction(id).ReturningToRoutine, "active action remains owned");
    }

    private static WorldStateStore RunningCareAction(out string id)
    {
        WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "care begins");
        NpcHealthCareRecord care = store.ReadNpcHealthCare(id);
        NpcTaskPlan task = Task(NpcHealthCare.MakeTaskId(care), id, 101d);
        AcceptAndBindCare(store, id, care, task, 101d);
        NpcTaskPlan bound = store.ActiveNpcTask(id);
        var action = new NpcActionJob { Id = bound.Id + ":step:0", ProposalId = bound.Id + ":step:0",
            ResidentId = id, Kind = NpcActionKind.MoveTo, Target = bound.Steps[0].Target,
            LastPosition = IndoorPosition(), Phase = NpcActionPhase.Accepted, Revision = 1,
            AcceptedAt = 102d, UpdatedAt = 102d, DeadlineAt = 900d };
        string reason;
        True(store.TryDispatchNpcTaskStep(bound.Id, bound.Revision, 102d, action, true, true, true, out reason),
            "care movement dispatches: " + reason);
        return store;
    }

    private static void PartialTreatmentContinuesImmediately()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "care begins");
        NpcHealthCareRecord care = store.ReadNpcHealthCare(id);
        NpcTaskPlan task = Task(NpcHealthCare.MakeTaskId(care), id, 101d);
        task.Steps.RemoveAt(0);
        AcceptAndBindCare(store, id, care, task, 101d);
        NpcTaskPlan bound = store.ActiveNpcTask(id);
        NpcVitalsView before = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        var action = new NpcActionJob { Id = bound.Id + ":step:0", ProposalId = bound.Id + ":step:0",
            ResidentId = id, Kind = NpcActionKind.RestToHeal, Target = bound.Steps[0].Target,
            LastPosition = IndoorPosition(), Phase = NpcActionPhase.Accepted, Revision = 1,
            AcceptedAt = 102d, UpdatedAt = 102d, DeadlineAt = 900d,
            RestDurationSeconds = 300d, ExpectedVitalsRevision = before.Revision };
        string reason;
        True(store.TryDispatchNpcTaskStep(bound.Id, bound.Revision, 102d, action, true, true, true, out reason),
            "rest care dispatches: " + reason);
        NpcActionJob accepted = store.ReadNpcAction(action.Id);
        True(store.ObserveNpcAction(action.Id, accepted.Revision, 103d, NpcActionObservation.Arrival()),
            "observed arrival starts rest clock");
        NpcActionJob executing = store.ReadNpcAction(action.Id);
        int afterHealth = Math.Min(before.MaximumHealth - 1, before.CurrentHealth + 1);
        True(afterHealth > before.CurrentHealth, "fixture has partial healing headroom");
        CompletePartialRest(store, id, executing, before, afterHealth);
        NpcActionJob completed = store.ReadNpcAction(action.Id);
        True(completed.TreatmentReceipt != null, "native treatment recorded typed receipt");
        True(store.SaveNpcActionPosition(action.Id, completed.Revision, IndoorPosition(), true, 403d),
            "completed treatment preserves return route");
        bound = store.ActiveNpcTask(id);
        True(store.ReconcileNpcTask(bound.Id, bound.Revision, 404d), "verified treatment completes task");
        Equal(NpcTaskPhase.Completed, store.ReadNpcTask(task.Id).Phase);
        care = store.ReadNpcHealthCare(id);

        True(store.TryContinueNpcHealthCareAfterTreatment(id, care.Revision, NpcArchetype.Citizen,
            405d, IndoorPosition()), "partial verified treatment can replan now");
        NpcHealthCareRecord seeking = store.ReadNpcHealthCare(id);
        Equal(NpcHealthCarePhase.Seeking, seeking.Phase);
        Equal(0d, seeking.RetryAt);
        True(seeking.TaskId == null, "prior task releases care ownership");
        True(seeking.Revision > care.Revision, "new attempt gets distinct task identity");
        True(NpcHealthCare.MakeTaskId(seeking) != task.Id, "next treatment cannot replay old task");
        True(store.ActiveNpcAction(id).ReturningToRoutine, "return survives until new route supersedes it");
        True(!store.TryContinueNpcHealthCareAfterTreatment(id, care.Revision, NpcArchetype.Citizen,
            406d, IndoorPosition()), "old completed task cannot continue twice");

        var reloaded = new WorldStateStore(0);
        reloaded.Restore(FullSerializerRoundtrip(store.Snapshot()));
        Equal(NpcHealthCarePhase.Seeking, reloaded.ReadNpcHealthCare(id).Phase);
        Equal(NpcHealthCare.MakeTaskId(seeking), NpcHealthCare.MakeTaskId(reloaded.ReadNpcHealthCare(id)));
    }

    private static void MagicHealingUsesBoundCareTask()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryReserveAppointment(new AppointmentRecord {Id="magic-care-meeting",ResidentId=id,
            Purpose="meet",StartsAt=103d,EndsAt=200d,GraceSeconds=30,Importance=1,
            Place=new AppointmentPlace {Kind="building",SettlementId=Settlement,BuildingKey=Home}},
            96d,out _),"accepted appointment overlaps urgent recovery");
        NpcVitalsView threshold=store.ReadNpcVitals(id,NpcArchetype.Citizen);
        CaptureHealth(store,id,threshold,Math.Max(1,threshold.MaximumHealth/2),100d);
        True(NpcHealthNeedLifecycle.Observe(store, id, NpcArchetype.Citizen, 100d), "native injury opens recovery goal");
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "care begins");
        NpcHealthCareRecord care = store.ReadNpcHealthCare(id);
        NpcVitalsView before = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        var spec = new NpcMagicHealingSpec { SpellId = "native:spellbook:0:abc", EffectId = "Heal-Health",
            MagickaCost = 1, MagickaBefore = before.CurrentMagicka, HealthBefore = before.CurrentHealth };
        var task = new NpcTaskPlan { Id = NpcHealthCare.MakeTaskId(care), ProposalId = NpcHealthCare.MakeTaskId(care),
            ResidentId = id, AcceptedAt = 101d, UpdatedAt = 101d, DeadlineAt = 700d };
        foreach(var goal in store.ReadNpcMotivationState(id).Goals)
            if(goal.Id==NpcHealthNeedLifecycle.RecoveryGoalId)
            {task.GoalId=goal.Id;task.GoalStartRevision=goal.GoalStartRevision;break;}
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.CastSpell, Target = IndoorPosition(), MagicHealing = spec });
        task.GoalStartRevision++;
        True(!store.TryAcceptAndBindNpcHealthCareTask(id,care.Revision,task,101d,out _),
            "wrong recovery-goal generation cannot admit spell task");
        task.GoalStartRevision--;
        AcceptAndBindCare(store, id, care, task, 101d);
        NpcTaskPlan bound = store.ActiveNpcTask(id);
        True(bound.Steps[0].MagicHealing.SpellId == spec.SpellId, "bound task retains exact spell");
        var action=new NpcActionJob { Id=bound.Id+":step:0",ProposalId=bound.Id+":step:0",ResidentId=id,
            Kind=NpcActionKind.CastSpell,Target=IndoorPosition(),MagicHealing=spec,
            Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=102d,UpdatedAt=102d,DeadlineAt=700d };
        True(store.TryDispatchNpcTaskStep(bound.Id,bound.Revision,102d,action,true,true,true,out string reason),
            "exact magic healing task dispatches: "+reason);
        action=store.ActiveNpcAction(id);
        True(!store.ObserveNpcAction(action.Id,action.Revision,104d,
            NpcActionObservation.Success(action.Id+":observed")),
            "generic action success cannot complete self-healing spell");
        True(store.ObserveNpcAction(action.Id,action.Revision,103d,NpcActionObservation.Arrival()),
            "observed self-cast position starts execution");
        action=store.ActiveNpcAction(id);
        var native=new NpcMagicObservedOutcome { ActorId=id,TargetId=id,SpellId=spec.SpellId,
            EffectId=spec.EffectId,EffectKind=NpcMagicEffectKind.Health,NativeAdapterExecuted=true,
            MagickaBefore=spec.MagickaBefore,MagickaAfter=spec.MagickaBefore-spec.MagickaCost,
            EffectValueBefore=spec.HealthBefore,EffectValueAfter=Math.Min(before.MaximumHealth-1,spec.HealthBefore+1) };
        True(native.EffectValueAfter>native.EffectValueBefore,"fixture has partial healing headroom");
        MethodInfo method=typeof(WorldStateStore).GetMethod("TryCompleteNpcMagicHealingAction",
            BindingFlags.Instance|BindingFlags.NonPublic);
        True(method!=null,"trusted magic healing commit exists");
        int calls=0;
        Func<NpcMagicObservedOutcome> callback=()=>{calls++;return native;};
        True(!(bool)method.Invoke(store,new object[] {action.Id,action.Revision-1,104d,
            NpcArchetype.Citizen,before.Revision,callback}) && calls==0,
            "stale action revision cannot call native effect");
        var unverified=new NpcMagicObservedOutcome { ActorId=id,TargetId=id,SpellId=spec.SpellId,
            EffectId=spec.EffectId,EffectKind=NpcMagicEffectKind.Health,NativeAdapterExecuted=false,
            MagickaBefore=spec.MagickaBefore,MagickaAfter=spec.MagickaBefore-spec.MagickaCost,
            EffectValueBefore=spec.HealthBefore,EffectValueAfter=native.EffectValueAfter };
        True(!(bool)method.Invoke(store,new object[] {action.Id,action.Revision,104d,
            NpcArchetype.Citizen,before.Revision,new Func<NpcMagicObservedOutcome>(()=>unverified)}),
            "unverified native cast cannot commit");
        Equal(before.Revision,store.ReadNpcVitals(id,NpcArchetype.Citizen).Revision);
        True(store.ReadNpcAction(action.Id).TreatmentReceipt==null,
            "failed native observation creates no receipt");
        True((bool)method.Invoke(store,new object[] {action.Id,action.Revision,104d,
            NpcArchetype.Citizen,before.Revision,callback}) && calls==1,
            "observed native self-heal commits once");
        NpcActionJob completed=store.ReadNpcAction(action.Id);
        True(completed.Phase==NpcActionPhase.Completed && completed.TreatmentReceipt.Kind==NpcTreatmentKind.MagicSelfCast &&
            completed.TreatmentReceipt.SpellId==spec.SpellId,"cast keeps exact receipt");
        True(NpcActionFeedback.Describe(completed,"Rhea",null,null).Contains("healing spell"),
            "HUD reports only receipted healing spell success");
        Equal(native.EffectValueAfter,store.ReadNpcVitals(id,NpcArchetype.Citizen).CurrentHealth);
        Equal(native.MagickaAfter,store.ReadNpcVitals(id,NpcArchetype.Citizen).CurrentMagicka);
        True(!(bool)method.Invoke(store,new object[] {action.Id,action.Revision,105d,
            NpcArchetype.Citizen,before.Revision,callback}) && calls==1,"replay cannot call native effect");
        True(store.TryProjectNpcTreatmentOutcome(action.Id,completed.Revision),
            "magic receipt enters bounded treatment history");
        NpcTreatmentOutcome retained=store.Snapshot().NpcTreatmentOutcomeHistories[0].Outcomes[0];
        Equal(NpcTreatmentKind.MagicSelfCast,retained.Kind);
        Equal(spec.SpellId,retained.SpellId);
        Equal(spec.MagickaCost,retained.MagickaCost);
        var reloaded=new WorldStateStore(0);
        reloaded.Restore(FullSerializerRoundtrip(store.Snapshot()));
        Equal(spec.SpellId,reloaded.Snapshot().NpcTreatmentOutcomeHistories[0].Outcomes[0].SpellId);
        MethodInfo recallMethod=typeof(WorldStateStore).GetMethod("CaptureNpcTreatmentRecall",
            BindingFlags.Instance|BindingFlags.NonPublic);
        object recall=recallMethod.Invoke(reloaded,new object[] {id,"How did you heal with magic?",105d,500});
        string recallText=(string)recall.GetType().GetField("Text",BindingFlags.Instance|BindingFlags.NonPublic)
            .GetValue(recall);
        True(recallText.Contains("Heal-Health") && recallText.Contains("magicka"),
            "retained treatment recalls observed spell and magicka cost");
        WorldState alteredSpec=store.Snapshot();
        alteredSpec.NpcActions[0].MagicHealing.SpellId="different-spell";
        Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(alteredSpec));
        WorldState alteredHistory=store.Snapshot();
        alteredHistory.NpcTreatmentOutcomeHistories[0].Outcomes[0].MagickaAfter++;
        Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(alteredHistory));
        WorldState oldVersion=store.Snapshot();oldVersion.Version=58;
        Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(oldVersion));
        completed=store.ReadNpcAction(action.Id);
        True(store.SaveNpcActionPosition(action.Id,completed.Revision,IndoorPosition(),true,105d),
            "cast completion records return position");
        bound=store.ActiveNpcTask(id);
        True(store.ReconcileNpcTask(bound.Id,bound.Revision,106d),"receipted cast completes care task");
        care=store.ReadNpcHealthCare(id);
        True(store.TryContinueNpcHealthCareAfterTreatment(id,care.Revision,NpcArchetype.Citizen,
            107d,IndoorPosition()),"partial magic heal promptly reopens same care episode");
        True(NpcHealthCare.MakeTaskId(store.ReadNpcHealthCare(id))!=task.Id,
            "next partial-healing attempt gets distinct task identity");
    }

    private static void CompletePartialRest(WorldStateStore store, string id, NpcActionJob action,
        NpcVitalsView before, int afterHealth)
    {
        Type outcomeType = typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcTreatmentRestNativeOutcome");
        True(outcomeType != null, "typed native rest outcome exists");
        MethodInfo factory = outcomeType.GetMethod("Verified", BindingFlags.Static | BindingFlags.NonPublic);
        True(factory != null, "typed native rest factory exists");
        object outcome = factory.Invoke(null, new object[] { before.CurrentHealth, afterHealth, before.CurrentMagicka });
        Type callbackType = typeof(Func<>).MakeGenericType(outcomeType);
        Delegate callback = Expression.Lambda(callbackType, Expression.Constant(outcome, outcomeType)).Compile();
        MethodInfo method = typeof(WorldStateStore).GetMethod("TryCompleteNpcRestAction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        True(method != null, "typed native rest commit exists");
        bool completed = (bool)method.Invoke(store, new object[] { action.Id, action.Revision, 403d,
            NpcArchetype.Citizen, before.Revision, callback });
        True(completed, "partial native rest commits real health and receipt");
        Equal(afterHealth, store.ReadNpcVitals(id, NpcArchetype.Citizen).CurrentHealth);
    }

    private static void CancelledTreatmentKeepsBackoff()
    {
        string id; WorldStateStore store = RunningCareAction(out id);
        NpcTaskPlan running = store.ActiveNpcTask(id);
        True(store.CancelNpcTask(running.Id, running.Revision, 103d, "Native treatment unavailable."),
            "failed care task cancels");
        NpcHealthCareRecord care = store.ReadNpcHealthCare(id);
        True(!store.TryContinueNpcHealthCareAfterTreatment(id, care.Revision,
            NpcArchetype.Citizen, 104d, OutdoorPosition()), "no receipt cannot fast replan");
        True(store.TryBlockNpcHealthCare(id, care.Revision, 104d, "Native treatment unavailable."),
            "unverified treatment enters ordinary retry backoff");
        Equal(NpcHealthCarePhase.Blocked, store.ReadNpcHealthCare(id).Phase);
        Equal(104d + NpcHealthCare.RetryDelaySeconds, store.ReadNpcHealthCare(id).RetryAt);
    }

    private static void FullSerializerAndLegacyMigration()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, OutdoorPosition()), "care starts");
        WorldState restoredWire = FullSerializerRoundtrip(store.Snapshot());
        var restored = new WorldStateStore(0);
        restored.Restore(restoredWire);
        Equal(WorldState.CurrentVersion, restored.Snapshot().Version);
        Equal(NpcHealthCarePhase.Seeking, restored.ReadNpcHealthCare(id).Phase);
        Equal("settlement:11", restored.ReadNpcHealthCare(id).PausedPosition.WorldSpaceId);

        WorldState legacy = store.Snapshot();
        legacy.Version = 43;
        legacy.NpcHealthCares = null;
        // This fixture represents an actual schema-43 payload. A current snapshot
        // carries the schema-54 v5 inbox header even when this test does not care
        // about experience rows; relabelling that header as v43 must fail closed.
        legacy.ConversationCueArchive = null;
        legacy.NpcExperienceInbox = NpcExperienceInbox.Create();
        legacy.NpcExperienceInbox.Version = 4;
        WorldState legacyWire = FullSerializerRoundtrip(legacy);
        var migrated = new WorldStateStore(0);
        migrated.Restore(legacyWire);
        True(migrated.ReadNpcHealthCare(id) == null, "schema 43 loads with no care rows");
        Equal(WorldState.CurrentVersion, migrated.Snapshot().Version);
    }

    private static void RestoreRejectsCorruptCareRows()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "care starts");
        WorldState duplicate = store.Snapshot();
        duplicate.NpcHealthCares.Add(duplicate.NpcHealthCares[0]);
        Throws<ArgumentException>(() => new WorldStateStore(0).Restore(duplicate));

        WorldState dynamicTarget = store.Snapshot();
        dynamicTarget.NpcHealthCares[0].PausedPosition = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" };
        Throws<ArgumentException>(() => new WorldStateStore(0).Restore(dynamicTarget));
    }

    private static void PausedPointsRejectDynamicMetadata()
    {
        string id; WorldStateStore store = Create(out id);
        Injure(store, id, 100d, 90d);
        True(store.TryBeginNpcHealthCare(id, NpcArchetype.Citizen, 100d, IndoorPosition()), "care starts");

        WorldState itemTarget = store.Snapshot();
        itemTarget.NpcHealthCares[0].PausedPosition.ItemId = "123";
        Throws<ArgumentException>(() => new WorldStateStore(0).Restore(itemTarget));

        WorldState personTarget = store.Snapshot();
        personTarget.NpcHealthCares[0].PausedPosition.PersonId = "player";
        Throws<ArgumentException>(() => new WorldStateStore(0).Restore(personTarget));
    }

    private static WorldStateStore Create(out string id)
    {
        var store = new WorldStateStore(9042);
        id = store.GetOrCreate(Settlement, new[] { Home }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static string CreateSecondResident(WorldStateStore store)
    {
        var population = store.GetOrCreate(Settlement + 1, new[] { Home }, 1);
        return population.Households[0].Residents[0].Id;
    }

    private static NpcVitalsView Injure(WorldStateStore store, string id, double capturedAt, double now)
    {
        NpcVitalsView baseline = store.ReadNpcVitals(id, NpcArchetype.Citizen);
        int injured = Math.Max(1, (int)((long)baseline.MaximumHealth * 3 / 5));
        CaptureHealth(store, id, baseline, injured, capturedAt);
        return store.ReadNpcVitals(id, NpcArchetype.Citizen);
    }

    private static void CaptureHealth(WorldStateStore store, string id, NpcVitalsView previous, int health, double now)
    {
        True(store.TryCaptureNpcVitals(id, NpcArchetype.Citizen, health, previous.CurrentMagicka, now, previous.Revision),
            "fixture captures observed native health");
    }

    private static NpcActionTarget IndoorPosition()
    {
        return new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = Settlement,
            BuildingKey = Home, X = 1.25, Y = 0, Z = -2.5 };
    }

    private static NpcActionTarget OutdoorPosition()
    {
        return new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint, SettlementId = Settlement,
            BuildingKey = -1, WorldSpaceId = "settlement:11", X = 5, Y = 0, Z = -3 };
    }

    private static NpcTaskPlan Task(string taskId, string residentId, double now)
    {
        var task = new NpcTaskPlan { Id = taskId, ProposalId = taskId, ResidentId = residentId,
            AcceptedAt = now, UpdatedAt = now, DeadlineAt = now + 900 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.MoveTo,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint, WorldSpaceId = "settlement:11",
                SettlementId = Settlement, BuildingKey = -1, X = 3, Y = 0, Z = 4 } });
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.RestToHeal, DurationSeconds = 300,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = Settlement,
                BuildingKey = Home, ContainerId = "bed:health-care:1", X = 1.25, Y = 0, Z = -2.5 } });
        return task;
    }

    private static NpcTaskPlan PurchasedCareTask(string taskId, string residentId, double now)
    {
        NpcTaskPlan task = Task(taskId, residentId, now);
        task.Steps.Clear();
        var position = IndoorPosition();
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.MoveTo, Target = position });
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Purchase, Target = new NpcActionTarget
        {
            Kind = NpcActionTargetKind.IndoorPoint, SettlementId = Settlement, BuildingKey = Home,
            PersonId = "npc:care-provider", ContainerId = "700", ItemId = "44",
            Quantity = 1, QuotedGold = 9, X = position.X, Y = position.Y, Z = position.Z
        } });
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.UseHealingItem, ItemSourceStep = 2,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = Settlement,
                BuildingKey = Home, ItemId = "44", Quantity = 1, X = position.X, Y = position.Y, Z = position.Z } });
        return task;
    }

    private static void AcceptAndBindCare(WorldStateStore store, string residentId,
        NpcHealthCareRecord care, NpcTaskPlan task, double now)
    {
        string reason;
        string kinds = task == null || task.Steps == null ? "none" : string.Join(",", task.Steps.ConvertAll(step => step == null ? "null" : step.Kind.ToString()));
        True(store.TryAcceptAndBindNpcHealthCareTask(residentId, care.Revision, task, now, out reason),
            "care task fixture accepted and bound (" + kinds + "): " + reason);
    }

    private static WorldState FullSerializerRoundtrip(WorldState state)
    {
        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { IncludeFields = true });
        fsData wire = fsJsonParser.Parse(json);
        var serializer = new fsSerializer();
        WorldState result = null;
        fsResult deserialized = serializer.TryDeserialize(wire, ref result);
        True(deserialized.Succeeded, "FullSerializer deserializes world state");
        True(result != null, "FullSerializer returns a world state");
        return result;
    }

    private static void True(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }
}
