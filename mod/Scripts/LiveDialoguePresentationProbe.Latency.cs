using System;
using System.Collections;
using System.Diagnostics;
using DaggerfallWorkshop.Game.UserInterface;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class LiveDialoguePresentationProbe
    {
        private static bool LatencyProbe { get { return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("POTIB_SPEECH_LATENCY")); } }

        private static IEnumerator ExerciseSpeechLatency(Context context)
        {
            const string question = "Hi, how are you? Tell me in three short sentences.";
            var host = context.Host;
            long before = context.Store.CaptureConversationMood(context.PersonId).AcceptedTurnSequence;
            int onsetsBefore = context.Onsets.Count;
            long start = Stopwatch.GetTimestamp();
            double onsetSeconds = -1, generationSeconds = -1;
            bool partialText = false, speechMouth = false;
            int maxTextEnd = 0;
            string requestId = null;
            long completions = context.Client.ReplyCompletionCount;
            string capture = Environment.GetEnvironmentVariable("POTIB_SPEECH_CAPTURE");
            if (!string.IsNullOrEmpty(capture))
                context.Client.ForegroundReplyTrace = (request, payload, response, extracted) =>
                {
                    System.IO.File.WriteAllText(capture + ".request.json", payload);
                    System.IO.File.WriteAllText(capture + ".response.json", response);
                };
            Method(host.GetType(), "SubmitFreeText", 2).Invoke(host, new object[] { context.Talk, question });
            float deadline = Time.realtimeSinceStartup + 100f;
            while (Time.realtimeSinceStartup < deadline)
            {
                double elapsed = (double)(Stopwatch.GetTimestamp() - start) / Stopwatch.Frequency;
                if (context.Client.ReplyCompletionCount > completions && generationSeconds < 0) generationSeconds = elapsed;
                var pending = ReadField(host, "pendingAcceptedSpeechReveal");
                if (pending != null && requestId == null) requestId = ReadString(pending, "RequestId");
                if (context.Onsets.Count > onsetsBefore && onsetSeconds < 0)
                {
                    var receipt = context.Onsets[onsetsBefore].Receipt;
                    Require(receipt.PersonId == context.PersonId && receipt.ConversationToken == context.ConversationToken,
                        "Latency sample received audio from the wrong NPC/conversation.");
                    Require(context.Onsets[onsetsBefore].AlignmentPresent && context.Onsets[onsetsBefore].VisemesPresent,
                        "Complete audio must have mouth cues and word alignment ready at playback onset.");
                    requestId = receipt.RequestId;
                    onsetSeconds = (double)(receipt.FirstNonZeroMixerTicks - start) / Stopwatch.Frequency;
                }
                var panel = context.Transcript.Parent as Panel;
                if (panel != null)
                    foreach (var component in panel.Components)
                    {
                        var overlay = component as StreamingWordHighlightOverlay;
                        if (overlay == null) continue;
                        string id = ReadString(overlay, "fullWavRequestId");
                        if (requestId == null || id != requestId) continue;
                        int textEnd = ReadInt(overlay, "currentCumulativeTextEnd");
                        string full = ReadString(overlay, "rowText");
                        Require(onsetSeconds >= 0 || textEnd == 0, "NPC words appeared before nonzero mixer playback.");
                        if (onsetSeconds >= 0 && context.Bridge.OutputAudioSource.isPlaying && textEnd > 0 &&
                            full != null && textEnd < full.Length &&
                            ReadString(overlay, "currentProgressSource") == "forced-word-alignment")
                        { partialText = true; maxTextEnd = Math.Max(maxTextEnd, textEnd); }
                    }
                var source = ReadField(host, "splitPortraitSpeech") as SplitPortraitSpeechSource;
                if (source != null && onsetSeconds >= 0)
                {
                    var speech = source.Read(context.PersonId, context.ConversationToken);
                    if (speech.IsPlaying && speech.RequestId == requestId && speech.MouthShape != PortraitMouthShape.X)
                        speechMouth = true;
                }
                bool finished = requestId != null && context.Bridge.GetRequestState(requestId) == BreezeTtsTrialBridge.RequestState.Completed;
                if (finished && !context.Client.IsBusy && !ReadBool(host, "conversationBusy")) break;
                yield return null;
            }
            Require(onsetSeconds >= 0 && generationSeconds >= 0 && partialText && speechMouth,
                "Speech latency sample lacks audio, partial word reveal or mouth movement: onset=" + onsetSeconds +
                "; generation=" + generationSeconds + "; partial=" + partialText + "; mouth=" + speechMouth);
            Require(!ReadBool(host, "conversationBusy") && !context.Client.IsBusy,
                "Speech latency sample did not finish.");
            Require(context.Store.CaptureConversationMood(context.PersonId).AcceptedTurnSequence == before + 1,
                "One completed reply must commit one social turn.");
            Require(Count(ReadHistory(context.Talk), "Player: " + question) == 1,
                "Speech presentation duplicated the conversation history.");
            context.Client.ForegroundReplyTrace = null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SPEECH_LATENCY_PASS: streaming=False" +
                "; firstAudioSeconds=" + onsetSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; llmCompleteSeconds=" + generationSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                "; wordsDuringAudio=true; wordsBeforeAudio=false; mouthAnimated=true; socialCommits=1; request=" + requestId +
                "; partialTextEnd=" + maxTextEnd + ".");
        }
    }
}
