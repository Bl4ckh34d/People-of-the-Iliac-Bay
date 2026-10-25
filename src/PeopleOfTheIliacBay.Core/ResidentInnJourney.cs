using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public enum ResidentInnPhase {Before,ApproachKeeper,AtKeeper,ToRoom,InRoom,Leaving,Finished}
    public struct ResidentInnPosition
    {
        public ResidentInnPhase Phase;
        public GuardPoint3 Position;
    }
    // Absolute game time keeps reception progress independent of actor loading.
    public sealed class ResidentInnJourney
    {
        private List<GuardPoint3> entry,room,exit;
        private double startsAt,endsAt,rate;
        public double CounterAt {get;private set;}
        public double ServiceEndsAt {get;private set;}
        public double RoomAt {get;private set;}
        public double ExitAt {get;private set;}
        public static ResidentInnJourney Create(IEnumerable<GuardPoint3> entry,IEnumerable<GuardPoint3> room,
            IEnumerable<GuardPoint3> exit,double start,double end,double secondsPerMetre,double serviceSeconds)
        {
            if(!Finite(start) || start<0 || !Finite(end) || end<=start || !Finite(secondsPerMetre) || secondsPerMetre<=0 ||
                !Finite(serviceSeconds) || serviceSeconds<=0) throw new ArgumentException("Invalid inn reception timing.");
            var result=new ResidentInnJourney {entry=Copy(entry),room=Copy(room),exit=Copy(exit),startsAt=start,endsAt=end,rate=secondsPerMetre};
            if(Distance(result.entry[result.entry.Count-1],result.room[0])>0.01 ||
                Distance(result.room[result.room.Count-1],result.exit[0])>0.01 || Distance(result.exit[result.exit.Count-1],result.entry[0])>0.01)
                throw new ArgumentException("Disconnected inn reception routes.");
            result.CounterAt=start+Length(result.entry)*secondsPerMetre;
            result.ServiceEndsAt=result.CounterAt+serviceSeconds;
            result.RoomAt=result.ServiceEndsAt+Length(result.room)*secondsPerMetre;
            result.ExitAt=end-Length(result.exit)*secondsPerMetre;
            if(!Finite(result.RoomAt) || !Finite(result.ExitAt) || result.RoomAt>result.ExitAt)
                throw new ArgumentException("Inn stay cannot fit reception and room travel.");
            return result;
        }
        public ResidentInnPosition Evaluate(double now)
        {
            if(!Finite(now) || now<0) throw new ArgumentOutOfRangeException(nameof(now));
            if(now<startsAt) return At(ResidentInnPhase.Before,entry,0);
            if(now<CounterAt) return At(ResidentInnPhase.ApproachKeeper,entry,(now-startsAt)/rate);
            if(now<ServiceEndsAt) return At(ResidentInnPhase.AtKeeper,entry,double.PositiveInfinity);
            if(now<RoomAt) return At(ResidentInnPhase.ToRoom,room,(now-ServiceEndsAt)/rate);
            if(now<ExitAt) return At(ResidentInnPhase.InRoom,room,double.PositiveInfinity);
            if(now<endsAt) return At(ResidentInnPhase.Leaving,exit,(now-ExitAt)/rate);
            return At(ResidentInnPhase.Finished,exit,double.PositiveInfinity);
        }
        private static ResidentInnPosition At(ResidentInnPhase phase,List<GuardPoint3> route,double along)
        {
            for(int i=1;i<route.Count;i++)
            {
                var a=route[i-1]; var b=route[i]; double distance=Distance(a,b);
                if(distance>0 && along<=distance)
                {
                    double t=along/distance;
                    return new ResidentInnPosition {Phase=phase,Position=new GuardPoint3 {X=a.X+(b.X-a.X)*t,Y=a.Y+(b.Y-a.Y)*t,Z=a.Z+(b.Z-a.Z)*t}};
                }
                along-=distance;
            }
            var last=route[route.Count-1];
            return new ResidentInnPosition {Phase=phase,Position=new GuardPoint3 {X=last.X,Y=last.Y,Z=last.Z}};
        }
        private static List<GuardPoint3> Copy(IEnumerable<GuardPoint3> source)
        {
            if(source==null) throw new ArgumentException("Missing inn reception route.");
            var result=new List<GuardPoint3>();
            foreach(var point in source)
            {
                if(point==null || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z) || result.Count>=10000)
                    throw new ArgumentException("Invalid inn reception point.");
                result.Add(new GuardPoint3 {X=point.X,Y=point.Y,Z=point.Z});
            }
            double length=Length(result);
            if(result.Count<2 || length<=0 || !Finite(length)) throw new ArgumentException("Invalid inn reception route.");
            return result;
        }
        private static double Length(List<GuardPoint3> route)
        {double total=0; for(int i=1;i<route.Count;i++) total+=Distance(route[i-1],route[i]); return total;}
        private static double Distance(GuardPoint3 a,GuardPoint3 b)
        {double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z; return Math.Sqrt(x*x+y*y+z*z);}
        private static bool Finite(double value) {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
