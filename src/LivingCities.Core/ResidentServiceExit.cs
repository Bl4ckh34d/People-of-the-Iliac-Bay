using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    [Serializable]
    public sealed class ResidentServiceExit
    {
        public int BuildingKey;
        public int? SettlementId;
        public string VisitId;
        public double VisitEnterAt,VisitLeaveAt,StartedAt,SecondsPerMetre;
        public List<GuardPoint3> Route=new List<GuardPoint3>();
        private double length;
        public double ExitAt { get { return StartedAt+length*SecondsPerMetre; } }
        public GuardPoint3 Position(double now)
        {
            double remaining=Math.Max(0,(now-StartedAt)/SecondsPerMetre);
            for(int i=1;i<Route.Count;i++)
            {
                var a=Route[i-1]; var b=Route[i]; double distance=Distance(a,b);
                if(distance<=0) continue;
                if(remaining<=distance)
                { double f=remaining/distance; return new GuardPoint3 {X=a.X+(b.X-a.X)*f,Y=a.Y+(b.Y-a.Y)*f,Z=a.Z+(b.Z-a.Z)*f}; }
                remaining-=distance;
            }
            var end=Route[Route.Count-1]; return new GuardPoint3 {X=end.X,Y=end.Y,Z=end.Z};
        }
        public static ResidentServiceExit Copy(ResidentServiceExit source)
        {
            if(source==null || source.BuildingKey<0 || source.SettlementId<0 || !Time(source.VisitEnterAt) || !Time(source.VisitLeaveAt) ||
                !Time(source.StartedAt) || source.StartedAt<source.VisitEnterAt || source.StartedAt>=source.VisitLeaveAt ||
                !Time(source.SecondsPerMetre) || source.SecondsPerMetre<=0 || source.Route==null || source.Route.Count<2 || source.Route.Count>10000)
                throw new ArgumentException("Invalid resident service exit.");
            var copy=new ResidentServiceExit {VisitId=source.VisitId,SettlementId=source.SettlementId,BuildingKey=source.BuildingKey,VisitEnterAt=source.VisitEnterAt,
                VisitLeaveAt=source.VisitLeaveAt,StartedAt=source.StartedAt,SecondsPerMetre=source.SecondsPerMetre};
            foreach(var p in source.Route)
            {
                if(p==null || !Coordinate(p.X) || !Coordinate(p.Y) || !Coordinate(p.Z)) throw new ArgumentException("Invalid service exit point.");
                if(copy.Route.Count>0) copy.length+=Distance(copy.Route[copy.Route.Count-1],p);
                copy.Route.Add(new GuardPoint3 {X=p.X,Y=p.Y,Z=p.Z});
            }
            if(copy.length<=0 || !Time(copy.ExitAt) || copy.ExitAt-copy.StartedAt>86400) throw new ArgumentException("Invalid service exit duration.");
            return copy;
        }
        private static bool Time(double value) {return Coordinate(value) && value>=0;}
        private static bool Coordinate(double value) {return !double.IsNaN(value) && !double.IsInfinity(value);}
        private static double Distance(GuardPoint3 a,GuardPoint3 b)
        {double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return Math.Sqrt(x*x+y*y+z*z);}
    }
}
