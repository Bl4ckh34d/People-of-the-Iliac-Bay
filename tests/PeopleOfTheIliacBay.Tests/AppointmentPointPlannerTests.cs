using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class AppointmentPointPlannerTests
    {
        private static SavedRoutePoint P(double x,double z) => new SavedRoutePoint {X=x,Z=z};
        private static Resident Person() => new Resident {Id="point-resident",HomeKey=10,AppearanceSeed=17,
            IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
        private static ResidentRoutine Routine() => new ResidentRoutine {ResidentId="point-resident",Itinerary=
            new ResidentDestination {Stops=new List<ResidentVisitStop> {new ResidentVisitStop {BuildingKey=20,
                Purpose=ResidentVisitPurpose.Social,WalkSeconds=100,DwellSeconds=600,
                Route=new List<SavedRoutePoint>{P(0,0),P(100,0)}}},
                ExitRoute=new List<SavedRoutePoint>{P(100,0),P(0,0)},ExitWalkSeconds=100},ItineraryDepartureSeconds=8*3600};
        private static AppointmentRecord Booking()
        {
            return new AppointmentRecord {Id="point-meeting",ResidentId="point-resident",Purpose="meet",
                StartsAt=9*3600,EndsAt=9*3600+600,GraceSeconds=0,Status="accepted",
                Place=new AppointmentPlace {Kind="world-point",SettlementId=100,BuildingKey=-1,X=150,Y=0,Z=30}};
        }
        private static AppointmentPlaceRoute VerifiedRoute(AppointmentRecord booking,int anchor=20)
        {
            return new AppointmentPlaceRoute {AppointmentId=booking.Id,Place=new AppointmentPlace {Kind=booking.Place.Kind,
                SettlementId=booking.Place.SettlementId,BuildingKey=booking.Place.BuildingKey,X=booking.Place.X,
                Y=booking.Place.Y,Z=booking.Place.Z,Detail=booking.Place.Detail},
                Connections=new List<AppointmentPlaceRouteConnection> {new AppointmentPlaceRouteConnection {AnchorBuildingKey=anchor,
                    Route=new List<SavedRoutePoint>{P(100,0),P(125,15),P(150,30)}}}};
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Exact outdoor appointment point is routed, waited at, and returned from",ExactPointOuting));
            cases.Add(("Point planner rejects stale, disconnected, or malformed native routes",PointRouteMustMatchCapturedPlace));
        }
        private static void ExactPointOuting()
        {
            var booking=Booking();var routes=new[]{VerifiedRoute(booking)};
            Check.True(AppointmentDayPlanner.TryCreate(Person(),Routine(),0,new[]{booking},routes,360,1320,
                out var schedule,out var reason),reason);
            var atTarget=schedule.EvaluateSeconds(booking.StartsAt);
            Check.Equal(Activity.Meeting,atTarget.Activity);
            Check.Equal(-1,atTarget.BuildingKey);
            Check.Near(booking.Place.X,atTarget.Position.X);
            Check.Near(booking.Place.Z,atTarget.Position.Z);
            double leave=AppointmentTiming.PlannedLeaveAt(booking);
            var outbound=schedule.EvaluateSeconds(booking.StartsAt-1);
            Check.Equal(Activity.Travel,outbound.Activity);
            Check.True(Math.Abs(outbound.Position.X-booking.Place.X)+Math.Abs(outbound.Position.Z-booking.Place.Z)>0.001,"Outbound route must reach target over time.");
            var returnLeg=schedule.EvaluateSeconds(leave+0.001);
            Check.Equal(Activity.Travel,returnLeg.Activity);
            Check.Near(booking.Place.X,returnLeg.Position.X,0.01);
            Check.Near(booking.Place.Z,returnLeg.Position.Z,0.01);
            Check.Equal(Activity.Home,schedule.EvaluateSeconds(schedule.ReturnHomeAt(leave+1)+0.001).Activity);
        }
        private static void PointRouteMustMatchCapturedPlace()
        {
            var booking=Booking();var route=VerifiedRoute(booking);
            Check.True(!AppointmentDayPlanner.TryCreate(Person(),Routine(),0,new[]{booking},null,360,1320,out _,out _));
            booking.Place.X+=1;
            Check.True(!AppointmentDayPlanner.TryCreate(Person(),Routine(),0,new[]{booking},new[]{route},360,1320,out _,out _));
            booking=Booking();route=VerifiedRoute(booking,99);
            Check.True(!AppointmentDayPlanner.TryCreate(Person(),Routine(),0,new[]{booking},new[]{route},360,1320,out _,out _));
            route=VerifiedRoute(booking);route.Connections[0].Route[2].Z+=0.5;
            Check.True(!AppointmentDayPlanner.TryCreate(Person(),Routine(),0,new[]{booking},new[]{route},360,1320,out _,out _));
        }
    }
}
