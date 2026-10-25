using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static partial class AppointmentDayPlanner
    {
        private static void AddHouseholdGraph(RouteGraph graph,HouseholdDayPlan plan,string residentId)
        {
            if(plan.JointInn==null)return;
            var outing=plan.First.ResidentId==residentId?plan.JointInn.First:plan.JointInn.Second;
            int previous=plan.HomeKey;
            foreach(var stop in outing.Itinerary.Stops){graph.Add(previous,stop.BuildingKey,stop.Route);previous=stop.BuildingKey;}
            graph.Add(previous,plan.HomeKey,outing.Itinerary.ExitRoute);
        }
        private static void BuildHouseholdOptional(Resident resident,ResidentRoutine routine,HouseholdDayPlan plan,List<Outing> result)
        {
            var member=plan.First.ResidentId==resident.Id?plan.First:plan.Second;
            var originals=ResidentRoutineDay.CopyOutings(routine);
            foreach(var selected in member.Outings)
            {
                var source=originals[selected.SourceOutingIndex];int offset=0;
                for(int index=0;index<selected.SourceOutingIndex;index++)offset+=originals[index].Itinerary.Stops.Count;
                AddHouseholdOptional(resident,source.Itinerary,selected.DepartureSeconds,offset,plan,false,result);
            }
            if(plan.JointInn!=null && !plan.JointSuppressed)
            {
                var outing=plan.First.ResidentId==resident.Id?plan.JointInn.First:plan.JointInn.Second;
                AddHouseholdOptional(resident,outing.Itinerary,outing.DepartureSeconds,HouseholdDayPlanner.JointVisitIndex,plan,true,result);
            }
        }
        private static void AddHouseholdOptional(Resident resident,ResidentDestination itinerary,double departure,int index,
            HouseholdDayPlan plan,bool joint,List<Outing> result)
        {
            var journey=ResidentVisitJourney.Create(departure,itinerary);
            var visits=new List<ResidentVisitSnapshot>(journey.ScheduledVisits());
            result.Add(new Outing{Departure=departure,Arrival=visits[0].EnterAt,Leave=visits[visits.Count-1].LeaveAt,
                Return=journey.EndsAt,Building=visits[0].BuildingKey,Joint=joint,
                Schedule=DailySchedule.ItineraryDay(resident.HomeKey,itinerary,departure,plan.WakeMinute,plan.SleepMinute,index)});
        }
    }
}
