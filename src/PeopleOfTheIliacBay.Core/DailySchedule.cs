using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public struct RoutePoint
    {
        public readonly double X, Z;
        public RoutePoint(double x, double z) { X = x; Z = z; }
    }
    public enum Activity { Sleep, Home, Work, Travel, Visit, Meeting, Recovery }
    public struct ActivitySnapshot
    {
        public readonly Activity Activity;
        public readonly int BuildingKey;
        public readonly RoutePoint Position;
        public readonly double RouteProgress;
        public readonly RoutePoint? TravelDestination;
        public readonly int? WaitingForBuildingKey;
        public ActivitySnapshot(Activity activity, int buildingKey, RoutePoint position, double progress, RoutePoint? travelDestination=null,
            int? waitingForBuildingKey=null)
        { Activity = activity; BuildingKey = buildingKey; Position = position; RouteProgress = progress; TravelDestination=travelDestination;
            WaitingForBuildingKey=waitingForBuildingKey; }
    }
    public sealed partial class DailySchedule
    {
        private readonly int home, work, wake, sleep, open, close;
        private double commute;
        private readonly RoutePoint[] points;
        private readonly double[] distances;
        private Activity awayActivity = Activity.Work;
        private ResidentVisitJourney itineraryJourney;
        private ResidentVisitSnapshot[] itineraryVisits;
        private double itineraryDepartureSeconds;
        private double itineraryEndsAt;
        private PointOuting pointOuting;
        private DailySchedule[] composedOutings;
        private RoutineRecoveryPlan recoveryPlan;
        private DailySchedule recoverySource;
        private sealed class PointOuting
        {
            public double Departure, Arrival, Leave, Return;
            public RoutePoint Target;
            public RoutePoint[] Outbound, Inbound;
            public double[] OutboundLengths, InboundLengths;
            public ActivitySnapshot Evaluate(double seconds, int home)
            {
                if(seconds<Departure || seconds>=Return) return Interior(Activity.Home,home);
                if(seconds<Arrival) return Along(Outbound,OutboundLengths,(seconds-Departure)/(Arrival-Departure),Activity.Travel);
                if(seconds<Leave) return new ActivitySnapshot(Activity.Meeting,-1,Target,1);
                return Along(Inbound,InboundLengths,(seconds-Leave)/(Return-Leave),Activity.Travel);
            }
            private static ActivitySnapshot Along(RoutePoint[] points,double[] lengths,double fraction,Activity activity)
            {
                double distance=Math.Max(0,Math.Min(1,fraction))*lengths[lengths.Length-1];
                for(int i=1;i<points.Length;i++)
                {
                    double span=lengths[i]-lengths[i-1];
                    if(span<=0 || distance>lengths[i]) continue;
                    double f=(distance-lengths[i-1])/span;
                    return new ActivitySnapshot(activity,-1,new RoutePoint(
                        points[i-1].X+(points[i].X-points[i-1].X)*f,
                        points[i-1].Z+(points[i].Z-points[i-1].Z)*f),fraction,points[points.Length-1]);
                }
                return new ActivitySnapshot(activity,-1,points[points.Length-1],fraction,points[points.Length-1]);
            }
        }
        public static DailySchedule ContinueDay(DailySchedule ordinary,RoutineRecoveryPlan continuation)
        {
            if(ordinary==null)throw new ArgumentNullException(nameof(ordinary));
            var copy=RoutineRecoveryPlan.Copy(continuation);
            if(copy.HomeBuildingKey!=ordinary.home || copy.WakeMinute!=ordinary.wake || copy.SleepMinute!=ordinary.sleep)
                throw new ArgumentException("Continuation must keep the resident's home and waking window.");
            var result=HomeDay(ordinary.home,ordinary.wake,ordinary.sleep);
            result.recoveryPlan=copy;result.recoverySource=ordinary;
            var visits=new List<ResidentVisitSnapshot>();double dayStart=copy.AbsoluteDay*86400d;
            foreach(var leg in copy.Legs)
            {
                if(leg.ReturnsHome)continue;
                var endpoint=leg.Route[leg.Route.Count-1];
                visits.Add(new ResidentVisitSnapshot {VisitIndex=leg.HouseholdJoint?HouseholdDayPlanner.JointVisitIndex:leg.OriginalStopIndex,BuildingKey=leg.BuildingKey,
                    TargetBuildingKey=leg.BuildingKey,Purpose=leg.Purpose,Phase=ResidentVisitPhase.Inside,
                    Position=new RoutePoint(endpoint.X,endpoint.Z),ArriveAt=leg.ArriveAt-dayStart,
                    EnterAt=leg.EnterAt-dayStart,LeaveAt=leg.LeaveAt-dayStart,RoomUntil=leg.LeaveAt-dayStart});
            }
            result.itineraryVisits=visits.ToArray();return result;
        }
        private bool RecoveryApplies(double now)
        {return recoveryPlan!=null && now>=recoveryPlan.CreatedAt && now<(recoveryPlan.AbsoluteDay+1d)*86400d;}
        public double ReturnHomeAt(double daySeconds)
        {
            if(dateSource!=null)return ScheduleOn(daySeconds).ReturnHomeAt(daySeconds);
            if(recoveryPlan!=null)return RecoveryApplies(daySeconds)?recoveryPlan.EndsAt:recoverySource.ReturnHomeAt(daySeconds);
            double day = Math.Floor(daySeconds / 86400) * 86400;
            if (composedOutings != null)
            {
                double seconds = daySeconds - day;
                DailySchedule previous = null;
                foreach (var outing in composedOutings)
                {
                    if (seconds >= outing.itineraryDepartureSeconds && seconds < outing.itineraryEndsAt)
                        return day + outing.itineraryEndsAt;
                    if (seconds < outing.itineraryDepartureSeconds) return day + outing.itineraryEndsAt;
                    previous = outing;
                }
                return previous == null ? day : day + previous.itineraryEndsAt;
            }
            return day + (itineraryJourney!=null ? itineraryEndsAt : (close+commute)*60d);
        }
        private DailySchedule(int home, int work, int wake, int sleep, int open, int close, int commute, RoutePoint[] points)
        {
            this.home = home; this.work = work; this.wake = wake; this.sleep = sleep;
            this.open = open; this.close = close; this.commute = commute; this.points = points;
            distances = new double[points.Length];
            for (int i = 1; i < points.Length; i++)
            {
                double dx = points[i].X - points[i - 1].X, dz = points[i].Z - points[i - 1].Z;
                distances[i] = distances[i - 1] + Math.Sqrt(dx * dx + dz * dz);
            }
            if (points.Length > 0 && (distances[points.Length - 1] <= 0 || !Finite(distances[points.Length - 1])))
                throw new ArgumentException("Route must have finite positive length.", nameof(points));
        }
        public static DailySchedule Workday(int homeKey, int workKey, IEnumerable<RoutePoint> route,
            int openMinute, int closeMinute, int commuteMinutes, int wakeMinute, int sleepMinute)
        {
            ValidateHome(homeKey, wakeMinute, sleepMinute);
            bool onSite = workKey == homeKey;
            if (workKey < 0 || openMinute < 0 || closeMinute >= 1440 ||
                openMinute >= closeMinute || (onSite ? commuteMinutes != 0 : commuteMinutes < 1) || commuteMinutes >= 1440 ||
                openMinute - commuteMinutes < wakeMinute || closeMinute + commuteMinutes > sleepMinute)
                throw new ArgumentException("Work and travel must fit inside waking hours.");
            if (route == null) throw new ArgumentNullException(nameof(route));
            RoutePoint[] copy = new List<RoutePoint>(route).ToArray();
            if (onSite)
            {
                if (copy.Length != 0) throw new ArgumentException("On-site work cannot have an exterior commute.", nameof(route));
                return new DailySchedule(homeKey, workKey, wakeMinute, sleepMinute, openMinute, closeMinute, 0, copy);
            }
            if (copy.Length < 2) throw new ArgumentException("A commute requires at least two points.", nameof(route));
            foreach (RoutePoint point in copy)
                if (!Finite(point.X) || !Finite(point.Z)) throw new ArgumentException("Route coordinates must be finite.", nameof(route));
            return new DailySchedule(homeKey, workKey, wakeMinute, sleepMinute, openMinute, closeMinute, commuteMinutes, copy);
        }
        public static DailySchedule HomeDay(int homeKey, int wakeMinute, int sleepMinute)
        {
            ValidateHome(homeKey, wakeMinute, sleepMinute);
            return new DailySchedule(homeKey, -1, wakeMinute, sleepMinute, 0, 0, 0, new RoutePoint[0]);
        }
        public static DailySchedule ItineraryDay(int homeKey, ResidentDestination itinerary, double departureSeconds,
            int wakeMinute, int sleepMinute)
        {return ItineraryDay(homeKey,itinerary,departureSeconds,wakeMinute,sleepMinute,0);}
        public static DailySchedule ItineraryDay(int homeKey, ResidentDestination itinerary, double departureSeconds,
            int wakeMinute, int sleepMinute,int visitIndexOffset)
        {
            ValidateHome(homeKey, wakeMinute, sleepMinute);
            // The appointment ledger permits 10,000 records, after routine IDs.
            if(visitIndexOffset<0 || visitIndexOffset>16384)throw new ArgumentOutOfRangeException(nameof(visitIndexOffset));
            if (!Finite(departureSeconds) || departureSeconds < wakeMinute * 60d || departureSeconds >= sleepMinute * 60d ||
                departureSeconds >= 86400 || itinerary == null)
                throw new ArgumentException("Itinerary departure must fit inside waking hours.");
            var copied = ResidentVisitJourney.Copy(itinerary);
            if (copied.ThroughRoute) throw new ArgumentException("Daily itinerary must return home.", nameof(itinerary));
            foreach (var stop in copied.Stops)
                if (stop.Purpose == ResidentVisitPurpose.Inn)
                    throw new ArgumentException("Daily itineraries cannot reserve overnight inns.", nameof(itinerary));
            var journey = ResidentVisitJourney.Create(departureSeconds, copied);
            if (journey.EndsAt >= 86400 || journey.EndsAt > sleepMinute * 60d)
                throw new ArgumentException("Itinerary return must fit inside the same waking day.", nameof(itinerary));
            var schedule = HomeDay(homeKey, wakeMinute, sleepMinute);
            schedule.itineraryJourney = journey;
            schedule.itineraryDepartureSeconds = departureSeconds;
            schedule.itineraryEndsAt = journey.EndsAt;
            schedule.itineraryVisits = new List<ResidentVisitSnapshot>(journey.ScheduledVisits()).ToArray();
            for(int i=0;i<schedule.itineraryVisits.Length;i++)schedule.itineraryVisits[i].VisitIndex+=visitIndexOffset;
            return schedule;
        }
        public static DailySchedule PointOutingDay(int homeKey,IEnumerable<RoutePoint> outbound,IEnumerable<RoutePoint> inbound,
            RoutePoint target,double departureSeconds,double arrivalSeconds,double leaveSeconds,double returnSeconds,
            int wakeMinute,int sleepMinute)
        {
            ValidateHome(homeKey,wakeMinute,sleepMinute);
            if(!Finite(departureSeconds)||!Finite(arrivalSeconds)||!Finite(leaveSeconds)||!Finite(returnSeconds)||
                departureSeconds<wakeMinute*60d||departureSeconds>=arrivalSeconds||arrivalSeconds>=leaveSeconds||
                leaveSeconds>=returnSeconds||returnSeconds>sleepMinute*60d||returnSeconds>=86400||
                !Finite(target.X)||!Finite(target.Z)) throw new ArgumentException("Point meeting must fit inside waking hours.");
            var outPoints=CopyRoute(outbound);var inPoints=CopyRoute(inbound);
            if(!Same(outPoints[outPoints.Length-1],target)||!Same(inPoints[0],target))
                throw new ArgumentException("Point meeting routes must use the exact captured endpoint.");
            var schedule=HomeDay(homeKey,wakeMinute,sleepMinute);
            schedule.pointOuting=new PointOuting {Departure=departureSeconds,Arrival=arrivalSeconds,Leave=leaveSeconds,
                Return=returnSeconds,Target=target,Outbound=outPoints,Inbound=inPoints,
                OutboundLengths=Lengths(outPoints),InboundLengths=Lengths(inPoints)};
            schedule.itineraryDepartureSeconds=departureSeconds;schedule.itineraryEndsAt=returnSeconds;
            return schedule;
        }
        public static DailySchedule OutingsDay(int homeKey, IEnumerable<DailySchedule> outings,
            int wakeMinute, int sleepMinute)
        {
            ValidateHome(homeKey, wakeMinute, sleepMinute);
            if (outings == null) throw new ArgumentNullException(nameof(outings));
            var copy = new List<DailySchedule>(outings);
            if (copy.Exists(item => item == null))
                throw new ArgumentException("Outings cannot contain null schedules.", nameof(outings));
            copy.Sort((a,b) => a.itineraryDepartureSeconds.CompareTo(b.itineraryDepartureSeconds));
            double previousEnd = -1;
            foreach (var outing in copy)
            {
                if (outing.home != homeKey || outing.wake != wakeMinute || outing.sleep != sleepMinute ||
                    outing.itineraryJourney == null && outing.pointOuting == null ||
                    outing.composedOutings != null || outing.itineraryDepartureSeconds < wakeMinute * 60d ||
                    outing.itineraryEndsAt > sleepMinute * 60d || outing.itineraryEndsAt >= 86400 ||
                    // Native save rounding can move adjacent departures by a few
                    // floating-point bits relative to the recomputed return time.
                    outing.itineraryDepartureSeconds + 0.000001 < previousEnd)
                    throw new ArgumentException("Outings must be non-overlapping home-return itineraries inside waking hours.", nameof(outings));
                previousEnd = outing.itineraryEndsAt;
            }
            var result = HomeDay(homeKey, wakeMinute, sleepMinute);
            result.composedOutings = copy.ToArray();
            return result;
        }
        public static DailySchedule DayVisit(int homeKey, int destinationKey, IEnumerable<RoutePoint> route,
            int opens, int closes, int dwellMinutes, double metresPerRealSecond, double gameSecondsPerRealSecond,
            uint seed, int wakeMinute, int sleepMinute, int streetsOpenMinute=0, int streetsCloseMinute=1440)
        {
            ValidateHome(homeKey, wakeMinute, sleepMinute);
            if(streetsOpenMinute<0 || streetsCloseMinute>1440 || streetsOpenMinute>=streetsCloseMinute)
                throw new ArgumentException("Invalid exterior travel window.");
            if (!Finite(metresPerRealSecond) || metresPerRealSecond <= 0 ||
                !Finite(gameSecondsPerRealSecond) || gameSecondsPerRealSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(metresPerRealSecond));
            if (destinationKey < 0 || destinationKey == homeKey || opens < 0 || closes > 1440 ||
                opens >= closes || dwellMinutes < 1) throw new ArgumentException("Invalid visit window.");
            if (route == null) throw new ArgumentNullException(nameof(route));
            RoutePoint[] copy = new List<RoutePoint>(route).ToArray();
            double length = 0;
            for (int i = 0; i < copy.Length; i++)
            {
                if (!Finite(copy[i].X) || !Finite(copy[i].Z)) throw new ArgumentException("Invalid route coordinate.");
                if (i == 0) continue;
                double dx = copy[i].X - copy[i - 1].X, dz = copy[i].Z - copy[i - 1].Z;
                length += Math.Sqrt(dx * dx + dz * dz);
            }
            double travel = Math.Ceiling(length / metresPerRealSecond * gameSecondsPerRealSecond / 60);
            if (copy.Length < 2 || length <= 0 || !Finite(travel) || travel >= 1440)
                return HomeDay(homeKey, wakeMinute, sleepMinute);
            int commuteMinutes = Math.Max(1, (int)travel);
            int earliest = Math.Max(opens, Math.Max(wakeMinute,streetsOpenMinute) + commuteMinutes);
            int latest = Math.Min(closes, Math.Min(sleepMinute,streetsCloseMinute) - commuteMinutes) - dwellMinutes;
            if (latest < earliest) return HomeDay(homeKey, wakeMinute, sleepMinute);
            int arrival = earliest + (int)(seed % (uint)(latest - earliest + 1));
            var schedule = Workday(homeKey, destinationKey, copy, arrival, arrival + dwellMinutes,
                commuteMinutes, wakeMinute, sleepMinute);
            // Calendar slots may use whole minutes; physical movement must retain
            // the exact route duration, especially with a slower world clock.
            schedule.commute=length/metresPerRealSecond*gameSecondsPerRealSecond/60;
            schedule.awayActivity = Activity.Visit;
            return schedule;
        }
        public ActivitySnapshot EvaluateSeconds(double absoluteSeconds)
        {
            if (!Finite(absoluteSeconds) || absoluteSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(absoluteSeconds));
            if(dateSource!=null)return ScheduleOn(absoluteSeconds).EvaluateSeconds(absoluteSeconds);
            if(recoveryPlan!=null)return RecoveryApplies(absoluteSeconds)?recoveryPlan.Evaluate(absoluteSeconds):recoverySource.EvaluateSeconds(absoluteSeconds);
            // Reduce to the day before division to retain sub-minute precision even
            // at large game-calendar epochs. No frame-by-frame catch-up is needed.
            return EvaluateMinuteOfDay((absoluteSeconds % 86400) / 60);
        }
        public bool TryVisitWindow(double now,out double startsAt,out double endsAt)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if(dateSource!=null)return ScheduleOn(now).TryVisitWindow(now,out startsAt,out endsAt);
            if(recoveryPlan!=null && !RecoveryApplies(now))return recoverySource.TryVisitWindow(now,out startsAt,out endsAt);
            if (composedOutings != null)
            {
                double composedDay=Math.Floor(now/86400)*86400, local=now-composedDay;
                foreach(var outing in composedOutings)
                    if(outing.TryVisitWindow(local,out startsAt,out endsAt))
                    { startsAt+=composedDay; endsAt+=composedDay; return true; }
                startsAt=endsAt=0; return false;
            }
            if (itineraryVisits != null)
            {
                double itineraryDay=Math.Floor(now/86400)*86400, local=now-itineraryDay;
                foreach(var visit in itineraryVisits)
                    if(local>=visit.EnterAt && local<visit.LeaveAt)
                    { startsAt=itineraryDay+visit.EnterAt; endsAt=itineraryDay+visit.LeaveAt; return true; }
                startsAt=endsAt=0; return false;
            }
            var state=EvaluateSeconds(now);
            double day=Math.Floor(now/86400)*86400;
            startsAt=day+open*60; endsAt=day+close*60;
            return awayActivity==Activity.Visit && state.Activity==Activity.Visit;
        }
        public bool TryVisitWindowForBuilding(double now, int buildingKey, out double startsAt, out double endsAt)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            if(dateSource!=null)return ScheduleOn(now).TryVisitWindowForBuilding(now,buildingKey,out startsAt,out endsAt);
            if(recoveryPlan!=null && !RecoveryApplies(now))return recoverySource.TryVisitWindowForBuilding(now,buildingKey,out startsAt,out endsAt);
            double day = Math.Floor(now / 86400) * 86400;
            if (composedOutings != null)
            {
                double local=now-day;
                bool found=false; double selectedStart=0,selectedEnd=0; int selectedRank=int.MaxValue;
                foreach(var outing in composedOutings)
                {
                    foreach(var visit in outing.itineraryVisits)
                    {
                        if(visit.BuildingKey!=buildingKey) continue;
                        int rank=local>=visit.EnterAt && local<visit.LeaveAt ? 0 :
                            visit.EnterAt>local ? 1 : 2;
                        if(rank<selectedRank || rank==selectedRank &&
                            (rank==1 ? visit.EnterAt<selectedStart-day : visit.EnterAt>selectedStart-day))
                        { found=true; selectedRank=rank; selectedStart=day+visit.EnterAt; selectedEnd=day+visit.LeaveAt; }
                    }
                }
                startsAt=found ? selectedStart : 0; endsAt=found ? selectedEnd : 0; return found;
            }
            if (itineraryVisits != null)
            {
                bool found=false; double selectedStart=0,selectedEnd=0; int selectedRank=int.MaxValue;
                foreach (var visit in itineraryVisits)
                    if (visit.BuildingKey == buildingKey)
                    {
                        double local=now-day;
                        int rank=local>=visit.EnterAt && local<visit.LeaveAt ? 0 :
                            visit.EnterAt>local ? 1 : 2;
                        if(rank<selectedRank || rank==selectedRank &&
                            (rank==1 ? visit.EnterAt<selectedStart-day : visit.EnterAt>selectedStart-day))
                        { found=true; selectedRank=rank; selectedStart=day+visit.EnterAt; selectedEnd=day+visit.LeaveAt; }
                    }
                startsAt=found ? selectedStart : 0; endsAt=found ? selectedEnd : 0; return found;
            }
            startsAt = day + open * 60;
            endsAt = day + close * 60;
            return awayActivity == Activity.Visit && work == buildingKey;
        }
        public bool HasVisit { get { return dateSource!=null ? dateSchedule.HasVisit : composedOutings != null ? AnyComposedVisit() : itineraryVisits != null ? itineraryVisits.Length > 0 : awayActivity==Activity.Visit && work>=0; } }
        public List<ResidentVisitSnapshot> ScheduledVisits()
        {
            // Parameterless metadata describes the selected date. Consumers
            // looking across calendar boundaries must pass their query time.
            if(dateSource!=null)return dateSchedule.ScheduledVisits();
            var result=new List<ResidentVisitSnapshot>();
            if(composedOutings!=null)
                foreach(var outing in composedOutings) result.AddRange(outing.ScheduledVisits());
            else if(itineraryVisits!=null) result.AddRange(itineraryVisits);
            else if(awayActivity==Activity.Visit && work>=0)
                result.Add(new ResidentVisitSnapshot { BuildingKey=work,TargetBuildingKey=work,
                    Purpose=ResidentVisitPurpose.Social,EnterAt=open*60d,LeaveAt=close*60d });
            return result;
        }
        public bool ContainsVisitBuilding(int buildingKey)
        {
            if(dateSource!=null)return dateSchedule.ContainsVisitBuilding(buildingKey);
            if (composedOutings != null)
            {
                foreach(var outing in composedOutings)
                    if(outing.ContainsVisitBuilding(buildingKey)) return true;
                return false;
            }
            if (itineraryVisits != null)
            {
                foreach (var visit in itineraryVisits) if (visit.BuildingKey == buildingKey) return true;
                return false;
            }
            return HasVisit && work == buildingKey;
        }
        private bool AnyComposedVisit()
        { foreach(var outing in composedOutings) if(outing.HasVisit) return true; return false; }
        public ActivitySnapshot Evaluate(long absoluteMinute)
        {
            if (absoluteMinute < 0) throw new ArgumentOutOfRangeException(nameof(absoluteMinute));
            if(recoveryPlan!=null || dateSource!=null)return EvaluateSeconds(absoluteMinute*60d);
            return EvaluateMinuteOfDay(absoluteMinute % 1440);
        }
        private ActivitySnapshot EvaluateMinuteOfDay(double minute)
        {
            if (minute < wake || minute >= sleep) return Interior(Activity.Sleep, home);
            if (composedOutings != null)
            {
                double seconds=minute*60;
                foreach(var outing in composedOutings)
                    if(seconds>=outing.itineraryDepartureSeconds && seconds<outing.itineraryEndsAt)
                        return outing.EvaluateMinuteOfDay(minute);
                return Interior(Activity.Home,home);
            }
            if (itineraryJourney != null)
            {
                double seconds = minute * 60;
                if (seconds < itineraryDepartureSeconds || seconds >= itineraryEndsAt) return Interior(Activity.Home, home);
                var visit = itineraryJourney.Evaluate(seconds);
                if (visit.Phase == ResidentVisitPhase.Inside)
                    return new ActivitySnapshot(Activity.Visit, visit.BuildingKey, visit.Position, 0);
                bool walking=visit.Phase==ResidentVisitPhase.Walking||visit.Phase==ResidentVisitPhase.Leaving;
                return new ActivitySnapshot(Activity.Travel, -1, visit.Position, 0,walking?visit.Destination:visit.Position,
                    visit.Phase==ResidentVisitPhase.WaitingForOpening?(int?)visit.TargetBuildingKey:null);
            }
            if(pointOuting!=null)
            {
                double seconds=minute*60;
                if(seconds>=pointOuting.Departure && seconds<pointOuting.Return)
                    return pointOuting.Evaluate(seconds,home);
                return Interior(Activity.Home,home);
            }
            if (work < 0 || minute < open - commute || minute >= close + commute) return Interior(Activity.Home, home);
            if (minute >= open && minute < close) return Interior(awayActivity, work);
            double progress = minute < open ? (minute - (open - commute)) / (double)commute : 1 - (minute - close) / (double)commute;
            double along = progress * distances[distances.Length - 1];
            for (int i = 1; i < points.Length; i++)
            {
                double segment = distances[i] - distances[i - 1];
                if (segment <= 0 || along > distances[i]) continue;
                double fraction = (along - distances[i - 1]) / segment;
                var position = new RoutePoint(points[i - 1].X + (points[i].X - points[i - 1].X) * fraction,
                    points[i - 1].Z + (points[i].Z - points[i - 1].Z) * fraction);
                return new ActivitySnapshot(Activity.Travel, -1, position, progress,minute<open?points[points.Length-1]:points[0]);
            }
            return new ActivitySnapshot(Activity.Travel, -1, points[points.Length - 1], progress,minute<open?points[points.Length-1]:points[0]);
        }
        private static ActivitySnapshot Interior(Activity activity, int key) { return new ActivitySnapshot(activity, key, default(RoutePoint), 0); }
        private static RoutePoint[] CopyRoute(IEnumerable<RoutePoint> source)
        {
            if(source==null) throw new ArgumentNullException(nameof(source));
            var points=new List<RoutePoint>(source);
            if(points.Count<2) throw new ArgumentException("Point outing route needs at least two points.");
            foreach(var point in points) if(!Finite(point.X)||!Finite(point.Z)) throw new ArgumentException("Invalid point outing route.");
            if(Lengths(points.ToArray())[points.Count-1]<=0) throw new ArgumentException("Point outing route must have positive length.");
            return points.ToArray();
        }
        private static double[] Lengths(RoutePoint[] points)
        {
            var result=new double[points.Length];
            for(int i=1;i<points.Length;i++)
            {double x=points[i].X-points[i-1].X,z=points[i].Z-points[i-1].Z;result[i]=result[i-1]+Math.Sqrt(x*x+z*z);}
            return result;
        }
        private static bool Same(RoutePoint a,RoutePoint b)
        {return Math.Abs(a.X-b.X)<0.001 && Math.Abs(a.Z-b.Z)<0.001;}
        private static bool Finite(double number) { return !double.IsNaN(number) && !double.IsInfinity(number); }
        private static void ValidateHome(int homeKey, int wakeMinute, int sleepMinute)
        {
            if (homeKey < 0 || wakeMinute < 0 || sleepMinute >= 1440 || wakeMinute >= sleepMinute)
                throw new ArgumentException("Home and waking window must be valid.");
        }
    }
}
