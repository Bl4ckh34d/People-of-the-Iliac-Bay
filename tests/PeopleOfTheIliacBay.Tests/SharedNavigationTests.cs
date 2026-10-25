using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SharedNavigationTests
    {
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Cheap shared navigation steps use available frame budget instead of one tiny batch",()=>{
                int count=0;double elapsed=0;
                System.Collections.IEnumerator Work(){for(int i=0;i<1000;i++){count++;elapsed+=0.01;yield return null;}}
                Check.True(!BoundedWorkSlice.Advance(Work(),()=>elapsed,()=>true));
                Check.True(count>=399&&count<=401);
            }));
            cases.Add(("Shared clearance surveys crossed squares once across different personal lanes",()=>{
                int surveys=0,exact=0;
                var map=new SharedClearanceMap(0.5,4096,(x,z)=>{surveys++;return true;},(a,b)=>{exact++;return true;});
                for(int lane=0;lane<32;lane++) Check.True(map.Clear(new RoutePoint(0.1,0.01+lane*0.005),new RoutePoint(20.1,0.01+lane*0.005)));
                Check.Equal(41,surveys);Check.Equal(0,exact);Check.Equal(41,map.CellCount);
            }));
            cases.Add(("Clearance pages pack neighboring cells and bound sparse town coverage",()=>{
                var map=new SharedClearanceMap(0.5,128,(x,z)=>true,(a,b)=>true);
                map.Clear(new RoutePoint(0.1,0.1),new RoutePoint(3.9,0.1));
                Check.Equal(1,map.PageCount);
                for(int i=0;i<100;i++)map.Clear(new RoutePoint(i*20,0),new RoutePoint(i*20,0));
                Check.True(map.PageCount<=2);Check.True(map.CellCount<=128);
            }));
            cases.Add(("Shared clearance checks every crossed cell and retains exact narrow passage fallback",()=>{
                int exact=0;
                var map=new SharedClearanceMap(0.5,128,(x,z)=>x!=3,(a,b)=>{exact++;return false;});
                Check.True(!map.Clear(new RoutePoint(0.1,0.1),new RoutePoint(4.1,0.1)));Check.Equal(1,exact);
                Check.True(!map.Clear(new RoutePoint(4.1,0.1),new RoutePoint(0.1,0.1)));Check.Equal(1,exact);
                var narrow=new SharedClearanceMap(0.5,128,(x,z)=>false,(a,b)=>true);
                Check.True(narrow.Clear(new RoutePoint(-0.01,0),new RoutePoint(0.01,0)));
            }));
            cases.Add(("Clearance invalidation repairs only affected cells and invalidates crossing exact proofs",()=>{
                bool blocked=false;int surveys=0;
                var map=new SharedClearanceMap(0.5,128,(x,z)=>{surveys++;return !blocked;},(a,b)=>!blocked);
                Check.True(map.Clear(new RoutePoint(0.1,0.1),new RoutePoint(4.1,0.1)));
                Check.True(map.Clear(new RoutePoint(10.1,0.1),new RoutePoint(11.1,0.1)));int initial=surveys;
                blocked=true;map.Invalidate(1.5,0,2,0.5);
                Check.True(!map.Clear(new RoutePoint(0.1,0.1),new RoutePoint(4.1,0.1)));
                Check.True(map.Clear(new RoutePoint(10.1,0.1),new RoutePoint(11.1,0.1)));
                Check.True(surveys-initial<4);
            }));
            cases.Add(("Shared clearance is bounded and rejects nonfinite or excessive requests",()=>{
                int calls=0;
                var map=new SharedClearanceMap(0.5,16,(x,z)=>{calls++;return true;},(a,b)=>false);
                for(int i=0;i<100;i++) map.Clear(new RoutePoint(i,0.1),new RoutePoint(i+1,0.1));
                Check.True(map.CellCount<=16);Check.True(map.SegmentCount<=16);
                int before=calls;
                Check.True(!map.Clear(new RoutePoint(double.NaN,0),new RoutePoint(1,0)));
                Check.True(!map.Clear(new RoutePoint(0,0),new RoutePoint(100000,0)));Check.Equal(before,calls);
                Check.True(!map.Clear(new RoutePoint(0,0),new RoutePoint(24,24)));Check.Equal(before,calls);
            }));
            cases.Add(("Local invalidation removes an exact narrow passage proof after obstruction",()=>{
                bool blocked=false;int exact=0;
                var map=new SharedClearanceMap(0.5,128,(x,z)=>false,(a,b)=>{exact++;return !blocked;});
                var a=new RoutePoint(0,0);var b=new RoutePoint(4,0);
                Check.True(map.Clear(a,b));blocked=true;
                map.Invalidate(1.9,-0.1,2.1,0.1);
                Check.True(!map.Clear(a,b));Check.Equal(2,exact);
            }));
            cases.Add(("Dynamic gate overlay detects crossing with both endpoints beyond the apron",()=>{
                Check.True(NavigationBounds.IntersectsSegment(new RoutePoint(-10,0),new RoutePoint(10,0),-3,-2,3,2));
                Check.True(!NavigationBounds.IntersectsSegment(new RoutePoint(-10,4),new RoutePoint(10,4),-3,-2,3,2));
                Check.True(NavigationBounds.IntersectsSegment(new RoutePoint(0,0),new RoutePoint(0,0),-3,-2,3,2));
            }));
            cases.Add(("Shared endpoint routes preserve copies and separate direction and topology",()=>{
                var cache=new SharedRouteCache(2);
                var a=new RoutePoint(1,2);var b=new RoutePoint(3,4);
                var source=new List<SavedRoutePoint>{new SavedRoutePoint{X=1,Z=2},new SavedRoutePoint{X=3,Z=4}};
                cache.Put(a,b,source);source[1].X=100;
                List<SavedRoutePoint> result;
                Check.True(cache.TryGet(a,b,out result));Check.Near(3,result[1].X);result[1].X=200;
                Check.True(cache.TryGet(b,a,out result));Check.Near(3,result[0].X);Check.Near(1,result[1].X);
                cache.Clear();Check.True(!cache.TryGet(a,b,out result));
            }));
            cases.Add(("Shared endpoint cache bounds route points as well as entries",()=>{
                var cache=new SharedRouteCache(100,6);
                for(int i=0;i<10;i++)cache.Put(new RoutePoint(i,0),new RoutePoint(i,1),
                    new[]{new SavedRoutePoint{X=i,Z=0},new SavedRoutePoint{X=i,Z=1}});
                Check.True(cache.Count<=3);Check.True(cache.PointCount<=6);
            }));
        }
    }
}
