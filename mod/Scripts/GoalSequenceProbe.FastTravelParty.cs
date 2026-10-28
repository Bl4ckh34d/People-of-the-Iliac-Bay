using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GoalSequenceProbe
    {
        private static bool MultipleFastTravel => Environment.GetEnvironmentVariable("POTIB_FAST_TRAVEL_MULTIPLE")=="1";
        private static ResidentActor secondTravelCompanion;
        private const string SecondTravelPerson="s:1185098091:h:2:r:0";

        private static IEnumerator ResumeActualTravelParty(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;var store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            RequireFastTravel(game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior.EntryDoor.buildingKey==197127,
                "Party resume requires its actual saved second Inn.");
            float deadline=Time.realtimeSinceStartup+30;ResidentActor first=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                first=streets.FindActor(SequenceCompanion);secondTravelCompanion=streets.FindActor(SecondTravelPerson);
                if(first && first.gameObject.activeInHierarchy && secondTravelCompanion && secondTravelCompanion.gameObject.activeInHierarchy)break;
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);yield return null;
            }
            var firstJob=store.ReadPaidFollowForTravelTask(SequenceCompanion,FastTravelNow());
            var secondJob=store.ReadPaidFollowForTravelTask(SecondTravelPerson,FastTravelNow());
            RequireFastTravel(first && secondTravelCompanion && firstJob!=null && secondJob!=null,
                "Party checkpoint did not restore both actual paid companions and their active original actions.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_RESUMED: first="+firstJob.Id+"; second="+secondJob.Id+"; actualSavedContracts=true; newHire=false; newPayment=false");
            yield return ExitActualHiredInn(host,streets,first,firstJob,true);
        }

        private static IEnumerator ApproachSecondGuestInn(StreetPopulation streets,WorldStateStore store)
        {
            var game=GameManager.Instance;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            TravelerVisit visit=null;foreach(var candidate in store.TravelerVisitsFor(directory.MapID))
                if(candidate.PersonId==SecondTravelPerson && candidate.Admission==TravelerAdmission.Admitted)visit=candidate;
            RequireFastTravel(visit!=null,"The actual second mage guest is not admitted in this fixture.");
            DaggerfallWorkshop.StaticDoor entry=default(DaggerfallWorkshop.StaticDoor);bool found=false;
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallWorkshop.DaggerfallStaticDoors>())
                foreach(var door in owner.Doors??new DaggerfallWorkshop.StaticDoor[0])
                    if(door.doorType==DaggerfallWorkshop.DoorTypes.Building && door.buildingKey==visit.TavernKey)
                    {entry=door;entry.ownerPosition=owner.transform.position;entry.ownerRotation=owner.transform.rotation;found=true;break;}
            RequireFastTravel(found,"Actual second guest Inn has no exterior door.");
            var geometry=(TownGeometry)Field(typeof(StreetPopulation),"geometry").GetValue(streets);
            var from=directory.transform.InverseTransformPoint(game.PlayerMotor.transform.position-Vector3.up);
            var to=directory.transform.InverseTransformPoint(StreetPopulation.FollowExteriorDoorGround(entry));
            var points=geometry.RouteBetweenPoints(new SavedRoutePoint {X=from.x,Z=from.z},new SavedRoutePoint {X=to.x,Z=to.z});
            RequireFastTravel(points.Count>=2,"Actual second Inn has no surveyed approach.");
            var route=new List<Vector3>();foreach(var point in points)route.Add(geometry.ScenePoint(new RoutePoint(point.X,point.Z)));
            float metres=0;for(int i=1;i<route.Count;i++)metres+=Vector3.Distance(route[i-1],route[i]);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_INN_APPROACH: person="+SecondTravelPerson+"; inn="+visit.TavernKey+
                "; surveyedMetres="+metres+"; controlledObserverRoute=true; npcPlaced=false; timeSkipped=false");
            var controller=game.PlayerMotor.GetComponent<CharacterController>();
            var initialFollower=streets.FindActor(SequenceCompanion);var initialFollow=store.ActiveNpcAction(SequenceCompanion);
            RequireFastTravel(initialFollower && initialFollow!=null,"Actual first companion is missing before the street walk.");
            var lastFollowerPoint=initialFollower.transform.position;string lastNativeState="initial";
            var groundReader=Method(typeof(NativeDungeonFollowProbe),"PlayerGround",StaticMembers,0);
            var mover=Method(typeof(NativeDungeonFollowProbe),"MoveObserverToward",StaticMembers,3);
            bool motorEnabled=game.PlayerMotor.enabled;game.PlayerMotor.enabled=false;
            float walkDeadline=Time.realtimeSinceStartup+Mathf.Max(90,metres/1.25f+60),reportAt=0;
            try
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                for(int i=1;i<route.Count;i++)
                {
                    while(true)
                    {
                        var ground=(Vector3)groundReader.Invoke(null,null);var delta=route[i]-ground;
                        var follow=store.ReadNpcAction(initialFollow.Id);var follower=streets.FindActor(SequenceCompanion);
                        if(follower)
                        {
                            lastFollowerPoint=follower.transform.position;var navigation=follower.GetComponent<NativeResidentNavigation>();
                            lastNativeState=navigation?navigation.PathState+"/"+navigation.FailureReason:"no-navigation";
                        }
                        bool intact=follow!=null && !NpcActionExecutor.IsTerminal(follow) && follower && follower.gameObject.activeInHierarchy && follower==initialFollower;
                        if(!intact)
                        {
                            var actual=lastFollowerPoint-Vector3.up;var target=game.PlayerMotor.transform.position-Vector3.up;
                            var away=actual-target;away.y=0;var preferred=target+away.normalized*1.35f;
                            var localFrom=directory.transform.InverseTransformPoint(actual);
                            var localPreferred=directory.transform.InverseTransformPoint(preferred);var localTarget=directory.transform.InverseTransformPoint(target);
                            int preferredPoints=geometry.RouteBetweenPoints(new SavedRoutePoint {X=localFrom.x,Z=localFrom.z},
                                new SavedRoutePoint {X=localPreferred.x,Z=localPreferred.z}).Count;
                            int targetPoints=geometry.RouteBetweenPoints(new SavedRoutePoint {X=localFrom.x,Z=localFrom.z},
                                new SavedRoutePoint {X=localTarget.x,Z=localTarget.z}).Count;
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_ROUTE_DIAGNOSTIC: actual="+actual.ToString("F5")+"; target="+target.ToString("F5")+
                                "; preferred="+preferred.ToString("F5")+"; preferredRoutePoints="+preferredPoints+"; targetRoutePoints="+targetPoints);
                        }
                        RequireFastTravel(intact,
                            "First companion failed during actual street approach; corner="+i+"; player="+ground+"; lastNpc="+lastFollowerPoint+
                            "; native="+lastNativeState+"; phase="+(follow==null?"missing":follow.Phase.ToString())+
                            "; reason="+(follow?.FailureReason??"none")+"; now="+FastTravelNow()+"; finish="+initialFollow.FinishAt+
                            "; streetBody="+(bool)follower+"; originalBody="+(bool)initialFollower);
                        if(new Vector2(delta.x,delta.z).magnitude<=0.4f && Mathf.Abs(delta.y)<=0.6f)break;
                        RequireFastTravel(Time.realtimeSinceStartup<walkDeadline,"Second Inn observer route timed out; corner="+i+
                            "; actual="+ground+"; target="+route[i]+"; distance="+delta.magnitude);
                        if(Time.realtimeSinceStartup>=reportAt)
                        {reportAt=Time.realtimeSinceStartup+30;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_INN_WALK: corner="+i+"/"+route.Count+"; actual="+ground+"; target="+route[i]+"; npc="+lastFollowerPoint+"; native="+lastNativeState+"; phase="+follow.Phase);}
                        mover.Invoke(null,new object[]{route[i],controller,true});yield return null;
                    }
                }
            }
            finally {game.PlayerMotor.enabled=motorEnabled;}
            float deadline=Time.realtimeSinceStartup+30;ResidentActor original=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                original=streets.FindActor(SequenceCompanion);
                if(original && original.gameObject.activeInHierarchy && Vector3.Distance(original.transform.position,game.PlayerMotor.transform.position)<8)break;
                yield return null;
            }
            RequireFastTravel(original && original.gameObject.activeInHierarchy && Vector3.Distance(original.transform.position,game.PlayerMotor.transform.position)<8 &&
                store.ReadPaidFollowForTravelTask(SequenceCompanion,FastTravelNow())!=null,"Original paid companion did not reach the second Inn with the observer.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_INN_APPROACH_PASS: originalFollow=true; companionDistance="+
                Vector3.Distance(original.transform.position,game.PlayerMotor.transform.position)+"; controlledObserverRoute=true; npcPlaced=false");
        }

        // Acceptance setup uses a second existing native person and the normal
        // model/confirmation/payment path. No action or willingness is injected.
        private static IEnumerator HireSecondTravelCompanion(PeopleOfTheIliacBayMod host,StreetPopulation streets,WorldStateStore store)
        {
            secondTravelCompanion=null;
            var game=GameManager.Instance;int initialPurse=game.PlayerEntity.GetGoldAmount();
            RequireFastTravel(!game.IsPlayerInside,"Second guest discovery requires the actual exterior after Allan's exit.");
            yield return ApproachSecondGuestInn(streets,store);
            int entryCorrection=(int)Method(typeof(NpcFollowTransitionProbe),"NativeEntryCorrectionCount",StaticMembers,0).Invoke(null,null);
            Type fixtureType=typeof(NativeDungeonFollowProbe).GetNestedType("Fixture",System.Reflection.BindingFlags.NonPublic);
            object fixture=Activator.CreateInstance(fixtureType,true);
            fixtureType.GetField("residentId").SetValue(fixture,SecondTravelPerson);
            yield return (IEnumerator)Method(typeof(NativeDungeonFollowProbe),"FindOrdinaryFixtureCompanion",StaticMembers,4)
                .Invoke(null,new object[]{host,streets,store,fixture});
            yield return (IEnumerator)Method(typeof(NpcFollowTransitionProbe),"WaitForNativeEntryCorrection",StaticMembers,1)
                .Invoke(null,new object[]{entryCorrection});
            RequireFastTravel(game.PlayerEntity.GetGoldAmount()==initialPurse,"Second guest discovery changed actual player funds.");
            string selectedId=(string)fixtureType.GetField("residentId").GetValue(fixture);
            var selectedActor=streets.FindActor(selectedId);
            RequireFastTravel(selectedActor,"Second actual guest vanished during native entry correction.");
            NativeTravelerFixtureWarmup.PlacePlayerNearActualActor(game,streets,selectedActor);
            var candidates=new List<ResidentActor>();
            var inquiry=new NpcActionDecision {DurationSeconds=NpcPaidCompanionAction.DurationSeconds,
                Option=new NpcActionOption {Kind=NpcActionKind.Follow,PaidCompanion=true,
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"}}};
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                int wage;string reason;
                if(actor && actor.gameObject.activeInHierarchy && actor.Resident!=null && actor.Resident.Id==selectedId &&
                    actor.Resident.IsAlive && streets.FindActor(actor.Resident.Id)==actor &&
                    store.ActiveNpcAction(actor.Resident.Id)==null && store.ActiveNpcTask(actor.Resident.Id)==null &&
                    streets.TryReadPaidCompanionWage(actor.Resident.Id,out wage))
                {
                    bool eligible=streets.TryQuotePaidCompanion(actor.Resident.Id,inquiry,out wage,out reason);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_CANDIDATE: person="+actor.Resident.Id+"; archetype="+store.ResolveNpcArchetype(actor.Resident.Id)+"; eligible="+eligible+"; reason="+reason);
                    if(eligible)candidates.Add(actor);
                }
            }
            candidates.Sort((left,right)=>string.CompareOrdinal(left.Resident.Id,right.Resident.Id));
            RequireFastTravel(candidates.Count==1,"The selected real guest does not offer paid companionship.");
            var chosen=candidates[0];string id=chosen.Resident.Id;
            int quote;RequireFastTravel(streets.TryReadPaidCompanionWage(id,out quote),"Second native wage disappeared.");
            int purse=game.PlayerEntity.GetGoldAmount();
            RequireFastTravel(purse>=quote,"Actual purse cannot pay the second companion's normal wage.");
            var speech=host.GetComponent<BreezeTtsTrialBridge>();
            bool voice=speech && speech.VoiceEnabled,streaming=speech && speech.SocialStreamingEnabled;
            if(speech)speech.SetFeatureOptions(false,false);
            try
            {
                DaggerfallTalkWindow talk=null;
                yield return ConversationMoodProbe.OpenTalk(host,game,chosen,value=>talk=value);
                RequireFastTravel(talk!=null,"Second actual companion Talk did not open.");
                var client=(LocalDialogueClient)Field(host.GetType(),"localDialogue").GetValue(host);
                float timeout=Time.realtimeSinceStartup+90;
                while(Time.realtimeSinceStartup<timeout && (client.IsBusy || (bool)Field(host.GetType(),"conversationBusy").GetValue(host)))yield return null;
                RequireFastTravel(!client.IsBusy && !(bool)Field(host.GetType(),"conversationBusy").GetValue(host),"Second companion greeting did not finish.");
                var rows=(ListBox)Field(typeof(DaggerfallTalkWindow),"listboxConversation").GetValue(talk);int first=rows.Count;
                var options=streets.ConversationActionCandidates(id);
                RequireFastTravel(options.Exists(option=>option.PaidCompanion),"Actual second guest has no offered paid Follow after settled entry; distance="+
                    Vector3.Distance(chosen.transform.position,game.PlayerMotor.transform.position)+"; options="+options.Count);
                long consent=ReadLong(host,"dialogueWillingnessCompletionCount");
                string request="I would like to hire you as my travel companion for one day at your daily rate of "+quote+" gold. Will you accept?";
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_HIRE_BEGIN: person="+id+"; wage="+quote+"; actualCandidates="+candidates.Count+"; actualModel=true; speech=false");
                Method(host.GetType(),"SubmitFreeText",Members,2).Invoke(host,new object[]{talk,request});
                bool answered=false;timeout=Time.realtimeSinceStartup+180;
                while(Time.realtimeSinceStartup<timeout)
                {
                    if(NativeHireConfirmationOpen() && !answered)
                    {
                        VerifyNativeHirePopup();var box=(DaggerfallMessageBox)DaggerfallUI.UIManager.TopWindow;
                        var buttons=(List<Button>)Field(typeof(DaggerfallMessageBox),"buttons").GetValue(box);Button yes=null;
                        foreach(var button in buttons)if((DaggerfallMessageBox.MessageBoxButtons)button.Tag==DaggerfallMessageBox.MessageBoxButtons.Yes)yes=button;
                        RequireFastTravel(yes!=null,"Second hire has no native Yes button.");
                        Method(typeof(DaggerfallMessageBox),"ButtonClickHandler",Members,2).Invoke(box,new object[]{yes,Vector2.zero});answered=true;
                    }
                    if(!client.IsBusy && !(bool)Field(host.GetType(),"conversationBusy").GetValue(host) && HasReply(host,talk,first,request))break;
                    yield return null;
                }
                var hired=store.ActiveNpcAction(id);NpcServiceContract paid;
                RequireFastTravel(answered && ReadLong(host,"dialogueWillingnessCompletionCount")==consent+1 &&
                    ReadString(host,"dialogueWillingnessLastChoice")=="ACCEPT" && hired!=null &&
                    NpcPaidCompanionTravel.TryReadActiveTravel(store,hired,FastTravelNow(),out paid) &&
                    game.PlayerEntity.GetGoldAmount()==purse-quote,"Second actual native hire did not complete; person="+id+
                    "; choice="+ReadString(host,"dialogueWillingnessLastChoice")+"; answered="+answered+
                    "; distance="+Vector3.Distance(chosen.transform.position,game.PlayerMotor.transform.position)+
                    "; options="+streets.ConversationActionCandidates(id).Count+"; reply="+(FindReply(talk,first,request)??"<none>"));
                timeout=Time.realtimeSinceStartup+60;
                while(Time.realtimeSinceStartup<timeout && !NativeBiographyDialogueProbe.AllNewAnswersRevealed(rows,first))yield return null;
                RequireFastTravel(NativeBiographyDialogueProbe.AllNewAnswersRevealed(rows,first),"Second hire reply did not finish revealing.");
                secondTravelCompanion=chosen;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PARTY_HIRE_PASS: person="+id+"; action="+hired.Id+"; originalBody="+chosen.GetInstanceID()+
                    "; nativeYes=true; actualModelWillingness=ACCEPT; actualDebit="+quote+"; consentInjected=false");
                yield return ConversationMoodProbe.CloseTalk(host);
                yield return SaveFastTravelCheckpoint(game,"People - Daggerfall two hired companions");
            }
            finally {if(speech)speech.SetFeatureOptions(voice,streaming);}
        }

        private sealed class AdditionalTravelEvidence
        {
            internal ResidentActor Actor;
            internal string Action,Inventory,Relationship;
            internal double Accepted,Finish,Deadline,BlockStart,BlockEnd;
            private string contract;
            private int body,person,ownerId,funded;
            private NativeNpcInventoryOwner owner;
            private readonly Dictionary<ulong,DaggerfallUnityItem> items=new Dictionary<ulong,DaggerfallUnityItem>();
            private readonly Dictionary<ulong,EquipSlots> equipment=new Dictionary<ulong,EquipSlots>();
            internal AdditionalTravelEvidence(WorldStateStore store,CompanionFastTravelMember member)
            {
                Actor=member.Actor;Action=member.ActionId;Actor.CaptureState();
                var job=store.ReadNpcAction(Action);var paid=store.ReadNpcServiceContracts().Read(NpcPaidCompanionAction.ContractId(job));
                owner=Actor.GetComponent<NativeNpcInventoryOwner>();
                RequireFastTravel(owner && owner.Binding!=null && paid!=null,"Second travel member lacks native items or paid contract.");
                body=Actor.GetInstanceID();person=Actor.Person.GetInstanceID();ownerId=owner.GetInstanceID();
                Inventory=FastTravelInventory(store,Actor.Resident.Id);Relationship=FastTravelRelationship(store,Actor.Resident.Id);
                Accepted=job.AcceptedAt;Finish=job.FinishAt;Deadline=job.DeadlineAt;
                contract=paid.ContractId;BlockStart=paid.BlockStartAt;BlockEnd=paid.BlockEndAt;funded=paid.FundedGold;
                for(int i=0;i<owner.Binding.Items.Count;i++){var item=owner.Binding.Items.GetItem(i);items.Add(item.UID,item);equipment.Add(item.UID,item.EquipSlot);}
            }
            internal void Check(WorldStateStore store)
            {
                var job=store.ReadNpcAction(Action);var paid=store.ReadNpcServiceContracts().Read(contract);
                RequireFastTravel(Actor && Actor.gameObject.activeInHierarchy && Actor.GetInstanceID()==body && Actor.Person.GetInstanceID()==person &&
                    Actor.GetComponent<NativeNpcInventoryOwner>().GetInstanceID()==ownerId,"Second travel member changed actual body/person/inventory owner.");
                RequireFastTravel(job!=null && job.AcceptedAt==Accepted && job.FinishAt==Finish && job.DeadlineAt==Deadline &&
                    paid!=null && paid.BlockStartAt==BlockStart && paid.BlockEndAt==BlockEnd && paid.FundedGold==funded,
                    "Second member's actual consent or payment changed during travel.");
                Actor.CaptureState();RequireFastTravel(FastTravelInventory(store,Actor.Resident.Id)==Inventory &&
                    FastTravelRelationship(store,Actor.Resident.Id)==Relationship && owner.Binding.Items.Count==items.Count,
                    "Second member changed native inventory, wallet or relationship.");
                for(int i=0;i<owner.Binding.Items.Count;i++){var item=owner.Binding.Items.GetItem(i);DaggerfallUnityItem original;
                    RequireFastTravel(items.TryGetValue(item.UID,out original) && ReferenceEquals(item,original) && item.EquipSlot==equipment[item.UID],"Second member changed an item UID/object/equipment.");}
                int count=0;foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(candidate && candidate.gameObject.activeInHierarchy && candidate.Resident!=null && candidate.Resident.Id==Actor.Resident.Id)count++;
                RequireFastTravel(count==1 && NpcCompanionFastTravel.CanFollow(store,job,FastTravelNow()),"Second member duplicated or lost its accepted active Follow.");
            }
        }
    }
}
