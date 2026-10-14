using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    [Serializable]
    public sealed class CivilianEscape
    {
        public int BuildingKey;
        public double StartedAt;
        public double Speed;
        public List<GuardPoint3> InteriorRoute;
        public List<SavedRoutePoint> ExteriorRoute;
        // Routes are immutable after creation/validated restore. Avoid scanning or allocating
        // a long street route for every resident's alarm query on every frame.
        [NonSerialized] private double? insideLength, outsideLength;
        public static CivilianEscape Create(AlarmPerson person, double now, List<GuardPoint3> inside, List<SavedRoutePoint> outside, double speed = 2.8)
        {
            if (person == null || !person.HasPosition || person.BuildingKey < 0 || person.Reaction != CivilianReaction.Flee)
                throw new ArgumentException("Witness cannot flee this interior.");
            var result = Copy(new CivilianEscape { BuildingKey = person.BuildingKey, StartedAt = now, Speed = speed,
                InteriorRoute = inside, ExteriorRoute = outside });
            if (Distance(inside[0], new GuardPoint3 { X = person.X, Y = person.Y, Z = person.Z }) > 0.05)
                throw new ArgumentException("Escape must start at witness position.");
            return result;
        }
        // Zero is the additive-field default in historical saves; keep their timing.
        private double EffectiveSpeed { get { return Speed == 0 ? 2.8 : Speed; } }
        public double ExitAt { get { if (!insideLength.HasValue) insideLength = Length(InteriorRoute); return StartedAt + insideLength.Value / EffectiveSpeed * 12; } }
        public double ReportAt
        {
            get
            {
                if (!outsideLength.HasValue)
                {
                    double length = 0;
                    for (int i = 1; i < ExteriorRoute.Count; i++)
                    { double x = ExteriorRoute[i].X-ExteriorRoute[i-1].X, z = ExteriorRoute[i].Z-ExteriorRoute[i-1].Z; length += Math.Sqrt(x*x+z*z); }
                    outsideLength = length;
                }
                return ExitAt + outsideLength.Value / EffectiveSpeed * 12;
            }
        }
        public bool Arrived(double now) { return now >= ReportAt; }
        public void Advance(AlarmPerson person, double now)
        {
            if (now < StartedAt) return;
            bool inside = now < ExitAt;
            double distance = (now - (inside ? StartedAt : ExitAt)) / 12 * EffectiveSpeed;
            var point = inside ? Along(InteriorRoute, distance) : AlongExterior(distance);
            person.HasPosition = true; person.BuildingKey = inside ? BuildingKey : -1;
            person.X = point.X; person.Y = point.Y; person.Z = point.Z;
        }
        public static CivilianEscape Copy(CivilianEscape source)
        {
            if (source == null) return null;
            if (source.BuildingKey < 0 || !Finite(source.StartedAt) || source.StartedAt < 0 || !Finite(source.Speed) || source.Speed < 0 || source.Speed > 20 ||
                source.InteriorRoute == null || source.InteriorRoute.Count < 2 || source.InteriorRoute.Count > 1000 ||
                source.ExteriorRoute == null || source.ExteriorRoute.Count < 2 || source.ExteriorRoute.Count > 10000)
                throw new ArgumentException("Invalid witness escape plan.");
            var result = new CivilianEscape { BuildingKey = source.BuildingKey, StartedAt = source.StartedAt, Speed = source.Speed,
                InteriorRoute = new List<GuardPoint3>(), ExteriorRoute = new List<SavedRoutePoint>() };
            foreach (var p in source.InteriorRoute)
            {
                if (p == null || !Coordinate(p.X) || !Coordinate(p.Y) || !Coordinate(p.Z)) throw new ArgumentException("Invalid escape point.");
                result.InteriorRoute.Add(new GuardPoint3 { X = p.X, Y = p.Y, Z = p.Z });
            }
            foreach (var p in source.ExteriorRoute)
            {
                if (p == null || !Coordinate(p.X) || !Coordinate(p.Z)) throw new ArgumentException("Invalid escape point.");
                result.ExteriorRoute.Add(new SavedRoutePoint { X = p.X, Z = p.Z });
            }
            if (!Finite(result.ReportAt)) throw new ArgumentException("Invalid escape duration.");
            return result;
        }
        private GuardPoint3 AlongExterior(double distance)
        {
            for (int i = 1; i < ExteriorRoute.Count; i++)
            {
                var a = ExteriorRoute[i-1]; var b = ExteriorRoute[i]; double x = b.X-a.X, z = b.Z-a.Z;
                double segment = Math.Sqrt(x*x+z*z);
                if (segment > 0 && distance <= segment)
                { double t = Math.Max(0, distance / segment); return new GuardPoint3 { X = a.X+x*t, Z = a.Z+z*t }; }
                distance -= segment;
            }
            var end = ExteriorRoute[ExteriorRoute.Count-1]; return new GuardPoint3 { X = end.X, Z = end.Z };
        }
        private static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        private static bool Coordinate(double n) { return Finite(n) && Math.Abs(n) <= 1000000; }
        private static double Distance(GuardPoint3 a, GuardPoint3 b)
        { double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return Math.Sqrt(x*x + y*y + z*z); }
        private static double Length(List<GuardPoint3> points)
        { double length = 0; for (int i = 1; i < points.Count; i++) length += Distance(points[i-1], points[i]); return length; }
        private static GuardPoint3 Along(List<GuardPoint3> points, double distance)
        {
            for (int i = 1; i < points.Count; i++)
            {
                var a = points[i-1]; var b = points[i]; double segment = Distance(a,b);
                if (segment > 0 && distance <= segment)
                {
                    double t = Math.Max(0, distance / segment);
                    return new GuardPoint3 { X = a.X + (b.X-a.X)*t, Y = a.Y + (b.Y-a.Y)*t, Z = a.Z + (b.Z-a.Z)*t };
                }
                distance -= segment;
            }
            return points[points.Count-1];
        }
    }
    public sealed partial class WorldStateStore
    {
        public List<CivilianIncident> AdvanceEscapes(int settlementId, double now)
        {
            Require(ValidTime(now), "Invalid witness time.");
            var reports = new List<CivilianIncident>();
            foreach (var alarm in state.AlarmIncidents)
            {
                if (alarm.SettlementId != settlementId || !Active(alarm, now)) continue;
                bool ready = false;
                foreach (var person in alarm.People)
                {
                    if (person.Escape == null || person.Reaction != CivilianReaction.Flee ||
                        !AlarmResident(settlementId, person.ResidentId).IsAlive) continue;
                    person.Escape.Advance(person, now);
                    if ((person.DirectWitness || person.HeardViolence) && person.Escape.Arrived(now)) ready = true;
                }
                if (ready) reports.Add(alarm);
            }
            return reports;
        }
    }
}
