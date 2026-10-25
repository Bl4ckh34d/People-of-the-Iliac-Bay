using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class ResidentVisitIdentityTests
    {
        private static List<SavedRoutePoint> Path(double a,double b) {return new List<SavedRoutePoint> {new SavedRoutePoint {X=a},new SavedRoutePoint {X=b}};}
        private static ResidentInnReception Reception() {return new ResidentInnReception {BuildingKey=10,VisitEnterAt=70,VisitLeaveAt=190,
            StartedAt=70,SecondsPerMetre=1,ServiceSeconds=5,Route=new List<GuardPoint3> {new GuardPoint3(),new GuardPoint3 {X=5}}};}
        private static ResidentServiceExit Exit() {return new ResidentServiceExit {BuildingKey=10,VisitEnterAt=70,VisitLeaveAt=190,
            StartedAt=100,SecondsPerMetre=1,Route=new List<GuardPoint3> {new GuardPoint3 {X=5},new GuardPoint3()}};}
        private static WorldStateStore Fixture(out string id)
        {
            var store=new WorldStateStore(12);id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
            foreach(int town in new[]{200,300})
            {store.GetOrCreate(town,new[]{2},1);store.RegisterLodging(town,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"+town}}});}
            var trip=ResidentTrip.Create(id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
            trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(10,0),Stops=new List<ResidentVisitStop> {
                new ResidentVisitStop {BuildingKey=10,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=120,Route=Path(0,10)}}};
            store.ScheduleResidentTrip(trip);store.SaveResidentInnReception(id,Reception());
            Check.True(store.TryAdmitResidentInn(id,80));store.SaveResidentServiceExit(id,Exit());return store;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Legacy visit IDs bind to itinerary entries without changing paid booking IDs",()=>{
                string id;var store=Fixture(out id);var saved=store.Snapshot();var trip=saved.ResidentTrips[0];
                var booking=trip.InnStays[0].BookingId;
                trip.InnStays[0].VisitId=null;trip.InnReceptions[0].VisitId=null;trip.ServiceExits[0].VisitId=null;
                store.Restore(saved);trip=store.Snapshot().ResidentTrips[0];
                Check.Equal("destination/0",trip.InnStays[0].VisitId);Check.Equal("destination/0",trip.InnReceptions[0].VisitId);
                Check.Equal("destination/0",trip.ServiceExits[0].VisitId);Check.Equal(booking,trip.InnStays[0].BookingId);
                Check.Equal("destination/0",store.ResidentTripAt(id,85).VisitId);
            }));
            cases.Add(("Conflicting visit IDs are rejected before restore or duplicate writes",()=>{
                string id;var store=Fixture(out id);
                for(int field=0;field<3;field++)
                {
                    var saved=store.Snapshot();var trip=saved.ResidentTrips[0];
                    if(field==0) trip.InnStays[0].VisitId="destination/1";
                    if(field==1) trip.InnReceptions[0].VisitId="destination/1";
                    if(field==2) trip.ServiceExits[0].VisitId="destination/1";
                    Check.Throws<ArgumentException>(()=>store.Restore(saved));Check.Equal("bed200",store.ResidentInnBed(id,85));
                }
                var reception=Reception();reception.VisitId="destination/1";
                Check.Throws<ArgumentException>(()=>store.SaveResidentInnReception(id,reception));
                var exit=Exit();exit.VisitId="destination/1";
                Check.Throws<ArgumentException>(()=>store.SaveResidentServiceExit(id,exit));
            }));
            cases.Add(("Planned later visit keeps its ID when earlier service exit changes timing",()=>{
                var store=new WorldStateStore(12);string id=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0].Id;
                store.GetOrCreate(200,new[]{2},1);
                var trip=ResidentTrip.Create(id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(10,0),Stops=new List<ResidentVisitStop> {
                    new ResidentVisitStop {BuildingKey=10,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=120,Route=Path(0,10)},
                    new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Shop,WalkSeconds=60,DwellSeconds=120,Route=Path(10,20)},
                    new ResidentVisitStop {BuildingKey=10,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=120,Route=Path(20,10)}}};
                store.ScheduleResidentTrip(trip);store.SaveResidentInnReception(id,Reception());
                var later=Reception();later.VisitEnterAt=430;later.VisitLeaveAt=550;later.StartedAt=430;
                store.SaveResidentInnReception(id,later);Check.Equal("destination/2",store.Snapshot().ResidentTrips[0].InnReceptions[1].VisitId);
                store.SaveResidentServiceExit(id,Exit());var saved=store.Snapshot();
                var changed=saved.ResidentTrips[0].InnReceptions[1];Check.Near(345,changed.VisitEnterAt);
                Check.Equal("destination/2",changed.VisitId);store.Restore(saved);
                Check.Equal("destination/2",store.ResidentInnReceptionAt(id,350).VisitId);
            }));
            cases.Add(("Legacy visit records infer destination without moving a paid booking to equal foreign building key",()=>{
                string id;var store=Fixture(out id);var saved=store.Snapshot();var trip=saved.ResidentTrips[0];
                string booking=trip.InnStays[0].BookingId;
                trip.InnStays[0].SettlementId=null;trip.InnReceptions[0].SettlementId=null;trip.ServiceExits[0].SettlementId=null;
                var restored=new WorldStateStore(1);restored.Restore(saved);var copy=restored.Snapshot().ResidentTrips[0];
                Check.Equal((int?)200,copy.InnStays[0].SettlementId);Check.Equal((int?)200,copy.InnReceptions[0].SettlementId);
                Check.Equal((int?)200,copy.ServiceExits[0].SettlementId);Check.Equal(booking,copy.InnStays[0].BookingId);
                Check.Equal("bed200",restored.ResidentInnBed(id,85));
                Check.Equal(0,restored.Snapshot().Lodgings.Find(l=>l.SettlementId==300).Bookings.Count);
                Check.Equal((int?)200,restored.ResidentInnReceptionAt(id,85).SettlementId);
            }));
            cases.Add(("Restore rejects foreign settlement on every visit record atomically",()=>{
                string id;var store=Fixture(out id);
                for(int field=0;field<3;field++)
                {
                    var saved=store.Snapshot();var trip=saved.ResidentTrips[0];
                    if(field==0) trip.InnStays[0].SettlementId=300;
                    if(field==1) trip.InnReceptions[0].SettlementId=300;
                    if(field==2) trip.ServiceExits[0].SettlementId=300;
                    Check.Throws<ArgumentException>(()=>store.Restore(saved));
                    Check.Equal("bed200",store.ResidentInnBed(id,85));
                    Check.Equal((int?)200,store.Snapshot().ResidentTrips[0].InnStays[0].SettlementId);
                }
            }));
            cases.Add(("Duplicate visit writes cannot bypass settlement validation",()=>{
                string id;var store=Fixture(out id);var reception=Reception();reception.SettlementId=300;
                Check.Throws<ArgumentException>(()=>store.SaveResidentInnReception(id,reception));
                var exit=Exit();exit.SettlementId=300;
                Check.Throws<ArgumentException>(()=>store.SaveResidentServiceExit(id,exit));
                reception.SettlementId=200;store.SaveResidentInnReception(id,reception);
                exit.SettlementId=200;store.SaveResidentServiceExit(id,exit);
                var saved=store.Snapshot().ResidentTrips[0];Check.Equal(1,saved.InnReceptions.Count);Check.Equal(1,saved.ServiceExits.Count);
            }));
        }
    }
}
