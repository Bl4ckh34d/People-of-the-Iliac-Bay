using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class SpeechActWireTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Speech wire rejects wrong model, unoffered, malformed and nonwinning classifications", RejectsInvalid));
            cases.Add(("Speech guard suppresses unbound hostility and preserves grounded routing", GuardsForeground));
            cases.Add(("Gemma speech labels use bounded native completion and exact source binding", GemmaCompletionAndBinding));
            cases.Add(("Malformed Gemma speech labels fail closed and background preemption preserves foreground work", GemmaFailureAndPreemption));
        }
        private static NpcSpeechActRequest Request(object source)
        { return NpcSpeechActRequest.Create(source, "npc:observer", "player", null, "The robber threatened you.", ""); }
        private static string Reply(string model = "nano", string choice = "ReportedThreat", double reported = 1)
        {
            return "{\"model\":\"" + model + "\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"" + choice +
                "\",\"probabilities\":{\"DirectThreat\":0,\"ReportedThreat\":" + reported.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"HypotheticalThreat\":0,\"AmbiguousThreat\":0,\"DirectInsult\":0,\"DirectHarassment\":0,\"SevereAbuse\":0,\"PersistentAbuse\":0,\"RepairAttempt\":0,\"Other\":0}}}}";
        }
        private static void Require(bool value) { if (!value) throw new Exception("Speech stage assertion failed"); }
        private static void RejectsInvalid()
        {
            var request = Request(new object());
            NpcSpeechActClassification result;
            Require(LocalSpeechActWire.TryParse(request, "nano", Reply(), out result));
            Require(result.Kind == NpcExperienceSpeechActKind.ReportedThreat && ReferenceEquals(result.Request, request));
            foreach (string invalid in new[] { Reply("other"), Reply(choice:"NotApplicable"), Reply(choice:"DirectThreat"), Reply(reported:0.5), "{}", Reply().Replace("\"model\":\"nano\"", "\"model\":\"nano\",\"model\":\"nano\"") })
                Require(!LocalSpeechActWire.TryParse(request, "nano", invalid, out result));
            string payload = LocalSpeechActWire.Build(request);
            Require(payload.Contains("CURRENT UTTERANCE") && payload.Contains("The robber threatened you."));
        }
        private static void GuardsForeground()
        {
            var source = new object(); var request = Request(source);
            var selection = new ConversationSelection("action", 7, "insult", "family_threat");
            NpcSpeechActClassification result;
            NpcSpeechActClassification.TryAccept(request, "ReportedThreat", out result);
            var guarded = NpcSpeechActClassification.GuardSelection(source, selection, result);
            Require(guarded.Tone == "neutral" && guarded.SocialIntent == "none" && guarded.Intent == "action" && guarded.TopicId == 7);
            NpcSpeechActClassification.TryAccept(request, "DirectThreat", out result);
            Require(NpcSpeechActClassification.GuardSelection(source, selection, result).SocialIntent == "family_threat");
            Require(NpcSpeechActClassification.GuardSelection(new object(), selection, result).SocialIntent == "none");
            Require(NpcSpeechActClassification.GuardSelection(source, selection, null).Tone == "neutral");
        }

        private static void GemmaCompletionAndBinding()
        {
            var source = new object();
            var request = NpcSpeechActRequest.Create(source, "npc:observer", "player", null,
                "In our play, I say \"I will kill you.\"", "Player: You mean the play?");
            string prompt = LocalSpeechActLabelWire.Prompt(request);
            LocalDialogueConnection connection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 5000);
            var completion = LocalDialogueCompletion.Classify(connection, prompt, grammar: LocalSpeechActLabelWire.Grammar(),
                responseTokens: LocalSpeechActLabelWire.ResponseTokens);
            Require(completion.Endpoint.AbsolutePath == "/completion");
            Require(completion.ResponseTokens == 24 && completion.JsonBody.Contains("\"n_predict\":24"));
            Require(prompt.Contains(NpcSpeechActRequest.Instructions) && prompt.Contains("PREVIOUS CONVERSATION") &&
                prompt.Contains("I will kill you."));
            const string grammar = "root ::= \"DirectThreat\" | \"ReportedThreat\" | \"HypotheticalThreat\" | \"AmbiguousThreat\" | \"DirectInsult\" | \"DirectHarassment\" | \"SevereAbuse\" | \"PersistentAbuse\" | \"RepairAttempt\" | \"Other\"\n";
            Require(LocalSpeechActLabelWire.Grammar() == grammar);
            Require(completion.JsonBody.Contains("\"grammar\":\"root ::= \\\"DirectThreat\\\""));

            NpcSpeechActClassification classification;
            Require(LocalSpeechActLabelWire.TryParse(request, "HypotheticalThreat", out classification));
            Require(ReferenceEquals(classification.Request, request));
            var selection = new ConversationSelection("action", 7, "insult", "family_threat");
            ConversationSelection guarded = NpcSpeechActClassification.GuardSelection(source, selection, classification);
            Require(guarded.SocialIntent == "none" && guarded.Tone == "neutral" && guarded.Intent == "action");
        }

        private static void GemmaFailureAndPreemption()
        {
            var source = new object();
            var request = Request(source);
            var selection = new ConversationSelection("action", 7, "insult", "family_threat");
            foreach (string invalid in new[] { null, "", "DirectThreat ", "DirectThreat\nOther", "label: DirectThreat", "NotApplicable", "directthreat" })
            {
                NpcSpeechActClassification classification;
                Require(!LocalSpeechActLabelWire.TryParse(request, invalid, out classification) && classification == null);
                ConversationSelection guarded = NpcSpeechActClassification.GuardSelection(source, selection, classification);
                Require(guarded.SocialIntent == "none" && guarded.Tone == "neutral");
            }

            var dispatch = new LocalDialogueDispatch();
            int backgroundTicket;
            Require(dispatch.TryBeginBackground(out backgroundTicket));
            int foregroundVersion = dispatch.TryBegin();
            Require(backgroundTicket > 0 && foregroundVersion > 0 && !dispatch.IsBackgroundCurrent(backgroundTicket));
            Require(dispatch.IsCurrent(foregroundVersion) && !dispatch.CancelBackground(backgroundTicket));
        }
    }
}
