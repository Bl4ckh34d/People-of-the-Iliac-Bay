using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
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
        public double TimeScale = 12;
        public SavedRoutePoint ExteriorPursuit;
        public double ReturnAt, ExitAt;
        public double PursuitUntil;
    }
    public sealed partial class GuardRoster
    {
        public void EnterBuilding(string id, int buildingKey, double now, GuardPoint3 entrance)
        { EnterBuildingCore(id,buildingKey,now,entrance,false); }
        private void EnterBuildingCore(string id,int buildingKey,double now,GuardPoint3 entrance,bool witnessedDoor)
        {
            var landing = Point3(entrance);
            Require(Time(now), "Invalid entry time.");
            var preview = CopyValidated(this); preview.Advance(now);
            var candidate = preview.Find(id);
            var report = preview.Reports.Find(r => r.Id == candidate.IncidentId);
            Require(candidate.IsAlive && candidate.Duty == GuardDuty.Searching && candidate.Interior == null &&
                report != null && (candidate.DoorPursuit==null && report.BuildingKey == buildingKey || witnessedDoor && candidate.DoorPursuit!=null &&
                candidate.DoorPursuit.BuildingKey==buildingKey), "Guard has not arrived at this building.");
            double searchUntil = candidate.SearchUntil;
            Engage(id, now);
            var guard = Find(id);
            guard.SearchUntil = searchUntil;
            guard.Interior = new GuardInteriorState { BuildingKey = buildingKey, TimeScale = 2, Entrance = landing,
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
                room.PursuitUntil = now + 300;
                var report = Reports.Find(r => r.Id == guard.IncidentId);
                double occupation = report != null && report.OccupiesScene ?
                    (report.InvestigationUntil > 0 ? report.InvestigationUntil : guard.ArrivesAt + UnwitnessedScene.OccupationSeconds) : 0;
                guard.SearchUntil = guard.DoorPursuit!=null ? now+1800 : Math.Max(now + 300, occupation);
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
        public bool InteriorPursuitExpired(string id,double now)
        {
            Require(Time(now),"Invalid pursuit time.");
            var room=Find(id).Interior;
            return room!=null && !room.Returning && room.PursuitUntil>0 && now>=room.PursuitUntil;
        }
        public void FinishInteriorPursuit(string id,double now)
        {
            Require(InteriorPursuitExpired(id,now),"Interior pursuit has not expired.");
            var room=Find(id).Interior;
            room.PursuitUntil=0; room.LastSeenSuspect=null;
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
        public void PursueOutside(string id,double now)
        {
            Require(Time(now) && Time(now+300),"Invalid exterior pursuit time.");
            var guard=Find(id); var room=guard.Interior;
            var report=Reports.Find(r=>r.Id==guard.IncidentId);
            Require(guard.IsAlive && guard.Duty==GuardDuty.Engaged && room!=null && !room.Returning &&
                report!=null && report.ThreatId==null && report.BuildingKey==room.BuildingKey,
                "Guard cannot pursue outside from this state.");
            room.ExteriorPursuit=CopyPoint(report.Entrance);
            EndInteriorSearch(id,now);
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
                room.ExitAt = room.ReturnAt + TrailLength(room.Trail) / 2.8 * EffectiveTimeScale(room);
            }
            if (!room.Returning || now < room.ExitAt) return;
            if(room.ExteriorPursuit!=null)
            {
                guard.EngagementPosition=CopyPoint(room.ExteriorPursuit);
                guard.LastSeenSuspect=CopyPoint(room.ExteriorPursuit);
                guard.SearchUntil=room.ExitAt+300;
                guard.Interior=null;
                return;
            }
            BeginReturn(guard, room.ExitAt);
            guard.Interior = null; guard.EngagementPosition = null; guard.LastSeenSuspect = null;
        }
        private static GuardInteriorState CopyInterior(GuardRecord guard)
        {
            var source = guard.Interior;
            if (source == null) return null;
            Require((guard.Duty == GuardDuty.Engaged || guard.Duty == GuardDuty.Dead) && source.BuildingKey >= 0 &&
                source.Trail != null && source.Trail.Count > 0 && Time(source.ReturnAt) && Time(source.ExitAt) && Time(source.PursuitUntil) &&
                Finite(source.TimeScale) && source.TimeScale >= 0,
                "Invalid indoor guard state.");
            var copy = new GuardInteriorState { BuildingKey = source.BuildingKey, Entrance = Point3(source.Entrance),
                Position = Point3(source.Position), LastSeenSuspect = source.LastSeenSuspect == null ? null : Point3(source.LastSeenSuspect),
                Returning = source.Returning, TimeScale = source.TimeScale, ReturnAt = source.ReturnAt, ExitAt = source.ExitAt,PursuitUntil=source.PursuitUntil };
            Require(source.ExteriorPursuit==null || source.Returning && Time(source.ExitAt+300),"Invalid doorway pursuit.");
            copy.ExteriorPursuit=source.ExteriorPursuit==null ? null : CopyPoint(source.ExteriorPursuit);
            foreach (var point in source.Trail) copy.Trail.Add(Point3(point));
            Require(source.Returning ? source.ReturnAt == guard.SearchUntil && source.ExitAt >= source.ReturnAt &&
                (Math.Abs(source.ExitAt - source.ReturnAt - TrailLength(copy.Trail) / 2.8 * (source.TimeScale == 0 ? 12 : source.TimeScale)) < 0.001) &&
                Distance3(copy.Trail[copy.Trail.Count - 1], copy.Entrance) < 0.001 :
                source.ReturnAt == 0 && source.ExitAt == 0 && Distance3(copy.Trail[0], copy.Entrance) < 0.001,
                "Invalid indoor return route.");
            return copy;
        }
        private static double EffectiveTimeScale(GuardInteriorState room) { return room.TimeScale == 0 ? 12 : room.TimeScale; }
        internal static void MigrateInteriorClock(GuardRoster roster, double now, double previousRate, double currentRate)
        {
            foreach (var guard in roster.Guards)
            {
                var room = guard.Interior;
                if (room == null) continue;
                if (room.Returning)
                {
                    ClockInterval interval;
                    if (now >= room.ExitAt)
                        interval = new ClockInterval(room.ExitAt - (room.ExitAt - room.ReturnAt) * currentRate / previousRate,
                            room.ExitAt, now);
                    else interval = ClockRateMigration.RescaleInterval(room.ReturnAt, room.ExitAt, now, previousRate, currentRate);
                    room.ReturnAt = interval.Start; room.ExitAt = interval.End;
                    guard.SearchUntil = interval.Start;
                }
                room.TimeScale = currentRate;
            }
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
