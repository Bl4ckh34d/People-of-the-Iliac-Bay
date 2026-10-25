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
        private bool FamilyAllowsOptionalOuting(Resident resident,double starts,double ends)
        {
            var household=population.Households.Find(h=>h.BuildingKey==resident.HomeKey &&
                h.Residents.Exists(p=>p.Id==resident.Id));
            if(household==null)return false;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            double day=Math.Floor(now/86400)*86400;
            // Completed slots do not run retroactively. The recurring next day
            // still needs admission when today's occurrence has already passed.
            if(day+ends<=now)day+=86400;
            double from=day+starts,until=day+ends;
            Func<Resident,DailySchedule> actual=other=>
            {
                var plan=plans.Find(p=>p.Resident.Id==other.Id);
                if(plan==null || IsAlarmed(other.Id) || shopDepartures.ContainsKey(other.Id) ||
                    worldStore.IsResidentAway(other.Id,now))return null;
                var observed=worldStore.GetObservedVisit(other.Id);
                if(observed!=null && observed.AffectsRoutineDuring(from,until))return null;
                var trip=worldStore.GetResidentDialogueTrip(other.Id);
                if(trip!=null && trip.HomeAt>now)return null;
                // Until per-date optional cancellation exists, do not publish a
                // repeating obligation across an unresolved fixed appointment.
                if(worldStore.AppointmentsFor(other.Id).Exists(a=>a.Status!="cancelled" && a.EndsAt>now))return null;
                return ActiveSchedule(plan);
            };
            if(!HouseholdChildcare.CanTakeOptionalOuting(household,resident.Id,from,until,actual))return false;
            // Initial generation can have a newer persisted routine than the
            // currently displayed Plan. Check both, without activating that plan
            // early or changing a resident's physical position.
            return HouseholdChildcare.CanTakeOptionalOuting(household,resident.Id,from,until,other=>
            {
                if(actual(other)==null)return null;
                var routine=worldStore.GetResidentDialogueRoutine(population.SettlementId,other.Id);
                if(routine==null)return null;
                DailySchedule ordinary,haunted;
                BuildOutdoorSchedules(other,routine,wakeHour,sleepHour,out ordinary,out haunted);
                bool restricted=population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(population.SettlementId,
                    GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now);
                return restricted?haunted:ordinary;
            });
        }

        private bool FamilyAllowsAddedOutings(Plan plan,ResidentRoutine candidate)
        {
            var old=ResidentRoutineDay.CopyOutings(plan.Routine);var added=ResidentRoutineDay.CopyOutings(candidate);
            for(int i=old.Count;i<added.Count;i++)
                if(!FamilyAllowsOptionalOuting(plan.Resident,added[i].DepartureSeconds,
                    ResidentVisitJourney.Create(added[i].DepartureSeconds,added[i].Itinerary).EndsAt))return false;
            return true;
        }

        // Source legs are already personal. Survey a bounded chain graph for
        // additions and prepare lanes only for those additions, never twice for
        // the old executable routes. Publication remains a separate home gate.
        private IEnumerator ExpandAdultDailyOutings(Plan plan,ResidentRoutine source,PreparedOutdoorRoutine result,
            Func<bool> valid,int wake,int sleep)
        {
            var resident=plan.Resident;var old=ResidentRoutineDay.CopyOutings(source);
            if(!resident.IsAlive || resident.AgeGroup!=ResidentAgeGroup.Adult || resident.NativeIdentity!=null ||
                QuestOwned(resident) || (old.Count>0 || source.Route.Count>=2) &&
                source.PersonalLaneVersion!=OutdoorRoutineMigration.CurrentPersonalLaneVersion ||
                old.Count>=ResidentRoutineDay.MaximumOutings || old.Count==0 && source.Route.Count>=2 ||
                !geometry.Entrances.ContainsKey(resident.HomeKey))yield break;
            var slots=DailyActivityWindows.Create(wake*60,sleep*60,DailyReturnDeadline(sleep),resident.AppearanceSeed);
            double previousEnd=old.Count==0?0:ResidentVisitJourney.Create(old[old.Count-1].DepartureSeconds,old[old.Count-1].Itinerary).EndsAt;
            foreach(var slot in slots)slot.DepartureAt=Math.Max(slot.DepartureAt,previousEnd+20*60);
            slots.RemoveAll(s=>s.LatestHomeAt-s.DepartureAt<45*60);
            int capacity=ResidentRoutineDay.MaximumOutings-old.Count;
            if(slots.Count>capacity)
            {
                var evening=slots[slots.Count-1];
                bool retainEvening=evening.DepartureAt>=16*3600 &&
                    !old.Exists(o=>o.Itinerary.Stops.Exists(s=>s.Purpose==ResidentVisitPurpose.Social));
                slots.RemoveRange(capacity,slots.Count-capacity);
                if(retainEvening)slots[slots.Count-1]=evening;
            }
            if(slots.Count==0)yield break;
            var buildings=new List<BuildingSummary>();
            foreach(var key in geometry.Entrances.Keys)
            {BuildingSummary summary;if(directory.GetBuildingSummary(key,out summary) && IsVisitDestination(summary.BuildingType))buildings.Add(summary);}
            var options=SelectDailyItineraryOptions(buildings,resident,resident.HomeKey,24);
            var used=new HashSet<int>();foreach(var outing in old)foreach(var stop in outing.Itinerary.Stops)used.Add(stop.BuildingKey);
            options.RemoveAll(o=>used.Contains(o.BuildingKey));
            // Reserve evening social space before filling daytime errands.
            options.Sort((a,b)=>a.Purpose==ResidentVisitPurpose.Social && b.Purpose!=ResidentVisitPurpose.Social?-1:
                b.Purpose==ResidentVisitPurpose.Social && a.Purpose!=ResidentVisitPurpose.Social?1:
                DailyCandidateRank(resident.AppearanceSeed,resident.HomeKey,a.BuildingKey,a.Purpose).CompareTo(
                    DailyCandidateRank(resident.AppearanceSeed,resident.HomeKey,b.BuildingKey,b.Purpose)));
            foreach(var option in options)
            {
                AdultDailyOutingSlot chosen=null;
                foreach(var slot in slots)
                {
                    if(slot.Options.Count>=4 || option.OpenMinute*60d>=slot.LatestHomeAt ||
                        option.CloseMinute*60d<=slot.DepartureAt ||
                        option.Purpose==ResidentVisitPurpose.Social && slot.DepartureAt<16*3600)continue;
                    if(chosen==null || slot.Options.Count<chosen.Options.Count)chosen=slot;
                }
                if(chosen!=null)chosen.Options.Add(option);
            }
            var keys=new List<DailyRouteKey>();var unique=new HashSet<DailyRouteKey>();
            foreach(var slot in slots)
            {
                int previous=resident.HomeKey;
                foreach(var option in slot.Options)
                {
                    var outward=new DailyRouteKey{From=resident.HomeKey,To=option.BuildingKey};
                    if(unique.Add(outward))keys.Add(outward);
                    if(previous!=resident.HomeKey)
                    {var link=new DailyRouteKey{From=previous,To=option.BuildingKey};if(unique.Add(link))keys.Add(link);}
                    previous=option.BuildingKey;
                }
            }
            var legs=new List<AdultDailyItineraryLeg>();
            foreach(var key in keys)
            {
                if(!valid())yield break;
                var route=GetDailyRoute(key.From,key.To);
                if(route==null)
                {
                    var survey=new TownGeometry.RouteResult();
                    var work=geometry.ReadRouteSteps(geometry.Entrances[key.From],geometry.Entrances[key.To],survey,valid);
                    try
                    {
                        while(valid())
                        {
                            while(GameManager.Instance.PlayerEnterExit.IsPlayerInside || GameManager.IsGamePaused)yield return null;
                            if(!valid() || !work.MoveNext())break;
                            yield return null;
                        }
                    }
                    finally{(work as IDisposable)?.Dispose();}
                    if(!valid())yield break;
                    route=SurveyDailyRoute(key.From,key.To,survey.Points);
                }
                if(route.Points.Count<2 || route.WalkSeconds<=0)continue;
                double walk=route.WalkSeconds*ResidentMovement.WalkSpeed/ResidentMovement.WalkSpeedFor(resident.AppearanceSeed);
                legs.Add(new AdultDailyItineraryLeg{FromBuildingKey=key.From,ToBuildingKey=key.To,WalkSeconds=walk,Route=route.Points});
                if(key.From==resident.HomeKey)
                {var reverse=new List<SavedRoutePoint>(route.Points);reverse.Reverse();
                    legs.Add(new AdultDailyItineraryLeg{FromBuildingKey=key.To,ToBuildingKey=key.From,WalkSeconds=walk,Route=reverse});}
            }
            ResidentRoutine expanded;bool planned;
            try{planned=AdultDailyRoutinePlanner.TryCreate(new AdultDailyRoutineRequest{HomeBuildingKey=resident.HomeKey,
                Seed=resident.AppearanceSeed,Source=source,Slots=slots,Routes=legs,
                IsOutingAllowed=(start,end)=>FamilyAllowsOptionalOuting(resident,start,end)},out expanded);}
            catch(ArgumentException error){Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DAILY_EXPANSION_DEFERRED: "+resident.Id+"; "+error.Message);yield break;}
            if(!planned)yield break;
            var all=ResidentRoutineDay.CopyOutings(expanded);var personal=new List<List<SavedRoutePoint>>();
            for(int i=0;i<all.Count;i++)
            {
                var itinerary=all[i].Itinerary;
                if(i>=old.Count)
                {
                    var addition=new ResidentRoutine{ResidentId=resident.Id,Itinerary=itinerary,ItineraryDepartureSeconds=all[i].DepartureSeconds};
                    var prepare=PrepareRoutineLanes(addition,resident.AppearanceSeed,valid);
                    try{while(prepare.MoveNext()){yield return null;if(!valid())yield break;}}
                    finally{(prepare as IDisposable)?.Dispose();}
                }
                foreach(var stop in itinerary.Stops)personal.Add(i<old.Count?stop.Route:OffsetSavedRoute(stop.Route,resident.AppearanceSeed));
                personal.Add(i<old.Count?itinerary.ExitRoute:OffsetSavedRoute(itinerary.ExitRoute,resident.AppearanceSeed));
            }
            try{expanded=OutdoorRoutineMigration.Rebuild(expanded,personal,ResidentMovement.WalkSpeedFor(resident.AppearanceSeed),
                DaggerfallUnity.Instance.WorldTime.TimeScale,true,old.Count);}
            catch(ArgumentException error){Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DAILY_EXPANSION_DEFERRED: "+resident.Id+"; lanes: "+error.Message);yield break;}
            if(!FamilyAllowsAddedOutings(plan,expanded))yield break;
            result.Routine=expanded;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DAILY_EXPANSION_PREPARED: resident="+resident.Id+"; before="+old.Count+
                "; after="+all.Count+"; surveyedLegs="+keys.Count+"; graphLegs="+legs.Count+"; publication=pending-home");
        }
    }
}
