using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class GuardPatrolTests
    {
        private static SavedRoutePoint P(double x, double z = 0) { return new SavedRoutePoint { X = x, Z = z }; }
        private static List<SavedRoutePoint> Path(params SavedRoutePoint[] p) { return new List<SavedRoutePoint>(p); }
        private static GuardRoster Roster()
        { return GuardRoster.Create(1, 42, 2, new[] { P(-10), P(2, -20) }); }
        private static void Start(GuardRoster roster)
        {
            roster.StartPatrol(PatrolPairPlan.Create(roster.Guards[0].Id, roster.Guards[1].Id,
                Path(P(-10), P(0)), Path(P(2, -20), P(2)),
                Path(P(0), P(20), P(20, 20), P(0, 20), P(0)), 100, 1000, 2, 10, 2), 100);
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Blocked witness retreats along surveyed streets without inventing a guard contact",()=>{
                var roster=GuardRoster.Create(1,42,2,new[]{P(6),P(-20)});
                roster.CacheBuildingApproach(7,P(0),new Dictionary<string,List<SavedRoutePoint>> {
                    {roster.Guards[0].Id,Path(P(6),P(0))},
                    {roster.Guards[1].Id,Path(P(-20),P(-4),P(0))} });
                roster.Kill(roster.Guards[1].Id,100);
                var retreat=roster.PlanWitnessRoute(7,100,null,P(2),true);
                Check.True(retreat!=null && retreat.GuardId==null);
                Check.Near(-4,retreat.Route[retreat.Route.Count-1].X);
                Check.Equal(0,roster.GuardsInReportingRange(retreat.Route[retreat.Route.Count-1],100).Count);
                Check.Equal(0,roster.Reports.Count);
                var trapped=roster.PlanWitnessRoute(7,100,P(-20),P(-18),true);
                Check.True(trapped!=null && trapped.GuardId==null);
                Check.Near(0,trapped.Length);
                Check.Equal(2,trapped.Route.Count);
                Check.Equal(roster.Guards[0].Id,roster.PlanWitnessRoute(7,100,P(-4)).GuardId);
            }));
            cases.Add(("Witness routing avoids the attacker even when the unsafe guard is closer",()=>{
                var roster=GuardRoster.Create(1,42,2,new[]{P(6),P(-20)});
                roster.CacheBuildingApproach(7,P(0),new Dictionary<string,List<SavedRoutePoint>> {
                    {roster.Guards[0].Id,Path(P(6),P(0))},
                    {roster.Guards[1].Id,Path(P(-20),P(0))} });
                Check.Equal(roster.Guards[0].Id,roster.PlanWitnessRoute(7,100).GuardId);
                var safe=roster.PlanWitnessRoute(7,100,null,P(2));
                Check.Equal(roster.Guards[1].Id,safe.GuardId);
                Check.Near(20,safe.Length);
                Check.Equal(0,roster.Reports.Count);
                Check.Equal(safe.GuardId,GuardRoster.CopyValidated(roster).PlanWitnessRoute(7,100,null,P(2)).GuardId);
                roster.Kill(roster.Guards[1].Id,100);
                Check.True(roster.PlanWitnessRoute(7,100,null,P(2))==null);
                Check.Throws<ArgumentException>(()=>roster.PlanWitnessRoute(7,100,null,P(double.NaN)));
            }));
            cases.Add(("Witness can detour around danger along surveyed streets",()=>{
                var roster=GuardRoster.Create(1,42,2,new[]{P(20),P(-20)});
                roster.CacheBuildingApproach(7,P(0),new Dictionary<string,List<SavedRoutePoint>> {
                    {roster.Guards[0].Id,Path(P(20),P(20,20),P(0,20),P(0))},
                    {roster.Guards[1].Id,Path(P(-20),P(0))} });
                roster.Kill(roster.Guards[1].Id,100);
                var plan=roster.PlanWitnessRoute(7,100,null,P(5));
                Check.True(plan!=null);
                Check.Near(60,plan.Length);
                for(int i=1;i<plan.Route.Count;i++)
                    Check.True(FlightSafety.SafeStep(plan.Route[i-1].X,plan.Route[i-1].Z,
                        plan.Route[i].X,plan.Route[i].Z,5,0));
                Check.Near(0,roster.PlanWitnessRoute(7,100,P(20),P(5)).Length);
            }));
            cases.Add(("Guards remember a wanted suspect after returning to post without teaching uninformed guards",()=>{
                var roster=Roster(); string id=roster.Guards[0].Id;
                Check.True(!roster.KnowsPlayerCrime(id));
                roster.Dispatch("crime",100,new Dictionary<string,List<SavedRoutePoint>> {
                    {id,Path(P(-10),P(0))} },1,2,10);
                Check.True(roster.KnowsPlayerCrime(id));
                roster.Advance(455);
                Check.Equal(GuardDuty.Returning,roster.Guards[0].Duty);
                Check.True(roster.KnowsPlayerCrime(id));
                roster.Advance(500);
                Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
                Check.True(GuardRoster.CopyValidated(roster).KnowsPlayerCrime(id));
                Check.True(!roster.KnowsPlayerCrime(roster.Guards[1].Id));
                roster.Reports[0].ThreatId="rat";
                Check.True(!roster.KnowsPlayerCrime(id));
                roster.Reports[0].ThreatId=null; roster.Reports[0].Investigation=true;
                Check.True(!roster.KnowsPlayerCrime(id));
                roster.Reports[0].Investigation=false; roster.Kill(id,501);
                Check.True(!roster.KnowsPlayerCrime(id));
            }));
            cases.Add(("Pursuing guards recruit available neighbors within a stable varied radius",()=>{
                var roster=Roster(); string caller=roster.Guards[0].Id,other=roster.Guards[1].Id;
                roster.Dispatch("crime",100,new Dictionary<string,List<SavedRoutePoint>> {
                    {caller,Path(P(-10),P(0))} },1,2,10);
                Check.True(!roster.CanRecruitPursuit(caller,other,100,1));
                roster.Engage(caller,100);
                double radius=roster.PursuitRecruitmentRadius(other);
                Check.True(radius>=8 && radius<=12);
                Check.True(roster.CanRecruitPursuit(caller,other,100,radius-0.01));
                Check.True(!roster.CanRecruitPursuit(caller,other,100,radius+0.01));
                Check.True(!roster.CanRecruitPursuit(caller,caller,100,0));
                var saved=GuardRoster.CopyValidated(roster);
                Check.Near(radius,saved.PursuitRecruitmentRadius(other));
                saved.Dispatch("reinforcement",110,new Dictionary<string,List<SavedRoutePoint>> {
                    {other,Path(P(2,-20),P(0))} },1,2,10);
                saved.Engage(other,110);
                saved.Guards[1].LastSeenSuspect=new SavedRoutePoint { X=0,Z=0 };
                saved.Guards[1].SearchUntil=saved.Guards[0].SearchUntil;
                Check.True(!saved.CanRecruitPursuit(caller,other,110,1));
                var joined=GuardRoster.CopyValidated(saved);
                Check.Equal(2,joined.Guards.Count);
                Check.Near(joined.Guards[0].SearchUntil,joined.Guards[1].SearchUntil);
                Check.Near(0,joined.Guards[1].LastSeenSuspect.X);
                Check.True(!roster.CanRecruitPursuit(caller,other,roster.Guards[0].SearchUntil,1));
                roster.Reports[0].ThreatId="rat";
                Check.True(!roster.CanRecruitPursuit(caller,other,100,1));
                roster.Reports[0].ThreatId=null;
                roster.Kill(other,100);
                Check.True(!roster.CanRecruitPursuit(caller,other,100,1));
            }));
            cases.Add(("Witness route joins a surveyed branch at an intermediate patrol segment point",()=>{
                var roster=GuardRoster.Create(1,42,3,new[]{P(-10),P(2,-20),P(0,-10)}); Start(roster);
                roster.CacheBuildingApproach(7,P(8,5),new Dictionary<string,List<SavedRoutePoint>> {
                    {roster.Guards[2].Id,Path(P(0,-10),P(8,-10),P(8),P(8,5))} });
                roster.Kill(roster.Guards[2].Id,200);
                var plan=roster.PlanWitnessRoute(7,250);
                Check.True(plan!=null);
                Check.Equal(roster.Guards[0].Id,plan.GuardId);
                Check.Near(7,plan.Length);
                Check.Near(6,roster.PlanWitnessRoute(7,250,P(8,4)).Length);
                Check.Near(7,GuardRoster.CopyValidated(roster).PlanWitnessRoute(7,250).Length);
            }));
            cases.Add(("Witness routing chooses a nearby patrol on surveyed paths and replans after its movement",()=>{
                var roster=Roster(); Start(roster);
                roster.CacheBuildingApproach(7,P(8),new Dictionary<string,List<SavedRoutePoint>> {
                    {roster.Guards[0].Id,Path(P(-10),P(0),P(8))},
                    {roster.Guards[1].Id,Path(P(2,-20),P(2),P(8))} });
                var plan=roster.PlanWitnessRoute(7,250);
                Check.Equal(roster.Guards[0].Id,plan.GuardId); Check.Near(2,plan.Length);
                Check.Near(8,plan.Route[0].X); Check.Near(10,plan.Route[plan.Route.Count-1].X);
                var later=roster.PlanWitnessRoute(7,300,P(9));
                Check.Near(9,later.Route[0].X); Check.True(later.Length>2);
                Check.Equal(0,roster.Reports.Count);
                Check.True(roster.PlanWitnessRoute(7,300,P(500,500))==null);
                var saved=GuardRoster.CopyValidated(roster);
                Check.Near(plan.Length,saved.PlanWitnessRoute(7,250).Length);
                saved.Kill(saved.Guards[0].Id,250);
                Check.Equal(saved.Guards[1].Id,saved.PlanWitnessRoute(7,250).GuardId);
            }));
            cases.Add(("Witness routing prefers a closer stationary guard and ignores unreachable posts",()=>{
                var roster=GuardRoster.Create(1,42,3,new[]{P(-10),P(2,-20),P(8.5)}); Start(roster);
                roster.CacheBuildingApproach(7,P(8),new Dictionary<string,List<SavedRoutePoint>> {
                    {roster.Guards[0].Id,Path(P(-10),P(0),P(8))},
                    {roster.Guards[1].Id,Path(P(2,-20),P(2),P(8))},
                    {roster.Guards[2].Id,Path(P(8.5),P(8))} });
                Check.Equal(roster.Guards[2].Id,roster.PlanWitnessRoute(7,250).GuardId);
                roster.ReassignPost(roster.Guards[2].Id,P(8,0.1),250);
                roster.CacheBuildingApproach(7,P(8),new Dictionary<string,List<SavedRoutePoint>> {
                    {roster.Guards[0].Id,Path(P(-10),P(0),P(8))},
                    {roster.Guards[1].Id,Path(P(2,-20),P(2),P(8))} });
                Check.Equal(roster.Guards[0].Id,roster.PlanWitnessRoute(7,250).GuardId);
            }));
            cases.Add(("Witness contact uses moving patrol positions rather than their empty posts",()=>{
                var roster=Roster(); Start(roster);
                Check.Equal(1,roster.GuardsInReportingRange(P(10),250).Count);
                Check.Equal(roster.Guards[0].Id,roster.GuardsInReportingRange(P(10),250)[0]);
                Check.Equal(0,roster.GuardsInReportingRange(P(-10),250).Count);
                var restored=GuardRoster.CopyValidated(roster);
                Check.Equal(1,restored.GuardsInReportingRange(P(10),250).Count);
                restored.Kill(restored.Guards[0].Id,250);
                Check.Equal(0,restored.GuardsInReportingRange(P(10),250).Count);
                Check.Throws<ArgumentException>(()=>roster.GuardsInReportingRange(P(double.NaN),250));
            }));
            cases.Add(("Public post migration leaves active patrol journeys intact",()=>{
                var roster=Roster(); Start(roster); string id=roster.Guards[0].Id;
                var before=roster.Position(id,105);
                Check.Throws<ArgumentException>(()=>roster.ReassignPost(id,P(-20),105));
                Check.Throws<ArgumentException>(()=>roster.MovePost(id,Path(P(-10),P(-20)),105,1.3,12));
                Check.Near(before.X,roster.Position(id,105).X); Check.Near(before.Z,roster.Position(id,105).Z);
                Check.Equal(1,GuardRoster.CopyValidated(roster).Patrols.Count);
            }));
            cases.Add(("Roster patrol positions survive world-shaped save without adding guard identities", () => {
                var roster = Roster(); Start(roster);
                Check.Near(10, roster.Position(roster.Guards[0].Id, 250).X);
                var options = new JsonSerializerOptions { IncludeFields = true };
                var copy = GuardRoster.CopyValidated(JsonSerializer.Deserialize<GuardRoster>(JsonSerializer.Serialize(roster, options), options));
                Check.Equal(2, copy.Guards.Count);
                Check.Near(12, copy.Position(copy.Guards[1].Id, 250).X);
                Check.Near(-10, copy.Position(copy.Guards[0].Id, 5000).X);
            }));
            cases.Add(("Patrol animation distinguishes joining and circuit travel from waiting and posted guards", () => {
                var roster=Roster(); Start(roster); string id=roster.Guards[0].Id;
                Check.True(roster.IsPatrolMoving(id,125));
                Check.True(!roster.IsPatrolMoving(id,175));
                Check.True(roster.IsPatrolMoving(id,250));
                roster.StopPatrol(id,250);
                Check.True(roster.IsPatrolMoving(id,275));
                roster.Advance(500); Check.True(!roster.IsPatrolMoving(id,500));
            }));
            cases.Add(("Building response starts at actual patrol position and returns to the assigned post", () => {
                var roster = Roster(); Start(roster); string id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, P(100), new Dictionary<string, List<SavedRoutePoint>> { { id, Path(P(-10), P(100)) } });
                var sent = roster.DispatchBuilding("crime", 73, 250, 1, 2, 10);
                Check.Equal(1, sent.Count); Check.Near(10, sent[0].Route[0].X);
                Check.Near(10, roster.Position(id, 250).X); Check.Near(900, sent[0].ArrivesAt);
                Check.Near(2, roster.Position(roster.Guards[1].Id, 300).X);
                Check.Near(0, roster.Position(roster.Guards[1].Id, 300).Z);
                var copy = GuardRoster.CopyValidated(roster);
                copy.Advance(1200); Check.Near(1750, copy.Guards[0].ArrivesAt);
                Check.Near(100, copy.Position(id, 1200).X);
                Check.Near(-10, copy.Position(id, 1750).X);
                Check.Equal(GuardDuty.Posted, copy.Guards[0].Duty);
            }));
            cases.Add(("A patrol partner death freezes the death position and physically returns the survivor", () => {
                var roster = Roster(); Start(roster);
                roster.Kill(roster.Guards[0].Id, 250);
                Check.Near(10, roster.Guards[0].DeathPosition.X);
                Check.Near(2, roster.Position(roster.Guards[1].Id, 300).X);
                Check.Near(-20, roster.Position(roster.Guards[1].Id, 400).Z);
                GuardRoster.CopyValidated(roster);
            }));
            cases.Add(("Stopping a patrol preserves both current positions and returns without renewing the stop time", () => {
                var roster = Roster(); Start(roster); string leader = roster.Guards[0].Id;
                roster.StopPatrol(leader, 250);
                Check.Near(10, roster.Position(leader, 250).X);
                roster.StopPatrol(leader, 275);
                Check.Near(5, roster.Position(leader, 275).X);
                Check.Near(-10, roster.Position(leader, 350).X);
                Check.Near(-20, roster.Position(roster.Guards[1].Id, 400).Z);
                Check.Equal(0, roster.Patrols.Count);
            }));
            cases.Add(("Patrol start rejects duplicate participation and mismatched posts transactionally", () => {
                var roster = Roster(); Start(roster);
                Check.Throws<ArgumentException>(() => Start(roster));
                Check.Equal(2, roster.Guards.Count);
                Check.Near(10, roster.Position(roster.Guards[0].Id, 250).X);
                var invalid = Roster(); invalid.Guards[0].Post.X = -11;
                Check.Throws<ArgumentException>(() => Start(invalid));
                Check.Near(-11, invalid.Position(invalid.Guards[0].Id, 250).X);
            }));
            cases.Add(("Crime before patrol departure cancels the future pair without corrupting saves", () => {
                var roster = Roster(); Start(roster); string id = roster.Guards[0].Id;
                roster.Dispatch("early", 50, new Dictionary<string, List<SavedRoutePoint>> { { id, Path(P(-10), P(10)) } }, 1, 2, 10);
                Check.Equal(0, roster.Patrols.Count);
                Check.Near(-10, roster.Position(id, 50).X);
                Check.Near(-20, roster.Position(roster.Guards[1].Id, 500).Z);
                GuardRoster.CopyValidated(roster);
            }));
            cases.Add(("Patrol response validation rejects fabricated return endpoints and unknown saved partners", () => {
                var roster = Roster(); Start(roster);
                var broken = GuardRoster.CopyValidated(roster); broken.Patrols[0].Plan.FollowerId = "unknown";
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Patrols[0].InterruptedAt = 99;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                string id = roster.Guards[0].Id;
                roster.Dispatch("direct", 250, new Dictionary<string, List<SavedRoutePoint>> { { id, Path(P(10), P(100)) } }, 1, 2, 10);
                Check.Near(10, roster.Position(id, 250).X);
                broken = GuardRoster.CopyValidated(roster); broken.Guards[0].ReturnPath[broken.Guards[0].ReturnPath.Count - 1].X = -9;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Guards[0].ReturnRate = double.MaxValue;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                roster.Advance(1000); Check.Equal(GuardDuty.Returning, roster.Guards[0].Duty);
                Check.Near(-10, roster.Position(id, 1600).X);
            }));
        }
    }
}
