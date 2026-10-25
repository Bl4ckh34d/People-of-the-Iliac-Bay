using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        internal const string AppraisalReopenSwitch = "-peopleoftheiliacbay-decision-routing-appraisal-reopen-probe";
        internal const string SafeDowngradeSwitch = "-peopleoftheiliacbay-decision-routing-safe-downgrade-probe";
        internal const string AppraisalReopenSpeechAcceptanceSwitch = "-peopleoftheiliacbay-appraisal-reopen-speech-probe";
        private const string AppraisalReopenBaseline = "People - Gothway Garden";

        private sealed class AppraisalTraceRow
        {
            internal string Request;
            internal string Raw;
            internal string Parsed;
        }

        private sealed class NativeAttitudeSnapshot
        {
            internal bool Exists;
            internal int Offset;
            internal int NativeBaseline;
            internal bool HasNativeBaseline;
        }

        [Serializable]
        private sealed class QwenPromptPayload
        {
            public string prompt = string.Empty;
        }

        private sealed class NestedEnumeratorDriver : IDisposable
        {
            private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
            internal Exception Failure;

            internal NestedEnumeratorDriver(IEnumerator root)
            {
                if (root == null) throw new ArgumentNullException(nameof(root));
                stack.Push(root);
            }

            internal bool TryMoveNext(out object current)
            {
                current = null;
                try
                {
                    while (stack.Count > 0)
                    {
                        IEnumerator top = stack.Peek();
                        if (!top.MoveNext())
                        {
                            stack.Pop();
                            (top as IDisposable)?.Dispose();
                            continue;
                        }
                        current = top.Current;
                        IEnumerator nested = current as IEnumerator;
                        if (nested != null) { stack.Push(nested); continue; }
                        return true;
                    }
                    return false;
                }
                catch (Exception error)
                {
                    Failure = error;
                    Dispose();
                    return false;
                }
            }

            public void Dispose()
            {
                while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            }
        }

        private static IEnumerator RunAppraisalReopen(PeopleOfTheIliacBayMod host,
            StreetPopulation streets, string character)
        {
            GameManager game = GameManager.Instance;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), AppraisalReopenSwitch) >= 0 &&
                character == OwnedCharacter && ArgumentValue("-peopleoftheiliacbay-fixture-character") == OwnedCharacter &&
                ArgumentValue("-peopleoftheiliacbay-fixture-save") == AppraisalReopenBaseline,
                "Native appraisal-reopen requires its opt-in flag and exact owned character/save arguments.");
            Require(host && streets && game && game.IsReady && game.PlayerEntity != null &&
                game.PlayerEntity.Name == OwnedCharacter && streets.Ready && DaggerfallUI.UIManager != null,
                "Native appraisal-reopen requires the ready owned disposable fixture.");
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, AppraisalReopenBaseline) >= 0 &&
                !game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.Name == "Gothway Garden",
                "Native appraisal-reopen must start at the exact owned exterior baseline.");

            FieldInfo scheduler = RequireField(typeof(PeopleOfTheIliacBayMod), "experienceAppraisalsSuspended", InstanceMembers);
            PropertyInfo trace = typeof(PeopleOfTheIliacBayMod).GetProperty("ExperienceAppraisalTrace", InstanceMembers);
            Require(trace != null && trace.CanRead && trace.CanWrite && trace.PropertyType == typeof(Action<string, string, string>),
                "Native appraisal-reopen cannot attach the existing passive appraisal trace.");
            bool originalSuspended = Convert.ToBoolean(scheduler.GetValue(host), CultureInfo.InvariantCulture);
            object originalTrace = trace.GetValue(host, null);
            Require(!originalSuspended && originalTrace == null,
                "Native appraisal-reopen requires an enabled scheduler and an unused diagnostic trace.");

            bool baselineLoadAttempted = false;
            Exception probeFailure = null;
            Exception restoreFailure = null;
            bool restored = false;
            bool safeDowngrade = Array.IndexOf(Environment.GetCommandLineArgs(), SafeDowngradeSwitch) >= 0;
            bool speechAcceptance = Array.IndexOf(Environment.GetCommandLineArgs(), AppraisalReopenSpeechAcceptanceSwitch) >= 0;
            Require(!speechAcceptance || (!safeDowngrade &&
                Array.IndexOf(Environment.GetCommandLineArgs(), AppraisalReopenSwitch) >= 0),
                "Speech acceptance requires the existing appraisal-reopen probe and cannot combine with safe-downgrade.");
            appraisalReopenSpeechTurnsPassed = 0;
            string journalPath = safeDowngrade ? CreateSafeDowngradeJournal() : CreateJournal();
            try
            {
                scheduler.SetValue(host, true);
                baselineLoadAttempted = true;
                var load = new NestedEnumeratorDriver(ConversationMoodProbe.LoadSave(host, streets,
                    OwnedCharacter, AppraisalReopenBaseline, true));
                try
                {
                    object yielded;
                    while (load.TryMoveNext(out yielded)) yield return yielded;
                    probeFailure = load.Failure;
                }
                finally { load.Dispose(); }

                if (probeFailure == null)
                {
                    scheduler.SetValue(host, false);
                    IEnumerator core = safeDowngrade
                        ? RunSafeDowngradeCore(host, streets, journalPath)
                        : RunAppraisalReopenCore(host, streets, trace, originalTrace, journalPath, speechAcceptance);
                    var phase = new NestedEnumeratorDriver(core);
                    try
                    {
                        object yielded;
                        while (phase.TryMoveNext(out yielded)) yield return yielded;
                        probeFailure = phase.Failure;
                    }
                    finally { phase.Dispose(); }
                }
            }
            finally
            {
                scheduler.SetValue(host, true);
                trace.SetValue(host, originalTrace, null);
                if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
            }

            if (baselineLoadAttempted)
            {
                var restore = new NestedEnumeratorDriver(RestoreAppraisalReopenBaseline(host, streets));
                try
                {
                    object yielded;
                    while (restore.TryMoveNext(out yielded)) yield return yielded;
                    restoreFailure = restore.Failure;
                    restored = restoreFailure == null;
                }
                finally { restore.Dispose(); }
                if (restored)
                {
                    scheduler.SetValue(host, originalSuspended);
                    trace.SetValue(host, originalTrace, null);
                    Debug.Log((safeDowngrade ? "PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_BASELINE_RESTORED: " :
                        "PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_BASELINE_RESTORED: ") + "character=" +
                        OwnedCharacter + "; save=" + AppraisalReopenBaseline + "; exactExterior=true; journal=" + journalPath + ".");
                    AppendJournal(journalPath, "baseline_restored", "\"character\":\"" + Json(OwnedCharacter) +
                        "\",\"save\":\"" + Json(AppraisalReopenBaseline) + "\",\"exactExterior\":true");
                }
                else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_RESTORE_FAILURE: journal=" + journalPath + ".");
            }

            if (probeFailure != null) TryAppendJournalFailure(journalPath, "probe_failure", probeFailure);
            if (restoreFailure != null) TryAppendJournalFailure(journalPath, "restore_failure", restoreFailure);

            if (probeFailure != null && restoreFailure != null)
                throw new AggregateException("Native D3/Qwen appraisal-reopen and baseline restoration both failed.", probeFailure, restoreFailure);
            if (probeFailure != null) throw probeFailure;
            if (restoreFailure != null) throw restoreFailure;
            Require(restored, "Native appraisal-reopen did not restore the exact owned baseline.");
            if (speechAcceptance)
            {
                Require(appraisalReopenSpeechTurnsPassed == 3,
                    "Speech acceptance baseline restored, but fewer than three full-WAV turns completed and drained.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPRAISAL_REOPEN_SPEECH_PASS: turns=3; allMixerReceipts=true; " +
                    "allRequestsCompleted=true; allPlaybackDrained=true; baselineRestored=true; journal=" + journalPath + ".");
            }
            if (safeDowngrade)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SAFE_DOWNGRADE_PASS: route=controlled-OFFER_RESPONSE; " +
                    "qwen-social-refinement=true; one-social-reply=true; offer-action-preserved=true; baselineRestored=true; journal=" + journalPath + ".");
            else
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_PASS: turns=3; initialReceipts=2; " +
                    "normalScheduler=true; sameResidentReopened=true; firstOutputsRetained=true; baselineRestored=true; journal=" + journalPath + ".");
        }

        private static IEnumerator RunAppraisalReopenCore(PeopleOfTheIliacBayMod host,
            StreetPopulation streets, PropertyInfo trace, object originalTrace, string journalPath, bool speechAcceptance)
        {
            GameManager game = GameManager.Instance;
            var store = RequireField(typeof(PeopleOfTheIliacBayMod), "store", InstanceMembers).GetValue(host) as WorldStateStore;
            Require(store != null && !Convert.ToBoolean(RequireField(typeof(PeopleOfTheIliacBayMod),
                "experienceAppraisalsSuspended", InstanceMembers).GetValue(host)),
                "Normal appraiser or active save-owned Core store is unavailable.");
            var client = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogue", InstanceMembers).GetValue(host) as LocalDialogueClient;
            Require(client && !client.IsBusy && Convert.ToBoolean(RequireField(typeof(PeopleOfTheIliacBayMod),
                "localDialogueEnabled", InstanceMembers).GetValue(host)) &&
                Convert.ToInt32(RequireField(typeof(PeopleOfTheIliacBayMod), "activeExperienceBackgroundTicket",
                    InstanceMembers).GetValue(host), CultureInfo.InvariantCulture) == 0,
                "Native appraisal-reopen requires enabled and idle local Qwen dialogue.");
            bool dispositionEnabled = ReadBool(host, "conversationDispositionEnabled");
            AppendJournal(journalPath, "disposition_contract", "\"conversationDispositionEnabled\":" +
                dispositionEnabled.ToString().ToLowerInvariant() + ",\"requiredEffectsMask\":\"Mood|Affinity\"");
            Require(dispositionEnabled,
                "Native Mood|Affinity first-application coverage requires the existing ConversationDisposition setting enabled.");

            WorldState before = store.Snapshot();
            Require(before != null && before.NpcExperienceInbox != null && before.NpcExperienceInbox.Entries != null,
                "Native appraisal-reopen requires an initialized experience inbox.");
            ResidentActor actor = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host, game.PlayerMotor.transform.position,
                value => actor = value, requireNeutral: false);
            uint seed = 0;
            int initialFaction = -1;
            bool protectedResident = true;
            Require(actor && actor.Resident != null && store.TryReadNpcPersonalityIdentity(actor.Resident.Id,
                out seed, out initialFaction, out protectedResident) && !protectedResident &&
                seed == actor.Resident.AppearanceSeed,
                "Selected actor lacks a saved stable resident identity or is quest protected.");

            string personId = actor.Resident.Id;
            int targetPendingBefore = CountPendingForObserver(before, personId);
            int unrelatedPendingBefore = CountPendingOtherObservers(before, personId);
            AppendJournal(journalPath, "pending_scope_before", "\"personId\":\"" + Json(personId) +
                "\",\"targetPending\":" + targetPendingBefore.ToString(CultureInfo.InvariantCulture) +
                ",\"unrelatedPending\":" + unrelatedPendingBefore.ToString(CultureInfo.InvariantCulture));
            Require(targetPendingBefore == 0,
                "Native appraisal-reopen refuses to consume pre-existing pending evidence for its selected observer.");
            string initialProfile = ProfileFingerprint(store.ReadNpcPersonality(personId));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_SELECTED: person=" + personId +
                "; seed=" + seed.ToString(CultureInfo.InvariantCulture) + "; nativeFaction=" +
                initialFaction.ToString(CultureInfo.InvariantCulture) + "; profileBefore=" + (initialProfile ?? "absent") +
                "; pendingBefore=0.");
            AppendJournal(journalPath, "resident_baseline", "\"personId\":\"" + Json(personId) + "\",\"seed\":" +
                seed.ToString(CultureInfo.InvariantCulture) + ",\"nativeFaction\":" + initialFaction.ToString(CultureInfo.InvariantCulture) +
                ",\"profileBefore\":\"" + Json(initialProfile ?? "absent") + "\",\"pendingBefore\":0");

            bool oldDialogueEnabled = ReadBool(host, "localDialogueEnabled");
            bool oldPrewarmEnabled = ReadBool(host, "dialoguePrewarmEnabled");
            bool oldGreetingPending = ReadBool(host, "returningGreetingPending");
            float oldGreetingRetry = ReadFloat(host, "returningGreetingRetryAt");
            bool oldBusy = ReadBool(host, "conversationBusy");
            BreezeTtsTrialBridge speechBridge = speechAcceptance
                ? RequireSpeechAcceptanceBridge(host) : null;
            var liveTransport = new RecordingTransport(new LocalDecisionTransport());
            string expectedModelId = ResolveD3ModelId();
            var liveSession = new LocalDecisionSession(liveTransport) { ExpectedModelId = expectedModelId, Enabled = true };
            var liveConnection = new LocalDialogueConnection("http://127.0.0.1:8093/completion", 10000);
            var traces = new List<AppraisalTraceRow>();
            string traceJournalFailure = null;
            Action<string, string, string> traceObserver = (request, raw, parsed) =>
            {
                lock (traces) traces.Add(new AppraisalTraceRow { Request = request, Raw = raw, Parsed = parsed });
                try { AppendJournal(journalPath, "background_appraisal", "\"requestJson\":\"" + Json(request) +
                    "\",\"rawResponse\":\"" + Json(raw) + "\",\"parsedResponse\":\"" + Json(parsed) + "\""); }
                catch (Exception error)
                {
                    lock (traces) traceJournalFailure = error.ToString();
                    Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_JOURNAL_FAILURE: " + error);
                }
            };
            DaggerfallTalkWindow talk = null;
            bool talkOpen = false;
            try
            {
                ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
                textFeatureSet(host, true);
                textPrewarmSet(host, false);
                SuppressGreeting(host);
                if (speechAcceptance) RequireSpeechAcceptanceIdle(speechBridge, "before initial Talk");
                RequireMethod(typeof(PeopleOfTheIliacBayMod), "InstallDecisionRoutingForProbe", InstanceMembers, 2)
                    .Invoke(host, new object[] { liveSession, liveConnection });
                string requestBeforeInitialTalk = speechAcceptance ? speechBridge.LastQueuedRequestId : null;
                yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
                talkOpen = true;
                Require(talk != null && string.Equals(ReadPerson(host), personId, StringComparison.Ordinal),
                    "Native Talk did not bind the selected stable resident.");
                if (speechAcceptance)
                    yield return DrainExcludedOpeningSpeech(speechBridge, requestBeforeInitialTalk, personId, journalPath);

                ConversationContextSnapshot initialContext = ConversationMoodProbe.CaptureSnapshot(host, talk,
                    "How has your day been so far?") as ConversationContextSnapshot;
                Require(initialContext != null && initialContext.Binding.PersonId == personId,
                    "Initial native context snapshot is not bound to the selected resident.");
                RequireProfileIdentity(store.ReadNpcPersonality(personId), personId, seed, initialFaction, null, false);
                long saveEpochBefore = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                    "conversationSaveEpoch", InstanceMembers).GetValue(host), CultureInfo.InvariantCulture);
                long inboxRevisionBefore = store.CaptureNpcExperienceInboxRevision();
                string moodAnchorBeforeTurns = MoodAnchor(store.Snapshot(), personId);
                string relationshipBefore = RelationshipFingerprint(store.ReadNpcRelationship(personId,
                    NpcDirectedRelationship.PlayerTargetId));
                int dispositionBefore = initialContext.EffectiveDisposition;
                NativeAttitudeSnapshot attitudeBeforeTurns = ReadAttitudeSnapshot(store.Snapshot(), personId);
                AppendJournal(journalPath, "conversation_anchor_before", "\"moodAnchor\":\"" + Json(moodAnchorBeforeTurns) +
                    "\",\"relationship\":\"" + Json(relationshipBefore) + "\",\"disposition\":" +
                    dispositionBefore.ToString(CultureInfo.InvariantCulture) + ",\"attitudeOffset\":" +
                    attitudeBeforeTurns.Offset.ToString(CultureInfo.InvariantCulture) + ",\"nativeBaseline\":" +
                    attitudeBeforeTurns.NativeBaseline.ToString(CultureInfo.InvariantCulture) + ",\"hasNativeBaseline\":" +
                    attitudeBeforeTurns.HasNativeBaseline.ToString().ToLowerInvariant() + ",\"attitudeExists\":" +
                    attitudeBeforeTurns.Exists.ToString().ToLowerInvariant());
                var acceptedTurns = new[]
                {
                    "How has your day been so far?",
                    "You are a fool, and I do not respect you."
                };
                WorldState beforeTurns = store.Snapshot();
                HashSet<string> beforeTurnKeys = InboxKeys(beforeTurns);
                for (int i = 0; i < acceptedTurns.Length; i++)
                {
                    yield return speechAcceptance
                        ? SubmitLiveSocialTurnWithSpeechAcceptance(host, talk, liveSession, liveTransport,
                            acceptedTurns[i], expectedModelId, journalPath, speechBridge, i + 1, personId)
                        : SubmitLiveSocialTurn(host, talk, liveSession, liveTransport, acceptedTurns[i], expectedModelId, journalPath, false);
                    Require(ReadPerson(host) == personId && object.ReferenceEquals(ReadHostStore(host), store),
                        "Live conversation changed resident binding or save-owned store.");
                    Require(Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSaveEpoch",
                        InstanceMembers).GetValue(host), CultureInfo.InvariantCulture) == saveEpochBefore,
                        "Live conversation changed the current save epoch.");
                    if (i == 0) RequireProfileIdentity(store.ReadNpcPersonality(personId), personId, seed,
                        initialFaction, null, false);
                }

                WorldState afterTurns = store.Snapshot();
                List<NpcExperienceInboxEntry> speechRows = FindNewSpeechRows(afterTurns, personId, acceptedTurns, beforeTurnKeys);
                int targetPendingAfterTurns = CountPendingForObserver(afterTurns, personId);
                int targetReceiptPendingAfterTurns = CountPendingForRows(afterTurns, speechRows);
                int unrelatedPendingAfterTurns = CountPendingOtherObservers(afterTurns, personId);
                NativeAttitudeSnapshot attitudeAfterTurns = ReadAttitudeSnapshot(afterTurns, personId);
                bool allImmediateAffinityKnown = speechRows.Count == acceptedTurns.Length;
                int immediateAffinityAfterTurns = 0;
                var immediateAffinityRows = new List<string>();
                foreach (NpcExperienceInboxEntry row in speechRows)
                {
                    allImmediateAffinityKnown &= row.HasKnownImmediateAffinityDelta &&
                        Enum.IsDefined(typeof(NpcObservedSpeechAffinityExclusion), row.AffinityExclusion) &&
                        row.ImmediateAffinityDelta >= -NpcObservedSpeechReceipt.NativeAffinityDeltaBound &&
                        row.ImmediateAffinityDelta <= NpcObservedSpeechReceipt.NativeAffinityDeltaBound;
                    if (row.HasKnownImmediateAffinityDelta) immediateAffinityAfterTurns += row.ImmediateAffinityDelta;
                    immediateAffinityRows.Add(row.DeduplicationKey + "=" + row.HasKnownImmediateAffinityDelta + ":" +
                        row.ImmediateAffinityDelta.ToString(CultureInfo.InvariantCulture) + ":" + row.AffinityExclusion);
                }
                AppendJournal(journalPath, "pending_scope_after_turns", "\"targetPending\":" +
                    targetPendingAfterTurns.ToString(CultureInfo.InvariantCulture) + ",\"unrelatedPending\":" +
                    unrelatedPendingAfterTurns.ToString(CultureInfo.InvariantCulture) + ",\"globalInboxRevision\":" +
                    afterTurns.NpcExperienceInbox.Revision.ToString(CultureInfo.InvariantCulture) + ",\"newSpeechRows\":" +
                    speechRows.Count.ToString(CultureInfo.InvariantCulture) + ",\"targetReceiptPending\":" +
                    targetReceiptPendingAfterTurns.ToString(CultureInfo.InvariantCulture) + ",\"attitudeOffsetBefore\":" +
                    attitudeBeforeTurns.Offset.ToString(CultureInfo.InvariantCulture) + ",\"attitudeOffsetAfter\":" +
                    attitudeAfterTurns.Offset.ToString(CultureInfo.InvariantCulture) + ",\"immediateAffinityRows\":\"" +
                    Json(string.Join(";", immediateAffinityRows.ToArray())) + "\",\"immediateAffinitySum\":" +
                    immediateAffinityAfterTurns.ToString(CultureInfo.InvariantCulture));
                Require(afterTurns.NpcExperienceInbox.Revision >= inboxRevisionBefore + acceptedTurns.Length &&
                    speechRows.Count == acceptedTurns.Length && targetReceiptPendingAfterTurns == acceptedTurns.Length &&
                    allImmediateAffinityKnown && attitudeBeforeTurns.Exists && attitudeBeforeTurns.HasNativeBaseline &&
                    attitudeAfterTurns.Exists && attitudeAfterTurns.HasNativeBaseline &&
                    attitudeAfterTurns.Offset - attitudeBeforeTurns.Offset == immediateAffinityAfterTurns,
                    "The two live speech receipts failed receipt binding, source-affinity metadata, or native offset accounting.");
                foreach (NpcExperienceInboxEntry row in speechRows)
                    Require(row.Origin == NpcExperienceEventOrigin.AttributedSpeech &&
                        row.AlreadyAppliedEffectsMask == (NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity) &&
                        row.Status == NpcExperienceInboxStatus.Pending && row.ObservedTurnSpeakerId == NpcDirectedRelationship.PlayerTargetId,
                        "Accepted speech receipt is missing its actual speaker or already-applied effect mask.");
                string moodAnchorAfterTurns = MoodAnchor(afterTurns, personId);
                string profileAfterTurns = ProfileFingerprint(store.ReadNpcPersonality(personId));
                RequireProfileIdentity(store.ReadNpcPersonality(personId), personId, seed, initialFaction, null, false);
                RequireStableIdentity(store, personId, seed);
                string relationshipAfterTurns = RelationshipFingerprint(store.ReadNpcRelationship(personId,
                    NpcDirectedRelationship.PlayerTargetId));
                trace.SetValue(host, traceObserver, null);
                AppendJournal(journalPath, "accepted_speech_receipts", "\"personId\":\"" + Json(personId) +
                    "\",\"rows\":\"" + Json(string.Join(";", speechRows.ConvertAll(row => row.DeduplicationKey + "=" + row.ObservedTurnText).ToArray())) +
                    "\",\"moodAnchor\":\"" + Json(moodAnchorAfterTurns) + "\",\"profile\":\"" + Json(profileAfterTurns ?? "absent") +
                    "\",\"attitudeOffsetBeforeTurns\":" + attitudeBeforeTurns.Offset.ToString(CultureInfo.InvariantCulture) +
                    ",\"attitudeOffsetAfterTurns\":" + attitudeAfterTurns.Offset.ToString(CultureInfo.InvariantCulture) +
                    ",\"immediateAffinityRows\":\"" + Json(string.Join(";", immediateAffinityRows.ToArray())) + "\"");

                yield return ConversationMoodProbe.CloseTalk(host);
                talkOpen = false;
                float appraisalDeadline = Time.realtimeSinceStartup + 120f;
                int priorTraceCount = 0;
                while (Time.realtimeSinceStartup < appraisalDeadline)
                {
                    Require(ReferenceEquals(ReadHostStore(host), store) && Convert.ToInt64(RequireField(
                        typeof(PeopleOfTheIliacBayMod), "conversationSaveEpoch", InstanceMembers).GetValue(host),
                        CultureInfo.InvariantCulture) == saveEpochBefore,
                        "Appraisal drain changed the save-owned store or save epoch.");
                    WorldState current = store.Snapshot();
                    int consumed = CountConsumedForRows(current, speechRows);
                    int traceCount = CountTracesForRows(traces, speechRows);
                    if (traceCount > priorTraceCount)
                    {
                        yield return null;
                        current = store.Snapshot();
                        consumed = CountConsumedForRows(current, speechRows);
                        traceCount = CountTracesForRows(traces, speechRows);
                        string journalFailure;
                        lock (traces) journalFailure = traceJournalFailure;
                        if (journalFailure != null)
                        {
                            RequireField(typeof(PeopleOfTheIliacBayMod), "experienceAppraisalsSuspended", InstanceMembers)
                                .SetValue(host, true);
                            throw new InvalidOperationException("Raw background appraisal trace could not be persisted; suspended before retry. " +
                                journalFailure);
                        }
                        if (consumed < traceCount)
                        {
                            RequireField(typeof(PeopleOfTheIliacBayMod), "experienceAppraisalsSuspended", InstanceMembers)
                                .SetValue(host, true);
                            throw new InvalidOperationException("A completed background appraisal left its first receipt pending; suspended before scheduler retry. " +
                                "traceCount=" + traceCount.ToString(CultureInfo.InvariantCulture) + "; consumed=" +
                                consumed.ToString(CultureInfo.InvariantCulture) + ".");
                        }
                        if (traceCount > speechRows.Count)
                            throw new InvalidOperationException("Unexpected extra first appraisal response arrived before reopen.");
                        priorTraceCount = traceCount;
                    }
                    if (consumed == speechRows.Count) break;
                    yield return null;
                }
                WorldState afterAppraisals = store.Snapshot();
                int targetPendingAfterAppraisals = CountPendingForObserver(afterAppraisals, personId);
                int targetReceiptPendingAfterAppraisals = CountPendingForRows(afterAppraisals, speechRows);
                int unrelatedPendingAfterAppraisals = CountPendingOtherObservers(afterAppraisals, personId);
                int targetTraceCount = CountTracesForRows(traces, speechRows);
                int allTraceCount;
                lock (traces) allTraceCount = traces.Count;
                AppendJournal(journalPath, "pending_scope_after_appraisals", "\"targetPending\":" +
                    targetPendingAfterAppraisals.ToString(CultureInfo.InvariantCulture) + ",\"unrelatedPending\":" +
                    unrelatedPendingAfterAppraisals.ToString(CultureInfo.InvariantCulture) + ",\"targetTraceCount\":" +
                    targetTraceCount.ToString(CultureInfo.InvariantCulture) + ",\"allTraceCount\":" + allTraceCount.ToString(CultureInfo.InvariantCulture) +
                    ",\"targetReceiptPending\":" + targetReceiptPendingAfterAppraisals.ToString(CultureInfo.InvariantCulture));
                Require(CountConsumedForRows(afterAppraisals, speechRows) == speechRows.Count &&
                    targetReceiptPendingAfterAppraisals == 0,
                    "Normal idle scheduler did not consume both exact live speech receipts before the bounded deadline.");
                Require(targetTraceCount == speechRows.Count,
                    "Expected exactly one first background appraisal trace per speech receipt.");
                lock (traces)
                {
                    foreach (NpcExperienceInboxEntry row in speechRows)
                    {
                        int matches = 0;
                        foreach (AppraisalTraceRow item in traces)
                            if (item.Request != null && item.Request.IndexOf(row.DeduplicationKey, StringComparison.Ordinal) >= 0)
                            {
                                matches++;
                                string immediateMetadata = "Native immediate affinity already applied; recorded signed delta=" +
                                    (row.HasKnownImmediateAffinityDelta
                                        ? row.ImmediateAffinityDelta.ToString(CultureInfo.InvariantCulture) : "unknown") + ".";
                                Require(item.Request.IndexOf(LocalDialogueCompletion.EscapeJson(immediateMetadata),
                                    StringComparison.Ordinal) >= 0,
                                    "Background appraisal request omitted the exact saved immediate-affinity provenance for its receipt.");
                                if (row.AffinityExclusion == NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor)
                                    Require(item.Request.IndexOf(LocalDialogueCompletion.EscapeJson(
                                        "Native family-threat disposition floor is active; affinityDelta must be exactly 0."),
                                        StringComparison.Ordinal) >= 0,
                                        "Background appraisal request omitted the saved family-threat affinity exclusion.");
                            }
                        Require(matches == 1, "First background appraisal request did not correlate exactly once to its speech receipt.");
                    }
                }
                Require(MoodAnchor(afterAppraisals, personId) == moodAnchorAfterTurns,
                    "Appraisal changed the persisted mood anchor after the same turns already applied mood.");
                List<NpcExperienceInboxEntry> appraisedRows = FindRows(afterAppraisals, speechRows);
                NativeAttitudeSnapshot attitudeAfterAppraisals = ReadAttitudeSnapshot(afterAppraisals, personId);
                bool appraisedAffinityValid = appraisedRows.Count == speechRows.Count;
                int supplementalAffinitySum = 0;
                var supplementalAffinityRows = new List<string>();
                foreach (NpcExperienceInboxEntry receipt in appraisedRows)
                {
                    NpcExperienceInboxEntry source = speechRows.Find(row => row.DeduplicationKey == receipt.DeduplicationKey);
                    int supplemental = receipt.Appraisal == null ? 0 : receipt.Appraisal.AppliedAffinityDelta;
                    supplementalAffinitySum += supplemental;
                    supplementalAffinityRows.Add(receipt.DeduplicationKey + "=" + supplemental.ToString(CultureInfo.InvariantCulture));
                    appraisedAffinityValid &= source != null && receipt.HasKnownImmediateAffinityDelta &&
                        receipt.ImmediateAffinityDelta == source.ImmediateAffinityDelta &&
                        receipt.AffinityExclusion == source.AffinityExclusion && receipt.Appraisal != null &&
                        receipt.Appraisal.AppliedMoodDelta == 0 && supplemental <= 0 &&
                        (supplemental == 0 || receipt.AffinityExclusion == NpcObservedSpeechAffinityExclusion.None &&
                            receipt.Appraisal.SpeechTargetPersonId == personId &&
                            (receipt.Appraisal.SpeechActKind == NpcExperienceSpeechActKind.DirectInsult &&
                                supplemental >= -NpcExperienceAppraisalContract.DirectInsultAffinityLossBound ||
                             receipt.Appraisal.SpeechActKind == NpcExperienceSpeechActKind.DirectThreat &&
                                supplemental >= -NpcExperienceAppraisalContract.DirectThreatAffinityLossBound)) &&
                        (receipt.AffinityExclusion != NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor || supplemental == 0);
                }
                AppendJournal(journalPath, "affinity_appraisal_accounting", "\"offsetBeforeAppraisal\":" +
                    attitudeAfterTurns.Offset.ToString(CultureInfo.InvariantCulture) + ",\"offsetAfterAppraisal\":" +
                    attitudeAfterAppraisals.Offset.ToString(CultureInfo.InvariantCulture) + ",\"sourceImmediateAffinitySum\":" +
                    immediateAffinityAfterTurns.ToString(CultureInfo.InvariantCulture) + ",\"supplementalAffinityRows\":\"" +
                    Json(string.Join(";", supplementalAffinityRows.ToArray())) + "\",\"supplementalAffinitySum\":" +
                    supplementalAffinitySum.ToString(CultureInfo.InvariantCulture) + ",\"receiptMetadataRetained\":" +
                    appraisedAffinityValid.ToString().ToLowerInvariant());
                Require(appraisedAffinityValid && attitudeAfterAppraisals.Exists && attitudeAfterAppraisals.HasNativeBaseline &&
                    attitudeAfterAppraisals.Offset - attitudeAfterTurns.Offset == supplementalAffinitySum,
                    "Consumed appraisals changed native attitude beyond their receipt-bound negative severe-speech supplements.");
                RequireStableIdentity(store, personId, seed);
                NpcSocialBackground? resolvedAfterAppraisal = ResolveCurrentBackground(store, personId);
                RequireProfileIdentity(store.ReadNpcPersonality(personId), personId, seed, initialFaction,
                    resolvedAfterAppraisal, true);
                string relationshipAfterAppraisals = RelationshipFingerprint(store.ReadNpcRelationship(personId,
                    NpcDirectedRelationship.PlayerTargetId));
                bool hasDispositionAfterAppraisal = store.TryGetActualConversationDisposition(personId, out int dispositionAfterAppraisal);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_APPRAISED: person=" + personId +
                    "; inboxRevision=" + afterTurns.NpcExperienceInbox.Revision.ToString(CultureInfo.InvariantCulture) + "->" +
                    afterAppraisals.NpcExperienceInbox.Revision.ToString(CultureInfo.InvariantCulture) +
                    "; moodAnchorUnchanged=true; profileBefore=" + (initialProfile ?? "absent") +
                    "; profileAfter=" + profileAfterTurns + "; relationshipBeforeTurns=" + relationshipBefore +
                    "; relationshipAfterTurns=" + relationshipAfterTurns + "; relationshipAfterAppraisal=" +
                    relationshipAfterAppraisals + "; dispositionBefore=" + dispositionBefore.ToString(CultureInfo.InvariantCulture) +
                    "; dispositionAfterKnown=" + hasDispositionAfterAppraisal + "; dispositionAfter=" +
                    dispositionAfterAppraisal.ToString(CultureInfo.InvariantCulture) + "; rawAppraisalCount=" + traces.Count + ".");
                LogAppraisalTraces(traces, personId);
                AppendJournal(journalPath, "appraisals_consumed", "\"personId\":\"" + Json(personId) +
                    "\",\"moodAnchorUnchanged\":true,\"relationshipBefore\":\"" + Json(relationshipBefore) +
                    "\",\"relationshipAfterTurns\":\"" + Json(relationshipAfterTurns) +
                    "\",\"relationshipAfterAppraisal\":\"" + Json(relationshipAfterAppraisals) + "\"");

                actor = FindActor(personId);
                Require(actor != null, "Same native resident is no longer available for Talk reopen.");
                ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
                string requestBeforeReopenedTalk = speechAcceptance ? speechBridge.LastQueuedRequestId : null;
                yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
                talkOpen = true;
                Require(talk != null && string.Equals(ReadPerson(host), personId, StringComparison.Ordinal),
                    "Reopened Talk did not bind the same native resident.");
                if (speechAcceptance)
                    yield return DrainExcludedOpeningSpeech(speechBridge, requestBeforeReopenedTalk, personId, journalPath);
                RequireStableIdentity(store, personId, seed);
                const string apology = "I would like to apologize for how I spoke earlier.";
                ConversationContextSnapshot reopenedContext = ConversationMoodProbe.CaptureSnapshot(host, talk, apology) as ConversationContextSnapshot;
                Require(reopenedContext != null && reopenedContext.Binding.PersonId == personId &&
                    reopenedContext.PersistentMood != null,
                    "Reopened native context omitted the same resident's persisted mood binding.");
                ConversationMoodView projectedMood = store.CaptureConversationMood(personId, reopenedContext.CapturedGameTime);
                Require(reopenedContext.PersistentMood.Value == projectedMood.Value &&
                    reopenedContext.PersistentMood.StateRevision == projectedMood.Revision &&
                    reopenedContext.PersistentMood.AcceptedTurnSequence == projectedMood.AcceptedTurnSequence,
                    "Reopened context mood does not match the store projection at its actual snapshot time.");
                NpcDirectedRelationshipView relationshipAtReopen = store.ReadNpcRelationship(personId,
                    NpcDirectedRelationship.PlayerTargetId, reopenedContext.CapturedGameTime);
                Require(RelationshipFingerprint(reopenedContext.DirectedRelationship) == RelationshipFingerprint(relationshipAtReopen),
                    "Reopened context relationship differs from the persisted directed relationship.");
                bool hasDisposition = store.TryGetActualConversationDisposition(personId, out int actualDisposition);
                if (hasDisposition) Require(reopenedContext.EffectiveDisposition == actualDisposition,
                    "Reopened context disposition differs from persisted effective affinity.");
                MethodInfo readHistory = typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory", StaticMembers);
                string history = Convert.ToString(readHistory == null ? null : readHistory.Invoke(null, new object[] { talk }),
                    CultureInfo.InvariantCulture) ?? string.Empty;
                Require(history.IndexOf(acceptedTurns[0], StringComparison.Ordinal) >= 0 &&
                    history.IndexOf(acceptedTurns[1], StringComparison.Ordinal) >= 0,
                    "Reopened native conversation history omitted one of the two real accepted exchanges.");
                WorldState beforeThird = store.Snapshot();
                HashSet<string> beforeThirdKeys = InboxKeys(beforeThird);
                NativeAttitudeSnapshot attitudeBeforeThird = ReadAttitudeSnapshot(beforeThird, personId);
                int targetPendingBeforeThird = CountPendingForObserver(beforeThird, personId);
                int unrelatedPendingBeforeThird = CountPendingOtherObservers(beforeThird, personId);
                AppendJournal(journalPath, "pending_scope_before_third", "\"targetPending\":" +
                    targetPendingBeforeThird.ToString(CultureInfo.InvariantCulture) + ",\"unrelatedPending\":" +
                    unrelatedPendingBeforeThird.ToString(CultureInfo.InvariantCulture));
                yield return speechAcceptance
                    ? SubmitLiveSocialTurnWithSpeechAcceptance(host, talk, liveSession, liveTransport,
                        apology, expectedModelId, journalPath, speechBridge, 3, personId)
                    : SubmitLiveSocialTurn(host, talk, liveSession, liveTransport, apology, expectedModelId, journalPath, false);
                WorldState afterThird = store.Snapshot();
                List<NpcExperienceInboxEntry> thirdRows = FindNewSpeechRows(afterThird, personId, new[] { apology }, beforeThirdKeys);
                int targetPendingAfterThird = CountPendingForObserver(afterThird, personId);
                int targetReceiptPendingAfterThird = CountPendingForRows(afterThird, thirdRows);
                int unrelatedPendingAfterThird = CountPendingOtherObservers(afterThird, personId);
                NativeAttitudeSnapshot attitudeAfterThird = ReadAttitudeSnapshot(afterThird, personId);
                bool thirdImmediateAffinityKnown = thirdRows.Count == 1 && thirdRows[0].HasKnownImmediateAffinityDelta &&
                    Enum.IsDefined(typeof(NpcObservedSpeechAffinityExclusion), thirdRows[0].AffinityExclusion) &&
                    thirdRows[0].ImmediateAffinityDelta >= -NpcObservedSpeechReceipt.NativeAffinityDeltaBound &&
                    thirdRows[0].ImmediateAffinityDelta <= NpcObservedSpeechReceipt.NativeAffinityDeltaBound;
                int thirdImmediateAffinityDelta = thirdRows.Count == 1 && thirdRows[0].HasKnownImmediateAffinityDelta
                    ? thirdRows[0].ImmediateAffinityDelta : 0;
                AppendJournal(journalPath, "pending_scope_after_third", "\"targetPending\":" +
                    targetPendingAfterThird.ToString(CultureInfo.InvariantCulture) + ",\"unrelatedPending\":" +
                    unrelatedPendingAfterThird.ToString(CultureInfo.InvariantCulture) + ",\"newSpeechRows\":" +
                    thirdRows.Count.ToString(CultureInfo.InvariantCulture) + ",\"targetReceiptPending\":" +
                    targetReceiptPendingAfterThird.ToString(CultureInfo.InvariantCulture) + ",\"attitudeOffsetBefore\":" +
                    attitudeBeforeThird.Offset.ToString(CultureInfo.InvariantCulture) + ",\"attitudeOffsetAfter\":" +
                    attitudeAfterThird.Offset.ToString(CultureInfo.InvariantCulture) + ",\"hasKnownImmediateAffinityDelta\":" +
                    thirdImmediateAffinityKnown.ToString().ToLowerInvariant() + ",\"immediateAffinityDelta\":" +
                    thirdImmediateAffinityDelta.ToString(CultureInfo.InvariantCulture) + ",\"affinityExclusion\":\"" +
                    (thirdRows.Count == 1 ? thirdRows[0].AffinityExclusion.ToString() : "unavailable") + "\",\"attitudeExistsBefore\":" +
                    attitudeBeforeThird.Exists.ToString().ToLowerInvariant() + ",\"attitudeExistsAfter\":" +
                    attitudeAfterThird.Exists.ToString().ToLowerInvariant());
                Require(thirdRows.Count == 1 && thirdRows[0].Status == NpcExperienceInboxStatus.Pending &&
                    thirdRows[0].AlreadyAppliedEffectsMask == (NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity) &&
                    targetReceiptPendingAfterThird == 1 && thirdImmediateAffinityKnown &&
                    attitudeBeforeThird.Exists && attitudeBeforeThird.HasNativeBaseline &&
                    attitudeAfterThird.Exists && attitudeAfterThird.HasNativeBaseline &&
                    attitudeAfterThird.Offset - attitudeBeforeThird.Offset == thirdImmediateAffinityDelta,
                    "Reopened live apology did not leave its third receipt pending for baseline rollback.");
                Require(ReferenceEquals(ReadHostStore(host), store) && ReadPerson(host) == personId,
                    "Reopened third turn changed the resident binding or save epoch/store.");
                AppendJournal(journalPath, "reopened_third_receipt", "\"personId\":\"" + Json(personId) +
                    "\",\"receiptKey\":\"" + Json(thirdRows[0].DeduplicationKey) + "\",\"status\":\"Pending\"");
            }
            finally
            {
                if (talkOpen && DaggerfallUI.Instance)
                {
                    RequireField(typeof(PeopleOfTheIliacBayMod), "experienceAppraisalsSuspended", InstanceMembers)
                        .SetValue(host, true);
                    DaggerfallUI.Instance.PopToHUD();
                }
                trace.SetValue(host, originalTrace, null);
                try { RequireMethod(typeof(PeopleOfTheIliacBayMod), "CancelDecisionShadow", InstanceMembers, 0).Invoke(host, null); }
                catch { }
                liveSession.Dispose();
                RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogueEnabled", InstanceMembers).SetValue(host, oldDialogueEnabled);
                RequireField(typeof(PeopleOfTheIliacBayMod), "dialoguePrewarmEnabled", InstanceMembers).SetValue(host, oldPrewarmEnabled);
                RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending", InstanceMembers).SetValue(host, oldGreetingPending);
                RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt", InstanceMembers).SetValue(host, oldGreetingRetry);
                RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy", InstanceMembers).SetValue(host, oldBusy);
            }
        }

        private static IEnumerator SubmitLiveSocialTurn(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            LocalDecisionSession session, RecordingTransport transport, string text, string expectedModelId,
            string journalPath, bool speechAcceptance)
        {
            Require(DaggerfallUI.UIManager.TopWindow == talk, "Production free-text submission lost the native Talk window.");
            int transcriptBefore = TranscriptCount(talk);
            int callsBefore = transport.CompletedCalls;
            long stageBefore = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                "conversationContextStageSequence", InstanceMembers).GetValue(host), CultureInfo.InvariantCulture);
            LocalDialogueClient client = RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogue", InstanceMembers)
                .GetValue(host) as LocalDialogueClient;
            Require(client != null, "Foreground Qwen client is unavailable for raw-reply evidence.");
            long qwenRequestsBefore = client.ReplyRequestCount;
            long qwenCompletionsBefore = client.ReplyCompletionCount;
            Action<DialogueRequest, string, string, string> previousTrace = client.ForegroundReplyTrace;
            Action<string,string,string> previousClassifierTrace = client.ForegroundClassificationTrace;
            client.ForegroundClassificationTrace = (request,response,classification) =>
            {
                AppendJournal(journalPath,"foreground_classifier","\"input\":\""+Json(text)+
                    "\",\"requestJson\":\""+Json(request)+"\",\"rawResponse\":\""+Json(response)+
                    "\",\"classification\":\""+Json(classification)+"\"");
                if(previousClassifierTrace != null)previousClassifierTrace(request,response,classification);
            };
            string expectedPersonId = ReadPerson(host);
            DialogueRequest tracedRequest = null;
            string qwenRequestJson = null;
            string qwenRawBody = null;
            string qwenExtracted = null;
            int traceCount = 0;
            ConversationContextSnapshot qwenSnapshot = null;
            ConversationMoodView qwenMoodAtSnapshot = null;
            string qwenRelationshipAtSnapshot = null;
            long qwenSnapshotRequestRevision = -1;
            WorldStateStore qwenSnapshotStore = null;
            string qwenContextCaptureFailure = null;
            client.ForegroundReplyTrace = (request, requestJson, rawBody, extracted) =>
            {
                if (traceCount++ == 0)
                {
                    tracedRequest = request;
                    qwenRequestJson = requestJson;
                    qwenRawBody = rawBody;
                    qwenExtracted = extracted;
                    try
                    {
                        qwenSnapshot = RequireField(typeof(PeopleOfTheIliacBayMod),
                            "conversationContextCurrentSnapshot", InstanceMembers).GetValue(host) as ConversationContextSnapshot;
                        qwenSnapshotRequestRevision = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                            "conversationContextRequestRevision", InstanceMembers).GetValue(host), CultureInfo.InvariantCulture);
                        qwenSnapshotStore = ReadHostStore(host);
                        if (qwenSnapshot != null && qwenSnapshot.Binding != null && qwenSnapshotStore != null &&
                            qwenSnapshot.Binding.PersonId == expectedPersonId)
                        {
                            qwenMoodAtSnapshot = qwenSnapshotStore.CaptureConversationMood(expectedPersonId,
                                qwenSnapshot.CapturedGameTime);
                            qwenRelationshipAtSnapshot = RelationshipFingerprint(qwenSnapshotStore.ReadNpcRelationship(
                                expectedPersonId, NpcDirectedRelationship.PlayerTargetId, qwenSnapshot.CapturedGameTime));
                        }
                        else qwenContextCaptureFailure = "Final Qwen-stage snapshot or same-person store binding was unavailable.";
                    }
                    catch (Exception error) { qwenContextCaptureFailure = error.ToString(); }
                }
                if (previousTrace != null)
                {
                    try { previousTrace(request, requestJson, rawBody, extracted); }
                    catch (Exception error) { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_FOREGROUND_TRACE_CHAIN_FAILURE: " + error.GetType().Name); }
                }
            };
            ConversationContextSnapshot submittedSnapshot = null;
            long submittedRequestRevision = -1;
            string historyBefore = Convert.ToString(RequireMethod(typeof(PeopleOfTheIliacBayMod),
                "ReadConversationHistory", StaticMembers, 1).Invoke(null, new object[] { talk }),
                CultureInfo.InvariantCulture) ?? string.Empty;
            try
            {
                RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitFreeText", InstanceMembers, 2)
                    .Invoke(host, new object[] { talk, text });
                submittedSnapshot = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationContextCurrentSnapshot", InstanceMembers)
                    .GetValue(host) as ConversationContextSnapshot;
                submittedRequestRevision = Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod),
                    "conversationContextRequestRevision", InstanceMembers).GetValue(host), CultureInfo.InvariantCulture);
                float deadline = Time.realtimeSinceStartup + (speechAcceptance ? 300f : 120f);
                while (Time.realtimeSinceStartup < deadline)
                {
                    // Preserve the actual completion even if the first route or
                    // reply violates this case's social contract. Do not cancel
                    // an in-flight classifier merely because its route differs.
                    // A completed template/fallback also fails below immediately;
                    // it does not need a full timeout to prove missing Qwen work.
                    if (HasRememberedReply(talk, text, transcriptBefore)) break;
                    yield return null;
                }
            }
            finally
            {
                client.ForegroundReplyTrace = previousTrace;
                client.ForegroundClassificationTrace = previousClassifierTrace;
            }

            RecordingTransport.Result result = transport.LastResult;
            string recordedTurn = result == null ? "<none>" : result.OriginalTurn ?? "<none>";
            string selectedRoute = result == null || result.Parsed == null ? "<none>" : result.Parsed.SelectedOptionId ?? "<none>";
            string model = result == null || result.Parsed == null ? "<none>" : result.Parsed.ModelId ?? "<none>";
            string acceptedReply = ReadRememberedReply(talk, text, transcriptBefore);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_FOREGROUND: input=" + text +
                "; d3Calls=" + (transport.CompletedCalls - callsBefore).ToString(CultureInfo.InvariantCulture) +
                "; d3Model=" + model + "; d3Route=" + selectedRoute + "; d3TransportTurn=" + recordedTurn +
                "; d3Request=" + (result == null ? "<none>" : result.RequestJson ?? "<none>") +
                "; d3RawResponse=" + (result == null ? "<none>" : result.ResponseBody ?? "<none>") +
                "; qwenRequests=" + (client.ReplyRequestCount - qwenRequestsBefore).ToString(CultureInfo.InvariantCulture) +
                "; qwenCompletions=" + (client.ReplyCompletionCount - qwenCompletionsBefore).ToString(CultureInfo.InvariantCulture) +
                "; qwenRawRequest=" + (qwenRequestJson ?? "<none>") + "; qwenRawResponse=" + (qwenRawBody ?? "<none>") +
                "; qwenExtracted=" + (qwenExtracted ?? "<none>") + "; acceptedQwenReply=" + acceptedReply +
                "; transcript=" + DescribeLatestTranscriptRows(talk, 4) + ".");
            AppendJournal(journalPath, "foreground_turn", "\"input\":\"" + Json(text) + "\",\"d3RequestJson\":\"" +
                Json(result == null ? null : result.RequestJson) + "\",\"d3RawResponse\":\"" +
                Json(result == null ? null : result.ResponseBody) + "\",\"d3Model\":\"" + Json(model) +
                "\",\"d3Route\":\"" + Json(selectedRoute) + "\",\"d3TransportTurn\":\"" + Json(recordedTurn) +
                "\",\"qwenRequestJson\":\"" + Json(qwenRequestJson) + "\",\"qwenRawResponse\":\"" +
                Json(qwenRawBody) + "\",\"qwenExtracted\":\"" + Json(qwenExtracted) + "\",\"acceptedQwenReply\":\"" +
                Json(acceptedReply) + "\",\"qwenRequestCountDelta\":" +
                (client.ReplyRequestCount - qwenRequestsBefore).ToString(CultureInfo.InvariantCulture) +
                ",\"qwenCompletionCountDelta\":" + (client.ReplyCompletionCount - qwenCompletionsBefore).ToString(CultureInfo.InvariantCulture) +
                ",\"traceCount\":" + traceCount.ToString(CultureInfo.InvariantCulture));

            SocialDialogueResult parsedReply;
            parsedReply = null;
            bool validSocialResult = !string.IsNullOrWhiteSpace(qwenExtracted) &&
                SocialDialogueResult.TryParse(qwenExtracted, out parsedReply);
            if (!validSocialResult) parsedReply = null;
            if (qwenSnapshot != null && tracedRequest != null)
                JournalAndValidateActualForegroundContext(journalPath, text, historyBefore, submittedSnapshot,
                    submittedRequestRevision, qwenSnapshot, tracedRequest,
                    qwenRequestJson, qwenSnapshotStore, qwenMoodAtSnapshot, qwenRelationshipAtSnapshot,
                    expectedPersonId, qwenSnapshotRequestRevision, qwenContextCaptureFailure);
            Require(transport.CompletedCalls == callsBefore + 1 && IsSuccessfulRoute(result, expectedModelId) &&
                string.Equals(result.OriginalTurn, text, StringComparison.Ordinal) &&
                string.Equals(result.Parsed.SelectedOptionId, "SOCIAL", StringComparison.Ordinal) &&
                session.CallbackFailureCount == 0 && HasRememberedReply(talk, text, transcriptBefore) &&
                client.ReplyRequestCount == qwenRequestsBefore + 1 &&
                client.ReplyCompletionCount == qwenCompletionsBefore + 1 && traceCount == 1 &&
                tracedRequest != null && qwenSnapshot != null && string.IsNullOrEmpty(qwenContextCaptureFailure) &&
                tracedRequest.SocialOnly && tracedRequest.MoodEnabled &&
                validSocialResult && string.Equals(parsedReply.Reply, acceptedReply, StringComparison.Ordinal),
                "Production SubmitFreeText did not complete one actual D3 SOCIAL route and one valid first Qwen social reply. " +
                "stageBefore=" + stageBefore.ToString(CultureInfo.InvariantCulture) + "; stageNow=" +
                Convert.ToInt64(RequireField(typeof(PeopleOfTheIliacBayMod), "conversationContextStageSequence",
                    InstanceMembers).GetValue(host), CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) +
                "; transcriptBefore=" + transcriptBefore.ToString(CultureInfo.InvariantCulture) + "; qwenRequests=" +
                (client.ReplyRequestCount - qwenRequestsBefore).ToString(CultureInfo.InvariantCulture) + "; qwenCompletions=" +
                (client.ReplyCompletionCount - qwenCompletionsBefore).ToString(CultureInfo.InvariantCulture) +
                "; validSocialResult=" + validSocialResult + "; d3Route=" + selectedRoute + ".");
        }

        private static void JournalAndValidateActualForegroundContext(string journalPath, string text, string historyBefore,
            ConversationContextSnapshot submittedSnapshot, long submittedRequestRevision,
            ConversationContextSnapshot snapshot, DialogueRequest request, string requestJson, WorldStateStore store,
            ConversationMoodView moodAtSnapshot, string relationshipAtSnapshot, string expectedPersonId,
            long expectedRequestRevision, string contextCaptureFailure)
        {
            string canonicalContext = ConversationContextFormatter.ForSocialReply(snapshot, 3072);
            // DialogueRequest normalizes whitespace before transport. Compare
            // that actual representation, while retaining both inputs even if
            // a later assertion fails. Formatting differences are not data loss.
            string normalizedContext = LocalDialogue.NormalizeContext(canonicalContext, int.MaxValue);
            AppendJournal(journalPath, "foreground_context_inputs", "\"input\":\"" + Json(text) +
                "\",\"canonicalContext\":\"" + Json(canonicalContext) +
                "\",\"normalizedContext\":\"" + Json(normalizedContext) +
                "\",\"requestContext\":\"" + Json(request.ConversationReaction) + "\"");
            Require(submittedSnapshot != null && submittedSnapshot.Binding != null &&
                submittedSnapshot.Binding.PersonId == expectedPersonId &&
                submittedSnapshot.Binding.RequestRevision == submittedRequestRevision &&
                snapshot.Binding != null && snapshot.Binding.PersonId == expectedPersonId &&
                snapshot.Binding.RequestRevision == expectedRequestRevision && snapshot.Binding.RequestRevision > 0 &&
                snapshot.Binding.HasSameRequestBinding(submittedSnapshot.Binding) &&
                snapshot.Revision >= submittedSnapshot.Revision &&
                request.PlayerText == text && request.History == historyBefore &&
                request.SocialOnly && request.MoodEnabled && request.ActionReply == null && snapshot.PersistentMood != null,
                "Actual foreground reply request does not match the current same-person social snapshot, turn, history, or mood contract.");
            Require(string.IsNullOrEmpty(contextCaptureFailure) && store != null && moodAtSnapshot != null &&
                moodAtSnapshot.Value == snapshot.PersistentMood.Value &&
                moodAtSnapshot.Revision == snapshot.PersistentMood.StateRevision &&
                moodAtSnapshot.AcceptedTurnSequence == snapshot.PersistentMood.AcceptedTurnSequence &&
                request.MoodValue == moodAtSnapshot.Value && request.Disposition == snapshot.EffectiveDisposition,
                "Actual foreground request factors differ from the read-only post-appraisal view captured at the Qwen-stage snapshot clock. " +
                (contextCaptureFailure ?? string.Empty));
            Require(RelationshipFingerprint(snapshot.DirectedRelationship) == relationshipAtSnapshot,
                "Actual foreground request snapshot relationship differs from the read-only persisted relationship captured before reply commit.");
            Require(request.ConversationReaction != null && request.ConversationReaction.IndexOf(normalizedContext,
                StringComparison.Ordinal) >= 0,
                "Actual DialogueRequest omitted the canonical typed social context for its own snapshot.");
            string serializedContext = LocalDialogueCompletion.EscapeJson(normalizedContext.Replace("<|", "< |"));
            string serializedPlayerText = LocalDialogueCompletion.EscapeJson(request.PlayerText.Replace("<|", "< |"));
            string serializedDisposition = LocalDialogueCompletion.EscapeJson("Disposition: " +
                request.Disposition.ToString(CultureInfo.InvariantCulture) + "\nAuthoritative reaction to this approach:");
            string serializedMood = LocalDialogueCompletion.EscapeJson("Current persistent mood value: " +
                request.MoodValue.ToString(CultureInfo.InvariantCulture) + " on a scale from -100");
            string promptHistoryMatch = MatchFittedHistory(requestJson, request.History);
            AppendJournal(journalPath, "foreground_history_fit", "\"match\":\"" + Json(promptHistoryMatch) +
                "\",\"historyLength\":" + request.History.Length.ToString(CultureInfo.InvariantCulture) +
                ",\"requestJsonLength\":" + (requestJson == null ? 0 : requestJson.Length).ToString(CultureInfo.InvariantCulture));
            Require(!string.IsNullOrEmpty(requestJson) && requestJson.IndexOf(serializedContext, StringComparison.Ordinal) >= 0 &&
                promptHistoryMatch != "unmatched" &&
                requestJson.IndexOf(serializedPlayerText, StringComparison.Ordinal) >= 0 &&
                requestJson.IndexOf(serializedDisposition, StringComparison.Ordinal) >= 0 &&
                requestJson.IndexOf(serializedMood, StringComparison.Ordinal) >= 0,
                "Raw foreground Qwen request does not contain its exact current canonical context/player text or a valid production-fitted history representation.");
            AppendJournal(journalPath, "foreground_context", "\"personId\":\"" + Json(expectedPersonId) +
                "\",\"snapshotRevision\":" + snapshot.Revision.ToString(CultureInfo.InvariantCulture) +
                ",\"snapshotRequestRevision\":" + snapshot.Binding.RequestRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"submittedSnapshotRevision\":" + submittedSnapshot.Revision.ToString(CultureInfo.InvariantCulture) +
                ",\"submittedRequestRevision\":" + submittedRequestRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"capturedGameTime\":" + snapshot.CapturedGameTime.ToString("R", CultureInfo.InvariantCulture) +
                ",\"playerText\":\"" + Json(request.PlayerText) + "\",\"history\":\"" + Json(request.History) +
                "\",\"socialOnly\":" + request.SocialOnly.ToString().ToLowerInvariant() +
                ",\"moodEnabled\":" + request.MoodEnabled.ToString().ToLowerInvariant() +
                ",\"moodValue\":" + request.MoodValue.ToString(CultureInfo.InvariantCulture) +
                ",\"snapshotMoodValue\":" + snapshot.PersistentMood.Value.ToString(CultureInfo.InvariantCulture) +
                ",\"moodReadRevision\":" + moodAtSnapshot.Revision.ToString(CultureInfo.InvariantCulture) +
                ",\"moodReadSequence\":" + moodAtSnapshot.AcceptedTurnSequence.ToString(CultureInfo.InvariantCulture) +
                ",\"disposition\":" + request.Disposition.ToString(CultureInfo.InvariantCulture) +
                ",\"snapshotDisposition\":" + snapshot.EffectiveDisposition.ToString(CultureInfo.InvariantCulture) +
                ",\"relationship\":\"" + Json(RelationshipFingerprint(snapshot.DirectedRelationship)) +
                "\",\"relationshipReadBeforeCommit\":\"" + Json(relationshipAtSnapshot) +
                "\",\"canonicalContext\":\"" + Json(canonicalContext) + "\",\"requestContext\":\"" +
                Json(request.ConversationReaction) + "\",\"rawSerializedContextMatched\":true,\"fittedHistoryMatch\":\"" +
                Json(promptHistoryMatch) + "\"");
        }

        private static IEnumerator RestoreAppraisalReopenBaseline(PeopleOfTheIliacBayMod host, StreetPopulation streets)
        {
            if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
            yield return ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, AppraisalReopenBaseline, true);
            GameManager game = GameManager.Instance;
            Require(game && game.PlayerEntity != null && game.PlayerEntity.Name == OwnedCharacter && streets && streets.Ready &&
                !game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.Name == "Gothway Garden",
                "Native appraisal-reopen did not restore the exact owned exterior baseline.");
        }

        private static string ArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
            return null;
        }

        private static string CreateJournal()
        {
            string directory = Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "decision-routing-appraisal-reopen-" + Guid.NewGuid().ToString("N") + ".jsonl");
            AppendJournal(path, "run_start", "\"utc\":\"" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                "\",\"character\":\"" + Json(OwnedCharacter) + "\",\"save\":\"" + Json(AppraisalReopenBaseline) + "\"");
            return path;
        }

        private static string CreateSafeDowngradeJournal()
        {
            string directory = Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "decision-routing-safe-downgrade-" + Guid.NewGuid().ToString("N") + ".jsonl");
            AppendJournal(path, "run_start", "\"utc\":\"" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                "\",\"character\":\"" + Json(OwnedCharacter) + "\",\"save\":\"" + Json(AppraisalReopenBaseline) + "\",\"mode\":\"controlled-offer-route\"");
            return path;
        }

        private static void AppendJournal(string path, string eventName, string fields)
        {
            string line = "{\"event\":\"" + Json(eventName) + "\",\"utc\":\"" +
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\"," + fields + "}";
            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
        }

        private static void TryAppendJournalFailure(string path, string eventName, Exception error)
        {
            try { AppendJournal(path, eventName, "\"exception\":\"" + Json(error.ToString()) + "\""); }
            catch (Exception journalError)
            { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_JOURNAL_FAILURE: " + journalError); }
        }

        private static string Json(string value)
        {
            if (value == null) return "";
            var result = new StringBuilder(value.Length + 16);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': result.Append("\\\\"); break;
                    case '"': result.Append("\\\""); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (c < 0x20) result.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else result.Append(c);
                        break;
                }
            }
            return result.ToString();
        }

        private static HashSet<string> InboxKeys(WorldState world)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (world == null || world.NpcExperienceInbox == null || world.NpcExperienceInbox.Entries == null) return result;
            foreach (NpcExperienceInboxEntry entry in world.NpcExperienceInbox.Entries)
                if (entry != null && !string.IsNullOrEmpty(entry.DeduplicationKey)) result.Add(entry.DeduplicationKey);
            return result;
        }

        private static List<NpcExperienceInboxEntry> FindNewSpeechRows(WorldState world, string personId, string[] texts,
            HashSet<string> beforeKeys)
        {
            var result = new List<NpcExperienceInboxEntry>();
            if (world == null || world.NpcExperienceInbox == null || world.NpcExperienceInbox.Entries == null) return result;
            foreach (NpcExperienceInboxEntry entry in world.NpcExperienceInbox.Entries)
                if (entry != null && entry.ObserverId == personId && entry.Origin == NpcExperienceEventOrigin.AttributedSpeech &&
                    Array.IndexOf(texts, entry.ObservedTurnText) >= 0 && beforeKeys != null &&
                    !beforeKeys.Contains(entry.DeduplicationKey)) result.Add(entry);
            result.Sort((first, second) => first.ObservedGameTime.CompareTo(second.ObservedGameTime));
            return result;
        }

        private static List<NpcExperienceInboxEntry> FindRows(WorldState world, List<NpcExperienceInboxEntry> expected)
        {
            var result = new List<NpcExperienceInboxEntry>();
            if (world == null || world.NpcExperienceInbox == null || world.NpcExperienceInbox.Entries == null) return result;
            foreach (NpcExperienceInboxEntry entry in world.NpcExperienceInbox.Entries)
                if (entry != null && expected.Exists(value => value.DeduplicationKey == entry.DeduplicationKey)) result.Add(entry);
            return result;
        }

        private static NativeAttitudeSnapshot ReadAttitudeSnapshot(WorldState world, string personId)
        {
            ConversationAttitude attitude = world == null || world.ConversationAttitudes == null ? null :
                world.ConversationAttitudes.Find(value => value != null && value.PersonId == personId);
            return attitude == null ? new NativeAttitudeSnapshot() : new NativeAttitudeSnapshot { Exists = true,
                Offset = attitude.Offset, NativeBaseline = attitude.NativeBaseline, HasNativeBaseline = attitude.HasNativeBaseline };
        }

        private static int CountConsumedForRows(WorldState world, List<NpcExperienceInboxEntry> expected)
        {
            int count = 0;
            foreach (NpcExperienceInboxEntry entry in FindRows(world, expected))
                if (entry.Status == NpcExperienceInboxStatus.Consumed && entry.Appraisal != null) count++;
            return count;
        }

        private static int CountPendingForObserver(WorldState world, string observerId)
        {
            int count = 0;
            if (world == null || world.NpcExperienceInbox == null || world.NpcExperienceInbox.Entries == null) return -1;
            foreach (NpcExperienceInboxEntry entry in world.NpcExperienceInbox.Entries)
                if (entry != null && entry.Status == NpcExperienceInboxStatus.Pending && entry.ObserverId == observerId) count++;
            return count;
        }

        private static int CountPendingOtherObservers(WorldState world, string observerId)
        {
            int count = 0;
            if (world == null || world.NpcExperienceInbox == null || world.NpcExperienceInbox.Entries == null) return -1;
            foreach (NpcExperienceInboxEntry entry in world.NpcExperienceInbox.Entries)
                if (entry != null && entry.Status == NpcExperienceInboxStatus.Pending && entry.ObserverId != observerId) count++;
            return count;
        }

        private static int CountPendingForRows(WorldState world, List<NpcExperienceInboxEntry> rows)
        {
            int count = 0;
            foreach (NpcExperienceInboxEntry entry in FindRows(world, rows))
                if (entry.Status == NpcExperienceInboxStatus.Pending) count++;
            return count;
        }

        private static int CountTracesForRows(List<AppraisalTraceRow> traces, List<NpcExperienceInboxEntry> rows)
        {
            int count = 0;
            lock (traces)
                foreach (AppraisalTraceRow traceRow in traces)
                    if (traceRow.Request != null && rows.Exists(row =>
                        traceRow.Request.IndexOf(row.DeduplicationKey, StringComparison.Ordinal) >= 0)) count++;
            return count;
        }

        private static string MatchFittedHistory(string requestJson, string history)
        {
            if (string.IsNullOrEmpty(history)) return "empty";
            QwenPromptPayload payload = null;
            try { payload = JsonUtility.FromJson<QwenPromptPayload>(requestJson); }
            catch { }
            string prompt = payload == null ? null : payload.prompt;
            if (string.IsNullOrEmpty(prompt)) return "unmatched";
            string full = DialogueHistoryWindow.Messages(history);
            if (full.Length == 0) return "empty_structured_history";
            if (prompt.IndexOf(full, StringComparison.Ordinal) >= 0) return "full";
            string[] lines = history.Split('\n');
            int offset = 0;
            foreach (string line in lines)
            {
                if (line.StartsWith("Player: ", StringComparison.Ordinal))
                {
                    string recent = DialogueHistoryWindow.Messages(history.Substring(offset));
                    if (recent.Length > 0 && prompt.IndexOf(recent, StringComparison.Ordinal) >= 0) return "recent_suffix";
                }
                offset += line.Length + 1;
            }
            return "unmatched";
        }

        private static string MoodAnchor(WorldState world, string personId)
        {
            ConversationMood mood = world == null || world.ConversationMoods == null ? null :
                world.ConversationMoods.Find(value => value != null && value.PersonId == personId);
            return mood == null ? "absent" : mood.PersonId + "|" + mood.Value.ToString(CultureInfo.InvariantCulture) + "|" +
                mood.AcceptedTurnSequence.ToString(CultureInfo.InvariantCulture) + "|" + mood.Revision.ToString(CultureInfo.InvariantCulture) +
                "|" + mood.HasTimeAnchor + "|" + mood.UpdatedAt.ToString("R", CultureInfo.InvariantCulture) +
                "|" + mood.RecoveryHalfLifeSeconds.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string ProfileFingerprint(NpcPersonalityProfile profile)
        {
            return profile == null ? null : profile.PersonId + "|" + profile.Version.ToString(CultureInfo.InvariantCulture) + "|" +
                profile.Seed.ToString(CultureInfo.InvariantCulture) + "|" + profile.Background + "|" + profile.Description();
        }

        private static void RequireProfileIdentity(NpcPersonalityProfile profile, string personId, uint seed,
            int savedFaction, NpcSocialBackground? expectedBackground, bool requirePresent)
        {
            if (profile == null)
            {
                if (requirePresent) throw new InvalidOperationException("Normal conversation/appraisal did not create a stable personality profile.");
                return;
            }
            Require(profile.PersonId == personId && profile.Seed == seed && profile.Version == 3 &&
                profile.SelectedTraits.Count == 3 &&
                (expectedBackground == null || profile.Background == expectedBackground.Value),
                "Native-created personality does not match the saved stable seed/background and three-trait profile.");
            NpcSocialBackground resolved;
            if (TryResolveBackground(savedFaction, out resolved))
                Require(profile.Background == resolved, "Native-created profile background differs from resolved saved faction.");
        }

        private static void RequireStableIdentity(WorldStateStore store, string personId, uint expectedSeed)
        {
            Require(store.TryReadNpcPersonalityIdentity(personId, out uint seed, out int faction, out bool protectedResident) &&
                !protectedResident && seed == expectedSeed,
                "Stable saved resident identity changed or became protected during the native conversation/appraisal path.");
        }

        private static NpcSocialBackground? ResolveCurrentBackground(WorldStateStore store, string personId)
        {
            uint seed;
            int faction;
            bool protectedResident;
            if (!store.TryReadNpcPersonalityIdentity(personId, out seed, out faction, out protectedResident) ||
                protectedResident) return null;
            return TryResolveBackground(faction, out NpcSocialBackground background)
                ? (NpcSocialBackground?)background : null;
        }

        private static bool TryResolveBackground(int factionId, out NpcSocialBackground background)
        {
            background = NpcSocialBackground.Unknown;
            MethodInfo method = typeof(PeopleOfTheIliacBayMod).GetMethod("TryResolveNpcPersonalityBackground",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) return false;
            object[] args = { factionId, NpcSocialBackground.Unknown };
            bool resolved = Convert.ToBoolean(method.Invoke(null, args), CultureInfo.InvariantCulture);
            if (args[1] is NpcSocialBackground value) background = value;
            return resolved;
        }

        private static string RelationshipFingerprint(NpcDirectedRelationshipView relationship)
        {
            if (relationship == null) return "null";
            return relationship.ObserverId + "|" + relationship.TargetId + "|" +
                relationship.Revision.ToString(CultureInfo.InvariantCulture) + "|" +
                relationship.LastGameTime.ToString("R", CultureInfo.InvariantCulture) + "|" + relationship.TrustKnown + "|" +
                relationship.ReliabilityTrust.ToString(CultureInfo.InvariantCulture) + "|" +
                relationship.TrustConfidence.ToString(CultureInfo.InvariantCulture) + "|" +
                relationship.TrustEvidenceCount.ToString(CultureInfo.InvariantCulture) + "|" +
                relationship.Resentment.ToString(CultureInfo.InvariantCulture) + "|" +
                relationship.InterpersonalFear.ToString(CultureInfo.InvariantCulture) + "|projected=" +
                relationship.ProjectedInterpersonalFear.ToString(CultureInfo.InvariantCulture) + "|fearAnchorKnown=" +
                relationship.HasFearRecoveryAnchor + "|fearAnchor=" +
                relationship.FearRecoveryAnchorGameTime.ToString("R", CultureInfo.InvariantCulture) + "|fearHalfLife=" +
                relationship.FearRecoveryHalfLifeSeconds.ToString("R", CultureInfo.InvariantCulture);
        }

        private static void LogAppraisalTraces(List<AppraisalTraceRow> traces, string personId)
        {
            AppraisalTraceRow[] copy;
            lock (traces) copy = traces.ToArray();
            for (int i = 0; i < copy.Length; i++)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_APPRAISAL_REOPEN_RAW_APPRAISAL: index=" +
                    i.ToString(CultureInfo.InvariantCulture) + "; person=" + personId + "; request=" +
                    (copy[i].Request ?? "<null>") + "; raw=" + (copy[i].Raw ?? "<null>") +
                    "; parsed=" + (copy[i].Parsed ?? "<null>"));
        }

        private static string ReadRememberedReply(DaggerfallTalkWindow talk, string question, int firstNewRow)
        {
            var field = typeof(DaggerfallTalkWindow).GetField("listboxConversation", InstanceMembers);
            var transcript = field == null ? null : field.GetValue(talk) as DaggerfallWorkshop.Game.UserInterface.ListBox;
            if (transcript == null) return "<unavailable>";
            for (int index = Math.Max(0, firstNewRow); index + 1 < transcript.Count; index++)
            {
                var player = transcript.GetItem(index);
                var reply = transcript.GetItem(index + 1);
                if (player == null || player.textLabel == null || reply == null || reply.textLabel == null ||
                    player.textLabel.Name == "PeopleOfTheIliacBayPendingEphemeral" ||
                    reply.textLabel.Name == "PeopleOfTheIliacBayPendingEphemeral" ||
                    player.textColor != DaggerfallUI.DaggerfallQuestionTextColor || player.textLabel.Text != question ||
                    reply.textColor == DaggerfallUI.DaggerfallQuestionTextColor) continue;
                // Visible text is progressively revealed during speech. Compare the
                // exact registered transcript text, not the first visible word.
                return Convert.ToString(RequireMethod(typeof(PeopleOfTheIliacBayMod),
                    "CanonicalTranscriptText", StaticMembers, 1).Invoke(null, new object[] { reply }),
                    CultureInfo.InvariantCulture) ?? "";
            }
            return "<missing>";
        }

        private static ResidentActor FindActor(string personId)
        {
            foreach (ResidentActor actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if (actor && actor.Resident != null && string.Equals(actor.Resident.Id, personId, StringComparison.Ordinal)) return actor;
            return null;
        }

        private static WorldStateStore ReadHostStore(PeopleOfTheIliacBayMod host) =>
            RequireField(typeof(PeopleOfTheIliacBayMod), "store", InstanceMembers).GetValue(host) as WorldStateStore;

        private static string ReadPerson(PeopleOfTheIliacBayMod host) =>
            Convert.ToString(RequireField(typeof(PeopleOfTheIliacBayMod), "conversationPersonId", InstanceMembers).GetValue(host),
                CultureInfo.InvariantCulture);

        private static bool ReadBool(PeopleOfTheIliacBayMod host, string name) =>
            Convert.ToBoolean(RequireField(typeof(PeopleOfTheIliacBayMod), name, InstanceMembers).GetValue(host), CultureInfo.InvariantCulture);

        private static float ReadFloat(PeopleOfTheIliacBayMod host, string name) =>
            Convert.ToSingle(RequireField(typeof(PeopleOfTheIliacBayMod), name, InstanceMembers).GetValue(host), CultureInfo.InvariantCulture);

        private static void textFeatureSet(PeopleOfTheIliacBayMod host, bool value) =>
            RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogueEnabled", InstanceMembers).SetValue(host, value);

        private static void textPrewarmSet(PeopleOfTheIliacBayMod host, bool value) =>
            RequireField(typeof(PeopleOfTheIliacBayMod), "dialoguePrewarmEnabled", InstanceMembers).SetValue(host, value);

        private static void SuppressGreeting(PeopleOfTheIliacBayMod host)
        {
            RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending", InstanceMembers).SetValue(host, false);
            RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt", InstanceMembers)
                .SetValue(host, Time.unscaledTime + 86400f);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
