using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit native acceptance only. Uses the shipped module loader and the
    // ordinary resident adapter; never creates geometry or builds navigation.
    internal static class NativeDungeonModuleProbe
    {
        private const int MapId=187853213;
        private const string Scene="dungeon-187853213";
        private sealed class Anchor {public Vector3 point;public DaggerfallRDBBlock block;}
        private sealed class Route {public Anchor start,end;public NavMeshPath path;public float length;}

        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            if(Environment.GetEnvironmentVariable("POTIB_NAVMESH_MODULES_ONLY")!="1" || !game ||
                game.PlayerEntity==null || !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name))
                throw new InvalidOperationException("Dungeon module probe requires forced modular loading and an owned native fixture.");
            yield return Enter();
            var dungeon=game.PlayerEnterExit.Dungeon;
            VerifyStaticInputs(dungeon.transform);
            var names=new List<string>();foreach(var block in dungeon.Summary.LocationData.Dungeon.Blocks)names.Add(block.BlockName);
            if(!ExpectedBlockNames(names.ToArray()))throw new InvalidOperationException("Privateer's Hold does not contain the five intended native RDB modules.");
            float deadline=Time.realtimeSinceStartup+15;
            while(Time.realtimeSinceStartup<deadline && (!NativeNavigationWorld.Current ||
                !NativeNavigationWorld.Current.Ready || NativeNavigationWorld.Current.Scene!=Scene ||
                NativeNavigationWorld.Current.ModuleCount!=5))yield return null;
            var world=NativeNavigationWorld.Current;
            RequireWorld(world);
            var field=typeof(NativeNavigationWorld).GetField("manifest",BindingFlags.Instance|BindingFlags.NonPublic);
            var manifest=field==null?null:field.GetValue(world) as NativeNavigationManifest;
            List<NativeNavigationModule> selected;string refusal;
            if(!NativeDungeonNavigationModules.TrySelect(dungeon.transform,Scene,manifest,out selected,out refusal) || selected.Count!=5)
                throw new InvalidOperationException("Five shipped RDB modules/provenance were not accepted: "+refusal);
            for(int index=0;index<selected.Count;index++)
            {
                if(names[index].Equals("S0000999.RDB",StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(selected[index].collisionProviders))
                    throw new InvalidOperationException("Starting RDB module lacks its captured unreadable collision provider.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_PROBE_MODULE: block="+names[index]+"; asset="+selected[index].asset+
                    "; position="+selected[index].position+"; collisionProviders="+(selected[index].collisionProviders??"readable-source-vertices"));
            }
            Route route=null;
            yield return FindRoute(dungeon.transform,value=>route=value);
            if(route==null)throw new InvalidOperationException("No safe complete six-metre path connects two actual native RDB blocks.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_PROBE_ROUTE: from="+route.start.block.name+"; to="+route.end.block.name+
                "; start="+route.start.point+"; destination="+route.end.point+"; metres="+route.length+"; corners="+route.path.corners.Length);
            var root=new GameObject("Native dungeon module resident trial");
            try
            {
                root.transform.SetParent(dungeon.transform,true);root.transform.position=route.start.point;
                var body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,root.transform);
                body.transform.localPosition=Vector3.up;
                var actor=body.AddComponent<ResidentActor>();
                actor.Initialize(new Resident {Id="native-dungeon-module-trial",FullName="Dungeon Walker",AppearanceSeed=52,
                    Sex=ResidentSex.Male,AgeGroup=ResidentAgeGroup.Adult,IsAlive=true},1,game.StreamingWorld.GetCurrentCityNavigation());
                var crowd=actor.GetComponent<CrowdMotion>();
                if(!crowd)throw new InvalidOperationException("Dungeon module probe lacks the ordinary resident crowd body.");
                crowd.RestorePlacement(route.start.point);Physics.SyncTransforms();TownSmokeHarness.Watch(actor.transform);
                float outward=0,returned=0;
                yield return Walk(actor,dungeon.transform,route.end,world,value=>outward=value);
                var native=actor.GetComponent<NativeResidentNavigation>();
                float nativeOut=native?native.DistanceWalked:0;
                if(outward<=5 || nativeOut<=5)throw new InvalidOperationException("Cross-block outbound travel did not use ordinary native movement.");
                yield return Walk(actor,dungeon.transform,route.start,world,value=>returned=value);
                float nativeBack=native?native.DistanceWalked-nativeOut:0;
                if(returned<=5 || nativeBack<=5)throw new InvalidOperationException("Cross-block return travel did not use ordinary native movement.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_MODULE_PROBE_PASS: scene="+Scene+
                    "; modules=5; forcedModules=true; provenanceAccepted=true; crossBlockPath=true; residentActor=true; proxyOnly=false"+
                    "; outboundMetres="+outward+"; returnMetres="+returned+"; nativeOut="+nativeOut+"; nativeReturn="+nativeBack+
                    "; runtimeBake=false; staticFloorSupport=true");
            }
            finally
            {TownSmokeHarness.Watch(null);root.SetActive(false);UnityEngine.Object.Destroy(root);}
        }

        internal static bool ExpectedBlockNames(string[] names)
        {
            if(names==null || names.Length!=5)return false;
            var expected=new HashSet<string>(new[]{"S0000999.RDB","B0000009.RDB","B0000006.RDB","B0000003.RDB","B0000012.RDB"},StringComparer.OrdinalIgnoreCase);
            foreach(string name in names)if(name==null || !expected.Remove(name))return false;
            return expected.Count==0;
        }

        internal static void VerifyStaticInputs(Transform dungeon)
        {
            int triggers=0,sourceCount=0;
            foreach(var collider in dungeon.GetComponentsInChildren<Collider>())
                if(collider.enabled && collider.isTrigger)triggers++;
            foreach(var block in dungeon.GetComponentsInChildren<DaggerfallRDBBlock>())
                foreach(var source in NativeNavigationGeometry.Collect(block.transform))
                {
                    sourceCount++;
                    var collider=source.component as Collider;
                    if(collider && collider.isTrigger)
                        throw new InvalidOperationException("Non-physical interaction trigger entered static RDB navigation input: "+collider.name);
                }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RDB_STATIC_INPUTS: sources="+sourceCount+"; observedTriggers="+triggers+"; capturedTriggers=0");
        }

        private static void RequireWorld(NativeNavigationWorld world)
        {
            if(!world || world!=NativeNavigationWorld.Current || !world.Ready || world.Scene!=Scene || world.ModuleCount!=5 ||
                Environment.GetEnvironmentVariable("POTIB_NAVMESH_MODULES_ONLY")!="1")
                throw new InvalidOperationException("Forced five-module dungeon navigation is unavailable or changed during the probe.");
        }

        internal static IEnumerator Enter()
        {
            var game=GameManager.Instance;
            if(game.PlayerEnterExit.IsPlayerInsideDungeon && game.PlayerEnterExit.Dungeon && game.PlayerEnterExit.Dungeon.Summary.ID==MapId)yield break;
            if(game.PlayerEnterExit.IsPlayerInside)throw new InvalidOperationException("Dungeon module probe must start outside or in Privateer's Hold.");
            DFLocation location;
            if(!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall","Privateer's Hold",out location) ||
                !location.Loaded || !location.HasDungeon || location.MapTableData.MapId!=MapId)
                throw new InvalidOperationException("Privateer's Hold native location is unavailable.");
            var pixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
            game.StreamingWorld.TeleportToCoordinates(pixel.X,pixel.Y,StreamingWorld.RepositionMethods.RandomStartMarker);
            yield return null;
            DaggerfallStaticDoors owner=null;StaticDoor entrance=default(StaticDoor);
            float deadline=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<deadline && !owner)
            {
                if(game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer && game.PlayerGPS.HasCurrentLocation &&
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId==MapId)
                {
                    var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                    if(directory && directory.MapID==MapId)
                        foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                        {
                            foreach(var door in candidate.Doors??new StaticDoor[0])
                                if(door.doorType==DoorTypes.DungeonEntrance){owner=candidate;entrance=door;break;}
                            if(owner)break;
                        }
                }
                if(!owner)yield return null;
            }
            if(!owner)throw new InvalidOperationException("Privateer's Hold native entrance did not load.");
            game.PlayerEnterExit.TransitionDungeonInterior(owner.transform,entrance,game.PlayerGPS.CurrentLocation);
            deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;var dungeon=game.PlayerEnterExit.Dungeon;
                if(!game.PlayerEnterExit.IsPlayerInsideDungeon || !dungeon || dungeon.Summary.ID!=MapId ||
                    !dungeon.StartMarker || dungeon.GetComponentsInChildren<Collider>().Length==0)continue;
                DaggerfallUI.Instance.PopToHUD();yield return new WaitForSecondsRealtime(1);yield break;
            }
            throw new InvalidOperationException("Privateer's Hold native transition did not complete.");
        }

        private static IEnumerator FindRoute(Transform dungeon,Action<Route> record)
        {
            var mesh=NavMesh.CalculateTriangulation();var anchors=new List<Anchor>();
            var counts=new Dictionary<DaggerfallRDBBlock,int>();var seen=new HashSet<string>();
            for(int index=0;index+2<mesh.indices.Length;index+=3)
            {
                if(index%384==0)yield return null;
                var point=(mesh.vertices[mesh.indices[index]]+mesh.vertices[mesh.indices[index+1]]+mesh.vertices[mesh.indices[index+2]])/3;
                NavMeshHit sampled;
                if(!NavMesh.SamplePosition(point,out sampled,0.25f,NavMesh.AllAreas))continue;
                var block=FloorBlock(dungeon,sampled.position);if(!block)continue;
                int count;counts.TryGetValue(block,out count);if(count>=128)continue;
                string key=block.GetInstanceID()+":"+Mathf.RoundToInt(point.x/1.5f)+":"+Mathf.RoundToInt(point.z/1.5f)+":"+Mathf.RoundToInt(point.y/0.5f);
                if(!seen.Add(key) || !BodyClear(sampled.position,sampled.position,null))continue;
                counts[block]=count+1;anchors.Add(new Anchor {point=sampled.position,block=block});
            }
            var pairs=new List<Route>();
            for(int a=0;a<anchors.Count;a++)for(int b=a+1;b<anchors.Count;b++)
            {
                if(anchors[a].block==anchors[b].block || Mathf.Abs(anchors[a].point.y-anchors[b].point.y)>0.5f)continue;
                float distance=Vector3.Distance(anchors[a].point,anchors[b].point);
                if(distance>6 && distance<25)pairs.Add(new Route {start=anchors[a],end=anchors[b],length=distance});
            }
            pairs.Sort((a,b)=>a.length.CompareTo(b.length));int attempts=0;
            foreach(var pair in pairs)
            {
                if(++attempts>4096)break;if(attempts%32==0)yield return null;
                var path=new NavMeshPath();
                if(!NavMesh.CalculatePath(pair.start.point,pair.end.point,NavMesh.AllAreas,path) ||
                    path.status!=NavMeshPathStatus.PathComplete || path.corners.Length<2)continue;
                float length=0;bool clear=true;
                for(int corner=1;corner<path.corners.Length;corner++)
                {
                    var from=path.corners[corner-1];var to=path.corners[corner];float distance=Vector3.Distance(from,to);length+=distance;
                    if(!BodyClear(from,to,null)){clear=false;break;}
                    int steps=Mathf.Max(1,Mathf.CeilToInt(distance/0.4f));
                    for(int step=0;step<=steps;step++)
                    {
                        var point=Vector3.Lerp(from,to,(float)step/steps);
                        if(!FloorBlock(dungeon,point) || Vector3.Distance(point,GameManager.Instance.PlayerMotor.transform.position)<2.5f)
                        {clear=false;break;}
                    }
                    if(!clear)break;
                }
                if(!clear || length<=6 || length>35)continue;
                pair.path=path;pair.length=length;record(pair);yield break;
            }
        }

        private static IEnumerator Walk(ResidentActor actor,Transform dungeon,Anchor destination,NativeNavigationWorld world,Action<float> record)
        {
            var previous=actor.transform.position-Vector3.up;float travelled=0,deadline=Time.realtimeSinceStartup+120;
            var visited=new HashSet<DaggerfallRDBBlock>();var first=FloorBlock(dungeon,previous);if(first)visited.Add(first);
            while(Time.realtimeSinceStartup<deadline)
            {
                RequireWorld(world);
                actor.Place(previous,destination.point-previous,navigationDestination:destination.point);
                var navigation=actor.GetComponent<NativeResidentNavigation>();
                if(!navigation || !navigation.Active || !navigation.HasDestination)
                    throw new InvalidOperationException("Dungeon resident lost the ordinary native adapter: "+(navigation?navigation.FailureReason:"missing"));
                var current=actor.transform.position-Vector3.up;float step=Vector3.Distance(previous,current);
                var block=FloorBlock(dungeon,current);
                if(step>0.55f || !block || !BodyClear(previous,current,actor.transform))
                    throw new InvalidOperationException("Dungeon resident jumped, lost static floor support, or crossed native collision: "+current+"; step="+step);
                visited.Add(block);travelled+=step;previous=current;
                if(Vector3.Distance(current,destination.point)<0.18f)
                {
                    if(block!=destination.block || visited.Count<2)throw new InvalidOperationException("Dungeon arrival did not physically cross an RDB block boundary.");
                    record(travelled);yield break;
                }
                yield return null;
            }
            var native=actor.GetComponent<NativeResidentNavigation>();
            throw new InvalidOperationException("Ordinary dungeon resident timed out: "+(native?native.PathState+"; failure="+native.FailureReason:"missing adapter"));
        }

        private static DaggerfallRDBBlock FloorBlock(Transform dungeon,Vector3 point)
        {
            float nearest=float.MaxValue;DaggerfallRDBBlock floor=null;
            foreach(var hit in Physics.RaycastAll(point+Vector3.up*0.55f,Vector3.down,0.85f,~0,QueryTriggerInteraction.Ignore))
            {
                if(!hit.collider || !hit.collider.transform.IsChildOf(dungeon) || NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider) ||
                    hit.normal.y<0.7f || Mathf.Abs(hit.point.y-point.y)>0.3f || hit.distance>=nearest)continue;
                var block=hit.collider.GetComponentInParent<DaggerfallRDBBlock>();if(!block || block.transform.parent!=dungeon)continue;
                floor=block;nearest=hit.distance;
            }
            return floor;
        }

        private static bool BodyClear(Vector3 from,Vector3 to,Transform own)
        {
            foreach(var collider in Physics.OverlapCapsule(to+Vector3.up*0.76f,to+Vector3.up*1.65f,0.25f,~0,QueryTriggerInteraction.Ignore))
                if(collider && (!own || !collider.transform.IsChildOf(own)))return false;
            var delta=to-from;if(delta.sqrMagnitude<0.000001f)return true;
            foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*0.76f,from+Vector3.up*1.65f,0.25f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider && (!own || !hit.collider.transform.IsChildOf(own)))return false;
            return true;
        }
    }
}
