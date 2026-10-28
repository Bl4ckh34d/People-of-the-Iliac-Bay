using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Disposable-fixture acceptance for actual native NPC potion treatment.</summary>
    public static class NpcHealthCareNativeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const string Flag="-peopleoftheiliacbay-npc-healthcare-probe";
        private const string PaidServiceContractId="fixture-native-healthcare-paid-service";
        private const string PaidServiceFundingId="fixture-native-healthcare-paid-funding";
        private const int PaidServiceRate=120;
        private sealed class AutonomousCareResult
        {
            public string Failure;
            public bool Completed;
            public WorldState RecoverySnapshot;
            public string RecoveryPersonId;
        }
        private sealed class AlarmCareResult
        {
            public string Failure, TaskId;
            public long GoalStartRevision;
            public int NeedIntensity;
        }
        private sealed class PaidServiceEvidence
        {
            public NpcServiceContract Paused;
            public string CareId;
            public bool PausedBeforeTreatment;
            public bool StableDuringAlarm;
            public bool StableDuringTreatment;
            public bool ReturnObservedWhilePaused;
        }

        public static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            string originalSave=CommandLineValue("-peopleoftheiliacbay-fixture-save");
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),Flag)>=0 &&
                NativeProbeText.IsTestCharacter(character??@""),
                "Native NPC healthcare probe requires its explicit flag and an owned disposable character.");
            Require(!string.IsNullOrWhiteSpace(originalSave),
                "Native NPC healthcare probe requires the exact original -peopleoftheiliacbay-fixture-save title.");
            Require(host && streets!=null && streets.Ready,"Native NPC healthcare probe requires a ready town and mod host.");
            var game=GameManager.Instance;
            Require(game!=null && game.PlayerEntity!=null && game.PlayerEnterExit,
                "Native NPC healthcare probe has no live game session.");
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(character,originalSave)>=0,
                "Exact original fixture save is missing: "+originalSave);

            string failure=null;
            if(game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior(true);
                float exteriorUntil=Time.realtimeSinceStartup+90f;
                while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<exteriorUntil)yield return null;
                if(game.PlayerEnterExit.IsPlayerInside || !streets.Ready)
                    failure="Could not return the owned fixture to its exterior before selecting a resident.";
            }

            ResidentActor actor=null;
            if(failure==null)
            {
                // The disposable dawn save precedes ordinary outdoor routines.
                // Observe normal noon materialization; never create or relabel a resident.
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                var clock=DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
                yield return null;
                float until=Time.realtimeSinceStartup+120f;
                while(Time.realtimeSinceStartup<until)
                {
                    actor=FindCandidate(streets);
                    if(actor)break;
                    yield return null;
                }
                if(!actor)failure="No existing eligible adult with live native inventory and potion manager was materialized.";
            }

            if(failure==null)
            {
                try
                {
                    // Exercise current-schema persistence with DFU's actual serializer.
                    // Its detached fixture restores native currency in its own finally;
                    // this enclosing probe still reloads the original save on failure.
                    NpcFrameworkPersistenceProbe.Verify();
                    RunNativeChecks(streets,actor);
                    NativeNpcMagicHealingProbe.Verify(Store(streets),actor);
                }
                catch(Exception error) {failure=error.Message;}
            }

            if(failure==null)
            {
                // Direct adapter checks deliberately create care tasks without an
                // autonomous recovery goal. Reload the owned baseline before the
                // coordinator proof so it cannot mistake those fixture receipts for
                // source-bound autonomous treatment on the same resident.
                yield return ConversationMoodProbe.LoadSave(host,streets,character,originalSave,false);
                if(!streets.Ready)
                    failure="Intermediate fixture reload completed without a ready town population.";
                else
                {
                    if(game.PlayerEnterExit.IsPlayerInside)
                    {
                        game.PlayerEnterExit.TransitionExterior(true);
                        float exteriorUntil=Time.realtimeSinceStartup+90f;
                        while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) &&
                            Time.realtimeSinceStartup<exteriorUntil)yield return null;
                        if(game.PlayerEnterExit.IsPlayerInside || !streets.Ready)
                            failure="Could not return the reloaded owned fixture to its exterior.";
                    }
                    actor=null;
                    if(failure==null)
                    {
                        // The exact baseline reload also restores dawn. Repeat the
                        // same normal routine observation before autonomous care.
                        DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                        var clock=DaggerfallUnity.Instance.WorldTime.Now;
                        clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
                        yield return null;
                    }
                    float until=Time.realtimeSinceStartup+120f;
                    while(failure==null && Time.realtimeSinceStartup<until)
                    {
                        actor=FindCandidate(streets);
                        if(actor)break;
                        yield return null;
                    }
                    if(failure==null && !actor)
                        failure="No eligible live resident materialized after the controlled-check fixture reload.";
                    if(failure==null)
                    {
                        float vitalsUntil=Time.realtimeSinceStartup+15f;
                        while(Time.realtimeSinceStartup<vitalsUntil &&
                            !LiveVitalsReadyForAutonomousCare(streets,actor))yield return null;
                        if(!LiveVitalsReadyForAutonomousCare(streets,actor))
                            failure="Reloaded resident's native vitals binding did not match its saved or generated baseline within 15 seconds.";
                    }
                }
            }

            if(failure==null)
            {
                NativeNpcServiceContractFunding.CurrencySnapshot originalCurrency=null;
                bool inboxV5Verified=false;
                try
                {
                    originalCurrency=NativeNpcServiceContractFunding.CaptureCurrency(game.PlayerEntity);
                    if(game.PlayerEntity.GetGoldAmount()<PaidServiceRate)
                        game.PlayerEntity.GoldPieces=PaidServiceRate;
                    var result=new AutonomousCareResult();
                    var service=new PaidServiceEvidence();
                    yield return RunAutonomousCare(streets,actor,result,service);
                    if(result.Failure!=null)failure=result.Failure;
                    else
                    {
                        try
                        {
                            NpcExperienceProjectionNativeProbe.Verify(result.RecoverySnapshot,result.RecoveryPersonId);
                            NpcExperienceInboxV5NativeProbe.Verify(result.RecoverySnapshot,result.RecoveryPersonId);
                            inboxV5Verified=true;
                        }
                        catch(Exception error) { failure=error.Message; }
                    }
                }
                finally
                {
                    if(originalCurrency!=null)
                    {
                        try { NativeNpcServiceContractFunding.RestoreCurrency(game.PlayerEntity,originalCurrency); }
                        catch(Exception error) { failure="Exact original player currency restoration failed: "+error; }
                    }
                }
                if(inboxV5Verified && failure==null)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INBOX_V5_NATIVE_VERIFIED: source=actual-native-health; " +
                        "appointment=controlled-Core-attendance; deterministicAppraisal=true; " +
                        "boundedRetirement=true; pendingPinned=true; nativeFullSerializer=true; replayBlocked=true");
            }

            // The fixture is never saved after mutation; loading its exact named
            // baseline discards test-only potion, injury, actions and receipts.
            yield return ConversationMoodProbe.LoadSave(host,streets,character,originalSave,false);
            Require(streets.Ready,"Original fixture reload completed without a ready town population.");
            if(failure!=null)throw new InvalidOperationException("NPC healthcare probe failed before baseline restore: "+failure);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_ORIGINAL_BASELINE_RESTORED: character="+character+
                "; save="+originalSave+"; exactSaveLoaded=true; townReady=true");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INBOX_V5_NATIVE_PASS: source=actual-native-health; " +
                "appointment=controlled-Core-attendance; deterministicNoLLM=true; boundedRetirement=true; " +
                "pendingPinned=true; monotoneCounters=true; typedHistory=true; " +
                "nativeFullSerializer=true; replayBlocked=true; exactBaselineRestored=true");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_NATIVE_PASS: ownedPotion=true; actualDrinkPotion=true; " +
                "oneUnitStackConsumption=true; rollbackAfterNativeEffect=true; staleReplayRejected=true; " +
                "realPassiveHostileAlarm=true; alarmInterruptedCare=true; nativeMovementObserved=true; " +
                "careReplannedAfterAlarmExpiry=true; autonomousSufficientRecovery=true; careOwnershipReleased=true; ordinaryScheduleUnblocked=true; " +
                "noPostRecoveryConsumption=true; paidServicePausedBeforePotion=true; paidServiceBillingFrozen=true; " +
                "paidServiceResumedAfterReturn=true; exactPlayerCurrencyRestored=true; exactBaselineRestored=true");
        }

        private static IEnumerator RunAutonomousCare(StreetPopulation streets,ResidentActor selected,
            AutonomousCareResult result,PaidServiceEvidence service)
        {
            WorldStateStore store=null;ResidentActor actor=selected;NativeNpcVitalsBinding vitals=null;
            DaggerfallEntityBehaviour body=null;NativeNpcInventoryOwner owner=null;NpcArchetype archetype=NpcArchetype.Citizen;
            ulong sourceUid=0;string careTaskId=null,treatmentReceiptId=null;
            long observedInventoryRevision=0,recoveryGoalStart=0;
            NpcActorProgress originalInjury=null;
            int healthBefore=0,maximumHealth=0,needIntensityBefore=-1,needIntensityAfter=-1;
            int observedHealth=0,treatmentCount=0;
            IList<NpcInventoryItem> observedItems=null;
            var careTaskIds=new List<string>();
            var observedReceipts=new HashSet<string>(StringComparer.Ordinal);
            try
            {
                store=Store(streets);
                actor=streets.FindActor(selected.Resident.Id);
                owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
                vitals=actor?actor.GetComponent<NativeNpcVitalsBinding>():null;
                body=actor?actor.GetComponent<DaggerfallEntityBehaviour>():null;
                Require(store!=null && actor && actor.gameObject.activeInHierarchy && owner && owner.Binding!=null &&
                    vitals && body && body.Entity!=null,"Autonomous probe lost its selected live resident.");
                Require(body.Entity.Level>=1,"Ordinary resident retained invalid native level zero.");
                if(!NativeNpcHealingItem.TryFindOwned(owner.Binding,out sourceUid))
                {
                    Require(NativeNpcHealingItem.TryFindHealingPotionRecipeKey(out int recipeKey),
                        "DFU has no native HealHealth potion recipe for autonomous care.");
                    DaggerfallUnityItem seeded=ItemBuilder.CreatePotion(recipeKey,1);
                    Require(seeded!=null && NativeNpcHealingItem.IsSupportedPotion(seeded),
                        "Could not create one native potion for disposable autonomous-care setup.");
                    owner.Binding.Items.AddItem(seeded,noStack:true);
                    string seedFailure;
                    Require(NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out seedFailure),
                        "Could not capture the fixture-owned potion before injury: "+seedFailure);
                    sourceUid=seeded.UID;
                }

                DaggerfallUnityItem sourcePotion=owner.Binding.Items.GetItem(sourceUid);
                int minimumHeal=0;
                Require(sourcePotion!=null && NativeNpcHealingItem.IsSupportedPotion(sourcePotion) &&
                    NativeNpcHealingItem.TryEstimateHealthGain(sourcePotion,body.Entity,out minimumHeal) && minimumHeal>0,
                    "Selected fixture potion has no positive native HealHealth minimum.");

                archetype=Archetype(store,actor.Resident.Id);
                var savedVitals=store.ReadNpcVitals(actor.Resident.Id,archetype);
                Require(savedVitals.IsAlive && savedVitals.MaximumHealth==body.Entity.MaxHealth &&
                    savedVitals.CurrentHealth==body.Entity.CurrentHealth &&
                    savedVitals.MaximumMagicka==body.Entity.MaxMagicka &&
                    savedVitals.CurrentMagicka==body.Entity.CurrentMagicka,
                    "Autonomous probe lost its verified live native vitals baseline.");
                FundAndStartPaidService(store,actor);
                maximumHealth=body.Entity.MaxHealth;
                int preInjuryHealth=body.Entity.CurrentHealth;
                Require(preInjuryHealth>1,"Autonomous probe resident has no safe nonlethal injury range.");
                var progressBefore=store.ReadNpcProgress(actor.Resident.Id);
                long damageBefore=progressBefore==null?0:progressBefore.HealthEpisodeDamageTotal;
                int criticalHealth=Math.Max(1,maximumHealth/5);
                int damage=preInjuryHealth>criticalHealth?preInjuryHealth-criticalHealth:0;
                if(damage>0)body.DamageHealthFromSource((DaggerfallEntityBehaviour)null,damage,false,Vector3.zero);
                Require(body.Entity.CurrentHealth>0 && body.Entity.CurrentHealth*4<=maximumHealth,
                    "Could not establish a nonlethal critical-health state for autonomous care.");
                long requiredUnits=((long)maximumHealth-body.Entity.CurrentHealth+minimumHeal-1)/minimumHeal;
                Require(requiredUnits>0 && requiredUnits<=int.MaxValue,
                    "Fixture injury exceeds the finite native potion stack range.");
                if(sourcePotion.stackCount<requiredUnits)sourcePotion.stackCount=(int)requiredUnits;
                string inventoryFailure;
                Require(NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out inventoryFailure),
                    "Could not capture the finite fixture potion stock: "+inventoryFailure);
                Require(store.NpcInventoryItemOwner(sourceUid)==actor.Resident.Id &&
                    NativeNpcHealingItem.TryFindOwned(owner.Binding,out ulong selectedUid) && selectedUid==sourceUid,
                    "The ordinary coordinator cannot select the finite fixture potion stock.");
                vitals.Capture();
                savedVitals=store.ReadNpcVitals(actor.Resident.Id,archetype);
                Require(savedVitals.IsPersisted && savedVitals.Revision>0 &&
                    savedVitals.CurrentHealth==body.Entity.CurrentHealth &&
                    savedVitals.CurrentHealth*4<=savedVitals.MaximumHealth,
                    "Critical native injury was not committed before autonomous coordination.");
                originalInjury=store.ReadNpcProgress(actor.Resident.Id);
                Require(originalInjury!=null && originalInjury.HasPendingHealthEpisode &&
                    originalInjury.HealthEpisodeDamageTotal>damageBefore &&
                    originalInjury.HealthEpisodeLastVitalsRevision==savedVitals.Revision &&
                    originalInjury.HealthEpisodeFinalHealth==savedVitals.CurrentHealth,
                    "Actual fixture injury did not enter the native health episode before alarm and care.");
                healthBefore=savedVitals.CurrentHealth;
                observedHealth=healthBefore;
                var inventoryBeforeCare=store.ReadNpcInventory(actor.Resident.Id);
                observedItems=inventoryBeforeCare.Items;
                observedInventoryRevision=inventoryBeforeCare.Revision;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_START: resident="+selected.Resident.Id+
                    "; health="+healthBefore+"/"+maximumHealth+"; sourceUid="+sourceUid+
                    "; nativeLevel="+body.Entity.Level+"; minimumPotionGain="+minimumHeal);
            }
            catch(Exception error) {result.Failure=error.Message;}
            if(result.Failure!=null)yield break;

            var alarmCare=new AlarmCareResult();
            yield return RunAlarmInterruptedCare(streets,actor,store,archetype,sourceUid,
                healthBefore,observedItems,alarmCare,service);
            if(alarmCare.Failure!=null) {result.Failure=alarmCare.Failure;yield break;}
            careTaskId=alarmCare.TaskId;
            careTaskIds.Add(careTaskId);
            recoveryGoalStart=alarmCare.GoalStartRevision;
            needIntensityBefore=alarmCare.NeedIntensity;

            float deadline=Time.realtimeSinceStartup+240f;
            float nextTreatmentStateLog=Time.realtimeSinceStartup+10f;
            while(Time.realtimeSinceStartup<deadline && !result.Completed && result.Failure==null)
            {
                try
                {
                    TickPopulation(streets);
                    actor=streets.FindActor(selected.Resident.Id);
                    var care=store.ReadNpcHealthCare(selected.Resident.Id);
                    if(care!=null && care.Phase!=NpcHealthCarePhase.Recovered)
                        RequirePausedService(store,service,"autonomous care before sufficient recovery");
                    if(care!=null && care.Phase==NpcHealthCarePhase.Acting && !string.IsNullOrWhiteSpace(care.TaskId))
                    {
                        careTaskId=care.TaskId;
                        if(!careTaskIds.Contains(care.TaskId))careTaskIds.Add(care.TaskId);
                    }
                    NpcActionJob mostRecent=store.LatestNpcAction(selected.Resident.Id);
                    int stepAt=mostRecent==null || string.IsNullOrEmpty(mostRecent.Id)?-1:
                        mostRecent.Id.IndexOf(":step:",StringComparison.Ordinal);
                    if(care!=null && stepAt>0)
                    {
                        string latestTaskId=mostRecent.Id.Substring(0,stepAt);
                        NpcTaskPlan latestTask=store.ReadNpcTask(latestTaskId);
                        if(latestTaskId.StartsWith("npc-health-care:"+care.Id+":attempt:",StringComparison.Ordinal) &&
                            latestTask!=null && latestTask.ResidentId==selected.Resident.Id &&
                            !careTaskIds.Contains(latestTaskId))careTaskIds.Add(latestTaskId);
                    }

                    var motivation=store.ReadNpcMotivationState(selected.Resident.Id);
                    NpcMotivationNeedView need=FindHealthNeed(motivation);
                    NpcMotivationGoalView goal=FindRecoveryGoal(motivation);
                    // The ordinary coordinator has a half-second sampling lease.
                    // A first frame before that lease expires is not a failure.
                    if(needIntensityBefore<0 && need!=null && goal!=null)
                    {
                        if(need==null || need.Kind!=NpcMotivationNeedKind.Health || !need.Intensity.HasValue ||
                            need.Intensity.Value<=0 ||
                            need.EvidenceKind!=NpcMotivationStateEvidenceKind.NativeObservation ||
                            goal==null || goal.Kind!=NpcMotivationGoalKind.Recovery ||
                            goal.Status!=NpcMotivationGoalStatus.Active || goal.Priority!=NpcMotivationPriorityLevel.Emergency)
                            throw new InvalidOperationException("Ordinary population update did not persist the emergency health need and recovery goal.");
                        needIntensityBefore=need.Intensity.Value;
                        Require(goal.GoalStartRevision>0,"Recovery goal has no persisted activation generation.");
                        recoveryGoalStart=goal.GoalStartRevision;
                    }

                    if(recoveryGoalStart>0)
                        Require(goal!=null && goal.GoalStartRevision==recoveryGoalStart,
                            "Partial treatment changed the recovery goal activation generation.");

                    foreach(string taskId in careTaskIds)
                    {
                        NpcActionJob latest=store.ReadNpcAction(taskId+":step:0");
                        if(latest==null || latest.Kind!=NpcActionKind.UseHealingItem ||
                            latest.Phase!=NpcActionPhase.Completed || latest.TreatmentReceipt==null ||
                            observedReceipts.Contains(latest.TreatmentReceipt.TransactionId))continue;
                        NpcTaskPlan treatedTask=store.ReadNpcTask(taskId);
                        Require(treatedTask!=null && treatedTask.GoalId==NpcHealthNeedLifecycle.RecoveryGoalId &&
                            recoveryGoalStart>0 && treatedTask.GoalStartRevision==recoveryGoalStart,
                            "Autonomous treatment task is not bound to its actual recovery goal episode.");
                        var afterVitals=store.ReadNpcVitals(selected.Resident.Id,archetype);
                        var afterProgress=store.ReadNpcProgress(selected.Resident.Id);
                        var afterInventory=store.ReadNpcInventory(selected.Resident.Id);
                        bool oneUnit=NativeNpcHealingItem.IsExactlyOneUnitConsumed(
                            observedItems,afterInventory.Items,sourceUid);
                        bool injuryPreserved=treatmentCount!=0 ||
                            OriginalInjuryPreserved(store,selected.Resident.Id,originalInjury,afterProgress);
                        bool exactReceipt=latest.TreatmentReceipt.Kind==NpcTreatmentKind.HealingItem &&
                            latest.TreatmentReceipt.SourceItemUid==sourceUid &&
                            latest.TreatmentReceipt.TransactionId==latest.Id+":treatment" &&
                            latest.CompletedEffectId==latest.Id+":observed" &&
                            latest.TreatmentReceipt.HealthBefore==observedHealth &&
                            latest.TreatmentReceipt.HealthAfter==afterVitals.CurrentHealth &&
                            afterVitals.CurrentHealth>observedHealth &&
                            oneUnit &&
                            afterInventory.Revision==observedInventoryRevision+1 &&
                            afterInventory.LastTransactionId==latest.TreatmentReceipt.TransactionId &&
                            afterProgress!=null &&
                            afterProgress.HasPendingHealthEpisode &&
                            afterProgress.HealthEpisodeHealingTotal>0 &&
                            afterProgress.HealthEpisodeLastVitalsRevision==afterVitals.Revision &&
                            injuryPreserved;
                        if(!exactReceipt)
                            throw new InvalidOperationException("Autonomous care did not consume exactly one actual owned potion and commit its native healing receipt. "+
                                "receiptKind="+latest.TreatmentReceipt.Kind+
                                "; uid="+latest.TreatmentReceipt.SourceItemUid+"/"+sourceUid+
                                "; transaction="+latest.TreatmentReceipt.TransactionId+"/"+latest.Id+":treatment"+
                                "; effect="+latest.CompletedEffectId+"/"+latest.Id+":observed"+
                                "; hp="+latest.TreatmentReceipt.HealthBefore+"->"+latest.TreatmentReceipt.HealthAfter+
                                    "/"+observedHealth+"->"+afterVitals.CurrentHealth+
                                "; oneUnit="+oneUnit+"; itemCounts="+observedItems.Count+"->"+afterInventory.Items.Count+
                                "; inventoryRevision="+observedInventoryRevision+"->"+afterInventory.Revision+
                                "; lastTransaction="+afterInventory.LastTransactionId+
                                "; progressDamage="+originalInjury.HealthEpisodeDamageTotal+"->"+
                                    (afterProgress==null?-1:afterProgress.HealthEpisodeDamageTotal)+
                                "; progressHealing="+(afterProgress==null?-1:afterProgress.HealthEpisodeHealingTotal)+
                                "; injuryProjectedThrough="+(afterProgress==null?-1:afterProgress.HealthExperienceProjectedThroughVitalsRevision)+
                                "; injuryRevision="+originalInjury.HealthEpisodeLastVitalsRevision+
                                "; injuryPreserved="+injuryPreserved+".");
                        if(actor && actor.gameObject.activeInHierarchy)
                        {
                            var liveBody=actor.GetComponent<DaggerfallEntityBehaviour>();
                            Require(liveBody && liveBody.Entity!=null &&
                                liveBody.Entity.CurrentHealth==afterVitals.CurrentHealth,
                                "Autonomous receipt does not match the live native resident's health.");
                        }
                        observedHealth=afterVitals.CurrentHealth;
                        observedItems=afterInventory.Items;
                        observedInventoryRevision=afterInventory.Revision;
                        observedReceipts.Add(latest.TreatmentReceipt.TransactionId);
                        treatmentCount++;
                        treatmentReceiptId=latest.TreatmentReceipt.TransactionId;
                        if(!NpcHealthNeedLifecycle.HasRecoveredForWork(afterVitals) ||
                            store.ActiveNpcAction(selected.Resident.Id)!=null)
                            RequirePausedService(store,service,"autonomous potion treatment");
                        service.StableDuringTreatment=true;
                        if(NpcHealthNeedLifecycle.HasRecoveredForWork(afterVitals) && result.RecoverySnapshot==null)
                        {
                            Require(afterProgress.HasPendingHealthEpisode &&
                                afterProgress.HealthEpisodeLastVitalsRevision==afterVitals.Revision,
                                "Actual final potion receipt did not leave a pending native health episode for source projection.");
                            result.RecoverySnapshot=store.Snapshot();
                            result.RecoveryPersonId=selected.Resident.Id;
                        }
                    }
                    var recoveredVitals=store.ReadNpcVitals(selected.Resident.Id,archetype);
                    if(Time.realtimeSinceStartup>=nextTreatmentStateLog)
                    {
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_TREATMENT_STATE: observed="+treatmentCount+
                            "; tasks="+careTaskIds.Count+"; generation="+recoveryGoalStart+"; "+
                            DescribeCareHandoff(streets,store,selected.Resident.Id,archetype));
                        nextTreatmentStateLog=Time.realtimeSinceStartup+10f;
                    }
                    if(treatmentCount>0 && NpcHealthNeedLifecycle.HasRecoveredForWork(recoveredVitals))
                    {
                        Require(recoveredVitals.CurrentHealth==observedHealth,
                            "Resident reached sufficient health without a matching actual potion receipt.");
                        Require(recoveredVitals.CurrentHealth<recoveredVitals.MaximumHealth,
                            "Fixture must leave missing health for later free rest instead of using potions to full health.");
                        Require(treatmentCount>=2,
                            "Fixture reached sufficient health without exercising repeated autonomous potion treatment.");
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_SUFFICIENT_HEALTH_STATE: "+
                            DescribeCareHandoff(streets,store,selected.Resident.Id,archetype));
                        result.Completed=true;
                    }
                }
                catch(Exception error) {result.Failure=error.Message;}
                if(!result.Completed && result.Failure==null)yield return null;
            }
            if(!result.Completed && result.Failure==null)
                result.Failure="Autonomous care did not reach two-thirds native health through owned potion receipts within 240 seconds. "+
                    "Observed treatments="+treatmentCount+"; taskIds="+careTaskIds.Count+"; goalGeneration="+recoveryGoalStart+
                    "; "+DescribeCareHandoff(streets,store,selected.Resident.Id,archetype);
            if(result.Completed)
            {
                bool careOwnershipReleased=false,routineReturnComplete=false;
                float restoreDeadline=Time.realtimeSinceStartup+120f;
                float nextRouteStateLog=Time.realtimeSinceStartup+10f;
                while(Time.realtimeSinceStartup<restoreDeadline && !routineReturnComplete && result.Failure==null)
                {
                    try
                    {
                        TickPopulation(streets);
                        var care=store.ReadNpcHealthCare(selected.Resident.Id);
                        NpcTaskPlan active=store.ActiveNpcTask(selected.Resident.Id);
                        NpcActionJob activeAction=store.ActiveNpcAction(selected.Resident.Id);
                        careOwnershipReleased=care!=null && care.Phase==NpcHealthCarePhase.Recovered &&
                            care.TaskId==null && active==null;
                        if(activeAction!=null)
                        {
                            RequirePausedService(store,service,"physical return to ordinary routine");
                            if(care!=null && care.Phase==NpcHealthCarePhase.Recovered &&
                                activeAction.ReturningToRoutine)
                                service.ReturnObservedWhilePaused=true;
                        }
                        var paid=store.ReadNpcServiceContracts().Read(PaidServiceContractId);
                        if(paid!=null && paid.Status==NpcServiceContractStatus.Active)
                            Require(careOwnershipReleased && activeAction==null,
                                "Paid service resumed before care ownership and physical return were released.");
                        routineReturnComplete=careOwnershipReleased && activeAction==null &&
                            PaidServiceResumedWithoutPauseBilling(paid,service);
                        if(routineReturnComplete)
                        {
                            NpcMotivationGoalView recoveredGoal=FindRecoveryGoal(store.ReadNpcMotivationState(selected.Resident.Id));
                            Require(recoveredGoal!=null && recoveredGoal.GoalStartRevision==recoveryGoalStart &&
                                recoveredGoal.Status==(store.ReadNpcVitals(selected.Resident.Id,archetype).CurrentHealth==
                                    store.ReadNpcVitals(selected.Resident.Id,archetype).MaximumHealth?
                                    NpcMotivationGoalStatus.Fulfilled:NpcMotivationGoalStatus.Active),
                                "Observed sufficient recovery did not fulfill the same bound goal episode.");
                            NpcMotivationNeedView need=FindHealthNeed(store.ReadNpcMotivationState(selected.Resident.Id));
                            Require(need!=null && need.Intensity.HasValue &&
                                need.EvidenceKind==NpcMotivationStateEvidenceKind.NativeObservation &&
                                need.Intensity.Value<needIntensityBefore,
                                "Sufficient recovery did not lower the observed native Health need.");
                            needIntensityAfter=need.Intensity.Value;
                            Require(service.PausedBeforeTreatment && service.StableDuringAlarm &&
                                service.StableDuringTreatment && service.ReturnObservedWhilePaused,
                                "Paid service did not prove pause, frozen billing, and physical return before resumption.");
                        }
                        else if(Time.realtimeSinceStartup>=nextRouteStateLog)
                        {
                            nextRouteStateLog=Time.realtimeSinceStartup+10f;
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_RETURN_STATE: "+
                                DescribeCareHandoff(streets,store,selected.Resident.Id,archetype));
                        }
                    }
                    catch(Exception error) {result.Failure=error.Message;}
                    if(!routineReturnComplete && result.Failure==null)yield return null;
                }
                if(!routineReturnComplete && result.Failure==null)
                {
                    string handoff=DescribeCareHandoff(streets,store,selected.Resident.Id,archetype);
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_RELEASE_TIMEOUT: "+handoff);
                    result.Failure=careOwnershipReleased?
                        "Care ownership released, but the physical routine return did not complete within 120 seconds. "+handoff:
                        "Sufficient health did not release autonomous care ownership within 120 seconds. "+handoff;
                }
                if(result.Failure==null)
                {
                    NpcInventoryState inventoryAtRecovery=null;
                    NpcVitalsView vitalsAtRecovery=null;
                    try
                    {
                        inventoryAtRecovery=store.ReadNpcInventory(selected.Resident.Id);
                        vitalsAtRecovery=store.ReadNpcVitals(selected.Resident.Id,archetype);
                        Require(inventoryAtRecovery.Revision==observedInventoryRevision &&
                            SameInventoryItems(inventoryAtRecovery.Items,observedItems),
                            "A potion or inventory item changed after the final recovery receipt.");
                    }
                    catch(Exception error) {result.Failure=error.Message;}
                    float ordinaryUntil=Time.realtimeSinceStartup+1.25f;
                    while(Time.realtimeSinceStartup<ordinaryUntil && result.Failure==null)
                    {
                        try
                        {
                            TickPopulation(streets);
                            var care=store.ReadNpcHealthCare(selected.Resident.Id);
                            var inventory=store.ReadNpcInventory(selected.Resident.Id);
                            var observedVitals=store.ReadNpcVitals(selected.Resident.Id,archetype);
                            Require(care!=null && care.Phase==NpcHealthCarePhase.Recovered && care.TaskId==null &&
                                store.ActiveNpcTask(selected.Resident.Id)==null &&
                                store.ActiveNpcAction(selected.Resident.Id)==null &&
                                inventory.Revision==inventoryAtRecovery.Revision &&
                                SameInventoryItems(inventory.Items,inventoryAtRecovery.Items) &&
                                NpcHealthNeedLifecycle.HasRecoveredForWork(observedVitals) &&
                                observedVitals.CurrentHealth==vitalsAtRecovery.CurrentHealth,
                                "Ordinary updates resumed care or consumed another potion after sufficient recovery.");
                        }
                        catch(Exception error) {result.Failure=error.Message;}
                        if(result.Failure==null)yield return null;
                    }
                    if(result.Failure==null)
                    {
                        try
                        {
                            Require(OrdinaryScheduleUnblocked(streets,selected.Resident.Id),
                                "Recovered resident's ordinary schedule remains blocked by an action overlay.");
                            Require(PaidServiceResumedWithoutPauseBilling(
                                store.ReadNpcServiceContracts().Read(PaidServiceContractId),service),
                                "Paid service lost its no-billing health pause after ordinary updates.");
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_AUTONOMOUS_PASS: resident="+selected.Resident.Id+
                                "; sourceUid="+sourceUid+"; before="+healthBefore+"; after="+vitalsAtRecovery.CurrentHealth+
                                "; needIntensity="+needIntensityBefore+"->"+needIntensityAfter+
                                "; treatments="+treatmentCount+"; careTask="+careTaskId+"; receipt="+treatmentReceiptId+
                                "; sufficientRecovery=true; careReleased=true; ordinaryScheduleUnblocked=true; extraConsumption=false");
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_ROUTINE_UNBLOCKED: resident="+selected.Resident.Id+
                                "; carePhase=Recovered; activeCareTask=false; activeCareAction=false");
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_GOAL_BINDING_PASS: generation="+
                                recoveryGoalStart+"; treatments="+treatmentCount+"; stableThroughRecovery=true");
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_PAID_SERVICE_PASS: resident="+
                                selected.Resident.Id+"; contract="+PaidServiceContractId+
                                "; realPlayerFunding=true; pausedBeforePotion=true; alarmAndTreatmentBillingFrozen=true; "+
                                "physicalReturnObservedWhilePaused=true; resumedAfterPhysicalReturn=true; "+
                                "pausedTimeNotBilled=true");
                        }
                        catch(Exception error) {result.Failure=error.Message;}
                    }
                }
            }
        }

        private static IEnumerator RunAlarmInterruptedCare(StreetPopulation streets,ResidentActor actor,
            WorldStateStore store,NpcArchetype archetype,ulong sourceUid,int healthBefore,
            IList<NpcInventoryItem> itemsBefore,AlarmCareResult result,PaidServiceEvidence service)
        {
            GameObject foe=null;
            var guards=streets.GetComponent<GuardPopulation>();
            bool oldDefense=guards && guards.EnemyDefense;
            bool oldReactions=streets.ReactionsEnabled;
            CivilianIncident alarm=null;
            Vector3 initialPosition=actor.transform.position;
            string id=actor.Resident.Id;
            try
            {
                Require(guards && GameManager.Instance.PlayerGPS.HasCurrentLocation &&
                    store.ActiveAlarm(id,GameTimeSeconds())==null && !actor.IsSleeping && !actor.IsConversing,
                    "Controlled care-alarm fixture needs an awake resident without a prior alarm.");
                object plan=FindPopulationPlan(streets,id);
                FieldInfo checksField=typeof(StreetPopulation).GetField("healthCareChecks",Private);
                MethodInfo prepare=typeof(StreetPopulation).GetMethod("PrepareNpcHealthCare",Private);
                Require(checksField!=null && prepare!=null,"Native care coordinator hook is unavailable.");
                var checks=checksField.GetValue(streets) as IDictionary;
                Require(checks!=null,"Native care sampling lease is unavailable.");
                checks.Remove(id);
                var nativeTime=DaggerfallUnity.Instance.WorldTime.Now;
                double now=nativeTime.ToSeconds()+(nativeTime.Second-Math.Floor(nativeTime.Second));
                object[] args={plan,actor,now};
                bool prepared;
                try {prepared=(bool)prepare.Invoke(streets,args);}
                catch(TargetInvocationException error) {throw error.InnerException??error;}
                Require(prepared,"Native coordinator did not begin the fixture's critical care task.");
                var care=store.ReadNpcHealthCare(id);
                var task=care==null?null:store.ReadNpcTask(care.TaskId);
                var action=task==null?null:store.ReadNpcAction(task.ActiveActionId);
                var motivation=store.ReadNpcMotivationState(id);
                var goal=FindRecoveryGoal(motivation);
                var need=FindHealthNeed(motivation);
                Require(care!=null && care.Phase==NpcHealthCarePhase.Acting && task!=null &&
                    task.Phase==NpcTaskPhase.Running && action!=null &&
                    action.Kind==NpcActionKind.UseHealingItem && !NpcActionExecutor.IsTerminal(action) &&
                    action.TreatmentReceipt==null && goal!=null &&
                    goal.Status==NpcMotivationGoalStatus.Active && goal.GoalStartRevision>0 &&
                    need!=null && need.Intensity.HasValue && need.Intensity.Value>0,
                    "Initial care task was not running before native alarm observation.");
                NpcServiceContract paused=store.ReadNpcServiceContracts().Read(PaidServiceContractId);
                Require(paused!=null && paused.Status==NpcServiceContractStatus.Suspended &&
                    paused.SuspensionOwnerKind==NpcServiceSuspensionOwnerKind.HealthCare &&
                    paused.SuspensionOwnerId==care.Id && paused.TransferReceipts.Count==1 &&
                    paused.TransferReceipts[0].ReceiptId==PaidServiceFundingId &&
                    paused.ReleasedGold==0 && paused.RefundedGold==0 &&
                    paused.EscrowGold==PaidServiceRate,
                    "Ordinary PrepareNpcHealthCare did not pause the paid service before UseHealingItem.");
                service.Paused=paused;
                service.CareId=care.Id;
                service.PausedBeforeTreatment=true;
                result.TaskId=task.Id;
                result.GoalStartRevision=goal.GoalStartRevision;
                result.NeedIntensity=need.Intensity.Value;

                Vector3[] directions={Vector3.forward,Vector3.right,Vector3.back,Vector3.left};
                UnityEngine.Random.State randomState=UnityEngine.Random.state;
                uint nativeSeed=DFRandom.Seed;
                try
                {
                    GameObject[] spawned=GameObjectHelper.CreateFoeGameObjects(
                        actor.transform.position+Vector3.forward*3,MobileTypes.Thief,1,MobileReactions.Passive);
                    Require(spawned!=null && spawned.Length==1 && spawned[0],
                        "Controlled passive Thief could not be spawned for actual LOS alarm.");
                    foe=spawned[0];
                }
                finally {UnityEngine.Random.state=randomState;DFRandom.Seed=nativeSeed;}
                var motor=foe.GetComponent<EnemyMotor>();
                var senses=foe.GetComponent<EnemySenses>();
                var attack=foe.GetComponent<EnemyAttack>();
                var target=foe.GetComponent<DaggerfallEntityBehaviour>();
                Require(motor && senses && attack && target,
                    "Controlled passive Thief lacks production threat components.");
                motor.enabled=false;senses.enabled=false;attack.enabled=false;
                foe.SetActive(true);
                bool visible=false;
                foreach(Vector3 direction in directions)
                {
                    foe.transform.position=actor.transform.position+direction*3;
                    Physics.SyncTransforms();
                    if(!CivilianResponses.Sees(actor.transform.position,foe.transform.position,20))continue;
                    visible=true;break;
                }
                Require(visible,"Controlled passive Thief has no real unobstructed LOS to the care resident.");
                motor.IsHostile=true;
                guards.EnemyDefense=false;
                streets.ReactionsEnabled=true;
                string threatId=guards.RegisterHostileThreat(target);
                Require(!string.IsNullOrWhiteSpace(threatId),
                    "Production hostile registry rejected the controlled passive Thief.");
                int settlementId=GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId;
                var responses=new CivilianResponses(store,settlementId);
                responses.Threat(target,threatId,new[] {actor},now,-1,ignored=>false);
                alarm=store.ActiveAlarm(id,now);
                AlarmPerson witness=alarm==null?null:alarm.People.Find(p=>p.ResidentId==id);
                Require(alarm!=null && alarm.ThreatId==threatId && witness!=null &&
                    witness.DirectWitness && witness.Reaction==CivilianReaction.Flee && streets.ReactionsEnabled,
                    "Real LOS observation did not create a direct native flight alarm.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_ALARM_START: resident="+id+
                    "; runningTask="+task.Id+"; action="+action.Id+"; threat="+threatId+
                    "; controlledPassiveThief=true; productionLOS=true");
            }
            catch(Exception error) {result.Failure=error.Message;}

            try
            {
                if(result.Failure==null)
                {
                    float until=Time.realtimeSinceStartup+8f;
                    bool moved=false;
                    while(Time.realtimeSinceStartup<until && !moved && result.Failure==null)
                    {
                        try
                        {
                            TickPopulation(streets);
                            var first=store.ReadNpcAction(result.TaskId+":step:0");
                            var latest=store.LatestNpcAction(id);
                            var live=streets.FindActor(id);
                            var vitals=store.ReadNpcVitals(id,archetype);
                            var inventory=store.ReadNpcInventory(id);
                            var liveOwner=live?live.GetComponent<NativeNpcInventoryOwner>():null;
                            ulong stillOwned;
                            bool nativePotionUnchanged=!live || liveOwner && liveOwner.Binding!=null &&
                                NativeNpcHealingItem.TryFindOwned(liveOwner.Binding,out stillOwned) && stillOwned==sourceUid;
                            Require(first!=null && NpcActionExecutor.IsTerminal(first) &&
                                first.Phase!=NpcActionPhase.Completed &&
                                first.TreatmentReceipt==null && latest!=null && latest.Id==first.Id &&
                                store.ActiveNpcAction(id)==null && vitals.CurrentHealth==healthBefore &&
                                SameInventoryItems(inventory.Items,itemsBefore) && nativePotionUnchanged &&
                                store.ActiveAlarm(id,ExactGameTimeSeconds())!=null,
                                "Alarm dispatched treatment or consumed a potion instead of yielding movement.");
                            RequirePausedService(store,service,"native alarm movement");
                            service.StableDuringAlarm=true;
                            var active=store.ActiveAlarm(id,ExactGameTimeSeconds());
                            var person=active.People.Find(p=>p.ResidentId==id);
                            moved=person.ReachedRefuge || live &&
                                Vector3.Distance(initialPosition,live.transform.position)>0.25f;
                        }
                        catch(Exception error) {result.Failure=error.Message;}
                        if(!moved && result.Failure==null)yield return null;
                    }
                    if(!moved && result.Failure==null)
                        result.Failure="Real native flight did not move or reach refuge while care was interrupted.";
                }
            }
            finally
            {
                if(foe) {foe.SetActive(false);UnityEngine.Object.Destroy(foe);}
                if(guards)guards.EnemyDefense=oldDefense;
                streets.ReactionsEnabled=oldReactions;
            }
            if(result.Failure!=null)yield break;
            try
            {
                double now=ExactGameTimeSeconds();
                CivilianIncident latestAlarm=store.ActiveAlarm(id,now);
                Require(latestAlarm!=null && latestAlarm.Id==alarm.Id,
                    "Observed care alarm disappeared before native expiry check.");
                double expiry=CivilianAlarms.ActiveUntil(latestAlarm);
                Require(expiry>=now && expiry-now<float.MaxValue-2,
                    "Native alarm expiry is not representable in disposable fixture time.");
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(expiry-now+1));
                Require(store.ActiveAlarm(id,ExactGameTimeSeconds())==null && !streets.IsAlarmed(id),
                    "Real native alarm remained active after its persisted expiry.");
                RequirePausedService(store,service,"native alarm expiry");
            }
            catch(Exception error) {result.Failure=error.Message;}
            if(result.Failure!=null)yield break;
            NpcHealthCareRecord blockedCare=null;
            float retryObservationUntil=Time.realtimeSinceStartup+10f;
            while(Time.realtimeSinceStartup<retryObservationUntil && blockedCare==null && result.Failure==null)
            {
                try
                {
                    TickPopulation(streets);
                    var care=store.ReadNpcHealthCare(id);
                    RequirePausedService(store,service,"blocked alarm retry");
                    if(care!=null && care.Phase==NpcHealthCarePhase.Blocked)blockedCare=care;
                }
                catch(Exception error) {result.Failure=error.Message;}
                if(blockedCare==null && result.Failure==null)yield return null;
            }
            if(blockedCare==null && result.Failure==null)
                result.Failure="Interrupted native care did not enter actual Blocked retry after alarm expiry.";
            if(result.Failure!=null)yield break;
            try
            {
                double now=ExactGameTimeSeconds();
                Require(blockedCare.RetryAt-now<float.MaxValue-2,
                    "Observed care retry deadline is not representable in fixture game time.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_ALARM_RETRY: resident="+id+
                    "; actualGameTime="+now.ToString("R",CultureInfo.InvariantCulture)+
                    "; observedRetryAt="+blockedCare.RetryAt.ToString("R",CultureInfo.InvariantCulture)+
                    "; savedCarePosition="+(blockedCare.PausedPosition==null?"none":
                        blockedCare.PausedPosition.Kind+"/("+
                        blockedCare.PausedPosition.X.ToString("R",CultureInfo.InvariantCulture)+","+
                        blockedCare.PausedPosition.Y.ToString("R",CultureInfo.InvariantCulture)+","+
                        blockedCare.PausedPosition.Z.ToString("R",CultureInfo.InvariantCulture)+")"));
                if(blockedCare.RetryAt>now)
                    DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(blockedCare.RetryAt-now+1));
                RequirePausedService(store,service,"native care retry clock advance");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_ALARM_CLEAR: resident="+id+
                    "; originalTask="+result.TaskId+
                    "; actualAlarmExpired=true; blockedRetryObserved=true; nativeMovementObserved=true; potionUnconsumed=true");
            }
            catch(Exception error) {result.Failure=error.Message;}
        }

        private static object FindPopulationPlan(StreetPopulation streets,string residentId)
        {
            FieldInfo plansField=typeof(StreetPopulation).GetField("plans",Private);
            IList plans=plansField==null?null:plansField.GetValue(streets) as IList;
            if(plans==null)throw new MissingMemberException("Ordinary population plans are unavailable.");
            foreach(object plan in plans)
            {
                FieldInfo residentField=plan.GetType().GetField("Resident");
                Resident resident=residentField==null?null:residentField.GetValue(plan) as Resident;
                if(resident!=null && resident.Id==residentId)return plan;
            }
            throw new InvalidOperationException("Care resident has no ordinary population plan.");
        }

        private static bool SameInventoryItems(IList<NpcInventoryItem> left,IList<NpcInventoryItem> right)
        {
            if(left==null || right==null || left.Count!=right.Count)return false;
            for(int i=0;i<left.Count;i++)
                if(left[i]==null || right[i]==null || left[i].Uid!=right[i].Uid ||
                    left[i].NativePayload!=right[i].NativePayload)return false;
            return true;
        }

        private static string DescribeCareHandoff(StreetPopulation streets,WorldStateStore store,
            string residentId,NpcArchetype archetype)
        {
            try
            {
                var care=store.ReadNpcHealthCare(residentId);
                var careTask=care==null || care.TaskId==null?null:store.ReadNpcTask(care.TaskId);
                var activeTask=store.ActiveNpcTask(residentId);
                var action=store.ActiveNpcAction(residentId);
                var latest=store.LatestNpcAction(residentId);
                var vitals=store.ReadNpcVitals(residentId,archetype);
                var actor=streets.FindActor(residentId);
                string actorPosition=actor && actor.gameObject.activeInHierarchy?
                    (actor.transform.position-Vector3.up).ToString():"not materialized";
                return "resident="+residentId+
                    "; hp="+vitals.CurrentHealth+"/"+vitals.MaximumHealth+
                    "; carePhase="+(care==null?"none":care.Phase.ToString())+
                    "; careTaskId="+(care==null?"none":care.TaskId??"none")+
                    "; careTaskPhase="+(careTask==null?"none":careTask.Phase.ToString())+
                    "; activeTask="+(activeTask==null?"none":activeTask.Id+":"+activeTask.Phase)+
                    "; activeAction="+DescribeCareAction(action)+
                    "; latestAction="+DescribeCareAction(latest)+
                    "; actorPosition="+actorPosition;
            }
            catch(Exception error)
            {return "diagnosticError="+error.GetType().Name+": "+error.Message;}
        }

        private static string DescribeCareAction(NpcActionJob action)
        {
            if(action==null)return "none";
            NpcActionTarget position=action.LastPosition;
            return action.Id+":"+action.Kind+":"+action.Phase+
                ":returning="+action.ReturningToRoutine+
                ":lastPosition="+(position==null?"none":position.Kind+"/"+position.WorldSpaceId+"/"+
                    position.SettlementId+"/"+position.BuildingKey+"/("+
                    position.X.ToString("R",CultureInfo.InvariantCulture)+","+
                    position.Y.ToString("R",CultureInfo.InvariantCulture)+","+
                    position.Z.ToString("R",CultureInfo.InvariantCulture)+")");
        }

        private static bool OrdinaryScheduleUnblocked(StreetPopulation streets,string residentId)
        {
            FieldInfo plansField=typeof(StreetPopulation).GetField("plans",Private);
            MethodInfo overlay=typeof(StreetPopulation).GetMethod("ApplyNpcActionOverlay",Private);
            IList plans=plansField==null?null:plansField.GetValue(streets) as IList;
            if(plans==null || overlay==null)
                throw new MissingMemberException("Ordinary population schedule or action overlay is unavailable.");
            foreach(object plan in plans)
            {
                FieldInfo residentField=plan.GetType().GetField("Resident");
                Resident resident=residentField==null?null:residentField.GetValue(plan) as Resident;
                if(resident==null || resident.Id!=residentId)continue;
                object[] args={plan,streets.FindActor(residentId),GameTimeSeconds(),null};
                try {return !(bool)overlay.Invoke(streets,args);}
                catch(TargetInvocationException error) {throw error.InnerException??error;}
            }
            throw new InvalidOperationException("Recovered resident has no ordinary population plan.");
        }

        private static void TickPopulation(StreetPopulation streets)
        {
            MethodInfo tick=typeof(StreetPopulation).GetMethod("UpdatePopulation",Private);
            if(tick==null)throw new MissingMethodException("StreetPopulation.UpdatePopulation runtime loop is unavailable.");
            try {tick.Invoke(streets,null);}
            catch(TargetInvocationException error)
            {throw error.InnerException??error;}
        }

        private static bool OriginalInjuryPreserved(WorldStateStore store,string residentId,
            NpcActorProgress injury,NpcActorProgress current)
        {
            if(injury==null || current==null)return false;
            if(current.HasPendingHealthEpisode &&
                current.HealthEpisodeFirstVitalsRevision==injury.HealthEpisodeFirstVitalsRevision &&
                current.HealthEpisodeLastVitalsRevision>=injury.HealthEpisodeLastVitalsRevision &&
                current.HealthEpisodeInitialHealth==injury.HealthEpisodeInitialHealth &&
                current.HealthEpisodeDamageTotal>=injury.HealthEpisodeDamageTotal)return true;
            if(current.HealthExperienceProjectedThroughVitalsRevision<injury.HealthEpisodeLastVitalsRevision)
                return false;
            var snapshot=store.Snapshot();
            if(snapshot.NpcExperienceInbox==null || snapshot.NpcExperienceInbox.Entries==null)return false;
            foreach(NpcExperienceInboxEntry evidence in snapshot.NpcExperienceInbox.Entries)
                if(evidence!=null && evidence.Origin==NpcExperienceEventOrigin.NativeOutcome &&
                    evidence.ObserverId==residentId && evidence.SubjectPersonId==residentId &&
                    evidence.EventKind==NpcExperienceEventKind.Injury &&
                    evidence.ObservedGameTime==injury.HealthEpisodeLastChangedAt &&
                    evidence.HealthInitial==injury.HealthEpisodeInitialHealth &&
                    evidence.HealthFinal==injury.HealthEpisodeFinalHealth &&
                    evidence.HealthMaximum==injury.HealthEpisodeMaximumHealth &&
                    evidence.HealthDamageTotal==injury.HealthEpisodeDamageTotal &&
                    evidence.HealthHealingTotal==injury.HealthEpisodeHealingTotal &&
                    evidence.HealthChangeCount==injury.HealthEpisodeChangeCount)return true;
            return false;
        }

        private static NpcMotivationNeedView FindHealthNeed(NpcMotivationStateView state)
        {
            if(state==null)return null;
            foreach(NpcMotivationNeedView need in state.Needs)
                if(need.Id==NpcHealthNeedLifecycle.HealthNeedId)return need;
            return null;
        }

        private static NpcMotivationGoalView FindRecoveryGoal(NpcMotivationStateView state)
        {
            if(state==null)return null;
            foreach(NpcMotivationGoalView goal in state.Goals)
                if(goal.Id==NpcHealthNeedLifecycle.RecoveryGoalId)return goal;
            return null;
        }

        private static void RunNativeChecks(StreetPopulation streets,ResidentActor actor)
        {
            WorldStateStore store=Store(streets);
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            var vitals=actor.GetComponent<NativeNpcVitalsBinding>();
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var effectManager=actor.GetComponent<EntityEffectManager>();
            Require(store!=null && owner && owner.Binding!=null && vitals && body && body.Entity!=null && effectManager,
                "Selected resident lost a required native component.");
            Require(NativeNpcHealingItem.TryFindHealingPotionRecipeKey(out int recipeKey),
                "DFU has no registered single-effect, duration-free HealHealth potion recipe.");

            string reason;
            Require(NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out reason),
                "Could not synchronize the fixture resident's native inventory: "+reason);
            var potion=ItemBuilder.CreatePotion(recipeKey,2);
            Require(potion!=null && potion.IsPotion && potion.stackCount==2 && NativeNpcHealingItem.IsSupportedPotion(potion),
                "Could not create a two-unit native potion from the broker-discovered healing recipe.");
            owner.Binding.Items.AddItem(potion,noStack:true);
            Require(NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out reason),
                "Could not seed the test-only owned healing potion: "+reason);
            ulong uid=potion.UID;
            Require(uid!=0 && owner.Binding.Items.GetItem(uid)==potion && store.NpcInventoryItemOwner(uid)==actor.Resident.Id &&
                NativeNpcHealingItem.TryFindOwned(owner.Binding,out ulong foundUid) && foundUid!=0 &&
                store.NpcInventoryItemOwner(foundUid)==actor.Resident.Id,
                "Seeded healing potion or an existing potion was not registered under this resident's ownership.");

            int fullHealth=body.Entity.CurrentHealth;
            int damage=Math.Max(1,Math.Min(fullHealth-1,
                fullHealth-(body.Entity.MaxHealth*3/5)));
            body.DamageHealthFromSource((DaggerfallEntityBehaviour)null,damage,false,Vector3.zero);
            Require(body.Entity.CurrentHealth>0 && body.Entity.CurrentHealth<fullHealth,
                "DFU native damage API did not create a nonlethal injury.");
            vitals.Capture();
            NpcArchetype archetype=Archetype(store,actor.Resident.Id);
            var savedVitals=store.ReadNpcVitals(actor.Resident.Id,archetype);
            var savedInventory=store.ReadNpcInventory(actor.Resident.Id);
            Require(savedVitals.IsPersisted && savedVitals.CurrentHealth==body.Entity.CurrentHealth &&
                savedVitals.CurrentHealth<savedVitals.MaximumHealth && savedInventory.Revision==owner.Binding.Revision &&
                savedInventory.WalletGold>=0 && savedInventory.Items.Exists(item=>item.Uid==uid),
                "Native injury or owned potion was not represented by the fixture ledgers before treatment.");

            double now=GameTimeSeconds();
            NpcActionJob first=AcceptHealingAction(store,actor,uid,savedInventory.Revision,now);
            int woundedHealth=body.Entity.CurrentHealth;
            int woundedMagicka=body.Entity.CurrentMagicka;
            long woundedVitalsRevision=savedVitals.Revision;
            long woundedInventoryRevision=savedInventory.Revision;

            // The job's owner string alone cannot prove that this is the same
            // resident represented by the live native actor.
            FieldInfo residentField=typeof(ResidentActor).GetField("<Resident>k__BackingField",Private);
            Resident originalResident=actor.Resident;
            Require(residentField!=null,"Could not inspect the controlled actor's resident identity.");
            residentField.SetValue(actor,new Resident {Id=originalResident.Id+"-identity-probe",IsAlive=true,
                AgeGroup=ResidentAgeGroup.Adult});
            bool mismatchedActorRejected;
            try {mismatchedActorRejected=!NativeNpcHealingItem.TryExecute(store,first,actor.gameObject,now,out reason);}
            finally {residentField.SetValue(actor,originalResident);}
            Require(mismatchedActorRejected && body.Entity.CurrentHealth==woundedHealth && potion.stackCount==2,
                "Adapter accepted a job when the live actor's resident identity differed from its inventory owner.");

            // The binding must be the exact ItemCollection attached to the
            // bound native entity; a same-owner replacement collection is stale.
            ItemCollection nativeItems=owner.Binding.Items;
            owner.Binding.Items=new ItemCollection();
            bool mismatchedCollectionRejected;
            try {mismatchedCollectionRejected=!NativeNpcHealingItem.TryExecute(store,first,actor.gameObject,now,out reason);}
            finally {owner.Binding.Items=nativeItems;}
            Require(mismatchedCollectionRejected && ReferenceEquals(owner.Binding.Items,body.Entity.Items) &&
                body.Entity.CurrentHealth==woundedHealth && potion.stackCount==2,
                "Adapter accepted a resident inventory binding detached from the native entity collection.");

            int potionValueBefore=potion.value;
            bool payloadMutationRejected=NativeNpcHealingItem.TryExecute(store,first,actor.gameObject,now,item=>
            {
                effectManager.DrinkPotion(item);
                item.value++;
            },out reason);
            Require(!payloadMutationRejected && body.Entity.CurrentHealth==woundedHealth &&
                body.Entity.CurrentMagicka==woundedMagicka && owner.Binding.Items.GetItem(uid)==potion &&
                potion.stackCount==2 && potion.value==potionValueBefore,
                "Adapter accepted or failed to roll back a potion payload mutation beyond one stack unit.");

            bool rejected=NativeNpcHealingItem.TryExecute(store,first,actor.gameObject,now,item=>
            {
                effectManager.DrinkPotion(item);
                // Simulate an invalid native collection mutation after the real
                // effect so the adapter must restore both resources and stack.
                owner.Binding.Items.RemoveOne(item);
            },out reason);
            Require(!rejected,"Adapter accepted a two-unit native stack mutation for one treatment action.");
            Require(body.Entity.CurrentHealth==woundedHealth && body.Entity.CurrentMagicka==woundedMagicka &&
                owner.Binding.Items.GetItem(uid)==potion && potion.stackCount==2,
                "Rejected native effect did not restore the exact native HP/MP and potion object/stack.");
            var afterRollbackVitals=store.ReadNpcVitals(actor.Resident.Id,archetype);
            var afterRollbackInventory=store.ReadNpcInventory(actor.Resident.Id);
            var stillExecuting=store.ReadNpcAction(first.Id);
            Require(afterRollbackVitals.Revision==woundedVitalsRevision && afterRollbackVitals.CurrentHealth==woundedHealth &&
                afterRollbackInventory.Revision==woundedInventoryRevision && afterRollbackInventory.WalletGold==savedInventory.WalletGold &&
                stillExecuting!=null && stillExecuting.Phase==NpcActionPhase.Executing && stillExecuting.Revision==first.Revision,
                "Rejected native treatment mutated persisted vitals, wallet, inventory or action.");

            now=GameTimeSeconds();
            Require(NativeNpcHealingItem.TryEstimateHealthGain(potion,body.Entity,out int minimumHeal),
                "Could not derive a conservative HealHealth magnitude from the potion's native recipe and caster level.");
            IList<NpcInventoryItem> firstBeforeItems=NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items);
            Require(NativeNpcHealingItem.TryExecute(store,first,actor.gameObject,now,out reason),
                "Native potion treatment did not commit: "+reason);
            IList<NpcInventoryItem> firstAfterItems=NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items);
            Require(NativeNpcHealingItem.IsExactlyOneUnitConsumed(firstBeforeItems,firstAfterItems,uid) &&
                body.Entity.CurrentHealth-woundedHealth>=Math.Min(minimumHeal,body.Entity.MaxHealth-woundedHealth),
                "Native effect fell below its recipe-derived minimum or changed payload beyond one consumed unit.");
            AssertCommittedTreatment(store,actor,owner,vitals,body,first,uid,potion,2,1,woundedHealth,
                woundedMagicka,savedInventory.WalletGold,woundedVitalsRevision,woundedInventoryRevision,archetype,now);
            int healedHealth=body.Entity.CurrentHealth;
            long healedVitalsRevision=store.ReadNpcVitals(actor.Resident.Id,archetype).Revision;
            long healedInventoryRevision=store.ReadNpcInventory(actor.Resident.Id).Revision;
            Require(!NativeNpcHealingItem.TryExecute(store,first,actor.gameObject,GameTimeSeconds(),out reason) &&
                body.Entity.CurrentHealth==healedHealth && potion.stackCount==1 &&
                store.ReadNpcVitals(actor.Resident.Id,archetype).Revision==healedVitalsRevision &&
                store.ReadNpcInventory(actor.Resident.Id).Revision==healedInventoryRevision,
                "Completed treatment action replay changed native or persisted state.");

            int secondDamage=Math.Max(1,Math.Min(body.Entity.CurrentHealth-1,
                body.Entity.CurrentHealth-(body.Entity.MaxHealth*3/5)));
            body.DamageHealthFromSource((DaggerfallEntityBehaviour)null,secondDamage,false,Vector3.zero);
            Require(body.Entity.CurrentHealth>0 && body.Entity.CurrentHealth<healedHealth,
                "Could not create a second nonlethal injury for the final stack unit.");
            vitals.Capture();
            savedVitals=store.ReadNpcVitals(actor.Resident.Id,archetype);
            savedInventory=store.ReadNpcInventory(actor.Resident.Id);
            now=GameTimeSeconds();
            NpcActionJob second=AcceptHealingAction(store,actor,uid,savedInventory.Revision,now);
            int secondWound=body.Entity.CurrentHealth;
            long secondVitalsRevision=savedVitals.Revision;
            long secondInventoryRevision=savedInventory.Revision;
            Require(NativeNpcHealingItem.TryEstimateHealthGain(potion,body.Entity,out minimumHeal),
                "Could not derive the second conservative HealHealth magnitude.");
            IList<NpcInventoryItem> secondBeforeItems=NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items);
            Require(NativeNpcHealingItem.TryExecute(store,second,actor.gameObject,now,out reason),
                "Native treatment did not consume the final stack unit: "+reason);
            IList<NpcInventoryItem> secondAfterItems=NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items);
            Require(NativeNpcHealingItem.IsExactlyOneUnitConsumed(secondBeforeItems,secondAfterItems,uid) &&
                body.Entity.CurrentHealth-secondWound>=Math.Min(minimumHeal,body.Entity.MaxHealth-secondWound),
                "Final-unit effect fell below its recipe-derived minimum or changed another item payload.");
            AssertCommittedTreatment(store,actor,owner,vitals,body,second,uid,potion,1,0,secondWound,
                body.Entity.CurrentMagicka,savedInventory.WalletGold,secondVitalsRevision,secondInventoryRevision,archetype,now);
            Require(owner.Binding.Items.GetItem(uid)==null && store.NpcInventoryItemOwner(uid)==null,
                "Final native potion unit remains owned after exact last-unit consumption.");
            Require(!NativeNpcHealingItem.TryExecute(store,second,actor.gameObject,GameTimeSeconds(),out reason) &&
                store.NpcInventoryItemOwner(uid)==null,
                "Replayed final treatment action re-consumed or restored the last unit.");
        }

        private static NpcActionJob AcceptHealingAction(WorldStateStore store,ResidentActor actor,ulong uid,long inventoryRevision,double now)
        {
            string personId=actor.Resident.Id;
            var position=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,
                WorldSpaceId="native-healthcare-probe"};
            var previousTask=store.ActiveNpcTask(personId);
            string reason;
            if(previousTask!=null)
            {
                var previousAction=store.ReadNpcAction(previousTask.ActiveActionId);
                Require(previousAction!=null && previousAction.Phase==NpcActionPhase.Completed &&
                    previousAction.TreatmentReceipt!=null,
                    "Previous controlled treatment lacks a completed source-bound action.");
                Require(store.SaveNpcActionPosition(previousAction.Id,previousAction.Revision,position,true,now),
                    "Previous controlled treatment could not retain its routine-return position.");
                Require(store.ReconcileNpcTask(previousTask.Id,previousTask.Revision,now),
                    "Previous controlled care task did not reconcile its verified treatment.");
                var previousCare=store.ReadNpcHealthCare(personId);
                Require(previousCare!=null && store.TryContinueNpcHealthCareAfterTreatment(personId,
                    previousCare.Revision,Archetype(store,personId),now,position),
                    "Previous controlled treatment did not release the still-injured care task.");
            }
            var care=store.ReadNpcHealthCare(personId);
            if(care==null || care.Phase==NpcHealthCarePhase.Recovered)
            {
                Require(store.TryBeginNpcHealthCare(personId,Archetype(store,personId),now,position),
                    "Controlled injury did not begin source-bound health care.");
                care=store.ReadNpcHealthCare(personId);
            }
            Require(care!=null && care.Phase==NpcHealthCarePhase.Seeking,
                "Controlled health care did not expose a seekable treatment source.");
            string taskId=NpcHealthCare.MakeTaskId(care);
            var target=NpcActionExecutor.CopyTarget(position);
            target.ItemId=uid.ToString(CultureInfo.InvariantCulture);
            var task=new NpcTaskPlan {Id=taskId,ProposalId=taskId,ResidentId=personId,
                Phase=NpcTaskPhase.Ready,Revision=1,AcceptedAt=now,UpdatedAt=now,DeadlineAt=now+1200};
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.UseHealingItem,
                Target=NpcActionExecutor.CopyTarget(target)});
            Require(store.TryAcceptAndBindNpcHealthCareTask(personId,care.Revision,task,now,out reason),
                "Controlled healing task did not bind to actual care: "+reason);
            string id=NpcTaskContract.ActionId(task);
            var job=new NpcActionJob {Id=id,ProposalId=id,ResidentId=personId,Kind=NpcActionKind.UseHealingItem,
                Target=target,LastPosition=position,
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=now,UpdatedAt=now,DeadlineAt=now+600,
                ExpectedInventoryRevision=inventoryRevision};
            Require(store.TryDispatchNpcTaskStep(task.Id,task.Revision,now,job,true,true,true,out reason),
                "Cannot dispatch controlled healing care step: "+reason);
            Require(store.ObserveNpcAction(id,job.Revision,now,NpcActionObservation.Approach()),
                "Cannot advance controlled healing action to approach.");
            var approaching=store.ReadNpcAction(id);
            Require(approaching!=null && approaching.Phase==NpcActionPhase.Approaching &&
                store.ObserveNpcAction(id,approaching.Revision,now,NpcActionObservation.Arrival()),
                "Cannot mark controlled healing action arrived.");
            var executing=store.ReadNpcAction(id);
            Require(executing!=null && executing.Phase==NpcActionPhase.Executing,"Healing action is not executing at arrival.");
            return executing;
        }

        private static void FundAndStartPaidService(WorldStateStore store,ResidentActor actor)
        {
            string personId=actor.Resident.Id;
            string home=SavedHomeReference(store,personId,actor.Resident.HomeKey);
            Require(home!=null,"Paid-care fixture resident has no matching saved household home.");
            Require(store.ReadNpcServiceContracts().Read(PaidServiceContractId)==null,
                "Disposable paid-care contract ID is already present.");
            NpcServiceContractTerms terms;
            Require(NpcServiceContractTerms.TryCreate(PaidServiceContractId,
                NpcDirectedRelationship.PlayerTargetId,personId,home,NpcServiceRole.Fighter,
                NpcServiceCoverage.Follow,PaidServiceRate,false,NpcServiceRiskScope.None,
                new NpcServiceProtectedTarget[0],NpcServiceRestTreatment.RestNotIncluded,
                NpcServiceResupplyResponsibility.SeparatelyNegotiated,
                new NpcServiceReservedObligation[0],
                id=>id==NpcDirectedRelationship.PlayerTargetId || id==personId,
                id=>id==home,(id,category,revision)=>false,
                (id,revision,kind,starts,ends)=>false,out terms),
                "Actual care resident could not receive controlled paid-service terms.");
            double fundingAt=GameTimeSeconds();
            NpcServiceTransferReceipt funding;
            NpcServiceContractLedger funded;
            Exception failure;
            Require(NativeNpcServiceContractFunding.TryReserveFromPlayer(store,
                store.CaptureNpcServiceContractWriteTicket(),GameManager.Instance.PlayerEntity,
                terms,fundingAt,PaidServiceFundingId,out funding,out funded,out failure),
                "Actual player funding of care resident failed: "+failure);
            Require(funding!=null && funding.Amount==PaidServiceRate && funding.GameTime==fundingAt &&
                funding.FromOwnerId==NpcDirectedRelationship.PlayerTargetId &&
                funding.ToOwnerId==terms.NpcId,
                "Paid-care fixture lacks its actual native player funding receipt.");
            var ticket=store.CaptureNpcServiceContractWriteTicket();
            var reserved=ticket.SourceLedger.Read(PaidServiceContractId);
            NpcServiceContract started;
            NpcServiceContractLedger candidate;
            string error;
            double startAt=GameTimeSeconds();
            Require(reserved!=null && reserved.NpcId==personId &&
                reserved.TryStart(reserved.Revision,startAt,out started) &&
                NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,
                    started,out candidate) &&
                store.TryCommitNpcServiceContracts(ticket,candidate,new NpcServiceTransferReceipt[0],
                    new NpcServiceRenewalAuthorization[0],out error),
                "Funded care resident could not enter saved active service.");
            var active=store.ReadNpcServiceContracts().Read(PaidServiceContractId);
            Require(active!=null && active.Status==NpcServiceContractStatus.Active &&
                active.NpcId==personId && active.FundedGold==PaidServiceRate &&
                active.EscrowGold==PaidServiceRate && active.TransferReceipts.Count==1 &&
                active.BlockEndAt-GameTimeSeconds()>80000,
                "Paid-care fixture did not retain a long active service block before injury.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_HEALTHCARE_PAID_SERVICE_START: resident="+
                personId+"; contract="+PaidServiceContractId+"; funding="+PaidServiceFundingId+
                "; rate="+PaidServiceRate+"; nativePlayerDebit=true; savedActive=true");
        }

        private static string SavedHomeReference(WorldStateStore store,string personId,int homeKey)
        {
            var state=store.Snapshot();
            if(state.Settlements==null)return null;
            foreach(var settlement in state.Settlements)
            {
                if(settlement==null || settlement.Households==null)continue;
                foreach(var household in settlement.Households)
                {
                    if(household==null || household.BuildingKey!=homeKey || household.Residents==null)continue;
                    foreach(var resident in household.Residents)
                        if(resident!=null && resident.Id==personId && resident.IsAlive)
                            return "home:"+settlement.SettlementId.ToString(CultureInfo.InvariantCulture)+
                                ":"+homeKey.ToString(CultureInfo.InvariantCulture);
                }
            }
            return null;
        }

        private static void RequirePausedService(WorldStateStore store,PaidServiceEvidence evidence,string stage)
        {
            var paused=store.ReadNpcServiceContracts().Read(PaidServiceContractId);
            var baseline=evidence.Paused;
            Require(baseline!=null && paused!=null &&
                paused.Status==NpcServiceContractStatus.Suspended &&
                paused.SuspensionOwnerKind==NpcServiceSuspensionOwnerKind.HealthCare &&
                paused.SuspensionOwnerId==evidence.CareId &&
                paused.Revision==baseline.Revision &&
                paused.EarnedGold==baseline.EarnedGold &&
                paused.EligibleServiceSeconds==baseline.EligibleServiceSeconds &&
                paused.CurrentBlockEligibleSeconds==baseline.CurrentBlockEligibleSeconds &&
                paused.LastServiceUpdateAt==baseline.LastServiceUpdateAt &&
                paused.EscrowGold==baseline.EscrowGold &&
                paused.TransferReceipts.Count==baseline.TransferReceipts.Count,
                "Paid service resumed or accrued during "+stage+" instead of preserving its health-owned pause.");
        }

        private static bool PaidServiceResumedWithoutPauseBilling(NpcServiceContract active,
            PaidServiceEvidence evidence)
        {
            var paused=evidence.Paused;
            return paused!=null && active!=null && active.Status==NpcServiceContractStatus.Active &&
                active.SuspensionOwnerKind==NpcServiceSuspensionOwnerKind.None &&
                active.SuspensionOwnerId==null && active.Revision==paused.Revision+1 &&
                active.LastServiceUpdateAt>=paused.LastServiceUpdateAt &&
                active.EarnedGold==paused.EarnedGold &&
                active.EligibleServiceSeconds==paused.EligibleServiceSeconds &&
                active.CurrentBlockEligibleSeconds==paused.CurrentBlockEligibleSeconds &&
                active.EscrowGold==paused.EscrowGold &&
                active.TransferReceipts.Count==paused.TransferReceipts.Count;
        }

        private static void AssertCommittedTreatment(WorldStateStore store,ResidentActor actor,
            NativeNpcInventoryOwner owner,NativeNpcVitalsBinding vitals,DaggerfallEntityBehaviour body,
            NpcActionJob action,ulong uid,DaggerfallUnityItem potion,int stackBefore,int stackAfter,
            int healthBefore,int magickaBefore,int walletBefore,long vitalsRevision,long inventoryRevision,
            NpcArchetype archetype,double observedAt)
        {
            var completed=store.ReadNpcAction(action.Id);
            var receipt=completed==null?null:completed.TreatmentReceipt;
            var savedVitals=store.ReadNpcVitals(actor.Resident.Id,archetype);
            var savedInventory=store.ReadNpcInventory(actor.Resident.Id);
            Require(completed!=null && completed.Phase==NpcActionPhase.Completed &&
                completed.CompletedEffectId==action.Id+":observed" && receipt!=null &&
                receipt.Kind==NpcTreatmentKind.HealingItem && receipt.SourceItemUid==uid &&
                receipt.TransactionId==action.Id+":treatment" && receipt.HealthBefore==healthBefore &&
                receipt.HealthAfter==body.Entity.CurrentHealth && receipt.HealthAfter>healthBefore &&
                receipt.ObservedGameTime==observedAt && body.Entity.CurrentMagicka==magickaBefore &&
                savedVitals.Revision==vitalsRevision+1 && savedVitals.CurrentHealth==body.Entity.CurrentHealth &&
                savedVitals.CurrentMagicka==magickaBefore && savedInventory.Revision==inventoryRevision+1 &&
                savedInventory.LastTransactionId==action.Id+":treatment" &&
                savedInventory.WalletGold==walletBefore &&
                owner.Binding.Revision==savedInventory.Revision &&
                (stackAfter==0?owner.Binding.Items.GetItem(uid)==null:
                    owner.Binding.Items.GetItem(uid)==potion && potion.stackCount==stackAfter),
                "Native potion, exact stack delta, wallet, receipt or treatment ledger did not commit together.");

            long committedRevision=savedVitals.Revision;
            vitals.Capture();
            Require(store.ReadNpcVitals(actor.Resident.Id,archetype).Revision==committedRevision,
                "Vitals sampler duplicated the already-committed treatment as a second observation.");
        }

        private static bool LiveVitalsReadyForAutonomousCare(StreetPopulation streets,ResidentActor actor)
        {
            if(!actor || actor.Resident==null || !actor.gameObject.activeInHierarchy ||
                streets.FindActor(actor.Resident.Id)!=actor)return false;
            var binding=actor.GetComponent<NativeNpcVitalsBinding>();
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            if(!binding || !body || body.Entity==null)return false;
            int health,maximumHealth,magicka,maximumMagicka;
            if(!binding.TryReadCurrent(out health,out maximumHealth,out magicka,out maximumMagicka) ||
                health!=body.Entity.CurrentHealth || maximumHealth!=body.Entity.MaxHealth ||
                magicka!=body.Entity.CurrentMagicka || maximumMagicka!=body.Entity.MaxMagicka)return false;
            var store=Store(streets);
            if(store==null || store.ActiveNpcTask(actor.Resident.Id)!=null ||
                store.ActiveNpcAction(actor.Resident.Id)!=null || store.ReadNpcHealthCare(actor.Resident.Id)!=null)return false;
            NpcVitalsView saved;
            try {saved=store.ReadNpcVitals(actor.Resident.Id,Archetype(store,actor.Resident.Id));}
            catch(ArgumentException) {return false;}
            return saved!=null && saved.IsAlive && saved.MaximumHealth==maximumHealth &&
                saved.CurrentHealth==health && saved.MaximumMagicka==maximumMagicka &&
                saved.CurrentMagicka==magicka &&
                (saved.IsPersisted && saved.Revision>0 || !saved.IsPersisted && saved.Revision==0 &&
                 health==maximumHealth && magicka==maximumMagicka);
        }

        private static ResidentActor FindCandidate(StreetPopulation streets)
        {
            var store=Store(streets);
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(!actor || !actor.gameObject.activeInHierarchy || actor.Resident==null || !actor.Resident.IsAlive ||
                    actor.Resident.IsQuestProtected || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                    actor.IsSleeping || actor.IsConversing ||
                    CivilianAlarms.ChooseReaction(actor.Resident)!=CivilianReaction.Flee ||
                    streets.IsAlarmed(actor.Resident.Id) ||
                    store.ActiveNpcTask(actor.Resident.Id)!=null || store.ActiveNpcAction(actor.Resident.Id)!=null ||
                    store.ReadNpcHealthCare(actor.Resident.Id)!=null)continue;
                var owner=actor.GetComponent<NativeNpcInventoryOwner>();
                var vitals=actor.GetComponent<NativeNpcVitalsBinding>();
                var body=actor.GetComponent<DaggerfallEntityBehaviour>();
                if(owner && owner.Binding!=null && vitals && body && body.Entity!=null &&
                    actor.GetComponent<EntityEffectManager>() &&
                    SavedHomeReference(store,actor.Resident.Id,actor.Resident.HomeKey)!=null)return actor;
            }
            return null;
        }

        private static NpcArchetype Archetype(WorldStateStore store,string id)
        {
            var progress=store.ReadNpcProgress(id);
            return progress==null?NpcArchetype.Citizen:progress.Archetype;
        }

        private static double GameTimeSeconds()
        {return DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();}

        private static double ExactGameTimeSeconds()
        {
            var now=DaggerfallUnity.Instance.WorldTime.Now;
            return now.ToSeconds()+(now.Second-Math.Floor(now.Second));
        }

        private static WorldStateStore Store(StreetPopulation streets)
        {return (WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);}

        private static string CommandLineValue(string key)
        {
            string[] args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,key);
            return index>=0 && index+1<args.Length?args[index+1]:null;
        }

        private static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException(message);}
    }
}
