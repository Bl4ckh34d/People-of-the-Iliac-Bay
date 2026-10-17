using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public static class InteriorRouteSimplifier
    {
        public const int MaximumLookaheadLimit = 64;
        public const int MaximumShortcutCheckLimit = 256;
        public const double MaximumDistanceLimit = 4.0;
        public const double MaximumVerticalDifferenceLimit = 0.5;

        // The callback must verify both body clearance and supported floor along
        // the whole segment. Original surveyed edges are retained when no shortcut
        // passes those physical checks.
        public static List<GuardPoint3> Simplify(IList<GuardPoint3> route,
            Func<GuardPoint3,GuardPoint3,bool> shortcutSupportedAndClear,
            int maximumLookahead=24,int maximumShortcutChecks=32,
            double maximumDistance=3.0,double maximumVerticalDifference=0.35)
        {
            if(route==null) throw new ArgumentNullException(nameof(route));
            if(shortcutSupportedAndClear==null) throw new ArgumentNullException(nameof(shortcutSupportedAndClear));
            if(maximumLookahead<1 || maximumLookahead>MaximumLookaheadLimit ||
                maximumShortcutChecks<1 || maximumShortcutChecks>MaximumShortcutCheckLimit ||
                !Finite(maximumDistance) || maximumDistance<=0 || maximumDistance>MaximumDistanceLimit ||
                !Finite(maximumVerticalDifference) || maximumVerticalDifference<0 ||
                maximumVerticalDifference>MaximumVerticalDifferenceLimit)
                throw new ArgumentOutOfRangeException("Route simplification limits exceed their supported bounds.");

            var points=new List<GuardPoint3>(route.Count);
            foreach(var point in route)
            {
                if(point==null || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z))
                    throw new ArgumentException("Route points must have finite coordinates.",nameof(route));
                points.Add(Copy(point));
            }
            if(points.Count<3) return points;

            var result=new List<GuardPoint3> { Copy(points[0]) };
            int index=0,checks=0,last=points.Count-1;
            double maximumDistanceSquared=maximumDistance*maximumDistance;
            while(index<last)
            {
                if(checks>=maximumShortcutChecks)
                {
                    for(int i=index+1;i<=last;i++) result.Add(Copy(points[i]));
                    break;
                }

                int furthest=Math.Min(last,index+maximumLookahead),chosen=-1;
                for(int candidate=furthest;candidate>index+1;candidate--)
                {
                    var from=points[index];var to=points[candidate];
                    if(DistanceSquared(from,to)>maximumDistanceSquared ||
                        Math.Abs(to.Y-from.Y)>maximumVerticalDifference) continue;
                    checks++;
                    if(shortcutSupportedAndClear(Copy(from),Copy(to)))
                    { chosen=candidate;break; }
                    if(checks>=maximumShortcutChecks) break;
                }
                if(chosen<0) chosen=index+1;
                result.Add(Copy(points[chosen]));
                index=chosen;
            }
            return result;
        }

        private static GuardPoint3 Copy(GuardPoint3 point)
        { return new GuardPoint3 { X=point.X,Y=point.Y,Z=point.Z }; }
        private static double DistanceSquared(GuardPoint3 a,GuardPoint3 b)
        {
            double x=b.X-a.X,y=b.Y-a.Y,z=b.Z-a.Z;
            return x*x+y*y+z*z;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
