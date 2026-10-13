using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class ScheduleTests
    {
        private static DailySchedule Work(IEnumerable<RoutePoint> route = null) => DailySchedule.Workday(10, 20,
            route ?? new[] { new RoutePoint(0, 0), new RoutePoint(100, 0) }, 480, 1020, 20, 360, 1320);
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Travel advances within a game minute and between fractional seconds", () => {
                var p = Work();
                Check.Near(52.5, p.EvaluateSeconds(470 * 60 + 30).Position.X);
                Check.Near(50 + 0.5 / 12, p.EvaluateSeconds(470 * 60 + 0.5).Position.X);
                Check.Near(47.5, p.EvaluateSeconds(1030 * 60 + 30).Position.X);
            }));
            cases.Add(("Second-resolution schedule preserves exact arrival and midnight boundaries", () => {
                var p = Work();
                Check.Equal(Activity.Travel, p.EvaluateSeconds(480 * 60 - 0.1).Activity);
                Check.Equal(Activity.Work, p.EvaluateSeconds(480 * 60).Activity);
                Check.Equal(Activity.Travel, p.EvaluateSeconds(1040 * 60 - 0.1).Activity);
                Check.Equal(Activity.Home, p.EvaluateSeconds(1040 * 60).Activity);
                Check.Equal(Activity.Sleep, p.EvaluateSeconds(86400).Activity);
                Check.Near(p.EvaluateSeconds(470 * 60 + 0.5).Position.X,
                    p.EvaluateSeconds(86400d * 1000000 + 470 * 60 + 0.5).Position.X);
            }));
            cases.Add(("Invalid second-resolution times are rejected", () => {
                foreach (double time in new[] { -0.1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                    Check.Throws<ArgumentOutOfRangeException>(() => Work().EvaluateSeconds(time));
            }));
            cases.Add(("Sleep and waking respect exact boundaries", () => {
                var p = Work();
                Check.Equal(Activity.Sleep, p.Evaluate(359).Activity);
                Check.Equal(Activity.Home, p.Evaluate(360).Activity);
                Check.Equal(Activity.Home, p.Evaluate(1319).Activity);
                Check.Equal(Activity.Sleep, p.Evaluate(1320).Activity);
                Check.Equal(10, p.Evaluate(0).BuildingKey);
            }));
            cases.Add(("Commutes arrive at opening and leave at closing", () => {
                var p = Work();
                Check.Equal(Activity.Home, p.Evaluate(459).Activity);
                Check.Equal(Activity.Travel, p.Evaluate(460).Activity);
                Check.Equal(-1, p.Evaluate(470).BuildingKey);
                Check.Near(50, p.Evaluate(470).Position.X);
                Check.Equal(Activity.Work, p.Evaluate(480).Activity);
                Check.Equal(20, p.Evaluate(1019).BuildingKey);
                Check.Equal(Activity.Travel, p.Evaluate(1020).Activity);
                Check.Near(100, p.Evaluate(1020).Position.X);
                Check.Near(50, p.Evaluate(1030).Position.X);
                Check.Equal(Activity.Home, p.Evaluate(1040).Activity);
                Check.Equal(10, p.Evaluate(1040).BuildingKey);
            }));
            cases.Add(("Unloaded time jumps produce the same schedule state", () => {
                var p = Work();
                p.Evaluate(470);
                Check.Equal(Activity.Work, p.Evaluate(900).Activity);
                Check.Near(p.Evaluate(470).Position.X, p.Evaluate(1440L * 1000000 + 470).Position.X);
                Check.Equal(Activity.Sleep, p.Evaluate(1440 + 270).Activity);
                Check.Equal(Activity.Sleep, p.Evaluate(1440).Activity);
            }));
            cases.Add(("Travel interpolation uses distance rather than waypoint count", () => {
                var p = Work(new[] { new RoutePoint(0, 0), new RoutePoint(10, 0), new RoutePoint(10, 90) });
                Check.Near(10, p.Evaluate(470).Position.X);
                Check.Near(40, p.Evaluate(470).Position.Z);
                Check.Near(0.5, p.Evaluate(470).RouteProgress);
            }));
            cases.Add(("Schedules copy routes rather than retaining mutable input", () => {
                var points = new[] { new RoutePoint(0, 0), new RoutePoint(100, 0) };
                var p = Work(points); points[1] = new RoutePoint(900, 0);
                Check.Near(50, p.Evaluate(470).Position.X);
            }));
            cases.Add(("No-route residents stay home or sleep", () => {
                var p = DailySchedule.HomeDay(10, 360, 1320);
                Check.Equal(Activity.Home, p.Evaluate(900).Activity);
                Check.Equal(Activity.Sleep, p.Evaluate(1500).Activity);
                Check.Equal(10, p.Evaluate(900).BuildingKey);
            }));
            cases.Add(("Impossible schedule windows are rejected", () => {
                var route = new[] { new RoutePoint(0, 0), new RoutePoint(100, 0) };
                Check.Throws<ArgumentException>(() => DailySchedule.Workday(10, 20, route, 370, 1020, 20, 360, 1320));
                Check.Throws<ArgumentException>(() => DailySchedule.Workday(10, 20, route, 480, 1310, 20, 360, 1320));
                Check.Throws<ArgumentException>(() => DailySchedule.Workday(10, 20, route, 480, 460, 20, 360, 1320));
                Check.Throws<ArgumentException>(() => DailySchedule.Workday(10, 20, route, 480, 1020, 0, 360, 1320));
                Check.Throws<ArgumentException>(() => DailySchedule.HomeDay(10, -1, 1320));
                Check.Throws<ArgumentException>(() => DailySchedule.HomeDay(10, 360, 1440));
                Check.Throws<ArgumentException>(() => DailySchedule.HomeDay(10, 1320, 360));
            }));
            cases.Add(("Invalid or empty routes are rejected", () => {
                Check.Throws<ArgumentException>(() => Work(new RoutePoint[0]));
                Check.Throws<ArgumentException>(() => Work(new[] { new RoutePoint(0, 0), new RoutePoint(0, 0) }));
                Check.Throws<ArgumentException>(() => Work(new[] { new RoutePoint(double.NaN, 0), new RoutePoint(1, 0) }));
                Check.Throws<ArgumentException>(() => Work(new[] { new RoutePoint(0, 0), new RoutePoint(double.PositiveInfinity, 0) }));
            }));
            cases.Add(("Negative absolute time is rejected", () => Check.Throws<ArgumentOutOfRangeException>(() => Work().Evaluate(-1))));
        }
    }
}
