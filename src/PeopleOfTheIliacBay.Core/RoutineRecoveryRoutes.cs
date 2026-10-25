using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    internal static class RoutineRecoveryRoutes
    {
        private const double Tolerance = 0.001;

        // Validate appointment ownership and the proof-to-leg relationship even
        // when a recovery is inert because its routine fingerprint is stale. A
        // later routine can match again, so stale records still need safe bindings.
        public static bool PointProofsBelongToPlan(RoutineRecoveryPlan plan,
            IList<AppointmentRecord> appointments)
        {
            if (plan == null || plan.Legs == null || plan.AppointmentPointRoutes == null) return false;
            var appointmentById = new Dictionary<string, AppointmentRecord>(StringComparer.Ordinal);
            if (appointments != null)
                foreach (var booking in appointments)
                    if (booking != null && !string.IsNullOrWhiteSpace(booking.Id))
                        appointmentById[booking.Id] = booking;

            bool currentAppointments = appointments != null && string.Equals(plan.AppointmentFingerprint,
                RoutineRecoveryIdentity.Appointments(appointments, plan.ResidentId, plan.AbsoluteDay),
                StringComparison.Ordinal);
            var proofIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var proof in plan.AppointmentPointRoutes)
            {
                AppointmentRecord booking;
                if (proof == null || string.IsNullOrWhiteSpace(proof.AppointmentId) ||
                    !proofIds.Add(proof.AppointmentId) || !appointmentById.TryGetValue(proof.AppointmentId, out booking) ||
                    booking.ResidentId != plan.ResidentId || booking.Place == null ||
                    booking.Place.SettlementId != plan.SettlementId ||
                    !AppointmentPlaceRouteValidation.Matches(booking, proof)) return false;

                int linkedLegs = 0;
                foreach (var leg in plan.Legs)
                    if (leg != null && leg.PointDestination && leg.AppointmentId == proof.AppointmentId)
                    {
                        linkedLegs++;
                        if (leg.BuildingKey != -1 || leg.RouteNodeKey < 0) return false;
                    }
                if (linkedLegs != 1) return false;

                if (currentAppointments &&
                    !RoutineRecoveryIdentity.Reserves(booking, plan.ResidentId, plan.AbsoluteDay)) return false;
            }

            foreach (var leg in plan.Legs)
                if (leg != null && leg.PointDestination &&
                    (string.IsNullOrWhiteSpace(leg.AppointmentId) || !proofIds.Contains(leg.AppointmentId))) return false;
            return true;
        }

        private struct Segment
        {
            public SavedRoutePoint A, B;
        }

        private struct Cell : IEquatable<Cell>
        {
            public int X, Z;

            public bool Equals(Cell other) { return X == other.X && Z == other.Z; }
            public override bool Equals(object obj) { return obj is Cell && Equals((Cell)obj); }
            public override int GetHashCode() { unchecked { return (X * 397) ^ Z; } }
        }

        public static bool Matches(RoutineRecoveryPlan plan, ResidentRoutine routine, int homeBuildingKey,
            IList<AppointmentRecord> appointments=null,HouseholdDayPlan family=null)
        {
            if (plan == null || routine == null || homeBuildingKey < 0) return false;
            try
            {
                var doors = new Dictionary<int, SavedRoutePoint>();
                var segments = new Dictionary<Cell, List<Segment>>();
                if (routine.Itinerary != null)
                {
                    foreach (var outing in ResidentRoutineDay.CopyOutings(routine))
                    {
                        int previous = homeBuildingKey;
                        foreach (var stop in outing.Itinerary.Stops)
                        {
                            if (!AddSourceEdge(previous, stop.BuildingKey, stop.Route, doors, segments)) return false;
                            previous = stop.BuildingKey;
                        }
                        if (!AddSourceEdge(previous, homeBuildingKey, outing.Itinerary.ExitRoute, doors, segments)) return false;
                    }
                }
                else if(family==null || routine.DestinationKey>=0)
                {
                    if (routine.DestinationKey < 0 || routine.DestinationKey == homeBuildingKey ||
                        !AddSourceEdge(homeBuildingKey, routine.DestinationKey, routine.Route, doors, segments)) return false;
                }

                if(plan.HouseholdDayId!=null)
                {
                    if(family==null || family.Id!=plan.HouseholdDayId || family.AbsoluteDay!=plan.AbsoluteDay ||
                        family.SettlementId!=plan.SettlementId || family.HomeKey!=homeBuildingKey ||
                        family.First.ResidentId!=plan.ResidentId && family.Second.ResidentId!=plan.ResidentId ||
                        RoutineRecoveryIdentity.HouseholdDay(family)!=plan.HouseholdDayFingerprint)return false;
                    if(family.JointInn!=null)
                    {
                        if(!plan.HouseholdJointSkipped && !plan.Legs.Exists(l=>l.HouseholdJoint))return false;
                        var joint=family.First.ResidentId==plan.ResidentId?family.JointInn.First:family.JointInn.Second;
                        var stop=joint.Itinerary.Stops[0];
                        if(!AddSourceEdge(homeBuildingKey,stop.BuildingKey,stop.Route,doors,segments) ||
                            !AddSourceEdge(stop.BuildingKey,homeBuildingKey,joint.Itinerary.ExitRoute,doors,segments))return false;
                        foreach(var leg in plan.Legs)if(leg.HouseholdJoint)
                        {
                            var visit=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(joint.DepartureSeconds,joint.Itinerary).ScheduledVisits())[0];
                            double start=family.AbsoluteDay*86400d;
                            if(family.JointSuppressed || leg.BuildingKey!=family.JointInn.InnKey ||
                                leg.EnterAt<start+visit.EnterAt-Tolerance || Math.Abs(leg.LeaveAt-start-visit.LeaveAt)>Tolerance)return false;
                        }
                    }
                    else if(plan.Legs.Exists(l=>l.HouseholdJoint) || plan.HouseholdJointSkipped)return false;
                    var selected=HouseholdDayPlanner.Schedule(family,plan.ResidentId,routine).ScheduledVisits(plan.CreatedAt);
                    foreach(var leg in plan.Legs)if(leg.OriginalStopIndex>=0)
                    {
                        if(!selected.Exists(v=>v.VisitIndex==leg.OriginalStopIndex))return false;
                        var visit=selected.Find(v=>v.VisitIndex==leg.OriginalStopIndex);
                        double start=family.AbsoluteDay*86400d;
                        if(visit.TargetBuildingKey!=leg.BuildingKey || visit.Purpose!=leg.Purpose ||
                            leg.EnterAt<start+visit.EnterAt-Tolerance || Math.Abs(leg.LeaveAt-start-visit.LeaveAt)>Tolerance)return false;
                    }
                }
                else if(family!=null || plan.Legs.Exists(l=>l.HouseholdJoint))return false;

                if (!doors.ContainsKey(plan.OriginBuildingKey)) return false;
                var appointmentById=new Dictionary<string,AppointmentRecord>(StringComparer.Ordinal);
                if(appointments!=null)foreach(var booking in appointments)
                    if(booking!=null&&!string.IsNullOrWhiteSpace(booking.Id))appointmentById[booking.Id]=booking;
                // A matching point and native route are not enough: the proof must
                // belong to this resident. When the appointment fingerprint still
                // matches the current day's ledger, also require each proof to name
                // a currently reserved booking in that same day. A stale fingerprint
                // can represent a legitimate cancellation/outcome; keep that history
                // structurally checked but inert rather than rejecting the whole save.
                bool currentAppointments=appointments!=null && string.Equals(plan.AppointmentFingerprint,
                    RoutineRecoveryIdentity.Appointments(appointments,plan.ResidentId,plan.AbsoluteDay),StringComparison.Ordinal);
                var pointNodes=new HashSet<int>();
                foreach(var proof in plan.AppointmentPointRoutes)
                {
                    AppointmentRecord booking;
                    if(proof==null || !appointmentById.TryGetValue(proof.AppointmentId,out booking) ||
                        booking.ResidentId!=plan.ResidentId || currentAppointments &&
                            !RoutineRecoveryIdentity.Reserves(booking,plan.ResidentId,plan.AbsoluteDay) ||
                        !AppointmentPlaceRouteValidation.Matches(booking,proof))return false;
                    var leg=plan.Legs.Find(item=>item.PointDestination && item.AppointmentId==proof.AppointmentId);
                    if(leg==null || leg.RouteNodeKey<0 || doors.ContainsKey(leg.RouteNodeKey) || !pointNodes.Add(leg.RouteNodeKey))return false;
                    if(currentAppointments)
                    {
                        double leave;
                        try { leave=AppointmentTiming.PlannedLeaveAt(booking); }
                        catch(ArgumentException) { return false; }
                        if(leg.EnterAt<booking.StartsAt-Tolerance || leg.EnterAt>=leave ||
                            Math.Abs(leg.LeaveAt-leave)>Tolerance)return false;
                    }
                    var target=new SavedRoutePoint {X=proof.Place.X,Z=proof.Place.Z};
                    foreach(var connection in proof.Connections)
                    {
                        SavedRoutePoint anchor;
                        if(connection==null || !doors.TryGetValue(connection.AnchorBuildingKey,out anchor) ||
                            connection.Route==null || connection.Route.Count<2 || !Near(connection.Route[0],anchor) ||
                            !Near(connection.Route[connection.Route.Count-1],target))return false;
                        for(int i=1;i<connection.Route.Count;i++)
                            if(!AddSegment(connection.Route[i-1],connection.Route[i],segments) ||
                                !AddSegment(connection.Route[i],connection.Route[i-1],segments))return false;
                    }
                    doors.Add(leg.RouteNodeKey,target);
                }
                int origin = plan.OriginBuildingKey;
                foreach (var leg in plan.Legs)
                {
                    int targetNode=leg.RouteNodeKey==0?leg.BuildingKey:leg.RouteNodeKey;
                    SavedRoutePoint start, end;
                    if(leg.PointDestination)
                    {
                        AppointmentRecord booking;
                        var proof=plan.AppointmentPointRoutes.Find(item=>item.AppointmentId==leg.AppointmentId);
                        if(leg.BuildingKey!=-1 || !appointmentById.TryGetValue(leg.AppointmentId,out booking) ||
                            booking.ResidentId!=plan.ResidentId || currentAppointments &&
                                !RoutineRecoveryIdentity.Reserves(booking,plan.ResidentId,plan.AbsoluteDay) ||
                            proof==null || !AppointmentPlaceRouteValidation.Matches(booking,proof) ||
                            targetNode<0 || !pointNodes.Contains(targetNode))return false;
                    }
                    else if(leg.BuildingKey<0 || targetNode!=leg.BuildingKey)return false;
                    if(family!=null && leg.AppointmentId!=null && !leg.PointDestination)
                    {
                        AppointmentRecord booking;
                        if(!appointmentById.TryGetValue(leg.AppointmentId,out booking) || booking.ResidentId!=plan.ResidentId ||
                            Math.Floor(booking.StartsAt/86400d)!=plan.AbsoluteDay || booking.Place==null ||
                            booking.Place.Kind!="building" || booking.Place.SettlementId!=plan.SettlementId ||
                            booking.Place.BuildingKey!=leg.BuildingKey ||
                            currentAppointments && !RoutineRecoveryIdentity.Reserves(booking,plan.ResidentId,plan.AbsoluteDay))return false;
                        double leave=AppointmentTiming.PlannedLeaveAt(booking);
                        if(leg.EnterAt<booking.StartsAt-Tolerance || leg.EnterAt>=leave || Math.Abs(leg.LeaveAt-leave)>Tolerance)return false;
                    }
                    if (!doors.TryGetValue(origin, out start) || !doors.TryGetValue(targetNode, out end) ||
                        leg.Route == null || leg.Route.Count == 0 ||
                        !Near(leg.Route[0], start) || !Near(leg.Route[leg.Route.Count - 1], end)) return false;

                    if (origin == targetNode)
                    {
                        if (leg.Route.Count != 1) return false;
                    }
                    else
                    {
                        bool hasMovement = false;
                        for (int i = 1; i < leg.Route.Count; i++)
                        {
                            var a = leg.Route[i - 1];
                            var b = leg.Route[i];
                            if (Near(a, b)) continue;
                            hasMovement = true;
                            if (!HasSourceSegment(a, b, segments)) return false;
                        }
                        if (!hasMovement) return false;
                    }
                    origin = targetNode;
                }
                return origin == homeBuildingKey;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool AddSourceEdge(int from, int to, List<SavedRoutePoint> route,
            Dictionary<int, SavedRoutePoint> doors, Dictionary<Cell, List<Segment>> segments)
        {
            if (from < 0 || to < 0 || route == null || route.Count < 2) return false;
            if (!AddDoor(from, route[0], doors) || !AddDoor(to, route[route.Count - 1], doors)) return false;
            for (int i = 1; i < route.Count; i++)
            {
                if (Near(route[i - 1], route[i])) continue;
                if (!AddSegment(route[i - 1], route[i], segments) ||
                    !AddSegment(route[i], route[i - 1], segments)) return false;
            }
            return true;
        }

        private static bool AddDoor(int building, SavedRoutePoint point, Dictionary<int, SavedRoutePoint> doors)
        {
            SavedRoutePoint existing;
            if (!doors.TryGetValue(building, out existing))
            {
                doors.Add(building, point);
                return true;
            }
            return Near(existing, point);
        }

        private static bool Near(SavedRoutePoint a, SavedRoutePoint b)
        {
            return a != null && b != null && RoutineRecoveryPlan.Distance(a, b) <= Tolerance;
        }

        private static bool AddSegment(SavedRoutePoint start, SavedRoutePoint end,
            Dictionary<Cell, List<Segment>> segments)
        {
            Cell cell;
            if (!TryGetCell(start, out cell)) return false;
            List<Segment> bucket;
            if (!segments.TryGetValue(cell, out bucket))
            {
                bucket = new List<Segment>();
                segments.Add(cell, bucket);
            }
            bucket.Add(new Segment { A = start, B = end });
            return true;
        }

        private static bool HasSourceSegment(SavedRoutePoint start, SavedRoutePoint end,
            Dictionary<Cell, List<Segment>> segments)
        {
            Cell center;
            if (!TryGetCell(start, out center)) return false;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                // TryGetCell leaves a full-cell margin at each int boundary, so these
                // additions cannot overflow even for malformed extreme coordinates.
                var cell = new Cell { X = center.X + dx, Z = center.Z + dz };
                List<Segment> bucket;
                if (!segments.TryGetValue(cell, out bucket)) continue;
                foreach (var segment in bucket)
                    if (Near(start, segment.A) && Near(end, segment.B)) return true;
            }
            return false;
        }

        private static bool TryGetCell(SavedRoutePoint point, out Cell cell)
        {
            cell = default(Cell);
            if (point == null || double.IsNaN(point.X) || double.IsInfinity(point.X) ||
                double.IsNaN(point.Z) || double.IsInfinity(point.Z)) return false;
            double x = Math.Floor(point.X / Tolerance);
            double z = Math.Floor(point.Z / Tolerance);
            if (x <= int.MinValue || x >= int.MaxValue || z <= int.MinValue || z >= int.MaxValue)
                return false;
            cell = new Cell { X = (int)x, Z = (int)z };
            return true;
        }
    }
}
