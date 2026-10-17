using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class GuardPolicyTests
    {
        private static SavedRoutePoint P(double x) { return new SavedRoutePoint { X=x }; }
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Garrison capacity supports doubled base plus gate reserves and rejects higher allocations",()=>{
                var roster=GuardRoster.Create(1,42,GuardRoster.MaximumTarget,new[] { P(0) });
                Check.Equal(GuardRoster.MaximumTarget,GuardRoster.CopyValidated(roster).Target);
                Check.Throws<ArgumentException>(()=>GuardRoster.Create(1,42,GuardRoster.MaximumTarget+1,new[] { P(0) }));
                roster.Target=GuardRoster.MaximumTarget+1; Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(roster));
            }));
            cases.Add(("Increasing saved garrison target preserves old posts, deaths and identity sequence",()=>{
                var roster=GuardRoster.Create(1,42,2,new[] { P(0),P(10) });
                string first=roster.Guards[0].Id,second=roster.Guards[1].Id;
                roster.Kill(first,100);
                Check.Equal(3,roster.IncreaseTarget(5,new[] { P(0),P(10),P(20),P(30),P(40) }));
                Check.Equal(5,roster.Target); Check.Equal(5,roster.Guards.Count);
                Check.Equal(first,roster.Guards[0].Id); Check.True(!roster.Guards[0].IsAlive);
                Check.Equal(second,roster.Guards[1].Id); Check.Near(10,roster.Guards[1].Post.X);
                Check.Near(20,roster.Guards[2].Post.X); Check.Near(40,roster.Guards[4].Post.X);
                Check.Equal(0,roster.IncreaseTarget(5,new[] { P(0) }));
                Check.Equal(0,roster.IncreaseTarget(4,new[] { P(0) }));
                Check.Equal(5,roster.Target);
                var copy=GuardRoster.CopyValidated(roster);
                Check.Equal(0,copy.Recruit(100+7*86400-1));
                Check.Equal(1,copy.Recruit(100+7*86400));
                Check.Equal(6,copy.Guards.Count); Check.True(!copy.Guards[0].IsAlive);
            }));
            cases.Add(("Added guards use the full available post range rather than one end of town",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] {P(0)});
                var posts=new List<SavedRoutePoint>(); for(int i=1;i<=20;i++) posts.Add(P(i*10));
                roster.IncreaseTarget(5,posts);
                Check.Near(10,roster.Guards[1].Post.X);
                Check.Near(160,roster.Guards[4].Post.X);
            }));
            cases.Add(("Garrison expansion with insufficient distinct posts fails without partial identities",()=>{
                var roster=GuardRoster.Create(1,42,2,new[] { P(0),P(10) });
                Check.Throws<ArgumentException>(()=>roster.IncreaseTarget(4,new[] { P(0),P(10),P(20),P(20) }));
                Check.Equal(2,roster.Target); Check.Equal(2,roster.NextIdentity); Check.Equal(2,roster.Guards.Count);
                Check.Throws<ArgumentException>(()=>roster.IncreaseTarget(GuardRoster.MaximumTarget+1,new[] { P(20) }));
            }));
            cases.Add(("Each assigned guard can issue Halt only once per report, including after reload",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] { P(0) }); var guard=roster.Guards[0];
                Check.True(!roster.TryGuardHalt(guard.Id));
                roster.Dispatch("crime",100,new Dictionary<string,List<SavedRoutePoint>> {
                    { guard.Id,new List<SavedRoutePoint> { P(0),P(10) } } },1,2,12);
                Check.True(roster.TryGuardHalt(guard.Id)); Check.True(!roster.TryGuardHalt(guard.Id));
                var copy=GuardRoster.CopyValidated(roster);
                Check.True(!copy.TryGuardHalt(guard.Id)); Check.Equal(1,copy.Reports[0].HaltGuardIds.Count);
                copy.Reports[0].HaltGuardIds.Add(guard.Id);
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(copy));
            }));
            cases.Add(("Legacy reports gain empty warning history and reject unknown warning speakers",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] { P(0) }); var guard=roster.Guards[0];
                roster.Dispatch("crime",100,new Dictionary<string,List<SavedRoutePoint>> {
                    { guard.Id,new List<SavedRoutePoint> { P(0),P(10) } } },1,2,12);
                roster.Reports[0].HaltGuardIds=null;
                var copy=GuardRoster.CopyValidated(roster); Check.Equal(0,copy.Reports[0].HaltGuardIds.Count);
                copy.Reports[0].HaltGuardIds.Add("unknown");
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(copy));
            }));
        }
    }
}
