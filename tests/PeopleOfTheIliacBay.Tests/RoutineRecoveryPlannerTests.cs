using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class RoutineRecoveryPlannerTests
    {
        private static Resident Person() { return new Resident {Id="recover",HomeKey=10,AppearanceSeed=17,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult}; }
        private static SavedRoutePoint P(double x) { return new SavedRoutePoint {X=x,Z=0}; }
        private static ResidentRoutine Routine()
        {
            return new ResidentRoutine {ResidentId="recover",ItineraryDepartureSeconds=9*3600-100,
                Itinerary=new ResidentDestination {Stops=new List<ResidentVisitStop> {
                    new ResidentVisitStop {BuildingKey=20,WalkSeconds=100,DwellSeconds=3600,Route=new List<SavedRoutePoint>{P(0),P(100)}},
                    new ResidentVisitStop {BuildingKey=30,WalkSeconds=100,DwellSeconds=3600,Route=new List<SavedRoutePoint>{P(100),P(200)}}},
                    ExitRoute=new List<SavedRoutePoint>{P(200),P(100),P(0)},ExitWalkSeconds=200}};
        }
        private static RoutineRecoveryPlan Build(ResidentRoutine routine,double now,int origin=10,IList<AppointmentRecord> bookings=null,bool originOutside=false)
        {
            var ordinary=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
            Check.True(RoutineRecoveryPlanner.TryCreate(100,Person(),routine,ordinary,bookings,0,now,origin,now+10,360,1320,1,
                out var plan,out var reason,originOutside),reason);
            return plan;
        }
        private static void SkipOnlyInfeasibleStop()
        {
            var plan=Build(Routine(),9*3600+50*60,30);
            Check.Equal(2,plan.SkippedStopIndices.Count);Check.Equal(1,plan.Legs.Count);
            Check.True(plan.Legs[0].ReturnsHome);Check.Near(200,plan.EndsAt-plan.DepartAt);
            Check.Equal(Activity.Travel,plan.Evaluate(plan.DepartAt+100).Activity);
            Check.Equal(Activity.Home,plan.Evaluate(plan.EndsAt).Activity);
            Check.Equal(Activity.Sleep,plan.Evaluate(22*3600).Activity);
        }
        private static void ExpiredStopsNeverReplay()
        {
            var plan=Build(Routine(),12*3600,30);
            Check.Equal(2,plan.SkippedStopIndices.Count); Check.Equal(1,plan.Legs.Count);
            Check.True(plan.Legs[0].ReturnsHome); Check.Near(200,plan.EndsAt-plan.DepartAt);
            var copy=RoutineRecoveryPlan.Copy(plan);
            plan.Legs[0].Route[0].X=999; plan.SkippedStopIndices.Clear();
            Check.Near(200,copy.Legs[0].Route[0].X); Check.Equal(2,copy.SkippedStopIndices.Count);
        }
        private static void AppointmentTimesStayFixed()
        {
            var booking=new AppointmentRecord {Id="meeting",ResidentId="recover",Status="accepted",StartsAt=12*3600+17,
                EndsAt=12*3600+617,GraceSeconds=30,Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=20}};
            var plan=Build(Routine(),9*3600+50*60,10,new[]{booking});
            var meeting=plan.Legs.Find(l=>l.AppointmentId=="meeting");
            Check.True(meeting!=null); Check.Equal(-1,meeting.OriginalStopIndex);
            Check.Near(booking.StartsAt,meeting.EnterAt); Check.Near(booking.EndsAt+30,meeting.LeaveAt);
            Check.Equal("meeting",plan.Legs[0].AppointmentId);
            var missed=BuildRequest(Routine(),12*3600+10,10,new[]{booking},out var reason);
            Check.True(missed==null,"Recovery silently moved or erased an unreachable required appointment.");
            Check.True(reason.Contains("appointment"));
        }
        private static RoutineRecoveryPlan BuildRequest(ResidentRoutine routine,double now,int origin,IList<AppointmentRecord> bookings,out string reason)
        {
            var ordinary=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
            return RoutineRecoveryPlanner.TryCreate(100,Person(),routine,ordinary,bookings,0,now,origin,now+10,360,1320,1,
                out var result,out reason) ? result : null;
        }
        private static void CapturedPatienceSurvivesRoutineRecovery()
        {
            var booking=new AppointmentRecord {Id="patient",ResidentId="recover",StartsAt=12*3600,EndsAt=12*3600+600,
                PatienceSeconds=9000,Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=20}};
            var plan=Build(Routine(),12*3600+1200,20,new[]{booking});
            Check.Equal("patient",plan.Legs[0].AppointmentId);
            Check.Near(booking.StartsAt+9000,plan.Legs[0].LeaveAt);
            string fingerprint=RoutineRecoveryIdentity.Appointments(new[]{booking},"recover",0);
            booking.PatienceSeconds=600;
            Check.True(fingerprint!=RoutineRecoveryIdentity.Appointments(new[]{booking},"recover",0),"Captured patience changed without invalidating recovery.");
            booking.PatienceSeconds=9000;booking.Status="attended";booking.OutcomeRecorded=true;booking.ResolvedAt=booking.StartsAt+60;
            Check.True(fingerprint!=RoutineRecoveryIdentity.Appointments(new[]{booking},"recover",0),"Attendance changed departure without invalidating recovery.");
            plan=Build(Routine(),12*3600+1200,20,new[]{booking});
            Check.True(!plan.Legs.Exists(leg=>leg.AppointmentId==booking.Id),"An attended meeting held the NPC for no-show patience.");
            booking.Status="player-absent";booking.NpcArrivedAt=booking.StartsAt;booking.ResolvedAt=booking.StartsAt+9001;
            plan=Build(Routine(),booking.ResolvedAt+60,20,new[]{booking});
            Check.True(!plan.Legs.Exists(leg=>leg.AppointmentId==booking.Id),"An expired no-show was replayed after recovery.");
        }
        private static void NoShowExitContinuesFromSameDoor()
        {
            var routine=Routine();
            var ordinary=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
            double now=9*3600+601,exitAt=now+40;
            var booking=new AppointmentRecord {Id="missed",ResidentId="recover",StartsAt=9*3600,EndsAt=10*3600,
                PatienceSeconds=600,Status="player-absent",OutcomeRecorded=true,NpcArrivedAt=9*3600,ResolvedAt=now,
                Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=20}};
            Check.True(RoutineRecoveryPlanner.TryCreate(100,Person(),routine,ordinary,new[]{booking},0,now,20,exitAt,
                360,1320,1,out var plan,out var reason,true),reason);
            Check.True(plan.OriginOutside);Check.Near(now,plan.CreatedAt);Check.Near(exitAt,plan.DepartAt);
            Check.True(plan.SkippedStopIndices.Contains(0),"No-show departure replayed the source visit.");
            Check.True(!plan.Legs.Exists(leg=>leg.AppointmentId==booking.Id));
            Check.Equal(10,plan.Legs[0].BuildingKey);Check.True(plan.Legs[0].ReturnsHome);
            Check.Near(100,plan.Legs[0].Route[0].X);
            Check.Near(100,plan.Evaluate(exitAt).Position.X);
            Check.Equal(Activity.Home,plan.Evaluate(plan.Legs[0].EnterAt).Activity);
            Check.Equal(Activity.Home,plan.Evaluate(plan.EndsAt).Activity);
        }
        private static void FingerprintCoversTimingAndGeometry()
        {
            var routine=Routine(); string original=RoutineRecoveryIdentity.Fingerprint(routine,10,360,1320);
            Check.Equal(original,RoutineRecoveryIdentity.Fingerprint(routine,10,360,1320));
            routine.Itinerary.Stops[0].DwellSeconds++;
            Check.True(original!=RoutineRecoveryIdentity.Fingerprint(routine,10,360,1320));
            routine=Routine(); routine.Itinerary.Stops[0].Route.Insert(1,new SavedRoutePoint {X=50,Z=1});
            Check.True(original!=RoutineRecoveryIdentity.Fingerprint(routine,10,360,1320));
        }
        private static void FingerprintSurvivesNativeDoubleFormatting()
        {
            // FullSerializer on the game's runtime emits these 15-digit values.
            // The .NET 8 printer keeps more digits and would hide this regression.
            var before=Routine();before.Itinerary.Stops[0].WalkSeconds=123.45678901234567;
            before.ItineraryDepartureSeconds=38422.509316737123;
            var restored=Routine();restored.Itinerary.Stops[0].WalkSeconds=123.456789012346;
            restored.ItineraryDepartureSeconds=38422.5093167371;
            Check.Equal(RoutineRecoveryIdentity.Fingerprint(before,10,360,1320),
                RoutineRecoveryIdentity.Fingerprint(restored,10,360,1320));
        }
        private static void MalformedContinuationRejected()
        {
            var plan=Build(Routine(),9*3600+50*60);
            plan.Legs[0].Route.Insert(1,new SavedRoutePoint {X=50,Z=double.NaN});
            Check.Throws<ArgumentException>(()=>RoutineRecoveryPlan.Copy(plan));
            Check.True(BuildRequest(Routine(),9*3600,99,null,out var reason)==null);
            Check.True(!string.IsNullOrEmpty(reason));
        }
        private static void ResumeCurrentVisitWithoutExtendingItsWindow()
        {
            var plan=Build(Routine(),9*3600+30*60,20);
            Check.True(plan.SkippedStopIndices.Contains(0));Check.True(plan.SkippedStopIndices.Contains(1));
            Check.Equal(1,plan.Legs.Count);Check.True(plan.Legs[0].ReturnsHome);
            Check.Near(100,plan.Legs[0].Route[0].X);
        }
        private static void OutsideOriginSkipsCurrentSourceStopAndKeepsLaterStop()
        {
            var plan=Build(Routine(),9*3600+30*60,20,null,true);
            Check.True(plan.OriginOutside);
            Check.True(plan.SkippedStopIndices.Contains(0),"Outdoor recovery re-entered the current source visit.");
            Check.True(!plan.Legs.Exists(l=>l.OriginalStopIndex>=0),"Outdoor recovery replayed optional errands.");
        }
        private static void OutsideOriginKeepsFutureVisitAtSameBuilding()
        {
            var plan=Build(Routine(),8*3600+50*60,20,null,true);
            Check.True(plan.OriginOutside);
            Check.True(plan.SkippedStopIndices.Contains(0),"Recovery replayed future optional visit.");
            Check.True(plan.Legs[0].ReturnsHome);
        }
        private static void OutsideOriginStillKeepsRequiredAppointmentAtSource()
        {
            var booking=new AppointmentRecord {Id="outdoor-current",ResidentId="recover",Status="accepted",
                StartsAt=9*3600+30*60,EndsAt=9*3600+40*60,GraceSeconds=0,
                Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=20}};
            var plan=Build(Routine(),9*3600+30*60,20,new[]{booking},true);
            Check.True(plan.OriginOutside);
            Check.True(plan.Legs.Exists(l=>l.AppointmentId==booking.Id),"Required appointment at source was dropped.");
            Check.True(plan.SkippedStopIndices.Contains(0),"Outdoor recovery re-entered optional current source visit.");
        }
        private static void RepeatedErrandKeepsCurrentVisitAfterContinuationChangesItsStart()
        {
            var routine=Routine();
            var original=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
            var first=Build(routine,9*3600+30*60,20);
            var resumed=DailySchedule.ContinueDay(original,first);
            Check.True(RoutineRecoveryPlanner.TryCreate(100,Person(),routine,resumed,null,0,9*3600+31*60,20,
                9*3600+31*60+10,360,1320,1,out var second,out var reason),reason);
            Check.Equal(1,second.Legs.Count);Check.True(second.Legs[0].ReturnsHome);
            Check.Near(100,second.Legs[0].Route[0].X);
        }
        private static void ResumeAppointmentAlreadyAtVenue()
        {
            var booking=new AppointmentRecord {Id="current",ResidentId="recover",Status="attended",StartsAt=12*3600,
                EndsAt=12*3600+600,GraceSeconds=30,Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=20}};
            var plan=Build(Routine(),12*3600+60,20,new[]{booking});
            Check.Equal("current",plan.Legs[0].AppointmentId);
            Check.Near(12*3600+630,plan.Legs[0].LeaveAt);
            Check.Equal(20,plan.Evaluate(12*3600+120).BuildingKey);
        }
        private static void ContinuedScheduleUsesItsOwnDayOnly()
        {
            var routine=Routine();var original=DailySchedule.ItineraryDay(10,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
            var plan=Build(routine,9*3600+50*60,30);
            var schedule=DailySchedule.ContinueDay(original,plan);
            Check.Equal(0,schedule.ScheduledVisits().Count);Check.True(!schedule.ContainsVisitBuilding(20));
            Check.Near(plan.EndsAt,schedule.ReturnHomeAt(10*3600));
            Check.Equal(Activity.Travel,schedule.EvaluateSeconds(plan.DepartAt+50).Activity);
            Check.Equal(20,schedule.EvaluateSeconds(9*3600+20*60).BuildingKey);
            Check.Equal(20,schedule.EvaluateSeconds(86400+9*3600+20*60).BuildingKey);
            plan.Legs[0].Route[0].X=999;
            Check.Near(150,schedule.EvaluateSeconds(plan.DepartAt+50).Position.X);
        }
        private static void HomeReturnMayCrossSleepBoundary()
        {
            var late=BuildRequest(Routine(),22*3600-60,30,null,out var reason);
            Check.True(late!=null,reason);Check.Near(22*3600+150,late.EndsAt);
            Check.Equal(Activity.Travel,late.Evaluate(22*3600).Activity);
            Check.Equal(Activity.Sleep,late.Evaluate(late.EndsAt).Activity);
            var feasible=Build(Routine(),22*3600-300,30);
            Check.True(feasible.Legs.Count==1 && feasible.Legs[0].ReturnsHome);
            Check.True(feasible.EndsAt<=22*3600);
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Routine recovery skips optional errands and keeps measured home route",SkipOnlyInfeasibleStop));
            cases.Add(("Routine recovery never replays expired visits and copies exact connected return routes",ExpiredStopsNeverReplay));
            cases.Add(("Routine recovery protects exact appointment times and rejects impossible arrival",AppointmentTimesStayFixed));
            cases.Add(("Routine recovery keeps captured appointment patience and invalidates changed outcomes",CapturedPatienceSurvivesRoutineRecovery));
            cases.Add(("No-show continuation starts at measured exit and returns home",NoShowExitContinuesFromSameDoor));
            cases.Add(("Routine recovery identity changes with timing or geometry",FingerprintCoversTimingAndGeometry));
            cases.Add(("Routine recovery identity survives native fifteen-digit number serialization",FingerprintSurvivesNativeDoubleFormatting));
            cases.Add(("Routine recovery rejects disconnected continuations and unknown origins",MalformedContinuationRejected));
            cases.Add(("Routine recovery skips interrupted optional visit and returns home",ResumeCurrentVisitWithoutExtendingItsWindow));
            cases.Add(("Outdoor recovery skips optional source and later errands",OutsideOriginSkipsCurrentSourceStopAndKeepsLaterStop));
            cases.Add(("Outdoor recovery skips future optional visit at same building",OutsideOriginKeepsFutureVisitAtSameBuilding));
            cases.Add(("Outdoor recovery preserves required appointment at source",OutsideOriginStillKeepsRequiredAppointmentAtSource));
            cases.Add(("Repeated routine recovery preserves physical return without replaying errands",RepeatedErrandKeepsCurrentVisitAfterContinuationChangesItsStart));
            cases.Add(("Routine recovery preserves an appointment already underway at the actual venue",ResumeAppointmentAlreadyAtVenue));
            cases.Add(("Continued schedules skip errands without rewriting another day or earlier history",ContinuedScheduleUsesItsOwnDayOnly));
            cases.Add(("Routine recovery retains measured home travel across ordinary sleep",HomeReturnMayCrossSleepBoundary));
        }
    }
}
