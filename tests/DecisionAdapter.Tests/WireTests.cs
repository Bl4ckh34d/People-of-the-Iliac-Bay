using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class WireTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("wire request is ordered, escaped, hashed, and excludes gold", BuildRequest));
            cases.Add(("wire preserves separate multiline instructions and purpose metadata", MultilineInstructions));
            cases.Add(("wire request rejects invalid UTF-16 instead of emitting invalid UTF-8", BuildRejectsInvalidUnicode));
            cases.Add(("wire payload bytes are defensively copied", DefensiveCopy));
            cases.Add(("wire parses the real systemone answer shape", ParsesAnswer));
            cases.Add(("wire rejects duplicate keys at every depth", DuplicateKeys));
            cases.Add(("wire rejects malformed, trailing, deep, large and control input", MalformedBounds));
            cases.Add(("wire rejects wrong types, unknown answers and missing probabilities", SchemaFailures));
        }

        private static NpcDecisionRequest Request(string originalTurn = "Say \"wait\" & café", string instructions = null,
            string context = "Context\\line\\nsecond")
        {
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, "resident:42", null,
                "session-a", 3, 7, new string('a', 32), new string('b', 64), "scene-a", 5);
            var empty = ConversationContextFact<string>.Unknown();
            var snapshot = new ConversationContextSnapshot(binding, 100, empty, empty,
                ConversationContextFact<ConversationDialogueRole>.Unknown(), empty, empty,
                ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Unknown(), ConversationContextFact<bool>.Unknown(),
                ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(), empty, 0, 17,
                null, new List<ConversationContextCommitment>(), new List<ConversationContextSourceHandle>());
            var options = new[]
                {
                    new NpcDecisionOption("wait_here", "Wait at café", true),
                    new NpcDecisionOption("follow", "Follow resident", true)
                };
            return instructions == null
                ? new NpcDecisionRequest(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), snapshot, 11,
                    "decision-purpose", originalTurn, context, options)
                : new NpcDecisionRequest(Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"), snapshot, 11,
                    "decision-purpose", instructions, originalTurn, context, options);
        }

        private static void BuildRequest()
        {
            var payload = LocalDecisionWire.Build(Request());
            var json = Encoding.UTF8.GetString(payload.Bytes);
            Check.True(json.Contains("\"questions\":{\"decision\":{\"type\":\"choice\""), "Question key/type must be fixed.");
            Check.True(json.IndexOf("\"wait_here\"", StringComparison.Ordinal) < json.IndexOf("\"follow\"", StringComparison.Ordinal), "Offered option order must be retained.");
            Check.True(json.Contains("decision-purpose"), "Purpose must be sent as instructions.");
            Check.True(json.Contains("Say " + new string('\\', 3) + "\"wait" + new string('\\', 3) + "\" & café"),
                "Question text must preserve UTF-8 and escape JSON quotes in both JSON layers.");
            Check.True(json.Contains("resident:42") && json.Contains("01234567-89ab-cdef-0123-456789abcdef") &&
                json.Contains("originalTurn") && json.Contains("snapshotRevision") && json.Contains("candidateSetRevision"),
                "State must include request identity, actor, turn, and revision bindings.");
            Check.True(!json.Contains("selectedOptionId") && !json.Contains("expected") && !json.Contains("gold"),
                "Gold/scoring data must never enter the model wire request.");
            using (var sha = SHA256.Create())
                Check.Equal(BitConverter.ToString(sha.ComputeHash(payload.Bytes)).Replace("-", "").ToLowerInvariant(), payload.Sha256);
        }

        private static void MultilineInstructions()
        {
            string instructions = "Instruction A\r\nInstruction B\t" + new string('i', 300);
            string turn = "Player says:\nplease wait\tthen follow";
            string context = "Earlier:\r\n- Wait mentioned\nCurrent state\tverified";
            var payload = LocalDecisionWire.Build(Request(turn, instructions, context));
            using (var document = JsonDocument.Parse(payload.Bytes))
            {
                JsonElement root = document.RootElement;
                string wireInstructions = root.GetProperty("questions").GetProperty("decision").GetProperty("instructions").GetString();
                Check.Equal(instructions, wireInstructions);
                using (var state = JsonDocument.Parse(root.GetProperty("state").GetString()))
                {
                    Check.Equal("decision-purpose", state.RootElement.GetProperty("purpose").GetString());
                    Check.Equal("npc-decision-v2", state.RootElement.GetProperty("protocolVersion").GetString());
                    Check.Equal(turn, state.RootElement.GetProperty("originalTurn").GetString());
                    Check.Equal(context, state.RootElement.GetProperty("context").GetString());
                }
            }
            Check.True(instructions.Length > 128 && !instructions.Equals("decision-purpose", StringComparison.Ordinal),
                "The instruction text must stay separate from short purpose metadata.");
        }

        private static void DefensiveCopy()
        {
            var payload = LocalDecisionWire.Build(Request());
            var first = payload.Bytes;
            byte original = first[0];
            first[0] ^= 0xff;
            Check.Equal(original, payload.Bytes[0]);
        }

        private static void BuildRejectsInvalidUnicode()
        {
            Check.Throws<System.Text.EncoderFallbackException>(() => LocalDecisionWire.Build(Request("bad" + '\ud800')));
        }

        private static void ParsesAnswer()
        {
            var parsed = LocalDecisionWire.Parse("{\"model\":\"local-d3\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"wait_here\",\"probabilities\":{\"wait_here\":0.8,\"follow\":0.2},\"confidence\":0.8}},\"usage\":{\"input_tokens\":42,\"output_tokens\":3}}");
            Check.True(parsed.IsValid);
            Check.Equal(NpcDecisionFailureKind.None, parsed.FailureKind);
            Check.Equal("wait_here", parsed.SelectedOptionId);
            Check.Equal("local-d3", parsed.ModelId);
            Check.Equal(0.8, parsed.Probabilities["wait_here"]);
            Check.Equal((double?)0.8, parsed.ReportedConfidence);

            var withoutConfidence = LocalDecisionWire.Parse("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1}}}}");
            Check.True(withoutConfidence.IsValid);
            Check.Equal((double?)null, withoutConfidence.ReportedConfidence);

            var zeroCoefficients = LocalDecisionWire.Parse("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"zero\",\"probabilities\":{\"zero\":0,\"negativeZero\":-0,\"exponentZero\":0e-400}}}}");
            Check.True(zeroCoefficients.IsValid, "Coefficient-all-zero literals must remain valid zero values.");
            Check.Equal(0.0, zeroCoefficients.Probabilities["exponentZero"]);
        }

        private static void DuplicateKeys()
        {
            InvalidJson("{\"model\":\"m\",\"model\":\"n\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1}}}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"choice\":\"y\",\"probabilities\":{\"x\":1}}}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1,\"x\":0}}}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1}},\"nested\":{\"a\":1,\"a\":2}}}", NpcDecisionFailureKind.MalformedJson);
        }

        private static void MalformedBounds()
        {
            InvalidJson("{", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1}}}} trailing", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\n\",\"probabilities\":{\"x\":1}}}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\\n\",\"answers\":{}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\uD800\",\"answers\":{}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\\uD800\",\"answers\":{}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\\uDC00\",\"answers\":{}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":01}}}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1e400}}}}", NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1,\"y\":1e-400}}}}", NpcDecisionFailureKind.MalformedJson);
            string deep = "0";
            for (int i = 0; i < 17; i++) deep = "[" + deep + "]";
            InvalidJson(deep, NpcDecisionFailureKind.MalformedJson);
            InvalidJson(new string(' ', 128 * 1024 + 1), NpcDecisionFailureKind.MalformedJson);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1}},\"other\":{}}}", NpcDecisionFailureKind.InvalidSchema);
        }

        private static void SchemaFailures()
        {
            InvalidJson("[]", NpcDecisionFailureKind.InvalidSchema);
            InvalidJson("{\"model\":7,\"answers\":{}}", NpcDecisionFailureKind.InvalidSchema);
            InvalidJson("{\"model\":\"m\",\"answers\":[]}", NpcDecisionFailureKind.InvalidSchema);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":[]}}}", NpcDecisionFailureKind.InvalidSchema);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\"}}}", NpcDecisionFailureKind.InvalidSchema);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"route\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1}}}}", NpcDecisionFailureKind.InvalidSchema);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1},\"extra\":true}}}", NpcDecisionFailureKind.InvalidSchema);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":4,\"probabilities\":{\"x\":1}}}}", NpcDecisionFailureKind.InvalidSchema);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":true}}}}", NpcDecisionFailureKind.InvalidSchema);
            InvalidJson("{\"model\":\"m\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"x\",\"probabilities\":{\"x\":1},\"confidence\":\"0.8\"}}}", NpcDecisionFailureKind.InvalidSchema);
        }

        private static void InvalidJson(string text, NpcDecisionFailureKind kind)
        {
            var parsed = LocalDecisionWire.Parse(text);
            Check.True(!parsed.IsValid);
            Check.Equal(kind, parsed.FailureKind);
            Check.Equal(null, parsed.SelectedOptionId);
            Check.True(parsed.Probabilities == null || parsed.Probabilities.Count == 0);
            Check.Equal((double?)null, parsed.ReportedConfidence);
        }
    }

    internal static class Check
    {
        public static void True(bool value, string message = "Assertion failed.")
        { if (!value) throw new InvalidOperationException(message); }
        public static void Equal<T>(T expected, T actual)
        { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + " but got " + actual + "."); }
        public static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected exception " + typeof(T).Name + ".");
        }
    }
}
