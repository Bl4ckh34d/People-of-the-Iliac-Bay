using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class RoutineRecoveryLeg
    {
        // RouteNodeKey may identify an appointment's transient exact-point graph
        // node. BuildingKey remains -1 for that leg; synthetic routing identities
        // never escape into resident or appointment building state.
        public int OriginalStopIndex = -1, BuildingKey, RouteNodeKey;
        public ResidentVisitPurpose Purpose;
        public string AppointmentId;
        public bool PointDestination;
        public bool HouseholdJoint;
        public bool ReturnsHome;
        public double WalkAt, ArriveAt, EnterAt, LeaveAt;
        public List<SavedRoutePoint> Route = new List<SavedRoutePoint>();
    }

    // A one-day continuation, created only for an interrupted resident. Times are
    // absolute; rebuilding or loading the day cannot move a booked meeting.
    [Serializable]
    public sealed class RoutineRecoveryPlan
    {
        public string ResidentId, RoutineFingerprint, AppointmentFingerprint;
        public string HouseholdDayId, HouseholdDayFingerprint, OtherParentAppointmentFingerprint;
        public bool HouseholdJointSkipped;
        public int SettlementId, AbsoluteDay, HomeBuildingKey, OriginBuildingKey, WakeMinute, SleepMinute;
        public bool OriginOutside;
        public double CreatedAt, DepartAt, EndsAt;
        public List<int> SkippedStopIndices = new List<int>();
        public List<RoutineRecoveryLeg> Legs = new List<RoutineRecoveryLeg>();
        public List<AppointmentPlaceRoute> AppointmentPointRoutes = new List<AppointmentPlaceRoute>();

        public static RoutineRecoveryPlan Copy(RoutineRecoveryPlan source)
        {
            if (source == null || !Text(source.ResidentId,256) || !Text(source.RoutineFingerprint,128) ||
                !Text(source.AppointmentFingerprint,128) || source.SettlementId<0 || source.AbsoluteDay<0 ||
                source.HomeBuildingKey<0 || source.OriginBuildingKey<0 || source.WakeMinute<0 || source.SleepMinute>=1440 ||
                source.WakeMinute>=source.SleepMinute || !Finite(source.CreatedAt) || !Finite(source.DepartAt) ||
                !Finite(source.EndsAt) || source.CreatedAt<source.AbsoluteDay*86400d ||
                source.CreatedAt>source.DepartAt || source.DepartAt>source.EndsAt ||
                source.EndsAt>source.AbsoluteDay*86400d+source.SleepMinute*60d || source.Legs==null ||
                source.AppointmentPointRoutes!=null && source.AppointmentPointRoutes.Count>32 ||
                source.Legs.Count<1 || source.Legs.Count>65 || source.SkippedStopIndices==null || source.SkippedStopIndices.Count>32)
                throw new ArgumentException("Invalid routine recovery identity, day, or times.");
            bool family=source.HouseholdDayId!=null;
            if(family && (!Text(source.HouseholdDayId,256) || !Digest(source.HouseholdDayFingerprint) ||
                !Digest(source.OtherParentAppointmentFingerprint)) || !family &&
                (source.HouseholdDayFingerprint!=null || source.OtherParentAppointmentFingerprint!=null || source.HouseholdJointSkipped))
                throw new ArgumentException("Invalid household recovery provenance.");
            var copy=new RoutineRecoveryPlan {ResidentId=source.ResidentId,RoutineFingerprint=source.RoutineFingerprint,
                HouseholdDayId=source.HouseholdDayId,HouseholdDayFingerprint=source.HouseholdDayFingerprint,
                OtherParentAppointmentFingerprint=source.OtherParentAppointmentFingerprint,HouseholdJointSkipped=source.HouseholdJointSkipped,
                AppointmentFingerprint=source.AppointmentFingerprint,SettlementId=source.SettlementId,AbsoluteDay=source.AbsoluteDay,
                HomeBuildingKey=source.HomeBuildingKey,OriginBuildingKey=source.OriginBuildingKey,WakeMinute=source.WakeMinute,
                SleepMinute=source.SleepMinute,OriginOutside=source.OriginOutside,CreatedAt=source.CreatedAt,DepartAt=source.DepartAt,EndsAt=source.EndsAt};
            var pointRouteIds=new HashSet<string>(StringComparer.Ordinal);int pointRoutePoints=0;
            if(source.AppointmentPointRoutes!=null)foreach(var raw in source.AppointmentPointRoutes)
            {
                if(raw==null || string.IsNullOrWhiteSpace(raw.AppointmentId) || raw.AppointmentId.Length>256 || raw.Place==null ||
                    raw.Place.Kind!="world-point" || raw.Place.SettlementId!=source.SettlementId || raw.Place.BuildingKey>=0 ||
                    !Finite(raw.Place.X)||!Finite(raw.Place.Y)||!Finite(raw.Place.Z)||raw.Connections==null ||
                    raw.Connections.Count<1 || raw.Connections.Count>32 || !pointRouteIds.Add(raw.AppointmentId))
                    throw new ArgumentException("Invalid point appointment route proof.");
                var routeCopy=new AppointmentPlaceRoute {AppointmentId=raw.AppointmentId,
                    Place=AppointmentPlace.Copy(raw.Place)};
                var anchors=new HashSet<int>();
                foreach(var connection in raw.Connections)
                {
                    if(connection==null || connection.AnchorBuildingKey<0 || !anchors.Add(connection.AnchorBuildingKey) ||
                        connection.Route==null || connection.Route.Count<2 || connection.Route.Count>10000)
                        throw new ArgumentException("Invalid point appointment source route.");
                    var points=new List<SavedRoutePoint>();double length=0;
                    foreach(var point in connection.Route)
                    {
                        if(point==null || !Finite(point.X)||!Finite(point.Z))throw new ArgumentException("Invalid point route coordinate.");
                        if(points.Count>0)length+=Distance(points[points.Count-1],point);
                        points.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                    }
                    if(length<=0 || Math.Abs(points[points.Count-1].X-raw.Place.X)>0.001 ||
                        Math.Abs(points[points.Count-1].Z-raw.Place.Z)>0.001)
                        throw new ArgumentException("Point route does not reach the saved appointment place.");
                    pointRoutePoints+=points.Count;
                    if(pointRoutePoints>100000)throw new ArgumentException("Point route proof is too large.");
                    routeCopy.Connections.Add(new AppointmentPlaceRouteConnection {AnchorBuildingKey=connection.AnchorBuildingKey,Route=points});
                }
                copy.AppointmentPointRoutes.Add(routeCopy);
            }
            var skipped=new HashSet<int>(); var retained=new HashSet<int>(); var bookings=new HashSet<string>(StringComparer.Ordinal);
            foreach(int index in source.SkippedStopIndices)
            {
                if(index<0 || index>=32 || !skipped.Add(index)) throw new ArgumentException("Invalid skipped routine stop.");
                copy.SkippedStopIndices.Add(index);
            }
            double previous=source.DepartAt; SavedRoutePoint endpoint=null; int totalPoints=0;bool jointRetained=false;
            for(int i=0;i<source.Legs.Count;i++)
            {
                var leg=source.Legs[i]; bool last=i==source.Legs.Count-1;
                int routeNode=leg==null?0:(leg.RouteNodeKey==0?leg.BuildingKey:leg.RouteNodeKey);
                int sourceKinds=leg==null?0:(leg.OriginalStopIndex>=0?1:0)+(leg.AppointmentId!=null?1:0)+(leg.HouseholdJoint?1:0);
                if(leg==null || leg.BuildingKey < (leg.PointDestination?-1:0) || routeNode<0 ||
                    leg.HouseholdJoint && (!family || jointRetained || source.HouseholdJointSkipped || leg.PointDestination ||
                        leg.Purpose!=ResidentVisitPurpose.Social) ||
                    leg.PointDestination&&(last||leg.BuildingKey!=-1||leg.AppointmentId==null||leg.OriginalStopIndex!=-1) ||
                    !leg.PointDestination&&routeNode!=leg.BuildingKey || !Enum.IsDefined(typeof(ResidentVisitPurpose),leg.Purpose) ||
                    leg.OriginalStopIndex < -1 || leg.OriginalStopIndex>=32 ||
                    !Finite(leg.WalkAt) || !Finite(leg.ArriveAt) || !Finite(leg.EnterAt) || !Finite(leg.LeaveAt) ||
                    leg.WalkAt<previous || leg.ArriveAt<leg.WalkAt || leg.EnterAt<leg.ArriveAt || leg.LeaveAt<leg.EnterAt ||
                    leg.LeaveAt>source.EndsAt || leg.ReturnsHome!=last ||
                    last && (leg.BuildingKey!=source.HomeBuildingKey || leg.RouteNodeKey!=0 && leg.RouteNodeKey!=leg.BuildingKey ||
                        leg.PointDestination || sourceKinds!=0 ||
                        leg.ArriveAt!=leg.EnterAt || leg.EnterAt!=leg.LeaveAt || leg.LeaveAt!=source.EndsAt) ||
                    !last && (leg.LeaveAt<=leg.EnterAt || sourceKinds!=1) ||
                    leg.OriginalStopIndex>=0 && (skipped.Contains(leg.OriginalStopIndex) || !retained.Add(leg.OriginalStopIndex)) ||
                    leg.AppointmentId!=null && (!Text(leg.AppointmentId,256) || !bookings.Add(leg.AppointmentId)) ||
                    leg.Route==null || leg.Route.Count<1 || leg.Route.Count>10000)
                    throw new ArgumentException("Invalid routine recovery leg.");
                var route=new List<SavedRoutePoint>(); double length=0;
                foreach(var point in leg.Route)
                {
                    if(point==null || !Finite(point.X) || !Finite(point.Z)) throw new ArgumentException("Invalid recovery route point.");
                    if(route.Count>0) length+=Distance(route[route.Count-1],point);
                    route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                }
                totalPoints+=route.Count;
                if(totalPoints>100000 || endpoint!=null && Distance(endpoint,route[0])>0.001 ||
                    length>0 && leg.ArriveAt<=leg.WalkAt || length==0 && leg.ArriveAt!=leg.WalkAt)
                    throw new ArgumentException("Disconnected or untimed recovery route.");
                endpoint=route[route.Count-1]; previous=leg.LeaveAt;
                jointRetained|=leg.HouseholdJoint;
                copy.Legs.Add(new RoutineRecoveryLeg {OriginalStopIndex=leg.OriginalStopIndex,BuildingKey=leg.BuildingKey,
                    HouseholdJoint=leg.HouseholdJoint,
                    RouteNodeKey=routeNode,PointDestination=leg.PointDestination,
                    Purpose=leg.Purpose,AppointmentId=leg.AppointmentId,ReturnsHome=leg.ReturnsHome,
                    WalkAt=leg.WalkAt,ArriveAt=leg.ArriveAt,EnterAt=leg.EnterAt,LeaveAt=leg.LeaveAt,Route=route});
            }
            foreach(var leg in copy.Legs)
            {
                if(!leg.PointDestination)continue;
                var proof=copy.AppointmentPointRoutes.Find(item=>item.AppointmentId==leg.AppointmentId);
                if(proof==null || Math.Abs(proof.Place.X-leg.Route[leg.Route.Count-1].X)>0.001 ||
                    Math.Abs(proof.Place.Z-leg.Route[leg.Route.Count-1].Z)>0.001)
                    throw new ArgumentException("Point recovery leg has no matching saved appointment route proof.");
            }
            return copy;
        }

        public ActivitySnapshot Evaluate(double now)
        {
            if(!Finite(now) || now<AbsoluteDay*86400d || now>=(AbsoluteDay+1d)*86400d)
                throw new ArgumentOutOfRangeException(nameof(now));
            if(now>=AbsoluteDay*86400d+SleepMinute*60d || now<AbsoluteDay*86400d+WakeMinute*60d)
                return Interior(Activity.Sleep,HomeBuildingKey);
            int origin=OriginBuildingKey;bool originPoint=false;RoutePoint originPointTarget=default(RoutePoint);
            bool first=true;
            foreach(var leg in Legs)
            {
                if(now<leg.WalkAt)
                {
                    if(first&&OriginOutside)
                    {var start=leg.Route[0];return new ActivitySnapshot(Activity.Travel,-1,new RoutePoint(start.X,start.Z),0);}
                    if(originPoint)return new ActivitySnapshot(Activity.Meeting,-1,originPointTarget,1);
                    return Interior(origin==HomeBuildingKey?Activity.Home:Activity.Visit,origin);
                }
                if(now<leg.ArriveAt) return new ActivitySnapshot(Activity.Travel,-1,Along(leg.Route,
                    (now-leg.WalkAt)/(leg.ArriveAt-leg.WalkAt)),0,new RoutePoint(leg.Route[leg.Route.Count-1].X,leg.Route[leg.Route.Count-1].Z));
                if(now<leg.EnterAt) return new ActivitySnapshot(Activity.Travel,-1,
                    new RoutePoint(leg.Route[leg.Route.Count-1].X,leg.Route[leg.Route.Count-1].Z),0);
                if(now<leg.LeaveAt)
                {
                    if(leg.PointDestination)
                    {var target=leg.Route[leg.Route.Count-1];return new ActivitySnapshot(Activity.Meeting,-1,new RoutePoint(target.X,target.Z),1);}
                    return Interior(Activity.Visit,leg.BuildingKey);
                }
                origin=leg.RouteNodeKey;originPoint=leg.PointDestination;
                if(originPoint){var target=leg.Route[leg.Route.Count-1];originPointTarget=new RoutePoint(target.X,target.Z);}
                first=false;
            }
            return Interior(Activity.Home,HomeBuildingKey);
        }
        private static ActivitySnapshot Interior(Activity activity,int building) {return new ActivitySnapshot(activity,building,new RoutePoint(),0);}
        private static RoutePoint Along(List<SavedRoutePoint> route,double fraction)
        {
            double length=0;for(int i=1;i<route.Count;i++)length+=Distance(route[i-1],route[i]);
            double remaining=length*Math.Max(0,Math.Min(1,fraction));
            for(int i=1;i<route.Count;i++)
            {
                double span=Distance(route[i-1],route[i]); if(span<=0)continue;
                if(remaining<=span) return new RoutePoint(route[i-1].X+(route[i].X-route[i-1].X)*remaining/span,
                    route[i-1].Z+(route[i].Z-route[i-1].Z)*remaining/span);
                remaining-=span;
            }
            var last=route[route.Count-1];return new RoutePoint(last.X,last.Z);
        }
        internal static double Distance(SavedRoutePoint a,SavedRoutePoint b) {double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        internal static bool Finite(double v) {return !double.IsNaN(v)&&!double.IsInfinity(v);}
        private static bool Text(string value,int max) {return !string.IsNullOrWhiteSpace(value)&&value.Length<=max;}
        private static bool Digest(string value)
        {if(value==null || value.Length!=64)return false;foreach(char c in value)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))return false;return true;}
    }

    public static class RoutineRecoveryIdentity
    {
        public static string HouseholdDay(HouseholdDayPlan source)
        {
            var plan=HouseholdDayPlan.Copy(source);var text=new StringBuilder();
            Add(text,plan.Id);Add(text,plan.Status);Add(text,plan.WakeMinute);Add(text,plan.SleepMinute);Add(text,plan.PlannedAt);
            Add(text,plan.JointSuppressed?1:0);
            foreach(var member in new[]{plan.First,plan.Second})
            {
                Add(text,member.ResidentId);Add(text,member.RoutineFingerprint);Add(text,member.Outings.Count);
                foreach(var selection in member.Outings){Add(text,selection.SourceOutingIndex);Add(text,selection.DepartureSeconds);}
            }
            Add(text,plan.ChildIds.Count);foreach(var child in plan.ChildIds)Add(text,child);
            Add(text,plan.JointInn==null?-1:plan.JointInn.InnKey);
            if(plan.JointInn?.PayerId!=null)
            {
                Add(text,plan.JointInn.PayerId);Add(text,plan.JointInn.FirstMenuIndex);Add(text,plan.JointInn.SecondMenuIndex);
                Add(text,plan.JointInn.FirstPriceGold);Add(text,plan.JointInn.SecondPriceGold);
            }
            if(plan.JointInn!=null)foreach(var outing in new[]{plan.JointInn.First,plan.JointInn.Second})
                Add(text,Fingerprint(new ResidentRoutine{ResidentId="joint",Itinerary=outing.Itinerary,
                    ItineraryDepartureSeconds=outing.DepartureSeconds},plan.HomeKey,plan.WakeMinute,plan.SleepMinute));
            return Hash(text.ToString());
        }
        public static string Fingerprint(ResidentRoutine routine,int home,int wake,int sleep)
        {
            if(routine==null) throw new ArgumentNullException(nameof(routine));
            var text=new StringBuilder();
            Add(text,routine.ResidentId);Add(text,home);Add(text,wake);Add(text,sleep);Add(text,routine.RouteGeometryVersion);
            Add(text,routine.PersonalLaneVersion);Add(text,routine.DestinationKey);Add(text,routine.OpenMinute);Add(text,routine.CloseMinute);
            Route(text,routine.Route);Add(text,routine.ItineraryDepartureSeconds);
            if(routine.Itinerary!=null)
            {
                var itinerary=ResidentVisitJourney.Copy(routine.Itinerary);
                Add(text,itinerary.Stops.Count);
                foreach(var stop in itinerary.Stops)
                {Add(text,stop.BuildingKey);Add(text,(int)stop.Purpose);Add(text,stop.OpenMinute);Add(text,stop.CloseMinute);
                    Add(text,stop.WalkSeconds);Add(text,stop.DwellSeconds);Add(text,stop.KeepRoomUntil);Route(text,stop.Route);}
                Route(text,itinerary.ExitRoute);Add(text,itinerary.ExitWalkSeconds);Add(text,itinerary.EntryGateSeconds);
                Add(text,itinerary.ExitGateSeconds);Add(text,itinerary.Walled?1:0);Add(text,itinerary.ThroughRoute?1:0);
            }
            if(routine.AdditionalOutings!=null && routine.AdditionalOutings.Count>0)
            {
                // Leave legacy fingerprints byte-for-byte unchanged. Each later
                // outing contributes its exact timing and full route evidence.
                var outings=ResidentRoutineDay.CopyOutings(routine);
                Add(text,"additional-outings-v2");Add(text,outings.Count-1);
                for(int i=1;i<outings.Count;i++)
                {
                    var child=new ResidentRoutine{ResidentId=routine.ResidentId,
                        Itinerary=outings[i].Itinerary,ItineraryDepartureSeconds=outings[i].DepartureSeconds};
                    Add(text,Fingerprint(child,home,wake,sleep));
                }
            }
            return Hash(text.ToString());
        }
        public static string Appointments(IList<AppointmentRecord> bookings,string residentId,int day)
        {
            var selected=new List<AppointmentRecord>();
            if(bookings!=null)foreach(var booking in bookings)
                if(Reserves(booking,residentId,day))selected.Add(booking);
            selected.Sort((a,b)=>string.CompareOrdinal(a.Id,b.Id));var text=new StringBuilder();
            foreach(var booking in selected)
            {Add(text,booking.Id);Add(text,booking.StartsAt);Add(text,booking.EndsAt);Add(text,booking.GraceSeconds);
                Add(text,booking.Place==null?"":booking.Place.Kind);Add(text,booking.Place==null?-1:booking.Place.BuildingKey);
                Add(text,booking.Place==null?-1:booking.Place.SettlementId);
                // Preserve exact fingerprints of legacy saves. New bookings also
                // depend on captured patience and any outcome that changes departure.
                if(booking.PatienceSeconds>0)
                {Add(text,booking.PatienceSeconds);Add(text,AppointmentTiming.PlannedLeaveAt(booking));}}
            return Hash(text.ToString());
        }
        // Household availability also depends on the exact point venue of the
        // other parent. Keep legacy recovery fingerprints unchanged.
        public static string HouseholdAppointments(IList<AppointmentRecord> bookings,string residentId,int day)
        {
            var text=new StringBuilder();Add(text,Appointments(bookings,residentId,day));
            var points=new List<AppointmentRecord>();
            if(bookings!=null)foreach(var booking in bookings)
                if(Reserves(booking,residentId,day) && booking.Place!=null && booking.Place.Kind=="world-point")points.Add(booking);
            points.Sort((a,b)=>string.CompareOrdinal(a.Id,b.Id));
            foreach(var booking in points){Add(text,booking.Id);Add(text,booking.Place.X);Add(text,booking.Place.Y);Add(text,booking.Place.Z);}
            return Hash(text.ToString());
        }
        internal static bool Reserves(AppointmentRecord booking,string residentId,int day)
        {return booking!=null && booking.ResidentId==residentId && Math.Floor(booking.StartsAt/86400d)==day &&
            (booking.Status=="accepted" || booking.Status=="waiting" || booking.Status=="attended" ||
             booking.Status=="player-absent" || booking.Status=="npc-absent" || booking.Status=="interrupted");}
        private static void Route(StringBuilder text,IList<SavedRoutePoint> points)
        {Add(text,points==null?-1:points.Count);if(points!=null)foreach(var p in points){Add(text,p.X);Add(text,p.Z);}}
        private static void Add(StringBuilder text,string value) {text.Append(value==null?-1:value.Length).Append(':').Append(value).Append(';');}
        private static void Add(StringBuilder text,int value) {Add(text,value.ToString(CultureInfo.InvariantCulture));}
        // DFU's Mono/.NET Framework FullSerializer prints 15 significant digits.
        // Hash that same representation; .NET 8's round-trip format would make
        // an unchanged native save appear to contain a different routine.
        private static void Add(StringBuilder text,double value) {Add(text,value.ToString("G15",CultureInfo.InvariantCulture));}
        private static string Hash(string value)
        {using(var sha=SHA256.Create()){var bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(value));var result=new StringBuilder(64);foreach(byte b in bytes)result.Append(b.ToString("x2",CultureInfo.InvariantCulture));return result.ToString();}}
    }
}
