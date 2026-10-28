using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class GoalSequenceProbe
    {
        private static DFLocation SelectNativeContinuationDestination(double consentEnds)
        {
            var game=GameManager.Instance;var reader=DaggerfallUnity.Instance.ContentReader;
            var region=reader.MapFileReader.GetRegion(game.PlayerGPS.CurrentRegionIndex);
            var items=game.PlayerEntity.Items;
            bool horse=items.Contains(ItemGroups.Transportation,(int)Transportation.Horse);
            bool cart=items.Contains(ItemGroups.Transportation,(int)Transportation.Small_cart);
            int best=-1,bestMinutes=int.MaxValue;
            for(int index=0;index<region.LocationCount;index++)
            {
                var entry=region.MapTable[index];
                if(entry.MapId==game.PlayerGPS.CurrentLocation.MapTableData.MapId ||
                    entry.LocationType!=DFRegion.LocationTypes.TownCity && entry.LocationType!=DFRegion.LocationTypes.TownHamlet &&
                    entry.LocationType!=DFRegion.LocationTypes.TownVillage)continue;
                var target=MapsFile.LongitudeLatitudeToMapPixel(entry.Longitude,entry.Latitude);
                var calculator=new TravelTimeCalculator();
                int minutes=game.GuildManager.FastTravel(calculator.CalculateTravelTime(target,false,true,false,horse,cart));
                if(minutes<=0 || minutes>=bestMinutes || FastTravelNow()+minutes*60+600>=consentEnds)continue;
                best=index;bestMinutes=minutes;
            }
            RequireFastTravel(best>=0,"No actual nearby settlement fits the saved contract with ten game minutes remaining.");
            DFLocation destination;
            RequireFastTravel(reader.GetLocation(region.Name,region.MapNames[best],out destination) && destination.Loaded &&
                destination.MapTableData.MapId==region.MapTable[best].MapId,"Selected native settlement cannot be read unambiguously.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_CONTINUATION_DESTINATION: name="+destination.Name+
                "; map="+destination.MapTableData.MapId+"; nativeMinutes="+bestMinutes+
                "; originalConsentEnd="+consentEnds+"; newConsent=false; observerTransport=false");
            return destination;
        }

        private static IEnumerator VerifyContinuedFastTravelFollow(WorldStateStore store,ResidentActor actor,string actionId,
            int body,double finish,double consentDeadline,string inventory)
        {
            var game=GameManager.Instance;var controller=game.PlayerMotor.GetComponent<CharacterController>();
            var readGround=Method(typeof(NativeDungeonFollowProbe),"PlayerGround",StaticMembers,0);
            var readFloor=Method(typeof(NativeDungeonFollowProbe),"StaticFloor",StaticMembers,3);
            var routeClear=Method(typeof(NativeDungeonFollowProbe),"ClearSupportedRoute",StaticMembers,3);
            var stepObserver=Method(typeof(NativeDungeonFollowProbe),"MoveObserverToward",StaticMembers,3);
            var actorClear=Method(typeof(NativeDungeonFollowProbe),"BodyClear",StaticMembers,5);
            var start=(Vector3)readGround.Invoke(null,null);var actorStart=actor.transform.position-Vector3.up;
            Vector3 endpoint=Vector3.zero;bool found=false;
            for(int index=0;index<16;index++)
            {
                float angle=index*Mathf.PI/8;
                object[] floor={start+new Vector3(Mathf.Sin(angle),0,Mathf.Cos(angle))*8,null,Vector3.zero};
                if(!(bool)readFloor.Invoke(null,floor))continue;
                var candidate=(Vector3)floor[2];
                if(Vector3.Distance(candidate,actorStart)<7 ||
                    !(bool)routeClear.Invoke(null,new object[]{new[]{start,candidate},null,controller.transform}))continue;
                endpoint=candidate;found=true;break;
            }
            RequireFastTravel(found,"Actual fast-travel destination has no supported clear eight-metre observer route.");
            bool motorEnabled=game.PlayerMotor.enabled;game.PlayerMotor.enabled=false;
            float walked=0,maximumStep=0;var previous=actorStart;
            try
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                float timeout=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<timeout)
                {
                    var job=store.ReadNpcAction(actionId);
                    RequireFastTravel(actor && actor.GetInstanceID()==body && actor.gameObject.activeInHierarchy &&
                        job!=null && !NpcActionExecutor.IsTerminal(job) && job.FinishAt==finish && job.DeadlineAt==consentDeadline &&
                        NpcCompanionFastTravel.CanFollow(store,job,FastTravelNow()),
                        "Continuing fast-travel companion lost its original body or accepted Follow action.");
                    var actual=actor.transform.position-Vector3.up;float step=Vector3.Distance(previous,actual);
                    RequireFastTravel(step<=0.55f,"Continuing companion exceeded bounded physical movement: "+step);
                    object[] floor={actual,null,Vector3.zero};
                    RequireFastTravel((bool)readFloor.Invoke(null,floor) &&
                        (bool)actorClear.Invoke(null,new object[]{previous,actual,actor.transform,false,CrowdMotion.BodyRadius}),
                        "Continuing companion lost native floor support or crossed a collider.");
                    walked+=step;maximumStep=Mathf.Max(maximumStep,step);previous=actual;
                    var observer=(Vector3)readGround.Invoke(null,null);
                    bool observerArrived=Vector3.Distance(observer,endpoint)<=0.4f;
                    if(observerArrived && Vector3.Distance(actual,observer)<=2.4f && Vector3.Distance(actual,actorStart)>=4)break;
                    if(!observerArrived)stepObserver.Invoke(null,new object[]{endpoint,controller,false});
                    yield return null;
                }
                var player=(Vector3)readGround.Invoke(null,null);
                var final=actor.transform.position-Vector3.up;
                RequireFastTravel(Vector3.Distance(player,endpoint)<=0.4f && Vector3.Distance(final,player)<=2.4f &&
                    Vector3.Distance(final,actorStart)>=4 && walked>=4,
                    "Native companion did not physically continue Follow after fast travel; walked="+walked+
                    "; playerRemaining="+Vector3.Distance(player,endpoint)+"; followerDistance="+Vector3.Distance(final,player));
                actor.CaptureState();RequireFastTravel(FastTravelInventory(store,actor.Resident.Id)==inventory,
                    "Continued Follow changed the preserved native inventory or wallet.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_CONTINUED_FOLLOW_PASS: person="+actor.Resident.Id+
                    "; sameBody=true; originalAction=true; physicalMetres="+walked+"; maximumStep="+maximumStep+
                    "; observerMetres="+Vector3.Distance(player,start)+"; colliderAndFloorChecks=true; contractExtended=false");
            }
            finally{game.PlayerMotor.enabled=motorEnabled;}
        }

        private static IEnumerator VerifyFastTravelCheckpointReload(PeopleOfTheIliacBayMod host,StreetPopulation streets,string actionId)
        {
            const string title="People - Ripwych fast travel follow";
            var game=GameManager.Instance;var roads=host.GetComponent<RoadTravelActors>();
            var store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            var job=store.ReadNpcAction(actionId);var actor=roads.FindPaidCompanion(job.ResidentId);
            RequireFastTravel(actor && job.FastTravelFollowOwner && !NpcActionExecutor.IsTerminal(job),"Checkpoint needs its actual active fast-travel follower.");
            // Let the existing position writer capture the stopped body before
            // invoking the native save. Do not inject a test position or action.
            float deadline=Time.realtimeSinceStartup+2;
            while(Time.realtimeSinceStartup<deadline)yield return null;
            yield return SaveFastTravelCheckpoint(game,title);
            job=store.ReadNpcAction(actionId);
            var expected=NpcActionExecutor.CopyTarget(job.LastPosition);
            string residentId=job.ResidentId,inventory=FastTravelInventory(store,residentId),relation=FastTravelRelationship(store,residentId);
            double finish=job.FinishAt,consentDeadline=job.DeadlineAt;
            yield return ConversationMoodProbe.LoadSave(host,streets,game.PlayerEntity.Name,title,true);
            store=(WorldStateStore)Field(host.GetType(),"store").GetValue(host);
            deadline=Time.realtimeSinceStartup+30;actor=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                actor=roads.FindPaidCompanion(residentId);
                if(actor && actor.gameObject.activeInHierarchy && !game.StreamingWorld.IsInit && game.StreamingWorld.IsReady &&
                    !game.StreamingWorld.IsRepositioningPlayer)break;
                yield return null;
            }
            RequireFastTravel(actor && actor.gameObject.activeInHierarchy,"Saved fast-travel follower did not restore a native body.");
            job=store.ReadNpcAction(actionId);var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            RequireFastTravel(directory && directory.MapID==expected.SettlementId && job!=null && job.ResidentId==residentId &&
                job.FastTravelFollowOwner && !NpcActionExecutor.IsTerminal(job) && Math.Abs(job.FinishAt-finish)<0.001 &&
                Math.Abs(job.DeadlineAt-consentDeadline)<0.001,"Native reload lost destination, action or original consent.");
            var savedGround=directory.transform.TransformPoint(new Vector3((float)expected.X,(float)expected.Y,(float)expected.Z));
            float drift=Vector3.Distance(actor.transform.position-Vector3.up,savedGround);
            RequireFastTravel(drift<=0.55f,"Native fast-travel reload moved the saved companion relative to the player; drift="+drift+
                "; saved="+savedGround+"; actual="+(actor.transform.position-Vector3.up));
            actor.CaptureState();RequireFastTravel(FastTravelInventory(store,residentId)==inventory && FastTravelRelationship(store,residentId)==relation,
                "Native fast-travel reload changed inventory, wallet or relationship.");
            int bodies=0;foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(candidate && candidate.gameObject.activeInHierarchy && candidate.Resident!=null && candidate.Resident.Id==residentId)bodies++;
            RequireFastTravel(bodies==1,"Native fast-travel reload duplicated the companion.");
            yield return VerifyContinuedFastTravelFollow(store,actor,actionId,actor.GetInstanceID(),finish,consentDeadline,inventory);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_RELOAD_PASS: person="+residentId+"; actualDiskSaveLoad=true; sameInventoryAndRelationship=true"+
                "; originalActionAndConsent=true; uniqueBody=true; savedPositionDrift="+drift+"; continuedPhysicalFollow=true; checkpoint="+title);
        }

        private static IEnumerator SaveFastTravelCheckpoint(GameManager game,string title)
        {
            bool saved=false;SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnSave+=onSave;
            try
            {
                // Reuse the owned named fixture on retries rather than creating
                // a series of equivalent full saves.
                game.SaveLoadManager.Save(game.PlayerEntity.Name,title);
                float deadline=Time.realtimeSinceStartup+30;
                while(!saved && Time.realtimeSinceStartup<deadline)yield return null;
                RequireFastTravel(saved,"Native fast-travel checkpoint did not save: "+title);
            }
            finally{SaveLoadManager.OnSave-=onSave;}
        }
    }
}
