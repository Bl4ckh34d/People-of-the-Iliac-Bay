using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class RoutineRecoveryPointTests
    {
        private static SavedRoutePoint P(double x,double z=0)=>new SavedRoutePoint {X=x,Z=z};
        private static Resident Person()=>new Resident {Id="point-recovery",HomeKey=10,AppearanceSeed=8,
            IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
        private static ResidentRoutine Routine()=>new ResidentRoutine {ResidentId="point-recovery",
            ItineraryDepartureSeconds=9*3600,Itinerary=new ResidentDestination {Stops=new List<ResidentVisitStop> {
                new ResidentVisitStop {BuildingKey=20,WalkSeconds=100,DwellSeconds=3600,Route=new List<SavedRoutePoint>{P(0),P(100)}},
                new ResidentVisitStop {BuildingKey=30,WalkSeconds=100,DwellSeconds=3600,Route=new List<SavedRoutePoint>{P(100),P(200)}}},
                ExitRoute=new List<SavedRoutePoint>{P(200),P(100),P(0)},ExitWalkSeconds=200}};
        private static AppointmentRecord Booking()=>new AppointmentRecord {Id="outdoor-meeting",ResidentId="point-recovery",
            StartsAt=11*3600,EndsAt=11*3600+600,PatienceSeconds=900,Status="accepted",
            Place=new AppointmentPlace {Kind="world-point",SettlementId=100,BuildingKey=-1,X=150,Y=0,Z=0}};
        private static AppointmentPlaceRoute Route(AppointmentRecord booking)=>new AppointmentPlaceRoute {
            AppointmentId=booking.Id,Place=new AppointmentPlace {Kind=booking.Place.Kind,SettlementId=booking.Place.SettlementId,
                BuildingKey=booking.Place.BuildingKey,X=booking.Place.X,Y=booking.Place.Y,Z=booking.Place.Z,Detail=booking.Place.Detail},
            Connections=new List<AppointmentPlaceRouteConnection> {
                new AppointmentPlaceRouteConnection {AnchorBuildingKey=20,Route=new List<SavedRoutePoint>{P(100),P(125,10),P(150)}}}};

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Routine recovery preserves exact outdoor appointments through a typed point leg",KeepsPointAppointment));
            cases.Add(("Saved recovery point proof is bound to exact booking identity and coordinates",PointProofBoundToBooking));
        }
        private static void KeepsPointAppointment()
        {
            var routine=Routine();var booking=Booking();
            var ordinary=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
            Check.True(RoutineRecoveryPlanner.TryCreate(100,Person(),routine,ordinary,new[]{booking},new[]{Route(booking)},
                0,10*3600,20,10*3600+10,360,1320,1,out var plan,out var reason,out var failure),reason);
            Check.Equal(RoutineRecoveryFailure.None,failure);
            var meeting=plan.Legs.Find(leg=>leg.AppointmentId==booking.Id);
            Check.True(meeting!=null && meeting.PointDestination);
            Check.Equal(-1,meeting.BuildingKey);
            Check.True(meeting.RouteNodeKey>=0);
            Check.Near(booking.Place.X,meeting.Route[meeting.Route.Count-1].X);
            Check.Near(booking.Place.Z,meeting.Route[meeting.Route.Count-1].Z);
            var atMeeting=plan.Evaluate(booking.StartsAt+1);
            Check.Equal(Activity.Meeting,atMeeting.Activity);
            Check.Equal(-1,atMeeting.BuildingKey);
            Check.Near(booking.Place.X,atMeeting.Position.X);
            Check.Near(booking.Place.Z,atMeeting.Position.Z);
            var saved=RoutineRecoveryPlan.Copy(plan);
            Check.True(RoutineRecoveryRoutes.Matches(saved,routine,10,new[]{booking}));
            Check.True(saved.Legs[saved.Legs.Count-1].ReturnsHome);
        }
        private static void PointProofBoundToBooking()
        {
            var routine=Routine();var booking=Booking();var ordinary=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
            Check.True(RoutineRecoveryPlanner.TryCreate(100,Person(),routine,ordinary,new[]{booking},new[]{Route(booking)},
                0,10*3600,20,10*3600+10,360,1320,1,out var plan,out var reason),reason);
            var stale=Booking();stale.Place.X+=2;
            Check.True(!RoutineRecoveryRoutes.Matches(plan,routine,10,new[]{stale}));
            plan.AppointmentPointRoutes[0].Connections[0].Route[2].X+=2;
            Check.True(!RoutineRecoveryRoutes.Matches(plan,routine,10,new[]{booking}));
        }
    }
}
