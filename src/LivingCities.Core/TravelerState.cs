using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public enum TravelerAdmission { Pending, Admitted, Refused }
    [Serializable]
    public sealed class TravelerPerson
    {
        public string Id,Name;
        public int HomeSettlement,Race;
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
        public TravelerAdmission Admission;
        public List<SavedRoutePoint> Route=new List<SavedRoutePoint>();
    }
    public sealed partial class WorldStateStore
    {
        public void RegisterTraveler(TravelerPerson person)
        {
            var copied=CopyTraveler(person);
            Require(copied.IsAlive,"New travelers must be alive.");
            var prior=state.Travelers.Find(p=>p.Id==copied.Id);
            if(prior!=null)
            {
                Require(prior.Name==copied.Name && prior.HomeSettlement==copied.HomeSettlement && prior.Race==copied.Race &&
                    prior.AppearanceSeed==copied.AppearanceSeed && prior.Sex==copied.Sex,"Traveler identity changed.");
                return; // Idempotent registration must never revive a saved death.
            }
            foreach(var town in state.Settlements) foreach(var home in town.Households)
                Require(!home.Residents.Exists(r=>r.Id==copied.Id),"Traveler duplicates resident identity.");
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
            foreach(var other in state.TravelerVisits)
            {
                if(other.PersonId!=copy.PersonId) continue;
                var otherJourney=Journey(person,other);
                Require(journey.EntryAt>=otherJourney.ExitAt || otherJourney.EntryAt>=journey.ExitAt,"Traveler visits overlap.");
            }
            state.TravelerVisits.Add(copy);
        }
        public bool TryAdmitTraveler(string visitId,double now)
        {
            Require(DefenseTime(now),"Invalid traveler admission time.");
            var visit=state.TravelerVisits.Find(v=>v.Id==visitId); Require(visit!=null,"Unknown traveler visit.");
            if(visit.Admission!=TravelerAdmission.Pending) return visit.Admission==TravelerAdmission.Admitted;
            var person=state.Travelers.Find(p=>p.Id==visit.PersonId); var journey=Journey(person,visit);
            if(now<journey.CheckInAt) return false;
            string booking="traveler-booking:"+visit.Id;
            bool admitted=person.IsAlive && now<journey.CheckOutAt && TryBookLodging(visit.SettlementId,visit.TavernKey,
                booking,person.Id,person.HomeSettlement,false,1,journey.CheckInAt,journey.CheckOutAt);
            visit.AttemptedAt=now; visit.Admission=admitted ? TravelerAdmission.Admitted : TravelerAdmission.Refused;
            visit.BookingId=admitted ? booking : null;
            return admitted;
        }
        public bool KillTraveler(string personId,double now)
        {
            Require(DefenseTime(now),"Invalid traveler death time.");
            var person=state.Travelers.Find(p=>p.Id==personId); Require(person!=null,"Unknown traveler.");
            if(!person.IsAlive) return false;
            Require(!state.TravelerVisits.Exists(v=>v.PersonId==personId &&
                v.Admission==TravelerAdmission.Admitted && v.AttemptedAt>now),"Traveler death precedes admission.");
            person.IsAlive=false; person.DiedAt=now; return true;
        }
        private static TravelerJourney Journey(TravelerPerson person,TravelerVisit visit)
        {
            var route=new List<RoutePoint>(); foreach(var p in visit.Route) route.Add(new RoutePoint(p.X,p.Z));
            return TravelerJourney.Create(person.AppearanceSeed,visit.DesiredArrival,visit.Nights,visit.CommuteSeconds,visit.Walled,route);
        }
        private static TravelerPerson CopyTraveler(TravelerPerson person)
        {
            Require(person!=null && DefenseId(person.Id) && person.Id.StartsWith("traveler:",StringComparison.Ordinal) && DefenseId(person.Name) &&
                person.HomeSettlement>=0 && person.Race>=0 && (person.Sex==ResidentSex.Male || person.Sex==ResidentSex.Female) &&
                (person.IsAlive ? person.DiedAt==-1 : DefenseTime(person.DiedAt)),"Invalid traveler identity.");
            return new TravelerPerson { Id=person.Id,Name=person.Name,HomeSettlement=person.HomeSettlement,Race=person.Race,
                AppearanceSeed=person.AppearanceSeed,Sex=person.Sex,IsAlive=person.IsAlive,DiedAt=person.DiedAt };
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
                CommuteSeconds=visit.CommuteSeconds,AttemptedAt=visit.AttemptedAt,Admission=visit.Admission };
            foreach(var point in visit.Route)
            {
                Require(point!=null && DefenseCoordinate(point.X) && DefenseCoordinate(point.Z),"Invalid traveler route coordinate.");
                copy.Route.Add(new SavedRoutePoint { X=point.X,Z=point.Z });
            }
            var journey=Journey(person,copy);
            Require(visit.Admission==TravelerAdmission.Pending ? visit.AttemptedAt==-1 && visit.BookingId==null :
                DefenseTime(visit.AttemptedAt) && visit.AttemptedAt>=journey.CheckInAt,"Invalid traveler admission outcome.");
            if(visit.Admission==TravelerAdmission.Admitted)
            {
                Require(person.IsAlive || person.DiedAt>=visit.AttemptedAt,"Traveler death precedes admission.");
                var tavern=world.Lodgings.Find(t=>t.SettlementId==visit.SettlementId && t.BuildingKey==visit.TavernKey);
                var booking=tavern==null ? null : tavern.Bookings.Find(b=>b.Id==visit.BookingId);
                Require(visit.BookingId=="traveler-booking:"+visit.Id && booking!=null && !booking.Player && booking.GuestId==person.Id &&
                    booking.HomeSettlement==person.HomeSettlement && booking.PartySize==1 && booking.StartsAt==journey.CheckInAt &&
                    booking.EndsAt==journey.CheckOutAt && visit.AttemptedAt<journey.CheckOutAt,"Traveler lacks matching paid room.");
            }
            else Require(visit.BookingId==null && !world.Lodgings.Exists(t=>t.Bookings.Exists(b=>b.Id=="traveler-booking:"+visit.Id)),
                "Unadmitted traveler has a booking.");
            return copy;
        }
        private static bool SameVisit(TravelerVisit a,TravelerVisit b)
        {
            if(a.PersonId!=b.PersonId || a.SettlementId!=b.SettlementId || a.TavernKey!=b.TavernKey || a.Nights!=b.Nights ||
                a.Walled!=b.Walled || a.DesiredArrival!=b.DesiredArrival || a.CommuteSeconds!=b.CommuteSeconds || a.Route.Count!=b.Route.Count) return false;
            for(int i=0;i<a.Route.Count;i++) if(b.Route[i]==null || a.Route[i].X!=b.Route[i].X || a.Route[i].Z!=b.Route[i].Z) return false;
            return true;
        }
        private static void CopyTravelers(WorldState source,WorldState world,HashSet<string> identities)
        {
            if(source.Version<9) return;
            Require(source.Travelers!=null && source.TravelerVisits!=null,"Missing traveler ledgers.");
            foreach(var person in source.Travelers)
            {
                var copy=CopyTraveler(person);
                Require(identities.Add(copy.Id) && !world.NativePeople.Exists(p=>p.Id==copy.Id),"Duplicate traveler identity.");
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
                    Require(journey.EntryAt>=previous.ExitAt || previous.EntryAt>=journey.ExitAt,"Traveler visits overlap.");
                }
                world.TravelerVisits.Add(copy);
            }
        }
    }
}
