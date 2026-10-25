using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Constrains the existing Qwen route refinement to a coarse D3 route.
    // It does not interpret player text or authorize native effects.
    internal static class LocalDecisionDialogueRoute
    {
        private static readonly string[] SocialIntents = { "topic", "map", "unknown", "trade", "borrow", "return_loan" };
        private static readonly string[] ActionIntents = { "action", "guide", "appointment", "home", "map", "unknown", "trade", "borrow", "return_loan" };
        private static readonly string[] CommitmentIntents = { "action", "guide", "appointment", "home", "unknown" };
        private static readonly string[] OfferIntents = { "action" };
        private static readonly string[] ToneValues = { "neutral", "polite", "apology", "rude", "insult", "flirt" };
        private static readonly string[] SocialIntentValues = { "none", "busy_pause", "family_threat" };

        public static string RefinePrompt(string route)
        {
            if (route == null) return string.Empty;

            string allowed;
            string instruction;
            switch (route)
            {
                case "SOCIAL":
                    allowed = "social, topic, map, trade, borrow, return_loan or unknown";
                    instruction = "The D3 first-stage route is SOCIAL. Refine only within " + allowed +
                        ". This route covers information questions and ordinary conversation. A map result here is retained for legacy/indirect-information phrasing; it still requires a separately bound native target. Use socialIntent busy_pause or family_threat only for the current player message.";
                    break;
                case "ACTION_REQUEST":
                    allowed = "social, action, guide, appointment, home, map, trade, borrow, return_loan or unknown";
                    instruction = "The D3 first-stage route is ACTION_REQUEST. Refine only within " + allowed +
                        ". A map result means the player asks this NPC to place a marker; it still requires a separately bound native target. Keep unsupported or unclear current requests as unknown for clarification. Use socialIntent busy_pause or family_threat only when the current player message requires that override.";
                    break;
                case "OFFER_RESPONSE":
                    instruction = "The D3 first-stage route is OFFER_RESPONSE. Use action only when the current player message accepts, refuses or withdraws the verified pending offer. Its presence alone does not establish a response to it. For unrelated social speech, return social with socialIntent none. Do not accept, reject or execute the offer in this routing pass; the existing offer interpreter handles a confirmed offer response. A current busy_pause or family_threat override remains social.";
                    break;
                case "COMMITMENT_UPDATE":
                    allowed = "social, action, guide, appointment, home or unknown";
                    instruction = "The D3 first-stage route is COMMITMENT_UPDATE. Refine only within " + allowed +
                        ". Use action for a current request to change the NPC's active commitment. Use unknown when the requested change is unclear. A current busy_pause or family_threat override remains social.";
                    break;
                default:
                    throw new ArgumentException("Unknown D3 dialogue route.", nameof(route));
            }

            return "\nD3 route refinement constraint: " + instruction +
                " The D3 route is provisional. If the current player message is unrelated to it, refine to social and preserve that message's tone. Preserve the existing intent|topicID|tone|socialIntent format. This route does not grant action authority.";
        }

        public static string Grammar(string route)
        {
            if (route == null) return ConversationKnowledge.RoutingGrammar();

            string[] intents;
            switch (route)
            {
                case "SOCIAL": intents = SocialIntents; break;
                case "ACTION_REQUEST": intents = ActionIntents; break;
                case "COMMITMENT_UPDATE": intents = CommitmentIntents; break;
                case "OFFER_RESPONSE": intents = OfferIntents; break;
                default: throw new ArgumentException("Unknown D3 dialogue route.", nameof(route));
            }

            var branches = new List<string>
            {
                "\"social|-1|\" tone \"|\" social-intent"
            };
            if (intents.Length == 1)
                branches.Add("\"" + intents[0] + "|-1|\" tone \"|none\"");
            else
            {
                var alternatives = new List<string>(intents.Length);
                foreach (string intent in intents) alternatives.Add("\"" + intent + "\"");
                branches.Add("(" + string.Join(" | ", alternatives) + ") \"|-1|\" tone \"|none\"");
            }

            string grammar = "root ::= " + string.Join(" | ", branches) + "\n" +
                "tone ::= " + QuotedAlternatives(ToneValues) + "\n" +
                "social-intent ::= " + QuotedAlternatives(SocialIntentValues) + "\n";
            return grammar;
        }

        public static bool Allows(string route, ConversationSelection selection)
        {
            if (selection == null || selection.TopicId != -1 || !Contains(ToneValues, selection.Tone) ||
                !Contains(SocialIntentValues, selection.SocialIntent)) return false;

            bool specialSocialOverride = selection.Intent == "social" &&
                (selection.SocialIntent == "busy_pause" || selection.SocialIntent == "family_threat");
            if (specialSocialOverride) return route == null || IsKnownRoute(route);
            if (selection.SocialIntent != "none") return false;

            if (route == null)
                return Contains(new[] { "social", "topic", "map", "unknown", "action", "guide", "appointment", "home", "trade", "borrow", "return_loan" }, selection.Intent);

            switch (route)
            {
                case "SOCIAL": return selection.Intent == "social" || Contains(SocialIntents, selection.Intent);
                case "ACTION_REQUEST": return selection.Intent == "social" || Contains(ActionIntents, selection.Intent);
                case "COMMITMENT_UPDATE": return selection.Intent == "social" || Contains(CommitmentIntents, selection.Intent);
                case "OFFER_RESPONSE": return selection.Intent == "social" || Contains(OfferIntents, selection.Intent);
                default: return false;
            }
        }

        public static bool IsOrdinarySocialDowngrade(string route, ConversationSelection selection)
        {
            return route != null && route != "SOCIAL" && IsKnownRoute(route) &&
                selection != null && selection.Intent == "social" && selection.SocialIntent == "none" &&
                Allows(route, selection);
        }

        private static bool IsKnownRoute(string route)
        {
            return route == "SOCIAL" || route == "ACTION_REQUEST" || route == "OFFER_RESPONSE" ||
                route == "COMMITMENT_UPDATE";
        }

        private static string QuotedAlternatives(IEnumerable<string> values)
        {
            var alternatives = new List<string>();
            foreach (string value in values) alternatives.Add("\"" + value + "\"");
            return string.Join(" | ", alternatives);
        }

        private static bool Contains(IEnumerable<string> values, string value)
        {
            if (value == null) return false;
            foreach (string candidate in values)
                if (string.Equals(candidate, value, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
