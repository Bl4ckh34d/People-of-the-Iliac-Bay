using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NpcFollowTransitionProbe
    {
        private static IEnumerator RunRequestedBuildingActions(PeopleOfTheIliacBayMod host,StreetPopulation streets,
            string residentId,DaggerfallStaticDoors owner,StaticDoor entry)
        {
            var game=GameManager.Instance;var store=Store(streets);
            foreach(var kind in new[] {NpcActionKind.Exit,NpcActionKind.Enter})
            {
                var actor=streets.FindActor(residentId);
                NeedBuilding(actor && actor.gameObject.activeInHierarchy,"The real patron is not available for "+kind);
                actor.CaptureInventory();var before=store.ReadNpcInventory(residentId);
                bool sourceInside=game.PlayerEnterExit.IsPlayerInsideBuilding;
                var sourcePosition=actor.transform.position;
                game.PlayerMotor.transform.position=actor.transform.position;Physics.SyncTransforms();
                game.TalkManager.TalkToMobileNPC(actor.Person);DaggerfallTalkWindow talk=null;
                yield return WaitForFollowTalk(host,actor,residentId,value=>talk=value);
                sourcePosition=actor.transform.position;
                float initialDoorDistance=sourceInside?Vector3.Distance(actor.transform.position-Vector3.up,RoomGeometry(streets).EntryGround.Value):0;
                var candidates=streets.ConversationActionCandidates(residentId);
                NeedBuilding(candidates.Exists(option=>option.Kind==kind),"Exact door action was absent from native candidates: "+kind);
                var client=(LocalDialogueClient)typeof(PeopleOfTheIliacBayMod).GetField("localDialogue",Private).GetValue(host);
                var previousTrace=client.ForegroundClassificationTrace;
                client.ForegroundClassificationTrace=(requestJson,responseJson,output)=>
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BUILDING_ACTION_MODEL: result="+output+"; request="+requestJson);
                var reaction=typeof(TalkManager).GetField("reactionToPlayer",Private);var previousReaction=reaction.GetValue(game.TalkManager);
                string request=kind==NpcActionKind.Exit?"Could you go outside?":"Please enter "+game.TalkManager.GetBuildingNameForBuildingKey(entry.buildingKey)+".";
                try
                {
                    // Willingness is still decided by the real model. Only its native
                    // baseline reaction is a controlled friendly fixture condition.
                    reaction.SetValue(game.TalkManager,90);
                    typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText",Private).Invoke(host,new object[] {talk,request});
                    float until=Time.realtimeSinceStartup+90;
                    while(IsConversationBusy(host) && Time.realtimeSinceStartup<until)yield return null;
                    NeedBuilding(!IsConversationBusy(host),"Actual command did not finish: "+kind);
                }
                finally {reaction.SetValue(game.TalkManager,previousReaction);client.ForegroundClassificationTrace=previousTrace;}
                var job=store.ActiveNpcAction(residentId);
                NeedBuilding(job!=null && job.Kind==kind && job.Phase==NpcActionPhase.Accepted && job.BuildingTransitionReceipt==null,
                    "Actual Gemma request did not admit "+kind+"; selected="+(job==null?"none":job.Kind+":"+job.Phase));
                var motions=(IDictionary)typeof(StreetPopulation).GetField("actionMotions",Private).GetValue(streets);
                var motion=motions[residentId];
                var departure=(SelectedSeatAccess)motion.GetType().GetField("DepartureSeat").GetValue(motion);
                NeedBuilding(departure==null || Vector3.Distance(actor.transform.position-Vector3.up,departure.Slot.Ground)<=1.6f,
                    "Action inherited a seat exit the standing patron never reached; distance="+
                    (departure==null?0:Vector3.Distance(actor.transform.position-Vector3.up,departure.Slot.Ground)));
                NeedBuilding(game.PlayerEnterExit.IsPlayerInsideBuilding==sourceInside &&
                    Vector3.Distance(sourcePosition,actor.transform.position)<0.15f,"Command moved a body during Talk.");
                string id=job.Id;CloseFollowTalk(talk);game.PauseGame(false);
                float deadline=Time.realtimeSinceStartup+100;Vector3 previous=actor.transform.position;float walked=0,maxStep=0;
                float diagnosticAt=Time.realtimeSinceStartup+2;
                while(Time.realtimeSinceStartup<deadline)
                {
                    job=store.ReadNpcAction(id);
                    if(NpcActionExecutor.IsTerminal(job))break;
                    var moving=streets.FindActor(residentId);
                    if(moving && moving.gameObject.activeInHierarchy)
                    {float step=Vector3.Distance(previous,moving.transform.position);walked+=step;maxStep=Math.Max(maxStep,step);previous=moving.transform.position;}
                    if(Time.realtimeSinceStartup>=diagnosticAt)
                    {
                        diagnosticAt=Time.realtimeSinceStartup+20;
                        var type=typeof(StreetPopulation);
                        var families=(System.Collections.Generic.HashSet<string>)type.GetField("simulatedFamilyFrame",Private).GetValue(streets);
                        var initializing=(System.Collections.Generic.HashSet<string>)type.GetField("pendingHouseholdInitialization",Private).GetValue(streets);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BUILDING_ACTION_STATE: phase="+job.Phase+"; revision="+job.Revision+
                            "; paused="+GameManager.IsGamePaused+"; conversing="+(moving && moving.IsConversing)+
                            "; family="+families.Contains(residentId)+"; initializing="+initializing.Contains(residentId)+
                            "; enabled="+streets.enabled+"; top="+DaggerfallUI.UIManager.TopWindow.GetType().Name+
                            "; time="+Now()+"; position="+(moving?moving.transform.position.ToString():"absent"));
                    }
                    yield return null;
                }
                job=store.ReadNpcAction(id);
                NeedBuilding(job.Phase==NpcActionPhase.Completed && job.BuildingTransitionReceipt!=null,
                    "Native door did not complete: "+kind+"; "+job.Phase+"; "+job.FailureReason);
                NeedBuilding(game.PlayerEnterExit.IsPlayerInsideBuilding==sourceInside,"NPC command transitioned the player.");
                NeedBuilding(!streets.FindActor(residentId) && maxStep<1.5f,"NPC did not leave through the door, or teleported on its source route.");
                NeedBuilding(initialDoorDistance<=1.5f || walked>0.5f,"A distant source patron did not actually walk to the door.");
                var receipt=job.BuildingTransitionReceipt;
                NeedBuilding(receipt.ActorId==residentId && receipt.BuildingKey==entry.buildingKey &&
                    receipt.DoorId==job.Target.DoorId && receipt.SourceSpaceId==job.Target.WorldSpaceId &&
                    receipt.DestinationSpaceId==job.Target.DestinationSpaceId,"Door receipt lost exact native identity.");
                if(kind==NpcActionKind.Exit)
                {
                    // Waiting in the source room cannot make the departed NPC reappear
                    // here when its old action window expires.
                    DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(0,job.DeadlineAt-Now()+2));
                    for(int frame=0;frame<4;frame++)yield return null;
                    NeedBuilding(store.ActiveNpcAction(residentId)!=null && store.ActiveNpcAction(residentId).Id==id &&
                        !streets.FindActor(residentId),"Expired handoff respawned the departed NPC in the visible source room.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BUILDING_ACTION_SOURCE_STAYS_EMPTY: afterWindow=true; sameSourceRoom=true");
                }
                var copy=new WorldStateStore(0);
                copy.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
                NeedBuilding(copy.ReadNpcAction(id).BuildingTransitionReceipt.DoorId==receipt.DoorId &&
                    copy.ReadNpcAction(id).ReturningToRoutine,"Native serializer lost pending destination handoff.");
                if(kind==NpcActionKind.Exit)yield return ExitFixture(streets);
                else yield return EnterFixture(streets,owner,entry);
                deadline=Time.realtimeSinceStartup+35;actor=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    actor=streets.FindActor(residentId);
                    if(actor && actor.gameObject.activeInHierarchy)break;
                    yield return null;
                }
                NeedBuilding(actor && actor.gameObject.activeInHierarchy && actor.Resident.Id==residentId,
                    "The same NPC did not materialize at its actual destination door: "+kind);
                var landed=store.ReadNpcAction(id).LastPosition;
                NeedBuilding(landed!=null && landed.SettlementId==job.Target.SettlementId &&
                    (kind==NpcActionKind.Enter?landed.Kind==NpcActionTargetKind.IndoorPoint && landed.BuildingKey==entry.buildingKey:
                        landed.Kind==NpcActionTargetKind.WorldPoint && landed.WorldSpaceId==job.Target.DestinationSpaceId),
                    "A routine respawn cannot substitute for the exact native destination handoff.");
                actor.CaptureInventory();var after=store.ReadNpcInventory(residentId);
                NeedBuilding(before.WalletGold==after.WalletGold && before.Items.Count==after.Items.Count,"Door action changed the NPC's belongings.");
                foreach(var item in before.Items)
                    NeedBuilding(after.Items.Exists(i=>i.Uid==item.Uid && i.NativePayload==item.NativePayload),"Door action changed an exact item.");
                NeedBuilding(store.ReadNpcAction(id).BuildingTransitionReceipt.ObservedGameTime==receipt.ObservedGameTime,
                    "Arrival or routine movement rewrote the native crossing time.");
                var landedCopy=new WorldStateStore(0);
                landedCopy.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
                NeedBuilding(landedCopy.ReadNpcAction(id).BuildingTransitionReceipt.DestinationObserved,
                    "Native serializer lost the observed destination and could rearm its original handoff.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BUILDING_ACTION_NATIVE_PASS: kind="+kind+
                    "; actualGemma=true; nativeDoor=true; sourceWalk="+walked+"; maxStep="+maxStep+
                    "; playerNotMoved=true; destinationObserved=true; nativeSerializer=true; exactIdentityAndItems=true");
            }
        }
        private static void NeedBuilding(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Requested building action: "+message);}
    }
}
