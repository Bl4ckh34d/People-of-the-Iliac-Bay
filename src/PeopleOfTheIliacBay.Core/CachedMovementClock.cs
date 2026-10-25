using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        // Schema 22 migrated active responders but left precomputed daily routes
        // in 12x game seconds. They can also survive in schema 22/23 saves. Detect
        // the encoded rate geometrically so already-correct routes stay intact.
        private static void RepairCachedMovementClock(WorldState world,double rate)
        {
            var people=new Dictionary<string,Resident>(StringComparer.Ordinal);
            foreach(var settlement in world.Settlements) foreach(var home in settlement.Households)
                foreach(var person in home.Residents) people.Add(person.Id,person);
            foreach(var town in world.Routines)
                foreach(var routine in town.Residents)
                {
                    // Personal route geometry and its durations were committed
                    // together. They are authoritative across scene restoration.
                    if(routine.PersonalLaneVersion>0) continue;
                    RepairDestinationClock(routine.Itinerary,rate,ResidentMovement.WalkSpeedFor(people[routine.ResidentId].AppearanceSeed));
                }
        }

        private static void RepairDestinationClock(ResidentDestination destination,double rate,double speed)
        {
            if(destination==null) return;
            for(int n=0;n<destination.Stops.Count;n++)
            {
                var stop=destination.Stops[n];
                double ratio=LegacyWalkingRatio(stop.Route,stop.WalkSeconds,rate,speed);
                stop.WalkSeconds*=ratio;
                if(n==0) destination.EntryGateSeconds*=ratio;
            }
            double exitRatio=LegacyWalkingRatio(destination.ExitRoute,destination.ExitWalkSeconds,rate,speed);
            destination.ExitWalkSeconds*=exitRatio;
            destination.ExitGateSeconds*=exitRatio;
        }

        private static double LegacyWalkingRatio(List<SavedRoutePoint> route,double seconds,double rate,double speed)
        {
            double length=0;
            for(int n=1;n<route.Count;n++)
            {double x=route[n].X-route[n-1].X,z=route[n].Z-route[n-1].Z; length+=Math.Sqrt(x*x+z*z);}
            if(length<=0) return 1;
            double encodedRate=seconds/length*ResidentMovement.WalkSpeed;
            double personalRate=rate*ResidentMovement.WalkSpeed/speed;
            if(Math.Abs(encodedRate-personalRate)<0.000001) return 1;
            return Math.Abs(encodedRate-12)<0.001 || Math.Abs(encodedRate-2)<0.001 ? personalRate/encodedRate : 1;
        }
    }
}
