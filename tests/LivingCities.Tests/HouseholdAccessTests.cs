using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class HouseholdAccessTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("House greeting counts living visitors and sleepers but not corpses or absent owners", () => {
                var people = new List<Resident> { new Resident { Id = "owner", IsAlive = false } };
                Check.True(!HouseholdAccess.HasLivingOccupant(4, people, r => { throw new Exception("Corpse has no activity"); }));
                people[0].IsAlive = true;
                Check.True(!HouseholdAccess.HasLivingOccupant(4, people, r => At(Activity.Visit, 8)));
                Check.True(!HouseholdAccess.HasLivingOccupant(4, people, r => At(Activity.Travel, 4)));
                Check.True(HouseholdAccess.HasLivingOccupant(4, people, r => At(Activity.Sleep, 4)));
                people[0].HomeKey = 8;
                Check.True(HouseholdAccess.HasLivingOccupant(4, people, r => At(Activity.Visit, 4)));
                people.Clear();
                Check.True(!HouseholdAccess.HasLivingOccupant(4, people, r => At(Activity.Home, 4)));
            }));
            cases.Add(("Shelter lock survives alarm expiry and save, expires exactly one hour after arrival",()=>{
                var store=new WorldStateStore(3); var town=store.GetOrCreate(1,new[]{4,8},1);
                var victim=town.Households[0].Residents[0]; var runner=town.Households[1].Residents[0];
                var alarm=store.RecordViolence(1,victim.Id,100); var person=store.Witness(alarm.Id,runner.Id,100);
                person.Reaction=CivilianReaction.Flee; person.HasPosition=true;person.BuildingKey=8;person.ReachedRefuge=true;
                store.RecordShelterArrival(alarm.Id,runner.Id,200);
                Check.True(!store.ShelterLocked(1,8,199)); Check.True(store.ShelterLocked(1,8,200));
                Check.True(!store.ShelterLocked(2,8,500)); Check.True(!store.ShelterLocked(1,4,500));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.True(restored.ShelterLocked(1,8,3799.9)); Check.True(!restored.ShelterLocked(1,8,3800));
                restored.RecordShelterArrival(alarm.Id,runner.Id,500);
                Check.True(!restored.ShelterLocked(1,8,3800));
                var old=store.Snapshot();old.ShelterLocks=null; restored.Restore(old);
                Check.True(!restored.ShelterLocked(1,8,500));
            }));
            cases.Add(("Shelter arrival requires a living fleeing occupant and rejects corrupt saved locks atomically",()=>{
                var store=new WorldStateStore(3);var town=store.GetOrCreate(1,new[]{4,8},1);
                var alarm=store.RecordViolence(1,town.Households[0].Residents[0].Id,100);
                var runner=town.Households[1].Residents[0];var person=store.Witness(alarm.Id,runner.Id,100);
                person.Reaction=CivilianReaction.Flee; person.HasPosition=true;person.BuildingKey=-1;
                Check.Throws<ArgumentException>(()=>store.RecordShelterArrival(alarm.Id,runner.Id,200));
                person.BuildingKey=8;person.ReachedRefuge=true;store.RecordShelterArrival(alarm.Id,runner.Id,200);
                var bad=store.Snapshot();bad.ShelterLocks[0].Until=double.NaN;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));Check.True(store.ShelterLocked(1,8,500));
            }));
            cases.Add(("A new fleeing arrival renews the shelter hour without duplicate door records",()=>{
                var store=new WorldStateStore(3);var town=store.GetOrCreate(1,new[]{4,8,12,16},1);
                var runner=town.Households[1].Residents[0];
                int victim=0;
                foreach(double at in new[] {100.0,300.0,5000.0})
                {
                    var alarm=store.RecordViolence(1,town.Households[victim].Residents[0].Id,at);
                    victim= victim==0 ? 2 : 3;
                    var person=store.Witness(alarm.Id,runner.Id,at);
                    person.Reaction=CivilianReaction.Flee;person.HasPosition=true;person.ReachedRefuge=true;person.BuildingKey=8;
                    store.RecordShelterArrival(alarm.Id,runner.Id,at+10);
                    Check.Equal(1,store.Snapshot().ShelterLocks.Count);
                    Check.True(store.ShelterLocked(1,8,at+3609)); Check.True(!store.ShelterLocked(1,8,at+3610));
                }
            }));
            cases.Add(("Alarm location overrides sleeping schedule without mutating saved flight state", () => {
                var home = new Household { BuildingKey = 4 };
                home.Residents.Add(new Resident { Id = "owner", HomeKey = 4 });
                var person = new AlarmPerson { HasPosition = true, BuildingKey = 4, Reaction = CivilianReaction.Flee };
                person.Escape = CivilianEscape.Create(person, 100,
                    new List<GuardPoint3> { new GuardPoint3(), new GuardPoint3 { X = 28 } },
                    new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 28 } });
                Func<double, bool> locked = now => HouseholdAccess.RoutineLocked(home,
                    r => HouseholdAccess.EffectiveState(At(Activity.Sleep, 4), person, now));
                Check.True(!locked(100));
                Check.True(!locked(person.Escape.ExitAt - 0.01));
                Check.True(locked(person.Escape.ExitAt));
                Check.Equal(4, person.BuildingKey);
                Check.Equal(0.0, person.X);
                person.Escape = null; person.BuildingKey = 8;
                Check.True(locked(300));
                person.BuildingKey = 4; person.Reaction = CivilianReaction.Fight;
                Check.True(!locked(300));
            }));
            cases.Add(("House locks when last awake owner leaves sleepers behind", () => {
                var home = new Household { BuildingKey = 4 };
                home.Residents.Add(new Resident { Id = "early", HomeKey = 4 });
                home.Residents.Add(new Resident { Id = "late", HomeKey = 4 });
                var states = new Dictionary<string, ActivitySnapshot> {
                    { "early", At(Activity.Home, 4) }, { "late", At(Activity.Sleep, 4) }
                };
                Check.True(!HouseholdAccess.RoutineLocked(home, r => states[r.Id]));
                states["early"] = At(Activity.Travel, -1);
                Check.True(HouseholdAccess.RoutineLocked(home, r => states[r.Id]));
                states["late"] = At(Activity.Home, 4);
                Check.True(!HouseholdAccess.RoutineLocked(home, r => states[r.Id]));
                states["late"] = At(Activity.Visit, 8);
                Check.True(HouseholdAccess.RoutineLocked(home, r => states[r.Id]));
                states["early"] = At(Activity.Home, 4);
                Check.True(!HouseholdAccess.RoutineLocked(home, r => states[r.Id]));
            }));
            cases.Add(("Dead owners and vacant homes cannot release routine lock", () => {
                var home = new Household { BuildingKey = 4 };
                Check.True(HouseholdAccess.RoutineLocked(home, r => { throw new Exception("No resident expected"); }));
                home.Residents.Add(new Resident { Id = "dead", HomeKey = 4, IsAlive = false });
                Check.True(HouseholdAccess.RoutineLocked(home, r => { throw new Exception("Dead resident must not be evaluated"); }));
            }));
            cases.Add(("House access uses deterministic schedule through sleep departure and arrival boundaries", () => {
                var home = new Household { BuildingKey = 4 };
                home.Residents.Add(new Resident { Id = "owner", HomeKey = 4 });
                var schedule = DailySchedule.Workday(4, 8, new[] { new RoutePoint(0, 0), new RoutePoint(10, 0) },
                    480, 1020, 10, 360, 1320);
                foreach (var day in new[] { 0L, 3000L }) {
                    foreach (var pair in new[] { (359, true), (360, false), (470, true), (1029, true), (1030, false), (1320, true) })
                        Check.Equal(pair.Item2, HouseholdAccess.RoutineLocked(home, r => schedule.Evaluate(day * 1440 + pair.Item1)));
                }
            }));
        }
        private static ActivitySnapshot At(Activity activity, int building)
        { return new ActivitySnapshot(activity, building, new RoutePoint(0, 0), 0); }
    }
}
