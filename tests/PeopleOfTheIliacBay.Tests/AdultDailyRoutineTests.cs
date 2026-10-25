using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class AdultDailyRoutineTests
    {
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Personal expansion preserves old route timing while measuring only the added outings",(Action)(()=>{
                var initial=Request();initial.Slots.RemoveRange(1,4);
                Check.True(AdultDailyRoutinePlanner.TryCreate(initial,out var first));
                first.PersonalLaneVersion=OutdoorRoutineMigration.CurrentPersonalLaneVersion;first.RouteGeometryVersion=OutdoorRoutineMigration.CurrentVersion;
                first.Itinerary.Stops[0].WalkSeconds=333.25;first.Itinerary.ExitWalkSeconds=444.5;
                var request=Request();request.Source=first;request.Slots.RemoveRange(2,3);
                Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var planned));
                var routes=new List<List<SavedRoutePoint>>();
                foreach(var outing in ResidentRoutineDay.CopyOutings(planned))
                {foreach(var stop in outing.Itinerary.Stops)routes.Add(stop.Route);routes.Add(outing.Itinerary.ExitRoute);}
                var rebuilt=OutdoorRoutineMigration.Rebuild(planned,routes,1,1,true,preserveOutings:1);
                Check.Near(333.25,rebuilt.Itinerary.Stops[0].WalkSeconds);Check.Near(444.5,rebuilt.Itinerary.ExitWalkSeconds);
                Check.Near(40,rebuilt.AdditionalOutings[0].Itinerary.Stops[0].WalkSeconds);
                var changed=new List<List<SavedRoutePoint>>(routes);changed[0]=new List<SavedRoutePoint>{
                    new SavedRoutePoint(),new SavedRoutePoint{X=10,Z=1},new SavedRoutePoint{X=20}};
                Check.Throws<ArgumentException>(()=>OutdoorRoutineMigration.Rebuild(planned,changed,1,1,true,preserveOutings:1));
            })));
            cases.Add(("Expansion never replaces an unconverted legacy single-destination routine",(Action)(()=>{
                var request=Request();request.Source.DestinationKey=77;
                request.Source.Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=10}};
                Check.True(!AdultDailyRoutinePlanner.TryCreate(request,out var planned));Check.True(planned==null);
                Check.Equal(77,request.Source.DestinationKey);Check.Equal(2,request.Source.Route.Count);
            })));
            cases.Add(("Native day windows cover daytime and evening with deterministic home breaks and curfew",(Action)(()=>{
                var slots=DailyActivityWindows.Create(360,1380,18*3600+45*60,73);
                var again=DailyActivityWindows.Create(360,1380,18*3600+45*60,73);
                Check.Equal(5,slots.Count);double previous=0;
                for(int i=0;i<slots.Count;i++)
                {
                    Check.Equal(slots[i].DepartureAt,again[i].DepartureAt);
                    Check.True(slots[i].DepartureAt>=previous && slots[i].LatestHomeAt<=18*3600+45*60);
                    if(i>0)Check.True(slots[i].DepartureAt-previous>=20*60);
                    previous=slots[i].LatestHomeAt;
                }
                Check.True(slots[0].DepartureAt>=8*3600 && slots[4].DepartureAt>=16*3600);
            })));
            cases.Add(("Childcare admission can shorten a planned outing without losing the shared search budget",(Action)(()=>{
                var request=Request();request.Slots[0].Options.Add(request.Slots[1].Options[0]);request.Slots.RemoveRange(1,4);
                request.Routes.Add(new AdultDailyItineraryLeg{FromBuildingKey=100,ToBuildingKey=101,WalkSeconds=300,
                    Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=20},new SavedRoutePoint{X=40}}});
                request.IsOutingAllowed=(starts,ends)=>ends<=request.Slots[0].DepartureAt+1800;
                Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var planned));
                Check.True(ResidentVisitJourney.Create(planned.ItineraryDepartureSeconds,planned.Itinerary).EndsAt<=
                    request.Slots[0].DepartureAt+1800);
                Check.Equal(1,planned.Itinerary.Stops.Count);
                request.IsOutingAllowed=(starts,ends)=>false;
                Check.True(!AdultDailyRoutinePlanner.TryCreate(request,out _));
                Check.True(request.Source.Itinerary==null);
            })));
            cases.Add(("Five bounded outings cover the day with home gaps and detached deterministic plans",()=>{
                var request=Request();
                Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var planned));
                Check.Equal(4,planned.AdditionalOutings.Count);
                Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var again));
                Check.Equal(RoutineRecoveryIdentity.Fingerprint(planned,20,360,1380),RoutineRecoveryIdentity.Fingerprint(again,20,360,1380));
                var schedule=ResidentRoutineDay.Schedule(20,planned,360,1380);
                Check.Equal(5,schedule.ScheduledVisits().Count);
                Check.Equal(Activity.Home,schedule.EvaluateSeconds(12*3600).Activity);
                Check.Equal(104,schedule.EvaluateSeconds(19*3600+360).BuildingKey);
                Check.Equal(0,request.Source.AdditionalOutings.Count);Check.True(request.Source.Itinerary==null);
                planned.AdditionalOutings[0].Itinerary.Stops[0].Route[0].X=999;
                Check.Equal(0d,request.Routes[2].Route[0].X);
            }));
            cases.Add(("Local social outings cannot turn into morning drinking",()=>{
                var request=Request();request.Slots.RemoveRange(1,4);request.Slots[0].Options[0].Purpose=ResidentVisitPurpose.Social;
                Check.True(!AdultDailyRoutinePlanner.TryCreate(request,out var planned));Check.True(planned==null);
            }));
            cases.Add(("Home-only residents can start their first outing at an evening Inn",()=>{
                var request=Request();
                foreach(var slot in request.Slots)slot.Options[0].Purpose=ResidentVisitPurpose.Social;
                Check.True(request.Source.Itinerary==null && request.Source.Route.Count==0);
                Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var planned));
                var outings=ResidentRoutineDay.CopyOutings(planned);
                Check.Equal(1,outings.Count);Check.Equal(16*3600d,outings[0].DepartureSeconds);
                var visits=ResidentRoutineDay.ScheduledVisits(planned);
                Check.Equal(1,visits.Count);Check.Equal(ResidentVisitPurpose.Social,visits[0].Purpose);
                Check.True(visits[0].EnterAt>=16*3600);
                Check.True(request.Source.Itinerary==null && request.Source.Route.Count==0);
            }));
            cases.Add(("Morning first outings retain later errands and evening Inn activity across resident seeds",()=>{
                var initial=Request();initial.Slots.RemoveRange(1,4);
                Check.True(AdultDailyRoutinePlanner.TryCreate(initial,out var first));
                for(uint seed=1;seed<=64;seed++)
                {
                    var source=DailyOutingDistribution.Spread(first,seed,6,22);
                    var request=Request();request.Source=source;
                    Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var planned));
                    var outings=ResidentRoutineDay.CopyOutings(planned);
                    Check.Equal(5,outings.Count);
                    Check.True(outings[0].DepartureSeconds>=8*3600 && outings[0].DepartureSeconds<=10*3600);
                    var visits=ResidentRoutineDay.ScheduledVisits(planned);
                    Check.True(visits.Exists(v=>v.BuildingKey==102 && v.EnterAt>=13*3600));
                    Check.True(visits.Exists(v=>v.Purpose==ResidentVisitPurpose.Social && v.EnterAt>=19*3600));
                }
            }));
            cases.Add(("A day never repeats a venue or multiple discretionary temple visits",()=>{
                var request=Request();request.Slots[0].Options[0].Purpose=ResidentVisitPurpose.Temple;
                request.Slots[1].Options[0].Purpose=ResidentVisitPurpose.Temple;
                request.Slots[2].Options[0].BuildingKey=100;
                Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var planned));
                var visits=ResidentRoutineDay.ScheduledVisits(planned);
                Check.Equal(3,visits.Count);Check.Equal(1,visits.FindAll(v=>v.Purpose==ResidentVisitPurpose.Temple).Count);
                Check.Equal(1,visits.FindAll(v=>v.TargetBuildingKey==100).Count);
            }));
            cases.Add(("All outing searches consume one shared day budget",()=>{
                var request=Request();request.SearchBudget=4;
                Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var planned));
                Check.Equal(2,ResidentRoutineDay.CopyOutings(planned).Count);
            }));
            cases.Add(("Existing outing remains unchanged while nonconflicting later activities are added",()=>{
                var initial=Request();initial.Slots.RemoveRange(1,4);
                Check.True(AdultDailyRoutinePlanner.TryCreate(initial,out var first));
                var request=Request();request.Source=first;
                string before=RoutineRecoveryIdentity.Fingerprint(first,20,360,1380);
                Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var planned));
                Check.Equal(4,planned.AdditionalOutings.Count);Check.Equal(first.ItineraryDepartureSeconds,planned.ItineraryDepartureSeconds);
                Check.Equal(before,RoutineRecoveryIdentity.Fingerprint(first,20,360,1380));
            }));
            cases.Add(("Closed or unreachable later venues create no outing or invented route",()=>{
                var request=Request();request.Slots[0].Options[0].CloseMinute=8*60;
                request.Routes.RemoveAll(leg=>leg.FromBuildingKey==101 || leg.ToBuildingKey==101);
                request.Slots[4].LatestHomeAt=request.Slots[4].DepartureAt+30;
                Check.True(AdultDailyRoutinePlanner.TryCreate(request,out var planned));
                var visits=ResidentRoutineDay.ScheduledVisits(planned);Check.Equal(2,visits.Count);
                Check.True(visits.TrueForAll(v=>v.TargetBuildingKey==102 || v.TargetBuildingKey==103));
            }));
        }
        private static AdultDailyRoutineRequest Request()
        {
            var request=new AdultDailyRoutineRequest{HomeBuildingKey=20,Source=new ResidentRoutine{ResidentId="daily-resident"},Seed=73};
            int[] hours={9,11,13,16,19};
            for(int i=0;i<hours.Length;i++)
            {
                int building=100+i;double x=20+i*20;
                request.Slots.Add(new AdultDailyOutingSlot{DepartureAt=hours[i]*3600,LatestHomeAt=hours[i]*3600+3000,
                    Options=new List<AdultDailyItineraryOption>{new AdultDailyItineraryOption{BuildingKey=building,
                        Purpose=i==4?ResidentVisitPurpose.Social:ResidentVisitPurpose.Shop,OpenMinute=0,CloseMinute=1439,
                        MinimumDwellSeconds=600,MaximumDwellSeconds=1200}}});
                request.Routes.Add(new AdultDailyItineraryLeg{FromBuildingKey=20,ToBuildingKey=building,WalkSeconds=300,
                    Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=x}}});
                request.Routes.Add(new AdultDailyItineraryLeg{FromBuildingKey=building,ToBuildingKey=20,WalkSeconds=300,
                    Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=x},new SavedRoutePoint()}});
            }
            return request;
        }
    }
}
