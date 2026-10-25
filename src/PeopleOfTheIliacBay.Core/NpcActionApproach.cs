using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // A person has an interaction radius. A radial offset is only a preference,
    // not proof of walkable floor. Every selected endpoint needs a real route
    // and visibility to that person within the original interaction radius.
    public static class NpcActionApproach
    {
        public static List<GuardPoint3> FindRoute(GuardPoint3 target,double range,IEnumerable<GuardPoint3> candidates,
            Func<GuardPoint3,List<GuardPoint3>> routeTo,Func<GuardPoint3,bool> visible)
        {
            if(!Point(target) || double.IsNaN(range) || double.IsInfinity(range) || range<=0.5 ||
                candidates==null || routeTo==null || visible==null) return null;
            int examined=0;
            foreach(var candidate in candidates)
            {
                if(++examined>32) break;
                if(!Point(candidate) || DistanceSquared(candidate,target)>range*range || DistanceSquared(candidate,target)<0.36 || !visible(candidate)) continue;
                var route=routeTo(candidate);
                if(route==null || route.Count<2) continue;
                var end=route[route.Count-1];
                if(!Point(end) || DistanceSquared(end,candidate)>0.000001 || DistanceSquared(end,target)>range*range) continue;
                return route;
            }
            return null;
        }
        private static double DistanceSquared(GuardPoint3 a,GuardPoint3 b)
        {double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return x*x+y*y+z*z;}
        private static bool Point(GuardPoint3 p)
        {return p!=null && Finite(p.X) && Finite(p.Y) && Finite(p.Z);}
        private static bool Finite(double value) {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
