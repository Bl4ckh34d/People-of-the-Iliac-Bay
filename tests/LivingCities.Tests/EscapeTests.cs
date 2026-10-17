using System;
using System.Collections.Generic;
using System.Linq;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class EscapeTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Witness safety preparation precedes a large elapsed movement step",()=>{
                var store=new WorldStateStore(1); var town=store.GetOrCreate(1,new[]{1,2,3});
                var residents=town.Households.SelectMany(h=>h.Residents).ToArray();
                var alarm=store.RecordViolence(1,residents[0].Id,100);
                store.LocateViolence(alarm.Id,1,0,0,0);
                var person=store.Witness(alarm.Id,residents[1].Id,100);
                person.Reaction=CivilianReaction.Flee;person.HasPosition=true;person.BuildingKey=1;
                person.Escape=CivilianEscape.Create(person,100,
                    new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2.8}},
                    new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=20}}, timeScale:12);
                store.AdvanceEscapes(1,112,true);
                Check.Near(112,person.EscapeAdvancedAt);
                var reload=new WorldStateStore(0);reload.Restore(store.Snapshot());store=reload;
                person=store.ActiveAlarm(residents[1].Id,112).People[0];
                bool prepared=false;
                store.AdvanceEscapes(1,160,true,(incident,witness,now)=>{
                    Check.Near(0,witness.X);Check.Equal(alarm.Id,incident.Id);
                    Check.Near(112,witness.EscapeAdvancedAt);
                    witness.Escape=witness.Escape.RedirectExterior(witness.EscapeAdvancedAt,new List<SavedRoutePoint>{
                        new SavedRoutePoint(),new SavedRoutePoint{X=-20}});
                    prepared=true;
                });
                Check.True(prepared);Check.Near(-11.2,person.X);Check.Near(160,person.EscapeAdvancedAt);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                restored.AdvanceEscapes(1,172,true);
                Check.Near(-14,restored.ActiveAlarm(residents[1].Id,172).People[0].X);
                Check.Near(172,restored.ActiveAlarm(residents[1].Id,172).People[0].EscapeAdvancedAt);
                var corrupt=restored.Snapshot();
                corrupt.AlarmIncidents[0].People[0].EscapeAdvancedAt=double.NaN;
                Check.Throws<ArgumentException>(()=>restored.Restore(corrupt));
                Check.Near(172,restored.ActiveAlarm(residents[1].Id,172).People[0].EscapeAdvancedAt);
                corrupt=restored.Snapshot();corrupt.AlarmIncidents[0].People[0].EscapeAdvancedAt=99;
                Check.Throws<ArgumentException>(()=>restored.Restore(corrupt));
                corrupt=restored.Snapshot();corrupt.AlarmIncidents[0].People[0].EscapeAdvancedAt=111;
                Check.Throws<ArgumentException>(()=>restored.Restore(corrupt));
                corrupt=restored.Snapshot();corrupt.AlarmIncidents[0].People[0].Escape=null;
                Check.Throws<ArgumentException>(()=>restored.Restore(corrupt));
                var legacy=store.Snapshot();legacy.AlarmIncidents[0].People[0].EscapeAdvancedAt=0;
                restored.Restore(legacy);
                Check.Near(0,restored.ActiveAlarm(residents[1].Id,160).People[0].EscapeAdvancedAt);
                restored.AdvanceEscapes(1,172,true);
                Check.Near(172,restored.ActiveAlarm(residents[1].Id,172).People[0].EscapeAdvancedAt);
            }));
            cases.Add(("Escaped witnesses can retarget from their current position without restarting indoor flight",()=>{
                var person=new AlarmPerson {HasPosition=true,BuildingKey=7,Reaction=CivilianReaction.Flee};
                var escape=CivilianEscape.Create(person,100,
                    new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3 {X=3}},
                    new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=12}},3,12);
                escape.Advance(person,124); Check.Near(3,person.X);
                var changed=escape.RedirectExterior(124,new List<SavedRoutePoint>{
                    new SavedRoutePoint {X=3},new SavedRoutePoint {X=3,Z=6}});
                Check.Near(112,changed.ExitAt); Check.Near(148,changed.ReportAt);
                changed.Advance(person,136); Check.Near(3,person.X); Check.Near(3,person.Z);
                var restored=CivilianEscape.Copy(changed); restored.Advance(person,148);
                Check.Near(6,person.Z); Check.True(restored.Arrived(148));
                Check.Near(160,escape.ReportAt);
                Check.Throws<ArgumentException>(()=>escape.RedirectExterior(110,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=1}}));
                Check.Throws<ArgumentException>(()=>escape.RedirectExterior(124,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=1}}));
            }));
            cases.Add(("Fleeing speed varies persistently between 2.5 and 3.5 times walking speed", () => {
                var speeds = new HashSet<double>();
                for (uint seed = 0; seed < 1000; seed++)
                {
                    double speed = ResidentMovement.FleeSpeed(seed);
                    Check.True(speed >= ResidentMovement.WalkSpeed * 2.5 && speed <= ResidentMovement.WalkSpeed * 3.5);
                    Check.Equal(speed, ResidentMovement.FleeSpeed(seed)); speeds.Add(speed);
                }
                Check.True(speeds.Count > 100);
                var person = new AlarmPerson { HasPosition = true, BuildingKey = 1, Reaction = CivilianReaction.Flee };
                var escape = CivilianEscape.Create(person, 100,
                    new List<GuardPoint3> { new GuardPoint3(), new GuardPoint3 { X = 3.9 } },
                    new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 3.9 } }, 3.9, 12);
                Check.Near(112, escape.ExitAt); Check.Near(124, escape.ReportAt);
                var copy = CivilianEscape.Copy(escape); Check.Near(3.9, copy.Speed);
                copy.Advance(person, 106); Check.Near(1.95, person.X);
                var legacy = CivilianEscape.Copy(escape); legacy.Speed = 0;
                Check.Near(100 + 3.9 / 2.8 * 12, CivilianEscape.Copy(legacy).ExitAt);
                copy.Speed = double.NaN; Check.Throws<ArgumentException>(() => CivilianEscape.Copy(copy));
            }));
            cases.Add(("Fleeing witness reaches door then guard using elapsed game time", () => {
                var person = new AlarmPerson { ResidentId = "w", DirectWitness = true, Reaction = CivilianReaction.Flee,
                    HasPosition = true, BuildingKey = 12, X = 2 };
                var escape = CivilianEscape.Create(person, 100,
                    new List<GuardPoint3> { new GuardPoint3 { X = 2 }, new GuardPoint3 { X = 4.8 } },
                    new List<SavedRoutePoint> { new SavedRoutePoint { X = 50 }, new SavedRoutePoint { X = 52.8 } }, timeScale:12);
                person.Escape = escape;
                escape.Advance(person, 106);
                Check.Equal(12, person.BuildingKey); Check.Near(3.4, person.X);
                escape.Advance(person, 118);
                Check.Equal(-1, person.BuildingKey); Check.Near(51.4, person.X);
                Check.True(!escape.Arrived(118)); Check.True(escape.Arrived(124));
                escape.Advance(person, 500); Check.Near(52.8, person.X);
            }));
            cases.Add(("Escape plan rejects disconnected start and keeps caller routes independent", () => {
                var person = new AlarmPerson { HasPosition = true, BuildingKey = 3, Reaction = CivilianReaction.Flee };
                var inside = new List<GuardPoint3> { new GuardPoint3(), new GuardPoint3 { X = 3 } };
                var outside = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { Z = 8 } };
                var escape = CivilianEscape.Create(person, 10, inside, outside);
                inside[0].X = 99; outside[1].Z = 99;
                Check.Near(0, escape.InteriorRoute[0].X); Check.Near(8, escape.ExteriorRoute[1].Z);
                Check.Throws<ArgumentException>(() => CivilianEscape.Create(person, 10, inside, outside));
            }));
            cases.Add(("Witness escape persists and dead witnesses cannot report", () => {
                var store = new WorldStateStore(1); var town = store.GetOrCreate(1, new[] { 1, 2, 3 });
                var residents = town.Households.SelectMany(h => h.Residents).ToArray();
                var alarm = store.RecordViolence(1, residents[0].Id, 100);
                store.LocateViolence(alarm.Id, 1, 0, 0, 0);
                var person = store.Witness(alarm.Id, residents[1].Id, 100);
                person.Reaction = CivilianReaction.Flee; person.HasPosition = true; person.BuildingKey = 1;
                person.Escape = CivilianEscape.Create(person, 100,
                    new List<GuardPoint3> { new GuardPoint3(), new GuardPoint3 { X = 2.8 } },
                    new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 2.8 } }, timeScale:12);
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.Equal(0, restored.AdvanceEscapes(1, 110).Count);
                var saved = restored.ActiveAlarm(residents[1].Id, 110).People[0];
                Check.Equal(1, saved.BuildingKey);
                Check.Equal(1, restored.AdvanceEscapes(1, 130).Count); Check.Equal(-1, saved.BuildingKey);
                residents[1].IsAlive = false;
                Check.Equal(0, store.AdvanceEscapes(1, 130).Count);
                var invalid = restored.Snapshot(); invalid.AlarmIncidents[0].People[0].Escape.InteriorRoute[0].X = double.NaN;
                Check.Throws<ArgumentException>(() => restored.Restore(invalid));
                var fighting = restored.Snapshot(); fighting.AlarmIncidents[0].People[0].Reaction = CivilianReaction.Fight;
                Check.Throws<ArgumentException>(() => restored.Restore(fighting));
                // A distant guard must not make a real witness forget the murder halfway there.
                var longJourney = restored.Snapshot();
                longJourney.AlarmIncidents[0].People[0].Escape.ExteriorRoute[1].X = 280;
                restored.Restore(longJourney);
                Check.True(restored.ActiveAlarm(residents[1].Id, 1100) != null, "Witness forgot crime before reaching distant guard.");
                Check.Equal(1, restored.AdvanceEscapes(1, 1400).Count);
                Check.True(restored.ActiveAlarm(residents[1].Id, 1700) == null);
            }));
        }
    }
}
