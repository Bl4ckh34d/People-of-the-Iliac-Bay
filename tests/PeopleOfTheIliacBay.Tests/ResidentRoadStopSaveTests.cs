using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class ResidentRoadStopSaveTests
    {
        private static List<SavedRoutePoint> Path(double a,double b) {return new List<SavedRoutePoint> {new SavedRoutePoint {X=a},new SavedRoutePoint {X=b}};}
        private static ResidentDestination Inn(double dwell) {return new ResidentDestination {ExitWalkSeconds=2,ExitRoute=Path(1002,1000),
            Stops=new List<ResidentVisitStop> {new ResidentVisitStop {BuildingKey=7,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=2,DwellSeconds=dwell,Route=Path(1000,1002)}}};}
        private static ResidentTrip Trip(string id)
        {
            var trip=ResidentTrip.Create(id,100,200,100,40,10,new[]{new RoutePoint(0,0),new RoutePoint(1,0),new RoutePoint(2,0),new RoutePoint(3,0),new RoutePoint(4,0)});
            trip.HomeRoute=Path(0,1);trip.HomeWalkSeconds=10;trip.Destination=Inn(40);
            trip.OutboundStops.Add(new ResidentRoadStop {RoadNode=1,SettlementId=300,Visit=Inn(20)});
            trip.ReturnStops.Add(new ResidentRoadStop {RoadNode=2,SettlementId=400,Visit=Inn(30)});
            return trip;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Reception planning binds both intermediate visits without replacing destination service",()=>{
                var source=Trip("person");source.ReturnStops[0].SettlementId=300;
                var route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}};
                source=ResidentOvernightPlan.WithReceptions(source,7,route,1,2,"destination-keeper");
                var planned=ResidentOvernightPlan.WithSettlementReceptions(source,300,7,route,1,2,"road-keeper");
                Check.Equal(1,source.InnReceptions.Count);Check.Equal(3,planned.InnReceptions.Count);
                Check.Equal("destination-keeper",planned.InnReceptions[0].KeeperId);
                Check.Equal("outbound/0/0",planned.InnReceptions[1].VisitId);
                Check.Equal("return/0/0",planned.InnReceptions[2].VisitId);
                Check.Equal("road-keeper",planned.InnReceptions[2].KeeperId);
                Check.Equal(0,planned.InnStays.Count);
                route[1].X=99;Check.Near(1,planned.InnReceptions[1].Route[1].X);
                Check.Throws<ArgumentException>(()=>ResidentOvernightPlan.WithSettlementReceptions(planned,300,7,route,1,2));
                Check.Throws<ArgumentException>(()=>ResidentOvernightPlan.WithSettlementReceptions(source,400,7,route,1,2));
                Check.Equal(1,source.InnReceptions.Count);
            }));
            cases.Add(("Intermediate visitor murder retains the original resident death and the actual crime town",()=>{
                var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0];
                store.GetOrCreate(300,new[]{7},1);store.ScheduleResidentTrip(Trip(person.Id));
                Check.Throws<ArgumentException>(()=>store.RecordViolence(300,person.Id,110));
                var alarm=store.RecordViolence(300,person.Id,123);store.LocateViolence(alarm.Id,7,1,2,3);
                person.IsAlive=false;store.ConfirmCorpse(alarm.Id,124);store.Restore(store.Snapshot());
                Check.Equal(ResidentTripPhase.Unavailable,store.ResidentTripAt(person.Id,125).Phase);
                Check.Equal(0,store.DestinationResidents(300,125).Count);
                Check.Equal(alarm.Id,store.RecordViolence(300,person.Id,400).Id);
                Check.Equal(300,store.Snapshot().AlarmIncidents[0].SettlementId);
                Check.True(!store.Snapshot().Settlements.Find(s=>s.SettlementId==100).Households[0].Residents[0].IsAlive);
            }));
            cases.Add(("Intermediate visitor can witness entry only while present and historical reports survive return home",()=>{
                var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0];
                var local=store.GetOrCreate(300,new[]{7},1).Households[0].Residents[0];store.ScheduleResidentTrip(Trip(person.Id));
                Check.Throws<ArgumentException>(()=>store.RecordEntry(300,7,110,new[]{person.Id},new string[0]));
                var report=store.RecordEntry(300,7,123,new[]{person.Id},new string[0]);
                var alarm=store.RecordViolence(300,local.Id,123);Check.True(store.Witness(alarm.Id,person.Id,123)!=null);
                Check.Throws<ArgumentException>(()=>store.RecordEntry(300,7,180,new[]{person.Id},new string[0]));
                store.Restore(store.Snapshot());Check.Equal(report.Id,store.EntriesFor(300)[0].Id);
                Check.Equal(person.Id,store.EntriesFor(300)[0].CitizenWitnesses[0]);
                Check.True(store.Witness(alarm.Id,person.Id,124)!=null);
            }));
            cases.Add(("Through-town joins persist and cannot be silently read as older single-join stops",()=>{
                var store=new WorldStateStore(12);string id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                var trip=Trip(id);trip.OutboundStops[0].ExitRoadNode=3;
                trip.OutboundStops[0].Visit.ThroughRoute=true;trip.OutboundStops[0].Visit.ExitRoute[1].X=1010;
                store.ScheduleResidentTrip(trip);var saved=store.Snapshot();Check.Equal(new WorldState().Version,saved.Version);
                Check.Near(154,saved.ResidentTrips[0].ArrivesAt);Check.Near(282,saved.ResidentTrips[0].HomeAt);
                store.Restore(saved);Check.Near(3.1,store.ResidentTripAt(id,145).Position.X);
                saved.Version=16;Check.Throws<ArgumentException>(()=>store.Restore(saved));
                Check.Near(282,store.Snapshot().ResidentTrips[0].HomeAt);
                var legacy=new WorldStateStore(12);id=legacy.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                legacy.ScheduleResidentTrip(Trip(id));saved=legacy.Snapshot();saved.Version=16;
                legacy.Restore(saved);Check.Near(302,legacy.Snapshot().ResidentTrips[0].HomeAt);
                trip=Trip(id);trip.Destination.ThroughRoute=true;
                Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(trip));
            }));
            cases.Add(("Rejected historical refusal cannot leave a partial failed admission",()=>{
                var store=new WorldStateStore(12);string id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                foreach(int town in new[]{200,300})
                {store.GetOrCreate(town,new[]{2},1);store.RegisterLodging(town,7,new[]{new LodgingRoom{Id="room",Beds=new List<string>{"bed"+town}}});}
                store.ScheduleResidentTrip(Trip(id));
                store.SaveResidentInnReception(id,new ResidentInnReception {VisitId="outbound/0/0",SettlementId=300,BuildingKey=7,
                    VisitEnterAt=122,VisitLeaveAt=142,StartedAt=122,SecondsPerMetre=1,ServiceSeconds=2,
                    Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}}});
                Check.True(store.TryAdmitResidentInn(id,177));
                Check.True(store.TryBookLodging(300,7,"player-room","player",300,true,1,120,200));
                Check.Throws<ArgumentException>(()=>store.TryAdmitResidentInn(id,125));
                Check.Equal(1,store.Snapshot().ResidentTrips[0].InnStays.Count);
                Check.Equal(0,store.Snapshot().ResidentTrips[0].ServiceExits.Count);
                Check.Equal("bed200",store.ResidentInnBed(id,178));store.Restore(store.Snapshot());
            }));
            cases.Add(("Outbound destination and return interruptions compose without losing later reception identity",()=>{
                var store=new WorldStateStore(12);string id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                store.ScheduleResidentTrip(Trip(id));
                store.SaveResidentInnReception(id,new ResidentInnReception {VisitId="return/0/0",SettlementId=400,BuildingKey=7,
                    VisitEnterAt=240,VisitLeaveAt=270,StartedAt=240,SecondsPerMetre=1,ServiceSeconds=2,
                    Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}}});
                store.SaveResidentServiceExit(id,new ResidentServiceExit {VisitId="outbound/0/0",SettlementId=300,BuildingKey=7,
                    VisitEnterAt=122,VisitLeaveAt=142,StartedAt=125,SecondsPerMetre=1,
                    Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=3}}});
                store.SaveResidentServiceExit(id,new ResidentServiceExit {VisitId="destination/0",SettlementId=200,BuildingKey=7,
                    VisitEnterAt=162,VisitLeaveAt=202,StartedAt=170,SecondsPerMetre=1,
                    Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}}});
                Check.Near(195,store.Snapshot().ResidentTrips[0].InnReceptions[0].VisitEnterAt);
                store.SaveResidentServiceExit(id,new ResidentServiceExit {VisitId="return/0/0",SettlementId=400,BuildingKey=7,
                    VisitEnterAt=195,VisitLeaveAt=225,StartedAt=198,SecondsPerMetre=1,
                    Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}}});
                store.Restore(store.Snapshot());Check.Near(231,store.Snapshot().ResidentTrips[0].HomeAt);
                Check.Equal(ResidentVisitPhase.ExitingService,store.ResidentTripAt(id,198.5).VisitPhase);
                Check.Equal(3,store.Snapshot().ResidentTrips[0].ServiceExits.Count);
            }));
            cases.Add(("Full intermediate inn sends guest through the counter exit and retimes later keeper appointments",()=>{
                var store=new WorldStateStore(12);string id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                store.GetOrCreate(300,new[]{2},1);store.RegisterLodging(300,7,new[]{new LodgingRoom{Id="room",Beds=new List<string>{"bed"}}});
                Check.True(store.TryBookLodging(300,7,"player-room","player",300,true,1,120,200));
                store.ScheduleResidentTrip(Trip(id));
                foreach(double entered in new[]{122d,176d,240d})
                {
                    var visit=store.ResidentTripAt(id,entered);
                    store.SaveResidentInnReception(id,new ResidentInnReception {VisitId=visit.VisitId,SettlementId=visit.SettlementId,
                        BuildingKey=7,VisitEnterAt=entered,VisitLeaveAt=visit.VisitLeaveAt,StartedAt=entered,
                        SecondsPerMetre=1,ServiceSeconds=2,Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}}});
                }
                Check.True(!store.TryAdmitResidentInn(id,125));
                Check.Equal(ResidentVisitPhase.ExitingService,store.ResidentTripAt(id,125.5).VisitPhase);
                Check.Near(.5,store.ResidentTripAt(id,125.5).Position.X);
                Check.Near(160,store.Snapshot().ResidentTrips[0].InnReceptions[1].VisitEnterAt);
                Check.Near(224,store.Snapshot().ResidentTrips[0].InnReceptions[2].VisitEnterAt);
                store.Restore(store.Snapshot());Check.Equal(1,store.Snapshot().ResidentTrips[0].ServiceExits.Count);
                Check.Equal(1,store.Snapshot().Lodgings[0].Bookings.Count);
                Check.Equal(TravelerAdmission.Refused,store.ResidentInnAdmission(id,125.5));
            }));
            cases.Add(("Saved intermediate exit retains interior position then resumes the original journey",()=>{
                var store=new WorldStateStore(12);string id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                store.ScheduleResidentTrip(Trip(id));
                store.SaveResidentServiceExit(id,new ResidentServiceExit {VisitId="outbound/0/0",SettlementId=300,BuildingKey=7,
                    VisitEnterAt=122,VisitLeaveAt=142,StartedAt=125,SecondsPerMetre=1,
                    Route=new List<GuardPoint3>{new GuardPoint3 {X=5,Y=2},new GuardPoint3 {X=8,Y=2}}});
                var snapshot=store.ResidentTripAt(id,126);Check.Equal(ResidentVisitPhase.ExitingService,snapshot.VisitPhase);
                Check.Near(6,snapshot.Position.X);Check.Near(2,snapshot.InteriorY);
                Check.Equal(300,snapshot.SettlementId);Check.Equal(0,store.RoadResidents(126).Count);
                Check.Equal(ResidentTripPhase.Outbound,store.ResidentTripAt(id,130).Phase);
                Check.Near(160,store.Snapshot().ResidentTrips[0].ArrivesAt);Check.Near(288,store.Snapshot().ResidentTrips[0].HomeAt);
                store.Restore(store.Snapshot());Check.Near(6,store.ResidentTripAt(id,126).Position.X);
                var bad=store.Snapshot();bad.ResidentTrips[0].ServiceExits[0].SettlementId=400;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));Check.Near(288,store.Snapshot().ResidentTrips[0].HomeAt);
            }));
            cases.Add(("Intermediate room capacity is first come and never borrowed from another town",()=>{
                var store=new WorldStateStore(12);var town=store.GetOrCreate(100,new[]{1,2},1);
                string first=town.Households[0].Residents[0].Id,second=town.Households[1].Residents[0].Id;
                foreach(int place in new[]{300,400})
                {
                    store.GetOrCreate(place,new[]{3},1);
                    store.RegisterLodging(place,7,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"+place}}});
                }
                store.ScheduleResidentTrip(Trip(first));store.ScheduleResidentTrip(Trip(second));
                Check.Equal(0,store.Snapshot().Lodgings[0].Bookings.Count);
                Check.True(store.TryAdmitResidentInn(first,123));Check.True(!store.TryAdmitResidentInn(second,124));
                Check.Equal(TravelerAdmission.Refused,store.ResidentInnAdmission(second,124));
                Check.Equal(0,store.Snapshot().Lodgings.Find(l=>l.SettlementId==400).Bookings.Count);
                store.Restore(store.Snapshot());Check.Equal("bed300",store.ResidentInnBed(first,125));
                Check.Equal((string)null,store.ResidentInnBed(second,125));
            }));
            cases.Add(("Road inns pay only at arrival and retain separate beds across native building key collisions",()=>{
                var store=new WorldStateStore(12);string id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                foreach(int town in new[]{200,300,400})
                {
                    store.GetOrCreate(town,new[]{2},1);
                    store.RegisterLodging(town,7,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"+town}}});
                }
                store.ScheduleResidentTrip(Trip(id));Check.True(!store.TryAdmitResidentInn(id,121));
                foreach(double now in new[]{123d,177d,241d})
                {
                    int town=store.ResidentTripAt(id,now).SettlementId;
                    Check.True(store.HasDueResidentInnAdmission(town,7,now));
                    Check.Equal(1,store.AdvanceResidentInnAdmissions(town,now,1,7));
                    Check.Equal("bed"+town,store.ResidentInnBed(id,now));
                    Check.True(store.TryAdmitResidentInn(id,now));
                    Check.Equal(1,store.Snapshot().Lodgings.Find(l=>l.SettlementId==town).Bookings.Count);
                    var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());store=restored;
                    Check.Equal("bed"+town,store.ResidentInnBed(id,now));
                }
                Check.Equal(3,store.Snapshot().ResidentTrips[0].InnStays.Count);
                var corrupt=store.Snapshot();corrupt.ResidentTrips[0].InnStays[0].SettlementId=400;
                Check.Throws<ArgumentException>(()=>store.Restore(corrupt));
                Check.Equal("bed400",store.ResidentInnBed(id,241));
            }));
            cases.Add(("Intermediate keeper approaches bind equal building keys to their own stop and settlement",()=>{
                var store=new WorldStateStore(12);string id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                store.ScheduleResidentTrip(Trip(id));
                foreach(double entered in new[]{122d,176d,240d})
                {
                    var visit=store.ResidentTripAt(id,entered);
                    store.SaveResidentInnReception(id,new ResidentInnReception {VisitId=visit.VisitId,SettlementId=visit.SettlementId,
                        BuildingKey=7,VisitEnterAt=entered,VisitLeaveAt=visit.VisitLeaveAt,StartedAt=entered,
                        SecondsPerMetre=1,ServiceSeconds=2,Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}}});
                }
                var saved=store.Snapshot();Check.Equal(3,saved.ResidentTrips[0].InnReceptions.Count);
                var restored=new WorldStateStore(1);restored.Restore(saved);
                foreach(double entered in new[]{122d,176d,240d})
                {
                    var reception=restored.ResidentInnReceptionAt(id,entered+1);
                    Check.Equal(restored.ResidentTripAt(id,entered).VisitId,reception.VisitId);
                    Check.Equal((int?)restored.ResidentTripAt(id,entered).SettlementId,reception.SettlementId);
                    reception.SettlementId=999;
                    Check.Throws<ArgumentException>(()=>restored.SaveResidentInnReception(id,reception));
                }
                saved.ResidentTrips[0].InnReceptions[0].VisitId="destination/0";
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
                Check.Equal("outbound/0/0",restored.ResidentInnReceptionAt(id,123).VisitId);
                saved=store.Snapshot();saved.ResidentTrips[0].InnReceptions[2].SettlementId=300;
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
                Check.Equal((int?)400,restored.ResidentInnReceptionAt(id,241).SettlementId);
                saved=store.Snapshot();
                foreach(var reception in saved.ResidentTrips[0].InnReceptions) {reception.VisitId=null;reception.SettlementId=null;}
                restored.Restore(saved);
                Check.Equal("return/0/0",restored.ResidentInnReceptionAt(id,241).VisitId);
            }));
            cases.Add(("Historical discovery never invents intermediate inn admissions",()=>{
                var store=new WorldStateStore(12);
                var person=store.ReserveHousehold(100,new HomeProfile(10,1,100)).Residents[0];
                var trip=Trip(person.Id);
                Check.True(!store.DiscoverOutboundResidentTrip(trip,115));
                Check.Equal(0,store.Snapshot().ResidentTrips.Count);
                Check.True(!store.IsResidentAway(person.Id,115));
                trip.OutboundStops.Clear();trip.ReturnStops.Clear();
                Check.True(store.DiscoverOutboundResidentTrip(trip,115));
            }));
            cases.Add(("Saved road stops extend one original resident journey through both directions",()=>{
                var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0];
                foreach(int town in new[]{200,300,400}) store.GetOrCreate(town,new[]{2},1);
                store.ScheduleResidentTrip(Trip(person.Id));var saved=store.Snapshot();var trip=saved.ResidentTrips[0];
                Check.Equal(new WorldState().Version,saved.Version);Check.Near(174,trip.ArrivesAt);Check.Near(218,trip.ReturnsFromVisitAt);
                Check.Near(292,trip.RoadReturnsAt);Check.Near(302,trip.HomeAt);
                Check.Equal("outbound/0",trip.OutboundStops[0].Id);Check.Equal("return/0",trip.ReturnStops[0].Id);
                var outbound=store.ResidentTripAt(person.Id,122);Check.Equal(300,outbound.SettlementId);
                Check.Equal("outbound/0/0",outbound.VisitId);Check.Equal(7,outbound.BuildingKey);Check.Near(1002,outbound.Position.X);
                Check.Equal(0,store.RoadResidents(122).Count);Check.Equal(person.Id,store.DestinationResidents(300,122)[0].Id);
                ActivitySnapshot activity;Check.True(store.TryDestinationResidentState(person.Id,300,122,out activity));
                Check.Equal(7,activity.BuildingKey);Check.Equal(0,store.DestinationResidents(200,122).Count);
                Check.True(store.IsResidentAway(person.Id,122));Check.True(store.IsResidentAway(person.Id,240));
                Check.Equal("return/0/0",store.ResidentTripAt(person.Id,240).VisitId);Check.Equal(400,store.ResidentTripAt(person.Id,240).SettlementId);
                Check.Equal(1,store.RoadResidents(146).Count);Check.Near(1.2,store.ResidentTripAt(person.Id,146).Position.X);
                Check.True(!store.IsResidentAway(person.Id,302));
                var restored=new WorldStateStore(1);restored.Restore(saved);
                foreach(double now in new[]{100d,110,120,122,144,146,174,176,218,238,240,272,292,302})
                {var a=store.ResidentTripAt(person.Id,now);var b=restored.ResidentTripAt(person.Id,now);Check.Equal(a.Phase,b.Phase);Check.Equal(a.VisitId,b.VisitId);Check.Near(a.Position.X,b.Position.X);}
                saved.ResidentTrips[0].OutboundStops[0].Visit.Stops[0].DwellSeconds=999;
                Check.Near(302,restored.Snapshot().ResidentTrips[0].HomeAt);
            }));
            cases.Add(("Legacy road trips retain their original uninterrupted timing",()=>{
                var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0];
                var trip=Trip(person.Id);trip.OutboundStops=null;trip.ReturnStops=null;store.ScheduleResidentTrip(trip);
                var saved=store.Snapshot();saved.Version=15;saved.ResidentTrips[0].OutboundStops=null;saved.ResidentTrips[0].ReturnStops=null;
                store.Restore(saved);trip=store.Snapshot().ResidentTrips[0];Check.Near(150,trip.ArrivesAt);Check.Near(244,trip.HomeAt);
                Check.Equal(0,trip.OutboundStops.Count);Check.Equal(0,trip.ReturnStops.Count);Check.Equal(new WorldState().Version,store.Snapshot().Version);
            }));
            cases.Add(("Road stop IDs, ordering and endpoints are validated without partial replacement",()=>{
                var source=Trip("person");source.OutboundStops[0].Id="return/0";
                Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(source));
                source=Trip("person");source.OutboundStops[0].RoadNode=4;
                Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(source));
                source=Trip("person");source.OutboundStops[0].SettlementId=100;
                Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(source));
                source=Trip("person");source.OutboundStops.Add(new ResidentRoadStop {Id="outbound/0",RoadNode=2,SettlementId=500,Visit=Inn(20)});
                Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(source));
                var store=new WorldStateStore(12);string id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                store.ScheduleResidentTrip(Trip(id));var saved=store.Snapshot();saved.Version=15;
                Check.Throws<ArgumentException>(()=>store.Restore(saved));Check.Near(302,store.Snapshot().ResidentTrips[0].HomeAt);
            }));
        }
    }
}
