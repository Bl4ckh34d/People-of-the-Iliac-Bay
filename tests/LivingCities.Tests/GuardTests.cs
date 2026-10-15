using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class GuardTests
    {
        private static GuardRoster Roster(int count = 4)
        {
            return GuardRoster.Create(1, 42, count, Enumerable.Range(0, count).Select(i => new SavedRoutePoint { X = i * 10 }).ToList());
        }
        private static List<SavedRoutePoint> Route(double from, double to)
        { return new List<SavedRoutePoint> { new SavedRoutePoint { X = from }, new SavedRoutePoint { X = to } }; }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Guard effect snapshot survives validated roster copy without shared mutable state",()=>{
                var roster=Roster(1);
                roster.Guards[0].Effects=new GuardEffectSnapshot { Json="[]",MagicMinute=42 };
                var copy=GuardRoster.CopyValidated(roster);
                Check.Equal("[]",copy.Guards[0].Effects.Json);
                Check.Equal(42u,copy.Guards[0].Effects.MagicMinute);
                copy.Guards[0].Effects.Json="changed";
                Check.Equal("[]",roster.Guards[0].Effects.Json);
                roster.Guards[0].Effects.Json=" ";
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(roster));
                roster.Guards[0].Effects.Json=new string('x',1048577);
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(roster));
                Check.True(Roster(1).Guards[0].Effects==null);
            }));
            cases.Add(("Building defense with unavailable responders remains retryable without a phantom report",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.CacheBuildingApproach(73,new SavedRoutePoint { X=100 },
                    new Dictionary<string,List<SavedRoutePoint>> { { id,Route(0,100) } });
                Check.Equal(0,roster.DispatchBuilding("danger",73,0,0,2,10,threatId:"enemy:1").Count);
                Check.Equal(0,roster.Reports.Count);
                roster.Dispatch("busy",0,new Dictionary<string,List<SavedRoutePoint>> { { id,Route(0,20) } },1,2,10);
                Check.Equal(0,roster.DispatchBuilding("danger",73,1,1,2,10,threatId:"enemy:1").Count);
                Check.Equal(1,roster.Reports.Count);
                roster.Advance(500);
                Check.Equal(1,roster.DispatchBuilding("danger",73,501,1,2,10,threatId:"enemy:1").Count);
                Check.Equal("enemy:1",GuardRoster.CopyValidated(roster).Reports.Find(r=>r.Id=="danger").ThreatId);
            }));
            cases.Add(("Live post reassignment walks the checked route and survives reload",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.MovePost(id,Route(0,20),100,2,10);
                Check.Equal(GuardDuty.Returning,roster.Guards[0].Duty);
                Check.Near(0,roster.Position(id,100).X);
                roster=GuardRoster.CopyValidated(roster);
                Check.Near(10,roster.Position(id,150).X);
                roster.Advance(200); Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
                Check.Near(20,roster.Position(id,200).X);
            }));
            cases.Add(("Indoor pursuer retraces to doorway then resumes exterior pursuit after reload",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.CacheBuildingApproach(73,new SavedRoutePoint { X=100 },
                    new Dictionary<string,List<SavedRoutePoint>> { { id,Route(0,100) } });
                roster.DispatchBuilding("shop",73,0,1,2,10);
                roster.EnterBuilding(id,73,500,new GuardPoint3());
                roster.ObserveInterior(id,new GuardPoint3 { X=10 },0.5,501,true,new GuardPoint3 { X=12 });
                roster.PursueOutside(id,502);
                Check.True(roster.Guards[0].Interior.Returning);
                double exit=roster.Guards[0].Interior.ExitAt;
                Check.True(exit>502);
                roster=GuardRoster.CopyValidated(roster);
                var dead=GuardRoster.CopyValidated(roster); dead.Kill(id,503);
                Check.True(GuardRoster.CopyValidated(dead).Guards[0].Interior.ExteriorPursuit==null);
                var forged=GuardRoster.CopyValidated(roster); forged.Guards[0].Interior.ExteriorPursuit.X+=1;
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(forged));
                Check.Near(5,roster.InteriorPosition(id,(502+exit)/2).X);
                roster.Advance(exit);
                Check.True(roster.Guards[0].Interior==null);
                Check.Equal(GuardDuty.Engaged,roster.Guards[0].Duty);
                Check.Near(100,roster.Position(id,exit).X);
                Check.Near(100,roster.Guards[0].LastSeenSuspect.X);
                Check.Near(0.5,roster.Guards[0].HealthFraction);
                Check.Equal("shop",GuardRoster.CopyValidated(roster).Guards[0].IncidentId);
                roster.ReturnFromEngagement(id,exit+1,Route(100,0),2,10);
                roster.Advance(exit+501); Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
            }));
            cases.Add(("Post reassignment preserves identity and health and rejects active duties",()=>{
                var roster=Roster(1); var guard=roster.Guards[0]; guard.HealthFraction=0.5;
                roster.CacheBuildingApproach(73,new SavedRoutePoint { X=100 },
                    new Dictionary<string,List<SavedRoutePoint>> { { guard.Id,Route(0,100) } });
                string id=guard.Id; uint appearance=guard.AppearanceSeed;
                roster.ReassignPost(id,new SavedRoutePoint { X=20 },0);
                var copy=GuardRoster.CopyValidated(roster);
                Check.Equal(id,copy.Guards[0].Id); Check.Equal(appearance,copy.Guards[0].AppearanceSeed);
                Check.Near(0.5,copy.Guards[0].HealthFraction); Check.Near(20,copy.Position(id,0).X);
                Check.Equal(0,copy.BuildingApproaches.Count);
                copy.Dispatch("busy",0,new Dictionary<string,List<SavedRoutePoint>> { { id,Route(20,100) } },1,2,10);
                Check.Throws<ArgumentException>(()=>copy.ReassignPost(id,new SavedRoutePoint { X=30 },0));
                Check.Near(20,copy.Guards[0].Post.X);
                copy.Kill(id,1);
                Check.Throws<ArgumentException>(()=>copy.ReassignPost(id,new SavedRoutePoint { X=40 },2));
            }));
            cases.Add(("Junction posts require three paved branches and exclude a straight road",()=>{
                Func<int,int,int> cross=(x,z)=>x==0 || z==0 ? 15 : 0;
                Check.True(GuardPostSites.IsJunction(0,0,cross,6));
                Check.True(!GuardPostSites.IsJunction(0,0,(x,z)=>z==0 ? 15 : 0,6));
                Check.True(GuardPostSites.IsJunction(0,0,(x,z)=>4,6));
                Check.True(!GuardPostSites.IsJunction(0,0,(x,z)=>12,6));
            }));
            cases.Add(("Building defense preserves target through delayed entry and return", () => {
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.CacheBuildingApproach(73,new SavedRoutePoint { X=100 },
                    new Dictionary<string,List<SavedRoutePoint>> { { id,Route(0,100) } });
                var sent=roster.DispatchBuilding("hostile-shop",73,0,1,2,10,false,null,"native:123");
                Check.Equal(1,sent.Count); Check.Equal("native:123",roster.Reports[0].ThreatId);
                Check.Throws<ArgumentException>(()=>roster.EnterBuilding(id,73,499,new GuardPoint3()));
                roster=GuardRoster.CopyValidated(roster);
                roster.EnterBuilding(id,73,500,new GuardPoint3());
                roster.ObserveInterior(id,new GuardPoint3 { X=3 },0.5,501,true,new GuardPoint3 { X=5 });
                roster=GuardRoster.CopyValidated(roster);
                Check.Equal("native:123",roster.Reports[0].ThreatId);
                Check.Near(0.5,roster.Guards[0].HealthFraction);
                roster.EndInteriorSearch(id,502);
                double exit=roster.Guards[0].Interior.ExitAt;
                roster.Advance(exit+500);
                Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
            }));
            cases.Add(("Queued building defense retains target and forbids criminal conversion", () => {
                var roster=Roster(1);
                roster.QueueBuildingReport("danger",73,"native:123");
                roster=GuardRoster.CopyValidated(roster);
                Check.Equal("native:123",roster.Reports[0].ThreatId);
                Check.Throws<ArgumentException>(()=>roster.QueueBuildingReport("danger",73));
                Check.Throws<ArgumentException>(()=>roster.DispatchBuilding("danger",73,0,1,2,10));
                Check.Throws<ArgumentException>(()=>roster.DispatchBuilding("other",73,0,1,2,10,true,null,"native:123"));
            }));
            cases.Add(("Queued native building alarm survives save and dispatches only after a surveyed route", () => {
                var roster = Roster(1);
                roster.QueueBuildingReport("native:shop", 73);
                roster.QueueBuildingReport("native:shop", 73);
                Check.Equal(1, roster.Reports.Count);
                Check.Equal(0, roster.DispatchBuilding("native:shop", 73, 10, 1, 2, 10).Count);
                var copy = GuardRoster.CopyValidated(JsonSerializer.Deserialize<GuardRoster>(
                    JsonSerializer.Serialize(roster, new JsonSerializerOptions { IncludeFields = true }),
                    new JsonSerializerOptions { IncludeFields = true }));
                Check.Equal(73, copy.Reports[0].BuildingKey.Value);
                Check.Equal(0, copy.Reports[0].GuardIds.Count);
                Check.Throws<ArgumentException>(() => copy.QueueBuildingReport("native:shop", 74));
                copy.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { copy.Guards[0].Id, Route(0, 100) } });
                Check.Equal(1, copy.DispatchBuilding("native:shop", 73, 100, 1, 2, 10).Count);
                Check.Near(600, copy.Guards[0].ArrivesAt);
                Check.Equal(1, copy.DispatchBuilding("native:shop", 73, 110, 4, 2, 10).Count);
                Check.Near(600, copy.Guards[0].ArrivesAt);
                GuardRoster.CopyValidated(copy);
            }));
            cases.Add(("Queued building report validation rejects fake responders and missing queue markers", () => {
                var roster = Roster(1); roster.QueueBuildingReport("native:shop", 73);
                var broken = GuardRoster.CopyValidated(roster);
                broken.Reports[0].GuardIds.Add(broken.Guards[0].Id);
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Reports[0].AwaitingApproach = false;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Reports[0].Investigation = true;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Reports[0].Entrance = new SavedRoutePoint();
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                Check.Throws<ArgumentException>(() => roster.DispatchBuilding("native:shop", 73, 1, 1, 2, 10, true));
                Check.Equal(1, roster.Reports.Count);
            }));
            cases.Add(("Building alarm dispatches only guards who received the report", () => {
                var roster = Roster();
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100)));
                var informed = new HashSet<string> { roster.Guards[0].Id };
                var sent = roster.DispatchBuilding("witnessed-entry", 73, 0, 4, 2, 12, false, informed);
                Check.Equal(1, sent.Count);
                Check.Equal(roster.Guards[0].Id, sent[0].Id);
                Check.True(roster.Guards.Skip(1).All(g => g.Duty == GuardDuty.Posted));
                var copy = GuardRoster.CopyValidated(roster);
                Check.Equal(1, copy.Reports.Single().GuardIds.Count);
                var none = Roster();
                none.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    none.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100)));
                Check.Equal(0, none.DispatchBuilding("unseen-entry", 73, 0, 4, 2, 12, false, new HashSet<string>()).Count);
            }));
            cases.Add(("Indoor guard entry waits for physical arrival and saves its interior coordinates", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } });
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                Check.Throws<ArgumentException>(() => roster.EnterBuilding(id, 73, 499, new GuardPoint3 { Y = 2 }));
                Check.Throws<ArgumentException>(() => roster.EnterBuilding(id, 74, 500, new GuardPoint3 { Y = 2 }));
                roster.EnterBuilding(id, 73, 500, new GuardPoint3 { Y = 2 });
                roster.ObserveInterior(id, new GuardPoint3 { X = 5, Y = 3 }, 0.5, 510, true, new GuardPoint3 { X = 8, Y = 3 });
                var copy = GuardRoster.CopyValidated(roster);
                Check.Equal(73, copy.Guards[0].Interior.BuildingKey);
                Check.Near(5, copy.InteriorPosition(id, 520).X); Check.Near(3, copy.InteriorPosition(id, 520).Y);
                Check.Near(810, copy.Guards[0].SearchUntil); Check.Near(0.5, copy.Guards[0].HealthFraction);
                Check.Near(100, copy.Position(id, 520).X);
            }));
            cases.Add(("Indoor guards retrace their path before starting exterior return", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } });
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                roster.EnterBuilding(id, 73, 500, new GuardPoint3());
                roster.ObserveInterior(id, new GuardPoint3 { X = 10 }, 1, 501, false, null);
                roster.Advance(800);
                Check.True(roster.Guards[0].Interior.Returning);
                Check.Near(10, roster.InteriorPosition(id, 800).X);
                double exit = roster.Guards[0].Interior.ExitAt;
                Check.Near(5, roster.InteriorPosition(id, (800 + exit) / 2).X);
                var restored = GuardRoster.CopyValidated(roster);
                restored.Advance(exit);
                Check.True(restored.Guards[0].Interior == null);
                Check.Equal(GuardDuty.Returning, restored.Guards[0].Duty);
                Check.Near(100, restored.Position(id, exit).X);
                restored.Advance(exit + 500);
                Check.Equal(GuardDuty.Posted, restored.Guards[0].Duty);
            }));
            cases.Add(("Indoor stand-down returns through the entrance before the original search deadline", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } });
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                roster.EnterBuilding(id, 73, 500, new GuardPoint3());
                roster.ObserveInterior(id, new GuardPoint3 { X = 10 }, 1, 501, true, new GuardPoint3 { X = 12 });
                roster.EndInteriorSearch(id, 502);
                Check.True(roster.Guards[0].Interior.Returning); Check.Near(502, roster.Guards[0].Interior.ReturnAt);
                Check.Near(10, GuardRoster.CopyValidated(roster).InteriorPosition(id, 502).X);
                Check.Throws<ArgumentException>(() => roster.ObserveInterior(id, new GuardPoint3(), 1, 503, true, new GuardPoint3()));
            }));
            cases.Add(("Late indoor materialization preserves deadline and death preserves current room position", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } });
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                roster.EnterBuilding(id, 73, 700, new GuardPoint3());
                Check.Near(800, roster.Guards[0].SearchUntil);
                roster.Guards[0].Interior.Position = new GuardPoint3 { X = 7, Y = 2, Z = 3 };
                roster.Kill(id, 701);
                var copy = GuardRoster.CopyValidated(roster);
                Check.Near(7, copy.InteriorPosition(id, 9999).X);
                Check.Near(2, copy.InteriorPosition(id, 9999).Y);
                Check.True(!copy.Guards[0].IsAlive);
            }));
            cases.Add(("Building responses retain destination and travel deadline through reload", () => {
                var roster = Roster(2);
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 }, routes);
                routes[roster.Guards[0].Id][1].X = 999;
                var sent = roster.DispatchBuilding("shop", 73, 10, 1, 2, 10);
                Check.Equal(1, sent.Count); Check.Equal(roster.Guards[1].Id, sent[0].Id);
                Check.Near(460, sent[0].ArrivesAt);
                var copy = GuardRoster.CopyValidated(roster);
                Check.Equal(73, copy.Reports[0].BuildingKey.Value);
                Check.Near(100, copy.Reports[0].Entrance.X);
                Check.Near(60, copy.Position(sent[0].Id, 260).X);
                Check.Equal(1, copy.DispatchBuilding("shop", 73, 270, 4, 2, 10).Count);
                Check.Near(460, copy.Guards[1].ArrivesAt);
            }));
            cases.Add(("Building report cannot retarget or dispatch to mismatched door endpoints", () => {
                var roster = Roster(2);
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 }, routes);
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                Check.Throws<ArgumentException>(() => roster.DispatchBuilding("shop", 74, 1, 1, 2, 10));
                Check.Throws<ArgumentException>(() => roster.CacheBuildingApproach(74, new SavedRoutePoint { X = 101 }, routes));
                Check.Equal(1, roster.BuildingApproaches.Count); Check.Equal(1, roster.Reports.Count);
                var broken = GuardRoster.CopyValidated(roster); broken.Reports[0].Entrance.X = double.NaN;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
            }));
            cases.Add(("Missing building route dispatches nobody and corrupt cached origins reject restore", () => {
                var roster = Roster(1);
                Check.Equal(0, roster.DispatchBuilding("unknown", 99, 0, 4, 2, 10).Count);
                Check.Equal(0, roster.Reports.Count);
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { roster.Guards[0].Id, Route(0, 100) } });
                var copy = GuardRoster.CopyValidated(roster);
                copy.BuildingApproaches[0].Routes[0].Points[0].X = 50;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(copy));
                Check.Equal(1, roster.DispatchBuilding("shop", 73, 0, 1, 2, 10).Count);
            }));
            cases.Add(("Recruited guard uses the saved post approach without reviving its predecessor", () => {
                var roster = Roster(1); string oldId = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { oldId, Route(0, 100) } });
                roster.Kill(oldId, 1); double now = 1 + 7 * 86400;
                Check.Equal(1, roster.Recruit(now));
                var restored = GuardRoster.CopyValidated(roster);
                var sent = restored.DispatchBuilding("later", 73, now, 4, 2, 10);
                Check.Equal(1, sent.Count); Check.True(sent[0].Id != oldId);
                Check.True(!restored.Guards[0].IsAlive); Check.Near(now + 500, sent[0].ArrivesAt);
                var legacy = Roster(1); legacy.BuildingApproaches = null;
                Check.Equal(0, GuardRoster.CopyValidated(legacy).BuildingApproaches.Count);
            }));
            cases.Add(("Guard engagement preserves actual position and wounded health through reload", () => {
                var roster = Roster(1);
                roster.Dispatch("fight", 10, new Dictionary<string, List<SavedRoutePoint>> { { roster.Guards[0].Id, Route(0, 50) } }, 1, 2, 12);
                roster.Engage(roster.Guards[0].Id, 20);
                roster.ObserveEngagement(roster.Guards[0].Id, new SavedRoutePoint { X = 4, Z = 3 }, 0.4, 30, true, new SavedRoutePoint { X = 9 });
                var saved = GuardRoster.CopyValidated(roster);
                Check.Equal(GuardDuty.Engaged, saved.Guards[0].Duty);
                Check.Equal(4d, saved.Position(saved.Guards[0].Id, 40).X);
                Check.Equal(0.4, saved.Guards[0].HealthFraction);
                Check.Equal(330d, saved.Guards[0].SearchUntil);
                saved.ObserveEngagement(saved.Guards[0].Id, new SavedRoutePoint { X = 5 }, 0.3, 50, false, new SavedRoutePoint { X = 99 });
                Check.Equal(9d, GuardRoster.CopyValidated(saved).Guards[0].LastSeenSuspect.X);
                Check.Equal(330d, saved.Guards[0].SearchUntil);
                Check.Equal(0.3, GuardRoster.CopyValidated(saved).Guards[0].HealthFraction);
            }));
            cases.Add(("Guard return starts from actual engagement position without teleporting", () => {
                var roster = Roster(1); string id = roster.Guards[0].Id;
                roster.Dispatch("fight", 10, new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 50) } }, 1, 2, 12);
                roster.Engage(id, 20);
                roster.ObserveEngagement(id, new SavedRoutePoint { X = 30 }, 0.5, 30, false);
                Check.Throws<ArgumentException>(() => roster.ReturnFromEngagement(id, 40, Route(99, 0), 2, 12));
                Check.Equal(GuardDuty.Engaged, roster.Guards[0].Duty);
                roster.ReturnFromEngagement(id, 40, Route(30, 0), 2, 12);
                Check.Equal(30d, roster.Position(id, 40).X);
                Check.Equal(15d, roster.Position(id, 130).X);
                Check.Equal(GuardDuty.Returning, GuardRoster.CopyValidated(roster).Guards[0].Duty);
                roster.Advance(220);
                Check.Equal(GuardDuty.Posted, roster.Guards[0].Duty);
                Check.Equal(0.5, roster.Guards[0].HealthFraction);
            }));
            cases.Add(("Guard policy uses eight percent with one tiny-hamlet guard and larger settlement floors", () => {
                Check.Equal(1, GuardRoster.TargetCount(SettlementScale.Hamlet, 12));
                Check.Equal(8, GuardRoster.TargetCount(SettlementScale.Town, 30));
                Check.Equal(16, GuardRoster.TargetCount(SettlementScale.City, 80));
                Check.Equal(80, GuardRoster.TargetCount(SettlementScale.City, 1000));
                Check.Equal(1, GuardRoster.TargetCount(SettlementScale.Hamlet, 39));
                Check.Equal(8, GuardRoster.TargetCount(SettlementScale.Hamlet, 40));
                Check.Equal(15, GuardRoster.TargetCount(SettlementScale.Town, 181));
                Check.Equal(350, GuardRoster.TargetCount(SettlementScale.City, int.MaxValue));
            }));
            cases.Add(("Roster has permanent unique deterministic identities", () => {
                var a = Roster(); var b = Roster();
                Check.Equal(4, a.Guards.Count); Check.Equal(4, a.Guards.Select(g => g.Id).Distinct().Count());
                Check.Equal(string.Join(",", a.Guards.Select(g => g.Id)), string.Join(",", b.Guards.Select(g => g.Id)));
            }));
            cases.Add(("Dispatch picks nearest reachable available guards and deduplicates a report", () => {
                var roster = Roster();
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                var sent = roster.Dispatch("theft-1", 0, routes, 2, 2, 12);
                Check.Equal(2, sent.Count); Check.Equal(roster.Guards[3].Id, sent[0].Id);
                Check.Near(420, sent[0].ArrivesAt);
                Check.Equal(2, roster.Dispatch("theft-1", 1, routes, 4, 2, 12).Count);
                Check.Equal(2, roster.Guards.Count(g => g.Duty == GuardDuty.Responding));
                var second = roster.Dispatch("theft-2", 1, routes, 4, 2, 12);
                Check.Equal(2, second.Count);
                Check.Equal(0, roster.Dispatch("theft-3", 2, routes, 4, 2, 12).Count);
            }));
            cases.Add(("Guard location advances with game time, searches, then returns along its route", () => {
                var roster = Roster(1); var guard = roster.Guards[0];
                roster.Dispatch("alarm", 100, new Dictionary<string, List<SavedRoutePoint>> { { guard.Id, Route(0, 100) } }, 1, 2, 10);
                Check.Near(50, roster.Position(guard.Id, 350).X);
                roster.Advance(600); Check.Equal(GuardDuty.Searching, guard.Duty);
                roster.Advance(900); Check.Equal(GuardDuty.Returning, guard.Duty);
                Check.Near(50, roster.Position(guard.Id, 1150).X);
                roster.Advance(1400); Check.Equal(GuardDuty.Posted, guard.Duty); Check.Near(0, roster.Position(guard.Id, 1400).X);
                Check.Equal(1, roster.Dispatch("alarm", 1500, new Dictionary<string, List<SavedRoutePoint>>(), 4, 2, 10).Count);
                Check.Equal(GuardDuty.Posted, guard.Duty);
            }));
            cases.Add(("Guard deaths persist and recruitment is delayed, bounded and creates new identities", () => {
                var roster = Roster(); var ids = roster.Guards.Select(g => g.Id).ToArray();
                foreach (var id in ids) roster.Kill(id, 100);
                Check.Equal(0, roster.Recruit(100 + 7 * 86400 - 1));
                Check.Equal(1, roster.Recruit(100 + 7 * 86400));
                Check.Equal(0, roster.Recruit(100 + 7 * 86400));
                Check.Equal(1, roster.Recruit(100 + 8 * 86400));
                Check.True(roster.Guards.Where(g => g.IsAlive).All(g => !ids.Contains(g.Id)));
                Check.Equal(4, roster.Guards.Count(g => !g.IsAlive));
            }));
            cases.Add(("Missing routes and dead guards never create reinforcements", () => {
                var roster = Roster(); roster.Kill(roster.Guards[0].Id, 0);
                var routes = new Dictionary<string, List<SavedRoutePoint>> { { roster.Guards[0].Id, Route(0, 5) } };
                Check.Equal(0, roster.Dispatch("empty", 1, routes, 4, 2, 12).Count);
                Check.Equal(4, roster.Guards.Count);
            }));
            cases.Add(("A guard killed mid-response remains at the death location", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.Dispatch("attack", 0, new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } }, 1, 2, 10);
                roster.Kill(id, 250);
                Check.Near(50, roster.Position(id, 1000).X);
                Check.Near(50, GuardRoster.CopyValidated(roster).Position(id, 2000).X);
            }));
            cases.Add(("Guard restore rejects impossible search timers and route endpoints", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.Dispatch("alarm", 100, new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } }, 1, 2, 10);
                roster.Guards[0].SearchUntil = 599;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(roster));
                roster.Guards[0].SearchUntil = 900; roster.Guards[0].Route[0].X = 20;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(roster));
            }));
            cases.Add(("Schema five migrates without inventing garrisons", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 1 });
                var older = store.Snapshot(); older.Version = 5; older.Garrisons = null;
                store.Restore(older); Check.Equal(6, store.Snapshot().Version); Check.Equal(0, store.Snapshot().Garrisons.Count);
            }));
            cases.Add(("Bad guard routes reject transactionally without partial dispatch", () => {
                var roster = Roster();
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                routes[roster.Guards[3].Id][1].X = double.NaN;
                Check.Throws<ArgumentException>(() => roster.Dispatch("bad", 0, routes, 4, 2, 12));
                Check.True(roster.Guards.All(g => g.Duty == GuardDuty.Posted));
                Check.Equal(0, roster.Reports.Count);
            }));
            cases.Add(("Invalid route origin does not advance another guard before rejecting", () => {
                var roster = Roster(2); var first = roster.Guards[0]; var second = roster.Guards[1];
                roster.Dispatch("valid", 0, new Dictionary<string, List<SavedRoutePoint>> { { first.Id, Route(0, 100) } }, 1, 2, 10);
                Check.Throws<ArgumentException>(() => roster.Dispatch("invalid", 600,
                    new Dictionary<string, List<SavedRoutePoint>> { { second.Id, Route(999, 100) } }, 1, 2, 10));
                Check.Equal(GuardDuty.Responding, first.Duty); Check.Equal(1, roster.Reports.Count);
            }));
            cases.Add(("A pending report can use newly reachable guards without duplicating responders", () => {
                var roster = Roster(1); var guard = roster.Guards[0];
                Check.Equal(0, roster.Dispatch("pending", 0, new Dictionary<string, List<SavedRoutePoint>>(), 1, 2, 10).Count);
                var route = new Dictionary<string, List<SavedRoutePoint>> { { guard.Id, Route(0, 100) } };
                Check.Equal(1, roster.Dispatch("pending", 10, route, 1, 2, 10).Count);
                Check.Equal(1, roster.Dispatch("pending", 20, route, 1, 2, 10).Count);
                Check.Equal(1, roster.Reports.Count); Check.Equal(1, roster.Guards.Count);
            }));
            cases.Add(("Guard roster snapshot preserves journeys, deaths and exhausted report identities", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 1 });
                var roster = store.GetOrCreateGuards(1, 4, Enumerable.Range(0, 4).Select(i => new SavedRoutePoint { X = i * 10 }).ToList());
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                roster.Dispatch("saved", 100, routes, 2, 2, 12); roster.Kill(roster.Guards[0].Id, 150);
                var options = new JsonSerializerOptions { IncludeFields = true };
                string json = JsonSerializer.Serialize(store.Snapshot(), options);
                var restored = new WorldStateStore(0); restored.Restore(JsonSerializer.Deserialize<WorldState>(json, options));
                Check.Equal(json, JsonSerializer.Serialize(restored.Snapshot(), options));
                var same = restored.GetOrCreateGuards(1, 99, new List<SavedRoutePoint>());
                Check.Equal(4, same.Guards.Count); Check.Equal(2, same.Dispatch("saved", 160, routes, 4, 2, 12).Count);
                var broken = restored.Snapshot(); broken.Garrisons[0].Guards[1].Id = broken.Garrisons[0].Guards[0].Id;
                Check.Throws<ArgumentException>(() => restored.Restore(broken));
                Check.Equal(json, JsonSerializer.Serialize(restored.Snapshot(), options));
            }));
        }
    }
}
