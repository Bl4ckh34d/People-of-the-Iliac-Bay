using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const string Person = "npc:goal-pilot";

    private static int Main()
    {
        var tests = new List<(string, Action)>
        {
            ("recorded Important recovery defers to active appointment", ImportantRecoveryDefersToAppointment),
            ("recorded Urgent recovery can interrupt ordinary appointment task", UrgentRecoveryInterruptsAppointment),
            ("native alarm owns movement even if Safety goal write is absent", AlarmOwnsWithoutSavedGoal),
            ("unavailable treatment cannot interrupt an occupied task", NoFeasibleTreatmentKeepsTask),
            ("unrecorded recovery goal cannot start care", MissingRecoveryGoalCannotStartCare),
            ("ongoing care task keeps the single task owner", OngoingCareKeepsTask),
            ("protected service duty keeps current task", ServiceDutyKeepsTask),
            ("urgent recovery interrupts paid service only after source pause preflight", UrgentRecoveryInterruptsReadyService),
            ("important recovery cannot start a new unpaid service interruption", ImportantRecoveryDefersReadyService),
            ("negotiated paid rest admits important recovery without suspension", ImportantRecoveryUsesPaidRest),
            ("admitted paid-rest care can switch to potion after priority drops", AdmittedCareCanSuspendAfterPaidRest),
            ("emergency recovery interrupts ordinary work task", EmergencyInterruptsOrdinaryWork),
            ("different resident task cannot enter owner arbitration", OtherResidentTaskRejected),
            ("stale care generation is not relabeled with latest recovery goal", StaleCareGenerationNotRelabeled),
            ("deferred care does not steal schedule movement", DeferredCareLeavesRoutine),
            ("established recovery continues above interruption threshold", EstablishedRecoveryContinuesAboveThreshold),
            ("fresh mild injury does not interrupt routine", FreshMildInjuryDoesNotInterrupt),
            ("same saved input produces same selected generation", DeterministicSavedSelection),
            ("home-first policy requires proven access and feasible provider", HomeFirstDestinationPolicy),
            ("future needs map to distinct external providers without effects", TypedDestinationMapping),
        };
        int failed = 0;
        foreach (var test in tests)
        {
            try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Item1 + ": " + error.Message); }
        }
        Console.WriteLine((tests.Count - failed) + "/" + tests.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void ImportantRecoveryDefersToAppointment()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Important));
        input.HealthEligible = true;
        input.HealthTaskFeasible = true;
        input.ActiveAppointment = true;
        input.ActiveTask = Task("task:appointment");
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.KeepTask, result.Disposition);
        Equal(NpcGoalMovementOwner.ExistingTask, result.MovementOwner);
    }

    private static void UrgentRecoveryInterruptsAppointment()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Urgent));
        input.HealthEligible = true;
        input.HealthTaskFeasible = true;
        input.ActiveAppointment = true;
        input.ActiveTask = Task("task:appointment");
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.InterruptTask, result.Disposition);
        Equal(NpcHealthNeedLifecycle.RecoveryGoalId, result.GoalId);
        Equal(1L, result.GoalStartRevision);
    }

    private static void AlarmOwnsWithoutSavedGoal()
    {
        var input = Input(NpcMotivationState.Create(Person).ReadOnlyView());
        input.NativeAlarmActive = true;
        input.ActiveTask = Task("task:work");
        input.ActiveTask.Phase = NpcTaskPhase.Ready;
        input.ActiveServiceDuty = true;
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.NativeOwner, result.Disposition);
        Equal(NpcGoalMovementOwner.NativeAlarm, result.MovementOwner);
        Equal(null, result.GoalId);
    }

    private static void NoFeasibleTreatmentKeepsTask()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Emergency));
        input.HealthEligible = true;
        input.ActiveTask = Task("task:work");
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.KeepTask, result.Disposition);
    }

    private static void MissingRecoveryGoalCannotStartCare()
    {
        var input = Input(NpcMotivationState.Create(Person).ReadOnlyView());
        input.HealthEligible = true;
        input.HealthTaskFeasible = true;
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.OrdinaryRoutine, result.Disposition);
    }

    private static void OngoingCareKeepsTask()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Emergency));
        input.HealthEligible = true;
        input.HealthTaskFeasible = true;
        input.ActiveTask = Task("task:care");
        input.ActiveTaskIsCare = true;
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.KeepTask, result.Disposition);
        Equal(NpcGoalMovementOwner.ExistingTask, result.MovementOwner);
    }

    private static void ServiceDutyKeepsTask()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Emergency));
        input.HealthEligible = true;
        input.HealthTaskFeasible = true;
        input.ActiveServiceDuty = true;
        input.ActiveTask = Task("task:service");
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.KeepTask, result.Disposition);
    }

    private static void UrgentRecoveryInterruptsReadyService()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Urgent), true, true);
        input.ActiveServiceDuty = true;
        input.ServiceHealthAdmission = NpcGoalServiceHealthAdmission.RequiresPause;
        input.ActiveTask = Task("task:service");
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.InterruptTask, result.Disposition);
        Equal(NpcGoalMovementOwner.ExistingTask, result.MovementOwner);
    }

    private static void ImportantRecoveryDefersReadyService()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Important), true, true);
        input.ActiveServiceDuty = true;
        input.ServiceHealthAdmission = NpcGoalServiceHealthAdmission.RequiresPause;
        input.ActiveTask = Task("task:service");
        Equal(NpcGoalWorkDisposition.KeepTask, NpcGoalArbiter.Select(input).Disposition);
    }

    private static void ImportantRecoveryUsesPaidRest()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Important), true, true);
        input.ActiveServiceDuty = true;
        input.ServiceHealthAdmission = NpcGoalServiceHealthAdmission.PaidRest;
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.BeginGoalTask, result.Disposition);
        Equal(NpcGoalMovementOwner.HealthCare, result.MovementOwner);
    }

    private static void AdmittedCareCanSuspendAfterPaidRest()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Important), true, true);
        input.ActiveServiceDuty = true;
        input.ServiceHealthAdmission = NpcGoalServiceHealthAdmission.RequiresPause;
        input.CareTreatmentPreviouslyAdmitted = true;
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.BeginGoalTask, result.Disposition);
        Equal(NpcGoalMovementOwner.HealthCare, result.MovementOwner);
    }

    private static void EmergencyInterruptsOrdinaryWork()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Emergency));
        input.HealthEligible = true;
        input.HealthTaskFeasible = true;
        input.ActiveTask = Task("task:ordinary-work");
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.InterruptTask, result.Disposition);
    }

    private static void OtherResidentTaskRejected()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Emergency));
        input.ActiveTask = Task("task:other");
        input.ActiveTask.ResidentId = "npc:other";
        bool rejected = false;
        try { NpcGoalArbiter.Select(input); }
        catch (ArgumentException) { rejected = true; }
        Equal(true, rejected);
    }

    private static void StaleCareGenerationNotRelabeled()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Emergency));
        input.ActiveTask = Task("task:old-care");
        input.ActiveTask.GoalId = NpcHealthNeedLifecycle.RecoveryGoalId;
        input.ActiveTask.GoalStartRevision = 99;
        input.ActiveTaskIsCare = true;
        input.HealthEligible = true;
        input.HealthTaskFeasible = true;
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.KeepTask, result.Disposition);
        Equal(null, result.GoalId);
        Equal(0L, result.GoalStartRevision);
    }

    private static void DeferredCareLeavesRoutine()
    {
        var input = Input(Goal(NpcMotivationPriorityLevel.Important));
        input.HealthEligible = true;
        input.CareOwnsRoutine = true;
        var result = NpcGoalArbiter.Select(input);
        Equal(NpcGoalWorkDisposition.DeferGoal, result.Disposition);
        Equal(NpcGoalMovementOwner.OrdinaryRoutine, result.MovementOwner);
    }

    private static void EstablishedRecoveryContinuesAboveThreshold()
    {
        var vitals = MildlyInjuredVitals();
        Equal(false, NpcHealthNeedLifecycle.ShouldInterruptWork(vitals));
        foreach (var phase in new[] { NpcHealthCarePhase.Seeking, NpcHealthCarePhase.Blocked })
        {
            var care = new NpcHealthCareRecord { Phase = phase };
            var input = Input(Goal(NpcMotivationPriorityLevel.Important));
            input.HealthEligible = NpcGoalArbiter.HealthEligibleForArbitration(vitals, care);
            input.HealthTaskFeasible = true;
            Equal(NpcGoalWorkDisposition.BeginGoalTask, NpcGoalArbiter.Select(input).Disposition);
        }
    }

    private static void FreshMildInjuryDoesNotInterrupt()
    {
        var vitals = MildlyInjuredVitals();
        var input = Input(Goal(NpcMotivationPriorityLevel.Important));
        input.HealthEligible = NpcGoalArbiter.HealthEligibleForArbitration(vitals, null);
        input.HealthTaskFeasible = true;
        input.ActiveTask = Task("task:ordinary-work");
        Equal(NpcGoalWorkDisposition.KeepTask, NpcGoalArbiter.Select(input).Disposition);
    }

    private static NpcVitalsView MildlyInjuredVitals()
    {
        const string resident = "s:10:h:20:r:0";
        var store = new WorldStateStore(42);
        store.GetOrCreate(10, new[] { 20 }, 1);
        var baseline = store.ReadNpcVitals(resident, NpcArchetype.Citizen);
        var health = Math.Min(baseline.MaximumHealth - 1, (int)Math.Ceiling(baseline.MaximumHealth * 0.8));
        if (health <= baseline.MaximumHealth * 0.75) throw new Exception("test fixture needs a mild injury above 75%");
        if (!store.TryCaptureNpcVitals(resident, NpcArchetype.Citizen, health,
                baseline.CurrentMagicka, 10, baseline.Revision)) throw new Exception("vitals setup failed");
        return store.ReadNpcVitals(resident, NpcArchetype.Citizen);
    }

    private static void DeterministicSavedSelection()
    {
        var saved = Goal(NpcMotivationPriorityLevel.Urgent);
        var input = Input(saved);
        input.HealthEligible = true;
        input.HealthTaskFeasible = true;
        var first = NpcGoalArbiter.Select(input);
        var again = NpcGoalArbiter.Select(Input(saved, true, true));
        Equal(first.Disposition, again.Disposition);
        Equal(first.GoalId, again.GoalId);
        Equal(first.GoalStartRevision, again.GoalStartRevision);
    }

    private static void HomeFirstDestinationPolicy()
    {
        Equal(NpcNeedDestinationKind.Home, NpcNeedDestinationPolicy.Select(
            NpcNeedPurpose.Illness, NpcHomeAccess.Reachable, true, true));
        Equal(NpcNeedDestinationKind.None, NpcNeedDestinationPolicy.Select(
            NpcNeedPurpose.Illness, NpcHomeAccess.Reachable, false, true));
        Equal(NpcNeedDestinationKind.None, NpcNeedDestinationPolicy.Select(
            NpcNeedPurpose.Illness, NpcHomeAccess.Unknown, true, true));
        Equal(NpcNeedDestinationKind.None, NpcNeedDestinationPolicy.Select(
            NpcNeedPurpose.Illness, NpcHomeAccess.Unreachable, true, false));
        Equal(NpcNeedDestinationKind.None, NpcNeedDestinationPolicy.Select(
            NpcNeedPurpose.Illness, NpcHomeAccess.Unreachable, true, true));
    }

    private static void TypedDestinationMapping()
    {
        Equal(NpcNeedDestinationKind.None, NpcNeedDestinationPolicy.Select(
            (NpcNeedPurpose)1, NpcHomeAccess.Unreachable, false, true));
        Equal(NpcNeedDestinationKind.Temple, NpcNeedDestinationPolicy.Select(
            NpcNeedPurpose.Illness, NpcHomeAccess.Unreachable, false, true));
        Equal(NpcNeedDestinationKind.Guild, NpcNeedDestinationPolicy.Select(
            NpcNeedPurpose.PotionSupply, NpcHomeAccess.Unreachable, false, true));
        Equal(NpcNeedDestinationKind.Bank, NpcNeedDestinationPolicy.Select(
            NpcNeedPurpose.Banking, NpcHomeAccess.Unreachable, false, true));
    }

    private static NpcGoalArbitrationInput Input(NpcMotivationStateView state, bool eligible = false, bool feasible = false)
    {
        return new NpcGoalArbitrationInput { PersonId = Person, Motivation = state,
            EligibleResident = true, HealthEligible = eligible, HealthTaskFeasible = feasible };
    }

    private static NpcMotivationStateView Goal(NpcMotivationPriorityLevel priority)
    {
        var state = NpcMotivationState.Create(Person);
        var evidence = new NpcMotivationStateEvidence(Person, "vitals:1",
            NpcMotivationStateEvidenceKind.NativeObservation, 1, 10);
        var proposal = NpcMotivationStateUpdateProposal.ForGoal(Person, 0, "observe:1", 10,
            NpcHealthNeedLifecycle.RecoveryGoalId, NpcMotivationGoalKind.Recovery,
            NpcMotivationGoalStatus.Active, priority, NpcMotivationKnowledgeState.Known, evidence);
        NpcMotivationState next;
        if (!NpcMotivationState.TryApply(state, proposal, out next)) throw new Exception("test goal setup failed");
        return next.ReadOnlyView();
    }

    private static NpcTaskPlan Task(string id)
    {
        return new NpcTaskPlan { Id = id, ResidentId = Person, Phase = NpcTaskPhase.Running };
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception("expected " + expected + ", got " + actual);
    }
}
