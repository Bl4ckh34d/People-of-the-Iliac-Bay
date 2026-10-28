using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum TravelerAdmission { Pending, Admitted, Refused }
    [Serializable]
    public sealed class TravelerPerson
    {
        public string Id,Name;
        public int HomeSettlement,Race;
        public int HomeKey=-1;
        public uint AppearanceSeed;
        public ResidentSex Sex;
        public bool IsAlive=true;
        public double DiedAt=-1;
    }
    [Serializable]
    public sealed class TravelerVisit
    {
        public string Id,PersonId,BookingId;
        public int SettlementId,TavernKey,Nights;
        public bool Walled;
        public double DesiredArrival,CommuteSeconds,AttemptedAt=-1;
        public double NativeReturnSeconds,ReturnDepartureNotBefore;
        public TravelerAdmission Admission;
        public TravelerDiversion Diversion;
        public ResidentVisitPurpose Purpose;
        public List<SavedRoutePoint> Route=new List<SavedRoutePoint>();
    }
    public sealed partial class WorldStateStore
    {
        public long TravelerVisitRevision { get; private set; }
        public List<TravelerVisit> TravelerVisitsDuring(int settlementId,double start,double end)
        {
            Require(settlementId>=0 && DefenseTime(start) && DefenseTime(end) && end>start,"Invalid traveler window.");
            var result=new List<TravelerVisit>();
            foreach(var visit in state.TravelerVisits)
            {
                if(visit.SettlementId!=settlementId) continue;
                var journey=TravelerReservedJourney(Traveler(visit.PersonId),visit);
                if(journey.EntryAt<end && journey.ExitAt>start) result.Add(visit);
            }
            return result;
        }
        public List<TravelerVisit> TravelerVisitsFor(int settlementId)
        { return state.TravelerVisits.FindAll(v=>v.SettlementId==settlementId); }
        public TravelerPerson Traveler(string id)
        { return state.Travelers.Find(p=>p.Id==id); }
        public Resident TravelerOriginResident(string id)
        {
            var person=Traveler(id);
            return person==null ? null : OriginResident(state,person);
        }
        public Household TravelerOriginHousehold(string id)
        {
            var person=Traveler(id);if(person==null || person.HomeKey<0) return null;
            var town=state.Settlements.Find(t=>t.SettlementId==person.HomeSettlement);
            return town==null ? null : town.Households.Find(h=>h.BuildingKey==person.HomeKey && h.Residents.Exists(r=>r.Id==id));
        }
        private static Resident OriginResident(WorldState world,TravelerPerson person)
        {
            if(person.HomeKey<0) return null;
            var town=world.Settlements.Find(t=>t.SettlementId==person.HomeSettlement);
            var home=town==null ? null : town.Households.Find(h=>h.BuildingKey==person.HomeKey);
            return home==null ? null : home.Residents.Find(r=>r.Id==person.Id);
        }
        private static bool MatchingOrigin(WorldState world,TravelerPerson person)
        {
            var resident=OriginResident(world,person);
            return resident!=null && resident.FullName==person.Name && resident.Race==person.Race && resident.Sex==person.Sex &&
                resident.AppearanceSeed==person.AppearanceSeed && resident.IsAlive==person.IsAlive && resident.AgeGroup==ResidentAgeGroup.Adult;
        }
        public string TravelerBed(string visitId,double now)
        {
            Require(DefenseTime(now),"Invalid traveler bed time.");
            var visit=state.TravelerVisits.Find(v=>v.Id==visitId);
            Require(visit!=null,"Unknown traveler visit.");
            if(visit.Admission!=TravelerAdmission.Admitted || !Traveler(visit.PersonId).IsAlive) return null;
            var tavern=Lodging(visit.SettlementId,visit.TavernKey);
            var booking=tavern.Bookings.Find(b=>b.Id==visit.BookingId);
            if(booking==null || now<booking.StartsAt || now>=booking.EndsAt) return null;
            return booking.BedIds.Count==0 ? null : booking.BedIds[0];
        }
        public TravelerJourney TravelerJourneyFor(string visitId)
        {
            var visit=state.TravelerVisits.Find(v=>v.Id==visitId);
            Require(visit!=null,"Unknown traveler visit.");
            return Journey(state.Travelers.Find(p=>p.Id==visit.PersonId),visit);
        }
        public void RegisterTraveler(TravelerPerson person)
        {
            var copied=CopyTraveler(person);
            Require(copied.IsAlive,"New travelers must be alive.");
            var prior=state.Travelers.Find(p=>p.Id==copied.Id);
            if(prior!=null)
            {
                Require(prior.Name==copied.Name && prior.HomeSettlement==copied.HomeSettlement && prior.Race==copied.Race &&
                    prior.AppearanceSeed==copied.AppearanceSeed && prior.Sex==copied.Sex && prior.HomeKey==copied.HomeKey,"Traveler identity changed.");
                return; // Idempotent registration must never revive a saved death.
            }
            foreach(var town in state.Settlements) foreach(var home in town.Households)
                Require(!home.Residents.Exists(r=>r.Id==copied.Id) || MatchingOrigin(state,copied),"Traveler duplicates resident identity.");
            Require(copied.HomeKey<0 || MatchingOrigin(state,copied),"Traveler lacks its reserved origin household.");
            Require(!state.NativePeople.Exists(p=>p.Id==copied.Id),"Traveler duplicates native identity.");
            state.Travelers.Add(copied);
        }
        public void RegisterTravelerVisit(TravelerVisit visit)
        {
            Require(visit!=null,"Missing traveler visit.");
            var prior=state.TravelerVisits.Find(v=>v.Id==visit.Id);
            if(prior!=null)
            {
                Require(visit.Admission==TravelerAdmission.Pending && visit.AttemptedAt==-1 && visit.BookingId==null &&
                    visit.Route!=null && SameVisit(prior,visit),"Traveler visit changed."); return;
            }
            var copy=CopyTravelerVisit(visit,state);
            Require(copy.Admission==TravelerAdmission.Pending,"New visit already has an admission outcome.");
            var person=state.Travelers.Find(p=>p.Id==copy.PersonId);
            Require(person.IsAlive,"Dead traveler cannot start another visit.");
            var journey=Journey(person,copy);
            Require(person.HomeKey<0 || !IsResidentAwayDuring(person.Id,journey.EntryAt,TravelerHomeAt(person,copy)),"Origin resident already has an overlapping journey.");
            foreach(var other in state.TravelerVisits)
            {
                if(other.PersonId!=copy.PersonId) continue;
                var otherJourney=Journey(person,other);
                Require(journey.EntryAt>=TravelerVisitConflictUntil(person,other) || otherJourney.EntryAt>=TravelerVisitConflictUntil(person,copy),"Traveler visits overlap.");
            }
            state.TravelerVisits.Add(copy);
            TravelerVisitRevision++;
        }
        public bool TryAdmitTraveler(string visitId,double now)
        {
            Require(DefenseTime(now),"Invalid traveler admission time.");
            var visit=state.TravelerVisits.Find(v=>v.Id==visitId); Require(visit!=null,"Unknown traveler visit.");
            if(visit.Admission!=TravelerAdmission.Pending) return visit.Admission==TravelerAdmission.Admitted;
            var person=state.Travelers.Find(p=>p.Id==visit.PersonId); var journey=Journey(person,visit);
            if(now<journey.CheckInAt) return false;
            string booking="traveler-booking:"+visit.Id;
            bool admitted=person.IsAlive && now<journey.CheckOutAt && !InnUnderInvestigation(visit.SettlementId,visit.TavernKey,now) && TryBookLodging(visit.SettlementId,visit.TavernKey,
                booking,person.Id,person.HomeSettlement,false,1,journey.CheckInAt,journey.CheckOutAt);
            visit.AttemptedAt=now; visit.Admission=admitted ? TravelerAdmission.Admitted : TravelerAdmission.Refused;
            visit.BookingId=admitted ? booking : null;
            TravelerVisitRevision++;
            return admitted;
        }
        private bool InnUnderInvestigation(int settlementId,int buildingKey,double now)
        {
            var roster=state.Garrisons.Find(g=>g.SettlementId==settlementId);
            if(roster==null) return false;
            // Evaluate admission at its due time even when the interior is unloaded.
            // Do not rewind or otherwise mutate the live roster during catch-up.
            var preview=GuardRoster.CopyValidated(roster); preview.Advance(now);
            return preview.Reports.Exists(r=>r.BuildingKey==buildingKey && r.OccupiesScene && r.HasArrival &&
                now>=r.FirstArrivalAt && now<r.FirstArrivalAt+UnwitnessedScene.OccupationSeconds &&
                preview.Guards.Exists(g=>r.GuardIds.Contains(g.Id) && (g.IsAlive || g.DiedAt>now)));
        }
        // Call after registering the complete demand window and reconciling native
        // player receipts. Budget limits commits, never skips earlier arrivals.
        public int AdvanceTravelerAdmissions(int settlementId,double now,int maximumAttempts,int? reconciledTavern=null)
        {
            Require(DefenseTime(now) && maximumAttempts>0 && maximumAttempts<=10000 && (!reconciledTavern.HasValue || reconciledTavern.Value>=0) &&
                state.Settlements.Exists(t=>t.SettlementId==settlementId),"Invalid traveler admission catch-up.");
            var pending=new List<KeyValuePair<double,TravelerVisit>>();
            foreach(var visit in state.TravelerVisits)
            {
                if(visit.SettlementId!=settlementId || visit.Admission!=TravelerAdmission.Pending ||
                    (reconciledTavern.HasValue && visit.TavernKey!=reconciledTavern.Value) ||
                    !state.Lodgings.Exists(t=>t.SettlementId==settlementId && t.BuildingKey==visit.TavernKey)) continue;
                var person=state.Travelers.Find(p=>p.Id==visit.PersonId);
                double at=Journey(person,visit).CheckInAt;
                if(at<=now) pending.Add(new KeyValuePair<double,TravelerVisit>(at,visit));
            }
            pending.Sort((a,b)=> {
                int time=a.Key.CompareTo(b.Key);
                return time!=0 ? time : StringComparer.Ordinal.Compare(a.Value.Id,b.Value.Id);
            });
            int attempts=Math.Min(maximumAttempts,pending.Count);
            for(int index=0;index<attempts;index++) TryAdmitTraveler(pending[index].Value.Id,pending[index].Key);
            return attempts;
        }
        public bool HasDueTravelerAdmission(int settlementId,int tavern,double now)
        {
            Require(settlementId>=0 && tavern>=0 && DefenseTime(now),"Invalid traveler admission query.");
            foreach(var visit in state.TravelerVisits)
                if(visit.SettlementId==settlementId && visit.TavernKey==tavern && visit.Admission==TravelerAdmission.Pending &&
                    Journey(Traveler(visit.PersonId),visit).CheckInAt<=now) return true;
            return false;
        }
        public bool KillTraveler(string personId,double now)
        {
            Require(DefenseTime(now),"Invalid traveler death time.");
            var person=state.Travelers.Find(p=>p.Id==personId); Require(person!=null,"Unknown traveler.");
            if(!person.IsAlive) return false;
            Require(!state.AlarmIncidents.Exists(a=>a.VictimId==personId && a.StartedAt>now),"Traveler death precedes violence.");
            Require(!state.TravelerVisits.Exists(v=>v.PersonId==personId &&
                v.Admission==TravelerAdmission.Admitted && v.AttemptedAt>now),"Traveler death precedes admission.");
            person.IsAlive=false; person.DiedAt=now;
            var origin=OriginResident(state,person);if(origin!=null) origin.IsAlive=false;
            return true;
        }
        private static TravelerJourney Journey(TravelerPerson person,TravelerVisit visit)
        {
            if(visit.Diversion!=null)
                return DivertedTravelerJourney(person,visit);
            var route=new List<RoutePoint>(); foreach(var p in visit.Route) route.Add(new RoutePoint(p.X,p.Z));
            if(visit.Admission==TravelerAdmission.Refused)
                return TravelerJourney.CreateRefused(person.AppearanceSeed,visit.DesiredArrival,visit.CommuteSeconds,visit.Walled,route,visit.AttemptedAt);
            return TravelerJourney.Create(person.AppearanceSeed,visit.DesiredArrival,visit.Nights,visit.CommuteSeconds,visit.Walled,route);
        }
        private static TravelerPerson CopyTraveler(TravelerPerson person)
        {
            Require(person!=null && DefenseId(person.Id) && (person.HomeKey>=0 || person.Id.StartsWith("traveler:",StringComparison.Ordinal)) && DefenseId(person.Name) &&
                person.HomeKey>=-1 &&
                person.HomeSettlement>=0 && person.Race>=0 && (person.Sex==ResidentSex.Male || person.Sex==ResidentSex.Female) &&
                (person.IsAlive ? person.DiedAt==-1 : DefenseTime(person.DiedAt)),"Invalid traveler identity.");
            return new TravelerPerson { Id=person.Id,Name=person.Name,HomeSettlement=person.HomeSettlement,Race=person.Race,
                HomeKey=person.HomeKey,AppearanceSeed=person.AppearanceSeed,Sex=person.Sex,IsAlive=person.IsAlive,DiedAt=person.DiedAt };
        }
        private static TravelerVisit CopyTravelerVisit(TravelerVisit visit,WorldState world)
        {
            Require(visit!=null && DefenseId(visit.Id) && visit.Id.Length<=480 && DefenseId(visit.PersonId) &&
                world.Settlements.Exists(t=>t.SettlementId==visit.SettlementId) && visit.TavernKey>=0 &&
                visit.Route!=null && visit.Route.Count>=2 && visit.Route.Count<=10000 &&
                (visit.Admission==TravelerAdmission.Pending || visit.Admission==TravelerAdmission.Admitted || visit.Admission==TravelerAdmission.Refused),"Invalid traveler visit.");
            var person=world.Travelers.Find(p=>p.Id==visit.PersonId);
            Require(person!=null && person.HomeSettlement!=visit.SettlementId,"Missing or local traveler.");
            var copy=new TravelerVisit { Id=visit.Id,PersonId=visit.PersonId,BookingId=visit.BookingId,SettlementId=visit.SettlementId,
                TavernKey=visit.TavernKey,Nights=visit.Nights,Walled=visit.Walled,DesiredArrival=visit.DesiredArrival,
                CommuteSeconds=visit.CommuteSeconds,AttemptedAt=visit.AttemptedAt,Admission=visit.Admission,Purpose=visit.Purpose,
                NativeReturnSeconds=visit.NativeReturnSeconds,ReturnDepartureNotBefore=visit.ReturnDepartureNotBefore,
                Diversion=CopyTravelerDiversion(visit.Diversion) };
            Require(Enum.IsDefined(typeof(ResidentVisitPurpose),copy.Purpose),"Invalid traveler purpose.");
            foreach(var point in visit.Route)
            {
                Require(point!=null && DefenseCoordinate(point.X) && DefenseCoordinate(point.Z),"Invalid traveler route coordinate.");
                copy.Route.Add(new SavedRoutePoint { X=point.X,Z=point.Z });
            }
            var journey=Journey(person,copy);
            Require(DefenseTime(copy.NativeReturnSeconds) && DefenseTime(copy.ReturnDepartureNotBefore) &&
                (copy.ReturnDepartureNotBefore==0 || copy.NativeReturnSeconds>0 && copy.ReturnDepartureNotBefore>=journey.ExitAt) &&
                DefenseTime(TravelerHomeAt(person,copy)),"Invalid native traveler return timing.");
            // Native JSON uses limited decimal precision. At calendar epochs near
            // 1e10 seconds, recomputing entry + commute can differ by ~0.00005s.
            // Keep strict identity and exact-bed ownership while allowing sub-ms time loss.
            Require(visit.Admission==TravelerAdmission.Pending ? visit.AttemptedAt==-1 && visit.BookingId==null && visit.Diversion==null :
                DefenseTime(visit.AttemptedAt) && (visit.Diversion==null?visit.AttemptedAt+0.001>=journey.CheckInAt:
                Math.Abs(visit.AttemptedAt-visit.Diversion.DecidedAt)<0.001),"Invalid traveler admission outcome.");
            if(visit.Diversion!=null)ValidateTravelerDiversion(copy,person);
            if(visit.Admission==TravelerAdmission.Admitted)
            {
                Require(person.IsAlive || person.DiedAt>=visit.AttemptedAt,"Traveler death precedes admission.");
                var tavern=world.Lodgings.Find(t=>t.SettlementId==visit.SettlementId && t.BuildingKey==visit.TavernKey);
                var booking=tavern==null ? null : tavern.Bookings.Find(b=>b.Id==visit.BookingId);
                Require(visit.BookingId=="traveler-booking:"+visit.Id && booking!=null && !booking.Player && booking.GuestId==person.Id &&
                    booking.HomeSettlement==person.HomeSettlement && booking.PartySize==1 && Math.Abs(booking.StartsAt-journey.CheckInAt)<0.001 &&
                    Math.Abs(booking.EndsAt-journey.CheckOutAt)<0.001 && visit.AttemptedAt<journey.CheckOutAt,"Traveler lacks matching paid bed.");
            }
            else Require(visit.BookingId==null && !world.Lodgings.Exists(t=>t.Bookings.Exists(b=>b.Id=="traveler-booking:"+visit.Id)),
                "Unadmitted traveler has a booking.");
            return copy;
        }
        private static bool SameVisit(TravelerVisit a,TravelerVisit b)
        {
            if(a.PersonId!=b.PersonId || a.SettlementId!=b.SettlementId || a.TavernKey!=b.TavernKey || a.Nights!=b.Nights ||
                a.Walled!=b.Walled || a.DesiredArrival!=b.DesiredArrival || a.CommuteSeconds!=b.CommuteSeconds ||
                a.NativeReturnSeconds!=b.NativeReturnSeconds || a.Purpose!=b.Purpose || a.Route.Count!=b.Route.Count) return false;
            for(int i=0;i<a.Route.Count;i++) if(b.Route[i]==null || a.Route[i].X!=b.Route[i].X || a.Route[i].Z!=b.Route[i].Z) return false;
            return true;
        }
        private static void CopyTravelers(WorldState source,WorldState world,HashSet<string> identities)
        {
            Require(source.Travelers!=null && source.TravelerVisits!=null,"Missing traveler ledgers.");
            foreach(var person in source.Travelers)
            {
                var copy=CopyTraveler(person);
                Require((identities.Add(copy.Id) && copy.HomeKey<0 || MatchingOrigin(world,copy)) &&
                    !world.Travelers.Exists(p=>p.Id==copy.Id) && !world.NativePeople.Exists(p=>p.Id==copy.Id),"Duplicate traveler identity.");
                world.Travelers.Add(copy);
            }
            var ids=new HashSet<string>();
            foreach(var visit in source.TravelerVisits)
            {
                var copy=CopyTravelerVisit(visit,world); Require(ids.Add(copy.Id),"Duplicate traveler visit.");
                var person=world.Travelers.Find(p=>p.Id==copy.PersonId); var journey=Journey(person,copy);
                foreach(var other in world.TravelerVisits)
                {
                    if(other.PersonId!=copy.PersonId) continue;
                    var previous=Journey(person,other);
                    Require(journey.EntryAt>=TravelerVisitConflictUntil(person,other) || previous.EntryAt>=TravelerVisitConflictUntil(person,copy),"Traveler visits overlap.");
                }
                world.TravelerVisits.Add(copy);
            }
        }
    }
}
