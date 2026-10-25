using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public enum RefugePhase { Approaching, Entering, Sheltered, Exiting, Returning, Complete }
    public struct RefugeSnapshot { public RefugePhase Phase; public GuardPoint3 Position; }
    [Serializable]
    public sealed class GuardRefugeJourney
    {
        public int BuildingKey;
        public List<GuardPoint3> Exterior, Interior;
        public List<GuardPoint3> ReturnExterior;
        public double StartsAt, SecondsPerMetre, TimeScale = 12;
        public double? ReleasedAt;
        public bool WallDuty;
        public int? RestBedRecord;
        public bool ConflictsWith(int buildingKey, int? bedRecord)
        {
            return BuildingKey == buildingKey &&
                (!RestBedRecord.HasValue || !bedRecord.HasValue || RestBedRecord == bedRecord);
        }
        public static GuardRefugeJourney Create(int building, IEnumerable<GuardPoint3> exterior,
            IEnumerable<GuardPoint3> interior, double now, double speed, double timeScale)
        {
            Require(Finite(speed) && speed > 0 && Finite(timeScale) && timeScale > 0);
            return CopyValidated(new GuardRefugeJourney { BuildingKey=building, Exterior=CopyRoute(exterior),
                Interior=CopyRoute(interior), StartsAt=now, SecondsPerMetre=timeScale/speed, TimeScale=timeScale });
        }
        public static GuardRefugeJourney CopyValidated(GuardRefugeJourney source)
        {
            Require(source != null && source.BuildingKey >= 0 && Time(source.StartsAt) &&
                Finite(source.SecondsPerMetre) && source.SecondsPerMetre > 0 &&
                Finite(source.TimeScale) && source.TimeScale >= 0);
            var copy = new GuardRefugeJourney { BuildingKey=source.BuildingKey, StartsAt=source.StartsAt,
                SecondsPerMetre=source.SecondsPerMetre, TimeScale=source.TimeScale, ReleasedAt=source.ReleasedAt, WallDuty=source.WallDuty, RestBedRecord=source.RestBedRecord,
                Exterior=CopyRoute(source.Exterior), Interior=CopyRoute(source.Interior),
                ReturnExterior=source.ReturnExterior==null ? null : CopyRoute(source.ReturnExterior) };
            Require(copy.ReturnExterior==null || Distance(copy.ReturnExterior[0],copy.Exterior[copy.Exterior.Count-1])<0.05);
            Require(!copy.RestBedRecord.HasValue || copy.WallDuty && copy.RestBedRecord.Value>=0);
            Require(Time(copy.ShelteredAt));
            if (copy.ReleasedAt.HasValue)
                Require(Time(copy.ReleasedAt.Value) && copy.ReleasedAt.Value >= copy.ShelteredAt &&
                    Time(copy.ReleasedAt.Value + copy.ReturnSeconds));
            return copy;
        }
        public void MigrateClockRate(double now, double previousRate, double currentRate)
        {
            if (TimeScale == currentRate) return;
            Require(Time(now) && Finite(previousRate) && previousRate > 0 && Finite(currentRate) && currentRate > 0);
            double oldShelteredAt = ShelteredAt;
            double ratio = currentRate / previousRate;
            if (now < StartsAt) { /* Future departure remains on its calendar schedule. */ }
            else if (now < oldShelteredAt || WallDuty && !RestBedRecord.HasValue &&
                (!ReleasedAt.HasValue || ReleasedAt.Value > now))
                StartsAt = now - (now - StartsAt) * ratio;
            if (ReleasedAt.HasValue && ReleasedAt.Value <= now)
                ReleasedAt = now - (now - ReleasedAt.Value) * ratio;
            SecondsPerMetre *= ratio;
            TimeScale = currentRate;
        }
        private double TravelSeconds { get { return (Length(Exterior)+Length(Interior))*SecondsPerMetre; } }
        private double ReturnSeconds { get { return (Length(ReturnExterior ?? Exterior)+Length(Interior))*SecondsPerMetre; } }
        private double ShelteredAt { get { return StartsAt+TravelSeconds; } }
        public void Release(double now)
        {
            Require(Time(now));
            if (ReleasedAt.HasValue) return;
            double release = Math.Max(now,ShelteredAt);
            // Finish the current indoor circuit before returning along the entry
            // route. This avoids a discontinuous jump from a patrol point at dawn.
            if(WallDuty && !RestBedRecord.HasValue)
            {
                double cycle=2*Math.Min(6,Length(Interior)*0.5)*SecondsPerMetre;
                release=ShelteredAt+Math.Ceiling((release-ShelteredAt)/cycle)*cycle;
            }
            Require(Time(release+ReturnSeconds));
            ReleasedAt=release;
        }
        public RefugeSnapshot Evaluate(double now)
        {
            Require(Time(now));
            double outside=Length(Exterior), inside=Length(Interior);
            if (ReleasedAt.HasValue && now >= ReleasedAt.Value)
            {
                double travelled=(now-ReleasedAt.Value)/SecondsPerMetre;
                if (travelled < inside) return Snapshot(RefugePhase.Exiting,Interior,inside-travelled);
                if (ReturnExterior!=null)
                {
                    double length=Length(ReturnExterior);
                    return Snapshot(travelled<inside+length ? RefugePhase.Returning : RefugePhase.Complete,
                        ReturnExterior,Math.Min(length,travelled-inside));
                }
                if (travelled < inside+outside) return Snapshot(RefugePhase.Returning,Exterior,outside-(travelled-inside));
                return Snapshot(RefugePhase.Complete,Exterior,0);
            }
            double along=Math.Max(0,now-StartsAt)/SecondsPerMetre;
            if (along < outside) return Snapshot(RefugePhase.Approaching,Exterior,along);
            if (along < outside+inside) return Snapshot(RefugePhase.Entering,Interior,along-outside);
            if(WallDuty && !RestBedRecord.HasValue)
            {
                double span=Math.Min(6,inside*0.5);
                double cycle=(along-outside-inside)%(2*span);
                return Snapshot(RefugePhase.Sheltered,Interior,inside-(cycle<=span ? cycle : 2*span-cycle));
            }
            return Snapshot(RefugePhase.Sheltered,Interior,inside);
        }
        // Exterior and interior coordinates deliberately use their own scene origins.
        // The phase change is the explicit doorway transition, never an interpolated gap.
        private static RefugeSnapshot Snapshot(RefugePhase phase,List<GuardPoint3> route,double along)
        {
            for (int i=1;i<route.Count;i++)
            {
                double length=Distance(route[i-1],route[i]);
                if (length <= 0) continue;
                if (along <= length)
                {
                    double t=along/length; var a=route[i-1]; var b=route[i];
                    return new RefugeSnapshot { Phase=phase, Position=new GuardPoint3 {
                        X=a.X+(b.X-a.X)*t, Y=a.Y+(b.Y-a.Y)*t, Z=a.Z+(b.Z-a.Z)*t } };
                }
                along-=length;
            }
            return new RefugeSnapshot { Phase=phase,Position=Point(route[route.Count-1]) };
        }
        private static List<GuardPoint3> CopyRoute(IEnumerable<GuardPoint3> source)
        {
            Require(source != null); var route=new List<GuardPoint3>();
            foreach (var point in source) { Require(route.Count < 10000); route.Add(Point(point)); }
            Require(route.Count >= 2 && Finite(Length(route)) && Length(route)>0); return route;
        }
        private static GuardPoint3 Point(GuardPoint3 p)
        { Require(p != null && Finite(p.X) && Finite(p.Y) && Finite(p.Z)); return new GuardPoint3 { X=p.X,Y=p.Y,Z=p.Z }; }
        private static double Length(List<GuardPoint3> route)
        { double length=0; for(int i=1;i<route.Count;i++) length+=Distance(route[i-1],route[i]); return length; }
        private static double Distance(GuardPoint3 a,GuardPoint3 b)
        { double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z; return Math.Sqrt(x*x+y*y+z*z); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Time(double value) { return Finite(value) && value>=0; }
        private static void Require(bool valid) { if(!valid) throw new ArgumentException("Invalid guard refuge journey."); }
    }
}
