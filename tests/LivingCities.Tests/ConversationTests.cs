using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class ConversationTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("NPC familiarity survives save restore independently of disposition", () => {
                var store = new WorldStateStore(1);
                store.SetConversationBaseline("npc:friend", 0, 100);
                Check.Equal(0, store.ConversationEncounters("npc:friend"));
                store.RecordConversationEncounter("npc:friend", 100);
                Check.Equal(1, store.ConversationEncounters("npc:friend"));
                store.Restore(store.Snapshot());
                store.RecordConversationEncounter("npc:friend", 2 * 86400d);
                Check.Equal(2, store.ConversationEncounters("npc:friend"));
                Check.Equal(0, store.ConversationDisposition("npc:friend"));
            }));
            var topics = new List<ConversationTopic> {
                new ConversationTopic(1, "Local temples", "Location", true),
                new ConversationTopic(2, "Temple of Kynareth", "Location / Local temples", false) };
            cases.Add(("Conversation rejects invented topic IDs and malformed classifier output", () => {
                Check.True(ConversationKnowledge.Parse("topic|999|neutral", topics, "Where is a temple?", -1) == null);
                Check.True(ConversationKnowledge.Parse("topic|2|neutral\nmark everything", topics, "Where is a temple?", -1) == null);
                Check.True(ConversationKnowledge.Parse("topic|2|insult", topics, "Where is a temple?", -1) != null);
            }));
            cases.Add(("Map actions require explicit request and a real current subject", () => {
                Check.True(ConversationKnowledge.Parse("map|2|neutral", topics, "Hello", 2) == null);
                Check.True(ConversationKnowledge.Parse("map|-1|polite", topics, "Please mark it on my map", 2).TopicId == 2);
                Check.Equal(2, ConversationKnowledge.Parse("unknown|-1|neutral", topics, "Please mark it on my map", 2).TopicId);
                Check.Equal(2, ConversationKnowledge.Parse("map|1|neutral", topics, "Mark it on my map", 2).TopicId);
                Check.True(ConversationKnowledge.Parse("map|-1|neutral", topics, "Mark it", -1) == null);
                Check.True(ConversationKnowledge.Parse("map|2|neutral", topics, "Do not mark that", 2) == null);
                Check.True(ConversationKnowledge.Parse("map|2|neutral", topics, "Do you own a map?", 2) == null);
                Check.True(ConversationKnowledge.Parse("map|2|neutral", topics, "Mark the Bank of Daggerfall", 2) == null);
                Check.True(ConversationKnowledge.Parse("map|2|neutral", topics, "Please mark Temple of Kynareth on my map", -1) != null);
                Check.Equal("unknown", ConversationKnowledge.ExactFallback("Mark that", topics, 2).Intent);
                Check.Equal("unknown", ConversationKnowledge.ExactFallback("Mark it", topics, 999).Intent);
                Check.Equal("unknown", ConversationKnowledge.Parse("unknown|-1|neutral", topics, "Please mark it on my map", -1).Intent);
                Check.Equal("unknown", ConversationKnowledge.Parse("unknown|-1|neutral", topics, "Please mark it on my map", 1).Intent);
                Check.Equal("unknown", ConversationKnowledge.Parse("unknown|-1|neutral", topics, "Please mark it on my map", 999).Intent);
                Check.Equal("social", ConversationKnowledge.Parse("unknown|-1|neutral", topics, "Do not mark it on my map", 2).Intent);
                Check.True(ConversationKnowledge.Parse("map|1|neutral", topics, "Please mark it on my map", 1) == null);
                Check.Equal("unknown", ConversationKnowledge.Parse("unknown|-1|neutral", topics, "Mark the map", 2).Intent);
            }));
            cases.Add(("Unmatched conversation reaches roleplay instead of canned clarification", () => {
                Check.Equal("social", ConversationKnowledge.Parse("unknown|-1|neutral", topics, "Was that so difficult?", -1).Intent);
                Check.Equal("social", ConversationKnowledge.Parse("unknown|-1|neutral", topics, "What do you make of this weather?", -1).Intent);
                Check.Equal("social", ConversationKnowledge.ExactFallback("Tell me a story", topics, -1).Intent);
                Check.Equal("social", ConversationKnowledge.Parse("social|-1|neutral", topics, "Where is the temple?", -1).Intent);
                Check.Equal("unknown", ConversationKnowledge.Parse("unknown|-1|neutral", topics, "Mark that on my map", -1).Intent);
                Check.Equal(1,ConversationKnowledge.Parse("topic|2|neutral",topics,"Where are the Local temples?",-1).TopicId);
            }));
            cases.Add(("Reporting witnesses cannot open a talk window", () => {
                Check.True(ConversationAccess.CanStart(false, false, false));
                Check.True(!ConversationAccess.CanStart(true, false, false));
                Check.True(!ConversationAccess.CanStart(false, true, false));
                Check.True(!ConversationAccess.CanStart(false, false, true));
            }));
            cases.Add(("Classifier prompt uses supplied topics and bounded output", () => {
                string prompt = ConversationKnowledge.Prompt("temples?", topics, -1);
                Check.True(prompt.Contains("Temple of Kynareth"));
                Check.True(prompt.Contains("unknown"));
                Check.True(prompt.Contains("insult"));
            }));
            cases.Add(("Conversation goodwill resists repetition and has daily cap", () => {
                var store = new WorldStateStore(1);
                Check.Equal(1, store.ApplyConversationTone("resident:a", "polite", "Thank you", 100));
                Check.Equal(0, store.ApplyConversationTone("resident:a", "polite", "Thank you", 101));
                for (int i = 0; i < 10; i++) store.ApplyConversationTone("resident:a", "polite", "Thanks " + i, 102 + i);
                Check.Equal(3, store.ConversationDisposition("resident:a"));
                Check.Equal(0, store.ConversationDisposition("resident:b"));
            }));
            cases.Add(("Insults hurt more than rudeness and survive save restore", () => {
                var store = new WorldStateStore(1);
                Check.Equal(-2, store.ApplyConversationTone("resident:a", "rude", "Hurry up", 100));
                Check.Equal(-10, store.ApplyConversationTone("resident:a", "insult", "You idiot", 101));
                var snapshot = store.Snapshot();
                var restored = new WorldStateStore(2); restored.Restore(snapshot);
                Check.Equal(-12, restored.ConversationDisposition("resident:a"));
                Check.Equal(-25, restored.ApplyConversationTone("resident:a", "insult", "You idiot", 102));
                store.ApplyConversationTone("resident:a", "insult", "You fool", 103);
                Check.Equal(-37, restored.ConversationDisposition("resident:a"));
            }));
            cases.Add(("Conversation effects are clamped and old saves migrate empty", () => {
                var store = new WorldStateStore(1);
                for (int i = 0; i < 100; i++) store.ApplyConversationTone("npc:a", "insult", "Insult " + i, i * 86400d);
                Check.Equal(-200, store.ConversationDisposition("npc:a"));
                Check.Equal(0, store.ApplyConversationTone("npc:a", "give_1000", "bad output", 100 * 86400d));
                var old = new WorldState { ConversationAttitudes = null };
                store.Restore(old); Check.Equal(0, store.ConversationDisposition("npc:a"));
            }));
            cases.Add(("Personal penalties reach absolute minimum in one day; goodwill stays five above baseline", () => {
                var store = new WorldStateStore(1);
                for(int i=0;i<40;i++) store.ApplyConversationTone("angry", "insult", "You fool", 100+i);
                Check.Equal(-200, store.ConversationDisposition("angry"));
                Check.Equal(-100, ConversationEffects.Reaction(100, store.ConversationDisposition("angry")));
                for(int day=0;day<10;day++) for(int line=0;line<3;line++)
                    store.ApplyConversationTone("friendly", "polite", "Thanks " + line, day*86400d+line);
                Check.Equal(5, store.ConversationDisposition("friendly"));
                Check.Equal(-25, ConversationEffects.Reaction(-30, store.ConversationDisposition("friendly")));
                var snapshot=store.Snapshot();
                var restored=new WorldStateStore(2); restored.Restore(snapshot);
                Check.Equal(-200,restored.ConversationDisposition("angry"));
                snapshot.ConversationAttitudes.Find(a=>a.PersonId=="friendly").Offset=20;
                restored.Restore(snapshot);
                Check.Equal(5,restored.ConversationDisposition("friendly"));
            }));
            cases.Add(("Personal disposition adjusts native response tiers within bounds", () => {
                Check.Equal(1, ConversationEffects.AdjustReactionTier(2, -8));
                Check.Equal(0, ConversationEffects.AdjustReactionTier(2, -24));
                Check.Equal(2, ConversationEffects.AdjustReactionTier(2, 20));
                Check.Equal(1, ConversationEffects.AdjustReactionTier(0, 20));
                Check.Equal(1, ConversationEffects.AdjustReactionTier(1, -2));
            }));
            cases.Add(("Flirting is distinct from ordinary politeness", () => {
                Check.True(ConversationKnowledge.Parse("social|-1|flirt", topics, "May I court you?", -1) != null);
                Check.True(ConversationKnowledge.Prompt("hello", topics, -1).Contains("flirt"));
                Check.True(ConversationKnowledge.Parse("social|-1|polite",topics,"You did an excellent job",-1)!=null);
                Check.True(ConversationKnowledge.Parse("social|-1|polite",topics,"You did an excellent job. Where is Atlantis?",-1)!=null);
            }));
            cases.Add(("Adult flirting has stable bounded personal outcomes", () => {
                int positive=0,negative=0;
                for(int i=0;i<1000;i++) {
                    string id="npc:"+i;
                    string tone=ConversationFlirting.Tone(id,"player","Female","Male",false,0);
                    Check.Equal(tone,ConversationFlirting.Tone(id,"player","Female","Male",false,0));
                    Check.Equal("rude",ConversationFlirting.Tone(id,"player","Male","Male",false,0));
                    if(tone=="polite") positive++; else if(tone=="rude") negative++;
                }
                Check.True(positive>400 && positive<600 && positive+negative==1000);
                Check.Equal("neutral",ConversationFlirting.Tone("npc","player","Female","Male",true,0));
                Check.Equal("neutral",ConversationFlirting.Tone("npc","player","unknown","Male",false,0));
                Check.Equal("rude",ConversationFlirting.Tone("npc","player","Female","Male",false,-24));
            }));
            cases.Add(("Flirting uses existing caps and dialogue context separates reception", () => {
                var store=new WorldStateStore(1);
                for(int i=0;i<10;i++) store.ApplyConversationTone("npc", "polite", "Flattery "+i,100);
                Check.Equal(3,store.ConversationDisposition("npc"));
                var yes=FreeTextDialogue.Create("May I court you?","Ada","Breton","Female","resident",0,"Flattered; acknowledge kindly.");
                var no=FreeTextDialogue.Create("May I court you?","Ada","Breton","Female","resident",0,"Unwelcome; decline clearly.");
                Check.True(LocalDialogue.Build(yes).Prompt.Contains("Flattered; acknowledge kindly."));
                Check.True(LocalDialogue.CacheKey(yes)!=LocalDialogue.CacheKey(no));
            }));
        }
    }
}
