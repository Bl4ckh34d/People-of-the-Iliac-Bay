using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class NpcActionWaypointProgress
    {
        private const double ProgressThreshold=0.12;
        private const double DefaultStallSeconds=1.0;
        private int lastObservedIndex=-1;
        private double lastObservedAt;
        private bool observed;

        public int NextIndex {get;private set;}=-1;
        public double BestDistance {get;private set;}
        public double ProgressAt {get;private set;}

        public bool Observe(int nextIndex,double targetDistance,double monotonicRealTime)
        {
            if(nextIndex<0 || !Finite(targetDistance) || targetDistance<0 || !Finite(monotonicRealTime) || monotonicRealTime<0)
                throw new ArgumentException("Invalid NPC action waypoint progress observation.");
            if(observed && monotonicRealTime<lastObservedAt)
                throw new ArgumentException("NPC action waypoint time must be monotonic.");
            lastObservedAt=monotonicRealTime;
            observed=true;
            if(nextIndex!=lastObservedIndex)
            {
                lastObservedIndex=nextIndex;
                NextIndex=nextIndex;
                BestDistance=targetDistance;
                ProgressAt=monotonicRealTime;
                return false;
            }
            if(targetDistance<=BestDistance-ProgressThreshold+0.000000001)
            {
                BestDistance=targetDistance;
                ProgressAt=monotonicRealTime;
            }
            return IsStalled(monotonicRealTime);
        }

        public bool IsStalled(double monotonicRealTime,double stallSeconds=DefaultStallSeconds)
        {
            if(!observed || !Finite(monotonicRealTime) || monotonicRealTime<ProgressAt ||
                !Finite(stallSeconds) || stallSeconds<=0) return false;
            return monotonicRealTime-ProgressAt>=stallSeconds-0.000000001;
        }

        private static bool Finite(double value) {return !double.IsNaN(value)&&!double.IsInfinity(value);}
    }

    public static class NpcActionWaypointRejoin
    {
        private const double MaximumCandidateDistance=4.5;
        private const int MaximumCandidates=4;

        public static bool TrySelect(IList<GuardPoint3> route,int currentIndex,GuardPoint3 actual,
            Func<GuardPoint3,bool> canTraverse,out int selectedIndex)
        {
            selectedIndex=-1;
            if(route==null || route.Count<2 || currentIndex<0 || currentIndex>=route.Count || !Valid(actual) || canTraverse==null)
                return false;
            for(int i=0;i<route.Count;i++) if(!Valid(route[i])) return false;
            int checkedCandidates=0;
            for(int i=currentIndex+1;i<route.Count && checkedCandidates<MaximumCandidates;i++)
            {
                var candidate=route[i];
                double dx=candidate.X-actual.X,dy=candidate.Y-actual.Y,dz=candidate.Z-actual.Z;
                if(dx*dx+dy*dy+dz*dz>MaximumCandidateDistance*MaximumCandidateDistance) continue;
                checkedCandidates++;
                var probe=new GuardPoint3 {X=candidate.X,Y=candidate.Y,Z=candidate.Z};
                if(!canTraverse(probe)) continue;
                selectedIndex=i;
                return true;
            }
            return false;
        }

        private static bool Valid(GuardPoint3 point)
        {
            return point!=null && Coordinate(point.X) && Coordinate(point.Y) && Coordinate(point.Z);
        }
        private static bool Coordinate(double value)
        {return !double.IsNaN(value)&&!double.IsInfinity(value)&&Math.Abs(value)<=1000000;}
    }
}
