using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class AppointmentDayPlannerTests
    {
        private const int Home = 10;
        private static Resident Resident(uint seed=17) => new Resident { Id="planner-resident", HomeKey=Home,
            AppearanceSeed=seed, IsAlive=true, AgeGroup=ResidentAgeGroup.Adult };

        private static AppointmentRecord Booking(string id, int building, double start, double end, double grace=0)
        { return new AppointmentRecord { Id=id, ResidentId="planner-resident", Purpose="meet", StartsAt=start,
            EndsAt=end, GraceSeconds=grace, Status="accepted",
            Place=new AppointmentPlace { Kind="building", SettlementId=100, BuildingKey=building } }; }

        private static ResidentRoutine Routine(bool twoStops=false)
        {
            var itinerary=new ResidentDestination
            {
                Stops=new List<ResidentVisitStop>
                {
                    new ResidentVisitStop { BuildingKey=20, Purpose=ResidentVisitPurpose.Shop,
                        OpenMinute=0, CloseMinute=0, WalkSeconds=Travel(100), DwellSeconds=600,
                        Route=new List<SavedRoutePoint>{P(0,0),P(100,0)} }
                },
                ExitRoute=new List<SavedRoutePoint>{P(100,0),P(0,0)}, ExitWalkSeconds=Travel(100)
            };
            if(twoStops)
            {
                itinerary.Stops.Add(new ResidentVisitStop { BuildingKey=30, Purpose=ResidentVisitPurpose.Social,
                    OpenMinute=0, CloseMinute=0, WalkSeconds=Travel(100), DwellSeconds=600,
                    Route=new List<SavedRoutePoint>{P(100,0),P(200,0)} });
                itinerary.ExitRoute=new List<SavedRoutePoint>{P(200,0),P(100,0),P(0,0)};
            }
            return new ResidentRoutine { ResidentId="planner-resident", Itinerary=itinerary, ItineraryDepartureSeconds=8*3600 };
        }
        private static double Travel(double metres) => metres/ResidentMovement.WalkSpeedFor(17)*2;
        private static SavedRoutePoint P(double x,double z) => new SavedRoutePoint {X=x,Z=z};

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Appointment day planner uses exact arrival and real out-and-back walking", RequiredVisitsArriveExactlyAndWalk));
            cases.Add(("Appointment day planner retains only nonconflicting ordinary visits", OptionalVisitsKeepOriginalTimesOrYieldToBookings));
            cases.Add(("Appointment day planner ignores other days and rejects unreachable or conflicting bookings", DateReachabilityAndConflictValidation));
            cases.Add(("Composed appointment schedules expose repeated venue windows and isolate route copies", RepeatedVenueAndCopyIsolation));
            cases.Add(("Resolved appointments preserve physical day plans across schedule rebuild", ResolvedAppointmentsKeepTheirDaySlot));
            cases.Add(("Appointment patience and meeting duration reserve different parts of the day", PatienceAndMeetingDurationUseSeparateDeadlines));
            cases.Add(("Resolved no-show ends its hold at actual outcome and preserves later plans", ResolvedNoShowReleasesItsOldHoldAtOutcome));
            cases.Add(("Appointment return route starts at actual interior position with saved clock rate", AppointmentReturnStartsAtMeasuredPosition));
        }

        private static void RequiredVisitsArriveExactlyAndWalk()
        {
            var resident=Resident(); var routine=Routine(true);
            var appointments=new List<AppointmentRecord>{
                Booking("a",20,9*3600,9*3600+600,30),
                Booking("b",30,11*3600,11*3600+600)
            };
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,appointments,360,1320,out var schedule,out var reason),reason);
            Check.True(schedule.ContainsVisitBuilding(20)); Check.True(schedule.ContainsVisitBuilding(30));
            Check.True(schedule.TryVisitWindowForBuilding(8*3600+20*60,20,out var start,out var end));
            Check.Near(9*3600,start); Check.Near(9*3600+630,end);
            Check.Equal(Activity.Travel,schedule.EvaluateSeconds(9*3600-1).Activity);
            Check.Equal(Activity.Visit,schedule.EvaluateSeconds(9*3600).Activity);
            Check.Equal(20,schedule.EvaluateSeconds(9*3600).BuildingKey);
            Check.Equal(Activity.Home,schedule.EvaluateSeconds(9*3600+630+Travel(100)).Activity);
            Check.True(schedule.TryVisitWindowForBuilding(9*3600,30,out start,out end));
            Check.Near(11*3600,start);
            Check.True(schedule.ReturnHomeAt(11*3600+1)<86400);
            Check.Near(11*3600+600+Travel(200),schedule.ReturnHomeAt(11*3600+1));
            string context=ConversationLifeContext.EffectiveSchedule(schedule,11*3600+1,
                schedule.EvaluateSeconds(11*3600+1),0,key=>key==20 ? "Copper Kettle" : key==30 ? "Fox and Firkin" : "home");
            Check.True(context.Contains("currently at Fox and Firkin"));
            Check.True(context.Contains("11:00") && context.Contains("11:10"));
            Check.True(context.Contains("09:00"));
        }

        private static void OptionalVisitsKeepOriginalTimesOrYieldToBookings()
        {
            var resident=Resident(); var routine=Routine();
            routine.ItineraryDepartureSeconds=8*3600;
            // The saved itinerary reaches building 20 at 08:00+travel and visits for ten minutes.
            var optional=DailySchedule.ItineraryDay(Home,routine.Itinerary,routine.ItineraryDepartureSeconds,360,1320);
            optional.TryVisitWindowForBuilding(0,20,out var originalStart,out var originalEnd);
            var later=Booking("later",20,12*3600,12*3600+600);
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{later},360,1320,out var schedule,out var reason),reason);
            Check.True(schedule.TryVisitWindowForBuilding(0,20,out var actualStart,out var actualEnd));
            Check.Near(originalStart,actualStart); Check.Near(originalEnd,actualEnd);
            Check.True(schedule.ContainsVisitBuilding(20));

            var conflicting=Booking("conflict",20,originalStart,originalEnd);
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{conflicting},360,1320,out schedule,out reason),reason);
            Check.True(schedule.ContainsVisitBuilding(20));
            Check.True(!schedule.TryVisitWindowForBuilding(originalStart,20,out actualStart,out actualEnd) ||
                Math.Abs(actualStart-originalStart)<0.001);
        }

        private static void DateReachabilityAndConflictValidation()
        {
            var resident=Resident(); var routine=Routine(true);
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,
                new[]{Booking("tomorrow",20,86400+9*3600,86400+9*3600+600)},360,1320,out var schedule,out var reason),reason);
            Check.True(!schedule.ContainsVisitBuilding(99));
            var unreachable=Booking("unreachable",99,9*3600,9*3600+600);
            Check.True(!AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{unreachable},360,1320,out schedule,out reason));
            Check.True(!string.IsNullOrWhiteSpace(reason));
            var overlapping=new[]{Booking("first",20,9*3600,9*3600+1800),Booking("second",20,9*3600+1200,9*3600+2400)};
            Check.True(!AppointmentDayPlanner.TryCreate(resident,routine,0,overlapping,360,1320,out schedule,out reason));
            var outsideWake=Booking("sleep",20,5*3600,5*3600+600);
            Check.True(!AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{outsideWake},360,1320,out schedule,out reason));
            var patiencePastSleep=Booking("patience-past-sleep",20,17*3600+30*60,17*3600+40*60);
            patiencePastSleep.PatienceSeconds=7200;
            Check.True(!AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{patiencePastSleep},360,1140,out schedule,out reason));
            resident.NativeIdentity=new NativePerson {SettlementId=101};
            Check.True(!AppointmentDayPlanner.TryCreate(resident,routine,0,
                new[]{Booking("other-town",20,9*3600,9*3600+600)},360,1320,out schedule,out reason));
        }

        private static void RepeatedVenueAndCopyIsolation()
        {
            var resident=Resident(); var routine=Routine();
            var booking=Booking("repeat-1",20,9*3600,9*3600+600);
            var second=Booking("repeat-2",20,11*3600,11*3600+600);
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{booking,second},360,1320,out var schedule,out var reason),reason);
            booking.Place.BuildingKey=999;
            Check.True(schedule.TryVisitWindowForBuilding(8*3600+20*60,20,out var start,out var end));
            Check.Near(9*3600,start);
            Check.True(schedule.TryVisitWindowForBuilding(10*3600,20,out start,out end));
            Check.Near(11*3600,start);
            Check.True(!schedule.ContainsVisitBuilding(999));
            var mismatched=DailySchedule.ItineraryDay(Home,Routine().Itinerary,8*3600,300,1300);
            Check.Throws<ArgumentException>(()=>DailySchedule.OutingsDay(Home,new[]{mismatched},360,1320));
        }

        private static void ResolvedAppointmentsKeepTheirDaySlot()
        {
            var resident=Resident(); var routine=Routine(true);
            var accepted=Booking("resolved",30,9*3600,9*3600+600);
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{accepted},360,1320,out var before,out var reason),reason);
            accepted.Status="attended"; accepted.OutcomeRecorded=true; accepted.ResolvedAt=9*3600+120;
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{accepted},360,1320,out var after,out reason),reason);
            Check.True(after.TryVisitWindowForBuilding(8*3600+30*60,30,out var beforeStart,out var beforeEnd));
            Check.Near(9*3600,beforeStart); Check.Near(9*3600,beforeEnd-600);
            Check.True(before.TryVisitWindowForBuilding(8*3600+30*60,30,out var acceptedStart,out var acceptedEnd));
            Check.Near(acceptedStart,beforeStart); Check.Near(acceptedEnd,beforeEnd);
            accepted.Status="cancelled";
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{accepted},360,1320,out var cancelled,out reason),reason);
            Check.True(!cancelled.ContainsVisitBuilding(30));
        }

        private static void PatienceAndMeetingDurationUseSeparateDeadlines()
        {
            var resident=Resident(); var routine=Routine(true);
            var shortPatience=Booking("short-patience",20,9*3600,9*3600+1800);
            shortPatience.PatienceSeconds=600;
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{shortPatience},360,1320,out var schedule,out var reason),reason);
            Check.True(schedule.TryVisitWindowForBuilding(8*3600+30*60,20,out var start,out var end));
            Check.Near(9*3600,start); Check.Near(9*3600+1800,end);

            // Before outcome, longer patience reserves time beyond the agreed meeting end.
            var longPatience=Booking("long-patience",20,9*3600,9*3600+600);
            longPatience.PatienceSeconds=7200;
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{longPatience},360,1320,out schedule,out reason),reason);
            Check.True(schedule.TryVisitWindowForBuilding(8*3600+30*60,20,out start,out end));
            Check.Near(9*3600+7200,end);

            var missedEarly=Booking("short-patience-missed",20,9*3600,10*3600);
            missedEarly.PatienceSeconds=600; missedEarly.Status="player-absent"; missedEarly.OutcomeRecorded=true;
            missedEarly.NpcArrivedAt=9*3600; missedEarly.NpcLastSeenAt=9*3600+600;
            missedEarly.NpcWaitedSeconds=30; missedEarly.ResolvedAt=9*3600+615; missedEarly.NoShowPenalty=5;
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{missedEarly},360,1320,out schedule,out reason),reason);
            Check.True(schedule.TryVisitWindowForBuilding(8*3600+30*60,20,out start,out end));
            Check.Near(9*3600+615,end);

            // Once attended, patience is irrelevant; the resident remains for agreed duration.
            longPatience.Status="attended"; longPatience.OutcomeRecorded=true;
            longPatience.ResolvedAt=9*3600+300; longPatience.NpcArrivedAt=9*3600;
            longPatience.PlayerArrivedAt=9*3600+300;
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{longPatience},360,1320,out schedule,out reason),reason);
            Check.True(schedule.TryVisitWindowForBuilding(8*3600+30*60,20,out start,out end));
            Check.Near(9*3600+600,end);
        }

        private static void ResolvedNoShowReleasesItsOldHoldAtOutcome()
        {
            var resident=Resident(); var routine=Routine(true);
            var noShow=Booking("long-patience-no-show",20,9*3600,9*3600+600);
            noShow.PatienceSeconds=7200;
            var overlapsPatience=Booking("overlaps-patience",30,10*3600+1800,10*3600+2400);
            Check.True(!AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{noShow,overlapsPatience},360,1320,out _,out _));

            noShow.Status="player-absent"; noShow.OutcomeRecorded=true;
            noShow.NpcArrivedAt=9*3600; noShow.NpcLastSeenAt=11*3600;
            noShow.NpcWaitedSeconds=30; noShow.ResolvedAt=11*3600+15; noShow.NoShowPenalty=5;
            var later=Booking("later",30,11*3600+1800,11*3600+2400);
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{noShow,later},360,1320,out var schedule,out var reason),reason);
            Check.True(schedule.TryVisitWindowForBuilding(10*3600,20,out var start,out var end));
            Check.Near(9*3600,start); Check.Near(11*3600+15,end);
            Check.True(schedule.ContainsVisitBuilding(30));
            Check.Near(11*3600+2400+Travel(200),schedule.ReturnHomeAt(12*3600));
            Check.Equal(Activity.Sleep,schedule.EvaluateSeconds(1320*60).Activity);

            // An NPC absence has no physical arrival evidence. Keep conservative reservation
            // window rather than claiming the actor left from the venue.
            noShow.Status="npc-absent"; noShow.NpcArrivedAt=-1; noShow.NpcLastSeenAt=-1;
            noShow.NpcWaitedSeconds=0; noShow.ResolvedAt=11*3600+30; noShow.NoShowPenalty=0;
            Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{noShow},360,1320,out schedule,out reason),reason);
            Check.True(schedule.TryVisitWindowForBuilding(10*3600,20,out start,out end));
            Check.Near(11*3600,end);
        }

        private static void AppointmentReturnStartsAtMeasuredPosition()
        {
            const double startedAt=10000;
            var departure=VisitDeparture.Create("planner-resident",100,Home,30,startedAt,20000,
                new List<GuardPoint3> { new GuardPoint3 {X=7.25,Y=1.5,Z=-4},
                    new GuardPoint3 {X=10.25,Y=1.5,Z=-4} },
                new List<SavedRoutePoint> { P(20,-4),P(0,0) },2,4);
            var start=departure.Position(startedAt);
            Check.Equal(30,start.BuildingKey); Check.Near(7.25,start.X); Check.Near(-4,start.Z);
            var moving=departure.Position(startedAt+2);
            Check.Equal(30,moving.BuildingKey); Check.Near(8.25,moving.X);
            var exterior=departure.Position(startedAt+6);
            Check.Equal(-1,exterior.BuildingKey); Check.Near(20,exterior.X);
        }
    }
}
