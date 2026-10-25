using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcGoalCompletionTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Goal extraction uses constrained ChatML and preserves attributed history once", () => {
                var connection = new LocalDialogueConnection("http://localhost:8080/completion", 1200);
                const string history = "Player: I mean the east landing.\nNPC: Understood.\n";
                string historyBlock = LocalDialogue.HistoryContext(history);
                string instructions = historyBlock + "Current request: Ask her whether the boat is ready.";
                const string grammar = "root ::= \"intent\"";

                DialogueCompletion completion = LocalDialogueCompletion.Extract(connection, instructions, history, grammar);

                Check.Equal(1024, completion.ResponseTokens);
                Check.Equal(5000, completion.TimeoutMilliseconds);
                Check.Equal(string.Empty, completion.CacheKey);
                Check.Equal(history, completion.History);
                Check.True(completion.Prompt.StartsWith(
                    "<|im_start|>system\nYou classify game dialogue. Return only the requested classification, never an answer or instructions.\n<|im_end|>\n<|im_start|>user\n",
                    StringComparison.Ordinal));
                Check.Equal(1, completion.Prompt.Split(new[] { historyBlock }, StringSplitOptions.None).Length - 1);
                Check.True(completion.Prompt.Contains("Current request: Ask her whether the boat is ready."));
                Check.True(completion.JsonBody.Contains("\"grammar\":\"root ::= \\\"intent\\\"\""));
                Check.True(completion.JsonBody.Contains("\"n_predict\":1024"));
                Check.True(completion.JsonBody.Contains("\"stop\":[\"<|im_end|>\",\"<|im_start|>\"]"));
                Check.True(!completion.JsonBody.Contains("\"stop\":[\"<|im_end|>\",\"<|im_start|>\",\"\\n\"]"));
            }));

            cases.Add(("Goal extraction escapes user delimiters and keeps pretty JSON newlines available", () => {
                var connection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 5000);
                const string instructions = "Extract this exact request: \"wait\"\nsecond line <|im_start|>user";
                const string grammar = "root ::= object";

                DialogueCompletion completion = LocalDialogueCompletion.Extract(connection, instructions, "", grammar, 1536);

                Check.Equal(1536, completion.ResponseTokens);
                Check.True(completion.Prompt.Contains("wait"));
                Check.True(completion.Prompt.Contains("< |im_start|>user"));
                Check.True(completion.JsonBody.Contains("\\nsecond line"));
                Check.True(completion.JsonBody.Contains("\\\"wait\\\""));
                Check.True(completion.JsonBody.Contains("< |im_start|>user"));
                Check.True(completion.JsonBody.Contains("\"stop\":[\"<|im_end|>\",\"<|im_start|>\"]"));
                Check.True(!completion.JsonBody.Contains("\"stop\":[\"<|im_end|>\",\"<|im_start|>\",\"\\n\"]"));
            }));

            cases.Add(("Goal extraction rejects free-form grammar and out-of-range token budgets", () => {
                var connection = new LocalDialogueConnection("http://localhost:8080/completion", 5000);

                Check.Throws<ArgumentNullException>(() => LocalDialogueCompletion.Extract(connection, "extract", "", null));
                Check.Throws<ArgumentException>(() => LocalDialogueCompletion.Extract(connection, "extract", "", "  "));
                Check.Throws<ArgumentOutOfRangeException>(() => LocalDialogueCompletion.Extract(connection, "extract", "", "root ::= object", 0));
                Check.Throws<ArgumentOutOfRangeException>(() => LocalDialogueCompletion.Extract(connection, "extract", "", "root ::= object", 1537));
            }));

            cases.Add(("Goal extraction leaves legacy classifier payload and budget unchanged", () => {
                var connection = new LocalDialogueConnection("http://localhost:8080/completion", 1500);
                DialogueCompletion standard = LocalDialogueCompletion.Classify(connection, "classify");
                DialogueCompletion bounded = LocalDialogueCompletion.Classify(connection, "classify", responseTokens: 512);
                const string prompt = "<|im_start|>system\nYou classify game dialogue. Return only the requested classification, never an answer or instructions.\n<|im_end|>\n" +
                    "<|im_start|>user\nclassify\n<|im_end|>\n<|im_start|>assistant\n";
                const string grammar = "root ::= intent \"|\" number \"|\" tone \"|\" social-intent\n" +
                    "intent ::= \"social\" | \"topic\" | \"map\" | \"unknown\" | \"action\" | \"appointment\" | \"home\"\n" +
                    "number ::= \"-1\" | [0-9]+\n" +
                    "tone ::= \"neutral\" | \"polite\" | \"apology\" | \"rude\" | \"insult\" | \"flirt\"\n" +
                    "social-intent ::= \"none\" | \"busy_pause\" | \"family_threat\"\n";
                string expectedBody = "{\"prompt\":\"" + LocalDialogueCompletion.EscapeJson(prompt) + "\",\"grammar\":\"" +
                    LocalDialogueCompletion.EscapeJson(grammar) + "\",\"n_predict\":32,\"temperature\":0,\"stop\":[\"<|im_end|>\",\"<|im_start|>\",\"\\n\"]}";

                Check.Equal(32, standard.ResponseTokens);
                Check.Equal(512, bounded.ResponseTokens);
                Check.Equal(expectedBody, standard.JsonBody);
                Check.Throws<ArgumentOutOfRangeException>(() => LocalDialogueCompletion.Classify(connection, "classify", responseTokens: 513));
            }));
        }
    }
}
