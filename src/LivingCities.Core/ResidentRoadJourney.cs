using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    [Serializable]
    public sealed class ResidentRoadStop
    {
        public string Id;
        public int RoadNode,SettlementId;
        public int? ExitRoadNode;
        public ResidentDestination Visit;
    }
    public enum ResidentRoadPhase {Before,Traveling,Visiting,Finished}
    public struct ResidentRoadSnapshot
    {
        public ResidentRoadPhase Phase;
        public RoutePoint MapPosition;
        public int SettlementId,StopIndex;
        public ResidentVisitSnapshot Visit;
        public double InteriorY;
    }
    // Timing for an already surveyed road. This does not discover road links,
    // reserve rooms, pay keepers or establish ownership of a resident.
    public sealed class ResidentRoadJourney
    {
        private sealed class Segment
        {
            public RoutePoint From,To;
            public double StartsAt,EndsAt;
            public int Settlement=-1,StopIndex=-1;
            public ResidentVisitJourney Visit;
            public ResidentServiceExit Exit;
        }
        private readonly List<Segment> segments=new List<Segment>();
        public double DepartsAt {get;private set;}
        public double ArrivesAt {get;private set;}
        public static ResidentRoadJourney Create(IReadOnlyList<RoutePoint> road,double departure,double secondsPerMapPixel,
            IReadOnlyList<ResidentRoadStop> stops,IReadOnlyList<ResidentServiceExit> departures=null)
        {
            if(road==null || road.Count<2 || road.Count>10000 || stops==null || stops.Count>32 ||
                !Time(departure) || !Time(secondsPerMapPixel) || secondsPerMapPixel<=0)
                throw new ArgumentException("Invalid road journey.");
            foreach(var point in road)
                if(!Time(point.X) || !Time(point.Z) || point.X>=999.5 || point.Z>=499.5)
                    throw new ArgumentException("Invalid road coordinate.");
            int previous=0;
            foreach(var stop in stops)
            {
                if(stop==null || stop.SettlementId<0 || stop.RoadNode<=previous || stop.RoadNode>=road.Count-1)
                    throw new ArgumentException("Invalid intermediate road stop order.");
                int end=stop.ExitRoadNode ?? stop.RoadNode;
                if(end<stop.RoadNode || end>=road.Count-1 || stop.ExitRoadNode.HasValue && end==stop.RoadNode)
                    throw new ArgumentException("Invalid through-town road exit.");
                previous=end;
            }
            var exits=new List<ResidentServiceExit>();
            if(departures!=null)
            {
                if(departures.Count>32) throw new ArgumentException("Too many road-stop exits.");
                foreach(var item in departures) exits.Add(ResidentServiceExit.Copy(item));
            }
            int matched=0;
            var result=new ResidentRoadJourney {DepartsAt=departure};
            double time=departure;int nextStop=0;
            for(int node=1;node<road.Count;node++)
            {
                var a=road[node-1];var b=road[node];
                double x=b.X-a.X,z=b.Z-a.Z,duration=Math.Sqrt(x*x+z*z)*secondsPerMapPixel;
                if(!Time(duration) || duration<=0 || !Time(time+duration) || time+duration<=time)
                    throw new ArgumentException("Invalid road segment duration.");
                result.segments.Add(new Segment {From=a,To=b,StartsAt=time,EndsAt=time+duration});
                time+=duration;
                if(nextStop>=stops.Count || stops[nextStop].RoadNode!=node) continue;
                var stop=stops[nextStop];var visit=ResidentVisitJourney.Copy(stop.Visit);
                if(visit.Stops.Count!=1 || visit.Stops[0].Purpose!=ResidentVisitPurpose.Inn)
                    throw new ArgumentException("Intermediate road stop must be an inn visit.");
                var entry=visit.Stops[0].Route[0];var exit=visit.ExitRoute[visit.ExitRoute.Count-1];
                if(visit.ThroughRoute!=stop.ExitRoadNode.HasValue)
                    throw new ArgumentException("Through-town visit and road exit disagree.");
                if(!visit.ThroughRoute && (Math.Abs(entry.X-exit.X)>0.000001 || Math.Abs(entry.Z-exit.Z)>0.000001))
                    throw new ArgumentException("Intermediate inn must return to its road join.");
                ResidentServiceExit serviceExit=null;
                foreach(var candidate in exits)
                {
                    if(stop.Id==null || candidate.VisitId!=stop.Id+"/0") continue;
                    if(serviceExit!=null || candidate.SettlementId!=stop.SettlementId || candidate.BuildingKey!=visit.Stops[0].BuildingKey)
                        throw new ArgumentException("Road-stop exit scope mismatch or duplicate.");
                    serviceExit=candidate;matched++;
                }
                var journey=ResidentVisitJourney.Create(time,visit,serviceExit==null ? null : new List<ResidentServiceExit>{serviceExit});
                int exitNode=stop.ExitRoadNode ?? node;
                result.segments.Add(new Segment {From=b,To=road[exitNode],StartsAt=time,EndsAt=journey.EndsAt,
                    Settlement=stop.SettlementId,StopIndex=nextStop,Visit=journey,Exit=serviceExit});
                time=journey.EndsAt;node=exitNode;nextStop++;
            }
            if(matched!=exits.Count) throw new ArgumentException("Unmatched road-stop exit.");
            result.ArrivesAt=time;return result;
        }
        public ResidentRoadSnapshot Evaluate(double now)
        {
            if(!Time(now)) throw new ArgumentException("Invalid road journey time.");
            if(now<DepartsAt) return RoadState(ResidentRoadPhase.Before,segments[0].From);
            foreach(var segment in segments)
            {
                if(now>=segment.EndsAt) continue;
                if(segment.Visit!=null)
                {
                    var state=VisitState(segment,segment.Visit.Evaluate(now));
                    if(segment.Exit!=null && now>=segment.Exit.StartedAt && now<segment.Exit.ExitAt)
                    {
                        var point=segment.Exit.Position(now);
                        state.Visit.Phase=ResidentVisitPhase.ExitingService;
                        state.Visit.BuildingKey=segment.Exit.BuildingKey;
                        state.Visit.Position=new RoutePoint(point.X,point.Z);state.InteriorY=point.Y;
                    }
                    return state;
                }
                double fraction=(now-segment.StartsAt)/(segment.EndsAt-segment.StartsAt);
                return RoadState(ResidentRoadPhase.Traveling,new RoutePoint(
                    segment.From.X+(segment.To.X-segment.From.X)*fraction,segment.From.Z+(segment.To.Z-segment.From.Z)*fraction));
            }
            return RoadState(ResidentRoadPhase.Finished,segments[segments.Count-1].To);
        }
        public IEnumerable<ResidentRoadSnapshot> ScheduledVisits()
        {
            foreach(var segment in segments)
                if(segment.Visit!=null)
                    foreach(var visit in segment.Visit.ScheduledVisits()) yield return VisitState(segment,visit);
        }
        private static ResidentRoadSnapshot VisitState(Segment segment,ResidentVisitSnapshot visit)
        {return new ResidentRoadSnapshot {Phase=ResidentRoadPhase.Visiting,MapPosition=segment.From,
            SettlementId=segment.Settlement,StopIndex=segment.StopIndex,Visit=visit};}
        private static ResidentRoadSnapshot RoadState(ResidentRoadPhase phase,RoutePoint point)
        {return new ResidentRoadSnapshot {Phase=phase,MapPosition=point,SettlementId=-1,StopIndex=-1,
            Visit=new ResidentVisitSnapshot {BuildingKey=-1,TargetBuildingKey=-1}};}
        private static bool Time(double value) {return !double.IsNaN(value) && !double.IsInfinity(value) && value>=0;}
    }
}
