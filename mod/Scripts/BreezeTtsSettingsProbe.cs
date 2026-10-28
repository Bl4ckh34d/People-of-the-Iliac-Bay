using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Optional native check for base-game NPC greeting speech, live voice-off cancellation,
    /// bridge reopen, and source-bound resident voice metadata. Does not submit model requests.
    /// </summary>
    internal static class BreezeTtsSettingsProbe
    {
        private const string RunFlag = "-peopleoftheiliacbay-breeze-tts-settings-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";
        private const string CancelText = "This controlled line checks that turning NPC voice off stops active playback.";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable]
        private sealed class RequestIdentity
        {
            public int schemaVersion = 0;
            public string mode = null;
            public string requestId = null;
            public string conversationToken = null;
            public string personId = null;
            public string voiceGender = null;
            public string displayName = null;
            public string text = null;
            public long textReadyMonotonicTicks = 0;
            public long timestampFrequency = 0;
            public BreezeTtsTrialBridge.VoiceIdentity voiceIdentity = null;
        }

        [Serializable]
        private sealed class PlaybackMeasurement
        {
            public int schemaVersion = 0;
            public string requestId = null;
            public string conversationToken = null;
            public string personId = null;
            public string status = null;
            public long mixerCallbackFrames = 0;
            public long nonZeroOutputSamples = 0;
        }

        private sealed class PlaybackEvidence
        {
            public string RequestId;
            public string PersonId;
            public string ConversationToken;
            public string Text;
            public string WavePath;
            public int SampleRate;
            public int Channels;
            public int SampleFrames;
        }

        private sealed class OpeningRequestBinding
        {
            public string RequestId;
            public string PersonId;
            public string ConversationToken;
            public string Text;
            public ListBox Transcript;
            public ListBox.ListItem Row;
            public StreamingWordHighlightOverlay Highlight;
        }

        private sealed class WordProgressObservation
        {
            public int PreviousHighWater;
            public int MaximumHighWater;
            public int MaximumAudioCursor;
            public bool Monotonic = true;
            public bool SawTextWeightedEstimate;
            public bool SawForcedAlignment;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            ConversationMoodProbe.Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0,
                "Breeze settings probe requires its explicit command-line flag.");
            ConversationMoodProbe.Require(character == OwnedCharacter && fixtureSave == BaselineSave,
                "Breeze settings probe requires the owned disposable character and exact baseline save.");
            ConversationMoodProbe.Require(host && streets && streets.Ready && GameManager.Instance && GameManager.Instance.IsReady,
                "Breeze settings probe requires the ready owned native fixture.");

            var bridge = ConversationMoodProbe.ReadHostField(host, "breezeTtsTrial") as BreezeTtsTrialBridge;
            ConversationMoodProbe.Require(bridge && bridge.VoiceEnabled && !bridge.IsTrialMode &&
                !string.IsNullOrWhiteSpace(bridge.ConfiguredServerEndpoint),
                "Breeze settings probe requires the production bridge, NPC voice enabled, and configured loopback endpoint.");

            WorldStateStore store = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
            ConversationMoodProbe.Require(store != null, "Breeze settings probe cannot read the loaded native world store.");
            GameManager game = GameManager.Instance;
            ResidentActor first = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host,
                game.PlayerMotor.transform.position, value => first = value, false);
            ResidentActor second = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host,
                game.PlayerMotor.transform.position, value => second = value, false,
                first && first.Resident != null ? first.Resident.Id : null);
            ConversationMoodProbe.Require(first && second,
                "Breeze settings probe needs two eligible, unused adult residents after bounded actor waits.");

            Vector3 originalPosition = game.PlayerMotor.transform.position;
            bool originalGreetingPending = Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,
                "returningGreetingPending"), CultureInfo.InvariantCulture);
            float originalGreetingRetryAt = Convert.ToSingle(ConversationMoodProbe.ReadHostField(host,
                "returningGreetingRetryAt"), CultureInfo.InvariantCulture);
            bool originalVoice = bridge.VoiceEnabled;
            bool originalStreaming = bridge.SocialStreamingEnabled;
            LocalDialogueClient dialogue = ConversationMoodProbe.ReadHostField(host, "localDialogue") as LocalDialogueClient;
            long replyRequestsBefore = dialogue ? ConversationMoodProbe.ReadLongProperty(dialogue, "ReplyRequestCount") : 0;
            int publishedBefore = bridge.PublishedRequestCount;
            DaggerfallTalkWindow talk = null;
            string sessionId = null;
            Exception failure = null;

            IEnumerator work = RunCases(host, streets, game, bridge, first, second,
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
            bridge.SetFeatureOptions(originalVoice, originalStreaming);
            if (!string.IsNullOrEmpty(sessionId))
            {
                StreamingWordHighlightOverlay.DetachForConversation(sessionId);
                bridge.EndConversation(sessionId);
            }
            if (talk != null || DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
            {
                IEnumerator close = ConversationMoodProbe.CloseTalk(host);
                while (true)
                {
                    bool more = false;
                    object current = null;
                    try { more = close.MoveNext(); if (more) current = close.Current; }
                    catch (Exception error) { if (failure == null) failure = error; break; }
                    if (!more) break;
                    yield return current;
                }
                ConversationMoodProbe.Dispose(close, ref failure);
            }
            game.PlayerMotor.transform.position = originalPosition;
            Physics.SyncTransforms();
            typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingPending", PrivateInstance)
                .SetValue(host, originalGreetingPending);
            typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingRetryAt", PrivateInstance)
                .SetValue(host, originalGreetingRetryAt);

            if (failure != null)
                throw new InvalidOperationException("Native Breeze settings probe failed; no LLM or D3 request was submitted.", failure);
            long replyRequestsAfter = dialogue ? ConversationMoodProbe.ReadLongProperty(dialogue, "ReplyRequestCount") : 0;
            ConversationMoodProbe.Require(replyRequestsAfter == replyRequestsBefore,
                "Breeze settings probe unexpectedly changed local dialogue request count.");
            ConversationMoodProbe.Require(bridge.PublishedRequestCount >= publishedBefore + 3,
                "Breeze settings probe did not play first greeting, cancellation line, and reopened greeting.");
            Debug.Log("BREEZE_TTS_NATIVE_SETTINGS_PASS: native-greetings=2; voice-off-cancelled-active-WAV=true; " +
                "reopen-after-toggle=true; resident-sex-race-age-source-checked=true; qwen-requests=0; d3-requests=0.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets, GameManager game,
            BreezeTtsTrialBridge bridge, ResidentActor first, ResidentActor second,
            Action<DaggerfallTalkWindow> setTalk, Action<string> setSession)
        {
            ConversationMoodProbe.StagePlayerNearActor(streets, first, game);
            int countBefore = bridge.PublishedRequestCount;
            DaggerfallTalkWindow talk = null;
            yield return OpenNativeTalk(host, game, first, value => talk = value);
            ConversationMoodProbe.Require(talk != null, "Native Talk did not open for first greeting actor.");
            setTalk(talk);
            OpeningRequestBinding firstBinding = null;
            yield return WaitForOpeningRequestBinding(host, bridge, talk, first, countBefore, 180f,
                value => firstBinding = value);
            ConversationMoodProbe.Require(firstBinding != null,
                "First native opening did not expose its exact transcript-row request binding.");
            string session = firstBinding.ConversationToken;
            setSession(session);

            string firstGreeting = firstBinding.RequestId;
            VerifyRequestIdentity(host, bridge, firstBinding, first);
            ListBox greetingTranscript = firstBinding.Transcript;
            ListBox.ListItem greetingRow = firstBinding.Row;
            StreamingWordHighlightOverlay wordOverlay = firstBinding.Highlight;
            ConversationMoodProbe.Require(greetingTranscript != null && greetingRow != null && wordOverlay != null &&
                greetingRow.textLabel != null && greetingRow.textLabel.Text == firstBinding.Text,
                "Production native-topic greeting row has no bound word-progress renderer.");
            var wordProgress = new WordProgressObservation();
            PlaybackEvidence greetingAudio = null;
            yield return WaitForCompletedPlayback(bridge, firstGreeting, first.Resident.Id, session, 360f,
                value => greetingAudio = value, () => ObserveWordProgress(wordOverlay, wordProgress,
                    bridge, firstGreeting));
            ConversationMoodProbe.Require(greetingAudio != null && greetingAudio.Text.Length > 0,
                "Base-game greeting completed without exact active-audio evidence.");
            yield return VerifyCompletedWordProgress(greetingTranscript, greetingRow, wordOverlay, bridge,
                firstBinding, wordProgress);
            yield return ReplayCompletedGreeting(host, bridge, firstBinding, wordProgress);

            BreezeTtsTrialBridge.VoiceIdentity identity = ReadCurrentVoiceIdentity(host);
            string voiceGender = identity.sex != null && identity.sex.known ? identity.sex.value : "unknown";
            string cancellationRequest;
            ConversationMoodProbe.Require(bridge.NotifyFinalizedNpcText(first.Resident.Id, game.TalkManager.NameNPC,
                CancelText, session, voiceGender, null, identity, out cancellationRequest),
                "Bridge rejected controlled playback line before voice-off cancellation test.");
            yield return WaitForActivePlayback(bridge, cancellationRequest, first.Resident.Id, session, 180f);
            ConversationMoodProbe.Require(bridge.ActiveRequestId == cancellationRequest,
                "Active full-WAV identity did not match controlled cancellation request.");
            bridge.EndConversation(session + "-stale");
            ConversationMoodProbe.Require(bridge.ActiveRequestId == cancellationRequest,
                "A stale conversation-close token cancelled current native speech.");

            bridge.SetFeatureOptions(false, false);
            ConversationMoodProbe.Require(bridge.ActiveRequestId == null && bridge.OutputAudioSource &&
                !bridge.OutputAudioSource.isPlaying,
                "Turning NPC voice off did not stop active Unity playback immediately.");
            yield return WaitForCancelledPlayback(bridge, cancellationRequest, 20f);
            bridge.EndConversation(session);
            yield return ConversationMoodProbe.CloseTalk(host);
            setTalk(null);
            setSession(null);

            bridge.SetFeatureOptions(true, false);
            ConversationMoodProbe.Require(bridge.VoiceEnabled,
                "Turning NPC voice back on did not reopen the configured bridge.");
            ConversationMoodProbe.StagePlayerNearActor(streets, second, game);
            countBefore = bridge.PublishedRequestCount;
            talk = null;
            yield return OpenNativeTalk(host, game, second, value => talk = value);
            ConversationMoodProbe.Require(talk != null, "Native Talk did not reopen for second greeting actor.");
            setTalk(talk);
            OpeningRequestBinding secondBinding = null;
            yield return WaitForOpeningRequestBinding(host, bridge, talk, second, countBefore, 180f,
                value => secondBinding = value);
            ConversationMoodProbe.Require(secondBinding != null,
                "Reopened native greeting did not expose its exact transcript-row request binding.");
            session = secondBinding.ConversationToken;
            setSession(session);
            string reopenedGreeting = secondBinding.RequestId;
            VerifyRequestIdentity(host, bridge, secondBinding, second);
            PlaybackEvidence reopenedAudio = null;
            yield return WaitForCompletedPlayback(bridge, reopenedGreeting, second.Resident.Id, session, 360f,
                value => reopenedAudio = value);
            ConversationMoodProbe.Require(reopenedAudio != null && reopenedAudio.RequestId == reopenedGreeting &&
                reopenedAudio.Text.Length > 0 && reopenedAudio.WavePath.Length > 0,
                "Reopened native greeting did not complete with exact WAV evidence.");
        }

        private static void VerifyRequestIdentity(PeopleOfTheIliacBayMod host, BreezeTtsTrialBridge bridge,
            OpeningRequestBinding binding, ResidentActor actor)
        {
            string mappedPerson, mappedSession, mappedText;
            bool memoryBindingValid = bridge.TryReadRequestBinding(binding.RequestId, out mappedPerson,
                out mappedSession, out mappedText);
            FieldInfo rootField = typeof(BreezeTtsTrialBridge).GetField("trialDirectory", PrivateInstance);
            string root = rootField == null ? null : rootField.GetValue(bridge) as string;
            ConversationMoodProbe.Require(!string.IsNullOrWhiteSpace(root) && Path.IsPathRooted(root),
                "Breeze bridge did not expose its configured absolute request root to the native probe.");
            string path = Path.Combine(root, "requests", binding.RequestId + ".request.json");
            ConversationMoodProbe.Require(File.Exists(path), "Native greeting request file is missing.");
            string rawRequestJson = File.ReadAllText(path);
            string archivedRequestPath = PreserveRequestJson(root, binding.RequestId, rawRequestJson);
            FullSerializer.fsData requestData = null;
            try { requestData = FullSerializer.fsJsonParser.Parse(rawRequestJson); }
            catch (Exception error)
            {
                throw new InvalidDataException("Native greeting request JSON could not be parsed; preserved=" + archivedRequestPath, error);
            }
            ConversationMoodProbe.Require(requestData != null && requestData.IsDictionary,
                "Native greeting request JSON root is not an object; preserved=" + archivedRequestPath);
            var requestFields = requestData.AsDictionary;
            string[] expectedRequestFields = { "schemaVersion", "mode", "requestId", "conversationToken", "personId",
                "voiceGender", "displayName", "text", "textReadyMonotonicTicks", "timestampFrequency", "voiceIdentity" };
            bool rootShapeValid = requestFields.Count == expectedRequestFields.Length;
            foreach (string field in expectedRequestFields)
                if (!requestFields.ContainsKey(field)) rootShapeValid = false;
            FullSerializer.fsData identityData;
            bool rawVoiceIdentityPresent = requestFields.TryGetValue("voiceIdentity", out identityData) &&
                identityData != null && identityData.IsDictionary;
            bool voiceIdentityShapeValid = rawVoiceIdentityPresent && HasExactFields(identityData,
                "schemaVersion", "sex", "race", "age", "origin") &&
                HasExactFields(identityData.AsDictionary["sex"], "value", "known", "source") &&
                HasExactFields(identityData.AsDictionary["race"], "value", "known", "source") &&
                HasExactFields(identityData.AsDictionary["age"], "value", "confidence", "source") &&
                HasExactFields(identityData.AsDictionary["origin"], "value", "known", "source");
            RequestIdentity request = null;
            FullSerializer.fsResult deserializeResult = new FullSerializer.fsSerializer().TryDeserialize(requestData, ref request);
            bool serializerSucceeded = !deserializeResult.Failed && request != null;
            Debug.Log("BREEZE_TTS_NATIVE_REQUEST_JSON_DIAGNOSTIC: request=" + binding.RequestId +
                "; rawVoiceIdentityPresent=" + rawVoiceIdentityPresent + "; rootShapeValid=" + rootShapeValid +
                "; voiceIdentityShapeValid=" + voiceIdentityShapeValid + "; serializerSucceeded=" + serializerSucceeded +
                "; deserializedVoiceIdentity=" + (request != null && request.voiceIdentity != null) +
                "; archivedRequest=" + archivedRequestPath + ".");
            string groundedSession = Convert.ToString(ConversationMoodProbe.ReadHostField(host,
                "conversationContextSessionId"), CultureInfo.InvariantCulture);
            string requestPerson = request == null ? null : request.personId;
            string requestSession = request == null ? null : request.conversationToken;
            string requestText = request == null ? null : request.text;
            bool rowMatches = string.Equals(binding.PersonId, actor.Resident.Id, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(binding.Text);
            bool requestMatches = request != null && request.schemaVersion == 1 && request.mode == "full-wav-pilot" &&
                request.requestId == binding.RequestId && request.textReadyMonotonicTicks > 0 && request.timestampFrequency > 0 &&
                request.personId == binding.PersonId && request.conversationToken == binding.ConversationToken &&
                request.text == binding.Text && request.voiceIdentity != null && request.voiceIdentity.schemaVersion == 1 &&
                request.voiceIdentity.sex != null && request.voiceIdentity.race != null &&
                request.voiceIdentity.age != null && request.voiceIdentity.origin != null;
            Debug.Log("BREEZE_TTS_NATIVE_REQUEST_BINDING_DIAGNOSTIC: request=" + binding.RequestId +
                "; expectedPerson=" + actor.Resident.Id + "; rowPerson=" + binding.PersonId +
                "; bridgePerson=" + (memoryBindingValid ? mappedPerson : "<missing>") +
                "; rowSession=" + binding.ConversationToken +
                "; bridgeSession=" + (memoryBindingValid ? mappedSession : "<missing>") +
                "; groundedSession=" + (string.IsNullOrEmpty(groundedSession) ? "<none>" : groundedSession) +
                "; requestSession=" + (requestSession ?? "<missing>") +
                "; requestPerson=" + (requestPerson ?? "<missing>") +
                "; rowTextLength=" + (binding.Text == null ? 0 : binding.Text.Length) +
                "; bridgeTextMatches=" + (memoryBindingValid && mappedText == binding.Text) +
                "; requestTextMatches=" + (requestText == binding.Text) +
                "; rawVoiceIdentityPresent=" + rawVoiceIdentityPresent +
                "; rootShapeValid=" + rootShapeValid + "; voiceIdentityShapeValid=" + voiceIdentityShapeValid +
                "; serializerSucceeded=" + serializerSucceeded +
                "; deserializedVoiceIdentity=" + (request != null && request.voiceIdentity != null) +
                "; archivedRequest=" + archivedRequestPath + ".");
            ConversationMoodProbe.Require(rootShapeValid && rawVoiceIdentityPresent && voiceIdentityShapeValid && serializerSucceeded,
                "Native greeting request JSON is missing or malformed in its required voice identity structure; see BREEZE_TTS_NATIVE_REQUEST_JSON_DIAGNOSTIC.");
            ConversationMoodProbe.Require(rowMatches && memoryBindingValid && mappedPerson == binding.PersonId &&
                mappedSession == binding.ConversationToken && mappedText == binding.Text && requestMatches,
                "Native greeting request disagreed with its exact native transcript-row and bridge bindings; see BREEZE_TTS_NATIVE_REQUEST_BINDING_DIAGNOSTIC.");
            string expectedSex = actor.Resident.Sex == ResidentSex.Female ? "female" :
                actor.Resident.Sex == ResidentSex.Male ? "male" : "unknown";
            string expectedAge = actor.Resident.AgeGroup == ResidentAgeGroup.Child ? "child" :
                actor.Resident.AgeGroup == ResidentAgeGroup.Adult ? "adult" : "unknown";
            ConversationMoodProbe.Require(request.voiceGender == expectedSex && request.voiceIdentity.sex.value == expectedSex &&
                request.voiceIdentity.sex.known == (expectedSex != "unknown") && request.voiceIdentity.sex.source == "saved-native-resident-sex",
                "Native greeting sex metadata does not match saved resident identity.");
            string expectedRace = actor.Person.Race.ToString();
            bool expectedRaceKnown = !string.IsNullOrWhiteSpace(expectedRace) &&
                !string.Equals(expectedRace, "unknown", StringComparison.OrdinalIgnoreCase);
            ConversationMoodProbe.Require(request.voiceIdentity.race.value == (expectedRaceKnown ? expectedRace : "unknown") &&
                request.voiceIdentity.race.known == expectedRaceKnown && request.voiceIdentity.race.source == "saved-native-resident-race",
                "Native greeting race metadata does not match native resident actor.");
            ConversationMoodProbe.Require(request.voiceIdentity.age.value == expectedAge &&
                request.voiceIdentity.age.confidence == "high" && request.voiceIdentity.age.source == "saved-resident-age-group" &&
                request.voiceIdentity.origin.value == "unknown" && !request.voiceIdentity.origin.known,
                "Native greeting age or unknown-origin metadata is invalid.");
        }

        private static BreezeTtsTrialBridge.VoiceIdentity ReadCurrentVoiceIdentity(PeopleOfTheIliacBayMod host)
        {
            MethodInfo method = typeof(PeopleOfTheIliacBayMod).GetMethod("CurrentTtsVoiceIdentity", PrivateInstance);
            return method == null ? null : method.Invoke(host, null) as BreezeTtsTrialBridge.VoiceIdentity;
        }

        private static IEnumerator OpenNativeTalk(PeopleOfTheIliacBayMod host, GameManager game,
            ResidentActor actor, Action<DaggerfallTalkWindow> completed)
        {
            DaggerfallUI.Instance.PopToHUD();
            typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingPending", PrivateInstance)
                .SetValue(host, false);
            typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingRetryAt", PrivateInstance)
                .SetValue(host, Time.unscaledTime + 86400f);
            game.TalkManager.TalkToMobileNPC(actor.Person);
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                DaggerfallTalkWindow talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                string personId = Convert.ToString(ConversationMoodProbe.ReadHostField(host,
                    "nativeTopicTtsPersonId"), CultureInfo.InvariantCulture);
                string token = Convert.ToString(ConversationMoodProbe.ReadHostField(host,
                    "nativeTopicTtsToken"), CultureInfo.InvariantCulture);
                ConversationMoodProbe.Require(!Convert.ToBoolean(ConversationMoodProbe.ReadHostField(host,
                    "conversationSessionOpen"), CultureInfo.InvariantCulture),
                    "Breeze settings probe unexpectedly entered the grounded AI conversation path.");
                if (talk != null && talk.IsSetup && DaggerfallUI.UIManager.TopWindow == talk &&
                    game.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                    game.TalkManager.MobileNPC == actor.Person &&
                    ReferenceEquals(ConversationMoodProbe.ReadHostField(host, "nativeTopicTtsWindow"), talk) &&
                    personId == actor.Resident.Id && !string.IsNullOrWhiteSpace(token))
                {
                    if (completed != null) completed(talk);
                    yield break;
                }
                yield return null;
            }
            ConversationMoodProbe.Require(false,
                "Native AI-free Talk did not bind the selected resident's standalone TTS session.");
        }

        private static IEnumerator WaitForOpeningRequestBinding(PeopleOfTheIliacBayMod host,
            BreezeTtsTrialBridge bridge, DaggerfallTalkWindow talk, ResidentActor actor, int countBefore,
            float timeoutSeconds, Action<OpeningRequestBinding> completed)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            string lastRequest = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                ListBox list = GetTranscript(talk);
                ListBox.ListItem row = list != null && list.Count == 1 ? list.GetItem(0) : null;
                System.Collections.IDictionary rows = ConversationMoodProbe.ReadHostField(host,
                    "nativeTopicTtsReplayByRow") as System.Collections.IDictionary;
                object rowBinding = row == null || rows == null ? null : rows[row];
                if (rowBinding != null)
                {
                    Type bindingType = rowBinding.GetType();
                    string requestId = Convert.ToString(bindingType.GetField("RequestId").GetValue(rowBinding), CultureInfo.InvariantCulture);
                    string personId = Convert.ToString(bindingType.GetField("PersonId").GetValue(rowBinding), CultureInfo.InvariantCulture);
                    string token = Convert.ToString(bindingType.GetField("ConversationToken").GetValue(rowBinding), CultureInfo.InvariantCulture);
                    StreamingWordHighlightOverlay highlight = bindingType.GetField("Highlight") == null ? null :
                        bindingType.GetField("Highlight").GetValue(rowBinding) as StreamingWordHighlightOverlay;
                    string boundPerson, boundToken, boundText;
                    bool bridgeBound = bridge.TryReadRequestBinding(requestId, out boundPerson, out boundToken, out boundText);
                    lastRequest = requestId;
                    ConversationMoodProbe.Require(bridge.PublishedRequestCount == countBefore + 1 &&
                        bridge.LastQueuedRequestId == requestId && row.textLabel != null &&
                        row.textColor != DaggerfallUI.DaggerfallQuestionTextColor &&
                        personId == actor.Resident.Id && token == Convert.ToString(
                            ConversationMoodProbe.ReadHostField(host, "nativeTopicTtsToken"), CultureInfo.InvariantCulture) &&
                        bridgeBound && boundPerson == personId && boundToken == token &&
                        boundText == row.textLabel.Text,
                        "Native opening row, standalone session, bridge request, and queue head did not bind exactly.");
                    completed(new OpeningRequestBinding { RequestId = requestId, PersonId = personId,
                        ConversationToken = token, Text = row.textLabel.Text, Transcript = list,
                        Row = row, Highlight = highlight });
                    yield break;
                }
                ConversationMoodProbe.Require(bridge.PublishedRequestCount <= countBefore + 1,
                    "Native greeting produced multiple TTS requests before the opening row could bind.");
                yield return null;
            }
            Debug.LogError("BREEZE_TTS_NATIVE_OPENING_BINDING_TIMEOUT: person=" + actor.Resident.Id +
                "; request=" + (lastRequest ?? bridge.LastQueuedRequestId ?? "<none>") +
                "; publishedDelta=" + (bridge.PublishedRequestCount - countBefore) +
                "; nativeSession=" + Convert.ToString(ConversationMoodProbe.ReadHostField(host,
                    "nativeTopicTtsToken"), CultureInfo.InvariantCulture) + ".");
            ConversationMoodProbe.Require(false,
                "Native opening transcript row did not receive its exact standalone TTS request binding.");
        }

        private static ListBox GetTranscript(DaggerfallTalkWindow talk)
        {
            FieldInfo listHolder = typeof(PeopleOfTheIliacBayMod).GetField("conversationListField",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo transcriptField = listHolder == null ? null : listHolder.GetValue(null) as FieldInfo;
            return transcriptField == null || talk == null ? null : transcriptField.GetValue(talk) as ListBox;
        }

        private static string PreserveRequestJson(string root, string requestId, string rawJson)
        {
            ConversationMoodProbe.Require(!string.IsNullOrEmpty(requestId) && requestId.Length == 32,
                "Cannot archive native request JSON with an invalid request ID.");
            foreach (char value in requestId)
                ConversationMoodProbe.Require(Uri.IsHexDigit(value), "Cannot archive native request JSON with a non-GUID request ID.");
            string directory = Path.Combine(root, "measurements", "native-voice-settings-probe");
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, requestId + ".request.json");
            if (!File.Exists(destination))
            {
                try
                {
                    using (var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                        writer.Write(rawJson);
                }
                catch (IOException)
                {
                    if (!File.Exists(destination)) throw;
                }
            }
            ConversationMoodProbe.Require(string.Equals(File.ReadAllText(destination), rawJson, StringComparison.Ordinal),
                "A preserved native request diagnostic already exists with different bytes; refusing overwrite.");
            return destination;
        }

        private static bool HasExactFields(FullSerializer.fsData value, params string[] expectedFields)
        {
            if (value == null || !value.IsDictionary || expectedFields == null) return false;
            var fields = value.AsDictionary;
            if (fields.Count != expectedFields.Length) return false;
            foreach (string field in expectedFields)
                if (!fields.ContainsKey(field)) return false;
            return true;
        }

        private static IEnumerator WaitForActivePlayback(BreezeTtsTrialBridge bridge, string requestId,
            string personId, string session, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            long lastNonZeroSamples = 0;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (TryReadActiveNonZeroSamples(bridge, requestId, personId, session, out lastNonZeroSamples) &&
                    lastNonZeroSamples > 0)
                {
                    Debug.Log("BREEZE_TTS_NATIVE_AUDIBLE_START: request=" + requestId +
                        "; person=" + personId + "; session=" + session +
                        "; mixerNonZeroSamples=" + lastNonZeroSamples + ".");
                    yield break;
                }
                BreezeTtsTrialBridge.RequestState state = bridge.GetRequestState(requestId);
                ConversationMoodProbe.Require(state != BreezeTtsTrialBridge.RequestState.Failed &&
                    state != BreezeTtsTrialBridge.RequestState.Cancelled && state != BreezeTtsTrialBridge.RequestState.Completed,
                    "Controlled voice-off line ended before bound nonzero mixer output was observed.");
                yield return null;
            }
            Debug.LogError("BREEZE_TTS_NATIVE_AUDIBLE_START_TIMEOUT: request=" + requestId +
                "; person=" + personId + "; session=" + session +
                "; lastMixerNonZeroSamples=" + lastNonZeroSamples + ".");
            ConversationMoodProbe.Require(false, "Controlled voice-off line never produced bound nonzero mixer output.");
        }

        private static bool TryReadActiveNonZeroSamples(BreezeTtsTrialBridge bridge, string requestId,
            string personId, string session, out long nonZeroSamples)
        {
            nonZeroSamples = 0;
            if (bridge == null || bridge.ActiveRequestId != requestId ||
                bridge.GetRequestState(requestId) != BreezeTtsTrialBridge.RequestState.Playing) return false;
            BreezeTtsTrialBridge.ActiveAudioEvidence evidence;
            if (!bridge.TryReadActiveAudioEvidence(out evidence) || evidence == null ||
                evidence.RequestId != requestId || evidence.PersonId != personId ||
                evidence.ConversationToken != session || string.IsNullOrWhiteSpace(evidence.Text)) return false;

            FieldInfo entryField = typeof(BreezeTtsTrialBridge).GetField("activeEntry", PrivateInstance);
            FieldInfo mixerField = typeof(BreezeTtsTrialBridge).GetField("mixerCounters", PrivateInstance);
            object entry = entryField == null ? null : entryField.GetValue(bridge);
            object activeCounters = mixerField == null ? null : mixerField.GetValue(bridge);
            if (entry == null || activeCounters == null) return false;
            BindingFlags instanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo entryPersonField = entry.GetType().GetField("PersonId", instanceFields);
            FieldInfo entrySessionField = entry.GetType().GetField("ConversationToken", instanceFields);
            FieldInfo entryCountersField = entry.GetType().GetField("Counters", instanceFields);
            if (entryPersonField == null || entrySessionField == null || entryCountersField == null ||
                !ReferenceEquals(entryCountersField.GetValue(entry), activeCounters) ||
                !string.Equals(entryPersonField.GetValue(entry) as string, personId, StringComparison.Ordinal) ||
                !string.Equals(entrySessionField.GetValue(entry) as string, session, StringComparison.Ordinal)) return false;
            FieldInfo nonZeroField = activeCounters.GetType().GetField("NonZeroSamples", instanceFields);
            if (nonZeroField == null) return false;
            object rawSamples = nonZeroField.GetValue(activeCounters);
            if (!(rawSamples is long)) return false;
            nonZeroSamples = (long)rawSamples;
            return true;
        }

        private static IEnumerator WaitForCompletedPlayback(BreezeTtsTrialBridge bridge, string requestId,
            string personId, string session, float timeoutSeconds,
            Action<PlaybackEvidence> completed, Action observe = null)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            PlaybackEvidence activeEvidence = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (observe != null) observe();
                BreezeTtsTrialBridge.ActiveAudioEvidence active;
                if (bridge.ActiveRequestId == requestId && bridge.TryReadActiveAudioEvidence(out active))
                {
                    ConversationMoodProbe.Require(active.PersonId == personId && active.ConversationToken == session && active.Text.Length > 0,
                        "Active greeting evidence lost request/person/session/text binding.");
                    activeEvidence = new PlaybackEvidence { RequestId = active.RequestId, PersonId = active.PersonId,
                        ConversationToken = active.ConversationToken, Text = active.Text, WavePath = active.WavePath,
                        SampleRate = active.SampleRate, Channels = active.Channels, SampleFrames = active.SampleFrames };
                }

                string json;
                if (bridge.TryReadLatestPlaybackMeasurement(requestId, out json))
                {
                    PlaybackMeasurement measurement = JsonUtility.FromJson<PlaybackMeasurement>(json);
                    if (measurement != null && measurement.status == "completed")
                    {
                        ConversationMoodProbe.Require(measurement.requestId == requestId && measurement.personId == personId &&
                            measurement.conversationToken == session && measurement.mixerCallbackFrames > 0 &&
                            measurement.nonZeroOutputSamples > 0,
                            "Native greeting did not produce bound nonzero Unity playback evidence.");
                        string boundPerson, boundSession, boundText;
                        ConversationMoodProbe.Require(bridge.TryReadRequestBinding(requestId, out boundPerson,
                            out boundSession, out boundText) && boundPerson == personId && boundSession == session &&
                            !string.IsNullOrWhiteSpace(boundText) && bridge.GetRequestState(requestId) == BreezeTtsTrialBridge.RequestState.Completed,
                            "Completed greeting lost its native request binding.");
                        ConversationMoodProbe.Require(activeEvidence != null && activeEvidence.Text == boundText &&
                            File.Exists(activeEvidence.WavePath) && activeEvidence.SampleRate > 0 &&
                            activeEvidence.Channels > 0 && activeEvidence.SampleFrames > 0,
                            "Completed greeting lacks captured, validated active WAV evidence.");
                        completed(activeEvidence);
                        if (observe != null) observe();
                        yield break;
                    }
                    ConversationMoodProbe.Require(measurement == null || !measurement.status.StartsWith("cancelled", StringComparison.Ordinal) &&
                        measurement.status != "failed",
                        "Native greeting playback was cancelled or failed.");
                }
                yield return null;
            }
            ConversationMoodProbe.Require(false, "Native greeting playback did not complete before timeout.");
        }

        private static void ObserveWordProgress(object overlay, WordProgressObservation observation,
            BreezeTtsTrialBridge bridge = null, string requestId = null)
        {
            if (overlay == null || observation == null) return;
            Type type = overlay.GetType();
            FieldInfo highWaterField = type.GetField("currentCumulativeTextEnd", PrivateInstance);
            FieldInfo sourceField = type.GetField("currentProgressSource", PrivateInstance);
            FieldInfo estimatedField = type.GetField("usedTextWeightedEstimateThisPlayback", PrivateInstance);
            if (highWaterField == null || sourceField == null || estimatedField == null) return;
            int highWater = Convert.ToInt32(highWaterField.GetValue(overlay), CultureInfo.InvariantCulture);
            if (highWater < observation.PreviousHighWater) observation.Monotonic = false;
            observation.PreviousHighWater = highWater;
            observation.MaximumHighWater = Math.Max(observation.MaximumHighWater, highWater);
            string source = Convert.ToString(sourceField.GetValue(overlay), CultureInfo.InvariantCulture);
            if (source == "text-weighted-audio-estimate") observation.SawTextWeightedEstimate = true;
            if (source == "forced-word-alignment") observation.SawForcedAlignment = true;
            observation.SawTextWeightedEstimate |= Convert.ToBoolean(estimatedField.GetValue(overlay), CultureInfo.InvariantCulture);
            if (bridge != null && requestId != null && bridge.ActiveRequestId == requestId &&
                bridge.GetRequestState(requestId) == BreezeTtsTrialBridge.RequestState.Playing &&
                bridge.OutputAudioSource != null && bridge.OutputAudioSource.clip != null)
                observation.MaximumAudioCursor = Math.Max(observation.MaximumAudioCursor,
                    bridge.OutputAudioSource.timeSamples);
        }

        private static IEnumerator ReplayCompletedGreeting(PeopleOfTheIliacBayMod host,
            BreezeTtsTrialBridge bridge, OpeningRequestBinding opening, WordProgressObservation observation)
        {
            ListBox transcript = opening.Transcript;
            ListBox.ListItem row = opening.Row;
            FieldInfo mapField = typeof(PeopleOfTheIliacBayMod).GetField("nativeTopicTtsReplayByRow", PrivateInstance);
            IDictionary rowBindings = mapField == null ? null : mapField.GetValue(host) as IDictionary;
            object nativeBinding = rowBindings == null || row == null ? null : rowBindings[row];
            ConversationMoodProbe.Require(nativeBinding != null &&
                ReadStringField(nativeBinding, "RequestId") == opening.RequestId &&
                ReadStringField(nativeBinding, "ConversationToken") == opening.ConversationToken &&
                ReadStringField(nativeBinding, "PersonId") == opening.PersonId &&
                ReferenceEquals(ReadField(nativeBinding, "Highlight"), opening.Highlight),
                "Cached replay request is not the production binding for the exact opening row.");

            MethodInfo findRow = typeof(PeopleOfTheIliacBayMod).GetMethod("FindClickedTranscriptRow",
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo replayClick = typeof(PeopleOfTheIliacBayMod).GetMethod("OnNativeTopicTtsReplayClick",
                BindingFlags.Instance | BindingFlags.NonPublic);
            ConversationMoodProbe.Require(findRow != null && replayClick != null && transcript != null && row != null,
                "Production transcript replay click handler is unavailable.");

            int contentY = 0;
            int rowCenterY = -1;
            for (int index = 0; index < transcript.Count; index++)
            {
                ListBox.ListItem item = transcript.GetItem(index);
                if (item != null && item.textLabel != null)
                {
                    if (ReferenceEquals(item, row)) rowCenterY = contentY + item.textLabel.TextHeight / 2 - transcript.ScrollIndex;
                    contentY += item.textLabel.TextHeight + transcript.RowSpacing;
                }
            }
            ConversationMoodProbe.Require(rowCenterY >= 0 && rowCenterY < transcript.Rectangle.height,
                "Bound greeting row is outside the transcript click viewport for cached replay.");
            Vector2 clickPosition = new Vector2(4f, rowCenterY);
            ListBox.ListItem clickedRow = findRow.Invoke(null, new object[] { transcript, clickPosition }) as ListBox.ListItem;
            ConversationMoodProbe.Require(ReferenceEquals(clickedRow, row),
                "Cached replay click coordinate did not resolve the exact opening row.");

            int initialIndex = bridge.GetPlaybackIndex(opening.RequestId);
            int publishedBefore = bridge.PublishedRequestCount;
            replayClick.Invoke(host, new object[] { transcript, clickPosition });
            int replayIndex = bridge.GetPlaybackIndex(opening.RequestId);
            ConversationMoodProbe.Require(initialIndex >= 1 && replayIndex == initialIndex + 1 &&
                bridge.PublishedRequestCount == publishedBefore,
                "Opening-row click did not replay the cached WAV exactly once without a new TTS request.");

            FieldInfo resetIndexField = typeof(StreamingWordHighlightOverlay).GetField(
                "lastProgressResetPlaybackIndex", PrivateInstance);
            FieldInfo resetTextEndField = typeof(StreamingWordHighlightOverlay).GetField(
                "lastProgressResetTextEnd", PrivateInstance);
            FieldInfo highWaterField = typeof(StreamingWordHighlightOverlay).GetField(
                "currentCumulativeTextEnd", PrivateInstance);
            ConversationMoodProbe.Require(resetIndexField != null && resetTextEndField != null && highWaterField != null,
                "Replay progress-reset evidence is unavailable.");
            float resetDeadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < resetDeadline &&
                Convert.ToInt32(resetIndexField.GetValue(opening.Highlight), CultureInfo.InvariantCulture) != replayIndex)
                yield return null;
            int resetIndex = Convert.ToInt32(resetIndexField.GetValue(opening.Highlight), CultureInfo.InvariantCulture);
            int resetEnd = Convert.ToInt32(resetTextEndField.GetValue(opening.Highlight), CultureInfo.InvariantCulture);
            ConversationMoodProbe.Require(resetIndex == replayIndex && resetEnd == 0,
                "Replay did not reset cumulative highlight state for its new playback index.");

            observation.PreviousHighWater = Convert.ToInt32(highWaterField.GetValue(opening.Highlight),
                CultureInfo.InvariantCulture);
            observation.MaximumHighWater = observation.PreviousHighWater;
            observation.MaximumAudioCursor = 0;
            PlaybackEvidence replayEvidence = null;
            yield return WaitForCompletedPlayback(bridge, opening.RequestId, opening.PersonId,
                opening.ConversationToken, 360f, value => replayEvidence = value,
                () => ObserveWordProgress(opening.Highlight, observation, bridge, opening.RequestId));
            ConversationMoodProbe.Require(replayEvidence != null && replayEvidence.RequestId == opening.RequestId &&
                observation.MaximumAudioCursor > 0,
                "Cached replay did not advance its real Unity audio cursor under the exact retained request.");
            yield return VerifyCompletedWordProgress(transcript, row, opening.Highlight, bridge, opening, observation);
            Debug.Log("BREEZE_TTS_NATIVE_CACHED_REPLAY_PASS: request=" + opening.RequestId +
                "; row=exact-native-opening-occurrence; playback-index=" + replayIndex +
                "; progress-reset-to-utf16=0; max-audio-cursor=" + observation.MaximumAudioCursor +
                "; new-tts-requests=0; monotonic-after-reset=" + observation.Monotonic + ".");
        }

        private static object ReadField(object instance, string name)
        {
            if (instance == null) return null;
            FieldInfo field = instance.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(instance);
        }

        private static string ReadStringField(object instance, string name)
        { return Convert.ToString(ReadField(instance, name), CultureInfo.InvariantCulture); }

        private static IEnumerator VerifyCompletedWordProgress(ListBox transcript, ListBox.ListItem row,
            StreamingWordHighlightOverlay overlay, BreezeTtsTrialBridge bridge, OpeningRequestBinding binding,
            WordProgressObservation observation)
        {
            Type type = overlay.GetType();
            FieldInfo highWaterField = type.GetField("currentCumulativeTextEnd", PrivateInstance);
            FieldInfo sourceField = type.GetField("currentProgressSource", PrivateInstance);
            FieldInfo receiptField = type.GetField("completedFullWavReceiptValid", PrivateInstance);
            FieldInfo indexField = type.GetField("fullWavObservedPlaybackIndex", PrivateInstance);
            FieldInfo drawnGlyphsProperty = type.GetField("lastDrawnBlueGlyphCount", PrivateInstance);
            FieldInfo estimateField = type.GetField("usedTextWeightedEstimateThisPlayback", PrivateInstance);
            FieldInfo timelineField = type.GetField("fullWavTimeline", PrivateInstance);
            ConversationMoodProbe.Require(highWaterField != null && sourceField != null && receiptField != null &&
                indexField != null && drawnGlyphsProperty != null && estimateField != null && timelineField != null,
                "Native word-progress renderer diagnostics are unavailable.");

            float deadline = Time.realtimeSinceStartup + 8f;
            int highWater = 0, drawnGlyphs = 0, playbackIndex = -1;
            bool receiptValid = false, estimateUsed = false;
            string source = "none";
            while (Time.realtimeSinceStartup < deadline)
            {
                ObserveWordProgress(overlay, observation, bridge, binding.RequestId);
                highWater = Convert.ToInt32(highWaterField.GetValue(overlay), CultureInfo.InvariantCulture);
                drawnGlyphs = Convert.ToInt32(drawnGlyphsProperty.GetValue(overlay), CultureInfo.InvariantCulture);
                playbackIndex = Convert.ToInt32(indexField.GetValue(overlay), CultureInfo.InvariantCulture);
                receiptValid = Convert.ToBoolean(receiptField.GetValue(overlay), CultureInfo.InvariantCulture);
                estimateUsed = Convert.ToBoolean(estimateField.GetValue(overlay), CultureInfo.InvariantCulture);
                source = Convert.ToString(sourceField.GetValue(overlay), CultureInfo.InvariantCulture);
                if (receiptValid && playbackIndex == bridge.GetPlaybackIndex(binding.RequestId) &&
                    highWater >= binding.Text.Length && drawnGlyphs > 0) break;
                yield return null;
            }

            bool exactBinding = bridge.TryReadRequestBinding(binding.RequestId, out string personId,
                out string token, out string text) && personId == binding.PersonId &&
                token == binding.ConversationToken && text == binding.Text && row != null &&
                ReferenceEquals(transcript.GetItem(0), row) && row.textLabel.Text == text;
            ConversationMoodProbe.Require(exactBinding && observation.Monotonic && receiptValid &&
                playbackIndex == bridge.GetPlaybackIndex(binding.RequestId) && highWater == binding.Text.Length &&
                drawnGlyphs > 0 && source == "completed-playback-receipt",
                "Real greeting playback did not draw cumulative blue text under its exact completed receipt; " +
                "highWater=" + highWater + "; length=" + binding.Text.Length + "; source=" + source +
                "; drawnGlyphs=" + drawnGlyphs + "; monotonic=" + observation.Monotonic + ".");

            string directory = Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay");
            Directory.CreateDirectory(directory);
            string screenshot = Path.Combine(directory, "breeze-settings-word-progress-" + binding.RequestId +
                "-playback-" + playbackIndex.ToString(CultureInfo.InvariantCulture) + ".png");
            ScreenCapture.CaptureScreenshot(screenshot);
            yield return new WaitForEndOfFrame();
            WordAlignmentTimelineDocument timeline = timelineField.GetValue(overlay) as WordAlignmentTimelineDocument;
            Debug.Log("BREEZE_TTS_NATIVE_WORD_PROGRESS_PASS: request=" + binding.RequestId +
                "; playback-index=" + playbackIndex + "; source=" + source +
                "; estimated-progress-observed=" + (estimateUsed || observation.SawTextWeightedEstimate) +
                "; forced-alignment-observed=" + observation.SawForcedAlignment +
                "; validated-alignment-ready=" + (timeline != null) + "; cumulative-utf16=" + highWater +
                "; transcript-length=" + binding.Text.Length + "; monotonic=" + observation.Monotonic +
                "; drawn-blue-glyphs=" + drawnGlyphs + "; screenshot=" + screenshot + ".");
        }

        private static IEnumerator WaitForCancelledPlayback(BreezeTtsTrialBridge bridge, string requestId, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                string json;
                if (bridge.TryReadLatestPlaybackMeasurement(requestId, out json))
                {
                    PlaybackMeasurement measurement = JsonUtility.FromJson<PlaybackMeasurement>(json);
                    if (measurement != null && measurement.status == "cancelled:tts-disabled")
                    {
                        ConversationMoodProbe.Require(measurement.requestId == requestId &&
                            measurement.mixerCallbackFrames > 0 && measurement.nonZeroOutputSamples > 0,
                            "Voice-off cancellation lacks evidence that playback had started audibly.");
                        yield break;
                    }
                    ConversationMoodProbe.Require(measurement == null ||
                        !measurement.status.StartsWith("cancelled", StringComparison.Ordinal) && measurement.status != "failed",
                        "Voice-off playback ended for an unexpected reason.");
                }
                yield return null;
            }
            ConversationMoodProbe.Require(false, "Voice-off cancellation measurement did not arrive before timeout.");
        }
    }
}
