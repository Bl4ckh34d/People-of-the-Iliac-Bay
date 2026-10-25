using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcSocialFamiliarityTests
    {
        private static WorldStateStore Familiar(out string id)
        {
            var store=new WorldStateStore(61);
            id=store.GetOrCreate(100,new[]{7},1).Households[0].Residents[0].Id;
            store.SetConversationBaseline(id,40,100);
            store.RecordConversationEncounter(id,100);
            store.RememberConversation(id,100,"day 1","inn","How is your family?","They are well.","native");
            store.RecordConversationEncounter(id,200);
            store.RememberConversation(id,200,"day 1","inn","I am glad to hear that.","Thank you for asking.","model");
            return store;
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Friendly approaches require actual exchanges and familiarity, not native friendliness alone",()=>{
                var store=new WorldStateStore(61);
                var id=store.GetOrCreate(100,new[]{7},1).Households[0].Residents[0].Id;
                store.SetConversationBaseline(id,100,0);
                store.RecordConversationEncounter(id,0);store.RecordConversationEncounter(id,100);
                store.RememberObservation(id,100,"day 1","inn","The player walked nearby.");
                Check.True(!store.IsEligibleForFriendlyProactiveContact(id,86400));
                Check.Equal(0,store.Snapshot().ConversationAttitudes.Find(item=>item.PersonId==id).SharedExchangeCount);
                store.RememberConversation(id,200,"day 1","inn","Good day.","Good day to you.","native");
                Check.True(!store.IsEligibleForFriendlyProactiveContact(id,86400));
                store.RememberConversation(id,300,"day 1","inn","How are you?","Quite well.","model");
                Check.True(store.IsEligibleForFriendlyProactiveContact(id,86400));
                Check.True(!store.IsEligibleForFriendlyProactiveContact("unknown",86400));
                Check.True(!store.IsEligibleForFriendlyProactiveContact(id,double.NaN));
            }));
            cases.Add(("Friendly contact cooldown survives branch restore and prevents immediate reopening",()=>{
                var store=Familiar(out var id);
                double first=200+4*3600;
                Check.True(!store.IsEligibleForFriendlyProactiveContact(id,first-1));
                Check.True(store.IsEligibleForFriendlyProactiveContact(id,first));
                store.RecordConversationEncounter(id,first);
                store.RecordFriendlyProactiveContact(id,first);
                store.RecordFriendlyProactiveContact(id,first);
                Check.True(!store.IsEligibleForFriendlyProactiveContact(id,first+86400-1));
                var snapshot=store.Snapshot();
                Check.Equal(1,snapshot.ConversationAttitudes.Find(item=>item.PersonId==id).FriendlyInitiatedCount);
                var options=new JsonSerializerOptions {IncludeFields=true};
                var restored=new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(snapshot,options),options));
                Check.True(!restored.IsEligibleForFriendlyProactiveContact(id,first+1));
                Check.True(restored.IsEligibleForFriendlyProactiveContact(id,first+86400));
                restored.RecordFriendlyProactiveContact(id,first+86400);
                Check.Equal(1,store.Snapshot().ConversationAttitudes.Find(item=>item.PersonId==id).FriendlyInitiatedCount);
                Check.Equal(2,restored.Snapshot().ConversationAttitudes.Find(item=>item.PersonId==id).FriendlyInitiatedCount);
            }));
            cases.Add(("Hostility, death and protected ownership prevent friendly contact",()=>{
                var store=Familiar(out var id);
                store.SetConversationBaseline(id,29,200);
                Check.True(!store.IsEligibleForFriendlyProactiveContact(id,86400));
                store.SetConversationThreatDisposition(id,40,300);
                Check.True(!store.IsEligibleForFriendlyProactiveContact(id,86400));
                store=Familiar(out id);
                var snapshot=store.Snapshot();
                snapshot.Settlements[0].Households[0].Residents[0].IsAlive=false;
                store.Restore(snapshot);
                Check.True(!store.IsEligibleForFriendlyProactiveContact(id,86400));
            }));
            cases.Add(("Malformed social counters cannot corrupt restore or fabricate familiarity",()=>{
                var store=Familiar(out var id);var snapshot=store.Snapshot();
                snapshot.ConversationAttitudes.Find(item=>item.PersonId==id).SharedExchangeCount=-1;
                Check.Throws<ArgumentException>(()=>store.Restore(snapshot));
                Check.True(store.IsEligibleForFriendlyProactiveContact(id,86400));
                snapshot=store.Snapshot();snapshot.ConversationAttitudes.Find(item=>item.PersonId==id).LastFriendlyInitiatedAt=double.NaN;
                Check.Throws<ArgumentException>(()=>store.Restore(snapshot));
            }));
            cases.Add(("Friendly shared facts stay attributed and exclude future exchanges across cached cutoffs",()=>{
                var store=Familiar(out var id);
                store.RememberConversation(id,500,"day 1","shop","My brother is Daric.","My brother is Marius.","model");
                Check.True(store.TryGetLatestSharedExchange(id,250,out var old));
                Check.True(!old.PlayerText.Contains("Daric"));
                Check.True(store.TryGetLatestSharedExchange(id,600,out var latest));
                Check.Equal("My brother is Daric.",latest.PlayerText);
                Check.Equal("My brother is Marius.",latest.NpcText);
                latest.PlayerText="changed";
                Check.True(store.TryGetLatestSharedExchange(id,600,out latest));
                Check.Equal("My brother is Daric.",latest.PlayerText);
                Check.True(store.TryGetLatestSharedExchange(id,250,out old));
                Check.True(!old.PlayerText.Contains("Daric"));
                store.RememberConversation(id,700,"day 1","inn","My sister is Mara.","I remember that.","model");
                Check.True(store.TryGetLatestSharedExchange(id,700,out latest));
                Check.Equal("My sister is Mara.",latest.PlayerText);
            }));
        }
    }
}
