using System;
using System.Globalization;
namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class ResidentInnStay
    {
        public int BuildingKey;
        // Null is a legacy single-destination record, resolved when its trip is copied.
        public int? SettlementId;
        public string VisitId;
        public double EnterAt,LeaveAt,AttemptedAt;
        public TravelerAdmission Admission;
        public string BookingId;
        public static ResidentInnStay Copy(ResidentInnStay source)
        {
            if(source==null || source.BuildingKey<0 || source.SettlementId<0 || !Time(source.EnterAt) || !Time(source.LeaveAt) || !Time(source.AttemptedAt) ||
                source.LeaveAt<=source.EnterAt || source.AttemptedAt<source.EnterAt || source.AttemptedAt>=source.LeaveAt ||
                source.Admission!=TravelerAdmission.Admitted && source.Admission!=TravelerAdmission.Refused ||
                (source.Admission==TravelerAdmission.Admitted ? string.IsNullOrWhiteSpace(source.BookingId) : source.BookingId!=null))
                throw new ArgumentException("Invalid resident inn stay.");
            return new ResidentInnStay {VisitId=source.VisitId,SettlementId=source.SettlementId,BuildingKey=source.BuildingKey,EnterAt=source.EnterAt,LeaveAt=source.LeaveAt,
                AttemptedAt=source.AttemptedAt,Admission=source.Admission,BookingId=source.BookingId};
        }
        private static bool Time(double value) {return !double.IsNaN(value) && !double.IsInfinity(value) && value>=0;}
    }
    public sealed partial class WorldStateStore
    {
        private sealed class InnAdmissionCandidate
        {
            public double DueAt;
            public string Id;
            public Action Admit;
        }
        // Both guest sources share one budget and one first-come admission order.
        // Native receipts, permanent claims and loaded reception routes must be ready first.
        public int AdvanceInnAdmissions(int settlement,double now,int limit,int building)
        {
            Require(DefenseTime(now) && limit>0 && limit<=10000 && building>=0 &&
                state.Settlements.Exists(t=>t.SettlementId==settlement),"Invalid inn admission batch.");
            if(!state.Lodgings.Exists(t=>t.SettlementId==settlement && t.BuildingKey==building)) return 0;
            var pending=new System.Collections.Generic.List<InnAdmissionCandidate>();
            foreach(var candidate in PendingResidentInnAdmissions(settlement,building,now))
            {
                var resident=candidate;
                pending.Add(new InnAdmissionCandidate {DueAt=resident.DueAt,Id="resident:"+resident.Trip.ResidentId,
                    Admit=()=>AdmitResidentInn(resident.Trip,resident.Visit,resident.DueAt)});
            }
            foreach(var visit in state.TravelerVisits)
            {
                if(visit.SettlementId!=settlement || visit.TavernKey!=building || visit.Admission!=TravelerAdmission.Pending) continue;
                double due=Journey(Traveler(visit.PersonId),visit).CheckInAt;
                if(due>now) continue;
                string id=visit.Id;
                pending.Add(new InnAdmissionCandidate {DueAt=due,Id="traveler:"+id,Admit=()=>TryAdmitTraveler(id,due)});
            }
            pending.Sort((a,b)=>{int order=a.DueAt.CompareTo(b.DueAt); return order!=0 ? order : string.CompareOrdinal(a.Id,b.Id);});
            int count=Math.Min(limit,pending.Count);
            for(int i=0;i<count;i++) pending[i].Admit();
            return count;
        }
        private struct ResidentAdmissionCandidate
        {
            public ResidentTrip Trip;
            public ResidentTripSnapshot Visit;
            public double DueAt;
        }
        // Call only after native receipts and permanent bed claims are reconciled.
        public int AdvanceResidentInnAdmissions(int settlement,double now,int limit,int building)
        {
            Require(DefenseTime(now) && limit>0,"Invalid resident admission batch.");
            if(!state.Lodgings.Exists(t=>t.SettlementId==settlement && t.BuildingKey==building)) return 0;
            var pending=PendingResidentInnAdmissions(settlement,building,now);
            pending.Sort((a,b)=>{
                int order=a.DueAt.CompareTo(b.DueAt);
                return order!=0 ? order : string.CompareOrdinal(a.Trip.ResidentId,b.Trip.ResidentId);
            });
            int count=Math.Min(limit,pending.Count);
            for(int i=0;i<count;i++) AdmitResidentInn(pending[i].Trip,pending[i].Visit,pending[i].DueAt);
            return count;
        }
        public bool HasDueResidentInnAdmission(int settlement,int building,double now)
        {
            Require(DefenseTime(now),"Invalid resident admission query.");
            return PendingResidentInnAdmissions(settlement,building,now).Count>0;
        }
        public bool HasUnpreparedResidentInnAdmission(int settlement,int building,double now)
        {
            Require(DefenseTime(now),"Invalid resident admission query.");
            return PendingResidentInnAdmissions(settlement,building,now).Exists(c=>!c.Trip.InnReceptions.Exists(r=>
                r.SettlementId==settlement && r.BuildingKey==building && r.VisitId==c.Visit.VisitId));
        }
        private System.Collections.Generic.List<ResidentAdmissionCandidate> PendingResidentInnAdmissions(int settlement,int building,double now)
        {
            var pending=new System.Collections.Generic.List<ResidentAdmissionCandidate>();
            foreach(var trip in state.ResidentTrips)
            {
                if(!TripResident(state,trip.HomeSettlement,trip.ResidentId).IsAlive) continue;
                foreach(var visit in trip.ScheduledInnVisits())
                {
                    if(visit.SettlementId!=settlement || visit.BuildingKey!=building || ExistingInnStay(trip,visit)!=null) continue;
                    var reception=trip.InnReceptions.Find(r=>r.SettlementId==settlement && r.VisitId==visit.VisitId && r.BuildingKey==building);
                    double due=reception==null ? visit.VisitEnterAt : reception.ServiceEndsAt;
                    if(due<=now && due<visit.VisitLeaveAt)
                        pending.Add(new ResidentAdmissionCandidate {Trip=trip,Visit=visit,DueAt=due});
                }
            }
            return pending;
        }
        public bool TryAdmitResidentInn(string id,double now)
        {
            Require(DefenseTime(now),"Invalid resident admission time.");
            ResidentTrip trip;
            if(!residentTripIndex.TryGetValue(id,out trip) || !TripResident(state,trip.HomeSettlement,id).IsAlive) return false;
            var visit=trip.Evaluate(now);
            if(visit.Phase!=ResidentTripPhase.Visiting || visit.VisitPhase!=ResidentVisitPhase.Inside || visit.VisitPurpose!=ResidentVisitPurpose.Inn) return false;
            return AdmitResidentInn(trip,visit,now);
        }
        private bool AdmitResidentInn(ResidentTrip trip,ResidentTripSnapshot visit,double now)
        {
            string id=trip.ResidentId;
            // An earlier refusal in this batch can replace the itinerary. Never
            // admit a later candidate captured from that abandoned schedule.
            if(!residentTripIndex.TryGetValue(id,out trip)) return false;
            bool scheduled=false;
            foreach(var current in trip.ScheduledInnVisits())
                if(current.VisitId==visit.VisitId && current.SettlementId==visit.SettlementId && current.BuildingKey==visit.BuildingKey && current.VisitEnterAt==visit.VisitEnterAt &&
                    current.VisitLeaveAt==visit.VisitLeaveAt) {scheduled=true;break;}
            if(!scheduled) return false;
            var prior=ExistingInnStay(trip,visit);
            if(prior!=null) return prior.Admission==TravelerAdmission.Admitted;
            var reception=trip.InnReceptions.Find(r=>r.SettlementId==visit.SettlementId && r.VisitId==visit.VisitId && r.BuildingKey==visit.BuildingKey);
            if(reception!=null && now<reception.ServiceEndsAt) return false;
            // Missing geometry is not a refusal; admission waits for the real inventory.
            if(!state.Lodgings.Exists(t=>t.SettlementId==visit.SettlementId && t.BuildingKey==visit.BuildingKey)) return false;
            string booking=ResidentBookingId(id,visit.VisitEnterAt);
            double checkout=trip.InnCheckout(visit);
            bool admitted=ReceptionKeeperAvailable(trip,reception,now) && !InnUnderInvestigation(visit.SettlementId,visit.BuildingKey,now) &&
                TryBookLodging(visit.SettlementId,visit.BuildingKey,booking,id,trip.HomeSettlement,false,1,now,checkout);
            var recorded=new ResidentInnStay {VisitId=visit.VisitId,SettlementId=visit.SettlementId,BuildingKey=visit.BuildingKey,EnterAt=visit.VisitEnterAt,LeaveAt=checkout,
                AttemptedAt=now,Admission=admitted ? TravelerAdmission.Admitted : TravelerAdmission.Refused,BookingId=admitted ? booking : null};
            trip.InnStays.Add(recorded);
            if(!admitted && reception!=null)
            {
                var route=new System.Collections.Generic.List<GuardPoint3>();
                for(int i=reception.Route.Count-1;i>=0;i--)
                {
                    var point=reception.Route[i];
                    route.Add(new GuardPoint3 {X=point.X,Y=point.Y,Z=point.Z});
                }
                try
                {
                    SaveResidentServiceExit(id,new ResidentServiceExit {VisitId=visit.VisitId,SettlementId=visit.SettlementId,BuildingKey=visit.BuildingKey,
                        VisitEnterAt=visit.VisitEnterAt,VisitLeaveAt=visit.VisitLeaveAt,StartedAt=now,
                        SecondsPerMetre=reception.SecondsPerMetre,Route=route});
                }
                catch {trip.InnStays.Remove(recorded);throw;}
            }
            return admitted;
        }
        private bool ReceptionKeeperAvailable(ResidentTrip trip,ResidentInnReception reception,double now)
        {
            if(reception==null || reception.KeeperId==null) return true; // Older saves lack keeper bindings.
            var keeper=state.NativePeople.Find(p=>p.Id==reception.KeeperId && p.SettlementId==reception.SettlementId && p.WorkKey==reception.BuildingKey);
            if(keeper==null) return false;
            if(!keeper.IsAlive)
            {
                var death=state.AlarmIncidents.Find(a=>a.VictimId==keeper.Id && a.Corpse!=null);
                if(death==null || now>=death.Corpse.DiedAt) return false;
            }
            return !state.AlarmIncidents.Exists(a=>a.StartedAt<=now && now<CivilianAlarms.ActiveUntil(a) &&
                a.People.Exists(p=>p.ResidentId==keeper.Id));
        }
        public string ResidentInnBed(string id,double now)
        {
            Require(DefenseTime(now),"Invalid resident bed time."); ResidentTrip trip;
            if(!residentTripIndex.TryGetValue(id,out trip) || !TripResident(state,trip.HomeSettlement,id).IsAlive) return null;
            var stay=ResidentDetourStay(id,now) ?? trip.InnStays.Find(s=>s.Admission==TravelerAdmission.Admitted && now>=s.AttemptedAt && now<s.LeaveAt);
            if(stay==null) return null;
            var tavern=Lodging(stay.SettlementId.Value,stay.BuildingKey);
            var booking=tavern.Bookings.Find(b=>b.Id==stay.BookingId);
            return booking==null || booking.BedIds.Count==0 ? null : booking.BedIds[0];
        }
        public ResidentInnStay ResidentInnStayAt(string id,double now)
        {
            Require(DefenseTime(now),"Invalid resident stay time."); ResidentTrip trip;
            if(!residentTripIndex.TryGetValue(id,out trip) || !TripResident(state,trip.HomeSettlement,id).IsAlive) return null;
            var stay=ResidentDetourStay(id,now) ?? trip.InnStays.Find(s=>s.Admission==TravelerAdmission.Admitted && now>=s.AttemptedAt && now<s.LeaveAt);
            return stay==null ? null : ResidentInnStay.Copy(stay);
        }
        public TravelerAdmission ResidentInnAdmission(string id,double now)
        {
            Require(DefenseTime(now),"Invalid resident admission query."); ResidentTrip trip;
            if(!residentTripIndex.TryGetValue(id,out trip)) return TravelerAdmission.Pending;
            var detour=ResidentLodgingExit(id,now);
            if(detour!=null)return detour.LodgingDetour!=null && !detour.LodgingDetour.Camping?TravelerAdmission.Admitted:TravelerAdmission.Refused;
            var visit=trip.Evaluate(now);
            var stay=ExistingInnStay(trip,visit);
            return stay==null ? TravelerAdmission.Pending : stay.Admission;
        }
        private static ResidentInnStay ExistingInnStay(ResidentTrip trip,ResidentTripSnapshot visit)
        {
            return trip.InnStays.Find(s=>s.SettlementId==visit.SettlementId && s.BuildingKey==visit.BuildingKey && (s.VisitId==visit.VisitId ||
                s.Admission==TravelerAdmission.Admitted && s.AttemptedAt<=visit.VisitEnterAt && s.LeaveAt+0.001>=trip.InnCheckout(visit)));
        }
        private static string ResidentBookingId(string id,double start)
        {return "resident-stay:"+id+":"+start.ToString("R",CultureInfo.InvariantCulture);}
        private static void ValidateResidentInnBookings(WorldState world,ResidentTrip trip)
        {
            ValidateResidentLodgingDetours(world,trip);
            foreach(var reception in trip.InnReceptions)
                Require(reception.KeeperId==null || world.NativePeople.Exists(p=>p.Id==reception.KeeperId &&
                        p.SettlementId==reception.SettlementId && p.WorkKey==reception.BuildingKey),"Reception keeper does not belong to inn.");
            foreach(var stay in trip.InnStays)
            {
                // IDs retain their original timestamp text across native JSON;
                // rebuilding the ID from a rounded double would orphan payment.
                string id=stay.BookingId ?? ResidentBookingId(trip.ResidentId,stay.EnterAt);
                var tavern=world.Lodgings.Find(t=>t.SettlementId==stay.SettlementId && t.BuildingKey==stay.BuildingKey);
                var booking=tavern==null ? null : tavern.Bookings.Find(b=>b.Id==id);
                if(stay.Admission==TravelerAdmission.Admitted)
                {
                    string prefix="resident-stay:"+trip.ResidentId+":";double reference;
                    Require(id.StartsWith(prefix,StringComparison.Ordinal) && double.TryParse(id.Substring(prefix.Length),NumberStyles.Float,
                        CultureInfo.InvariantCulture,out reference) && ResidentVisitJourney.SameTime(reference,stay.EnterAt),"Invalid resident booking reference.");
                    Require(booking!=null && !booking.Player && booking.GuestId==trip.ResidentId &&
                        booking.HomeSettlement==trip.HomeSettlement && booking.PartySize==1 && booking.StartsAt==stay.AttemptedAt && booking.EndsAt==stay.LeaveAt,
                        "Resident inn stay lacks matching paid booking.");
                }
                else Require(booking==null,"Refused resident has a booking.");
            }
        }
    }
}
