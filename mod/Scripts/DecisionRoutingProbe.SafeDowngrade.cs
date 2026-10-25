using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class DecisionRoutingProbe
    {
        private const string ControlledRouteModelId = "controlled-native-offer-route-v1";
        private const string SafeDowngradeTurn = "You are a fool, and I do not respect you.";

        private sealed class ControlledOfferRouteTransport : ILocalDecisionTransport
        {
            private int completed;
            internal int CompletedCalls => Volatile.Read(ref completed);

            public LocalDecisionHttpResponse Send(Uri endpoint, byte[] bytes, int timeoutMilliseconds,
                CancellationToken cancellationToken)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new LocalDecisionHttpResponse(null, null, NpcDecisionFailureKind.Transport, "cancelled");
                var response = "{\"model\":\"" + ControlledRouteModelId +
                    "\",\"answers\":{\"decision\":{\"type\":\"choice\",\"choice\":\"OFFER_RESPONSE\"," +
                    "\"probabilities\":{\"SOCIAL\":0.0,\"ACTION_REQUEST\":0.0,\"OFFER_RESPONSE\":1.0},\"confidence\":1.0}}}";
                Interlocked.Increment(ref completed);
                return new LocalDecisionHttpResponse(200, response, NpcDecisionFailureKind.None);
            }
        }

        private static IEnumerator RunSafeDowngradeCore(PeopleOfTheIliacBayMod host,
            StreetPopulation streets, string journalPath)
        {
            GameManager game = GameManager.Instance;
            WorldStateStore store = ReadHostStore(host);
            var client = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogue", InstanceMembers).GetValue(host) as LocalDialogueClient;
            Require(host && streets && streets.Ready && game && game.IsReady && game.PlayerMotor && store != null && client != null &&
                !client.IsBusy && ReadBool(host, "localDialogueEnabled") &&
                Convert.ToInt32(RequireField(typeof(PeopleOfTheIliacBayMod), "activeExperienceBackgroundTicket",
                    InstanceMembers).GetValue(host), CultureInfo.InvariantCulture) == 0,
                "Controlled downgrade requires a ready owned game, enabled idle Qwen, and no running background appraisal.");
            Require(ReadBool(host, "conversationDispositionEnabled"),
                "Controlled downgrade requires the normal conversation disposition path enabled.");

            ResidentActor actor = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host, game.PlayerMotor.transform.position,
                value => actor = value, requireNeutral: false);
            Require(actor && actor.Resident != null, "No stable resident is available for the controlled offer fixture.");
            string personId = actor.Resident.Id;
            ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
            SuppressGreeting(host);
            textFeatureSet(host, true);
            textPrewarmSet(host, false);
            var busy = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy", InstanceMembers);
            busy.SetValue(host, false);
            DaggerfallTalkWindow talk = null;
            yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
            Require(talk != null && DaggerfallUI.UIManager.TopWindow == talk && ReadPerson(host) == personId &&
                object.ReferenceEquals(ReadHostStore(host), store),
                "Controlled downgrade failed to open Talk on the selected stable resident.");

            WorldState before = store.Snapshot();
            Require(before != null && before.NpcExperienceInbox != null && before.NpcExperienceInbox.Entries != null &&
                store.ActiveNpcAction(personId) == null && store.ActiveNpcTask(personId) == null,
                "Controlled downgrade requires an initialized inbox and no active saved action/task.");
            FieldInfo pendingOfferField = RequireField(typeof(PeopleOfTheIliacBayMod), "pendingConversationAction", InstanceMembers);
            FieldInfo pendingHistoryField = RequireField(typeof(PeopleOfTheIliacBayMod), "pendingConversationActionHistory", InstanceMembers);
            Require(pendingOfferField.GetValue(host) == null, "Controlled downgrade started with an unrelated pending offer.");
            var candidates = streets.ConversationActionCandidates(personId);
            NpcActionOption follow = null;
            foreach (NpcActionOption option in candidates)
                if (option != null && option.Kind == NpcActionKind.Follow && option.Target != null)
                { follow = option; break; }
            Require(follow != null, "No currently valid native Follow candidate exists for the controlled offer fixture.");
            int memorySession = Convert.ToInt32(RequireField(typeof(PeopleOfTheIliacBayMod), "memorySession", InstanceMembers)
                .GetValue(host), CultureInfo.InvariantCulture);
            double offerCreatedAt = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var offer = new NpcActionOffer(personId, memorySession, offerCreatedAt,
                new NpcActionDecision { Option = follow, DurationSeconds = 60 });
            MethodInfo readHistory = RequireMethod(typeof(PeopleOfTheIliacBayMod), "ReadConversationHistory", StaticMembers, 1);
            string historyBefore = Convert.ToString(readHistory.Invoke(null, new object[] { talk }), CultureInfo.InvariantCulture) ?? string.Empty;
            pendingOfferField.SetValue(host, offer);
            pendingHistoryField.SetValue(host, historyBefore);
            AppendJournal(journalPath, "controlled_offer_fixture", "\"origin\":\"controlled native NpcActionOffer factory; not NPC initiative\",\"personId\":\"" +
                Json(personId) + "\",\"session\":" + memorySession.ToString(CultureInfo.InvariantCulture) +
                ",\"kind\":\"Follow\",\"optionId\":" + follow.Id.ToString(CultureInfo.InvariantCulture) +
                ",\"label\":\"" + Json(follow.Label) + "\",\"durationSeconds\":60,\"history\":\"" + Json(historyBefore) + "\"");

            NpcActionOption liveOfferBefore;
            int liveDurationBefore;
            Require(offer.TryGetSemanticCandidate(personId, memorySession, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                out liveOfferBefore, out liveDurationBefore) && liveOfferBefore.Id == follow.Id && liveDurationBefore == 60,
                "Controlled offer fixture is not a live, validated native candidate before the test turn.");

            string[] turnTexts = { SafeDowngradeTurn };
            HashSet<string> beforeKeys = InboxKeys(before);
            int transcriptBefore = TranscriptCount(talk);
            int archiveBefore = CountArchivedPlayerTurns(store, personId, SafeDowngradeTurn);
            Require(archiveBefore == 0, "Controlled downgrade turn already exists in the resident archive.");
            Vector3 playerPositionBefore = game.PlayerMotor.transform.position;
            Vector3 actorPositionBefore = actor.transform.position;
            long replyRequestsBefore = client.ReplyRequestCount;
            long replyCompletionsBefore = client.ReplyCompletionCount;
            var classifierRows = new List<string>();
            int replyTraceCount = 0;
            DialogueRequest replyRequest = null;
            string replyRequestJson = null;
            string replyRaw = null;
            string replyExtracted = null;
            Action<string, string, string> oldClassifierTrace = client.ForegroundClassificationTrace;
            Action<DialogueRequest, string, string, string> oldReplyTrace = client.ForegroundReplyTrace;
            client.ForegroundClassificationTrace = (request, raw, parsed) =>
            {
                string row = "\"requestJson\":\"" + Json(request) + "\",\"rawResponse\":\"" + Json(raw) +
                    "\",\"classification\":\"" + Json(parsed) + "\"";
                classifierRows.Add(row);
                AppendJournal(journalPath, "qwen_classifier", row);
                if (oldClassifierTrace != null) oldClassifierTrace(request, raw, parsed);
            };
            client.ForegroundReplyTrace = (request, requestJson, raw, extracted) =>
            {
                replyTraceCount++;
                replyRequest = request;
                replyRequestJson = requestJson;
                replyRaw = raw;
                replyExtracted = extracted;
                AppendJournal(journalPath, "qwen_final_reply", "\"requestJson\":\"" + Json(requestJson) +
                    "\",\"rawResponse\":\"" + Json(raw) + "\",\"extracted\":\"" + Json(extracted) + "\"");
                if (oldReplyTrace != null)
                {
                    try { oldReplyTrace(request, requestJson, raw, extracted); }
                    catch (Exception error) { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SAFE_DOWNGRADE_TRACE_CHAIN_FAILURE: " + error.GetType().Name); }
                }
            };

            var controlledTransport = new ControlledOfferRouteTransport();
            var recordingTransport = new RecordingTransport(controlledTransport);
            var session = new LocalDecisionSession(recordingTransport)
            { ExpectedModelId = ControlledRouteModelId, Enabled = true };
            string acceptedReply = null;
            Exception turnFailure = null;
            var turn = new NestedEnumeratorDriver(SubmitSafeDowngradeTurn(host, talk, session,
                recordingTransport, client, replyCompletionsBefore, transcriptBefore));
            try
            {
                object yielded;
                while (turn.TryMoveNext(out yielded)) yield return yielded;
                turnFailure = turn.Failure;
                acceptedReply = ReadRememberedReply(talk, SafeDowngradeTurn, transcriptBefore);
            }
            finally
            {
                turn.Dispose();
                client.ForegroundClassificationTrace = oldClassifierTrace;
                client.ForegroundReplyTrace = oldReplyTrace;
                RequireMethod(typeof(PeopleOfTheIliacBayMod), "CancelDecisionShadow", InstanceMembers, 0).Invoke(host, null);
                session.Dispose();
            }

            RecordingTransport.Result route = recordingTransport.LastResult;
            string routeRequest = route == null ? null : route.RequestJson;
            string routeResponse = route == null ? null : route.ResponseBody;
            AppendJournal(journalPath, "controlled_d3_route", "\"fixture\":true,\"model\":\"" + ControlledRouteModelId +
                "\",\"input\":\"" + Json(SafeDowngradeTurn) + "\",\"originalTurn\":\"" +
                Json(route == null ? null : route.OriginalTurn) + "\",\"selectedRoute\":\"" +
                Json(route == null || route.Parsed == null ? null : route.Parsed.SelectedOptionId) +
                "\",\"requestJson\":\"" + Json(routeRequest) + "\",\"rawResponse\":\"" + Json(routeResponse) +
                "\",\"controlledTransportCalls\":" + controlledTransport.CompletedCalls.ToString(CultureInfo.InvariantCulture));

            string refinement = null;
            int refinementCount = 0;
            bool proposalClassifierSeen = false;
            bool offerInterpreterSeen = false;
            bool actionClassifierSeen = false;
            foreach (string row in classifierRows)
            {
                proposalClassifierSeen |= row.IndexOf("Decide whether this NPC wants to OFFER a concrete action", StringComparison.Ordinal) >= 0;
                offerInterpreterSeen |= row.IndexOf("Exact pending offer:", StringComparison.Ordinal) >= 0;
                actionClassifierSeen |= row.IndexOf("You are deciding this NPC's response to the current player.", StringComparison.Ordinal) >= 0;
                if (refinement == null && row.IndexOf("D3 route refinement constraint:", StringComparison.Ordinal) >= 0)
                {
                    try { refinement = ParseJournalClassification(row); }
                    catch { refinement = null; }
                }
                if (row.IndexOf("D3 route refinement constraint:", StringComparison.Ordinal) >= 0) refinementCount++;
            }
            ConversationSelection refined = ConversationKnowledge.ParseRouting(refinement);
            WorldState after = store.Snapshot();
            List<NpcExperienceInboxEntry> speechRows = FindNewSpeechRows(after, personId, turnTexts, beforeKeys);
            int archiveAfter = CountArchivedPlayerTurns(store, personId, SafeDowngradeTurn);
            NpcMemoryEntry archiveEntry = FindArchivedPlayerTurn(store, personId, SafeDowngradeTurn);
            string currentHistory = Convert.ToString(readHistory.Invoke(null, new object[] { talk }), CultureInfo.InvariantCulture) ?? string.Empty;
            NpcActionOption liveOfferAfter;
            int liveDurationAfter;
            bool currentOfferIntact = object.ReferenceEquals(pendingOfferField.GetValue(host), offer) &&
                string.Equals(Convert.ToString(pendingHistoryField.GetValue(host), CultureInfo.InvariantCulture), historyBefore,
                    StringComparison.Ordinal) && offer.TryGetSemanticCandidate(personId, memorySession,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(), out liveOfferAfter, out liveDurationAfter) &&
                liveOfferAfter.Id == follow.Id && liveDurationAfter == 60;
            bool exactPair = CountTranscriptPair(talk, SafeDowngradeTurn, acceptedReply) == 1 &&
                CountOccurrences(currentHistory, "Player: " + SafeDowngradeTurn) == 1 &&
                CountOccurrences(currentHistory, acceptedReply) == 1;
            AppendJournal(journalPath, "safe_downgrade_assertion_inputs", "\"refinement\":\"" + Json(refinement) +
                "\",\"refinementCount\":" + refinementCount.ToString(CultureInfo.InvariantCulture) +
                ",\"reply\":\"" + Json(acceptedReply) + "\",\"replyRequests\":" +
                (client.ReplyRequestCount - replyRequestsBefore).ToString(CultureInfo.InvariantCulture) +
                ",\"replyCompletions\":" + (client.ReplyCompletionCount - replyCompletionsBefore).ToString(CultureInfo.InvariantCulture) +
                ",\"replyTraceCount\":" + replyTraceCount.ToString(CultureInfo.InvariantCulture) +
                ",\"proposalClassifierSeen\":" + proposalClassifierSeen.ToString().ToLowerInvariant() +
                ",\"offerInterpreterSeen\":" + offerInterpreterSeen.ToString().ToLowerInvariant() +
                ",\"actionClassifierSeen\":" + actionClassifierSeen.ToString().ToLowerInvariant() +
                ",\"offerIntact\":" + currentOfferIntact.ToString().ToLowerInvariant() +
                ",\"newSpeechRows\":" + speechRows.Count.ToString(CultureInfo.InvariantCulture) +
                ",\"archiveCount\":" + archiveAfter.ToString(CultureInfo.InvariantCulture) +
                ",\"archiveSource\":\"" + Json(archiveEntry == null ? null : archiveEntry.Source) +
                "\",\"exactTranscriptPair\":" + exactPair.ToString().ToLowerInvariant());

            if (turnFailure != null) throw turnFailure;
            Require(recordingTransport.CompletedCalls == 1 && route != null && route.Parsed != null && route.Parsed.IsValid &&
                route.OriginalTurn == SafeDowngradeTurn && route.Parsed.SelectedOptionId == "OFFER_RESPONSE" &&
                ControlledRequestMatchesOffer(routeRequest, personId, SafeDowngradeTurn, follow.Kind.ToString(), follow.Label),
                "Controlled D3 fixture did not bind OFFER_RESPONSE to the exact current unrelated insult and live offer.");
            Require(client.ReplyRequestCount == replyRequestsBefore + 1 && client.ReplyCompletionCount == replyCompletionsBefore + 1 &&
                replyTraceCount == 1 && replyRequest != null && replyRequest.PlayerText == SafeDowngradeTurn &&
                replyRequest.SocialOnly && replyRequest.ActionReply == null && !string.IsNullOrWhiteSpace(replyRequestJson) &&
                !string.IsNullOrWhiteSpace(replyRaw) && !string.IsNullOrWhiteSpace(replyExtracted) &&
                acceptedReply != "<missing>" && acceptedReply != DialogueReplyFallback.UnavailableReply,
                "Safe downgrade did not produce exactly one real Qwen social reply for the exact turn.");
            Require(refinementCount == 1 && refined != null && refined.Intent == "social" && refined.SocialIntent == "none" &&
                (refined.Tone == "rude" || refined.Tone == "insult") &&
                LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade("OFFER_RESPONSE", refined),
                "Qwen did not safely refine the controlled offer route to an ordinary social turn.");
            Require(!proposalClassifierSeen && !offerInterpreterSeen && !actionClassifierSeen,
                "Social downgrade entered pending-offer interpretation or action proposal classification.");
            Require(currentOfferIntact && store.ActiveNpcAction(personId) == null && store.ActiveNpcTask(personId) == null &&
                Vector3.Distance(playerPositionBefore, game.PlayerMotor.transform.position) < 0.01f &&
                Vector3.Distance(actorPositionBefore, actor.transform.position) < 0.01f,
                "Social downgrade consumed/replaced the offer or changed action/task/movement state.");
            Require(speechRows.Count == 1 && speechRows[0].ObservedTurnText == SafeDowngradeTurn &&
                !string.IsNullOrWhiteSpace(speechRows[0].DeduplicationKey) && archiveAfter == archiveBefore + 1 &&
                archiveEntry != null && archiveEntry.PlayerText == SafeDowngradeTurn && archiveEntry.NpcText == acceptedReply &&
                exactPair,
                "Safe social turn did not produce exactly one attributable speech receipt, archive row, and transcript pair.");
            Require(ReadPerson(host) == personId && object.ReferenceEquals(ReadHostStore(host), store),
                "Safe downgrade changed resident or save-owned store binding.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_CONTROLLED_ROUTE_PASS: input=exact; " +
                "route=OFFER_RESPONSE; qwen-refinement=ordinary-social; final-reply=one; offer=preserved; " +
                "saved-action-task=none; speech-source=one; journal=" + journalPath + ".");
        }

        private static bool ControlledRequestMatchesOffer(string requestJson, string personId, string originalTurn,
            string offerKind, string offerLabel)
        {
            try
            {
                FullSerializer.fsData root = FullSerializer.fsJsonParser.Parse(requestJson);
                FullSerializer.fsData state = FullSerializer.fsJsonParser.Parse(root.AsDictionary["state"].AsString);
                if (state.AsDictionary["actorId"].AsString != personId || state.AsDictionary["originalTurn"].AsString != originalTurn)
                    return false;
                FullSerializer.fsData context = FullSerializer.fsJsonParser.Parse(state.AsDictionary["context"].AsString);
                var offer = context.AsDictionary["pendingOffer"].AsDictionary;
                return offer["kind"].AsString == offerKind && offer["status"].AsString == "pending" &&
                    offer["label"].AsString == offerLabel;
            }
            catch { return false; }
        }

        private static IEnumerator SubmitSafeDowngradeTurn(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            LocalDecisionSession session, RecordingTransport transport, LocalDialogueClient client,
            long replyCompletionsBefore, int transcriptBefore)
        {
            RequireMethod(typeof(PeopleOfTheIliacBayMod), "InstallDecisionRoutingForProbe", InstanceMembers, 2)
                .Invoke(host, new object[] { session, new LocalDialogueConnection("http://127.0.0.1:1/controlled-route", 10000) });
            MethodInfo submit = RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitFreeText", InstanceMembers, 2);
            submit.Invoke(host, new object[] { talk, SafeDowngradeTurn });
            float deadline = Time.realtimeSinceStartup + 150f;
            while (Time.realtimeSinceStartup < deadline)
            {
                string acceptedReply = ReadRememberedReply(talk, SafeDowngradeTurn, transcriptBefore);
                if (transport.CompletedCalls >= 1 && client.ReplyCompletionCount > replyCompletionsBefore &&
                    acceptedReply != "<missing>" && acceptedReply != DialogueReplyFallback.UnavailableReply) yield break;
                yield return null;
            }
        }

        private static string ParseJournalClassification(string row)
        {
            const string marker = "\"classification\":\"";
            int start = row.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            int end = row.IndexOf("\"", start, StringComparison.Ordinal);
            if (end < 0) return null;
            return row.Substring(start, end - start).Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\r", "\r");
        }

        private static int CountOccurrences(string text, string value)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value)) return 0;
            int count = 0;
            for (int start = 0; ;)
            {
                int index = text.IndexOf(value, start, StringComparison.Ordinal);
                if (index < 0) return count;
                count++;
                start = index + value.Length;
            }
        }

        private static int CountArchivedPlayerTurns(WorldStateStore store, string personId, string text)
        {
            int count = 0;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(personId,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 2, 256))
                if (entry != null && entry.Source != "observed" && entry.PlayerText == text) count++;
            return count;
        }

        private static NpcMemoryEntry FindArchivedPlayerTurn(WorldStateStore store, string personId, string text)
        {
            NpcMemoryEntry match = null;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(personId,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 2, 256))
                if (entry != null && entry.Source != "observed" && entry.PlayerText == text)
                {
                    if (match != null) return null;
                    match = entry;
                }
            return match;
        }

        private static int CountTranscriptPair(DaggerfallTalkWindow talk, string playerText, string npcText)
        {
            var field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", InstanceMembers);
            var transcript = field == null ? null : field.GetValue(talk) as ListBox;
            if (transcript == null) return 0;
            int count = 0;
            for (int index = 0; index + 1 < transcript.Count; index++)
            {
                var player = transcript.GetItem(index);
                var reply = transcript.GetItem(index + 1);
                if (player != null && player.textLabel != null && reply != null && reply.textLabel != null &&
                    player.textColor == DaggerfallUI.DaggerfallQuestionTextColor && player.textLabel.Text == playerText &&
                    reply.textColor != DaggerfallUI.DaggerfallQuestionTextColor && reply.textLabel.Text == npcText)
                    count++;
            }
            return count;
        }
    }
}
