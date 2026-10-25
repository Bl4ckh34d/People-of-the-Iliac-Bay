using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Tracks an already known pursuer by relative motion, including behind the fleeing resident.
    public sealed class ShelterPursuit
    {
        public const double NearDistance=12, ImmediateDistance=6;
        private string source;
        private SavedRoutePoint previousThreat;
        private double previousDistance, sampledAt=-1, nextSearchAt;
        public bool HasKnownPursuer {get;private set;}
        public bool ShouldSearch(string threatId,double now,SavedRoutePoint from,SavedRoutePoint threat,bool knownPursuer)
        {
            HasKnownPursuer=Finite(now)&&now>=0&&Point(from)&&Point(threat)&&knownPursuer&&!string.IsNullOrEmpty(threatId);
            if(!HasKnownPursuer)
            {source=null;previousThreat=null;sampledAt=-1;return false;}
            double distance=Distance(from,threat);
            bool same=source==threatId&&previousThreat!=null&&now>=sampledAt;
            if(same&&now-sampledAt<0.5)
                return distance<=ImmediateDistance&&SearchDue(now);
            bool closing=false;
            if(same)
            {
                double x=threat.X-previousThreat.X,z=threat.Z-previousThreat.Z;
                double towards=x*(from.X-previousThreat.X)+z*(from.Z-previousThreat.Z);
                double elapsed=now-sampledAt;
                closing=x*x+z*z>=0.01&&towards>0&&previousDistance-distance>=0.5*elapsed;
            }
            source=threatId;previousThreat=new SavedRoutePoint{X=threat.X,Z=threat.Z};
            previousDistance=distance;sampledAt=now;
            return (distance<=ImmediateDistance||distance<=NearDistance&&closing)&&SearchDue(now);
        }
        private bool SearchDue(double now)
        {
            if(now<nextSearchAt)return false;
            nextSearchAt=now+2;return true;
        }
        public static ShelterRoute Choose(SavedRoutePoint from,SavedRoutePoint threat,ShelterRoute remaining,
            IEnumerable<ShelterRoute> alternatives,SavedRoutePoint fleeDirection=null)
        {
            if(alternatives==null)throw new ArgumentNullException(nameof(alternatives));
            var current=InEscapeDirection(from,threat,remaining,fleeDirection)?ShelterRoutes.Nearest(from,threat,new[]{remaining}):null;
            var forward=new List<ShelterRoute>();
            foreach(var route in alternatives)
                if(InEscapeDirection(from,threat,route,fleeDirection))forward.Add(route);
            var candidate=ShelterRoutes.Nearest(from,threat,forward);
            if(current==null)return candidate;
            // Keep a safe route unless another door saves enough distance to justify a turn.
            return candidate!=null&&Length(candidate)+1.5<Length(current)?candidate:current;
        }
        private static bool InEscapeDirection(SavedRoutePoint from,SavedRoutePoint threat,ShelterRoute route,SavedRoutePoint heading)
        {
            if(!Point(from)||!Point(threat)||route==null||route.Points==null||route.Points.Count<2)return false;
            var end=route.Points[route.Points.Count-1];if(!Point(end))return false;
            double awayX=from.X-threat.X,awayZ=from.Z-threat.Z;
            double x=end.X-from.X,z=end.Z-from.Z,length=x*x+z*z;
            if(x*awayX+z*awayZ<0)return false;
            double hx=Point(heading)?heading.X:awayX,hz=Point(heading)?heading.Z:awayZ;
            if(hx*hx+hz*hz<0.000001||hx*awayX+hz*awayZ<0){hx=awayX;hz=awayZ;}
            double headingLength=hx*hx+hz*hz;
            if(length<0.000001||headingLength<0.000001)return true;
            // A broad forward cone allows nearby street-side doors without reversing toward the attacker.
            return x*hx+z*hz>=0.25*Math.Sqrt(length*headingLength);
        }
        private static double Length(ShelterRoute route)
        {
            double length=0;for(int i=1;i<route.Points.Count;i++)length+=Distance(route.Points[i-1],route.Points[i]);
            return length;
        }
        private static double Distance(SavedRoutePoint a,SavedRoutePoint b)
        {double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        private static bool Point(SavedRoutePoint p){return p!=null&&Finite(p.X)&&Finite(p.Z);}
        private static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
    }
}
