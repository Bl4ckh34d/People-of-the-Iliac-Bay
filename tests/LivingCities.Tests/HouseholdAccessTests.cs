using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class HouseholdAccessTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
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
