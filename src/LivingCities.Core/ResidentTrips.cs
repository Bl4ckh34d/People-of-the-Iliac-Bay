using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public enum ResidentTripPhase { Home, Outbound, Visiting, Returning, Unavailable, LeavingHome, WaitingHomeGate, ReturningHome }
    public struct ResidentTripSnapshot
    {
        public string VisitId;
        public ResidentTripPhase Phase;
        public int SettlementId;
        public RoutePoint Position;
        public int BuildingKey;
        public ResidentVisitPhase VisitPhase;
        public ResidentVisitPurpose VisitPurpose;
        public double VisitEnterAt,VisitLeaveAt;
        public double InteriorY;
    }
    [Serializable]
    public sealed partial class ResidentTrip
    {
        public string ResidentId;
        public int HomeSettlement,DestinationSettlement;
        public double DepartsAt,VisitSeconds,SecondsPerMapPixel;
        public List<SavedRoutePoint> Road=new List<SavedRoutePoint>();
        public List<SavedRoutePoint> HomeRoute=new List<SavedRoutePoint>();
        public double HomeWalkSeconds;
        public double HomeGateWalkSeconds;
        public bool HomeWalled;
        public ResidentDestination Destination;
        public List<ResidentServiceExit> ServiceExits=new List<ResidentServiceExit>();
        public List<ResidentInnStay> InnStays=new List<ResidentInnStay>();
        public List<ResidentInnReception> InnReceptions=new List<ResidentInnReception>();
        private ResidentVisitJourney destinationJourney;
        // Derived when entering the store, never trusted from serialized input.
        private double travelSeconds;
        private double homeLength;
        public double TravelSeconds { get { return travelSeconds; } }
        private double GateWalk { get { return HomeGateWalkSeconds>0 ? HomeGateWalkSeconds : HomeWalkSeconds; } }
        public double RoadLeavesAt { get { return HomeLeavesAt+HomeWalkSeconds; } }
        public double HomeLeavesAt { get { return HomeWalled ? NextOpen(DepartsAt+GateWalk)-GateWalk : DepartsAt; } }
        public double ArrivesAt { get { return outboundRoad!=null ? outboundRoad.ArrivesAt : RoadLeavesAt+TravelSeconds; } }
        public double ReturnsFromVisitAt { get { return destinationJourney!=null ? destinationJourney.EndsAt : ArrivesAt+VisitSeconds; } }
        public double RoadReturnsAt { get { return returnRoad!=null ? returnRoad.ArrivesAt : ReturnsFromVisitAt+TravelSeconds; } }
        public double HomeEntryAt { get { return HomeWalled ? NextOpen(RoadReturnsAt+HomeWalkSeconds-GateWalk)-(HomeWalkSeconds-GateWalk) : RoadReturnsAt; } }
        public double HomeAt { get { return HomeEntryAt+HomeWalkSeconds; } }
        public static ResidentTrip Create(string resident,int home,int destination,double departure,double visitSeconds,
            double secondsPerMapPixel,IEnumerable<RoutePoint> road)
        {
            if(road==null) throw new ArgumentNullException(nameof(road));
            var trip=new ResidentTrip {ResidentId=resident,HomeSettlement=home,DestinationSettlement=destination,
                DepartsAt=departure,VisitSeconds=visitSeconds,SecondsPerMapPixel=secondsPerMapPixel};
            foreach(var point in road) trip.Road.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
            return CopyValidated(trip);
        }
        public static ResidentTrip CopyValidated(ResidentTrip source)
        {
            if(source==null || string.IsNullOrWhiteSpace(source.ResidentId) || source.HomeSettlement<0 ||
                source.DestinationSettlement<0 || source.HomeSettlement==source.DestinationSettlement ||
                !Time(source.DepartsAt) || !Time(source.VisitSeconds) || source.VisitSeconds<=0 ||
                !Time(source.SecondsPerMapPixel) || source.SecondsPerMapPixel<=0 || source.Road==null ||
                source.Road.Count<2 || source.Road.Count>10000) throw new ArgumentException("Invalid resident trip.");
            var copy=new ResidentTrip {ResidentId=source.ResidentId,HomeSettlement=source.HomeSettlement,
                DestinationSettlement=source.DestinationSettlement,DepartsAt=source.DepartsAt,
                VisitSeconds=source.VisitSeconds,SecondsPerMapPixel=source.SecondsPerMapPixel};
            SavedRoutePoint previous=null;
            double length=0;
            for(int pointIndex=0;pointIndex<source.Road.Count;pointIndex++)
            {
                var point=source.Road[pointIndex];
                int passageNeighbour=PassageOutsideNeighbour(source,pointIndex);
                if(point==null || !Time(point.X) || !Time(point.Z) || point.X>=999.5 || point.Z>=499.5 ||
                    pointIndex>0 && pointIndex<source.Road.Count-1 && !IntegerPoint(point) && passageNeighbour<0)
                    throw new ArgumentException("Invalid map-pixel road point.");
                if(!IntegerPoint(point))
                {
                    var neighbour=source.Road[passageNeighbour>=0 ? passageNeighbour : pointIndex==0 ? 1 : pointIndex-1];
                    if(neighbour==null || passageNeighbour<0 && !IntegerPoint(neighbour) &&
                        (source.Road.Count!=2 || !SameUnitRoadEdge(point,neighbour)))
                        throw new ArgumentException("Trimmed endpoint lacks road node.");
                    double x=Math.Abs(point.X-neighbour.X),z=Math.Abs(point.Z-neighbour.Z);
                    if(x>0.0000001 && z>0.0000001 && Math.Abs(x-z)>0.0000001)
                        throw new ArgumentException("Trimmed endpoint is off its road segment.");
                    if(passageNeighbour>=0 && (x<0.0000001 ? point.X!=Math.Floor(point.X) : z<0.0000001 ? point.Z!=Math.Floor(point.Z) :
                        Math.Abs(((point.X-neighbour.X)*(point.Z-neighbour.Z)>0 ? point.X-point.Z : point.X+point.Z)-
                        Math.Round((point.X-neighbour.X)*(point.Z-neighbour.Z)>0 ? point.X-point.Z : point.X+point.Z))>0.0000001))
                        throw new ArgumentException("Passage join is off the integer road grid.");
                }
                if(previous!=null && (Distance(previous,point)==0 || Math.Abs(point.X-previous.X)>1 || Math.Abs(point.Z-previous.Z)>1))
                    throw new ArgumentException("Resident road route has a disconnected step.");
                if(previous!=null) length+=Distance(previous,point);
                copy.Road.Add(new SavedRoutePoint {X=point.X,Z=point.Z}); previous=point;
            }
            copy.travelSeconds=length*copy.SecondsPerMapPixel;
            if(!Time(source.HomeWalkSeconds) || source.HomeWalkSeconds>=12*3600)
                throw new ArgumentException("Invalid home walk duration.");
            copy.HomeWalkSeconds=source.HomeWalkSeconds; copy.HomeWalled=source.HomeWalled;
            if(!Time(source.HomeGateWalkSeconds) || source.HomeGateWalkSeconds>source.HomeWalkSeconds ||
                source.HomeGateWalkSeconds>0 && !source.HomeWalled) throw new ArgumentException("Invalid gate crossing duration.");
            copy.HomeGateWalkSeconds=source.HomeGateWalkSeconds;
            foreach(var point in source.HomeRoute ?? new List<SavedRoutePoint>())
            {
                if(point==null || double.IsNaN(point.X) || double.IsInfinity(point.X) ||
                    double.IsNaN(point.Z) || double.IsInfinity(point.Z) || copy.HomeRoute.Count>=10000)
                    throw new ArgumentException("Invalid home route point.");
                if(copy.HomeRoute.Count>0) copy.homeLength+=Distance(copy.HomeRoute[copy.HomeRoute.Count-1],point);
                copy.HomeRoute.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
            }
            if(copy.HomeRoute.Count==0 ? copy.HomeWalkSeconds!=0 || copy.HomeWalled :
                copy.HomeRoute.Count<2 || copy.HomeWalkSeconds<=0 || copy.homeLength<=0 || !Time(copy.homeLength))
                throw new ArgumentException("Home route and duration disagree.");
            copy.CopyRoadStops(source);
            ResidentDestination timingDestination=null;
            bool hasExits=source.ServiceExits!=null && source.ServiceExits.Exists(e=>!IsRoadExit(e));
            if(source.Destination!=null)
            {
                copy.Destination=ResidentVisitJourney.Copy(source.Destination);
                if(copy.Destination.ThroughRoute) throw new ArgumentException("Primary destination must return to its road join.");
                timingDestination=hasExits ? ResidentVisitJourney.Copy(copy.Destination) : copy.Destination;
                // Reservation bounds describe the final interrupted itinerary,
                // not the provisional schedule used to validate each exit.
                if(hasExits) foreach(var stop in timingDestination.Stops) stop.KeepRoomUntil=0;
                copy.destinationJourney=ResidentVisitJourney.Create(copy.ArrivesAt,timingDestination);
            }
            foreach(var item in source.ServiceExits ?? new List<ResidentServiceExit>())
            {
                if(IsRoadExit(item)) continue;
                var exit=ResidentServiceExit.Copy(item);
                exit.SettlementId=ResolveVisitSettlement(exit.SettlementId,copy.DestinationSettlement);
                exit.VisitId=copy.ResolveVisitId(exit.VisitId,exit.BuildingKey,exit.VisitEnterAt);
                if(copy.destinationJourney==null || copy.PrimaryExits().Count>=32) throw new ArgumentException("Service exit lacks destination.");
                ResidentVisitSnapshot scheduled=default(ResidentVisitSnapshot);
                foreach(var visit in copy.destinationJourney.ScheduledVisits())
                    if(visit.BuildingKey==exit.BuildingKey && ResidentVisitJourney.SameTime(visit.EnterAt,exit.VisitEnterAt)) {scheduled=visit;break;}
                if(scheduled.BuildingKey!=exit.BuildingKey || !ResidentVisitJourney.SameTime(scheduled.EnterAt,exit.VisitEnterAt) ||
                    !ResidentVisitJourney.SameTime(scheduled.LeaveAt,exit.VisitLeaveAt) || exit.StartedAt+0.001<scheduled.EnterAt || exit.StartedAt>=scheduled.LeaveAt+0.001 ||
                    copy.ServiceExits.Exists(e=>ResidentVisitJourney.SameTime(e.VisitEnterAt,exit.VisitEnterAt))) throw new ArgumentException("Service exit does not match itinerary.");
                copy.ServiceExits.Add(exit);
                copy.destinationJourney=ResidentVisitJourney.Create(copy.ArrivesAt,timingDestination,copy.PrimaryExits());
            }
            if(copy.Destination!=null && hasExits)
                copy.destinationJourney=ResidentVisitJourney.Create(copy.ArrivesAt,copy.Destination,copy.PrimaryExits());
            copy.BuildReturnRoad();
            foreach(var item in source.InnReceptions ?? new List<ResidentInnReception>())
            {
                var reception=ResidentInnReception.Copy(item);
                var scoped=copy.ResolveScopedVisit(reception.VisitId,reception.SettlementId,reception.BuildingKey,reception.VisitEnterAt);
                reception.SettlementId=scoped.Settlement;reception.VisitId=scoped.Id;
                if(copy.InnReceptions.Count>=96) throw new ArgumentException("Too many inn receptions.");
                var scheduled=scoped.Visit;
                var interrupted=copy.ServiceExits.Find(e=>e.VisitId==reception.VisitId);
                double leave=interrupted==null ? scheduled.LeaveAt : interrupted.VisitLeaveAt;
                if(scheduled.Purpose!=ResidentVisitPurpose.Inn || scheduled.BuildingKey!=reception.BuildingKey ||
                    !ResidentVisitJourney.SameTime(scheduled.EnterAt,reception.VisitEnterAt) || !ResidentVisitJourney.SameTime(leave,reception.VisitLeaveAt) ||
                    copy.InnReceptions.Exists(r=>r.VisitId==reception.VisitId)) throw new ArgumentException("Reception does not match itinerary.");
                copy.InnReceptions.Add(reception);
            }
            foreach(var item in source.InnStays ?? new List<ResidentInnStay>())
            {
                var stay=ResidentInnStay.Copy(item);
                var scoped=copy.ResolveScopedVisit(stay.VisitId,stay.SettlementId,stay.BuildingKey,stay.EnterAt);
                stay.SettlementId=scoped.Settlement;stay.VisitId=scoped.Id;
                var stopped=copy.ServiceExits.Find(e=>e.VisitId==stay.VisitId);
                if(copy.InnStays.Count>=96 || stay.LeaveAt-stay.EnterAt>60*86400 ||
                    (scoped.Id.StartsWith("destination/",StringComparison.Ordinal) ? stay.LeaveAt-copy.ArrivesAt>60*86400 :
                        !ResidentVisitJourney.SameTime(stay.LeaveAt,stopped==null ? scoped.Visit.RoomUntil : stopped.VisitLeaveAt)) ||
                    scoped.Visit.Purpose!=ResidentVisitPurpose.Inn || copy.InnStays.Exists(s=>s.VisitId==stay.VisitId))
                    throw new ArgumentException("Inn stay does not belong to resident itinerary.");
                copy.InnStays.Add(stay);
                var reception=copy.InnReceptions.Find(r=>r.VisitId==stay.VisitId);
                // Native JSON rounds absolute game timestamps; at year-scale values
                // recomputing route/service completion can differ by microseconds.
                if(reception!=null && stay.AttemptedAt+0.001<reception.ServiceEndsAt) throw new ArgumentException("Admission predates counter service: admission="+
                    stay.AttemptedAt.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"; service="+
                    reception.ServiceEndsAt.ToString("R",System.Globalization.CultureInfo.InvariantCulture));
            }
            if(Distance(copy.Road[0],copy.Road[copy.Road.Count-1])==0 || !Time(copy.HomeAt) ||
                copy.ArrivesAt<=copy.DepartsAt || copy.ReturnsFromVisitAt<=copy.ArrivesAt || copy.HomeAt<=copy.ReturnsFromVisitAt)
                throw new ArgumentException("Resident trip has invalid time or endpoints.");
            return copy;
        }
        private static string DestinationVisitId(int index)
        {return index<0 ? null : "destination/"+index.ToString(System.Globalization.CultureInfo.InvariantCulture);}
        internal string ResolveVisitId(string saved,int building,double entered)
        {
            if(destinationJourney!=null)
                foreach(var visit in destinationJourney.ScheduledVisits())
                    if(visit.BuildingKey==building && ResidentVisitJourney.SameTime(visit.EnterAt,entered))
                    {
                        string expected=DestinationVisitId(visit.VisitIndex);
                        if(saved!=null && saved!=expected) throw new ArgumentException("Visit ID does not match itinerary.");
                        return expected;
                    }
            throw new ArgumentException("Visit ID lacks matching itinerary entry.");
        }
        internal static int ResolveVisitSettlement(int? saved,int expected)
        {
            if(expected<0 || saved.HasValue && saved.Value!=expected)
                throw new ArgumentException("Visit belongs to another settlement.");
            return expected;
        }
        internal double InnCheckout(ResidentTripSnapshot visit)
        {
            foreach(var scoped in AllScheduledVisits())
                if(scoped.Id==visit.VisitId && scoped.Settlement==visit.SettlementId && scoped.Visit.BuildingKey==visit.BuildingKey)
                    return scoped.Visit.RoomUntil;
            return visit.VisitLeaveAt;
        }
        internal IEnumerable<ResidentTripSnapshot> ScheduledInnVisits()
        {
            foreach(var scoped in AllScheduledVisits())
            {
                var visit=scoped.Visit;
                if(visit.Purpose==ResidentVisitPurpose.Inn)
                    yield return new ResidentTripSnapshot {VisitId=scoped.Id,Phase=ResidentTripPhase.Visiting,SettlementId=scoped.Settlement,
                        BuildingKey=visit.BuildingKey,VisitPhase=ResidentVisitPhase.Inside,VisitPurpose=visit.Purpose,
                        Position=visit.Position,VisitEnterAt=visit.EnterAt,VisitLeaveAt=visit.LeaveAt};
            }
        }
        public ResidentTripSnapshot Evaluate(double now)
        {
            if(!Time(now)) throw new ArgumentException("Invalid trip time.");
            return EvaluateTimeline(now);
        }
        public ResidentTripSnapshot Evaluate(double now,ObservedVisit observedVisit)
        {
            if(!Time(now)) throw new ArgumentException("Invalid trip time.");
            ScopedVisit observedStop; double shift;
            if(!TryObservedVisit(observedVisit,out observedStop,out shift) || now<observedVisit.ObservedAt)
                return EvaluateTimeline(now);
            if(now<observedVisit.EndsAt)
            {
                var point=observedVisit.Evaluate(now).Position;
                return new ResidentTripSnapshot {VisitId=observedStop.Id,Phase=ResidentTripPhase.Visiting,
                    SettlementId=observedStop.Settlement,BuildingKey=observedVisit.BuildingKey,VisitPhase=ResidentVisitPhase.Inside,
                    Position=new RoutePoint(point.X,point.Z),InteriorY=point.Y,VisitPurpose=observedStop.Visit.Purpose,
                    VisitEnterAt=observedStop.Visit.EnterAt,VisitLeaveAt=observedStop.Visit.LeaveAt};
            }
            return EvaluateTimeline(now-shift);
        }
        private bool TryObservedVisit(ObservedVisit observedVisit,out ScopedVisit matchingStop,out double shift)
        {
            matchingStop=default(ScopedVisit);shift=0;
            if(observedVisit==null || observedVisit.ResidentId!=ResidentId || observedVisit.ResumeUntil>HomeAt ||
                !ResidentVisitJourney.SameTime(observedVisit.ResumeUntil,HomeAt))
                return false;
            foreach(var scoped in AllScheduledVisits())
            {
                var stop=scoped.Visit;
                if(scoped.Settlement!=observedVisit.SettlementId || stop.BuildingKey!=observedVisit.BuildingKey ||
                    stop.Purpose!=ResidentVisitPurpose.Shop && stop.Purpose!=ResidentVisitPurpose.Social ||
                    !ResidentVisitJourney.SameTime(stop.LeaveAt,observedVisit.PlannedEndAt) || observedVisit.ObservedAt<stop.EnterAt-0.001)
                    continue;
                if(ServiceExits.Exists(exit=>exit.VisitId==scoped.Id)) return false;
                matchingStop=scoped;shift=observedVisit.EndsAt-observedVisit.PlannedEndAt;
                return !double.IsNaN(shift) && !double.IsInfinity(shift);
            }
            return false;
        }
        internal double HomeAtFor(double now,ObservedVisit observedVisit)
        {
            ScopedVisit stop;double shift;
            return observedVisit!=null && now>=observedVisit.ObservedAt && TryObservedVisit(observedVisit,out stop,out shift) ? HomeAt+shift : HomeAt;
        }
        private ResidentTripSnapshot EvaluateTimeline(double now)
        {
            double travel=TravelSeconds,arrival=ArrivesAt,returnAt=ReturnsFromVisitAt;
            if(now<HomeLeavesAt || now>=HomeAt)
                return State(ResidentTripPhase.Home,HomeSettlement,Road[0]);
            if(now<RoadLeavesAt) return HomeSample(ResidentTripPhase.LeavingHome,(now-HomeLeavesAt)/HomeWalkSeconds);
            if(now>=RoadReturnsAt)
                return now<HomeEntryAt ? HomeSample(ResidentTripPhase.WaitingHomeGate,1) :
                    HomeSample(ResidentTripPhase.ReturningHome,1-(now-HomeEntryAt)/HomeWalkSeconds);
            if(now>=arrival && now<returnAt)
            {
                if(destinationJourney!=null)
                {
                    var visit=destinationJourney.Evaluate(now);
                    var departure=ServiceExits.Find(e=>now>=e.StartedAt && now<e.ExitAt);
                    if(departure!=null)
                    {
                        bool inside=now<departure.ExitAt;
                        var point=departure.Position(now);
                        return new ResidentTripSnapshot {VisitId=departure.VisitId,Phase=ResidentTripPhase.Visiting,SettlementId=DestinationSettlement,
                            BuildingKey=inside ? departure.BuildingKey : -1,
                            VisitPhase=inside ? ResidentVisitPhase.ExitingService : ResidentVisitPhase.WaitingToLeave,
                            Position=inside ? new RoutePoint(point.X,point.Z) : visit.Position,InteriorY=inside ? point.Y : 0,
                            VisitPurpose=visit.Purpose,VisitEnterAt=visit.EnterAt,VisitLeaveAt=visit.LeaveAt};
                    }
                    return new ResidentTripSnapshot {VisitId=DestinationVisitId(visit.VisitIndex),Phase=ResidentTripPhase.Visiting,SettlementId=DestinationSettlement,
                        BuildingKey=visit.BuildingKey,VisitPhase=visit.Phase,Position=visit.Position,
                        VisitPurpose=visit.Purpose,VisitEnterAt=visit.EnterAt,VisitLeaveAt=visit.LeaveAt};
                }
                return State(ResidentTripPhase.Visiting,DestinationSettlement,Road[Road.Count-1]);
            }
            bool returning=now>=returnAt;
            if(!returning && outboundRoad!=null) return RoadState(outboundRoad,OutboundStops,now,false);
            if(returning && returnRoad!=null) return RoadState(returnRoad,ReturnStops,now,true);
            double distance=(returning ? travel-(now-returnAt) : now-RoadLeavesAt)/SecondsPerMapPixel;
            for(int i=1;i<Road.Count;i++)
            {
                var a=Road[i-1]; var b=Road[i]; double segment=Distance(a,b);
                if(distance<=segment)
                    return new ResidentTripSnapshot {Phase=returning?ResidentTripPhase.Returning:ResidentTripPhase.Outbound,
                        SettlementId=-1,BuildingKey=-1,Position=new RoutePoint(a.X+(b.X-a.X)*distance/segment,a.Z+(b.Z-a.Z)*distance/segment)};
                distance-=segment;
            }
            return State(returning?ResidentTripPhase.Returning:ResidentTripPhase.Outbound,-1,Road[Road.Count-1]);
        }
        private ResidentTripSnapshot HomeSample(ResidentTripPhase phase,double progress)
        {
            double along=Math.Max(0,Math.Min(1,progress))*homeLength;
            for(int i=1;i<HomeRoute.Count;i++)
            {
                var a=HomeRoute[i-1]; var b=HomeRoute[i]; double length=Distance(a,b);
                if(length>0 && along<=length) return new ResidentTripSnapshot {Phase=phase,SettlementId=HomeSettlement,BuildingKey=-1,
                    Position=new RoutePoint(a.X+(b.X-a.X)*along/length,a.Z+(b.Z-a.Z)*along/length)};
                along-=length;
            }
            return State(phase,HomeSettlement,HomeRoute[HomeRoute.Count-1]);
        }
        private static double NextOpen(double time)
        {
            double day=Math.Floor(time/86400)*86400;
            if(time<day+6*3600) return day+6*3600;
            if(time>=day+18*3600) return day+30*3600;
            return time;
        }
        private static ResidentTripSnapshot State(ResidentTripPhase phase,int settlement,SavedRoutePoint point)
        { return new ResidentTripSnapshot {Phase=phase,SettlementId=settlement,BuildingKey=-1,Position=new RoutePoint(point.X,point.Z)}; }
        private static double Distance(SavedRoutePoint a,SavedRoutePoint b)
        { double x=b.X-a.X,z=b.Z-a.Z; return Math.Sqrt(x*x+z*z); }
        private static bool Time(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value>=0; }
        private static bool IntegerPoint(SavedRoutePoint point) { return point.X==Math.Floor(point.X) && point.Z==Math.Floor(point.Z); }
        private static int PassageOutsideNeighbour(ResidentTrip trip,int index)
        {
            if(trip.OutboundStops==null || trip.ReturnStops==null) return -1;
            foreach(var stop in trip.OutboundStops)
            {
                if(stop==null || !stop.ExitRoadNode.HasValue || stop.RoadNode<1 || stop.ExitRoadNode.Value>=trip.Road.Count-1 ||
                    stop.ExitRoadNode.Value<=stop.RoadNode || index!=stop.RoadNode && index!=stop.ExitRoadNode.Value) continue;
                int reverseEntry=trip.Road.Count-1-stop.ExitRoadNode.Value,reverseExit=trip.Road.Count-1-stop.RoadNode;
                if(trip.ReturnStops.Exists(s=>s!=null && s.RoadNode==reverseEntry && s.ExitRoadNode==reverseExit && s.SettlementId==stop.SettlementId))
                    return index==stop.RoadNode ? index-1 : index+1;
            }
            return -1;
        }
        private static bool SameUnitRoadEdge(SavedRoutePoint a,SavedRoutePoint b)
        {
            if(!Time(b.X) || !Time(b.Z)) return false;
            double dx=b.X-a.X,dz=b.Z-a.Z;
            // Both endpoints must lie inside the same unit edge between integer
            // road nodes. Crossing an intermediate node still requires saving it.
            if(Math.Floor(a.X)!=Math.Floor(b.X) || Math.Floor(a.Z)!=Math.Floor(b.Z)) return false;
            if(Math.Abs(dz)<0.0000001) return a.Z==Math.Floor(a.Z);
            if(Math.Abs(dx)<0.0000001) return a.X==Math.Floor(a.X);
            if(Math.Abs(Math.Abs(dx)-Math.Abs(dz))>0.0000001) return false;
            double gridLine=dx*dz>0 ? a.X-a.Z : a.X+a.Z;
            return Math.Abs(gridLine-Math.Round(gridLine))<0.0000001;
        }
    }
    public sealed partial class WorldStateStore
    {
        private readonly Dictionary<string,ResidentTrip> residentTripIndex=new Dictionary<string,ResidentTrip>(StringComparer.Ordinal);
        private void IndexResidentTrips()
        {
            residentTripIndex.Clear();
            foreach(var trip in state.ResidentTrips) residentTripIndex.Add(trip.ResidentId,trip);
        }
        // One current/latest trip per existing resident bounds history without
        // allocating a second person. Completed trips can be replaced, not overlapped.
        public void ScheduleResidentTrip(ResidentTrip trip)
        {
            var copy=ResidentTrip.CopyValidated(trip);
            ValidateResidentInnBookings(state,copy);
            var person=TripResident(state,copy.HomeSettlement,copy.ResidentId);
            Require(person!=null && person.IsAlive && person.AgeGroup==ResidentAgeGroup.Adult && !person.IsQuestProtected,
                "Trip requires an available adult resident.");
            residentTripIndex.TryGetValue(copy.ResidentId,out var previous);
            var observed=previous==null ? null : IndexedObservedVisit(copy.ResidentId);
            Require(previous==null || copy.DepartsAt>=previous.HomeAtFor(copy.DepartsAt,observed),"Resident already has an overlapping trip.");
            state.ResidentTrips.RemoveAll(t=>t.ResidentId==copy.ResidentId); state.ResidentTrips.Add(copy);
            residentTripIndex[copy.ResidentId]=copy;
            RemoveObservedVisit(copy.ResidentId);
        }
        public bool IsResidentAway(string id,double now)
        {
            Require(DefenseTime(now),"Invalid resident absence time.");
            residentTripIndex.TryGetValue(id,out var trip);
            var observed=trip==null ? null : IndexedObservedVisit(trip.ResidentId);
            return trip!=null && now>=trip.HomeLeavesAt && now<trip.HomeAtFor(now,observed);
        }
        public ResidentTrip GetResidentDialogueTrip(string id)
        {
            if(string.IsNullOrEmpty(id) || !residentTripIndex.TryGetValue(id,out var trip)) return null;
            return ResidentTrip.CopyValidated(trip);
        }
        public double ResidentTripResumeUntil(string id,double fallback)
        {
            return residentTripIndex.TryGetValue(id,out var trip) ? trip.HomeAt : fallback;
        }
        public void SaveResidentServiceExit(string id,ResidentServiceExit exit)
        {
            exit=ResidentServiceExit.Copy(exit);
            ResidentTrip original;
            Require(residentTripIndex.TryGetValue(id,out original),"Resident has no trip.");
            if(ResidentTrip.IsRoadExit(exit))
            {
                SaveResidentRoadExit(id,original,exit);
                RemoveObservedVisit(id);
                return;
            }
            exit.SettlementId=ResidentTrip.ResolveVisitSettlement(exit.SettlementId,original.DestinationSettlement);
            exit.VisitId=original.ResolveVisitId(exit.VisitId,exit.BuildingKey,exit.VisitEnterAt);
            Require(TripResident(state,original.HomeSettlement,id).IsAlive,"Dead resident cannot leave a service.");
            var copy=ResidentTrip.CopyValidated(original);
            if(copy.ServiceExits.Exists(e=>e.VisitId==exit.VisitId))
            {
                RemoveObservedVisit(id);
                return;
            }
            bool refused=copy.InnStays.Exists(s=>s.BuildingKey==exit.BuildingKey && ResidentVisitJourney.SameTime(s.EnterAt,exit.VisitEnterAt) && s.Admission==TravelerAdmission.Refused);
            if(refused)
            {
                var visits=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(copy.ArrivesAt,copy.Destination,copy.PrimaryExits()).ScheduledVisits());
                int index=visits.FindIndex(v=>v.BuildingKey==exit.BuildingKey && ResidentVisitJourney.SameTime(v.EnterAt,exit.VisitEnterAt) && v.Purpose==ResidentVisitPurpose.Inn);
                Require(index>=0 && !copy.InnStays.Exists(s=>s.EnterAt>exit.VisitEnterAt && !ResidentVisitJourney.SameTime(s.EnterAt,exit.VisitEnterAt)) &&
                    !copy.ServiceExits.Exists(e=>e.VisitEnterAt>exit.VisitEnterAt),
                    "Cannot replace already processed future visits after inn refusal.");
                var route=new List<SavedRoutePoint>(); double seconds=0;
                for(int stop=index;stop>=0;stop--)
                {
                    var leg=copy.Destination.Stops[stop];seconds+=leg.WalkSeconds;
                    for(int point=leg.Route.Count-1;point>=0;point--)
                        route.Add(new SavedRoutePoint {X=leg.Route[point].X,Z=leg.Route[point].Z});
                }
                // Follow already surveyed street legs back to the regional road.
                // The saved interior exit remains authoritative until the door.
                copy.Destination.Stops[index].KeepRoomUntil=0;
                copy.Destination.Stops.RemoveRange(index+1,copy.Destination.Stops.Count-index-1);
                // Future reception routes are plans, not completed payments or exits.
                copy.InnReceptions.RemoveAll(r=>r.VisitEnterAt>exit.VisitEnterAt && !ResidentVisitJourney.SameTime(r.VisitEnterAt,exit.VisitEnterAt));
                copy.Destination.ExitRoute=route;copy.Destination.ExitWalkSeconds=seconds;
                copy.Destination.ExitGateSeconds=copy.Destination.Walled ? seconds-copy.Destination.EntryGateSeconds : 0;
            }
            if(!refused && copy.InnReceptions.Exists(r=>r.VisitEnterAt>exit.VisitEnterAt && !ResidentVisitJourney.SameTime(r.VisitEnterAt,exit.VisitEnterAt)))
                RetimeFutureReceptions(copy,exit);
            else copy.ServiceExits.Add(exit);
            copy=ResidentTrip.CopyValidated(copy);
            state.ResidentTrips.RemoveAll(t=>t.ResidentId==id); state.ResidentTrips.Add(copy); residentTripIndex[id]=copy;
            RemoveObservedVisit(id);
        }
        private static void RetimeFutureReceptions(ResidentTrip trip,ResidentServiceExit exit)
        {
            Require(!trip.InnStays.Exists(s=>s.EnterAt>exit.VisitEnterAt) && !trip.ServiceExits.Exists(e=>e.VisitEnterAt>exit.VisitEnterAt),
                "Cannot retime completed future visits.");
            var before=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(trip.ArrivesAt,trip.Destination,trip.PrimaryExits()).ScheduledVisits());
            var holds=new List<double>();
            foreach(var stop in trip.Destination.Stops) {holds.Add(stop.KeepRoomUntil);stop.KeepRoomUntil=0;}
            trip.ServiceExits.Add(exit);
            var after=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(trip.ArrivesAt,trip.Destination,trip.PrimaryExits()).ScheduledVisits());
            for(int i=0;i<holds.Count;i++)
            {
                if(holds[i]<=0) continue;
                int checkout=before.FindIndex(v=>ResidentVisitJourney.SameTime(v.LeaveAt,holds[i]));
                if(checkout<0) checkout=i;
                trip.Destination.Stops[i].KeepRoomUntil=holds[i]+after[checkout].LeaveAt-before[checkout].LeaveAt;
            }
            var receptions=trip.InnReceptions;trip.InnReceptions=new List<ResidentInnReception>();
            var timed=ResidentTrip.CopyValidated(trip);trip.InnReceptions=receptions;
            foreach(var reception in trip.InnReceptions)
            {
                if(reception.VisitEnterAt<=exit.VisitEnterAt || ResidentVisitJourney.SameTime(reception.VisitEnterAt,exit.VisitEnterAt)) continue;
                var next=timed.VisitById(reception.VisitId).Visit;
                double offset=reception.StartedAt-reception.VisitEnterAt;
                reception.VisitEnterAt=next.EnterAt;reception.VisitLeaveAt=next.LeaveAt;
                reception.StartedAt=reception.VisitEnterAt+offset;
            }
        }
        public List<Resident> RoadResidents(double now)
        {
            Require(DefenseTime(now),"Invalid road resident time.");
            var result=new List<Resident>();
            foreach(var trip in residentTripIndex.Values)
            {
                var phase=trip.Evaluate(now,IndexedObservedVisit(trip.ResidentId)).Phase;
                if(phase!=ResidentTripPhase.Outbound && phase!=ResidentTripPhase.Returning) continue;
                var person=TripResident(state,trip.HomeSettlement,trip.ResidentId);
                if(person!=null && person.IsAlive) result.Add(person);
            }
            return result;
        }
        // Destination actors borrow the original household resident. Never create
        // a visitor identity or insert that person into a second household.
        public List<Resident> DestinationResidents(int settlement,double now)
        {
            Require(DefenseTime(now),"Invalid destination resident time.");
            var result=new List<Resident>();
            foreach(var trip in residentTripIndex.Values)
            {
                if(!trip.VisitsSettlement(settlement)) continue;
                var visit=trip.Evaluate(now,IndexedObservedVisit(trip.ResidentId));
                if(visit.Phase!=ResidentTripPhase.Visiting || visit.SettlementId!=settlement) continue;
                var person=TripResident(state,trip.HomeSettlement,trip.ResidentId);
                if(person!=null && person.IsAlive) result.Add(person);
            }
            return result;
        }
        public ResidentTripSnapshot ResidentTripAt(string id,double now)
        {
            residentTripIndex.TryGetValue(id,out var trip);
            Require(trip!=null,"Resident has no trip.");
            var result=trip.Evaluate(now,IndexedObservedVisit(trip.ResidentId));
            var person=TripResident(state,trip.HomeSettlement,id);
            if(person!=null && !person.IsAlive)
            { result.Phase=ResidentTripPhase.Unavailable; result.SettlementId=-1; result.BuildingKey=-1; }
            return result;
        }
        public bool TryDestinationResidentState(string id,int settlement,double now,out ActivitySnapshot activity)
        {
            Require(DefenseTime(now),"Invalid destination resident time.");
            activity=default(ActivitySnapshot);
            ResidentTrip trip;
            if(!residentTripIndex.TryGetValue(id,out trip) || !trip.VisitsSettlement(settlement)) return false;
            var person=TripResident(state,trip.HomeSettlement,id);
            if(person==null || !person.IsAlive) return false;
            var visit=trip.Evaluate(now,IndexedObservedVisit(trip.ResidentId));
            if(visit.Phase!=ResidentTripPhase.Visiting || visit.SettlementId!=settlement) return false;
            activity=new ActivitySnapshot(visit.BuildingKey>=0 ? Activity.Visit : Activity.Travel,
                visit.BuildingKey,visit.Position,0);
            return true;
        }
        public ActivitySnapshot LocalResidentState(string id,double now,ActivitySnapshot scheduled)
        {
            if(!IsResidentAway(id,now)) return scheduled;
            var trip=residentTripIndex[id]; var position=trip.Evaluate(now,IndexedObservedVisit(trip.ResidentId));
            return new ActivitySnapshot(IsHomeWalk(position.Phase) ? Activity.Travel : Activity.Visit,-1,
                IsHomeWalk(position.Phase) ? position.Position : new RoutePoint(0,0),0);
        }
        public bool IsResidentOnHomeRoute(string id,double now)
        { ResidentTrip trip; return residentTripIndex.TryGetValue(id,out trip) && IsHomeWalk(trip.Evaluate(now,IndexedObservedVisit(trip.ResidentId)).Phase); }
        private static bool IsHomeWalk(ResidentTripPhase phase)
        { return phase==ResidentTripPhase.LeavingHome || phase==ResidentTripPhase.ReturningHome || phase==ResidentTripPhase.WaitingHomeGate; }
        private static Resident TripResident(WorldState world,int settlement,string id)
        {
            var town=world.Settlements.Find(s=>s.SettlementId==settlement);
            if(town==null) return null;
            foreach(var home in town.Households)
            { var person=home.Residents.Find(r=>r.Id==id); if(person!=null) return person; }
            foreach(var retired in world.HouseholdHistory)
                if(retired.SettlementId==settlement)
                { var person=retired.Household.Residents.Find(r=>r.Id==id); if(person!=null) return person; }
            return null;
        }
        private static List<ResidentTrip> CopyResidentTrips(WorldState source,WorldState copy)
        {
            var trips=new List<ResidentTrip>();
            if(source.Version<12) return trips;
            Require(source.ResidentTrips!=null,"Missing resident trip ledger.");
            var owners=new HashSet<string>(StringComparer.Ordinal);
            foreach(var trip in source.ResidentTrips)
            {
                var saved=ResidentTrip.CopyValidated(trip);
                Require(source.Version>=16 || saved.OutboundStops.Count==0 && saved.ReturnStops.Count==0,"Road stops require save schema 16.");
                Require(source.Version>=17 || !saved.OutboundStops.Exists(s=>s.ExitRoadNode.HasValue) && !saved.ReturnStops.Exists(s=>s.ExitRoadNode.HasValue),
                    "Through-town stops require save schema 17.");
                var person=TripResident(copy,saved.HomeSettlement,saved.ResidentId);
                Require(person!=null && person.AgeGroup==ResidentAgeGroup.Adult && owners.Add(saved.ResidentId),
                    "Invalid or duplicate resident trip owner.");
                ValidateResidentInnBookings(copy,saved);
                trips.Add(saved);
            }
            return trips;
        }
    }
}
