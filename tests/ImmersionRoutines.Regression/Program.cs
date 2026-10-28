using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

static class Program
{
    static List<SavedRoutePoint> Path(double from,double to) => new List<SavedRoutePoint>{new SavedRoutePoint{X=from},new SavedRoutePoint{X=to}};
    static Resident Person() => new Resident{Id="daily",HomeKey=10,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
    static ResidentRoutine Routine() => new ResidentRoutine{ResidentId="daily",ItineraryDepartureSeconds=9*3600,
        Itinerary=new ResidentDestination{Stops=new List<ResidentVisitStop>{new ResidentVisitStop{BuildingKey=20,
            WalkSeconds=100,DwellSeconds=3600,Route=Path(0,100)}},ExitRoute=Path(100,0),ExitWalkSeconds=100}};
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static RoutineRecoveryPlan Recover(double now,int origin,IList<AppointmentRecord> bookings=null)
    {
        var routine=Routine();var schedule=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
        Require(RoutineRecoveryPlanner.TryCreate(100,Person(),routine,schedule,bookings,0,now,origin,now,360,1320,1,
            out var result,out var reason,true),reason);return result;
    }
    static int Main()
    {
        var tests=new List<(string,Action)>{
            ("Useful short destination ignores obsolete stop and absence quotas",()=>{
                var request=new AdultDailyItineraryRequest{HomeBuildingKey=10,DepartureAt=9*3600,LatestHomeAt=12*3600,
                    Seed=7,
                    Options=new List<AdultDailyItineraryOption>{new AdultDailyItineraryOption{BuildingKey=20,Purpose=ResidentVisitPurpose.Shop,
                        OpenMinute=0,CloseMinute=1439,MinimumDwellSeconds=600,MaximumDwellSeconds=600}},
                    Routes=new List<AdultDailyItineraryLeg>{new AdultDailyItineraryLeg{FromBuildingKey=10,ToBuildingKey=20,WalkSeconds=100,Route=Path(0,100)},
                        new AdultDailyItineraryLeg{FromBuildingKey=20,ToBuildingKey=10,WalkSeconds=100,Route=Path(100,0)}}};
                Require(AdultDailyItineraryPlanner.TryCreate(request,out var result),"One useful reachable destination was rejected by daily quotas.");
                Require(result.Stops.Count==1,"Planner filled unnecessary stops.");
                Require(ResidentVisitJourney.Create(request.DepartureAt,result).EndsAt==request.DepartureAt+800,"Measured route/dwell changed.");
            }),
            ("Interrupted routine skips optional errand and heads home",()=>{
                var plan=Recover(8*3600,20);Require(plan.Legs.Count==1 && plan.Legs[0].ReturnsHome,"Recovery replayed optional errand.");
                Require(plan.SkippedStopIndices.Contains(0),"Skipped errand missing.");
                Require(plan.Legs[0].Route[0].X==100 && plan.EndsAt==8*3600+100,"Recovery lost actual origin or walk duration.");
            }),
            ("Recovery keeps fixed appointment before returning home",()=>{
                var booking=new AppointmentRecord{Id="meeting",ResidentId="daily",Status="accepted",StartsAt=12*3600,EndsAt=12*3600+600,
                    Place=new AppointmentPlace{Kind="building",SettlementId=100,BuildingKey=20}};
                var plan=Recover(8*3600,10,new[]{booking});
                Require(plan.Legs.Count==2 && plan.Legs[0].AppointmentId==booking.Id,"Fixed appointment removed or optional errand retained.");
                Require(plan.Legs[0].EnterAt==booking.StartsAt,"Appointment moved.");
            }),
            ("Late home travel stays physical across sleep",()=>{
                var plan=Recover(22*3600-50,20);Require(plan.EndsAt==22*3600+50,"Late return rejected or shortened.");
                Require(plan.Evaluate(22*3600).Activity==Activity.Travel,"Bedtime teleported resident home.");
                Require(plan.Evaluate(plan.EndsAt).Activity==Activity.Sleep,"Late arrival does not begin sleep.");
                Require(RoutineRecoveryPlan.Copy(plan).EndsAt==plan.EndsAt,"Late plan cannot save/load.");
            }),
            ("Recovery may start after ordinary sleep",()=>{
                var routine=Routine();var schedule=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
                Require(RoutineRecoveryAcceptance.TryCreateAfterAction(100,Person(),routine,schedule,null,0,22*3600,600,20,360,1320,1,
                    out var plan,out var reason),reason);Require(plan.EndsAt==22*3600+700,"Action return duration lost.");
            }),
            ("Late return crosses midnight without false arrival",()=>{
                var plan=Recover(86400-50,20);var routine=Routine();
                var schedule=DailySchedule.ContinueDay(DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320),plan);
                Require(schedule.EvaluateSeconds(86400).Activity==Activity.Travel,"Midnight discarded physical return.");
                Require(schedule.EvaluateSeconds(plan.EndsAt).Activity==Activity.Sleep,"Midnight return lost sleep.");
            }),
            ("Recovery rejects unreachable actual origin",()=>{
                var routine=Routine();var schedule=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
                Require(!RoutineRecoveryPlanner.TryCreate(100,Person(),routine,schedule,null,0,12*3600,99,12*3600,360,1320,1,
                    out _,out _),"Unknown origin invented a route.");
            })};
        int failed=0;foreach(var test in tests)try{test.Item2();Console.WriteLine("PASS "+test.Item1);}catch(Exception error){failed++;Console.WriteLine("FAIL "+test.Item1+": "+error.Message);}
        Console.WriteLine((tests.Count-failed)+"/"+tests.Count+" passed");return failed+PeopleOfTheIliacBay.Tests.LegacyRunner.Run()==0?0:1;
    }
}
