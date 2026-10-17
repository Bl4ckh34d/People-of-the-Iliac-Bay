using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
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
