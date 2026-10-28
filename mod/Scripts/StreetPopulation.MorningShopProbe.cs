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
            bool timing=Environment.GetEnvironmentVariable("POTIB_VERIFY_SHOP_TIMING")=="1";
            if(timing)
            {
                // Let normal preparation publish at home before measuring the
                // following day's departure. Never move an occupied journey.
                clock.RaiseTime((float)((5*3600-clock.ToSeconds()%86400+86400)%86400));
                DaggerfallUI.Instance.PopToHUD();GameManager.Instance.PauseGame(false);
                float ready=Time.realtimeSinceStartup+420;
                while(!OutdoorRoutesPrepared && Time.realtimeSinceStartup<ready)yield return null;
                if(!OutdoorRoutesPrepared)throw new InvalidOperationException("Routine timing probe requires completed native route preparation.");
                ready=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<ready)yield return null;
                if(Environment.GetEnvironmentVariable("POTIB_VERIFY_ALL_SHOP_QUEUES")=="1")
                    yield return VerifyAllShopQueues();
            }
            clock.RaiseTime((float)((33000-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();GameManager.Instance.PauseGame(false);
            float until=Time.realtimeSinceStartup+30;while(Time.realtimeSinceStartup<until)yield return null;
            double now=clock.ToSeconds();int expected=0,excluded=0,actual=0,grouped=0,inn=0;
            LogRoutineDestinationDistribution(now);
            int physicalQueue=LogClothingDoorBodies(now);
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
            if(timing)
            {
                int early=0,shops=0;
                foreach(var plan in plans)foreach(var visit in plan.Schedule.ScheduledVisits(now))
                    if(visit.Purpose==ResidentVisitPurpose.Shop)
                    {shops++;if(visit.EnterAt>visit.ArriveAt+1)early++;}
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_TIMING: shops="+shops+"; early="+early+"; waiters0910="+expected);
                if(shops==0 || early>0 || expected>0 || physicalQueue>0)throw new InvalidOperationException("Prepared shopping plans still contain early arrivals, physical door waiters or no shopping coverage.");
            }
            else
            {
                if(expected==0)throw new InvalidOperationException("The owned save does not reproduce clothing-store waiters at 09:10; no fabricated crowd counted.");
                if(actual==0)throw new InvalidOperationException("Actual clothing-store waiters received no leisure plan.");
            }
            clock.RaiseTime((float)Math.Max(0,10*3600-clock.ToSeconds()%86400));
            until=Time.realtimeSinceStartup+15;while(Time.realtimeSinceStartup<until)yield return null;
            int delayed=0;foreach(var id in tracked)
            {ClosedShopWait wait;if(closedShopWaits.TryGetValue(id,out wait) && clock.ToSeconds()<wait.ReturnAt)delayed++;}
            clock.RaiseTime((float)Math.Max(0,10*3600+45*60-clock.ToSeconds()%86400));
            until=Time.realtimeSinceStartup+30;while(Time.realtimeSinceStartup<until)yield return null;
            LogClothingDoorBodies(clock.ToSeconds());
            int overdue=0;foreach(var id in tracked)
            {ClosedShopWait wait;if(closedShopWaits.TryGetValue(id,out wait) && clock.ToSeconds()<wait.ReturnAt)overdue++;}
            if(overdue>0)throw new InvalidOperationException("Closed-shop leisure still prevents return after 10:45.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MORNING_SHOP_PASS: tracked="+tracked.Count+"; delayedAfterOpening="+delayed+
                "; overdueAt1045=0; ordinarySchedules=true");
        }
        private int LogClothingDoorBodies(double now)
        {
            int nearby=0,queued=0,entered=0;
            foreach(var entry in geometry.Entrances)
            {
                BuildingSummary summary;
                if(!directory.GetBuildingSummary(entry.Key,out summary) || summary.BuildingType!=DFLocation.BuildingTypes.ClothingStore)continue;
                foreach(var plan in plans)
                {
                    var observed=worldStore.GetObservedVisit(plan.Resident.Id);
                    if(observed!=null && observed.BuildingKey==entry.Key)entered++;
                    var actor=FindActor(plan.Resident.Id);if(!actor || !actor.gameObject.activeInHierarchy)continue;
                    Vector3 local=directory.transform.InverseTransformPoint(actor.transform.position);
                    var door=entry.Value.LocalDoor;
                    if(new Vector2(local.x-door.x,local.z-door.z).sqrMagnitude>9)continue;
                    nearby++;var state=ActiveSchedule(plan).EvaluateSeconds(now);
                    bool targeting=state.TravelDestination.HasValue &&
                        Math.Abs(state.TravelDestination.Value.X-door.x)<1 && Math.Abs(state.TravelDestination.Value.Z-door.z)<1;
                    if(actor.IsStandingStill && !actor.IsConversing && targeting)queued++;
                }
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CLOTHING_BODIES: clock="+(now%86400)+
                "; nearDoor="+nearby+"; stationaryTargetingDoor="+queued+"; observedVisits="+entered);
            return queued;
        }
        private IEnumerator VerifyAllShopQueues()
        {
            var shops=new List<TownGeometry.Entrance>();
            var opening=new Dictionary<int,int>();
            foreach(var entry in geometry.Entrances)
            {
                BuildingSummary summary;ResidentVisitPurpose purpose;
                if(!directory.GetBuildingSummary(entry.Key,out summary) ||
                    !TryGetDailyPurpose(summary.BuildingType,out purpose) || purpose!=ResidentVisitPurpose.Shop)continue;
                shops.Add(entry.Value);opening[entry.Key]=PlayerActivate.openHours[(int)summary.BuildingType]*3600;
            }
            shops.Sort((a,b)=>opening[a.BuildingKey].CompareTo(opening[b.BuildingKey]));
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            Vector3 observer=game.PlayerMotor.transform.position;int currentOpening=-1,totalQueued=0,seen=0;
            try
            {
                foreach(var shop in shops)
                {
                    if(currentOpening!=opening[shop.BuildingKey])
                    {
                        currentOpening=opening[shop.BuildingKey];
                        clock.RaiseTime((float)((currentOpening-3600-(int)(clock.ToSeconds()%86400)+86400)%86400));
                    }
                    var door=geometry.ScenePoint(new RoutePoint(shop.LocalDoor.x,shop.LocalDoor.z));bool placed=false;
                    for(int i=0;i<8;i++)
                    {
                        float angle=i*Mathf.PI/4;UnityEngine.AI.NavMeshHit sample;
                        Vector3 wanted=door+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*6;
                        if(!UnityEngine.AI.NavMesh.SamplePosition(wanted,out sample,1,UnityEngine.AI.NavMesh.AllAreas))continue;
                        game.PlayerMotor.transform.position=sample.position+Vector3.up;placed=true;break;
                    }
                    if(!placed)throw new InvalidOperationException("Shop queue tour has no safe observer point at "+shop.BuildingKey);
                    game.PlayerMotor.CancelMovement=true;Physics.SyncTransforms();
                    float until=Time.realtimeSinceStartup+3;
                    while(Time.realtimeSinceStartup<until)yield return null;
                    if((int)(clock.ToSeconds()%86400)>=currentOpening)throw new InvalidOperationException("Queue observation missed its pre-opening window.");
                    int queued=0,nearby=0;
                    foreach(var actor in actors.Values)
                    {
                        if(!actor || !actor.gameObject.activeInHierarchy)continue;
                        Vector3 local=directory.transform.InverseTransformPoint(actor.transform.position);
                        if(new Vector2(local.x-shop.LocalDoor.x,local.z-shop.LocalDoor.z).sqrMagnitude>9)continue;
                        nearby++;var state=GetState(actor.Resident.Id,clock.ToSeconds());
                        var native=actor.GetComponent<NativeResidentNavigation>();
                        bool targets=state.WaitingForBuildingKey==shop.BuildingKey || state.TravelDestination.HasValue &&
                            Math.Abs(state.TravelDestination.Value.X-shop.LocalDoor.x)<1 &&
                            Math.Abs(state.TravelDestination.Value.Z-shop.LocalDoor.z)<1 ||
                            native && native.HasDestination && (native.Destination-door).sqrMagnitude<1;
                        if(targets && actor.IsStandingStill && !actor.IsConversing)queued++;
                    }
                    seen++;totalQueued+=queued;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_QUEUE_OBSERVED: building="+shop.BuildingKey+
                        "; clock="+(clock.ToSeconds()%86400)+"; opening="+currentOpening+"; nearby="+nearby+"; queued="+queued);
                }
            }
            finally {game.PlayerMotor.transform.position=observer;Physics.SyncTransforms();}
            if(seen==0 || totalQueued>0)throw new InvalidOperationException("Physical shop queue tour found "+totalQueued+" stationary shoppers across "+seen+" shops.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ALL_SHOP_QUEUES_PASS: shops="+seen+"; preOpeningQueued=0; actualSpawnRadius=true");
        }
        private void LogRoutineDestinationDistribution(double now)
        {
            var types=new SortedDictionary<string,int>();var buildings=new SortedDictionary<string,int>();
            var hours=new SortedDictionary<string,int>();var available=new SortedDictionary<string,int>();
            int residents=0,visits=0;
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
                    // The query chooses the calendar day's schedule; its visit
                    // metadata remains seconds since midnight, not absolute time.
                    if(visit.EnterAt<0 || visit.EnterAt>=86400)
                        throw new InvalidOperationException("Ordinary routine visit is outside its daily clock.");
                    BuildingSummary summary;if(!directory.GetBuildingSummary(visit.BuildingKey,out summary))continue;
                    if(visit.Purpose==ResidentVisitPurpose.Shop && visit.EnterAt-visit.ArriveAt>1)
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_EARLY_SHOP_PLAN: resident="+plan.Resident.Id+
                            "; building="+visit.BuildingKey+"; type="+summary.BuildingType+
                            "; arrives="+visit.ArriveAt+"; enters="+visit.EnterAt+
                            "; outings="+ResidentRoutineDay.CopyOutings(plan.Routine).Count+
                            "; laneVersion="+plan.Routine.PersonalLaneVersion+"; prepared="+OutdoorRoutesPrepared);
                    visits++;string type=summary.BuildingType.ToString();int count;
                    types.TryGetValue(type,out count);types[type]=count+1;
                    string building=type+":"+visit.BuildingKey;buildings.TryGetValue(building,out count);buildings[building]=count+1;
                    string hour=((int)(visit.EnterAt/3600)).ToString("D2")+":"+type;
                    hours.TryGetValue(hour,out count);hours[hour]=count+1;
                }
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_DISTRIBUTION: residents="+residents+"; plannedVisits="+visits+"; observedArrivals=false");
            if(visits==0)throw new InvalidOperationException("Destination audit contains no planned visits; it cannot establish a distribution.");
            foreach(var row in available)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_AVAILABLE: type="+row.Key+"; entrances="+row.Value);
            foreach(var row in types)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_TYPE: type="+row.Key+"; visits="+row.Value);
            foreach(var row in buildings)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_BUILDING: key="+row.Key+"; visits="+row.Value);
            foreach(var row in hours)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DESTINATION_HOUR: key="+row.Key+"; visits="+row.Value);
        }
    }
}
