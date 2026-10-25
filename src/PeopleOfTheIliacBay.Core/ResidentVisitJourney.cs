using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public enum ResidentVisitPurpose { Shop, Temple, Guild, Castle, Social, Inn }
    public enum ResidentVisitPhase { WaitingAtGate, Walking, WaitingForOpening, Inside, WaitingToLeave, Leaving, Finished, ExitingService }
    [Serializable] public sealed class ResidentVisitStop
    {
        public int BuildingKey,OpenMinute,CloseMinute;
        public ResidentVisitPurpose Purpose;
        public double WalkSeconds,DwellSeconds;
        public double KeepRoomUntil;
        public List<SavedRoutePoint> Route=new List<SavedRoutePoint>();
    }
    [Serializable] public sealed class ResidentDestination
    {
        public List<ResidentVisitStop> Stops=new List<ResidentVisitStop>();
        public List<SavedRoutePoint> ExitRoute=new List<SavedRoutePoint>();
        public double ExitWalkSeconds,EntryGateSeconds,ExitGateSeconds;
        public bool Walled;
        public bool ThroughRoute;
    }
    public struct ResidentVisitSnapshot
    {
        public int VisitIndex;
        public ResidentVisitPhase Phase;
        public int BuildingKey,TargetBuildingKey;
        public ResidentVisitPurpose Purpose;
        public RoutePoint Position;
        public RoutePoint Destination;
        public double ArriveAt,EnterAt,LeaveAt;
        public double RoomUntil;
    }
    // Timing only. An Inn purpose never reserves or pays for a bed by itself.
    public sealed class ResidentVisitJourney
    {
        // Native JSON rounds absolute game timestamps. Match visit references
        // within 1 ms without changing booking identifiers or payment times.
        internal static bool SameTime(double a,double b) {return Math.Abs(a-b)<=0.001;}
        private sealed class Stop
        {
            public int Index;
            public ResidentVisitStop Data;
            public Path Route;
            public double WalkAt,ArriveAt,EnterAt,EndAt;
        }
        private sealed class Path
        {
            public List<SavedRoutePoint> Points;
            private readonly double[] lengths;
            public Path(List<SavedRoutePoint> source)
            {
                if(source==null || source.Count<2 || source.Count>10000) throw new ArgumentException("Invalid visit route.");
                Points=new List<SavedRoutePoint>(); lengths=new double[source.Count];
                for(int i=0;i<source.Count;i++)
                {
                    var p=source[i];
                    if(p==null || !Finite(p.X) || !Finite(p.Z)) throw new ArgumentException("Invalid visit point.");
                    Points.Add(new SavedRoutePoint {X=p.X,Z=p.Z});
                    if(i>0) { double x=p.X-source[i-1].X,z=p.Z-source[i-1].Z; lengths[i]=lengths[i-1]+Math.Sqrt(x*x+z*z); }
                }
                if(!Finite(lengths[lengths.Length-1]) || lengths[lengths.Length-1]<=0) throw new ArgumentException("Empty visit walk.");
            }
            public RoutePoint At(double fraction)
            {
                double distance=Math.Max(0,Math.Min(1,fraction))*lengths[lengths.Length-1];
                for(int i=1;i<Points.Count;i++)
                {
                    double span=lengths[i]-lengths[i-1];
                    if(span<=0 || distance>lengths[i]) continue;
                    double f=(distance-lengths[i-1])/span; var a=Points[i-1]; var b=Points[i];
                    return new RoutePoint(a.X+(b.X-a.X)*f,a.Z+(b.Z-a.Z)*f);
                }
                var last=Points[Points.Count-1]; return new RoutePoint(last.X,last.Z);
            }
        }
        private readonly List<Stop> stops=new List<Stop>();
        private Path exit;
        private double exitAt,exitSeconds;
        public double EndsAt { get; private set; }
        public static ResidentDestination Copy(ResidentDestination source)
        {
            if(source==null || source.Stops==null || source.Stops.Count<1 || source.Stops.Count>32 ||
                !Positive(source.ExitWalkSeconds) || !Finite(source.EntryGateSeconds) || source.EntryGateSeconds<0 ||
                !Finite(source.ExitGateSeconds) || source.ExitGateSeconds<0 || source.ExitGateSeconds>source.ExitWalkSeconds)
                throw new ArgumentException("Invalid destination itinerary.");
            var copy=new ResidentDestination {ExitRoute=new Path(source.ExitRoute).Points,ExitWalkSeconds=source.ExitWalkSeconds,
                EntryGateSeconds=source.EntryGateSeconds,ExitGateSeconds=source.ExitGateSeconds,Walled=source.Walled,ThroughRoute=source.ThroughRoute};
            SavedRoutePoint previous=null,first=null;
            foreach(var stop in source.Stops)
            {
                if(stop==null || stop.BuildingKey<0 || !Enum.IsDefined(typeof(ResidentVisitPurpose),stop.Purpose) ||
                    stop.OpenMinute<0 || stop.OpenMinute>=1440 || stop.CloseMinute<0 || stop.CloseMinute>=1440 ||
                    !Positive(stop.WalkSeconds) || !Positive(stop.DwellSeconds) || stop.DwellSeconds>30*86400)
                    throw new ArgumentException("Invalid destination stop.");
                double window=(stop.CloseMinute-stop.OpenMinute+1440)%1440;
                if(window!=0 && stop.DwellSeconds>window*60) throw new ArgumentException("Visit exceeds service opening window.");
                var path=new Path(stop.Route);
                if(previous!=null && !Same(previous,path.Points[0])) throw new ArgumentException("Disconnected destination stops.");
                if(first==null) first=path.Points[0]; previous=path.Points[path.Points.Count-1];
                copy.Stops.Add(new ResidentVisitStop {BuildingKey=stop.BuildingKey,Purpose=stop.Purpose,OpenMinute=stop.OpenMinute,
                    CloseMinute=stop.CloseMinute,WalkSeconds=stop.WalkSeconds,DwellSeconds=stop.DwellSeconds,KeepRoomUntil=stop.KeepRoomUntil,Route=path.Points});
            }
            if(!Same(previous,copy.ExitRoute[0]) || !copy.ThroughRoute && !Same(first,copy.ExitRoute[copy.ExitRoute.Count-1]) ||
                copy.EntryGateSeconds>copy.Stops[0].WalkSeconds || !copy.Walled && (copy.EntryGateSeconds!=0 || copy.ExitGateSeconds!=0))
                throw new ArgumentException("Destination exit or gate does not match its routes.");
            return copy;
        }
        public static ResidentVisitJourney Create(double arrival,ResidentDestination source,List<ResidentServiceExit> departures=null)
        {
            if(!Finite(arrival) || arrival<0) throw new ArgumentException("Invalid arrival time.");
            var data=Copy(source); var result=new ResidentVisitJourney();
            var exits=departures==null ? new List<ResidentServiceExit>() : departures.ConvertAll(ResidentServiceExit.Copy);
            int matched=0;
            double time=data.Walled ? Gate(arrival+data.EntryGateSeconds)-data.EntryGateSeconds : arrival;
            foreach(var stop in data.Stops)
            {
                var leg=new Stop {Index=result.stops.Count,Data=stop,Route=new Path(stop.Route),WalkAt=time,ArriveAt=time+stop.WalkSeconds};
                leg.EnterAt=Opening(leg.ArriveAt,stop); leg.EndAt=leg.EnterAt+stop.DwellSeconds;
                foreach(var exit in exits) if(SameTime(exit.VisitEnterAt,leg.EnterAt) && exit.BuildingKey==stop.BuildingKey)
                {
                    if(!SameTime(exit.VisitLeaveAt,leg.EndAt)) throw new ArgumentException("Exit no longer matches service window.");
                    leg.EndAt=exit.ExitAt; matched++;
                }
                result.stops.Add(leg); time=leg.EndAt;
            }
            result.exit=new Path(data.ExitRoute); result.exitSeconds=data.ExitWalkSeconds;
            if(matched!=exits.Count) throw new ArgumentException("Unmatched service exit.");
            result.exitAt=data.Walled ? Gate(time+data.ExitGateSeconds)-data.ExitGateSeconds : time;
            result.EndsAt=result.exitAt+result.exitSeconds;
            foreach(var stop in result.stops)
                if(!Finite(stop.Data.KeepRoomUntil) || stop.Data.KeepRoomUntil<0 || stop.Data.KeepRoomUntil>0 &&
                    (stop.Data.Purpose!=ResidentVisitPurpose.Inn || stop.Data.KeepRoomUntil<stop.EndAt && !SameTime(stop.Data.KeepRoomUntil,stop.EndAt) ||
                        stop.Data.KeepRoomUntil>result.EndsAt && !SameTime(stop.Data.KeepRoomUntil,result.EndsAt)))
                    throw new ArgumentException("Room reservation exceeds itinerary or ends before inn visit: hold="+
                        stop.Data.KeepRoomUntil.ToString("R")+"; visitEnd="+stop.EndAt.ToString("R")+"; itineraryEnd="+result.EndsAt.ToString("R"));
            if(!Finite(result.EndsAt) || result.EndsAt<=arrival || result.EndsAt-arrival>60*86400)
                throw new ArgumentException("Destination itinerary exceeds bounded visit duration.");
            return result;
        }
        public ResidentVisitSnapshot Evaluate(double now)
        {
            if(!Finite(now) || now<0) throw new ArgumentException("Invalid visit time.");
            foreach(var stop in stops)
            {
                if(now<stop.WalkAt) return Sample(ResidentVisitPhase.WaitingAtGate,stop,stop.Route.At(0));
                if(now<stop.ArriveAt) return Sample(ResidentVisitPhase.Walking,stop,stop.Route.At((now-stop.WalkAt)/stop.Data.WalkSeconds));
                if(now<stop.EnterAt) return Sample(ResidentVisitPhase.WaitingForOpening,stop,stop.Route.At(1));
                if(now<stop.EndAt) return Sample(ResidentVisitPhase.Inside,stop,stop.Route.At(1));
            }
            return new ResidentVisitSnapshot {Phase=now<exitAt ? ResidentVisitPhase.WaitingToLeave : now<EndsAt ? ResidentVisitPhase.Leaving : ResidentVisitPhase.Finished,
                VisitIndex=-1,BuildingKey=-1,TargetBuildingKey=-1,Position=exit.At((now-exitAt)/exitSeconds),Destination=exit.At(1)};
        }
        public IEnumerable<ResidentVisitSnapshot> ScheduledVisits()
        {
            foreach(var stop in stops)
            {
                var end=stop.Route.Points[stop.Route.Points.Count-1];
                yield return Sample(ResidentVisitPhase.Inside,stop,new RoutePoint(end.X,end.Z));
            }
        }
        private static ResidentVisitSnapshot Sample(ResidentVisitPhase phase,Stop stop,RoutePoint point)
        { return new ResidentVisitSnapshot {VisitIndex=stop.Index,Phase=phase,BuildingKey=phase==ResidentVisitPhase.Inside ? stop.Data.BuildingKey : -1,
            TargetBuildingKey=stop.Data.BuildingKey,Purpose=stop.Data.Purpose,Position=point,Destination=stop.Route.At(1),ArriveAt=stop.ArriveAt,EnterAt=stop.EnterAt,LeaveAt=stop.EndAt,
            RoomUntil=Math.Max(stop.EndAt,stop.Data.KeepRoomUntil)}; }
        private static double Opening(double time,ResidentVisitStop stop)
        {
            if(stop.OpenMinute==stop.CloseMinute) return time;
            double day=Math.Floor(time/86400)*86400;
            for(int offset=-1;offset<=1;offset++)
            {
                double start=day+offset*86400+stop.OpenMinute*60;
                double end=day+offset*86400+stop.CloseMinute*60+(stop.CloseMinute<=stop.OpenMinute ? 86400 : 0);
                double candidate=Math.Max(time,start);
                if(candidate+stop.DwellSeconds<=end) return candidate;
            }
            throw new ArgumentException("No service opening can fit visit.");
        }
        private static double Gate(double time)
        { double day=Math.Floor(time/86400)*86400; return time<day+21600 ? day+21600 : time>=day+64800 ? day+108000 : time; }
        private static bool Same(SavedRoutePoint a,SavedRoutePoint b) { return Math.Abs(a.X-b.X)<0.000001 && Math.Abs(a.Z-b.Z)<0.000001; }
        private static bool Positive(double x) { return Finite(x) && x>0; }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
}
