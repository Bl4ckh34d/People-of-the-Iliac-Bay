using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ThreatResponseTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Flight rejects a route segment through the attacker even when endpoint is farther away",()=>{
                Check.True(!FlightSafety.SafeStep(0,0,8,0,3,0));
                Check.True(FlightSafety.SafeStep(0,0,-4,0,3,0));
                Check.True(FlightSafety.SafeStep(0,0,0,4,3,0));
            }));
            cases.Add(("Flight may approach home beyond danger range but cannot cross back into danger",()=>{
                Check.True(FlightSafety.SafeStep(20,0,15,0,0,0));
                Check.True(!FlightSafety.SafeStep(20,0,8,0,0,0));
                Check.True(!FlightSafety.SafeStep(0,0,double.NaN,0,3,0));
                Check.True(!FlightSafety.SafeStep(3,0,2.9,0,0,0));
            }));
            cases.Add(("Rejected visit walks out and home with saved cancellation", () => {
                var store=new WorldStateStore(17); var town=store.GetOrCreate(1,new[]{7,8},1);
                var resident=town.Households[0].Residents[0];
                var departure=VisitDeparture.Create(resident.Id,1,resident.HomeKey,8,100,1000,
                    new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},
                    new List<SavedRoutePoint>{new SavedRoutePoint{X=10},new SavedRoutePoint{X=20}},1,10);
                store.SaveVisitDeparture(departure);
                Check.Equal(8,store.VisitDepartureFor(resident.Id,110).Position(110).BuildingKey);
                Check.Equal(-1,departure.Position(125).BuildingKey);
                Check.Near(10.5,departure.Position(125).X);
                Check.Equal(resident.HomeKey,departure.Position(300).BuildingKey);
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Near(10.5,restored.VisitDepartureFor(resident.Id,125).Position(125).X);
                Check.True(restored.VisitDepartureFor(resident.Id,1000)==null);
                var invalid=store.Snapshot(); invalid.VisitDepartures[0].HomeKey=99;
                Check.Throws<ArgumentException>(()=>restored.Restore(invalid));
                Check.True(restored.VisitDepartureFor(resident.Id,125)!=null);
                var historical=store.Snapshot(); historical.VisitDepartures=null;
                restored.Restore(historical); Check.True(restored.VisitDepartureFor(resident.Id,125)==null);
            }));
            cases.Add(("Appointment-linked departure preserves witnessed booking through JSON restore", () => {
                var store=new WorldStateStore(23); var town=store.GetOrCreate(100,new[]{7,8},1);
                var resident=town.Households[0].Residents[0];
                var booking=new AppointmentRecord {Id="departure-booking",ResidentId=resident.Id,Purpose="meeting",
                    StartsAt=1000,EndsAt=4000,GraceSeconds=30,PatienceSeconds=600,
                    Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=8}};
                Check.True(store.TryReserveAppointment(booking,500,out var reason),reason);
                for(double at=1000;at<=1600;at+=10) store.ObserveAppointment(booking.Id,at,true,false,false);
                store.ObserveAppointment(booking.Id,1601,true,false,false);
                var resolved=store.FindAppointment(booking.Id);
                Check.Equal("player-absent",resolved.Status);
                Check.True(resolved.OutcomeRecorded && resolved.NpcArrivedAt>=0);
                double start=1610;
                var inside=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}};
                var outside=new List<SavedRoutePoint>{new SavedRoutePoint{X=3},new SavedRoutePoint{X=5}};
                var route=VisitDeparture.Create(resident.Id,100,resident.HomeKey,8,start,5000,inside,outside,1,10);
                var departure=VisitDeparture.Create(resident.Id,100,resident.HomeKey,8,start,5000,inside,outside,1,10,
                    booking.Id,route.Motion.ExitAt);
                store.SaveVisitDeparture(departure);
                Check.True(store.TryGetAppointmentDepartureContinuation(resident.Id,start,out var continuation));
                Check.Equal(booking.Id,continuation.AppointmentId);
                Check.Equal(8,continuation.SourceBuildingKey);
                Check.Near(start,continuation.StartedAt); Check.Near(route.Motion.ExitAt,continuation.ResumeRoutineAt);
                Check.True(!store.TryGetAppointmentDepartureContinuation(resident.Id,5000,out _));
                var copied=VisitDeparture.Copy(departure);
                Check.Equal(booking.Id,copied.AppointmentId); Check.Near(route.Motion.ExitAt,copied.ResumeRoutineAt);
                var json=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                var restored=new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                var saved=restored.VisitDepartureFor(resident.Id,start);
                Check.Equal(booking.Id,saved.AppointmentId); Check.Near(saved.Motion.ExitAt,saved.ResumeRoutineAt);
                Check.Equal("player-absent",restored.FindAppointment(booking.Id).Status);
                var unknown=VisitDeparture.Copy(departure); unknown.AppointmentId="missing-booking";
                Check.Throws<ArgumentException>(()=>store.SaveVisitDeparture(unknown));
                Check.Equal(booking.Id,store.VisitDepartureFor(resident.Id,start).AppointmentId);
                var tooEarly=VisitDeparture.Create(resident.Id,100,resident.HomeKey,8,1600,5000,inside,outside,1,10,
                    booking.Id,1620);
                Check.Throws<ArgumentException>(()=>store.SaveVisitDeparture(tooEarly));
                var invalidSnapshot=store.Snapshot(); invalidSnapshot.VisitDepartures[0].AppointmentId="missing-booking";
                Check.Throws<ArgumentException>(()=>restored.Restore(invalidSnapshot));
                Check.Equal(booking.Id,restored.VisitDepartureFor(resident.Id,start).AppointmentId);
                invalidSnapshot=store.Snapshot(); invalidSnapshot.Appointments[0].Place.BuildingKey=9;
                Check.Throws<ArgumentException>(()=>restored.Restore(invalidSnapshot));
                Check.Equal(booking.Id,restored.VisitDepartureFor(resident.Id,start).AppointmentId);
            }));
            cases.Add(("Appointment departure accepts native fifteen-digit derived exit timestamps", () => {
                double start=12597758701.12345;
                var route=VisitDeparture.Create("resident",100,7,8,start,start+1000,
                    new List<GuardPoint3>{new GuardPoint3 {X=0.123456789012345,Y=0.7,Z=1},
                        new GuardPoint3 {X=3.123456789012345,Y=0.7,Z=5}},
                    new List<SavedRoutePoint>{new SavedRoutePoint {X=3},new SavedRoutePoint {X=8}},0.673456789012345,2);
                route.AppointmentId="booking";route.ResumeRoutineAt=route.Motion.ExitAt;
                route.ResumeRoutineAt=double.Parse(route.ResumeRoutineAt.ToString("G15",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);
                route.Motion.StartedAt=double.Parse(start.ToString("G15",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);
                route.Motion.Speed=double.Parse(route.Motion.Speed.ToString("G15",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);
                var restored=VisitDeparture.Copy(route);
                Check.True(Math.Abs(restored.ResumeRoutineAt-restored.Motion.ExitAt)<0.001);
                restored.ResumeRoutineAt-=1;
                Check.Throws<ArgumentException>(()=>VisitDeparture.Copy(restored));
            }));
            cases.Add(("Legacy departure JSON defaults appointment continuation fields inert", () => {
                var store=new WorldStateStore(24); var resident=store.GetOrCreate(100,new[]{7,8},1).Households[0].Residents[0];
                store.SaveVisitDeparture(VisitDeparture.Create(resident.Id,100,resident.HomeKey,8,100,500,
                    new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},
                    new List<SavedRoutePoint>{new SavedRoutePoint{X=3},new SavedRoutePoint{X=5}},1,10));
                var json=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                json=JsonFixture.WithoutProperty(json,"AppointmentId");
                json=JsonFixture.WithoutProperty(json,"ResumeRoutineAt");
                Check.True(!json.Contains("\"AppointmentId\"") && !json.Contains("\"ResumeRoutineAt\""),
                    "Legacy fixture still contains continuation fields.");
                var restored=new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                var legacy=restored.VisitDepartureFor(resident.Id,100);
                Check.True(legacy!=null); Check.True(legacy.AppointmentId==null); Check.Near(0,legacy.ResumeRoutineAt);
                Check.True(!restored.TryGetAppointmentDepartureContinuation(resident.Id,100,out _));
            }));
            cases.Add(("Appointment departure rejects malformed continuation payload", () => {
                var store=new WorldStateStore(25); var resident=store.GetOrCreate(100,new[]{7,8},1).Households[0].Residents[0];
                var valid=VisitDeparture.Create(resident.Id,100,resident.HomeKey,8,100,500,
                    new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},
                    new List<SavedRoutePoint>{new SavedRoutePoint{X=3},new SavedRoutePoint{X=5}},1,10,
                    "booking",120);
                var invalid=VisitDeparture.Copy(valid); invalid.ResumeRoutineAt=119;
                Check.Throws<ArgumentException>(()=>VisitDeparture.Copy(invalid));
                invalid=VisitDeparture.Copy(valid); invalid.ResumeRoutineAt=double.NaN;
                Check.Throws<ArgumentException>(()=>VisitDeparture.Copy(invalid));
                invalid=VisitDeparture.Copy(valid); invalid.AppointmentId=" ";
                Check.Throws<ArgumentException>(()=>VisitDeparture.Copy(invalid));
                invalid=VisitDeparture.Copy(valid); invalid.AppointmentId=new string('x',121);
                Check.Throws<ArgumentException>(()=>VisitDeparture.Copy(invalid));
                invalid=VisitDeparture.Copy(valid); invalid.AppointmentId=null;
                Check.Throws<ArgumentException>(()=>VisitDeparture.Copy(invalid));
            }));
            cases.Add(("Late corpse discovery persists without reviving old witness responses", () => {
                var store = new WorldStateStore(17);
                var town = store.GetOrCreate(1, new[] { 7, 8, 9 }, 1);
                var people = new List<Resident>();
                foreach (var home in town.Households) people.AddRange(home.Residents);
                var victim=people[0]; var oldWitness=people[1]; var visitor=people[2];
                var alarm=store.RecordViolence(1,victim.Id,100);
                store.LocateViolence(alarm.Id,7,0,0,0);
                store.Witness(alarm.Id,oldWitness.Id,100);
                Population.MarkDead(town,victim.Id); store.ConfirmCorpse(alarm.Id,100);
                var observer=store.NoticeInteriorCorpse(alarm.Id,visitor.Id,7,2000);
                Check.True(observer.CorpseObserver && !observer.DirectWitness && !observer.InnAlarm);
                Check.Equal(CivilianReaction.Flee,observer.Reaction);
                Check.True(store.ActiveAlarm(oldWitness.Id,2000)==null);
                observer.HasPosition=true; observer.BuildingKey=7; observer.X=1;
                observer.Escape=CivilianEscape.Create(observer,2000,
                    new List<GuardPoint3>{new GuardPoint3{X=1},new GuardPoint3{X=2}},
                    new List<SavedRoutePoint>{new SavedRoutePoint{X=2},new SavedRoutePoint{X=4}});
                var restored=new WorldStateStore(18); restored.Restore(store.Snapshot());
                var saved=restored.ActiveAlarm(visitor.Id,2001).People.Find(p=>p.ResidentId==visitor.Id);
                Check.True(saved.CorpseObserver); Check.Equal(2000d,saved.CorpseSeenAt);
                Check.Equal(1,restored.AdvanceEscapes(1,saved.Escape.ReportAt).Count);
                Check.True(saved.EscapeAdvancedAt>0);
                Check.True(restored.ActiveAlarm(oldWitness.Id,2001)==null);
                Check.True(restored.NoticeInteriorCorpse(alarm.Id,visitor.Id,7,2005)==saved);
                Check.Equal(2000d,saved.CorpseSeenAt);
                var returning=restored.NoticeInteriorCorpse(alarm.Id,visitor.Id,7,4000);
                Check.Equal(4000d,returning.CorpseSeenAt);
                Check.True(returning.Escape==null && !returning.HasPosition);
                Check.Near(0,returning.EscapeAdvancedAt);
                Check.True(restored.Snapshot()!=null);
                Check.True(restored.ActiveAlarm(visitor.Id,4000)!=null);
                restored.ObserveCorpses(1,-1,86501);
                Check.True(restored.NoticeInteriorCorpse(alarm.Id,oldWitness.Id,7,86502)==null);
            }));
            cases.Add(("Bystander thresholds vary by resident and retain hysteresis", () => {
                var first = BystanderResponse.Evaluate(9.6, 11, BystanderPosture.Resume);
                var same = BystanderResponse.Evaluate(9.6, 11, BystanderPosture.Resume);
                Check.Equal(first, same);
                Check.True(BystanderResponse.Evaluate(8, 11, BystanderPosture.Resume) == BystanderPosture.Retreat);
                Check.True(BystanderResponse.Evaluate(10, 11, BystanderPosture.Retreat) == BystanderPosture.Retreat);
                Check.True(BystanderResponse.Evaluate(15, 11, BystanderPosture.Retreat) != BystanderPosture.Retreat);
                Check.Equal(BystanderPosture.Watch, BystanderResponse.Evaluate(15, 11, BystanderPosture.Resume));
                Check.Equal(BystanderPosture.Resume, BystanderResponse.Evaluate(21, 11, BystanderPosture.Resume));
                var thresholds = new HashSet<BystanderPosture>();
                for (uint seed = 0; seed < 50; seed++)
                    thresholds.Add(BystanderResponse.Evaluate(10, seed, BystanderPosture.Resume));
                Check.True(thresholds.Count > 1);
            }));
            cases.Add(("Inn alarm spreads to living occupants and enables an escape report", () => {
                var store = new WorldStateStore(17);
                var town = store.GetOrCreate(1, new[] { 7, 8, 9 }, 1);
                var people = new List<Resident>();
                foreach (var home in town.Households) people.AddRange(home.Residents);
                var victim = people[0];
                var witness = people[1];
                var occupant = people[2];
                var alarm = store.RecordViolence(1, victim.Id, 100);
                store.LocateViolence(alarm.Id, 7, 0, 0, 0);
                store.Witness(alarm.Id, witness.Id, 100);
                var person = store.AlertInnOccupant(alarm.Id, occupant.Id, 7, 101);
                person.HasPosition = true; person.BuildingKey = 7; person.X = 1;
                person.Escape = CivilianEscape.Create(person, 101,
                    new List<GuardPoint3> { new GuardPoint3 { X=1 }, new GuardPoint3 { X=2 } },
                    new List<SavedRoutePoint> { new SavedRoutePoint { X=2 }, new SavedRoutePoint { X=4 } });
                Check.True(person.InnAlarm); Check.Equal(CivilianReaction.Flee, person.Reaction);
                Check.True(!person.DirectWitness && !person.HeardViolence);
                var restored = new WorldStateStore(18); restored.Restore(store.Snapshot());
                var saved = restored.ActiveAlarm(occupant.Id, 102).People.Find(p => p.ResidentId == occupant.Id);
                Check.True(saved.InnAlarm); Check.True(saved.Escape != null);
                Check.Equal(1, restored.AdvanceEscapes(1, saved.Escape.ReportAt).Count);
            }));
        }
    }
}
