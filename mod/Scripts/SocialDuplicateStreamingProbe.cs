using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Exercises the production streaming duplicate holdback and bridge queue using local control files only.
    /// It does not contact a dialogue model or TTS coordinator and does not test audio playback.
    /// </summary>
    internal static class SocialDuplicateStreamingProbe
    {
        internal const string RunFlag = SocialDuplicateReplyProbe.RunFlag;
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const string PriorReply = "The northern gate is closed. I checked it before dawn.";
        private const string FirstSegment = "The northern gate is closed.";
        private const string DivergentSecondSegment = "I am heading back now.";

        private sealed class HostFieldSnapshot
        {
            public readonly Dictionary<string, object> Values = new Dictionary<string, object>(StringComparer.Ordinal);
        }

        private sealed class ConversationEvidence
        {
            public int Mood;
            public long Sequence;
            public long MoodRevision;
            public bool DispositionKnown;
            public int Disposition;
            public string NativeTranscript;
            public string StoredTranscript;
            public string ExperienceInbox;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk)
        {
            string temporaryRoot = null;
            GameObject bridgeOwner = null;
            HostFieldSnapshot hostFields = null;
            BreezeTtsTrialBridge disposableBridge = null;
            BreezeTtsStreamingPcmAdapter disposableAdapter = null;
            Exception failure = null;
            bool completed = false;

            try
            {
                Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0,
                    "Streaming duplicate probe requires its standalone opt-in flag.");
                Require(GameManager.Instance && GameManager.Instance.IsReady &&
                    GameManager.Instance.PlayerEntity != null &&
                    GameManager.Instance.PlayerEntity.Name == OwnedCharacter &&
                    GameManager.Instance.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, BaselineSave) >= 0,
                    "Streaming duplicate probe requires its exact owned character and disposable baseline save.");
                Require(host != null && talk != null && talk.IsSetup &&
                    DaggerfallUI.UIManager.TopWindow == talk && DaggerfallUI.UIManager.ContainsWindow(talk),
                    "Streaming duplicate probe requires the still-open native Talk window.");

                string personId = ReadHostField(host, "memoryPersonId") as string;
                string conversationToken = ReadHostField(host, "conversationContextSessionId") as string;
                string boundPersonId = ReadHostField(host, "conversationPersonId") as string;
                Require(!string.IsNullOrWhiteSpace(personId) && personId == boundPersonId &&
                    !string.IsNullOrWhiteSpace(conversationToken),
                    "Streaming duplicate probe requires a stable person and current conversation binding.");
                Require(Convert.ToBoolean(ReadHostField(host, "conversationSessionOpen"), CultureInfo.InvariantCulture) &&
                    ReferenceEquals(ReadHostField(host, "memoryWindow"), talk) &&
                    Convert.ToInt32(ReadHostField(host, "memorySession"), CultureInfo.InvariantCulture) >= 0,
                    "Streaming duplicate probe requires the live dialogue-memory session for this Talk window.");
                Require(!Convert.ToBoolean(ReadHostField(host, "conversationBusy"), CultureInfo.InvariantCulture) &&
                    Convert.ToInt64(ReadHostField(host, "conversationContextIssuedStageToken"), CultureInfo.InvariantCulture) == 0 &&
                    ReadHostField(host, "streamingSocialReply") == null,
                    "Streaming duplicate probe refuses an active reply or conversation stage.");

                BreezeTtsTrialBridge productionBridge = ReadHostField(host, "breezeTtsTrial") as BreezeTtsTrialBridge;
                Require(productionBridge == null || (!productionBridge.VoiceEnabled && !productionBridge.SocialStreamingEnabled),
                    "Streaming duplicate probe requires production voice and sentence streaming to be disabled.");
                BreezeTtsStreamingPcmAdapter productionAdapter =
                    ReadHostField(host, "socialPcmAdapter") as BreezeTtsStreamingPcmAdapter;
                Require(productionAdapter == null || IsTerminalOrIdle(productionAdapter.State),
                    "Streaming duplicate probe refuses to replace a live production PCM adapter.");

                MethodInfo currentSessionMethod = RequireMethod(typeof(PeopleOfTheIliacBayMod),
                    "CurrentMemorySession", PrivateInstance);
                int memorySession = Convert.ToInt32(ReadHostField(host, "memorySession"), CultureInfo.InvariantCulture);
                Require((bool)currentSessionMethod.Invoke(host, new object[] { talk, memorySession }),
                    "Streaming duplicate probe Talk is not the active memory session.");

                WorldStateStore store = ReadHostField(host, "store") as WorldStateStore;
                Require(store != null, "Streaming duplicate probe cannot read the active save store.");

                temporaryRoot = CreateOwnedTemporaryRoot();
                bridgeOwner = new GameObject("PeopleOfTheIliacBay social duplicate streaming probe");
                string bridgeDiagnostic;
                disposableBridge = BreezeTtsTrialBridge.TryCreateFromCommandLine(bridgeOwner,
                    new[] { BreezeTtsTrialBridge.RunFlag, temporaryRoot }, out bridgeDiagnostic);
                string isolatedResponseRoot;
                Require(disposableBridge != null && bridgeDiagnostic == "trial-bridge-enabled" &&
                    disposableBridge.IsTrialMode && !disposableBridge.SocialStreamingEnabled &&
                    disposableBridge.TryGetStreamingResponseRoot(out isolatedResponseRoot) &&
                    string.Equals(Path.GetFullPath(isolatedResponseRoot),
                        Path.GetFullPath(Path.Combine(temporaryRoot, "responses")), StringComparison.OrdinalIgnoreCase),
                    "Probe-owned trial bridge could not be initialized in its isolated root.");
                SetField(typeof(BreezeTtsTrialBridge), disposableBridge, "socialStreamingEnabled", true);
                Require(disposableBridge.VoiceEnabled && disposableBridge.SocialStreamingEnabled &&
                    string.IsNullOrEmpty(disposableBridge.ConfiguredServerEndpoint),
                    "Isolated trial bridge did not enter local-only streaming test mode.");

                disposableAdapter = bridgeOwner.AddComponent<BreezeTtsStreamingPcmAdapter>();
                hostFields = CaptureHostFields(host, new[] {
                    "breezeTtsTrial", "socialPcmAdapter", "streamingSocialReply", "conversationBusy",
                    "suppressFullWavTtsForNextAppend", "conversationContextEphemeralSpeakerToken",
                    "conversationContextRequestRevision", "conversationContextCapturedSceneHandle",
                    "conversationContextIssuedStageToken", "conversationContextRejectedRequestRevision",
                    "conversationContextCurrentSnapshot", "conversationContextCapturedPlayerText",
                    "capturedConversationMemory"
                });

                SetHostField(host, "breezeTtsTrial", disposableBridge);
                SetHostField(host, "socialPcmAdapter", disposableAdapter);
                SetHostField(host, "conversationBusy", false);
                SetHostField(host, "streamingSocialReply", null);

                long requestRevision = checked(Convert.ToInt64(ReadHostField(host,
                    "conversationContextRequestRevision"), CultureInfo.InvariantCulture) + 1L);
                ConversationContextSnapshot snapshot = host.CaptureConversationContext(talk,
                    "Local streaming duplicate queue probe; no dialogue request is sent.", requestRevision);
                Require(snapshot != null && host.IsCurrentConversationContext(snapshot, talk),
                    "Streaming duplicate probe could not capture the current native context binding.");

                ConversationEvidence before = CaptureEvidence(host, talk, store, personId);
                RunExactDuplicateCase(host, talk, disposableBridge, disposableAdapter, snapshot,
                    memorySession, personId, conversationToken, before);
                RunDivergenceCase(host, talk, disposableBridge, disposableAdapter, snapshot,
                    memorySession, personId, conversationToken, before);
                RunShorterCompletionCase(host, talk, disposableBridge, disposableAdapter, snapshot,
                    memorySession, personId, conversationToken, before);

                Require(ResponseFiles(temporaryRoot).Count == 0,
                    "Probe unexpectedly found generated or coordinator-produced response artifacts.");
                completed = true;
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                if (host != null && hostFields != null)
                {
                    object session = ReadHostField(host, "streamingSocialReply");
                    if (session != null && disposableAdapter != null && disposableBridge != null)
                    {
                        try { Abandon(host, session, "social-duplicate-probe-cleanup"); }
                        catch { /* Preserve the primary failure; owned bridge cleanup follows. */ }
                    }
                    RestoreHostFields(host, hostFields);
                }

                if (disposableAdapter != null && disposableAdapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Buffering)
                {
                    try { disposableAdapter.CancelFromOwner("social-duplicate-probe-cleanup"); }
                    catch { }
                }
                if (bridgeOwner != null) UnityEngine.Object.Destroy(bridgeOwner);
            }

            // Let Unity detach the temporary adapter/event handler before deleting its isolated files.
            yield return null;
            if (temporaryRoot != null)
            {
                try { DeleteOwnedTemporaryRoot(temporaryRoot); }
                catch (Exception cleanupError)
                {
                    if (failure == null) failure = new InvalidOperationException(
                        "Streaming duplicate probe could not clean its verified temporary root.", cleanupError);
                }
            }
            if (failure != null)
                throw new InvalidOperationException("Native streaming duplicate queue probe failed.", failure);
            Require(completed, "Native streaming duplicate queue probe did not complete.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_STREAM_QUEUE_PASS: cases=exact-duplicate-no-sentence-queue,prefix-divergence-releases-in-order,shorter-prefix-flushes-on-valid-completion; local-bridge-queue-files-only=true; model-requested=false; tts-coordinator-contacted=false; response-chunks=0; audio-playback=not-tested; UI-presentation=not-tested; speaker-output=not-tested.");
        }

        private static void RunExactDuplicateCase(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            BreezeTtsTrialBridge bridge, BreezeTtsStreamingPcmAdapter adapter,
            ConversationContextSnapshot snapshot, int memorySession, string personId, string token,
            ConversationEvidence before)
        {
            int callbackCount = 0;
            string callbackJson = null;
            object session = BeginCase(host, talk, bridge, adapter, snapshot, memorySession,
                personId, token, PriorReply, json => { callbackCount++; callbackJson = json; });
            string streamId = ReadSessionField(session, "StreamId") as string;
            string requestDirectory = RequestDirectory(bridge, streamId);

            SocialDialogueStreamParser parser = CreateParser(host, session);
            Feed(parser, "{\"reply\":\"The northern gate is closed. ", false);
            Require(SentenceFiles(requestDirectory).Count == 0 &&
                SessionList(session, "AcceptedSentences").Count == 0,
                "Exact-duplicate candidate prefix was queued before final validation.");
            Feed(parser, "I checked it before dawn.\",\"moodDelta\":0}", true);
            SocialDialogueStreamResult result = parser.Finish();
            RequireValidResult(result, PriorReply, 2);
            Complete(host, session, result);

            Require(SentenceFiles(requestDirectory).Count == 0 &&
                File.Exists(Path.Combine(requestDirectory, "cancel.json")) &&
                !File.Exists(Path.Combine(requestDirectory, "complete.json")) && callbackCount == 1 &&
                string.Equals(callbackJson, result.ToSocialDialogueJson(), StringComparison.Ordinal) &&
                ReadHostField(host, "streamingSocialReply") == null,
                "Exact duplicate did not cancel before delivering one buffered-repair callback with zero sentence requests.");
            RequireEvidenceUnchanged(host, talk, before, "exact-duplicate cancellation");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_STREAM_EXACT_PASS: prior-boundary=controlled-fixture; sentence-files=0; cancel-terminal=true; buffered-callbacks=1; repair-request=not-issued-by-probe; mood-transcript-inbox-unchanged=true.");
        }

        private static void RunDivergenceCase(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            BreezeTtsTrialBridge bridge, BreezeTtsStreamingPcmAdapter adapter,
            ConversationContextSnapshot snapshot, int memorySession, string personId, string token,
            ConversationEvidence before)
        {
            object session = BeginCase(host, talk, bridge, adapter, snapshot, memorySession,
                personId, token, PriorReply, ignored => { });
            string streamId = ReadSessionField(session, "StreamId") as string;
            string requestDirectory = RequestDirectory(bridge, streamId);

            SocialDialogueStreamParser parser = CreateParser(host, session);
            Feed(parser, "{\"reply\":\"" + FirstSegment + " ", false);
            Require(SentenceFiles(requestDirectory).Count == 0 &&
                SessionList(session, "AcceptedSentences").Count == 0,
                "Potential duplicate prefix was queued before the stream diverged.");
            Feed(parser, DivergentSecondSegment + "\",\"moodDelta\":0}", true);
            SocialDialogueStreamResult result = parser.Finish();
            RequireValidResult(result, FirstSegment + " " + DivergentSecondSegment, 2);

            List<string> sentenceFiles = SentenceFiles(requestDirectory);
            Require(sentenceFiles.Count == 2 && Path.GetFileName(sentenceFiles[0]) == "0000.json" &&
                Path.GetFileName(sentenceFiles[1]) == "0001.json" &&
                FileContainsExactSentence(sentenceFiles[0], 0, FirstSegment) &&
                FileContainsExactSentence(sentenceFiles[1], 1, DivergentSecondSegment) &&
                SessionList(session, "AcceptedSentences").Count == 2 &&
                (string)SessionList(session, "AcceptedSentences")[0] == FirstSegment &&
                (string)SessionList(session, "AcceptedSentences")[1] == DivergentSecondSegment,
                "Prefix divergence did not release held and new sentences in their original order.");

            Abandon(host, session, "probe-prefix-diverged");
            Require(File.Exists(Path.Combine(requestDirectory, "cancel.json")) &&
                !File.Exists(Path.Combine(requestDirectory, "complete.json")),
                "Divergence control was not cancelled before any audio response existed.");
            RequireEvidenceUnchanged(host, talk, before, "prefix-divergence cancellation");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_STREAM_DIVERGENCE_PASS: prior-boundary=controlled-fixture; queued=2; order=original-prefix,divergent-segment; cancelled-before-audio=true; mood-transcript-inbox-unchanged=true.");
        }

        private static void RunShorterCompletionCase(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            BreezeTtsTrialBridge bridge, BreezeTtsStreamingPcmAdapter adapter,
            ConversationContextSnapshot snapshot, int memorySession, string personId, string token,
            ConversationEvidence before)
        {
            object session = BeginCase(host, talk, bridge, adapter, snapshot, memorySession,
                personId, token, PriorReply, ignored => { });
            string streamId = ReadSessionField(session, "StreamId") as string;
            string requestDirectory = RequestDirectory(bridge, streamId);

            SocialDialogueStreamParser parser = CreateParser(host, session);
            Feed(parser, "{\"reply\":\"" + FirstSegment + "\",\"moodDelta\":0}", true);
            SocialDialogueStreamResult result = parser.Finish();
            RequireValidResult(result, FirstSegment, 1);
            Require(SentenceFiles(requestDirectory).Count == 0 &&
                SessionList(session, "AcceptedSentences").Count == 0,
                "Shorter prefix was flushed before validated generation completion.");

            Complete(host, session, result);
            List<string> sentenceFiles = SentenceFiles(requestDirectory);
            Require(sentenceFiles.Count == 1 && Path.GetFileName(sentenceFiles[0]) == "0000.json" &&
                FileContainsExactSentence(sentenceFiles[0], 0, FirstSegment) &&
                File.Exists(Path.Combine(requestDirectory, "complete.json")) &&
                !File.Exists(Path.Combine(requestDirectory, "cancel.json")),
                "Valid shorter nonduplicate prefix did not flush exactly once at completion.");
            RequireEvidenceUnchanged(host, talk, before, "shorter-prefix completion before playback");

            Abandon(host, session, "probe-shorter-completion-cleanup");
            RequireEvidenceUnchanged(host, talk, before, "shorter-prefix completion cleanup");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_STREAM_SHORTER_PASS: prior-boundary=controlled-fixture; queued-at-completion=1; complete-terminal=true; response-chunks=0; mood-transcript-inbox-unchanged=true.");
        }

        private static object BeginCase(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            BreezeTtsTrialBridge bridge, BreezeTtsStreamingPcmAdapter adapter,
            ConversationContextSnapshot snapshot, int memorySession, string personId, string token,
            string previousReply, Action<string> handleAnswer)
        {
            Require(adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Idle ||
                adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled,
                "Probe adapter is not clean before a new case.");
            // Real cancellation releases the bridge on the adapter's next Update.
            // These controlled cases run synchronously, so drive that actual tick
            // before starting another case rather than bypassing the drain guard.
            if (adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled)
                RequireMethod(typeof(BreezeTtsStreamingPcmAdapter), "Update", PrivateInstance).Invoke(adapter, null);
            string streamId;
            Require(bridge.TryBeginStreamingReply(personId, GameManager.Instance.TalkManager.NameNPC,
                token, "unknown", null, null, out streamId) && !string.IsNullOrWhiteSpace(streamId),
                "Isolated local bridge rejected a controlled stream begin.");
            string responseRoot;
            Require(bridge.TryGetStreamingResponseRoot(out responseRoot) &&
                adapter.Begin(bridge, responseRoot, streamId, token, personId),
                "Isolated local adapter rejected the controlled stream binding.");

            Type sessionType = typeof(PeopleOfTheIliacBayMod).GetNestedType(
                "StreamingSocialReplySession", BindingFlags.NonPublic);
            Require(sessionType != null, "Production streaming session type is unavailable.");
            object session = Activator.CreateInstance(sessionType, true);
            SetSessionField(session, "StreamId", streamId);
            SetSessionField(session, "ConversationToken", token);
            SetSessionField(session, "PersonId", personId);
            SetSessionField(session, "PlayerText", "Local streaming duplicate queue probe.");
            SetSessionField(session, "Talk", talk);
            SetSessionField(session, "Snapshot", snapshot);
            SetSessionField(session, "MemorySession", memorySession);
            SetSessionField(session, "ReplyStage", 0L);
            SetSessionField(session, "HandleAnswer", handleAnswer);
            SetSessionField(session, "ResponseRoot", responseRoot);
            SetSessionField(session, "PreviousNpcReply", previousReply);
            SetHostField(host, "streamingSocialReply", session);
            return session;
        }

        private static SocialDialogueStreamParser CreateParser(PeopleOfTheIliacBayMod host, object session)
        {
            MethodInfo accept = RequireMethod(typeof(PeopleOfTheIliacBayMod),
                "AcceptStreamingSocialSentence", PrivateInstance);
            var parser = new SocialDialogueStreamParser();
            parser.SentenceReady += sentence =>
            {
                try
                {
                    object accepted = accept.Invoke(host, new[] { session, (object)sentence });
                    if (!(accepted is bool) || !(bool)accepted)
                        throw new InvalidOperationException("Production streaming accept method rejected a parser segment.");
                }
                catch { throw; }
            };
            return parser;
        }

        private static void Feed(SocialDialogueStreamParser parser, string fragment, bool terminal)
        {
            if (!FeedSseFragment(parser, fragment, terminal))
                throw new InvalidOperationException("Controlled SSE fragment was rejected by the production parser.");
        }

        private static bool FeedSseFragment(SocialDialogueStreamParser parser, string fragment, bool terminal)
        {
            string payload = "{\"choices\":[{\"delta\":{\"content\":\"" + EscapeJson(fragment) + "\"}}]" +
                (terminal ? ",\"stop\":true" : string.Empty) + "}";
            return parser.FeedSseLine("data: " + payload);
        }

        private static void Complete(PeopleOfTheIliacBayMod host, object session, SocialDialogueStreamResult result)
        {
            MethodInfo complete = RequireMethod(typeof(PeopleOfTheIliacBayMod),
                "CompleteStreamingSocialGeneration", PrivateInstance);
            complete.Invoke(host, new object[] { session, result });
        }

        private static void Abandon(PeopleOfTheIliacBayMod host, object session, string reason)
        {
            MethodInfo abandon = RequireMethod(typeof(PeopleOfTheIliacBayMod),
                "AbandonStreamingSocialReply", PrivateInstance);
            abandon.Invoke(host, new object[] { session, reason });
        }

        private static void RequireValidResult(SocialDialogueStreamResult result, string expectedReply,
            int expectedSegments)
        {
            Require(result != null && result.Complete && result.MoodDelta.HasValue &&
                string.Equals(result.Reply, expectedReply, StringComparison.Ordinal) &&
                string.Equals(result.EmittedText, expectedReply, StringComparison.Ordinal) &&
                result.EmittedSegments != null && result.EmittedSegments.Count == expectedSegments,
                "Controlled parser result did not validate the expected reply and segment boundary.");
        }

        private static ConversationEvidence CaptureEvidence(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, WorldStateStore store, string personId)
        {
            double now = DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            ConversationMoodView mood = store.CaptureConversationMood(personId, now);
            int disposition;
            bool dispositionKnown = store.TryGetActualConversationDisposition(personId, out disposition);
            return new ConversationEvidence
            {
                Mood = mood.Value,
                Sequence = mood.AcceptedTurnSequence,
                MoodRevision = mood.Revision,
                DispositionKnown = dispositionKnown,
                Disposition = disposition,
                NativeTranscript = ReadNativeTranscript(talk),
                StoredTranscript = StoredTranscriptFingerprint(store.ConversationTranscript(personId, now, 256)),
                ExperienceInbox = ExperienceInboxFingerprint(store.ReadPendingNpcExperiences(personId, 100))
            };
        }

        private static void RequireEvidenceUnchanged(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, ConversationEvidence before, string label)
        {
            WorldStateStore store = ReadHostField(host, "store") as WorldStateStore;
            string personId = ReadHostField(host, "memoryPersonId") as string;
            Require(store != null && !string.IsNullOrWhiteSpace(personId),
                "Streaming duplicate probe lost the active store/person during " + label + ".");
            ConversationEvidence after = CaptureEvidence(host, talk, store, personId);
            Require(after.Mood == before.Mood && after.Sequence == before.Sequence &&
                after.MoodRevision == before.MoodRevision && after.DispositionKnown == before.DispositionKnown &&
                after.Disposition == before.Disposition &&
                string.Equals(after.NativeTranscript, before.NativeTranscript, StringComparison.Ordinal) &&
                string.Equals(after.StoredTranscript, before.StoredTranscript, StringComparison.Ordinal) &&
                string.Equals(after.ExperienceInbox, before.ExperienceInbox, StringComparison.Ordinal),
                "Mood, accepted sequence, disposition, transcript, or experience inbox changed during " + label + ".");
        }

        private static string ReadNativeTranscript(DaggerfallTalkWindow talk)
        {
            MethodInfo method = RequireMethod(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", PrivateStatic);
            return (string)method.Invoke(null, new object[] { talk });
        }

        private static string StoredTranscriptFingerprint(IList<NpcMemoryEntry> entries)
        {
            var builder = new StringBuilder();
            foreach (NpcMemoryEntry entry in entries)
            {
                AppendValue(builder, entry.At.ToString("R", CultureInfo.InvariantCulture));
                AppendValue(builder, entry.Date);
                AppendValue(builder, entry.Place);
                AppendValue(builder, entry.PlayerText);
                AppendValue(builder, entry.NpcText);
                AppendValue(builder, entry.Observation);
                AppendValue(builder, entry.Source);
            }
            return builder.ToString();
        }

        private static string ExperienceInboxFingerprint(IList<NpcExperienceEvent> events)
        {
            var builder = new StringBuilder();
            foreach (NpcExperienceEvent item in events)
            {
                AppendValue(builder, item.ObserverId);
                AppendValue(builder, item.SubjectPersonId);
                AppendValue(builder, item.ReceiptId);
                AppendValue(builder, item.EpisodeId);
                AppendValue(builder, item.OutcomeId);
                AppendValue(builder, item.UtteranceId);
                AppendValue(builder, item.EventKind.HasValue ? item.EventKind.Value.ToString() : string.Empty);
                AppendValue(builder, item.ClaimedEventKind.HasValue ? item.ClaimedEventKind.Value.ToString() : string.Empty);
                AppendValue(builder, item.Origin.ToString());
                AppendValue(builder, item.ObservedGameTime.ToString("R", CultureInfo.InvariantCulture));
                AppendValue(builder, item.ResponsibleActorAttribution.ToString());
                AppendValue(builder, item.ResponsibleActorId);
                AppendValue(builder, item.AttributedSpeakerId);
                AppendValue(builder, item.ClaimedResponsibleActorId);
                AppendValue(builder, item.DeduplicationKey);
            }
            return builder.ToString();
        }

        private static string RequestDirectory(BreezeTtsTrialBridge bridge, string streamId)
        {
            string root = RequireStreamingRoot(bridge);
            return Path.Combine(Path.GetDirectoryName(root), "requests", streamId);
        }

        private static string RequireStreamingRoot(BreezeTtsTrialBridge bridge)
        {
            string responseRoot;
            Require(bridge.TryGetStreamingResponseRoot(out responseRoot), "Probe bridge response root disappeared.");
            return Path.GetFullPath(responseRoot);
        }

        private static List<string> SentenceFiles(string requestDirectory)
        {
            var result = new List<string>();
            if (!Directory.Exists(requestDirectory)) return result;
            foreach (string path in Directory.GetFiles(requestDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(path);
                if (name.Length == 9 && name.EndsWith(".json", StringComparison.Ordinal) &&
                    char.IsDigit(name[0]) && char.IsDigit(name[1]) && char.IsDigit(name[2]) && char.IsDigit(name[3]))
                    result.Add(path);
            }
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static List<string> ResponseFiles(string root)
        {
            var result = new List<string>();
            string path = Path.Combine(root, "responses");
            if (Directory.Exists(path)) result.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
            return result;
        }

        private static bool FileContainsExactSentence(string path, int sequence, string exactText)
        {
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            return json.IndexOf("\"sequence\":" + sequence.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) >= 0 &&
                json.IndexOf("\"exactText\":" + QuoteJson(exactText), StringComparison.Ordinal) >= 0;
        }

        private static string CreateOwnedTemporaryRoot()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(),
                "peopleoftheiliacbay-social-duplicate-probe-" + Guid.NewGuid().ToString("N")));
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            Require(string.Equals(Path.GetDirectoryName(root).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar), temp, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(root).StartsWith("peopleoftheiliacbay-social-duplicate-probe-", StringComparison.Ordinal),
                "Refusing to create an unbounded streaming probe directory.");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, ".social-duplicate-probe-owned"), "owned local queue probe\n");
            return root;
        }

        private static void DeleteOwnedTemporaryRoot(string root)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            Require(string.Equals(Path.GetDirectoryName(fullRoot).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar), temp, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileName(fullRoot).StartsWith("peopleoftheiliacbay-social-duplicate-probe-", StringComparison.Ordinal) &&
                File.Exists(Path.Combine(fullRoot, ".social-duplicate-probe-owned")),
                "Refusing to remove a directory not proven to belong to this probe.");
            Directory.Delete(fullRoot, true);
        }

        private static HostFieldSnapshot CaptureHostFields(PeopleOfTheIliacBayMod host, string[] names)
        {
            var snapshot = new HostFieldSnapshot();
            foreach (string name in names) snapshot.Values.Add(name, ReadHostField(host, name));
            return snapshot;
        }

        private static void RestoreHostFields(PeopleOfTheIliacBayMod host, HostFieldSnapshot snapshot)
        {
            foreach (KeyValuePair<string, object> pair in snapshot.Values) SetHostField(host, pair.Key, pair.Value);
        }

        private static object ReadHostField(PeopleOfTheIliacBayMod host, string name)
        { return RequireField(typeof(PeopleOfTheIliacBayMod), name, PrivateInstance).GetValue(host); }

        private static void SetHostField(PeopleOfTheIliacBayMod host, string name, object value)
        { RequireField(typeof(PeopleOfTheIliacBayMod), name, PrivateInstance).SetValue(host, value); }

        private static object ReadSessionField(object session, string name)
        { return RequireField(session.GetType(), name, BindingFlags.Instance | BindingFlags.Public).GetValue(session); }

        private static void SetSessionField(object session, string name, object value)
        { RequireField(session.GetType(), name, BindingFlags.Instance | BindingFlags.Public).SetValue(session, value); }

        private static IList SessionList(object session, string name)
        { return (IList)ReadSessionField(session, name); }

        private static FieldInfo RequireField(Type type, string name, BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null) throw new InvalidOperationException("Required probe field is unavailable: " + type.Name + "." + name);
            return field;
        }

        private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags)
        {
            MethodInfo method = type.GetMethod(name, flags);
            if (method == null) throw new InvalidOperationException("Required probe method is unavailable: " + type.Name + "." + name);
            return method;
        }

        private static void SetField(Type type, object instance, string name, object value)
        { RequireField(type, name, PrivateInstance).SetValue(instance, value); }

        private static bool IsTerminalOrIdle(BreezeTtsStreamingPcmAdapter.PlaybackState state)
        {
            return state == BreezeTtsStreamingPcmAdapter.PlaybackState.Idle ||
                state == BreezeTtsStreamingPcmAdapter.PlaybackState.Completed ||
                state == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled ||
                state == BreezeTtsStreamingPcmAdapter.PlaybackState.Failed;
        }

        private static void AppendValue(StringBuilder builder, string value)
        {
            string safe = value ?? string.Empty;
            builder.Append(safe.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(safe).Append('|');
        }

        private static string EscapeJson(string value)
        {
            var builder = new StringBuilder();
            foreach (char character in value ?? string.Empty)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 0x20) builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else builder.Append(character);
                        break;
                }
            }
            return builder.ToString();
        }

        private static string QuoteJson(string value) { return "\"" + EscapeJson(value) + "\""; }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
