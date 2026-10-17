using System;
using System.Collections;
using LivingCities.Core;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class ResidentTripProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            game.PlayerEntity.GodMode=true;
            game.PlayerEntity.CrimeCommitted=DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            var host=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            yield return VisitorOriginProbe.Run();
            var clearance=new MeshWalkClearance();
            clearance.Add(new ModelData {
                Vertices=new[]{new Vector3(0,0,-2),new Vector3(0,3,-2),new Vector3(0,3,2),new Vector3(0,0,2)},
                Indices=new[]{0,1,2,0,2,3}},Matrix4x4.identity);
            if(clearance.Clear(new Vector3(-1,1,0),new Vector3(1,1,0),0.2f) ||
                clearance.Clear(new Vector3(0.1f,1,0),new Vector3(1,1,0),0.2f) ||
                clearance.Clear(new Vector3(-1,1,2.1f),new Vector3(1,1,2.1f),0.2f) ||
                !clearance.Clear(new Vector3(-1,1,2.3f),new Vector3(1,1,2.3f),0.2f) ||
                !clearance.Clear(new Vector3(-1,4,0),new Vector3(1,4,0),0.2f))
                throw new InvalidOperationException("Unloaded mesh clearance failed wall, overlap, corner or height fixtures.");
            var workStarts=new System.Collections.Generic.List<float>();
            host.StartCoroutine(CaptureWorkStart(workStarts,()=>true));
            host.StartCoroutine(CaptureWorkStart(workStarts,()=>true));
            host.StartCoroutine(CaptureWorkStart(workStarts,()=>false));
            float workDeadline=Time.unscaledTime+8;
            while(workStarts.Count<2 && Time.unscaledTime<workDeadline) yield return null;
            if(workStarts.Count!=2 || workStarts[1]-workStarts[0]<0.099f)
                throw new InvalidOperationException("Concurrent background requests were not staggered or cancellation consumed work.");
            Debug.Log("LIVING_CITIES_WORK_STAGGER_PASS: concurrent starts separated by "+(workStarts[1]-workStarts[0])+" seconds; cancelled request omitted.");
            OverlandRoadNetwork roads;
            if(!host.RoadNetwork.TryGet(out roads))
                throw new InvalidOperationException("Basic Roads runtime network unavailable.");
            // Validate a nontrivial connected route in the installed provider's data.
            var connected=roads.Find(207,213,207,211,10000);
            if(connected.Count<3) throw new InvalidOperationException("Basic Roads fixture route disconnected.");
            if(roads.Find(207,213,208,213,10000).Count!=0)
                throw new InvalidOperationException("Road planner crossed a disconnected adjacent map pixel.");
            OverlandRoadNetwork cached;
            if(!host.RoadNetwork.TryGet(out cached) || !ReferenceEquals(roads,cached))
                throw new InvalidOperationException("Road network was not cached.");
            System.Collections.Generic.IReadOnlyList<RoutePoint> sliced=null;
            int firstFrame=Time.frameCount;
            yield return host.RoadNetwork.FindRoute(207,213,207,211,10000,()=>true,path=>sliced=path,1);
            if(sliced==null || sliced.Count!=connected.Count || Time.frameCount<=firstFrame+1)
                throw new InvalidOperationException("Runtime road search did not finish across frames.");
            for(int i=0;i<connected.Count;i++)
                if(sliced[i].X!=connected[i].X || sliced[i].Z!=connected[i].Z)
                    throw new InvalidOperationException("Sliced road search changed the route.");
            bool invalidPublished=false;
            yield return host.RoadNetwork.FindRoute(207,213,207,211,10000,()=>false,path=>invalidPublished=true);
            if(invalidPublished) throw new InvalidOperationException("Invalidated route request published a result.");
            Debug.Log("LIVING_CITIES_ROAD_SLICES_PASS: equivalent route across frames; invalidated request suppressed.");
            Debug.Log("LIVING_CITIES_ROAD_NETWORK_PASS: provider road/track import; "+connected.Count+" route points; cached snapshot.");
            var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var unloaded=new UnloadedSettlementSurvey.Result();
            yield return UnloadedSettlementSurvey.ReadSteps(directory.LocationData,unloaded);
            var locationSummary=directory.GetComponent<DaggerfallLocation>().Summary;
            var terrainObject=game.StreamingWorld.GetTerrainFromPixel(locationSummary.MapPixelX,locationSummary.MapPixelY);
            var nativeRect=terrainObject.GetComponent<DaggerfallTerrain>().MapData.locationRect;
            if(unloaded.Grid==null || unloaded.Grid.LocationRect!=nativeRect)
                throw new InvalidOperationException("Unloaded wilderness boundary disagrees with native terrain.");
            Debug.Log("LIVING_CITIES_UNLOADED_BOUNDARY_PASS: native terrain location rectangle matches "+nativeRect);
            var navigation=game.StreamingWorld.GetCurrentCityNavigation();
            if(unloaded.Grid==null || unloaded.Grid.Width!=navigation.NavGridWidth || unloaded.Grid.Height!=navigation.NavGridHeight ||
                unloaded.Grid.Buildings.Count!=directory.BuildingCount) throw new InvalidOperationException("Unloaded settlement survey dimensions/buildings disagree.");
            for(int z=0;z<unloaded.Grid.Height;z++)
            {
                for(int x=0;x<unloaded.Grid.Width;x++)
                {
                    int weight=navigation.GetNavGridWeightLocal(x,z);
                    if(unloaded.Grid.Cost(x,z)!=(weight<=0 ? 0 : Math.Max(1,16-weight)))
                        throw new InvalidOperationException("Unloaded settlement walk grid differs from native layout.");
                    var nativePoint=directory.transform.InverseTransformPoint(navigation.WorldToScenePosition(
                        navigation.NavGridToWorldPosition(new DaggerfallConnect.Utility.DFPosition(x,z)),false));
                    var localPoint=unloaded.Grid.LocalCell(new GridCell(x,z));
                    if(Math.Abs(nativePoint.x-localPoint.x)>0.002f || Math.Abs(nativePoint.z-localPoint.z)>0.002f)
                        throw new InvalidOperationException("Unloaded cell coordinates differ from native layout.");
                }
                if(z%32==0) yield return null;
            }
            foreach(var building in unloaded.Grid.Buildings)
            {
                BuildingSummary actual;
                if(!directory.GetBuildingSummary(building.buildingKey,out actual) || actual.BuildingType!=building.BuildingType)
                    throw new InvalidOperationException("Unloaded service building differs from native directory.");
            }
            var remaining=new System.Collections.Generic.List<UnloadedSettlementSurvey.Entrance>(unloaded.Grid.Entrances);
            int matchedDoors=0;
            foreach(var collection in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var entry in collection.Doors ?? new StaticDoor[0])
                {
                    if(entry.doorType!=DoorTypes.Building) continue;
                    var position=entry.buildingMatrix.MultiplyPoint3x4(entry.centre);
                    var normal=entry.buildingMatrix.MultiplyVector(entry.normal).normalized;
                    int index=remaining.FindIndex(e=>e.BuildingKey==entry.buildingKey &&
                        Vector3.Distance(e.Position,position)<0.001f && Vector3.Distance(e.Normal,normal)<0.001f);
                    if(index<0) throw new InvalidOperationException("Unloaded entrance differs from native door: "+entry.buildingKey);
                    var nativeLocal=directory.transform.InverseTransformPoint(collection.transform.TransformPoint(position));
                    var predicted=unloaded.Grid.LocalEntrance(remaining[index]);
                    if(Vector3.Distance(nativeLocal,predicted)>0.002f)
                        throw new InvalidOperationException("Unloaded entrance settlement coordinates differ from native door: "+entry.buildingKey+" native="+nativeLocal.ToString("F4")+" predicted="+predicted.ToString("F4"));
                    remaining.RemoveAt(index); matchedDoors++;
                }
            if(remaining.Count!=0 || matchedDoors==0) throw new InvalidOperationException("Unloaded entrance coverage differs from native doors.");
            var nativeGates=directory.GetComponentsInChildren<DaggerfallCityGate>();
            if(nativeGates.Length==0 || unloaded.Grid.Gates.Count!=nativeGates.Length)
                throw new InvalidOperationException("Unloaded gate count differs from native city.");
            try
            {
                foreach(var gate in nativeGates) gate.SetOpen(true);
                Physics.SyncTransforms();
                foreach(var gate in unloaded.Grid.Gates)
                {
                    GridCell insideCell;
                    if(!unloaded.Grid.TryGateAccess(gate,out insideCell)) throw new InvalidOperationException("Unloaded gate has no clear street connection.");
                    foreach(float height in new[]{0.4f,1f})
                    {
                        var from=gate.Outside; from.y=height; var to=gate.Inside; to.y=height;
                        if(!NativeClear(directory.transform.TransformPoint(from),directory.transform.TransformPoint(to),0.2f))
                            throw new InvalidOperationException("Unloaded open gate crossing differs from native collision: "+lastNativeBlocker);
                    }
                    if(unloaded.Grid.ClearWalk(gate.Outside,gate.Inside,false))
                        throw new InvalidOperationException("Unloaded closed gate permits crossing.");
                }
                var regional=roads.Find(locationSummary.MapPixelX,locationSummary.MapPixelY,207,214,10000);
                var accessResult=new UnloadedSettlementSurvey.RoadAccessResult();
                yield return unloaded.Grid.ReadRoadAccessSteps(regional,accessResult,()=>true);
                if(accessResult.Accesses==null || accessResult.Accesses.Count==0)
                    throw new InvalidOperationException("No unloaded street-to-road candidates.");
                int verifiedAccesses=0;
                foreach(var access in accessResult.Accesses)
                {
                    // The native Update closes gates again at night while the
                    // sliced survey yields. Test open-plan geometry explicitly.
                    foreach(var gate in nativeGates) gate.SetOpen(true);
                    Physics.SyncTransforms();
                    Vector3 ground; Transform accessTerrainParent;
                    if(!RoadTravelActors.Wilderness(access.RoadPoint) || !RoadTravelActors.Ground(access.RoadPoint,out ground,out accessTerrainParent))
                        throw new InvalidOperationException("Road candidate endpoint is not loaded wilderness.");
                    var predicted=directory.transform.TransformPoint(unloaded.Grid.LocalRoadPoint(access.RoadPoint));
                    if(Vector2.Distance(new Vector2(predicted.x,predicted.z),new Vector2(ground.x,ground.z))>0.02f)
                        throw new InvalidOperationException("Road map/local coordinates disagree with native terrain.");
                    bool clear=true;
                    for(int i=1;i<access.Points.Count;i++)
                    {
                        var from=directory.transform.TransformPoint(access.Points[i-1]);
                        var to=directory.transform.TransformPoint(access.Points[i]);
                        var terrain=accessTerrainParent.GetComponent<Terrain>();
                        from.y=terrain.SampleHeight(from)+terrain.transform.position.y;
                        to.y=terrain.SampleHeight(to)+terrain.transform.position.y;
                        if(ResidentDepartureSurvey.Bridge(from,to,terrain,directory.transform,message=>Debug.Log("LIVING_CITIES_ROAD_ACCESS_REJECT: "+message))==null) {clear=false; break;}
                    }
                    if(clear) verifiedAccesses++;
                    yield return null;
                }
                if(verifiedAccesses==0) throw new InvalidOperationException("No unloaded road candidate passes live terrain and obstacle checks.");
                var cancelledAccess=new UnloadedSettlementSurvey.RoadAccessResult();
                yield return unloaded.Grid.ReadRoadAccessSteps(regional,cancelledAccess,()=>false);
                if(cancelledAccess.Accesses!=null) throw new InvalidOperationException("Cancelled road access survey published.");
                Debug.Log("LIVING_CITIES_UNLOADED_ROAD_ACCESS_PASS: candidates="+accessResult.Accesses.Count+"; live terrain/collision clear="+verifiedAccesses);
                var firstAccess=accessResult.Accesses[0];
                var doorCandidates=new System.Collections.Generic.List<UnloadedSettlementSurvey.Entrance>(unloaded.Grid.Entrances);
                var streetPoint=unloaded.Grid.LocalCell(firstAccess.StreetCell);
                doorCandidates.Sort((a,b)=>(unloaded.Grid.LocalEntrance(a)-streetPoint).sqrMagnitude.CompareTo((unloaded.Grid.LocalEntrance(b)-streetPoint).sqrMagnitude));
                UnloadedSettlementSurvey.BuildingRoadRoute buildingRoute=null;
                for(int attempt=0;attempt<Math.Min(8,doorCandidates.Count);attempt++)
                {
                    var candidate=new UnloadedSettlementSurvey.BuildingRoadRoute();
                    yield return unloaded.Grid.ReadRoadBuildingRouteSteps(firstAccess,doorCandidates[attempt],candidate,()=>true);
                    if(candidate.Points!=null) {buildingRoute=candidate; break;}
                }
                if(buildingRoute==null) throw new InvalidOperationException("Regional road cannot reach a nearby building.");
                foreach(var gate in nativeGates) gate.SetOpen(true);
                Physics.SyncTransforms();
                for(int i=1;i<buildingRoute.Points.Count;i++)
                    foreach(float height in new[]{0.4f,1f})
                    {
                        var a=buildingRoute.Points[i-1]; var b=buildingRoute.Points[i];
                        var from=directory.transform.TransformPoint(new Vector3((float)a.X,0,(float)a.Z));
                        var to=directory.transform.TransformPoint(new Vector3((float)b.X,0,(float)b.Z));
                        var routeTerrain=terrainObject.GetComponent<Terrain>();
                        int samples=Math.Max(1,Mathf.CeilToInt(Vector3.Distance(from,to)/2));
                        var previous=from; previous.y=routeTerrain.SampleHeight(previous)+routeTerrain.transform.position.y+height;
                        for(int sample=1;sample<=samples;sample++)
                        {
                            var next=Vector3.Lerp(from,to,(float)sample/samples);
                            next.y=routeTerrain.SampleHeight(next)+routeTerrain.transform.position.y+height;
                            if(!NativeClear(previous,next,0.2f)) throw new InvalidOperationException("Road-to-building native collision: "+lastNativeBlocker);
                            previous=next;
                        }
                    }
                var generatedVisit=buildingRoute.Visit(ResidentVisitPurpose.Social,0,0,600,1);
                var visitTiming=ResidentVisitJourney.Create(12*3600,generatedVisit);
                if(visitTiming.Evaluate(12*3600+generatedVisit.Stops[0].WalkSeconds+1).BuildingKey!=buildingRoute.BuildingKey ||
                    visitTiming.Evaluate(visitTiming.EndsAt).Phase!=ResidentVisitPhase.Finished)
                    throw new InvalidOperationException("Generated destination route timing does not reach service and return.");
                var cancelledBuilding=new UnloadedSettlementSurvey.BuildingRoadRoute();
                yield return unloaded.Grid.ReadRoadBuildingRouteSteps(firstAccess,doorCandidates[0],cancelledBuilding,()=>false);
                if(cancelledBuilding.Points!=null) throw new InvalidOperationException("Cancelled road/building route published.");
                Debug.Log("LIVING_CITIES_ROAD_BUILDING_ROUTE_PASS: building="+buildingRoute.BuildingKey+"; points="+buildingRoute.Points.Count+"; metres="+buildingRoute.Metres);
                var innKeys=new System.Collections.Generic.HashSet<int>();
                foreach(var summary in unloaded.Grid.Buildings)
                    if(summary.BuildingType==DaggerfallConnect.DFLocation.BuildingTypes.Tavern) innKeys.Add(summary.buildingKey);
                var innDoors=new System.Collections.Generic.List<UnloadedSettlementSurvey.Entrance>();
                foreach(var entrance in unloaded.Grid.Entrances)
                    if(innKeys.Contains(entrance.BuildingKey) && entrance.BuildingKey!=buildingRoute.BuildingKey) innDoors.Add(entrance);
                var sourceDoorPoint=unloaded.Grid.LocalEntrance(buildingRoute.Door);
                innDoors.Sort((a,b)=>(unloaded.Grid.LocalEntrance(a)-sourceDoorPoint).sqrMagnitude.CompareTo((unloaded.Grid.LocalEntrance(b)-sourceDoorPoint).sqrMagnitude));
                var toInn=new UnloadedSettlementSurvey.RouteResult(); int innKey=-1;
                for(int attempt=0;attempt<Math.Min(8,innDoors.Count);attempt++)
                {
                    yield return unloaded.Grid.ReadBuildingRouteSteps(buildingRoute.Door,innDoors[attempt],toInn,()=>true);
                    if(toInn.Points!=null) {innKey=innDoors[attempt].BuildingKey; break;}
                }
                if(innKey<0) throw new InvalidOperationException("No connected inn after the destination stop.");
                var priorEnd=buildingRoute.Points[buildingRoute.Points.Count-1];
                if(Math.Abs(priorEnd.X-toInn.Points[0].X)>0.0001 || Math.Abs(priorEnd.Z-toInn.Points[0].Z)>0.0001)
                    throw new InvalidOperationException("Service-to-inn route starts at a different doorway position.");
                double innMetres=0;
                for(int i=1;i<toInn.Points.Count;i++)
                {
                    var a=toInn.Points[i-1]; var b=toInn.Points[i];
                    double dx=b.X-a.X,dz=b.Z-a.Z; innMetres+=Math.Sqrt(dx*dx+dz*dz);
                    foreach(float height in new[]{0.4f,1f})
                        if(!NativeClear(directory.transform.TransformPoint(new Vector3((float)a.X,height,(float)a.Z)),
                            directory.transform.TransformPoint(new Vector3((float)b.X,height,(float)b.Z)),0.2f))
                            throw new InvalidOperationException("Service-to-inn native collision: "+lastNativeBlocker);
                }
                var innStop=new ResidentVisitStop {BuildingKey=innKey,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=innMetres,DwellSeconds=10*3600};
                foreach(var point in toInn.Points) innStop.Route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                generatedVisit.Stops.Add(innStop);
                var innExit=new System.Collections.Generic.List<SavedRoutePoint>();
                for(int i=toInn.Points.Count-1;i>=0;i--) innExit.Add(new SavedRoutePoint {X=toInn.Points[i].X,Z=toInn.Points[i].Z});
                innExit.AddRange(generatedVisit.ExitRoute); generatedVisit.ExitRoute=innExit;
                generatedVisit.ExitWalkSeconds+=innMetres; generatedVisit.ExitGateSeconds+=innMetres;
                var overnightTiming=ResidentVisitJourney.Create(12*3600,generatedVisit);
                var atInn=overnightTiming.Evaluate(12*3600+generatedVisit.Stops[0].WalkSeconds+generatedVisit.Stops[0].DwellSeconds+innMetres+1);
                if(atInn.BuildingKey!=innKey || atInn.Purpose!=ResidentVisitPurpose.Inn || atInn.Phase!=ResidentVisitPhase.Inside)
                    throw new InvalidOperationException("Chained itinerary did not reach its inn.");
                Debug.Log("LIVING_CITIES_SERVICE_INN_ROUTE_PASS: inn="+innKey+"; segments="+(toInn.Points.Count-1)+"; metres="+innMetres+"; itinerary only, no booking.");
                var nightSkeleton=ResidentTrip.Create("overnight-route-probe",1,directory.MapID,12*3600-10,600,10,
                    new[]{new RoutePoint(207,212),new RoutePoint(207,213)});
                nightSkeleton.Destination=generatedVisit;
                var nightPlan=ResidentOvernightPlan.Schedule(nightSkeleton,123);
                var nightSaved=(ResidentTrip)SaveLoadManager.Deserialize(typeof(ResidentTrip),SaveLoadManager.Serialize(typeof(ResidentTrip),nightPlan));
                var nightRestored=ResidentTrip.CopyValidated(nightSaved);
                var sleepingVisit=nightRestored.Evaluate(86400+3600);
                if(sleepingVisit.BuildingKey!=innKey || sleepingVisit.VisitPurpose!=ResidentVisitPurpose.Inn ||
                    sleepingVisit.VisitLeaveAt%86400<9*3600 || sleepingVisit.VisitLeaveAt%86400>=11*3600 ||
                    Math.Abs(nightPlan.HomeAt-nightRestored.HomeAt)>0.001)
                    throw new InvalidOperationException("Generated native overnight timing or serialization failed.");
                Debug.Log("LIVING_CITIES_OVERNIGHT_TIMING_PASS: real service/inn route retains night, morning checkout and return after serialization.");
                var plannerState=(WorldState)host.GetSaveData();
                var homePopulation=plannerState.Settlements.Find(s=>s.SettlementId==directory.MapID);
                var planningPeople=new System.Collections.Generic.List<Resident>();
                foreach(var household in homePopulation.Households)
                    foreach(var resident in household.Residents)
                        if(resident.IsAlive && resident.AgeGroup==ResidentAgeGroup.Adult && !resident.IsQuestProtected && resident.NativeIdentity==null)
                            planningPeople.Add(resident);
                DaggerfallConnect.DFLocation awayLocation;
                if(!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall","Gothway Garden",out awayLocation))
                    throw new InvalidOperationException("Planner destination fixture missing.");
                var awayPixel=DaggerfallConnect.Arena2.MapsFile.LongitudeLatitudeToMapPixel(awayLocation.MapTableData.Longitude,awayLocation.MapTableData.Latitude);
                System.Collections.Generic.IReadOnlyList<RoutePoint> plannedRoad=null;
                yield return host.RoadNetwork.FindRoute(unloaded.Grid.MapPixelX,unloaded.Grid.MapPixelY,awayPixel.X,awayPixel.Y,30000,()=>true,path=>plannedRoad=path);
                if(plannedRoad==null || plannedRoad.Count<3) throw new InvalidOperationException("Daggerfall/Gothway road fixture needs at least three nodes: "+(plannedRoad==null ? -1 : plannedRoad.Count));
                var plannerAccess=new UnloadedSettlementSurvey.RoadAccessResult();
                yield return unloaded.Grid.ReadRoadAccessSteps(plannedRoad,plannerAccess,()=>true);
                if(plannerAccess.Accesses.Count==0) throw new InvalidOperationException("Gothway road has no departure access.");
                var plannerStreetPoint=unloaded.Grid.LocalCell(plannerAccess.Accesses[0].StreetCell);
                planningPeople.Sort((a,b)=>{
                    Func<Resident,float> distance=r=> {
                        foreach(var entrance in unloaded.Grid.Entrances) if(entrance.BuildingKey==r.HomeKey)
                            return (unloaded.Grid.LocalEntrance(entrance)-plannerStreetPoint).sqrMagnitude;
                        return float.MaxValue;
                    };
                    return distance(a).CompareTo(distance(b));
                });
                Debug.Log("LIVING_CITIES_PLANNER_ROAD: nodes="+plannedRoad.Count+"; access="+plannerStreetPoint+"; candidates="+planningPeople.Count);
                var awaySurvey=new UnloadedSettlementSurvey.Result();
                yield return UnloadedSettlementSurvey.ReadSteps(awayLocation,awaySurvey);
                var coldStore=new WorldStateStore(plannerState.WorldSeed);
                var coldPopulation=new UnloadedPopulation.Result();
                foreach(var building in awaySurvey.Grid.Buildings)
                {
                    if(building.BuildingType<DaggerfallConnect.DFLocation.BuildingTypes.House1 || building.BuildingType>DaggerfallConnect.DFLocation.BuildingTypes.House6) continue;
                    yield return UnloadedPopulation.ReserveHome(awayLocation,awaySurvey.Grid,building.buildingKey,coldStore,coldPopulation,()=>true);
                    if(coldPopulation.Population!=null) break;
                }
                if(coldPopulation.Population==null || coldPopulation.Population.Households.Count==0 ||
                    game.StreamingWorld.GetCurrentBuildingDirectory()!=directory)
                    throw new InvalidOperationException("Unvisited origin did not obtain persistent households without changing the loaded scene.");
                if(!coldPopulation.Population.IsPartial || coldPopulation.Population.Households.Count!=1)
                    throw new InvalidOperationException("Traveler reservation generated the whole hometown.");
                var coldHome=coldPopulation.Population.Households[0];var coldPerson=coldHome.Residents[0];
                if(string.IsNullOrEmpty(coldPerson.FullName) || string.IsNullOrEmpty(coldHome.Surname) || coldPerson.Race<0)
                    throw new InvalidOperationException("Reserved household lacks its hometown identity.");
                coldStore.Restore(RoundTrip(coldStore.Snapshot()));
                var coldHomes=new System.Collections.Generic.List<BuildingSummary>();
                foreach(var building in awaySurvey.Grid.Buildings)
                    if(building.BuildingType>=DaggerfallConnect.DFLocation.BuildingTypes.House1 && building.BuildingType<=DaggerfallConnect.DFLocation.BuildingTypes.House6) coldHomes.Add(building);
                var completedCold=coldStore.GetOrCreateFamilies(awaySurvey.Grid.SettlementId,HousingSurvey.Read(DaggerfallUnity.Instance.ContentReader,awayLocation,coldHomes,DaggerfallUnity.Instance.MeshReader));
                var retainedCold=completedCold.Households.Find(h=>h.BuildingKey==coldHome.BuildingKey);
                if(completedCold.IsPartial || completedCold.Households.Count<=1 || retainedCold.Residents[0].Id!=coldPerson.Id ||
                    retainedCold.Residents[0].FullName!=coldPerson.FullName || retainedCold.Surname!=coldHome.Surname)
                    throw new InvalidOperationException("Reserved traveler identity changed when hometown was completed.");
                Debug.Log("LIVING_CITIES_RESERVED_HOUSEHOLD_PASS: one surveyed home reserved; native restore and full hometown completion preserve identity/family; loaded scene unchanged.");
                var incomingStore=new WorldStateStore(plannerState.WorldSeed);
                int incomingDay=(int)(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()/86400)+1;
                yield return host.ProbeUnloadedTravelDay(awayLocation,directory.LocationData,incomingStore,incomingDay,()=>true);
                var incomingState=incomingStore.Snapshot();
                if(incomingState.ResidentTrips.Count!=1) throw new InvalidOperationException("Unvisited origin did not schedule its reserved traveler.");
                var incomingTrip=incomingState.ResidentTrips[0];
                var incomingTown=incomingState.Settlements.Find(s=>s.SettlementId==awaySurvey.Grid.SettlementId);
                if(incomingTown!=null && incomingTown.Households.Exists(h=>string.IsNullOrEmpty(h.Surname) ||
                    h.Residents.Exists(p=>string.IsNullOrEmpty(p.FullName) || p.Race<0)))
                    throw new InvalidOperationException("Reserved traveler household lacks hometown names or race before departure.");
                if(incomingTown==null || !incomingTown.IsPartial || incomingTown.Households.Count!=1 ||
                    incomingTrip.HomeSettlement!=awaySurvey.Grid.SettlementId || incomingTrip.DestinationSettlement!=directory.MapID ||
                    game.StreamingWorld.GetCurrentBuildingDirectory()!=directory)
                    throw new InvalidOperationException("Unvisited-origin planning generated a full hometown or changed the loaded scene.");
                incomingStore.Restore(RoundTrip(incomingState));
                if(!incomingStore.IsResidentAway(incomingTrip.ResidentId,incomingTrip.ArrivesAt) ||
                    incomingStore.IsResidentAway(incomingTrip.ResidentId,incomingTrip.HomeAt))
                    throw new InvalidOperationException("Reserved incoming traveler lost serialized home absence or return.");
                Debug.Log("LIVING_CITIES_UNVISITED_ORIGIN_PASS: production frame runner; one reserved household; original traveler routes to loaded town; native restore.");
                var planned=new ResidentTripPlanner.Result();
                for(int candidate=0;candidate<Math.Min(4,planningPeople.Count);candidate++)
                {
                    var resident=planningPeople[candidate];
                    var proposal=new ResidentTravelProposal {ResidentId=resident.Id,HomeSettlement=directory.MapID,DestinationSettlement=awayLocation.MapTableData.MapId,
                        DesiredDeparture=Math.Floor(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()/86400.0)*86400+86400+8*3600,VisitDays=1,Purpose=ResidentVisitPurpose.Shop};
                    yield return ResidentTripPlanner.Prepare(proposal,resident,unloaded.Grid,awaySurvey.Grid,plannedRoad,1,planned,()=>true);
                    Debug.Log("LIVING_CITIES_PLANNER_CANDIDATE: home="+resident.HomeKey+"; result="+(planned.Trip!=null ? "prepared" : planned.Unavailable));
                    if(planned.Trip!=null) break;
                }
                if(planned.Trip==null) throw new InvalidOperationException("Original-resident planner failed: "+planned.Unavailable);
                var multiDay=new ResidentTripPlanner.Result();
                yield return ResidentTripPlanner.ExtendOvernight(planned.Trip,2,awaySurvey.Grid,1,123,multiDay,()=>true);
                int firstInnIndex=ResidentOvernightPlan.NeedsLodgingFirst(planned.Trip)?0:1;
                if(multiDay.Trip==null || multiDay.InnDoor==null || multiDay.Trip.Destination.Stops.Count!=(firstInnIndex==0?2:3) ||
                    multiDay.Trip.Destination.Stops[firstInnIndex].Purpose!=ResidentVisitPurpose.Inn ||
                    multiDay.Trip.Destination.Stops[firstInnIndex].DwellSeconds<2*3600 ||
                    multiDay.Trip.Destination.Stops[firstInnIndex].KeepRoomUntil<=multiDay.Trip.ArrivesAt)
                    throw new InvalidOperationException("Native multi-day routing failed: "+multiDay.Unavailable);
                var overnightStore=new WorldStateStore(1);overnightStore.Restore(plannerState);overnightStore.ScheduleResidentTrip(multiDay.Trip);
                var overnightReload=new WorldStateStore(1);overnightReload.Restore(RoundTrip(overnightStore.Snapshot()));
                if(!overnightReload.IsResidentAway(multiDay.Trip.ResidentId,multiDay.Trip.ArrivesAt) ||
                    overnightReload.IsResidentAway(multiDay.Trip.ResidentId,multiDay.Trip.HomeAt))
                    throw new InvalidOperationException("Multi-day original identity lost absence or return.");
                var cancelledNight=new ResidentTripPlanner.Result();
                yield return ResidentTripPlanner.ExtendOvernight(planned.Trip,2,awaySurvey.Grid,1,123,cancelledNight,()=>false);
                if(cancelledNight.Trip!=null) throw new InvalidOperationException("Cancelled multi-day route published a trip.");
                Debug.Log("LIVING_CITIES_OVERNIGHT_ROUTE_PASS: original="+multiDay.Trip.ResidentId+"; inn="+multiDay.InnDoor.BuildingKey+
                    "; stops="+multiDay.Trip.Destination.Stops.Count+"; no booking or automatic enablement.");
                var lateTrip=ResidentTrip.CopyValidated(planned.Trip);
                // Route-only fixture starts at the regional road. Home/gate actor
                // behavior remains covered by the separate departure probes.
                lateTrip.HomeRoute.Clear();lateTrip.HomeWalkSeconds=lateTrip.HomeGateWalkSeconds=0;lateTrip.HomeWalled=false;
                lateTrip=ResidentTrip.CopyValidated(lateTrip);
                var lateService=lateTrip.Destination.Stops[0];
                // Shops have different closing hours; 20:00 can still be open.
                double lateArrival=Math.Floor(lateTrip.ArrivesAt/86400)*86400+lateService.CloseMinute*60+15*60;
                lateTrip.DepartsAt+=lateArrival-lateTrip.ArrivesAt-lateService.WalkSeconds;
                if(lateTrip.Destination.Walled || !ResidentOvernightPlan.NeedsLodgingFirst(lateTrip))
                    throw new InvalidOperationException("Late-arrival fixture did not reach an unwalled service after its closing time.");
                var latePlan=new ResidentTripPlanner.Result();
                yield return ResidentTripPlanner.ExtendOvernight(lateTrip,2,awaySurvey.Grid,1,123,latePlan,()=>true);
                if(latePlan.Trip==null || latePlan.Trip.Destination.Stops.Count!=2 || latePlan.Trip.Destination.Stops[0].Purpose!=ResidentVisitPurpose.Inn ||
                    latePlan.Trip.Destination.Stops[1].BuildingKey!=lateTrip.Destination.Stops[0].BuildingKey)
                    throw new InvalidOperationException("Late arrival did not lodge before its original service.");
                var lateVisits=new System.Collections.Generic.List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(latePlan.Trip.ArrivesAt,latePlan.Trip.Destination).ScheduledVisits());
                if(lateVisits[1].EnterAt<lateVisits[0].LeaveAt || Math.Floor(lateVisits[1].EnterAt/86400)<=Math.Floor(lateVisits[0].EnterAt/86400))
                    throw new InvalidOperationException("Late service did not follow the overnight stay.");
                var lateStore=new WorldStateStore(1);lateStore.Restore(plannerState);lateStore.ScheduleResidentTrip(latePlan.Trip);
                lateStore.Restore(RoundTrip(lateStore.Snapshot()));
                if(lateStore.ResidentTripAt(latePlan.Trip.ResidentId,lateVisits[0].EnterAt+60).VisitPurpose!=ResidentVisitPurpose.Inn)
                    throw new InvalidOperationException("Lodging-first native restore lost the arrival night.");
                Debug.Log("LIVING_CITIES_LODGING_FIRST_PASS: inn="+latePlan.InnDoor.BuildingKey+"; original service="+lateVisits[1].BuildingKey+"; night before service, native restore.");
                var socialTrip=ResidentTrip.CopyValidated(planned.Trip);
                var socialStop=ResidentVisitJourney.Copy(multiDay.Trip.Destination).Stops[firstInnIndex];
                var socialApproach=new System.Collections.Generic.List<SavedRoutePoint>();
                if(firstInnIndex!=0) foreach(var p in socialTrip.Destination.Stops[0].Route) socialApproach.Add(new SavedRoutePoint {X=p.X,Z=p.Z});
                foreach(var p in socialStop.Route) socialApproach.Add(new SavedRoutePoint {X=p.X,Z=p.Z});
                if(firstInnIndex!=0) socialStop.WalkSeconds+=socialTrip.Destination.Stops[0].WalkSeconds;
                socialStop.Route=socialApproach;
                socialStop.Purpose=ResidentVisitPurpose.Social;socialStop.DwellSeconds=120;socialStop.KeepRoomUntil=0;
                socialTrip.Destination.Stops.Clear();socialTrip.Destination.Stops.Add(socialStop);
                socialTrip.Destination.ExitRoute=new System.Collections.Generic.List<SavedRoutePoint>();
                for(int i=socialApproach.Count-1;i>=0;i--) socialTrip.Destination.ExitRoute.Add(new SavedRoutePoint {X=socialApproach[i].X,Z=socialApproach[i].Z});
                socialTrip.Destination.ExitWalkSeconds=socialStop.WalkSeconds;
                socialTrip.Destination.ExitGateSeconds=socialTrip.Destination.Walled ? socialStop.WalkSeconds-socialTrip.Destination.EntryGateSeconds : 0;
                var excludedSocialInns=new System.Collections.Generic.HashSet<int>();
                foreach(var building in awaySurvey.Grid.Buildings)
                    if(building.BuildingType==DaggerfallConnect.DFLocation.BuildingTypes.Tavern && building.buildingKey!=socialStop.BuildingKey)
                        excludedSocialInns.Add(building.buildingKey);
                var sameInn=new ResidentTripPlanner.Result();
                yield return ResidentTripPlanner.ExtendOvernight(socialTrip,3,awaySurvey.Grid,1,123,sameInn,()=>true,excludedSocialInns);
                if(sameInn.Trip==null || sameInn.InnDoor.BuildingKey!=socialStop.BuildingKey || sameInn.Trip.Destination.Stops.Count!=1 ||
                    sameInn.Trip.Destination.Stops[0].WalkSeconds!=socialStop.WalkSeconds || sameInn.Trip.Destination.Stops[0].DwellSeconds<26*3600)
                    throw new InvalidOperationException("Social destination could not host its own overnight stay.");
                var socialStore=new WorldStateStore(1);socialStore.Restore(plannerState);socialStore.ScheduleResidentTrip(sameInn.Trip);
                socialStore.Restore(RoundTrip(socialStore.Snapshot()));
                if(!socialStore.IsResidentAway(sameInn.Trip.ResidentId,sameInn.Trip.ArrivesAt+86400))
                    throw new InvalidOperationException("Same-inn stay lost home absence on native restore.");
                Debug.Log("LIVING_CITIES_SAME_INN_STAY_PASS: only available inn="+socialStop.BuildingKey+"; original approach, two nights and serialized absence.");
                var lodgingStore=new WorldStateStore(plannerState.WorldSeed);
                lodgingStore.GetOrCreateFamilies(directory.MapID,HousingSurvey.Read(directory));
                var housed=new ResidentTripPlanner.Result();
                yield return ResidentTripPlanner.PrepareLodgedTrip(planned.Trip,2,awaySurvey.Grid,awayLocation,lodgingStore,1,123,housed,()=>true);
                if(housed.Trip==null) throw new InvalidOperationException("Overnight capacity preparation failed: "+housed.Unavailable);
                var lateDay=new ResidentTripPlanner.Result();
                yield return ResidentTripPlanner.PrepareLodgedTrip(lateTrip,ResidentOvernightPlan.RequiredVisitDays(lateTrip,1),
                    awaySurvey.Grid,awayLocation,lodgingStore,1,123,lateDay,()=>true);
                if(lateDay.Trip==null || lateDay.Trip.InnReceptions.Count!=1 || lateDay.Trip.InnStays.Count!=0 ||
                    lateDay.Trip.Destination.Stops.Count!=2 || lateDay.Trip.Destination.Stops[0].Purpose!=ResidentVisitPurpose.Inn ||
                    lateDay.Trip.Destination.Stops[1].BuildingKey!=lateTrip.Destination.Stops[0].BuildingKey)
                    throw new InvalidOperationException("Late day visit failed capacity-aware overnight promotion: "+lateDay.Unavailable);
                var lateDayStore=new WorldStateStore(1);lateDayStore.Restore(lodgingStore.Snapshot());
                lateDayStore.ScheduleResidentTrip(lateDay.Trip);lateDayStore.Restore(RoundTrip(lateDayStore.Snapshot()));
                if(!lateDayStore.IsResidentAway(lateDay.Trip.ResidentId,lateDay.Trip.InnReceptions[0].VisitEnterAt+60) ||
                    lateDayStore.IsResidentAway(lateDay.Trip.ResidentId,lateDay.Trip.HomeAt))
                    throw new InvalidOperationException("Promoted day visit lost original home absence or return.");
                Debug.Log("LIVING_CITIES_DAY_VISIT_LODGING_PASS: one-day request promotes to one keeper-bound night before original service; no prepayment; native restore.");
                var remoteHomes=new System.Collections.Generic.List<BuildingSummary>();
                foreach(var building in awaySurvey.Grid.Buildings)
                    if(building.BuildingType>=DaggerfallConnect.DFLocation.BuildingTypes.House1 && building.BuildingType<=DaggerfallConnect.DFLocation.BuildingTypes.House6) remoteHomes.Add(building);
                var expectedHomes=Population.CreateFamilies(plannerState.WorldSeed,awaySurvey.Grid.SettlementId,
                    HousingSurvey.Read(DaggerfallUnity.Instance.ContentReader,awayLocation,remoteHomes,DaggerfallUnity.Instance.MeshReader));
                var actualHomes=lodgingStore.Snapshot().Settlements.Find(s=>s.SettlementId==awaySurvey.Grid.SettlementId);
                if(actualHomes==null || actualHomes.Households.Count!=expectedHomes.Households.Count || actualHomes.Households.Count==0)
                    throw new InvalidOperationException("Remote lodging initialized an empty or incomplete destination population.");
                int firstInn=housed.InnDoor.BuildingKey;
                for(int fill=0;fill<100;fill++)
                    if(!lodgingStore.TryBookLodging(awaySurvey.Grid.SettlementId,firstInn,"overnight-capacity:"+fill,"other-guest:"+fill,
                        directory.MapID,false,1,housed.Trip.ArrivesAt,housed.Trip.HomeAt)) break;
                var alternative=new ResidentTripPlanner.Result();
                yield return ResidentTripPlanner.PrepareLodgedTrip(planned.Trip,2,awaySurvey.Grid,awayLocation,lodgingStore,1,123,alternative,()=>true);
                if(alternative.Trip==null || alternative.InnDoor.BuildingKey==firstInn)
                    throw new InvalidOperationException("Full-inn planning did not choose another reachable inn.");
                if(alternative.Trip.InnReceptions.Count!=1 || alternative.Trip.InnStays.Count!=0)
                    throw new InvalidOperationException("Prepared trip lacks reception plan or paid before arrival.");
                lodgingStore.ScheduleResidentTrip(alternative.Trip);
                var lodgingReload=new WorldStateStore(1);lodgingReload.Restore(RoundTrip(lodgingStore.Snapshot()));
                if(!lodgingReload.IsResidentAway(alternative.Trip.ResidentId,alternative.Trip.ArrivesAt))
                    throw new InvalidOperationException("Capacity-selected trip lost ownership on restore.");
                var reception=alternative.Trip.InnReceptions[0];
                if(string.IsNullOrEmpty(reception.KeeperId)) throw new InvalidOperationException("Remote reception lacks native keeper identity.");
                var restoredReception=lodgingReload.ResidentInnReceptionAt(alternative.Trip.ResidentId,reception.VisitEnterAt+.01);
                if(restoredReception==null || restoredReception.Route.Count!=reception.Route.Count ||
                    Math.Abs(restoredReception.ServiceEndsAt-reception.ServiceEndsAt)>.001)
                    throw new InvalidOperationException("Remote planned reception changed on native serialization.");
                if(lodgingReload.TryAdmitResidentInn(alternative.Trip.ResidentId,reception.StartedAt))
                    throw new InvalidOperationException("Remote resident paid before reaching keeper.");
                if(!lodgingReload.TryAdmitResidentInn(alternative.Trip.ResidentId,reception.ServiceEndsAt+.01))
                    throw new InvalidOperationException("Remote resident failed to pay after counter service.");
                lodgingReload.Restore(RoundTrip(lodgingReload.Snapshot()));
                if(lodgingReload.ResidentInnAdmission(alternative.Trip.ResidentId,reception.ServiceEndsAt+1)!=TravelerAdmission.Admitted ||
                    lodgingReload.ResidentInnBed(alternative.Trip.ResidentId,reception.ServiceEndsAt+1)==null)
                    throw new InvalidOperationException("Remote payment or bed lost on native restore.");
                Debug.Log("LIVING_CITIES_PLANNED_RECEPTION_PASS: building="+alternative.InnDoor.BuildingKey+"; remote route, native restore, delayed payment.");
                var deadState=RoundTrip(lodgingStore.Snapshot());
                var deadKeeper=deadState.NativePeople.Find(p=>p.Id==reception.KeeperId);
                if(deadKeeper==null) throw new InvalidOperationException("Remote keeper identity not persisted.");
                deadKeeper.IsAlive=false;
                var unavailable=new WorldStateStore(1);unavailable.Restore(deadState);
                if(unavailable.HasUnpreparedResidentInnAdmission(alternative.Trip.DestinationSettlement,reception.BuildingKey,reception.ServiceEndsAt+1) ||
                    unavailable.TryAdmitResidentInn(alternative.Trip.ResidentId,reception.ServiceEndsAt+1) ||
                    unavailable.Snapshot().ResidentTrips.Find(t=>t.ResidentId==alternative.Trip.ResidentId).ServiceExits.Count!=1)
                    throw new InvalidOperationException("Dead remote keeper accepted payment or failed to release visitor.");
                unavailable.Restore(RoundTrip(unavailable.Snapshot()));
                if(unavailable.NativePeopleFor(alternative.Trip.DestinationSettlement).Find(p=>p.Id==reception.KeeperId).IsAlive)
                    throw new InvalidOperationException("Refused arrival revived remote keeper.");
                Debug.Log("LIVING_CITIES_KEEPER_ADMISSION_PASS: dead bound keeper rejects payment, prepares exit and stays dead across native restore.");
                Debug.Log("LIVING_CITIES_OVERNIGHT_CAPACITY_PASS: homes="+actualHomes.Households.Count+"; full="+firstInn+
                    "; alternative="+alternative.InnDoor.BuildingKey+"; room remains unbooked until arrival.");
                var plannedStore=new WorldStateStore(1); plannedStore.Restore(plannerState);
                double commitTime=planned.Trip.DepartsAt-1;
                if(ResidentTripPlanner.Commit(plannedStore,planned.Trip,()=>false,commitTime) ||
                    ResidentTripPlanner.Commit(plannedStore,planned.Trip,()=>true,planned.Trip.DepartsAt))
                    throw new InvalidOperationException("Stale or late planning committed a trip.");
                if(!ResidentTripPlanner.Commit(plannedStore,planned.Trip,()=>true,commitTime) ||
                    ResidentTripPlanner.Commit(plannedStore,planned.Trip,()=>true,commitTime))
                    throw new InvalidOperationException("Planner commit failed or duplicated an overlapping trip.");
                Debug.Log("LIVING_CITIES_TRAVEL_COMMIT_PASS: stale/late requests rejected; valid original resident committed once.");
                var adjacent=ResidentTrip.Create(planned.Trip.ResidentId,planned.Trip.HomeSettlement,planned.Trip.DestinationSettlement,
                    planned.Trip.DepartsAt,600,100,new[]{new RoutePoint(207.2,213),new RoutePoint(207.8,213)});
                var adjacentStore=new WorldStateStore(1); adjacentStore.Restore(plannerState); adjacentStore.ScheduleResidentTrip(adjacent);
                var adjacentReload=new WorldStateStore(1); adjacentReload.Restore(RoundTrip(adjacentStore.Snapshot()));
                var adjacentPosition=adjacentReload.ResidentTripAt(adjacent.ResidentId,adjacent.DepartsAt+30);
                if(adjacentPosition.Phase!=ResidentTripPhase.Outbound || Math.Abs(adjacentPosition.Position.X-207.5)>0.000001 ||
                    adjacentReload.IsResidentAway(adjacent.ResidentId,adjacent.HomeAt))
                    throw new InvalidOperationException("Two trimmed endpoints lost native save continuity.");
                Debug.Log("LIVING_CITIES_ADJACENT_ROAD_SAVE_PASS: two trimmed endpoints retain midpoint travel and return after native save.");
                var restoredPlan=new WorldStateStore(1); restoredPlan.Restore(RoundTrip(plannedStore.Snapshot()));
                if(!restoredPlan.IsResidentAway(planned.Trip.ResidentId,planned.Trip.ArrivesAt) || restoredPlan.IsResidentAway(planned.Trip.ResidentId,planned.Trip.HomeAt))
                    throw new InvalidOperationException("Prepared trip lost original-resident absence/return across native save.");
                Debug.Log("LIVING_CITIES_RESIDENT_PLANNER_PASS: original="+planned.Trip.ResidentId+"; destination="+planned.Trip.DestinationSettlement+"; road nodes="+planned.Trip.Road.Count);
                var batchStore=new WorldStateStore(1); batchStore.Restore(plannerState);
                var batchProposal=new ResidentTravelProposal {ResidentId=planned.Trip.ResidentId,HomeSettlement=planned.Trip.HomeSettlement,
                    DestinationSettlement=planned.Trip.DestinationSettlement,DesiredDeparture=planned.Trip.DepartsAt,VisitDays=3,Purpose=ResidentVisitPurpose.Shop};
                int batchFrame=Time.frameCount;
                yield return host.ProbeTravelBatch(directory.LocationData,awayLocation,batchStore,homePopulation,batchProposal,()=>true);
                var batchTrip=batchStore.Snapshot().ResidentTrips.Find(t=>t.ResidentId==batchProposal.ResidentId);
                if(batchTrip==null || Time.frameCount<=batchFrame+10)
                    throw new InvalidOperationException("Live frame-driven travel batch failed to schedule the original resident.");
                if(batchTrip.InnReceptions.Count!=2 || batchTrip.InnStays.Count!=0 ||
                    batchTrip.InnReceptions.Exists(r=>string.IsNullOrEmpty(r.KeeperId)))
                    throw new InvalidOperationException("Automatic three-day trip lost its two planned nights or prepaid before arrival.");
                var batchReload=new WorldStateStore(1);batchReload.Restore(RoundTrip(batchStore.Snapshot()));
                if(!batchReload.IsResidentAway(batchTrip.ResidentId,batchTrip.InnReceptions[1].VisitEnterAt) ||
                    batchReload.IsResidentAway(batchTrip.ResidentId,batchTrip.HomeAt))
                    throw new InvalidOperationException("Automatic multi-day trip lost original identity absence or return after save/load.");
                var cancelledStore=new WorldStateStore(1); cancelledStore.Restore(plannerState);
                yield return host.ProbeTravelBatch(directory.LocationData,awayLocation,cancelledStore,homePopulation,batchProposal,()=>false);
                if(cancelledStore.Snapshot().ResidentTrips.Exists(t=>t.ResidentId==batchProposal.ResidentId))
                    throw new InvalidOperationException("Invalidated live travel batch committed a resident.");
                Debug.Log("LIVING_CITIES_DAILY_TRAVEL_BATCH_PASS: live Update runner scheduled original resident across frames; cancellation committed nothing.");
                Debug.Log("LIVING_CITIES_AUTOMATIC_OVERNIGHT_PASS: three-day proposal; two keeper-bound nights; no prepayment; original identity and return survive save/load.");
            }
            finally
            {
                foreach(var gate in nativeGates) gate.SetOpen(!DaggerfallUnity.Instance.WorldTime.Now.IsNight);
                Physics.SyncTransforms();
            }
            Debug.Log("LIVING_CITIES_UNLOADED_GATES_PASS: "+nativeGates.Length+" open crossings/street connections; closed model blocks crossing.");
            int clearApproaches=0;
            var approachCells=new System.Collections.Generic.List<GridCell>();
            foreach(var entrance in unloaded.Grid.Entrances)
            {
                GridCell cell;
                if(unloaded.Grid.TryApproach(entrance,out cell))
                {
                    var normal=entrance.Normal; normal.y=0;
                    var from=unloaded.Grid.LocalEntrance(entrance)+normal.normalized*0.6f; from.y=1;
                    var to=unloaded.Grid.LocalCell(cell); to.y=1;
                    if(!NativeClear(directory.transform.TransformPoint(from),directory.transform.TransformPoint(to),0.2f) ||
                        !NativeClear(directory.transform.TransformPoint(from-Vector3.up*0.6f),directory.transform.TransformPoint(to-Vector3.up*0.6f),0.2f))
                        throw new InvalidOperationException("Unloaded approach crosses native collision: "+entrance.BuildingKey+"; from="+from+"; to="+to);
                    clearApproaches++;
                    if(!approachCells.Contains(cell)) approachCells.Add(cell);
                }
                yield return null;
            }
            if(clearApproaches<400) throw new InvalidOperationException("Unloaded approach coverage unexpectedly low: "+clearApproaches);
            Debug.Log("LIVING_CITIES_MODEL_CLEARANCE_PASS: wall/corner/overlap fixtures; "+clearApproaches+" model-cleared entrance approaches also clear in native physics.");
            int routeAttempts=0,verifiedSegments=0;
            foreach(var destination in approachCells)
            {
                var startPoint=unloaded.Grid.LocalCell(approachCells[0]);
                var endPoint=unloaded.Grid.LocalCell(destination);
                float gap=(startPoint-endPoint).sqrMagnitude;
                if(gap<900 || gap>3600) continue;
                if(++routeAttempts>6) break;
                var candidate=new UnloadedSettlementSurvey.RouteResult();
                yield return unloaded.Grid.ReadRouteSteps(approachCells[0],destination,candidate,()=>true);
                if(candidate.Points==null || candidate.Points.Count<10) continue;
                for(int p=1;p<candidate.Points.Count;p++)
                {
                    var a=candidate.Points[p-1]; var b=candidate.Points[p];
                    foreach(float height in new[]{0.4f,1f})
                        if(!NativeClear(directory.transform.TransformPoint(new Vector3((float)a.X,height,(float)a.Z)),
                            directory.transform.TransformPoint(new Vector3((float)b.X,height,(float)b.Z)),0.2f))
                            throw new InvalidOperationException("Model route crosses native collision: "+a.X+","+a.Z+" to "+b.X+","+b.Z+"; "+lastNativeBlocker);
                    verifiedSegments++;
                    if(p%8==0) yield return null;
                }
                break;
            }
            if(verifiedSegments<9) throw new InvalidOperationException("No nontrivial model-cleared service route was found.");
            Debug.Log("LIVING_CITIES_MODEL_ROUTE_PASS: "+verifiedSegments+" route segments clear native collision at both walking heights.");
            GridCell routeStart=default(GridCell),routeEnd=default(GridCell);
            bool foundPair=false;
            for(int z=0;z<unloaded.Grid.Height && !foundPair;z++)
                for(int x=0;x<unloaded.Grid.Width-1;x++)
                    if(unloaded.Grid.Cost(x,z)>0 && unloaded.Grid.Cost(x+1,z)>0 &&
                        unloaded.Grid.ClearWalk(unloaded.Grid.LocalCell(new GridCell(x,z)),unloaded.Grid.LocalCell(new GridCell(x+1,z))))
                    {routeStart=new GridCell(x,z); routeEnd=new GridCell(x+1,z); foundPair=true; break;}
            if(!foundPair) throw new InvalidOperationException("Destination grid has no walkable adjacent cells.");
            var routeResult=new UnloadedSettlementSurvey.RouteResult();
            int routeFrame=Time.frameCount;
            yield return unloaded.Grid.ReadRouteSteps(routeStart,routeEnd,routeResult,()=>true,1);
            if(routeResult.Points==null || routeResult.Points.Count!=2 || Time.frameCount<=routeFrame+2)
                throw new InvalidOperationException("Destination route did not complete across frames.");
            bool routeValid=true;
            var cancelled=new UnloadedSettlementSurvey.RouteResult();
            var pending=unloaded.Grid.ReadRouteSteps(routeStart,routeEnd,cancelled,()=>routeValid,1);
            if(!pending.MoveNext()) throw new InvalidOperationException("Destination request did not delay work.");
            routeValid=false;
            while(pending.MoveNext()) yield return pending.Current;
            if(cancelled.Points!=null) throw new InvalidOperationException("Cancelled destination route was published.");
            Debug.Log("LIVING_CITIES_UNLOADED_SETTLEMENT_PASS: all native navigation cells, service buildings and "+matchedDoors+" building doors match without loading a scene.");
            var state=(WorldState)host.GetSaveData();
            var town=state.Settlements.Find(s=>s.SettlementId==directory.MapID);
            var home=town.Households.Find(h=>h.Residents.Count==1 && h.Residents[0].IsAlive &&
                h.Residents[0].AgeGroup==ResidentAgeGroup.Adult && !h.Residents[0].IsQuestProtected && h.Residents[0].NativeIdentity==null);
            if(home==null) throw new InvalidOperationException("Trip fixture needs a single generated adult household.");
            var person=home.Residents[0];
            var routines=state.Routines.Find(r=>r.SettlementId==directory.MapID);
            var routine=routines.Residents.Find(r=>r.ResidentId==person.Id);
            if(routine==null) throw new InvalidOperationException("Resident has no cached routine.");
            routine.DestinationKey=-1; routine.OpenMinute=0; routine.CloseMinute=0; routine.Route.Clear();
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((12*3600-clock.ToSeconds()%86400+86400)%86400));
            double start=clock.ToSeconds()+60;
            // Synthetic adjacent map pixels exercise persistence/absence only.
            // This probe does not claim a verified Basic Roads connection.
            var trip=ResidentTrip.Create(person.Id,directory.MapID,206728581,start,600,100,
                new[]{new RoutePoint(207,213),new RoutePoint(208,213),new RoutePoint(209,213)});
            state.ResidentTrips.RemoveAll(t=>t.ResidentId==person.Id); state.ResidentTrips.Add(trip);
            host.RestoreSaveData(RoundTrip(state));
            yield return Ready(streets);
            DaggerfallStaticDoors owner=null; StaticDoor door=default(StaticDoor);
            foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var entry in candidate.Doors ?? new StaticDoor[0])
                    if(entry.doorType==DoorTypes.Building && entry.buildingKey==home.BuildingKey)
                    { owner=candidate; door=entry; }
            if(!owner) throw new InvalidOperationException("Trip resident home entrance missing.");
            // Hold before departure while the household scene settles.
            trip=ResidentTrip.Create(person.Id,directory.MapID,206728581,clock.ToSeconds()+600,600,100,
                new[]{new RoutePoint(207,213),new RoutePoint(208,213),new RoutePoint(209,213)});
            state=(WorldState)host.GetSaveData(); state.ResidentTrips.Clear(); state.ResidentTrips.Add(trip);
            host.RestoreSaveData(RoundTrip(state));
            yield return Ready(streets);
            TownSmokeHarness.EnterInterior(owner.transform,door); yield return new WaitForSecondsRealtime(2);
            DaggerfallUI.Instance.PopToHUD(); game.PauseGame(false);
            var actor=streets.FindActor(person.Id);
            if(!actor || !actor.gameObject.activeInHierarchy || streets.HouseholdRoutineLocked(home.BuildingKey)!=false)
                throw new InvalidOperationException("Adult was not visibly home before trip.");
            clock.RaiseTime((float)(trip.DepartsAt+1-clock.ToSeconds()));
            yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person.Id);
            if(actor && actor.gameObject.activeInHierarchy || streets.HouseholdRoutineLocked(home.BuildingKey)!=true)
                throw new InvalidOperationException("Traveling resident remains visible at home or keeps home unlocked.");
            state=(WorldState)host.GetSaveData();
            host.RestoreSaveData(RoundTrip(state));
            yield return Ready(streets); yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person.Id);
            if(actor && actor.gameObject.activeInHierarchy || streets.HouseholdRoutineLocked(home.BuildingKey)!=true)
                throw new InvalidOperationException("Restored trip duplicated resident at home.");
            clock.RaiseTime((float)(trip.HomeAt+1-clock.ToSeconds()));
            yield return new WaitForSecondsRealtime(1);
            actor=streets.FindActor(person.Id);
            if(!actor || !actor.gameObject.activeInHierarchy || actor.Resident.Id!=person.Id ||
                actor.Resident.FullName!=person.FullName || streets.HouseholdRoutineLocked(home.BuildingKey)!=false)
                throw new InvalidOperationException("Returning resident did not resume same identity and home access.");
            Debug.Log("LIVING_CITIES_RESIDENT_TRIP_PASS: "+person.Id+"; home actor/lock departure, native serializer restore and same-identity return.");
            game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(2);
            var townGeometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation());
            // This home's surveyed exit is south; do not force a northbound
            // regional edge through the town after reaching that exit.
            var homeRoad=roads.Find(207,213,207,214,10000);
            if(homeRoad.Count<2) throw new InvalidOperationException("Southern road fixture disconnected.");
            var departure=ResidentDepartureSurvey.Read(townGeometry,directory,home.BuildingKey,homeRoad);
            if(departure==null) throw new InvalidOperationException("Home has no clear join to the regional road.");
            var homeTrip=ResidentTrip.Create(person.Id,directory.MapID,206728581,clock.ToSeconds()+120,600,4000,departure.Road);
            var homePath=departure.HomeRoute;
            double metres=0;
            for(int i=0;i<homePath.Count;i++)
            {
                homeTrip.HomeRoute.Add(new SavedRoutePoint {X=homePath[i].X,Z=homePath[i].Z});
                if(i>0) { double x=homePath[i].X-homePath[i-1].X,z=homePath[i].Z-homePath[i-1].Z; metres+=Math.Sqrt(x*x+z*z); }
            }
            homeTrip.HomeWalkSeconds=metres*DaggerfallUnity.Instance.WorldTime.TimeScale/1.3;
            homeTrip.HomeWalled=departure.Walled;
            homeTrip.HomeGateWalkSeconds=departure.Walled ? departure.GateMetres*DaggerfallUnity.Instance.WorldTime.TimeScale/1.3 : 0;
            homeTrip=ResidentTrip.CopyValidated(homeTrip);
            Vector3 joinGround; Transform joinTerrain;
            if(!RoadTravelActors.Ground(departure.Road[0],out joinGround,out joinTerrain) ||
                Vector3.Distance(townGeometry.ScenePoint(homePath[homePath.Count-1]),joinGround)>0.02f)
                throw new InvalidOperationException("Town and regional route endpoints disagree.");
            state=(WorldState)host.GetSaveData(); state.ResidentTrips.Clear(); state.ResidentTrips.Add(homeTrip);
            host.RestoreSaveData(RoundTrip(state)); yield return Ready(streets);
            clock.RaiseTime((float)(homeTrip.HomeLeavesAt+homeTrip.HomeWalkSeconds*0.3-clock.ToSeconds()));
            var homePosition=homeTrip.Evaluate(clock.ToSeconds()).Position;
            game.PlayerMotor.transform.position=townGeometry.ScenePoint(homePosition)+Vector3.up+Vector3.right*3;
            yield return new WaitForSecondsRealtime(1);
            var leaving=streets.FindActor(person.Id);
            if(!leaving || !leaving.gameObject.activeInHierarchy || streets.HouseholdRoutineLocked(home.BuildingKey)!=true)
                throw new InvalidOperationException("Departing resident missing from home connector or home unlocked.");
            Vector3 leaveBefore=leaving.transform.position; yield return new WaitForSecondsRealtime(2);
            if(Vector3.Distance(leaveBefore,leaving.transform.position)<0.05f)
                throw new InvalidOperationException("Resident did not walk toward the gate.");
            host.RestoreSaveData(RoundTrip((WorldState)host.GetSaveData())); yield return Ready(streets);
            yield return new WaitForSecondsRealtime(1);
            if(!streets.FindActor(person.Id)) throw new InvalidOperationException("Restored home connector lost its resident.");
            var originalBody=streets.FindActor(person.Id);
            var originalEntity=originalBody.GetComponent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>().Entity;
            originalEntity.MaxHealth=50; originalEntity.SetHealth(30);
            clock.RaiseTime((float)(homeTrip.RoadLeavesAt+1-clock.ToSeconds()));
            game.PlayerMotor.transform.position=joinGround+Vector3.up+Vector3.right*3;
            yield return new WaitForSecondsRealtime(1);
            if(streets.FindActor(person.Id)) throw new InvalidOperationException("Resident remained in town after exit.");
            var joinedActor=UnityEngine.Object.FindObjectOfType<RoadTravelActors>().Find(person.Id);
            if(!joinedActor || !joinedActor.gameObject.activeInHierarchy || Vector3.Distance(joinedActor.transform.position,joinGround)>15)
                throw new InvalidOperationException("Road actor missing at continuous exit handoff.");
            if(joinedActor!=originalBody || joinedActor.GetComponent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>().Entity.CurrentHealth!=30)
                throw new InvalidOperationException("Road handoff recreated the actor or reset health.");
            Debug.Log("LIVING_CITIES_ROAD_JOIN_PASS: surveyed clear bridge, matching endpoints and same-resident road handoff.");
            Debug.Log("LIVING_CITIES_HOME_CONNECTOR_PASS: surveyed home-to-gate movement, lock, serializer continuation and exit.");
            clock.RaiseTime((float)(homeTrip.HomeEntryAt+1-clock.ToSeconds()));
            yield return new WaitForSecondsRealtime(1);
            var returnedBody=streets.FindActor(person.Id);
            if(returnedBody!=originalBody || originalEntity.CurrentHealth!=30 ||
                UnityEngine.Object.FindObjectOfType<RoadTravelActors>().Find(person.Id))
                throw new InvalidOperationException("Home return recreated the actor or retained duplicate road ownership.");
            Debug.Log("LIVING_CITIES_ROAD_TRANSFER_PASS: same live body and health retained in both directions.");
            state=(WorldState)host.GetSaveData(); state.ResidentTrips.Clear();
            state.ResidentTrips.Add(ResidentTrip.Create(person.Id,directory.MapID,206728581,
                clock.ToSeconds()-4000,600,4000,connected));
            host.RestoreSaveData(RoundTrip(state));
            game.StreamingWorld.TeleportToCoordinates(207,212);
            yield return new WaitForSecondsRealtime(5);
            DaggerfallUI.Instance.PopToHUD(); game.PauseGame(false);
            Vector3 roadGround; Transform terrainParent;
            var saved=(WorldState)host.GetSaveData();
            var actualTrip=ResidentTrip.CopyValidated(saved.ResidentTrips[0]);
            if(!RoadTravelActors.Ground(actualTrip.Evaluate(clock.ToSeconds()).Position,out roadGround,out terrainParent))
                throw new InvalidOperationException("Road actor fixture terrain not loaded.");
            game.PlayerMotor.transform.position=roadGround+Vector3.up+Vector3.right*3;
            yield return new WaitForSecondsRealtime(2);
            var roadActors=UnityEngine.Object.FindObjectOfType<RoadTravelActors>();
            var roadActor=roadActors.Find(person.Id);
            if(!roadActor || !roadActor.gameObject.activeInHierarchy || roadActor.Person.NameNPC!=person.FullName)
                throw new InvalidOperationException("Named resident road actor not visible.");
            Vector3 before=roadActor.transform.position;
            double beforeClock=clock.ToSeconds(); bool animated=false;
            float motionDeadline=Time.realtimeSinceStartup+2;
            while(Time.realtimeSinceStartup<motionDeadline)
            { animated|=!roadActor.Person.Asset.IsIdle; yield return null; }
            if(Vector3.Distance(before,roadActor.transform.position)<0.01f || !animated)
                throw new InvalidOperationException("Road resident motion failed: distance="+Vector3.Distance(before,roadActor.transform.position)+
                    "; animated="+animated+"; elapsedGame="+(clock.ToSeconds()-beforeClock)+"; paused="+GameManager.IsGamePaused);
            game.PlayerMotor.transform.position+=Vector3.right*200;
            yield return new WaitForSecondsRealtime(1);
            if(roadActors.Find(person.Id)) throw new InvalidOperationException("Distant road resident was not culled.");
            RoadTravelActors.Ground(actualTrip.Evaluate(clock.ToSeconds()).Position,out roadGround,out terrainParent);
            game.PlayerMotor.transform.position=roadGround+Vector3.up+Vector3.right*3;
            yield return new WaitForSecondsRealtime(1);
            if(!roadActors.Find(person.Id)) throw new InvalidOperationException("Road resident failed to reconstruct nearby.");
            Debug.Log("LIVING_CITIES_ROAD_ACTOR_PASS: same named resident walks wilderness terrain; distance cull and reconstruction.");
            state=(WorldState)host.GetSaveData();
            var itinerary=new ResidentDestination {ExitWalkSeconds=60};
            itinerary.Stops.Add(new ResidentVisitStop {BuildingKey=10,Purpose=ResidentVisitPurpose.Shop,WalkSeconds=30,DwellSeconds=120,
                Route=new System.Collections.Generic.List<SavedRoutePoint> {new SavedRoutePoint {X=0,Z=0},new SavedRoutePoint {X=10,Z=0}}});
            itinerary.Stops.Add(new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Guild,WalkSeconds=30,DwellSeconds=120,
                Route=new System.Collections.Generic.List<SavedRoutePoint> {new SavedRoutePoint {X=10,Z=0},new SavedRoutePoint {X=20,Z=0}}});
            itinerary.ExitRoute.Add(new SavedRoutePoint {X=20,Z=0}); itinerary.ExitRoute.Add(new SavedRoutePoint {X=0,Z=0});
            state.ResidentTrips[0].Destination=itinerary;
            var itineraryStore=new WorldStateStore(1); itineraryStore.Restore(RoundTrip(state));
            var timed=ResidentTrip.CopyValidated(state.ResidentTrips[0]);
            if(itineraryStore.ResidentTripAt(person.Id,timed.ArrivesAt+60).BuildingKey!=10 ||
                itineraryStore.ResidentTripAt(person.Id,timed.ArrivesAt+200).BuildingKey!=20 ||
                itineraryStore.ResidentTripAt(person.Id,timed.HomeAt).Phase!=ResidentTripPhase.Home)
                throw new InvalidOperationException("Native destination itinerary serialization changed timing or stop identity.");
            Debug.Log("LIVING_CITIES_DESTINATION_SAVE_PASS: two-stop itinerary and derived home return survive native serializer.");
            itineraryStore.GetOrCreate(timed.DestinationSettlement,new[]{30},1);
            var victim=itineraryStore.DestinationResidents(timed.DestinationSettlement,timed.ArrivesAt+60).Find(r=>r.Id==person.Id);
            if(victim==null) throw new InvalidOperationException("Native visitor identity missing.");
            var incident=itineraryStore.RecordViolence(timed.DestinationSettlement,victim.Id,timed.ArrivesAt+60);
            itineraryStore.LocateViolence(incident.Id,10,0,0,0);
            victim.IsAlive=false;
            itineraryStore.ConfirmCorpse(incident.Id,timed.ArrivesAt+61);
            var crimeStore=new WorldStateStore(1); crimeStore.Restore(RoundTrip(itineraryStore.Snapshot()));
            if(crimeStore.DestinationResidents(timed.DestinationSettlement,timed.ArrivesAt+62).Count!=0 ||
                crimeStore.ResidentTripAt(victim.Id,timed.HomeAt).Phase!=ResidentTripPhase.Unavailable ||
                crimeStore.RecordViolence(timed.DestinationSettlement,victim.Id,timed.HomeAt).Id!=incident.Id)
                throw new InvalidOperationException("Native visitor death or incident lost across serialization.");
            Debug.Log("LIVING_CITIES_DESTINATION_CRIME_SAVE_PASS: original resident death and destination corpse survive native serializer.");
            game.StreamingWorld.TeleportToCoordinates(207,213);
            yield return new WaitForSecondsRealtime(3); yield return Ready(streets);
            directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            townGeometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation());
            var visitorStore=new WorldStateStore(1); visitorStore.Restore((WorldState)host.GetSaveData());
            var foreign=visitorStore.GetOrCreate(206728581,new[]{30},1).Households[0].Residents[0];
            var visitTrip=ResidentTrip.Create(foreign.Id,206728581,directory.MapID,clock.ToSeconds()-100,600,100,
                new[]{new RoutePoint(207,212),new RoutePoint(207,213)});
            var localPlan=new ResidentDestination {ExitWalkSeconds=600};
            var localStop=new ResidentVisitStop {BuildingKey=home.BuildingKey,Purpose=ResidentVisitPurpose.Social,WalkSeconds=6000,DwellSeconds=600};
            foreach(var point in homePath) localStop.Route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
            localPlan.Stops.Add(localStop);
            for(int i=homePath.Count-1;i>=0;i--) localPlan.ExitRoute.Add(new SavedRoutePoint {X=homePath[i].X,Z=homePath[i].Z});
            visitTrip.Destination=localPlan; visitorStore.ScheduleResidentTrip(visitTrip);
            host.RestoreSaveData(RoundTrip(visitorStore.Snapshot())); yield return Ready(streets);
            var visitNow=ResidentTrip.CopyValidated(visitTrip).Evaluate(clock.ToSeconds());
            game.PlayerMotor.transform.position=townGeometry.ScenePoint(visitNow.Position)+Vector3.up+Vector3.right*3;
            yield return new WaitForSecondsRealtime(2);
            var visitor=streets.FindActor(foreign.Id);
            if(!visitor || visitor.Resident.FullName!=foreign.FullName || !streets.IsLivingResident(foreign.Id) ||
                streets.GetState(foreign.Id,clock.ToSeconds()).Activity!=Activity.Travel)
                throw new InvalidOperationException("Destination visitor actor or original identity missing.");
            var visitorBefore=visitor.transform.position;
            yield return new WaitForSecondsRealtime(2);
            if(!visitor || Vector3.Distance(visitorBefore,visitor.transform.position)<0.01f)
                throw new InvalidOperationException("Destination visitor failed to move along visit route.");
            Debug.Log("LIVING_CITIES_DESTINATION_ACTOR_PASS: original foreign resident renders and walks destination itinerary.");
            owner=null;
            foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var entry in candidate.Doors ?? new StaticDoor[0])
                    if(entry.doorType==DoorTypes.Building && entry.buildingKey==home.BuildingKey)
                    { owner=candidate; door=entry; }
            if(!owner) throw new InvalidOperationException("Destination visit entrance missing.");
            var acceptedTrip=ResidentTrip.CopyValidated(visitTrip);
            clock.RaiseTime((float)(acceptedTrip.ArrivesAt+localStop.WalkSeconds+30-clock.ToSeconds()));
            TownSmokeHarness.EnterInterior(owner.transform,door);
            yield return new WaitForSecondsRealtime(2); DaggerfallUI.Instance.PopToHUD(); game.PauseGame(false);
            visitor=streets.FindActor(foreign.Id);
            if(!visitor || visitor.Resident.FullName!=foreign.FullName ||
                streets.GetState(foreign.Id,clock.ToSeconds()).BuildingKey!=home.BuildingKey ||
                visitor.transform.parent!=game.PlayerEnterExit.Interior.transform)
                throw new InvalidOperationException("Destination visitor missing inside scheduled building.");
            host.RestoreSaveData(RoundTrip((WorldState)host.GetSaveData())); yield return Ready(streets);
            yield return new WaitForSecondsRealtime(1);
            if(!streets.FindActor(foreign.Id)) throw new InvalidOperationException("Interior visitor lost after native restore.");
            var exitGeometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(streets);
            var exitRoute=exitGeometry.RouteToExit(streets.FindActor(foreign.Id).transform.position-Vector3.up);
            if(exitRoute.Count<2) throw new InvalidOperationException("Foreign visitor has no service exit route.");
            var exitStore=new WorldStateStore(1); exitStore.Restore((WorldState)host.GetSaveData());
            var exitVisit=exitStore.ResidentTripAt(foreign.Id,clock.ToSeconds());
            exitStore.SaveResidentServiceExit(foreign.Id,new ResidentServiceExit {BuildingKey=home.BuildingKey,
                VisitEnterAt=exitVisit.VisitEnterAt,VisitLeaveAt=exitVisit.VisitLeaveAt,StartedAt=clock.ToSeconds(),
                SecondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeed,Route=exitRoute});
            var savedExit=exitStore.Snapshot().ResidentTrips.Find(t=>t.ResidentId==foreign.Id).ServiceExits[0];
            var exitEnd=ResidentServiceExit.Copy(savedExit).ExitAt;
            host.RestoreSaveData(RoundTrip(exitStore.Snapshot())); yield return Ready(streets);
            yield return null;
            if(streets.GetState(foreign.Id,clock.ToSeconds()).BuildingKey!=home.BuildingKey || !streets.FindActor(foreign.Id))
                throw new InvalidOperationException("Foreign service exit vanished before reaching door.");
            clock.RaiseTime((float)(exitEnd+1-clock.ToSeconds())); yield return new WaitForSecondsRealtime(1);
            if(streets.FindActor(foreign.Id)) throw new InvalidOperationException("Foreign service exit failed to leave interior.");
            Debug.Log("LIVING_CITIES_SERVICE_EXIT_PASS: surveyed exit survives native restore, retains interior presence until exit then advances onward.");
            clock.RaiseTime((float)(acceptedTrip.ReturnsFromVisitAt+1-clock.ToSeconds()));
            yield return new WaitForSecondsRealtime(1);
            if(streets.FindActor(foreign.Id)) throw new InvalidOperationException("Departed visitor still occupies destination interior.");
            Debug.Log("LIVING_CITIES_DESTINATION_INTERIOR_PASS: same visitor enters scheduled building, restores and leaves on time.");
        }
        private static WorldState RoundTrip(WorldState state)
        { return (WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),state)); }
        private static string lastNativeBlocker;
        private static bool NativeClear(Vector3 from,Vector3 to,float radius)
        {
            foreach(var collider in Physics.OverlapSphere(from,radius,~0,QueryTriggerInteraction.Ignore))
                if(StaticBlocker(collider)) {lastNativeBlocker=collider.name; return false;}
            var delta=to-from;
            if(delta.sqrMagnitude<0.000001f) return true;
            foreach(var hit in Physics.SphereCastAll(from,radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(StaticBlocker(hit.collider)) {lastNativeBlocker=hit.collider.name; return false;}
            return true;
        }
        private static bool StaticBlocker(Collider collider)
        {
            return collider.GetComponentInParent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>()==null &&
                collider.GetComponentInParent<MobilePersonNPC>()==null && collider.GetComponentInParent<PlayerMotor>()==null;
        }
        private static IEnumerator CaptureWorkStart(System.Collections.Generic.List<float> starts,Func<bool> current)
        {
            yield return BackgroundWork.Wait(current);
            if(current()) starts.Add(Time.unscaledTime);
        }
        private static IEnumerator Ready(StreetPopulation streets)
        {
            // Clock jumps can open native messages in this disposable fixture.
            // Population restoration requires active gameplay, not a paused box.
            DaggerfallUI.Instance.PopToHUD(); GameManager.Instance.PauseGame(false);
            float deadline=Time.realtimeSinceStartup+30;
            while(!streets.Ready && Time.realtimeSinceStartup<deadline) yield return null;
            if(!streets.Ready) throw new InvalidOperationException("Trip fixture population reload timed out; "+
                UnityEngine.Object.FindObjectOfType<LivingCitiesMod>().TestStatus());
        }
    }
}
