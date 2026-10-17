using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    public sealed class ShelterRoute
    {
        public int BuildingKey;
        public bool Accessible;
        public List<SavedRoutePoint> Points;
    }
    public static class ShelterRoutes
    {
        public static ShelterRoute Nearest(SavedRoutePoint from,SavedRoutePoint threat,IEnumerable<ShelterRoute> candidates)
        {
            if(from==null || threat==null || candidates==null) throw new ArgumentNullException();
            ShelterRoute best=null;double shortest=double.MaxValue;
            foreach(var route in candidates)
            {
                if(route==null || !route.Accessible || route.BuildingKey<0 || route.Points==null || route.Points.Count<2) continue;
                var start=route.Points[0];
                if(start==null || Distance(from,start)>0.05) continue;
                double length=0;bool safe=true;
                for(int i=1;i<route.Points.Count;i++)
                {
                    var a=route.Points[i-1];var b=route.Points[i];
                    if(a==null || b==null || !FlightSafety.SafeStep(a.X,a.Z,b.X,b.Z,threat.X,threat.Z)) {safe=false;break;}
                    length+=Distance(a,b);
                }
                if(!safe || double.IsNaN(length) || double.IsInfinity(length)) continue;
                if(length<shortest || length==shortest && (best==null || route.BuildingKey<best.BuildingKey))
                {best=route;shortest=length;}
            }
            if(best==null) return null;
            var result=new ShelterRoute{BuildingKey=best.BuildingKey,Accessible=true,Points=new List<SavedRoutePoint>()};
            foreach(var p in best.Points) result.Points.Add(new SavedRoutePoint{X=p.X,Z=p.Z});
            return result;
        }
        private static double Distance(SavedRoutePoint a,SavedRoutePoint b)
        {double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
    }
}
