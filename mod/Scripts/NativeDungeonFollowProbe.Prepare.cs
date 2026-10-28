using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        // Explicit fixture staging is separate from measured acceptance. It may
        // relocate the disposable observer and existing paid body, never invent
        // an NPC, journey receipt, route, native item, floor or service identity.
        private static IEnumerator PrepareFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,RoadTravelActors roads,
            WorldStateStore store,Fixture fixture)
        {
            var game=GameManager.Instance;
            Need(fixture!=null && fixture.mapId==187853213 && fixture.stagedDoorDistance>=2.2f && fixture.stagedDoorDistance<=8 && roads && store!=null && streets.Ready &&
                !game.PlayerEnterExit.IsPlayerInsideDungeon && NativeProbeText.IsTestCharacter(game.PlayerEntity.Name),
                "Dungeon staging requires an owned loaded settlement/Inn, its real population, and the explicit Privateer's Hold map.");
            yield return FindOrdinaryFixtureCompanion(host,streets,store,fixture);
            Need(!string.IsNullOrWhiteSpace(fixture.residentId),"No actual nearby eligible adventurer exists; use the ordinary traveler warmup or supply an existing paid resident ID.");
            var actor=streets.FindActor(fixture.residentId);if(!actor)actor=roads.FindPaidCompanion(fixture.residentId);
            Need(actor && actor.Resident!=null && actor.Resident.IsAlive && actor.gameObject.activeInHierarchy &&
                !actor.Resident.IsQuestProtected,"Dungeon staging cannot find the existing real native actor.");
            var action=store.ActiveNpcAction(fixture.residentId);NpcServiceContract paid;
            if(!NpcPaidCompanionTravel.TryReadActiveTravel(store,action,Now(),out paid))
            {
                Need(action==null,"Dungeon staging refuses to replace an existing non-paid commitment.");
                int dailyGold;Need(streets.TryReadPaidCompanionWage(fixture.residentId,out dailyGold),"Actual fixture companion level is unavailable.");
                int fundsBefore=game.PlayerEntity.GetGoldAmount();int added=Math.Max(0,dailyGold-fundsBefore);
                if(added>0)game.PlayerEntity.GoldPieces=checked(game.PlayerEntity.GoldPieces+added);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_STAGE_FUNDS: before="+fundsBefore+"; added="+added+
                    "; after="+game.PlayerEntity.GetGoldAmount()+"; setupOnly=true; admissionDebitNotYetMade=true");
                NpcActionDecision decision=null;
                foreach(var option in streets.ConversationActionCandidates(fixture.residentId))
                    if(option.PaidCompanion){decision=new NpcActionDecision {Option=option,DurationSeconds=NpcPaidCompanionAction.DurationSeconds};break;}
                Need(decision!=null,"Actual adventurer has no current paid Follow offer.");
                int fee;string reason;Need(streets.TryQuotePaidCompanion(fixture.residentId,decision,out fee,out reason),reason);
                int fundedPurse=game.PlayerEntity.GetGoldAmount();string sessionId="dungeon-stage:"+Guid.NewGuid().ToString("N");
                NpcConversationCommandSession session;
                Need(store.TryBeginNpcConversationCommandSession(fixture.residentId,sessionId,out session),"Could not open explicit fixture hire command.");
                try
                {
                    var binding=new ConversationContextBinding(ConversationActorCategory.Mobile,fixture.residentId,null,sessionId,1,1,
                        store.NpcFrameworkScopeId,"","native-dungeon-follow-staging",1);
                    NpcConversationCommandTurn turn;
                    Need(store.TryCaptureNpcConversationCommandTurn(session,binding,"Hire you for "+fee+" gold for one day.",out turn),"Fixture hire command was not captured.");
                    Need(streets.TryHirePaidCompanion(fixture.residentId,"dungeon-stage:"+Guid.NewGuid().ToString("N"),decision,turn,fee,out reason),reason);
                }
                finally{store.TryCloseNpcConversationCommandSession(session);}
                action=store.ActiveNpcAction(fixture.residentId);
                int paidPurse=game.PlayerEntity.GetGoldAmount();
                Need(paidPurse==fundedPurse-fee,"Native hire debit mismatch: before="+fundedPurse+"; after="+paidPurse+"; quoted="+fee);
                Need(NpcPaidCompanionTravel.TryReadActiveTravel(store,action,Now(),out paid),
                    "Native hire contract is not active at the actual clock: now="+Now()+"; accepted="+(action==null?-1:action.AcceptedAt)+
                    "; finish="+(action==null?-1:action.FinishAt)+"; paidJob="+NpcPaidCompanionAction.IsPaidFollowJob(action));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_STAGE_HIRE: resident="+fixture.residentId+"; action="+action.Id+
                    "; nativeDebit="+fee+"; contract="+paid.ContractId+"; explicitFixtureConsent=true; dialogueModel=false");
            }
            actor.CaptureState();string inventory=InventorySignature(store.ReadNpcInventory(fixture.residentId));
            string contract=ContractSignature(store,action);string actionId=action.Id;
            Need(roads.TryDeferPaidCompanion(actor,store,Now()),"Existing paid exterior owner refused the staged traveler.");
            streets.ReleaseTransferredActor(actor);
            // No frame occurs between releasing the source street body, native
            // source exit and native world transport. This is fixture transport,
            // not evidence that a companion walked through an Inn or across Tamriel.
            if(game.PlayerEnterExit.IsPlayerInsideBuilding)game.PlayerEnterExit.TransitionExterior();
            Need(!game.PlayerEnterExit.IsPlayerInside,"Native fixture exit did not leave the source Inn.");
            DFLocation location;
            Need(DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall","Privateer's Hold",out location) &&
                location.Loaded && location.HasDungeon && location.MapTableData.MapId==fixture.mapId,"Native Privateer's Hold location is unavailable.");
            var pixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
            game.StreamingWorld.TeleportToCoordinates(pixel.X,pixel.Y,StreamingWorld.RepositionMethods.RandomStartMarker);
            DaggerfallStaticDoors entranceOwner=null;StaticDoor entrance=default(StaticDoor);float deadline=Time.realtimeSinceStartup+30;
            Vector3 normal=Vector3.zero,doorGround=Vector3.zero,playerGround=Vector3.zero,actorGround=Vector3.zero;bool floorsReady=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                if(game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer && directory && directory.MapID==fixture.mapId &&
                    game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId==fixture.mapId)
                {
                    entranceOwner=null;float lowest=float.MaxValue;
                    foreach(var collection in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    {
                        foreach(var raw in collection.Doors??new StaticDoor[0])
                        {
                            if(raw.doorType!=DoorTypes.DungeonEntrance)continue;
                            var door=raw;door.ownerPosition=collection.transform.position;door.ownerRotation=collection.transform.rotation;
                            float height=DaggerfallStaticDoors.GetDoorPosition(door).y;
                            if(height>=lowest)continue;lowest=height;entranceOwner=collection;entrance=door;
                        }
                    }
                    actor=roads.FindPaidCompanion(fixture.residentId);
                    if(entranceOwner && actor)
                    {
                        normal=DaggerfallStaticDoors.GetDoorNormal(entrance);normal.y=0;normal.Normalize();
                        doorGround=StreetPopulation.FollowExteriorDoorGround(entrance);
                        floorsReady=normal.sqrMagnitude>0.9f && StaticFloor(doorGround,null,out doorGround) &&
                            StaticFloor(doorGround+normal*0.8f,null,out playerGround) && StaticFloor(doorGround+normal*fixture.stagedDoorDistance,null,out actorGround);
                        if(floorsReady)break;
                    }
                }
                yield return null;
            }
            Need(entranceOwner && actor,"Native world transport did not load the actual entrance and existing paid companion.");
            action=store.ReadNpcAction(actionId);Need(action!=null && !NpcActionExecutor.IsTerminal(action),"Staged native transport ended its paid action.");
            actor.CaptureState();Need(InventorySignature(store.ReadNpcInventory(fixture.residentId))==inventory && ContractSignature(store,action)==contract,
                "Staged native transport changed resident native inventory, wallet or paid contract.");
            Need(floorsReady,"Actual owner-bound dungeon entrance has no measured staging floors within 30 seconds.");
            Need(BodyClear(playerGround,playerGround,actor.transform,true) && BodyClear(actorGround,actorGround,actor.transform,true) &&
                ClearSupportedRoute(new[]{actorGround,doorGround},null,actor.transform),"Actual native door staging points or approach are obstructed.");
            var controller=game.PlayerMotor.GetComponent<CharacterController>();bool controllerEnabled=controller && controller.enabled;
            if(controller)controller.enabled=false;game.PlayerMotor.transform.position=playerGround+Vector3.up;
            if(controller)controller.enabled=controllerEnabled;
            var navigation=actor.GetComponent<NativeResidentNavigation>();if(navigation)navigation.ResetPlacement();
            actor.GetComponent<CrowdMotion>().RestorePlacement(actorGround);actor.gameObject.SetActive(true);Physics.SyncTransforms();
            Need(Vector3.Distance(playerGround,actorGround)>1 && roads.FindPaidCompanion(fixture.residentId)==actor,
                "Staged existing body is not owned by the ordinary paid-road owner.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_STAGE_READY: resident="+fixture.residentId+"; action="+actionId+
                "; map="+fixture.mapId+"; nativeLocationTransport=true; sourceJourneyMeasured=false; setupBodyPlacement=true; newNpc=false; nativeInventoryPreserved=true; contractPreserved=true"+
                "; playerGround="+playerGround+"; actorGround="+actorGround);
        }
    }
}
