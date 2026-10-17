using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class StreetLaneRouteTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Straight two-point streets gain stable interior lanes while retaining exact approaches",(Action)(()=>{
                var route=StreetLaneRoutes.Offset(new[] {new RoutePoint(0,0),new RoutePoint(8,0)},27,0.8,2,(a,b)=>true);
                Check.True(route.Count>2);
                Check.Near(0,route[0].X);Check.Near(0,route[0].Z);
                Check.Near(8,route[route.Count-1].X);Check.Near(0,route[route.Count-1].Z);
                Check.True(Math.Abs(route[route.Count/2].Z)>0.1);
            })));
            cases.Add(("Long simplified street lane routes use bounded geometry controls",(Action)(()=>{
                var route=StreetLaneRoutes.Offset(new[] {new RoutePoint(0,0),new RoutePoint(10000,0)},27,0.8,2,(a,b)=>true);
                Check.Equal(4,route.Count);
                Check.Near(0,route[0].X);Check.Near(0,route[0].Z);
                Check.Near(10000,route[3].X);Check.Near(0,route[3].Z);
                Check.True(Math.Abs(route[1].Z)>0.1 && Math.Abs(route[2].Z)>0.1);
            })));
            cases.Add(("Resident lane routes stay stable, spread walkers, and preserve door approaches",(Action)(()=>{
                var centerline=new[] {
                    new RoutePoint(0,0),new RoutePoint(1,0),new RoutePoint(5,0),new RoutePoint(9,0),new RoutePoint(10,0)
                };
                Func<RoutePoint,RoutePoint,bool> clear=(a,b)=>true;
                var first=StreetLaneRoutes.Offset(centerline,11,0.8,2,clear);
                var same=StreetLaneRoutes.Offset(centerline,11,0.8,2,clear);
                var other=StreetLaneRoutes.Offset(centerline,12,0.8,2,clear);
                Check.Equal(centerline.Length,first.Count);
                Check.Near(0,first[0].X);Check.Near(0,first[0].Z);
                Check.Near(10,first[first.Count-1].X);Check.Near(0,first[first.Count-1].Z);
                for(int i=0;i<first.Count;i++)
                { Check.Near(first[i].X,same[i].X);Check.Near(first[i].Z,same[i].Z); }
                Check.True(Math.Abs(first[2].Z)>0.1);
                Check.True(Math.Abs(first[2].Z-other[2].Z)>0.1);
                Check.True(Math.Abs(first[1].Z)<Math.Abs(first[2].Z));
                Check.True(Math.Abs(first[3].Z)<Math.Abs(first[2].Z));
            })));
            cases.Add(("Street lane offset backs off as one continuous route when clearance blocks it",(Action)(()=>{
                var centerline=new[] { new RoutePoint(0,0),new RoutePoint(3,0),new RoutePoint(6,0) };
                var route=StreetLaneRoutes.Offset(centerline,11,0.8,1,
                    (a,b)=>Math.Abs(a.Z)<=0.41 && Math.Abs(b.Z)<=0.41);
                Check.Equal(centerline.Length,route.Count);
                Check.True(Math.Abs(route[1].Z)>0.01 && Math.Abs(route[1].Z)<=0.41);
                Check.Near(route[1].Z,route[2].Z,0.42);
            })));
            cases.Add(("Street lane offset falls back to surveyed centerline when no lane fits",(Action)(()=>{
                var centerline=new[] { new RoutePoint(0,0),new RoutePoint(3,0),new RoutePoint(6,0) };
                var route=StreetLaneRoutes.Offset(centerline,11,0.8,1,(a,b)=>Math.Abs(a.Z)<0.000001 && Math.Abs(b.Z)<0.000001);
                for(int i=0;i<centerline.Length;i++)
                { Check.Near(centerline[i].X,route[i].X);Check.Near(centerline[i].Z,route[i].Z); }
            })));
        }
    }
}
