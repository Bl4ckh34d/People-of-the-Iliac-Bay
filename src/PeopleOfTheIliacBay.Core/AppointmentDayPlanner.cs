using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Projects one day's active, building-based appointments onto verified edges
    // from the resident's saved routine. It never invents a route between buildings.
    public static partial class AppointmentDayPlanner
    {
        private sealed class Edge
        {
            public int To;
            public List<SavedRoutePoint> Points;
            public double Metres;
        }
        private sealed class RouteGraph
        {
            public readonly Dictionary<int,List<Edge>> Edges=new Dictionary<int,List<Edge>>();
            private readonly Dictionary<int,SavedRoutePoint> doors=new Dictionary<int,SavedRoutePoint>();
            public void Add(int from,int to,List<SavedRoutePoint> points)
            {
                double metres=Length(points);
                Anchor(from,points[0]);Anchor(to,points[points.Count-1]);
                if(!Edges.ContainsKey(from)) Edges.Add(from,new List<Edge>());
                if(!Edges.ContainsKey(to)) Edges.Add(to,new List<Edge>());
                Edges[from].Add(new Edge {To=to,Points=Copy(points),Metres=metres});
                var reverse=Copy(points); reverse.Reverse();
                Edges[to].Add(new Edge {To=from,Points=reverse,Metres=metres});
            }
            private void Anchor(int building,SavedRoutePoint point)
            {
                SavedRoutePoint known;
                if(doors.TryGetValue(building,out known))
                {
                    double x=known.X-point.X,z=known.Z-point.Z;
                    if(x*x+z*z>0.000001)throw new ArgumentException("Inconsistent appointment route entrance.");
                }
                else doors.Add(building,point);
            }
        }
        private sealed class PathResult
        {
            public List<SavedRoutePoint> Points;
            public double Metres;
        }
        private sealed class Outing
        {
            public double Departure,Arrival,Leave,Return;
            public int Building;
            public bool Required;
            public bool Joint;
            public DailySchedule Schedule;
            public RoutePoint PointTarget;
            public List<SavedRoutePoint> PointOutbound,PointInbound;
        }

        public static bool TryCreate(Resident resident,ResidentRoutine routine,int absoluteDay,
            IList<AppointmentRecord> appointments,int wakeMinute,int sleepMinute,
            out DailySchedule schedule,out string reason)
        { return TryCreate(resident,routine,absoluteDay,appointments,null,wakeMinute,sleepMinute,out schedule,out reason); }

        public static bool TryCreate(Resident resident,ResidentRoutine routine,int absoluteDay,
            IList<AppointmentRecord> appointments,IList<AppointmentPlaceRoute> placeRoutes,int wakeMinute,int sleepMinute,
            out DailySchedule schedule,out string reason)
        {return TryCreateCore(resident,routine,absoluteDay,appointments,placeRoutes,wakeMinute,sleepMinute,null,false,out schedule,out reason);}

        internal static bool TryCreateWithHouseholdDay(Resident resident,ResidentRoutine routine,int absoluteDay,
            IList<AppointmentRecord> appointments,IList<AppointmentPlaceRoute> placeRoutes,int wakeMinute,int sleepMinute,
            HouseholdDayPlan householdDay,bool requiredOnly,out DailySchedule schedule,out string reason)
        {return TryCreateCore(resident,routine,absoluteDay,appointments,placeRoutes,wakeMinute,sleepMinute,householdDay,requiredOnly,out schedule,out reason);}

        private static bool TryCreateCore(Resident resident,ResidentRoutine routine,int absoluteDay,
            IList<AppointmentRecord> appointments,IList<AppointmentPlaceRoute> placeRoutes,int wakeMinute,int sleepMinute,
            HouseholdDayPlan householdDay,bool requiredOnly,out DailySchedule schedule,out string reason)
        {
            schedule=null; reason="invalid";
            if(resident==null || !resident.IsAlive || resident.AgeGroup!=ResidentAgeGroup.Adult ||
                resident.IsQuestProtected || string.IsNullOrWhiteSpace(resident.Id) || routine==null ||
                routine.ResidentId!=resident.Id || absoluteDay<0 || wakeMinute<0 || sleepMinute>=1440 ||
                wakeMinute>=sleepMinute)
            { reason="resident or waking window is invalid"; return false; }

            RouteGraph graph;
            try
            {
                if(householdDay!=null)
                {
                    if(householdDay.AbsoluteDay!=absoluteDay || householdDay.WakeMinute!=wakeMinute || householdDay.SleepMinute!=sleepMinute ||
                        householdDay.HomeKey!=resident.HomeKey)throw new ArgumentException("Household appointment date/window mismatch.");
                    HouseholdDayPlanner.Schedule(householdDay,resident.Id,routine);
                }
                graph=householdDay!=null && routine.Itinerary==null && routine.DestinationKey<0?new RouteGraph():BuildGraph(resident.HomeKey,routine);
                if(householdDay!=null)AddHouseholdGraph(graph,householdDay,resident.Id);
            }
            catch(ArgumentException) { reason="saved routine route graph is invalid"; return false; }
            int appointmentVisitIndex=0;
            foreach(var itinerary in ResidentRoutineDay.Itineraries(routine))appointmentVisitIndex+=itinerary.Stops.Count;
            double dayStart=(double)absoluteDay*86400,dayEnd=dayStart+86400;
            double speed=ResidentMovement.WalkSpeedFor(resident.AppearanceSeed);
            var required=new List<Outing>();
            int pointNodeIndex=0;
            if(appointments!=null)
            {
                foreach(var booking in appointments)
                {
                    if(booking==null || booking.ResidentId!=resident.Id || !ReservesPhysicalSlot(booking.Status) ||
                        !Finite(booking.StartsAt) || booking.StartsAt<dayStart || booking.StartsAt>=dayEnd) continue;
                    if(booking.Place==null)
                    { reason="appointment place is missing"; return false; }
                    if(householdDay!=null && booking.Place.SettlementId!=householdDay.SettlementId)
                    {reason="appointment place is outside the household settlement";return false;}
                    if(resident.NativeIdentity!=null &&
                        booking.Place.SettlementId!=resident.NativeIdentity.SettlementId)
                    { reason="appointment place is outside the resident settlement"; return false; }
                    double leaveAt;
                    try { leaveAt=AppointmentTiming.PlannedLeaveAt(booking); }
                    catch(ArgumentException) { reason="appointment timing is invalid"; return false; }
                    if(!Finite(booking.EndsAt) || !Finite(booking.GraceSeconds) || booking.EndsAt<=booking.StartsAt ||
                        booking.GraceSeconds<0 || booking.PatienceSeconds<0 ||
                        booking.PatienceSeconds>0 && (booking.PatienceSeconds<AppointmentTiming.MinimumPatienceSeconds ||
                            booking.PatienceSeconds>AppointmentTiming.MaximumPatienceSeconds) ||
                        leaveAt<booking.StartsAt || leaveAt>dayEnd)
                    { reason="appointment does not fit inside its calendar day"; return false; }
                    if(booking.StartsAt<dayStart+wakeMinute*60d || leaveAt>dayStart+sleepMinute*60d)
                    { reason="appointment falls outside resident waking hours"; return false; }
                    bool point=booking.Place.Kind=="world-point";
                    int destination=booking.Place.BuildingKey;
                    if(point)
                    {
                        if(booking.Place.BuildingKey>=0 || !Finite(booking.Place.X) || !Finite(booking.Place.Y) || !Finite(booking.Place.Z))
                        {reason="exact appointment point is invalid";return false;}
                        AppointmentPlaceRoute captured=null;
                        if(placeRoutes!=null)
                            foreach(var candidate in placeRoutes)
                                if(AppointmentPlaceRouteValidation.Matches(booking,candidate)){captured=candidate;break;}
                        if(captured==null || captured.Connections==null || captured.Connections.Count==0)
                        {reason="exact appointment point has no matching native-verified route";return false;}
                        destination=NextSyntheticNode(graph,pointNodeIndex++);
                        int connected=0;
                        foreach(var connection in captured.Connections)
                        {
                            if(connection==null || connection.Route==null || !graph.Edges.ContainsKey(connection.AnchorBuildingKey)) continue;
                            try
                            {
                                var last=connection.Route[connection.Route.Count-1];
                                if(Math.Abs(last.X-booking.Place.X)>0.001 || Math.Abs(last.Z-booking.Place.Z)>0.001) continue;
                                graph.Add(connection.AnchorBuildingKey,destination,Copy(connection.Route));
                                connected++;
                            }
                            catch(ArgumentException) { }
                        }
                        if(connected==0){reason="exact appointment point is disconnected from the saved home route graph";return false;}
                    }
                    else if(booking.Place.Kind!="building" || booking.Place.BuildingKey<0)
                    {reason="appointment place kind has no route adapter";return false;}
                    PathResult outbound,inbound;
                    bool outboundFound=TryShortestPath(graph,resident.HomeKey,destination,out outbound);
                    bool inboundFound=TryShortestPath(graph,destination,resident.HomeKey,out inbound);
                    if(!outboundFound || !inboundFound)
                    { reason=point ? "appointment point is disconnected from the saved home route graph" :
                        "appointment building is disconnected from the saved home route graph"; return false; }
                    double walkOut=outbound.Metres/speed*2,walkHome=inbound.Metres/speed*2;
                    double arrival=booking.StartsAt-dayStart,leave=leaveAt-dayStart;
                    double departure=arrival-walkOut,returnAt=leave+walkHome;
                    if(departure<wakeMinute*60d || returnAt>sleepMinute*60d || arrival<=departure || returnAt<=leave)
                    { reason="appointment out-and-back travel does not fit waking hours"; return false; }
                    DailySchedule child;
                    RoutePoint pointTarget=default(RoutePoint);
                    if(point)
                    {
                        pointTarget=new RoutePoint(booking.Place.X,booking.Place.Z);
                        try { child=DailySchedule.PointOutingDay(resident.HomeKey,ToRoute(outbound.Points),ToRoute(inbound.Points),
                            pointTarget,departure,arrival,leave,returnAt,wakeMinute,sleepMinute); }
                        catch(ArgumentException) { reason="appointment point journey cannot fit inside the waking day"; return false; }
                    }
                    else
                    {
                        ResidentDestination destinationData=Destination(booking.Place.BuildingKey,outbound,inbound,walkOut,leave-arrival);
                        try { child=DailySchedule.ItineraryDay(resident.HomeKey,destinationData,departure,wakeMinute,sleepMinute,appointmentVisitIndex++); }
                        catch(ArgumentException) { reason="appointment journey cannot fit inside the waking day"; return false; }
                    }
                    // ResidentVisitJourney uses the appointment arrival as the requested
                    // arrival; equal opening minutes ensure it cannot silently delay entry.
                    required.Add(new Outing {Departure=departure,Arrival=arrival,Leave=leave,Return=returnAt,
                        Building=destination,Required=true,Schedule=child,PointTarget=pointTarget,
                        PointOutbound=point?Copy(outbound.Points):null,PointInbound=point?Copy(inbound.Points):null});
                }
            }
            required.Sort((a,b)=>a.Departure.CompareTo(b.Departure));
            for(int i=1;i<required.Count;i++)
                if(required[i].Departure<required[i-1].Return)
                { reason="appointment travel windows overlap"; return false; }

            var optional=new List<Outing>();
            try
            {
                if(!requiredOnly)
                {
                    if(householdDay!=null)BuildHouseholdOptional(resident,routine,householdDay,optional);
                    else BuildOptionalOutings(resident,routine,graph,speed,dayStart,wakeMinute,sleepMinute,optional);
                }
            }
            catch(ArgumentException) { reason="ordinary routine outing is invalid"; return false; }
            optional.Sort((a,b)=>a.Joint!=b.Joint?(a.Joint?-1:1):a.Arrival.CompareTo(b.Arrival));
            var accepted=new List<Outing>(required);
            foreach(var candidate in optional)
            {
                bool conflict=false;
                foreach(var existing in accepted)
                    if(candidate.Departure<existing.Return && existing.Departure<candidate.Return)
                    { conflict=true; break; }
                if(conflict && candidate.Joint){reason="joint household outing conflicts with a required appointment";return false;}
                if(!conflict) accepted.Add(candidate);
            }
            accepted.Sort((a,b)=>a.Departure.CompareTo(b.Departure));
            try
            {
                var children=new List<DailySchedule>();
                foreach(var outing in accepted) children.Add(outing.Schedule);
                schedule=DailySchedule.OutingsDay(resident.HomeKey,children,wakeMinute,sleepMinute);
                if(householdDay!=null)schedule=DailySchedule.ForDay(ResidentRoutineDay.Schedule(resident.HomeKey,routine,wakeMinute,sleepMinute),schedule,absoluteDay);
            }
            catch(ArgumentException) { schedule=null; reason="combined outings overlap or exceed waking hours"; return false; }
            reason="scheduled"; return true;
        }

        private static RouteGraph BuildGraph(int home,ResidentRoutine routine)
        {
            if(home<0) throw new ArgumentException("Missing home building.");
            var graph=new RouteGraph();
            if(routine.Itinerary!=null)
            {
                foreach(var outing in ResidentRoutineDay.CopyOutings(routine))
                {
                    int previous=home;
                    foreach(var stop in outing.Itinerary.Stops)
                    {
                        graph.Add(previous,stop.BuildingKey,stop.Route);
                        previous=stop.BuildingKey;
                    }
                    graph.Add(previous,home,outing.Itinerary.ExitRoute);
                }
                return graph;
            }
            if(routine.DestinationKey<0 || routine.DestinationKey==home || routine.Route==null || routine.Route.Count<2)
                throw new ArgumentException("Routine has no saved route.");
            graph.Add(home,routine.DestinationKey,routine.Route);
            return graph;
        }

        private static void BuildOptionalOutings(Resident resident,ResidentRoutine routine,RouteGraph graph,
            double speed,double dayStart,int wakeMinute,int sleepMinute,List<Outing> result)
        {
            if(routine.AdditionalOutings!=null && routine.AdditionalOutings.Count>0)
            {
                int visitOffset=0;
                foreach(var outing in ResidentRoutineDay.CopyOutings(routine))
                {
                    var journey=ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary);
                    int identity=visitOffset;visitOffset+=outing.Itinerary.Stops.Count;
                    if(outing.DepartureSeconds<wakeMinute*60d || journey.EndsAt>sleepMinute*60d)continue;
                    var visits=new List<ResidentVisitSnapshot>(journey.ScheduledVisits());
                    var child=DailySchedule.ItineraryDay(resident.HomeKey,outing.Itinerary,outing.DepartureSeconds,
                        wakeMinute,sleepMinute,identity);
                    result.Add(new Outing{Departure=outing.DepartureSeconds,Arrival=visits[0].EnterAt,
                        Leave=visits[visits.Count-1].LeaveAt,Return=journey.EndsAt,
                        Building=visits[0].TargetBuildingKey,Schedule=child});
                }
                return;
            }
            if(routine.Itinerary!=null)
            {
                var journey=ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds,routine.Itinerary);
                foreach(var visit in journey.ScheduledVisits())
                {
                    PathResult outbound,inbound;
                    if(!TryShortestPath(graph,resident.HomeKey,visit.TargetBuildingKey,out outbound) ||
                        !TryShortestPath(graph,visit.TargetBuildingKey,resident.HomeKey,out inbound)) continue;
                    double walkOut=outbound.Metres/speed*2,walkHome=inbound.Metres/speed*2;
                    double arrival=visit.EnterAt,leave=visit.LeaveAt;
                    double departure=arrival-walkOut,returnAt=leave+walkHome;
                    if(departure<wakeMinute*60d || returnAt>sleepMinute*60d || arrival<=departure || returnAt<=leave) continue;
                    var destination=Destination(visit.TargetBuildingKey,outbound,inbound,walkOut,leave-arrival);
                    DailySchedule child=DailySchedule.ItineraryDay(resident.HomeKey,destination,departure,wakeMinute,sleepMinute,visit.VisitIndex);
                    result.Add(new Outing {Departure=departure,Arrival=arrival,Leave=leave,Return=returnAt,
                        Building=visit.TargetBuildingKey,Schedule=child});
                }
                return;
            }
            if(routine.DestinationKey<0 || routine.OpenMinute<0 || routine.CloseMinute<=routine.OpenMinute ||
                routine.CloseMinute>=1440) return;
            PathResult outboundFallback,inboundFallback;
            if(!TryShortestPath(graph,resident.HomeKey,routine.DestinationKey,out outboundFallback) ||
                !TryShortestPath(graph,routine.DestinationKey,resident.HomeKey,out inboundFallback)) return;
            double travel=outboundFallback.Metres/speed*2,returnTravel=inboundFallback.Metres/speed*2;
            double arrivalFallback=routine.OpenMinute*60d,leaveFallback=routine.CloseMinute*60d;
            double departureFallback=arrivalFallback-travel,returnAtFallback=leaveFallback+returnTravel;
            if(departureFallback<wakeMinute*60d || returnAtFallback>sleepMinute*60d) return;
            var optionalDestination=Destination(routine.DestinationKey,outboundFallback,inboundFallback,travel,leaveFallback-arrivalFallback);
            var childSchedule=DailySchedule.ItineraryDay(resident.HomeKey,optionalDestination,departureFallback,wakeMinute,sleepMinute);
            result.Add(new Outing {Departure=departureFallback,Arrival=arrivalFallback,Leave=leaveFallback,
                Return=returnAtFallback,Building=routine.DestinationKey,Schedule=childSchedule});
        }

        private static ResidentDestination Destination(int building,PathResult outbound,PathResult inbound,
            double walkOut,double dwell)
        {
            if(building<0 || outbound==null || inbound==null || !Positive(walkOut) || !Positive(dwell))
                throw new ArgumentException("Invalid appointment outing.");
            return new ResidentDestination
            {
                Stops=new List<ResidentVisitStop> { new ResidentVisitStop {BuildingKey=building,
                    Purpose=ResidentVisitPurpose.Social,OpenMinute=0,CloseMinute=0,WalkSeconds=walkOut,
                    DwellSeconds=dwell,Route=Copy(outbound.Points)} },
                ExitRoute=Copy(inbound.Points),ExitWalkSeconds=inbound.Metres*(walkOut/outbound.Metres)
            };
        }

        private static bool TryShortestPath(RouteGraph graph,int start,int target,out PathResult path)
        {
            path=null;
            if(start==target || !graph.Edges.ContainsKey(start) || !graph.Edges.ContainsKey(target)) return false;
            var distance=new Dictionary<int,double>(); var previous=new Dictionary<int,Edge>();
            var previousNode=new Dictionary<int,int>(); var done=new HashSet<int>();
            foreach(var key in graph.Edges.Keys) distance[key]=double.PositiveInfinity;
            distance[start]=0;
            while(done.Count<graph.Edges.Count)
            {
                int current=-1; double best=double.PositiveInfinity;
                foreach(var pair in distance)
                    if(!done.Contains(pair.Key) && pair.Value<best) {current=pair.Key;best=pair.Value;}
                if(current<0 || double.IsInfinity(best)) break;
                if(current==target) break;
                done.Add(current);
                foreach(var edge in graph.Edges[current])
                {
                    double candidate=best+edge.Metres;
                    if(candidate<distance[edge.To]) {distance[edge.To]=candidate;previous[edge.To]=edge;previousNode[edge.To]=current;}
                }
            }
            if(double.IsInfinity(distance[target]) || !Finite(distance[target]) || distance[target]<=0) return false;
            var edges=new List<Edge>(); int node=target;
            while(node!=start)
            {
                Edge edge; int from;
                if(!previous.TryGetValue(node,out edge) || !previousNode.TryGetValue(node,out from)) return false;
                edges.Add(edge); node=from;
            }
            edges.Reverse(); var points=new List<SavedRoutePoint>();
            foreach(var edge in edges)
                for(int i=0;i<edge.Points.Count;i++)
                {
                    if(points.Count>0 && Same(points[points.Count-1],edge.Points[i])) continue;
                    points.Add(new SavedRoutePoint {X=edge.Points[i].X,Z=edge.Points[i].Z});
                }
            if(points.Count<2) return false;
            path=new PathResult {Points=points,Metres=distance[target]}; return true;
        }

        private static double Length(IList<SavedRoutePoint> points)
        {
            if(points==null || points.Count<2) throw new ArgumentException("Saved route has too few points.");
            double length=0;
            for(int i=0;i<points.Count;i++)
            {
                if(points[i]==null || !Finite(points[i].X) || !Finite(points[i].Z)) throw new ArgumentException("Invalid route point.");
                if(i>0) {double x=points[i].X-points[i-1].X,z=points[i].Z-points[i-1].Z; length+=Math.Sqrt(x*x+z*z);}
            }
            if(!Positive(length)) throw new ArgumentException("Saved route has no length.");
            return length;
        }
        private static List<SavedRoutePoint> Copy(IList<SavedRoutePoint> source)
        { var result=new List<SavedRoutePoint>(); foreach(var point in source) result.Add(new SavedRoutePoint {X=point.X,Z=point.Z}); return result; }
        private static bool Same(SavedRoutePoint a,SavedRoutePoint b) { return Math.Abs(a.X-b.X)<0.000001 && Math.Abs(a.Z-b.Z)<0.000001; }
        private static int NextSyntheticNode(RouteGraph graph,int ordinal)
        {
            int candidate=int.MaxValue-ordinal;
            while(candidate>=0 && graph.Edges.ContainsKey(candidate)) candidate--;
            if(candidate<0) throw new ArgumentException("No appointment point graph node is available.");
            return candidate;
        }
        private static List<RoutePoint> ToRoute(IList<SavedRoutePoint> points)
        {
            var result=new List<RoutePoint>(points.Count);
            foreach(var point in points) result.Add(new RoutePoint(point.X,point.Z));
            return result;
        }
        private static bool ReservesPhysicalSlot(string status)
        { return status=="accepted" || status=="waiting" || status=="attended" || status=="player-absent" ||
            status=="npc-absent" || status=="interrupted"; }
        private static bool Positive(double value) { return Finite(value) && value>0; }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
