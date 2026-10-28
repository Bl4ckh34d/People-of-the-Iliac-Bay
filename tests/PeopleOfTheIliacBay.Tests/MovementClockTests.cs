using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class MovementClockTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Loaded current daily routes preserve their saved timing",()=>{
                foreach(double encodedRate in new[]{12d,2d})
                {
                    var source=new WorldStateStore(42);
                    var home=source.GetOrCreate(1,new[]{10}).Households[0];
                    string id=home.Residents[0].Id;
                    source.SetRoutines(new TownRoutineState {SettlementId=1,ItineraryVersion=1,
                        Residents=new List<ResidentRoutine> {new ResidentRoutine {ResidentId=id,
                            ItineraryDepartureSeconds=8*3600,Itinerary=Destination(encodedRate)}}});
                    var saved=source.Snapshot();
                    var loaded=new WorldStateStore(0); loaded.Restore(saved,7*3600);
                    var routine=loaded.GetRoutines(1).Residents[0];
                    var journey=ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds,routine.Itinerary);
                    double start=routine.ItineraryDepartureSeconds;
                    Check.Near(130d*20/(100*encodedRate),journey.Evaluate(start+20).Position.X-journey.Evaluate(start).Position.X);
                    Check.Near(100*encodedRate,routine.Itinerary.Stops[0].WalkSeconds);
                    Check.Near(100*encodedRate,routine.Itinerary.ExitWalkSeconds);
                    Check.Near(600,routine.Itinerary.Stops[0].DwellSeconds);
                    Check.Near(100*encodedRate,saved.Routines[0].Residents[0].Itinerary.Stops[0].WalkSeconds);
                    loaded.Restore(loaded.Snapshot(),7*3600);
                    Check.Near(100*encodedRate,loaded.GetRoutines(1).Residents[0].Itinerary.Stops[0].WalkSeconds);
                }
            }));
            cases.Add(("Short fallback visits walk at the same physical speed at 12x and 2x",()=>{
                foreach(double rate in new[]{12d,2d})
                {
                    var schedule=DailySchedule.DayVisit(10,20,new[]{new RoutePoint(0,0),new RoutePoint(13,0)},
                        600,1200,30,ResidentMovement.WalkSpeed,rate,0,360,1320);
                    Check.True(schedule.TryVisitWindowForBuilding(8*3600,20,out double enter,out double leave));
                    double start=enter-10*rate;
                    var a=schedule.EvaluateSeconds(start+rate);
                    var b=schedule.EvaluateSeconds(start+4*rate);
                    Check.Equal(Activity.Travel,a.Activity); Check.Equal(Activity.Travel,b.Activity);
                    Check.Near(3.9,b.Position.X-a.Position.X);
                }
            }));
        }
        private static ResidentDestination Destination(double rate)
        {return new ResidentDestination {Stops=new List<ResidentVisitStop> {new ResidentVisitStop {
            BuildingKey=20,Purpose=ResidentVisitPurpose.Shop,WalkSeconds=100*rate,DwellSeconds=600,
            Route=new List<SavedRoutePoint> {new SavedRoutePoint(),new SavedRoutePoint {X=130}}}},
            ExitWalkSeconds=100*rate,ExitRoute=new List<SavedRoutePoint> {new SavedRoutePoint {X=130},new SavedRoutePoint()}};}
    }
}
