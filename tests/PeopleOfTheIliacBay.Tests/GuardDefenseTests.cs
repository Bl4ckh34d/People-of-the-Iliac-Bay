using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class GuardDefenseTests
    {
        private static SavedRoutePoint P(double x, double z = 0) { return new SavedRoutePoint { X = x, Z = z }; }
        private static List<SavedRoutePoint> Path(params SavedRoutePoint[] points) { return new List<SavedRoutePoint>(points); }
        private static GuardRoster Roster() { return GuardRoster.Create(1, 42, 2, new[] { P(0), P(20) }); }
        private static Dictionary<string, List<SavedRoutePoint>> Routes(GuardRoster roster)
        { return new Dictionary<string, List<SavedRoutePoint>> { { roster.Guards[0].Id, Path(P(0), P(10)) }, { roster.Guards[1].Id, Path(P(20), P(10)) } }; }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Enemy defense saves target identity and finite response without changing ordinary report defaults", () => {
                var roster = Roster(); var sent = roster.DispatchDefense("defense:1", "enemy:99", 10, Routes(roster), 1, 2, 12);
                Check.Equal(1, sent.Count); Check.Equal(2, roster.Guards.Count);
                Check.Equal("enemy:99", roster.Reports[0].ThreatId); Check.Near(70, sent[0].ArrivesAt);
                var json = JsonSerializer.Serialize(roster, new JsonSerializerOptions { IncludeFields = true });
                var copy = GuardRoster.CopyValidated(JsonSerializer.Deserialize<GuardRoster>(json, new JsonSerializerOptions { IncludeFields = true }));
                Check.Equal("enemy:99", copy.Reports[0].ThreatId); Check.Equal(sent[0].Id, copy.Reports[0].GuardIds[0]);
                Check.Equal(null, new GuardReport().ThreatId);
            }));
            cases.Add(("Repeated defense does not duplicate responders or retarget a saved report", () => {
                var roster = Roster(); var routes = Routes(roster);
                var sent = roster.DispatchDefense("defense:1", "enemy:99", 10, routes, 1, 2, 12);
                Check.Equal(1, roster.DispatchDefense("defense:1", "enemy:99", 20, routes, 2, 2, 12).Count);
                Check.Near(70, sent[0].ArrivesAt); Check.Equal(1, roster.Reports.Count);
                Check.Throws<ArgumentException>(() => roster.DispatchDefense("defense:1", "enemy:100", 20, routes, 2, 2, 12));
                Check.Throws<ArgumentException>(() => roster.Dispatch("defense:1", 20, routes, 2, 2, 12));
                Check.Equal("enemy:99", roster.Reports[0].ThreatId); Check.Near(70, sent[0].ArrivesAt);
            }));
            cases.Add(("Defense cannot consume guards assigned to player crimes or overwrite their report", () => {
                var roster = Roster(); var routes = Routes(roster);
                roster.Dispatch("crime:1", 10, routes, 2, 2, 12);
                Check.Throws<ArgumentException>(() => roster.DispatchDefense("crime:1", "enemy:99", 20, routes, 2, 2, 12));
                Check.Equal(0, roster.DispatchDefense("defense:1", "enemy:99", 20, routes, 2, 2, 12).Count);
                Check.Equal(1, roster.Reports.Count); Check.Equal(null, roster.Reports[0].ThreatId);
                foreach (var guard in roster.Guards) Check.Equal("crime:1", guard.IncidentId);
            }));
            cases.Add(("Unreachable defense creates no report or responder and rejects empty target identity", () => {
                var roster = Roster(); var routes = new Dictionary<string, List<SavedRoutePoint>>();
                Check.Throws<ArgumentException>(() => roster.DispatchDefense("defense:1", " ", 10, routes, 1, 2, 12));
                Check.Throws<ArgumentException>(() => roster.DispatchDefense("defense:1", new string('a',129), 10, routes, 1, 2, 12));
                Check.Equal(0, roster.DispatchDefense("defense:1", "enemy:99", 10, routes, 1, 2, 12).Count);
                Check.Equal(0, roster.Reports.Count); Check.Equal(GuardDuty.Posted, roster.Guards[0].Duty);
            }));
            cases.Add(("Defense interrupts a patrol at actual position and saved engagement returns physically", () => {
                var roster = GuardRoster.Create(1, 42, 2, new[] { P(-10), P(2,-20) });
                roster.StartPatrol(PatrolPairPlan.Create(roster.Guards[0].Id, roster.Guards[1].Id,
                    Path(P(-10),P(0)), Path(P(2,-20),P(2)), Path(P(0),P(20),P(20,20),P(0,20),P(0)), 100,1000,2,10,2),100);
                var guard = roster.Guards[0]; var actual = roster.Position(guard.Id,250);
                var routes = new Dictionary<string,List<SavedRoutePoint>> { { guard.Id, Path(actual,P(40)) } };
                roster.DispatchDefense("defense:1","enemy:99",250,routes,1,2,10);
                Check.Near(actual.X, guard.ResponseOrigin.X); Check.True(roster.Patrols[0].InterruptedAt.HasValue);
                roster.Engage(guard.Id,guard.ArrivesAt);
                roster.ObserveEngagement(guard.Id,P(41),0.5,guard.ArrivesAt+1,true,P(42));
                var copy = GuardRoster.CopyValidated(roster); var saved = copy.Guards[0];
                Check.Equal("enemy:99",copy.Reports[0].ThreatId); Check.Near(0.5,saved.HealthFraction);
                copy.ReturnFromEngagement(saved.Id,saved.ArrivesAt+2,Path(P(41),saved.Post),2,10);
                Check.Equal(GuardDuty.Returning,saved.Duty); Check.Near(41,copy.Position(saved.Id,saved.DepartedAt).X);
                copy.Advance(saved.ArrivesAt); Check.Equal(GuardDuty.Posted,saved.Duty); Check.Near(-10,copy.Position(saved.Id,saved.ArrivesAt).X);
            }));
            cases.Add(("Defense save rejects investigation and incomplete building targets transactionally", () => {
                var roster = Roster(); roster.DispatchDefense("defense:1","enemy:99",10,Routes(roster),1,2,12);
                var copy=GuardRoster.CopyValidated(roster); copy.Reports[0].ThreatId="";
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(copy));
                copy=GuardRoster.CopyValidated(roster); copy.Reports[0].Investigation=true;
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(copy));
                copy=GuardRoster.CopyValidated(roster); copy.Reports[0].BuildingKey=73;
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(copy));
                Check.Equal("enemy:99",roster.Reports[0].ThreatId); Check.Equal(null,roster.Reports[0].BuildingKey);
            }));
        }
    }
}
