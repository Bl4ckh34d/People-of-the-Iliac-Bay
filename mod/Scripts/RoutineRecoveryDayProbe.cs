using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Extends the doorway acceptance across retained recovery legs. The probe
    // makes any off-screen time advance explicit and measures only movement
    // observed after the player is staged at each real native doorway.
    public static class RoutineRecoveryDayProbe
    {
        private const BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        private const double ObservedWindowSeconds=90d;
        private sealed class ApproachResult
        { public float Walked,MaxStep,DoorGap=float.PositiveInfinity,ObservedRealtimeSeconds; public double VirtualCatchupSeconds; }

        public static IEnumerator Observe(StreetPopulation streets,string residentId,WorldStateStore store,int sourceStopIndex)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-routine-recovery-day-probe")>=0,
                "Recovery day probe requires its explicit Player flag.");
            Require(streets && store!=null && !string.IsNullOrWhiteSpace(residentId),"Recovery day probe has no live town, resident, or store.");
            Require(Math.Abs(DaggerfallUnity.Instance.WorldTime.TimeScale-2f)<0.001f,
                "Recovery day probe requires the native 2x clock.");
            store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",PrivateInstance).GetValue(streets);
            Require(store!=null,"Recovery day probe could not reacquire the live world store after native reload.");

            var recovery=store.Snapshot().RoutineRecoveries.Find(item=>item.ResidentId==residentId);
            Require(recovery!=null && recovery.OriginOutside && recovery.Legs.Count==2 && recovery.Legs[0].OriginalStopIndex>sourceStopIndex &&
                !recovery.Legs[0].ReturnsHome && recovery.Legs[1].ReturnsHome && recovery.Legs[1].BuildingKey==recovery.HomeBuildingKey,
                "Fixture recovery plan must retain exactly one later optional stop and the home return.");
            Require(recovery.SkippedStopIndices.Contains(sourceStopIndex),"Reloaded recovery plan did not retain the expired source-stop skip.");
            var visit=recovery.Legs[0];var home=recovery.Legs[1];
            double now=Now();
            double visitObserveAt=Math.Max(visit.WalkAt+0.5d,visit.ArriveAt-ObservedWindowSeconds);
            double homeObserveAt=Math.Max(home.WalkAt+0.5d,home.ArriveAt-ObservedWindowSeconds);
            Require(visit.ArriveAt-visit.WalkAt>1d && visitObserveAt>now+1d && visitObserveAt<visit.ArriveAt &&
                visit.LeaveAt>Now()+1d && homeObserveAt>visit.ArriveAt+1d && homeObserveAt<home.ArriveAt,
                "Fixture has no bounded future outdoor windows for observing a later stop and home arrival.");

            var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",PrivateInstance).GetValue(streets);
            Require(directory && geometry!=null,"Recovery day probe has no live outdoor geometry.");
            DaggerfallStaticDoors visitOwner,homeOwner;StaticDoor visitDoor,homeDoor;int visitDoorIndex,homeDoorIndex;
            var visitEndpoint=visit.Route[visit.Route.Count-1];
            var homeEndpoint=home.Route[home.Route.Count-1];
            Require(FindDoor(directory,geometry,visit.BuildingKey,visitEndpoint,out visitOwner,out visitDoor,out visitDoorIndex),
                "Retained optional recovery stop has no existing native exterior doorway.");
            Require(FindDoor(directory,geometry,home.BuildingKey,homeEndpoint,out homeOwner,out homeDoor,out homeDoorIndex),
                "Recovery home has no existing native exterior doorway.");
            Vector3 visitGround=geometry.ScenePoint(new RoutePoint((float)visitEndpoint.X,(float)visitEndpoint.Z));
            Vector3 homeGround=geometry.ScenePoint(new RoutePoint((float)homeEndpoint.X,(float)homeEndpoint.Z));

            if(game.PlayerEnterExit.IsPlayerInside)
                game.PlayerEnterExit.TransitionExterior();
            DaggerfallUI.AddHUDText("Testing recovery: waiting for exterior route survey.",4f);
            float exitDeadline=Time.realtimeSinceStartup+90f;
            while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready || !streets.EntranceSurveyReady) &&
                Time.realtimeSinceStartup<exitDeadline) yield return null;
            Require(!game.PlayerEnterExit.IsPlayerInside && streets.Ready && streets.EntranceSurveyReady,
                "Could not reach surveyed exterior geometry from the source shop.");
            // Interior transitions change StreamingWorld.WorldCompensation. A
            // ScenePoint sampled indoors has the interior's vertical offset.
            visitGround=geometry.ScenePoint(new RoutePoint((float)visitEndpoint.X,(float)visitEndpoint.Z));

            var visitResult=new ApproachResult();
            yield return CatchUpAndObserveDoor(streets,residentId,visitObserveAt,visit.ArriveAt,visitGround,
                geometry,directory,visitOwner,visitDoorIndex,visitEndpoint,"later optional stop",visitResult);
            Require(visitResult.Walked>0.2f && visitResult.MaxStep<1.5f && visitResult.DoorGap<=0.8f,
                "Later optional stop lacked measured native body movement and physical outdoor door arrival.");
            StageObserver(streets,game,geometry,directory,visitOwner,visitDoorIndex,visitEndpoint,visitGround);
            TownSmokeHarness.EnterInterior(visitOwner.transform,visitDoor);DaggerfallUI.Instance.PopToHUD();
            float insideDeadline=Time.realtimeSinceStartup+75f;
            ResidentActor visitor=null;
            while(Time.realtimeSinceStartup<insideDeadline)
            {
                visitor=streets.FindActor(residentId);
                if(streets.Ready && game.PlayerEnterExit.Interior && visitor && visitor.gameObject.activeInHierarchy &&
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey==visit.BuildingKey)break;
                yield return null;
            }
            Require(game.PlayerEnterExit.Interior && game.PlayerEnterExit.Interior.EntryDoor.buildingKey==visit.BuildingKey &&
                visitor && visitor.gameObject.activeInHierarchy,"Resident did not appear during the retained stop's actual indoor visit.");
            float indoorStayStarted=Time.realtimeSinceStartup;
            for(int frame=0;frame<120;frame++)
            {
                Require(visitor && visitor.gameObject.activeInHierarchy &&
                    game.PlayerEnterExit.Interior && game.PlayerEnterExit.Interior.EntryDoor.buildingKey==visit.BuildingKey,
                    "Resident left the actual later-stop interior during observed dwell.");
                SleepProbe.AimCamera(visitor.transform.position+Vector3.up*0.35f);
                yield return null;
            }
            float indoorStaySeconds=Time.realtimeSinceStartup-indoorStayStarted;
            Require(indoorStaySeconds>=1f && Now()<visit.LeaveAt,
                "Later-stop indoor observation crossed its saved leave time or was too short.");

            // Catch-up from the observed visit through its (possibly long) dwell
            // is virtual. Only subsequent home-route body travel counts below.
            now=Now();
            Require(homeObserveAt>now+1d,"Home observation window elapsed during the later-stop stay.");
            game.PlayerEnterExit.TransitionExterior();
            DaggerfallUI.AddHUDText("Testing recovery: waiting for home-route survey.",4f);
            float homeExitDeadline=Time.realtimeSinceStartup+90f;
            while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready || !streets.EntranceSurveyReady) &&
                Time.realtimeSinceStartup<homeExitDeadline)yield return null;
            Require(!game.PlayerEnterExit.IsPlayerInside && streets.Ready && streets.EntranceSurveyReady,
                "Could not exit to surveyed exterior geometry from the later stop.");
            homeGround=geometry.ScenePoint(new RoutePoint((float)homeEndpoint.X,(float)homeEndpoint.Z));
            double virtualAdvanceAcrossDwell=Now()<homeObserveAt?RaiseTo(homeObserveAt):0;
            yield return null;yield return null;

            var homeResult=new ApproachResult();
            yield return CatchUpAndObserveDoor(streets,residentId,homeObserveAt,home.ArriveAt,homeGround,
                geometry,directory,homeOwner,homeDoorIndex,homeEndpoint,"home return",homeResult);
            Require(homeResult.Walked>0.2f && homeResult.MaxStep<1.5f && homeResult.DoorGap<=0.8f,
                "Home return lacked measured native body movement and physical outdoor door arrival.");
            StageObserver(streets,game,geometry,directory,homeOwner,homeDoorIndex,homeEndpoint,homeGround);
            TownSmokeHarness.EnterInterior(homeOwner.transform,homeDoor);DaggerfallUI.Instance.PopToHUD();
            float homeDeadline=Time.realtimeSinceStartup+75f;
            ResidentActor returning=null;
            while(Time.realtimeSinceStartup<homeDeadline)
            {
                returning=streets.FindActor(residentId);
                if(streets.Ready && game.PlayerEnterExit.Interior && returning && returning.gameObject.activeInHierarchy &&
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey==home.BuildingKey)break;
                yield return null;
            }
            Require(game.PlayerEnterExit.Interior && game.PlayerEnterExit.Interior.EntryDoor.buildingKey==home.BuildingKey &&
                returning && returning.gameObject.activeInHierarchy,"Resident did not return to the actual native home interior.");
            SleepProbe.AimCamera(returning.transform.position+Vector3.up*0.35f);
            for(int frame=0;frame<60;frame++)yield return null;
            Require(returning && returning.gameObject.activeInHierarchy && Now()>=home.ArriveAt,
                "Home actor did not remain present after the saved home arrival time.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_DAY_PASS: resident="+residentId+
                "; stop="+visit.BuildingKey+"; home="+home.BuildingKey+"; sourceSkipped="+recovery.SkippedStopIndices.Contains(sourceStopIndex)+
                "; clockScale=2; virtualAdvanceToStop="+visitResult.VirtualCatchupSeconds.ToString("F1")+
                "; observedStopRealtime="+visitResult.ObservedRealtimeSeconds.ToString("F1")+
                "; observedStopMetres="+visitResult.Walked.ToString("F2")+"; observedStopMaxStep="+visitResult.MaxStep.ToString("F3")+
                "; stopDoorGap="+visitResult.DoorGap.ToString("F3")+"; indoorDwellRealtime="+indoorStaySeconds.ToString("F1")+
                "; virtualAdvanceAcrossDwell="+virtualAdvanceAcrossDwell.ToString("F1")+
                "; virtualAdvanceToHome="+homeResult.VirtualCatchupSeconds.ToString("F1")+
                "; observedHomeRealtime="+homeResult.ObservedRealtimeSeconds.ToString("F1")+
                "; observedHomeMetres="+homeResult.Walked.ToString("F2")+"; observedHomeMaxStep="+homeResult.MaxStep.ToString("F3")+
                "; homeDoorGap="+homeResult.DoorGap.ToString("F3")+"; nativeStopTransition=true; nativeHomeTransition=true");
        }

        private static IEnumerator CatchUpAndObserveDoor(StreetPopulation streets,string residentId,double observeAt,double arrivalAt,
            Vector3 doorGround,TownGeometry geometry,BuildingDirectory directory,DaggerfallStaticDoors doorOwner,int doorIndex,
            SavedRoutePoint routeEndpoint,string label,ApproachResult result)
        {
            result.Walked=0;result.MaxStep=0;result.DoorGap=float.PositiveInfinity;
            Require(Now()<arrivalAt && observeAt<arrivalAt,"Recovery "+label+" arrival window has elapsed.");
            double beforeCatchup=Now();if(observeAt>beforeCatchup)result.VirtualCatchupSeconds=RaiseTo(observeAt);
            yield return null;yield return null;
            var game=GameManager.Instance;StageObserver(streets,game,geometry,directory,doorOwner,doorIndex,routeEndpoint,doorGround);
            ResidentActor actor=null;float start=Time.realtimeSinceStartup,deadline=start+100f;
            Vector3 previous=Vector3.zero;bool sampled=false,arrived=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                actor=streets.FindActor(residentId);
                if(actor && actor.gameObject.activeInHierarchy && actor.transform.parent==directory.transform)
                {
                    SleepProbe.AimCamera(actor.transform.position+Vector3.up*0.35f);
                    Vector3 current=actor.transform.position;
                    if(sampled)
                    {
                        float step=Vector3.Distance(previous,current);result.Walked+=step;result.MaxStep=Mathf.Max(result.MaxStep,step);
                    }
                    previous=current;sampled=true;
                    result.DoorGap=Mathf.Min(result.DoorGap,Vector3.Distance(current-Vector3.up,doorGround));
                    if(Now()>=arrivalAt && result.DoorGap<=0.8f){arrived=true;break;}
                }
                // UpdatePopulation can retire the exterior body on the exact
                // arrival tick, before this coroutine samples it. Require the
                // already-observed physical door approach AND native destination
                // state; a missing body alone never proves arrival.
                if(sampled && result.DoorGap<=0.8f && Now()>=arrivalAt &&
                    (!actor || !actor.gameObject.activeInHierarchy))
                {
                    var state=streets.GetState(residentId,Now());
                    if(state.BuildingKey==doorOwner.Doors[doorIndex].buildingKey &&
                        (state.Activity==Activity.Visit || state.Activity==Activity.Home))
                    {arrived=true;break;}
                }
                yield return null;
            }
            result.ObservedRealtimeSeconds=Time.realtimeSinceStartup-start;
            Require(sampled && arrived,
                "Resident was not visibly tracked through the "+label+" approach (virtual advance "+
                result.VirtualCatchupSeconds.ToString("F1")+" game seconds; observed "+result.ObservedRealtimeSeconds.ToString("F1")+
                " real seconds; sampled="+sampled+"; walked="+result.Walked.ToString("F2")+"; doorGap="+
                result.DoorGap.ToString("F2")+"; exteriorDoorY="+doorGround.y.ToString("F2")+
                "; actorY="+(actor?actor.transform.position.y.ToString("F2"):"absent")+").");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_DAY_APPROACH: resident="+residentId+"; leg="+label+
                "; virtualAdvanceSeconds="+result.VirtualCatchupSeconds.ToString("F1")+"; observedRealtimeSeconds="+result.ObservedRealtimeSeconds.ToString("F1")+
                "; observedMetres="+result.Walked.ToString("F2")+"; maxStep="+result.MaxStep.ToString("F3")+"; doorGap="+result.DoorGap.ToString("F3"));
        }

        private static bool FindDoor(BuildingDirectory directory,TownGeometry geometry,int building,SavedRoutePoint routeEndpoint,
            out DaggerfallStaticDoors owner,out StaticDoor selected,out int selectedIndex)
        {
            owner=null;selected=default(StaticDoor);selectedIndex=-1;
            Vector3 endpoint=geometry.ScenePoint(new RoutePoint((float)routeEndpoint.X,(float)routeEndpoint.Z));
            float bestDistance=float.PositiveInfinity;
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if(doors.Doors==null)continue;
                for(int i=0;i<doors.Doors.Length;i++)
                {
                    var door=doors.Doors[i];
                    if(door.doorType==DoorTypes.Building && door.buildingKey==building)
                    {
                        Vector3 position=doors.GetDoorPosition(i);
                        float dx=position.x-endpoint.x,dz=position.z-endpoint.z;
                        float distance=dx*dx+dz*dz;
                        if(distance>=bestDistance)continue;
                        bestDistance=distance;owner=doors;selected=door;selectedIndex=i;
                    }
                }
            }
            return owner;
        }

        private static void StageObserver(StreetPopulation streets,GameManager game,TownGeometry geometry,BuildingDirectory directory,
            DaggerfallStaticDoors doorOwner,int doorIndex,SavedRoutePoint routeEndpoint,Vector3 lookAt)
        {
            var currentDirectory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var currentGeometry=typeof(StreetPopulation).GetField("geometry",PrivateInstance).GetValue(streets) as TownGeometry;
            Require(currentDirectory && currentDirectory==directory && currentGeometry!=null &&
                ReferenceEquals(currentGeometry,geometry) && doorOwner && doorOwner.transform.IsChildOf(currentDirectory.transform),
                "Observer staging references stale town geometry, directory, or native door.");
            Vector3 normal=doorOwner.GetDoorNormal(doorIndex);normal.y=0;
            Require(normal.sqrMagnitude>0.01f,"Native doorway has no outward direction for observer staging.");
            normal.Normalize();
            Vector3 nativeDoor=doorOwner.GetDoorPosition(doorIndex);
            int building=doorOwner.Doors[doorIndex].buildingKey;
            int candidates=0,routedCandidates=0,outsideCandidates=0;
            foreach(float distance in new[]{2.5f,3.5f,4.5f})
            {
                candidates++;
                Vector3 candidateWorld=nativeDoor+normal*distance;
                Vector3 local=directory.transform.InverseTransformPoint(candidateWorld);
                Vector3 candidateGround=geometry.ScenePoint(new RoutePoint(local.x,local.z));
                var verified=geometry.RouteBetweenPoints(routeEndpoint,new SavedRoutePoint{X=local.x,Z=local.z});
                if(verified.Count<2)continue;
                routedCandidates++;
                if(Vector3.Dot(candidateGround-nativeDoor,normal)<=1.5f)continue;
                outsideCandidates++;
                game.PlayerMotor.transform.position=candidateGround+Vector3.up;
                Physics.SyncTransforms();
                SleepProbe.AimCamera(lookAt+Vector3.up);
                return;
            }
            float endpointGap=Vector2.Distance(new Vector2(lookAt.x,lookAt.z),new Vector2(nativeDoor.x,nativeDoor.z));
            TownGeometry.Entrance mappedEntrance;
            bool mapped=geometry.Entrances.TryGetValue(building,out mappedEntrance);
            throw new InvalidOperationException("No collision-clear exterior observer point was found along the native doorway normal. building="+
                doorOwner.Doors[doorIndex].buildingKey+"; routeEndpointGap="+endpointGap.ToString("F2")+
                "; candidates="+candidates+"; routedCandidates="+routedCandidates+"; outsideCandidates="+outsideCandidates+
                "; entranceSurveyReady="+streets.EntranceSurveyReady+"; mappedEntrance="+mapped+"; mappedEntranceCount="+
                geometry.Entrances.Count+"; doorNormal="+normal.ToString("F3")+"; doorGround="+nativeDoor.ToString("F2"));
        }

        private static double Now()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
        }

        private static double RaiseTo(double target)
        {
            double before=Now();double delta=target-before;
            Require(delta>0,"Recovery day probe cannot move the native clock backwards.");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)delta);
            return Math.Max(0,Now()-before);
        }

        private static void Require(bool value,string message)
        {if(!value)throw new InvalidOperationException(message);}
    }
}
