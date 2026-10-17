using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    // A cancelled visit remains cancelled across scene changes and save/load.
    // This is movement only: it never creates a crime or a witness report.
    [Serializable]
    public sealed class VisitDeparture
    {
        public string ResidentId;
        public int SettlementId, HomeKey;
        public double Until;
        public CivilianEscape Motion;

        public static VisitDeparture Create(string residentId,int settlementId,int homeKey,int buildingKey,
            double now,double until,List<GuardPoint3> inside,List<SavedRoutePoint> homeRoute,double speed,double timeScale)
        {
            if(inside==null || inside.Count<2 || inside[0]==null || double.IsNaN(timeScale) ||
                double.IsInfinity(timeScale) || timeScale<=0 || double.IsNaN(speed) || double.IsInfinity(speed) || speed<=0)
                throw new ArgumentException("Invalid visitor departure route or speed.");
            var start=inside[0];
            var person=new AlarmPerson { HasPosition=true,BuildingKey=buildingKey,Reaction=CivilianReaction.Flee,
                X=start.X,Y=start.Y,Z=start.Z };
            if(double.IsNaN(until) || double.IsInfinity(until) || until<=now) throw new ArgumentException("Invalid cancellation deadline.");
            var motion=CivilianEscape.Create(person,now,inside,homeRoute,speed*12/timeScale);
            return Copy(new VisitDeparture { ResidentId=residentId,SettlementId=settlementId,HomeKey=homeKey,
                Until=Math.Max(until,motion.ReportAt+1),Motion=motion });
        }
        public AlarmPerson Position(double now)
        {
            if(double.IsNaN(now) || double.IsInfinity(now) || now<Motion.StartedAt) throw new ArgumentException("Invalid departure time.");
            var position=new AlarmPerson(); Motion.Advance(position,now);
            if(now>=Motion.ReportAt) position.BuildingKey=HomeKey;
            return position;
        }
        public static VisitDeparture Copy(VisitDeparture source)
        {
            if(source==null || string.IsNullOrWhiteSpace(source.ResidentId) || source.ResidentId.Length>256 ||
                source.SettlementId<0 || source.HomeKey<0 || double.IsNaN(source.Until) || double.IsInfinity(source.Until))
                throw new ArgumentException("Invalid visitor departure identity.");
            var motion=CivilianEscape.Copy(source.Motion);
            if(motion==null || source.Until<=motion.ReportAt || source.HomeKey==motion.BuildingKey)
                throw new ArgumentException("Departure must reach another home before cancellation expires.");
            return new VisitDeparture { ResidentId=source.ResidentId,SettlementId=source.SettlementId,
                HomeKey=source.HomeKey,Until=source.Until,Motion=motion };
        }
    }
    public sealed partial class WorldStateStore
    {
        public void SaveVisitDeparture(VisitDeparture departure)
        {
            var copy=VisitDeparture.Copy(departure);
            ValidateDepartureResident(state,copy);
            state.VisitDepartures.RemoveAll(d=>d.ResidentId==copy.ResidentId);
            state.VisitDepartures.Add(copy);
        }
        public VisitDeparture VisitDepartureFor(string residentId,double now)
        {
            Require(ValidTime(now),"Invalid departure query time.");
            var value=state.VisitDepartures.Find(d=>d.ResidentId==residentId && now>=d.Motion.StartedAt && now<d.Until);
            return value==null ? null : VisitDeparture.Copy(value);
        }
        public bool TryVisitDeparturePosition(string residentId,double now,out AlarmPerson position,out bool home)
        {
            Require(ValidTime(now),"Invalid departure query time.");
            var value=state.VisitDepartures.Find(d=>d.ResidentId==residentId && now>=d.Motion.StartedAt && now<d.Until);
            position=value==null ? null : value.Position(now);
            home=value!=null && now>=value.Motion.ReportAt;
            return value!=null;
        }
        private static void ValidateDepartureResident(WorldState world,VisitDeparture departure)
        {
            var town=world.Settlements.Find(t=>t.SettlementId==departure.SettlementId);
            Require(town!=null && town.Households.Exists(h=>h.BuildingKey==departure.HomeKey &&
                h.Residents.Exists(r=>r.Id==departure.ResidentId)),"Unknown departure resident or home.");
        }
        private static List<VisitDeparture> CopyVisitDepartures(WorldState source)
        {
            var result=new List<VisitDeparture>(); var ids=new HashSet<string>();
            // Additive field: historical saves contain no cancelled visits.
            if(source.VisitDepartures==null) return result;
            foreach(var value in source.VisitDepartures)
            {
                var copy=VisitDeparture.Copy(value); ValidateDepartureResident(source,copy);
                Require(ids.Add(copy.ResidentId),"Duplicate visitor departure."); result.Add(copy);
            }
            return result;
        }
    }
}
