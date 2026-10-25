using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcSceneMemoryTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Health and pursuit memories evaluate the affected player consistently for friends and enemies",()=>
            {
                var store=new WorldStateStore(13);var town=store.GetOrCreate(1,new[]{7,8},1);
                string friend=town.Households[0].Residents[0].Id,enemy=town.Households[1].Residents[0].Id;
                store.SetConversationBaseline(friend,80,100);store.SetConversationBaseline(enemy,-80,100);
                foreach(var kind in new[]{NpcSceneEventKind.Injury,NpcSceneEventKind.Illness,NpcSceneEventKind.GuardPursuit,
                    NpcSceneEventKind.Healing,NpcSceneEventKind.Recovery,NpcSceneEventKind.Help})
                {
                    bool harmful=kind==NpcSceneEventKind.Injury || kind==NpcSceneEventKind.Illness || kind==NpcSceneEventKind.GuardPursuit;
                    foreach(var observer in new[]{friend,enemy})
                        store.RememberSceneEvent(observer,"event:"+kind,kind,100,1,-1,"A witnessed change to the player.",
                            subject:NpcDirectedRelationship.PlayerTargetId);
                    var rows=store.Snapshot().SceneMemories;
                    int first=rows.Find(x=>x.ObserverId==friend && x.Kind==kind).Valence;
                    int second=rows.Find(x=>x.ObserverId==enemy && x.Kind==kind).Valence;
                    Check.True(harmful?first<0:first>0,"Missing health/pursuit valuation: "+kind);
                    Check.Equal(-first,second);
                }
            }));
            cases.Add(("Renewed hostile incidents produce separate memories while repeated sightings do not",()=>
            {
                var store=new WorldStateStore(13);var town=store.GetOrCreate(1,new[]{7,8},1);
                string observer=town.Households[0].Residents[0].Id;
                var first=store.RecordHostileThreat(1,"enemy:1",100);store.Witness(first.Id,observer,100);
                store.Witness(first.Id,observer,101);
                var second=store.RecordHostileThreat(1,"enemy:1",2000);store.Witness(second.Id,observer,2000);
                Check.Equal(2,store.Snapshot().SceneMemories.FindAll(x=>x.ObserverId==observer && x.Kind==NpcSceneEventKind.Fight).Count);
            }));
            cases.Add(("Verified murder attribution refines a current eyewitness memory but not a late corpse discovery",()=>
            {
                var store=new WorldStateStore(13);var town=store.GetOrCreate(1,new[]{7,8,9},1);
                var victim=town.Households[0].Residents[0];string witness=town.Households[1].Residents[0].Id;
                string late=town.Households[2].Residents[0].Id;
                var incident=store.RecordViolence(1,victim.Id,100);
                store.LocateViolence(incident.Id,-1,0,0,0);
                Population.MarkDead(town,victim.Id);store.ConfirmCorpse(incident.Id,100,-1,0,0,0);
                store.Witness(incident.Id,witness,100);
                var before=store.Snapshot().SceneMemories.Find(x=>x.ObserverId==witness && x.Kind==NpcSceneEventKind.Murder);
                Check.True(before!=null && before.ResponsibleId==null);
                store.MarkPlayerMurder(incident.Id);
                var after=store.Snapshot().SceneMemories.Find(x=>x.ObserverId==witness && x.Kind==NpcSceneEventKind.Murder);
                Check.Equal(NpcDirectedRelationship.PlayerTargetId,after.ResponsibleId);
                Check.True(after.Observation.Contains("The player killed them"));Check.Equal(before.ExpiresAt,after.ExpiresAt);
                store.Witness(incident.Id,late,110);store.MarkPlayerMurder(incident.Id);
                var lateMemories=store.Snapshot().SceneMemories.FindAll(x=>x.ObserverId==late);
                Check.True(lateMemories.Exists(x=>x.Kind==NpcSceneEventKind.Corpse));
                Check.True(!lateMemories.Exists(x=>x.Kind==NpcSceneEventKind.Murder || x.Kind==NpcSceneEventKind.Assault || x.ResponsibleId!=null));
            }));
            cases.Add(("Friendship cannot make a native stamina purchase and unchanged resale profitable",()=>
            {
                int buy=NpcSocialPricing.MerchantQuote(33,18,true,45),sell=NpcSocialPricing.MerchantQuote(33,18,false,45);
                Check.True(sell<=buy,"Buying then reselling the same unchanged item creates gold: buy="+buy+"; sell="+sell);
                Check.True(buy<33 && sell>=18);
                Check.Equal(33,NpcSocialPricing.MerchantQuote(33,18,true,0));
                Check.Equal(18,NpcSocialPricing.MerchantQuote(33,18,false,0));
                for(int favour=-45;favour<=45;favour++)
                    foreach(int value in new[]{0,1,2,25,100,100000,int.MaxValue})
                    {
                        int purchase=NpcSocialPricing.MerchantQuote(value,value/2,true,favour);
                        int resale=NpcSocialPricing.MerchantQuote(value,value/2,false,favour);
                        Check.True(resale>=0 && resale<=purchase);
                    }
            }));
            cases.Add(("Stationary market identities retain personal event recall independently after reload",()=>
            {
                var store=new WorldStateStore(42);store.GetOrCreate(100,new[]{10},1);
                var first=store.RegisterNativePerson(100,-1,110,"Market Person",ResidentSex.Female,0,0,182,0);
                var second=store.RegisterNativePerson(100,-1,111,"Other Market Person",ResidentSex.Female,0,0,182,0);
                store.RememberSceneEvent(first.Id,"market-chat",NpcSceneEventKind.Conversation,100,100,-1,"I spoke with a passing resident.",1);
                store.RememberSceneEvent(first.Id,"market-attack",NpcSceneEventKind.Assault,120,100,-1,"I saw the player attack someone.",-3,
                    responsible:NpcDirectedRelationship.PlayerTargetId);
                var restored=new WorldStateStore(43);restored.Restore(store.Snapshot());
                Check.True(restored.RecallSceneEvents(first.Id,121).Contains("passing resident"));
                Check.Equal("",restored.RecallSceneEvents(second.Id,121));
                NpcMemoryReadSnapshot capture;Check.True(restored.TryCaptureNpcMemoryRead(first.Id,"",121,2000,out capture));
                Check.True(capture.SceneRecall.Contains("attack"));
            }));
            cases.Add(("Scene memory authority ignores another observer but expires at its own boundary",()=>
            {
                var store=new WorldStateStore(42);var resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.RememberSceneEvent(resident.Id,"company:1",NpcSceneEventKind.Company,100,100,10,"Two people were here.");
                NpcMemoryReadSnapshot read;Check.True(store.TryCaptureNpcMemoryRead(resident.Id,"",101,2000,out read));
                store.RememberSceneEvent("other","unrelated",NpcSceneEventKind.Company,101,100,10,"Someone else.");
                Check.True(store.IsNpcMemoryReadCurrent(read,resident.Id,101));
                double expiry=store.Snapshot().SceneMemories[0].ExpiresAt;
                Check.True(!store.IsNpcMemoryReadCurrent(read,resident.Id,expiry));
            }));
            cases.Add(("Pleasure at a disliked player's injury is not a friendly pricing credit",()=>
            {
                var store=new WorldStateStore(42);var resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                store.SetConversationBaseline(resident.Id,-80,100);
                int before=store.PersonalTradeFavour(resident.Id,100);
                store.RememberSceneEvent(resident.Id,"injury:1",NpcSceneEventKind.Injury,100,100,10,"The player was hurt.",
                    4,NpcDirectedRelationship.PlayerTargetId);
                Check.Equal(before,store.PersonalTradeFavour(resident.Id,100));
                store.RememberSceneEvent(resident.Id,"healed:1",NpcSceneEventKind.Healing,100,100,10,"The player healed me.",
                    3,resident.Id,NpcDirectedRelationship.PlayerTargetId);
                Check.True(store.PersonalTradeFavour(resident.Id,100)>before);
            }));
            cases.Add(("Social pricing is integer, symmetric, bounded and stable for a captured interaction",()=>
            {
                int neutral=NpcSocialPricing.Favour(null,0,0,0,0,0);
                int friend=NpcSocialPricing.Favour(90,80,30,20,10,3);
                int enemy=NpcSocialPricing.Favour(10,-80,30,20,-10,-3);
                Check.Equal(0,neutral);Check.True(friend>=30 && friend<=45 && enemy<=-30 && enemy>=-45);
                Check.Equal(100,NpcSocialPricing.Apply(100,true,neutral));
                Check.True(NpcSocialPricing.Apply(100,true,friend)<100 && NpcSocialPricing.Apply(100,false,friend)>100);
                Check.Equal(0,NpcSocialPricing.Apply(0,true,friend));
                Check.Equal(1,NpcSocialPricing.Apply(1,true,friend));
                Check.True(NpcSocialPricing.Favour(90,80,30,20,0,0)>NpcSocialPricing.Favour(90,80,0,0,0,0));
            }));
            cases.Add(("Event valence reflects affected person, affinity and severity rather than objective harm alone",()=>
            {
                Check.Equal(-5,NpcEventValence.PersonalOutcome(-5,true,-100));
                Check.Equal(-5,NpcEventValence.PersonalOutcome(-5,false,80));
                Check.Equal(5,NpcEventValence.PersonalOutcome(-5,false,-80));
                Check.Equal(3,NpcEventValence.PersonalOutcome(3,false,80));
                Check.Equal(-3,NpcEventValence.PersonalOutcome(3,false,-80));
                Check.Equal(0,NpcEventValence.PersonalOutcome(3,false,null));
                Check.Equal(5,NpcEventValence.Trade(20,5));
                Check.Equal(0,NpcEventValence.Trade(5,5));
                Check.Equal(-1,NpcEventValence.Trade(5,10));
            }));
            cases.Add(("Trade recollection records both directions, assessment and expires at next game day",()=>
            {
                var store=new WorldStateStore(12);
                string observation=PersonalTradeRecollection.Completed(new[]{"1 parchment"},new[]{"2 books"},-10,-15);
                Check.True(observation.Contains("Player bought: 1 parchment") && observation.Contains("Player sold: 2 books"));
                Check.True(observation.Contains("I paid the player 10 g") && observation.Contains("better for me"));
                store.RememberSceneEvent("alice","trade:1",NpcSceneEventKind.Trade,86390,1,-1,observation);
                Check.True(store.RecallSceneEvents("alice",86399,1200).Contains("parchment"));
                Check.Equal("",store.RecallSceneEvents("alice",86400,1200));
                Check.True(PersonalTradeRecollection.Completed(new[]{"1 parchment"},new string[0],0,0).Contains("fair"));
                Check.True(PersonalTradeRecollection.Completed(new[]{"1 sword"},new string[0],5,10).Contains("worse for me"));
                Check.True(PersonalTradeRecollection.Cancelled.Contains("No items or gold changed hands"));
            }));
            cases.Add(("Ambient company expires at its stable recall boundary while witnessed murder survives five weeks and reload",()=>
            {
                var store=new WorldStateStore(12);
                store.RememberSceneEvent("alice","company:1",NpcSceneEventKind.Company,100,1,2,"Five people were sitting nearby in this Inn.");
                store.RememberSceneEvent("alice","murder:1",NpcSceneEventKind.Murder,100,1,2,"I saw a person killed; the attacker was unidentified.");
                double expiry=store.Snapshot().SceneMemories[0].ExpiresAt;
                Check.True(store.RecallSceneEvents("alice",expiry-1,1200).Contains("Five people"));
                Check.True(!store.RecallSceneEvents("alice",expiry,1200).Contains("Five people"));
                var restored=new WorldStateStore(13);restored.Restore(store.Snapshot());
                string memory=restored.RecallSceneEvents("alice",35*86400,1200);
                Check.True(memory.Contains("killed") && memory.Contains("unidentified") && !memory.Contains("Five people"));
                Check.Equal("",restored.RecallSceneEvents("bob",200,1200));
                Check.Equal("",restored.RecallSceneEvents("alice",99,1200));
            }));
            cases.Add(("Repeated event observations neither duplicate nor renew a memory",()=>
            {
                var store=new WorldStateStore(12);
                Check.True(store.RememberSceneEvent("alice","company:1",NpcSceneEventKind.Company,100,1,2,"People nearby."));
                Check.True(!store.RememberSceneEvent("alice","company:1",NpcSceneEventKind.Company,200,1,2,"People nearby."));
                Check.Equal("",store.RecallSceneEvents("alice",store.Snapshot().SceneMemories[0].ExpiresAt,1200));
            }));
            cases.Add(("Memory duration has reproducible long-biased variation, short forgetfulness and rare long retention",()=>
            {
                double minimum=double.MaxValue,maximum=0,total=0;var values=new HashSet<double>();
                for(int i=0;i<1000;i++)
                {
                    var row=new NpcSceneMemory {ObserverId="alice",EventId="company:"+i,Kind=NpcSceneEventKind.Company,At=100};
                    double duration=row.ExpiresAt-row.At;
                    minimum=Math.Min(minimum,duration);maximum=Math.Max(maximum,duration);total+=duration;values.Add(duration);
                    Check.Equal(row.ExpiresAt,row.ExpiresAt);
                }
                Check.True(minimum>=1800 && minimum<2700);
                Check.True(maximum>12*3600 && maximum<=86400);
                Check.True(total/1000>2.25*3600 && values.Count>900);
            }));
            cases.Add(("Casual event floods cannot evict a witnessed murder and pursuit does not imply guilt",()=>
            {
                var store=new WorldStateStore(12);
                store.RememberSceneEvent("alice","murder:1",NpcSceneEventKind.Murder,100,1,2,"Witnessed killing.");
                for(int i=0;i<60;i++)store.RememberSceneEvent("alice","company:"+i,NpcSceneEventKind.Company,101+i,1,2,"People nearby.");
                store.RememberSceneEvent("alice","pursuit:1",NpcSceneEventKind.GuardPursuit,200,1,-1,"I saw a guard pursuing the player. I do not know the reason.");
                string memory=store.RecallSceneEvents("alice",201,1200);
                Check.True(memory.Contains("Witnessed killing") && memory.Contains("do not know the reason"));
                Check.True(store.Snapshot().SceneMemories.Count<=32);
            }));
        }
    }
}
