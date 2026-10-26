using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit test only. Prove native scene geometry and crowd movement before
    // replacing the production movement owner. No saved routes are modified.
    internal static class NativeNavigationTrial
    {
        public static IEnumerator Run()
        {
            string interiorTarget=Environment.GetEnvironmentVariable("POTIB_NAVMESH_INTERIOR");
            if(!string.IsNullOrEmpty(interiorTarget))
                yield return NativeTravelerFixtureWarmup.EnterAfterOrdinaryWarmup(int.Parse(interiorTarget),EnterInterior);
            string roadmapProbe=Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE");
            if(!string.IsNullOrEmpty(roadmapProbe))
            {
                var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
                var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
                if(roadmapProbe=="companion-health")
                    yield return CompanionHealthContactProbe.RunFixture(host,streets,GameManager.Instance.PlayerEntity.Name);
                else if(roadmapProbe=="seat-task")
                    yield return SeatTaskChainProbe.Run(streets,GameManager.Instance.PlayerEntity.Name);
                else if(roadmapProbe=="interior-npc")yield return NativeInteriorResidentProbe.Run();
                else if(roadmapProbe=="doors")yield return NativeNavigationDoorProbe.Run();
                else if(roadmapProbe=="starting-spells")yield return NativeNpcStartingSpellsTrial.Run();
                else if(roadmapProbe=="combat-assist")
                    yield return NativeNpcCombatAssistProbe.RunFixture(host,streets,GameManager.Instance.PlayerEntity.Name);
                else if(roadmapProbe=="guard-assault")
                    yield return NpcGuardAssaultNativeProbe.RunFixture(host,streets,GameManager.Instance.PlayerEntity.Name);
                else if(roadmapProbe=="guard-door")
                    yield return GuardDoorNativeProbe.Run(host,streets,GameManager.Instance.PlayerEntity.Name);
                else if(roadmapProbe=="personal-trade")
                    yield return PersonalTradeNativeProbe.Run(host,streets);
                else if(roadmapProbe=="static-personal-trade")
                    yield return StaticPersonalTradeNativeProbe.RunFixture(host,streets,GameManager.Instance.PlayerEntity.Name);
                else if(roadmapProbe=="dungeon-modules")yield return NativeDungeonModuleProbe.Run();
                else if(roadmapProbe=="dungeon-jump")yield return NativeDungeonJumpProbe.Run();
                else if(roadmapProbe=="interior-module")yield return NativeInteriorModuleProbe.Run();
                else if(roadmapProbe=="paid-companion")
                {
                    if(!host)throw new InvalidOperationException("Paid companion probe requires the loaded mod host.");
                    yield return host.ProbePaidCompanionConsentAndDismiss(null,GameManager.Instance.PlayerEntity.Name);
                }
                else throw new InvalidOperationException("Unknown explicit roadmap probe: "+roadmapProbe);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TRIAL_PASS: roadmap="+roadmapProbe);
                yield break;
            }
            string catalog=Environment.GetEnvironmentVariable("POTIB_NAVMESH_CATALOG_EXPORT");
            string interiorCatalog=Environment.GetEnvironmentVariable("POTIB_NAVMESH_INTERIOR_CATALOG_EXPORT");
            if(!string.IsNullOrEmpty(interiorCatalog))
            {
                yield return NativeInteriorNavigationCatalog.ExportLoadedLocation(interiorCatalog);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TRIAL_PASS: interiorCatalogExport=true");
                yield break;
            }
            if(!string.IsNullOrEmpty(catalog))
            {
                yield return NativeNavigationCatalog.ExportRmb(catalog);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TRIAL_PASS: catalogExport=true");
                yield break;
            }
            string export=Environment.GetEnvironmentVariable("POTIB_NAVMESH_EXPORT");
            if(!string.IsNullOrEmpty(export))
            {
                string location=Environment.GetEnvironmentVariable("POTIB_NAVMESH_LOCATION");
                if(Environment.GetEnvironmentVariable("POTIB_NAVMESH_DUNGEON")=="1")
                    yield return NativeDungeonNavigationCapture.Run(export);
                else if(!string.IsNullOrEmpty(location))
                    yield return NativeLocationNavigationCapture.Run(export,
                        Environment.GetEnvironmentVariable("POTIB_NAVMESH_REGION")??"Daggerfall",location);
                else yield return NativeNavigationWorld.ExportLoadedScene(export);
                if(Environment.GetEnvironmentVariable("POTIB_NAVMESH_DOORS")=="1")
                    yield return NativeNavigationDoorProbe.Run();
                if(Environment.GetEnvironmentVariable("POTIB_NAVMESH_INTERIOR_TEMPLATE")=="1")
                {
                    var room=GameManager.Instance.PlayerEnterExit.Interior;
                    if(!room)throw new InvalidOperationException("Interior template capture requires a loaded native interior.");
                    var templateDoor=room.EntryDoor;
                    string block=DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlockName(templateDoor.blockIndex);
                    yield return NativeInteriorNavigationCatalog.Export(export,block,templateDoor.recordIndex);
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TRIAL_PASS: geometryExport=true");
                yield break;
            }
            if(Environment.GetEnvironmentVariable("POTIB_NAVMESH_PRODUCTION")=="1")
            {
                if(!string.IsNullOrEmpty(interiorTarget))yield return VerifyInterior();
                else yield return VerifyProduction();
                yield break;
            }
            VerifySeatColliderLifecycle();
            var game=GameManager.Instance;
            if(game.PlayerEnterExit.IsPlayerInside)throw new InvalidOperationException("Navigation trial requires exterior.");
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double timeOfDay=clock.ToSeconds()%86400;
            clock.RaiseTime((float)((43200-timeOfDay+86400)%86400));
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation());
            TownGeometry.Entrance entrance;
            if(!geometry.Entrances.TryGetValue(66575,out entrance))throw new InvalidOperationException("Trial Inn entrance missing.");
            var door=geometry.SceneDoor(entrance);
            bool terrace=Environment.GetEnvironmentVariable("POTIB_NAVMESH_TERRACE")=="1";
            FurnitureSeatCatalog seats=null;FurnitureSeatSlot selectedSeat=null;
            if(terrace)
            {
                seats=FurnitureSeatCatalog.ForLoadedScene(directory.transform,"navmesh:"+directory.MapID);
                float highest=0.75f;
                foreach(var seat in seats.Slots)
                {
                    float elevation=seat.Ground.y-geometry.ScenePoint(new PeopleOfTheIliacBay.Core.RoutePoint(
                        directory.transform.InverseTransformPoint(seat.Ground).x,directory.transform.InverseTransformPoint(seat.Ground).z)).y;
                    if(elevation<=highest)continue;
                    highest=elevation;selectedSeat=seat;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_SEAT_SURVEY: seats="+seats.Slots.Count+"; elevated="+(selectedSeat!=null)+"; elevation="+highest);
                if(selectedSeat==null)throw new InvalidOperationException("No actual elevated exterior seat found in fixture.");
                door=selectedSeat.Ground;
            }
            var bounds=new Bounds(door, new Vector3(128,24,128));
            var sources=new List<NavMeshBuildSource>();
            var watch=System.Diagnostics.Stopwatch.StartNew();
            NavMeshBuilder.CollectSources(bounds,~0,NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
            sources.RemoveAll(source=>source.component && (
                source.component.GetComponentInParent<DaggerfallEntityBehaviour>() ||
                source.component.GetComponentInParent<MobilePersonNPC>() ||
                source.component.GetComponentInParent<CrowdMotion>()));
            double collectionMs=watch.Elapsed.TotalMilliseconds;
            if(sources.Count==0)throw new InvalidOperationException("No native navigation sources.");
            var settings=NavMesh.GetSettingsByIndex(0);
            settings.agentRadius=CrowdMotion.BodyRadius+0.05f;settings.agentHeight=1.9f;
            settings.agentClimb=0.5f;settings.agentSlope=45;
            settings.overrideVoxelSize=true;settings.voxelSize=0.12f;
            settings.overrideTileSize=true;settings.tileSize=128;
            var data=new NavMeshData(settings.agentTypeID);
            NavMeshDataInstance instance=default(NavMeshDataInstance);
            var actors=new List<GameObject>();
            try
            {
                float previous=Time.realtimeSinceStartup,maxFrame=0;
                var build=NavMeshBuilder.UpdateNavMeshDataAsync(data,settings,sources,bounds);
                while(!build.isDone)
                {
                    yield return null;
                    float now=Time.realtimeSinceStartup;maxFrame=Math.Max(maxFrame,now-previous);previous=now;
                }
                instance=NavMesh.AddNavMeshData(data);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_BUILD: sources="+sources.Count+"; collectionMs="+collectionMs+
                    "; totalMs="+watch.Elapsed.TotalMilliseconds+"; maxBuildFrameMs="+(maxFrame*1000)+"; bounds="+bounds);
                NavMeshHit destination;
                if(!NavMesh.SamplePosition(door,out destination,2,NavMesh.AllAreas))throw new InvalidOperationException("No safe Inn approach within 2m.");
                Vector3 start=Vector3.zero;NavMeshPath route=null;
                for(int i=0;i<16;i++)
                {
                    float angle=i*Mathf.PI/8;
                    NavMeshHit candidate;
                    var requested=door+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*18;
                    if(terrace)
                    {
                        var local=directory.transform.InverseTransformPoint(requested);
                        requested=geometry.ScenePoint(new PeopleOfTheIliacBay.Core.RoutePoint(local.x,local.z));
                    }
                    if(!NavMesh.SamplePosition(requested,out candidate,2,NavMesh.AllAreas))continue;
                    var path=new NavMeshPath();
                    if(terrace)
                    {
                        if(door.y-candidate.position.y<0.75f || !NativeSeatNavigation.TryApproach(seats,selectedSeat,candidate.position,out path,
                            message=>Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_SEAT_ROUTE: "+message)))continue;
                        destination.position=selectedSeat.Approach;start=candidate.position;route=path;break;
                    }
                    if(NavMesh.CalculatePath(candidate.position,destination.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete && path.corners.Length>=2)
                    {start=candidate.position;route=path;break;}
                }
                if(route==null)
                {
                    if(terrace)
                    {
                        var camera=game.MainCamera;
                        camera.transform.position=selectedSeat.Ground+selectedSeat.Facing.normalized*6+Vector3.up*4;
                        camera.transform.LookAt(selectedSeat.Surface);
                        yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"navmesh-terrace.png"));
                        yield return null;
                    }
                    throw new InvalidOperationException("No complete native route to seat/door approach.");
                }
                // Corners omit intermediate stair-surface heights. Check actual
                // agent steps below, not a straight 3D chord through a staircase.
                var agents=new List<NavMeshAgent>();
                agents.Add(CreateActor(start,destination.position,settings.agentTypeID,30,Color.cyan,actors));
                agents.Add(CreateActor(destination.position,start,settings.agentTypeID,60,Color.magenta,actors));
                float deadline=Time.realtimeSinceStartup+60,minimumSeparation=float.MaxValue;
                var positions=new[]{start,destination.position};var moved=new float[2];
                while(Time.realtimeSinceStartup<deadline)
                {
                    yield return null;
                    bool finished=true;
                    for(int i=0;i<agents.Count;i++)
                    {
                        var agent=agents[i];var position=agent.transform.position;
                        AssertBodyClear(positions[i],position);
                        float step=Vector3.Distance(positions[i],position);
                        if(step>1)throw new InvalidOperationException("Native agent jumped over one metre.");
                        moved[i]+=step;positions[i]=position;
                        if(agent.pathPending || agent.remainingDistance>0.3f)finished=false;
                    }
                    minimumSeparation=Math.Min(minimumSeparation,Vector3.Distance(positions[0],positions[1]));
                    if(minimumSeparation<0.5f)throw new InvalidOperationException("Native agents overlap body footprints.");
                    if(finished)
                    {
                        if(moved[0]<10 || moved[1]<10)throw new InvalidOperationException("Native trial did not walk the route.");
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TRIAL_PASS: nativeGeometry=true; proxyAgents=2; productionNpcIntegration=false; building=66575; corners="+
                            route.corners.Length+"; moved="+moved[0]+","+moved[1]+"; minimumSeparation="+minimumSeparation+
                            "; requestedDoor="+door+"; safeApproach="+destination.position);
                        if(terrace)
                        {
                            foreach(var agent in agents){agent.enabled=false;agent.gameObject.SetActive(false);}
                            yield return WalkSeatAndReturn(start,selectedSeat,seats,settings.agentTypeID,actors);
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TERRACE_PASS: seat="+selectedSeat.Id+"; ascent="+(destination.position.y-start.y)+
                                "; approach="+selectedSeat.Approach+"; actualWalking=true; seatedAnimation=true; returnedToStreet=true");
                        }
                        yield break;
                    }
                }
                throw new InvalidOperationException("Native navigation agents did not arrive within 60 seconds.");
            }
            finally
            {
                foreach(var actor in actors)if(actor)UnityEngine.Object.Destroy(actor);
                if(instance.valid)instance.Remove();
                UnityEngine.Object.Destroy(data);
            }
        }
        private static IEnumerator VerifyProduction()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double timeOfDay=clock.ToSeconds()%86400;
            clock.RaiseTime((float)((43200-timeOfDay+86400)%86400));
            var world=NativeNavigationWorld.Current;
            float deadline=Time.realtimeSinceStartup+60;
            while(!world.Ready&&Time.realtimeSinceStartup<deadline)yield return null;
            if(!world.Ready)throw new InvalidOperationException("No shipped native navigation data loaded.");
            var positions=new Dictionary<int,Vector3>();var travelled=new Dictionary<int,float>();
            ResidentActor watched=null;
            deadline=Time.realtimeSinceStartup+40;
            while(Time.realtimeSinceStartup<deadline)
            {
                foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                {
                    var navigation=actor.GetComponent<NativeResidentNavigation>();
                    if(!navigation||!navigation.Active)continue;
                    if(!watched || !watched.gameObject.activeInHierarchy)
                    {watched=actor;TownSmokeHarness.Watch(actor.transform);}
                    int id=actor.GetInstanceID();Vector3 previous;var ground=actor.transform.position-Vector3.up;
                    if(positions.TryGetValue(id,out previous))
                    {
                        float moved=Vector3.Distance(previous,ground);
                        if(moved>1.0f)throw new InvalidOperationException("Ordinary native resident jumped: "+actor.Resident.Id+"; step="+moved);
                        AssertBodyClear(previous,ground);
                        travelled[id]=travelled.ContainsKey(id)?travelled[id]+moved:moved;
                    }
                    positions[id]=ground;
                }
                int walkers=0;float distance=0;
                foreach(var value in travelled.Values)if(value>5){walkers++;distance+=value;}
                if(walkers>=3)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TRIAL_PASS: productionNpcIntegration=true; runtimeBake=false; modules="+world.ModuleCount+
                        "; ordinaryWalkers="+walkers+"; metres="+distance);
                    yield break;
                }
                yield return null;
            }
            var residents=UnityEngine.Object.FindObjectsOfType<ResidentActor>();
            foreach(var actor in residents)
            {
                var navigation=actor.GetComponent<NativeResidentNavigation>();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_RESIDENT_DIAGNOSTIC: "+actor.Resident.Id+"; position="+actor.transform.position+
                    "; native="+(navigation!=null)+"; failure="+(navigation?navigation.FailureReason:"none"));
            }
            throw new InvalidOperationException("Fewer than three ordinary residents completed five metres on shipped native data; tracked="+travelled.Count+"; residents="+residents.Length);
        }
        private static void VerifySeatColliderLifecycle()
        {
            var body=new GameObject("Seat collider lifecycle test");
            try
            {
                var enabled=body.AddComponent<CapsuleCollider>();var disabled=body.AddComponent<BoxCollider>();disabled.enabled=false;
                var collision=body.AddComponent<SeatBodyCollision>();collision.Suspend();collision.Suspend();
                if(enabled.enabled||disabled.enabled)throw new InvalidOperationException("Seat approach left own collider active.");
                collision.Restore();
                if(!enabled.enabled||disabled.enabled)throw new InvalidOperationException("Seat exit lost original collider states.");
                collision.Suspend();body.SetActive(false);
                if(!enabled.enabled||disabled.enabled)throw new InvalidOperationException("Interrupted seat approach lost original collider states.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SEAT_COLLIDER_PASS: approachAndSitting=true; restoredOriginalStates=true; interruption=true");
            }
            finally{UnityEngine.Object.Destroy(body);}
        }
        private static IEnumerator WalkSeatAndReturn(Vector3 start,FurnitureSeatSlot seat,FurnitureSeatCatalog seats,int type,List<GameObject> objects)
        {
            var root=new GameObject("Native resident terrace trial");objects.Add(root);root.transform.position=start;
            var body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,root.transform);
            body.transform.localPosition=Vector3.up;
            var actor=body.AddComponent<ResidentActor>();
            actor.Initialize(new PeopleOfTheIliacBay.Core.Resident {Id="native-terrace-trial",FullName="Terrace Patron",AppearanceSeed=52,
                Sex=PeopleOfTheIliacBay.Core.ResidentSex.Male},1,GameManager.Instance.StreamingWorld.GetCurrentCityNavigation());
            var agent=root.AddComponent<NavMeshAgent>();agent.agentTypeID=type;agent.radius=CrowdMotion.BodyRadius+0.05f;agent.height=1.9f;
            agent.speed=2;agent.acceleration=4;agent.angularSpeed=180;agent.stoppingDistance=0.05f;
            actor.Person.Asset.IsIdle=false;
            if(!agent.SetDestination(seat.Approach))throw new InvalidOperationException("Resident cannot start terrace route.");
            yield return WalkAgent(agent,seat.Approach);
            agent.enabled=false;
            var crowd=actor.GetComponent<CrowdMotion>();crowd.RestorePlacement(root.transform.position);
            SelectedSeatAccess access;
            if(!seats.TryAcquire(actor.Resident.Id,seat.Id,out access))throw new InvalidOperationException("Terrace seat unavailable.");
            try
            {
                float deadline=Time.realtimeSinceStartup+10;bool arrived=false;
                while(Time.realtimeSinceStartup<deadline)
                {
                    arrived=access.Advance(actor);
                    foreach(var collider in body.GetComponentsInChildren<Collider>())
                        if(collider.enabled)throw new InvalidOperationException("NPC collider enabled during final seat approach.");
                    if(arrived&&actor.IsSeated)break;
                    yield return null;
                }
                if(!arrived||!actor.IsSeated)throw new InvalidOperationException("Actual resident could not sit on terrace chair.");
                var camera=GameManager.Instance.MainCamera;
                camera.transform.position=seat.Ground+seat.Facing.normalized*4+Vector3.up*2;
                camera.transform.LookAt(seat.Surface+Vector3.up*0.4f);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"navmesh-terrace-seated.png"));
                yield return new WaitForSecondsRealtime(1);
                deadline=Time.realtimeSinceStartup+10;arrived=false;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(access.Advance(actor,true)){arrived=true;break;}
                    yield return null;
                }
                if(!arrived||actor.IsSeated)throw new InvalidOperationException("Actual resident could not leave terrace chair.");
            }
            finally{access.Release();}
            var departed=actor.transform.position-Vector3.up;
            root.transform.position=departed;body.transform.localPosition=Vector3.up;
            agent.enabled=true;if(!agent.Warp(departed)||!agent.SetDestination(start))throw new InvalidOperationException("Resident cannot return from terrace.");
            actor.Person.Asset.IsIdle=false;
            yield return WalkAgent(agent,start);
        }
        private static IEnumerator EnterInterior(int buildingKey)
        {
            var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Interior capture requires a loaded exterior.");
            DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var door in candidate.Doors??new StaticDoor[0])
                    if(door.doorType==DoorTypes.Building && door.buildingKey==buildingKey)
                    {owner=candidate;entry=door;break;}
            if(!owner)throw new InvalidOperationException("Navigation interior entrance missing: "+buildingKey);
            TownSmokeHarness.EnterInterior(owner.transform,entry);
            float deadline=Time.realtimeSinceStartup+20;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;
                var interior=game.PlayerEnterExit.Interior;
                if(!game.PlayerEnterExit.IsPlayerInsideBuilding || !interior ||
                    interior.EntryDoor.buildingKey!=buildingKey || interior.GetComponentsInChildren<Collider>().Length==0)continue;
                DaggerfallUI.Instance.PopToHUD();
                yield return new WaitForSecondsRealtime(1);
                yield break;
            }
            throw new InvalidOperationException("Navigation interior entry did not complete.");
        }
        private static IEnumerator VerifyInterior()
        {
            var game=GameManager.Instance;
            string expected="interior-"+game.PlayerGPS.CurrentLocation.MapTableData.MapId+"-"+
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey;
            float deadline=Time.realtimeSinceStartup+10;
            while((!NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready ||
                NativeNavigationWorld.Current.Scene!=expected) && Time.realtimeSinceStartup<deadline)yield return null;
            var world=NativeNavigationWorld.Current;
            if(!world || !world.Ready || world.Scene!=expected || world.ModuleCount!=1)
                throw new InvalidOperationException("Exact prebaked interior navigation was not loaded: "+expected);
            NavMeshHit start;
            var geometry=new InteriorGeometry(game.PlayerEnterExit.Interior,sampleFloorOnly:true);
            if(!geometry.EntryGround.HasValue || !NavMesh.SamplePosition(geometry.EntryGround.Value,out start,0.5f,NavMesh.AllAreas))
                throw new InvalidOperationException("Interior entrance does not meet baked floor.");
            Vector3 destination=start.position;float distance=5;
            foreach(var point in NavMesh.CalculateTriangulation().vertices)
            {
                float gap=Vector3.Distance(start.position,point);if(gap<=distance)continue;
                NavMeshHit sampled;
                if(!NavMesh.SamplePosition(point,out sampled,0.4f,NavMesh.AllAreas))continue;
                var route=new NavMeshPath();
                if(!NavMesh.CalculatePath(start.position,sampled.position,NavMesh.AllAreas,route) || route.status!=NavMeshPathStatus.PathComplete)continue;
                bool clear=true;
                try {for(int corner=1;corner<route.corners.Length;corner++)AssertBodyClear(route.corners[corner-1],route.corners[corner]);}
                catch(InvalidOperationException){clear=false;}
                if(!clear)continue;
                float actualGap=Vector3.Distance(start.position,sampled.position);
                if(actualGap<=5)continue;
                destination=sampled.position;distance=actualGap;break;
            }
            if(distance<=5)throw new InvalidOperationException("Interior lacks a complete five-metre entrance route.");
            var objects=new List<GameObject>();
            var observerObstacle=game.PlayerMotor.GetComponent<NativeNavigationBody>();
            bool observerEnabled=observerObstacle && observerObstacle.enabled;
            try
            {
                // This isolated geometry probe returns to the native player
                // entrance. The observer must not occupy its return endpoint.
                if(observerObstacle)observerObstacle.enabled=false;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_ROUTE: start="+start.position+"; destination="+destination+
                    "; player="+game.PlayerMotor.transform.position+"; observerAvoidanceSuspended="+observerEnabled);
                var agent=CreateActor(start.position,destination,NativeNavigationGeometry.Settings().agentTypeID,40,Color.cyan,objects);
                agent.stoppingDistance=0.05f;
                yield return WalkAgent(agent,destination);
                yield return new WaitForEndOfFrame();
                var viewport=Camera.main.WorldToViewportPoint(agent.transform.position+Vector3.up*0.95f);
                if(viewport.z<=0 || Mathf.Abs(viewport.x-0.5f)>0.1f || Mathf.Abs(viewport.y-0.5f)>0.1f)
                    throw new InvalidOperationException("Navigation test camera missed target: "+viewport);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"navmesh-interior-target.png"));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_CAMERA_PASS: targetViewport="+viewport);
                if(!agent.SetDestination(start.position))throw new InvalidOperationException("Interior return route failed.");
                yield return WalkAgent(agent,start.position);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_INTERIOR_PASS: scene="+expected+
                    "; modules=1; runtimeBake=false; walkedBothWays=true; distance="+distance+
                    "; rootRotation="+game.PlayerEnterExit.Interior.transform.eulerAngles);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_TRIAL_PASS: prebakedInterior=true");
            }
            finally
            {
                TownSmokeHarness.Watch(null);
                foreach(var item in objects)if(item)UnityEngine.Object.Destroy(item);
                if(observerObstacle)observerObstacle.enabled=observerEnabled;
            }
        }
        private static IEnumerator WalkAgent(NavMeshAgent agent,Vector3 destination)
        {
            TownSmokeHarness.Watch(agent.transform,Vector3.up*0.95f);
            float deadline=Time.realtimeSinceStartup+80;var previous=agent.transform.position;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;
                var position=agent.transform.position;AssertBodyClear(previous,position);
                if(Vector3.Distance(previous,position)>1)throw new InvalidOperationException("Resident navigation jumped.");
                previous=position;
                if(!agent.pathPending && Vector3.Distance(position,destination)<0.15f)yield break;
            }
            throw new InvalidOperationException("Resident navigation walk timed out: position="+agent.transform.position+
                "; destination="+destination+"; remaining="+agent.remainingDistance+"; velocity="+agent.velocity+
                "; paused="+GameManager.IsGamePaused+"; status="+agent.pathStatus);
        }
        private static NavMeshAgent CreateActor(Vector3 start,Vector3 destination,int type,int priority,Color color,List<GameObject> actors)
        {
            var root=new GameObject("Native navigation trial agent");actors.Add(root);root.transform.position=start;
            var visual=GameObject.CreatePrimitive(PrimitiveType.Capsule);visual.transform.SetParent(root.transform,false);
            visual.transform.localPosition=Vector3.up*0.95f;visual.transform.localScale=new Vector3(0.5f,0.95f,0.5f);
            UnityEngine.Object.Destroy(visual.GetComponent<Collider>());
            visual.GetComponent<Renderer>().material.color=color;
            var agent=root.AddComponent<NavMeshAgent>();agent.agentTypeID=type;agent.radius=CrowdMotion.BodyRadius+0.05f;agent.height=1.9f;
            agent.speed=2;agent.acceleration=4;agent.angularSpeed=180;agent.stoppingDistance=0.15f;
            agent.avoidancePriority=priority;agent.obstacleAvoidanceType=ObstacleAvoidanceType.HighQualityObstacleAvoidance;
            if(!agent.isOnNavMesh || !agent.SetDestination(destination))throw new InvalidOperationException("Native navigation agent could not start.");
            return agent;
        }
        private static void AssertBodyClear(Vector3 from,Vector3 to)
        {
            foreach(var collider in Physics.OverlapCapsule(to+Vector3.up*0.76f,to+Vector3.up*1.65f,0.25f,~0,QueryTriggerInteraction.Ignore))
                if(Static(collider))throw new InvalidOperationException("NavMesh enters native obstacle: "+collider.name+" at "+to);
            var delta=to-from;if(delta.sqrMagnitude<0.000001f)return;
            foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*0.76f,from+Vector3.up*1.65f,0.25f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(Static(hit.collider))throw new InvalidOperationException("NavMesh crosses native obstacle: "+hit.collider.name);
        }
        private static bool Static(Collider collider)
        {
            return !collider.GetComponentInParent<DaggerfallEntityBehaviour>() && !collider.GetComponentInParent<MobilePersonNPC>() &&
                !collider.GetComponentInParent<CrowdMotion>() && !collider.GetComponentInParent<NavMeshAgent>();
        }
    }
}
