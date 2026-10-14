using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class GuardPoint3 { public double X, Y, Z; }
    [Serializable]
    public sealed class GuardInteriorState
    {
        public int BuildingKey;
        public GuardPoint3 Entrance, Position, LastSeenSuspect;
        public List<GuardPoint3> Trail = new List<GuardPoint3>();
        public bool Returning;
        public double ReturnAt, ExitAt;
    }
    public sealed partial class GuardRoster
    {
        public void EnterBuilding(string id, int buildingKey, double now, GuardPoint3 entrance)
        {
            var landing = Point3(entrance);
            Require(Time(now), "Invalid entry time.");
            var preview = CopyValidated(this); preview.Advance(now);
            var candidate = preview.Find(id);
            var report = preview.Reports.Find(r => r.Id == candidate.IncidentId);
            Require(candidate.IsAlive && candidate.Duty == GuardDuty.Searching && candidate.Interior == null &&
                report != null && report.BuildingKey == buildingKey, "Guard has not arrived at this building.");
            double searchUntil = candidate.SearchUntil;
            Engage(id, now);
            var guard = Find(id);
            guard.SearchUntil = searchUntil;
            guard.Interior = new GuardInteriorState { BuildingKey = buildingKey, Entrance = landing,
                Position = Point3(landing), Trail = new List<GuardPoint3> { Point3(landing) } };
        }
        public void ObserveInterior(string id, GuardPoint3 position, double health, double now, bool sees, GuardPoint3 suspect)
        {
            var point = Point3(position); var seen = suspect == null ? null : Point3(suspect);
            Require(Time(now) && Time(now + 300) && Finite(health) && health > 0 && health <= 1 &&
                (!sees || seen != null), "Invalid indoor observation.");
            var guard = Find(id); var room = guard.Interior;
            Require(guard.IsAlive && guard.Duty == GuardDuty.Engaged && room != null && !room.Returning,
                "Guard is not searching this interior.");
            guard.HealthFraction = health; room.Position = point;
            if (Distance3(room.Trail[room.Trail.Count - 1], point) >= 0.25) room.Trail.Add(Point3(point));
            if (sees)
            {
                room.LastSeenSuspect = seen;
                double occupation = Reports.Exists(r => r.Id == guard.IncidentId && r.Investigation) ? guard.ArrivesAt + UnwitnessedScene.OccupationSeconds : 0;
                guard.SearchUntil = Math.Max(now + 300, occupation);
            }
        }
        public GuardPoint3 InteriorPosition(string id, double now)
        {
            Advance(now); var room = Find(id).Interior;
            if (room == null) return null;
            if (!room.Returning) return Point3(room.Position);
            double duration = room.ExitAt - room.ReturnAt;
            double fraction = duration <= 0 ? 1 : Math.Max(0, Math.Min(1, (now - room.ReturnAt) / duration));
            double along = TrailLength(room.Trail) * fraction;
            for (int i = 1; i < room.Trail.Count; i++)
            {
                var a = room.Trail[i - 1]; var b = room.Trail[i]; double distance = Distance3(a, b);
                if (distance <= 0) continue;
                if (along <= distance) { double t = along / distance; return new GuardPoint3 {
                    X = a.X + (b.X - a.X) * t, Y = a.Y + (b.Y - a.Y) * t, Z = a.Z + (b.Z - a.Z) * t }; }
                along -= distance;
            }
            return Point3(room.Entrance);
        }
        public void EndInteriorSearch(string id, double now)
        {
            Require(Time(now), "Invalid stand-down time.");
            var guard = Find(id);
            Require(guard.IsAlive && guard.Interior != null, "Guard is not inside a building.");
            if (guard.Interior.Returning) return;
            guard.SearchUntil = now;
            AdvanceInterior(guard, now);
        }
        private static void AdvanceInterior(GuardRecord guard, double now)
        {
            var room = guard.Interior;
            if (room == null) return;
            if (!room.Returning && now >= guard.SearchUntil)
            {
                if (Distance3(room.Trail[room.Trail.Count - 1], room.Position) > 0)
                    room.Trail.Add(Point3(room.Position));
                room.Trail.Reverse(); room.Returning = true; room.ReturnAt = guard.SearchUntil;
                room.ExitAt = room.ReturnAt + TrailLength(room.Trail) / 2.8 * 12;
            }
            if (!room.Returning || now < room.ExitAt) return;
            BeginReturn(guard, room.ExitAt);
            guard.Interior = null; guard.EngagementPosition = null; guard.LastSeenSuspect = null;
        }
        private static GuardInteriorState CopyInterior(GuardRecord guard)
        {
            var source = guard.Interior;
            if (source == null) return null;
            Require((guard.Duty == GuardDuty.Engaged || guard.Duty == GuardDuty.Dead) && source.BuildingKey >= 0 &&
                source.Trail != null && source.Trail.Count > 0 && Time(source.ReturnAt) && Time(source.ExitAt),
                "Invalid indoor guard state.");
            var copy = new GuardInteriorState { BuildingKey = source.BuildingKey, Entrance = Point3(source.Entrance),
                Position = Point3(source.Position), LastSeenSuspect = source.LastSeenSuspect == null ? null : Point3(source.LastSeenSuspect),
                Returning = source.Returning, ReturnAt = source.ReturnAt, ExitAt = source.ExitAt };
            foreach (var point in source.Trail) copy.Trail.Add(Point3(point));
            Require(source.Returning ? source.ReturnAt == guard.SearchUntil && source.ExitAt >= source.ReturnAt &&
                Math.Abs(source.ExitAt - source.ReturnAt - TrailLength(copy.Trail) / 2.8 * 12) < 0.001 &&
                Distance3(copy.Trail[copy.Trail.Count - 1], copy.Entrance) < 0.001 :
                source.ReturnAt == 0 && source.ExitAt == 0 && Distance3(copy.Trail[0], copy.Entrance) < 0.001,
                "Invalid indoor return route.");
            return copy;
        }
        private static GuardPoint3 Point3(GuardPoint3 point)
        {
            Require(point != null && Finite(point.X) && Finite(point.Y) && Finite(point.Z) &&
                Math.Abs(point.X) <= 1000000 && Math.Abs(point.Y) <= 1000000 && Math.Abs(point.Z) <= 1000000, "Invalid indoor point.");
            return new GuardPoint3 { X = point.X, Y = point.Y, Z = point.Z };
        }
        private static double Distance3(GuardPoint3 a, GuardPoint3 b)
        { double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z; return Math.Sqrt(x * x + y * y + z * z); }
        private static double TrailLength(List<GuardPoint3> trail)
        { double length = 0; for (int i = 1; i < trail.Count; i++) length += Distance3(trail[i - 1], trail[i]); return length; }
    }
}
