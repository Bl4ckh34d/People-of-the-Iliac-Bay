using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static class OutdoorRoutineMigration
    {
        public const int CenterlineVersion=2;
        public const int CurrentVersion=2;
        // Increment when personal route timing or lane layout changes so saved routes are rebuilt.
        public const int CurrentPersonalLaneVersion=4;
        public static bool NeedsCenterlineRebuild(ResidentRoutine source)
        {return source.RouteGeometryVersion<CenterlineVersion ||
            source.PersonalLaneVersion>0 && source.PersonalLaneVersion<CurrentPersonalLaneVersion;}
        public static ResidentRoutine Rebuild(ResidentRoutine source,IList<List<SavedRoutePoint>> routes,double speed,double rate,
            bool personalLanes=false,int preserveOutings=0)
        {
            if(source==null || routes==null || !Finite(speed) || speed<=0 || !Finite(rate) || rate<=0)
                throw new ArgumentException("Invalid outdoor routine migration.");
            var outings=ResidentRoutineDay.CopyOutings(source);
            if(preserveOutings<0 || preserveOutings>outings.Count || preserveOutings>0 &&
                (!personalLanes || source.PersonalLaneVersion!=CurrentPersonalLaneVersion))
                throw new ArgumentException("Only unchanged personal outings can retain their saved timing.");
            int count=source.Itinerary==null ? (source.Route.Count>=2 ? 1 : 0) : 0;
            foreach(var outing in outings)count+=outing.Itinerary.Stops.Count+1;
            if(routes.Count!=count) throw new ArgumentException("Outdoor migration requires every saved leg.");
            var result=new ResidentRoutine {ResidentId=source.ResidentId,DestinationKey=source.DestinationKey,
                OpenMinute=source.OpenMinute,CloseMinute=source.CloseMinute,ItineraryDepartureSeconds=source.ItineraryDepartureSeconds,
                RouteGeometryVersion=personalLanes ? CurrentVersion : CenterlineVersion,
                PersonalLaneVersion=personalLanes ? CurrentPersonalLaneVersion : 0};
            if(source.Itinerary==null)
            {result.Route=count==0 ? new List<SavedRoutePoint>() : CheckedCopy(source.Route,routes[0]);return result;}
            int routeIndex=0,outingIndex=0;
            foreach(var outing in outings)
            {
                var itinerary=outing.Itinerary;
                bool preserve=outingIndex++<preserveOutings;
                // Local daily routes cannot reinterpret foreign gate intervals.
                if(itinerary.Walled || itinerary.EntryGateSeconds!=0 || itinerary.ExitGateSeconds!=0)
                    throw new ArgumentException("Outdoor daily migration cannot replace explicit gate timing.");
                foreach(var stop in itinerary.Stops)
                {
                    if(preserve && !SameRoute(stop.Route,routes[routeIndex]))
                        throw new ArgumentException("Preserved outing geometry changed.");
                    stop.Route=CheckedCopy(stop.Route,routes[routeIndex++]);
                    if(!preserve)stop.WalkSeconds=Length(stop.Route)*rate/speed;
                }
                if(preserve && !SameRoute(itinerary.ExitRoute,routes[routeIndex]))
                    throw new ArgumentException("Preserved return geometry changed.");
                itinerary.ExitRoute=CheckedCopy(itinerary.ExitRoute,routes[routeIndex++]);
                if(!preserve)itinerary.ExitWalkSeconds=Length(itinerary.ExitRoute)*rate/speed;
                if(result.Itinerary==null)result.Itinerary=itinerary;
                else result.AdditionalOutings.Add(outing);
            }
            result.Route=preserveOutings>0?Copy(source.Route):
                source.DestinationKey>=0 ? Copy(result.Itinerary.Stops[0].Route) : new List<SavedRoutePoint>();
            ResidentRoutineDay.CopyOutings(result);
            return result;
        }
        public static bool CanPublishAtHome(ActivitySnapshot current,ActivitySnapshot candidate,int home)
        {return AtHome(current,home) && AtHome(candidate,home);}
        public static bool CanPublishAtHome(WorldStateStore store,string residentId,double now,
            ActivitySnapshot current,ActivitySnapshot candidate,int home)
        {return !store.IsResidentAway(residentId,now) && CanPublishAtHome(current,candidate,home);}
        public static List<SavedRoutePoint> RuntimeRoute(ResidentRoutine source,Func<List<SavedRoutePoint>,List<SavedRoutePoint>> selectLane)
        {return source.PersonalLaneVersion>0 ? Copy(source.Route) : selectLane(Copy(source.Route));}
        public static ResidentDestination RuntimeItinerary(ResidentRoutine source,Func<List<SavedRoutePoint>,List<SavedRoutePoint>> selectLane)
        {
            var copy=ResidentVisitJourney.Copy(source.Itinerary);
            if(source.PersonalLaneVersion>0) return copy;
            foreach(var stop in copy.Stops)
            {double before=Length(stop.Route);stop.Route=selectLane(stop.Route);if(before>0) stop.WalkSeconds*=Length(stop.Route)/before;}
            double exitBefore=Length(copy.ExitRoute);copy.ExitRoute=selectLane(copy.ExitRoute);
            if(exitBefore>0) copy.ExitWalkSeconds*=Length(copy.ExitRoute)/exitBefore;
            if(copy.Stops.Count>0) copy.Stops[0].WalkSeconds=Math.Max(copy.Stops[0].WalkSeconds,copy.EntryGateSeconds);
            copy.ExitWalkSeconds=Math.Max(copy.ExitWalkSeconds,copy.ExitGateSeconds);
            return copy;
        }
        private static bool AtHome(ActivitySnapshot state,int home)
        {return state.BuildingKey==home && (state.Activity==Activity.Home || state.Activity==Activity.Sleep);}
        private static bool SameRoute(IList<SavedRoutePoint> first,IList<SavedRoutePoint> second)
        {
            if(first==null || second==null || first.Count!=second.Count)return false;
            for(int i=0;i<first.Count;i++)
                if(first[i]==null || second[i]==null || first[i].X!=second[i].X || first[i].Z!=second[i].Z)return false;
            return true;
        }
        private static List<SavedRoutePoint> CheckedCopy(IList<SavedRoutePoint> original,List<SavedRoutePoint> replacement)
        {
            if(replacement==null || replacement.Count<2 || replacement.Count>10000 ||
                !Same(original[0],replacement[0]) || !Same(original[original.Count-1],replacement[replacement.Count-1]) || Length(replacement)<=0)
                throw new ArgumentException("Outdoor migration changed exact route endpoints.");
            return Copy(replacement);
        }
        private static List<SavedRoutePoint> Copy(IList<SavedRoutePoint> source)
        {var copy=new List<SavedRoutePoint>();foreach(var p in source) copy.Add(new SavedRoutePoint {X=p.X,Z=p.Z});return copy;}
        private static double Length(IList<SavedRoutePoint> route)
        {double length=0;for(int i=0;i<route.Count;i++) {var p=route[i];if(p==null || !Finite(p.X) || !Finite(p.Z)) throw new ArgumentException("Invalid migration point.");
            if(i>0) {double x=p.X-route[i-1].X,z=p.Z-route[i-1].Z;length+=Math.Sqrt(x*x+z*z);}}return length;}
        private static bool Same(SavedRoutePoint a,SavedRoutePoint b)
        {return a!=null && b!=null && Math.Abs(a.X-b.X)<0.00001 && Math.Abs(a.Z-b.Z)<0.00001;}
        private static bool Finite(double n) {return !double.IsNaN(n) && !double.IsInfinity(n);}
    }
}
