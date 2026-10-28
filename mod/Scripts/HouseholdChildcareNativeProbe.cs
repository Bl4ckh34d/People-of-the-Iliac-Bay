using System;
using System.Collections;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // One observation interval from an actual published household day. Only
    // fixture clock/observer change; the ordinary population owns all bodies.
    internal static class HouseholdChildcareNativeProbe
    {
        private sealed class Selection
        {
            internal HouseholdDayPlan Plan;
            internal string HomeParent,AwayParent,Child;
            internal int Visit;
            internal double At;
        }
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            if(!host || !streets || !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name))
                throw new InvalidOperationException("Childcare home observation needs the owned disposable native fixture.");
            if(game.IsPlayerInside){game.PlayerEnterExit.TransitionExterior();yield return new WaitForSecondsRealtime(1);}
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var store=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
            Selection selected=null;float deadline=Time.realtimeSinceStartup+180;
            while(selected==null && Time.realtimeSinceStartup<deadline)
            {
                if(streets.Ready && streets.EntranceSurveyReady && directory)
                    selected=Select(store,directory,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                if(selected==null)yield return new WaitForSecondsRealtime(.5f);
            }
            if(selected==null)throw new InvalidOperationException("No naturally published current childcare day has a future daylight Home/Visit/child-Home interval.");
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if(selected.At>now)DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(selected.At-now));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CHILDCARE_HOME_FIXTURE: home="+selected.Plan.HomeKey+
                "; day="+selected.Plan.AbsoluteDay+"; at="+selected.At+"; homeParent="+selected.HomeParent+
                "; awayParent="+selected.AwayParent+"; child="+selected.Child+"; actualPublishedPlan=true; controlledClock=true; injectedPlan=false");
            // Observe the away parent through normal placement in the actual
            // selected visit building before visiting the family's home.
            Transform owner;StaticDoor door;
            FindDoor(directory,selected.Visit,out owner,out door);
            TownSmokeHarness.EnterInterior(owner,door);yield return null;
            deadline=Time.realtimeSinceStartup+45;ResidentActor away=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                var room=game.PlayerEnterExit.Interior;away=streets.FindActor(selected.AwayParent);
                if(room && room.EntryDoor.buildingKey==selected.Visit && ActualAwakeBody(away,room))break;
                yield return null;
            }
            var visitRoom=game.PlayerEnterExit.Interior;
            if(!visitRoom || visitRoom.EntryDoor.buildingKey!=selected.Visit || !ActualAwakeBody(away,visitRoom))
                throw new InvalidOperationException("The naturally scheduled away parent did not appear through normal native visit placement.");
            VerifyStates(store,streets,selected);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CHILDCARE_AWAY_PARENT_OBSERVED: person="+selected.AwayParent+
                "; building="+selected.Visit+"; actualBody=true; normalPopulation=true; physicalArrivalWalk=false");
            game.PlayerEnterExit.TransitionExterior();yield return new WaitForSecondsRealtime(1);
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            VerifyStates(store,streets,selected);
            if(DaggerfallUnity.Instance.WorldTime.Now.IsNight || streets.HouseholdRoutineLocked(selected.Plan.HomeKey)!=false)
                throw new InvalidOperationException("Actual awake-parent childcare home did not have native daylight access.");
            HouseholdInnPhysicalProbe.ActivateFamilyHome(directory,selected.Plan.HomeKey,true);
            yield return null;
            var greeting=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;if(greeting!=null)greeting.CloseWindow();
            deadline=Time.realtimeSinceStartup+30;
            while(!game.PlayerEnterExit.IsPlayerInsideBuilding && Time.realtimeSinceStartup<deadline)yield return null;
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            deadline=Time.realtimeSinceStartup+30;ResidentActor parent=null,child=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                var room=game.PlayerEnterExit.Interior;parent=streets.FindActor(selected.HomeParent);child=streets.FindActor(selected.Child);
                if(room && room.EntryDoor.buildingKey==selected.Plan.HomeKey && ActualAwakeBody(parent,room) && ActualAwakeBody(child,room))break;
                yield return null;
            }
            var homeRoom=game.PlayerEnterExit.Interior;
            if(!homeRoom || homeRoom.EntryDoor.buildingKey!=selected.Plan.HomeKey || !ActualAwakeBody(parent,homeRoom) ||
                !ActualAwakeBody(child,homeRoom) || child.Resident.AgeGroup!=ResidentAgeGroup.Child ||
                child.Resident.MotherId!=selected.HomeParent && child.Resident.FatherId!=selected.HomeParent)
                throw new InvalidOperationException("Normal native home placement did not show the actual awake parent with their actual child.");
            VerifyStates(store,streets,selected);
            away=streets.FindActor(selected.AwayParent);
            if(away && away.gameObject.activeInHierarchy && away.transform.parent==homeRoom.transform)
                throw new InvalidOperationException("The away parent incorrectly appeared at the observed childcare home.");
            if(DaggerfallUnity.Instance.WorldTime.Now.IsNight || streets.HouseholdRoutineLocked(selected.Plan.HomeKey)!=false)
                throw new InvalidOperationException("The observed awake-parent home lost its actual daylight access.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CHILDCARE_HOME_NATIVE_PASS: home="+selected.Plan.HomeKey+
                "; actualPublishedPlan=true; childcareDay=true; jointInn=false; actualAwayParentBody=true; actualHomeParentBody=true; actualChildBody=true; "+
                "awayParentNotAtHome=true; nativeDoorUnlocked=true; actualNativeHomeEntry=true; normalPopulation=true; controlledClock=true; injectedBodies=false; wholeDay=false");
        }
        private static Selection Select(WorldStateStore store,BuildingDirectory directory,double now)
        {
            if(store==null)return null;
            int day=(int)Math.Floor(now/86400d);var town=store.CurrentPopulation(directory.MapID);
            if(town==null)return null;
            foreach(var home in town.Households)
            {
                var plan=store.ReadHouseholdDay(directory.MapID,home.BuildingKey,day);
                if(plan==null || plan.Status!="planned" || plan.JointInn!=null || plan.ChildIds.Count==0)continue;
                BuildingSummary summary;
                if(!HouseholdDayPlanner.RequiresChildcare(store.WorldSeed,directory.MapID,home,day) ||
                    !directory.GetBuildingSummary(plan.HomeKey,out summary) || summary.FactionId!=0 ||
                    summary.BuildingType<DFLocation.BuildingTypes.House1 || summary.BuildingType>DFLocation.BuildingTypes.House6)continue;
                var child=home.Residents.Find(x=>x.IsAlive && x.AgeGroup==ResidentAgeGroup.Child && plan.ChildIds.Contains(x.Id));
                if(child==null)continue;
                DailySchedule first,second;string reason;
                if(!store.TryHouseholdDaySchedules(directory.MapID,home.BuildingKey,day,out first,out second,out reason))continue;
                var schedules=new[]{first,second};
                for(double at=Math.Max(now+60,Math.Max(plan.PlannedAt,day*86400d+7*3600));at<day*86400d+18*3600-600;at+=60)
                    for(int i=0;i<2;i++)
                    {
                        var a=schedules[i].EvaluateSeconds(at);var b=schedules[1-i].EvaluateSeconds(at);
                        var tailA=schedules[i].EvaluateSeconds(at+600);var tailB=schedules[1-i].EvaluateSeconds(at+600);
                        if(a.Activity!=Activity.Home || a.BuildingKey!=plan.HomeKey || b.Activity!=Activity.Visit || b.BuildingKey==plan.HomeKey || b.BuildingKey<0 ||
                            tailA.Activity!=Activity.Home || tailA.BuildingKey!=plan.HomeKey || tailB.Activity!=Activity.Visit || tailB.BuildingKey!=b.BuildingKey ||
                            !directory.GetBuildingSummary(b.BuildingKey,out summary))continue;
                        return new Selection{Plan=plan,HomeParent=i==0?plan.First.ResidentId:plan.Second.ResidentId,
                            AwayParent=i==0?plan.Second.ResidentId:plan.First.ResidentId,Child=child.Id,Visit=b.BuildingKey,At=at};
                    }
            }
            return null;
        }
        private static void VerifyStates(WorldStateStore store,StreetPopulation streets,Selection selected)
        {
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var day=store.ReadHouseholdDay(selected.Plan.SettlementId,selected.Plan.HomeKey,selected.Plan.AbsoluteDay);
            var parent=streets.GetState(selected.HomeParent,now);var child=streets.GetState(selected.Child,now);var away=streets.GetState(selected.AwayParent,now);
            if(day==null || day.JointInn!=null || day.Status!="planned" ||
                parent.Activity!=Activity.Home || parent.BuildingKey!=selected.Plan.HomeKey || child.Activity!=Activity.Home || child.BuildingKey!=selected.Plan.HomeKey ||
                away.Activity!=Activity.Visit || away.BuildingKey!=selected.Visit || !streets.HouseholdParentAvailable(selected.HomeParent,now))
                throw new InvalidOperationException("The actual childcare plan/normal GetState changed outside the selected native observation interval.");
        }
        private static bool ActualAwakeBody(ResidentActor actor,DaggerfallInterior room)
        {return actor && actor.Resident!=null && actor.Resident.IsAlive && actor.gameObject.activeInHierarchy && actor.transform.parent==room.transform && !actor.IsSleeping;}
        private static void FindDoor(BuildingDirectory directory,int building,out Transform owner,out StaticDoor result)
        {
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())foreach(var door in doors.Doors)
                if(door.doorType==DoorTypes.Building && door.buildingKey==building){owner=doors.transform;result=door;return;}
            throw new InvalidOperationException("Actual childcare visit has no native building door.");
        }
    }
}
