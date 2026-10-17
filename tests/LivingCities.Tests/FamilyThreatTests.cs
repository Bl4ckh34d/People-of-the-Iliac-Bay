using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class FamilyThreatTests
    {
        public static void Add(List<(string Name, System.Action Body)> cases)
        {
            cases.Add(("Only direct threats toward the NPC family become family_threat", () => {
                var topics = new List<ConversationTopic>();
                const string direct = "I'll kill your son if you refuse.";
                Check.True(ConversationKnowledge.IsDirectFamilyThreat(direct));
                Check.True(ConversationKnowledge.IsDirectFamilyThreat("I will kill all of your family."));
                var parsed = ConversationKnowledge.Parse("social|-1|family_threat", topics, direct, -1);
                Check.True(parsed != null && parsed.Tone == "family_threat");
                Check.Equal("family_threat", ConversationKnowledge.ExactFallback(direct, topics, -1).Tone);
                Check.True(ConversationKnowledge.Prompt(direct, topics, -1).Contains("family_threat"));
            }));
            cases.Add(("Quoted, protective and negated language is not a family threat", () => {
                Check.True(!ConversationKnowledge.IsDirectFamilyThreat("He said, \"I'll kill your son.\""));
                Check.True(!ConversationKnowledge.IsDirectFamilyThreat("Please protect your son from the brigand."));
                Check.True(!ConversationKnowledge.IsDirectFamilyThreat("I will not hurt your daughter."));
                Check.True(!ConversationKnowledge.IsDirectFamilyThreat("I will kill the bandit threatening your family."));
                Check.True(!ConversationKnowledge.IsDirectFamilyThreat("Your family will die unless we rescue them."));
                Check.True(!ConversationKnowledge.IsDirectFamilyThreat("My brother said, 'I will hurt your child.'"));
                Check.True(ConversationKnowledge.Parse("social|-1|family_threat", new List<ConversationTopic>(),
                    "Please protect your son from the brigand.", -1) == null);
                Check.True(ConversationKnowledge.Parse("social|-1|family_threat", new List<ConversationTopic>(),
                    "I will kill the bandit threatening your family.", -1) == null);
            }));
            cases.Add(("Direct family threat forces actual disposition to negative one hundred", () => {
                var store = new WorldStateStore(1);
                int actual;
                store.SetConversationBaseline("native:100", 100, 100, 12);
                store.SetConversationThreatDisposition("native:100", 100, 101);
                Check.Equal(-200, store.ConversationDisposition("native:100"));
                Check.True(store.TryGetActualConversationDisposition("native:100", out actual));
                Check.Equal(-100, actual);

                store.SetConversationBaseline("native:-40", -40, 100, 12);
                store.SetConversationThreatDisposition("native:-40", -40, 101);
                Check.Equal(-60, store.ConversationDisposition("native:-40"));
                Check.True(store.TryGetActualConversationDisposition("native:-40", out actual));
                Check.Equal(-100, actual);
            }));
        }
    }
}
