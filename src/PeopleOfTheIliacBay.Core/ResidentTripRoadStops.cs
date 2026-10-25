using System;
using System.Collections.Generic;
using System.Globalization;
namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class ResidentTrip
    {
        public List<ResidentRoadStop> OutboundStops=new List<ResidentRoadStop>();
        public List<ResidentRoadStop> ReturnStops=new List<ResidentRoadStop>();
        private ResidentRoadJourney outboundRoad,returnRoad;
        internal struct ScopedVisit
        {
            public string Id;
            public int Settlement;
            public ResidentVisitSnapshot Visit;
        }
        private IEnumerable<ScopedVisit> AllScheduledVisits()
        {
            if(outboundRoad!=null)
                foreach(var item in outboundRoad.ScheduledVisits())
                    yield return new ScopedVisit {Id=OutboundStops[item.StopIndex].Id+"/"+item.Visit.VisitIndex.ToString(CultureInfo.InvariantCulture),
                        Settlement=item.SettlementId,Visit=item.Visit};
            if(destinationJourney!=null)
                foreach(var visit in destinationJourney.ScheduledVisits())
                    yield return new ScopedVisit {Id=DestinationVisitId(visit.VisitIndex),Settlement=DestinationSettlement,Visit=visit};
            if(returnRoad!=null)
                foreach(var item in returnRoad.ScheduledVisits())
                    yield return new ScopedVisit {Id=ReturnStops[item.StopIndex].Id+"/"+item.Visit.VisitIndex.ToString(CultureInfo.InvariantCulture),
                        Settlement=item.SettlementId,Visit=item.Visit};
        }
        internal ScopedVisit ResolveScopedVisit(string id,int? settlement,int building,double entered)
        {
            foreach(var item in AllScheduledVisits())
            {
                if(item.Visit.BuildingKey!=building || !ResidentVisitJourney.SameTime(item.Visit.EnterAt,entered)) continue;
                if(id!=null && id!=item.Id || settlement.HasValue && settlement.Value!=item.Settlement)
                    throw new ArgumentException("Visit scope does not match itinerary.");
                return item;
            }
            throw new ArgumentException("Visit lacks matching itinerary entry.");
        }
        private void CopyRoadStops(ResidentTrip source)
        {
            foreach(var exit in source.ServiceExits ?? new List<ResidentServiceExit>())
                if(IsRoadExit(exit)) ServiceExits.Add(ResidentServiceExit.Copy(exit));
            OutboundStops=CopyStops(source.OutboundStops,"outbound/");
            ReturnStops=CopyStops(source.ReturnStops,"return/");
            var exits=ServiceExits.FindAll(e=>e.VisitId.StartsWith("outbound/",StringComparison.Ordinal));
            if(OutboundStops.Count>0) outboundRoad=ResidentRoadJourney.Create(RoadPoints(false),RoadLeavesAt,SecondsPerMapPixel,OutboundStops,exits);
            else if(exits.Count>0) throw new ArgumentException("Outbound exit has no road stop.");
        }
        internal static bool IsRoadExit(ResidentServiceExit exit)
        {return exit!=null && exit.VisitId!=null && (exit.VisitId.StartsWith("outbound/",StringComparison.Ordinal) || exit.VisitId.StartsWith("return/",StringComparison.Ordinal));}
        internal List<ResidentServiceExit> PrimaryExits() {return ServiceExits.FindAll(e=>!IsRoadExit(e));}
        private List<ResidentRoadStop> CopyStops(List<ResidentRoadStop> source,string prefix)
        {
            var result=new List<ResidentRoadStop>();if(source==null) return result;
            if(source.Count>32) throw new ArgumentException("Too many intermediate road stops.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var stop in source)
            {
                if(stop==null || stop.SettlementId<0 || stop.SettlementId==HomeSettlement || stop.SettlementId==DestinationSettlement)
                    throw new ArgumentException("Invalid intermediate settlement.");
                string id=stop.Id ?? prefix+result.Count.ToString(CultureInfo.InvariantCulture);int number;
                if(!id.StartsWith(prefix,StringComparison.Ordinal) || !int.TryParse(id.Substring(prefix.Length),NumberStyles.None,
                    CultureInfo.InvariantCulture,out number) || number<0 || number>=32 || id!=prefix+number.ToString(CultureInfo.InvariantCulture) || !ids.Add(id))
                    throw new ArgumentException("Invalid or duplicate road stop ID.");
                result.Add(new ResidentRoadStop {Id=id,RoadNode=stop.RoadNode,ExitRoadNode=stop.ExitRoadNode,SettlementId=stop.SettlementId,Visit=ResidentVisitJourney.Copy(stop.Visit)});
            }
            return result;
        }
        private List<RoutePoint> RoadPoints(bool reverse)
        {
            var points=new List<RoutePoint>();
            foreach(var point in Road) points.Add(new RoutePoint(point.X,point.Z));
            if(reverse) points.Reverse();return points;
        }
        private void BuildReturnRoad()
        {
            var exits=ServiceExits.FindAll(e=>e.VisitId!=null && e.VisitId.StartsWith("return/",StringComparison.Ordinal));
            if(ReturnStops.Count>0) returnRoad=ResidentRoadJourney.Create(RoadPoints(true),ReturnsFromVisitAt,SecondsPerMapPixel,ReturnStops,exits);
            else if(exits.Count>0) throw new ArgumentException("Return exit has no road stop.");
        }
        private ResidentTripSnapshot RoadState(ResidentRoadJourney road,List<ResidentRoadStop> stops,double now,bool returning)
        {
            var state=road.Evaluate(now);
            if(state.Phase==ResidentRoadPhase.Visiting)
            {
                var visit=state.Visit;
                return new ResidentTripSnapshot {Phase=ResidentTripPhase.Visiting,SettlementId=state.SettlementId,
                    VisitId=visit.VisitIndex<0 ? null : stops[state.StopIndex].Id+"/"+visit.VisitIndex.ToString(CultureInfo.InvariantCulture),
                    BuildingKey=visit.BuildingKey,Position=visit.Position,InteriorY=state.InteriorY,VisitPhase=visit.Phase,VisitPurpose=visit.Purpose,
                    VisitEnterAt=visit.EnterAt,VisitLeaveAt=visit.LeaveAt};
            }
            return new ResidentTripSnapshot {Phase=returning ? ResidentTripPhase.Returning : ResidentTripPhase.Outbound,
                SettlementId=-1,BuildingKey=-1,Position=state.MapPosition};
        }
        internal bool VisitsSettlement(int settlement)
        {
            return DestinationSettlement==settlement && Destination!=null ||
                OutboundStops.Exists(s=>s.SettlementId==settlement) || ReturnStops.Exists(s=>s.SettlementId==settlement);
        }
    }
}
