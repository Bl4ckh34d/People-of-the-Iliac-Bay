using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>One bounded real-model, production-voice dialogue presentation smoke.</summary>
    internal static partial class LiveDialoguePresentationProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-live-dialogue-presentation-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";
        private const string PlayerText = "Hi, how are you?";
        private const int FaceRecord = 77;
        private const double MinimumPendingSeconds = 1.0;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private const float ScenarioBudgetSeconds = 465f;
        private const float RestoreBudgetSeconds = 90f;

        private sealed class OnsetObservation
        {
            internal BreezeTtsTrialBridge.MixerPlaybackStartReceipt Receipt;
            internal BreezeTtsTrialBridge.ActiveAudioEvidence Evidence;
            internal bool AlignmentPresent;
            internal bool VisemesPresent;
        }

        private sealed class Context
        {
            internal PeopleOfTheIliacBayMod Host;
            internal StreetPopulation Streets;
            internal string Character;
            internal string FixtureSave;
            internal GameManager Game;
            internal WorldStateStore Store;
            internal LocalDialogueClient Client;
            internal BreezeTtsTrialBridge Bridge;
            internal ResidentActor Actor;
            internal DaggerfallTalkWindow Talk;
            internal ListBox Transcript;
            internal string PersonId;
            internal string ConversationToken;
            internal int OriginalFace = -1;
            internal bool FaceChanged;
            internal long BaselineReplyRequests;
            internal long BaselineReplyCompletions;
            internal ConversationMoodView MoodBefore;
            internal readonly List<OnsetObservation> Onsets = new List<OnsetObservation>();
            internal Action<BreezeTtsTrialBridge.MixerPlaybackStartReceipt> OnsetHandler;
            internal double ScenarioDeadline;
            internal bool ScreenshotCaptured;
            internal string ScreenshotPath;
            internal string AnswerText;
            internal string ReplyRequestId;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave && host != null &&
                streets != null && streets.Ready && GameManager.Instance != null && GameManager.Instance.IsReady,
                "Live presentation probe requires its exact opt-in Gothway Garden fixture.");

            var context = new Context { Host = host, Streets = streets, Character = character,
                FixtureSave = fixtureSave, Game = GameManager.Instance };
            int baselineSlot = context.Game.SaveLoadManager.FindSaveFolderByNames(character, fixtureSave);
            Require(baselineSlot >= 0, "Exact live-presentation baseline save is missing.");

            Exception scenarioFailure = null;
            IEnumerator scenario = Flatten(Exercise(context));
            while (true)
            {
                object current;
                Exception error;
                bool moved = TryAdvance(scenario, out current, out error);
                if (error != null) { scenarioFailure = error; break; }
                if (!moved) break;
                yield return current;
            }

            // Always release the selected record and close the owned Talk window before
            // reloading the exact save. The surrounding TownTest also restores settings.
            if (context.OnsetHandler != null && context.Bridge != null)
                context.Bridge.MixerPlaybackStarted -= context.OnsetHandler;
            if (context.Actor && context.Actor.Person && context.FaceChanged)
                context.Actor.Person.PersonFaceRecordId = context.OriginalFace;
            if (DaggerfallUI.Instance != null && DaggerfallUI.UIManager != null &&
                DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
                DaggerfallUI.Instance.PopToHUD();

            Exception closeFailure = null;
            if (Convert.ToBoolean(ReadField(host, "conversationSessionOpen")))
            {
                IEnumerator close = Flatten(ConversationMoodProbe.CloseTalk(host));
                while (true)
                {
                    object current;
                    Exception error;
                    bool moved = TryAdvance(close, out current, out error);
                    if (error != null) { closeFailure = error; break; }
                    if (!moved) break;
                    yield return current;
                }
            }

            Exception restoreFailure = null;
            IEnumerator restore = Flatten(ConversationMoodProbe.LoadSave(host, streets, character, fixtureSave, true));
            double restoreDeadline = Time.realtimeSinceStartup + RestoreBudgetSeconds;
            while (true)
            {
                object current;
                Exception error;
                bool moved = TryAdvance(restore, out current, out error);
                if (error != null) { restoreFailure = error; break; }
                if (!moved) break;
                if (Time.realtimeSinceStartup > restoreDeadline)
                { restoreFailure = new TimeoutException("Exact baseline reload exceeded its bounded deadline."); break; }
                yield return current;
            }
            if (restoreFailure == null)
            {
                Require(DaggerfallUI.UIManager.TopWindow == DaggerfallUI.Instance.DaggerfallHUD &&
                    streets.Ready && context.Game.SaveLoadManager.FindSaveFolderByNames(character, fixtureSave) >= 0,
                    "Live-presentation baseline reload did not leave the exact fixture ready on HUD.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LIVE_DIALOGUE_PRESENTATION_BASELINE_RESTORED: save=" +
                    fixtureSave + "; face-record-restored=" + (!context.FaceChanged || !context.Actor ||
                        !context.Actor.Person || context.Actor.Person.PersonFaceRecordId == context.OriginalFace) + ".");
            }

            if (scenarioFailure != null)
                throw new InvalidOperationException("Live dialogue presentation failed after baseline restoration.", scenarioFailure);
            if (closeFailure != null)
                throw new InvalidOperationException("Live dialogue presentation could not close Talk before baseline restoration.", closeFailure);
            if (restoreFailure != null)
                throw new InvalidOperationException("Live dialogue presentation could not restore the exact baseline save.", restoreFailure);

            if (LatencyProbe)
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LIVE_DIALOGUE_PRESENTATION_PASS: latency-probe=true; baseline-restored=true.");
                yield break;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LIVE_DIALOGUE_PRESENTATION_PASS: person=" + context.PersonId +
                "; reply-request=" + context.ReplyRequestId + "; generation-pending=" +
                "visible-when-at-least-one-second=true; tts-pending=" +
                "visible-until-mixer-when-at-least-one-second=true; mixer=nonzero-receipt; " +
                "alignment=validated-forced-word-timeline; visemes=validated-owner-bound-timeline; " +
                "mouth-shapes=animated; mood=committed-state-projected; opening-greeting=mixed; screenshot=" +
                (context.ScreenshotPath ?? "missing") + ".");
        }

        private static IEnumerator Exercise(Context context)
        {
            PeopleOfTheIliacBayMod host = context.Host;
            GameManager game = context.Game;
            context.Store = ReadField(host, "store") as WorldStateStore;
            context.Client = ReadField(host, "localDialogue") as LocalDialogueClient;
            context.Bridge = ReadField(host, "breezeTtsTrial") as BreezeTtsTrialBridge;
            Require(context.Store != null && context.Client != null && context.Bridge != null &&
                ReadBool(host, "localDialogueEnabled") && ReadBool(host, "npcVoiceEnabled") &&
                !ReadBool(host, "socialSentenceStreamingEnabled") &&
                !ReadBool(host, "dialoguePrewarmEnabled") && ReadBool(host, "animatedPortraitsEnabled") &&
                context.Bridge.VoiceEnabled && !context.Bridge.IsTrialMode &&
                context.Bridge.OutputAudioSource != null &&
                !context.Bridge.HasPendingSpeech && !context.Client.IsBusy,
                "Live presentation requires idle production Gemma/Breeze, free text, voice and animated portraits; " +
                "streaming, prewarm, planner and trial bridge must be off.");

            context.ScenarioDeadline = Time.realtimeSinceStartup + ScenarioBudgetSeconds;
            context.OnsetHandler = receipt => ObserveMixerOnset(context, receipt);
            context.Bridge.MixerPlaybackStarted += context.OnsetHandler;

            // Street schedules become ready before their first native actors are spawned.
            float actorDeadline = Time.realtimeSinceStartup + 30f;
            while (!context.Actor && Time.realtimeSinceStartup < actorDeadline)
            {
                context.Actor = FindFreshNeutralActor(context.Store, game);
                if (!context.Actor) yield return null;
            }
            Require(context.Actor && context.Actor.Person && context.Actor.Resident != null &&
                context.Actor.Resident.IsAlive, "No fresh, unused managed adult is available in the owned town fixture.");
            context.PersonId = context.Actor.Resident.Id;
            context.OriginalFace = context.Actor.Person.PersonFaceRecordId;
            context.Actor.Person.PersonFaceRecordId = FaceRecord;
            context.FaceChanged = true;
            context.MoodBefore = context.Store.CaptureConversationMood(context.PersonId);
            Require(context.MoodBefore.Value == 0 && context.MoodBefore.AcceptedTurnSequence == 0,
                "Selected fresh fixture resident does not have a neutral baseline mood.");

            ConversationMoodProbe.StagePlayerNearActor(context.Streets, context.Actor, game);
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(context.Actor.Person);

            float talkDeadline = (float)Math.Min(context.ScenarioDeadline, Time.realtimeSinceStartup + 30f);
            while (Time.realtimeSinceStartup < talkDeadline)
            {
                context.Talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if (context.Talk != null && context.Talk.IsSetup &&
                    Convert.ToBoolean(ReadField(host, "conversationSessionOpen")) &&
                    ReferenceEquals(ReadField(host, "memoryWindow"), context.Talk) &&
                    string.Equals(Convert.ToString(ReadField(host, "memoryPersonId"), CultureInfo.InvariantCulture),
                        context.PersonId, StringComparison.Ordinal)) break;
                yield return null;
            }
            Require(context.Talk != null && context.Talk.IsSetup && DaggerfallUI.UIManager.ContainsWindow(context.Talk),
                "Native Talk did not bind the selected fresh resident.");
            context.ConversationToken = Convert.ToString(ReadField(host, "conversationContextSessionId"),
                CultureInfo.InvariantCulture);
            context.Transcript = Field(typeof(DaggerfallTalkWindow), "listboxConversation", Instance)
                .GetValue(context.Talk) as ListBox;
            Require(!string.IsNullOrWhiteSpace(context.ConversationToken) && context.Transcript != null,
                "Live Talk session or native transcript binding is missing.");
            Require(!ReadBool(host, "returningGreetingPending"),
                "Fresh actor unexpectedly entered the returning-greeting path.");

            float portraitDeadline = (float)Math.Min(context.ScenarioDeadline, Time.realtimeSinceStartup + 15f);
            SplitPortraitRuntimeOverlay portrait = null;
            while (Time.realtimeSinceStartup < portraitDeadline)
            {
                portrait = ReadField(host, "splitPortraitOverlay") as SplitPortraitRuntimeOverlay;
                if (portrait != null && portrait.IsBound && portrait.HasValidArt &&
                    ReferenceEquals(portrait.Speaker, context.Actor.Person) && portrait.BoundTalk == context.Talk &&
                    portrait.BoundRecord == FaceRecord) break;
                yield return null;
            }
            Require(portrait != null && portrait.IsBound && portrait.HasValidArt &&
                ReferenceEquals(portrait.Speaker, context.Actor.Person) && portrait.BoundTalk == context.Talk &&
                portrait.BoundRecord == FaceRecord,
                "Production split portrait did not bind valid face 77 to the exact Talk speaker.");

            string openingRequestId = null;
            float openingBindingDeadline = (float)Math.Min(context.ScenarioDeadline, Time.realtimeSinceStartup + 25f);
            while (Time.realtimeSinceStartup < openingBindingDeadline && string.IsNullOrEmpty(openingRequestId))
            {
                openingRequestId = FindTranscriptRequest(host, context.Talk, context.Transcript,
                    context.ConversationToken, context.PersonId, context.Bridge, null, out _);
                if (string.IsNullOrEmpty(openingRequestId)) yield return null;
            }
            Require(!string.IsNullOrEmpty(openingRequestId),
                "Fresh native opening greeting did not enqueue its production voice request.");
            string openingPerson, openingToken, openingText;
            Require(context.Bridge.TryReadRequestBinding(openingRequestId, out openingPerson,
                out openingToken, out openingText) && openingPerson == context.PersonId &&
                openingToken == context.ConversationToken && !string.IsNullOrWhiteSpace(openingText),
                "Opening TTS request is not bound to its exact person, conversation and greeting text.");

            yield return WaitForCompletedMixerRequest(context, openingRequestId, openingToken,
                openingPerson, openingText, 300f, "opening native greeting");
            Require(!context.Bridge.HasPendingSpeech && !context.Bridge.OutputAudioSource.isPlaying,
                "Opening greeting did not finish before the typed player turn.");

            if (LatencyProbe)
            {
                yield return ExerciseSpeechLatency(context);
                yield break;
            }
            context.BaselineReplyRequests = ConversationMoodProbe.ReadLongProperty(context.Client, "ReplyRequestCount");
            context.BaselineReplyCompletions = ConversationMoodProbe.ReadLongProperty(context.Client, "ReplyCompletionCount");
            long submitTicks = Stopwatch.GetTimestamp();
            Method(host.GetType(), "SubmitFreeText", 2).Invoke(host, new object[] { context.Talk, PlayerText });

            bool generationPendingVisible = false;
            long completionTicks = 0;
            double modelDeadline = Math.Min(context.ScenarioDeadline, Time.realtimeSinceStartup + 180f);
            while (ConversationMoodProbe.ReadLongProperty(context.Client, "ReplyCompletionCount") <=
                context.BaselineReplyCompletions && Time.realtimeSinceStartup < modelDeadline)
            {
                if (ElapsedSeconds(submitTicks) >= MinimumPendingSeconds &&
                    !generationPendingVisible && PendingRowsVisible(host, context.Talk, context.Transcript, PlayerText))
                    generationPendingVisible = true;
                yield return null;
            }
            completionTicks = Stopwatch.GetTimestamp();
            Require(ConversationMoodProbe.ReadLongProperty(context.Client, "ReplyRequestCount") ==
                    context.BaselineReplyRequests + 1 &&
                ConversationMoodProbe.ReadLongProperty(context.Client, "ReplyCompletionCount") ==
                    context.BaselineReplyCompletions + 1,
                "One fresh non-streamed Qwen reply did not complete for the exact typed turn.");
            double generationSeconds = ElapsedSeconds(submitTicks, completionTicks);
            if (!generationPendingVisible && generationSeconds >= MinimumPendingSeconds &&
                PendingRowsVisible(host, context.Talk, context.Transcript, PlayerText))
                generationPendingVisible = true;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LIVE_PENDING_GENERATION: seconds=" + generationSeconds +
                "; visible=" + generationPendingVisible + "; tracking=" + ReadBool(host, "conversationPendingTracking") +
                "; suppressed=" + ReadBool(host, "conversationPendingSuppressed") +
                "; rendered=" + ReadBool(host, "conversationPendingRequestSent") +
                "; busy=" + ReadBool(host, "conversationBusy") + "; captured-player=" +
                ReadString(host, "conversationContextCapturedPlayerText") + ".");

            object pending = ReadField(host, "pendingAcceptedSpeechReveal");
            float acceptedSpeechDeadline = (float)Math.Min(context.ScenarioDeadline, Time.realtimeSinceStartup + 12f);
            while (pending == null && Time.realtimeSinceStartup < acceptedSpeechDeadline)
            {
                if (!ReadBool(host, "conversationBusy")) break;
                yield return null;
                pending = ReadField(host, "pendingAcceptedSpeechReveal");
            }
            Require(pending != null, "Accepted live social reply did not enter the production pending-speech gate.");

            context.ReplyRequestId = ReadString(pending, "RequestId");
            string replyToken = ReadString(pending, "ConversationToken");
            string replyPerson = ReadString(pending, "PersonId");
            context.AnswerText = ReadString(pending, "ExactText");
            string boundPerson, boundToken, boundText;
            Require(!string.IsNullOrWhiteSpace(context.ReplyRequestId) && replyToken == context.ConversationToken &&
                replyPerson == context.PersonId && !string.IsNullOrWhiteSpace(context.AnswerText) &&
                context.Bridge.TryReadRequestBinding(context.ReplyRequestId, out boundPerson, out boundToken, out boundText) &&
                boundPerson == context.PersonId && boundToken == context.ConversationToken &&
                boundText == context.AnswerText && ReadString(pending, "PlayerText") == PlayerText,
                "Accepted speech request is not bound to this exact typed turn, selected resident and conversation.");

            long speechQueuedTicks = Stopwatch.GetTimestamp();
            bool ttsPendingVisible = false;
            bool observedOnset = false;
            bool forcedWordReveal = false;
            bool validatedViseme = false;
            bool capturedMoodProjected = false;
            bool screenshotCaptured = false;
            var mouthShapes = new HashSet<PortraitMouthShape>();
            var eyePhases = new HashSet<SplitPortraitEyePhase>();
            int portraitUploadsBefore = portrait.TextureUploadCount;
            float responseDeadline = (float)Math.Min(context.ScenarioDeadline, Time.realtimeSinceStartup + 150f);
            while (Time.realtimeSinceStartup < responseDeadline)
            {
                OnsetObservation onset = FindOnset(context, context.ReplyRequestId);
                if (onset != null)
                {
                    observedOnset = true;
                    Require(onset.Receipt.ConversationToken == context.ConversationToken &&
                        onset.Receipt.PersonId == context.PersonId && onset.Receipt.ExactText == context.AnswerText &&
                        onset.Receipt.FirstNonZeroMixerTicks > 0 && onset.Receipt.FirstNonZeroMixerFrame >= 0,
                        "Reply mixer-onset receipt does not match the exact generated speech binding.");
                    Require(onset.AlignmentPresent && onset.VisemesPresent,
                        "Request-bound alignment and viseme sidecars were not present at the first nonzero mixer receipt.");

                    if (!ttsPendingVisible && ElapsedSeconds(speechQueuedTicks,
                        onset.Receipt.FirstNonZeroMixerTicks) >= MinimumPendingSeconds)
                        ttsPendingVisible = PendingRowsVisible(host, context.Talk, context.Transcript, PlayerText);
                    if (ElapsedSeconds(speechQueuedTicks, onset.Receipt.FirstNonZeroMixerTicks) >= MinimumPendingSeconds)
                        Require(ttsPendingVisible,
                            "TTS preparation lasted at least one second without retaining the purple pending line until mixer onset.");
                }

                pending = ReadField(host, "pendingAcceptedSpeechReveal");
                if (pending != null && ElapsedSeconds(speechQueuedTicks) >= MinimumPendingSeconds &&
                    PendingRowsVisible(host, context.Talk, context.Transcript, PlayerText))
                    ttsPendingVisible = true;

                ListBox.ListItem answerRow = FindReplyRow(context);
                StreamingWordHighlightOverlay wordOverlay = FindReplyOverlay(host, context.Talk,
                    context.Transcript, context.ReplyRequestId, out answerRow);
                BreezeTtsTrialBridge.ActiveAudioEvidence evidence;
                bool activeEvidence = context.Bridge.TryReadActiveAudioEvidence(out evidence) && evidence != null &&
                    evidence.RequestId == context.ReplyRequestId && evidence.PersonId == context.PersonId &&
                    evidence.ConversationToken == context.ConversationToken && evidence.Text == context.AnswerText;
                AudioSource audio = context.Bridge.OutputAudioSource;
                if (activeEvidence && audio != null && audio.isPlaying && wordOverlay != null && answerRow != null)
                {
                    string progressSource = ReadString(wordOverlay, "currentProgressSource");
                    int textEnd = ReadInt(wordOverlay, "currentCumulativeTextEnd");
                    int wordStart = ReadInt(wordOverlay, "currentWordStartUtf16");
                    int blueGlyphs = ReadInt(wordOverlay, "lastDrawnBlueGlyphCount");
                    long alignedSample = ReadLong(wordOverlay, "fullWavLastObservedSampleFrame");
                    string loadedTimelineRequest = ReadString(wordOverlay, "fullWavTimelineRequestId");
                    var timeline = ReadField(wordOverlay, "fullWavTimeline") as PortraitWordAlignmentTimeline;
                    if (loadedTimelineRequest == context.ReplyRequestId && timeline != null &&
                        timeline.TimelineKind == "forced-word-alignment" && timeline.Text == context.AnswerText &&
                        progressSource == "forced-word-alignment" && alignedSample > 0 && textEnd > 0 &&
                        textEnd < context.AnswerText.Length && wordStart >= 0 && textEnd > wordStart && blueGlyphs > 0)
                        forcedWordReveal = true;

                    SplitPortraitSpeechSource speechSource = ReadField(host, "splitPortraitSpeech") as SplitPortraitSpeechSource;
                    SplitPortraitSpeechRead speech = speechSource == null
                        ? default(SplitPortraitSpeechRead) : speechSource.Read(context.PersonId, context.ConversationToken);
                    if (speech.IsPlaying && speech.RequestId == context.ReplyRequestId &&
                        speech.HasValidatedTimeline && speech.MouthShape != PortraitMouthShape.X)
                    {
                        validatedViseme = true;
                        object presentation = ReadField(host, "lastPortraitPresentation");
                        if (presentation != null && ReferenceEquals(ReadField(host, "splitPortraitSpeechBridge"), context.Bridge))
                        {
                            PortraitMouthShape presentedMouth = (PortraitMouthShape)ReadField(presentation, "MouthShape");
                            if (presentedMouth == speech.MouthShape) mouthShapes.Add(presentedMouth);
                            eyePhases.Add((SplitPortraitEyePhase)ReadField(presentation, "EyePhase"));
                            if (CapturedEmotionMatches(host, context, presentation)) capturedMoodProjected = true;
                        }

                        if (!screenshotCaptured && forcedWordReveal && capturedMoodProjected)
                        {
                            yield return new WaitForEndOfFrame();
                            string captureDirectory = Path.Combine(Application.persistentDataPath,
                                "live-dialogue-presentation-captures");
                            Directory.CreateDirectory(captureDirectory);
                            context.ScreenshotPath = Path.Combine(captureDirectory,
                                "reply-" + context.ReplyRequestId + ".png");
                            ScreenCapture.CaptureScreenshot(context.ScreenshotPath);
                            screenshotCaptured = true;
                            context.ScreenshotCaptured = true;
                            float captureDeadline = Time.realtimeSinceStartup + 1.5f;
                            while (!File.Exists(context.ScreenshotPath) && Time.realtimeSinceStartup < captureDeadline)
                                yield return null;
                            Require(File.Exists(context.ScreenshotPath),
                                "Screenshot requested during actual speech was not written to persistent storage.");
                        }
                    }
                }

                BreezeTtsTrialBridge.RequestState state = context.Bridge.GetRequestState(context.ReplyRequestId);
                if (state == BreezeTtsTrialBridge.RequestState.Failed ||
                    state == BreezeTtsTrialBridge.RequestState.Cancelled)
                    throw new InvalidOperationException("Accepted reply TTS reached terminal state " + state + ".");
                if (state == BreezeTtsTrialBridge.RequestState.Completed) break;
                yield return null;
            }

            Require(observedOnset && FindOnset(context, context.ReplyRequestId) != null &&
                context.Bridge.GetRequestState(context.ReplyRequestId) == BreezeTtsTrialBridge.RequestState.Completed,
                "Accepted reply did not finish actual nonzero mixer playback within the bounded deadline.");
            double ttsSeconds = OnsetElapsed(context, context.ReplyRequestId, speechQueuedTicks);
            if (ttsSeconds >= MinimumPendingSeconds)
                Require(ttsPendingVisible, "TTS pending row was not visible through the full preparation interval.");
            Require(ReadField(host, "pendingAcceptedSpeechReveal") == null &&
                !ReadBool(host, "conversationPendingSpeechReveal") && !ReadBool(host, "conversationBusy"),
                "Mixer completion did not clear the accepted-speech and conversation pending gates.");

            ConversationMoodView moodAfter = context.Store.CaptureConversationMood(context.PersonId);
            if (generationSeconds >= MinimumPendingSeconds)
                Require(generationPendingVisible,
                    "LLM generation lasted at least one second without the visible purple pending line.");
            Require(moodAfter.AcceptedTurnSequence == context.MoodBefore.AcceptedTurnSequence + 1,
                "The generated live reply did not commit exactly one persistent social mood turn.");
            Require(forcedWordReveal && validatedViseme && capturedMoodProjected && screenshotCaptured &&
                mouthShapes.Count >= 2 && eyePhases.Count >= 1 &&
                portrait.TextureUploadCount > portraitUploadsBefore,
                "Live reply did not show validated aligned words, owner-bound animated portrait/mood, and an in-speech screenshot. " +
                "alignment=" + forcedWordReveal + "; viseme=" + validatedViseme + "; mood=" + capturedMoodProjected +
                "; screenshot=" + screenshotCaptured + "; mouth-shapes=" + mouthShapes.Count +
                "; eye-phases=" + eyePhases.Count + "; uploads=" + (portrait.TextureUploadCount - portraitUploadsBefore) + ".");

            string history = ReadHistory(context.Talk);
            Require(history.Contains("Player: " + PlayerText) && history.Contains("NPC: " + context.AnswerText) &&
                Count(history, "NPC: " + context.AnswerText) == 1,
                "Mixer onset did not reveal exactly one canonical player/reply exchange.");
        }

        private static ResidentActor FindFreshNeutralActor(WorldStateStore store, GameManager game)
        {
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            string excluded = null;
            for (int attempt = 0; attempt < 512; attempt++)
            {
                ResidentActor actor = ConversationMoodProbe.FindUnusedManagedAdult(store,
                    game.PlayerMotor.transform.position, true, excluded);
                if (!actor || actor.Resident == null) return null;
                excluded = actor.Resident.Id;
                if (store.ConversationTranscript(actor.Resident.Id, now).Count == 0)
                    return actor;
            }
            return null;
        }

        private static void ObserveMixerOnset(Context context,
            BreezeTtsTrialBridge.MixerPlaybackStartReceipt receipt)
        {
            if (receipt == null) return;
            var observation = new OnsetObservation { Receipt = receipt };
            BreezeTtsTrialBridge.ActiveAudioEvidence evidence;
            if (context.Bridge != null && context.Bridge.TryReadActiveAudioEvidence(out evidence) && evidence != null &&
                evidence.RequestId == receipt.RequestId && evidence.PersonId == receipt.PersonId &&
                evidence.ConversationToken == receipt.ConversationToken && evidence.Text == receipt.ExactText)
            {
                observation.Evidence = evidence;
                observation.AlignmentPresent = File.Exists(Path.ChangeExtension(evidence.WavePath, ".alignment.json"));
                observation.VisemesPresent = File.Exists(Path.ChangeExtension(evidence.WavePath, ".visemes.json"));
            }
            context.Onsets.Add(observation);
        }

        private static IEnumerator WaitForCompletedMixerRequest(Context context, string requestId,
            string token, string personId, string exactText, float maximumSeconds, string description)
        {
            double deadline = Math.Min(context.ScenarioDeadline, Time.realtimeSinceStartup + maximumSeconds);
            while (Time.realtimeSinceStartup < deadline)
            {
                BreezeTtsTrialBridge.RequestState state = context.Bridge.GetRequestState(requestId);
                if (state == BreezeTtsTrialBridge.RequestState.Completed)
                {
                    OnsetObservation onset = FindOnset(context, requestId);
                    Require(onset != null && onset.Receipt.ConversationToken == token &&
                        onset.Receipt.PersonId == personId && onset.Receipt.ExactText == exactText &&
                        onset.Receipt.FirstNonZeroMixerTicks > 0 && onset.Receipt.FirstNonZeroMixerFrame >= 0,
                        description + " never produced a bound nonzero mixer receipt.");
                    yield break;
                }
                if (state == BreezeTtsTrialBridge.RequestState.Failed ||
                    state == BreezeTtsTrialBridge.RequestState.Cancelled)
                    throw new InvalidOperationException(description + " TTS ended in terminal state " + state + ".");
                yield return null;
            }
            throw new TimeoutException(description + " did not finish within " + maximumSeconds.ToString("0", CultureInfo.InvariantCulture) + " seconds.");
        }

        private static string FindTranscriptRequest(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            ListBox transcript, string token, string personId, BreezeTtsTrialBridge bridge, string exactText,
            out ListBox.ListItem boundRow)
        {
            boundRow = null;
            IDictionary byTranscript = ReadField(host, "trialTranscriptBindings") as IDictionary;
            if (byTranscript == null || transcript == null || !byTranscript.Contains(transcript)) return null;
            object binding = byTranscript[transcript];
            if (binding == null || !ReferenceEquals(ReadField(binding, "TalkWindow"), talk) ||
                ReadString(binding, "ConversationToken") != token) return null;
            IDictionary rows = ReadField(binding, "RequestByRow") as IDictionary;
            if (rows == null) return null;
            for (int index = 0; index < transcript.Count; index++)
            {
                ListBox.ListItem row = transcript.GetItem(index);
                if (row == null || row.textLabel == null || row.textColor == DaggerfallUI.DaggerfallQuestionTextColor ||
                    row.textLabel.Name == "PeopleOfTheIliacBayHistoryDay" ||
                    row.textLabel.Name == "PeopleOfTheIliacBayPendingEphemeral" ||
                    !rows.Contains(row)) continue;
                string requestId = Convert.ToString(rows[row], CultureInfo.InvariantCulture);
                string boundPerson, boundToken, boundText;
                if (!bridge.TryReadRequestBinding(requestId, out boundPerson, out boundToken, out boundText) ||
                    boundPerson != personId || boundToken != token || boundText != row.textLabel.Text ||
                    (exactText != null && exactText != boundText)) continue;
                boundRow = row;
                return requestId;
            }
            return null;
        }

        private static bool PendingRowsVisible(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            ListBox transcript, string playerText)
        {
            ListBox.ListItem player = ReadField(host, "conversationPendingPlayerRow") as ListBox.ListItem;
            ListBox.ListItem pending = ReadField(host, "conversationPendingRow") as ListBox.ListItem;
            if (player == null || pending == null || player.textLabel == null || pending.textLabel == null ||
                player.textLabel.Text != playerText || string.IsNullOrWhiteSpace(pending.textLabel.Text) ||
                pending.textColor != PeopleOfTheIliacBayMod.ConversationPendingTextColor ||
                !ContainsRow(transcript, player) || !ContainsRow(transcript, pending) || talk != DaggerfallUI.UIManager.TopWindow)
                return false;
            return IndexOfRow(transcript, player) < IndexOfRow(transcript, pending);
        }

        private static ListBox.ListItem FindReplyRow(Context context)
        {
            for (int index = 0; index < context.Transcript.Count; index++)
            {
                ListBox.ListItem row = context.Transcript.GetItem(index);
                if (row == null || row.textLabel == null || row.textColor == DaggerfallUI.DaggerfallQuestionTextColor) continue;
                string canonical = Convert.ToString(Method(typeof(PeopleOfTheIliacBayMod),
                    "CanonicalTranscriptText", 1, false).Invoke(null, new object[] { row }), CultureInfo.InvariantCulture);
                if (canonical == context.AnswerText) return row;
            }
            return null;
        }

        private static StreamingWordHighlightOverlay FindReplyOverlay(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, ListBox transcript, string requestId, out ListBox.ListItem answerRow)
        {
            answerRow = null;
            if (string.IsNullOrEmpty(requestId) || transcript == null) return null;
            IDictionary byTranscript = ReadField(host, "trialTranscriptBindings") as IDictionary;
            if (byTranscript == null || !byTranscript.Contains(transcript)) return null;
            object binding = byTranscript[transcript];
            if (binding == null || !ReferenceEquals(ReadField(binding, "TalkWindow"), talk)) return null;
            IDictionary requests = ReadField(binding, "RequestByRow") as IDictionary;
            IDictionary highlights = ReadField(binding, "HighlightByRow") as IDictionary;
            if (requests == null || highlights == null) return null;
            for (int index = 0; index < transcript.Count; index++)
            {
                ListBox.ListItem row = transcript.GetItem(index);
                if (row == null || !requests.Contains(row) ||
                    !string.Equals(Convert.ToString(requests[row], CultureInfo.InvariantCulture), requestId,
                        StringComparison.Ordinal)) continue;
                answerRow = row;
                return highlights[row] as StreamingWordHighlightOverlay;
            }
            return null;
        }

        private static bool CapturedEmotionMatches(PeopleOfTheIliacBayMod host, Context context, object presentation)
        {
            IDictionary captures = ReadField(host, "speechPresentationByRequest") as IDictionary;
            if (captures == null || !captures.Contains(context.ReplyRequestId)) return false;
            object captured = captures[context.ReplyRequestId];
            NpcPresentationState state = captured == null ? null : ReadField(captured, "State") as NpcPresentationState;
            if (state == null || state.PersonId != context.PersonId ||
                state.Mood != context.Store.CaptureConversationMood(context.PersonId).Value) return false;
            int acuteFear = (int)Math.Round(state.AcuteFearIntensity, MidpointRounding.AwayFromZero);
            var expected = SplitPortraitEmotionProjector.ToManifestId(SplitPortraitEmotionProjector.Project(
                new SplitPortraitEmotionInput(state.PersonId, true, state.Mood,
                    state.DispositionKnown, state.Disposition, state.TrustKnown, state.Trust,
                    state.PlayerFearKnown, state.PlayerFear, state.RelationshipKnown, state.Resentment,
                    state.AcuteFearKnown, acuteFear)));
            return string.Equals(ReadString(presentation, "EmotionId"), expected, StringComparison.Ordinal);
        }

        private static OnsetObservation FindOnset(Context context, string requestId)
        {
            for (int index = 0; index < context.Onsets.Count; index++)
                if (context.Onsets[index] != null && context.Onsets[index].Receipt != null &&
                    context.Onsets[index].Receipt.RequestId == requestId) return context.Onsets[index];
            return null;
        }

        private static double OnsetElapsed(Context context, string requestId, long startTicks)
        {
            OnsetObservation onset = FindOnset(context, requestId);
            return onset == null ? 0 : ElapsedSeconds(startTicks, onset.Receipt.FirstNonZeroMixerTicks);
        }

        private static string ReadHistory(DaggerfallTalkWindow talk)
        {
            MethodInfo method = Method(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", 1, false);
            return Convert.ToString(method.Invoke(null, new object[] { talk }), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static int Count(string source, string value)
        {
            int count = 0, start = 0;
            while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
            { count++; start += value.Length; }
            return count;
        }

        private static bool ContainsRow(ListBox transcript, ListBox.ListItem target)
        { return IndexOfRow(transcript, target) >= 0; }

        private static int IndexOfRow(ListBox transcript, ListBox.ListItem target)
        {
            if (transcript == null || target == null) return -1;
            for (int index = 0; index < transcript.Count; index++)
                if (ReferenceEquals(transcript.GetItem(index), target)) return index;
            return -1;
        }

        private static double ElapsedSeconds(long startTicks)
        { return ElapsedSeconds(startTicks, Stopwatch.GetTimestamp()); }

        private static double ElapsedSeconds(long startTicks, long endTicks)
        { return Math.Max(0, (endTicks - startTicks) / (double)Stopwatch.Frequency); }

        private static bool TryAdvance(IEnumerator iterator, out object current, out Exception error)
        {
            current = null;
            error = null;
            try
            {
                if (iterator == null || !iterator.MoveNext()) return false;
                current = iterator.Current;
                return true;
            }
            catch (Exception exception) { error = exception; return false; }
        }

        private static IEnumerator Flatten(IEnumerator root)
        {
            var stack = new Stack<IEnumerator>();
            if (root != null) stack.Push(root);
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator currentEnumerator = stack.Peek();
                    object current;
                    Exception error;
                    bool moved = TryAdvance(currentEnumerator, out current, out error);
                    if (error != null) throw error;
                    if (!moved)
                    {
                        stack.Pop();
                        IDisposable completed = currentEnumerator as IDisposable;
                        if (completed != null) completed.Dispose();
                        continue;
                    }
                    IEnumerator nested = current as IEnumerator;
                    if (nested != null) stack.Push(nested);
                    else yield return current;
                }
            }
            finally
            {
                while (stack.Count > 0)
                {
                    IDisposable disposable = stack.Pop() as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
            }
        }

        private static FieldInfo Field(Type type, string name, BindingFlags flags = Instance)
        {
            FieldInfo field = type == null ? null : type.GetField(name, flags);
            if (field == null) throw new InvalidOperationException("Live presentation probe field missing: " + name);
            return field;
        }

        private static object ReadField(object target, string name)
        { return target == null ? null : Field(target.GetType(), name).GetValue(target); }

        private static bool ReadBool(object target, string name)
        { return Convert.ToBoolean(ReadField(target, name), CultureInfo.InvariantCulture); }

        private static string ReadString(object target, string name)
        { return Convert.ToString(ReadField(target, name), CultureInfo.InvariantCulture); }

        private static int ReadInt(object target, string name)
        { return Convert.ToInt32(ReadField(target, name), CultureInfo.InvariantCulture); }

        private static long ReadLong(object target, string name)
        { return Convert.ToInt64(ReadField(target, name), CultureInfo.InvariantCulture); }

        private static MethodInfo Method(Type type, string name, int parameterCount, bool instance = true)
        {
            BindingFlags flags = instance ? Instance : Static;
            MethodInfo[] methods = type.GetMethods(flags);
            for (int index = 0; index < methods.Length; index++)
                if (methods[index].Name == name && methods[index].GetParameters().Length == parameterCount)
                    return methods[index];
            throw new InvalidOperationException("Live presentation probe method missing: " + name);
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
