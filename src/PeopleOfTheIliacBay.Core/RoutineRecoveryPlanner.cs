using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public enum RoutineRecoveryFailure
    { None, Other, RequiredAppointmentUnreachable, HomeReturnUnavailable }

    public static partial class RoutineRecoveryPlanner
    {
        private sealed class Edge
        {public int To;public List<SavedRoutePoint> Route;public double Length;}
        private sealed class Graph
        {
            public readonly Dictionary<int,List<Edge>> Edges=new Dictionary<int,List<Edge>>();
            public readonly Dictionary<int,SavedRoutePoint> Doors=new Dictionary<int,SavedRoutePoint>();
            public void Add(int from,int to,List<SavedRoutePoint> route)
            {
                if(route==null || route.Count<2)throw new ArgumentException("Recovery needs verified saved routes.");
                Door(from,route[0]);Door(to,route[route.Count-1]);double length=0;
                for(int i=1;i<route.Count;i++)length+=RoutineRecoveryPlan.Distance(route[i-1],route[i]);
                if(!RoutineRecoveryPlan.Finite(length) || length<=0)throw new ArgumentException("Empty recovery route.");
                Edges[from].Add(new Edge {To=to,Route=Copy(route),Length=length});
                var reverse=Copy(route);reverse.Reverse();Edges[to].Add(new Edge {To=from,Route=reverse,Length=length});
            }
            private void Door(int building,SavedRoutePoint point)
            {
                if(Doors.ContainsKey(building))
                {if(RoutineRecoveryPlan.Distance(Doors[building],point)>0.001)throw new ArgumentException("Inconsistent saved building entrances.");return;}
                Doors.Add(building,new SavedRoutePoint {X=point.X,Z=point.Z});Edges.Add(building,new List<Edge>());
            }
            public bool Route(int from,int to,out List<SavedRoutePoint> points,out double length)
            {
                points=null;length=0;if(!Edges.ContainsKey(from)||!Edges.ContainsKey(to))return false;
                if(from==to){points=new List<SavedRoutePoint>{new SavedRoutePoint {X=Doors[from].X,Z=Doors[from].Z}};return true;}
                var costs=new Dictionary<int,double>();var previous=new Dictionary<int,Edge>();var parent=new Dictionary<int,int>();var done=new HashSet<int>();
                costs[from]=0;
                while(true)
                {
                    int current=-1;double best=double.PositiveInfinity;
                    foreach(var cost in costs)if(!done.Contains(cost.Key)&&cost.Value<best){current=cost.Key;best=cost.Value;}
                    if(current<0)return false;if(current==to)break;done.Add(current);
                    foreach(var edge in Edges[current])
                    {
                        double old,candidate=best+edge.Length;
                        if(!costs.TryGetValue(edge.To,out old)||candidate<old)
                        {costs[edge.To]=candidate;previous[edge.To]=edge;parent[edge.To]=current;}
                    }
                }
                var edges=new List<Edge>();for(int current=to;current!=from;current=parent[current])edges.Add(previous[current]);
                edges.Reverse();points=new List<SavedRoutePoint>();
                foreach(var edge in edges)for(int i=0;i<edge.Route.Count;i++)
                {if(points.Count>0&&i==0)continue;var p=edge.Route[i];points.Add(new SavedRoutePoint {X=p.X,Z=p.Z});}
                length=costs[to];return true;
            }
        }
        private sealed class Candidate
        {public int Index,Building;public bool Point,HouseholdJoint;public ResidentVisitPurpose Purpose;public double Enter,Leave;public AppointmentRecord Booking;}

        // The native caller measures the local exit first. Graph edges below are
        // already collision-checked saved outdoor paths, not fresh scene surveys.
        public static bool TryCreate(int settlementId,Resident resident,ResidentRoutine routine,DailySchedule ordinary,IList<AppointmentRecord> bookings,
            int day,double now,int origin,double departAt,int wake,int sleep,double secondsPerMetre,
            out RoutineRecoveryPlan plan,out string reason,bool originOutside=false)
        {return TryCreate(settlementId,resident,routine,ordinary,bookings,day,now,origin,departAt,wake,sleep,secondsPerMetre,
            out plan,out reason,out _,originOutside);}

        public static bool TryCreate(int settlementId,Resident resident,ResidentRoutine routine,DailySchedule ordinary,IList<AppointmentRecord> bookings,
            int day,double now,int origin,double departAt,int wake,int sleep,double secondsPerMetre,
            out RoutineRecoveryPlan plan,out string reason,out RoutineRecoveryFailure failure,bool originOutside=false)
        {return TryCreate(settlementId,resident,routine,ordinary,bookings,null,day,now,origin,departAt,wake,sleep,secondsPerMetre,
            out plan,out reason,out failure,originOutside);}

        public static bool TryCreate(int settlementId,Resident resident,ResidentRoutine routine,DailySchedule ordinary,
            IList<AppointmentRecord> bookings,IList<AppointmentPlaceRoute> placeRoutes,
            int day,double now,int origin,double departAt,int wake,int sleep,double secondsPerMetre,
            out RoutineRecoveryPlan plan,out string reason,bool originOutside=false)
        {return TryCreate(settlementId,resident,routine,ordinary,bookings,placeRoutes,day,now,origin,departAt,wake,sleep,
            secondsPerMetre,out plan,out reason,out _,originOutside);}

        public static bool TryCreate(int settlementId,Resident resident,ResidentRoutine routine,DailySchedule ordinary,
            IList<AppointmentRecord> bookings,IList<AppointmentPlaceRoute> placeRoutes,
            int day,double now,int origin,double departAt,int wake,int sleep,double secondsPerMetre,
            out RoutineRecoveryPlan plan,out string reason,out RoutineRecoveryFailure failure,bool originOutside=false)
        {return TryCreateCore(settlementId,resident,routine,ordinary,bookings,placeRoutes,day,now,origin,departAt,wake,sleep,
            secondsPerMetre,out plan,out reason,out failure,originOutside,null,false,null);}
        private static bool TryCreateCore(int settlementId,Resident resident,ResidentRoutine routine,DailySchedule ordinary,
            IList<AppointmentRecord> bookings,IList<AppointmentPlaceRoute> placeRoutes,
            int day,double now,int origin,double departAt,int wake,int sleep,double secondsPerMetre,
            out RoutineRecoveryPlan plan,out string reason,out RoutineRecoveryFailure failure,bool originOutside,
            HouseholdDayPlan family,bool skipJoint,HashSet<int> excludedFamilyVisits)
        {
            plan=null;reason="Invalid routine recovery request.";failure=RoutineRecoveryFailure.Other;
            double dayStart=day*86400d;
            if(settlementId<0||resident==null||!resident.IsAlive||resident.IsQuestProtected||resident.AgeGroup!=ResidentAgeGroup.Adult||
                resident.NativeIdentity!=null&&resident.NativeIdentity.SettlementId!=settlementId||
                routine==null||routine.ResidentId!=resident.Id||ordinary==null||day<0||wake<0||sleep>=1440||wake>=sleep||
                !Finite(now)||!Finite(departAt)||now<dayStart||now>=dayStart+86400d||departAt<now||
                departAt>=dayStart+86400d||!Finite(secondsPerMetre)||secondsPerMetre<=0)return false;
            try
            {
                var graph=new Graph();
                if(routine.Itinerary!=null)
                {
                    foreach(var outing in ResidentRoutineDay.CopyOutings(routine))
                    {
                        int previous=resident.HomeKey;
                        foreach(var stop in outing.Itinerary.Stops){graph.Add(previous,stop.BuildingKey,stop.Route);previous=stop.BuildingKey;}
                        graph.Add(previous,resident.HomeKey,outing.Itinerary.ExitRoute);
                    }
                }
                else if(family==null || routine.DestinationKey>=0)graph.Add(resident.HomeKey,routine.DestinationKey,routine.Route);
                if(family!=null && family.JointInn!=null)
                {
                    var joint=family.First.ResidentId==resident.Id?family.JointInn.First:family.JointInn.Second;
                    var stop=joint.Itinerary.Stops[0];graph.Add(resident.HomeKey,stop.BuildingKey,stop.Route);
                    graph.Add(stop.BuildingKey,resident.HomeKey,joint.Itinerary.ExitRoute);
                }
                if(!graph.Doors.ContainsKey(origin)){reason="The actual origin has no verified saved route.";return false;}
                var pointNodes=new Dictionary<string,int>(StringComparer.Ordinal);
                var pointProofs=new List<AppointmentPlaceRoute>();int pointOrdinal=0;
                if(bookings!=null)foreach(var booking in bookings)
                {
                    if(!RoutineRecoveryIdentity.Reserves(booking,resident.Id,day) || booking.Place==null || booking.Place.Kind!="world-point" ||
                        AppointmentTiming.PlannedLeaveAt(booking)<=now)continue;
                    if(booking.Place.SettlementId!=settlementId || booking.Place.BuildingKey>=0 ||
                        !Finite(booking.Place.X)||!Finite(booking.Place.Y)||!Finite(booking.Place.Z))
                    {reason="A required appointment has no valid local point venue.";failure=RoutineRecoveryFailure.RequiredAppointmentUnreachable;return false;}
                    AppointmentPlaceRoute verified=null;
                    if(placeRoutes!=null)foreach(var candidate in placeRoutes)
                        if(AppointmentPlaceRouteValidation.Matches(booking,candidate)){verified=candidate;break;}
                    if(verified==null || verified.Connections==null || verified.Connections.Count==0)
                    {reason="A required point appointment has no matching native-verified route.";failure=RoutineRecoveryFailure.RequiredAppointmentUnreachable;return false;}
                    int node=NextPointNode(graph,pointOrdinal++);int connected=0;
                    foreach(var connection in verified.Connections)
                    {
                        if(connection==null || connection.Route==null || !graph.Edges.ContainsKey(connection.AnchorBuildingKey))continue;
                        if(connection.Route.Count<2)continue;
                        var last=connection.Route[connection.Route.Count-1];
                        if(Math.Abs(last.X-booking.Place.X)>0.001||Math.Abs(last.Z-booking.Place.Z)>0.001)continue;
                        graph.Add(connection.AnchorBuildingKey,node,Copy(connection.Route));connected++;
                    }
                    if(connected==0)
                    {reason="A required point appointment is disconnected from saved routine routes.";failure=RoutineRecoveryFailure.RequiredAppointmentUnreachable;return false;}
                    pointNodes.Add(booking.Id,node);
                    pointProofs.Add(CopyPlaceRoute(verified));
                }
                var result=new RoutineRecoveryPlan {ResidentId=resident.Id,SettlementId=settlementId,
                    AbsoluteDay=day,HomeBuildingKey=resident.HomeKey,OriginBuildingKey=origin,WakeMinute=wake,SleepMinute=sleep,
                    OriginOutside=originOutside,CreatedAt=now,DepartAt=departAt,RoutineFingerprint=RoutineRecoveryIdentity.Fingerprint(routine,resident.HomeKey,wake,sleep),
                    AppointmentFingerprint=RoutineRecoveryIdentity.Appointments(bookings,resident.Id,day)};
                if(family!=null)
                {
                    result.HouseholdDayId=family.Id;result.HouseholdDayFingerprint=RoutineRecoveryIdentity.HouseholdDay(family);
                    string other=family.First.ResidentId==resident.Id?family.Second.ResidentId:family.First.ResidentId;
                    result.OtherParentAppointmentFingerprint=RoutineRecoveryIdentity.HouseholdAppointments(bookings,other,day);
                    result.HouseholdJointSkipped=family.JointInn!=null && (skipJoint || family.JointSuppressed);
                }
                result.AppointmentPointRoutes.AddRange(pointProofs);
                var required=new List<Candidate>();var optional=new List<Candidate>();
                if(bookings!=null)foreach(var booking in bookings)
                {
                    if(!RoutineRecoveryIdentity.Reserves(booking,resident.Id,day))continue;
                    double leave=AppointmentTiming.PlannedLeaveAt(booking);
                    if(leave<=now)continue;
                    bool point=booking.Place!=null&&booking.Place.Kind=="world-point";
                    int target=booking.Place==null?-1:booking.Place.BuildingKey;
                    if(point)pointNodes.TryGetValue(booking.Id,out target);
                    if(booking.Place==null||booking.Place.Kind!="building"&&!point||!point&&booking.Place.BuildingKey<0||
                        booking.Place.SettlementId!=settlementId||target<0||
                        !Finite(booking.StartsAt)||!Finite(booking.EndsAt)||!Finite(booking.GraceSeconds)||booking.GraceSeconds<0||
                        booking.EndsAt<=booking.StartsAt||!Finite(leave)||leave<=booking.StartsAt||
                        booking.PatienceSeconds!=0&&(booking.PatienceSeconds<AppointmentTiming.MinimumPatienceSeconds||
                            booking.PatienceSeconds>AppointmentTiming.MaximumPatienceSeconds))
                    {reason="A required appointment has no valid local time or venue.";return false;}
                    required.Add(new Candidate {Index=-1,Building=target,Point=point,Purpose=ResidentVisitPurpose.Social,
                        Enter=booking.StartsAt,Leave=leave,Booking=booking});
                }
                required.Sort((a,b)=>a.Enter.CompareTo(b.Enter));
                var active=ordinary.ScheduledVisits(now);
                var visits=family!=null?active:routine.Itinerary!=null?
                    ResidentRoutineDay.ScheduledVisits(routine):active;
                for(int i=0;i<visits.Count;i++)
                {
                    var visit=visits[i];double enter=dayStart+visit.EnterAt,leave=dayStart+visit.LeaveAt;
                    bool included=routine.Itinerary!=null?
                        active.Exists(v=>v.VisitIndex==visit.VisitIndex&&v.TargetBuildingKey==visit.TargetBuildingKey&&v.Purpose==visit.Purpose):
                        active.Exists(v=>v.TargetBuildingKey==visit.TargetBuildingKey&&Same(v.EnterAt,visit.EnterAt)&&Same(v.LeaveAt,visit.LeaveAt));
                    bool joint=family!=null && visit.VisitIndex==HouseholdDayPlanner.JointVisitIndex;
                    int index=joint?-1:family!=null?visit.VisitIndex:i;
                    if(!included||leave<=now||joint&&skipJoint||excludedFamilyVisits!=null&&excludedFamilyVisits.Contains(index)||
                        originOutside&&origin==visit.TargetBuildingKey&&enter<=now&&now<leave)
                    {if(joint)result.HouseholdJointSkipped=true;else result.SkippedStopIndices.Add(index);continue;}
                    // An interrupted errand is optional. Resume only shared family
                    // commitments and fixed bookings, then take the measured way home.
                    if (!joint) { result.SkippedStopIndices.Add(index); continue; }
                    optional.Add(new Candidate {Index=index,HouseholdJoint=joint,Building=visit.TargetBuildingKey,Purpose=visit.Purpose,Enter=enter,Leave=leave});
                }
                optional.Sort((a,b)=>a.Enter.CompareTo(b.Enter));int nextRequired=0,current=origin;double cursor=departAt;
                foreach(var candidate in optional)
                {
                    while(nextRequired<required.Count&&required[nextRequired].Enter<=candidate.Enter)
                    {
                        if(!AppendRequired(result,graph,required[nextRequired++],ref current,ref cursor,secondsPerMetre,out reason))
                        {failure=RoutineRecoveryFailure.RequiredAppointmentUnreachable;return false;}
                    }
                    List<SavedRoutePoint> route,escape;double distance,escapeDistance;
                    if(!graph.Route(current,candidate.Building,out route,out distance))
                    {Skip(result,candidate);continue;}
                    double arrive=cursor+distance*secondsPerMetre;
                    // Preserve the original stay length. A partly completed visit
                    // at the actual origin only needs its remaining stay.
                    double dwell=candidate.Leave-candidate.Enter;
                    if(origin==candidate.Building&&now>=candidate.Enter&&now<candidate.Leave)
                        dwell=candidate.Leave-Math.Max(now,cursor);
                    double enter=Math.Max(arrive,candidate.Enter),leave=enter+dwell;
                    var next=nextRequired<required.Count?required[nextRequired]:null;
                    int boundaryBuilding=next==null?resident.HomeKey:next.Building;
                    double boundary=next==null?dayStart+sleep*60d:next.Enter;
                    if(!graph.Route(candidate.Building,boundaryBuilding,out escape,out escapeDistance)||
                        dwell<=0||leave>candidate.Leave+0.001||leave+escapeDistance*secondsPerMetre>boundary+0.001)
                    {Skip(result,candidate);continue;}
                    // Stay at the current building until departure; never stand
                    // at a closed destination door just to consume unused time.
                    double walkAt=Math.Max(cursor,candidate.Enter-distance*secondsPerMetre);
                    result.Legs.Add(Leg(candidate,route,walkAt,walkAt+distance*secondsPerMetre,enter,leave));
                    current=candidate.Building;cursor=leave;
                }
                while(nextRequired<required.Count)
                    if(!AppendRequired(result,graph,required[nextRequired++],ref current,ref cursor,secondsPerMetre,out reason))
                    {failure=RoutineRecoveryFailure.RequiredAppointmentUnreachable;return false;}
                List<SavedRoutePoint> homeRoute;double homeDistance;
                if(!graph.Route(current,resident.HomeKey,out homeRoute,out homeDistance))
                {reason="No verified home-return route remains.";failure=RoutineRecoveryFailure.HomeReturnUnavailable;return false;}
                double homeAt=cursor+homeDistance*secondsPerMetre;
                result.Legs.Add(new RoutineRecoveryLeg {BuildingKey=resident.HomeKey,ReturnsHome=true,WalkAt=cursor,
                    ArriveAt=homeAt,EnterAt=homeAt,LeaveAt=homeAt,Route=homeRoute});result.EndsAt=homeAt;
                plan=RoutineRecoveryPlan.Copy(result);reason="A connected remaining-day continuation is ready.";failure=RoutineRecoveryFailure.None;return true;
            }
            catch(ArgumentException error){reason=error.Message;return false;}
        }
        private static bool AppendRequired(RoutineRecoveryPlan plan,Graph graph,Candidate candidate,ref int current,
            ref double cursor,double secondsPerMetre,out string reason)
        {
            reason="The required appointment cannot be reached at its agreed time.";
            List<SavedRoutePoint> route;double distance;
            if(!graph.Route(current,candidate.Building,out route,out distance))return false;
            // Arrival in the past is valid only when the actor is already at
            // that venue. Resume the remaining booked slot without extending it.
            if(current==candidate.Building && cursor>=candidate.Enter && cursor<candidate.Leave)
            {
                plan.Legs.Add(Leg(candidate,route,cursor,cursor,cursor,candidate.Leave));
                cursor=candidate.Leave;return true;
            }
            if(cursor+distance*secondsPerMetre>candidate.Enter+0.001)return false;
            double walkAt=candidate.Enter-distance*secondsPerMetre;
            plan.Legs.Add(Leg(candidate,route,walkAt,candidate.Enter,candidate.Enter,candidate.Leave));
            current=candidate.Building;cursor=candidate.Leave;return true;
        }
        private static RoutineRecoveryLeg Leg(Candidate candidate,List<SavedRoutePoint> route,double walk,double arrive,double enter,double leave)
        {return new RoutineRecoveryLeg {OriginalStopIndex=candidate.Index,BuildingKey=candidate.Point?-1:candidate.Building,
            HouseholdJoint=candidate.HouseholdJoint,
            RouteNodeKey=candidate.Building,PointDestination=candidate.Point,Purpose=candidate.Purpose,
            AppointmentId=candidate.Booking==null?null:candidate.Booking.Id,WalkAt=walk,ArriveAt=arrive,EnterAt=enter,LeaveAt=leave,Route=route};}
        private static void Skip(RoutineRecoveryPlan plan,Candidate candidate)
        {if(candidate.HouseholdJoint)plan.HouseholdJointSkipped=true;else plan.SkippedStopIndices.Add(candidate.Index);}
        private static int NextPointNode(Graph graph,int ordinal)
        {int node=int.MaxValue-ordinal;while(node>=0&&graph.Edges.ContainsKey(node))node--;if(node<0)throw new ArgumentException("No recovery point node is available.");return node;}
        private static AppointmentPlaceRoute CopyPlaceRoute(AppointmentPlaceRoute source)
        {
            var copy=new AppointmentPlaceRoute {AppointmentId=source.AppointmentId,Place=AppointmentPlace.Copy(source.Place)};
            foreach(var connection in source.Connections)
            {var route=new List<SavedRoutePoint>();foreach(var point in connection.Route)route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                copy.Connections.Add(new AppointmentPlaceRouteConnection {AnchorBuildingKey=connection.AnchorBuildingKey,Route=route});}
            return copy;
        }
        private static bool Finite(double value){return RoutineRecoveryPlan.Finite(value);}
        private static bool Same(double a,double b){return Math.Abs(a-b)<=0.001;}
        private static List<SavedRoutePoint> Copy(IList<SavedRoutePoint> source)
        {var copy=new List<SavedRoutePoint>();foreach(var point in source)copy.Add(new SavedRoutePoint {X=point.X,Z=point.Z});return copy;}
    }
}
