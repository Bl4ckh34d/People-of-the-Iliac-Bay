using System;
using System.Collections;
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
        internal IEnumerator VerifyMorningClothingShop()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((33000-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();GameManager.Instance.PauseGame(false);
            float until=Time.realtimeSinceStartup+30;while(Time.realtimeSinceStartup<until)yield return null;
            double now=clock.ToSeconds();int expected=0,excluded=0,actual=0,grouped=0,inn=0;
            LogRoutineDestinationDistribution(now);
            var tracked=new List<string>();var returns=new HashSet<double>();
            foreach(var plan in plans)
            {
                var state=ActiveSchedule(plan).EvaluateSeconds(now);ClosedShopWait wait;
                int building=closedShopWaits.TryGetValue(plan.Resident.Id,out wait)?wait.Building:state.WaitingForBuildingKey??-1;
                BuildingSummary summary;
                if(building<0 || !directory.GetBuildingSummary(building,out summary) || summary.BuildingType!=DFLocation.BuildingTypes.ClothingStore)continue;
                expected++;if(!ClosedShopEligible(plan,now))excluded++;
                if(wait==null)continue;
                actual++;tracked.Add(plan.Resident.Id);returns.Add(wait.ReturnAt%86400);
                if(wait.Group!=null && wait.Group.Members.Count>1)grouped++;
                if(wait.Inn>=0)inn++;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MORNING_SHOP_OBSERVED: clock="+(now%86400)+"; clothingWaiters="+expected+
                "; diversions="+actual+"; priorityExcluded="+excluded+"; grouped="+grouped+"; inns="+inn+"; distinctReturns="+returns.Count);
            if(expected==0)throw new InvalidOperationException("The owned save does not reproduce clothing-store waiters at 09:10; no fabricated crowd counted.");
            if(actual==0)throw new InvalidOperationException("Actual clothing-store waiters received no leisure plan.");
            clock.RaiseTime((float)Math.Max(0,10*3600-clock.ToSeconds()%86400));
            until=Time.realtimeSinceStartup+15;while(Time.realtimeSinceStartup<until)yield return null;
            int delayed=0;foreach(var id in tracked)
            {ClosedShopWait wait;if(closedShopWaits.TryGetValue(id,out wait) && clock.ToSeconds()<wait.ReturnAt)delayed++;}
            clock.RaiseTime((float)Math.Max(0,10*3600+45*60-clock.ToSeconds()%86400));
            until=Time.realtimeSinceStartup+30;while(Time.realtimeSinceStartup<until)yield return null;
            int overdue=0;foreach(var id in tracked)
            {ClosedShopWait wait;if(closedShopWaits.TryGetValue(id,out wait) && clock.ToSeconds()<wait.ReturnAt)overdue++;}
            if(overdue>0)throw new InvalidOperationException("Closed-shop leisure still prevents return after 10:45.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MORNING_SHOP_PASS: tracked="+tracked.Count+"; delayedAfterOpening="+delayed+
                "; overdueAt1045=0; ordinarySchedules=true");
        }
        private void LogRoutineDestinationDistribution(double now)
        {
            var types=new SortedDictionary<string,int>();var buildings=new SortedDictionary<string,int>();
            var hours=new SortedDictionary<string,int>();var available=new SortedDictionary<string,int>();
            double day=Math.Floor(now/86400)*86400;int residents=0,visits=0;
            foreach(var entry in geometry.Entrances)
            {
                BuildingSummary summary;if(!directory.GetBuildingSummary(entry.Key,out summary))continue;
                string key=summary.BuildingType.ToString();int count;available.TryGetValue(key,out count);available[key]=count+1;
            }
            foreach(var plan in plans)
            {
                if(!plan.Resident.IsAlive || plan.Schedule==null)continue;
                residents++;
                // Inspect the ordinary plan, before discretionary closed-shop
                // diversions. These are scheduled visits, not observed arrivals.
                foreach(var visit in plan.Schedule.ScheduledVisits(now))
                {
                    if(visit.EnterAt<day || visit.EnterAt>=day+86400)continue;
                    BuildingSummary summary;if(!directory.GetBuildingSummary(visit.BuildingKey,out summary))continue;
                    visits++;string type=summary.BuildingType.ToString();int count;
                    types.TryGetValue(type,out count);types[type]=count+1;
                    string building=type+":"+visit.BuildingKey;buildings.TryGetValue(building,out count);buildings[building]=count+1;
                    string hour=((int)((visit.EnterAt-day)/3600)).ToString("D2")+":"+type;
                    hours.TryGetValue(hour,out count);hours[hour]=count+1;
                }
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_DISTRIBUTION: residents="+residents+"; plannedVisits="+visits+"; observedArrivals=false");
            foreach(var row in available)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_AVAILABLE: type="+row.Key+"; entrances="+row.Value);
            foreach(var row in types)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_TYPE: type="+row.Key+"; visits="+row.Value);
            foreach(var row in buildings)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_BUILDING: key="+row.Key+"; visits="+row.Value);
            foreach(var row in hours)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_HOUR: key="+row.Key+"; visits="+row.Value);
        }
    }
}
