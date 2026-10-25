using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public struct TravelerRoomPosition
    {
        public GuardPoint3 Position;
        public bool Walking;
    }
    public sealed class TravelerRoomWalk
    {
        private readonly List<GuardPoint3> route=new List<GuardPoint3>();
        private double start,end,secondsPerMetre,length;
        public double Duration { get { return length*secondsPerMetre; } }
        public static TravelerRoomWalk Create(IEnumerable<GuardPoint3> points,double checkIn,double checkOut,double secondsPerMetre)
        {
            if(points==null || !Finite(checkIn) || checkIn<0 || !Finite(checkOut) || checkOut<=checkIn ||
                !Finite(secondsPerMetre) || secondsPerMetre<=0) throw new ArgumentException("Invalid inn walk.");
            var result=new TravelerRoomWalk { start=checkIn,end=checkOut,secondsPerMetre=secondsPerMetre };
            foreach(var point in points)
            {
                if(point==null || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) || result.route.Count>=10000)
                    throw new ArgumentException("Invalid inn route point.");
                if(result.route.Count>0) result.length+=Distance(result.route[result.route.Count-1],point);
                result.route.Add(new GuardPoint3 { X=point.X,Y=point.Y,Z=point.Z });
            }
            if(result.route.Count<2 || result.length<=0 || !Finite(result.Duration) || result.Duration*2>checkOut-checkIn)
                throw new ArgumentException("Inn stay cannot fit room travel.");
            return result;
        }
        public TravelerRoomPosition Evaluate(double now)
        {
            if(!Finite(now) || now<0) throw new ArgumentOutOfRangeException(nameof(now));
            double along=now<start+Duration ? Math.Max(0,now-start)/secondsPerMetre :
                now>=end-Duration ? Math.Max(0,end-now)/secondsPerMetre : length;
            bool walking=now>=start && now<start+Duration || now>=end-Duration && now<end;
            for(int i=1;i<route.Count;i++)
            {
                double segment=Distance(route[i-1],route[i]);
                if(segment>0 && along<=segment)
                {
                    double t=along/segment; var a=route[i-1]; var b=route[i];
                    return new TravelerRoomPosition { Walking=walking,Position=new GuardPoint3 {
                        X=a.X+(b.X-a.X)*t,Y=a.Y+(b.Y-a.Y)*t,Z=a.Z+(b.Z-a.Z)*t } };
                }
                along-=segment;
            }
            var last=route[route.Count-1];
            return new TravelerRoomPosition { Walking=walking,Position=new GuardPoint3{X=last.X,Y=last.Y,Z=last.Z} };
        }
        private static double Distance(GuardPoint3 a,GuardPoint3 b)
        { double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z; return Math.Sqrt(x*x+y*y+z*z); }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
}
