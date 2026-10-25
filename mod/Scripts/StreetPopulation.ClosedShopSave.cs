using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void SaveClosedShopWait(string id,ClosedShopWait wait)
        {
            worldStore.SaveClosedShopVisit(new ClosedShopVisit {PersonId=id,SettlementId=population.SettlementId,
                Building=wait.Building,Inn=wait.Inn,StartedAt=wait.StartedAt,Opening=wait.Opening,ReturnAt=wait.ReturnAt,
                EndsAt=wait.EndsAt,Pastime=wait.Pastime,WalkSeconds=wait.InnWalkSeconds,
                InnRoute=wait.InnRoute??new List<SavedRoutePoint>(),Reordered=wait.Reordered});
        }
        private void RestoreClosedShopWait(Plan plan,DailySchedule source,double now)
        {
            if(worldStore==null || population==null || geometry==null || source==null)return;
            var saved=worldStore.ReadClosedShopVisit(plan.Resident.Id,population.SettlementId,now);
            if(saved==null)return;
            TownGeometry.Entrance door;
            if(!geometry.Entrances.TryGetValue(saved.Building,out door) || !source.ContainsVisitBuilding(saved.Building))return;
            var wait=new ClosedShopWait {Building=saved.Building,Inn=saved.Inn,StartedAt=saved.StartedAt,Opening=saved.Opening,
                ReturnAt=saved.ReturnAt,EndsAt=saved.EndsAt,Pastime=saved.Pastime,LocalDoor=door.LocalDoor,Source=source,
                Seed=ClosedShopLeisure.Seed(plan.Resident.AppearanceSeed,saved.Building,saved.Opening),
                InnRoute=saved.InnRoute,InnWalkSeconds=saved.WalkSeconds,Reordered=saved.Reordered};
            if(saved.Reordered!=null)
            {
                try
                {
                    var temporary=DailySchedule.ItineraryDay(plan.Resident.HomeKey,saved.Reordered.Itinerary,
                        saved.Reordered.DepartureSeconds%86400,wakeHour*60,sleepHour*60,8192);
                    wait.Schedule=DailySchedule.Interlude(source,temporary,saved.StartedAt,saved.EndsAt);
                }
                catch(ArgumentException){worldStore.ForgetClosedShopVisit(plan.Resident.Id);return;}
            }
            else if(saved.Inn>=0)
            {
                var back=new List<SavedRoutePoint>(saved.InnRoute);back.Reverse();
                wait.InnReturnRoute=back;
                var trip=new ResidentDestination {ExitRoute=back,ExitWalkSeconds=saved.WalkSeconds};
                trip.Stops.Add(new ResidentVisitStop {BuildingKey=saved.Inn,Purpose=ResidentVisitPurpose.Social,
                    OpenMinute=0,CloseMinute=0,Route=saved.InnRoute,WalkSeconds=saved.WalkSeconds,
                    DwellSeconds=saved.ReturnAt-saved.StartedAt-2*saved.WalkSeconds});
                try
                {
                    var temporary=DailySchedule.ItineraryDay(plan.Resident.HomeKey,trip,saved.StartedAt%86400,wakeHour*60,sleepHour*60);
                    wait.Schedule=DailySchedule.Interlude(source,temporary,saved.StartedAt,saved.ReturnAt);
                }
                catch(ArgumentException){worldStore.ForgetClosedShopVisit(plan.Resident.Id);return;}
            }
            closedShopWaits[plan.Resident.Id]=wait;
        }
    }
}
