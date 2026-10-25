using System;
namespace PeopleOfTheIliacBay.Core
{
    public static class FlightSafety
    {
        // Check the whole segment: a distant endpoint can still lead through the threat.
        public static bool SafeStep(double x,double z,double nextX,double nextZ,double threatX,double threatZ)
        {
            foreach(var value in new[] {x,z,nextX,nextZ,threatX,threatZ})
                if(double.IsNaN(value) || double.IsInfinity(value)) return false;
            double dx=nextX-x,dz=nextZ-z;
            double length=dx*dx+dz*dz;
            double t=length==0 ? 0 : Math.Max(0,Math.Min(1,((threatX-x)*dx+(threatZ-z)*dz)/length));
            double nearX=x+dx*t-threatX,nearZ=z+dz*t-threatZ;
            double startX=x-threatX,startZ=z-threatZ;
            double minimum=Math.Max(0,Math.Min(12,Math.Sqrt(startX*startX+startZ*startZ))-0.02);
            return nearX*nearX+nearZ*nearZ>=minimum*minimum;
        }
    }
}
