using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public enum RefugePhase { Approaching, Entering, Sheltered, Exiting, Returning, Complete, WallApproaching, WallPosted, WallReturning }
    public struct RefugeSnapshot { public RefugePhase Phase; public GuardPoint3 Position; }
    [Serializable]
    public sealed class GuardRefugeJourney
    {
        public int BuildingKey;
        public List<GuardPoint3> Exterior, Interior;
        public List<GuardPoint3> ReturnExterior;
        public List<GuardPoint3> WallRoute;
        public int LowerDoorIndex, UpperDoorIndex;
        public double StartsAt, SecondsPerMetre, TimeScale = 12;
        public double? ReleasedAt;
        public bool WallDuty;
        public bool Resting;
        public bool RestPending { get { return Resting && Interior==null; } }
        public bool HauntingShelter;
        public int? RestBedRecord;
        public bool ConflictsWith(int buildingKey, int? bedRecord)
        {
            return !RestPending && !(WallRoute != null && bedRecord.HasValue) && BuildingKey == buildingKey &&
                (!RestBedRecord.HasValue || !bedRecord.HasValue || RestBedRecord == bedRecord);
        }
        public static GuardRefugeJourney Create(int building, IEnumerable<GuardPoint3> exterior,
            IEnumerable<GuardPoint3> interior, double now, double speed, double timeScale)
        {
            Require(Finite(speed) && speed > 0 && Finite(timeScale) && timeScale > 0);
            return CopyValidated(new GuardRefugeJourney { BuildingKey=building, Exterior=CopyRoute(exterior),
                Interior=interior==null ? null : CopyRoute(interior), WallDuty=interior==null, Resting=interior==null, StartsAt=now, SecondsPerMetre=timeScale/speed, TimeScale=timeScale });
        }
        public static GuardRefugeJourney CopyValidated(GuardRefugeJourney source)
        {
            Require(source != null && source.BuildingKey >= 0 && Time(source.StartsAt) &&
                Finite(source.SecondsPerMetre) && source.SecondsPerMetre > 0 &&
                Finite(source.TimeScale) && source.TimeScale >= 0);
            var copy = new GuardRefugeJourney { BuildingKey=source.BuildingKey, StartsAt=source.StartsAt,
                SecondsPerMetre=source.SecondsPerMetre, TimeScale=source.TimeScale, ReleasedAt=source.ReleasedAt, WallDuty=source.WallDuty,
                HauntingShelter=source.HauntingShelter, RestBedRecord=source.RestBedRecord, Resting=source.Resting,
                Exterior=CopyRoute(source.Exterior), Interior=source.Interior==null ? null : CopyRoute(source.Interior),
                WallRoute=source.WallRoute==null ? null : CopyRoute(source.WallRoute),
                LowerDoorIndex=source.LowerDoorIndex, UpperDoorIndex=source.UpperDoorIndex,
                ReturnExterior=source.ReturnExterior==null ? null : CopyRoute(source.ReturnExterior) };
            Require(copy.Interior!=null || copy.Resting && copy.WallDuty && copy.WallRoute==null && !copy.RestBedRecord.HasValue);
            Require(!copy.Resting || copy.WallDuty && copy.WallRoute==null);
            Require(copy.WallRoute==null || copy.WallDuty && !copy.RestBedRecord.HasValue &&
                copy.LowerDoorIndex>=0 && copy.UpperDoorIndex>=0 && copy.LowerDoorIndex!=copy.UpperDoorIndex);
            Require(copy.ReturnExterior==null || Distance(copy.ReturnExterior[0],copy.Exterior[copy.Exterior.Count-1])<0.05);
            Require(!copy.RestBedRecord.HasValue || copy.WallDuty && copy.RestBedRecord.Value>=0);
            Require(Time(copy.ShelteredAt));
            if (copy.ReleasedAt.HasValue)
                Require(Time(copy.ReleasedAt.Value) && copy.ReleasedAt.Value >= copy.ShelteredAt &&
                    Time(copy.ReleasedAt.Value + copy.ReturnSeconds));
            return copy;
        }
        /// <summary>Resolve an unobserved rest only after its quarters are loaded. Exterior approach remains physical.</summary>
        /// <returns>False when the destination cannot preserve the existing return deadline; the coarse rest remains unchanged.</returns>
        public bool MaterializeRest(IEnumerable<GuardPoint3> route,int? bedRecord,double now,double returnBy)
        {
            Require(RestPending && Time(now) && (!bedRecord.HasValue || bedRecord.Value>=0));
            var phase=Evaluate(now).Phase;
            Require(phase==RefugePhase.Approaching || phase==RefugePhase.Sheltered);
            var candidate=CopyValidated(this);
            candidate.Interior=CopyRoute(route); candidate.RestBedRecord=bedRecord;
            // A player entering after the guard is already inside sees its rest destination.
            // A guard still outdoors keeps the original departure and walks the observed indoor route.
            if(phase==RefugePhase.Sheltered) candidate.StartsAt=now-candidate.TravelSeconds;
            if(!Time(candidate.StartsAt))return false;
            candidate.ReleasedAt=null;
            if(!candidate.HauntingShelter && !candidate.ReleaseToReturnBy(returnBy))return false;
            candidate=CopyValidated(candidate);
            Interior=candidate.Interior; RestBedRecord=candidate.RestBedRecord;
            StartsAt=candidate.StartsAt; ReleasedAt=candidate.ReleasedAt;
            return true;
        }
        private double TravelSeconds { get { return (Length(Exterior)+Length(Interior)+(WallRoute==null ? 0 : Length(WallRoute)))*SecondsPerMetre; } }
        private double ReturnSeconds { get { return (Length(ReturnExterior ?? Exterior)+Length(Interior)+(WallRoute==null ? 0 : Length(WallRoute)))*SecondsPerMetre; } }
        private double ShelteredAt { get { return StartsAt+TravelSeconds; } }
        public bool ReleaseToReturnBy(double arrival)
        {
            Require(Time(arrival));
            if ((!Resting && !RestBedRecord.HasValue && WallRoute==null) || ReleasedAt.HasValue || arrival <= ShelteredAt + ReturnSeconds) return false;
            Release(arrival - ReturnSeconds);
            return true;
        }
        public void Release(double now)
        {
            Require(Time(now));
            if (ReleasedAt.HasValue) return;
            double release = Math.Max(now,ShelteredAt);
            // Finish the current indoor circuit before returning along the entry
            // route. This avoids a discontinuous jump from a patrol point at dawn.
            if(WallDuty && !Resting && !RestBedRecord.HasValue && WallRoute==null)
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
                if (WallRoute != null)
                {
                    double wall = Length(WallRoute);
                    if (travelled < wall) return Snapshot(RefugePhase.WallReturning,WallRoute,wall-travelled);
                    travelled -= wall;
                }
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
            if (WallRoute != null)
            {
                double wall = Length(WallRoute);
                return Snapshot(along < outside+inside+wall ? RefugePhase.WallApproaching : RefugePhase.WallPosted,
                    WallRoute,Math.Min(wall,along-outside-inside));
            }
            if(WallDuty && !Resting && !RestBedRecord.HasValue)
            {
                double span=Math.Min(6,inside*0.5);
                double cycle=(along-outside-inside)%(2*span);
                return Snapshot(RefugePhase.Sheltered,Interior,inside-(cycle<=span ? cycle : 2*span-cycle));
            }
            return Interior==null ? new RefugeSnapshot {Phase=RefugePhase.Sheltered} : Snapshot(RefugePhase.Sheltered,Interior,inside);
        }
        public bool IsUpperExterior(double now)
        {
            var phase = Evaluate(now).Phase;
            return phase == RefugePhase.WallApproaching || phase == RefugePhase.WallPosted || phase == RefugePhase.WallReturning;
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
        { if(route==null)return 0; double length=0; for(int i=1;i<route.Count;i++) length+=Distance(route[i-1],route[i]); return length; }
        private static double Distance(GuardPoint3 a,GuardPoint3 b)
        { double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z; return Math.Sqrt(x*x+y*y+z*z); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        private static bool Time(double value) { return Finite(value) && value>=0; }
        private static void Require(bool valid) { if(!valid) throw new ArgumentException("Invalid guard refuge journey."); }
    }
}
