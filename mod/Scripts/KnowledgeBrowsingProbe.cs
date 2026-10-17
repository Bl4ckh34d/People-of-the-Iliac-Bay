using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    internal static class KnowledgeBrowsingProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        public static IEnumerator ApproachSpeaker(LivingCitiesMod adapter, StreetPopulation streets)
        {
            var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var snapshot=(WorldState)adapter.GetSaveData();
            var population=snapshot.Settlements.Find(t=>t.SettlementId==directory.MapID);
            var adults=population.Households.SelectMany(h=>h.Residents).Where(r=>r.IsAlive && r.AgeGroup==ResidentAgeGroup.Adult && !r.IsQuestProtected).ToList();
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach(var resident in adults)
            {
                var state=streets.GetState(resident.Id,now);
                if(state.Activity!=Activity.Travel) continue;
                var geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation(),false);
                game.PlayerMotor.transform.position=geometry.ScenePoint(state.Position)+Vector3.up+Vector3.right*2;
                Debug.Log("LIVING_CITIES_KNOWLEDGE_FIXTURE: approached simulated traveler "+resident.Id);
                yield return new WaitForSecondsRealtime(2);
                break;
            }
            if(UnityEngine.Object.FindObjectsOfType<ResidentActor>().Count(p=>p.Person && p.Resident.IsAlive)>=2) yield break;
            // Dialogue-only fallback for an empty streamed view. These bodies are
            // owned by the disposable test process and are never written to saves.
            foreach(var resident in adults.Where(r=>!UnityEngine.Object.FindObjectsOfType<ResidentActor>().Any(p=>p.Resident.Id==r.Id)).Take(2))
            {
                var body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject);
                var actor=body.AddComponent<ResidentActor>();
                actor.Initialize(resident,1,game.StreamingWorld.GetCurrentCityNavigation());
                actor.Place(game.PlayerMotor.transform.position+game.PlayerMotor.transform.forward*3+Vector3.right*2,Vector3.forward);
                body.SetActive(true); // A blocked test placement must not hide the dialogue fixture.
                Debug.Log("LIVING_CITIES_KNOWLEDGE_FIXTURE: temporary dialogue body "+resident.Id);
            }
            yield return new WaitForSecondsRealtime(0.5f);
        }
        private static IEnumerable<TalkManager.ListItem> Leaves(IEnumerable<TalkManager.ListItem> items)
        {
            foreach(var item in items)
                if(item.type == TalkManager.ListItemType.ItemGroup) {
                    foreach(var child in Leaves(item.listChildItems ?? new List<TalkManager.ListItem>())) yield return child;
                }
                else if(item.questionType == TalkManager.QuestionType.LocalBuilding && item.questID == 0) yield return item;
        }
        private static HashSet<int> Discovered(List<TalkManager.ListItem> places)
        { return new HashSet<int>(places.Where(t => GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(t.buildingKey)).Select(t => t.buildingKey)); }
        private static void Reaction(TalkManager manager, int value)
        {
            typeof(TalkManager).GetField("reactionToPlayer_0_1_2",Private).SetValue(manager,value);
            typeof(TalkManager).GetField("lastToneIndex",Private).SetValue(manager,1);
        }
        public static IEnumerator Run(LivingCitiesMod adapter,DaggerfallTalkWindow window,MethodInfo submit,ListBox transcript)
        {
            var manager=GameManager.Instance.TalkManager;
            var places=Leaves(manager.ListTopicLocation).Distinct().ToList();
            var groups=manager.ListTopicLocation.Where(t=>t.type==TalkManager.ListItemType.ItemGroup).ToList();
            var inns=groups.First(g=>g.caption.IndexOf("Tavern",StringComparison.OrdinalIgnoreCase)>=0);
            var innPlaces=Leaves(inns.listChildItems).Where(t=>!GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(t.buildingKey)).Take(3).ToList();
            var temples=groups.First(g=>g.caption.IndexOf("temple",StringComparison.OrdinalIgnoreCase)>=0);
            var temple=Leaves(temples.listChildItems).First(t=>!GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(t.buildingKey));
            var shopGroup=groups.First(g=>g.caption.IndexOf("store",StringComparison.OrdinalIgnoreCase)>=0);
            var shop=Leaves(shopGroup.listChildItems).First();
            if(innPlaces.Count<3) throw new InvalidOperationException("Knowledge fixture needs three undiscovered inns.");
            var original=places.ToDictionary(t=>t,t=>t.npcKnowledgeAboutItem);
            var suggestions=typeof(LivingCitiesMod).GetField("conversationSuggestions",Private);
            var mapEnabled=typeof(LivingCitiesMod).GetField("conversationMapRequestsEnabled",Private);
            bool originalMap=(bool)mapEnabled.GetValue(adapter);
            try
            {
                var before=Discovered(places); uint seed=DFRandom.Seed;
                var actual=(List<TalkManager.ListItem>)typeof(LivingCitiesMod).GetMethod("KnownBrowsePlaces",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{innPlaces});
                var nativeKnowledge=typeof(TalkManager).GetMethod("GetNPCKnowledgeAboutItem",Private);
                foreach(var item in innPlaces)
                {
                    DFRandom.SaveSeed(); TalkManager.NPCKnowledgeAboutItem expected;
                    try {expected=(TalkManager.NPCKnowledgeAboutItem)nativeKnowledge.Invoke(manager,new object[]{item});}
                    finally {DFRandom.RestoreSeed();}
                    if(actual.Contains(item)!=(expected==TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem || manager.NPCsKnowEverything()))
                        throw new InvalidOperationException("Browse knowledge differs from vanilla for "+item.caption);
                }
                if(seed!=DFRandom.Seed || !before.SetEquals(Discovered(places))) throw new InvalidOperationException("Knowledge lookup changed RNG or discovered a place.");
                foreach(var item in places) item.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.DoesNotKnowAboutItem;
                innPlaces[0].npcKnowledgeAboutItem=innPlaces[1].npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;
                Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Is there any inn nearby?",answer=>{
                    var offered=(List<TalkManager.ListItem>)suggestions.GetValue(adapter);
                    if(offered.Count==0 || offered.Any(t=>!innPlaces.Take(2).Contains(t)) || answer.Contains(innPlaces[2].caption))
                        throw new InvalidOperationException("Inn suggestions omitted known names or disclosed an unknown inn: "+answer);
                    if(!before.SetEquals(Discovered(places))) throw new InvalidOperationException("Listing inns marked the map.");
                });
                var mentioned=new List<TalkManager.ListItem>((List<TalkManager.ListItem>)suggestions.GetValue(adapter));
                Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Mark one on my map",answer=>{
                    var added=Discovered(places);added.ExceptWith(before);
                    if(added.Count!=1 || !mentioned.Any(t=>added.Contains(t.buildingKey) && answer.Contains(t.caption)))
                        throw new InvalidOperationException("Unnamed map choice did not mark exactly one disclosed inn: "+answer);
                });
                before=Discovered(places);Reaction(manager,2);mapEnabled.SetValue(adapter,false);
                yield return Ask(adapter,window,submit,transcript,"Mark an inn on my map",answer=>{
                    if(!before.SetEquals(Discovered(places))) throw new InvalidOperationException("Disabled browse map request revealed a building.");
                });
                mapEnabled.SetValue(adapter,true);
                temple.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Mark a temple on my map",answer=>{
                    var offered=(List<TalkManager.ListItem>)suggestions.GetValue(adapter);
                    if(offered.Count!=1 || offered[0]!=temple || !answer.Contains(temple.caption))
                        throw new InvalidOperationException("Direct temple map request retained old inn suggestions: "+answer);
                });
                Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Know any temples nearby?",answer=>{
                    var offered=(List<TalkManager.ListItem>)suggestions.GetValue(adapter);
                    if(offered.Count!=1 || offered[0]!=temple || !answer.Contains(temple.caption))
                        throw new InvalidOperationException("Temple category did not replace inn suggestions: "+answer);
                });
                Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Mark one of those on my map",answer=>{
                    var added=Discovered(places);added.ExceptWith(before);
                    if(added.Count!=1 || !added.Contains(temple.buildingKey) || !answer.Contains(temple.caption))
                        throw new InvalidOperationException("Map follow-up used stale inn choices instead of the temple: "+answer);
                });
                shop.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem;Reaction(manager,2);
                yield return Ask(adapter,window,submit,transcript,"Are there any shops nearby?",answer=>{
                    if(!answer.Contains(shop.caption)) throw new InvalidOperationException("Broad shop search missed the known shop: "+answer);
                });
                before=Discovered(places);Reaction(manager,0);
                yield return Ask(adapter,window,submit,transcript,"Mark an inn on my map",answer=>{
                    if(!before.SetEquals(Discovered(places)) || ((List<TalkManager.ListItem>)suggestions.GetValue(adapter)).Count!=0)
                        throw new InvalidOperationException("Unwilling NPC retained suggestions or marked a destination.");
                });
                Reaction(manager,2);
                foreach(var item in innPlaces) item.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.DoesNotKnowAboutItem;
                yield return Ask(adapter,window,submit,transcript,"Is there any inn nearby?",answer=>{
                    if(((List<TalkManager.ListItem>)suggestions.GetValue(adapter)).Count!=0 || innPlaces.Any(t=>answer.Contains(t.caption)))
                        throw new InvalidOperationException("Unknown category leaked a remembered destination: "+answer);
                });
                var other=UnityEngine.Object.FindObjectsOfType<ResidentActor>().First(p=>p.Person!=manager.MobileNPC && p.Resident.IsAlive);
                ((List<TalkManager.ListItem>)suggestions.GetValue(adapter)).Add(innPlaces[0]);
                DaggerfallUI.Instance.PopToHUD();
                yield return null;
                manager.TalkToMobileNPC(other.Person);
                yield return new WaitForSecondsRealtime(0.5f);
                if(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) || ((List<TalkManager.ListItem>)suggestions.GetValue(adapter)).Count!=0)
                    throw new InvalidOperationException("Suggestions crossed into a different NPC conversation.");
                Debug.Log("LIVING_CITIES_KNOWLEDGE_BROWSE_PASS: native knowledge parity and RNG; known inn/shop/temple prose; no browse markers; unnamed single map choice; stale category replacement; map toggle; refusal; unknown places; speaker reset.");
            }
            finally {foreach(var entry in original) entry.Key.npcKnowledgeAboutItem=entry.Value;mapEnabled.SetValue(adapter,originalMap);}
        }
        private static IEnumerator Ask(LivingCitiesMod adapter,DaggerfallTalkWindow window,MethodInfo submit,ListBox transcript,string text,Action<string> check)
        {
            int before=transcript.Count;submit.Invoke(adapter,new object[]{window,text});
            float deadline=Time.realtimeSinceStartup+45;
            while(transcript.Count==before && Time.realtimeSinceStartup<deadline) yield return null;
            if(transcript.Count!=before+2) throw new InvalidOperationException("Browse dialogue timed out: "+text);
            string answer=transcript.GetItem(before+1).textLabel.Text;
            Debug.Log("LIVING_CITIES_KNOWLEDGE_REPLY: "+text+" => "+answer);check(answer);
        }
    }
}
