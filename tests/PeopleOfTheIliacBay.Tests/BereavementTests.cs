using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class BereavementTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Partner mourning requires a confirmed death and fades across two game years",()=>{
                var store=new WorldStateStore(42);Household family=null;
                for(int k=1;k<30 && family==null;k++)
                {var town=store.GetOrCreateFamilies(100+k,new[]{new HomeProfile(k,5,100)});if(town.Households[0].Residents.Count>=2) family=town.Households[0];}
                var person=family.Residents[0];var partner=family.Residents[1];int townId=0;
                foreach(var town in store.Snapshot().Settlements) if(town.Households[0].BuildingKey==family.BuildingKey) townId=town.SettlementId;
                Check.True(store.BereavementFor(person,100)==null);
                var incident=store.RecordViolence(townId,partner.Id,100);
                store.Witness(incident.Id,person.Id,100);
                Check.True(store.BereavementFor(person,101)==null);
                partner.IsAlive=false;store.LocateViolence(incident.Id,family.BuildingKey,0,0,0);store.ConfirmCorpse(incident.Id,102,family.BuildingKey,0,0,0);
                var recent=store.BereavementFor(person,103);Check.Equal(partner.FullName,recent.Name);
                Check.True(recent.WitnessedPlayerKilling);
                Check.True(store.HoldsPartnerGrudge(person,103));
                incident.Corpse.Cleared=true;incident.Corpse.ClearedAt=102+86400;
                Check.Equal(partner.Id,store.BereavementFor(person,103+86400).ResidentId);
                Check.True(store.BereavementFor(person,101)==null);
                var later=store.BereavementFor(person,102+360*86400d);
                Check.True(later.MentionChance<recent.MentionChance);
                store.Restore(store.Snapshot());Check.Equal(partner.Id,store.BereavementFor(person,103).ResidentId);
                Check.True(store.BereavementFor(person,102+720*86400d)==null);
                Check.True(store.HoldsPartnerGrudge(person,102+900*86400d));
                var saved=store.Snapshot();saved.AlarmIncidents[0].People[0].DirectWitness=false;store.Restore(saved);
                Check.True(!store.HoldsPartnerGrudge(person,103));
            }));
            cases.Add(("Witnessed child murder creates a lasting parental grudge",()=>{
                var store=new WorldStateStore(42);Household family=null;int townId=0;
                for(int k=1;k<100 && family==null;k++)
                {var town=store.GetOrCreateFamilies(100+k,new[]{new HomeProfile(k,5,100)});if(town.Households[0].Residents.Count>=3) {family=town.Households[0];townId=town.SettlementId;}}
                var child=family.Residents.Find(p=>p.AgeGroup==ResidentAgeGroup.Child);
                var parent=family.Residents.Find(p=>p.Id==child.MotherId);
                var incident=store.RecordViolence(townId,child.Id,100);store.Witness(incident.Id,parent.Id,100);
                child.IsAlive=false;store.LocateViolence(incident.Id,family.BuildingKey,0,0,0);store.ConfirmCorpse(incident.Id,101);
                store.Restore(store.Snapshot());Check.True(store.HoldsPartnerGrudge(parent,102+900*86400d));
            }));
        }
    }
}
