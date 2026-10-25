using System;
namespace PeopleOfTheIliacBay.Core
{
    public sealed class Bereavement
    {
        public string ResidentId,Name;
        public double DiedAt,MentionChance;
        public bool WitnessedPlayerKilling;
    }
    public sealed partial class WorldStateStore
    {
        public bool HoldsPartnerGrudge(Resident resident,double now)
        {
            if(resident==null || !ValidTime(now)) throw new ArgumentException("Invalid grudge query.");
            if(!resident.IsAlive) return false;
            var family=new System.Collections.Generic.HashSet<string>();
            if(!string.IsNullOrEmpty(resident.PartnerId)) family.Add(resident.PartnerId);
            foreach(var town in state.Settlements) foreach(var home in town.Households)
                foreach(var person in home.Residents)
                    if(person.MotherId==resident.Id || person.FatherId==resident.Id) family.Add(person.Id);
            return state.AlarmIncidents.Exists(a=>family.Contains(a.VictimId) && a.ThreatId==null &&
                a.Corpse!=null && a.Corpse.DiedAt<=now &&
                a.People.Exists(p=>p.ResidentId==resident.Id && p.DirectWitness));
        }
        // Derive from persisted family and corpse records; cleanup does not erase grief.
        public Bereavement BereavementFor(Resident resident,double now)
        {
            if(resident==null || !ValidTime(now)) throw new ArgumentException("Invalid bereavement query.");
            if(!resident.IsAlive || string.IsNullOrEmpty(resident.PartnerId)) return null;
            Resident partner=null;
            foreach(var town in state.Settlements)
            {
                foreach(var home in town.Households)
                {partner=home.Residents.Find(p=>p.Id==resident.PartnerId);if(partner!=null) break;}
                if(partner!=null) break;
            }
            if(partner==null || partner.IsAlive) return null;
            var death=state.AlarmIncidents.Find(a=>a.VictimId==partner.Id && a.Corpse!=null);
            if(death==null || now<death.Corpse.DiedAt) return null;
            double elapsed=now-death.Corpse.DiedAt,duration=720*86400d;
            if(elapsed>=duration) return null;
            return new Bereavement {ResidentId=partner.Id,Name=partner.FullName,DiedAt=death.Corpse.DiedAt,
                MentionChance=.6*(1-elapsed/duration),WitnessedPlayerKilling=death.ThreatId==null &&
                    death.People.Exists(p=>p.ResidentId==resident.Id && p.DirectWitness)};
        }
    }
}
