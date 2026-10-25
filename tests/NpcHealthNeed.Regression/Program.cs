using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const string Owner = "s:10:h:20:r:0";
    private const int Settlement = 10;
    private const int Home = 20;
    private const NpcArchetype Archetype = NpcArchetype.Citizen;

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("healthy residents do not gain recovery motivation", HealthyResidentStaysUnchanged),
            ("saved injury creates source-bound health need and recovery goal", InjuryCreatesNeedAndGoal),
            ("repeated observation does not append motivation receipts", RepeatedObservationIsIdempotent),
            ("injury severity updates priority and work interruption threshold", SeverityAndWorkThresholds),
            ("full healing clears pressure and fulfills stable recovery goal", HealingFulfillsAndReinjuryReopens),
            ("unknown dead child and quest-protected identities cannot write", IneligibleResidentsDoNotWrite),
            ("clock regressions and future vitals are rejected", TimeRegressionsAreRejected),
            ("unrelated motivation changes do not starve a later health update", SharedLedgerChangesDoNotStarve),
            ("save reload preserves injury state without replay", SaveReloadDoesNotReplay)
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

    private static void HealthyResidentStaysUnchanged()
    {
        var store = NewStore();
        True(!NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 100), "full health must not create recovery pressure");
        NpcMotivationStateView state = store.ReadNpcMotivationState(Owner);
        Equal(0, state.Needs.Count);
        Equal(0, state.Goals.Count);
        Equal(0, store.Snapshot().NpcMotivations.Count);
    }

    private static void InjuryCreatesNeedAndGoal()
    {
        var store = NewStore();
        NpcVitalsView injured = CaptureHealth(store, HalfHealth(store), 100);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 100), "saved injury should update motivation");

        NpcMotivationStateView state = store.ReadNpcMotivationState(Owner);
        Equal(1, state.Needs.Count);
        Equal(NpcHealthNeedLifecycle.HealthNeedId, state.Needs[0].Id);
        Equal(NpcMotivationNeedKind.Health, state.Needs[0].Kind);
        Equal(NpcMotivationKnowledgeState.Known, state.Needs[0].KnowledgeState);
        Equal(ExpectedIntensity(injured.MaximumHealth, injured.CurrentHealth), state.Needs[0].Intensity.Value);
        Equal(NpcMotivationStateEvidenceKind.NativeObservation, state.Needs[0].EvidenceKind.Value);
        True(state.Needs[0].EvidenceId.Contains("native-health:v1:need:" + injured.Revision + ":", StringComparison.Ordinal),
            "need evidence must bind to current native vitals revision");

        Equal(1, state.Goals.Count);
        Equal(NpcHealthNeedLifecycle.RecoveryGoalId, state.Goals[0].Id);
        Equal(NpcMotivationGoalKind.Recovery, state.Goals[0].Kind);
        Equal(NpcMotivationGoalStatus.Active, state.Goals[0].Status);
        Equal(NpcMotivationPriorityLevel.Urgent, state.Goals[0].Priority);
        Equal(NpcMotivationKnowledgeState.Known, state.Goals[0].KnowledgeState);
        Equal(NpcMotivationStateEvidenceKind.NativeObservation, state.Goals[0].EvidenceKind.Value);
        Equal(2, state.AcceptedUpdates.Count);
    }

    private static void RepeatedObservationIsIdempotent()
    {
        var store = NewStore();
        CaptureHealth(store, HalfHealth(store), 100);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 100), "first observation should commit");
        True(!NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 130), "same vitals must be a no-op at later time");
        NpcMotivationStateView state = store.ReadNpcMotivationState(Owner);
        Equal(2L, state.Revision);
        Equal(2, state.AcceptedUpdates.Count);
    }

    private static void SeverityAndWorkThresholds()
    {
        var store = NewStore();
        NpcVitalsView vitals = store.ReadNpcVitals(Owner, Archetype);
        int max = vitals.MaximumHealth;
        int workThreshold = (int)((2L * max - 1) / 3);

        vitals = CaptureHealth(store, workThreshold + 1, 100);
        True(!NpcHealthNeedLifecycle.ShouldInterruptWork(vitals), "health at or above two thirds should allow work");
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 100), "mild injury should create recovery goal");
        Equal(NpcMotivationPriorityLevel.Important, Recovery(store).Priority);

        vitals = CaptureHealth(store, workThreshold, 110);
        True(NpcHealthNeedLifecycle.ShouldInterruptWork(vitals), "health below two thirds should interrupt work");

        vitals = CaptureHealth(store, max / 2, 120);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 120), "half health should refresh severity");
        Equal(NpcMotivationPriorityLevel.Urgent, Recovery(store).Priority);

        vitals = CaptureHealth(store, max / 4, 130);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 130), "quarter health should refresh severity");
        Equal(NpcMotivationPriorityLevel.Emergency, Recovery(store).Priority);

        True(!NpcHealthNeedLifecycle.ShouldInterruptWork(null), "unknown vitals must not interrupt work");
        var deadStore = NewStore();
        True(Population.MarkDead(deadStore.GetOrCreate(Settlement, new[] { Home }, 1), Owner), "fixture should die");
        True(!NpcHealthNeedLifecycle.ShouldInterruptWork(DeadView(deadStore)),
            "dead vitals must not trigger work interruption");
    }

    private static void HealingFulfillsAndReinjuryReopens()
    {
        var store = NewStore();
        NpcVitalsView injured = CaptureHealth(store, HalfHealth(store), 100);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 100), "injury should start recovery");
        string firstEvidence = Recovery(store).EvidenceId;

        NpcVitalsView partial = CaptureHealth(store, injured.MaximumHealth / 2 + 1, 110);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 110), "partial healing should reduce pressure");
        True(HealthNeed(store).Intensity.Value < 50, "partial healing should reduce health need intensity");
        Equal(NpcMotivationGoalStatus.Active, Recovery(store).Status);

        NpcVitalsView full = CaptureHealth(store, partial.MaximumHealth, 120);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 120), "full healing should complete recovery");
        Equal(0, HealthNeed(store).Intensity.Value);
        Equal(NpcMotivationGoalStatus.Fulfilled, Recovery(store).Status);
        long fulfilledRevision = store.ReadNpcMotivationState(Owner).Revision;
        True(!NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 121), "full health must not replay completion");
        Equal(fulfilledRevision, store.ReadNpcMotivationState(Owner).Revision);

        NpcVitalsView again = CaptureHealth(store, full.MaximumHealth / 2, 130);
        True(again.Revision > full.Revision, "later injury must have a newer native vitals revision");
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 130), "new injury should reopen recovery");
        Equal(NpcHealthNeedLifecycle.RecoveryGoalId, Recovery(store).Id);
        Equal(NpcMotivationGoalStatus.Active, Recovery(store).Status);
        True(!string.Equals(firstEvidence, Recovery(store).EvidenceId, StringComparison.Ordinal),
            "new injury must bind goal to new vitals evidence");
        True(HealthNeed(store).Intensity.Value > 0, "reinjury must restore health pressure");
    }

    private static void IneligibleResidentsDoNotWrite()
    {
        var unknown = NewStore();
        True(!NpcHealthNeedLifecycle.Observe(unknown, "npc:unknown", Archetype, 100), "unknown identity must fail closed");
        Equal(0, unknown.Snapshot().NpcMotivations.Count);

        var protectedStore = NewStore();
        CaptureHealth(protectedStore, HalfHealth(protectedStore), 100);
        protectedStore.ResolveResident(Owner).IsQuestProtected = true;
        True(!NpcHealthNeedLifecycle.Observe(protectedStore, Owner, Archetype, 100), "quest protection must block writes");
        Equal(0, protectedStore.Snapshot().NpcMotivations.Count);

        var deadStore = NewStore();
        CaptureHealth(deadStore, HalfHealth(deadStore), 100);
        True(Population.MarkDead(deadStore.GetOrCreate(Settlement, new[] { Home }, 1), Owner), "fixture resident should die");
        True(!NpcHealthNeedLifecycle.Observe(deadStore, Owner, Archetype, 100), "dead resident must not create motivation");
        Equal(0, deadStore.Snapshot().NpcMotivations.Count);

        var childStore = NewStore();
        CaptureHealth(childStore, HalfHealth(childStore), 100);
        childStore.ResolveResident(Owner).AgeGroup = ResidentAgeGroup.Child;
        True(!NpcHealthNeedLifecycle.Observe(childStore, Owner, Archetype, 100), "children are outside this lifecycle");
        Equal(0L, childStore.ReadNpcMotivationState(Owner).Revision);
    }

    private static void TimeRegressionsAreRejected()
    {
        var store = NewStore();
        CaptureHealth(store, HalfHealth(store), 120);
        True(!NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 119), "observation before vitals capture must fail closed");
        True(!NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, double.NaN), "NaN time must fail closed");
        True(!NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, double.PositiveInfinity), "infinite time must fail closed");
        Equal(0, store.Snapshot().NpcMotivations.Count);

        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 120), "matching current time should commit");
        True(!NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 119), "motivation ledger time regression must fail closed");
        Equal(2L, store.ReadNpcMotivationState(Owner).Revision);
        True(!NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, -1), "negative time must fail closed");
    }

    private static void SharedLedgerChangesDoNotStarve()
    {
        var store = NewStore();
        CaptureHealth(store, HalfHealth(store), 100);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 100), "initial injury should be recorded");
        CommitUnrelatedWorkGoal(store, 110);

        NpcVitalsView before = store.ReadNpcVitals(Owner, Archetype);
        CaptureHealth(store, before.MaximumHealth / 4, 120);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 120),
            "unrelated shared-ledger revision must not block new health evidence");
        Equal(NpcMotivationPriorityLevel.Emergency, Recovery(store).Priority);
        True(HasGoal(store.ReadNpcMotivationState(Owner), "goal:work-test"),
            "health update must preserve unrelated accepted motivation state");
    }

    private static void SaveReloadDoesNotReplay()
    {
        var store = NewStore();
        CaptureHealth(store, HalfHealth(store), 100);
        True(NpcHealthNeedLifecycle.Observe(store, Owner, Archetype, 100), "injury should be recorded before save");

        var options = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(store.Snapshot(), options);
        FullSerializer.fsData wire = FullSerializer.fsJsonParser.Parse(json);
        var serializer = new FullSerializer.fsSerializer();
        WorldState saved = null;
        FullSerializer.fsResult result = serializer.TryDeserialize(wire, ref saved);
        True(result.Succeeded && saved != null, "native FullSerializer must restore the health lifecycle save");

        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        NpcVitalsView restoredVitals = restored.ReadNpcVitals(Owner, Archetype);
        True(restoredVitals.IsPersisted && restoredVitals.CurrentHealth < restoredVitals.MaximumHealth,
            "saved current health must survive reload");
        Equal(NpcMotivationGoalStatus.Active, Recovery(restored).Status);
        True(!NpcHealthNeedLifecycle.Observe(restored, Owner, Archetype, 120), "saved evidence must suppress replay");
        Equal(2L, restored.ReadNpcMotivationState(Owner).Revision);
        Equal(2, restored.ReadNpcMotivationState(Owner).AcceptedUpdates.Count);
    }

    private static NpcMotivationNeedView HealthNeed(WorldStateStore store)
    {
        foreach (NpcMotivationNeedView need in store.ReadNpcMotivationState(Owner).Needs)
            if (need.Id == NpcHealthNeedLifecycle.HealthNeedId) return need;
        return null;
    }

    private static NpcMotivationGoalView Recovery(WorldStateStore store)
    {
        foreach (NpcMotivationGoalView goal in store.ReadNpcMotivationState(Owner).Goals)
            if (goal.Id == NpcHealthNeedLifecycle.RecoveryGoalId) return goal;
        return null;
    }

    private static bool HasGoal(NpcMotivationStateView state, string id)
    {
        foreach (NpcMotivationGoalView goal in state.Goals)
            if (goal.Id == id) return true;
        return false;
    }

    private static int HalfHealth(WorldStateStore store)
    { return store.ReadNpcVitals(Owner, Archetype).MaximumHealth / 2; }

    private static NpcVitalsView CaptureHealth(WorldStateStore store, int health, double at)
    {
        NpcVitalsView previous = store.ReadNpcVitals(Owner, Archetype);
        True(store.TryCaptureNpcVitals(Owner, Archetype, health, previous.CurrentMagicka, at, previous.Revision),
            "native vitals capture should accept current sample");
        return store.ReadNpcVitals(Owner, Archetype);
    }

    private static NpcVitalsView DeadView(WorldStateStore store)
    {
        Population.MarkDead(store.GetOrCreate(Settlement, new[] { Home }, 1), Owner);
        return store.ReadNpcVitals(Owner, Archetype);
    }

    private static void CommitUnrelatedWorkGoal(WorldStateStore store, double now)
    {
        // Schema 46 accepted generic owner updates. Restore this valid v1 save through the
        // current migration to exercise an unrelated goal between two native health samples.
        WorldState legacy = store.Snapshot();
        legacy.Version = 46; legacy.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        NpcMotivationState state = legacy.NpcMotivations.Find(row => row.OwnerPersonId == Owner);
        True(state != null, "first health observation should have a saved motivation row");
        state.Version = 1;
        var evidence = new NpcMotivationStateEvidence(Owner, "work-test:v1", NpcMotivationStateEvidenceKind.NativeObservation,
            state.Revision + 1, now);
        var proposal = NpcMotivationStateUpdateProposal.ForGoal(Owner, state.Revision, "work-test:v1", now,
            "goal:work-test", NpcMotivationGoalKind.Work, NpcMotivationGoalStatus.Active,
            NpcMotivationPriorityLevel.Routine, NpcMotivationKnowledgeState.Known, evidence);
        True(NpcMotivationState.TryApply(state, proposal, out NpcMotivationState withWork),
            "schema-46 work goal should be a valid legacy update");
        legacy.NpcMotivations[legacy.NpcMotivations.IndexOf(state)] = withWork;
        store.Restore(legacy);
        Equal(NpcMotivationState.VersionCurrent, store.Snapshot().NpcMotivations[0].Version);
        Equal(3L, store.ReadNpcMotivationState(Owner).Revision);
        True(HasGoal(store.ReadNpcMotivationState(Owner), "goal:work-test"),
            "migration must retain the unrelated accepted work goal");
    }

    private static WorldStateStore NewStore()
    {
        var store = new WorldStateStore(42);
        store.GetOrCreate(Settlement, new[] { Home }, 1);
        True(store.ResolveResident(Owner) != null, "fixture resident ID must be stable");
        return store;
    }

    private static int ExpectedIntensity(int maximumHealth, int currentHealth)
    {
        return currentHealth == maximumHealth ? 0 : (int)Math.Ceiling(100.0 * (maximumHealth - currentHealth) / maximumHealth);
    }

    private static void True(bool value, string message = "assertion failed")
    { if (!value) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException("expected " + expected + ", got " + actual);
    }
}
