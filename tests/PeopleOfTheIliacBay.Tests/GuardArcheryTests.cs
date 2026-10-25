using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class GuardArcheryTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Guard archery assignment is stable and twenty percent",()=>{
                int count=0;
                for(uint seed=0;seed<1000;seed++)
                {
                    var guard=new GuardRecord {AppearanceSeed=seed};
                    GuardArchery.EnsureLoadout(guard);
                    if(guard.HasBow) count++;
                    guard.ArrowsRemaining=0;
                    GuardArchery.EnsureLoadout(guard);
                    Check.Equal(0,guard.ArrowsRemaining);
                }
                Check.Equal(200,count);
            }));
            cases.Add(("Guard defense can reroute from engagement to an archer and survive reload",()=>{
                var roster=GuardRoster.Create(1,42,1,new List<SavedRoutePoint>{new SavedRoutePoint()});
                var guard=roster.Guards[0];
                roster.DispatchDefense("hit","foe",100,new Dictionary<string,List<SavedRoutePoint>>{
                    {guard.Id,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint()}}},1,1,1);
                roster.Engage(guard.Id,100);
                roster.RedirectDefense(guard.Id,101,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=20}},
                    new List<SavedRoutePoint>{new SavedRoutePoint {X=20},new SavedRoutePoint()},1,1);
                guard.BowSupportGuardId="helper";GuardArchery.EnsureLoadout(guard);guard.ArrowsRemaining=0;
                var restored=GuardRoster.CopyValidated(roster).Guards[0];
                Check.Equal(GuardDuty.Responding,restored.Duty);
                Check.Equal("helper",restored.BowSupportGuardId);
                Check.Equal(0,restored.ArrowsRemaining);
                Check.True(restored.EngagementPosition==null);
                var options=new System.Text.Json.JsonSerializerOptions {IncludeFields=true};
                var disk=System.Text.Json.JsonSerializer.Deserialize<GuardRoster>(
                    System.Text.Json.JsonSerializer.Serialize(roster,options),options);
                var loaded=GuardRoster.CopyValidated(disk).Guards[0];
                Check.Equal("helper",loaded.BowSupportGuardId);Check.Equal(0,loaded.ArrowsRemaining);
                Check.Throws<ArgumentException>(()=>roster.RedirectDefense(guard.Id,102,
                    new List<SavedRoutePoint>{new SavedRoutePoint {X=999},new SavedRoutePoint {X=20}},
                    new List<SavedRoutePoint>{new SavedRoutePoint {X=20},new SavedRoutePoint()},1,1));
            }));
        }
    }
}
