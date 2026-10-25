using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool TryAdvanceClosedShopVisit(Plan plan,ClosedShopWait wait,double now)
        {
            // Reorder only an ordinary outing, never an appointment or family override.
            if(plan.Routine==null || !ReferenceEquals(wait.Source,plan.Schedule))return false;
            double day=Math.Floor(now/86400)*86400;
            foreach(var outing in ResidentRoutineDay.CopyOutings(plan.Routine))
            {
                var journey=ResidentVisitJourney.Create(day+outing.DepartureSeconds,outing.Itinerary);
                var state=journey.Evaluate(now);
                if(state.Phase!=ResidentVisitPhase.WaitingForOpening || state.TargetBuildingKey!=wait.Building ||
                    state.VisitIndex+1>=outing.Itinerary.Stops.Count)continue;
                int index=state.VisitIndex;
                int onward=index+2<outing.Itinerary.Stops.Count?outing.Itinerary.Stops[index+2].BuildingKey:plan.Resident.HomeKey;
                var route=GetDailyRoute(wait.Building,onward);
                if(route==null)
                {
                    TownGeometry.Entrance destination;
                    if(!geometry.Entrances.TryGetValue(onward,out destination))return false;
                    route=SurveyDailyRoute(wait.Building,onward,geometry.RouteBetweenPoints(
                        new SavedRoutePoint{X=wait.LocalDoor.x,Z=wait.LocalDoor.z},
                        new SavedRoutePoint{X=destination.LocalDoor.x,Z=destination.LocalDoor.z}));
                }
                if(route.Points.Count<2)return false;
                var changed=ClosedShopReorder.TryCreate(outing.Itinerary,index,now,wait.ReturnAt,journey.EndsAt,route.Points,route.WalkSeconds);
                if(changed==null)return false;
                try
                {
                    var temporary=DailySchedule.ItineraryDay(plan.Resident.HomeKey,changed.Itinerary,changed.DepartureSeconds%86400,
                        wakeHour*60,sleepHour*60,8192);
                    wait.Schedule=DailySchedule.Interlude(wait.Source,temporary,now,journey.EndsAt);
                }
                catch(ArgumentException){return false;}
                wait.Reordered=changed;wait.EndsAt=journey.EndsAt;
                var names=GameManager.Instance.TalkManager;
                NativeSceneRecollection.Remember(worldStore,plan.Resident.Id,"replan:"+Guid.NewGuid().ToString("N"),NpcSceneEventKind.ClosedShop,
                    names.GetBuildingNameForBuildingKey(wait.Building)+" was closed. I moved my already planned visit to "+
                    names.GetBuildingNameForBuildingKey(changed.Itinerary.Stops[0].BuildingKey)+
                    " ahead of it and intend to return afterwards. These visits have not yet been completed.",-1);
                return true;
            }
            return false;
        }
        private bool ReorderedShopContext(ClosedShopWait wait,double now,Func<int,string> name,out string context)
        {
            var state=wait.Schedule.EvaluateSeconds(now);
            bool outside=!GameManager.Instance.PlayerEnterExit.IsPlayerInside;
            string activity;
            if(!outside && interior)activity="inside "+name(interior.EntryDoor.buildingKey);
            else if(state.Activity==Activity.Visit)activity="approaching "+name(state.BuildingKey)+"; still outside";
            else if(state.Activity==Activity.Home)activity="returning home; still outside";
            else
            {
                var current=ResidentVisitJourney.Create(wait.Reordered.DepartureSeconds,wait.Reordered.Itinerary).Evaluate(now);
                activity=current.TargetBuildingKey>=0?"on the way to "+name(current.TargetBuildingKey)+"; still outside":"on my way home; still outside";
            }
            context="Currently "+activity+". "+name(wait.Building)+" was closed when I arrived, so I brought forward my planned visit to "+
                name(wait.Reordered.Itinerary.Stops[0].BuildingKey)+". I then return to the original shop and continue the remaining outing.";
            return true;
        }
    }
}
