using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        internal IEnumerator VerifyInnDiversionTransition()
        {
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((43200-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            var inns=new HashSet<int>();foreach(var inn in directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern))inns.Add(inn.buildingKey);
            DaggerfallStaticDoors owner=null;StaticDoor chosen=default(StaticDoor);float closest=float.MaxValue;
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                for(int i=0;i<(doors.Doors==null?0:doors.Doors.Length);i++)
                {
                    var door=doors.Doors[i];if(door.doorType!=DoorTypes.Building || !inns.Contains(door.buildingKey))continue;
                    float distance=(doors.GetDoorPosition(i)-game.PlayerMotor.transform.position).sqrMagnitude;
                    if(distance<closest){closest=distance;owner=doors;chosen=door;}
                }
            if(!owner)throw new InvalidOperationException("No native Inn entrance for diversion regression.");
            TownSmokeHarness.EnterInterior(owner.transform,chosen);
            float deadline=Time.realtimeSinceStartup+60;
            while((!interior || interior.EntryDoor.buildingKey!=chosen.buildingKey || shopGeometry==null || !shopGeometry.IsTavern) &&
                Time.realtimeSinceStartup<deadline)yield return null;
            if(!interior || shopGeometry==null || !shopGeometry.IsTavern)throw new InvalidOperationException("Native Inn entry failed.");
            var plan=plans.Find(p=>p.Resident.IsAlive && p.Resident.AgeGroup==ResidentAgeGroup.Adult &&
                !p.Schedule.ContainsVisitBuilding(chosen.buildingKey) && !QuestOwned(p.Resident));
            if(plan==null)throw new InvalidOperationException("No resident with an unplanned Inn target for regression.");
            bool before=enabled;enabled=false;ClearActors();
            try
            {
                var room=interior;var geometry=indoorGeometry;
                shopGeometry=new ShopVisitGeometry(room,geometry,npc=>KeeperPerson(npc)!=null);
                double now=clock.ToSeconds(),end=now+360;
                var journey=shopGeometry.Create(plan.Resident.Id,plan.Resident.AppearanceSeed,now,end);
                if(journey==null)throw new InvalidOperationException("Inn visit fixture rejected: "+shopGeometry.RejectionFor(plan.Resident.Id));
                worldStore.SaveClosedShopVisit(new ClosedShopVisit {PersonId=plan.Resident.Id,SettlementId=population.SettlementId,
                    Building=plan.Resident.HomeKey,Inn=chosen.buildingKey,StartedAt=now-60,Opening=now+60,ReturnAt=end+60,
                    EndsAt=end+600,WalkSeconds=60,Pastime=ClosedShopPastime.Inn,
                    InnRoute=new List<SavedRoutePoint>{new SavedRoutePoint{X=0},new SavedRoutePoint{X=10}}});
                var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,room.transform);
                var actor=body.AddComponent<ResidentActor>();
                actor.Initialize(plan.Resident,1,game.StreamingWorld.GetCurrentCityNavigation(),inventoryStore:worldStore);
                actors[plan.Resident.Id]=actor;
                var physical=new PhysicalShopVisit(journey){ResumeUntil=end+600};
                physicalShopVisits[plan.Resident.Id]=physical;shopJourneys[plan.Resident.Id]=journey;
                // Move the observer away from the entry route without disabling collisions.
                game.PlayerMotor.transform.position=room.transform.TransformPoint(geometry.LocalAnchors[geometry.LocalAnchors.Count-1])+Vector3.up;
                yield return null;Physics.SyncTransforms();
                actor.Place(geometry.EntryGround.Value,Vector3.forward,true,preferNative:false);
                if(!actor.gameObject.activeSelf || !actor.GetComponent<CrowdMotion>().HasPlacement)
                    throw new InvalidOperationException("Inn fixture could not place its visitor at the cleared entry.");
                bool counter=false,seated=false;float seatedAt=0;
                deadline=Time.realtimeSinceStartup+150;
                while(Time.realtimeSinceStartup<deadline)
                {
                    now=clock.ToSeconds();physical.Advance(actor,room,geometry,shopGeometry,now,
                        ()=>TryCompleteInnPurchaseAtCounter(actor,physical,clock.ToSeconds()));
                    if(physical.Snapshot.Phase==ShopVisitPhase.Haggling && worldStore.GetObservedVisit(plan.Resident.Id)!=null)
                    {counter=true;if(!actor.IsStandingStill)throw new InvalidOperationException("Arrived counter patron still animates locomotion.");}
                    if(actor.IsSeated)
                    {
                        if(!seated){seated=true;seatedAt=Time.realtimeSinceStartup;}
                        if(Time.realtimeSinceStartup-seatedAt>=5)break;
                    }
                    else if(seated)throw new InvalidOperationException("Seated diversion visitor lost the seated pose before its dwell ended.");
                    yield return null;
                }
                if(!counter || !seated)throw new InvalidOperationException("Inn diversion did not reach counter and stable seat: counter="+counter+"; seated="+seated+"; phase="+physical.Snapshot.Phase+"; reason="+physical.LastFailure);
                CaptureObservedVisits();
                var copy=new WorldStateStore(1);copy.Restore(worldStore.Snapshot());
                if(copy.GetObservedVisit(plan.Resident.Id)==null)throw new InvalidOperationException("Diversion checkpoint failed restore.");
                game.PlayerEnterExit.TransitionExterior(true);
                deadline=Time.realtimeSinceStartup+20;
                while(game.PlayerEnterExit.IsPlayerInside && Time.realtimeSinceStartup<deadline)yield return null;
                if(game.PlayerEnterExit.IsPlayerInside)throw new InvalidOperationException("Player faded Inn exit did not reach exterior.");
                yield return new WaitForSecondsRealtime(2);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_DIVERSION_PASS: unplannedInn=true; counterCheckpoint=true; standingCounter=true; stableSeatSeconds=5; snapshotRestore=true; playerFadedExit=true");
            }
            finally{enabled=before;}
        }
    }
}
