using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public const double FriendlyContactCooldownSeconds=86400;
        public const double FriendlyConversationGapSeconds=4*3600;
        private WorldState socialAttitudeOwner;
        private int socialAttitudeCount=-1;
        private Dictionary<string,ConversationAttitude> socialAttitudes;
        private WorldState sharedExchangeOwner;
        private readonly Dictionary<string,SharedExchangeLookup> sharedExchangeLookups=
            new Dictionary<string,SharedExchangeLookup>(StringComparer.Ordinal);

        private sealed class SharedExchangeLookup
        {
            public NpcConversationMemory Journal;
            public int Count;
            public double AsOf;
            public double NextAt;
            public NpcMemoryEntry Entry;
        }

        private ConversationAttitude SocialAttitude(string id)
        {
            if(string.IsNullOrWhiteSpace(id))return null;
            if(socialAttitudeOwner!=state || socialAttitudeCount!=state.ConversationAttitudes.Count)
            {
                socialAttitudes=new Dictionary<string,ConversationAttitude>(StringComparer.Ordinal);
                foreach(var value in state.ConversationAttitudes)socialAttitudes[value.PersonId]=value;
                socialAttitudeOwner=state;socialAttitudeCount=state.ConversationAttitudes.Count;
            }
            ConversationAttitude found;
            return socialAttitudes.TryGetValue(id,out found)?found:null;
        }

        private void RecordSharedConversationExchange(string id,double now)
        {
            var attitude=SocialAttitude(id);
            if(attitude==null)
            {
                attitude=new ConversationAttitude {PersonId=id,Day=Math.Floor(now/86400)};
                state.ConversationAttitudes.Add(attitude);
            }
            if(attitude.SharedExchangeCount<1000000)attitude.SharedExchangeCount++;
            sharedExchangeLookups.Remove(id);
        }

        public bool IsEligibleForFriendlyProactiveContact(string residentId,double now)
        {
            if(!Finite(now)||now<0)return false;
            var person=ResolveResident(residentId);
            if(person==null || !person.IsAlive || person.AgeGroup!=ResidentAgeGroup.Adult || person.IsQuestProtected)return false;
            var attitude=SocialAttitude(residentId);
            return attitude!=null && attitude.HasNativeBaseline &&
                ConversationEffects.Reaction(attitude.NativeBaseline,attitude.Offset)>=30 &&
                attitude.EncounterCount>=2 && attitude.SharedExchangeCount>=2 &&
                now>=attitude.LastEncounterAt+FriendlyConversationGapSeconds &&
                (attitude.FriendlyInitiatedCount==0 || now>=attitude.LastFriendlyInitiatedAt+FriendlyContactCooldownSeconds);
        }

        // Called only after the real window opens. Repeated same-event confirmation is inert.
        public void RecordFriendlyProactiveContact(string residentId,double now)
        {
            Require(Finite(now)&&now>=0,"Invalid friendly contact time.");
            var attitude=SocialAttitude(residentId);
            if(attitude!=null && attitude.FriendlyInitiatedCount>0 && attitude.LastFriendlyInitiatedAt==now)return;
            // Opening the real window can already have recorded its new encounter.
            // The claim checked the gap before opening; do not reject that receipt
            // because LastEncounterAt now refers to the encounter being confirmed.
            var person=ResolveResident(residentId);
            Require(person!=null && person.IsAlive && person.AgeGroup==ResidentAgeGroup.Adult && !person.IsQuestProtected &&
                attitude!=null && attitude.HasNativeBaseline &&
                ConversationEffects.Reaction(attitude.NativeBaseline,attitude.Offset)>=30 &&
                attitude.EncounterCount>=2 && attitude.SharedExchangeCount>=2 &&
                (attitude.FriendlyInitiatedCount==0 || now>=attitude.LastFriendlyInitiatedAt+FriendlyContactCooldownSeconds),
                "Friendly contact is not eligible.");
            if(attitude.FriendlyInitiatedCount<1000000)attitude.FriendlyInitiatedCount++;
            attitude.LastFriendlyInitiatedAt=now;
        }

        public bool TryGetLatestSharedExchange(string residentId,double now,out NpcMemoryEntry entry)
        {
            entry=null;
            if(string.IsNullOrWhiteSpace(residentId)||!Finite(now)||now<0)return false;
            if(sharedExchangeOwner!=state)
            {sharedExchangeLookups.Clear();sharedExchangeOwner=state;}
            var journal=MemoryFor(residentId,false);
            if(journal==null)return false;
            SharedExchangeLookup cached;
            if(!sharedExchangeLookups.TryGetValue(residentId,out cached) || !ReferenceEquals(cached.Journal,journal) ||
                cached.Count!=journal.Entries.Count || now<cached.AsOf || now>=cached.NextAt)
            {
                NpcMemoryEntry latest=null;
                double nextAt=double.PositiveInfinity;
                foreach(var value in journal.Entries)
                    if(ConversationMemoryProvenance.IsSuccessfulSharedExchange(value))
                    {
                        if(value.At>now){nextAt=Math.Min(nextAt,value.At);continue;}
                        if(latest==null || value.At>=latest.At)latest=value;
                    }
                cached=new SharedExchangeLookup {Journal=journal,Count=journal.Entries.Count,AsOf=now,NextAt=nextAt,Entry=latest};
                sharedExchangeLookups[residentId]=cached;
            }
            if(cached.Entry==null)return false;
            var source=cached.Entry;
            entry=new NpcMemoryEntry {At=source.At,Date=source.Date,Place=source.Place,PlayerText=source.PlayerText,
                NpcText=source.NpcText,Observation=source.Observation,Source=source.Source};
            return true;
        }
    }
}
