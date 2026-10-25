using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Controlled native RED probe for the mood-enabled ordinary social duplicate path.
    /// Backend completions are swallowed and replaced with controlled test callbacks.
    /// </summary>
    internal static class SocialDuplicateReplyProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-social-duplicate-reply-probe";
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        private static bool active;
        private static bool expectedRed;
        private static Action<string> heldCompletion;
        private static object lastRequest;

        private sealed class Evidence
        {
            internal string PersonId;
            internal double BaselineClock;
            internal Vector3 BaselinePosition;
            internal int InitialMoodValue;
            internal long InitialMoodSequence;
            internal bool ExpectedRed;
            internal string ExpectedRedEvidence;
        }

        internal static bool IsEnabled
        {
            get { return active && Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0; }
        }
        internal static bool ExpectedRed { get { return expectedRed; } }

        // The new switch reaches this wrapper only through the production
        // mood-enabled callback site in ConversationMoodProbe.WrapReply.
        internal static Action<string> WrapReply(DaggerfallTalkWindow talk, DialogueRequest request,
            Action<string> complete)
        {
            if (!IsEnabled || complete == null || request == null) return complete;
            lastRequest = request;
            heldCompletion = complete;
            return answer => { };
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            ConversationMoodProbe.Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave && host && streets &&
                GameManager.Instance && GameManager.Instance.IsReady && streets.Ready,
                "Social duplicate probe requires opt-in and the exact ready owned Gothway Garden fixture.");

            GameManager game = GameManager.Instance;
            Evidence evidence = new Evidence
            {
                BaselineClock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                BaselinePosition = game.PlayerMotor.transform.position
            };
            Exception probeFailure = null;
            Exception restoreFailure = null;
            active = true;
            expectedRed = false;
            heldCompletion = null;
            lastRequest = null;

            IEnumerator cases = DriveEnumeratorTree(RunCases(host, streets, evidence));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = cases.MoveNext(); if (more) current = cases.Current; }
                    catch (Exception error) { probeFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                DisposeTree(cases, ref probeFailure);
                active = false;
                heldCompletion = null;
                lastRequest = null;
                ConversationMoodProbe.CancelTransport(host);
                if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
            }

            IEnumerator restore = DriveEnumeratorTree(ConversationMoodProbe.LoadSave(host, streets,
                character, BaselineSave, true));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = restore.MoveNext(); if (more) current = restore.Current; }
                    catch (Exception error) { restoreFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally { DisposeTree(restore, ref restoreFailure); }

            if (restoreFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_BASELINE_RESTORE_FAIL: " + restoreFailure);
                throw new InvalidOperationException("Social duplicate probe could not reload the exact baseline.", restoreFailure);
            }
            try
            {
                Require(Math.Abs(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() - evidence.BaselineClock) <= 2d &&
                    Vector3.Distance(game.PlayerMotor.transform.position, evidence.BaselinePosition) <= 0.05f,
                    "Exact baseline reload did not restore clock and player position.");
                if (!string.IsNullOrEmpty(evidence.PersonId))
                {
                    WorldStateStore baselineStore = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
                    Require(baselineStore != null, "Exact baseline reload did not restore the world store.");
                    ConversationMoodView mood = baselineStore.CaptureConversationMood(evidence.PersonId);
                    Require(mood.Value == evidence.InitialMoodValue && mood.AcceptedTurnSequence == evidence.InitialMoodSequence,
                        "Exact baseline reload did not restore the selected resident's initial mood.");
                }
            }
            catch (Exception error)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_BASELINE_VERIFY_FAIL: " + error);
                throw new InvalidOperationException("Exact baseline reload completed, but its clock, position, or mood verification failed.", error);
            }

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_BASELINE_RESTORED: character=" + character +
                "; save=" + BaselineSave + "; clock-and-position-verified=true.");
            if (probeFailure != null)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_RED_AFTER_BASELINE_RESTORE: " + probeFailure.Message);
                throw new InvalidOperationException("Social duplicate probe failed after exact baseline restore.", probeFailure);
            }
            if (evidence.ExpectedRed)
            {
                expectedRed = true;
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_EXPECTED_RED: " + evidence.ExpectedRedEvidence);
                yield break;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_DUPLICATE_REPLY_PASS: first-duplicate-repair=true; " +
                "single-final-mood-and-speech-commit=true; stale-callbacks-inert=true; bounded-repeat-unavailable=true; " +
                "retry-transport-unavailable=true; invalid-receipt-fallback=true; baseline-restored=true; controlled-callbacks=true; " +
                "accepted-backend-completion=0; audio=0.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets, Evidence evidence)
        {
            Require(Convert.ToBoolean(ReadHostField(host, "localDialogueEnabled"), CultureInfo.InvariantCulture) &&
                !Convert.ToBoolean(ReadHostField(host, "npcVoiceEnabled"), CultureInfo.InvariantCulture) &&
                !Convert.ToBoolean(ReadHostField(host, "dialoguePrewarmEnabled"), CultureInfo.InvariantCulture),
                "Social duplicate probe requires free text enabled, voice disabled, and model prewarm disabled.");
            GameManager game = GameManager.Instance;
            WorldStateStore store = ReadHostField(host, "store") as WorldStateStore;
            Require(store != null, "Social duplicate probe cannot read the owned fixture store.");
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, BaselineSave) >= 0,
                "Exact social duplicate baseline save is missing.");
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                game.PlayerEnterExit.TransitionExterior();
                DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 30f;
                while (game.PlayerEnterExit.IsPlayerInsideBuilding && Time.realtimeSinceStartup < deadline)
                    yield return new WaitForSecondsRealtime(0.25f);
                Require(!game.PlayerEnterExit.IsPlayerInsideBuilding,
                    "Social duplicate probe could not enter the saved exterior scene.");
            }

            ResidentActor actor = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host, game.PlayerMotor.transform.position,
                selected => actor = selected, requireNeutral: true);
            Require(actor && actor.Resident != null && actor.Resident.IsAlive && !actor.Resident.IsQuestProtected,
                "No eligible neutral managed adult resident was available for the duplicate probe.");
            evidence.PersonId = actor.Resident.Id;
            ConversationMoodView initialMood = store.CaptureConversationMood(evidence.PersonId);
            evidence.InitialMoodValue = initialMood.Value;
            evidence.InitialMoodSequence = initialMood.AcceptedTurnSequence;
            ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);

            DaggerfallTalkWindow talk = null;
            yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
            Require(talk != null && ReferenceEquals(ReadHostField(host, "memoryWindow"), talk),
                "Native Talk did not bind the selected duplicate-probe resident.");
            LocalDialogueClient client = ReadHostField(host, "localDialogue") as LocalDialogueClient;
            Require(client != null && client.enabled, "Social duplicate probe needs an enabled local dialogue client.");
            FieldInfo transcriptField = ReadTranscriptField(host);

            const string firstPlayerText = "Tell me something about this street.";
            const string previousReply = "The courtyard is quiet.";
            Action<string> first = StartHeldReply(host, talk, firstPlayerText, out _);
            ConversationMoodProbe.CancelTransport(host);
            first("{\"reply\":\"" + previousReply + "\",\"moodDelta\":0}");
            yield return null;
            Require(ConversationMoodProbe.CountExchange(store, evidence.PersonId, firstPlayerText,
                    previousReply, "model") == 1,
                "Controlled setup reply did not append exactly one prior NPC answer.");

            const string duplicatePlayerText = "What else can you tell me about this street?";
            const string repairReply = "I find this street pleasant in the afternoon.";
            int moodValueBefore = store.CaptureConversationMood(evidence.PersonId).Value;
            long sequenceBefore = store.CaptureConversationMood(evidence.PersonId).AcceptedTurnSequence;
            long inboxRevisionBefore = store.CaptureNpcExperienceInboxRevision();
            int transcriptCountBefore = CountDurableTranscriptRows(transcriptField, talk);
            int duplicateExchangeBefore = ConversationMoodProbe.CountExchange(store, evidence.PersonId,
                duplicatePlayerText, previousReply, "model");
            Action<string> duplicate = StartHeldReply(host, talk, duplicatePlayerText, out _);
            var observed = (NpcObservedSpeechReceipt)ReadHostField(host, "conversationObservedSpeech");
            ConversationMoodProbe.CancelTransport(host);
            heldCompletion = null;
            duplicate("{\"reply\":\"" + previousReply + "\",\"moodDelta\":4}");
            yield return null;

            ConversationMoodView afterFirstDuplicate = store.CaptureConversationMood(evidence.PersonId);
            int transcriptCountAfterDuplicate = CountDurableTranscriptRows(transcriptField, talk);
            NpcExperienceInbox inboxAfterDuplicate = store.Snapshot().NpcExperienceInbox;
            bool observedSpeechStillPending = ReferenceEquals(ReadHostField(host, "conversationObservedSpeech"), observed);
            Action<string> repair = heldCompletion;
            object repairRequest = lastRequest;
            bool heldWithoutMutation = afterFirstDuplicate.Value == moodValueBefore &&
                afterFirstDuplicate.AcceptedTurnSequence == sequenceBefore &&
                store.CaptureNpcExperienceInboxRevision() == inboxRevisionBefore &&
                observedSpeechStillPending && inboxAfterDuplicate != null &&
                inboxAfterDuplicate.Entries.FindAll(entry => entry != null && entry.ReceiptId == observed.Evidence.ReceiptId).Count == 0 &&
                ConversationMoodProbe.CountExchange(store, evidence.PersonId, duplicatePlayerText,
                    previousReply, "model") == duplicateExchangeBefore &&
                transcriptCountAfterDuplicate == transcriptCountBefore && repair != null &&
                !ReferenceEquals(repair, duplicate);
            if (!heldWithoutMutation)
            {
                bool mutationCommitted = afterFirstDuplicate.Value != moodValueBefore ||
                    afterFirstDuplicate.AcceptedTurnSequence != sequenceBefore ||
                    store.CaptureNpcExperienceInboxRevision() != inboxRevisionBefore ||
                    !observedSpeechStillPending ||
                    ConversationMoodProbe.CountExchange(store, evidence.PersonId, duplicatePlayerText,
                        previousReply, "model") > duplicateExchangeBefore ||
                    transcriptCountAfterDuplicate != transcriptCountBefore;
                Require(mutationCommitted,
                    "Exact duplicate did not mutate state and no controlled fresh repair was captured; this is not the expected RED result.");
                evidence.ExpectedRed = true;
                evidence.ExpectedRedEvidence = "first-duplicate-mutated=true; mood=" + moodValueBefore + "->" + afterFirstDuplicate.Value +
                    "; sequence=" + sequenceBefore + "->" + afterFirstDuplicate.AcceptedTurnSequence +
                    "; inbox-revision=" + inboxRevisionBefore + "->" + store.CaptureNpcExperienceInboxRevision() +
                    "; transcript-rows=" + transcriptCountBefore + "->" + transcriptCountAfterDuplicate +
                "; repair-captured=" + (repair != null) + "; accepted-backend-completion=0; baseline-restored=true.";
                yield break;
            }

            string avoidDuplicate = ReadOptionalStringMember(repairRequest, "AvoidDuplicateSocialReply");
            Require(repairRequest != null && ReadBoolMember(repairRequest, "MoodEnabled") && avoidDuplicate == previousReply,
                "Fresh social repair did not retain mood mode and the exact prior wording exclusion.");
            long repairRequests = ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount");
            ConversationMoodProbe.CancelTransport(host);
            repair("{\"reply\":\"" + repairReply + "\",\"moodDelta\":2}");
            yield return null;

            ConversationMoodView afterRepair = store.CaptureConversationMood(evidence.PersonId);
            NpcExperienceInbox finalInbox = store.Snapshot().NpcExperienceInbox;
            int finalTranscriptCount = CountDurableTranscriptRows(transcriptField, talk);
            Require(afterRepair.Value == moodValueBefore + 2 &&
                afterRepair.AcceptedTurnSequence == sequenceBefore + 1 &&
                ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount") == repairRequests &&
                ConversationMoodProbe.CountExchange(store, evidence.PersonId, duplicatePlayerText,
                    previousReply, "model") == 0 &&
                ConversationMoodProbe.CountExchange(store, evidence.PersonId, duplicatePlayerText,
                    repairReply, "model") == 1 &&
                finalInbox != null && finalInbox.Entries.FindAll(entry => entry != null &&
                    entry.ReceiptId == observed.Evidence.ReceiptId &&
                    entry.ObservedTurnText == duplicatePlayerText).Count == 1 &&
                ReadHostField(host, "conversationObservedSpeech") == null &&
                finalTranscriptCount == transcriptCountBefore + 2,
                "Distinct repair did not commit one final social reply and its observed speech exactly once.");
            Require(FindTranscriptPair(transcriptField, talk, duplicatePlayerText, repairReply) &&
                !FindTranscriptPair(transcriptField, talk, duplicatePlayerText, previousReply),
                "Transcript did not contain only the final repair wording for the retried turn.");

            int stableMood = afterRepair.Value;
            long stableSequence = afterRepair.AcceptedTurnSequence;
            long stableInboxRevision = store.CaptureNpcExperienceInboxRevision();
            int stableTranscriptCount = CountDurableTranscriptRows(transcriptField, talk);
            long stableRequestCount = ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount");
            ConversationMoodProbe.CancelTransport(host);
            first("{\"reply\":\"A stale earlier reply.\",\"moodDelta\":9}");
            duplicate("{\"reply\":\"" + previousReply + "\",\"moodDelta\":9}");
            repair("{\"reply\":\"" + repairReply + "\",\"moodDelta\":9}");
            yield return null;
            NpcExperienceInbox replayInbox = store.Snapshot().NpcExperienceInbox;
            Require(store.CaptureConversationMood(evidence.PersonId).Value == stableMood &&
                store.CaptureConversationMood(evidence.PersonId).AcceptedTurnSequence == stableSequence &&
                store.CaptureNpcExperienceInboxRevision() == stableInboxRevision && replayInbox != null &&
                CountObservedReceipt(replayInbox, observed) == 1 &&
                ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount") == stableRequestCount &&
                CountDurableTranscriptRows(transcriptField, talk) == stableTranscriptCount &&
                ConversationMoodProbe.CountExchange(store, evidence.PersonId, firstPlayerText,
                    previousReply, "model") == 1 &&
                ConversationMoodProbe.CountExchange(store, evidence.PersonId, duplicatePlayerText,
                    repairReply, "model") == 1,
                "A stale earlier callback or replayed duplicate/repair callback mutated the accepted turn.");

            yield return RunUnavailableTurn(host, talk, store, client, transcriptField,
                evidence.PersonId, repairReply, "What else stands out to you about this street?", false);
            yield return RunUnavailableTurn(host, talk, store, client, transcriptField,
                evidence.PersonId, repairReply, "What is another pleasant thing about this neighborhood?", true);
            yield return RunInvalidObservedReceiptTurn(host, talk, store, client, transcriptField,
                evidence.PersonId, repairReply);
            yield return SocialDuplicateStreamingProbe.Run(host, talk);
        }

        private static IEnumerator RunUnavailableTurn(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            WorldStateStore store, LocalDialogueClient client, FieldInfo transcriptField, string personId,
            string acceptedLastReply, string playerText, bool retryUnavailable)
        {
            string beforeHistory = ReadConversationHistory(talk);
            Require(DialogueHistoryWindow.LastNpcText(beforeHistory) == acceptedLastReply,
                "Unavailable test precondition lost the latest accepted NPC reply.");
            ConversationMoodView moodBefore = store.CaptureConversationMood(personId);
            long inboxRevisionBefore = store.CaptureNpcExperienceInboxRevision();
            int transcriptBefore = CountDurableTranscriptRows(transcriptField, talk);
            long requestsBefore = ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount");
            Action<string> firstAttempt = StartHeldReply(host, talk, playerText, out _);
            var observed = (NpcObservedSpeechReceipt)ReadHostField(host, "conversationObservedSpeech");
            ConversationMoodProbe.CancelTransport(host);
            heldCompletion = null;
            firstAttempt(SocialResult(acceptedLastReply, 7));
            yield return null;

            Action<string> retry = heldCompletion;
            object retryRequest = lastRequest;
            ConversationMoodView afterFirstDuplicate = store.CaptureConversationMood(personId);
            NpcExperienceInbox afterFirstInbox = store.Snapshot().NpcExperienceInbox;
            Require(retry != null && retryRequest != null && !ReferenceEquals(retry, firstAttempt) &&
                ReadBoolMember(retryRequest, "MoodEnabled") &&
                ReadOptionalStringMember(retryRequest, "AvoidDuplicateSocialReply") == acceptedLastReply &&
                ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount") == requestsBefore + 2 &&
                afterFirstDuplicate.Value == moodBefore.Value &&
                afterFirstDuplicate.AcceptedTurnSequence == moodBefore.AcceptedTurnSequence &&
                store.CaptureNpcExperienceInboxRevision() == inboxRevisionBefore && afterFirstInbox != null &&
                CountObservedReceipt(afterFirstInbox, observed) == 0 &&
                CountDurableTranscriptRows(transcriptField, talk) == transcriptBefore &&
                ConversationMoodProbe.CountExchange(store, personId, playerText, acceptedLastReply, "model") == 0,
                "Exact duplicate did not dispatch exactly one bounded fresh retry for the unavailable case.");

            ConversationMoodProbe.CancelTransport(host);
            retry(retryUnavailable ? LocalDialogueClient.Unavailable : SocialResult(acceptedLastReply, 8));
            yield return null;

            ConversationMoodView moodAfter = store.CaptureConversationMood(personId);
            NpcExperienceInbox inbox = store.Snapshot().NpcExperienceInbox;
            int transcriptAfter = CountDurableTranscriptRows(transcriptField, talk);
            NpcExperienceInboxEntry observedEntry = FindObservedReceipt(inbox, observed);
            Require(moodAfter.Value == moodBefore.Value && moodAfter.AcceptedTurnSequence == moodBefore.AcceptedTurnSequence &&
                store.CaptureNpcExperienceInboxRevision() == inboxRevisionBefore + 1 && observedEntry != null &&
                observedEntry.ObservedTurnText == playerText &&
                CountObservedReceipt(inbox, observed) == 1 &&
                (observedEntry.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Mood) == 0 &&
                ReadHostField(host, "conversationObservedSpeech") == null &&
                ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount") == requestsBefore + 2 &&
                ConversationMoodProbe.CountExchange(store, personId, playerText, acceptedLastReply, "model") == 0 &&
                ConversationMoodProbe.CountExchange(store, personId, playerText,
                    DialogueReplyFallback.UnavailableReply, "unavailable") == 1 &&
                transcriptAfter == transcriptBefore + 2 &&
                FindTranscriptPair(transcriptField, talk, playerText, DialogueReplyFallback.UnavailableReply) &&
                !FindTranscriptPair(transcriptField, talk, playerText, acceptedLastReply),
                "Duplicate retry failure did not settle once as unavailable and record the actual player speech without mood application.");
            Require(DialogueHistoryWindow.LastNpcText(ReadConversationHistory(talk)) == acceptedLastReply,
                "An unavailable row replaced the prior accepted NPC reply as LastNpcText.");
        }

        private static IEnumerator RunInvalidObservedReceiptTurn(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, WorldStateStore store, LocalDialogueClient client,
            FieldInfo transcriptField, string personId, string acceptedLastReply)
        {
            const string playerText = "Could you share one more thought on city life?";
            const string mismatchedReceiptText = "A different player statement that does not match.";
            const string uniqueCandidate = "A quiet walk can make an ordinary afternoon better.";
            Require(DialogueHistoryWindow.LastNpcText(ReadConversationHistory(talk)) == acceptedLastReply,
                "Invalid-receipt test precondition lost the latest accepted NPC reply.");
            ConversationMoodView moodBefore = store.CaptureConversationMood(personId);
            long inboxRevisionBefore = store.CaptureNpcExperienceInboxRevision();
            int transcriptBefore = CountDurableTranscriptRows(transcriptField, talk);
            long requestsBefore = ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount");
            NpcObservedSpeechReceipt mismatchedReceipt = NpcObservedSpeechReceipt.Create(personId,
                checked(inboxRevisionBefore + 1), mismatchedReceiptText,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            Action<string> completion = StartHeldReply(host, talk, playerText, out _, mismatchedReceipt);
            ConversationMoodProbe.CancelTransport(host);
            completion(SocialResult(uniqueCandidate, 1));
            yield return null;

            ConversationMoodView moodAfter = store.CaptureConversationMood(personId);
            NpcExperienceInbox inbox = store.Snapshot().NpcExperienceInbox;
            int transcriptAfter = CountDurableTranscriptRows(transcriptField, talk);
            Require(moodAfter.Value == moodBefore.Value &&
                moodAfter.AcceptedTurnSequence == moodBefore.AcceptedTurnSequence &&
                store.CaptureNpcExperienceInboxRevision() == inboxRevisionBefore && inbox != null &&
                CountObservedReceipt(inbox, mismatchedReceipt) == 0 &&
                ReadHostField(host, "conversationObservedSpeech") == null &&
                ReadHostField(host, "conversationObservedSpeechTicket") == null &&
                ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount") == requestsBefore + 1 &&
                ConversationMoodProbe.CountExchange(store, personId, playerText, uniqueCandidate, "model") == 0 &&
                ConversationMoodProbe.CountExchange(store, personId, playerText,
                    DialogueReplyFallback.UnavailableReply, "unavailable") == 1 &&
                transcriptAfter == transcriptBefore + 2 &&
                FindTranscriptPair(transcriptField, talk, playerText, DialogueReplyFallback.UnavailableReply) &&
                !FindTranscriptPair(transcriptField, talk, playerText, uniqueCandidate),
                "A valid unique reply with mismatched observed-speech evidence must retain the player turn as unavailable without committing mood or receipt.");
            Require(DialogueHistoryWindow.LastNpcText(ReadConversationHistory(talk)) == acceptedLastReply,
                "An invalid-receipt unavailable row replaced the prior accepted NPC reply as LastNpcText.");
        }

        private static string SocialResult(string reply, int moodDelta)
        { return "{\"reply\":\"" + reply + "\",\"moodDelta\":" + moodDelta.ToString(CultureInfo.InvariantCulture) + "}"; }

        private static int CountObservedReceipt(NpcExperienceInbox inbox, NpcObservedSpeechReceipt observed)
        {
            if (inbox == null || inbox.Entries == null || observed == null || observed.Evidence == null) return 0;
            return inbox.Entries.FindAll(entry => entry != null &&
                entry.ReceiptId == observed.Evidence.ReceiptId).Count;
        }

        private static NpcExperienceInboxEntry FindObservedReceipt(NpcExperienceInbox inbox,
            NpcObservedSpeechReceipt observed)
        {
            if (inbox == null || inbox.Entries == null || observed == null || observed.Evidence == null) return null;
            return inbox.Entries.Find(entry => entry != null &&
                entry.ReceiptId == observed.Evidence.ReceiptId);
        }

        private static string ReadConversationHistory(DaggerfallTalkWindow talk)
        {
            MethodInfo readHistory = typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory", Members);
            Require(readHistory != null && readHistory.IsStatic,
                "Production conversation history reader is missing.");
            return Convert.ToString(readHistory.Invoke(null, new object[] { talk }), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static Action<string> StartHeldReply(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            string playerText, out object request, NpcObservedSpeechReceipt invalidReceipt = null)
        {
            heldCompletion = null;
            lastRequest = null;
            var snapshot = (ConversationContextSnapshot)ConversationMoodProbe.CaptureSnapshot(host, talk, playerText);
            var store = (WorldStateStore)ReadHostField(host, "store");
            var speechSession = ReadHostField(host, "conversationSpeechSession") as NpcObservedSpeechSession;
            string conversationPerson = ReadHostField(host, "conversationPersonId") as string;
            NpcObservedSpeechTicket speechTicket;
            Require(store.IsKnownNpcObservedSpeechSpeaker(snapshot.Binding.PersonId) &&
                conversationPerson == snapshot.Binding.PersonId && speechSession != null &&
                speechSession.SessionId == snapshot.Binding.SessionId,
                "Native Talk has no matching live speech session: known=" +
                store.IsKnownNpcObservedSpeechSpeaker(snapshot.Binding.PersonId) +
                "; hostPerson=" + conversationPerson + "; snapshotPerson=" + snapshot.Binding.PersonId +
                "; hostSession=" + (speechSession == null ? "<null>" : speechSession.SessionId) +
                "; snapshotSession=" + snapshot.Binding.SessionId + ".");
            // SubmitFreeText issues at the native whole-second clock; the context has fractional time.
            Require(store.TryIssueNpcObservedSpeech(speechSession, snapshot.Binding.RequestRevision,
                playerText, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(), out speechTicket) &&
                store.TryBindNpcObservedSpeechContext(speechTicket, snapshot.Binding),
                "Controlled reply did not receive an exact issued speech source.");
            RequireField(typeof(PeopleOfTheIliacBayMod), "conversationObservedSpeechTicket").SetValue(host, speechTicket);
            RequireField(typeof(PeopleOfTheIliacBayMod), "conversationObservedSpeech").SetValue(host,
                invalidReceipt ?? speechTicket.Receipt);
            string history = ReadConversationHistory(talk);
            MethodInfo submit = ConversationMoodProbe.RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitSocialText", 9);
            RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy").SetValue(host, false);
            long requestsBefore = ConversationMoodProbe.ReadLongProperty(
                ReadHostField(host, "localDialogue"), "ReplyRequestCount");
            submit.Invoke(host, new object[] { talk, playerText, history, null, string.Empty,
                null, false, null, snapshot });
            Action<string> callback = heldCompletion;
            request = lastRequest;
            Require(callback != null && request != null && ReadBoolMember(request, "MoodEnabled") &&
                ConversationMoodProbe.ReadLongProperty(ReadHostField(host, "localDialogue"), "ReplyRequestCount") == requestsBefore + 1,
                "Production mood-enabled social completion was not captured as one fresh request.");
            return callback;
        }

        private static bool FindTranscriptPair(FieldInfo transcriptField, DaggerfallTalkWindow talk,
            string playerText, string npcText)
        {
            ListBox transcript = ReadTranscript(transcriptField, talk);
            for (int index = 0; index + 1 < transcript.Count; index++)
            {
                ListBox.ListItem player = transcript.GetItem(index);
                ListBox.ListItem npc = transcript.GetItem(index + 1);
                if (player != null && npc != null && player.textLabel != null && npc.textLabel != null &&
                    player.textLabel.Text == playerText && npc.textLabel.Text == npcText) return true;
            }
            return false;
        }

        private static FieldInfo ReadTranscriptField(PeopleOfTheIliacBayMod host)
        {
            var field = ConversationMoodProbe.ReadHostField(host, "conversationListField") as FieldInfo;
            Require(field != null, "Production Talk transcript field is unavailable.");
            return field;
        }

        private static int CountDurableTranscriptRows(FieldInfo transcriptField, DaggerfallTalkWindow talk)
        {
            ListBox transcript = ReadTranscript(transcriptField, talk);
            string pendingName = (string)RequireField(typeof(PeopleOfTheIliacBayMod),
                "PendingTranscriptRowName").GetRawConstantValue();
            int count = 0;
            for (int index = 0; index < transcript.Count; index++)
            {
                ListBox.ListItem item = transcript.GetItem(index);
                if (item == null || item.textLabel == null || item.textLabel.Name != pendingName) count++;
            }
            return count;
        }

        private static ListBox ReadTranscript(FieldInfo transcriptField, DaggerfallTalkWindow talk)
        {
            var transcript = transcriptField.GetValue(talk) as ListBox;
            Require(transcript != null, "Native Talk transcript is unavailable.");
            return transcript;
        }

        private static object ReadHostField(PeopleOfTheIliacBayMod host, string name)
        { return ConversationMoodProbe.ReadHostField(host, name); }

        private static FieldInfo RequireField(Type type, string name)
        { return ConversationMoodProbe.RequireField(type, name); }

        private static bool ReadBoolMember(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, Members);
            if (property != null) return Convert.ToBoolean(property.GetValue(target, null), CultureInfo.InvariantCulture);
            FieldInfo field = target.GetType().GetField(name, Members);
            if (field != null) return Convert.ToBoolean(field.GetValue(target), CultureInfo.InvariantCulture);
            throw new InvalidOperationException("Required social duplicate request member is missing: " + name);
        }

        private static string ReadOptionalStringMember(object target, string name)
        {
            if (target == null) return null;
            PropertyInfo property = target.GetType().GetProperty(name, Members);
            if (property != null) return Convert.ToString(property.GetValue(target, null), CultureInfo.InvariantCulture);
            FieldInfo field = target.GetType().GetField(name, Members);
            return field == null ? null : Convert.ToString(field.GetValue(target), CultureInfo.InvariantCulture);
        }

        private static IEnumerator DriveEnumeratorTree(IEnumerator root)
        {
            var stack = new List<IEnumerator>();
            if (root != null) stack.Add(root);
            try
            {
                while (stack.Count > 0)
                {
                    int top = stack.Count - 1;
                    IEnumerator current = stack[top];
                    if (!current.MoveNext())
                    {
                        stack.RemoveAt(top);
                        IDisposable completed = current as IDisposable;
                        if (completed != null) completed.Dispose();
                        continue;
                    }
                    IEnumerator nested = current.Current as IEnumerator;
                    if (nested != null) stack.Add(nested);
                    else yield return current.Current;
                }
            }
            finally
            {
                while (stack.Count > 0)
                {
                    int top = stack.Count - 1;
                    IEnumerator pending = stack[top];
                    stack.RemoveAt(top);
                    IDisposable disposable = pending as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
            }
        }

        private static void DisposeTree(IEnumerator iterator, ref Exception failure)
        {
            IDisposable disposable = iterator as IDisposable;
            if (disposable == null) return;
            try { disposable.Dispose(); }
            catch (Exception error) { if (failure == null) failure = error; }
        }

        private static void Require(bool condition, string message)
        { ConversationMoodProbe.Require(condition, message); }
    }
}
