using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit native test of the ordinary resident and room-follower path.
    internal static class NativeInteriorResidentProbe
    {
        private const int MapId=1291010263;
        private const int InnKey=263177;

        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            var interior=game.PlayerEnterExit.Interior;
            var world=NativeNavigationWorld.Current;
            const string scene="interior-1291010263-263177";
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding || !interior ||
                !game.PlayerGPS.HasCurrentLocation || game.PlayerGPS.CurrentLocation.MapTableData.MapId!=MapId ||
                interior.EntryDoor.buildingKey!=InnKey)
                throw new InvalidOperationException("Resident probe requires the exact prebaked Daggerfall Inn.");
            float readyDeadline=Time.realtimeSinceStartup+10;
            while(Time.realtimeSinceStartup<readyDeadline &&
                (!world || !world.Ready || world.Scene!=scene || world.ModuleCount!=1))
            {
                yield return null;
                world=NativeNavigationWorld.Current;
            }
            if(!world || !world.Ready || world.Scene!=scene || world.ModuleCount!=1)
                throw new InvalidOperationException("Resident probe did not load the exact prebaked Inn NavMesh.");

            var geometry=new InteriorGeometry(interior,deferSurvey:true);
            while(geometry.AdvanceSurvey())yield return null;
            if(!geometry.EntryGround.HasValue)
                throw new InvalidOperationException("Resident probe has no native Inn entry floor.");
            var start=geometry.EntryGround.Value;
            var localStart=interior.transform.InverseTransformPoint(start);
            NavMeshHit navStart;
            if(!NavMesh.SamplePosition(start,out navStart,0.5f,NavMesh.AllAreas))
                throw new InvalidOperationException("Resident probe entrance is off the shipped Inn NavMesh.");

            List<GuardPoint3> route=null;
            Vector3 destination=Vector3.zero;
            foreach(var anchor in geometry.LocalAnchors)
            {
                var candidate=interior.transform.TransformPoint(anchor);
                if(Vector3.Distance(start,candidate)<6)continue;
                var proposed=geometry.RouteBetween(localStart,anchor);
                if(proposed.Count<2 || RouteLength(proposed)<=5)continue;
                NavMeshHit navEnd;
                if(!NavMesh.SamplePosition(candidate,out navEnd,0.5f,NavMesh.AllAreas) ||
                    Mathf.Abs(navEnd.position.y-candidate.y)>0.35f)continue;
                var nativeRoute=new NavMeshPath();
                if(!NavMesh.CalculatePath(navStart.position,navEnd.position,NavMesh.AllAreas,nativeRoute) ||
                    nativeRoute.status!=NavMeshPathStatus.PathComplete)continue;
                route=proposed;destination=candidate;break;
            }
            if(route==null)throw new InvalidOperationException("Resident probe has no surveyed five-metre Inn route on the shipped NavMesh.");

            Vector3 observer=Vector3.zero;
            bool observerFound=false;
            foreach(var anchor in geometry.LocalAnchors)
            {
                var candidate=interior.transform.TransformPoint(anchor);
                if(Vector3.Distance(candidate,start)<=2 || Vector3.Distance(candidate,destination)<=2)continue;
                bool onRoute=false;
                for(int i=1;i<route.Count;i++)
                {
                    var a=interior.transform.TransformPoint(new Vector3((float)route[i-1].X,(float)route[i-1].Y,(float)route[i-1].Z));
                    var b=interior.transform.TransformPoint(new Vector3((float)route[i].X,(float)route[i].Y,(float)route[i].Z));
                    var segment=b-a;
                    float progress=segment.sqrMagnitude<0.000001f?0:
                        Mathf.Clamp01(Vector3.Dot(candidate-a,segment)/segment.sqrMagnitude);
                    if(Vector3.Distance(candidate,a+segment*progress)<=1) {onRoute=true;break;}
                }
                if(onRoute)continue;
                observer=candidate;observerFound=true;break;
            }
            if(!observerFound)throw new InvalidOperationException("Resident probe has no surveyed observer anchor clear of both route legs.");

            var root=new GameObject("Native interior resident trial");
            var playerPosition=game.PlayerMotor.transform.position;
            try
            {
                game.PlayerMotor.transform.position=observer+Vector3.up*(playerPosition.y-start.y);
                Physics.SyncTransforms();
                root.transform.SetParent(interior.transform,true);
                root.transform.position=start;
                var body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,root.transform);
                body.transform.localPosition=Vector3.up;
                var actor=body.AddComponent<ResidentActor>();
                actor.Initialize(new Resident {Id="native-interior-trial",FullName="Inn Walker",AppearanceSeed=52,
                    Sex=ResidentSex.Male,AgeGroup=ResidentAgeGroup.Adult,IsAlive=true},1,
                    game.StreamingWorld.GetCurrentCityNavigation());
                var crowd=actor.GetComponent<CrowdMotion>();
                if(!crowd)throw new InvalidOperationException("Resident probe actor has no crowd body.");
                crowd.RestorePlacement(start);
                var follower=new InteriorRoomFollower(route,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed);
                TownSmokeHarness.Watch(actor.transform);

                float outward=0;
                yield return Walk(follower,actor,interior,geometry,destination,value=>outward=value);
                var native=actor.GetComponent<NativeResidentNavigation>();
                float nativeOut=native?native.DistanceWalked:0;
                if(nativeOut<=1 || outward<=5)
                    throw new InvalidOperationException("Resident outbound Inn route did not use native walking for real travel.");

                follower.BeginReturn();
                float returned=0;
                yield return Walk(follower,actor,interior,geometry,start,value=>returned=value);
                float nativeReturn=native?native.DistanceWalked-nativeOut:0;
                if(nativeReturn<=1 || returned<=5)
                    throw new InvalidOperationException("Resident return Inn route did not use native walking for real travel.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_RESIDENT_PASS: scene="+scene+
                    "; residentActor=true; roomFollower=true; outboundMetres="+outward+
                    "; returnMetres="+returned+"; nativeOut="+nativeOut+
                    "; nativeReturn="+nativeReturn+"; proxyOnly=false");
            }
            finally
            {
                TownSmokeHarness.Watch(null);
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                game.PlayerMotor.transform.position=playerPosition;
                Physics.SyncTransforms();
            }
        }

        private static IEnumerator Walk(InteriorRoomFollower follower,ResidentActor actor,
            DaggerfallInterior interior,InteriorGeometry geometry,Vector3 destination,Action<float> record)
        {
            float travelled=0;
            float deadline=Time.realtimeSinceStartup+90;
            var previous=actor.transform.position-Vector3.up;
            while(Time.realtimeSinceStartup<deadline)
            {
                bool arrived=follower.Advance(actor,interior,geometry,Time.unscaledTime);
                var current=actor.transform.position-Vector3.up;
                float step=Vector3.Distance(previous,current);
                if(step>0.55f)throw new InvalidOperationException("Native Inn resident jumped: "+step);
                travelled+=step;previous=current;
                if(arrived)
                {
                    if(Vector3.Distance(current,destination)>=0.15f)
                        throw new InvalidOperationException("Inn room follower claimed arrival away from the surveyed endpoint.");
                    record(travelled);
                    yield break;
                }
                yield return null;
            }
            throw new InvalidOperationException("Native Inn resident did not finish surveyed route within 90 seconds.");
        }

        private static double RouteLength(IList<GuardPoint3> route)
        {
            double distance=0;
            for(int i=1;i<route.Count;i++)
            {
                var a=route[i-1];var b=route[i];
                double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;
                distance+=Math.Sqrt(x*x+y*y+z*z);
            }
            return distance;
        }
    }
}
