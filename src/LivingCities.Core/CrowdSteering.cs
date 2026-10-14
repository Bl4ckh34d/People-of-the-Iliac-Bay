using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    public static class CrowdSteering
    {
        public static GuardPoint3 Move(GuardPoint3 from,GuardPoint3 target,IEnumerable<GuardPoint3> neighbours,
            double separation,double maximumStep,Func<GuardPoint3,bool> clear)
        {
            if(!Valid(from) || !Valid(target) || neighbours==null || clear==null || !Finite(separation) || separation<=0 ||
                !Finite(maximumStep) || maximumStep<0) throw new ArgumentException("Invalid crowd movement.");
            var others=new List<GuardPoint3>();
            foreach(var other in neighbours) { if(!Valid(other)) throw new ArgumentException("Invalid crowd neighbour."); others.Add(other); }
            double dx=target.X-from.X,dz=target.Z-from.Z,dy=target.Y-from.Y;
            double length=Math.Sqrt(dx*dx+dz*dz+dy*dy);
            if(length<0.000001 || maximumStep==0) return Copy(from);
            double step=Math.Min(length,maximumStep);
            foreach(double degrees in new[] { 0d,35,-35,70,-70,90,-90 })
            {
                double angle=degrees*Math.PI/180,cos=Math.Cos(angle),sin=Math.Sin(angle);
                var candidate=new GuardPoint3 { X=from.X+(dx*cos-dz*sin)/length*step,
                    Z=from.Z+(dx*sin+dz*cos)/length*step,Y=from.Y+dy/length*step };
                bool occupied=false;
                foreach(var other in others)
                {
                    if(Math.Abs(other.Y-candidate.Y)>2.1 && Math.Abs(other.Y-from.Y)>2.1) continue;
                    double vx=candidate.X-from.X,vz=candidate.Z-from.Z;
                    double square=vx*vx+vz*vz;
                    double t=square<0.000001 ? 0 : Math.Max(0,Math.Min(1,((other.X-from.X)*vx+(other.Z-from.Z)*vz)/square));
                    double x=from.X+vx*t-other.X,z=from.Z+vz*t-other.Z;
                    if(x*x+z*z<separation*separation-0.0000001) { occupied=true; break; }
                }
                if(!occupied && clear(candidate)) return candidate;
            }
            return Copy(from);
        }
        private static GuardPoint3 Copy(GuardPoint3 p) { return new GuardPoint3 { X=p.X,Y=p.Y,Z=p.Z }; }
        private static bool Valid(GuardPoint3 p) { return p!=null && Finite(p.X) && Finite(p.Y) && Finite(p.Z); }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
}
