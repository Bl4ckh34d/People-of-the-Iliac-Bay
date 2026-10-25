using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class AdultDailyOutingSlot
    {
        public double DepartureAt, LatestHomeAt;
        public List<AdultDailyItineraryOption> Options=new List<AdultDailyItineraryOption>();
    }
    public sealed class AdultDailyRoutineRequest
    {
        public int HomeBuildingKey, SearchBudget=512;
        public uint Seed;
        public ResidentRoutine Source;
        public Func<double,double,bool> IsOutingAllowed;
        public List<AdultDailyOutingSlot> Slots=new List<AdultDailyOutingSlot>();
        public List<AdultDailyItineraryLeg> Routes=new List<AdultDailyItineraryLeg>();
    }

    // Plans optional local activity only. The native caller supplies service
    // windows, physical routes and slots that fit the resident's obligations.
    public static class AdultDailyRoutinePlanner
    {
        public static bool TryCreate(AdultDailyRoutineRequest request,out ResidentRoutine routine)
        {
            routine=null;
            if(request==null || request.Source==null || string.IsNullOrWhiteSpace(request.Source.ResidentId) ||
                request.Source.Route==null || request.HomeBuildingKey<0 || request.SearchBudget<0 || request.SearchBudget>512 ||
                request.Slots==null || request.Slots.Count>ResidentRoutineDay.MaximumOutings ||
                request.Routes==null || request.Routes.Count>60)
                throw new ArgumentException("Invalid adult daily routine request.");
            if(request.Source.Itinerary==null && request.Source.Route.Count>=2)return false;
            double previous=-1;
            foreach(var slot in request.Slots)
            {
                if(slot==null || !Finite(slot.DepartureAt) || !Finite(slot.LatestHomeAt) || slot.DepartureAt<0 ||
                    slot.LatestHomeAt>=86400 || slot.DepartureAt>=slot.LatestHomeAt || slot.DepartureAt<previous ||
                    slot.Options==null || slot.Options.Count>32)
                    throw new ArgumentException("Invalid daily outing slot.");
                previous=slot.LatestHomeAt;
            }
            var planned=Copy(request.Source);var outings=ResidentRoutineDay.CopyOutings(planned);
            var used=new HashSet<int>();bool temple=false,social=false;
            foreach(var outing in outings)foreach(var stop in outing.Itinerary.Stops)
            {used.Add(stop.BuildingKey);temple|=stop.Purpose==ResidentVisitPurpose.Temple;social|=stop.Purpose==ResidentVisitPurpose.Social;}
            double end=outings.Count==0?0:ResidentVisitJourney.Create(outings[outings.Count-1].DepartureSeconds,outings[outings.Count-1].Itinerary).EndsAt;
            int remaining=request.SearchBudget,added=0;
            foreach(var slot in request.Slots)
            {
                if(outings.Count>=ResidentRoutineDay.MaximumOutings || remaining==0)break;
                if(slot.DepartureAt<end)continue;
                var options=new List<AdultDailyItineraryOption>();
                foreach(var option in slot.Options)
                {
                    if(option==null)throw new ArgumentException("Null daily activity option.");
                    if(used.Contains(option.BuildingKey) || option.Purpose==ResidentVisitPurpose.Temple && temple ||
                        option.Purpose==ResidentVisitPurpose.Social && (social || slot.DepartureAt<16*3600))continue;
                    options.Add(option);
                }
                if(options.Count==0)continue;
                var buildings=new HashSet<int>{request.HomeBuildingKey};
                foreach(var option in options)buildings.Add(option.BuildingKey);
                var slotRoutes=new List<AdultDailyItineraryLeg>();
                foreach(var leg in request.Routes)
                {
                    if(leg==null)throw new ArgumentException("Null daily activity route.");
                    if(buildings.Contains(leg.FromBuildingKey) && buildings.Contains(leg.ToBuildingKey))slotRoutes.Add(leg);
                }
                ResidentDestination itinerary=null;
                for(int maximum=Math.Min(5,options.Count);maximum>=1 && remaining>0;maximum--)
                {
                    var childRequest=new AdultDailyItineraryRequest{HomeBuildingKey=request.HomeBuildingKey,
                        DepartureAt=slot.DepartureAt,LatestHomeAt=slot.LatestHomeAt,MinimumAwaySeconds=1,
                        MinimumStops=1,MaximumStops=maximum,Options=options,Routes=slotRoutes,
                        Seed=StableSeed.Combine(request.Seed,(uint)added,(uint)slot.DepartureAt,0x646179u)};
                    ResidentDestination proposed;
                    if(!AdultDailyItineraryPlanner.TryCreate(childRequest,ref remaining,out proposed))continue;
                    double returnsAt=ResidentVisitJourney.Create(slot.DepartureAt,proposed).EndsAt;
                    if(proposed.Stops.FindAll(s=>s.Purpose==ResidentVisitPurpose.Temple).Count>(temple?0:1) ||
                        proposed.Stops.FindAll(s=>s.Purpose==ResidentVisitPurpose.Social).Count>(social?0:1) ||
                        request.IsOutingAllowed!=null && !request.IsOutingAllowed(slot.DepartureAt,returnsAt))continue;
                    itinerary=proposed;break;
                }
                if(itinerary==null)continue;
                // A temple or social venue is one discretionary activity per day,
                // not a reason to visit every building in that category.
                if(itinerary.Stops.FindAll(s=>s.Purpose==ResidentVisitPurpose.Temple).Count>(temple?0:1) ||
                    itinerary.Stops.FindAll(s=>s.Purpose==ResidentVisitPurpose.Social).Count>(social?0:1))continue;
                var candidate=Copy(planned);
                if(candidate.Itinerary==null)
                {
                    candidate.Itinerary=itinerary;candidate.ItineraryDepartureSeconds=slot.DepartureAt;
                    var first=itinerary.Stops[0];candidate.DestinationKey=first.BuildingKey;
                    candidate.OpenMinute=first.OpenMinute;candidate.CloseMinute=first.CloseMinute;
                    candidate.Route=CopyRoute(first.Route);
                }
                else candidate.AdditionalOutings.Add(new ResidentDailyOuting{DepartureSeconds=slot.DepartureAt,Itinerary=itinerary});
                try {outings=ResidentRoutineDay.CopyOutings(candidate);}
                catch(ArgumentException){continue;}
                planned=candidate;added++;end=ResidentVisitJourney.Create(slot.DepartureAt,itinerary).EndsAt;
                foreach(var stop in itinerary.Stops)
                {used.Add(stop.BuildingKey);temple|=stop.Purpose==ResidentVisitPurpose.Temple;social|=stop.Purpose==ResidentVisitPurpose.Social;}
            }
            if(added==0)return false;
            routine=planned;return true;
        }
        private static ResidentRoutine Copy(ResidentRoutine source)
        {
            var outings=ResidentRoutineDay.CopyOutings(source);
            var result=new ResidentRoutine{ResidentId=source.ResidentId,DestinationKey=source.DestinationKey,
                OpenMinute=source.OpenMinute,CloseMinute=source.CloseMinute,Route=CopyRoute(source.Route),
                ItineraryDepartureSeconds=source.ItineraryDepartureSeconds,RouteGeometryVersion=source.RouteGeometryVersion,
                PersonalLaneVersion=source.PersonalLaneVersion};
            if(outings.Count>0){result.Itinerary=outings[0].Itinerary;outings.RemoveAt(0);result.AdditionalOutings=outings;}
            return result;
        }
        private static List<SavedRoutePoint> CopyRoute(List<SavedRoutePoint> source)
        {
            if(source==null)throw new ArgumentException("Missing daily route.");
            var result=new List<SavedRoutePoint>();
            foreach(var point in source)
            {if(point==null || !Finite(point.X) || !Finite(point.Z))throw new ArgumentException("Invalid daily route point.");
                result.Add(new SavedRoutePoint{X=point.X,Z=point.Z});}
            return result;
        }
        private static bool Finite(double value){return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
