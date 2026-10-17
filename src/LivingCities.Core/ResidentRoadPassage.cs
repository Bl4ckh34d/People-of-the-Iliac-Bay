using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    public static class ResidentRoadPassage
    {
        // Planning only. Call before committing a trip or processing any admission.
        public static ResidentTrip Attach(ResidentTrip source,int node,int settlement,RoutePoint entry,RoutePoint exit,
            ResidentDestination local,double returnDwell)
        {
            var trip=ResidentTrip.CopyValidated(source);
            if(node<1 || node>=trip.Road.Count-1 || trip.InnStays.Count!=0 || trip.ServiceExits.Count!=0 ||
                settlement<0 || settlement==trip.HomeSettlement || settlement==trip.DestinationSettlement)
                throw new ArgumentException("Road passage requires an unprocessed intermediate trip node.");
            if(!OnEdge(trip.Road[node-1],trip.Road[node],entry) || !OnEdge(trip.Road[node],trip.Road[node+1],exit))
                throw new ArgumentException("Road joins must lie inside the original adjacent edges.");
            var visit=ResidentVisitJourney.Copy(local);
            if(!visit.ThroughRoute || visit.Stops.Count!=1 || visit.Stops[0].Purpose!=ResidentVisitPurpose.Inn || visit.Stops[0].KeepRoomUntil!=0)
                throw new ArgumentException("Passage requires one through-town inn visit without advance room holds.");
            var reverse=new ResidentDestination {ThroughRoute=true,Walled=visit.Walled,ExitWalkSeconds=visit.Stops[0].WalkSeconds,
                EntryGateSeconds=visit.Walled ? visit.ExitWalkSeconds-visit.ExitGateSeconds : 0,
                ExitGateSeconds=visit.Walled ? visit.Stops[0].WalkSeconds-visit.EntryGateSeconds : 0,
                ExitRoute=Reverse(visit.Stops[0].Route)};
            reverse.Stops.Add(new ResidentVisitStop {BuildingKey=visit.Stops[0].BuildingKey,Purpose=ResidentVisitPurpose.Inn,
                OpenMinute=visit.Stops[0].OpenMinute,CloseMinute=visit.Stops[0].CloseMinute,WalkSeconds=visit.ExitWalkSeconds,
                DwellSeconds=returnDwell,Route=Reverse(visit.ExitRoute)});
            reverse=ResidentVisitJourney.Copy(reverse);
            var receptions=trip.InnReceptions;trip.InnReceptions=new List<ResidentInnReception>();
            var before=trip.Destination==null ? new List<ResidentVisitSnapshot>() :
                new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(trip.ArrivesAt,trip.Destination).ScheduledVisits());
            var holds=new List<double>();
            if(trip.Destination!=null) foreach(var stop in trip.Destination.Stops) {holds.Add(stop.KeepRoomUntil);stop.KeepRoomUntil=0;}
            int returnNode=trip.Road.Count-1-node;
            Remap(trip.OutboundStops,node);Remap(trip.ReturnStops,returnNode);
            trip.Road[node]=new SavedRoutePoint {X=entry.X,Z=entry.Z};trip.Road.Insert(node+1,new SavedRoutePoint {X=exit.X,Z=exit.Z});
            trip.OutboundStops.Add(new ResidentRoadStop {Id=FreeId(trip.OutboundStops,"outbound/"),RoadNode=node,ExitRoadNode=node+1,SettlementId=settlement,Visit=visit});
            trip.ReturnStops.Add(new ResidentRoadStop {Id=FreeId(trip.ReturnStops,"return/"),RoadNode=returnNode,ExitRoadNode=returnNode+1,SettlementId=settlement,Visit=reverse});
            trip.OutboundStops.Sort((a,b)=>a.RoadNode.CompareTo(b.RoadNode));trip.ReturnStops.Sort((a,b)=>a.RoadNode.CompareTo(b.RoadNode));
            trip=ResidentTrip.CopyValidated(trip);
            if(trip.Destination!=null)
            {
                var after=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(trip.ArrivesAt,trip.Destination).ScheduledVisits());
                for(int i=0;i<holds.Count;i++)
                {
                    if(holds[i]==0) continue;
                    int target=before.FindIndex(v=>ResidentVisitJourney.SameTime(v.LeaveAt,holds[i]));
                    if(target<0) throw new ArgumentException("Cannot retime an unbound planned room checkout.");
                    trip.Destination.Stops[i].KeepRoomUntil=after[target].LeaveAt;
                }
                trip=ResidentTrip.CopyValidated(trip);
            }
            foreach(var reception in receptions)
            {
                var next=trip.VisitById(reception.VisitId).Visit;double offset=reception.StartedAt-reception.VisitEnterAt;
                reception.VisitEnterAt=next.EnterAt;reception.VisitLeaveAt=next.LeaveAt;reception.StartedAt=next.EnterAt+offset;
                trip.InnReceptions.Add(reception);
            }
            return ResidentTrip.CopyValidated(trip);
        }
        private static void Remap(List<ResidentRoadStop> stops,int node)
        {
            foreach(var stop in stops)
            {
                int end=stop.ExitRoadNode ?? stop.RoadNode;
                if(node>=stop.RoadNode && node<=end) throw new ArgumentException("New passage overlaps an existing stop.");
                if(stop.RoadNode>node) stop.RoadNode++;
                if(stop.ExitRoadNode.HasValue && stop.ExitRoadNode.Value>node) stop.ExitRoadNode++;
            }
        }
        private static string FreeId(List<ResidentRoadStop> stops,string prefix)
        {
            for(int i=0;i<32;i++) {string id=prefix+i.ToString(System.Globalization.CultureInfo.InvariantCulture);if(!stops.Exists(s=>s.Id==id)) return id;}
            throw new ArgumentException("Road stop limit reached.");
        }
        private static List<SavedRoutePoint> Reverse(List<SavedRoutePoint> points)
        {var result=new List<SavedRoutePoint>();for(int i=points.Count-1;i>=0;i--) result.Add(new SavedRoutePoint{X=points[i].X,Z=points[i].Z});return result;}
        private static bool OnEdge(SavedRoutePoint a,SavedRoutePoint b,RoutePoint point)
        {
            double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;
            double fraction=((point.X-a.X)*dx+(point.Z-a.Z)*dz)/length;
            return length>0 && fraction>0 && fraction<1 && Math.Abs(a.X+fraction*dx-point.X)<0.000001 && Math.Abs(a.Z+fraction*dz-point.Z)<0.000001;
        }
    }
}
