using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private const string Owner = "npc:safety-resident";
    private const int Settlement = 10;
    private const int Home = 20;

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("direct eligible threat creates one explainable emergency goal", DirectThreatStartsOneGoal),
            ("sleeping quest-protected unknown and hearsay witnesses cannot create goals", EligibilityAndSourceGates),
            ("only a sealed native refuge arrival fulfills safety", RefugeArrivalFulfills),
            ("concurrent incidents share one slot and stale arrival cannot fulfill the latest", ConcurrentIncidentsStayBound),
            ("expired alarm and resident death abandon once", ExpiryAndDeathAbandonOnce),
            ("terminal replay stays closed and refreshed same-threat incident reuses one slot", ReplayAndRefreshedIncident),
            ("save reload preserves lifecycle receipts and context projection", SaveReloadAndContext),
            ("legacy closed safety row survives missing incident and accepts a new source", LegacyMissingIncidentMigration),
            ("incident serial exhaustion rejects without replacing an alarm", IncidentSerialOverflowFailsClosed),
            ("task-pinned origin cannot replace a pruned latest goal anchor", PinnedOriginCannotStandInForLatestRow),
            ("years of refreshed threats continue with bounded receipts and durable serial", YearsOfThreats)
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

    private static void DirectThreatStartsOneGoal()
    {
        var f = NewFixture("opaque-hostile-identity", 100);
        var detachedIncident = f.Store.Snapshot().AlarmIncidents.Find(a => a.Id == f.Incident.Id);
        var detachedWitness = detachedIncident.People.Find(p => p.ResidentId == f.Owner);
        True(!NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(f.Store, f.Owner, detachedIncident,
            detachedWitness, 100, true, true, false), "matching owner and IDs cannot authorize a detached incident/witness copy");
        True(!NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(f.Store, f.Owner, f.Incident,
            detachedWitness, 100, true, true, false), "matching resident ID cannot authorize a detached witness copy");
        True(f.Store.Snapshot().NpcMotivations.Count == 0, "foreign event objects must not allocate or commit state");
        True(Observe(f, 100, true, true, false), "eligible direct witness should create the goal");
        var view = f.Store.ReadNpcMotivationState(f.Owner);
        Equal(1, view.Goals.Count);
        Equal("goal:native-safety", view.Goals[0].Id);
        Equal(NpcMotivationGoalKind.Safety, view.Goals[0].Kind);
        Equal(NpcMotivationGoalStatus.Active, view.Goals[0].Status);
        Equal(NpcMotivationPriorityLevel.Emergency, view.Goals[0].Priority);
        Equal(NpcMotivationKnowledgeState.Known, view.Goals[0].KnowledgeState);
        Equal(NpcMotivationStateEvidenceKind.NativeObservation, view.Goals[0].EvidenceKind.Value);
        True(!view.Goals[0].EvidenceId.Contains("opaque-hostile-identity", StringComparison.Ordinal), "evidence must not attribute the hostile");
        Equal(1, view.AcceptedUpdates.Count);
        True(!Observe(f, 100, true, true, false), "duplicate event phase must be idempotent");
        Equal(1L, f.Store.ReadNpcMotivationState(f.Owner).Revision);
        Equal(1, f.Store.ReadNpcMotivationState(f.Owner).Goals.Count);
    }

    private static void EligibilityAndSourceGates()
    {
        var sleeping = NewFixture("sleeping", 100);
        True(!Observe(sleeping, 100, true, false, false), "sleeping witness is ineligible");
        Equal(0, sleeping.Store.Snapshot().NpcMotivations.Count);

        var protectedResident = NewFixture("quest-protected", 100);
        True(!Observe(protectedResident, 100, true, true, true), "quest-protected witness is ineligible");
        Equal(0, protectedResident.Store.Snapshot().NpcMotivations.Count);

        var storeProtected = NewFixture("store-quest-protected", 100);
        storeProtected.Store.ResolveResident(Settlement, storeProtected.Owner).IsQuestProtected = true;
        True(!Observe(storeProtected, 100, true, true, false), "persisted quest protection overrides a permissive runtime argument");
        True(storeProtected.Store.Snapshot().NpcMotivations.Count == 0, "protected resident must not allocate motivation state");

        var storeDead = NewFixture("store-dead", 100);
        True(Population.MarkDead(storeDead.Store.GetOrCreate(Settlement, new[] { Home }, 1), storeDead.Owner), "fixture resident should die");
        True(!Observe(storeDead, 100, true, true, false), "persisted death overrides a permissive runtime argument");
        True(storeDead.Store.Snapshot().NpcMotivations.Count == 0, "dead resident must not allocate motivation state");

        var dead = NewFixture("dead", 100);
        True(!Observe(dead, 100, false, true, false), "dead witness is ineligible");
        Equal(0, dead.Store.Snapshot().NpcMotivations.Count);

        var unknown = NewFixture("unknown", 100);
        True(!NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(unknown.Store, "npc:unknown", unknown.Incident,
            unknown.Witness, 100d, true, true, false), "unknown owner must fail closed");
        True(unknown.Store.ReadNpcMotivationState("npc:unknown") == null, "unknown identity must not allocate state");

        var hearsay = NewFixture("hearsay", 100);
        hearsay.Witness.DirectWitness = false;
        True(!Observe(hearsay, 100, true, true, false), "hearsay cannot be promoted to direct native evidence");
        Equal(0, hearsay.Store.Snapshot().NpcMotivations.Count);

        var playerAttributed = NewFixture("not-player-crime", 100);
        playerAttributed.Incident.PlayerAttributed = true;
        True(!Observe(playerAttributed, 100, true, true, false), "player attribution cannot enter this hostile-threat bridge");
        playerAttributed.Incident.PlayerAttributed = false;
        Equal(0, playerAttributed.Store.Snapshot().NpcMotivations.Count);
    }

    private static void RefugeArrivalFulfills()
    {
        var f = NewFixture("arrival", 100);
        True(Observe(f, 100, true, true, false), "threat should start the goal");
        True(!Fulfill(f, 110, true, false), "a route start or unsealed position is not arrival");
        Equal(NpcMotivationGoalStatus.Active, f.Store.ReadNpcMotivationState(f.Owner).Goals[0].Status);

        f.Witness.HasPosition = true;
        f.Witness.Reaction = CivilianReaction.Flee;
        f.Witness.BuildingKey = Home;
        f.Witness.ReachedRefuge = true;
        f.Witness.X = 4;
        f.Witness.Y = 0;
        f.Witness.Z = 3;
        f.Store.RecordShelterArrival(f.Incident.Id, f.Owner, 110);
        True(f.Witness.RefugeSealed, "native store must confirm the refuge arrival");
        var detachedIncident = f.Store.Snapshot().AlarmIncidents.Find(a => a.Id == f.Incident.Id);
        var detachedWitness = detachedIncident.People.Find(p => p.ResidentId == f.Owner);
        True(!NpcNativeSafetyGoalLifecycle.TryFulfillAtRefuge(f.Store, f.Owner, detachedIncident,
            detachedWitness, 110, true, false), "a copied sealed-arrival object cannot fulfill the live incident");
        Equal(NpcMotivationGoalStatus.Active, f.Store.ReadNpcMotivationState(f.Owner).Goals[0].Status);
        True(Fulfill(f, 110, true, false), "sealed living refuge arrival should fulfill the goal");
        Equal(NpcMotivationGoalStatus.Fulfilled, f.Store.ReadNpcMotivationState(f.Owner).Goals[0].Status);
        Equal(2, f.Store.ReadNpcMotivationState(f.Owner).AcceptedUpdates.Count);
        True(!Fulfill(f, 110, true, false), "replayed arrival cannot append another receipt");
        Equal(2L, f.Store.ReadNpcMotivationState(f.Owner).Revision);
    }

    private static void ExpiryAndDeathAbandonOnce()
    {
        var noGoal = NewFixture("no-goal-reconciliation", 100);
        True(!NpcNativeSafetyGoalLifecycle.TryReconcile(noGoal.Store, noGoal.Owner, 100d, true, false),
            "resident without an active Safety row should take the allocation-free reconciliation exit");
        True(noGoal.Store.Snapshot().NpcMotivations.Count == 0, "reconciliation without a goal must not allocate a ledger row");

        var expired = NewFixture("expiry", 100);
        True(Observe(expired, 100, true, true, false), "threat should start the goal");
        True(NpcNativeSafetyGoalLifecycle.TryReconcile(expired.Store, expired.Owner, 1000d, true, false), "expired alarm should abandon active goal");
        Equal(NpcMotivationGoalStatus.Abandoned, expired.Store.ReadNpcMotivationState(expired.Owner).Goals[0].Status);
        Equal(2L, expired.Store.ReadNpcMotivationState(expired.Owner).Revision);
        True(!NpcNativeSafetyGoalLifecycle.TryReconcile(expired.Store, expired.Owner, 1001d, true, false), "reconciliation must be idempotent");
        Equal(2L, expired.Store.ReadNpcMotivationState(expired.Owner).Revision);

        var unrelated = NewFixture("expired-before-new-murder", 100, 2);
        True(Observe(unrelated, 100, true, true, false), "threat should start the goal");
        string otherResident = unrelated.Store.Snapshot().Settlements.Find(s => s.SettlementId == Settlement).Households[1].Residents[0].Id;
        var murder = unrelated.Store.RecordViolence(Settlement, otherResident, 999d);
        var murderWitness = unrelated.Store.Witness(murder.Id, unrelated.Owner, 999d);
        True(murderWitness != null && murderWitness.DirectWitness, "newer unrelated alarm should be active for this resident");
        True(unrelated.Store.ActiveAlarm(unrelated.Owner, 999d).Id == murder.Id, "fixture should have a newer unrelated active alarm");
        True(!NpcNativeSafetyGoalLifecycle.TryReconcile(unrelated.Store, unrelated.Owner, 999d, true, false),
            "the original source incident remains active despite a newer unrelated alarm");
        True(NpcNativeSafetyGoalLifecycle.TryReconcile(unrelated.Store, unrelated.Owner, 1001d, true, false),
            "an unrelated active alarm must not keep an expired safety episode open");
        Equal(NpcMotivationGoalStatus.Abandoned, unrelated.Store.ReadNpcMotivationState(unrelated.Owner).Goals[0].Status);

        var dead = NewFixture("death", 100);
        True(Observe(dead, 100, true, true, false), "threat should start the goal");
        True(NpcNativeSafetyGoalLifecycle.TryReconcile(dead.Store, dead.Owner, 101d, false, false), "death should abandon before alarm expiry");
        Equal(NpcMotivationGoalStatus.Abandoned, dead.Store.ReadNpcMotivationState(dead.Owner).Goals[0].Status);

        var persistedDeath = NewFixture("persisted-death-reconcile", 100);
        True(Observe(persistedDeath, 100, true, true, false), "threat should start the goal");
        True(Population.MarkDead(persistedDeath.Store.GetOrCreate(Settlement, new[] { Home }, 1), persistedDeath.Owner), "fixture resident should die");
        True(NpcNativeSafetyGoalLifecycle.TryReconcile(persistedDeath.Store, persistedDeath.Owner, 101d, true, false),
            "persisted death must abandon even when the caller reports stale alive state");
        Equal(NpcMotivationGoalStatus.Abandoned, persistedDeath.Store.ReadNpcMotivationState(persistedDeath.Owner).Goals[0].Status);
    }

    private static void ConcurrentIncidentsStayBound()
    {
        var f = NewFixture("older-incident", 100);
        True(Observe(f, 100, true, true, false), "first incident should start");
        var newer = f.Store.RecordHostileThreat(Settlement, "newer-incident", 101);
        var duplicateSerial = f.Store.Snapshot();
        duplicateSerial.AlarmIncidents[1].GenerationSerial = duplicateSerial.AlarmIncidents[0].GenerationSerial;
        ThrowsArgument(() => new WorldStateStore(8).Restore(duplicateSerial), "duplicate incident generation serial loaded");
        var newerWitness = f.Store.Witness(newer.Id, f.Owner, 101);
        True(NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(f.Store, f.Owner, newer,
            newerWitness, 101, true, true, false), "new concurrent incident should advance the current slot");
        True(f.Store.ReadNpcMotivationState(f.Owner).Goals[0].GoalStartRevision == 2,
            "concurrent new incident reused the old goal generation");
        Equal(1, f.Store.ReadNpcMotivationState(f.Owner).Goals.Count);
        Equal(2L, f.Store.ReadNpcMotivationState(f.Owner).Revision);
        True(!NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(f.Store, f.Owner, f.Incident,
            f.Witness, 102, true, true, false), "resurveying an older still-active incident cannot replace the latest goal");
        Equal(2L, f.Store.ReadNpcMotivationState(f.Owner).Revision);

        f.Witness.HasPosition = true;
        f.Witness.Reaction = CivilianReaction.Flee;
        f.Witness.BuildingKey = Home;
        f.Witness.ReachedRefuge = true;
        f.Witness.X = 4;
        f.Witness.Z = 3;
        f.Store.RecordShelterArrival(f.Incident.Id, f.Owner, 102);
        True(!NpcNativeSafetyGoalLifecycle.TryFulfillAtRefuge(f.Store, f.Owner, f.Incident,
            f.Witness, 102, true, false), "older arrival cannot fulfill a newer incident's slot");
        Equal(2L, f.Store.ReadNpcMotivationState(f.Owner).Revision);

        newerWitness.HasPosition = true;
        newerWitness.Reaction = CivilianReaction.Flee;
        newerWitness.BuildingKey = Home;
        newerWitness.ReachedRefuge = true;
        newerWitness.X = 4;
        newerWitness.Z = 3;
        f.Store.RecordShelterArrival(newer.Id, f.Owner, 103);
        True(NpcNativeSafetyGoalLifecycle.TryFulfillAtRefuge(f.Store, f.Owner, newer,
            newerWitness, 103, true, false), "latest incident arrival fulfills the current slot");
        Equal(NpcMotivationGoalStatus.Fulfilled, f.Store.ReadNpcMotivationState(f.Owner).Goals[0].Status);
        Equal(1, f.Store.ReadNpcMotivationState(f.Owner).Goals.Count);
    }

    private static void ReplayAndRefreshedIncident()
    {
        var f = NewFixture("same-threat", 100);
        True(Observe(f, 100, true, true, false), "first incident should start");
        f.Witness.HasPosition = true;
        f.Witness.Reaction = CivilianReaction.Flee;
        f.Witness.BuildingKey = Home;
        f.Witness.ReachedRefuge = true;
        f.Store.RecordShelterArrival(f.Incident.Id, f.Owner, 110);
        True(Fulfill(f, 110, true, false), "first incident should fulfill");
        True(!Observe(f, 111, true, true, false), "the same still-visible incident must not reopen a terminal slot");
        Equal(NpcMotivationGoalStatus.Fulfilled, f.Store.ReadNpcMotivationState(f.Owner).Goals[0].Status);
        Equal(2L, f.Store.ReadNpcMotivationState(f.Owner).Revision);

        True(!Fulfill(f, 111, true, false), "stale route cannot re-complete a terminal goal");
        var replacement = f.Store.RecordHostileThreat(Settlement, "same-threat", 4001);
        True(replacement.Id == f.Incident.Id && replacement.StartedAt > f.Incident.StartedAt,
            "fixture must model a refreshed episode with the same stable threat ID");
        var witness = f.Store.Witness(replacement.Id, f.Owner, 4001);
        True(NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(f.Store, f.Owner, replacement, witness, 4001d, true, true, false),
            "a genuinely new episode must advance the stable goal slot");
        Equal(1, f.Store.ReadNpcMotivationState(f.Owner).Goals.Count);
        Equal(NpcMotivationGoalStatus.Active, f.Store.ReadNpcMotivationState(f.Owner).Goals[0].Status);
        Equal(3L, f.Store.ReadNpcMotivationState(f.Owner).Revision);
        True(!NpcNativeSafetyGoalLifecycle.TryFulfillAtRefuge(f.Store, f.Owner, f.Incident, f.Witness, 4002d, true, false),
            "an older incident cannot fulfill a newer goal episode");
    }

    private static void SaveReloadAndContext()
    {
        var f = NewFixture("save-replay", 100);
        True(Observe(f, 100, true, true, false), "threat should start the goal");
        True(f.Store.TryCaptureNpcMotivationUpdate(f.Owner, out NpcMotivationUpdateTicket staleTicket), "ticket fixture");
        var staleProposal = NpcMotivationStateUpdateProposal.ForGoal(f.Owner, staleTicket.ExpectedRevision,
            "operation:stale-after-reload", 101, "goal:stale", NpcMotivationGoalKind.Safety,
            NpcMotivationGoalStatus.Active, NpcMotivationPriorityLevel.Emergency, NpcMotivationKnowledgeState.Known,
            new NpcMotivationStateEvidence(f.Owner, "evidence:stale-after-reload",
                NpcMotivationStateEvidenceKind.NativeObservation, staleTicket.ExpectedRevision + 1, 101));

        var options = new JsonSerializerOptions { IncludeFields = true };
        string json = JsonSerializer.Serialize(f.Store.Snapshot(), options);
        FullSerializer.fsData wire = FullSerializer.fsJsonParser.Parse(json);
        var serializer = new FullSerializer.fsSerializer();
        WorldState saved = null;
        FullSerializer.fsResult deserializeResult = serializer.TryDeserialize(wire, ref saved);
        True(deserializeResult.Succeeded, "native FullSerializer should deserialize the saved world state");
        True(saved != null, "serialized world state should deserialize");
        f.Store.Restore(saved);
        True(!f.Store.TryCommitNpcMotivationUpdate(staleTicket, staleProposal), "restore invalidates pre-load tickets");
        var restoredView = f.Store.ReadNpcMotivationState(f.Owner);
        Equal(NpcMotivationGoalStatus.Active, restoredView.Goals[0].Status);
        Equal(1, restoredView.AcceptedUpdates.Count);

        var restoredIncident = f.Store.ActiveAlarm(f.Owner, 100d);
        var restoredWitness = restoredIncident.People.Find(p => p.ResidentId == f.Owner);
        True(!NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(f.Store, f.Owner, restoredIncident,
            restoredWitness, 100d, true, true, false), "serialized accepted receipt prevents replay with the restored live event");
        Equal(1L, f.Store.ReadNpcMotivationState(f.Owner).Revision);

        string branch = Conversation(f.Owner, 120, out ConversationContextSnapshot conversation);
        var clock = new NpcMotivationGameClock(120, 2,
            Evidence(branch, f.Owner, "clock:safety", NpcMotivationEvidenceKind.WorldClock, 2, 120));
        var profile = new NpcMotivationProfileReference(f.Owner, 3,
            Evidence(branch, f.Owner, "profile:safety", NpcMotivationEvidenceKind.PersonalityProfile, 3, 120));
        var context = f.Store.CaptureNpcMotivationContext(conversation, clock, profile,
            null, null, null, null, null);
        Equal(1, context.Goals.Count);
        Equal("goal:native-safety", context.Goals[0].Id);
        Contains(NpcMotivationContextFormatter.ForModels(context), "id=goal:native-safety");
    }

    private static void YearsOfThreats()
    {
        var f = NewFixture("years-threat", 100);
        const int episodes = 21000;
        for (int i = 0; i < episodes; i++)
        {
            double start = 100 + i * 1000d;
            var incident = i == 0 ? f.Incident : f.Store.RecordHostileThreat(Settlement, "years-threat", start);
            var witness = i == 0 ? f.Witness : f.Store.Witness(incident.Id, f.Owner, start);
            True(NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(f.Store, f.Owner, incident, witness,
                start, true, true, false), "safety source stopped at episode " + i);
            True(NpcNativeSafetyGoalLifecycle.TryReconcile(f.Store, f.Owner, start + 901, true, false),
                "safety closure stopped at episode " + i);
        }
        var before = f.Store.ReadNpcMotivationState(f.Owner);
        Equal(episodes * 2L, before.Revision);
        True(before.AcceptedUpdates.Count <= 256, "safety receipt tail grew without bound");
        var lowered = f.Store.Snapshot();
        lowered.NpcMotivations[0].LastStartedSafetySerial = 1;
        ThrowsArgument(() => new WorldStateStore(7).Restore(lowered), "lowered safety cursor loaded after trimming");
        var rolledBackWorld = f.Store.Snapshot();
        rolledBackWorld.NextIncidentSerial = rolledBackWorld.AlarmIncidents[0].GenerationSerial;
        ThrowsArgument(() => new WorldStateStore(7).Restore(rolledBackWorld), "incident serial counter rollback loaded");
        var restored = new WorldStateStore(7); restored.Restore(f.Store.Snapshot());
        var last = restored.Snapshot().AlarmIncidents.Find(x => x.Id == f.Incident.Id);
        var live = restored.RecordHostileThreat(Settlement, "years-threat", 100 + episodes * 1000d);
        True(live.GenerationSerial > last.GenerationSerial, "incident serial did not survive restore");
        var witnessNext = restored.Witness(live.Id, f.Owner, 100 + episodes * 1000d);
        True(NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(restored, f.Owner, live, witnessNext,
            100 + episodes * 1000d, true, true, false), "new safety source stopped after restore");
        Equal(episodes * 2L + 1, restored.ReadNpcMotivationState(f.Owner).Revision);
    }

    private static void LegacyMissingIncidentMigration()
    {
        var f = NewFixture("legacy-threat", 100);
        True(Observe(f, 100, true, true, false), "legacy safety start failed");
        True(NpcNativeSafetyGoalLifecycle.TryReconcile(f.Store, f.Owner, 1001, true, false), "legacy safety close failed");
        var saved = f.Store.Snapshot();
        saved.Version = 46; saved.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        saved.NpcMotivations[0].Version = 1;
        saved.AlarmIncidents.Clear();
        string before = JsonSerializer.Serialize(saved, new JsonSerializerOptions { IncludeFields = true });
        var restored = new WorldStateStore(7); restored.Restore(saved);
        Equal(before, JsonSerializer.Serialize(saved, new JsonSerializerOptions { IncludeFields = true }));
        var migrated = restored.Snapshot().NpcMotivations[0];
        Equal(0L, migrated.LastStartedSafetySerial);
        True(migrated.LastStartedSafetyDigest != null && migrated.LastClosedSafetyDigest == migrated.LastStartedSafetyDigest,
            "migration lost the closed safety phase without a retained incident");
        var fresh = restored.RecordHostileThreat(Settlement, "legacy-threat", 1100);
        var witness = restored.Witness(fresh.Id, f.Owner, 1100);
        True(NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(restored, f.Owner, fresh, witness, 1100,
            true, true, false), "new threat was blocked by missing legacy incident");
        Equal(3L, restored.ReadNpcMotivationState(f.Owner).Revision);
    }

    private static void IncidentSerialOverflowFailsClosed()
    {
        var f = NewFixture("overflow-threat", 100);
        var saved = f.Store.Snapshot(); saved.NextIncidentSerial = long.MaxValue - 1;
        var restored = new WorldStateStore(7); restored.Restore(saved);
        long originalSerial = restored.Snapshot().AlarmIncidents[0].GenerationSerial;
        ThrowsArgument(() => restored.RecordHostileThreat(Settlement, "overflow-threat", 1001),
            "exhausted incident serial allocated a replacement");
        var after = restored.Snapshot();
        Equal(1, after.AlarmIncidents.Count);
        Equal(originalSerial, after.AlarmIncidents[0].GenerationSerial);
        Equal(long.MaxValue - 1, after.NextIncidentSerial);
    }

    private static void PinnedOriginCannotStandInForLatestRow()
    {
        var f = NewFixture("pinned-origin", 100);
        True(Observe(f, 100, true, true, false), "initial safety goal missing");
        var oldRow = f.Store.Snapshot().NpcMotivations[0].Goals[0];
        var task = new NpcTaskPlan { Id = "task:pinned-safety", ProposalId = "task:pinned-safety", ResidentId = f.Owner,
            GoalId = NpcNativeSafetyGoalLifecycle.GoalId, GoalStartRevision = 1,
            AcceptedAt = 101, UpdatedAt = 101, DeadlineAt = 201 };
        task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
        True(ClassifiedAdmissionFixture.TryAcceptTask(f.Store, task, true, out string reason), reason);
        var accepted = f.Store.ReadNpcTask(task.Id);
        True(f.Store.CancelNpcTask(task.Id, accepted.Revision, 102, "Threat passed."), "task cancel failed");
        True(NpcNativeSafetyGoalLifecycle.TryReconcile(f.Store, f.Owner, 1001, true, false), "safety goal did not close");
        var initialVitals = f.Store.ReadNpcVitals(f.Owner, NpcArchetype.Citizen);
        for (int i = 0; i < 130; i++)
        {
            double now = 1010 + i * 10d;
            var before = f.Store.ReadNpcVitals(f.Owner, NpcArchetype.Citizen);
            int health = i % 2 == 0 ? Math.Max(1, initialVitals.MaximumHealth / 2) : initialVitals.MaximumHealth;
            True(f.Store.TryCaptureNpcVitals(f.Owner, NpcArchetype.Citizen, health, before.CurrentMagicka, now,
                before.Revision), "health filler source failed");
            True(NpcHealthNeedLifecycle.Observe(f.Store, f.Owner, NpcArchetype.Citizen, now), "health filler motivation failed");
        }
        var valid = f.Store.Snapshot();
        True(valid.NpcMotivations[0].ReceiptBaseRevision > 2, "safety latest receipt remains in tail");
        True(valid.NpcMotivations[0].GoalOriginAnchors.Exists(x => x.RowId == NpcNativeSafetyGoalLifecycle.GoalId && x.Revision == 1),
            "bound task lost its original safety admission");
        True(valid.NpcMotivations[0].LatestRowAnchors.Exists(x => x.RowId == NpcNativeSafetyGoalLifecycle.GoalId && x.Revision == 2),
            "current safety goal lost its latest-state anchor");
        var restored = new WorldStateStore(7); restored.Restore(valid);
        Equal(1L, restored.ReadNpcTask(task.Id).GoalStartRevision);
        var forged = f.Store.Snapshot();
        forged.NpcMotivations[0].LatestRowAnchors.RemoveAll(x => x.RowId == NpcNativeSafetyGoalLifecycle.GoalId);
        forged.NpcMotivations[0].Goals[0] = oldRow;
        ThrowsArgument(() => new WorldStateStore(7).Restore(forged), "old pinned origin substituted for missing latest-row anchor");
        var copied = f.Store.Snapshot();
        var pinnedOrigin = copied.NpcMotivations[0].GoalOriginAnchors.Find(x =>
            x.RowId == NpcNativeSafetyGoalLifecycle.GoalId && x.Revision == 1);
        copied.NpcMotivations[0].LatestRowAnchors.RemoveAll(x => x.RowId == NpcNativeSafetyGoalLifecycle.GoalId);
        copied.NpcMotivations[0].LatestRowAnchors.Add(pinnedOrigin);
        copied.NpcMotivations[0].Goals[0] = oldRow;
        ThrowsArgument(() => new WorldStateStore(7).Restore(copied), "old pinned origin copied into latest-row anchor");
        Equal(1L, pinnedOrigin.Revision);
        Equal(2L, valid.NpcMotivations[0].Goals[0].StateRevision);
    }

    private static bool Observe(Fixture f, double now, bool alive, bool awake, bool questProtected)
    { return NpcNativeSafetyGoalLifecycle.TryObserveDirectThreat(f.Store, f.Owner, f.Incident, f.Witness, now, alive, awake, questProtected); }

    private static bool Fulfill(Fixture f, double now, bool alive, bool questProtected)
    { return NpcNativeSafetyGoalLifecycle.TryFulfillAtRefuge(f.Store, f.Owner, f.Incident, f.Witness, now, alive, questProtected); }

    private static Fixture NewFixture(string threatId, double now, int householdCount = 1)
    {
        var store = new WorldStateStore(42);
        store.GetOrCreate(Settlement, householdCount > 1 ? new[] { Home, Home + 1 } : new[] { Home }, 1);
        var resident = store.ResolveResident(Settlement, Owner);
        if (resident == null)
        {
            var population = store.Snapshot().Settlements.Find(x => x.SettlementId == Settlement);
            resident = population.Households[0].Residents[0];
        }
        string owner = resident.Id;
        var incident = store.RecordHostileThreat(Settlement, threatId, now);
        var witness = store.Witness(incident.Id, owner, now);
        True(witness != null && witness.DirectWitness, "fixture must contain a native direct witness");
        return new Fixture(store, owner, incident, witness);
    }

    private static string Conversation(string owner, double now, out ConversationContextSnapshot snapshot)
    {
        const string archive = "0123456789abcdef0123456789abcdef";
        var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, owner, null,
            "session:safety", 1, 7, archive, "", "scene:street", 1);
        string branch = binding.SaveEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + archive;
        var unknownString = ConversationContextFact<string>.Unknown();
        snapshot = new ConversationContextSnapshot(binding, now,
            ConversationContextFact<string>.Verified("clock", ConversationContextFactSource.LiveObservation, now),
            ConversationContextFact<string>.Verified("Ria", ConversationContextFactSource.NativeActor, now),
            ConversationContextFact<ConversationDialogueRole>.Verified(ConversationDialogueRole.ManagedResident, ConversationContextFactSource.NativeActor, now),
            ConversationContextFact<string>.Verified("Street", ConversationContextFactSource.LiveObservation, now),
            unknownString, ConversationContextFact<ConversationNpcPosition>.Unknown(),
            ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
            ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), unknownString, 0, 1, null,
            Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
        return branch;
    }

    private static NpcMotivationEvidenceRef Evidence(string branch, string observer, string id,
        NpcMotivationEvidenceKind kind, long revision, double time)
    { return new NpcMotivationEvidenceRef(branch, observer, id, kind, revision, time); }

    private sealed class Fixture
    {
        public readonly WorldStateStore Store;
        public readonly string Owner;
        public readonly CivilianIncident Incident;
        public readonly AlarmPerson Witness;
        public Fixture(WorldStateStore store, string owner, CivilianIncident incident, AlarmPerson witness)
        { Store = store; Owner = owner; Incident = incident; Witness = witness; }
    }

    private static void True(bool value, string message = "assertion failed")
    { if (!value) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("expected " + expected + ", got " + actual); }

    private static void Contains(string value, string fragment)
    { if (value == null || !value.Contains(fragment, StringComparison.Ordinal)) throw new InvalidOperationException("missing fragment: " + fragment); }
    private static void ThrowsArgument(Action action, string message)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException(message); }
}
