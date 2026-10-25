using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    // The legacy itinerary is outing zero. Additional outings never duplicate it.
    public static class ResidentRoutineDay
    {
        public const int MaximumOutings=5,MaximumVisits=25,MaximumRoutePoints=100000;
        public static IEnumerable<ResidentDestination> Itineraries(ResidentRoutine routine)
        {
            if(routine==null)throw new ArgumentNullException(nameof(routine));
            if(routine.Itinerary!=null)yield return routine.Itinerary;
            if(routine.AdditionalOutings!=null)
                foreach(var outing in routine.AdditionalOutings)yield return outing.Itinerary;
        }
        public static List<ResidentDailyOuting> CopyOutings(ResidentRoutine routine)
        {
            if(routine==null)throw new ArgumentNullException(nameof(routine));
            var extra=routine.AdditionalOutings;
            if(extra!=null && extra.Count>=MaximumOutings)throw new ArgumentException("Too many daily outings.");
            var result=new List<ResidentDailyOuting>();
            if(routine.Itinerary==null)
            {
                if(extra!=null && extra.Count>0)throw new ArgumentException("Additional outings require their primary itinerary.");
                return result;
            }
            result.Add(Copy(routine.ItineraryDepartureSeconds,routine.Itinerary));
            if(extra!=null)foreach(var outing in extra)
            {if(outing==null)throw new ArgumentException("Null daily outing.");result.Add(Copy(outing.DepartureSeconds,outing.Itinerary));}
            double end=-1;int stops=0,points=0;
            var origin=result[0].Itinerary.Stops[0].Route[0];
            foreach(var outing in result)
            {
                if(result.Count>1 && outing.Itinerary.Walled)throw new ArgumentException("Composed local outings cannot reinterpret legacy gate timing.");
                var first=outing.Itinerary.Stops[0].Route[0];
                if(Math.Abs(first.X-origin.X)>0.00001 || Math.Abs(first.Z-origin.Z)>0.00001 || outing.DepartureSeconds<end)
                    throw new ArgumentException("Daily outings must share a home origin and cannot overlap.");
                var journey=ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary);
                end=journey.EndsAt;if(end>=86400)throw new ArgumentException("Daily outing crosses midnight.");
                stops+=outing.Itinerary.Stops.Count;points+=outing.Itinerary.ExitRoute.Count;
                foreach(var stop in outing.Itinerary.Stops)points+=stop.Route.Count;
            }
            // Preserve genuine legacy single itineraries under their original bounds.
            if(result.Count>1 && (stops>MaximumVisits || points>MaximumRoutePoints))
                throw new ArgumentException("Daily activity plan exceeds its bounded visit or route budget.");
            return result;
        }
        public static List<ResidentDailyOuting> CopyAdditionalOutings(ResidentRoutine routine)
        {
            if(routine==null)throw new ArgumentNullException(nameof(routine));
            if(routine.AdditionalOutings==null || routine.AdditionalOutings.Count==0)return new List<ResidentDailyOuting>();
            var result=CopyOutings(routine);if(result.Count>0)result.RemoveAt(0);return result;
        }
        public static DailySchedule Schedule(int home,ResidentRoutine routine,int wakeMinute,int sleepMinute)
        {
            var outings=CopyOutings(routine);var schedules=new List<DailySchedule>();int visitIndex=0;
            foreach(var outing in outings)
            {
                schedules.Add(DailySchedule.ItineraryDay(home,outing.Itinerary,outing.DepartureSeconds,wakeMinute,sleepMinute,visitIndex));
                visitIndex+=outing.Itinerary.Stops.Count;
            }
            return DailySchedule.OutingsDay(home,schedules,wakeMinute,sleepMinute);
        }
        public static List<ResidentVisitSnapshot> ScheduledVisits(ResidentRoutine routine)
        {
            var result=new List<ResidentVisitSnapshot>();int offset=0;
            foreach(var outing in CopyOutings(routine))
            {
                foreach(var visit in ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).ScheduledVisits())
                {
                    var global=visit;global.VisitIndex+=offset;result.Add(global);
                }
                offset+=outing.Itinerary.Stops.Count;
            }
            return result;
        }
        private static ResidentDailyOuting Copy(double departure,ResidentDestination itinerary)
        {
            if(double.IsNaN(departure) || double.IsInfinity(departure) || departure<0 || departure>=86400)
                throw new ArgumentException("Invalid daily outing departure.");
            var copy=ResidentVisitJourney.Copy(itinerary);
            if(copy.ThroughRoute || copy.Stops.Exists(s=>s.Purpose==ResidentVisitPurpose.Inn))
                throw new ArgumentException("A daily outing must return home without reserving an overnight inn.");
            return new ResidentDailyOuting{DepartureSeconds=departure,Itinerary=copy};
        }
    }
}
