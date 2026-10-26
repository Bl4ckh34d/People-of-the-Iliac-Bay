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
        private sealed class JointCandidate {internal int Day;internal bool Ready;internal float RetryAt;internal HouseholdJointInn Inn;}
        private readonly Dictionary<int,JointCandidate> householdJointCandidates=new Dictionary<int,JointCandidate>();
        private bool householdJointSurvey;
        private bool TryFamilyJointCandidate(Household home,Plan first,Plan second,int day,double now,bool restricted,out HouseholdJointInn candidate)
        {
            candidate=null;
            if(!HouseholdDayPlanner.SharedDay(worldStore.WorldSeed,population.SettlementId,home,day) || now-day*86400d>=16*3600)return true;
            JointCandidate saved;
            if(householdJointCandidates.TryGetValue(home.BuildingKey,out saved) && saved.Day==day)
            {
                if(saved.Ready){candidate=saved.Inn;return true;}
                if(saved.RetryAt==0 || Time.unscaledTime<saved.RetryAt)return false;
            }
            if(householdJointSurvey)return false;
            saved=new JointCandidate{Day=day};householdJointCandidates[home.BuildingKey]=saved;
            StartCoroutine(PrepareFamilyJointCandidate(home,first,second,saved,restricted));return false;
        }
        private IEnumerator PrepareFamilyJointCandidate(Household home,Plan first,Plan second,JointCandidate result,bool restricted)
        {
            householdJointSurvey=true;var sourceGeometry=geometry;var sourceStore=worldStore;var sourcePopulation=population;
            object sourceEpoch=sourceStore.CaptureNpcVitalsWriteEpoch();
            bool probe=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-verify-family-cash")>=0;
            int catalogCount=0,routeCount=0,laneCount=0,timeCount=0;
            var firstRoutine=first.Routine;var secondRoutine=second.Routine;
            Func<bool> valid=()=>this && Ready && geometry==sourceGeometry && worldStore==sourceStore && population==sourcePopulation &&
                first.Routine==firstRoutine && second.Routine==secondRoutine && sourceStore.IsNpcVitalsWriteEpochCurrent(sourceEpoch) &&
                (int)Math.Floor(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()/86400d)==result.Day;
            try
            {
                TownGeometry.Entrance origin;if(!geometry.Entrances.TryGetValue(home.BuildingKey,out origin))yield break;
                var inns=new List<int>();
                foreach(int key in geometry.Entrances.Keys)
                {BuildingSummary summary;if(directory.GetBuildingSummary(key,out summary) && summary.BuildingType==DFLocation.BuildingTypes.Tavern)inns.Add(key);}
                bool catalogReady=inns.Count==0;
                foreach(int key in inns)if(worldStore.ReadNpcInnServiceCatalog(population.SettlementId,key)!=null){catalogReady=true;break;}
                if(!catalogReady){result.RetryAt=Time.unscaledTime+5;yield break;}
                uint seed=StableSeed.Combine(worldStore.WorldSeed,(uint)home.BuildingKey,(uint)result.Day,0x70616972u);
                inns.Sort((a,b)=>StableSeed.Combine(seed,(uint)a,0,0).CompareTo(StableSeed.Combine(seed,(uint)b,0,0)));
                foreach(int inn in inns)
                {
                    if(!valid())yield break;
                    // Payment and pair routes must already have a real native source.
                    if(worldStore.ReadNpcInnServiceCatalog(population.SettlementId,inn)==null)continue;
                    catalogCount++;
                    var path=GetDailyRoute(home.BuildingKey,inn);
                    if(path==null)
                    {
                        var found=new TownGeometry.RouteResult();var work=geometry.ReadRouteSteps(origin,geometry.Entrances[inn],found,valid);
                        try{while(valid() && work.MoveNext())yield return null;}finally{(work as IDisposable)?.Dispose();}
                        if(!valid())yield break;path=SurveyDailyRoute(home.BuildingKey,inn,found.Points);
                    }
                    if(path.Points.Count<2)continue;
                    routeCount++;
                    var lanes=new TownGeometry.HouseholdLaneResult();var prepare=geometry.PrepareHouseholdLanes(path.Points,lanes,valid);
                    try{while(valid() && prepare.MoveNext())yield return null;}finally{(prepare as IDisposable)?.Dispose();}
                    if(!valid())yield break;if(lanes.First==null || lanes.Second==null)continue;
                    laneCount++;
                    DailyServiceWindow hours;
                    if(!DailyServiceHours.TryFromNativeHours(PlayerActivate.openHours[(int)DFLocation.BuildingTypes.Tavern],
                        PlayerActivate.closeHours[(int)DFLocation.BuildingTypes.Tavern],out hours))continue;
                    double departure=16.5*3600+(seed%91)*60,dwell=(45+(seed/97)%46)*60;
                    var a=FamilyJointOuting(first,inn,lanes.First,departure,dwell,hours);
                    var b=FamilyJointOuting(second,inn,lanes.Second,departure,dwell,hours);
                    double deadline=Math.Min(sleepHour*3600d,restricted?19*3600d:hours.CloseMinute*60d);
                    if(probe)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_TIME: home="+home.BuildingKey+"; inn="+inn+
                        "; departure="+departure+"; dwell="+dwell+"; firstWalk="+a.Itinerary.Stops[0].WalkSeconds+
                        "; secondWalk="+b.Itinerary.Stops[0].WalkSeconds+"; deadline="+deadline);
                    if(ResidentVisitJourney.Create(departure,a.Itinerary).EndsAt>=deadline ||
                        ResidentVisitJourney.Create(departure,b.Itinerary).EndsAt>=deadline)continue;
                    timeCount++;
                    var outing=new HouseholdJointInn{InnKey=inn,First=a,Second=b};
                    if(!PrepareSharedInnBudget(outing,first,second,result.Day))continue;
                    result.Inn=outing;yield break;
                }
            }
            finally
            {
                if(probe)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_CANDIDATE: home="+home.BuildingKey+"; valid="+valid()+
                        "; inn="+result.Inn?.InnKey+"; retry="+result.RetryAt+"; restricted="+restricted+
                        "; catalogs="+catalogCount+"; routes="+routeCount+"; lanes="+laneCount+"; fitsTime="+timeCount);
                result.Ready=result.RetryAt==0;householdJointSurvey=false;
            }
        }
        private ResidentDailyOuting FamilyJointOuting(Plan parent,int inn,List<SavedRoutePoint> route,double departure,double dwell,DailyServiceWindow hours)
        {
            var reverse=new List<SavedRoutePoint>(route);reverse.Reverse();
            double walk=RouteLength(route)*DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(parent.Resident.AppearanceSeed);
            return new ResidentDailyOuting{DepartureSeconds=departure,Itinerary=new ResidentDestination{
                Stops=new List<ResidentVisitStop>{new ResidentVisitStop{BuildingKey=inn,Purpose=ResidentVisitPurpose.Social,
                    OpenMinute=hours.OpenMinute,CloseMinute=hours.CloseMinute,WalkSeconds=walk,DwellSeconds=dwell,Route=route}},
                ExitRoute=reverse,ExitWalkSeconds=walk}};
        }
    }
}
