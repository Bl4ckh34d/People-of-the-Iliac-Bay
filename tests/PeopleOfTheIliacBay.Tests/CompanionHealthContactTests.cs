using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class CompanionHealthContactTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Companion health request uses contact receipt and suppresses repeat contact",new Action(()=>{
                var store=new WorldStateStore(71);
                string resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
                Check.True(!store.TryGetCompanionHealthContactOffer(resident,"injury",1000,out _));
                Check.True(!store.TryGetCompanionHealthContactOffer("unknown","poison",1000,out _));
                Check.True(store.TryGetCompanionHealthContactOffer(resident,"poison",1000,out var offer));
                Check.Equal(ProactiveContactKind.CompanionHealthRequest,offer.Kind);
                Check.Equal("poison",offer.Purpose);
                Check.True(store.TryNoticeProactiveContactPlayer(offer,1000,1030));
                Check.True(store.TryGetProactiveContactOffer(resident,1001,out var noticed));
                Check.Equal(offer.ContactId,noticed.ContactId);
                Check.True(store.TryClaimProactiveContactConversation(offer.ContactId,offer.Kind,resident,resident,
                    1002,true,true,true,true,out var receipt,out var isNew));
                Check.True(isNew);Check.Equal("poison",receipt.Purpose);
                Check.True(!store.TryGetCompanionHealthContactOffer(resident,"poison",1003,out _));
                Check.True(!store.TryGetCompanionHealthContactOffer(resident,"disease",1003,out _));
                var json=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                var restored=new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                Check.True(!restored.TryGetCompanionHealthContactOffer(resident,"poison",1003,out _));
                Check.True(restored.TryGetCompanionHealthContactOffer(resident,"disease",86400,out var nextDay));
                Check.True(nextDay.ContactId!=offer.ContactId);
            })));

            cases.Add(("Companion health request interruption consumes the daily attempt",new Action(()=>{
                var store=new WorldStateStore(72);
                string resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
                Check.True(store.TryGetCompanionHealthContactOffer(resident,"disease",1000,out var offer));
                Check.True(store.TryNoticeProactiveContactPlayer(offer,1000,1030));
                Check.True(store.TryInterruptProactiveContactApproach(offer.ContactId,offer.Kind,resident,1001));
                Check.True(!store.TryGetCompanionHealthContactOffer(resident,"disease",1002,out _));
                Check.True(!store.TryGetCompanionHealthContactOffer(resident,"poison",1002,out _));
            })));
        }
    }
}
