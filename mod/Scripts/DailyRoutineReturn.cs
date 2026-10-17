using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        // A cancelled later errand must start at that errand's door, not at the
        // legacy first destination. Reverse the already surveyed outward prefix.
        private static List<SavedRoutePoint> DailyReturnRoute(ResidentRoutine routine,int building)
        {
            var outward=new List<SavedRoutePoint>();
            bool found=false;
            if(routine.Itinerary!=null)
                foreach(var stop in routine.Itinerary.Stops)
                {
                    foreach(var point in stop.Route)
                    {
                        if(outward.Count>0 && outward[outward.Count-1].X==point.X && outward[outward.Count-1].Z==point.Z) continue;
                        outward.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                    }
                    if(stop.BuildingKey==building) {found=true;break;}
                }
            if(!found)
            {
                outward.Clear();
                if(routine.DestinationKey==building && routine.Route!=null)
                    foreach(var point in routine.Route) outward.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
            }
            outward.Reverse();
            return outward;
        }
    }
}
