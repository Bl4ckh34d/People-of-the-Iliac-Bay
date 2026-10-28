using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    // Stable variation across residents, constrained by every service window.
    // Runtime publishes changes only when both schedules place the resident at home.
    public static class DailyOutingDistribution
    {
        public static ResidentRoutine Spread(ResidentRoutine source, uint seed, int wake, int sleep)
        {return Spread(source,seed,wake,sleep,sleep*3600d-1);}

        public static ResidentRoutine Spread(ResidentRoutine source,uint seed,int wake,int sleep,double latestHomeAt)
        {
            if(wake<0 || sleep>24 || wake>=sleep || double.IsNaN(latestHomeAt) || double.IsInfinity(latestHomeAt) ||
                latestHomeAt<wake*3600d || latestHomeAt>=sleep*3600d)
                throw new ArgumentException("Invalid daily outing return window.");
            if (source == null || source.Itinerary == null || source.Itinerary.Walled || source.Itinerary.ThroughRoute)
                return source;
            if(source.AdditionalOutings!=null && source.AdditionalOutings.Count>0)
                return DelayEarlyShopping(source,seed,latestHomeAt);
            var data = source.Itinerary;
            double latest = latestHomeAt - data.ExitWalkSeconds;
            for (int i = data.Stops.Count - 1; i >= 0; i--)
            {
                var stop = data.Stops[i];
                if (stop.OpenMinute >= stop.CloseMinute || stop.KeepRoomUntil > 0) return source;
                latest = Math.Min(latest, stop.CloseMinute * 60d) - stop.DwellSeconds;
                if (latest < stop.OpenMinute * 60d) return source;
                latest -= stop.WalkSeconds;
            }
            double earliest = Math.Max(wake * 3600d, 8 * 3600);
            double elapsed=0;
            foreach(var stop in data.Stops)
            {
                elapsed+=stop.WalkSeconds;
                if(stop.Purpose==ResidentVisitPurpose.Shop)earliest=Math.Max(earliest,stop.OpenMinute*60d-elapsed);
                elapsed+=stop.DwellSeconds;
            }
            // This is the first outing. Later native day windows provide the
            // afternoon/evening activity; a late start would consume those slots.
            latest = Math.Min(latest,Math.Min(12*3600,earliest+2*3600));
            if (latest < earliest) return source;
            uint rank = seed;
            unchecked { rank ^= rank >> 16; rank *= 0x7feb352du; rank ^= rank >> 15; rank *= 0x846ca68bu; rank ^= rank >> 16; }
            double departure = earliest + Math.Floor((latest - earliest) * (rank / 4294967296d));
            if (departure == source.ItineraryDepartureSeconds) return source;
            var copy = ResidentVisitJourney.Copy(data);
            var journey = ResidentVisitJourney.Create(departure, copy);
            if (journey.EndsAt >= sleep * 3600d || journey.EndsAt > latestHomeAt) return source;
            foreach (var visit in journey.ScheduledVisits())
            {
                var stop = copy.Stops[visit.VisitIndex];
                if (visit.EnterAt < stop.OpenMinute * 60d || visit.LeaveAt > stop.CloseMinute * 60d) return source;
                if (stop.Purpose == ResidentVisitPurpose.Social && visit.EnterAt < 16 * 3600) return source;
            }
            var result = new ResidentRoutine { ResidentId=source.ResidentId, DestinationKey=source.DestinationKey,
                RouteGeometryVersion=source.RouteGeometryVersion, PersonalLaneVersion=source.PersonalLaneVersion,
                OpenMinute=source.OpenMinute, CloseMinute=source.CloseMinute,
                Itinerary=copy, ItineraryDepartureSeconds=departure };
            foreach (var point in source.Route) result.Route.Add(new SavedRoutePoint { X=point.X,Z=point.Z });
            return result;
        }

        // Native hours and physical route durations can change after a day has
        // acquired additional outings. Recheck actual arrival, not the journey's
        // opening-clamped entry time. Valid outings keep their original times.
        private static ResidentRoutine DelayEarlyShopping(ResidentRoutine source,uint seed,double latestHomeAt)
        {
            var outings=ResidentRoutineDay.CopyOutings(source);bool changed=false;
            for(int index=0;index<outings.Count;index++)
            {
                var outing=outings[index];var data=outing.Itinerary;
                var before=ResidentVisitJourney.Create(outing.DepartureSeconds,data);bool early=false;
                foreach(var visit in before.ScheduledVisits())
                    if(visit.Purpose==ResidentVisitPurpose.Shop && visit.EnterAt>visit.ArriveAt+1)early=true;
                if(!early || data.Walled || data.ThroughRoute || data.Stops.Exists(s=>s.KeepRoomUntil>0))continue;
                double deadline=latestHomeAt;
                if(index+1<outings.Count)
                {
                    double next=outings[index+1].DepartureSeconds;
                    double homeBreak=Math.Min(20*60,Math.Max(0,next-before.EndsAt));
                    deadline=Math.Min(deadline,next-homeBreak);
                }
                double earliest=outing.DepartureSeconds,elapsed=0;
                double latest=deadline-data.ExitWalkSeconds;bool supported=true;
                foreach(var stop in data.Stops)
                {
                    if(stop.OpenMinute>=stop.CloseMinute){supported=false;break;}
                    elapsed+=stop.WalkSeconds;
                    earliest=Math.Max(earliest,stop.OpenMinute*60d-elapsed);
                    if(stop.Purpose==ResidentVisitPurpose.Social)earliest=Math.Max(earliest,16*3600-elapsed);
                    elapsed+=stop.DwellSeconds;
                }
                if(!supported)continue;
                for(int i=data.Stops.Count-1;i>=0;i--)
                {
                    var stop=data.Stops[i];
                    latest=Math.Min(latest,stop.CloseMinute*60d)-stop.DwellSeconds-stop.WalkSeconds;
                }
                latest=Math.Min(latest,earliest+2*3600);
                if(latest<earliest)continue;
                uint rank=StableSeed.Combine(seed,(uint)index,(uint)data.Stops[0].BuildingKey,0x73686f70u);
                double departure=earliest+Math.Floor((latest-earliest)*(rank/4294967296d));
                var after=ResidentVisitJourney.Create(departure,data);
                if(after.EndsAt>deadline)continue;
                bool fits=true;
                foreach(var visit in after.ScheduledVisits())
                    if(visit.EnterAt>visit.ArriveAt+0.001)fits=false;
                if(!fits)continue;
                outing.DepartureSeconds=departure;changed=true;
            }
            if(!changed)return source;
            var result=new ResidentRoutine{ResidentId=source.ResidentId,DestinationKey=source.DestinationKey,
                RouteGeometryVersion=source.RouteGeometryVersion,PersonalLaneVersion=source.PersonalLaneVersion,
                OpenMinute=source.OpenMinute,CloseMinute=source.CloseMinute,
                Itinerary=outings[0].Itinerary,ItineraryDepartureSeconds=outings[0].DepartureSeconds};
            foreach(var point in source.Route)result.Route.Add(new SavedRoutePoint{X=point.X,Z=point.Z});
            for(int i=1;i<outings.Count;i++)result.AdditionalOutings.Add(outings[i]);
            ResidentRoutineDay.CopyOutings(result);
            return result;
        }
    }
}
