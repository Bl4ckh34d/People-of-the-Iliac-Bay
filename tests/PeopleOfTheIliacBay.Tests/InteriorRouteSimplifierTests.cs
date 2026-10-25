using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class InteriorRouteSimplifierTests
    {
        public static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Interior route simplifier joins a clear straight run and preserves endpoints",new Action(()=>
            {
                var input=Route((0,0,0),(0.2,0,0),(0.4,0,0),(0.6,0,0),(0.8,0,0),(1,0,0));
                var result=InteriorRouteSimplifier.Simplify(input,(a,b)=>true);
                Check.Equal(2,result.Count);
                NearPoint(input[0],result[0]); NearPoint(input[input.Count-1],result[1]);
                Check.True(!object.ReferenceEquals(input[0],result[0]));
                input[0].X=99;
                Check.Near(0,result[0].X);
            })));

            cases.Add(("Interior route simplifier retains a blocked corner",new Action(()=>
            {
                var input=Route((0,0,0),(1,0,0),(1,0,1),(2,0,1));
                bool Clear(GuardPoint3 a,GuardPoint3 b)
                {
                    if(a.X==0 && a.Z==0 && b.Z>0) return false;
                    if(a.X==1 && a.Z==0 && b.X>1 && b.Z>0) return false;
                    return true;
                }
                var result=InteriorRouteSimplifier.Simplify(input,Clear,8,32,4,0.5);
                Check.True(result.Count>=3);
                Check.True(result.Exists(p=>p.X==1 && p.Z==0));
                Check.True(result.Exists(p=>p.X==1 && p.Z==1));
            })));

            cases.Add(("Interior route simplifier retains an unsupported shortcut",new Action(()=>
            {
                var input=Route((0,0,0),(0.5,0,0),(1,0,0));
                var result=InteriorRouteSimplifier.Simplify(input,(a,b)=>b.X-a.X<=0.5+1e-9,8,32,2,0.5);
                Check.Equal(3,result.Count);
                EqualPoints(input,result);
            })));

            cases.Add(("Interior route simplifier preserves stair support and rejects tall shortcuts",new Action(()=>
            {
                var stairs=Route((0,0,0),(0.2,0.2,0),(0.4,0.2,0),(0.6,0.2,0),(0.8,0.4,0));
                int checks=0;
                bool SupportedStep(GuardPoint3 a,GuardPoint3 b)
                {
                    checks++;
                    // The native callback samples the floor at <=0.2 m and verifies
                    // capsule clearance; here only neighboring surveyed treads pass.
                    double dx=b.X-a.X,dy=b.Y-a.Y,dz=b.Z-a.Z;
                    return Math.Sqrt(dx*dx+dy*dy+dz*dz)<=0.3;
                }
                var result=InteriorRouteSimplifier.Simplify(stairs,SupportedStep,16,32,2,0.3);
                Check.Equal(stairs.Count,result.Count);
                EqualPoints(stairs,result);
                Check.True(checks>0,"Stair callback was not used.");
                Check.True(Math.Abs(result[result.Count-1].Y-result[0].Y)>0.3,"Stair endpoints collapsed.");
            })));

            cases.Add(("Interior route simplifier limits shortcut checks and retains the tail",new Action(()=>
            {
                var input=new List<GuardPoint3>();
                for(int i=0;i<80;i++) input.Add(new GuardPoint3 { X=i*0.2,Y=0,Z=0 });
                int checks=0;
                var result=InteriorRouteSimplifier.Simplify(input,(a,b)=>{checks++;return false;},24,3,3,0.5);
                Check.True(checks<=3);
                NearPoint(input[0],result[0]);
                NearPoint(input[input.Count-1],result[result.Count-1]);
                Check.True(result.Count>3);
            })));

            cases.Add(("Interior route simplifier validates callbacks, bounds and finite points",new Action(()=>
            {
                var route=Route((0,0,0),(1,0,0));
                Check.Throws<ArgumentNullException>(()=>InteriorRouteSimplifier.Simplify(null,(a,b)=>true));
                Check.Throws<ArgumentNullException>(()=>InteriorRouteSimplifier.Simplify(route,null));
                Check.Throws<ArgumentOutOfRangeException>(()=>InteriorRouteSimplifier.Simplify(route,(a,b)=>true,65));
                Check.Throws<ArgumentOutOfRangeException>(()=>InteriorRouteSimplifier.Simplify(route,(a,b)=>true,24,257));
                Check.Throws<ArgumentOutOfRangeException>(()=>InteriorRouteSimplifier.Simplify(route,(a,b)=>true,24,32,4.1));
                Check.Throws<ArgumentOutOfRangeException>(()=>InteriorRouteSimplifier.Simplify(route,(a,b)=>true,24,32,3,0.51));
                Check.Throws<ArgumentException>(()=>InteriorRouteSimplifier.Simplify(Route((0,0,0),(double.NaN,0,0)),(a,b)=>true));
            })));
        }

        private static List<GuardPoint3> Route(params (double X,double Y,double Z)[] points)
        {
            var result=new List<GuardPoint3>();
            foreach(var point in points) result.Add(new GuardPoint3 { X=point.X,Y=point.Y,Z=point.Z });
            return result;
        }
        private static void EqualPoints(IList<GuardPoint3> expected,IList<GuardPoint3> actual)
        {
            Check.Equal(expected.Count,actual.Count);
            for(int i=0;i<expected.Count;i++) NearPoint(expected[i],actual[i]);
        }
        private static void NearPoint(GuardPoint3 expected,GuardPoint3 actual)
        {
            Check.Near(expected.X,actual.X); Check.Near(expected.Y,actual.Y); Check.Near(expected.Z,actual.Z);
        }
    }
}
