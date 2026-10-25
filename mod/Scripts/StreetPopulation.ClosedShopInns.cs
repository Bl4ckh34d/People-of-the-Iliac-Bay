using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool TryClosedShopInn(Plan plan,ClosedShopWait wait,double now)
        {
            if(interior || wait.ReturnAt-now<15*60)return false;
            TownGeometry.Entrance nearest=null;float best=50*50;
            foreach(var door in geometry.Entrances.Values)
            {
                BuildingSummary building;
                if(door.BuildingKey==wait.Building || !directory.GetBuildingSummary(door.BuildingKey,out building) ||
                    building.BuildingType!=DFLocation.BuildingTypes.Tavern)continue;
                float distance=(door.LocalDoor-wait.LocalDoor).sqrMagnitude;
                if(distance<best){best=distance;nearest=door;}
            }
            if(nearest==null)return false;
            var route=GetDailyRoute(wait.Building,nearest.BuildingKey);
            if(route==null)
            {
                var points=geometry.RouteBetweenPoints(new SavedRoutePoint{X=wait.LocalDoor.x,Z=wait.LocalDoor.z},
                    new SavedRoutePoint{X=nearest.LocalDoor.x,Z=nearest.LocalDoor.z});
                route=SurveyDailyRoute(wait.Building,nearest.BuildingKey,points);
            }
            if(route.Points.Count<2 || route.WalkSeconds<=0 || wait.ReturnAt-now-route.WalkSeconds*2<5*60)return false;
            var reverse=ReverseDailyRoute(route);
            int type=(int)DFLocation.BuildingTypes.Tavern;DailyServiceWindow hours;
            if(!DailyServiceHours.TryFromNativeHours(PlayerActivate.openHours[type],PlayerActivate.closeHours[type],out hours))return false;
            var stop=new ResidentVisitStop {BuildingKey=nearest.BuildingKey,Purpose=ResidentVisitPurpose.Social,
                OpenMinute=hours.OpenMinute,CloseMinute=hours.CloseMinute,Route=route.Points,
                WalkSeconds=route.WalkSeconds,DwellSeconds=wait.ReturnAt-now-route.WalkSeconds*2};
            if(ResidentVisitJourney.Opening(now,stop)>now)return false;
            var itinerary=new ResidentDestination {ExitRoute=reverse.Points,ExitWalkSeconds=route.WalkSeconds};
            itinerary.Stops.Add(stop);
            try
            {
                var temporary=DailySchedule.ItineraryDay(plan.Resident.HomeKey,itinerary,now%86400,wakeHour*60,sleepHour*60);
                wait.Schedule=DailySchedule.Interlude(wait.Source,temporary,now,wait.ReturnAt);
            }
            catch(ArgumentException){return false;}
            wait.Inn=nearest.BuildingKey;wait.InnRoute=route.Points;wait.InnReturnRoute=reverse.Points;
            wait.InnWalkSeconds=route.WalkSeconds;return true;
        }
        private bool ContinueClosedShopArrival(Plan plan,ResidentActor actor,ActivitySnapshot current,double now,out bool handled)
        {
            handled=false;ClosedShopWait wait;
            if(plan==null || !actor || !closedShopWaits.TryGetValue(plan.Resident.Id,out wait) || wait.Schedule==null ||
                current.Activity!=Activity.Visit && current.Activity!=Activity.Home)return false;
            if(wait.Reordered!=null)
            {
                List<SavedRoutePoint> route=current.Activity==Activity.Home?wait.Reordered.Itinerary.ExitRoute:null;
                foreach(var stop in wait.Reordered.Itinerary.Stops)if(stop.BuildingKey==current.BuildingKey)route=stop.Route;
                if(route==null)return false;
                handled=true;var end=route[route.Count-1];
                return ContinueOutdoorArrival(actor,new RoutePoint(end.X,end.Z),route);
            }
            if(current.BuildingKey==wait.Building && now>=wait.ReturnAt && wait.InnReturnRoute!=null)
            {
                handled=true;var home=wait.InnReturnRoute[wait.InnReturnRoute.Count-1];
                return ContinueOutdoorArrival(actor,new RoutePoint(home.X,home.Z),wait.InnReturnRoute);
            }
            if(current.BuildingKey!=wait.Inn || now>=wait.ReturnAt)return false;
            handled=true;var last=wait.InnRoute[wait.InnRoute.Count-1];
            return ContinueOutdoorArrival(actor,new RoutePoint(last.X,last.Z),wait.InnRoute);
        }
        internal bool TryClosedShopContext(ResidentActor actor,double now,Func<int,string> name,out string context)
        {
            context=null;ClosedShopWait wait;
            if(!actor || !closedShopWaits.TryGetValue(actor.Resident.Id,out wait) || now<wait.StartedAt || now>=wait.EndsAt ||
                IsAlarmed(actor.Resident.Id) || worldStore.ReadNpcCashVisit(actor.Resident.Id)!=null ||
                worldStore.ReadNpcCashWait(actor.Resident.Id)!=null || worldStore.ReadNpcTreatmentVisit(actor.Resident.Id)!=null)return false;
            if(wait.Reordered!=null)return ReorderedShopContext(wait,now,name,out context);
            bool outside=!GameManager.Instance.PlayerEnterExit.IsPlayerInside;
            if(!outside && (!interior || interior.EntryDoor.buildingKey!=wait.Inn))return false;
            string activity;
            if(!outside)activity="inside "+name(wait.Inn)+", spending the waiting time at this Inn before returning to "+name(wait.Building);
            else if(now>=wait.ReturnAt)activity="walking back to the entrance of "+name(wait.Building)+"; still outside";
            else if(wait.Inn>=0)
            {
                var state=wait.Schedule.EvaluateSeconds(now);
                var end=wait.InnRoute[wait.InnRoute.Count-1];
                bool outbound=state.TravelDestination.HasValue && Math.Abs(state.TravelDestination.Value.X-end.X)<0.01 &&
                    Math.Abs(state.TravelDestination.Value.Z-end.Z)<0.01;
                activity=outbound || state.BuildingKey==wait.Inn?"walking toward "+name(wait.Inn)+" for a short diversion; still outdoors":
                    "walking back from "+name(wait.Inn)+" toward "+name(wait.Building)+"; still outdoors";
            }
            else
            {
                var nearby=new List<string>();
                if(wait.Group!=null)foreach(string id in wait.Group.Members)
                {
                    var other=FindActor(id);ClosedShopWait otherWait;
                    if(id==actor.Resident.Id || !other || other.IsConversing || !closedShopWaits.TryGetValue(id,out otherWait) ||
                        now>=otherWait.ReturnAt || !otherWait.HasTarget ||
                        Vector3.Distance(other.transform.position-Vector3.up,directory.transform.TransformPoint(otherWait.LocalTarget))>0.6f ||
                        Vector3.Distance(actor.transform.position,other.transform.position)>3)continue;
                    nearby.Add(other.Person.NameNPC);
                }
                bool atTarget=wait.HasTarget && Vector3.Distance(actor.transform.position-Vector3.up,
                    directory.transform.TransformPoint(wait.LocalTarget))<0.6f;
                if(nearby.Count>0 && atTarget)activity="talking with "+string.Join(" and ",nearby.ToArray())+" outside "+name(wait.Building);
                else if(wait.Pastime==ClosedShopPastime.Browse && atTarget)activity="looking at the storefront of "+name(wait.Building)+" from outside";
                else if(wait.Pastime==ClosedShopPastime.Conversation && wait.Group!=null && wait.Group.Members.Count>1)
                    activity="joining a small conversation outside "+name(wait.Building);
                else activity=(atTarget?"taking a short pause":"taking a short walk")+" near "+name(wait.Building)+"; still outdoors";
            }
            context=ClosedShopLeisure.Context(name(wait.Building),wait.Opening,wait.ReturnAt,now,activity);return true;
        }
    }
}
