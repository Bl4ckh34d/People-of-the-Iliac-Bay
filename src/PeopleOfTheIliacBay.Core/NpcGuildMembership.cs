using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcGuildMembership
    {
        public static bool Matches(Resident person,int faction)
        {return person!=null && person.IsAlive && MatchesIdentity(person,faction);}
        public static bool MatchesIdentity(Resident person,int faction)
        {return person!=null && person.AgeGroup==ResidentAgeGroup.Adult && person.GuildIdentityKnown &&
            faction>0 && person.GuildFactionId==faction && person.GuildRank>=0 && person.GuildRank<=9 && person.GuildHomeKey>=0;}
        public static void Validate(Resident person)
        {
            bool none=person.GuildFactionId==0 && person.GuildRank==-1 && person.GuildHomeKey==-1;
            if(!none && (!person.GuildIdentityKnown || person.AgeGroup!=ResidentAgeGroup.Adult ||
                person.GuildFactionId<=0 || person.GuildRank<0 || person.GuildRank>9 || person.GuildHomeKey<0))
                throw new ArgumentException("Invalid saved NPC guild identity.");
        }
    }
    public sealed partial class WorldStateStore
    {
        // The native adapter establishes the actual home hall and native entry
        // skill requirements once. Unknown is distinct from a known nonmember.
        public bool RecordInitialNpcGuildIdentity(string personId,int faction,int rank,int homeGuildKey)
        {
            var person=ResolveResident(personId);
            if(person==null || !person.IsAlive || person.IsQuestProtected || person.AgeGroup!=ResidentAgeGroup.Adult || person.GuildIdentityKnown)return false;
            if(faction==0 ? rank!=-1 || homeGuildKey!=-1 : faction<0 || rank<0 || rank>9 || homeGuildKey<0)return false;
            person.GuildIdentityKnown=true;person.GuildFactionId=faction;person.GuildRank=rank;person.GuildHomeKey=homeGuildKey;return true;
        }
        public bool HasNpcGuildMembership(string personId,int faction)
        {return NpcGuildMembership.Matches(ResolveResident(personId),faction);}
        public int LodgingGuildFaction(int town,int building)
        {var lodging=state.Lodgings.Find(value=>value.SettlementId==town && value.BuildingKey==building);return lodging==null?0:lodging.GuildFactionId;}
        public void RegisterGuildLodgingBeds(int town,int building,int faction,IEnumerable<LodgingBed> beds)
        {
            Require(faction>0,"Guild lodging requires an actual faction.");
            RegisterLodgingBeds(town,building,beds);var lodging=Lodging(town,building);
            Require((lodging.GuildFactionId==0 || lodging.GuildFactionId==faction) &&
                lodging.Bookings.TrueForAll(value=>!value.Player && value.PartySize==1 && GuildGuest(state,value.GuestId,value.HomeSettlement,faction,false)),
                "Guild lodging cannot replace another identity or admit an unauthorized guest.");
            lodging.GuildFactionId=faction;
        }
        private static bool GuildGuest(WorldState world,string person,int home,int faction,bool requireAlive=true)
        {var resident=ResolveResident(world,home,person);return NpcGuildMembership.MatchesIdentity(resident,faction) && (!requireAlive || resident.IsAlive);}
    }
}
