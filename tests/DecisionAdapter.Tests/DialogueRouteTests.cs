using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class DialogueRouteTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("dialogue route grammar limits social refinement", SocialGrammarIsConstrained));
            cases.Add(("dialogue route grammar limits action refinement", ActionGrammarIsConstrained));
            cases.Add(("dialogue route grammar limits offer refinement", OfferGrammarIsConstrained));
            cases.Add(("dialogue route grammar limits commitment refinement", CommitmentGrammarIsConstrained));
            cases.Add(("dialogue route allows busy and threat overrides", SocialOverridesAreAllowedForEveryRoute));
            cases.Add(("dialogue route grammar agrees with selection policy", GrammarAndSelectionPolicyAgree));
            cases.Add(("dialogue route rejects malformed selections", MalformedSelectionsAreRejected));
            cases.Add(("dialogue route preserves legacy fallback", NullRoutePreservesLegacyBehavior));
            cases.Add(("dialogue route refinement preserves submitted text", RefinementPreservesOriginalText));
            cases.Add(("dialogue route identifies only ordinary social downgrades", OrdinarySocialDowngradeIsNarrowlyScoped));
        }

        private static void SocialGrammarIsConstrained()
        {
            string grammar = LocalDecisionDialogueRoute.Grammar("SOCIAL");
            Check.Equal(
                "root ::= \"social|-1|\" tone \"|\" social-intent | (\"topic\" | \"map\" | \"unknown\" | \"trade\" | \"borrow\" | \"return_loan\") \"|-1|\" tone \"|none\"\n" +
                "tone ::= \"neutral\" | \"polite\" | \"apology\" | \"rude\" | \"insult\" | \"flirt\"\n" +
                "social-intent ::= \"none\" | \"busy_pause\" | \"family_threat\"\n", grammar);
            Check.True(Allows("SOCIAL", "social|-1|neutral|none"));
            Check.True(Allows("SOCIAL", "topic|-1|polite|none"));
            Check.True(Allows("SOCIAL", "map|-1|neutral|none"));
            Check.True(Allows("SOCIAL", "unknown|-1|neutral|none"));
            Check.True(Allows("SOCIAL", "trade|-1|neutral|none"));
            Check.True(!Allows("SOCIAL", "action|-1|neutral|none"));
            Check.True(!Allows("SOCIAL", "appointment|-1|neutral|none"));
            Check.True(!Allows("SOCIAL", "home|-1|neutral|none"));
        }

        private static void ActionGrammarIsConstrained()
        {
            string grammar = LocalDecisionDialogueRoute.Grammar("ACTION_REQUEST");
            Check.Equal(
                "root ::= \"social|-1|\" tone \"|\" social-intent | (\"action\" | \"guide\" | \"appointment\" | \"home\" | \"map\" | \"unknown\" | \"trade\" | \"borrow\" | \"return_loan\") \"|-1|\" tone \"|none\"\n" +
                "tone ::= \"neutral\" | \"polite\" | \"apology\" | \"rude\" | \"insult\" | \"flirt\"\n" +
                "social-intent ::= \"none\" | \"busy_pause\" | \"family_threat\"\n", grammar);
            Check.True(Allows("ACTION_REQUEST", "action|-1|polite|none"));
            Check.True(Allows("ACTION_REQUEST", "appointment|-1|neutral|none"));
            Check.True(Allows("ACTION_REQUEST", "home|-1|neutral|none"));
            Check.True(Allows("ACTION_REQUEST", "map|-1|neutral|none"));
            Check.True(Allows("ACTION_REQUEST", "unknown|-1|neutral|none"));
            Check.True(Allows("ACTION_REQUEST", "borrow|-1|neutral|none"));
            Check.True(Allows("ACTION_REQUEST", "social|-1|neutral|none"));
            Check.True(!Allows("ACTION_REQUEST", "topic|-1|neutral|none"));
        }

        private static void OfferGrammarIsConstrained()
        {
            string grammar = LocalDecisionDialogueRoute.Grammar("OFFER_RESPONSE");
            Check.Equal(
                "root ::= \"social|-1|\" tone \"|\" social-intent | \"action|-1|\" tone \"|none\"\n" +
                "tone ::= \"neutral\" | \"polite\" | \"apology\" | \"rude\" | \"insult\" | \"flirt\"\n" +
                "social-intent ::= \"none\" | \"busy_pause\" | \"family_threat\"\n", grammar);
            Check.True(Allows("OFFER_RESPONSE", "action|-1|polite|none"));
            Check.True(Allows("OFFER_RESPONSE", "social|-1|neutral|none"));
            Check.True(!Allows("OFFER_RESPONSE", "unknown|-1|neutral|none"));
            Check.True(!Allows("OFFER_RESPONSE", "appointment|-1|neutral|none"));
            Check.True(Allows("OFFER_RESPONSE", "social|-1|neutral|none"));
        }

        private static void CommitmentGrammarIsConstrained()
        {
            string grammar = LocalDecisionDialogueRoute.Grammar("COMMITMENT_UPDATE");
            Check.Equal(
                "root ::= \"social|-1|\" tone \"|\" social-intent | (\"action\" | \"guide\" | \"appointment\" | \"home\" | \"unknown\") \"|-1|\" tone \"|none\"\n" +
                "tone ::= \"neutral\" | \"polite\" | \"apology\" | \"rude\" | \"insult\" | \"flirt\"\n" +
                "social-intent ::= \"none\" | \"busy_pause\" | \"family_threat\"\n", grammar);
            Check.True(Allows("COMMITMENT_UPDATE", "action|-1|neutral|none"));
            Check.True(Allows("COMMITMENT_UPDATE", "unknown|-1|neutral|none"));
            Check.True(Allows("COMMITMENT_UPDATE", "social|-1|neutral|none"));
            Check.True(!Allows("COMMITMENT_UPDATE", "topic|-1|neutral|none"));
            Check.True(!Allows("COMMITMENT_UPDATE", "map|-1|neutral|none"));
        }

        private static void SocialOverridesAreAllowedForEveryRoute()
        {
            string[] routes = { "SOCIAL", "ACTION_REQUEST", "OFFER_RESPONSE", "COMMITMENT_UPDATE" };
            foreach (string route in routes)
            {
                Check.True(Allows(route, "social|-1|neutral|busy_pause"));
                Check.True(Allows(route, "social|-1|neutral|family_threat"));
                Check.True(!Allows(route, "action|-1|neutral|busy_pause"));
                Check.True(!Allows(route, "topic|-1|neutral|family_threat"));
            }
        }

        private static void MalformedSelectionsAreRejected()
        {
            Check.True(!LocalDecisionDialogueRoute.Allows("ACTION_REQUEST", null));
            Check.True(!LocalDecisionDialogueRoute.Allows("ACTION_REQUEST", new ConversationSelection("invented", -1, "neutral", "none")));
            Check.True(!LocalDecisionDialogueRoute.Allows("ACTION_REQUEST", new ConversationSelection("action", 4, "neutral", "none")));
            Check.True(!LocalDecisionDialogueRoute.Allows("ACTION_REQUEST", new ConversationSelection("action", -1, "warm", "none")));
            Check.True(!LocalDecisionDialogueRoute.Allows("ACTION_REQUEST", new ConversationSelection("action", -1, "neutral", "maybe")));
            Check.True(!LocalDecisionDialogueRoute.Allows("ACTION_REQUEST", new ConversationSelection("action", -1, "neutral", "family_threat")));
            Check.True(!LocalDecisionDialogueRoute.Allows("not-a-route", new ConversationSelection("action", -1, "neutral", "none")));
        }

        private static void GrammarAndSelectionPolicyAgree()
        {
            var cases = new[]
            {
                Tuple.Create("SOCIAL", "social|-1|neutral|none"),
                Tuple.Create("SOCIAL", "social|-1|neutral|busy_pause"),
                Tuple.Create("SOCIAL", "topic|-1|polite|none"),
                Tuple.Create("SOCIAL", "action|-1|neutral|none"),
                Tuple.Create("ACTION_REQUEST", "action|-1|polite|none"),
                Tuple.Create("ACTION_REQUEST", "social|-1|neutral|busy_pause"),
                Tuple.Create("ACTION_REQUEST", "social|-1|neutral|none"),
                Tuple.Create("ACTION_REQUEST", "map|-1|neutral|none"),
                Tuple.Create("SOCIAL", "map|-1|neutral|none"),
                Tuple.Create("COMMITMENT_UPDATE", "map|-1|neutral|none"),
                Tuple.Create("OFFER_RESPONSE", "action|-1|polite|none"),
                Tuple.Create("OFFER_RESPONSE", "social|-1|neutral|family_threat"),
                Tuple.Create("OFFER_RESPONSE", "unknown|-1|neutral|none"),
                Tuple.Create("COMMITMENT_UPDATE", "action|-1|neutral|none"),
                Tuple.Create("COMMITMENT_UPDATE", "social|-1|neutral|busy_pause"),
                Tuple.Create("COMMITMENT_UPDATE", "topic|-1|neutral|none")
            };

            foreach (var item in cases)
            {
                ConversationSelection selection = ConversationKnowledge.ParseRouting(item.Item2);
                Check.Equal(GrammarAccepts(item.Item1, selection), LocalDecisionDialogueRoute.Allows(item.Item1, selection));
            }
        }

        private static void NullRoutePreservesLegacyBehavior()
        {
            Check.Equal(ConversationKnowledge.RoutingGrammar(), LocalDecisionDialogueRoute.Grammar(null));
            Check.Equal(string.Empty, LocalDecisionDialogueRoute.RefinePrompt(null));
            Check.True(LocalDecisionDialogueRoute.Allows(null, new ConversationSelection("topic", -1, "neutral", "none")));
            Check.True(LocalDecisionDialogueRoute.Allows(null, new ConversationSelection("action", -1, "neutral", "none")));
            Check.True(!LocalDecisionDialogueRoute.Allows(null, new ConversationSelection("action", 2, "neutral", "none")));
        }

        private static void RefinementPreservesOriginalText()
        {
            const string playerText = "Could you buy café bread? Keep ‘this’ exact.";
            const string history = "NPC: Which bread?\nPlayer: The rye.";
            string original = ConversationKnowledge.RoutingPrompt(playerText, history);
            string refined = original + LocalDecisionDialogueRoute.RefinePrompt("ACTION_REQUEST");
            Check.True(refined.StartsWith(original, StringComparison.Ordinal));
            Check.True(refined.Contains(playerText, StringComparison.Ordinal));
            Check.True(refined.Contains("D3 first-stage route is ACTION_REQUEST", StringComparison.Ordinal));
            Check.True(LocalDecisionDialogueRoute.RefinePrompt("OFFER_RESPONSE").Contains("provisional", StringComparison.OrdinalIgnoreCase));
            Check.True(LocalDecisionDialogueRoute.RefinePrompt("OFFER_RESPONSE").Contains("unrelated social", StringComparison.OrdinalIgnoreCase));
            Check.True(LocalDecisionDialogueRoute.RefinePrompt("SOCIAL").Contains(
                "A map result here is retained for legacy/indirect-information phrasing", StringComparison.Ordinal));
            Check.True(LocalDecisionDialogueRoute.RefinePrompt("ACTION_REQUEST").Contains(
                "A map result means the player asks this NPC to place a marker", StringComparison.Ordinal));
            Check.True(!LocalDecisionDialogueRoute.RefinePrompt("COMMITMENT_UPDATE").Contains("map", StringComparison.Ordinal));
            Check.True(!LocalDecisionDialogueRoute.RefinePrompt("ACTION_REQUEST").Contains(playerText, StringComparison.Ordinal));
        }

        private static void OrdinarySocialDowngradeIsNarrowlyScoped()
        {
            string[] routes = { "ACTION_REQUEST", "OFFER_RESPONSE", "COMMITMENT_UPDATE" };
            foreach (string route in routes)
            {
                var social = new ConversationSelection("social", -1, "rude", "none");
                Check.True(LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade(route, social));
                Check.True(!LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade(route,
                    new ConversationSelection("social", -1, "neutral", "busy_pause")));
                Check.True(!LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade(route,
                    new ConversationSelection("action", -1, "neutral", "none")));
            }

            Check.True(!LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade("SOCIAL",
                new ConversationSelection("social", -1, "neutral", "none")));
            Check.True(!LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade("unknown-route",
                new ConversationSelection("social", -1, "neutral", "none")));
            Check.True(!LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade(null,
                new ConversationSelection("social", -1, "neutral", "none")));
            Check.True(!LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade("OFFER_RESPONSE",
                new ConversationSelection("social", 2, "neutral", "none")));
            Check.True(!LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade("OFFER_RESPONSE", null));
        }

        private static bool Allows(string route, string output)
        {
            ConversationSelection selection = ConversationKnowledge.ParseRouting(output);
            return LocalDecisionDialogueRoute.Allows(route, selection);
        }

        private static bool GrammarAccepts(string route, ConversationSelection selection)
        {
            if (selection == null) return false;
            string grammar = LocalDecisionDialogueRoute.Grammar(route);
            string root = grammar.Substring(0, grammar.IndexOf('\n'));
            if (selection.Intent == "social")
            {
                string rule = "social-intent";
                return root.Contains("|\" " + rule, StringComparison.Ordinal) &&
                    RuleDefinition(grammar, rule).Contains("\"" + selection.SocialIntent + "\"", StringComparison.Ordinal) &&
                    RuleDefinition(grammar, "tone").Contains("\"" + selection.Tone + "\"", StringComparison.Ordinal);
            }

            return selection.SocialIntent == "none" &&
                (root.Contains("\"" + selection.Intent + "|-1|\"", StringComparison.Ordinal) ||
                 root.Contains("\"" + selection.Intent + "\"", StringComparison.Ordinal)) &&
                RuleDefinition(grammar, "tone").Contains("\"" + selection.Tone + "\"", StringComparison.Ordinal);
        }

        private static string RuleDefinition(string grammar, string rule)
        {
            string prefix = rule + " ::= ";
            int start = grammar.IndexOf(prefix, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += prefix.Length;
            int end = grammar.IndexOf('\n', start);
            return end < 0 ? grammar.Substring(start) : grammar.Substring(start, end - start);
        }
    }
}
