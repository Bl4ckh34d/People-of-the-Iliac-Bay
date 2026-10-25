using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcActionWaypointRecoveryTests
    {
        private static GuardPoint3 Point(double x,double y=0,double z=0)
        {return new GuardPoint3 {X=x,Y=y,Z=z};}
        private static void Case(List<(string Name,Action Body)> cases,string name,Action body)
        {cases.Add((name,body));}

        public static void Add(List<(string Name,Action Body)> cases)
        {
            Case(cases,"Circling without target-distance improvement reaches the waypoint stall threshold",()=>{
                var progress=new NpcActionWaypointProgress();
                Check.True(!progress.Observe(5,0.6,10));
                Check.True(!progress.Observe(5,0.58,10.5));
                Check.True(progress.Observe(5,0.61,11.01),"Walking around the target must not reset stall time.");
            });
            Case(cases,"Waypoint progress resets on a new index or meaningful target approach",()=>{
                var progress=new NpcActionWaypointProgress();
                Check.True(!progress.Observe(5,1,4));
                Check.True(progress.Observe(5,1,5));
                Check.True(!progress.Observe(6,0.9,5.1));
                Check.True(progress.Observe(6,0.9,6.1));
                Check.True(!progress.Observe(6,0.77,6.2));
                Check.Near(0.77,progress.BestDistance);
                Check.True(progress.Observe(6,0.77,7.2));
            });
            Case(cases,"Waypoint progress rejects invalid and nonmonotonic observations",()=>{
                var progress=new NpcActionWaypointProgress();
                Check.True(!progress.IsStalled(0));
                Check.Throws<ArgumentException>(()=>progress.Observe(-1,1,0));
                Check.Throws<ArgumentException>(()=>progress.Observe(0,double.NaN,0));
                Check.Throws<ArgumentException>(()=>progress.Observe(0,1,double.PositiveInfinity));
                Check.True(!progress.Observe(0,1,10));
                Check.Throws<ArgumentException>(()=>progress.Observe(0,1,9));
                Check.True(progress.Observe(0,1,11));
                Check.True(!progress.IsStalled(10.5),"A backwards query must not throw or report a stall.");
                Check.True(progress.IsStalled(12));
            });
            Case(cases,"Rejoin selection skips blocked later points and returns only a validated index",()=>{
                var route=new List<GuardPoint3> {Point(0),Point(1),Point(2),Point(3),Point(4)};
                var original=new[] {route[0].X,route[1].X,route[2].X,route[3].X,route[4].X};
                var checkedIndices=new List<double>();
                int selected;
                bool found=NpcActionWaypointRejoin.TrySelect(route,0,Point(0),point=>{
                    checkedIndices.Add(point.X);
                    return point.X==3;
                },out selected);
                Check.True(found);Check.Equal(3,selected);
                Check.Equal(3,checkedIndices.Count);
                for(int i=0;i<route.Count;i++)Check.Near(original[i],route[i].X);
            });
            Case(cases,"Rejoin validation callback cannot mutate the saved route point",()=>{
                var route=new List<GuardPoint3> {Point(0),Point(1)};
                int selected;
                bool found=NpcActionWaypointRejoin.TrySelect(route,0,Point(0),point=>{
                    point.X=99;
                    return true;
                },out selected);
                Check.True(found);Check.Equal(1,selected);Check.Near(1,route[1].X);
            });
            Case(cases,"Rejoin selection checks at most four candidates and leaves route unchanged on failure",()=>{
                var route=new List<GuardPoint3> {Point(0),Point(1),Point(2),Point(3),Point(4),Point(5),Point(6)};
                int calls=0,selected;
                bool found=NpcActionWaypointRejoin.TrySelect(route,0,Point(0),point=>{calls++;return false;},out selected);
                Check.True(!found);Check.Equal(-1,selected);Check.Equal(4,calls);
                Check.Near(6,route[6].X);
            });
            Case(cases,"Rejoin selection ignores distant points and can select the final point without declaring arrival",()=>{
                var route=new List<GuardPoint3> {Point(0),Point(6),Point(4)};
                int calls=0,selected;
                bool found=NpcActionWaypointRejoin.TrySelect(route,0,Point(0),point=>{calls++;return true;},out selected);
                Check.True(found);Check.Equal(2,selected);Check.Equal(1,calls);
                Check.Near(6,route[1].X);Check.Near(4,route[2].X);
            });
            Case(cases,"Rejoin selection rejects malformed routes and targets before probing",()=>{
                int calls=0,selected;
                Check.True(!NpcActionWaypointRejoin.TrySelect(null,0,Point(0),point=>{calls++;return true;},out selected));
                Check.Equal(-1,selected);Check.Equal(0,calls);
                var malformed=new List<GuardPoint3> {Point(0),Point(double.NaN)};
                Check.True(!NpcActionWaypointRejoin.TrySelect(malformed,0,Point(0),point=>{calls++;return true;},out selected));
                Check.True(!NpcActionWaypointRejoin.TrySelect(new List<GuardPoint3>{Point(0),Point(1)},0,null,point=>{calls++;return true;},out selected));
                Check.True(!NpcActionWaypointRejoin.TrySelect(new List<GuardPoint3>{Point(0),Point(1)},0,Point(0),null,out selected));
                Check.Equal(0,calls);
            });
            Case(cases,"CanTraverse rejects a swept crowd collision despite a clear endpoint",()=>{
                var neighbours=new List<GuardPoint3> {Point(0.5,0,0.1)};
                Check.True(!CrowdSteering.CanTraverse(Point(0),Point(1),neighbours,0.5,point=>true));
            });
            Case(cases,"CanTraverse preserves geometry rejection and accepts clear paths",()=>{
                var neighbours=new List<GuardPoint3> {Point(0,2)};
                Check.True(!CrowdSteering.CanTraverse(Point(0),Point(1),neighbours,0.5,point=>false));
                Check.True(CrowdSteering.CanTraverse(Point(0),Point(1),neighbours,0.5,point=>true));
            });
        }
    }
}
