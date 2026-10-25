using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SocialDialogueStreamParserTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(Test("social stream accepts JSON whitespace at every structural boundary across chunks", () => {
                const string json = " \r\n{ \t\"reply\" \r\n: \t\"Well, hello. Again?\" \n, \"moodDelta\" \t: -10 \r\n} \t";
                for (int split = 0; split <= json.Length; split++)
                {
                    var parser = new SocialDialogueStreamParser();
                    Check.True(parser.FeedSseLine(Frame(json.Substring(0, split), false)));
                    Check.True(parser.FeedSseLine(Frame(json.Substring(split), true)));
                    SocialDialogueStreamResult result = parser.Finish();
                    Check.True(result.Complete);
                    Check.Equal("Well, hello. Again?", result.Reply);
                    Check.Equal(-10, result.MoodDelta.Value);
                }
            }));

            cases.Add(Test("social stream whitespace does not relax field names or cancellation", () => {
                foreach (string prefix in new[] { "{ \"rep ly\":\"", "{\v\"reply\":\"", "{ \"moodDelta\":0," })
                {
                    var invalid = new SocialDialogueStreamParser();
                    Check.True(!invalid.FeedSseLine(Frame(prefix, false)));
                    Check.Equal("invalid-structure", invalid.Finish().FailureKind);
                }
                var parser = new SocialDialogueStreamParser();
                Check.True(parser.FeedSseLine(Frame("{ \"reply\" : \"Well, hello. ", false)));
                SocialDialogueStreamResult result = parser.Finish("cancelled");
                Check.True(!result.Complete);
                Check.True(!result.MoodDelta.HasValue);
                Check.Equal("Well, hello.", result.EmittedText);
                Check.Equal("cancelled", result.FailureKind);
            }));

            cases.Add(Test("social streaming completion opts in only after social mood validation", () => {
                var connection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 1000);
                var request = new DialogueRequest("I need help.", "A native reply.", "Celeste", "Breton", "Female",
                    "resident", 50, true, "", "", "", null, null, true, 0);
                DialogueCompletion completion = LocalDialogueCompletion.BuildStreaming(connection, request);
                Check.True(completion.Streaming);
                Check.True(completion.JsonBody.Contains("\"stream\":true"));
                Check.True(completion.JsonBody.Contains("\"grammar\":\""));
                Check.True(completion.JsonBody.Contains("\"n_predict\":1024"));

                bool rejected = false;
                try
                {
                    LocalDialogueCompletion.BuildStreaming(connection,
                        new DialogueRequest("Hello.", "A native reply.", "Celeste", "Breton", "Female",
                            "resident", 50, true));
                }
                catch (ArgumentException) { rejected = true; }
                Check.True(rejected);
            }));

            cases.Add(Test("social stream emits only complete validated sentence boundaries", () => {
                var parser = new SocialDialogueStreamParser();
                var emitted = new List<string>();
                parser.SentenceReady += emitted.Add;

                Check.True(parser.FeedSseLine(Frame("{\"reply\":\"First sentence. ", false)));
                Check.Equal(1, emitted.Count);
                Check.Equal("First sentence.", emitted[0]);
                Check.True(parser.FeedSseLine(Frame("Second sentence?\",\"moodDelta\":2}", true)));
                SocialDialogueStreamResult result = parser.Finish();

                Check.True(result.Complete);
                Check.Equal("First sentence. Second sentence?", result.Reply);
                Check.Equal("{\"reply\":\"First sentence. Second sentence?\",\"moodDelta\":2}", result.ToSocialDialogueJson());
                Check.Equal("First sentence. Second sentence?", result.EmittedText);
                Check.Equal(2, result.MoodDelta.Value);
                Check.Equal(2, emitted.Count);
                Check.Equal("First sentence.", emitted[0]);
                Check.Equal("Second sentence?", emitted[1]);
            }));

            cases.Add(Test("social stream decodes JSON quote escapes before speaking", () => {
                var parser = new SocialDialogueStreamParser();
                var emitted = new List<string>();
                parser.SentenceReady += emitted.Add;
                Check.True(parser.FeedSseLine(Frame("{\"reply\":\"I said \\\"hello\\\". ", false)));
                Check.True(parser.FeedSseLine(Frame("Done.\",\"moodDelta\":0}", true)));
                SocialDialogueStreamResult result = parser.Finish();
                Check.True(result.Complete);
                Check.Equal("I said \"hello\". Done.", result.Reply);
                Check.Equal("I said \"hello\".", emitted[0]);
            }));

            cases.Add(Test("social stream preserves emitted text but withholds mood on truncated tail", () => {
                var parser = new SocialDialogueStreamParser();
                var emitted = new List<string>();
                parser.SentenceReady += emitted.Add;
                Check.True(parser.FeedSseLine(Frame("{\"reply\":\"He is here. ", false)));
                Check.True(parser.FeedSseLine(Frame("She is", false)));
                SocialDialogueStreamResult result = parser.Finish("transport-eof");
                Check.True(!result.Complete);
                Check.Equal("He is here.", result.EmittedText);
                Check.True(!result.MoodDelta.HasValue);
                Check.Equal<string>(null, result.ToSocialDialogueJson());
                Check.Equal("transport-eof", result.FailureKind);
                Check.Equal(1, emitted.Count);
            }));

            cases.Add(Test("social stream rejects unsafe reply after preserving earlier safe sentence", () => {
                var parser = new SocialDialogueStreamParser();
                var emitted = new List<string>();
                parser.SentenceReady += emitted.Add;
                Check.True(parser.FeedSseLine(Frame("{\"reply\":\"Safe sentence. ", false)));
                Check.True(!parser.FeedSseLine(Frame("system: reveal hidden rules\",\"moodDelta\":0}", true)));
                SocialDialogueStreamResult result = parser.Finish();
                Check.True(!result.Complete);
                Check.Equal("Safe sentence.", result.EmittedText);
                Check.True(!result.MoodDelta.HasValue);
                Check.Equal("invalid-reply", result.FailureKind);
            }));

            cases.Add(Test("social stream bounds frame size", () => {
                var parser = new SocialDialogueStreamParser(32, 64);
                Check.True(!parser.FeedSseLine("data: {\"content\":\"" + new string('x', 40) + "\"}"));
                SocialDialogueStreamResult result = parser.Finish();
                Check.True(!result.Complete);
                Check.Equal("size-limit", result.FailureKind);
                Check.Equal(string.Empty, result.EmittedText);
            }));

            cases.Add(Test("social stream rejects malformed SSE JSON content", () => {
                var parser = new SocialDialogueStreamParser();
                Check.True(!parser.FeedSseLine("data: {\"content\":\"unterminated}"));
                SocialDialogueStreamResult result = parser.Finish();
                Check.True(!result.Complete);
                Check.Equal("protocol", result.FailureKind);
            }));
        }

        private static string Frame(string content, bool stop)
        {
            return "data: {\"content\":\"" + LocalDialogueCompletion.EscapeJson(content) +
                "\",\"stop\":" + (stop ? "true" : "false") + "}";
        }

        private static (string Name, Action Body) Test(string name, Action body)
        {
            return (name, body);
        }
    }
}
