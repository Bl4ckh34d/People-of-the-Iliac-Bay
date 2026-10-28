using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Read-only native transport probe. It does not display text or commit mood.
    // TownSmokeHarness owns the opt-in run-flag hook and native execution.
    internal static class SocialDialogueStreamingProbe
    {
        private const string RunFlag = "-peopleoftheiliacbay-social-dialogue-stream-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, string character, string fixtureSave)
        {
            ConversationMoodProbe.Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave && GameManager.Instance &&
                GameManager.Instance.IsReady && GameManager.Instance.PlayerEntity != null &&
                GameManager.Instance.PlayerEntity.Name == OwnedCharacter,
                "Social dialogue stream probe requires its explicit opt-in flag and owned disposable fixture.");
            ConversationMoodProbe.Require(host != null, "Social dialogue stream probe requires the active mod host.");

            LocalDialogueClient client = ConversationMoodProbe.ReadHostField(host, "localDialogue") as LocalDialogueClient;
            ConversationMoodProbe.Require(client != null && client.enabled && !client.IsBusy,
                "Social dialogue stream probe requires an idle local dialogue client.");
            MethodInfo connectionFactory = typeof(PeopleOfTheIliacBayMod).GetMethod("NewDialogueConnection", PrivateInstance);
            ConversationMoodProbe.Require(connectionFactory != null,
                "The production local dialogue connection factory is unavailable.");
            LocalDialogueConnection connection = connectionFactory.Invoke(host, null) as LocalDialogueConnection;
            ConversationMoodProbe.Require(connection != null,
                "The production local dialogue connection could not be created.");

            var request = new DialogueRequest(
                "Please answer in exactly two short sentences about the night sky.",
                "I can only offer two brief sentences.",
                "Streaming Probe Resident", "Breton", "Female", "resident", 50,
                true, "", "", "", null, null, true, 0);

            int order = 0;
            int firstSentenceOrder = 0;
            int sentenceCallbackCount = 0;
            int completionOrder = 0;
            float firstSentenceMonotonicSeconds = -1f;
            float finalResultMonotonicSeconds = -1f;
            string firstSentence = null;
            var callbackEvidence = new List<string>();
            SocialDialogueStreamResult finalResult = null;
            bool terminalDelivered = false;
            long successfulCompletionsBefore = client.ReplyCompletionCount;
            ConversationMoodProbe.Require(client.RequestStreaming(connection, request,
                sentence =>
                {
                    sentenceCallbackCount++;
                    int callbackOrder = ++order;
                    float callbackMonotonicSeconds = Time.realtimeSinceStartup;
                    if (firstSentenceOrder == 0)
                    {
                        firstSentenceOrder = callbackOrder;
                        firstSentenceMonotonicSeconds = callbackMonotonicSeconds;
                        firstSentence = sentence;
                    }
                    callbackEvidence.Add("index=" + sentenceCallbackCount + ";order=" + callbackOrder +
                        ";monotonic-seconds=" + callbackMonotonicSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        ";text=" + Quote(sentence));
                    return true;
                },
                result =>
                {
                    completionOrder = ++order;
                    finalResultMonotonicSeconds = Time.realtimeSinceStartup;
                    finalResult = result;
                    terminalDelivered = true;
                }), "Native streaming request was not accepted by the idle dialogue client.");

            float deadline = Time.realtimeSinceStartup + 120f;
            while (!terminalDelivered && Time.realtimeSinceStartup < deadline) yield return null;
            ConversationMoodProbe.Require(terminalDelivered && finalResult != null && finalResult.Complete,
                "Native social stream did not deliver a complete validated terminal result.");
            string emittedSegments = finalResult.EmittedSegments == null ? "null" :
                string.Join(" | ", CopyQuotedSegments(finalResult.EmittedSegments));
            string diagnostic = "sentence-callback-count=" + sentenceCallbackCount +
                "; callback-evidence=[" + string.Join(" || ", callbackEvidence.ToArray()) + "]" +
                "; first-sentence-order=" + firstSentenceOrder + "; completion-order=" + completionOrder +
                "; first-sentence-monotonic-seconds=" + firstSentenceMonotonicSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                "; final-result-monotonic-seconds=" + finalResultMonotonicSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                "; mood-has-value=" + finalResult.MoodDelta.HasValue +
                "; mood-delta=" + (finalResult.MoodDelta.HasValue ? finalResult.MoodDelta.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null") +
                "; reply=" + Quote(finalResult.Reply) + "; emitted-text=" + Quote(finalResult.EmittedText) +
                "; emitted-segments=[" + emittedSegments + "]; request-player-text=" + Quote(request.PlayerText) +
                "; timestamp-source=Unity-main-thread-callback-not-backend-SSE-arrival.";
            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DIALOGUE_STREAM_DIAGNOSTIC: " + diagnostic);
            ConversationMoodProbe.Require(sentenceCallbackCount >= 2 && firstSentenceOrder > 0 &&
                firstSentenceOrder < completionOrder && firstSentenceMonotonicSeconds >= 0f &&
                finalResultMonotonicSeconds > firstSentenceMonotonicSeconds &&
                !string.IsNullOrWhiteSpace(firstSentence) && finalResult.MoodDelta.HasValue &&
                string.Equals(finalResult.EmittedText, finalResult.Reply, StringComparison.Ordinal),
                "Native stream criteria failed: at least two sentences, first before final structure, mood present, emitted text exact. " + diagnostic);
            ConversationMoodProbe.Require(client.ReplyCompletionCount == successfulCompletionsBefore + 1,
                "Native stream successful-completion accounting did not advance exactly once.");

            int cancelledSentenceCallbacks = 0;
            int cancelledTerminalCallbacks = 0;
            long successfulCompletionsAfterSuccess = client.ReplyCompletionCount;
            ConversationMoodProbe.Require(client.RequestStreaming(connection, request,
                sentence =>
                {
                    cancelledSentenceCallbacks++;
                    client.Cancel();
                    return true;
                },
                result =>
                {
                    cancelledTerminalCallbacks++;
                }), "Native cancellation request was not accepted by the idle dialogue client.");

            deadline = Time.realtimeSinceStartup + 120f;
            while (cancelledSentenceCallbacks == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            ConversationMoodProbe.Require(cancelledSentenceCallbacks == 1 && !client.IsBusy,
                "Native stream cancellation did not occur immediately after its first sentence callback.");
            float quietUntil = Time.realtimeSinceStartup + 2f;
            while (Time.realtimeSinceStartup < quietUntil) yield return null;
            ConversationMoodProbe.Require(cancelledTerminalCallbacks == 0 &&
                client.ReplyCompletionCount == successfulCompletionsAfterSuccess,
                "Cancelled native stream delivered a ghost terminal result or completion that could commit mood.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DIALOGUE_STREAM_PROBE_PASS: first-sentence-before-final=true; " +
                "sentence-callback-count=" + sentenceCallbackCount + "; first-sentence-monotonic-seconds=" +
                firstSentenceMonotonicSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                "; final-result-monotonic-seconds=" + finalResultMonotonicSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                "; cancel-after-first-sentence=true; cancelled-terminal-callbacks=0; mood-commit-path=not-invoked.");
        }

        private static string[] CopyQuotedSegments(IReadOnlyList<string> segments)
        {
            var copy = new string[segments.Count];
            for (int index = 0; index < segments.Count; index++) copy[index] = Quote(segments[index]);
            return copy;
        }

        private static string Quote(string value)
        { return "\"" + LocalDialogueCompletion.EscapeJson(value ?? string.Empty) + "\""; }
    }
}
