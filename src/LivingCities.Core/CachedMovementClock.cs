using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public sealed partial class WorldStateStore
    {
        // Schema 22 migrated active responders but left precomputed daily routes
        // in 12x game seconds. They can also survive in schema 22/23 saves. Detect
        // the encoded rate geometrically so already-correct routes stay intact.
        private static void RepairCachedMovementClock(WorldState world,double rate)
        {
            foreach(var town in world.Routines)
                foreach(var routine in town.Residents)
                    RepairDestinationClock(routine.Itinerary,rate);
        }

        private static void RepairDestinationClock(ResidentDestination destination,double rate)
        {
            if(destination==null) return;
            for(int n=0;n<destination.Stops.Count;n++)
            {
                var stop=destination.Stops[n];
                double ratio=LegacyWalkingRatio(stop.Route,stop.WalkSeconds,rate);
                stop.WalkSeconds*=ratio;
                if(n==0) destination.EntryGateSeconds*=ratio;
            }
            double exitRatio=LegacyWalkingRatio(destination.ExitRoute,destination.ExitWalkSeconds,rate);
            destination.ExitWalkSeconds*=exitRatio;
            destination.ExitGateSeconds*=exitRatio;
        }

        private static double LegacyWalkingRatio(List<SavedRoutePoint> route,double seconds,double rate)
        {
            double length=0;
            for(int n=1;n<route.Count;n++)
            {double x=route[n].X-route[n-1].X,z=route[n].Z-route[n-1].Z; length+=Math.Sqrt(x*x+z*z);}
            if(length<=0) return 1;
            double encodedRate=seconds/length*ResidentMovement.WalkSpeed;
            return Math.Abs(encodedRate-12)<0.001 ? rate/12 : 1;
        }
    }
}
