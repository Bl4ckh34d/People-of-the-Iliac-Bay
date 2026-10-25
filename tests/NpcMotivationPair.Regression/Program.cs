using System;
using System.Collections.Generic;
using System.Globalization;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const string NeedId = NpcHealthNeedLifecycle.HealthNeedId;
    private const string GoalId = NpcHealthNeedLifecycle.RecoveryGoalId;
    private static int Main()
    {
        var cases = new List<(string, Action)>
        {
            ("valid pair publishes exact revisions and one goal generation", ValidPair),
            ("invalid second update rolls back valid first update", InvalidSecondRollsBack),
            ("stale replay restored and wrong-owner tickets publish nothing", TicketGuards),
            ("legacy receipt boundary still publishes complete injury pair", CapacityCannotSplitHealth),
            ("typed native health commit cannot bypass resident eligibility", TypedSourceEligibility),
            ("malformed legacy native source fails migration without changing input", MalformedLegacySource),
            ("injury partial healing full cure and reinjury keep exact generations", HealthLifecycle),
            ("five years of health changes continue beyond lifetime receipt cap", FiveYearsOfHealthChanges)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + test.Item1 + ": " + e.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static WorldStateStore NewStore(out string owner)
    {
        var store = new WorldStateStore(42);
        owner = store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static NpcVitalsView Injure(WorldStateStore store, string owner, double now)
    {
        var before = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        CaptureHealth(store, owner, Math.Max(1, before.MaximumHealth / 2), now);
        return store.ReadNpcVitals(owner, NpcArchetype.Citizen);
    }

    private static NpcMotivationStateUpdateProposal Need(string owner, long expected, double now, NpcVitalsView vitals)
    {
        string source = vitals.Revision.ToString(CultureInfo.InvariantCulture);
        string evidenceId = "native-health:v1:need:" + source + ":" + vitals.CurrentHealth + ":" + vitals.MaximumHealth;
        var evidence = new NpcMotivationStateEvidence(owner, evidenceId, NpcMotivationStateEvidenceKind.NativeObservation, expected + 1, now);
        int intensity = (int)Math.Min(100, (100L * (vitals.MaximumHealth - vitals.CurrentHealth) + vitals.MaximumHealth - 1) / vitals.MaximumHealth);
        return NpcMotivationStateUpdateProposal.ForNeed(owner, expected, "motivation:native-health:v1:need:" + source, now,
            NeedId, NpcMotivationNeedKind.Health, NpcMotivationKnowledgeState.Known, intensity, evidence);
    }

    private static NpcMotivationStateUpdateProposal Goal(string owner, long expected, double now, NpcVitalsView vitals)
    {
        string source = vitals.Revision.ToString(CultureInfo.InvariantCulture);
        string evidenceId = "native-health:v1:goal:" + source + ":" + vitals.CurrentHealth + ":" + vitals.MaximumHealth;
        var evidence = new NpcMotivationStateEvidence(owner, evidenceId, NpcMotivationStateEvidenceKind.NativeObservation, expected + 1, now);
        bool injured = vitals.CurrentHealth < vitals.MaximumHealth;
        var priority = !injured ? NpcMotivationPriorityLevel.Routine :
            (long)vitals.CurrentHealth * 4 <= vitals.MaximumHealth ? NpcMotivationPriorityLevel.Emergency :
            (long)vitals.CurrentHealth * 2 <= vitals.MaximumHealth ? NpcMotivationPriorityLevel.Urgent : NpcMotivationPriorityLevel.Important;
        return NpcMotivationStateUpdateProposal.ForGoal(owner, expected, "motivation:native-health:v1:goal:" + source, now,
            GoalId, NpcMotivationGoalKind.Recovery, injured ? NpcMotivationGoalStatus.Active : NpcMotivationGoalStatus.Fulfilled,
            priority, NpcMotivationKnowledgeState.Known, evidence);
    }

    private static void ValidPair()
    {
        string owner; var store = NewStore(out owner);
        var vitals = Injure(store, owner, 90);
        True(store.TryCaptureNpcMotivationUpdate(owner, out var ticket), "ticket missing");
        True(store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Citizen,
            Need(owner, 0, 100, vitals), Goal(owner, 1, 100, vitals)),
            "valid pair rejected");
        var state = store.ReadNpcMotivationState(owner);
        Equal(2L, state.Revision); Equal(2, state.AcceptedUpdates.Count);
        Equal(1L, state.Needs[0].Revision); Equal(2L, state.Goals[0].Revision);
        Equal(2L, state.Goals[0].GoalStartRevision);
        Equal(NpcMotivationGoalStatus.Active, state.Goals[0].Status);
        var restored = new WorldStateStore(7); restored.Restore(store.Snapshot());
        Equal(2L, restored.ReadNpcMotivationState(owner).Goals[0].GoalStartRevision);
    }

    private static void InvalidSecondRollsBack()
    {
        string owner; var store = NewStore(out owner);
        var vitals = Injure(store, owner, 90);
        True(store.TryCaptureNpcMotivationUpdate(owner, out var ticket), "ticket missing");
        var invalidEvidence = new NpcMotivationStateEvidence(owner, "evidence:goal:invalid",
            NpcMotivationStateEvidenceKind.AuthorizedDecisionIntent, 2, 100);
        var invalidGoal = NpcMotivationStateUpdateProposal.ForGoal(owner, 1, "operation:goal:invalid", 100,
            GoalId, NpcMotivationGoalKind.Recovery, NpcMotivationGoalStatus.Fulfilled,
            NpcMotivationPriorityLevel.Urgent, NpcMotivationKnowledgeState.Known, invalidEvidence);
        True(!store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Citizen, Need(owner, 0, 100, vitals),
            invalidGoal), "invalid second update accepted");
        Equal(0, store.Snapshot().NpcMotivations.Count);
        Equal(0L, store.ReadNpcMotivationState(owner).Revision);
        True(store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Citizen,
            Need(owner, 0, 100, vitals), Goal(owner, 1, 100, vitals)), "rejected pair poisoned ticket");
    }

    private static void TicketGuards()
    {
        string owner; var store = NewStore(out owner);
        string other; var otherStore = NewStore(out other);
        var vitals = Injure(store, owner, 90);
        True(store.TryCaptureNpcMotivationUpdate(owner, out var ticket), "ticket missing");
        True(!store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Citizen, Need(owner, 0, 100, vitals),
            Goal("npc:wrong-owner", 1, 100, vitals)), "wrong-owner pair accepted");
        True(!otherStore.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Citizen, Need(owner, 0, 100, vitals),
            Goal(owner, 1, 100, vitals)), "cross-store ticket accepted");
        var restored = new WorldStateStore(7); restored.Restore(store.Snapshot());
        True(!restored.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Citizen, Need(owner, 0, 100, vitals),
            Goal(owner, 1, 100, vitals)), "pre-restore ticket accepted");
        True(store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Citizen,
            Need(owner, 0, 100, vitals), Goal(owner, 1, 100, vitals)), "valid pair rejected");
        True(!store.TryCommitNativeHealthMotivationUpdate(ticket, NpcArchetype.Citizen,
            Need(owner, 0, 100, vitals)), "replay accepted");
        var healthy = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        CaptureHealth(store, owner, healthy.MaximumHealth, 101);
        var healed = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        True(store.TryCaptureNpcMotivationUpdate(owner, out var stale), "new ticket missing");
        True(store.TryCommitNativeHealthMotivationUpdatePair(stale, NpcArchetype.Citizen,
            Need(owner, 2, 101, healed), Goal(owner, 3, 101, healed)), "intervening update rejected");
        True(!store.TryCommitNativeHealthMotivationUpdatePair(stale, NpcArchetype.Citizen,
            Need(owner, 2, 102, healed), Goal(owner, 3, 102, healed)), "stale ticket accepted");
    }

    private static void CapacityCannotSplitHealth()
    {
        string owner; var source = NewStore(out owner);
        var saved = source.Snapshot();
        var row = NpcMotivationState.Create(owner);
        row.Version = 1;
        row.Revision = 4095;
        for (long revision = 1; revision <= row.Revision; revision++)
            row.AcceptedUpdates.Add(new NpcAcceptedMotivationUpdate("filler:" + revision, revision, 0,
                "need:filler", owner, null, NpcMotivationStateChangeKind.Need));
        saved.NpcMotivations.Add(row);
        saved.Version = 46; saved.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        var store = new WorldStateStore(7); store.Restore(saved);
        Equal(46, saved.Version); Equal(1, saved.NpcMotivations[0].Version);
        Equal(4095, saved.NpcMotivations[0].AcceptedUpdates.Count);
        Equal(0L, saved.NpcMotivations[0].ReceiptBaseRevision);
        var baseline = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        CaptureHealth(store, owner, Math.Max(1, baseline.MaximumHealth / 2), 90);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 100),
            "legacy receipt boundary blocked atomic injury publication");
        var after = store.ReadNpcMotivationState(owner);
        Equal(row.Revision + 2, after.Revision); Equal(1, after.Needs.Count); Equal(1, after.Goals.Count);
        True(after.AcceptedUpdates.Count <= 256, "receipt window exceeded its bound");
    }

    private static void TypedSourceEligibility()
    {
        string owner; var store = NewStore(out owner);
        var vitals = Injure(store, owner, 90);
        True(store.TryCaptureNpcMotivationUpdate(owner, out var ticket), "eligible ticket missing");
        True(!store.TryCommitNpcMotivationUpdatePair(ticket, Need(owner, 0, 100, vitals), Goal(owner, 1, 100, vitals)),
            "generic v2 commit bypassed source binding");
        var resident = store.ResolveResident(owner);
        resident.IsQuestProtected = true;
        True(!store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Citizen,
            Need(owner, 0, 100, vitals), Goal(owner, 1, 100, vitals)), "quest-protected resident accepted direct typed update");
        resident.IsQuestProtected = false;
        resident.AgeGroup = ResidentAgeGroup.Child;
        True(!store.TryCommitNativeHealthMotivationUpdatePair(ticket, NpcArchetype.Citizen,
            Need(owner, 0, 100, vitals), Goal(owner, 1, 100, vitals)), "child accepted direct typed update");

        var nativeStore = new WorldStateStore(42);
        nativeStore.GetOrCreate(11, new[] { 23 }, 1);
        var native = nativeStore.RegisterNativePerson(11, 23, 77, "Native", ResidentSex.Male, 0, 0, 0, 0);
        var nativeVitals = nativeStore.ReadNpcVitals(native.Id, NpcArchetype.Citizen);
        True(nativeStore.TryCaptureNpcMotivationUpdate(native.Id, out var nativeTicket), "native ticket missing");
        True(!nativeStore.TryCommitNativeHealthMotivationUpdate(nativeTicket, NpcArchetype.Citizen,
            Need(native.Id, 0, 100, nativeVitals)), "native-only actor accepted resident health source");
    }

    private static void MalformedLegacySource()
    {
        string owner; var store = NewStore(out owner);
        Injure(store, owner, 90);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 100), "legacy source fixture missing");
        var saved = store.Snapshot(); saved.Version = 46; saved.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        saved.NpcMotivations[0].Version = 1;
        saved.NpcMotivations[0].AcceptedUpdates[0].OperationId = "motivation:native-health:v1:need:malformed";
        long before = store.ReadNpcMotivationState(owner).Revision;
        ThrowsArgument(() => store.Restore(saved), "malformed native health source migrated");
        Equal(before, store.ReadNpcMotivationState(owner).Revision);
        Equal(1, saved.NpcMotivations[0].Version);
        Equal("motivation:native-health:v1:need:malformed", saved.NpcMotivations[0].AcceptedUpdates[0].OperationId);
    }

    private static void HealthLifecycle()
    {
        string owner; var store = NewStore(out owner);
        var baseVitals = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        int max = baseVitals.MaximumHealth;
        CaptureHealth(store, owner, Math.Max(1, max / 4), 90);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 100), "injury not observed");
        var initial = store.ReadNpcMotivationState(owner);
        Equal(2L, initial.Revision);
        long generation = initial.Goals[0].GoalStartRevision;
        CaptureHealth(store, owner, Math.Max(1, max / 2), 110);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 110), "partial healing not observed");
        var partial = store.ReadNpcMotivationState(owner);
        Equal(4L, partial.Revision); Equal(generation, partial.Goals[0].GoalStartRevision);
        CaptureHealth(store, owner, max, 120);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 120), "full cure not observed");
        var full = store.ReadNpcMotivationState(owner);
        Equal(6L, full.Revision); Equal(NpcMotivationGoalStatus.Fulfilled, full.Goals[0].Status);
        Equal(0, full.Needs[0].Intensity.Value); Equal(generation, full.Goals[0].GoalStartRevision);
        CaptureHealth(store, owner, Math.Max(1, max / 4), 130);
        True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, 130), "new injury not observed");
        var newInjury = store.ReadNpcMotivationState(owner);
        Equal(8L, newInjury.Revision);
        True(newInjury.Goals[0].GoalStartRevision > generation, "reinjury reused generation");
    }

    private static void FiveYearsOfHealthChanges()
    {
        string owner; var store = NewStore(out owner);
        var initial = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        int half = Math.Max(1, initial.MaximumHealth / 2);
        const int changes = 21000;
        NpcMotivationNeedState oldNeed = null;
        NpcAcceptedMotivationUpdate oldNeedReceipt = null;
        for (int i = 0; i < changes; i++)
        {
            double now = 100 + i * 7500d;
            CaptureHealth(store, owner, i % 2 == 0 ? half : initial.MaximumHealth, now);
            True(NpcHealthNeedLifecycle.Observe(store, owner, NpcArchetype.Citizen, now),
                "health motivation stopped at change " + i);
            if (i == 3)
            {
                var early = store.Snapshot().NpcMotivations[0];
                oldNeed = early.Needs[0];
                oldNeedReceipt = early.AcceptedUpdates.Find(x => x.Revision == oldNeed.StateRevision);
            }
        }
        var before = store.ReadNpcMotivationState(owner);
        Equal(changes * 2L, before.Revision);
        True(before.AcceptedUpdates.Count <= 256, "retained motivation receipts grew without bound");
        var lowered = store.Snapshot();
        lowered.NpcMotivations[0].LastHealthNeedVitalsRevision = 1;
        ThrowsArgument(() => new WorldStateStore(7).Restore(lowered), "lowered health cursor loaded after trimming");
        var rewound = store.Snapshot();
        rewound.NpcMotivations[0].Needs[0] = oldNeed;
        ThrowsArgument(() => new WorldStateStore(7).Restore(rewound), "old complete row replaced latest anchored row");
        var copied = store.Snapshot();
        copied.NpcMotivations[0].LatestRowAnchors.RemoveAll(x => x.RowId == NpcHealthNeedLifecycle.HealthNeedId);
        copied.NpcMotivations[0].LatestRowAnchors.Add(oldNeedReceipt);
        copied.NpcMotivations[0].Needs[0] = oldNeed;
        ThrowsArgument(() => new WorldStateStore(7).Restore(copied), "old health receipt copied into latest-row anchor");
        Equal(7L, oldNeedReceipt.Revision);
        var orphan = store.Snapshot();
        orphan.NpcMotivations[0].GoalOriginAnchors.Add(new NpcAcceptedMotivationUpdate(
            "orphan:old-goal-origin", 2, 100, NpcHealthNeedLifecycle.RecoveryGoalId, owner,
            "native-health:v1:goal:1:1:2", NpcMotivationStateChangeKind.Goal, true, 2));
        ThrowsArgument(() => new WorldStateStore(7).Restore(orphan), "orphaned goal origin loaded");
        var restored = new WorldStateStore(7); restored.Restore(store.Snapshot());
        True(!NpcHealthNeedLifecycle.Observe(restored, owner, NpcArchetype.Citizen,
            100 + (changes - 1) * 7500d), "restore replayed the last native vitals source");
        double next = 100 + changes * 7500d;
        CaptureHealth(restored, owner, half, next);
        True(NpcHealthNeedLifecycle.Observe(restored, owner, NpcArchetype.Citizen, next),
            "new health source stopped after restore");
        Equal(changes * 2L + 2, restored.ReadNpcMotivationState(owner).Revision);
    }

    private static void CaptureHealth(WorldStateStore store, string owner, int wanted, double now)
    {
        var before = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        int health = Math.Max(1, Math.Min(wanted, before.MaximumHealth));
        True(store.TryCaptureNpcVitals(owner, NpcArchetype.Citizen, health, before.CurrentMagicka, now, before.Revision),
            "vitals capture failed");
    }

    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception("expected " + expected + ", got " + actual); }
    private static void ThrowsArgument(Action action, string message)
    { try { action(); } catch (ArgumentException) { return; } throw new Exception(message); }
}
