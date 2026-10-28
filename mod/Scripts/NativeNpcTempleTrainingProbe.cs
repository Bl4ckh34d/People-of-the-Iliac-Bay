using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in acceptance only. It consumes a real loaded temple, trainer and
    // resident. Its owned disposable save grants one existing resident the
    // exact native lesson price through the normal inventory binding; it never
    // creates an actor, item, income source, or production wallet credit.
    internal static class NativeNpcTempleTrainingProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string TrainingReadyCheckpoint="People - Daggerfall temple training ready";
        private const string TrainingPaidCheckpoint="People - Daggerfall temple training paid";
        internal static IEnumerator RunFixture(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-temple-training-probe") >= 0 &&
                character == "Hakon Ironhand", "Temple training probe requires its explicit owned fixture.");
            var game = GameManager.Instance; game.SaveLoadManager.EnumerateSaves();
            bool paidResume=game.SaveLoadManager.FindSaveFolderByNames(character,TrainingPaidCheckpoint)>=0;
            bool resume=paidResume || game.SaveLoadManager.FindSaveFolderByNames(character,TrainingReadyCheckpoint)>=0;
            string fixtureName = paidResume?TrainingPaidCheckpoint:resume?TrainingReadyCheckpoint:"People - Daggerfall acceptance ready";
            int slot = game.SaveLoadManager.FindSaveFolderByNames(character, fixtureName);
            Require(slot >= 0, "Owned observed Daggerfall checkpoint is missing.");
            bool loaded = false; SaveLoadManager.OnLoadEventHandler onLoad = data => loaded = true; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                float until = Time.realtimeSinceStartup + 30;
                while ((!loaded || !streets.Ready || !resume && !streets.EntranceSurveyReady || game.PlayerEnterExit.IsPlayerInside!=resume) && Time.realtimeSinceStartup < until) yield return null;
                Require(loaded && streets.Ready && (resume || streets.EntranceSurveyReady) && game.PlayerEnterExit.IsPlayerInside==resume && game.PlayerEntity.Name == character,
                    "Owned temple fixture did not load its exact exterior or saved interior space.");
            }
            finally { SaveLoadManager.OnLoad -= onLoad; }
            // Observe the existing noon checkpoint. The Gothway baseline contains
            // saved hostile Assassins; their legitimate flight must not be suppressed.
            DaggerfallUI.Instance.PopToHUD(); game.PauseGame(false);
            Require(PlayerActivate.IsBuildingOpen(DFLocation.BuildingTypes.Temple),
                "Observed checkpoint is outside the actual native temple opening window.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_CHECKPOINT: save=" + fixtureName + "; time=" + Now() +
                "; timeAdvance=false; enemyChanged=false; spawningChanged=false");
            yield return null;
            var store = (WorldStateStore)typeof(StreetPopulation).GetField("worldStore", Private).GetValue(streets);
            Require(store != null, "Temple probe has no live world store.");
            if(paidResume){yield return ResumePaidTempleTraining(streets,store,game);yield break;}
            ResidentActor trainee=null;string traineeId=null,diagnostic=null,loggedDiagnostic=null,acceptReason=null;
            int controlledBudget=0;float deadline;NpcActionJob entered=null;NpcTaskPlan stagingTask=null;
            DaggerfallInterior room=null;
            if(!resume)
            {
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory(); Require(directory, "Temple fixture has no building directory.");
            DaggerfallStaticDoors owner = null; StaticDoor entry = default(StaticDoor); float nearestTemple=float.PositiveInfinity;
            foreach (var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            foreach (var door in doors.Doors)
            {
                BuildingSummary summary;
                if (door.doorType == DoorTypes.Building && directory.GetBuildingSummary(door.buildingKey, out summary) &&
                    summary.BuildingType == DFLocation.BuildingTypes.Temple && PlayerActivate.IsBuildingOpen(summary.BuildingType))
                {
                    var point=doors.transform.TransformPoint(door.buildingMatrix.MultiplyPoint3x4(door.centre));
                    float distance=(point-game.PlayerMotor.transform.position).sqrMagnitude;
                    if(distance>=nearestTemple)continue;
                    nearestTemple=distance;owner=doors;entry=door;
                }
            }
            Require(owner, "No currently open real temple door exists in the owned fixture.");
            NpcActionOption enter = null;
            float candidateDeadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < candidateDeadline && !TryFindEligibleTempleEntry(streets, store, entry.buildingKey,
                out trainee, out enter, out diagnostic))
            {
                if (!string.IsNullOrEmpty(diagnostic) && diagnostic != loggedDiagnostic)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_PROBE_CANDIDATE: " + diagnostic); loggedDiagnostic = diagnostic; }
                yield return new WaitForSecondsRealtime(.25f);
            }
            Require(trainee && enter != null, "No existing eligible resident can enter this real temple: " + diagnostic);
            traineeId = trainee.Resident.Id;
            string budgetReason;
            Require(TrySetControlledFixtureBudget(store, trainee, out controlledBudget, out budgetReason),
                "Temple probe could not apply its controlled disposable NPC budget: " + budgetReason);
            Require(Vector3.Distance(game.PlayerMotor.transform.position, trainee.transform.position) <= 8f,
                "Temple probe lost the observed conversation range of its selected existing resident.");
            Require(NpcProbeCommandSource.TryAcceptEnterWait(streets,store,traineeId,"native-temple-entry:"+Guid.NewGuid().ToString("N"),
                enter,300,300,out stagingTask,out acceptReason),
                "Native temple Enter/Wait task was not accepted: " + acceptReason);
            entered = store.ActiveNpcAction(traineeId);
            Require(entered != null && entered.Kind == NpcActionKind.Enter && entered.Target != null &&
                entered.Target.BuildingKey == entry.buildingKey, "Selected resident lost the exact native temple doorway.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_ENTRY_ACCEPTED: resident="+traineeId+"; action="+entered.Id+
                "; door="+entered.Target.DoorId+"; acceptedAt="+entered.AcceptedAt+"; deadlineAt="+entered.DeadlineAt+
                "; actualFoot="+(trainee.transform.position-Vector3.up).ToString("R")+"; timeScale="+DaggerfallUnity.Instance.WorldTime.TimeScale);
            deadline = Time.realtimeSinceStartup + 120;
            while (Time.realtimeSinceStartup < deadline)
            {
                entered = store.ReadNpcAction(entered.Id);
                if (entered == null || NpcActionExecutor.IsTerminal(entered)) break;
                yield return null;
            }
            Require(entered != null && entered.Phase == NpcActionPhase.Completed && entered.BuildingTransitionReceipt != null &&
                entered.BuildingTransitionReceipt.BuildingKey == entry.buildingKey && !game.PlayerEnterExit.IsPlayerInside &&
                !streets.FindActor(traineeId), "Selected resident did not physically cross the exact temple door: action="+entered?.Id+
                    "; phase="+entered?.Phase+"; failure="+entered?.FailureReason+"; gameNow="+Now()+
                    "; acceptedAt="+entered?.AcceptedAt+"; updatedAt="+entered?.UpdatedAt+"; deadlineAt="+entered?.DeadlineAt+
                    "; receipt="+(entered?.BuildingTransitionReceipt!=null)+"; actualBody="+(bool)streets.FindActor(traineeId)+
                    "; playerInside="+game.PlayerEnterExit.IsPlayerInside+"; alarm="+streets.IsAlarmed(traineeId)+
                    "; danger="+DescribeCurrentDanger(streets,store,traineeId)+
                    "; motion="+NativeMoveToWaitProbe.DescribeActionMotion(streets,traineeId,streets.FindActor(traineeId)));
            TownSmokeHarness.EnterInterior(owner.transform, entry); DaggerfallUI.Instance.PopToHUD();
            deadline = Time.realtimeSinceStartup + 30; bool observerClearedLanding=false;
            while ((!game.PlayerEnterExit.Interior || !streets.Ready || !InteriorAdmissionReady(streets) || !streets.FindActor(traineeId)) &&
                Time.realtimeSinceStartup < deadline)
            {
                if(!observerClearedLanding && game.PlayerEnterExit.Interior && streets.Ready && InteriorAdmissionReady(streets))
                    observerClearedLanding=StageTempleLandingObserver(streets,game);
                yield return null;
            }
            room = game.PlayerEnterExit.Interior;
            trainee = streets.FindActor(traineeId);
            Require(room && room.EntryDoor.buildingKey == entry.buildingKey && room.BuildingData.BuildingType == DFLocation.BuildingTypes.Temple &&
                trainee && trainee.gameObject.activeInHierarchy && store.ReadNpcInventory(traineeId).WalletGold == controlledBudget,
                "Native temple entry did not materialize the same funded resident in the selected public building: building="+
                room?.EntryDoor.buildingKey+"; nativeGeometryReady="+InteriorAdmissionReady(streets)+"; exactBody="+(bool)trainee+
                "; action="+store.ReadNpcAction(entered.Id)?.Phase+"; failure="+store.ReadNpcAction(entered.Id)?.FailureReason);
            var landedEntry=store.ReadNpcAction(entered.Id);
            Require(landedEntry?.BuildingTransitionReceipt?.DestinationObserved==true,
                "The same native Enter receipt did not observe its actual interior landing.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_ENTRY_BODY_LANDED: resident="+traineeId+"; action="+entered.Id+
                "; exactDoor="+landedEntry.BuildingTransitionReceipt.DoorId+"; destinationObserved=true; actualBody=true; walletPreserved=true");
            deadline=Time.realtimeSinceStartup+30;
            while(!TryReadTempleStagingWait(store,room,traineeId,out stagingTask,out entered) && Time.realtimeSinceStartup<deadline)yield return null;
            Require(stagingTask!=null && entered!=null,"The actual native temple landing did not dispatch its agreed Wait step.");
            yield return ConversationMoodProbe.SaveCheckpoint(game,character,TrainingReadyCheckpoint);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_READY_CHECKPOINT: title="+TrainingReadyCheckpoint+"; resident="+traineeId+
                "; actualBody=true; normalEnterWait=true; purchasePending=true");
            }
            else
            {
                room=game.PlayerEnterExit.Interior;deadline=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(room && InteriorAdmissionReady(streets) && TryReadTempleStagingWait(store,room,null,out stagingTask,out entered))
                    {traineeId=stagingTask.ResidentId;trainee=streets.FindActor(traineeId);if(trainee && trainee.gameObject.activeInHierarchy)break;}
                    yield return null;
                }
                Require(room && stagingTask!=null && entered!=null && trainee,"Saved temple checkpoint did not restore its actual funded waiting resident.");
                controlledBudget=store.ReadNpcInventory(traineeId).WalletGold;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_READY_RESUME: resident="+traineeId+"; actualBody=true; normalWait=true; exteriorWalkRepeated=false");
            }
            var keeper = typeof(StreetPopulation).GetMethod("KeeperPerson", Private);
            Require(keeper != null, "Temple probe cannot resolve the real native trainer.");
            Func<StaticNPC, NativePerson> resolve = npc => (NativePerson)keeper.Invoke(streets, new object[] { npc });
            NpcActionOption choice = null; diagnostic = null; loggedDiagnostic = null;
            float admissionDeadline = Time.realtimeSinceStartup + 30;
            while (Time.realtimeSinceStartup < admissionDeadline && !TryFindCurrentOffer(streets, room, store, resolve, traineeId, out trainee, out choice, out diagnostic))
            {
                if (!string.IsNullOrEmpty(diagnostic) && diagnostic != loggedDiagnostic)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_PROBE_WAIT: " + diagnostic); loggedDiagnostic = diagnostic; }
                yield return new WaitForSecondsRealtime(.25f);
            }
            Require(trainee && choice != null, "No existing eligible resident and current public temple trainer offered native training: " + diagnostic);
            int skill; Require(int.TryParse(choice.Target.ItemId, out skill), "Temple offer omitted its native skill.");
            StaticNPC trainer = null; NativeNpcTempleTrainingOffer offer = null;
            foreach (var candidate in room.GetComponentsInChildren<StaticNPC>())
            {
                NativeNpcTempleTrainingOffer proposed; string rejected;
                if (NativeNpcTempleTraining.TryPrepareOffer(store, trainee, candidate, resolve, room.BuildingData.FactionId, skill, out proposed, out rejected) &&
                    proposed.Trainer.Id == choice.Target.PersonId) { trainer = candidate; offer = proposed; break; }
            }
            Require(trainer && offer != null && offer.Price == choice.Target.QuotedGold, "Offered trainer, eligibility, or native non-member price changed.");
            Require(StagePlayerNearExistingActor(game, trainee), "Temple probe could not stage the player near the existing resident.");
            var inventoryOwner = trainee.GetComponent<NativeNpcInventoryOwner>();
            Require(inventoryOwner && inventoryOwner.Binding != null && inventoryOwner.Binding.PersonId == trainee.Resident.Id,
                "Training resident has no matching native inventory identity.");
            int npcGold = store.ReadNpcInventory(trainee.Resident.Id).WalletGold;
            int playerGold = game.PlayerEntity.GetGoldAmount(); short[] playerSkills = PlayerSkills(game);
            var beforeActor = store.ReadNpcActor(trainee.Resident.Id, store.ResolveNpcArchetype(trainee.Resident.Id));
            int beforeSkill = beforeActor.Skill(skill); int trainerHash = trainer.Data.hash;
            long beforeExperience = Experience(store.ReadNpcProgress(traineeId), skill);
            var traineeBody = trainee.GetComponent<DaggerfallEntityBehaviour>(); Require(traineeBody && traineeBody.Entity != null, "Training resident has no native skill body.");
            short beforeNativeSkill = traineeBody.Entity.Skills.GetPermanentSkillValue((DFCareer.Skills)skill);
            int nativeNpcLevel = traineeBody.Entity.Level;
            Require(nativeNpcLevel == 1 && offer.Price == Guild.nonMemberTrainingCost * nativeNpcLevel,
                "Temple offer did not retain native non-member cost at the resident's actual native level.");
            Require(npcGold == offer.Price && controlledBudget == offer.Price,
                "Controlled fixture budget was not the actual native lesson price.");
            double playerTimeBeforeStart = Now();
            Require(streets.TryStopConversationAction(traineeId,out acceptReason),
                "The actual temple staging Wait could not end normally before service: "+acceptReason);
            Require(NpcProbeCommandSource.TryAcceptAction(streets, traineeId, "native-temple-training:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = choice, DurationSeconds = 300 }, out acceptReason),
                "Native temple training action was not accepted: " + acceptReason);
            var accepted = store.ActiveNpcAction(traineeId);
            Require(accepted != null && accepted.ServiceCode == NativeNpcTempleTraining.ServiceCode && accepted.Target.PersonId == offer.Trainer.Id &&
                accepted.Target.ItemId == choice.Target.ItemId,
                "Accepted training action lost the exact native trainer or selected skill.");
            StageTempleServiceObserver(streets,game,room,trainee);
            deadline = Time.realtimeSinceStartup + 120; NpcActionJob running = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                running = store.ReadNpcAction(accepted.Id);
                if (running == null || NpcActionExecutor.IsTerminal(running) || running.ServiceStep == 1) break;
                yield return null;
            }
            Require(running != null && running.Phase == NpcActionPhase.Executing && running.ServiceStep == 1 && running.FinishAt > 0,
                "Native lesson did not reach its paid running stage: " + (running == null ? "missing" : running.Phase + " " + running.FailureReason));
            double playerTimeAtRunning = Now();
            Require(playerTimeAtRunning >= playerTimeBeforeStart && playerTimeAtRunning - playerTimeBeforeStart < 120d &&
                running.FinishAt - playerTimeAtRunning > NativeNpcTempleTraining.DurationSeconds - 120d &&
                running.FinishAt - playerTimeAtRunning <= NativeNpcTempleTraining.DurationSeconds + 1d,
                "Starting training advanced global time instead of arming a three-hour NPC timer.");
            Require(store.ReadNpcInventory(traineeId).WalletGold == npcGold - offer.Price && SamePlayer(game, playerGold, playerSkills) &&
                store.ReadNpcActor(traineeId, store.ResolveNpcArchetype(traineeId)).Skill(skill) == beforeSkill && trainee.Resident.Id == traineeId &&
                inventoryOwner.Binding.PersonId == traineeId && trainer.Data.hash == trainerHash && traineeBody.Entity.Level == nativeNpcLevel,
                "Paid running lesson changed player state, NPC identity, or skill before its finish time.");
            yield return null;
            Require(store.ReadNpcActor(traineeId, store.ResolveNpcArchetype(traineeId)).Skill(skill) == beforeSkill &&
                Experience(store.ReadNpcProgress(traineeId), skill) == beforeExperience,
                "Native training awarded its skill before three game hours elapsed.");
            Require(traineeBody.Entity.Skills.GetPermanentSkillValue((DFCareer.Skills)skill) == beforeNativeSkill,
                "Native training changed the live NPC skill before three game hours elapsed.");
            yield return ConversationMoodProbe.SaveCheckpoint(game,character,TrainingPaidCheckpoint);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_PAID_CHECKPOINT: title="+TrainingPaidCheckpoint+"; action="+running.Id+
                "; resident="+traineeId+"; serviceStep=1; actualPayment=true; noEarlySkill=true; noEarlyExperience=true; finishAt="+running.FinishAt);
            yield return CompletePaidTempleTraining(streets,store,game,running,beforeSkill,beforeExperience,nativeNpcLevel,
                trainerHash,npcGold-offer.Price,playerGold,playerSkills);
        }
        private static IEnumerator ResumePaidTempleTraining(StreetPopulation streets,WorldStateStore store,GameManager game)
        {
            NpcActionJob running=null;ResidentActor actor=null;float deadline=Time.realtimeSinceStartup+30;
            var room=game.PlayerEnterExit.Interior;
            while(Time.realtimeSinceStartup<deadline)
            {
                if(room && InteriorAdmissionReady(streets))foreach(var candidate in store.PendingNpcActions())
                {
                    if(candidate.Kind!=NpcActionKind.Purchase || candidate.ServiceCode!=NativeNpcTempleTraining.ServiceCode ||
                        candidate.ServiceStep!=1 || candidate.Phase!=NpcActionPhase.Executing ||
                        candidate.Target?.BuildingKey!=room.EntryDoor.buildingKey)continue;
                    var body=streets.FindActor(candidate.ResidentId);
                    if(body && body.gameObject.activeInHierarchy){running=candidate;actor=body;break;}
                }
                if(running!=null)break;yield return null;
            }
            Require(running!=null && actor,"Paid temple checkpoint did not restore its actual executing lesson/body.");
            int skill=int.Parse(running.Target.ItemId);var bodyState=actor.GetComponent<DaggerfallEntityBehaviour>();
            var trainer=FindCurrentTempleTrainer(streets,room,running.Target.PersonId);
            Require(bodyState && bodyState.Entity!=null && trainer && running.FinishAt>Now(),
                "Paid checkpoint lost its native trainer/body or original unfinished timer.");
            int beforeSkill=store.ReadNpcActor(running.ResidentId,store.ResolveNpcArchetype(running.ResidentId)).Skill(skill);
            long beforeExperience=Experience(store.ReadNpcProgress(running.ResidentId),skill);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_PAID_RESUME: resident="+running.ResidentId+"; action="+running.Id+
                "; originalFinishAt="+running.FinishAt+"; actualBody=true; routeRepeated=false; paymentRepeated=false");
            yield return CompletePaidTempleTraining(streets,store,game,running,beforeSkill,beforeExperience,bodyState.Entity.Level,
                trainer.Data.hash,store.ReadNpcInventory(running.ResidentId).WalletGold,game.PlayerEntity.GetGoldAmount(),PlayerSkills(game));
        }
        private static StaticNPC FindCurrentTempleTrainer(StreetPopulation streets,DaggerfallInterior room,string id)
        {
            if(!room)return null;var keeper=typeof(StreetPopulation).GetMethod("KeeperPerson",Private);
            foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
            {var person=(NativePerson)keeper.Invoke(streets,new object[]{npc});if(person!=null && person.Id==id)return npc;}
            return null;
        }
        private static IEnumerator CompletePaidTempleTraining(StreetPopulation streets,WorldStateStore store,GameManager game,
            NpcActionJob running,int beforeSkill,long beforeExperience,int expectedLevel,int expectedTrainerHash,
            int expectedWallet,int playerGold,short[] playerSkills)
        {
            string id=running.ResidentId;int skill=int.Parse(running.Target.ItemId);
            var original=streets.FindActor(id);int originalInstance=original?original.GetInstanceID():0;
            double remaining=running.FinishAt-Now();Require(remaining>0,"Training timer elapsed before controlled finish check.");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(remaining+1d));
            float deadline=Time.realtimeSinceStartup+30;NpcActionJob completed=running;
            while(Time.realtimeSinceStartup<deadline)
            {completed=store.ReadNpcAction(running.Id);if(completed==null || NpcActionExecutor.IsTerminal(completed))break;yield return null;}
            // Native time catch-up legitimately reconstructs rendered bodies.
            // Compare the actual current identity/binding, never a destroyed proxy.
            var actor=streets.FindActor(id);var body=actor?actor.GetComponent<DaggerfallEntityBehaviour>():null;
            var inventory=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
            var trainer=FindCurrentTempleTrainer(streets,game.PlayerEnterExit.Interior,running.Target.PersonId);
            int savedSkill=store.ReadNpcActor(id,store.ResolveNpcArchetype(id)).Skill(skill);
            long experience=Experience(store.ReadNpcProgress(id),skill);int wallet=store.ReadNpcInventory(id).WalletGold;
            int liveSkill=body && body.Entity!=null?body.Entity.Skills.GetPermanentSkillValue((DFCareer.Skills)skill):-1;
            int level=body && body.Entity!=null?body.Entity.Level:-1;
            bool completedOnce=completed!=null && completed.Phase==NpcActionPhase.Completed && completed.ServiceStep==1 && completed.CompletedEffectId!=null;
            bool sameActor=actor && actor.gameObject.activeInHierarchy && actor.Resident.Id==id;
            bool sameInventory=inventory && inventory.Binding!=null && inventory.Binding.PersonId==id;
            bool sameTrainer=trainer && trainer.Data.hash==expectedTrainerHash;
            bool playerUnchanged=SamePlayer(game,playerGold,playerSkills);
            string values="action="+running.Id+"; phase="+completed?.Phase+"; failure="+completed?.FailureReason+
                "; serviceStep="+completed?.ServiceStep+"; effect="+completed?.CompletedEffectId+"; completedOnce="+completedOnce+
                "; wallet="+wallet+"; expectedWallet="+expectedWallet+"; playerUnchanged="+playerUnchanged+
                "; playerGold="+game.PlayerEntity.GetGoldAmount()+"; expectedPlayerGold="+playerGold+
                "; playerSkills=["+string.Join(",",PlayerSkills(game))+"]; expectedPlayerSkills=["+string.Join(",",playerSkills)+"]"+
                "; savedSkill="+savedSkill+"; beforeSkill="+beforeSkill+"; experience="+experience+"; beforeExperience="+beforeExperience+
                "; liveSkill="+liveSkill+"; sameActor="+sameActor+"; actualActorId="+actor?.Resident?.Id+
                "; sameInventory="+sameInventory+"; inventoryPerson="+inventory?.Binding?.PersonId+
                "; sameTrainer="+sameTrainer+"; trainerHash="+(trainer?trainer.Data.hash:0)+"; expectedTrainerHash="+expectedTrainerHash+
                "; nativeLevel="+level+"; expectedLevel="+expectedLevel+"; originalBodyAlive="+(bool)original+
                "; originalBodyInstance="+originalInstance+"; currentBodyInstance="+(actor?actor.GetInstanceID():0);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_COMPLETION_VALUES: "+values);
            Require(completedOnce && wallet==expectedWallet && playerUnchanged && savedSkill>=beforeSkill && experience>beforeExperience &&
                liveSkill==savedSkill && sameActor && sameInventory && sameTrainer && level==expectedLevel,
                "Native temple completion conditions failed: "+values);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_NATIVE_PASS: resident="+id+"; trainer="+running.Target.PersonId+
                "; trainerHash="+expectedTrainerHash+"; skillId="+skill+"; price="+running.ServicePrice+"; npcGoldAfterPayment="+wallet+
                "; skillChange="+beforeSkill+"->"+savedSkill+"; rawExperience="+beforeExperience+"->"+experience+
                "; serviceClock="+NativeNpcTempleTraining.DurationSeconds+"; playerUnchanged=true; sameIdentity=true; currentNativeBody=true");
        }
        private static double Now() { var time = DaggerfallUnity.Instance.WorldTime.Now; return time.ToSeconds() + (time.Second - Math.Floor(time.Second)); }
        private static bool TryReadTempleStagingWait(WorldStateStore store,DaggerfallInterior room,string expectedResident,
            out NpcTaskPlan task,out NpcActionJob entered)
        {
            task=null;entered=null;
            if(store==null || !room || room.BuildingData.BuildingType!=DFLocation.BuildingTypes.Temple)return false;
            foreach(var candidate in store.PendingNpcTasks())
            {
                if(expectedResident!=null && candidate.ResidentId!=expectedResident || candidate.Phase!=NpcTaskPhase.Running ||
                    candidate.CurrentStepIndex!=1 || candidate.Steps.Count!=2 || candidate.Steps[0].Kind!=NpcActionKind.Enter ||
                    candidate.Steps[1].Kind!=NpcActionKind.Wait || candidate.Steps[0].Target?.BuildingKey!=room.EntryDoor.buildingKey)continue;
                var wait=store.ReadNpcAction(candidate.ActiveActionId);var arrival=store.ReadNpcAction(candidate.Id+":step:0");
                if(wait==null || wait.Kind!=NpcActionKind.Wait || wait.Phase!=NpcActionPhase.Executing || wait.FinishAt<=Now() ||
                    wait.LastPosition?.Kind!=NpcActionTargetKind.IndoorPoint || wait.LastPosition.BuildingKey!=room.EntryDoor.buildingKey ||
                    !NpcTaskPositionBinding.HasCompletedStaticArrivalReceipt(candidate.Steps[0],arrival,candidate.Steps[1]))continue;
                task=candidate;entered=arrival;return true;
            }
            return false;
        }
        private static bool InteriorAdmissionReady(StreetPopulation streets)
        {
            var geometry = typeof(StreetPopulation).GetField("indoorGeometry", Private).GetValue(streets) as InteriorGeometry;
            return geometry != null && geometry.IsComplete;
        }
        private static bool StageTempleLandingObserver(StreetPopulation streets,GameManager game)
        {
            var room=game.PlayerEnterExit.Interior;
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);
            if(!room || geometry==null || !geometry.EntryGround.HasValue)return false;
            Vector3 entry=geometry.EntryGround.Value;
            var blockers=new List<string>();
            foreach(var collider in Physics.OverlapCapsule(entry+Vector3.up*.78f,entry+Vector3.up*1.6f,
                CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore))
                blockers.Add(collider.name+":player="+(bool)collider.GetComponentInParent<PlayerMotor>());
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_LANDING_BLOCKERS: nativeEntryGround="+entry.ToString("R")+
                "; blockers=["+string.Join("|",blockers.ToArray())+"]; beforeObserverMove=true");
            foreach(var anchor in geometry.LocalAnchors)
            {
                Vector3 point=room.transform.TransformPoint(anchor);float distance=Vector3.Distance(point,entry);
                if(distance<2 || distance>6 || geometry.RouteToExit(point).Count<2 ||
                    Physics.OverlapCapsule(point+Vector3.up*.78f,point+Vector3.up*1.6f,CrowdMotion.BodyRadius,
                        ~0,QueryTriggerInteraction.Ignore).Length!=0)continue;
                game.PlayerMotor.transform.position=point+Vector3.up;Physics.SyncTransforms();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_LANDING_OBSERVER: nativeEntryGround="+entry.ToString("R")+
                    "; priorBlockers=["+string.Join("|",blockers.ToArray())+"]; actualObserverGround="+point.ToString("R")+
                    "; existingConnectedFloor=true; npcMoved=false; receiptChanged=false");
                return true;
            }
            throw new InvalidOperationException("Native temple landing observer has no existing connected clear floor: blockers=["+
                string.Join("|",blockers.ToArray())+"].");
        }
        // Select only an already materialized ordinary resident. The owned
        // fixture budget below is explicit test data, not a production income
        // source, and remains restricted to this command-line probe.
        private static bool TryFindEligibleTempleEntry(StreetPopulation streets, WorldStateStore store, int templeKey,
            out ResidentActor selected, out NpcActionOption entry, out string diagnostic)
        {
            selected = null; entry = null;
            var candidates = new List<ResidentActor>();
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if (actor && actor.gameObject.activeInHierarchy && actor.Resident != null) candidates.Add(actor);
            candidates.Sort((left, right) => string.CompareOrdinal(left.Resident.Id, right.Resident.Id));
            int active = 0, ordinary = 0, levelOne = 0, missingBinding = 0, observed = 0, enterOffers = 0;
            var eligible = new List<ResidentActor>();
            foreach (var actor in candidates)
            {
                active++;
                if (!actor.Resident.IsAlive || actor.Resident.IsQuestProtected || actor.Resident.NativeIdentity != null ||
                    actor.Resident.AgeGroup != ResidentAgeGroup.Adult || actor.IsSleeping || actor.IsConversing ||
                    CurrentReaction(streets,actor.Resident.Id) != null ||
                    store.ActiveNpcTask(actor.Resident.Id) != null) continue;
                ordinary++;
                var body = actor.GetComponent<DaggerfallEntityBehaviour>();
                if (body == null || body.Entity == null || body.Entity.Level != 1) continue;
                levelOne++;
                var owner = actor.GetComponent<NativeNpcInventoryOwner>();
                if (owner == null || owner.Binding == null || owner.Binding.PersonId != actor.Resident.Id)
                { missingBinding++; continue; }
                eligible.Add(actor);
            }
            var game = GameManager.Instance;
            var geometry = typeof(StreetPopulation).GetField("geometry", Private).GetValue(streets) as TownGeometry;
            var directory = typeof(StreetPopulation).GetField("directory", Private).GetValue(streets) as BuildingDirectory;
            if(!directory || geometry==null){diagnostic="current native temple geometry unavailable";return false;}
            Vector3 templePosition=TempleEntrancePosition(directory,templeKey);
            eligible.Sort((left,right)=>{
                int order=(left.transform.position-templePosition).sqrMagnitude.CompareTo((right.transform.position-templePosition).sqrMagnitude);
                return order!=0?order:string.CompareOrdinal(left.Resident.Id,right.Resident.Id);
            });
            string name = game == null || game.TalkManager == null ? null : game.TalkManager.GetBuildingNameForBuildingKey(templeKey);
            if (!string.IsNullOrWhiteSpace(name)) foreach (var actor in eligible)
            {
                // Inspect actual current hostile bodies. Do not disable them or
                // accept an ordinary lesson walk through their immediate vicinity.
                bool threatened = false;
                foreach (var threat in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                    if (GuardPopulation.IsEnemyThreat(threat) &&
                        (CivilianResponses.Sees(actor.transform.position, threat.transform.position,20) ||
                         CivilianResponses.Sees(templePosition+Vector3.up, threat.transform.position,20)))
                    { threatened = true; break; }
                if (threatened) continue;
                Vector3 observerGround;
                if (geometry == null || !directory || !NativeMoveToWaitProbe.TryObserverGround(geometry, directory,
                    actor.transform.position - Vector3.up, out observerGround)) continue;
                // The real conversation catalog requires the player within 8 m.
                // Move only the disposable observer to a surveyed clear point.
                game.PlayerMotor.transform.position = observerGround + Vector3.up; Physics.SyncTransforms();
                observed++;
                var options = streets.ConversationActionCandidates(actor.Resident.Id);
                // The controlled service fixture knows this exact real temple.
                // Use the existing native door adapter also used by Enter/Wait
                // acceptance when a named door is outside the bounded catalog.
                bool hasExactEntry = options.Exists(option => option != null && option.Kind == NpcActionKind.Enter &&
                    option.Target != null && option.Target.BuildingKey == templeKey);
                if (!hasExactEntry)
                {
                    var offer = typeof(StreetPopulation).GetMethod("OfferBuildingEntry", Private);
                    Require(offer != null, "The existing native building-entry adapter is missing.");
                    options = new List<NpcActionOption>();
                    offer.Invoke(streets, new object[] { actor, options, templeKey, name, null, true });
                }
                foreach (var option in options)
                {
                    if (option == null || option.Kind != NpcActionKind.Enter || option.Target == null ||
                        option.Target.Kind != NpcActionTargetKind.Building || option.Target.BuildingKey != templeKey) continue;
                    enterOffers++; selected = actor; entry = option;
                    diagnostic = "selected=" + actor.Resident.Id + "; controlledBudget=" + Guild.nonMemberTrainingCost +
                        "; exactTempleEnter=true";
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_SAFE_CANDIDATE: " + diagnostic + "; danger=" +
                        DescribeCurrentDanger(streets,store,actor.Resident.Id));
                    return true;
                }
            }
            diagnostic = "activeResidents=" + active + "; ordinaryAdults=" + ordinary + "; levelOne=" + levelOne +
                "; missingBinding=" + missingBinding + "; observedInRange=" + observed + "; eligibleEnterOffers=" + enterOffers +
                "; templeName=" + (string.IsNullOrWhiteSpace(name) ? "absent" : name);
            return false;
        }

        private static Vector3 TempleEntrancePosition(BuildingDirectory directory,int key)
        {
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            foreach(var door in doors.Doors)
                if(door.buildingKey==key && door.doorType==DoorTypes.Building)
                    return doors.transform.TransformPoint(door.buildingMatrix.MultiplyPoint3x4(door.centre));
            return Vector3.positiveInfinity;
        }
        private static AlarmPerson CurrentReaction(StreetPopulation streets,string id)
        { return (AlarmPerson)typeof(StreetPopulation).GetMethod("Reaction",Private).Invoke(streets,new object[]{id,Now()}); }
        private static string DescribeCurrentDanger(StreetPopulation streets,WorldStateStore store,string id)
        {
            var alarm=store.ActiveAlarm(id,Now());var reaction=CurrentReaction(streets,id);
            var threats=new List<string>();
            foreach(var target in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if(GuardPopulation.IsEnemyThreat(target))
                {
                    var native=target.GetComponent<DaggerfallEnemy>();
                    threats.Add(target.name+"#"+(native?native.LoadID:0)+"@"+target.transform.position.ToString("R"));
                }
            return "reaction="+reaction?.Reaction+",incident="+alarm?.Id+",threat="+alarm?.ThreatId+
                ",victim="+alarm?.VictimId+",hostiles=["+string.Join("|",threats.ToArray())+"]";
        }

        private static bool TrySetControlledFixtureBudget(WorldStateStore store, ResidentActor trainee,
            out int budget, out string reason)
        {
            budget = 0; reason = null;
            try
            {
                if (store == null || !trainee || trainee.Resident == null) { reason = "resident is unavailable"; return false; }
                var body = trainee.GetComponent<DaggerfallEntityBehaviour>();
                var owner = trainee.GetComponent<NativeNpcInventoryOwner>();
                if (body == null || body.Entity == null || body.Entity.Level < 1 || owner == null || owner.Binding == null ||
                    owner.Binding.PersonId != trainee.Resident.Id) { reason = "existing native body or inventory binding is unavailable"; return false; }
                if (!NativeNpcInventoryBridge.TryCapture(store, owner.Binding, out reason)) return false;
                var current = store.ReadNpcInventory(trainee.Resident.Id);
                budget = checked(Guild.nonMemberTrainingCost * body.Entity.Level);
                if (current == null || current.Revision != owner.Binding.Revision || budget < 1) { reason = "saved wallet binding changed"; return false; }
                if (!store.CommitNpcInventoryTransaction(
                    "temple-training-controlled-budget:" + trainee.Resident.Id + ":" + current.Revision,
                    new[] { new NpcInventoryUpdate { PersonId = trainee.Resident.Id, ExpectedRevision = current.Revision,
                        WalletGold = budget, Items = NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items) } }))
                { reason = "controlled wallet update was rejected"; return false; }
                if (!NativeNpcInventoryBridge.TryCapture(store, owner.Binding, out reason)) return false;
                current = store.ReadNpcInventory(trainee.Resident.Id);
                if (current == null || current.WalletGold != budget || current.Revision != owner.Binding.Revision)
                { reason = "controlled wallet update did not retain its bound revision"; return false; }
                return true;
            }
            catch (Exception error) { reason = error.Message; return false; }
        }

        private static bool TryFindCurrentOffer(StreetPopulation streets, DaggerfallInterior room, WorldStateStore store,
            Func<StaticNPC, NativePerson> resolve, string expectedTraineeId, out ResidentActor trainee, out NpcActionOption choice, out string diagnostic)
        {
            trainee = null; choice = null;
            var actors = new List<ResidentActor>(UnityEngine.Object.FindObjectsOfType<ResidentActor>());
            actors.Sort((left, right) => string.CompareOrdinal(left == null || left.Resident == null ? string.Empty : left.Resident.Id,
                right == null || right.Resident == null ? string.Empty : right.Resident.Id));
            var trainers = new List<StaticNPC>(room.GetComponentsInChildren<StaticNPC>(true));
            trainers.Sort((left, right) => (left == null ? 0 : left.Data.hash).CompareTo(right == null ? 0 : right.Data.hash));
            var reasons = new HashSet<string>(); int active = 0, direct = 0;
            foreach (var actor in actors)
            {
                if (!actor || !actor.gameObject.activeInHierarchy || actor.Resident == null || actor.Resident.Id != expectedTraineeId ||
                    actor.gameObject.scene != room.gameObject.scene) continue;
                active++;
                if (actor.IsSleeping || actor.IsConversing) { reasons.Add(actor.IsSleeping ? "resident sleeping" : "resident conversing"); continue; }
                if (!StagePlayerNearExistingActor(GameManager.Instance, actor))
                { reasons.Add("no observed conversation point beside the existing trainee"); continue; }
                int offeredLessons = 0;
                string firstSkill = null;
                var options=streets.ConversationActionOptions(actor.Resident.Id,"Train me.");
                foreach (var option in options)
                    if (option.Kind == NpcActionKind.Purchase && option.Target != null && option.Target.ContainerId == NativeNpcTempleTraining.TargetContainerId)
                    {
                        offeredLessons++;
                        if (firstSkill == null) firstSkill = option.Target.ItemId;
                        else if (choice == null && option.Target.ItemId != firstSkill) choice = option;
                    }
                if (choice != null)
                {
                    // Require a distinct non-first native lesson from the live
                    // catalog; one available skill cannot prove skill selection.
                    trainee = actor;
                    diagnostic = "resident=" + actor.Resident.Id + "; trainer=" + choice.Target.PersonId +
                        "; offeredLessons=" + offeredLessons + "; firstSkill=" + firstSkill +
                        "; selectedSkill=" + choice.Target.ItemId + "; selectedNonFirst=true; offer ready";
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_CATALOG: " + diagnostic);
                    foreach(var candidate in trainers)
                    {
                        var person=resolve(candidate);
                        if(person==null || person.Id!=choice.Target.PersonId)continue;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINER_APPROACH: "+
                            DescribeTempleTrainingApproach(streets,store,actor,candidate,room));break;
                    }
                    return true;
                }
                if (offeredLessons > 0) reasons.Add("no distinct non-first lesson; offeredLessons=" + offeredLessons + "; firstSkill=" + firstSkill);
                foreach (var trainer in trainers)
                for (int skill = 0; skill < 35; skill++)
                {
                    NativeNpcTempleTrainingOffer offer; string reason;
                    if (NativeNpcTempleTraining.TryPrepareOffer(store, actor, trainer, resolve, room.BuildingData.FactionId, skill, false, out offer, out reason))
                    {
                        direct++;
                        int gold = store.ReadNpcInventory(actor.Resident.Id).WalletGold;
                        reasons.Add(gold < offer.Price ? "wallet " + gold + " < native price " + offer.Price :
                            "approach: trainer="+offer.Trainer.Id+"; skill="+skill+"; catalogCount="+options.Count+"; "+
                            DescribeTempleTrainingApproach(streets,store,actor,trainer,room));
                        break;
                    }
                    if (!string.IsNullOrEmpty(reason)) reasons.Add(reason);
                }
            }
            var orderedReasons = new List<string>(reasons); orderedReasons.Sort(StringComparer.Ordinal);
            diagnostic = "activeResidents=" + active + "; staticNpcs=" + trainers.Count + "; directOffers=" + direct + "; reasons=" +
                string.Join(" | ", orderedReasons.GetRange(0, Math.Min(4, orderedReasons.Count)).ToArray());
            return false;
        }
        private static string DescribeTempleTrainingApproach(StreetPopulation streets,WorldStateStore store,ResidentActor actor,
            StaticNPC trainer,DaggerfallInterior room)
        {
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);
            var billboard=trainer.GetComponent<DaggerfallBillboard>();float halfHeight=billboard?billboard.Summary.Size.y*.5f:1f;
            Vector3 raw=trainer.transform.position,ground=raw-Vector3.up*halfHeight;
            var args=new object[]{actor,trainer,Vector3.zero};
            bool admitted=(bool)typeof(StreetPopulation).GetMethod("TryTempleTrainingApproach",Private).Invoke(streets,args);
            Vector3 point=(Vector3)args[2];
            int rawFloors=geometry.NearbyApproachPoints(room.transform.InverseTransformPoint(raw),1.8f,.4f,31).Count;
            int groundFloors=geometry.NearbyApproachPoints(room.transform.InverseTransformPoint(ground),1.8f,.4f,31).Count;
            var hits=new List<RaycastHit>(Physics.RaycastAll(raw+Vector3.up*.25f,Vector3.down,3,~0,QueryTriggerInteraction.Ignore));
            hits.Sort((left,right)=>left.distance.CompareTo(right.distance));string floors="";
            for(int i=0;i<Math.Min(4,hits.Count);i++)floors+=hits[i].collider.name+"@"+hits[i].point.ToString("F3")+" ";
            var active=store.ActiveNpcAction(actor.Resident.Id);
            return "nativeTransform="+raw.ToString("R")+"; nativeHalfHeight="+halfHeight+"; nativeGround="+ground.ToString("R")+
                "; rawHeightFloorCandidates="+rawFloors+"; groundFloorCandidates="+groundFloors+"; actualFloorHits=["+floors+
                "]; actualApproach="+admitted+"; actualPoint="+point.ToString("R")+
                "; groundDistance="+(admitted?Vector3.Distance(point,ground):-1)+
                "; groundLOS="+(admitted && CivilianResponses.Sees(point+Vector3.up,ground+Vector3.up,1.8f))+
                "; currentAction="+active?.Kind+"/"+active?.Phase;
        }
        private static bool StagePlayerNearExistingActor(GameManager game, ResidentActor actor)
        {
            if (!game || !actor || !actor.gameObject.activeInHierarchy) return false;
            Vector3 away = actor.transform.right; away.y = 0;
            if (away.sqrMagnitude < .01f) away = Vector3.forward;
            Vector3 point = actor.transform.position - Vector3.up + away.normalized * 1.5f + Vector3.up;
            game.PlayerMotor.transform.position = point; Physics.SyncTransforms();
            return Vector3.Distance(game.PlayerMotor.transform.position, actor.transform.position) <= 2.1f &&
                CivilianResponses.Sees(game.PlayerMotor.transform.position, actor.transform.position, 3);
        }
        private static void StageTempleServiceObserver(StreetPopulation streets,GameManager game,DaggerfallInterior room,ResidentActor actor)
        {
            var motions=(IDictionary)typeof(StreetPopulation).GetField("actionMotions",Private).GetValue(streets);
            object motion=motions[actor.Resident.Id];
            var follower=motion==null?null:motion.GetType().GetField("Indoor").GetValue(motion) as InteriorRoomFollower;
            Require(follower!=null,"Accepted native lesson has no actual recorded room route for observer clearance.");
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);
            Vector3 actual=actor.transform.position-Vector3.up;
            var route=follower.RemainingRoute(room.transform.InverseTransformPoint(actual));
            Vector3 previous=game.PlayerMotor.transform.position-Vector3.up,best=Vector3.zero;
            float nearest=float.PositiveInfinity,clearance=0;bool found=false;
            foreach(var anchor in geometry.LocalAnchors)
            {
                Vector3 point=room.transform.TransformPoint(anchor);float gap=Vector3.Distance(point,actual);
                if(gap<2 || gap>7.5f || Mathf.Abs(point.y-actual.y)>.5f || geometry.RouteToExit(point).Count<2 ||
                    FurnitureSeatCatalog.Blocks(point,point,CrowdMotion.BodyRadius))continue;
                float routeGap=float.PositiveInfinity;
                for(int i=1;i<route.Count;i++)
                {
                    var a=room.transform.TransformPoint(new Vector3((float)route[i-1].X,(float)route[i-1].Y,(float)route[i-1].Z));
                    var b=room.transform.TransformPoint(new Vector3((float)route[i].X,(float)route[i].Y,(float)route[i].Z));
                    if(Mathf.Abs(point.y-a.y)>2.1f && Mathf.Abs(point.y-b.y)>2.1f)continue;
                    a.y=b.y=point.y;Vector3 segment=b-a;
                    float along=segment.sqrMagnitude<.000001f?0:Mathf.Clamp01(Vector3.Dot(point-a,segment)/segment.sqrMagnitude);
                    routeGap=Mathf.Min(routeGap,Vector3.Distance(point,a+segment*along));
                }
                if(routeGap<1.2f || !CivilianResponses.Sees(point+Vector3.up,actor.transform.position,8))continue;
                bool occupied=false;
                foreach(var neighbour in StreetPopulation.ShopDepartureNeighbours(point,null))
                    if(Vector3.Distance(point,neighbour)<1.2f){occupied=true;break;}
                if(occupied || Physics.OverlapCapsule(point+Vector3.up*.78f,point+Vector3.up*1.6f,
                    CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore).Length!=0)continue;
                float distance=(point-previous).sqrMagnitude;if(distance>=nearest)continue;
                nearest=distance;best=point;clearance=routeGap;found=true;
            }
            Require(found,"Actual native lesson route has no existing connected clear observer floor outside its 1.2m corridor.");
            var obstacle=game.PlayerMotor.GetComponent<NavMeshObstacle>();var controller=game.PlayerMotor.GetComponent<CharacterController>();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_SERVICE_OBSERVER: priorGround="+previous.ToString("R")+
                "; playerColliderRadius="+(controller?controller.radius:-1)+"; nativeObstacle="+(bool)obstacle+
                "; nativeObstacleEnabled="+(obstacle && obstacle.enabled)+"; nativeObstacleRadius="+(obstacle?obstacle.radius:-1)+
                "; actualRoomRoutePoints="+route.Count+"; observerGround="+best.ToString("R")+"; minimumRouteClearance="+clearance+
                "; existingConnectedFloor=true; npcMoved=false; collidersChanged=false; acceptedRouteChanged=false");
            game.PlayerMotor.transform.position=best+Vector3.up;Physics.SyncTransforms();
        }
        private static short[] PlayerSkills(GameManager game) { var values = new short[35]; for (int i = 0; i < values.Length; i++) values[i] = game.PlayerEntity.Skills.GetPermanentSkillValue(i); return values; }
        private static long Experience(NpcActorProgress progress, int skill) { if (progress == null) return 0; var row = progress.Skills.Find(item => item.Skill == skill); return row == null ? 0 : row.Experience; }
        private static bool SamePlayer(GameManager game, int gold, short[] skills) { if (game.PlayerEntity.GetGoldAmount() != gold) return false; for (int i = 0; i < skills.Length; i++) if (game.PlayerEntity.Skills.GetPermanentSkillValue(i) != skills[i]) return false; return true; }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
