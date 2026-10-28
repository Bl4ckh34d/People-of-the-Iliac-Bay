using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class RoutineRecoveryAcceptanceTests
    {
        private static Resident Person() => new Resident {Id="recover",HomeKey=10,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
        private static SavedRoutePoint P(double x) => new SavedRoutePoint {X=x,Z=0};
        private static ResidentRoutine Routine()
        {
            return new ResidentRoutine {ResidentId="recover",ItineraryDepartureSeconds=9*3600,
                Itinerary=new ResidentDestination {Stops=new List<ResidentVisitStop> {
                    new ResidentVisitStop {BuildingKey=20,WalkSeconds=60,DwellSeconds=900,Route=new List<SavedRoutePoint>{P(0),P(100)}}},
                    ExitRoute=new List<SavedRoutePoint>{P(100),P(0)},ExitWalkSeconds=60}};
        }

        private static DailySchedule Ordinary(ResidentRoutine routine) =>
            DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);

        private static void ReturnDurationProtectsFixedAppointment()
        {
            var routine=Routine();
            var booking=new AppointmentRecord {Id="meeting",ResidentId="recover",Status="accepted",
                StartsAt=9*3600+30*60,EndsAt=9*3600+40*60,Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=20}};
            Check.True(!RoutineRecoveryAcceptance.TryCreateAfterAction(100,Person(),routine,Ordinary(routine),new[]{booking},
                0,9*3600+25*60,600,10,360,1320,1,out _,out var reason,out var failure),
                "The action's physical return time was ignored; fixed appointment became unreachable.");
            Check.True(reason.Contains("appointment"));
            Check.True(failure==RoutineRecoveryFailure.RequiredAppointmentUnreachable,
                "Planner must classify an unreachable appointment independently of its display text.");
        }

        private static void ReturnDurationMaySkipOptionalVisit()
        {
            var routine=Routine();
            var optional=RoutineRecoveryAcceptance.TryCreateAfterAction(100,Person(),routine,Ordinary(routine),null,
                0,9*3600+20*60,300,10,360,1320,1,out var plan,out var reason);
            Check.True(optional,reason);
            Check.True(plan.Legs[plan.Legs.Count-1].ReturnsHome);
            Check.True(plan.SkippedStopIndices.Contains(0),"A missed optional visit should be skipped while home return remains feasible.");
        }

        private static void ReturnDurationMayCrossOrdinarySleep()
        {
            var routine=Routine();
            Check.True(RoutineRecoveryAcceptance.TryCreateAfterAction(100,Person(),routine,Ordinary(routine),null,
                0,21*3600+50*60,20*60,20,360,1320,1,out var plan,out var reason),reason);
            Check.Near(22*3600+700,plan.EndsAt);
            Check.Equal(Activity.Travel,plan.Evaluate(plan.DepartAt+50).Activity);
            Check.Equal(Activity.Sleep,plan.Evaluate(plan.EndsAt).Activity);
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Routine recovery acceptance includes verified physical return time before fixed appointments",ReturnDurationProtectsFixedAppointment));
            cases.Add(("Routine recovery acceptance skips an infeasible optional visit and protects home return",ReturnDurationMaySkipOptionalVisit));
            cases.Add(("Routine recovery acceptance permits measured late return and sleep",ReturnDurationMayCrossOrdinarySleep));
        }
    }
}
