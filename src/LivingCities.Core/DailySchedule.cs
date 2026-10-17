using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public struct RoutePoint
    {
        public readonly double X, Z;
        public RoutePoint(double x, double z) { X = x; Z = z; }
    }
    public enum Activity { Sleep, Home, Work, Travel, Visit }
    public struct ActivitySnapshot
    {
        public readonly Activity Activity;
        public readonly int BuildingKey;
        public readonly RoutePoint Position;
        public readonly double RouteProgress;
        public ActivitySnapshot(Activity activity, int buildingKey, RoutePoint position, double progress)
        { Activity = activity; BuildingKey = buildingKey; Position = position; RouteProgress = progress; }
    }
    public sealed class DailySchedule
    {
        private readonly int home, work, wake, sleep, open, close, commute;
        private readonly RoutePoint[] points;
        private readonly double[] distances;
        private Activity awayActivity = Activity.Work;
        private ResidentVisitJourney itineraryJourney;
        private ResidentVisitSnapshot[] itineraryVisits;
        private double itineraryDepartureSeconds;
        private double itineraryEndsAt;
        public double ReturnHomeAt(double daySeconds)
        { return Math.Floor(daySeconds/86400)*86400+(itineraryJourney!=null ? itineraryEndsAt : (close+commute)*60d); }
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
        {
            ValidateHome(homeKey, wakeMinute, sleepMinute);
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
            return schedule;
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
            schedule.awayActivity = Activity.Visit;
            return schedule;
        }
        public ActivitySnapshot EvaluateSeconds(double absoluteSeconds)
        {
            if (!Finite(absoluteSeconds) || absoluteSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(absoluteSeconds));
            // Reduce to the day before division to retain sub-minute precision even
            // at large game-calendar epochs. No frame-by-frame catch-up is needed.
            return EvaluateMinuteOfDay((absoluteSeconds % 86400) / 60);
        }
        public bool TryVisitWindow(double now,out double startsAt,out double endsAt)
        {
            var state=EvaluateSeconds(now);
            double day=Math.Floor(now/86400)*86400;
            startsAt=day+open*60; endsAt=day+close*60;
            return awayActivity==Activity.Visit && state.Activity==Activity.Visit;
        }
        public bool TryVisitWindowForBuilding(double now, int buildingKey, out double startsAt, out double endsAt)
        {
            if (!Finite(now) || now < 0) throw new ArgumentOutOfRangeException(nameof(now));
            double day = Math.Floor(now / 86400) * 86400;
            if (itineraryVisits != null)
            {
                foreach (var visit in itineraryVisits)
                    if (visit.BuildingKey == buildingKey)
                    {
                        startsAt = day + visit.EnterAt;
                        endsAt = day + visit.LeaveAt;
                        return true;
                    }
                startsAt = endsAt = 0;
                return false;
            }
            startsAt = day + open * 60;
            endsAt = day + close * 60;
            return awayActivity == Activity.Visit && work == buildingKey;
        }
        public bool HasVisit { get { return itineraryVisits != null ? itineraryVisits.Length > 0 : awayActivity==Activity.Visit && work>=0; } }
        public bool ContainsVisitBuilding(int buildingKey)
        {
            if (itineraryVisits != null)
            {
                foreach (var visit in itineraryVisits) if (visit.BuildingKey == buildingKey) return true;
                return false;
            }
            return HasVisit && work == buildingKey;
        }
        public ActivitySnapshot Evaluate(long absoluteMinute)
        {
            if (absoluteMinute < 0) throw new ArgumentOutOfRangeException(nameof(absoluteMinute));
            return EvaluateMinuteOfDay(absoluteMinute % 1440);
        }
        private ActivitySnapshot EvaluateMinuteOfDay(double minute)
        {
            if (minute < wake || minute >= sleep) return Interior(Activity.Sleep, home);
            if (itineraryJourney != null)
            {
                double seconds = minute * 60;
                if (seconds < itineraryDepartureSeconds || seconds >= itineraryEndsAt) return Interior(Activity.Home, home);
                var visit = itineraryJourney.Evaluate(seconds);
                if (visit.Phase == ResidentVisitPhase.Inside)
                    return new ActivitySnapshot(Activity.Visit, visit.BuildingKey, visit.Position, 0);
                return new ActivitySnapshot(Activity.Travel, -1, visit.Position, 0);
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
                return new ActivitySnapshot(Activity.Travel, -1, position, progress);
            }
            return new ActivitySnapshot(Activity.Travel, -1, points[points.Length - 1], progress);
        }
        private static ActivitySnapshot Interior(Activity activity, int key) { return new ActivitySnapshot(activity, key, default(RoutePoint), 0); }
        private static bool Finite(double number) { return !double.IsNaN(number) && !double.IsInfinity(number); }
        private static void ValidateHome(int homeKey, int wakeMinute, int sleepMinute)
        {
            if (homeKey < 0 || wakeMinute < 0 || sleepMinute >= 1440 || wakeMinute >= sleepMinute)
                throw new ArgumentException("Home and waking window must be valid.");
        }
    }
}
