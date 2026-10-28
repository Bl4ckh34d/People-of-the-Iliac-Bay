using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Observe ordinary plans in the same noon fixture as the guard test.
        // No resident, route, destination or door radius is changed here.
        internal IEnumerator VerifyOrdinaryDoorCrowds()
        {
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            if(!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.IsPlayerInside)
                throw new InvalidOperationException("Door crowd observation requires an owned outdoor fixture.");
            float deadline=Time.realtimeSinceStartup+90;
            while((!Ready || !EntranceSurveyReady) && Time.realtimeSinceStartup<deadline)yield return null;
            if(!Ready || !EntranceSurveyReady)throw new InvalidOperationException("Door crowd geometry not ready.");
            int at;
            if(int.TryParse(Environment.GetEnvironmentVariable("POTIB_DOOR_CROWD_SECONDS"),out at) && at>=0 && at<86400)
                clock.RaiseTime((float)((at-(double)(clock.ToSeconds()%86400)+86400)%86400));
            else if(Environment.GetEnvironmentVariable("POTIB_DOOR_CROWD_KEEP_CLOCK")!="1")
                clock.RaiseTime((float)((43200-(double)(clock.ToSeconds()%86400)+86400)%86400));
            int target;
            if(int.TryParse(Environment.GetEnvironmentVariable("POTIB_DOOR_CROWD_BUILDING"),out target))
            {
                TownGeometry.Entrance entrance;
                if(!geometry.Entrances.TryGetValue(target,out entrance))
                    throw new InvalidOperationException("Door crowd target is absent: "+target);
                var door=geometry.ScenePoint(new PeopleOfTheIliacBay.Core.RoutePoint(entrance.LocalDoor.x,entrance.LocalDoor.z));
                bool placed=false;
                for(int side=0;side<16;side++)
                {
                    float angle=side*Mathf.PI/8;
                    UnityEngine.AI.NavMeshHit hit;
                    if(!UnityEngine.AI.NavMesh.SamplePosition(door+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*7,
                        out hit,1,UnityEngine.AI.NavMesh.AllAreas))continue;
                    game.PlayerMotor.transform.position=hit.position+Vector3.up;placed=true;break;
                }
                if(!placed)throw new InvalidOperationException("No observer floor beside crowd target.");
                Physics.SyncTransforms();
            }
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DOOR_CROWD_START: time="+clock.ToSeconds()+"; observer="+game.PlayerMotor.transform.position);
            if(Environment.GetEnvironmentVariable("POTIB_VERIFY_INN_ENTRY")=="1")
            {
                yield return VerifyOrdinaryInnAdmission(target);
                yield break;
            }
            if(Environment.GetEnvironmentVariable("POTIB_VERIFY_INN_BEDS")=="1")
                yield return VerifyInnBeds(target);
            yield return ObserveDoorCrowds();
        }

        private IEnumerator VerifyInnBeds(int building)
        {
            DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var door in candidate.Doors)
                    if(door.doorType==DoorTypes.Building && door.buildingKey==building)
                    {owner=candidate;entry=door;break;}
            if(!owner)throw new InvalidOperationException("Inn partition test entrance absent.");
            var result=new TavernBedSurvey.Result();
            yield return TavernBedSurvey.ReadServiceSteps(owner.transform,entry,directory.LocationData.Climate.ClimateType,result,()=>directory);
            if(result.Beds==null || result.Beds.Count==0 || result.Markers==null || result.Markers.Count==0)
                throw new InvalidOperationException("Inn survey produced no native accommodation.");
            var beds=new HashSet<string>();
            foreach(var bed in result.Beds)
                if(!beds.Add(bed.Id))throw new InvalidOperationException("Duplicate native bed.");
            foreach(var marker in result.Markers)
                if(!result.Beds.Exists(b=>b.Id==marker.Value))throw new InvalidOperationException("Native rental marker has no bed.");
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            NativeTavernRentals.RegisterInventory(worldStore,directory.MapID,directory.LocationData,entry,result,
                GameManager.Instance.PlayerEntity.GetRentedRoom(directory.MapID,building),now);
            int attempted=worldStore.AdvanceInnAdmissions(directory.MapID,now,100,building);
            var visits=worldStore.TravelerVisitsFor(directory.MapID).FindAll(v=>v.TavernKey==building);
            int admitted=visits.FindAll(v=>v.Admission==PeopleOfTheIliacBay.Core.TravelerAdmission.Admitted).Count;
            int refused=visits.FindAll(v=>v.Admission==PeopleOfTheIliacBay.Core.TravelerAdmission.Refused).Count;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_PARTITION_ADMISSION_PASS: building="+building+
                "; beds="+beds.Count+"; nativeMarkers="+result.Markers.Count+"; attempted="+attempted+"; admitted="+admitted+"; refused="+refused+
                "; explicitNativePreparation=true; physicalEntryNotYetAsserted=true");
        }

        internal IEnumerator ObserveDoorCrowds()
        {
            float ready=Time.realtimeSinceStartup+90;
            while((!Ready || !EntranceSurveyReady) && Time.realtimeSinceStartup<ready)yield return null;
            if(!Ready || !EntranceSurveyReady)yield break;
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            var first=new Dictionary<string,Vector3>();
            var samples=new Dictionary<string,int>();
            var doorPositions=new Dictionary<int,Vector3>();
            foreach(var entrance in geometry.Entrances.Values)
                doorPositions[entrance.BuildingKey]=geometry.ScenePoint(new PeopleOfTheIliacBay.Core.RoutePoint(entrance.LocalDoor.x,entrance.LocalDoor.z));
            for(int pass=0;pass<18;pass++)
            {
                yield return new WaitForSecondsRealtime(10);
                double now=clock.ToSeconds();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DOOR_CROWD_SAMPLE: pass="+pass+"; actors="+actors.Count+"; travelers="+travelerPlans.Count);
                foreach(var actor in new List<ResidentActor>(actors.Values))
                {
                    if(!actor || !actor.gameObject.activeInHierarchy)continue;
                    string id=actor.Resident.Id;var plan=plans.Find(p=>p.Resident.Id==id);
                    Vector3 ground=actor.transform.position-Vector3.up;
                    TownGeometry.Entrance closest=null;float gap=3;
                    foreach(var entrance in geometry.Entrances.Values)
                    {
                        float distance=Vector3.Distance(ground,doorPositions[entrance.BuildingKey]);
                        if(distance<gap){gap=distance;closest=entrance;}
                    }
                    if(closest==null)continue;
                    Vector3 previous;float moved=first.TryGetValue(id,out previous)?Vector3.Distance(previous,ground):-1;
                    if(moved<0)first[id]=ground;
                    int count;samples.TryGetValue(id,out count);samples[id]=count+1;
                    var state=GetState(id,now);
                    TravelerPlan traveler;travelerPlans.TryGetValue(id,out traveler);
                    var native=actor.GetComponent<NativeResidentNavigation>();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DOOR_CROWD_BODY: pass="+pass+"; resident="+id+
                        "; door="+closest.BuildingKey+"; gap="+gap+"; moved="+moved+"; activity="+state.Activity+
                        "; scheduledBuilding="+state.BuildingKey+"; waiting="+state.WaitingForBuildingKey+
                        "; laneVersion="+(plan==null || plan.Routine==null?-1:plan.Routine.PersonalLaneVersion)+"; standing="+actor.IsStandingStill+
                        "; traveler="+(traveler==null?"none":traveler.Visit.Admission+":"+traveler.Journey.Evaluate(now).Phase)+
                        "; talking="+actor.IsConversing+"; failure="+(native?native.FailureReason:"no-native")+
                        "; ground="+ground+"; doorPoint="+doorPositions[closest.BuildingKey]+
                        "; destination="+(native&&native.HasDestination?native.Destination.ToString():"none")+
                        "; path="+(native?native.PathState:"none"));
                }
            }
            int repeated=0;foreach(var count in samples.Values)if(count>=3)repeated++;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DOOR_CROWD_OBSERVED: repeatedNearbyBodies="+repeated+
                "; observationOnly=true; noEntrySuccessClaim=true");
        }
    }
}
