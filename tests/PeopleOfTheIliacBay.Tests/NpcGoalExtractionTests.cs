using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcGoalExtractionTests
    {
        private const string Turn = "Wait here for 10 seconds, then follow me for 10 seconds.";

        private static ConversationContextBinding Binding()
        {
            return new ConversationContextBinding(ConversationActorCategory.Mobile, "person", null,
                "session", 4, 2, new string('a', 32), "", "scene", 3);
        }

        private static string Duration(string amount, string unit, string source)
        {
            return "{\"amount\":" + amount + ",\"unit\":\"" + unit + "\",\"source\":\"" + source + "\"}";
        }

        private static string Quantity(string amount, string source)
        {
            return "{\"amount\":" + amount + ",\"source\":\"" + source + "\"}";
        }

        private static string Step(string kind, string source, string target, string duration, string quantity, string missing)
        {
            return "{\"kind\":\"" + kind + "\",\"source\":\"" + source + "\",\"target\":\"" + target +
                "\",\"duration\":" + duration + ",\"quantity\":" + quantity + ",\"missing\":" + missing + "}";
        }

        private static string Root(string intent, string steps, string conditions, string exclusions, string missing)
        {
            return "{\"intent\":\"" + intent + "\",\"steps\":" + steps + ",\"conditions\":" + conditions +
                ",\"exclusions\":" + exclusions + ",\"missing\":" + missing + "}";
        }

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Goal extraction identifies the exact field needing source repair", () =>
            {
                string first = Step("Wait", "Wait here for 10 seconds", "here", Duration("10", "game_seconds", "10 seconds"), "null", "[]");
                string invalidTarget = Root("request", "[" + first + "," + Step("Follow", "follow me for 10 seconds", "here", "null", "null", "[]") + "]", "[]", "[]", "[]");
                var result = NpcGoalChat.Parse(invalidTarget, Binding(), Turn);
                Check.Equal(NpcGoalExtractionStatus.Invalid, result.Status);
                Check.True(result.Reason.Contains("steps[1].target"), "Feedback must localize the failing target, not just reject the whole plan.");
                string invalidDuration = Root("request", "[" + Step("Wait", "Wait here for 10 seconds", "here", Duration("10", "game_seconds", "ten seconds"), "null", "[]") + "]", "[]", "[]", "[]");
                result = NpcGoalChat.Parse(invalidDuration, Binding(), Turn);
                Check.Equal(NpcGoalExtractionStatus.Invalid, result.Status);
                Check.True(result.Reason.Contains("steps[0].duration.source"));
            }));
            cases.Add(("Goal extraction accepts ordered exact source spans and preserves constraints", () =>
            {
                string output = Root("request", "[" +
                    Step("Wait", "Wait here for 10 seconds", "here", Duration("10", "game_seconds", "10 seconds"), "null", "[]") + "," +
                    Step("Follow", "follow me for 10 seconds", "me", Duration("10", "game_seconds", "10 seconds"), "null", "[]") + "]",
                    "[\"if the shop is open\"]", "[\"without spending over 5 gold\"]", "[]");
                NpcGoalExtractionResult result = NpcGoalChat.Parse(output, Binding(), Turn);
                Check.Equal(NpcGoalExtractionStatus.Valid, result.Status);
                Check.Equal(2, result.Proposal.Steps.Count);
                Check.Equal(Turn.IndexOf("Wait here", StringComparison.Ordinal), result.Proposal.Steps[0].Source.Start);
                Check.Equal("Wait here for 10 seconds", result.Proposal.Steps[0].Source.Text);
                Check.Equal("Follow", result.Proposal.Steps[1].Kind);
                Check.Equal("if the shop is open", result.Proposal.Conditions[0]);
                Check.Equal("without spending over 5 gold", result.Proposal.Exclusions[0]);
            }));

            cases.Add(("Goal extraction rejects reordered, unknown, duplicate, and malformed fields", () =>
            {
                string validStep = Step("Wait", "Wait here for 10 seconds", "here", Duration("10", "game_seconds", "10 seconds"), "null", "[]");
                string reordered = "{\"source\":\"Wait here for 10 seconds\",\"kind\":\"Wait\",\"target\":\"here\",\"duration\":null,\"quantity\":null,\"missing\":[]}";
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(Root("request", "[" + reordered + "]", "[]", "[]", "[]"), Binding(), Turn).Status);
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(Root("request", "[" + validStep + "]", "[]", "[]", "[]")
                    .Replace("\"intent\":", "\"extra\":\"x\",\"intent\":"), Binding(), Turn).Status);
                string duplicate = Root("request", "[" + validStep + "]", "[]", "[]", "[]")
                    .Replace("\"steps\":", "\"intent\":\"request\",\"steps\":");
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(duplicate, Binding(), Turn).Status);
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse("{}", Binding(), Turn).Status);
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(Root("request", "[]", "[]", "[]", "[]") + " trailing", Binding(), Turn).Status);
            }));

            cases.Add(("Goal extraction preserves explicit unsupported and missing fields without execution", () =>
            {
                string output = Root("request", "[" +
                    Step("Other", "pick the lock at the north door", "the north door", "null", "null", "[\"lockpick skill\"]") + "]",
                    "[]", "[]", "[\"which door\"]");
                NpcGoalExtractionResult result = NpcGoalChat.Parse(output, Binding(), "pick the lock at the north door");
                Check.Equal(NpcGoalExtractionStatus.Valid, result.Status);
                Check.Equal(NpcGoalIntent.Request, result.Proposal.Intent);
                Check.Equal("Other", result.Proposal.Steps[0].Kind);
                Check.Equal("lockpick skill", result.Proposal.Steps[0].Missing[0]);
                Check.Equal("which door", result.Proposal.Missing[0]);
                Check.True(result.Proposal.Steps[0].Duration == null, "Missing duration stays unspecified.");
            }));

            cases.Add(("Goal extraction binds structured numeric values to exact quoted parameter spans", () =>
            {
                string durationValue = Root("request", "[" +
                    Step("Wait", "Wait here for ten seconds", "here", Duration("60", "game_seconds", "ten seconds"), "null", "[]") + "]", "[]", "[]", "[]");
                NpcGoalExtractionResult parsed = NpcGoalChat.Parse(durationValue, Binding(), "Wait here for ten seconds");
                Check.Equal(NpcGoalExtractionStatus.Valid, parsed.Status);
                Check.Equal(60m, parsed.Proposal.Steps[0].Duration.Amount);
                Check.Equal(NpcGoalTimeUnit.GameSeconds, parsed.Proposal.Steps[0].Duration.Unit);
                string quantityValue = Root("request", "[" + Step("Purchase", "buy 3 keys", "keys", "null", Quantity("2", "3 keys"), "[]") + "]",
                    "[]", "[]", "[]");
                parsed = NpcGoalChat.Parse(quantityValue, Binding(), "buy 3 keys");
                Check.Equal(NpcGoalExtractionStatus.Valid, parsed.Status);
                Check.Equal(2, parsed.Proposal.Steps[0].Quantity.Amount);
                string unquotedValue = Root("request", "[" +
                    Step("Wait", "Wait here for 10 seconds", "here", Duration("60", "game_seconds", "60 seconds"), "null", "[]") + "]", "[]", "[]", "[]");
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(unquotedValue, Binding(), Turn).Status);
                string fractional = Root("request", "[" +
                    Step("Wait", "Wait here for half an hour", "here", Duration("0.5", "game_hours", "half an hour"), "null", "[]") + "]", "[]", "[]", "[]");
                parsed = NpcGoalChat.Parse(fractional, Binding(), "Wait here for half an hour");
                Check.Equal(NpcGoalExtractionStatus.Valid, parsed.Status);
                Check.Equal(0.5m, parsed.Proposal.Steps[0].Duration.Amount);
                string zero = Root("request", "[" +
                    Step("Wait", "Wait here for 10 seconds", "here", Duration("0", "game_seconds", "10 seconds"), "null", "[]") + "]", "[]", "[]", "[]");
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(zero, Binding(), Turn).Status);
                string exponent = Root("request", "[" +
                    Step("Wait", "Wait here for 10 seconds", "here", Duration("1e2", "game_seconds", "10 seconds"), "null", "[]") + "]", "[]", "[]", "[]");
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(exponent, Binding(), Turn).Status);
                string fractionalQuantity = Root("request", "[" + Step("Purchase", "buy 3 keys", "keys", "null", Quantity("3.0", "3 keys"), "[]") + "]",
                    "[]", "[]", "[]");
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(fractionalQuantity, Binding(), "buy 3 keys").Status);
                string absentSource = Root("request", "[" +
                    Step("Wait", "wait forever", "", "null", "null", "[]") + "]", "[]", "[]", "[]");
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(absentSource, Binding(), Turn).Status);
                string missingTarget = Root("request", "[" +
                    Step("MoveTo", "Wait here for 10 seconds", "the moon", "null", "null", "[]") + "]", "[]", "[]", "[]");
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(missingTarget, Binding(), Turn).Status);
            }));

            cases.Add(("Goal extraction bounds steps, turn length, and handles none or clarify as non-executable", () =>
            {
                string step = Step("Wait", "Wait here for 10 seconds", "here", Duration("10", "game_seconds", "10 seconds"), "null", "[]");
                string eight = "[" + string.Join(",", new[] { step, step, step, step, step, step, step, step }) + "]";
                Check.Equal(NpcGoalExtractionStatus.Valid, NpcGoalChat.Parse(Root("request", eight, "[]", "[]", "[]"), Binding(),
                    string.Join(" ", new[] { Turn, Turn, Turn, Turn, Turn, Turn, Turn, Turn })).Status);
                string nine = "[" + string.Join(",", new[] { step, step, step, step, step, step, step, step, step }) + "]";
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(Root("request", nine, "[]", "[]", "[]"), Binding(), Turn).Status);
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(Root("request", "[]", "[]", "[]", "[]"), Binding(), Turn).Status);
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(Root("none", "[" + step + "]", "[]", "[]", "[]"), Binding(), Turn).Status);
                Check.Equal(NpcGoalExtractionStatus.Valid, NpcGoalChat.Parse(Root("none", "[]", "[]", "[]", "[]"), Binding(), Turn).Status);
                Check.Equal(NpcGoalIntent.None, NpcGoalChat.Parse(Root("none", "[]", "[]", "[]", "[]"), Binding(), Turn).Proposal.Intent);
                Check.Equal(NpcGoalIntent.Clarify, NpcGoalChat.Parse(Root("clarify", "[]", "[]", "[]", "[\"destination\"]"), Binding(), Turn).Proposal.Intent);
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(Root("request", "[]", "[]", "[]", "[]"), Binding(), new string('x', 8193)).Status);
            }));

            cases.Add(("Goal extraction strictly validates JSON escapes, types, and UTF16", () =>
            {
                string escaped = Root("request", "[" +
                    Step("Interact", "say \\\"hi\\\"", "", "null", "null", "[]") + "]", "[]", "[]", "[]");
                Check.Equal(NpcGoalExtractionStatus.Valid, NpcGoalChat.Parse(escaped, Binding(), "say \"hi\"").Status);
                string wrongType = Root("request", "[]", "[1]", "[]", "[]");
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse(wrongType, Binding(), Turn).Status);
                Check.Equal(NpcGoalExtractionStatus.Invalid, NpcGoalChat.Parse("{\"intent\":\"request\",\"steps\":[],\"conditions\":[\"\\uD800\"],\"exclusions\":[],\"missing\":[]}", Binding(), Turn).Status);
                Check.Equal(NpcGoalExtractionStatus.Unavailable, NpcGoalChat.Parse("", Binding(), Turn).Status);
            }));

            cases.Add(("Goal proposal collections are immutable defensive copies and prompt has no candidate menu", () =>
            {
                var steps = new List<NpcGoalStep>();
                var conditions = new List<string> { "door open" };
                var proposal = new NpcGoalProposal(Binding(), Turn, NpcGoalIntent.Clarify, steps, conditions,
                    new List<string>(), new List<string>());
                conditions[0] = "mutated";
                Check.Equal("door open", proposal.Conditions[0]);
                bool readOnly = false;
                try { ((IList<string>)proposal.Conditions)[0] = "mutated"; }
                catch (NotSupportedException) { readOnly = true; }
                Check.True(readOnly, "Proposal list must be read-only.");
                string prompt = NpcGoalChat.Prompt(Turn, "attributed player history", "It is raining outdoors.");
                Check.True(prompt.Contains("It is raining outdoors."));
                Check.True(prompt.Contains("All duration units mean in-game time, never real-world elapsed time."));
                Check.True(!prompt.Contains("native candidate") && !prompt.Contains("candidate list"));
                string grammar = NpcGoalChat.Grammar();
                Check.True(grammar.Contains("intent") && grammar.Contains("positive-decimal") &&
                    grammar.Contains("nullable-duration") && grammar.Contains("time-unit"));
                Check.True(grammar.Contains("\"\\\"Wait\\\"\"") && grammar.Contains("\"\\\"game_seconds\\\"\""), grammar);
                Check.True(grammar.Contains("\"\\\"\"") && grammar.Contains("\"\\\\\""), grammar);
                Check.True(grammar.Contains("\"u\" [0-9a-fA-F]{4}"), grammar);
                Check.True(grammar.Contains(@"[^""\\\x00-\x1f]") && !grammar.Contains("nullable_duration"), grammar);
            }));

            cases.Add(("Runtime extraction keeps current turn and facts fixed while history stays windowable", () =>
            {
                string history = "Conversation: 2026-10-20 10:00\n" +
                    "Player: I call the old stone arch the blue gate.\n" +
                    "NPC: I understand which place you mean.\n" +
                    "Player: I will wait near the blue gate.\n" +
                    "NPC: Very well.\n" +
                    "Player: The last landmark is the river bridge.\n" +
                    "NPC: I will remember.\n";
                string turn = "Wait by it for 10 seconds, then follow me.";
                string facts = "Factual context only; current place is the market square.";
                string instructions = NpcGoalChat.ExtractionInstructions(turn, facts);
                var connection = new LocalDialogueConnection("http://127.0.0.1:8080", 1000);
                DialogueCompletion completion = LocalDialogueCompletion.Extract(connection, instructions, history,
                    NpcGoalChat.Grammar());

                string historyBlock = LocalDialogue.HistoryContext(history);
                Check.Equal(1, Count(completion.Prompt, historyBlock));
                Check.True(completion.Prompt.Contains("Exact current player turn:\n" + turn + "\n"));
                Check.True(completion.Prompt.Contains(facts));
                Check.True(completion.Prompt.Contains("Return only one JSON object matching this exact field order and schema:"));

                string recentTwo = history.Substring(history.IndexOf("Player: I will wait", StringComparison.Ordinal));
                int fixedLength = completion.Prompt.Length - historyBlock.Length;
                int budget = fixedLength + LocalDialogue.HistoryContext(recentTwo).Length;
                string fitted = DialogueHistoryWindow.Fit(completion.Prompt, history, budget, value => value.Length);
                Check.True(fitted != null, "Recent complete exchanges should fit.");
                Check.True(fitted.Contains("Player: I will wait near the blue gate."));
                Check.True(fitted.Contains("NPC: Very well."));
                Check.True(fitted.Contains("Player: The last landmark is the river bridge."));
                Check.True(fitted.Contains("NPC: I will remember."));
                Check.True(!fitted.Contains("Player: I call the old stone arch"));
                Check.True(fitted.Contains("Exact current player turn:\n" + turn + "\n"));
                Check.True(fitted.Contains(facts));

                Check.Equal<string>(null, DialogueHistoryWindow.Fit(completion.Prompt, history, fixedLength - 1, value => value.Length));
            }));
        }

        private static int Count(string value, string search)
        {
            int count = 0;
            int start = 0;
            while ((start = value.IndexOf(search, start, StringComparison.Ordinal)) >= 0)
            {
                count++;
                start += search.Length;
            }
            return count;
        }
    }
}
