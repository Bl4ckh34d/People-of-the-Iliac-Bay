using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Repair only missing outings. Never replace an occupied or native-owned role.
        private IEnumerator RepairMissingAdultOuting(Plan plan,PreparedOutdoorRoutine result,
            Func<bool> valid,int wake,int sleep)
        {
            var resident=plan.Resident;
            if(!resident.IsAlive || resident.AgeGroup!=ResidentAgeGroup.Adult || resident.NativeIdentity!=null ||
                QuestOwned(resident) || !geometry.Entrances.ContainsKey(resident.HomeKey)) yield break;
            var saved=worldStore.GetRoutines(population.SettlementId);
            if(saved==null || !saved.Residents.Exists(r=>r.ResidentId==resident.Id)) yield break;
            var buildings=new List<BuildingSummary>();
            foreach(var pair in geometry.Entrances)
            {
                BuildingSummary summary;
                if(directory.GetBuildingSummary(pair.Key,out summary) && IsVisitDestination(summary.BuildingType)) buildings.Add(summary);
            }
            var options=SelectDailyItineraryOptions(buildings,resident,resident.HomeKey);
            foreach(var option in options)
            {
                var routeResult=new TownGeometry.RouteResult();
                var work=geometry.ReadRouteSteps(geometry.Entrances[resident.HomeKey],geometry.Entrances[option.BuildingKey],routeResult,valid);
                try
                {
                    while(valid())
                    {
                        while(GameManager.Instance.PlayerEnterExit.IsPlayerInside || GameManager.IsGamePaused) yield return null;
                        if(!valid() || !work.MoveNext()) break;
                        yield return null;
                    }
                }
                finally {(work as IDisposable)?.Dispose();}
                if(!valid()) yield break;
                if(routeResult.Points==null || routeResult.Points.Count<2) continue;
                var route=SurveyDailyRoute(resident.HomeKey,option.BuildingKey,routeResult.Points);
                if(route.WalkSeconds<=0) continue;
                var reverse=new List<SavedRoutePoint>(route.Points);reverse.Reverse();
                double walk=route.WalkSeconds*ResidentMovement.WalkSpeed/ResidentMovement.WalkSpeedFor(resident.AppearanceSeed);
                var request=new AdultDailyItineraryRequest {HomeBuildingKey=resident.HomeKey,
                    DepartureAt=DailyDepartureSeconds(resident,wake,sleep),LatestHomeAt=DailyReturnDeadline(sleep),
                    MinimumAwaySeconds=20*60,MinimumStops=1,MaximumStops=1,Seed=resident.AppearanceSeed,
                    Options=new List<AdultDailyItineraryOption>{option},Routes=new List<AdultDailyItineraryLeg>{
                        new AdultDailyItineraryLeg {FromBuildingKey=resident.HomeKey,ToBuildingKey=option.BuildingKey,WalkSeconds=walk,Route=route.Points},
                        new AdultDailyItineraryLeg {FromBuildingKey=option.BuildingKey,ToBuildingKey=resident.HomeKey,WalkSeconds=walk,Route=reverse}}};
                ResidentDestination itinerary;bool planned;
                try {planned=AdultDailyItineraryPlanner.TryCreate(request,out itinerary);}
                catch(ArgumentException) {continue;}
                if(!planned) continue;
                var repaired=new ResidentRoutine {ResidentId=resident.Id,Itinerary=itinerary,ItineraryDepartureSeconds=request.DepartureAt};
                KeepFirstStopAsLegacyRoute(repaired,itinerary);
                result.Routine=repaired;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MISSING_OUTING_REPAIRED: resident="+resident.Id+"; destination="+option.BuildingKey+"; pendingHomePublication=True");
                yield break;
            }
            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_MISSING_OUTING_UNREACHABLE: resident="+resident.Id);
        }
    }
}
