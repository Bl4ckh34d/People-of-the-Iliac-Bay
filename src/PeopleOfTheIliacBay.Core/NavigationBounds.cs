using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class NavigationBounds
    {
        // Conservative bounding overlap: false positives request an exact check;
        // false negatives would incorrectly cache a dynamic gate crossing.
        public static bool IntersectsSegment(RoutePoint a,RoutePoint b,double minX,double minZ,double maxX,double maxZ)
        {return Math.Max(a.X,b.X)>=minX&&Math.Min(a.X,b.X)<=maxX&&Math.Max(a.Z,b.Z)>=minZ&&Math.Min(a.Z,b.Z)<=maxZ;}
    }
}
