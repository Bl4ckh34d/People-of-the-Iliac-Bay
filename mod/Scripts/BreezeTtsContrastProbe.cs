using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Opt-in controlled same-resident TTS contrast; sends fixed text without Qwen or NPC state writes.</summary>
    internal static class BreezeTtsContrastProbe
    {
        private const string ManifestFlag = "-peopleoftheiliacbay-tts-contrast-manifest";
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";
        private const string SampleKind = "controlled-voice-contrast";
        private const string FixedText = "You came back. I was beginning to wonder whether I would see you again.";
        private static readonly string[] CaseLabels =
        {
            "neutral", "positiveMood", "lowMood", "highFear", "highResentment", "whisper"
        };

        private sealed class Manifest
        {
            public int schemaVersion = 0;
            public string personId = null;
            public string fixedText = null;
            public string manifestKind = null;
            public string sampleKind = null;
            public string sourceKind = null;
            public string status = null;
            public string subjectName = null;
            public string expectedProfileId = null;
            public int voiceSeed = 0;
            public string referencePath = null;
            public string referenceSha256 = null;
            public string baseTraitsSha256 = null;
            public Case[] cases = null;
            public FullSerializer.fsData baseContext = null;
        }

        private sealed class Case
        {
            public string label = null;
            public int mood = 0;
            public int fear = 0;
            public int resentment = 0;
            public ControlledDirection controlledDirection = null;
        }

        private sealed class ControlledDirection
        {
            public string emotion = null;
            public string deliveryMode = null;
        }

        [Serializable]
        private sealed class PlaybackMeasurement
        {
            public int schemaVersion = 0;
            public string requestId = null;
            public string conversationToken = null;
            public string personId = null;
            public string status = null;
            public long textReadyMonotonicTicks = 0;
            public long playbackStartMonotonicTicks = 0;
            public long timestampFrequency = 0;
            public long mixerCallbackFrames = 0;
            public long mixerCallbackSamples = 0;
            public long nonZeroOutputSamples = 0;
            public long peakOutputAmplitudeMicrounits = 0;
            public int expectedSampleFrames = 0;
            public int sampleRate = 0;
            public int channels = 0;
            public long playbackEndMonotonicTicks = 0;
            public long expectedPlaybackMilliseconds = 0;
            public long observedPlaybackMilliseconds = 0;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            string manifestPath = ReadManifestPath(Environment.GetCommandLineArgs());
            ConversationMoodProbe.Require(!string.IsNullOrEmpty(manifestPath),
                "TTS contrast requires -peopleoftheiliacbay-tts-contrast-manifest and an absolute manifest path.");
            ConversationMoodProbe.Require(character == OwnedCharacter && fixtureSave == BaselineSave,
                "TTS contrast requires the owned disposable character and exact Gothway Garden fixture.");
            ConversationMoodProbe.Require(host && streets && streets.Ready && GameManager.Instance && GameManager.Instance.IsReady,
                "TTS contrast requires the ready owned native fixture.");

            Manifest manifest = ReadManifest(manifestPath);
            BreezeTtsTrialBridge bridge = ConversationMoodProbe.ReadHostField(host, "breezeTtsTrial") as BreezeTtsTrialBridge;
            ConversationMoodProbe.Require(bridge && bridge.PublishedRequestCount == 0 && !bridge.HasPendingSpeech,
                "TTS contrast requires a fresh empty trial directory.");
            GameManager game = GameManager.Instance;
            ConversationMoodProbe.Require(game.SaveLoadManager.FindSaveFolderByNames(character, fixtureSave) >= 0,
                "Exact TTS contrast baseline fixture is missing.");

            ResidentActor actor = streets.FindActor(manifest.personId);
            ConversationMoodProbe.Require(actor && actor.Resident != null && actor.Resident.IsAlive &&
                actor.Resident.Id == manifest.personId && actor.Person,
                "Manifest personId does not resolve to the live matched resident in the owned fixture.");
            Vector3 originalPlayerPosition = game.PlayerMotor.transform.position;
            try { ConversationMoodProbe.StagePlayerNearActor(streets, actor, game); }
            catch
            {
                game.PlayerMotor.transform.position = originalPlayerPosition;
                Physics.SyncTransforms();
                throw;
            }

            bool originalGreetingPending = Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,
                "returningGreetingPending"), CultureInfo.InvariantCulture);
            float originalGreetingRetryAt = Convert.ToSingle(ConversationMoodProbe.ReadHostField(host,
                "returningGreetingRetryAt"), CultureInfo.InvariantCulture);
            DaggerfallTalkWindow talk = null;
            string sessionId = null;
            Exception failure = null;
            IEnumerator work = RunTalkSession(host, game, bridge, actor, manifest,
                value => talk = value, value => sessionId = value);
            while (true)
            {
                bool more = false;
                object current = null;
                try { more = work.MoveNext(); if (more) current = work.Current; }
                catch (Exception error) { failure = error; break; }
                if (!more) break;
                yield return current;
            }
            ConversationMoodProbe.Dispose(work, ref failure);

            Exception closeFailure = null;
            if (bridge && !string.IsNullOrEmpty(sessionId)) bridge.EndConversation(sessionId);
            if (talk != null || DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
            {
                IEnumerator close = ConversationMoodProbe.CloseTalk(host);
                while (true)
                {
                    bool more = false;
                    object current = null;
                    try { more = close.MoveNext(); if (more) current = close.Current; }
                    catch (Exception error) { closeFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
                ConversationMoodProbe.Dispose(close, ref closeFailure);
            }
            game.PlayerMotor.transform.position = originalPlayerPosition;
            Physics.SyncTransforms();
            typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingPending",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(host, originalGreetingPending);
            typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingRetryAt",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(host, originalGreetingRetryAt);

            if (failure != null) throw new InvalidOperationException("Native controlled TTS contrast failed; no NPC state was written.", failure);
            if (closeFailure != null) throw new InvalidOperationException("Native controlled TTS contrast could not close Talk cleanly.", closeFailure);
            ConversationMoodProbe.Require(bridge.PublishedRequestCount == CaseLabels.Length + 1 && !bridge.HasPendingSpeech,
                "TTS contrast did not complete one native opening greeting and six sequential contrast playback requests.");
            Debug.Log("BREEZE_TTS_NATIVE_CONTROLLED_CONTRAST_PASS: source=fixed-manifest-text; person=" + manifest.personId +
                "; native-opening-greeting=1; contrast-cases=6; same-person=true; same-text=true; profile=" + manifest.expectedProfileId +
                "; unity-playback-completion-and-nonzero-output-verified=true; dialogue-model-called=false; " +
                "saved-social-state-written=false.");
        }

        private static IEnumerator RunTalkSession(PeopleOfTheIliacBayMod host, GameManager game,
            BreezeTtsTrialBridge bridge, ResidentActor actor, Manifest manifest,
            Action<DaggerfallTalkWindow> setTalk, Action<string> setSession)
        {
            DaggerfallTalkWindow talk = null;
            Exception openingFailure = null;
            IEnumerator opening = ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
            while (true)
            {
                bool more = false;
                object current = null;
                try { more = opening.MoveNext(); if (more) current = opening.Current; }
                catch (Exception error) { openingFailure = error; break; }
                if (!more) break;
                yield return current;
            }
            ConversationMoodProbe.Dispose(opening, ref openingFailure);
            if (openingFailure != null) throw new InvalidOperationException("Native Talk could not open for manifest resident.", openingFailure);
            ConversationMoodProbe.Require(talk != null, "Native Talk did not open for manifest resident.");
            setTalk(talk);
            string sessionId = Convert.ToString(ConversationMoodProbe.ReadHostField(host,
                "conversationContextSessionId"), CultureInfo.InvariantCulture);
            ConversationMoodProbe.Require(!string.IsNullOrWhiteSpace(sessionId) &&
                Convert.ToString(ConversationMoodProbe.ReadHostField(host, "conversationPersonId"), CultureInfo.InvariantCulture) ==
                manifest.personId, "Native conversation did not bind the manifest resident.");
            ConversationMoodProbe.Require(GameManager.Instance.TalkManager.NameNPC == manifest.subjectName,
                "Manifest resident display name does not match the bound native Talk actor.");
            LocalDialogueClient dialogue = ConversationMoodProbe.ReadHostField(host, "localDialogue") as LocalDialogueClient;
            ConversationMoodProbe.Require(dialogue != null &&
                !Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host, "returningGreetingPending"), CultureInfo.InvariantCulture),
                "Native Talk opening did not suppress the delayed returning-greeting retry.");
            setSession(sessionId);

            IEnumerator greeting = DrainNativeGreeting(host, bridge, dialogue, actor, sessionId);
            Exception greetingFailure = null;
            while (true)
            {
                bool more = false;
                object current = null;
                try { more = greeting.MoveNext(); if (more) current = greeting.Current; }
                catch (Exception error) { greetingFailure = error; break; }
                if (!more) break;
                yield return current;
            }
            ConversationMoodProbe.Dispose(greeting, ref greetingFailure);
            if (greetingFailure != null) throw new InvalidOperationException("Native opening greeting did not finish cleanly.", greetingFailure);

            BreezeTtsTrialDeliveryContext baseline = CaptureLiveDeliveryContext(host);
            ValidateManifestBaseline(manifest, baseline);
            IEnumerator cases = RunCases(host, bridge, actor, sessionId, baseline, manifest);
            Exception casesFailure = null;
            while (true)
            {
                bool more = false;
                object current = null;
                try { more = cases.MoveNext(); if (more) current = cases.Current; }
                catch (Exception error) { casesFailure = error; break; }
                if (!more) break;
                yield return current;
            }
            ConversationMoodProbe.Dispose(cases, ref casesFailure);
            if (casesFailure != null) throw new InvalidOperationException("Controlled TTS contrast cases failed.", casesFailure);
        }

        private static IEnumerator DrainNativeGreeting(PeopleOfTheIliacBayMod host, BreezeTtsTrialBridge bridge,
            LocalDialogueClient dialogue, ResidentActor actor, string sessionId)
        {
            const int openingCount = 0;
            ConversationMoodProbe.Require(bridge.PublishedRequestCount <= openingCount + 1,
                "Native Talk opening published multiple requests before its greeting was drained.");
            float deadline = Time.realtimeSinceStartup + 360f;
            while (bridge.PublishedRequestCount == openingCount && Time.realtimeSinceStartup < deadline)
                yield return null;
            ConversationMoodProbe.Require(bridge.PublishedRequestCount == openingCount + 1,
                "Native Talk did not publish exactly one genuine opening greeting.");

            string requestId = bridge.LastQueuedRequestId;
            string personId = null, boundSession = null, text = null;
            ConversationMoodProbe.Require(!string.IsNullOrWhiteSpace(requestId) &&
                bridge.TryReadRequestBinding(requestId, out personId, out boundSession, out text) &&
                personId == actor.Resident.Id && boundSession == sessionId && !string.IsNullOrWhiteSpace(text) &&
                text != LocalDialogueClient.Unavailable && text != LocalDialogueClient.ContextLimit,
                "Native opening greeting request did not bind the actual resident, session, and finalized text.");
            DaggerfallUI.AddHUDText("Native opening greeting: " + text);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_CONTRAST_NATIVE_GREETING: person=" + personId +
                "; request=" + requestId + "; source=actual-native-greeting; text=" + text + ".");

            IEnumerator playback = WaitForPlayback(bridge, requestId, personId, sessionId, text,
                "Native opening greeting: " + text);
            Exception playbackFailure = null;
            while (true)
            {
                bool more = false;
                object current = null;
                try { more = playback.MoveNext(); if (more) current = playback.Current; }
                catch (Exception error) { playbackFailure = error; break; }
                if (!more) break;
                yield return current;
            }
            ConversationMoodProbe.Dispose(playback, ref playbackFailure);
            if (playbackFailure != null) throw new InvalidOperationException("Native opening greeting playback failed.", playbackFailure);

            float settleDeadline = Time.realtimeSinceStartup + 30f;
            while ((dialogue.IsBusy || bridge.HasPendingSpeech ||
                Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host, "conversationBusy"), CultureInfo.InvariantCulture)) &&
                Time.realtimeSinceStartup < settleDeadline)
                yield return null;
            ConversationMoodProbe.Require(bridge.PublishedRequestCount == openingCount + 1 &&
                !dialogue.IsBusy && !bridge.HasPendingSpeech &&
                !Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host, "conversationBusy"), CultureInfo.InvariantCulture),
                "Native opening greeting did not settle before controlled contrast.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, BreezeTtsTrialBridge bridge,
            ResidentActor actor, string sessionId,
            BreezeTtsTrialDeliveryContext baseline, Manifest manifest)
        {
            for (int i = 0; i < manifest.cases.Length; i++)
            {
                Case item = manifest.cases[i];
                ConversationMoodProbe.Require(!bridge.HasPendingSpeech,
                    "Previous TTS contrast sample still owns audio; refusing to advance.");
                BreezeTtsTrialDeliveryContext variant = CopyWithControls(baseline, item.mood, item.fear, item.resentment);
                string display = "TTS contrast [" + item.label + "]: " + manifest.fixedText;
                DaggerfallUI.AddHUDText(display);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_CONTRAST_SAMPLE: label=" + item.label +
                    "; person=" + actor.Resident.Id + "; text=" + manifest.fixedText +
                    "; mood=" + variant.Mood + "; fear=" + variant.Fear + "; resentment=" + variant.Resentment +
                    "; emotion=" + item.controlledDirection.emotion +
                    "; deliveryMode=" + item.controlledDirection.deliveryMode + "; profile=" + manifest.expectedProfileId + ".");

                int publishedBefore = bridge.PublishedRequestCount;
                string requestId;
                bool published = bridge.NotifyFinalizedNpcText(actor.Resident.Id,
                    GameManager.Instance.TalkManager.NameNPC, manifest.fixedText, sessionId,
                    CurrentVoiceGender(actor), variant, SampleKind,
                    new BreezeTtsTrialControlledDirection(item.controlledDirection.emotion,
                        item.controlledDirection.deliveryMode), item.label, out requestId);
                ConversationMoodProbe.Require(published && !string.IsNullOrWhiteSpace(requestId) &&
                    bridge.PublishedRequestCount == publishedBefore + 1,
                    "Controlled TTS contrast request was not published exactly once.");
                IEnumerator playback = WaitForPlayback(bridge, requestId, actor.Resident.Id, sessionId,
                    manifest.fixedText, display);
                Exception playbackFailure = null;
                while (true)
                {
                    bool more = false;
                    object current = null;
                    try { more = playback.MoveNext(); if (more) current = playback.Current; }
                    catch (Exception error) { playbackFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
                ConversationMoodProbe.Dispose(playback, ref playbackFailure);
                if (playbackFailure != null) throw new InvalidOperationException("TTS contrast playback failed.", playbackFailure);
                LocalDialogueClient dialogue = ConversationMoodProbe.ReadHostField(host, "localDialogue") as LocalDialogueClient;
                ConversationMoodProbe.Require(!bridge.HasPendingSpeech && dialogue != null && !dialogue.IsBusy &&
                    !Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host, "conversationBusy"), CultureInfo.InvariantCulture) &&
                    !Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host, "returningGreetingPending"), CultureInfo.InvariantCulture),
                    "Dialogue or TTS work remained active after completed contrast playback.");
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        private static IEnumerator WaitForPlayback(BreezeTtsTrialBridge bridge, string requestId,
            string personId, string sessionId, string expectedText, string hudText)
        {
            string boundPerson, boundSession, boundText;
            ConversationMoodProbe.Require(bridge.TryReadRequestBinding(requestId, out boundPerson, out boundSession, out boundText) &&
                boundPerson == personId && boundSession == sessionId && boundText == expectedText,
                "TTS request binding differs from the actual resident, native session, or fixed transcript text.");
            bool observedPlaying = false;
            float deadline = Time.realtimeSinceStartup + 360f;
            while (Time.realtimeSinceStartup < deadline)
            {
                BreezeTtsTrialBridge.RequestState state = bridge.GetRequestState(requestId);
                if (state == BreezeTtsTrialBridge.RequestState.Playing && !observedPlaying)
                {
                    observedPlaying = true;
                    DaggerfallUI.AddHUDText(hudText);
                }
                if (state == BreezeTtsTrialBridge.RequestState.Cancelled || state == BreezeTtsTrialBridge.RequestState.Failed ||
                    state == BreezeTtsTrialBridge.RequestState.Unknown)
                    throw new InvalidOperationException("TTS contrast request ended without playback: " + state + ".");
                if (state == BreezeTtsTrialBridge.RequestState.Completed)
                {
                    string json;
                    ConversationMoodProbe.Require(bridge.TryReadPlaybackMeasurement(requestId, out json) &&
                        !string.IsNullOrWhiteSpace(json), "Completed contrast request lacks native playback measurement.");
                    PlaybackMeasurement measure = JsonUtility.FromJson<PlaybackMeasurement>(json);
                    ConversationMoodProbe.Require(measure != null && measure.schemaVersion == 1 &&
                        measure.requestId == requestId && measure.personId == personId &&
                        measure.conversationToken == sessionId && measure.status == "completed" &&
                        measure.playbackStartMonotonicTicks > 0 && measure.timestampFrequency > 0 &&
                        measure.mixerCallbackFrames > 0 && measure.mixerCallbackSamples > 0 &&
                        measure.nonZeroOutputSamples > 0 && measure.peakOutputAmplitudeMicrounits > 0 &&
                        measure.expectedSampleFrames > 0 && measure.sampleRate > 0 && measure.channels > 0 &&
                        measure.playbackEndMonotonicTicks > measure.playbackStartMonotonicTicks &&
                        measure.expectedPlaybackMilliseconds > 0 &&
                        measure.observedPlaybackMilliseconds >= measure.expectedPlaybackMilliseconds -
                            Math.Min(100, Math.Max(1, measure.expectedPlaybackMilliseconds / 50)) &&
                        (observedPlaying || measure.playbackStartMonotonicTicks > measure.textReadyMonotonicTicks),
                        "Native playback lacks complete duration or nonzero Unity output evidence.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_CONTRAST_PLAYBACK_PASS: request=" + requestId +
                        "; person=" + personId + "; status=" + measure.status +
                        "; nonzero-samples=" + measure.nonZeroOutputSamples +
                        "; expected-ms=" + measure.expectedPlaybackMilliseconds +
                        "; observed-ms=" + measure.observedPlaybackMilliseconds + ".");
                    yield break;
                }
                yield return null;
            }
            throw new TimeoutException("TTS contrast request did not complete native playback within six minutes.");
        }

        private static Manifest ReadManifest(string path)
        {
            ConversationMoodProbe.Require(Path.IsPathRooted(path) && File.Exists(path),
                "TTS contrast manifest path must be absolute and point to an existing file.");
            FileInfo info = new FileInfo(path);
            ConversationMoodProbe.Require(info.Length > 0 && info.Length <= 1024 * 1024,
                "TTS contrast manifest size is invalid.");
            string json = File.ReadAllText(path);
            FullSerializer.fsData root = FullSerializer.fsJsonParser.Parse(json);
            ConversationMoodProbe.Require(root != null && root.IsDictionary,
                "TTS contrast manifest root must be a JSON object.");
            FullSerializer.fsData rows = null;
            ConversationMoodProbe.Require(root.AsDictionary.TryGetValue("cases", out rows) &&
                rows != null && rows.IsList && rows.AsList.Count == CaseLabels.Length,
                "TTS contrast manifest must contain exactly six cases.");
            var parsedCases = new Case[rows.AsList.Count];
            for (int i = 0; i < parsedCases.Length; i++)
            {
                FullSerializer.fsData row = rows.AsList[i];
                FullSerializer.fsData direction = null;
                ConversationMoodProbe.Require(row != null && row.IsDictionary &&
                    row.AsDictionary.TryGetValue("controlledDirection", out direction) &&
                    direction != null && direction.IsDictionary,
                    "TTS contrast case must include typed controlledDirection fields.");
                parsedCases[i] = new Case
                {
                    label = ReadString(row, "label"),
                    mood = ReadInt(row, "mood"),
                    fear = ReadInt(row, "fear"),
                    resentment = ReadInt(row, "resentment"),
                    controlledDirection = new ControlledDirection
                    {
                        emotion = ReadString(direction, "emotion"),
                        deliveryMode = ReadString(direction, "deliveryMode")
                    }
                };
            }
            FullSerializer.fsData baseContext = null;
            ConversationMoodProbe.Require(root.AsDictionary.TryGetValue("baseContext", out baseContext) &&
                baseContext != null && baseContext.IsDictionary,
                "TTS contrast manifest must include baseContext as a JSON object.");
            var manifest = new Manifest
            {
                schemaVersion = ReadInt(root, "schemaVersion"),
                personId = ReadString(root, "personId"),
                fixedText = ReadString(root, "fixedText"),
                manifestKind = ReadString(root, "manifestKind"),
                sampleKind = ReadString(root, "sampleKind"),
                sourceKind = ReadString(root, "sourceKind"),
                status = ReadString(root, "status"),
                subjectName = ReadString(root, "subjectName"),
                expectedProfileId = ReadString(root, "expectedProfileId"),
                voiceSeed = ReadInt(root, "voiceSeed"),
                referencePath = ReadString(root, "referencePath"),
                referenceSha256 = ReadString(root, "referenceSha256"),
                baseTraitsSha256 = ReadString(root, "baseTraitsSha256"),
                cases = parsedCases,
                baseContext = baseContext
            };
            ConversationMoodProbe.Require(manifest != null && manifest.schemaVersion == 1 &&
                manifest.manifestKind == SampleKind && manifest.sampleKind == SampleKind &&
                manifest.sourceKind == "fixed-authored-text-not-submit-free-text" &&
                manifest.status == "prepared-no-synthesis" && manifest.subjectName == "Gwen Cienne" &&
                !string.IsNullOrWhiteSpace(manifest.personId) && manifest.fixedText == FixedText &&
                manifest.fixedText.Length <= BreezeTtsTrialProtocol.MaximumTextCharacters &&
                manifest.expectedProfileId == "npcvoice-01" && manifest.voiceSeed >= 0 &&
                Path.IsPathRooted(manifest.referencePath) && IsSha256(manifest.referenceSha256) &&
                IsSha256(manifest.baseTraitsSha256) && manifest.cases != null && manifest.cases.Length == CaseLabels.Length,
                "TTS contrast manifest is incomplete or has unsupported identity/profile metadata.");
            for (int i = 0; i < CaseLabels.Length; i++)
            {
                Case item = manifest.cases[i];
                ConversationMoodProbe.Require(item != null && item.label == CaseLabels[i] &&
                    item.controlledDirection != null && item.controlledDirection.emotion == "neutral" &&
                    item.controlledDirection.deliveryMode == (i == CaseLabels.Length - 1 ? "whisper" : "normal"),
                    "TTS contrast manifest cases must use the six fixed labels and neutral/normal controls, with whisper last.");
                int expectedMood = i == 1 ? 70 : i == 2 ? -70 : 0;
                int expectedFear = i == 3 ? 90 : 0;
                int expectedResentment = i == 4 ? 90 : 0;
                ConversationMoodProbe.Require(item.mood == expectedMood && item.fear == expectedFear &&
                    item.resentment == expectedResentment,
                    "TTS contrast manifest changed the fixed mood, fear, or resentment constellation.");
            }
            return manifest;
        }

        private static void ValidateManifestBaseline(Manifest manifest, BreezeTtsTrialDeliveryContext actual)
        {
            ConversationMoodProbe.Require(actual != null && actual.PersonId == manifest.personId,
                "Native actor-bound delivery context does not match manifest personId.");
            FullSerializer.fsData expected = manifest.baseContext;
            ConversationMoodProbe.Require(expected != null && expected.IsDictionary,
                "Manifest must include the exact captured native baseContext.");
            RequireInt(expected, "schemaVersion", actual.SchemaVersion);
            RequireString(expected, "personId", actual.PersonId);
            RequireInt(expected, "mood", actual.Mood);
            RequireBool(expected, "dispositionKnown", actual.DispositionKnown);
            RequireInt(expected, "disposition", actual.Disposition);
            RequireBool(expected, "trustKnown", actual.TrustKnown);
            RequireInt(expected, "trust", actual.Trust);
            RequireInt(expected, "fear", actual.Fear);
            RequireInt(expected, "resentment", actual.Resentment);
            RequireInt(expected, "encounters", actual.Encounters);

            FullSerializer.fsData traits;
            ConversationMoodProbe.Require(expected.AsDictionary.TryGetValue("traits", out traits) && traits != null &&
                traits.IsDictionary && traits.AsDictionary.Count == actual.Traits.Count,
                "Manifest baseContext must contain all saved personality traits.");
            foreach (KeyValuePair<string, string> pair in actual.Traits)
            {
                FullSerializer.fsData value;
                ConversationMoodProbe.Require(traits.AsDictionary.TryGetValue(pair.Key, out value) && value != null &&
                    value.IsString && value.AsString == pair.Value,
                    "Manifest baseContext personality differs from the saved native profile.");
            }
            string hash = HashTraits(actual.Traits);
            ConversationMoodProbe.Require(string.Equals(hash, manifest.baseTraitsSha256, StringComparison.OrdinalIgnoreCase),
                "Native saved personality hash differs from manifest baseTraitsSha256.");
        }

        private static BreezeTtsTrialDeliveryContext CaptureLiveDeliveryContext(PeopleOfTheIliacBayMod host)
        {
            MethodInfo method = typeof(PeopleOfTheIliacBayMod).GetMethod("CurrentTrialDeliveryContext",
                BindingFlags.Instance | BindingFlags.NonPublic);
            ConversationMoodProbe.Require(method != null, "Native actor-bound TTS delivery context capture is unavailable.");
            BreezeTtsTrialDeliveryContext context = method.Invoke(host, null) as BreezeTtsTrialDeliveryContext;
            ConversationMoodProbe.Require(context != null,
                "Native resident has no saved personality context; refusing to invent profile data for contrast.");
            return context;
        }

        private static BreezeTtsTrialDeliveryContext CopyWithControls(BreezeTtsTrialDeliveryContext source,
            int mood, int fear, int resentment)
        {
            var traits = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in source.Traits) traits.Add(pair.Key, pair.Value);
            return new BreezeTtsTrialDeliveryContext(source.PersonId, mood, source.DispositionKnown,
                source.Disposition, source.TrustKnown, source.Trust, fear, resentment, source.Encounters, traits);
        }

        private static string CurrentVoiceGender(ResidentActor actor)
        {
            if (actor.Resident.Sex == ResidentSex.Female) return "female";
            if (actor.Resident.Sex == ResidentSex.Male) return "male";
            return "unknown";
        }

        private static string ReadManifestPath(string[] args)
        {
            int index = Array.IndexOf(args, ManifestFlag);
            if (index < 0 || index + 1 >= args.Length) return null;
            string value = args[index + 1].Trim().Trim('"');
            return Path.IsPathRooted(value) ? Path.GetFullPath(value) : null;
        }

        private static string HashTraits(IReadOnlyDictionary<string, string> traits)
        {
            var keys = new List<string>(traits.Keys);
            keys.Sort(StringComparer.Ordinal);
            var json = new StringBuilder("{");
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(Quote(keys[i])).Append(':').Append(Quote(traits[keys[i]]));
            }
            json.Append('}');
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json.ToString()));
                var hex = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++) hex.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        private static string Quote(string value)
        {
            var result = new StringBuilder(value.Length + 2).Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '"' || c == '\\') result.Append('\\').Append(c);
                else if (c == '\b') result.Append("\\b");
                else if (c == '\f') result.Append("\\f");
                else if (c == '\n') result.Append("\\n");
                else if (c == '\r') result.Append("\\r");
                else if (c == '\t') result.Append("\\t");
                else if (c < 0x20) result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else result.Append(c);
            }
            return result.Append('"').ToString();
        }

        private static void RequireInt(FullSerializer.fsData parent, string name, int expected)
        {
            FullSerializer.fsData value;
            ConversationMoodProbe.Require(parent.AsDictionary.TryGetValue(name, out value) && value != null && value.IsInt64 &&
                value.AsInt64 == expected, "Manifest baseContext " + name + " differs from actor-bound native context.");
        }

        private static string ReadString(FullSerializer.fsData parent, string name)
        {
            FullSerializer.fsData value = null;
            ConversationMoodProbe.Require(parent != null && parent.IsDictionary &&
                parent.AsDictionary.TryGetValue(name, out value) && value != null && value.IsString,
                "TTS contrast manifest field " + name + " must be a string.");
            return value.AsString;
        }

        private static int ReadInt(FullSerializer.fsData parent, string name)
        {
            FullSerializer.fsData value = null;
            ConversationMoodProbe.Require(parent != null && parent.IsDictionary &&
                parent.AsDictionary.TryGetValue(name, out value) && value != null && value.IsInt64,
                "TTS contrast manifest field " + name + " must be an integer.");
            return (int)value.AsInt64;
        }

        private static void RequireBool(FullSerializer.fsData parent, string name, bool expected)
        {
            FullSerializer.fsData value;
            ConversationMoodProbe.Require(parent.AsDictionary.TryGetValue(name, out value) && value != null && value.IsBool &&
                value.AsBool == expected, "Manifest baseContext " + name + " differs from actor-bound native context.");
        }

        private static void RequireString(FullSerializer.fsData parent, string name, string expected)
        {
            FullSerializer.fsData value;
            ConversationMoodProbe.Require(parent.AsDictionary.TryGetValue(name, out value) && value != null && value.IsString &&
                value.AsString == expected, "Manifest baseContext " + name + " differs from actor-bound native context.");
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
                if (!((value[i] >= '0' && value[i] <= '9') || (value[i] >= 'a' && value[i] <= 'f') ||
                    (value[i] >= 'A' && value[i] <= 'F'))) return false;
            return true;
        }
    }
}
