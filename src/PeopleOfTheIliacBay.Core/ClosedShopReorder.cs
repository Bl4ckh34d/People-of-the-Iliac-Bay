using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Advance the next already-planned visit, then return to the closed shop.
    // The caller supplies the one changed onward route from real native geometry.
    public static class ClosedShopReorder
    {
        public static ResidentDailyOuting TryCreate(ResidentDestination original,int waitingIndex,double now,double returnAt,
            double latestEnd,List<SavedRoutePoint> onward,double onwardSeconds)
        {
            if(original==null || waitingIndex<0 || waitingIndex+1>=original.Stops.Count || !Finite(now) || !Finite(returnAt) ||
                !Finite(latestEnd) || !Finite(onwardSeconds) || returnAt<=now || latestEnd<=returnAt || onwardSeconds<=0)return null;
            try
            {
                var copy=ResidentVisitJourney.Copy(original);
                if(copy.Walled || copy.ThroughRoute || copy.Stops.Exists(s=>s.KeepRoomUntil>0 || s.Purpose==ResidentVisitPurpose.Inn))return null;
                var waiting=copy.Stops[waitingIndex];var advanced=copy.Stops[waitingIndex+1];
                if(waiting.BuildingKey==advanced.BuildingKey)return null;
                double opening=ResidentVisitJourney.Opening(now,waiting);
                if(opening<=now || returnAt<opening || returnAt>opening+2700)return null;
                double dwell=returnAt-now-2*advanced.WalkSeconds;
                // Do not turn a ten-minute errand into an arbitrary multi-hour stay.
                if(dwell<300 || dwell>5400)return null;
                advanced.DwellSeconds=dwell;
                if(ResidentVisitJourney.Opening(now+advanced.WalkSeconds,advanced)>now+advanced.WalkSeconds)return null;
                var prefix=new List<SavedRoutePoint>();double prefixSeconds=0;
                for(int i=0;i<=waitingIndex;i++)
                {
                    prefixSeconds+=copy.Stops[i].WalkSeconds;
                    Append(prefix,copy.Stops[i].Route);
                }
                var reverse=new List<SavedRoutePoint>(advanced.Route);reverse.Reverse();
                double returnWalk=advanced.WalkSeconds;
                Append(prefix,advanced.Route);
                advanced.Route=prefix;advanced.WalkSeconds+=prefixSeconds;
                waiting.Route=reverse;waiting.WalkSeconds=returnWalk;
                var result=new ResidentDestination {ExitRoute=copy.ExitRoute,ExitWalkSeconds=copy.ExitWalkSeconds};
                result.Stops.Add(advanced);result.Stops.Add(waiting);
                for(int i=waitingIndex+2;i<copy.Stops.Count;i++)result.Stops.Add(copy.Stops[i]);
                if(result.Stops.Count>2){result.Stops[2].Route=onward;result.Stops[2].WalkSeconds=onwardSeconds;}
                else {result.ExitRoute=onward;result.ExitWalkSeconds=onwardSeconds;}
                double departure=now-prefixSeconds;
                if(departure<Math.Floor(now/86400)*86400)return null;
                var journey=ResidentVisitJourney.Create(departure,result);
                if(journey.EndsAt>latestEnd)return null;
                foreach(var visit in journey.ScheduledVisits())
                    if(visit.EnterAt>visit.ArriveAt+0.001)return null;
                return new ResidentDailyOuting {DepartureSeconds=departure,Itinerary=ResidentVisitJourney.Copy(result)};
            }
            catch(ArgumentException){return null;}
        }
        private static void Append(List<SavedRoutePoint> target,List<SavedRoutePoint> source)
        {for(int i=target.Count==0?0:1;i<source.Count;i++)target.Add(new SavedRoutePoint{X=source[i].X,Z=source[i].Z});}
        private static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
    }
}
