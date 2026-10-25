using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class ResidentVisitTests
    {
        private static List<SavedRoutePoint> Path(double a,double b)
        { return new List<SavedRoutePoint>{new SavedRoutePoint{X=a,Z=0},new SavedRoutePoint{X=b,Z=0}}; }
        private static ResidentDestination Plan()
        {
            return new ResidentDestination { Stops=new List<ResidentVisitStop> {
                new ResidentVisitStop {BuildingKey=10,Purpose=ResidentVisitPurpose.Shop,OpenMinute=480,CloseMinute=1080,
                    WalkSeconds=60,DwellSeconds=120,Route=Path(0,10)},
                new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Guild,OpenMinute=540,CloseMinute=1020,
                    WalkSeconds=60,DwellSeconds=180,Route=Path(10,20)} }, ExitRoute=Path(20,0),ExitWalkSeconds=120 };
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Day visit promotes an overnight opening wait without changing short waits or requested long stays",()=>{
                var trip=ResidentTrip.Create("resident",100,200,20*3600,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(20,0),Stops=new List<ResidentVisitStop>{
                    new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Shop,OpenMinute=540,CloseMinute=1020,
                        WalkSeconds=60,DwellSeconds=120,Route=Path(0,20)}}};
                Check.Equal(2,ResidentOvernightPlan.RequiredVisitDays(trip,1));
                for(int days=2;days<=4;days++) Check.Equal(days,ResidentOvernightPlan.RequiredVisitDays(trip,days));
                var shortWait=ResidentTrip.CopyValidated(trip);shortWait.DepartsAt=8*3600+45*60;
                Check.Equal(1,ResidentOvernightPlan.RequiredVisitDays(shortWait,1));
                var walled=ResidentTrip.CopyValidated(trip);walled.Destination.Walled=true;
                walled.Destination.EntryGateSeconds=20;walled.Destination.ExitGateSeconds=40;
                Check.Equal(1,ResidentOvernightPlan.RequiredVisitDays(walled,1));
                Check.Throws<ArgumentException>(()=>ResidentOvernightPlan.RequiredVisitDays(trip,0));
                Check.Throws<ArgumentException>(()=>ResidentOvernightPlan.RequiredVisitDays(trip,5));
            }));
            cases.Add(("Late service arrivals lodge first while short opening and closed-gate waits remain intact",()=>{
                var trip=ResidentTrip.Create("resident",100,200,20*3600,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(20,0),Stops=new List<ResidentVisitStop>{
                    new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Shop,OpenMinute=540,CloseMinute=1020,
                        WalkSeconds=60,DwellSeconds=120,Route=Path(0,20)}}};
                Check.True(ResidentOvernightPlan.NeedsLodgingFirst(trip));
                var route=new[]{new RoutePoint(20,0),new RoutePoint(30,0)};
                var lodging=ResidentOvernightPlan.Schedule(ResidentOvernightPlan.PrependInn(trip,30,route,2),123);
                Check.Equal(2,lodging.Destination.Stops.Count);Check.Near(80,lodging.Destination.Stops[0].WalkSeconds);
                Check.Equal(30,lodging.Evaluate(21*3600).BuildingKey);
                var visits=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(lodging.ArrivesAt,lodging.Destination).ScheduledVisits());
                Check.Equal(20,visits[1].BuildingKey);Check.True(visits[1].EnterAt>=86400+9*3600 && visits[1].LeaveAt<=86400+17*3600);
                Check.Equal(1,trip.Destination.Stops.Count);Check.Near(60,lodging.Destination.ExitWalkSeconds);
                var early=ResidentTrip.CopyValidated(trip);early.DepartsAt=8*3600+45*60;
                Check.True(!ResidentOvernightPlan.NeedsLodgingFirst(early));
                var walled=ResidentTrip.CopyValidated(trip);walled.Destination.Walled=true;walled.Destination.EntryGateSeconds=20;walled.Destination.ExitGateSeconds=40;
                Check.True(!ResidentOvernightPlan.NeedsLodgingFirst(walled));
                Check.Equal(ResidentVisitPhase.WaitingAtGate,ResidentTrip.CopyValidated(walled).Evaluate(21*3600).VisitPhase);
            }));
            cases.Add(("Social destination inn can host several nights without an invented walking leg",()=>{
                var trip=ResidentTrip.Create("resident",100,200,12*3600,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(20,0),Stops=new List<ResidentVisitStop>{
                    new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Social,WalkSeconds=60,DwellSeconds=120,Route=Path(0,20)}}};
                var shortStay=ResidentOvernightPlan.StayAtInn(trip,2,123);
                var longStay=ResidentOvernightPlan.StayAtInn(trip,4,123);
                Check.Equal(ResidentVisitPurpose.Inn,shortStay.Destination.Stops[0].Purpose);
                Check.Equal(1,longStay.Destination.Stops.Count);Check.Equal(2,longStay.Destination.Stops[0].Route.Count);
                Check.Near(60,longStay.Destination.Stops[0].WalkSeconds);Check.Near(60,longStay.Destination.ExitWalkSeconds);
                Check.Near(2*86400,longStay.HomeAt-shortStay.HomeAt);
                Check.Equal(ResidentVisitPurpose.Social,trip.Destination.Stops[0].Purpose);
                var visit=longStay.Evaluate(2*86400);Check.Equal(ResidentVisitPurpose.Inn,visit.VisitPurpose);
                Check.True(visit.VisitLeaveAt%86400>=9*3600 && visit.VisitLeaveAt%86400<11*3600);
                var restored=ResidentTrip.CopyValidated(longStay);Check.Near(longStay.HomeAt,restored.HomeAt);
                Check.Throws<ArgumentException>(()=>ResidentOvernightPlan.StayAtInn(trip,5,123));
            }));
            cases.Add(("Dead or alarmed innkeeper cannot take traveler payment or respawn on registration",()=>{
                foreach(bool dead in new[]{true,false})
                {
                    var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                    var town=store.GetOrCreate(200,new[]{30},1);store.RegisterLodging(200,20,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"}}});
                    var keeper=store.RegisterNativePerson(200,20,1,"Keeper Kinghart",ResidentSex.Female,1,41,182,1);
                    var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                    trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(20,0),Stops=new List<ResidentVisitStop>{
                        new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=120,Route=Path(0,20)}}};
                    trip=ResidentOvernightPlan.WithReceptions(trip,20,new List<GuardPoint3>{new GuardPoint3 {Y=3},new GuardPoint3 {X=4}},2,5);
                    trip.InnReceptions[0].KeeperId=keeper.Id;store.ScheduleResidentTrip(trip);
                    if(dead) keeper.IsAlive=false;
                    else {var alarm=store.RecordViolence(200,town.Households[0].Residents[0].Id,80);store.Witness(alarm.Id,keeper.Id,80);}
                    var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                    Check.True(!restored.TryAdmitResidentInn(person.Id,86));
                    var saved=restored.Snapshot();Check.Equal(0,saved.Lodgings[0].Bookings.Count);Check.Equal(1,saved.ResidentTrips[0].ServiceExits.Count);
                    double rounding=dead?.0001:-.0001;
                    saved.ResidentTrips[0].ServiceExits[0].VisitEnterAt+=rounding;
                    saved.ResidentTrips[0].ServiceExits[0].VisitLeaveAt+=rounding;
                    restored.Restore(saved);
                    var corrupt=restored.Snapshot();corrupt.ResidentTrips[0].ServiceExits[0].VisitEnterAt-=1;
                    Check.Throws<ArgumentException>(()=>new WorldStateStore(1).Restore(corrupt));
                    corrupt=restored.Snapshot();corrupt.ResidentTrips[0].InnReceptions[0].KeeperId="missing-keeper";
                    Check.Throws<ArgumentException>(()=>new WorldStateStore(1).Restore(corrupt));
                    var same=restored.RegisterNativePerson(200,20,1,"Replacement Name",ResidentSex.Female,1,41,182,1);
                    Check.Equal(!dead,same.IsAlive);Check.Equal("Keeper Kinghart",same.Name);Check.Equal(1,restored.NativePeopleFor(200).Count);
                }
            }));
            cases.Add(("Interrupted services retime future receptions and room holds without changing paid bookings",()=>{
                foreach(bool paidFirst in new[]{false,true})
                {
                    var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                    store.GetOrCreate(200,new[]{30},1);store.RegisterLodging(200,20,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"}}});
                    var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                    trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(20,0),Stops=new List<ResidentVisitStop>()};
                    if(paidFirst) trip.Destination.Stops.Add(new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Inn,
                        WalkSeconds=60,DwellSeconds=120,KeepRoomUntil=550,Route=Path(0,20)});
                    trip.Destination.Stops.Add(new ResidentVisitStop {BuildingKey=30,Purpose=ResidentVisitPurpose.Shop,
                        WalkSeconds=60,DwellSeconds=120,Route=Path(paidFirst?20:0,30)});
                    trip.Destination.Stops.Add(new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Inn,
                        WalkSeconds=60,DwellSeconds=120,KeepRoomUntil=paidFirst?0:370,Route=Path(30,20)});
                    trip=ResidentOvernightPlan.WithReceptions(trip,20,new List<GuardPoint3>{new GuardPoint3 {Y=3},new GuardPoint3 {X=4}},2,5);
                    store.ScheduleResidentTrip(trip);
                    if(paidFirst) Check.True(store.TryAdmitResidentInn(person.Id,86));
                    double enter=paidFirst?250:70;
                    store.SaveResidentServiceExit(person.Id,new ResidentServiceExit {BuildingKey=30,VisitEnterAt=enter,VisitLeaveAt=enter+120,
                        StartedAt=enter+10,SecondsPerMetre=2,Route=new List<GuardPoint3>{new GuardPoint3 {Y=3},new GuardPoint3 {X=4}}});
                    var saved=store.Snapshot();var changed=saved.ResidentTrips[0];
                    Check.Near(enter+80,changed.InnReceptions[changed.InnReceptions.Count-1].VisitEnterAt);
                    Check.Near(paidFirst?450:270,changed.Destination.Stops[paidFirst?0:1].KeepRoomUntil);
                    var restored=new WorldStateStore(1);restored.Restore(saved);
                    Check.True(!restored.HasDueResidentInnAdmission(200,20,enter+90));
                    if(paidFirst)
                    {
                        Check.Near(550,saved.Lodgings[0].Bookings[0].EndsAt);Check.Near(70,changed.InnReceptions[0].VisitEnterAt);
                        Check.Equal("bed",restored.ResidentInnBed(person.Id,500));
                    }
                    else Check.True(restored.TryAdmitResidentInn(person.Id,enter+100));
                }
            }));
            cases.Add(("Planned reception tolerates bounded timestamp rounding and still refuses a full inn",()=>{
                foreach(double rounding in new[]{-.0001,.0001})
                {
                var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);store.RegisterLodging(200,20,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"}}});
                store.TryBookLodging(200,20,"full","player",200,true,1,0,3000);
                var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(20,0),Stops=new List<ResidentVisitStop>{
                    new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=120,Route=Path(0,20)}}};
                trip=ResidentOvernightPlan.WithReceptions(trip,20,new List<GuardPoint3>{new GuardPoint3 {Y=3},new GuardPoint3 {X=4}},2,5);
                trip.InnReceptions[0].VisitEnterAt+=rounding;trip.InnReceptions[0].VisitLeaveAt+=rounding;trip.InnReceptions[0].StartedAt+=rounding;
                store.ScheduleResidentTrip(trip);
                Check.True(store.ResidentInnReceptionAt(person.Id,71)!=null);
                Check.True(!store.HasDueResidentInnAdmission(200,20,80));
                store.AdvanceInnAdmissions(200,1000,8,20);
                Check.Equal(1,store.Snapshot().ResidentTrips[0].ServiceExits.Count);
                Check.Equal(1,store.Snapshot().ResidentTrips[0].InnReceptions.Count);
                var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                var paidStore=new WorldStateStore(12);paidStore.GetOrCreate(100,new[]{10},1);paidStore.GetOrCreate(200,new[]{30},1);
                paidStore.RegisterLodging(200,20,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"}}});
                paidStore.ScheduleResidentTrip(trip);Check.True(paidStore.TryAdmitResidentInn(person.Id,86));
                var paidSave=paidStore.Snapshot();paidSave.ResidentTrips[0].InnStays[0].EnterAt+=rounding;
                restored.Restore(paidSave);
                Check.Equal(TravelerAdmission.Admitted,restored.ResidentInnAdmission(person.Id,87));
                Check.True(restored.TryAdmitResidentInn(person.Id,87));Check.Equal(1,restored.Snapshot().Lodgings[0].Bookings.Count);
                trip.InnReceptions[0].VisitEnterAt-=1;trip.InnReceptions[0].StartedAt-=1;
                Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(trip));
                }
            }));
            cases.Add(("Offscreen refused reception exits along saved route and cancels queued later nights",()=>{
                var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);store.RegisterLodging(200,20,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"}}});
                store.TryBookLodging(200,20,"full","player",200,true,1,0,3000);
                var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(20,0),Stops=new List<ResidentVisitStop>{
                    new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=120,Route=Path(0,20)},
                    new ResidentVisitStop {BuildingKey=30,Purpose=ResidentVisitPurpose.Shop,WalkSeconds=60,DwellSeconds=120,Route=Path(20,30)},
                    new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=120,Route=Path(30,20)}}};
                var prepared=ResidentOvernightPlan.WithReceptions(trip,20,new List<GuardPoint3>{new GuardPoint3 {Y=3},new GuardPoint3 {X=4}},2,5);
                Check.Equal(0,trip.InnReceptions.Count);Check.Equal(2,prepared.InnReceptions.Count);
                Check.Near(70,prepared.InnReceptions[0].StartedAt);Check.Near(430,prepared.InnReceptions[1].StartedAt);
                trip=prepared;
                store.ScheduleResidentTrip(trip);
                store.SaveResidentInnReception(person.Id,new ResidentInnReception {BuildingKey=20,VisitEnterAt=70,VisitLeaveAt=190,
                    StartedAt=70,SecondsPerMetre=2,ServiceSeconds=5,Route=new List<GuardPoint3>{new GuardPoint3 {Y=3},new GuardPoint3 {X=4}}});
                store.AdvanceInnAdmissions(200,1000,8,20);
                var saved=store.Snapshot();var result=saved.ResidentTrips[0];
                Check.Equal(1,result.Destination.Stops.Count);Check.Equal(1,result.InnStays.Count);Check.Equal(1,result.ServiceExits.Count);
                Check.Equal(1,result.InnReceptions.Count);
                Check.Near(85,result.ServiceExits[0].StartedAt);Check.Near(4,result.ServiceExits[0].Route[0].X);
                Check.Near(3,result.ServiceExits[0].Route[1].Y);
                var restored=new WorldStateStore(1);restored.Restore(saved);
                Check.Equal(ResidentVisitPhase.ExitingService,restored.ResidentTripAt(person.Id,90).VisitPhase);
                Check.Equal(ResidentVisitPhase.Leaving,restored.ResidentTripAt(person.Id,96).VisitPhase);
                Check.True(!restored.IsResidentAway(person.Id,166));Check.True(!restored.HasDueResidentInnAdmission(200,20,2000));
            }));
            cases.Add(("Refused overnight guest physically exits then returns home without later appointments",()=>{
                var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);store.RegisterLodging(200,20,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed"}}});
                store.TryBookLodging(200,20,"full","player",200,true,1,0,1000);
                var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                var plan=Plan();foreach(var stop in plan.Stops) {stop.OpenMinute=stop.CloseMinute=0;stop.DwellSeconds=120;}
                plan.Stops[1].Purpose=ResidentVisitPurpose.Inn;plan.Stops[1].KeepRoomUntil=550;
                plan.Stops.Add(new ResidentVisitStop {BuildingKey=30,Purpose=ResidentVisitPurpose.Shop,WalkSeconds=60,DwellSeconds=120,Route=Path(20,30)});
                plan.ExitRoute=Path(30,0);trip.Destination=plan;store.ScheduleResidentTrip(trip);
                Check.True(!store.TryAdmitResidentInn(person.Id,251));
                var exit=new ResidentServiceExit {BuildingKey=20,VisitEnterAt=250,VisitLeaveAt=370,StartedAt=252,SecondsPerMetre=2,
                    Route=new List<GuardPoint3>{new GuardPoint3 {Y=3},new GuardPoint3 {X=4}}};
                store.SaveResidentServiceExit(person.Id,exit);
                var changed=store.Snapshot().ResidentTrips[0];
                Check.Equal(2,changed.Destination.Stops.Count);
                Check.Near(20,changed.Destination.ExitRoute[0].X);
                Check.Near(0,changed.Destination.ExitRoute[changed.Destination.ExitRoute.Count-1].X);
                var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                Check.Equal(ResidentVisitPhase.ExitingService,restored.ResidentTripAt(person.Id,255).VisitPhase);
                Check.Equal(ResidentVisitPhase.Leaving,restored.ResidentTripAt(person.Id,263).VisitPhase);
                Check.Equal(ResidentTripPhase.Returning,restored.ResidentTripAt(person.Id,383).Phase);
                Check.Equal(ResidentTripPhase.Home,restored.ResidentTripAt(person.Id,392).Phase);
                Check.True(!restored.HasDueResidentInnAdmission(200,20,600));
            }));
            cases.Add(("Overnight planner aligns nights to morning and preserves service routes and room ownership",()=>{
                var trip=ResidentTrip.Create("resident",100,200,12*3600,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=new ResidentDestination {ExitWalkSeconds=60,ExitRoute=Path(10,0),Stops=new List<ResidentVisitStop>{
                    new ResidentVisitStop {BuildingKey=10,Purpose=ResidentVisitPurpose.Shop,OpenMinute=480,CloseMinute=1080,WalkSeconds=60,DwellSeconds=120,Route=Path(0,10)},
                    new ResidentVisitStop {BuildingKey=30,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=1,Route=Path(10,30)},
                    new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Guild,OpenMinute=540,CloseMinute=1020,WalkSeconds=60,DwellSeconds=120,Route=Path(30,20)},
                    new ResidentVisitStop {BuildingKey=30,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=1,Route=Path(20,30)},
                    new ResidentVisitStop {BuildingKey=10,Purpose=ResidentVisitPurpose.Shop,OpenMinute=480,CloseMinute=1080,WalkSeconds=60,DwellSeconds=120,Route=Path(30,10)}}};
                var planned=ResidentOvernightPlan.Schedule(trip,123);
                Check.Near(1,trip.Destination.Stops[1].DwellSeconds);
                Check.True(planned.HomeAt>trip.DepartsAt+2*86400-12*3600);
                var firstNight=planned.Evaluate(86400+3600);var secondNight=planned.Evaluate(2*86400+3600);
                Check.Equal(30,firstNight.BuildingKey);Check.Equal(30,secondNight.BuildingKey);
                Check.True(firstNight.VisitLeaveAt%86400>=9*3600 && firstNight.VisitLeaveAt%86400<11*3600);
                Check.True(secondNight.VisitLeaveAt%86400>=9*3600 && secondNight.VisitLeaveAt%86400<11*3600);
                Check.Near(secondNight.VisitLeaveAt,planned.Destination.Stops[1].KeepRoomUntil);
                Check.Equal(20,planned.Evaluate(firstNight.VisitLeaveAt+61).BuildingKey);
                Check.Near(planned.HomeAt,ResidentOvernightPlan.Schedule(trip,123).HomeAt);
                trip.Destination.Stops[3].BuildingKey=31;
                Check.Throws<ArgumentException>(()=>ResidentOvernightPlan.Schedule(trip,123));
            }));
            cases.Add(("Inn admission restore tolerates native sub-millisecond rounding but rejects early service",()=>{
                const double start=12597552000;
                var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);
                store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                var trip=ResidentTrip.Create(person.Id,100,200,start,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                var plan=Plan();plan.Stops.RemoveAt(1);plan.ExitRoute=Path(10,0);
                plan.Stops[0].Purpose=ResidentVisitPurpose.Inn;plan.Stops[0].OpenMinute=plan.Stops[0].CloseMinute=0;
                trip.Destination=plan;store.ScheduleResidentTrip(trip);
                var reception=new ResidentInnReception {BuildingKey=10,VisitEnterAt=start+70,VisitLeaveAt=start+190,StartedAt=start+75,
                    SecondsPerMetre=2,ServiceSeconds=20,Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3 {X=3}}};
                store.SaveResidentInnReception(person.Id,reception);
                Check.True(store.TryAdmitResidentInn(person.Id,ResidentInnReception.Copy(reception).ServiceEndsAt));
                var saved=store.Snapshot();saved.ResidentTrips[0].InnStays[0].AttemptedAt-=0.0001;
                saved.Lodgings[0].Bookings[0].StartsAt=saved.ResidentTrips[0].InnStays[0].AttemptedAt;
                var restored=new WorldStateStore(1);restored.Restore(saved);
                saved.ResidentTrips[0].InnStays[0].AttemptedAt-=1;saved.Lodgings[0].Bookings[0].StartsAt-=1;
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
            }));
            cases.Add(("Resident keeps paid room during daytime services and returns without paying again",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);
                store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                var plan=Plan();
                foreach(var stop in plan.Stops) {stop.OpenMinute=stop.CloseMinute=0;stop.DwellSeconds=120;}
                plan.Stops[0].Purpose=ResidentVisitPurpose.Inn;plan.Stops[0].KeepRoomUntil=550;
                plan.Stops.Add(new ResidentVisitStop {BuildingKey=10,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=60,DwellSeconds=120,Route=Path(20,10)});
                plan.ExitRoute=Path(10,0);trip.Destination=plan;store.ScheduleResidentTrip(trip);
                Check.True(store.TryAdmitResidentInn(person.Id,70));
                Check.Equal("bed:0",store.ResidentInnBed(person.Id,300));
                Check.True(!store.TryBookLodging(200,10,"other","visitor",300,false,1,200,400));
                var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                Check.Equal(TravelerAdmission.Admitted,restored.ResidentInnAdmission(person.Id,450));
                Check.Equal(0,restored.AdvanceInnAdmissions(200,450,8,10));
                Check.True(restored.TryAdmitResidentInn(person.Id,450));
                Check.Equal(1,restored.Snapshot().Lodgings[0].Bookings.Count);
                Check.Equal(1,restored.Snapshot().ResidentTrips[0].InnStays.Count);
                Check.True(restored.ResidentInnBed(person.Id,550)==null);
                Check.True(restored.TryBookLodging(200,10,"next","visitor",300,false,1,550,800));
                plan.Stops[0].KeepRoomUntil=180;
                Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(trip));
                plan.Stops[0].KeepRoomUntil=10000;
                Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(trip));
            }));
            cases.Add(("Mixed inn guests compete chronologically across bounded catch-up and restore",()=>{
                for(int scenario=0;scenario<3;scenario++)
                {
                    bool residentFirst=scenario==0;
                    var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                    store.GetOrCreate(200,new[]{30},1);
                    store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                    var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                    var plan=Plan(); plan.Stops.RemoveAt(1); plan.ExitRoute=Path(10,0);
                    plan.Stops[0].Purpose=ResidentVisitPurpose.Inn; plan.Stops[0].OpenMinute=plan.Stops[0].CloseMinute=0;
                    trip.Destination=plan; store.ScheduleResidentTrip(trip);
                    if(scenario==2) store.SaveResidentInnReception(person.Id,new ResidentInnReception {BuildingKey=10,
                        VisitEnterAt=70,VisitLeaveAt=190,StartedAt=70,SecondsPerMetre=2,ServiceSeconds=30,
                        Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3 {X=5}}});
                    store.RegisterTraveler(new TravelerPerson {Id="traveler:mixed",Name="Visiting Guest",HomeSettlement=300});
                    store.RegisterTravelerVisit(new TravelerVisit {Id="mixed-visit",PersonId="traveler:mixed",SettlementId=200,TavernKey=10,
                        DesiredArrival=scenario==1 ? 40 : 80,CommuteSeconds=10,Nights=1,
                        Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=10}}});
                    Check.Equal(1,store.AdvanceInnAdmissions(200,250,1,10));
                    var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                    Check.Equal(1,restored.AdvanceInnAdmissions(200,250,1,10));
                    Check.Equal(residentFirst ? TravelerAdmission.Admitted : TravelerAdmission.Refused,restored.ResidentInnAdmission(person.Id,100));
                    Check.Equal(residentFirst ? TravelerAdmission.Refused : TravelerAdmission.Admitted,restored.Snapshot().TravelerVisits[0].Admission);
                    Check.Equal(1,restored.Snapshot().Lodgings[0].Bookings.Count);
                    Check.Equal(0,restored.AdvanceInnAdmissions(200,250,8,10));
                }
            }));
            cases.Add(("Delayed resident admissions retain earlier occupied stays before considering later guests",()=>{
                var store=new WorldStateStore(12); var homes=store.GetOrCreate(100,new[]{10,11},1).Households;
                store.GetOrCreate(200,new[]{30},1);
                store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                for(int i=0;i<2;i++)
                {
                    var trip=ResidentTrip.Create(homes[i].Residents[0].Id,100,200,i*50,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                    var plan=Plan(); plan.Stops.RemoveAt(1); plan.ExitRoute=Path(10,0);
                    plan.Stops[0].Purpose=ResidentVisitPurpose.Inn; plan.Stops[0].OpenMinute=plan.Stops[0].CloseMinute=0;
                    plan.Stops[0].DwellSeconds=i==0 ? 120 : 300;
                    trip.Destination=plan; store.ScheduleResidentTrip(trip);
                }
                Check.Equal(1,store.AdvanceResidentInnAdmissions(200,250,1,10));
                Check.Equal(TravelerAdmission.Admitted,store.ResidentInnAdmission(homes[0].Residents[0].Id,100));
                Check.Equal(TravelerAdmission.Pending,store.ResidentInnAdmission(homes[1].Residents[0].Id,250));
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.Equal(1,restored.AdvanceResidentInnAdmissions(200,250,1,10));
                Check.Equal(TravelerAdmission.Refused,restored.ResidentInnAdmission(homes[1].Residents[0].Id,250));
                Check.Near(70,restored.Snapshot().Lodgings[0].Bookings[0].StartsAt);
                Check.Near(190,restored.Snapshot().Lodgings[0].Bookings[0].EndsAt);
                Check.Equal(0,restored.AdvanceResidentInnAdmissions(200,250,8,10));
            }));
            cases.Add(("Resident inn catch-up processes every completed visit once across save restore",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);
                store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                var plan=Plan();
                foreach(var stop in plan.Stops) {stop.BuildingKey=10; stop.Purpose=ResidentVisitPurpose.Inn; stop.OpenMinute=stop.CloseMinute=0; stop.DwellSeconds=120;}
                trip.Destination=plan; store.ScheduleResidentTrip(trip);
                Check.Equal(2,store.AdvanceResidentInnAdmissions(200,400,8,10));
                Check.Equal(2,store.Snapshot().ResidentTrips[0].InnStays.Count);
                Check.Equal(2,store.Snapshot().Lodgings[0].Bookings.Count);
                Check.True(!store.HasDueResidentInnAdmission(200,10,400));
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.Equal(0,restored.AdvanceResidentInnAdmissions(200,500,8,10));
                Check.Equal(TravelerAdmission.Admitted,restored.ResidentInnAdmission(person.Id,100));
                Check.Equal(TravelerAdmission.Admitted,restored.ResidentInnAdmission(person.Id,300));
            }));
            cases.Add(("Inn catch-up allocates last room by counter completion rather than doorway arrival",()=>{
                var store=new WorldStateStore(12); var homes=store.GetOrCreate(100,new[]{10,11},1).Households;
                store.GetOrCreate(200,new[]{30},1);
                store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                for(int i=0;i<2;i++)
                {
                    var person=homes[i].Residents[0];
                    var trip=ResidentTrip.Create(person.Id,100,200,i,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                    var plan=Plan(); plan.Stops.RemoveAt(1); plan.ExitRoute=Path(10,0);
                    plan.Stops[0].Purpose=ResidentVisitPurpose.Inn; plan.Stops[0].OpenMinute=plan.Stops[0].CloseMinute=0;
                    trip.Destination=plan; store.ScheduleResidentTrip(trip);
                    store.SaveResidentInnReception(person.Id,new ResidentInnReception {BuildingKey=10,VisitEnterAt=70+i,VisitLeaveAt=190+i,
                        StartedAt=75+i,SecondsPerMetre=2,ServiceSeconds=i==0 ? 20 : 2,
                        Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3 {X=i==0 ? 5 : 1}}});
                }
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.Equal(1,restored.AdvanceResidentInnAdmissions(200,110,1,10));
                Check.Equal(TravelerAdmission.Admitted,restored.ResidentInnAdmission(homes[1].Residents[0].Id,110));
                Check.Equal(TravelerAdmission.Pending,restored.ResidentInnAdmission(homes[0].Residents[0].Id,110));
                Check.Equal(1,restored.AdvanceResidentInnAdmissions(200,110,1,10));
                Check.Equal(TravelerAdmission.Refused,restored.ResidentInnAdmission(homes[0].Residents[0].Id,110));
            }));
            cases.Add(("Resident inn booking waits for saved counter service across restore",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);
                store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                var plan=Plan(); plan.Stops.RemoveAt(1); plan.ExitRoute=Path(10,0);
                plan.Stops[0].Purpose=ResidentVisitPurpose.Inn; plan.Stops[0].OpenMinute=plan.Stops[0].CloseMinute=0;
                trip.Destination=plan; store.ScheduleResidentTrip(trip);
                var reception=new ResidentInnReception {BuildingKey=10,VisitEnterAt=70,VisitLeaveAt=190,StartedAt=75,
                    SecondsPerMetre=2,ServiceSeconds=20,Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3 {X=3,Y=4}}};
                store.SaveResidentInnReception(person.Id,reception); reception.Route[1].X=999;
                Check.True(!store.TryAdmitResidentInn(person.Id,90));
                Check.True(!store.HasDueResidentInnAdmission(200,10,90));
                Check.Equal(0,store.AdvanceResidentInnAdmissions(200,90,8,10));
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                var copied=ResidentInnReception.Copy(restored.Snapshot().ResidentTrips[0].InnReceptions[0]);
                Check.Near(1.5,copied.Position(80).X); Check.Near(2,copied.Position(80).Y);
                Check.Near(3,copied.Position(110).X);
                Check.True(!restored.TryAdmitResidentInn(person.Id,104));
                Check.Equal(1,restored.AdvanceResidentInnAdmissions(200,105,8,10));
                Check.Near(105,restored.ResidentInnStayAt(person.Id,105).AttemptedAt);
                var saved=restored.Snapshot(); saved.ResidentTrips[0].InnReceptions[0].ServiceSeconds=40;
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
                Check.True(restored.ResidentInnBed(person.Id,106)!=null);
            }));
            cases.Add(("Resident admission batches respect reconciled inn and retain pending unsurveyed arrivals",()=>{
                var store=new WorldStateStore(12);
                var people=store.GetOrCreate(100,new[]{10,11,12},1).Households;
                store.GetOrCreate(200,new[]{30},1);
                for(int i=0;i<3;i++)
                {
                    var person=people[i].Residents[0];
                    var trip=ResidentTrip.Create(person.Id,100,200,i,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                    var plan=Plan(); plan.Stops.RemoveAt(1); plan.ExitRoute=Path(10,0);
                    plan.Stops[0].Purpose=ResidentVisitPurpose.Inn; plan.Stops[0].OpenMinute=plan.Stops[0].CloseMinute=0;
                    if(i==2) plan.Stops[0].BuildingKey=20;
                    trip.Destination=plan; store.ScheduleResidentTrip(trip);
                }
                Check.Equal(0,store.AdvanceResidentInnAdmissions(200,80,1,10));
                store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                Check.Equal(0,store.AdvanceResidentInnAdmissions(201,80,1,10));
                Check.Equal(1,store.AdvanceResidentInnAdmissions(200,80,1,10));
                Check.Equal(TravelerAdmission.Admitted,store.ResidentInnAdmission(people[0].Residents[0].Id,80));
                Check.Equal(TravelerAdmission.Pending,store.ResidentInnAdmission(people[1].Residents[0].Id,80));
                Check.True(store.HasDueResidentInnAdmission(200,10,80));
                Check.Equal(1,store.AdvanceResidentInnAdmissions(200,80,1,10));
                Check.Equal(TravelerAdmission.Refused,store.ResidentInnAdmission(people[1].Residents[0].Id,80));
                Check.Equal(TravelerAdmission.Pending,store.ResidentInnAdmission(people[2].Residents[0].Id,80));
                Check.Equal(0,store.AdvanceResidentInnAdmissions(200,80,1,10));
                Check.True(!store.HasDueResidentInnAdmission(200,10,80));
            }));
            cases.Add(("Resident inn admission reserves shared inventory without creating a second traveler",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);
                store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                var plan=Plan(); plan.Stops.RemoveAt(1); plan.ExitRoute=Path(10,0);
                plan.Stops[0].Purpose=ResidentVisitPurpose.Inn; plan.Stops[0].OpenMinute=plan.Stops[0].CloseMinute=0;
                trip.Destination=plan; store.ScheduleResidentTrip(trip);
                Check.True(!store.TryAdmitResidentInn(person.Id,60));
                Check.True(store.TryAdmitResidentInn(person.Id,75));
                Check.Equal("bed:0",store.ResidentInnBed(person.Id,80));
                var paid=store.ResidentInnStayAt(person.Id,80);
                Check.Near(75,paid.AttemptedAt); paid.LeaveAt=999;
                Check.Near(190,store.ResidentInnStayAt(person.Id,80).LeaveAt);
                Check.True(store.ResidentInnStayAt(person.Id,74)==null);
                Check.True(store.ResidentInnStayAt(person.Id,190)==null);
                Check.True(store.IsLodgingBedReserved(200,10,"bed:0",80,"player"));
                Check.True(store.TryAdmitResidentInn(person.Id,80));
                var saved=store.Snapshot(); Check.Equal(1,saved.Lodgings[0].Bookings.Count); Check.Equal(0,saved.Travelers.Count);
                Check.True(!store.TryExtendLodging(saved.Lodgings[0].Bookings[0].Id,200));
                Check.Equal(TravelerAdmission.Admitted,store.ResidentInnAdmission(person.Id,80));
                var restored=new WorldStateStore(1); restored.Restore(saved);
                Check.Equal("bed:0",restored.ResidentInnBed(person.Id,80));
                saved.Lodgings[0].Bookings[0].GuestId="wrong";
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
                Check.True(restored.ResidentInnBed(person.Id,190)==null);
            }));
            cases.Add(("Full inn refusal persists after the player room becomes free",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);
                store.RegisterLodging(200,10,new[]{new LodgingRoom {Id="room",Beds=new List<string>{"bed:0"}}});
                store.TryBookLodging(200,10,"paid-player","player",200,true,1,0,100);
                var trip=ResidentTrip.Create(person.Id,100,200,0,600,10,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                var plan=Plan(); plan.Stops.RemoveAt(1); plan.ExitRoute=Path(10,0);
                plan.Stops[0].Purpose=ResidentVisitPurpose.Inn; plan.Stops[0].OpenMinute=plan.Stops[0].CloseMinute=0;
                trip.Destination=plan; store.ScheduleResidentTrip(trip);
                Check.True(!store.TryAdmitResidentInn(person.Id,75));
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.True(!restored.TryAdmitResidentInn(person.Id,101));
                Check.Equal(TravelerAdmission.Refused,restored.ResidentInnAdmission(person.Id,101));
                Check.Equal(1,restored.Snapshot().Lodgings[0].Bookings.Count);
            }));
            cases.Add(("Cancelled destination service persists exit motion and immediately starts next leg",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                var trip=ResidentTrip.Create(person.Id,100,200,17*3600,600,3600,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=Plan(); store.ScheduleResidentTrip(trip);
                double start=32*3600;
                var baseline=store.Snapshot();
                var exit=new ResidentServiceExit {BuildingKey=10,VisitEnterAt=start,VisitLeaveAt=start+120,StartedAt=start+10,SecondsPerMetre=2,
                    Route=new List<GuardPoint3> {new GuardPoint3 {X=0,Y=3,Z=0},new GuardPoint3 {X=4,Y=0,Z=0}}};
                store.SaveResidentServiceExit(person.Id,exit);
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                var during=restored.ResidentTripAt(person.Id,start+15);
                Check.Equal(ResidentVisitPhase.ExitingService,during.VisitPhase); Check.Near(1.5,during.InteriorY);
                Check.Equal(10,during.BuildingKey);
                Check.Equal(-1,restored.ResidentTripAt(person.Id,start+20).BuildingKey);
                Check.Equal(ResidentVisitPhase.Walking,restored.ResidentTripAt(person.Id,start+20).VisitPhase);
                Check.Equal(20,restored.ResidentTripAt(person.Id,33*3600).BuildingKey);
                var late=new WorldStateStore(1); late.Restore(baseline);
                exit.StartedAt=start+119; late.SaveResidentServiceExit(person.Id,exit);
                Check.Equal(ResidentVisitPhase.ExitingService,late.ResidentTripAt(person.Id,start+125).VisitPhase);
                Check.Equal(ResidentVisitPhase.Walking,late.ResidentTripAt(person.Id,start+129).VisitPhase);
                var invalid=store.Snapshot(); invalid.ResidentTrips[0].ServiceExits[0].BuildingKey=99;
                Check.Throws<ArgumentException>(()=>restored.Restore(invalid));
                Check.Equal(20,restored.ResidentTripAt(person.Id,33*3600).BuildingKey);
            }));
            cases.Add(("Service exit follows stairs, copies route and permits a late physical exit",()=>{
                var source=new ResidentServiceExit {BuildingKey=10,VisitEnterAt=100,VisitLeaveAt=200,StartedAt=110,SecondsPerMetre=2,
                    Route=new List<GuardPoint3> {new GuardPoint3 {X=0,Y=3,Z=0},new GuardPoint3 {X=4,Y=0,Z=0}}};
                var exit=ResidentServiceExit.Copy(source);
                Check.Near(120,exit.ExitAt); Check.Near(2,exit.Position(115).X); Check.Near(1.5,exit.Position(115).Y);
                source.Route[1].X=99; Check.Near(4,exit.Position(121).X);
                Check.True(ResidentServiceExit.Copy(source).ExitAt>source.VisitLeaveAt);
                source.SecondsPerMetre=double.NaN;
                Check.Throws<ArgumentException>(()=>ResidentServiceExit.Copy(source));
            }));
            cases.Add(("Visiting resident can witness entry and saved evidence survives trip replacement",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);
                var trip=ResidentTrip.Create(person.Id,100,200,17*3600,600,3600,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=Plan(); store.ScheduleResidentTrip(trip);
                Check.Throws<ArgumentException>(()=>store.RecordEntry(200,30,17*3600,new[]{person.Id},new string[0]));
                var report=store.RecordEntry(200,30,32*3600,new[]{person.Id},new string[0]);
                store.ScheduleResidentTrip(ResidentTrip.Create(person.Id,100,300,40*3600,600,3600,
                    new[]{new RoutePoint(10,10),new RoutePoint(11,10)}));
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.Equal(person.Id,restored.EntriesFor(200)[0].CitizenWitnesses[0]);
                Check.Equal(report.Id,restored.EntriesFor(200)[0].Id);
            }));
            cases.Add(("Resident visitor witness survives a later trip replacing the original journey",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                var victim=store.GetOrCreate(200,new[]{30},1).Households[0].Residents[0];
                var trip=ResidentTrip.Create(person.Id,100,200,17*3600,600,3600,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=Plan(); store.ScheduleResidentTrip(trip);
                var alarm=store.RecordViolence(200,victim.Id,32*3600);
                Check.True(store.Witness(alarm.Id,person.Id,32*3600)!=null);
                var next=ResidentTrip.Create(person.Id,100,300,40*3600,600,3600,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                store.ScheduleResidentTrip(next);
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.True(restored.Witness(alarm.Id,person.Id,32*3600+1)!=null);
            }));
            cases.Add(("Resident visitor murder preserves original death and destination incident across restore",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.GetOrCreate(200,new[]{30},1);
                var trip=ResidentTrip.Create(person.Id,100,200,17*3600,600,3600,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=Plan(); store.ScheduleResidentTrip(trip);
                Check.Throws<ArgumentException>(()=>store.RecordViolence(200,person.Id,17*3600));
                var alarm=store.RecordViolence(200,person.Id,32*3600);
                store.LocateViolence(alarm.Id,10,0,0,0);
                person.IsAlive=false; store.ConfirmCorpse(alarm.Id,32*3600+1);
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.Equal(0,restored.DestinationResidents(200,32*3600+2).Count);
                Check.Equal(ResidentTripPhase.Unavailable,restored.ResidentTripAt(person.Id,35*3600).Phase);
                Check.Equal(alarm.Id,restored.RecordViolence(200,person.Id,35*3600).Id);
            }));
            cases.Add(("Destination roster uses the living home resident only during the destination visit",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                var trip=ResidentTrip.Create(person.Id,100,200,17*3600,600,3600,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=Plan(); store.ScheduleResidentTrip(trip);
                Check.Equal(0,store.DestinationResidents(200,17*3600).Count);
                Check.Equal(0,store.DestinationResidents(100,32*3600).Count);
                var visitors=store.DestinationResidents(200,32*3600);
                Check.Equal(1,visitors.Count);
                Check.True(Object.ReferenceEquals(person,visitors[0]));
                Check.Equal(0,store.RoadResidents(32*3600).Count);
                ActivitySnapshot activity;
                Check.True(store.TryDestinationResidentState(person.Id,200,32*3600,out activity));
                Check.Equal(10,activity.BuildingKey);
                Check.Equal(Activity.Visit,activity.Activity);
                Check.True(!store.TryDestinationResidentState(person.Id,100,32*3600,out activity));
                Check.True(store.TryDestinationResidentState(person.Id,200,18*3600+30,out activity));
                Check.Equal(-1,activity.BuildingKey);
                Check.Equal(Activity.Travel,activity.Activity);
                Check.True(store.IsResidentAway(person.Id,32*3600));
                Check.Equal(0,store.DestinationResidents(200,35*3600).Count);
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.Equal(person.Id,restored.DestinationResidents(200,32*3600)[0].Id);
                person.IsAlive=false;
                Check.Equal(0,store.DestinationResidents(200,32*3600).Count);
                Check.True(!store.TryDestinationResidentState(person.Id,200,32*3600,out activity));
                Check.Throws<ArgumentException>(()=>store.DestinationResidents(200,double.NaN));
            }));
            cases.Add(("Destination timing supports overnight services and holds closed-gate departures",()=>{
                var plan=Plan(); plan.Stops.RemoveAt(1); plan.ExitRoute=Path(10,0);
                plan.Stops[0].OpenMinute=1320; plan.Stops[0].CloseMinute=360;
                Check.Equal(10,ResidentVisitJourney.Create(23*3600,plan).Evaluate(23*3600+90).BuildingKey);
                plan.Stops[0].OpenMinute=0; plan.Stops[0].CloseMinute=0;
                plan.Walled=true; plan.EntryGateSeconds=30; plan.ExitGateSeconds=60;
                var late=ResidentVisitJourney.Create(17*3600+57*60,plan);
                Check.Equal(ResidentVisitPhase.WaitingToLeave,late.Evaluate(20*3600).Phase);
                Check.Equal(-1,late.Evaluate(20*3600).BuildingKey);
                Check.Near(30*3600+60,late.EndsAt);
                var night=ResidentVisitJourney.Create(20*3600,plan);
                Check.Equal(ResidentVisitPhase.WaitingAtGate,night.Evaluate(29*3600).Phase);
                Check.Equal(ResidentVisitPhase.Walking,night.Evaluate(30*3600).Phase);
            }));
            cases.Add(("Destination visits walk, wait for opening, enter services and return to the same road join",()=>{
                var journey=ResidentVisitJourney.Create(7*3600+58*60,Plan());
                Check.Equal(ResidentVisitPhase.Walking,journey.Evaluate(7*3600+58*60+30).Phase);
                Check.Near(5,journey.Evaluate(7*3600+58*60+30).Position.X);
                Check.Equal(ResidentVisitPhase.WaitingForOpening,journey.Evaluate(7*3600+59*60).Phase);
                Check.Equal(-1,journey.Evaluate(7*3600+59*60).BuildingKey);
                Check.Equal(10,journey.Evaluate(8*3600).BuildingKey);
                Check.Near(8*3600,journey.Evaluate(8*3600).EnterAt);
                Check.Near(8*3600+120,journey.Evaluate(8*3600).LeaveAt);
                Check.Equal(20,journey.Evaluate(9*3600).BuildingKey);
                Check.Near(9*3600+300,journey.EndsAt);
                Check.Equal(ResidentVisitPhase.Leaving,journey.Evaluate(journey.EndsAt-60).Phase);
                Check.Near(10,journey.Evaluate(journey.EndsAt-60).Position.X);
                Check.Equal(ResidentVisitPhase.Finished,journey.Evaluate(journey.EndsAt).Phase);
            }));
            cases.Add(("Closed services postpone visits across days and reject disconnected itineraries",()=>{
                var plan=Plan(); var journey=ResidentVisitJourney.Create(18*3600,plan);
                Check.Equal(ResidentVisitPhase.WaitingForOpening,journey.Evaluate(22*3600).Phase);
                Check.Equal(10,journey.Evaluate(32*3600).BuildingKey);
                plan.Stops[1].Route[0].X=99;
                Check.Throws<ArgumentException>(()=>ResidentVisitJourney.Create(0,plan));
                Check.Equal(10,journey.Evaluate(32*3600).BuildingKey);
                plan=Plan(); plan.Stops[0].DwellSeconds=11*3600;
                Check.Throws<ArgumentException>(()=>ResidentVisitJourney.Create(0,plan));
            }));
            cases.Add(("Resident destination plans persist and determine the actual home return time",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                var trip=ResidentTrip.Create(person.Id,100,200,17*3600,600,3600,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
                trip.Destination=Plan(); store.ScheduleResidentTrip(trip);
                Check.Equal(ResidentTripPhase.Visiting,store.ResidentTripAt(person.Id,32*3600).Phase);
                Check.Equal(10,store.ResidentTripAt(person.Id,32*3600).BuildingKey);
                Check.Near(32*3600,store.ResidentTripAt(person.Id,32*3600).VisitEnterAt);
                Check.Near(32*3600+120,store.ResidentTripAt(person.Id,32*3600).VisitLeaveAt);
                Check.Equal(ResidentVisitPurpose.Shop,store.ResidentTripAt(person.Id,32*3600).VisitPurpose);
                Check.True(store.IsResidentAway(person.Id,32*3600));
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.Equal(20,restored.ResidentTripAt(person.Id,33*3600).BuildingKey);
                Check.Equal(ResidentTripPhase.Home,restored.ResidentTripAt(person.Id,35*3600).Phase);
                var legacy=store.Snapshot(); legacy.Version=14; legacy.ResidentTrips[0].Destination=null;
                restored.Restore(legacy);
                Check.Equal(ResidentTripPhase.Home,restored.ResidentTripAt(person.Id,32*3600).Phase);
            }));
        }
    }
}
