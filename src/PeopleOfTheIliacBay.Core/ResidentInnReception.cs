using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class ResidentInnReception
    {
        public string KeeperId;
        public int BuildingKey;
        public int? SettlementId;
        public string VisitId;
        public double VisitEnterAt,VisitLeaveAt,StartedAt,SecondsPerMetre,ServiceSeconds;
        public List<GuardPoint3> Route=new List<GuardPoint3>();
        private double length;
        public double CounterAt {get {return StartedAt+length*SecondsPerMetre;}}
        public double ServiceEndsAt {get {return CounterAt+ServiceSeconds;}}
        public GuardPoint3 Position(double now)
        {
            if(!Time(now)) throw new ArgumentException("Invalid reception sample time.");
            double along=Math.Max(0,(now-StartedAt)/SecondsPerMetre);
            for(int i=1;i<Route.Count;i++)
            {
                var a=Route[i-1]; var b=Route[i]; double distance=Distance(a,b);
                if(distance>0 && along<=distance)
                {double t=along/distance; return new GuardPoint3 {X=a.X+(b.X-a.X)*t,Y=a.Y+(b.Y-a.Y)*t,Z=a.Z+(b.Z-a.Z)*t};}
                along-=distance;
            }
            var end=Route[Route.Count-1]; return new GuardPoint3 {X=end.X,Y=end.Y,Z=end.Z};
        }
        public static ResidentInnReception Copy(ResidentInnReception source)
        {
            if(source==null || source.BuildingKey<0 || source.SettlementId<0 || !Time(source.VisitEnterAt) || !Time(source.VisitLeaveAt) ||
                !Time(source.StartedAt) || source.StartedAt<source.VisitEnterAt || !Time(source.SecondsPerMetre) || source.SecondsPerMetre<=0 ||
                !Time(source.ServiceSeconds) || source.ServiceSeconds<=0 || source.Route==null || source.Route.Count<2 || source.Route.Count>10000)
                throw new ArgumentException("Invalid inn reception.");
            var copy=new ResidentInnReception {VisitId=source.VisitId,SettlementId=source.SettlementId,KeeperId=source.KeeperId,BuildingKey=source.BuildingKey,VisitEnterAt=source.VisitEnterAt,VisitLeaveAt=source.VisitLeaveAt,
                StartedAt=source.StartedAt,SecondsPerMetre=source.SecondsPerMetre,ServiceSeconds=source.ServiceSeconds};
            foreach(var point in source.Route)
            {
                if(point==null || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z)) throw new ArgumentException("Invalid reception route.");
                if(copy.Route.Count>0) copy.length+=Distance(copy.Route[copy.Route.Count-1],point);
                copy.Route.Add(new GuardPoint3 {X=point.X,Y=point.Y,Z=point.Z});
            }
            if(copy.length<=0 || !Time(copy.ServiceEndsAt) || copy.ServiceEndsAt>=copy.VisitLeaveAt)
                throw new ArgumentException("Reception exceeds inn visit.");
            return copy;
        }
        private static double Distance(GuardPoint3 a,GuardPoint3 b)
        {double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return Math.Sqrt(x*x+y*y+z*z);}
        private static bool Finite(double x) {return !double.IsNaN(x) && !double.IsInfinity(x);}
        private static bool Time(double x) {return Finite(x) && x>=0;}
    }
    public sealed partial class WorldStateStore
    {
        public ResidentInnReception ResidentInnReceptionAt(string id,double now)
        {
            Require(DefenseTime(now),"Invalid reception query."); ResidentTrip trip;
            if(id==null || !residentTripIndex.TryGetValue(id,out trip)) return null;
            var visit=trip.Evaluate(now);
            var reception=trip.InnReceptions.Find(r=>r.SettlementId==visit.SettlementId && r.VisitId==visit.VisitId && r.BuildingKey==visit.BuildingKey);
            return reception==null ? null : ResidentInnReception.Copy(reception);
        }
        public void SaveResidentInnReception(string id,ResidentInnReception source)
        {
            ResidentTrip original;
            Require(id!=null && residentTripIndex.TryGetValue(id,out original),"Unknown resident trip.");
            original=residentTripIndex[id]; var reception=ResidentInnReception.Copy(source);
            var scoped=original.ResolveScopedVisit(reception.VisitId,reception.SettlementId,reception.BuildingKey,reception.VisitEnterAt);
            reception.SettlementId=scoped.Settlement;reception.VisitId=scoped.Id;
            Require(TripResident(state,original.HomeSettlement,id).IsAlive,"Dead resident cannot approach keeper.");
            if(original.InnReceptions.Exists(r=>r.VisitId==reception.VisitId)) return;
            Require(!original.InnStays.Exists(s=>ResidentVisitJourney.SameTime(s.EnterAt,reception.VisitEnterAt)),"Cannot retime completed admission.");
            var copy=ResidentTrip.CopyValidated(original); copy.InnReceptions.Add(reception); copy=ResidentTrip.CopyValidated(copy);
            ValidateResidentInnBookings(state,copy);
            state.ResidentTrips.RemoveAll(t=>t.ResidentId==id); state.ResidentTrips.Add(copy); residentTripIndex[id]=copy;
        }
    }
}
