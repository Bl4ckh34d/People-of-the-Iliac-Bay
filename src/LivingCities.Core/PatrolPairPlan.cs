using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class PatrolPairPlan
    {
        public string LeaderId, FollowerId;
        public List<SavedRoutePoint> LeaderJoin, FollowerJoin, Circuit;
        public double StartsAt, EndsAt, Speed, TimeScale, Spacing;
        public double CircuitStartsAt { get { return StartsAt + Math.Max(Length(LeaderJoin), Length(FollowerJoin)) / Speed * TimeScale; } }
        public static PatrolPairPlan Create(string leader, string follower, List<SavedRoutePoint> leaderJoin,
            List<SavedRoutePoint> followerJoin, List<SavedRoutePoint> circuit,
            double start, double end, double speed, double timeScale, double spacing)
        {
            return CopyValidated(new PatrolPairPlan { LeaderId = leader, FollowerId = follower,
                LeaderJoin = leaderJoin, FollowerJoin = followerJoin, Circuit = circuit,
                StartsAt = start, EndsAt = end, Speed = speed, TimeScale = timeScale, Spacing = spacing });
        }
        public static PatrolPairPlan CopyValidated(PatrolPairPlan source)
        {
            Require(source != null && !string.IsNullOrWhiteSpace(source.LeaderId) &&
                !string.IsNullOrWhiteSpace(source.FollowerId) && source.LeaderId != source.FollowerId,
                "A patrol needs two distinct guard identities.");
            Require(Finite(source.StartsAt) && source.StartsAt >= 0 && Finite(source.EndsAt) &&
                Finite(source.Speed) && source.Speed > 0 && Finite(source.TimeScale) && source.TimeScale > 0 &&
                Finite(source.Spacing) && source.Spacing >= 1.5 && Finite(source.Speed / source.TimeScale) &&
                source.Speed / source.TimeScale > 0, "Invalid patrol timing or spacing.");
            var copy = new PatrolPairPlan { LeaderId = source.LeaderId, FollowerId = source.FollowerId,
                LeaderJoin = CopyRoute(source.LeaderJoin), FollowerJoin = CopyRoute(source.FollowerJoin),
                Circuit = CopyRoute(source.Circuit), StartsAt = source.StartsAt, EndsAt = source.EndsAt,
                Speed = source.Speed, TimeScale = source.TimeScale, Spacing = source.Spacing };
            Require(copy.Circuit.Count >= 4 && copy.Circuit.Count <= 512 && Length(copy.Circuit) > 4 * copy.Spacing &&
                Distance(copy.Circuit[0], copy.Circuit[copy.Circuit.Count - 1]) < 0.000001,
                "Patrol circuit must close and allow pair spacing.");
            Require(Distance(copy.LeaderJoin[copy.LeaderJoin.Count - 1], copy.Circuit[0]) < 0.000001 &&
                Distance(copy.FollowerJoin[copy.FollowerJoin.Count - 1], At(copy.Circuit, copy.Spacing)) < 0.000001,
                "Patrol joins must reach each member's circuit entry.");
            Require(Finite(copy.CircuitStartsAt) && copy.EndsAt > copy.CircuitStartsAt &&
                Finite((copy.EndsAt - copy.StartsAt) * (copy.Speed / copy.TimeScale)) &&
                Finite(copy.EndsAt + (Length(copy.Circuit) + Math.Max(Length(copy.LeaderJoin), Length(copy.FollowerJoin))) / copy.Speed * copy.TimeScale),
                "Patrol cannot finish before joining or overflow return time.");
            copy.ValidateFormation();
            return copy;
        }
        public void MigrateClockRate(double now, double previousRate, double currentRate)
        {
            if (TimeScale == currentRate) return;
            Require(Finite(now) && now >= 0 && Finite(previousRate) && previousRate > 0 &&
                Finite(currentRate) && currentRate > 0, "Invalid patrol clock migration.");
            double ratio = currentRate / previousRate;
            double oldCircuitStart = CircuitStartsAt;
            double joinLength = Math.Max(Length(LeaderJoin), Length(FollowerJoin));
            if (now < StartsAt)
            {
                // The scheduled departure is calendar intent; only the travel rate changes.
            }
            else if (now < EndsAt && now < oldCircuitStart)
                StartsAt = now - (now - StartsAt) * ratio;
            else if (now < EndsAt)
            {
                double newCircuitStart = now - (now - oldCircuitStart) * ratio;
                StartsAt = newCircuitStart - joinLength * currentRate / Speed;
            }
            else
            {
                double newEnd = now - (now - EndsAt) * ratio;
                double newCircuitStart = newEnd - (EndsAt - oldCircuitStart) * ratio;
                StartsAt = newCircuitStart - joinLength * currentRate / Speed;
                EndsAt = newEnd;
            }
            TimeScale = currentRate;
        }
        private void ValidateFormation()
        {
            // Between corner events, both positions are linear. Minimize their
            // relative squared distance over each interval, including its interior.
            double length = Length(Circuit), along = 0;
            var events = new List<double> { 0, length };
            for (int i = 0; i < Circuit.Count - 1; i++)
            {
                if (i > 0) along += Distance(Circuit[i - 1], Circuit[i]);
                events.Add(along); events.Add((along - Spacing + length) % length);
            }
            events.Sort();
            for (int i = 1; i < events.Count; i++)
            {
                double from = events[i - 1], to = events[i];
                if (to - from < 0.000000001) continue;
                var a = At(Circuit, from); var b = At(Circuit, (from + Spacing) % length);
                var c = At(Circuit, to); var d = At(Circuit, (to + Spacing) % length);
                double x = a.X - b.X, z = a.Z - b.Z;
                double dx = c.X - d.X - x, dz = c.Z - d.Z - z;
                double change = dx * dx + dz * dz;
                double t = change <= 0 ? 0 : Math.Max(0, Math.Min(1, -(x * dx + z * dz) / change));
                x += dx * t; z += dz * t;
                Require(x * x + z * z >= 1 - 0.000001, "Patrol formation crosses itself or leaves less than one metre of separation.");
            }
        }
        public SavedRoutePoint Position(string guardId, double now)
        {
            var join = Join(guardId); CheckTime(now);
            if (now < CircuitStartsAt) return At(join, Math.Max(0, now - StartsAt) * Speed / TimeScale);
            if (now < EndsAt) return At(MemberCircuit(guardId), CircuitDistance(now));
            return At(ReturnAt(guardId, EndsAt), (now - EndsAt) * Speed / TimeScale);
        }
        public List<SavedRoutePoint> ReturnRoute(string guardId, double now)
        {
            Join(guardId); CheckTime(now);
            var route = ReturnAt(guardId, Math.Min(now, EndsAt));
            return now <= EndsAt ? route : Suffix(route, (now - EndsAt) * Speed / TimeScale);
        }
        private List<SavedRoutePoint> ReturnAt(string guardId, double now)
        {
            var join = Join(guardId);
            if (now < CircuitStartsAt) return ReversePrefix(join, Math.Max(0, now - StartsAt) * Speed / TimeScale);
            var path = ReversePrefix(MemberCircuit(guardId), CircuitDistance(now));
            for (int i = join.Count - 2; i >= 0; i--) Add(path, join[i]);
            EnsureTwo(path); return path;
        }
        private double CircuitDistance(double now)
        { return Math.Max(0, now - CircuitStartsAt) * (Speed / TimeScale) % Length(Circuit); }
        private List<SavedRoutePoint> Join(string guardId)
        {
            Require(guardId == LeaderId || guardId == FollowerId, "Guard does not belong to this patrol.");
            return guardId == LeaderId ? LeaderJoin : FollowerJoin;
        }
        private List<SavedRoutePoint> MemberCircuit(string guardId)
        {
            if (guardId == LeaderId) return Circuit;
            var result = Suffix(Circuit, Spacing);
            double walked = 0;
            for (int i = 1; i < Circuit.Count; i++)
            {
                walked += Distance(Circuit[i - 1], Circuit[i]);
                if (walked >= Spacing) break;
                Add(result, Circuit[i]);
            }
            Add(result, At(Circuit, Spacing)); return result;
        }
        private static List<SavedRoutePoint> ReversePrefix(List<SavedRoutePoint> route, double along)
        {
            along = Math.Min(along, Length(route));
            var result = new List<SavedRoutePoint> { At(route, along) };
            double walked = 0; int last = 0;
            for (int i = 1; i < route.Count; i++)
            {
                walked += Distance(route[i - 1], route[i]);
                if (walked > along) break;
                last = i;
            }
            for (int i = last; i >= 0; i--) Add(result, route[i]);
            EnsureTwo(result); return result;
        }
        private static List<SavedRoutePoint> Suffix(List<SavedRoutePoint> route, double along)
        {
            var result = new List<SavedRoutePoint> { At(route, along) }; double walked = 0;
            for (int i = 1; i < route.Count; i++)
            {
                walked += Distance(route[i - 1], route[i]);
                if (walked > along) Add(result, route[i]);
            }
            EnsureTwo(result); return result;
        }
        private static SavedRoutePoint At(List<SavedRoutePoint> route, double along)
        {
            if (along <= 0) return Copy(route[0]);
            for (int i = 1; i < route.Count; i++)
            {
                double distance = Distance(route[i - 1], route[i]);
                if (distance <= 0) continue;
                if (along <= distance) return new SavedRoutePoint {
                    X = route[i - 1].X + (route[i].X - route[i - 1].X) * along / distance,
                    Z = route[i - 1].Z + (route[i].Z - route[i - 1].Z) * along / distance };
                along -= distance;
            }
            return Copy(route[route.Count - 1]);
        }
        private static void Add(List<SavedRoutePoint> route, SavedRoutePoint point)
        { if (Distance(route[route.Count - 1], point) > 0.000001) route.Add(Copy(point)); }
        private static void EnsureTwo(List<SavedRoutePoint> route)
        { if (route.Count == 1) route.Add(Copy(route[0])); }
        private static double Length(List<SavedRoutePoint> route)
        { double length = 0; for (int i = 1; i < route.Count; i++) length += Distance(route[i - 1], route[i]); return length; }
        private static double Distance(SavedRoutePoint a, SavedRoutePoint b)
        { double x = a.X - b.X, z = a.Z - b.Z; return Math.Sqrt(x * x + z * z); }
        private static SavedRoutePoint Copy(SavedRoutePoint p)
        {
            Require(p != null && Finite(p.X) && Finite(p.Z) && Math.Abs(p.X) <= 1000000 && Math.Abs(p.Z) <= 1000000,
                "Invalid patrol point.");
            return new SavedRoutePoint { X = p.X, Z = p.Z };
        }
        private static List<SavedRoutePoint> CopyRoute(List<SavedRoutePoint> route)
        {
            Require(route != null && route.Count >= 2 && route.Count <= 10000, "Invalid patrol route.");
            var copy = new List<SavedRoutePoint>(); foreach (var p in route) copy.Add(Copy(p)); return copy;
        }
        private void CheckTime(double now)
        { Require(Finite(now) && now >= 0 && Finite(Math.Max(0, now - StartsAt) * (Speed / TimeScale)), "Invalid patrol time."); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static void Require(bool condition, string message) { if (!condition) throw new ArgumentException(message); }
    }
}
