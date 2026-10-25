using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class GuardRefugeTests
    {
        private static GuardRefugeJourney Plan() { return GuardRefugeJourney.Create(7,
            new[] { new GuardPoint3(),new GuardPoint3 { X=10 } },
            new[] { new GuardPoint3(),new GuardPoint3 { Z=4 } },100,2,12); }
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Engaged refuge preserves health and reports and requires a surveyed route home",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] { new SavedRoutePoint() }); var id=roster.Guards[0].Id;
                roster.Dispatch("murder",100,new Dictionary<string,List<SavedRoutePoint>> {
                    { id,new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint { X=10 } } }
                },1,2,12);
                roster.Engage(id,130);
                roster.ObserveEngagement(id,new SavedRoutePoint { X=7 },0.4,131,false);
                var plan=GuardRefugeJourney.Create(7,new[] { new GuardPoint3 { X=7 },new GuardPoint3 { X=20 } },
                    new[] { new GuardPoint3(),new GuardPoint3 { Z=4 } },132,2,12);
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(id,plan,132));
                Check.Equal(GuardDuty.Engaged,roster.Guards[0].Duty);
                roster.TakeRefuge(id,plan,132,new List<SavedRoutePoint> { new SavedRoutePoint { X=7 },new SavedRoutePoint() });
                roster=GuardRoster.CopyValidated(roster);
                Check.Near(0.4,roster.Guards[0].HealthFraction);
                Check.Equal("murder",roster.Reports[0].Id);
                Check.True(roster.Guards[0].IncidentId==null && roster.Guards[0].EngagementPosition==null);
                roster.Guards[0].Refuge.Release(300); roster.Advance(1000);
                Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
                Check.Near(0,roster.Position(id,1000).X);
                Check.Equal(1,roster.Reports.Count);
            }));
            cases.Add(("Engaged wall refuge release survives world save and blocks dispatch until return",()=>{
                var store=new WorldStateStore(42); store.GetOrCreate(1,new[] {1});
                var roster=store.GetOrCreateGuards(1,1,new[] {new SavedRoutePoint()});
                string id=roster.Guards[0].Id;
                roster.Dispatch("citywatch-incident",100,new Dictionary<string,List<SavedRoutePoint>> {
                    {id,new List<SavedRoutePoint> {new SavedRoutePoint(),new SavedRoutePoint {X=10}}}
                },1,2,12);
                roster.Engage(id,130);
                roster.ObserveEngagement(id,new SavedRoutePoint {X=7},0.2,131,false);
                var plan=GuardRefugeJourney.Create(7,
                    new[] {new GuardPoint3 {X=7},new GuardPoint3 {X=20},new GuardPoint3 {X=30}},
                    new[] {new GuardPoint3(),new GuardPoint3 {Z=4}},132,3.8,12);
                var home=new List<SavedRoutePoint> {new SavedRoutePoint {X=7},new SavedRoutePoint()};
                roster.TakeRefuge(id,plan,132,home);
                var saved=roster.Guards.Find(guard=>guard.Id==id);
                Check.True(saved.Refuge!=null && !saved.Refuge.ReleasedAt.HasValue);
                saved.Refuge.Release(4000);

                var options=new System.Text.Json.JsonSerializerOptions {IncludeFields=true};
                string json=System.Text.Json.JsonSerializer.Serialize(store.Snapshot(),options);
                var restored=new WorldStateStore(0);
                restored.Restore(System.Text.Json.JsonSerializer.Deserialize<WorldState>(json,options));
                var copy=restored.GetGuards(1);
                Check.Equal(GuardDuty.Refuge,copy.Guards[0].Duty);
                Check.Near(4000,copy.Guards[0].Refuge.ReleasedAt.Value);
                Check.Near(0.2,copy.Guards[0].HealthFraction);
                Check.True(copy.Guards[0].IncidentId==null);
                Check.Equal("citywatch-incident",copy.Reports[0].Id);

                var blocked=copy.Dispatch("while-sheltered",3999,new Dictionary<string,List<SavedRoutePoint>> {
                    {id,new List<SavedRoutePoint> {new SavedRoutePoint(),new SavedRoutePoint {X=5}}}
                },1,2,12);
                Check.Equal(0,blocked.Count);
                double completedAt=4000+(4+30)*12/3.8+1;
                copy.Advance(completedAt);
                Check.Equal(GuardDuty.Posted,copy.Guards[0].Duty);
                Check.True(copy.Guards[0].Refuge==null);
                var resumed=copy.Dispatch("after-return",completedAt,new Dictionary<string,List<SavedRoutePoint>> {
                    {id,new List<SavedRoutePoint> {new SavedRoutePoint(),new SavedRoutePoint {X=5}}}
                },1,2,12);
                Check.Equal(1,resumed.Count);
                Check.Equal(GuardDuty.Responding,copy.Guards[0].Duty);
                Check.Equal(1,copy.Guards.Count);
            }));
            cases.Add(("Patrolling guard takes refuge from current position and physically returns to post",()=>{
                var roster=GuardRoster.Create(1,42,2,new[] { new SavedRoutePoint { X=-10 },new SavedRoutePoint { X=2,Z=-20 } });
                var a=roster.Guards[0].Id; var b=roster.Guards[1].Id;
                roster.StartPatrol(PatrolPairPlan.Create(a,b,
                    new List<SavedRoutePoint> { new SavedRoutePoint { X=-10 },new SavedRoutePoint() },
                    new List<SavedRoutePoint> { new SavedRoutePoint { X=2,Z=-20 },new SavedRoutePoint { X=2 } },
                    new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint { X=20 },new SavedRoutePoint { X=20,Z=20 },new SavedRoutePoint { Z=20 },new SavedRoutePoint() },
                    100,1000,2,10,2),100);
                Check.Near(10,roster.Position(a,250).X);
                var plan=GuardRefugeJourney.Create(7,new[] { new GuardPoint3 { X=10 },new GuardPoint3 { X=30 } },
                    new[] { new GuardPoint3(),new GuardPoint3 { Z=4 } },250,2,10);
                roster.TakeRefuge(a,plan,250);
                Check.Near(10,roster.Position(a,250).X);
                Check.True(roster.Patrols[0].InterruptedAt==250);
                Check.True(!roster.IsOnPatrol(a));
                Check.True(roster.IsOnPatrol(b));
                var options=new System.Text.Json.JsonSerializerOptions { IncludeFields=true };
                roster=GuardRoster.CopyValidated(System.Text.Json.JsonSerializer.Deserialize<GuardRoster>(
                    System.Text.Json.JsonSerializer.Serialize(roster,options),options));
                roster.Guards[0].Refuge.Release(400);
                Check.Near(10,roster.Position(a,520).X);
                Check.Equal(GuardDuty.Refuge,roster.Guards[0].Duty);
                Check.Near(0,roster.Position(a,570).X);
                Check.Near(-10,roster.Position(a,620).X);
                Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
                Check.Near(-20,roster.Position(b,620).Z);
            }));
            cases.Add(("Refuge destination reservations reject overlapping guards after copying",()=>{
                var roster=GuardRoster.Create(1,42,3,new[] { new SavedRoutePoint() });
                roster.TakeRefuge(roster.Guards[0].Id,Plan(),100);
                roster=GuardRoster.CopyValidated(roster);
                var overlap=Plan(); overlap.Interior[1].X=0.5;
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(roster.Guards[1].Id,overlap,100));
                Check.Equal(GuardDuty.Posted,roster.Guards[1].Duty);
                var separate=Plan(); separate.Interior[1].X=1;
                roster.TakeRefuge(roster.Guards[1].Id,separate,100);
                var otherBuilding=Plan(); otherBuilding.BuildingKey=8;
                roster.TakeRefuge(roster.Guards[2].Id,otherBuilding,100);
            }));
            cases.Add(("Refuge reservation remains during exit but frees after the guard reaches the exterior",()=>{
                var roster=GuardRoster.Create(1,42,2,new[] { new SavedRoutePoint() });
                roster.TakeRefuge(roster.Guards[0].Id,Plan(),100); roster.Guards[0].Refuge.Release(200);
                var later=Plan(); later.StartsAt=212;
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(roster.Guards[1].Id,later,212));
                later.StartsAt=224; roster.TakeRefuge(roster.Guards[1].Id,later,224);
            }));
            cases.Add(("Refuge indoor corpse blocks its actual position while exterior death frees reservation",()=>{
                var roster=GuardRoster.Create(1,42,2,new[] { new SavedRoutePoint() });
                roster.TakeRefuge(roster.Guards[0].Id,Plan(),100); roster.Kill(roster.Guards[0].Id,200);
                var later=Plan(); later.StartsAt=201;
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(roster.Guards[1].Id,later,201));
                roster=GuardRoster.Create(1,42,2,new[] { new SavedRoutePoint() });
                roster.TakeRefuge(roster.Guards[0].Id,Plan(),100); roster.Kill(roster.Guards[0].Id,130);
                roster.TakeRefuge(roster.Guards[1].Id,later,201);
            }));
            cases.Add(("Refuge roster state survives copying and blocks dispatch until physical return",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] { new SavedRoutePoint() }); var id=roster.Guards[0].Id;
                roster.TakeRefuge(id,Plan(),100);
                Check.Near(5,roster.Position(id,130).X);
                var copy=GuardRoster.CopyValidated(roster);
                Check.Equal(0,copy.Dispatch("crime",130,new Dictionary<string,List<SavedRoutePoint>> {
                    { id,new List<SavedRoutePoint> { new SavedRoutePoint { X=5 },new SavedRoutePoint { X=20 } } }
                },1,2,12).Count);
                Check.Equal(RefugePhase.Sheltered,copy.Guards[0].Refuge.Evaluate(200).Phase);
                copy.Guards[0].Refuge.Release(200); copy.Advance(283);
                Check.Equal(GuardDuty.Refuge,copy.Guards[0].Duty);
                copy.Advance(284); Check.Equal(GuardDuty.Posted,copy.Guards[0].Duty);
                Check.True(copy.Guards[0].Refuge==null); Check.Near(0,copy.Position(id,284).X);
            }));
            cases.Add(("Refuge deaths retain journey and never revive or resume travel",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] { new SavedRoutePoint() }); var id=roster.Guards[0].Id;
                roster.TakeRefuge(id,Plan(),100); roster.Kill(id,130);
                var copy=GuardRoster.CopyValidated(roster); copy.Advance(99999);
                Check.True(!copy.Guards[0].IsAlive); Check.Near(5,copy.Position(id,99999).X);
                Check.True(copy.Guards[0].Refuge!=null);
            }));
            cases.Add(("Refuge admission rejects wrong route origin without mutating guard",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] { new SavedRoutePoint { X=50 } });
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(roster.Guards[0].Id,Plan(),100));
                Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty); Check.True(roster.Guards[0].Refuge==null);
            }));
        }
    }
}
