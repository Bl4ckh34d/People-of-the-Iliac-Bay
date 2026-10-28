using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Read the same live geometry used by ordinary demand. No traveler or route injection.
    internal static class NativeTravelerRouteProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;
            if(!host || !streets || !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Traveler route probe requires an owned outdoor fixture.");
            float deadline=Time.realtimeSinceStartup+90;
            var guards=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            TownGeometry geometry=null,guardGeometry=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                geometry=Field<TownGeometry>(streets,"geometry");
                guardGeometry=guards?Field<TownGeometry>(guards,"geometry"):null;
                if(streets.EntranceSurveyReady && geometry!=null && guardGeometry!=null &&
                    Field<List<SavedRoutePoint[]>>(guardGeometry,"gateCrossings").Count>0)break;
                yield return null;
            }
            if(!streets.EntranceSurveyReady || geometry==null || guardGeometry==null)
                throw new InvalidOperationException("Ordinary street/guard geometry did not become ready.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory || directory.MapID!=1291010263 || !geometry.UsesNativeNavigation)
                throw new InvalidOperationException("Route regression requires Daggerfall's loaded production NavMesh.");
            OverlandRoadNetwork roads;
            if(!host.RoadNetwork.TryGet(out roads))throw new InvalidOperationException("Installed Basic Roads API is unavailable.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ROUTE_PROVIDER: BasicRoads=true; productionNavMesh=true");
            yield return TravelerIdentityAdapter.PrepareOrigins(directory.MapID,()=>directory);
            TownGeometry.Entrance target;
            if(!geometry.Entrances.TryGetValue(197386,out target))throw new InvalidOperationException("Target Inn doorway absent.");
            int gateIndex=0;
            foreach(var crossing in Field<List<SavedRoutePoint[]>>(guardGeometry,"gateCrossings"))
            {
                var outer=crossing[1];var destination=new SavedRoutePoint {X=target.LocalDoor.x,Z=target.LocalDoor.z};
                var a=geometry.ScenePoint(new RoutePoint(outer.X,outer.Z));
                var b=geometry.ScenePoint(new RoutePoint(destination.X,destination.Z));
                NavMeshHit start,end;var path=new NavMeshPath();
                bool startFound=NavMesh.SamplePosition(a,out start,0.4f,NavMesh.AllAreas);
                bool endFound=NavMesh.SamplePosition(b,out end,0.4f,NavMesh.AllAreas);
                bool calculated=startFound && endFound && NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path);
                var guarded=guardGeometry.GateRouteBetweenPoints(outer,destination);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_GATE_COMPARE: gate="+gateIndex+++
                    "; start="+a+"; end="+b+"; startFound="+startFound+"; endFound="+endFound+
                    "; calculated="+calculated+"; status="+path.status+"; guardRoutePoints="+guarded.Count);
                var inner=geometry.ScenePoint(new RoutePoint(crossing[0].X,crossing[0].Z));
                ReportPath("outer-inner",a,inner);
                ReportPath("inner-inn",inner,b);
                ReportPath("outer-inn",a,b);
                var delta=inner-a;
                foreach(float height in new[]{0.4f,1f})
                    foreach(var hit in Physics.SphereCastAll(a+Vector3.up*height,0.2f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_GATE_HIT: height="+height+"; object="+hit.collider.name+
                            "; point="+hit.point+"; distance="+hit.distance);
                yield return null;
            }
            foreach(var site in guardGeometry.SurveyGateGuardPosts())
            {
                var outside=geometry.ScenePoint(new RoutePoint(site.OutsideLeft.X,site.OutsideLeft.Z));
                var inside=geometry.ScenePoint(new RoutePoint(site.InsideLeft.X,site.InsideLeft.Z));
                ReportPath("guard-posts-left-"+site.GateIndex,outside,inside);
                ReportPath("guard-post-inn-"+site.GateIndex,inside,geometry.ScenePoint(new RoutePoint(target.LocalDoor.x,target.LocalDoor.z)));
                outside=geometry.ScenePoint(new RoutePoint(site.OutsideRight.X,site.OutsideRight.Z));
                inside=geometry.ScenePoint(new RoutePoint(site.InsideRight.X,site.InsideRight.Z));
                ReportPath("guard-posts-right-"+site.GateIndex,outside,inside);
                yield return null;
            }
            var firstCrossing=Field<List<SavedRoutePoint[]>>(guardGeometry,"gateCrossings")[0];
            var innerGround=geometry.ScenePoint(new RoutePoint(firstCrossing[0].X,firstCrossing[0].Z));
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                for(int door=0;owner.Doors!=null && door<owner.Doors.Length;door++)
                    if(owner.Doors[door].buildingKey==197386 && owner.Doors[door].doorType==DoorTypes.Building)
                    {
                        var normal=owner.GetDoorNormal(door);normal.y=0;
                        var real=owner.GetDoorPosition(door)+normal.normalized*0.6f;
                        var local=directory.transform.InverseTransformPoint(real);
                        ReportPath("actual-inn-door-"+door,innerGround,geometry.ScenePoint(new RoutePoint(local.x,local.z)));
                    }
            int connected=0,total=0;
            foreach(var inn in directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern))
            {
                var routes=geometry.SurveyTravelerRoutes(inn.buildingKey);total++;
                if(routes.Count>0)connected++;
                if(routes.Count>0)
                {
                    var proposal=TravelerIdentityAdapter.Create(42,directory.MapID,inn.buildingKey,
                        (int)(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()/86400),0,routes[0]);
                    if(!TravelerIdentityAdapter.HasVerifiedRoad(proposal.Person.HomeSettlement))
                        throw new InvalidOperationException("Traveler origin lacks a verified Basic Roads route.");
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ROUTE_CHECK: inn="+inn.buildingKey+"; routes="+routes.Count);
                yield return null;
            }
            if(total==0 || connected!=total)
                throw new InvalidOperationException("Actual traveler routes connect "+connected+"/"+total+" Inns while gates are open.");
            foreach(var crossing in Field<List<SavedRoutePoint[]>>(guardGeometry,"gateCrossings"))
            {
                var from=geometry.ScenePoint(new RoutePoint(crossing[1].X,crossing[1].Z));
                var to=geometry.ScenePoint(new RoutePoint(crossing[0].X,crossing[0].Z));
                var body=new GameObject("Owned gate traversal probe");body.transform.position=from+Vector3.up;
                var mover=body.AddComponent<NativeResidentNavigation>();
                try
                {
                    float until=Time.realtimeSinceStartup+20;float maximumStep=0;
                    while(Vector3.Distance(body.transform.position-Vector3.up,to)>0.4f && Time.realtimeSinceStartup<until)
                    {
                        var before=body.transform.position;
                        if(!mover.TryMove(from,to,3,null))throw new InvalidOperationException("Native gate mover failed: "+mover.FailureReason);
                        maximumStep=Mathf.Max(maximumStep,Vector3.Distance(before,body.transform.position));
                        yield return null;
                    }
                    if(Vector3.Distance(body.transform.position-Vector3.up,to)>0.4f || maximumStep>0.501f)
                        throw new InvalidOperationException("Native gate traversal failed or snapped: "+mover.PathState+"; maxStep="+maximumStep);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_GATE_WALK_PASS: distance="+mover.DistanceWalked+"; maxStep="+maximumStep);
                }
                finally {UnityEngine.Object.Destroy(body);}
            }
            var nativeGates=directory.GetComponentsInChildren<DaggerfallCityGate>();
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            try
            {
                clock.RaiseTime((float)((18*3600+60-clock.ToSeconds()%86400+86400)%86400));
                yield return null;yield return null;
                if(geometry.SurveyTravelerRoutes(197386).Count!=0)
                    throw new InvalidOperationException("Traveler survey admits a physically closed native gate.");
                foreach(var crossing in Field<List<SavedRoutePoint[]>>(guardGeometry,"gateCrossings"))
                {
                    var route=guardGeometry.GateRouteBetweenPoints(crossing[1],crossing[0]);
                    if(route.Count>0)throw new InvalidOperationException("Guard route admits a physically closed native gate.");
                }
                // Model an open-state bake without removing any native geometry:
                // explicit test-only connectivity bypasses the baked closed leaves.
                // Production planning must still reject the actual closed gate.
                var bypasses=new List<NavMeshLinkInstance>();
                try
                {
                    foreach(var crossing in Field<List<SavedRoutePoint[]>>(guardGeometry,"gateCrossings"))
                    {
                        NavMeshHit from,to;
                        if(!NavMesh.SamplePosition(geometry.ScenePoint(new RoutePoint(crossing[1].X,crossing[1].Z)),out from,0.4f,NavMesh.AllAreas) ||
                            !NavMesh.SamplePosition(geometry.ScenePoint(new RoutePoint(crossing[0].X,crossing[0].Z)),out to,0.4f,NavMesh.AllAreas))
                            throw new InvalidOperationException("Closed gate topology fixture endpoints are missing.");
                        bypasses.Add(NavMesh.AddLink(new NavMeshLinkData {startPosition=from.position,endPosition=to.position,
                            width=0,bidirectional=true,agentTypeID=NativeNavigationGeometry.Settings().agentTypeID,costModifier=-1}));
                        var raw=new NavMeshPath();
                        if(!NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,raw) || raw.status!=NavMeshPathStatus.PathComplete)
                            throw new InvalidOperationException("Closed gate topology fixture did not create a complete native path.");
                        if(guardGeometry.GateRouteBetweenPoints(crossing[1],crossing[0]).Count!=0)
                            throw new InvalidOperationException("Guard planner trusts baked connectivity through a closed native gate.");
                    }
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_CLOSED_TOPOLOGY_PASS: rawNativePaths=4; admittedGuardRoutes=0; testOnlyLinks=true");
                }
                finally {foreach(var bypass in bypasses)if(bypass.valid)bypass.Remove();}
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_CLOSED_GATES_PASS: travelerRoutes=0; guardRoutes=0");
            }
            finally {clock.RaiseTime((float)((6*3600-clock.ToSeconds()%86400+86400)%86400));}
            yield return null;yield return null;
            if(geometry.SurveyTravelerRoutes(197386).Count==0)
                throw new InvalidOperationException("Reopened native gate does not restore traveler routes.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ROUTE_PASS: connected="+connected+"/"+total+"; ordinaryGeometry=true; injectedTravelers=false");
        }

        private static T Field<T>(object instance,string name)
        {return (T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);}

        private static void ReportPath(string segment,Vector3 from,Vector3 to)
        {
            NavMeshHit a,b;var path=new NavMeshPath();
            bool start=NavMesh.SamplePosition(from,out a,0.4f,NavMesh.AllAreas);
            bool end=NavMesh.SamplePosition(to,out b,0.4f,NavMesh.AllAreas);
            bool found=start && end && NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_SEGMENT: segment="+segment+"; from="+from+"; to="+to+
                "; samples="+start+"/"+end+"; path="+found+"; status="+path.status+
                "; first="+(path.corners.Length>0?path.corners[0].ToString():"none")+
                "; last="+(path.corners.Length>0?path.corners[path.corners.Length-1].ToString():"none"));
        }
    }
}
