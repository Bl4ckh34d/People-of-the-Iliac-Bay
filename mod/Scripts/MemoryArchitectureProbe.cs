using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Live acceptance for save-backed own-memory and mood-cause reads. Fixtures
    // are source-owned and typed; selection and final language remain model-owned.
    internal static class MemoryArchitectureProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-memory-architecture-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class Case
        {
            internal string Name, Query, Marker, PlayerMarker, ExpectedSource, ExpectedAttribution, ReceiptKey, OutcomeId;
            internal NpcExperienceEventKind? EventKind;
            internal NpcResponsibleActorAttribution Attribution;
            internal int MoodDelta;
        }

        private sealed class RunState
        {
            internal string Checkpoint, StagingCheckpoint;
            internal bool BaselineRestored;
            internal int Cases, SourceValidCases, SemanticErrors;
        }

        private sealed class SubmissionObservation
        {
            internal NpcMemoryReadSnapshot ReadBeforePublication;
            internal bool ReceiptValidBeforePublication;
            internal bool ReadWasBeforePublication;
            internal bool CurrentTurnObserved;
            internal bool ModelCompleted;
            internal string FinalReply;
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            GameManager game = GameManager.Instance;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave &&
                ArgumentValue("-peopleoftheiliacbay-fixture-character") == OwnedCharacter &&
                ArgumentValue("-peopleoftheiliacbay-fixture-save") == BaselineSave,
                "Memory architecture probe requires its flag and exact owned baseline fixture.");
            Require(host && streets && streets.Ready && game && game.IsReady &&
                game.PlayerEntity != null && game.PlayerEntity.Name == OwnedCharacter &&
                !game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.Name == "Gothway Garden",
                "Memory architecture probe requires the ready Gothway Garden exterior fixture.");

            bool originalSuspended = ReadBool(host, "experienceAppraisalsSuspended");
            var state = new RunState();
            Case[] scenarios = {
                new Case { Name="old-own-memory", Query="What did you say your own river map is called?",
                    Marker="Ash Harbor", ExpectedSource="Ash Harbor" },
                new Case { Name="injury-unknown", Query="Why has your mood been worse recently?",
                    EventKind=NpcExperienceEventKind.Injury, Attribution=NpcResponsibleActorAttribution.Unknown,
                    MoodDelta=-8, ExpectedSource="you: injury", ExpectedAttribution="responsible actor unknown" },
                new Case { Name="gift-not-applicable", Query="Why has your mood been better recently?",
                    EventKind=NpcExperienceEventKind.GiftReceived, Attribution=NpcResponsibleActorAttribution.NotApplicable,
                    MoodDelta=8, ExpectedSource="you: gift received", ExpectedAttribution="responsibility not applicable" }
            };
            // Human-readable names are the recall target. Checkpoint IDs carry
            // uniqueness; an opaque nonce is not part of a spoken map name.
            scenarios[0].Marker = "Ash Harbor";
            scenarios[0].PlayerMarker = "Copper Kestrel";
            Exception failure = null;
            SetBool(host, "experienceAppraisalsSuspended", true);
            IEnumerator work = DriveEnumeratorTree(RunCases(host, streets, game, character, scenarios, state));
            while (true)
            {
                object yielded = null;
                bool more = false;
                try { more = work.MoveNext(); if (more) yielded = work.Current; }
                catch (Exception e) { failure = e; break; }
                if (!more) break;
                yield return yielded;
            }
            IEnumerator close = DriveEnumeratorTree(ConversationMoodProbe.CloseTalk(host));
            Exception cleanupFailure = null;
            while (true)
            {
                object yielded = null;
                bool more = false;
                try { more = close.MoveNext(); if (more) yielded = close.Current; }
                catch (Exception e) { cleanupFailure = e; break; }
                if (!more) break;
                yield return yielded;
            }
            try
            {
                if (state.StagingCheckpoint != null)
                {
                    ConversationMoodProbe.DeleteCheckpoint(game, character, state.StagingCheckpoint);
                    state.StagingCheckpoint = null;
                }
                if (state.Checkpoint != null)
                {
                    ConversationMoodProbe.DeleteCheckpoint(game, character, state.Checkpoint);
                    state.Checkpoint = null;
                }
            }
            catch (Exception e) { if (cleanupFailure == null) cleanupFailure = e; }
            IEnumerator restore = DriveEnumeratorTree(ConversationMoodProbe.LoadSave(host, streets, character, BaselineSave, true));
            bool restoreSucceeded = false;
            while (true)
            {
                object yielded = null;
                bool more = false;
                try { more = restore.MoveNext(); if (more) yielded = restore.Current; }
                catch (Exception e) { if (cleanupFailure == null) cleanupFailure = e; break; }
                if (!more) { restoreSucceeded = true; break; }
                yield return yielded;
            }
            state.BaselineRestored = restoreSucceeded;
            try { SetBool(host, "experienceAppraisalsSuspended", originalSuspended); }
            catch (Exception e) { if (cleanupFailure == null) cleanupFailure = e; }
            if (cleanupFailure != null && failure == null) failure = cleanupFailure;
            if (state.BaselineRestored)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_ARCHITECTURE_BASELINE_RESTORED: true; temporaryCheckpointCleaned=" + (state.Checkpoint == null) + ".");
            if (failure != null) throw new InvalidOperationException("Memory architecture probe failed after " + state.Cases + " case(s).", failure);
            if (!state.BaselineRestored || state.Checkpoint != null || state.StagingCheckpoint != null || state.Cases != scenarios.Length ||
                state.SourceValidCases != scenarios.Length || state.SemanticErrors != 0)
                throw new InvalidOperationException("Memory architecture probe had incomplete/invalid source or semantic errors; cases=" +
                    state.Cases + "; sourceValid=" + state.SourceValidCases + "; semanticErrors=" + state.SemanticErrors + ".");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_ARCHITECTURE_PASS: cases=" + state.Cases +
                "; sourceValid=" + state.SourceValidCases + "; baselineRestored=" + state.BaselineRestored +
                "; temporaryCheckpointCleaned=true; genuineSubmitFreeText=true.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            GameManager game, string character, Case[] scenarios, RunState state)
        {
            foreach (Case scenario in scenarios)
            {
                    // Isolate every source in a freshly reloaded disposable baseline.
                    yield return ConversationMoodProbe.CloseTalk(host);
                    yield return ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, BaselineSave, true);
                    WorldStateStore store = ReadField(host, "store") as WorldStateStore;
                    Require(store != null, "Baseline load did not bind its native Core store.");
                    ResidentActor actor = null;
                    yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host,
                        game.PlayerMotor.transform.position, value => actor = value, true);
                    Require(actor && actor.Resident != null, "No eligible managed adult for memory fixture.");
                    string selectedPersonId = actor.Resident.Id;
                    NpcExperienceEvent evidence = null;
                    if (scenario.EventKind.HasValue)
                    {
                        // This historical outcome has deliberately authored IDs. Import only
                        // before Talk, then force a native save/load to rebind every live actor.
                        double historicalAt = Math.Floor(GameTime());
                        string id = Guid.NewGuid().ToString("N");
                        evidence = NpcExperienceEvent.FromNativeOutcome(selectedPersonId, selectedPersonId,
                            "memory-probe-receipt-" + id, "memory-probe-episode-" + id,
                            "memory-probe-outcome-" + id, scenario.EventKind.Value, historicalAt, scenario.Attribution);
                        NpcProbeLegacyExperienceFixture.Import(store, evidence);
                        actor = null; // Previous actor/resident binding belongs to the pre-Restore epoch.
                        state.StagingCheckpoint = "People - Memory Seed " + scenario.Name + " " +
                            Guid.NewGuid().ToString("N").Substring(0, 8);
                        yield return SaveCheckpoint(game, character, state.StagingCheckpoint);
                        yield return ConversationMoodProbe.LoadSave(host, streets, character, state.StagingCheckpoint, true);
                        ConversationMoodProbe.DeleteCheckpoint(game, character, state.StagingCheckpoint);
                        state.StagingCheckpoint = null;
                        store = ReadField(host, "store") as WorldStateStore;
                        Require(store != null && store.ReadPendingNpcExperiences(selectedPersonId, 100).Exists(
                            item => item.DeduplicationKey == evidence.DeduplicationKey),
                            "Historical mood-cause fixture was not retained through owned staging reload.");
                        float stagingActorDeadline = Time.realtimeSinceStartup + 45f;
                        while (!actor && Time.realtimeSinceStartup < stagingActorDeadline)
                        {
                            actor = streets.FindActor(selectedPersonId);
                            if (!actor || actor.Resident == null || actor.Resident.Id != selectedPersonId || !actor.Person)
                                actor = null;
                            if (!actor) yield return new WaitForSecondsRealtime(0.25f);
                        }
                        Require(actor != null, "Historical mood-cause reload did not rebind the same native actor.");
                    }
                    ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
                    DaggerfallTalkWindow talk = null;
                    yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
                    Require(talk != null, "Native Talk did not open for the selected resident.");
                    string personId = Convert.ToString(ReadField(host, "memoryPersonId"), CultureInfo.InvariantCulture);
                    Require(!string.IsNullOrWhiteSpace(personId) && personId == actor.Resident.Id,
                        "Live Talk identity is not bound to the selected resident.");

                    // Native save files restore whole seconds. Fixture events
                    // must not land fractionally in the future after reload.
                    double now = Math.Floor(GameTime());
                    if (!scenario.EventKind.HasValue)
                    {
                        string playerFact = "I named my own river map " + scenario.PlayerMarker + ".";
                        string npcFact = "My own river map is " + scenario.Marker + ".";
                        store.RememberConversation(personId, Math.Max(0, Math.Floor(now) - 86400d),
                            "the river ferry", "Gothway Garden", playerFact, npcFact, "native");
                        NpcMemoryReadSnapshot fixtureRead;
                        scenario.ExpectedSource = scenario.Marker;
                        Require(store.TryCaptureNpcMemoryRead(personId, "river map", now, 2000, out fixtureRead) &&
                            fixtureRead != null && fixtureRead.Text.Contains("Player said: \"" + playerFact) &&
                            fixtureRead.Text.Contains("NPC said (native): \"" + npcFact),
                            "Memory fixture did not preserve separate player and NPC source lines.");
                    }
                    else
                    {
                        Require(evidence != null && evidence.ObserverId == personId,
                            "Historical mood-cause fixture changed speaker after owned reload.");
                        NpcExperienceAppraisalTicket ticket;
                        Require(store.TryCaptureNpcExperienceAppraisal(personId, now, out ticket) && ticket != null,
                            "Native cause fixture could not be captured for typed appraisal.");
                        Require(ticket.Request != null && ticket.Request.ObserverId == personId &&
                            ticket.Request.SubjectPersonId == personId &&
                            ticket.Request.EvidenceDeduplicationKey == evidence.DeduplicationKey &&
                            ticket.Request.CanonicalEvidenceId == evidence.OutcomeId,
                            "Typed appraisal ticket is not bound to this exact fixture receipt.");
                        string typedFixture = TypedAppraisal(ticket.Request, scenario.MoodDelta);
                        Require(store.TryCommitNpcExperienceAppraisal(ticket, typedFixture),
                            "Typed appraisal fixture failed native contract validation.");
                        NpcExperienceInboxEntry committed = FindOutcome(store, personId, evidence.DeduplicationKey);
                        Require(committed != null && committed.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                            committed.SubjectPersonId == personId && committed.OutcomeId == evidence.OutcomeId &&
                            committed.EventKind == scenario.EventKind.Value && committed.Status == NpcExperienceInboxStatus.Consumed &&
                            committed.Revision == 2 && committed.Appraisal != null && committed.Appraisal.AppliedMoodDelta != 0 &&
                            committed.ResponsibleActorAttribution == scenario.Attribution && committed.ResponsibleActorId == null,
                            "Typed appraisal did not consume the exact source receipt with expected attribution and nonzero mood.");
                        scenario.ReceiptKey = evidence.DeduplicationKey;
                        scenario.OutcomeId = evidence.OutcomeId;
                        NpcMemoryReadSnapshot fixtureRead;
                        Require(store.TryCaptureNpcMoodCauseRead(personId, now, 600, out fixtureRead) &&
                            fixtureRead != null && fixtureRead.Text.IndexOf(scenario.ExpectedSource,
                                StringComparison.OrdinalIgnoreCase) >= 0 &&
                            fixtureRead.Text.IndexOf(scenario.ExpectedAttribution,
                                StringComparison.OrdinalIgnoreCase) >= 0,
                            "Committed mood cause does not retain its expected source and attribution.");
                    }

                    // SaveLoadManager is the disk boundary under test. Reopen the
                    // same speaker only after the exact checkpoint has reloaded.
                    yield return ConversationMoodProbe.CloseTalk(host);
                    state.Checkpoint = "People - Memory Arch " + scenario.Name + " " + Guid.NewGuid().ToString("N").Substring(0, 8);
                    yield return SaveCheckpoint(game, character, state.Checkpoint);
                    yield return ConversationMoodProbe.LoadSave(host, streets, character, state.Checkpoint, true);
                    store = ReadField(host, "store") as WorldStateStore;
                    Require(store != null, "Checkpoint reload did not bind its native Core store.");
                    NpcMemoryReadSnapshot diskRead;
                    double loadedNow = GameTime();
                    bool sourceValid;
                    if (scenario.EventKind.HasValue)
                    {
                        sourceValid = store.TryCaptureNpcMoodCauseRead(personId, loadedNow, 600, out diskRead) &&
                            diskRead != null && diskRead.Text.IndexOf(scenario.ExpectedSource,
                                StringComparison.OrdinalIgnoreCase) >= 0 &&
                            diskRead.Text.IndexOf(scenario.ExpectedAttribution,
                                StringComparison.OrdinalIgnoreCase) >= 0 &&
                            store.IsNpcMemoryReadCurrent(diskRead, personId, loadedNow) &&
                            IsExactCommittedOutcome(store, scenario, personId);
                    }
                    else
                    {
                        sourceValid = store.TryCaptureNpcMemoryRead(personId, "river map", loadedNow, 2000, out diskRead) &&
                            diskRead != null && diskRead.Text.Contains("Player said: \"I named my own river map " + scenario.PlayerMarker + ".") &&
                            diskRead.Text.Contains("NPC said (native): \"My own river map is " + scenario.Marker + ".") &&
                            store.IsNpcMemoryReadCurrent(diskRead, personId, loadedNow);
                    }
                    if (sourceValid) state.SourceValidCases++;
                    else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_ARCHITECTURE_SOURCE_FAIL: case=" + scenario.Name + "; diskRoundTrip=true.");

                    actor = null;
                    float actorDeadline = Time.realtimeSinceStartup + 25f;
                    while (!actor && Time.realtimeSinceStartup < actorDeadline)
                    { actor = streets.FindActor(personId); if (!actor) yield return null; }
                    Require(actor && actor.Resident != null, "Checkpoint reload did not restore the test speaker actor.");
                    ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
                    talk = null;
                    yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
                    SetGreetingSuppressed(host);
                    ListBox transcript = ReadField(talk, "listboxConversation") as ListBox;
                    MethodInfo submit = typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText", Members);
                    Require(ReadField(host, "conversationMemoryReadEvidence") == null,
                        "Fresh Talk already contains a selected read receipt before the fixture query.");
                    var observation = new SubmissionObservation();
                    var tracedClient = ReadField(host, "localDialogue") as LocalDialogueClient;
                    var previousTrace = tracedClient.ForegroundClassificationTrace;
                    tracedClient.ForegroundClassificationTrace = (request, response, classification) =>
                    {
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_ARCHITECTURE_CLASSIFIER: case=" + scenario.Name +
                            "; ownLookup=" + request.Contains("Select at most one additional read") +
                            "; selected=" + classification);
                        if (previousTrace != null) previousTrace(request, response, classification);
                    };
                    try
                    {
                        yield return SubmitAndCaptureRead(host, talk, submit, transcript, store, personId,
                            scenario.Query, observation, 150f);
                    }
                    finally { tracedClient.ForegroundClassificationTrace = previousTrace; }
                    string finalReply = observation.FinalReply;
                    NpcMemoryReadSnapshot selected = ReadField(host, "conversationMemoryReadEvidence") as NpcMemoryReadSnapshot;
                    string actualSelected = selected == null ? "NONE" :
                        selected.Kind == NpcMemoryReadKind.Memory ? "Memory" :
                        selected.Kind == NpcMemoryReadKind.MoodCauses ? "MoodCauses" : "UNKNOWN";
                    bool lookupAttempted = ReadBool(host, "npcKnowledgeLookupAttempted") &&
                        ReferenceEquals(ReadField(host, "npcKnowledgeLookupAttemptedSnapshot"),
                            ReadField(host, "conversationContextCurrentSnapshot"));
                    bool selectedValid = observation.ReadWasBeforePublication && observation.ReceiptValidBeforePublication &&
                        selected != null && ReferenceEquals(observation.ReadBeforePublication, selected) &&
                        selected.PersonId == personId;
                    if (selectedValid)
                    {
                        if (selected.Kind != (scenario.EventKind.HasValue ? NpcMemoryReadKind.MoodCauses : NpcMemoryReadKind.Memory))
                            selectedValid = false;
                        if (scenario.EventKind.HasValue &&
                            (selected.Text.IndexOf(scenario.ExpectedSource, StringComparison.OrdinalIgnoreCase) < 0 ||
                             selected.Text.IndexOf(scenario.ExpectedAttribution, StringComparison.OrdinalIgnoreCase) < 0))
                            selectedValid = false;
                        if (!scenario.EventKind.HasValue &&
                            (selected.Text.IndexOf("Player said: \"I named my own river map " + scenario.PlayerMarker + ".", StringComparison.Ordinal) < 0 ||
                             selected.Text.IndexOf("NPC said (native): \"My own river map is " + scenario.Marker + ".", StringComparison.Ordinal) < 0))
                            selectedValid = false;
                    }
                    string semanticError = null;
                    string replyLower = (finalReply ?? string.Empty).ToLowerInvariant();
                    if (!scenario.EventKind.HasValue &&
                        replyLower.IndexOf(scenario.Marker.ToLowerInvariant(), StringComparison.Ordinal) < 0)
                        semanticError = "memory-answer-not-substantive";
                    if (!scenario.EventKind.HasValue &&
                        replyLower.IndexOf(scenario.PlayerMarker.ToLowerInvariant(), StringComparison.Ordinal) >= 0)
                        semanticError = "player-memory-misattributed-as-npc-memory";
                    if (scenario.EventKind == NpcExperienceEventKind.Injury &&
                        !ContainsAny(replyLower, "injur", "hurt", "wound", "ache", "pain"))
                        semanticError = "injury-answer-not-substantive";
                    if (scenario.EventKind == NpcExperienceEventKind.GiftReceived &&
                        !ContainsAny(replyLower, "gift", "present", "received", "gave me"))
                        semanticError = "gift-answer-not-substantive";
                    if (scenario.EventKind.HasValue && BlamesPlayer(replyLower))
                        semanticError = "unsupported-player-blame";
                    if (scenario.EventKind == NpcExperienceEventKind.GiftReceived &&
                        ContainsAny(replyLower, "you gave", "you brought", "your gift", "your present", "from you"))
                        semanticError = "unsupported-player-gift-attribution";
                    if (!observation.ModelCompleted || IsSafetyFallback(replyLower))
                        semanticError = "safety-fallback-or-no-model-completion";
                    if (!sourceValid || selected != null && !selectedValid)
                        semanticError = (semanticError == null ? "" : semanticError + ";") + "source-or-receipt-invalid";
                    if (!string.IsNullOrEmpty(semanticError)) state.SemanticErrors++;
                    state.Cases++;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEMORY_ARCHITECTURE_CASE: {\"case\":\"" + Escape(scenario.Name) +
                        "\",\"rawResponse\":\"" + Escape(finalReply) + "\",\"actualSelected\":\"" + actualSelected +
                        "\",\"sourceValid\":" + (sourceValid ? "true" : "false") +
                        ",\"lookupAttempted\":" + (lookupAttempted ? "true" : "false") +
                        ",\"selectionReceiptValid\":" + (selectedValid ? "true" : "false") +
                        ",\"readCapturedBeforePublication\":" + (observation.ReadWasBeforePublication ? "true" : "false") +
                        ",\"modelCompleted\":" + (observation.ModelCompleted ? "true" : "false") +
                        ",\"diskRoundTrip\":true,\"semanticError\":\"" + Escape(semanticError) + "\"}");
                    yield return ConversationMoodProbe.CloseTalk(host);
                    ConversationMoodProbe.DeleteCheckpoint(game, character, state.Checkpoint);
                    state.Checkpoint = null;
                    yield return ConversationMoodProbe.LoadSave(host, streets, character, BaselineSave, true);
                    state.BaselineRestored = true;
                    if (!ReadBool(host, "experienceAppraisalsSuspended"))
                        SetBool(host, "experienceAppraisalsSuspended", true);
            }
        }

        private static IEnumerator SaveCheckpoint(GameManager game, string character, string title)
        {
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(character, title) < 0,
                "Refusing to overwrite a generated memory architecture checkpoint.");
            bool saved = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnSave += onSave;
            try
            {
                game.SaveLoadManager.Save(character, title);
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                Require(saved, "Native SaveLoadManager checkpoint timed out.");
                game.SaveLoadManager.EnumerateSaves();
                Require(game.SaveLoadManager.FindSaveFolderByNames(character, title) >= 0,
                    "Native checkpoint does not exist after SaveLoadManager.Save.");
            }
            finally { SaveLoadManager.OnSave -= onSave; }
        }

        private static IEnumerator SubmitAndCaptureRead(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, MethodInfo submit, ListBox transcript, WorldStateStore store,
            string personId, string question, SubmissionObservation observation, float timeoutSeconds)
        {
            if (!host || talk == null || submit == null || transcript == null || store == null ||
                string.IsNullOrWhiteSpace(question) || observation == null)
                throw new ArgumentException("Live read acceptance needs a bound speaker, transcript, submit entry point and store.");

            FieldInfo clientField = typeof(PeopleOfTheIliacBayMod).GetField("localDialogue", Members);
            FieldInfo busyField = typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy", Members);
            FieldInfo snapshotField = typeof(PeopleOfTheIliacBayMod).GetField("conversationContextCurrentSnapshot", Members);
            FieldInfo capturedTextField = typeof(PeopleOfTheIliacBayMod).GetField("conversationContextCapturedPlayerText", Members);
            FieldInfo stageField = typeof(PeopleOfTheIliacBayMod).GetField("conversationContextIssuedStageToken", Members);
            FieldInfo evidenceField = typeof(PeopleOfTheIliacBayMod).GetField("conversationMemoryReadEvidence", Members);
            if (clientField == null || busyField == null || snapshotField == null || capturedTextField == null ||
                stageField == null || evidenceField == null)
                throw new InvalidOperationException("Live read observation fields are not available.");
            var client = clientField.GetValue(host) as LocalDialogueClient;
            if (client == null) throw new InvalidOperationException("Live read submission has no dialogue client.");
            var prior = snapshotField.GetValue(host) as ConversationContextSnapshot;
            long priorRevision = prior == null || prior.Binding == null ? -1 : prior.Binding.RequestRevision;
            string priorSession = prior == null || prior.Binding == null ? null : prior.Binding.SessionId;
            int priorQuestions = CountQuestionRows(transcript, question);
            long requestsBefore = ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount");
            long completionsBefore = ConversationMoodProbe.ReadLongProperty(client, "ReplyCompletionCount");
            submit.Invoke(host, new object[] { talk, question });

            float deadline = Time.realtimeSinceStartup + Mathf.Clamp(timeoutSeconds, 1f, 300f);
            int answerIndex = -1;
            while (Time.realtimeSinceStartup < deadline)
            {
                ObserveReadBeforePublication(host, store, personId, evidenceField, transcript,
                    question, priorQuestions, observation, out answerIndex);
                bool pair = TryFindReplyPair(transcript, question, out answerIndex);
                observation.CurrentTurnObserved = IsCurrentTurn(host, snapshotField, capturedTextField, question,
                    priorRevision, priorSession) || observation.CurrentTurnObserved;
                bool idle = !client.IsBusy && !Convert.ToBoolean(busyField.GetValue(host), CultureInfo.InvariantCulture) &&
                    Convert.ToInt64(stageField.GetValue(host), CultureInfo.InvariantCulture) == 0;
                if (pair && idle) break;
                yield return null;
            }

            ObserveReadBeforePublication(host, store, personId, evidenceField, transcript,
                question, priorQuestions, observation, out answerIndex);
            long requestsAfter = ConversationMoodProbe.ReadLongProperty(client, "ReplyRequestCount");
            long completionsAfter = ConversationMoodProbe.ReadLongProperty(client, "ReplyCompletionCount");
            bool finalPair = TryFindReplyPair(transcript, question, out answerIndex);
            bool settled = !client.IsBusy && !Convert.ToBoolean(busyField.GetValue(host), CultureInfo.InvariantCulture) &&
                Convert.ToInt64(stageField.GetValue(host), CultureInfo.InvariantCulture) == 0;
            if (!finalPair || !settled || CountQuestionRows(transcript, question) != priorQuestions + 1)
                throw new InvalidOperationException("Live SubmitFreeText did not settle to one exact transcript pair; requests=" +
                    requestsBefore + "->" + requestsAfter + "; completions=" + completionsBefore + "->" + completionsAfter + ".");
            var answer = transcript.GetItem(answerIndex);
            observation.FinalReply = answer == null || answer.textLabel == null ? string.Empty : answer.textLabel.Text;
            observation.CurrentTurnObserved = IsCurrentTurn(host, snapshotField, capturedTextField, question,
                priorRevision, priorSession) || observation.CurrentTurnObserved;
            observation.ModelCompleted = observation.CurrentTurnObserved &&
                requestsAfter > requestsBefore && completionsAfter > completionsBefore;
        }

        private static bool IsCurrentTurn(PeopleOfTheIliacBayMod host, FieldInfo snapshotField,
            FieldInfo capturedTextField, string text, long priorRevision, string priorSession)
        {
            var current = snapshotField.GetValue(host) as ConversationContextSnapshot;
            return current != null && current.Binding != null && current.Binding.RequestRevision > priorRevision &&
                (priorSession == null || current.Binding.SessionId == priorSession) &&
                string.Equals(capturedTextField.GetValue(host) as string, text, StringComparison.Ordinal);
        }

        private static void ObserveReadBeforePublication(PeopleOfTheIliacBayMod host,
            WorldStateStore store, string personId, FieldInfo evidenceField, ListBox transcript,
            string question, int priorQuestions, SubmissionObservation observation, out int answerIndex)
        {
            answerIndex = -1;
            NpcMemoryReadSnapshot evidence = evidenceField.GetValue(host) as NpcMemoryReadSnapshot;
            if (evidence == null || observation.ReadBeforePublication != null) return;
            bool alreadyPublished = TryFindReplyPair(transcript, question, out answerIndex);
            observation.ReadBeforePublication = evidence;
            observation.ReadWasBeforePublication = !alreadyPublished;
            observation.ReceiptValidBeforePublication = !alreadyPublished &&
                store.IsNpcMemoryReadCurrent(evidence, personId, GameTime());
        }

        private static int CountQuestionRows(ListBox transcript, string question)
        {
            int count = 0;
            if (transcript == null) return count;
            for (int i = 0; i < transcript.Count; i++)
            {
                var row = transcript.GetItem(i);
                if (row != null && row.textLabel != null && row.textLabel.Name != "PeopleOfTheIliacBayPendingEphemeral" &&
                    string.Equals(row.textLabel.Text, question, StringComparison.Ordinal)) count++;
            }
            return count;
        }

        private static bool TryFindReplyPair(ListBox transcript, string question, out int answerIndex)
        {
            answerIndex = -1;
            if (transcript == null) return false;
            for (int i = transcript.Count - 1; i >= 0; i--)
            {
                var row = transcript.GetItem(i);
                if (row == null || row.textLabel == null || row.textLabel.Name == "PeopleOfTheIliacBayPendingEphemeral" ||
                    !string.Equals(row.textLabel.Text, question, StringComparison.Ordinal)) continue;
                for (int j = i + 1; j < transcript.Count; j++)
                {
                    var answer = transcript.GetItem(j);
                    if (answer == null || answer.textLabel == null || answer.textLabel.Name == "PeopleOfTheIliacBayPendingEphemeral") continue;
                    if (string.IsNullOrWhiteSpace(answer.textLabel.Text)) return false;
                    answerIndex = j;
                    return true;
                }
                return false;
            }
            return false;
        }

        internal static IEnumerator DriveEnumeratorTree(IEnumerator root)
        {
            var stack = new List<IEnumerator>();
            if (root != null) stack.Add(root);
            try
            {
                while (stack.Count > 0)
                {
                    int top = stack.Count - 1;
                    IEnumerator current = stack[top];
                    bool more;
                    object yielded = null;
                    try { more = current.MoveNext(); if (more) yielded = current.Current; }
                    catch { throw; }
                    if (!more)
                    {
                        stack.RemoveAt(top);
                        IDisposable completed = current as IDisposable;
                        if (completed != null) completed.Dispose();
                        continue;
                    }
                    IEnumerator nested = yielded as IEnumerator;
                    if (nested != null) stack.Add(nested);
                    else yield return yielded;
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

        private static string TypedAppraisal(NpcExperienceAppraisalRequest r, int delta)
        {
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"saveBranchId\":\"" + Escape(r.SaveBranchId) + "\",\"observerId\":\"" + Escape(r.ObserverId) +
                "\",\"subjectPersonId\":\"" + Escape(r.SubjectPersonId) + "\",\"evidenceDeduplicationKey\":\"" + Escape(r.EvidenceDeduplicationKey) +
                "\",\"canonicalEvidenceId\":\"" + Escape(r.CanonicalEvidenceId) + "\",\"relationshipTargetId\":\"" + Escape(r.RelationshipTargetId) +
                "\",\"expectedMoodRevision\":" + r.ExpectedMoodRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"expectedAffinityRevision\":" + r.ExpectedAffinityRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"expectedRelationshipRevision\":" + r.ExpectedRelationshipRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"moodDelta\":" + delta.ToString(CultureInfo.InvariantCulture) +
                ",\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":0," +
                "\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
        }

        private static NpcExperienceInboxEntry FindOutcome(WorldStateStore store, string personId, string receiptKey)
        {
            WorldState snapshot = store == null ? null : store.Snapshot();
            if (snapshot == null || snapshot.NpcExperienceInbox == null || snapshot.NpcExperienceInbox.Entries == null)
                return null;
            foreach (NpcExperienceInboxEntry entry in snapshot.NpcExperienceInbox.Entries)
                if (entry != null && entry.ObserverId == personId && entry.DeduplicationKey == receiptKey)
                    return entry;
            return null;
        }

        private static bool IsExactCommittedOutcome(WorldStateStore store, Case scenario, string personId)
        {
            if (scenario == null || !scenario.EventKind.HasValue || string.IsNullOrEmpty(scenario.ReceiptKey)) return false;
            NpcExperienceInboxEntry entry = FindOutcome(store, personId, scenario.ReceiptKey);
            return entry != null && entry.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                entry.ObserverId == personId && entry.SubjectPersonId == personId &&
                entry.OutcomeId == scenario.OutcomeId && entry.EventKind == scenario.EventKind.Value &&
                entry.ResponsibleActorAttribution == scenario.Attribution && entry.ResponsibleActorId == null &&
                entry.Status == NpcExperienceInboxStatus.Consumed && entry.Revision == 2 &&
                entry.Appraisal != null && entry.Appraisal.AppliedMoodDelta != 0;
        }

        private static string ArgumentValue(string flag)
        {
            string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, flag);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        private static object ReadField(object instance, string name)
        {
            FieldInfo field = instance == null ? null : instance.GetType().GetField(name, Members);
            if (field == null) field = typeof(PeopleOfTheIliacBayMod).GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Required probe field missing: " + name);
            return field.GetValue(field.IsStatic ? null : instance);
        }
        private static bool ReadBool(object instance, string name)
        { return Convert.ToBoolean(ReadField(instance, name), CultureInfo.InvariantCulture); }
        private static void SetBool(object instance, string name, bool value)
        {
            FieldInfo field = typeof(PeopleOfTheIliacBayMod).GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Required probe field missing: " + name);
            field.SetValue(instance, value);
        }
        private static void SetGreetingSuppressed(PeopleOfTheIliacBayMod host)
        {
            typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingPending", Members).SetValue(host, false);
            typeof(PeopleOfTheIliacBayMod).GetField("returningGreetingRetryAt", Members).SetValue(host, Time.unscaledTime + 86400f);
        }
        private static double GameTime()
        {
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            return (double)clock.ToSeconds() + clock.Second - Math.Floor(clock.Second);
        }
        private static bool BlamesPlayer(string text)
        {
            string[] claims = { "you injured me", "you hurt me", "your attack", "you caused", "because of you", "you made me" };
            foreach (string claim in claims) if (text.Contains(claim)) return true;
            return false;
        }
        private static bool ContainsAny(string value, params string[] terms)
        {
            foreach (string term in terms) if (value.IndexOf(term, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
        private static bool IsSafetyFallback(string value)
        {
            string[] markers = { "lost my train of thought", "please say that again", "don't know", "do not know",
                "can't remember", "cannot remember", "remind me", "no recorded", "unavailable reply" };
            foreach (string marker in markers) if (value.IndexOf(marker, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
        private static string Escape(string value)
        {
            if (value == null) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
