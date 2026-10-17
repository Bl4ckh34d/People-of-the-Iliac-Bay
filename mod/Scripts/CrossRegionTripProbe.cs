using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class CrossRegionTripProbe
    {
        public static IEnumerator Run()
        {
            VerifyRoadStopSerialization();
            var game=GameManager.Instance;
            game.PlayerEntity.GodMode=true;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var host=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var catalog=new TravelOriginCatalog.Result();
            yield return TravelOriginCatalog.Read(catalog,()=>true);
            var target=catalog.Sites.Find(s=>s.Settlement==directory.MapID);
            var candidates=catalog.Sites.FindAll(s=>s.Region!=target.Region);
            candidates.Sort((a,b)=>Distance(a,target).CompareTo(Distance(b,target)));
            int day=(int)(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()/86400)+1;
            for(int i=0;i<Math.Min(12,candidates.Count);i++)
            {
                var origin=candidates[i];
                var source=DaggerfallUnity.Instance.ContentReader.MapFileReader.GetLocation(origin.Region,origin.LocationIndex);
                var store=new WorldStateStore(12345,LivingCitiesMod.ConversationArchiveRoot());
                Debug.Log("LIVING_CITIES_CROSS_REGION_ATTEMPT: origin="+origin.Settlement+"; region="+origin.Region);
                yield return host.ProbeUnloadedTravelDay(source,directory.LocationData,store,day,()=>true);
                var state=store.Snapshot();
                if(state.ResidentTrips.Count==0) continue;
                var trip=state.ResidentTrips[0];
                var routePoints=new System.Collections.Generic.List<RoutePoint>();
                foreach(var point in trip.Road) routePoints.Add(new RoutePoint(point.X,point.Z));
                var roadSites=new RoadSettlementIndex(catalog.Sites).Along(routePoints,trip.HomeSettlement,trip.DestinationSettlement);
                int roadInns=0;
                ResidentRoadInnSurvey.Result passageRoute=null;
                int passageNode=-1;
                UnloadedSettlementSurvey passageTown=null;
                DaggerfallConnect.DFLocation passageLocation=default(DaggerfallConnect.DFLocation);
                foreach(var candidate in roadSites)
                {
                    var place=DaggerfallUnity.Instance.ContentReader.MapFileReader.GetLocation(candidate.Site.Region,candidate.Site.LocationIndex);
                    var survey=new UnloadedSettlementSurvey.Result();yield return UnloadedSettlementSurvey.ReadSteps(place,survey);
                    bool inn=false;
                    foreach(var building in survey.Grid.Buildings)
                        if(building.BuildingType==DaggerfallConnect.DFLocation.BuildingTypes.Tavern) {inn=true;break;}
                    if(inn) roadInns++;
                    if(inn && passageRoute==null)
                    {
                        var route=new ResidentRoadInnSurvey.Result();
                        yield return ResidentRoadInnSurvey.Prepare(survey.Grid,routePoints,candidate.RoadNode,1,3600,route,()=>true);
                        if(route.Visit!=null) {passageRoute=route;passageNode=candidate.RoadNode;passageTown=survey.Grid;passageLocation=place;}
                    }
                    if(survey.Grid.MapPixelX!=routePoints[candidate.RoadNode].X || survey.Grid.MapPixelY!=routePoints[candidate.RoadNode].Z)
                        throw new InvalidOperationException("Road settlement index points outside actual surveyed route.");
                }
                Debug.Log("LIVING_CITIES_ROAD_SETTLEMENT_INDEX_PASS: road nodes="+trip.Road.Count+"; crossed settlements="+roadSites.Count+
                    "; settlements with inns="+roadInns+"; candidate metadata only, no room reservation or stop activation.");
                if(passageRoute==null) throw new InvalidOperationException("No real intermediate inn connects both road approaches.");
                var passageVisit=ResidentVisitJourney.Create(8*3600,passageRoute.Visit);
                if(!passageRoute.Visit.ThroughRoute || passageRoute.EntryRoad.X==passageRoute.ExitRoad.X && passageRoute.EntryRoad.Z==passageRoute.ExitRoad.Z ||
                    passageVisit.EndsAt<=8*3600+3600)
                    throw new InvalidOperationException("Real intermediate inn lost distinct joins or local travel timing.");
                Debug.Log("LIVING_CITIES_ROAD_INN_SURVEY_PASS: settlement="+passageRoute.SettlementId+"; inn="+passageRoute.Door.BuildingKey+
                    "; entry points="+passageRoute.Visit.Stops[0].Route.Count+"; exit points="+passageRoute.Visit.ExitRoute.Count+
                    "; both road joins and model-clear local paths; no rendered actor or terrain acceptance yet.");
                var town=state.Settlements.Find(s=>s.SettlementId==origin.Settlement);
                if(town==null || !town.IsPartial || town.Households.Count!=1 || trip.HomeSettlement!=origin.Settlement ||
                    trip.DestinationSettlement!=target.Settlement || trip.Road.Count<2 || trip.HomeRoute.Count<2 || trip.Destination==null)
                    throw new InvalidOperationException("Cross-region trip lacks reserved hometown or complete route.");
                var person=town.Households[0].Residents.Find(p=>p.Id==trip.ResidentId);
                if(person==null || string.IsNullOrEmpty(person.FullName) || person.Race<0)
                    throw new InvalidOperationException("Cross-region traveler lacks original identity.");
                var paired=ResidentRoadPassage.Attach(trip,passageNode,passageRoute.SettlementId,passageRoute.EntryRoad,
                    passageRoute.ExitRoad,passageRoute.Visit,1800);
                var pairedState=store.Snapshot();pairedState.ResidentTrips[0]=paired;
                var receptionStore=new WorldStateStore(1,LivingCitiesMod.ConversationArchiveRoot());receptionStore.Restore(pairedState);
                var prepared=new ResidentTripPlanner.Result();
                yield return ResidentTripPlanner.PrepareRoadInnReception(paired,passageTown,passageLocation,passageRoute.Door,
                    receptionStore,1,42,prepared,()=>true);
                if(prepared.Trip==null) throw new InvalidOperationException("Real intermediate keeper unavailable: "+prepared.Unavailable);
                int preparedCount=0;
                foreach(var reception in prepared.Trip.InnReceptions)
                {
                    if(reception.SettlementId!=passageTown.SettlementId) continue;
                    if(string.IsNullOrEmpty(reception.KeeperId) || reception.Route.Count<2 || reception.ServiceEndsAt<=reception.VisitEnterAt)
                        throw new InvalidOperationException("Intermediate reception lacks real keeper or counter service.");
                    preparedCount++;
                }
                if(preparedCount!=2 || prepared.Trip.InnStays.Count!=0)
                    throw new InvalidOperationException("Intermediate preparation lost a direction or prepaid a room.");
                var receptionState=receptionStore.Snapshot();receptionState.ResidentTrips[0]=prepared.Trip;
                receptionStore.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),receptionState)));
                Debug.Log("LIVING_CITIES_ROAD_KEEPER_PLAN_PASS: real remote keeper, counter routes, both directions, no advance payment, native restore.");
                var pairedStore=new WorldStateStore(1,LivingCitiesMod.ConversationArchiveRoot());
                pairedStore.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),pairedState)));
                var pairedRoad=new System.Collections.Generic.List<RoutePoint>();foreach(var point in paired.Road) pairedRoad.Add(new RoutePoint(point.X,point.Z));
                int pairedVisits=0;
                for(int direction=0;direction<2;direction++)
                {
                    if(direction==1) pairedRoad.Reverse();
                    var journey=ResidentRoadJourney.Create(pairedRoad,direction==0 ? paired.RoadLeavesAt : paired.ReturnsFromVisitAt,
                        paired.SecondsPerMapPixel,direction==0 ? paired.OutboundStops : paired.ReturnStops);
                    foreach(var visit in journey.ScheduledVisits())
                    {
                        double now=visit.Visit.EnterAt+1;
                        var active=pairedStore.ResidentTripAt(person.Id,now);
                        if(active.SettlementId!=passageRoute.SettlementId || active.BuildingKey!=passageRoute.Door.BuildingKey ||
                            !pairedStore.IsResidentAway(person.Id,now) || pairedStore.RoadResidents(now).Count!=0)
                            throw new InvalidOperationException("Real passage trip lost inn presence or original home absence.");
                        pairedVisits++;
                    }
                }
                if(pairedVisits!=2 || paired.Road.Count!=trip.Road.Count+1 || trip.OutboundStops.Count!=0)
                    throw new InvalidOperationException("Real passage altered its source or lost reverse travel.");
                Debug.Log("LIVING_CITIES_ROAD_PASSAGE_PLAN_PASS: real surveyed inn inserted outbound/return; original resident; existing destination appointments retimed; native restore; source unchanged.");
                // Isolated crime fixture; never change the live resident or player world.
                pairedStore.GetOrCreate(passageRoute.SettlementId,new[]{777},1);
                pairedRoad.Reverse();
                var firstStop=new System.Collections.Generic.List<ResidentRoadSnapshot>(ResidentRoadJourney.Create(pairedRoad,paired.RoadLeavesAt,
                    paired.SecondsPerMapPixel,paired.OutboundStops).ScheduledVisits())[0];
                double crimeAt=firstStop.Visit.EnterAt+2;
                var witnessReport=pairedStore.RecordEntry(passageRoute.SettlementId,777,crimeAt,new[]{person.Id},new string[0]);
                var crime=pairedStore.RecordViolence(passageRoute.SettlementId,person.Id,crimeAt);
                pairedStore.LocateViolence(crime.Id,passageRoute.Door.BuildingKey,1,0,1);
                pairedStore.DestinationResidents(passageRoute.SettlementId,crimeAt).Find(p=>p.Id==person.Id).IsAlive=false;
                pairedStore.ConfirmCorpse(crime.Id,crimeAt+1);
                pairedStore.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),pairedStore.Snapshot())));
                if(pairedStore.ResidentTripAt(person.Id,crimeAt+2).Phase!=ResidentTripPhase.Unavailable ||
                    pairedStore.RecordViolence(passageRoute.SettlementId,person.Id,paired.HomeAt+1).Id!=crime.Id ||
                    pairedStore.EntriesFor(passageRoute.SettlementId)[0].Id!=witnessReport.Id)
                    throw new InvalidOperationException("Native intermediate crime lost original identity or local evidence.");
                Debug.Log("LIVING_CITIES_ROAD_STOP_CRIME_PASS: intermediate entry witness and original resident death retain local incident identity across native restore.");
                store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),state)));
                if(store.ResidentTripAt(trip.ResidentId,(trip.RoadLeavesAt+trip.ArrivesAt)/2).Phase!=ResidentTripPhase.Outbound ||
                    store.ResidentTripAt(trip.ResidentId,(trip.ReturnsFromVisitAt+trip.RoadReturnsAt)/2).Phase!=ResidentTripPhase.Returning)
                    throw new InvalidOperationException("Cross-region restore lost road phases.");
                int visits=0;
                foreach(var visit in ResidentVisitJourney.Create(trip.ArrivesAt,trip.Destination).ScheduledVisits())
                {
                    var restored=store.ResidentTripAt(trip.ResidentId,(visit.EnterAt+visit.LeaveAt)/2);
                    if(restored.Phase!=ResidentTripPhase.Visiting || restored.SettlementId!=target.Settlement ||
                        restored.BuildingKey!=visit.BuildingKey || restored.VisitPhase!=ResidentVisitPhase.Inside)
                        throw new InvalidOperationException("Cross-region restore lost destination service presence.");
                    visits++;
                }
                if(visits==0) throw new InvalidOperationException("Cross-region traveler has no service visits.");
                if(!store.IsResidentAway(trip.ResidentId,trip.ArrivesAt) || store.IsResidentAway(trip.ResidentId,trip.HomeAt) ||
                    game.StreamingWorld.GetCurrentBuildingDirectory()!=directory)
                    throw new InvalidOperationException("Cross-region restore lost absence/return or changed scene.");
                var discoveryState=store.Snapshot();discoveryState.ResidentTrips.Clear();
                var discovered=new WorldStateStore(1,LivingCitiesMod.ConversationArchiveRoot());discovered.Restore(discoveryState);
                double roadNow=(trip.RoadLeavesAt+trip.ArrivesAt)/2;
                if(!discovered.DiscoverOutboundResidentTrip(trip,roadNow) || discovered.DiscoverOutboundResidentTrip(trip,roadNow))
                    throw new InvalidOperationException("Original outbound discovery failed or duplicated identity.");
                discovered.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),
                    SaveLoadManager.Serialize(typeof(WorldState),discovered.Snapshot())));
                if(discovered.ResidentTripAt(trip.ResidentId,roadNow).Phase!=ResidentTripPhase.Outbound ||
                    !discovered.IsResidentAway(trip.ResidentId,roadNow) || discovered.IsResidentAway(trip.ResidentId,trip.HomeAt))
                    throw new InvalidOperationException("Native discovery restore lost original resident lifecycle.");
                Debug.Log("LIVING_CITIES_OUTBOUND_DISCOVERY_PASS: original partial hometown resident; no duplicate; native restore and future return.");
                var liveDiscovery=new WorldStateStore(12345,LivingCitiesMod.ConversationArchiveRoot());
                foreach(int pastDay in ResidentDiscovery.CandidateDepartureDays(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),trip.TravelSeconds))
                {
                    yield return host.ProbeUnloadedTravelDay(source,directory.LocationData,liveDiscovery,pastDay,()=>true,true);
                    if(liveDiscovery.Snapshot().ResidentTrips.Count>0) break;
                }
                var liveState=liveDiscovery.Snapshot();
                if(liveState.ResidentTrips.Count!=1) throw new InvalidOperationException("Production historical candidate runner discovered no outbound traveler.");
                var historical=liveState.ResidentTrips[0];double actualNow=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                liveDiscovery.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),liveState)));
                if(historical.DepartsAt>=actualNow || historical.ArrivesAt<=actualNow ||
                    liveDiscovery.ResidentTripAt(historical.ResidentId,actualNow).Phase!=ResidentTripPhase.Outbound ||
                    !liveDiscovery.IsResidentAway(historical.ResidentId,actualNow) || historical.InnStays.Count!=0 ||
                    liveState.Settlements.Find(s=>s.SettlementId==origin.Settlement).IsPartial==false)
                    throw new InvalidOperationException("Historical production discovery invented history or lost original absence.");
                Debug.Log("LIVING_CITIES_INCOMING_DISCOVERY_PASS: production frame runner; original resident already outbound at current game time; future arrival; no past inn payment; native restore.");
                Debug.Log("LIVING_CITIES_CROSS_REGION_TRIP_PASS: origin="+origin.Settlement+"; region="+origin.Region+
                    "; destination="+target.Settlement+"; region="+target.Region+"; resident="+person.FullName+
                    "; road points="+trip.Road.Count+"; visits="+visits+"; native restore, outbound/returning phases, service presence and home absence/return.");
                yield break;
            }
            throw new InvalidOperationException("No cross-region resident trip succeeded in twelve nearest foreign origins.");
        }
        private static double Distance(VisitorOriginSite a,VisitorOriginSite b)
        { double x=a.X-b.X,z=a.Z-b.Z;return x*x+z*z; }
        private static void VerifyRoadStopSerialization()
        {
            // Isolated fixture: never insert synthetic road stops into the player's world.
            var store=new WorldStateStore(12,LivingCitiesMod.ConversationArchiveRoot());
            var person=store.ReserveHousehold(100,new HomeProfile(10,1,100)).Residents[0];
            foreach(int town in new[]{200,300,400})
            {
                store.GetOrCreate(town,new[]{2},1);
                store.RegisterLodging(town,7,new[]{new LodgingRoom {Id="room",Beds=new System.Collections.Generic.List<string>{"bed"+town}}});
            }
            double start=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+100.123456;
            var trip=ResidentTrip.Create(person.Id,100,200,start,40,10,new[]{
                new RoutePoint(0,0),new RoutePoint(1,0),new RoutePoint(2,0),new RoutePoint(3,0),new RoutePoint(4,0)});
            trip.Destination=ProbeInn(40);
            trip.OutboundStops.Add(new ResidentRoadStop {RoadNode=1,SettlementId=300,Visit=ProbeInn(20)});
            trip.ReturnStops.Add(new ResidentRoadStop {RoadNode=2,SettlementId=400,Visit=ProbeInn(30)});
            store.ScheduleResidentTrip(trip);
            foreach(double offset in new[]{12d,66d,130d})
            {
                double entered=start+offset;var visit=store.ResidentTripAt(person.Id,entered+0.01);
                store.SaveResidentInnReception(person.Id,new ResidentInnReception {VisitId=visit.VisitId,SettlementId=visit.SettlementId,
                    BuildingKey=7,VisitEnterAt=visit.VisitEnterAt,VisitLeaveAt=visit.VisitLeaveAt,StartedAt=entered+0.01,
                    SecondsPerMetre=1,ServiceSeconds=2,Route=new System.Collections.Generic.List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}}});
            }
            var planned=store.Snapshot();
            foreach(double offset in new[]{16d,70d,134d})
                if(!store.TryAdmitResidentInn(person.Id,start+offset)) throw new InvalidOperationException("Scoped inn admission failed.");
            var restored=new WorldStateStore(1,LivingCitiesMod.ConversationArchiveRoot());
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),
                SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            var saved=restored.Snapshot();
            foreach(double offset in new[]{13d,67d,131d})
            {
                var visit=restored.ResidentTripAt(person.Id,start+offset);
                var reception=restored.ResidentInnReceptionAt(person.Id,start+offset);
                if(reception==null || reception.VisitId!=visit.VisitId || reception.SettlementId!=visit.SettlementId)
                    throw new InvalidOperationException("Native road-stop reception lost stop/settlement identity.");
            }
            Debug.Log("LIVING_CITIES_ROAD_STOP_RECEPTION_PASS: three scoped keeper approaches; equal building keys in different towns; native restore.");
            foreach(double offset in new[]{16.25,70.25,134.25})
            {
                var visit=restored.ResidentTripAt(person.Id,start+offset);
                if(restored.ResidentInnBed(person.Id,start+offset)!="bed"+visit.SettlementId ||
                    !restored.TryAdmitResidentInn(person.Id,start+offset))
                    throw new InvalidOperationException("Native intermediate admission lost paid bed or replayed payment.");
            }
            if(restored.Snapshot().Lodgings.Exists(l=>l.Bookings.Count!=1)) throw new InvalidOperationException("Duplicate scoped inn payment.");
            Debug.Log("LIVING_CITIES_ROAD_STOP_ADMISSION_PASS: original traveler; three scoped paid beds; keeper service before admission; native restore; no duplicate payment. Isolated fixture.");
            if(saved.Version!=19 || saved.ResidentTrips.Count!=1 || saved.ResidentTrips[0].OutboundStops[0].Id!="outbound/0" ||
                saved.ResidentTrips[0].ReturnStops[0].Id!="return/0")
                throw new InvalidOperationException("Native road-stop save lost schema or scoped identity.");
            foreach(double offset in new[]{0.25,10.25,12.25,32.25,34.25,64.25,66.25,106.25,108.25,128.25,130.25,160.25,162.25,182.25})
            {
                double now=start+offset;
                var a=store.ResidentTripAt(person.Id,now);var b=restored.ResidentTripAt(person.Id,now);
                if(a.Phase!=b.Phase || a.VisitPhase!=b.VisitPhase || a.VisitId!=b.VisitId || a.SettlementId!=b.SettlementId ||
                    Math.Abs(a.Position.X-b.Position.X)>0.001 || store.IsResidentAway(person.Id,now)!=restored.IsResidentAway(person.Id,now))
                    throw new InvalidOperationException("Native road-stop save changed phase, local position or home absence.");
                if(b.Phase==ResidentTripPhase.Visiting && b.SettlementId!=200 &&
                    (restored.RoadResidents(now).Count!=0 || restored.DestinationResidents(b.SettlementId,now).Count!=1))
                    throw new InvalidOperationException("Intermediate inn presence duplicated or lost original resident.");
            }
            Debug.Log("LIVING_CITIES_ROAD_STOP_SAVE_PASS: schema "+saved.Version+"; outbound/return scopes; native year-scale timestamps; original identity and phase continuity. Isolated timing fixture, not physical inn admission.");
            var refused=new WorldStateStore(1,LivingCitiesMod.ConversationArchiveRoot());refused.Restore(planned);
            if(!refused.TryBookLodging(300,7,"player-room","player",300,true,1,start+10,start+100) ||
                refused.TryAdmitResidentInn(person.Id,start+16)) throw new InvalidOperationException("Intermediate full inn failed to refuse guest.");
            refused.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),
                SaveLoadManager.Serialize(typeof(WorldState),refused.Snapshot())));
            var fleeing=refused.ResidentTripAt(person.Id,start+16.5);
            var changed=refused.Snapshot().ResidentTrips[0];
            if(fleeing.VisitPhase!=ResidentVisitPhase.ExitingService || fleeing.SettlementId!=300 ||
                Math.Abs(fleeing.Position.X-.5)>0.001 || Math.Abs(changed.InnReceptions[1].VisitEnterAt-start-51)>0.001 ||
                Math.Abs(changed.InnReceptions[2].VisitEnterAt-start-115)>0.001 || changed.ServiceExits.Count!=1 ||
                refused.ResidentInnAdmission(person.Id,start+16.5)!=TravelerAdmission.Refused ||
                refused.RoadResidents(start+16.5).Count!=0)
                throw new InvalidOperationException("Native intermediate refusal lost exit, revised appointments or original identity.");
            Debug.Log("LIVING_CITIES_ROAD_STOP_REFUSAL_PASS: full inn; actual counter exit route; retimed destination/return receptions; native restore. Isolated fixture.");
            planned.ResidentTrips[0].InnReceptions.Clear();
            var passage=planned.ResidentTrips[0].OutboundStops[0];passage.ExitRoadNode=3;
            passage.Visit.ThroughRoute=true;passage.Visit.ExitRoute[1].X=1010;
            var through=new WorldStateStore(1,LivingCitiesMod.ConversationArchiveRoot());
            through.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),planned)));
            if(through.Snapshot().Version!=19 || Math.Abs(through.ResidentTripAt(person.Id,start+35).Position.X-3.1)>0.001 ||
                Math.Abs(through.Snapshot().ResidentTrips[0].ArrivesAt-start-44)>0.001)
                throw new InvalidOperationException("Native through-town route did not replace regional center segments.");
            Debug.Log("LIVING_CITIES_ROAD_PASSAGE_SAVE_PASS: schema 17; distinct entry/exit joins replace regional center segments; native restore. Geometry fixture only.");
        }
        private static ResidentDestination ProbeInn(double dwell)
        {
            return new ResidentDestination {ExitWalkSeconds=2,
                ExitRoute=new System.Collections.Generic.List<SavedRoutePoint>{new SavedRoutePoint {X=1002},new SavedRoutePoint {X=1000}},
                Stops=new System.Collections.Generic.List<ResidentVisitStop>{new ResidentVisitStop {BuildingKey=7,
                    Purpose=ResidentVisitPurpose.Inn,WalkSeconds=2,DwellSeconds=dwell,
                    Route=new System.Collections.Generic.List<SavedRoutePoint>{new SavedRoutePoint {X=1000},new SavedRoutePoint {X=1002}}}}};
        }
    }
}
