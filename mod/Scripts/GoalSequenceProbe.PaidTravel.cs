using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GoalSequenceProbe
    {
        internal const string SequenceCompanion="s:1242772764:h:131074:r:0";
        private const string HiredSequenceCheckpoint="People - Daggerfall hired Allan ready";
        private static bool PaidTravelFixture => Environment.GetEnvironmentVariable("POTIB_GOAL_PAID_TRAVEL")=="1";
        private static bool ResumeHiredFixture => PaidTravelFixture && Environment.GetEnvironmentVariable("POTIB_GOAL_RESUME_HIRED")=="1";
        private static string SequenceBaselineSave => ResumeHiredFixture?HiredSequenceCheckpoint:PaidTravelFixture?"People - Daggerfall acceptance ready":SaveName;
        private static bool SequenceStartScene(GameManager game)
        {
            return !game.PlayerEnterExit.IsPlayerInsideBuilding || ResumeHiredFixture && game.PlayerEnterExit.Interior &&
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey==263177;
        }

        internal static IEnumerator HireActualSequenceCompanion(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;
            var store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            var person=store.Traveler(SequenceCompanion);
            if(store.WorldSeed!=1210190687u || person==null || person.Name!="Allan Stafford")
                throw new InvalidOperationException("Paid sequence requires the actual SAVE9 Allan Stafford source.");
            Field(host.GetType(),"localDialogueEnabled").SetValue(host,true);
            Field(host.GetType(),"dialoguePrewarmEnabled").SetValue(host,false);
            ResidentActor actor=null;NpcActionJob hired=null;float deadline;
            if(ResumeHiredFixture)
            {
                hired=store.ActiveNpcAction(SequenceCompanion);NpcServiceContract contract;
                if(!game.PlayerEnterExit.IsPlayerInsideBuilding || game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=263177 ||
                    !NpcPaidCompanionTravel.TryReadActiveTravel(store,hired,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),out contract))
                    throw new InvalidOperationException("Hired checkpoint has no actual active Allan contract at its recorded Inn.");
                deadline=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<deadline && !(actor=streets.FindActor(SequenceCompanion)))yield return null;
                if(!actor || !actor.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Hired checkpoint did not restore the actual Allan body.");
                hired=store.ActiveNpcAction(SequenceCompanion);
                if(hired==null || NpcActionExecutor.IsTerminal(hired) ||
                    !NpcPaidCompanionTravel.TryReadActiveTravel(store,hired,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),out contract))
                    throw new InvalidOperationException("Hired checkpoint lost its actual paid Follow while restoring the native body.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_HIRED_CHECKPOINT_RESUMED: person="+SequenceCompanion+"; follow="+hired.Id+
                    "; contract="+contract.ContractId+"; newHire=false; newPayment=false; hireModelCalls=false");
            }
            else
            {
            yield return EnterActualSequenceCompanion(host,streets);
            actor=streets.FindActor(SequenceCompanion);
            if(!actor || store.ActiveNpcAction(SequenceCompanion)!=null || store.ActiveNpcTask(SequenceCompanion)!=null)
                throw new InvalidOperationException("Actual Allan must be present and free before normal recruitment.");
            DaggerfallTalkWindow talk=null;
            yield return ConversationMoodProbe.OpenTalk(host,game,actor,value=>talk=value);
            if(talk==null)throw new InvalidOperationException("Native Allan recruitment Talk did not open.");
            var client=(LocalDialogueClient)Field(host.GetType(),"localDialogue").GetValue(host);
            deadline=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<deadline &&
                ((bool)Field(host.GetType(),"conversationBusy").GetValue(host) || client.IsBusy ||
                    (bool)Field(host.GetType(),"returningGreetingPending").GetValue(host)))yield return null;
            if(client.IsBusy || (bool)Field(host.GetType(),"conversationBusy").GetValue(host))
                throw new InvalidOperationException("Actual recruitment greeting has not finished.");
            int quote;
            if(!streets.TryReadPaidCompanionWage(SequenceCompanion,out quote))
                throw new InvalidOperationException("Allan's native daily wage is unavailable.");
            int purseBefore=game.PlayerEntity.GetGoldAmount();
            long consentBefore=ReadLong(host,"dialogueWillingnessCompletionCount");
            var rows=(ListBox)Field(typeof(DaggerfallTalkWindow),"listboxConversation").GetValue(talk);
            int first=rows.Count;
            string request="I would like to hire you as my travel companion for one day at your daily rate of "+quote+" gold. Will you accept?";
            Method(host.GetType(),"SubmitFreeText",Members,2).Invoke(host,new object[]{talk,request});
            deadline=Time.realtimeSinceStartup+180;bool confirmationLogged=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                hired=store.ActiveNpcAction(SequenceCompanion);
                if(hired!=null)break;
                if(NativeHireConfirmationOpen() && !confirmationLogged)
                {
                    VerifyNativeHirePopup();
                    confirmationLogged=true;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_HIRE_CONFIRM: person="+SequenceCompanion+
                        "; name=Allan Stafford; nativeYesRequired=true; quotedGold="+quote+"; controlledConsent=false");
                }
                if(HasReply(host,talk,first,request) && !client.IsBusy &&
                    !NativeHireConfirmationOpen())break;
                yield return null;
            }
            if(hired==null || !NpcPaidCompanionAction.IsPaidFollowJob(hired) || !confirmationLogged ||
                ReadLong(host,"dialogueWillingnessCompletionCount")!=consentBefore+1 ||
                ReadString(host,"dialogueWillingnessLastChoice")!="ACCEPT" || game.PlayerEntity.GetGoldAmount()!=purseBefore-quote)
                throw new InvalidOperationException("Normal paid recruitment did not complete: choice="+
                    ReadString(host,"dialogueWillingnessLastChoice")+"; confirmation="+confirmationLogged+
                    "; reply="+(FindReply(talk,first,request)??"<none>"));
            deadline=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<deadline && ((bool)Field(host.GetType(),"conversationBusy").GetValue(host) ||
                client.IsBusy || !NativeBiographyDialogueProbe.AllNewAnswersRevealed(rows,first)))yield return null;
            if(!NativeBiographyDialogueProbe.AllNewAnswersRevealed(rows,first))
                throw new InvalidOperationException("Actual hire answer did not finish revealing.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_HIRE_PASS: person="+SequenceCompanion+
                "; follow="+hired.Id+"; contract="+NpcPaidCompanionAction.ContractId(hired)+"; nativeDebit="+quote+
                "; normalModelWillingness=ACCEPT; nativeYes=true; reply="+FindReply(talk,first,request));
            yield return ConversationMoodProbe.CloseTalk(host);
            yield return SaveActualHiredCheckpoint(game,hired,true);
            }
            yield return ExitActualHiredInn(host,streets,actor,hired,false);
            if(MultipleFastTravel)
            {
                yield return HireSecondTravelCompanion(host,streets,store);
                deadline=Time.realtimeSinceStartup+300;float nextReport=0;
                while(Time.realtimeSinceStartup<deadline)
                {
                    actor=streets.FindActor(SequenceCompanion);var current=store.ReadNpcAction(hired.Id);
                    if(actor && actor.gameObject.activeInHierarchy)break;
                    RequireFastTravel(current!=null && !NpcActionExecutor.IsTerminal(current),"Original companion ended before second Inn arrival: "+(current?.FailureReason??"missing"));
                    if(Time.realtimeSinceStartup>=nextReport)
                    {
                        nextReport=Time.realtimeSinceStartup+30;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_FIRST_ARRIVAL_WAIT: phase="+current.Phase+"; now="+FastTravelNow()+
                            "; transition="+(current.FollowTransition!=null)+"; arrives="+(current.FollowTransition==null?0:current.FollowTransition.ArrivesAt));
                    }
                    DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);yield return null;
                }
                RequireFastTravel(actor && actor.gameObject.activeInHierarchy,"Original companion did not reach the second actual guest Inn within 300 seconds.");
                yield return ExitActualHiredInn(host,streets,actor,hired,true);
            }
        }

        private static IEnumerator ExitActualHiredInn(PeopleOfTheIliacBayMod host,StreetPopulation streets,ResidentActor actor,NpcActionJob hired,bool requireSecond)
        {
            var game=GameManager.Instance;var store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);float deadline;
            var secondJob=requireSecond?store.ReadPaidFollowForTravelTask(SecondTravelPerson,FastTravelNow()):null;
            string secondInventory=null;
            if(requireSecond)
            {
                RequireFastTravel(secondJob!=null && secondTravelCompanion,"Second actual hire is missing before the Inn exit.");
                secondTravelCompanion.CaptureState();secondInventory=FastTravelInventory(store,SecondTravelPerson);
            }
            // Native exit selects the closest exterior door to the observer.
            // The saved hire is beside the guest, not at an exit. Position only
            // the observer on the existing surveyed exit floor for this test.
            NpcFollowTransitionProbe.PositionObserverAtExit(streets);
            yield return null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_EXIT_APPROACH: surveyedExitFloor=true; controlledObserverPlacement=true; npcMovedByProbe=false");
            var actualExitDoor=game.PlayerEnterExit.Interior.EntryDoor;
            game.PlayerEnterExit.TransitionExterior();
            var postExit=store.ReadNpcAction(hired.Id);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_HIRE_EXIT_JOB: phase="+(postExit==null?"missing":postExit.Phase.ToString())+
                "; transit="+(postExit!=null && postExit.FollowTransition!=null)+"; reason="+(postExit?.FailureReason??"none"));
            if(postExit==null || NpcActionExecutor.IsTerminal(postExit))
                throw new InvalidOperationException("Actual paid Follow ended at the Inn exit: "+(postExit?.FailureReason??"missing action"));
            // TransitionExterior schedules native repositioning. Waiting for
            // this owner's exterior scene also yields past that native frame;
            // otherwise the subsequent reposition undoes observer clearance.
            yield return NpcFollowTransitionProbe.WaitForScene(streets,false);
            deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline && (game.IsPlayerInside || !game.StreamingWorld.IsReady ||
                game.StreamingWorld.IsRepositioningPlayer))yield return null;
            if(game.IsPlayerInside || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer)
                throw new InvalidOperationException("Actual paid-hire observer did not reach the native exterior.");
            // The native player exit occupies the follower's exact landing. Reuse
            // the existing dungeon-exit observer checks before awaiting that body.
            var playerGround=Method(typeof(NativeDungeonFollowProbe),"PlayerGround",StaticMembers,0);
            var start=(Vector3)playerGround.Invoke(null,null);
            var forward=DaggerfallStaticDoors.GetDoorNormal(actualExitDoor);forward.y=0;
            // The ordinary doorway may already have replaced the indoor body.
            // Use the actual destination body for the existing observer-clearance check.
            actor=streets.FindActor(SequenceCompanion);
            var follower=actor?actor.transform:null;
            var clear=Vector3.zero;bool route=false;
            foreach(float angle in new[]{0f,45f,-45f,90f,-90f,135f,-135f,180f})
            {
                object[] floorArguments={start+Quaternion.Euler(0,angle,0)*forward.normalized*3,null,Vector3.zero};
                if(!(bool)Method(typeof(NativeDungeonFollowProbe),"StaticFloor",StaticMembers,3).Invoke(null,floorArguments))
                {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_EXIT_ROUTE: angle="+angle+"; endpointFloor=false; endpoint="+floorArguments[0]);continue;}
                var candidate=(Vector3)floorArguments[2];
                if(!(bool)Method(typeof(NativeDungeonFollowProbe),"ClearSupportedRoute",StaticMembers,3)
                    .Invoke(null,new object[]{new[]{start,candidate},null,follower}))
                {
                    var blocker=(Collider)Method(typeof(NativeDungeonFollowProbe),"BodyBlocker",StaticMembers,5)
                        .Invoke(null,new object[]{start,candidate,follower,true,CrowdMotion.BodyRadius});
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_EXIT_ROUTE: angle="+angle+"; endpoint="+candidate+
                        "; blocker="+(blocker?blocker.name+"@"+blocker.transform.position:"none")+"; ignoredFollower="+(bool)follower);
                    continue;
                }
                clear=candidate;route=true;break;
            }
            var controller=game.PlayerMotor.GetComponent<CharacterController>();
            if(!route || !controller || !controller.enabled)
                throw new InvalidOperationException("Actual paid-hire exit has no supported clear observer departure; start="+start+"; outward="+forward);
            controller.Move(clear-start);Physics.SyncTransforms();
            yield return null;
            var actual=(Vector3)playerGround.Invoke(null,null);
            if(Vector3.Distance(actual,clear)>0.55f || Vector3.Distance(actual,start)<2.4f)
                throw new InvalidOperationException("Native paid-hire observer did not clear the follower's landing.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_SEQUENCE_EXIT_OBSERVER_CLEAR: nativeFloor=true; supportedRoute=true; playerOnly=true; controllerMove=true; distance="+
                Vector3.Distance(actual,start)+"; npcMovedByProbe=false");
            deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline)
            {
                var current=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
                actor=streets.FindActor(SequenceCompanion);
                var second=requireSecond?streets.FindActor(SecondTravelPerson):null;
                var secondCurrent=requireSecond?current.ReadPaidFollowForTravelTask(SecondTravelPerson,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()):null;
                if(!game.PlayerEnterExit.IsPlayerInside && streets.Ready && streets.OutdoorRoutesPrepared && actor &&
                    actor.gameObject.activeInHierarchy && current.ReadPaidFollowForTravelTask(SequenceCompanion,
                        DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())!=null &&
                    (!requireSecond || second && second.gameObject.activeInHierarchy && secondCurrent!=null && secondCurrent.Id==secondJob.Id))
                {
                    if(requireSecond)
                    {
                        second.CaptureState();RequireFastTravel(FastTravelInventory(current,SecondTravelPerson)==secondInventory &&
                            secondCurrent.AcceptedAt==secondJob.AcceptedAt && secondCurrent.FinishAt==secondJob.FinishAt && secondCurrent.DeadlineAt==secondJob.DeadlineAt,
                            "Second companion's native Inn crossing changed inventory or original contract dates.");
                        // This ordinary unobserved doorway path recreates its
                        // destination body. Actual fast-travel identity is captured afterward.
                        secondTravelCompanion=second;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_INN_EXIT_PASS: originalActions=true; secondInventoryAndDates=true; nativeDestinationBodies=true; unobservedDoorway=true");
                    }
                    yield break;
                }
                yield return null;
            }
            postExit=store.ReadNpcAction(hired.Id);
            if(requireSecond)
            {
                var second=streets.FindActor(SecondTravelPerson);var current=store.ReadNpcAction(secondJob.Id);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_SECOND_EXIT_PENDING: body="+(bool)second+"; active="+(second && second.gameObject.activeInHierarchy)+
                    "; phase="+(current==null?"missing":current.Phase.ToString())+"; reason="+(current?.FailureReason??"none")+
                    "; transition="+(current!=null && current.FollowTransition!=null)+"; arrives="+(current?.FollowTransition==null?0:current.FollowTransition.ArrivesAt));
            }
            if(postExit!=null && postExit.FollowTransition!=null)
            {
                var landing=(Vector3)Method(typeof(StreetPopulation),"ActionWorldPoint",Members,1)
                    .Invoke(streets,new object[]{postExit.FollowTransition.Destination});
                var blockers=new System.Collections.Generic.List<string>();
                foreach(var collider in Physics.OverlapCapsule(landing+Vector3.up*0.78f,landing+Vector3.up*1.6f,
                    CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore))
                    if(blockers.Count<8)blockers.Add(collider.name+"@"+collider.transform.position);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_EXIT_PENDING: now="+DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+
                    "; arrives="+postExit.FollowTransition.ArrivesAt+"; paused="+GameManager.IsGamePaused+
                    "; landing="+landing+"; blockers="+string.Join("|",blockers.ToArray()));
            }
            throw new InvalidOperationException("Actually hired Allan did not follow through the native Inn exit within 30 seconds: phase="+
                (postExit==null?"missing":postExit.Phase.ToString())+"; reason="+(postExit?.FailureReason??"none")+
                "; transit="+(postExit!=null && postExit.FollowTransition!=null)+"; streetsReady="+streets.Ready+
                "; routesPrepared="+streets.OutdoorRoutesPrepared+"; actualBody="+(bool)actor);
        }

        private static bool NativeHireConfirmationOpen()
        {
            var window=DaggerfallUI.UIManager.TopWindow;
            return window!=null && window.GetType()==typeof(PeopleOfTheIliacBayMod).GetNestedType("PersonalCommerceConfirmBox",BindingFlags.NonPublic);
        }

        internal static IEnumerator EnterActualSequenceCompanion(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var owner=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            var person=owner.Traveler(SequenceCompanion);
            if(owner.WorldSeed!=1210190687u || person==null || person.Name!="Allan Stafford")
                throw new InvalidOperationException("Concrete companion fixture requires actual SAVE9 Allan Stafford.");
            Type sourceFixture=typeof(NativeDungeonFollowProbe).GetNestedType("Fixture",BindingFlags.NonPublic);
            object source=Activator.CreateInstance(sourceFixture,true);
            sourceFixture.GetField("residentId").SetValue(source,SequenceCompanion);
            yield return (IEnumerator)Method(typeof(NativeDungeonFollowProbe),"FindOrdinaryFixtureCompanion",StaticMembers,4)
                .Invoke(null,new object[]{host,streets,owner,source});
        }

        internal static IEnumerator SaveActualHiredCheckpoint(GameManager game,NpcActionJob hired,bool modelHire)
        {
            yield return (IEnumerator)Method(typeof(NativeBiographyDialogueProbe),"Save",StaticMembers,2)
                .Invoke(null,new object[]{game,HiredSequenceCheckpoint});
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_HIRED_CHECKPOINT: save=People - Daggerfall hired Allan ready; person="+
                SequenceCompanion+"; follow="+hired.Id+"; contract="+NpcPaidCompanionAction.ContractId(hired)+
                "; actualNativeSave=true; dialogueModel="+modelHire.ToString().ToLowerInvariant());
        }

        internal static void VerifyNativeHirePopup()
        {
            var box=(DaggerfallMessageBox)DaggerfallUI.UIManager.TopWindow;
            var label=(MultiFormatTextLabel)typeof(DaggerfallMessageBox).GetField("label",Members).GetValue(box);
            var panel=(Panel)typeof(DaggerfallMessageBox).GetField("messagePanel",Members).GetValue(box);
            string displayed=string.Join(" ",label.TextLabels.ConvertAll(line=>line.Text).ToArray());
            if(displayed.IndexOf("one day",StringComparison.OrdinalIgnoreCase)<0 ||
                displayed.IndexOf("game hours",StringComparison.OrdinalIgnoreCase)>=0 ||
                displayed.IndexOf("Follow",StringComparison.Ordinal)>=0 || displayed.IndexOf("native",StringComparison.OrdinalIgnoreCase)>=0)
                throw new InvalidOperationException("Hire confirmation exposes internal wording instead of its natural day offer.");
            if(!label.WrapText || !label.WrapWords || label.MaxTextWidth!=288 || label.Size.x>288 || label.LineCount<5 ||
                panel.Size.x>box.NativePanel.Size.x || panel.Size.y>box.NativePanel.Size.y)
                throw new InvalidOperationException("Native hire confirmation text exceeds its bounded wrapped panel.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GOAL_HIRE_POPUP_LAYOUT_PASS: nativeWrapping=true; maxTextWidth="+label.MaxTextWidth+
                "; textWidth="+label.Size.x+"; textHeight="+label.Size.y+"; lines="+label.LineCount+"; panel="+panel.Size+"; fitsNativePanel=true");
        }
    }
}
