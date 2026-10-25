using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // IMM-09 native acceptance. Controlled results enter through the production
    // social completion captured immediately before LocalDialogueClient.Request.
    internal static class ConversationMoodProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;
        private const string RunFlag = "-peopleoftheiliacbay-conversation-mood-probe";
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";

        private enum ReplyPhase { PassThrough, ObserveLive, ObserveFreeText, Control }
        private enum FreeTextBranch { None, SpontaneousOffer, MoodSocialCompletion, Multiple }

        private static ReplyPhase phase;
        private static Action<string> heldCompletion;
        private static DialogueRequest lastRequest;
        private static string lastLiveReply;
        private static FreeTextBranch freeTextBranch;
        private static int freeTextBranchEvents;
        private static int freeTextMoodCompletionEvents;
        private static int freeTextOfferEvents;
        private static readonly List<FreeTextBranch> freeTextBranchSequence = new List<FreeTextBranch>();
        private static NpcActionDecision freeTextOffer;

        private sealed class Evidence
        {
            internal string PersonId;
            internal string CheckpointTitle;
            internal string StagingCheckpointTitle;
            internal int InitialValue;
            internal long InitialSequence;
            internal int SavedValue;
            internal long SavedSequence;
            internal long LiveRequests;
            internal long LiveCompletions;
            internal int LiveDelta;
            internal int LiveMoodBefore;
            internal int LiveMoodAfter;
            internal string FreeTextBranch;
            internal long FreeTextRequests;
            internal long FreeTextCompletions;
            internal int FreeTextVisibleRows;
            internal string FreeTextBeforeControlsBranch;
            internal string FreeTextAfterControlsBranch;
            internal double BaselineClock;
            internal Vector3 BaselinePlayerPosition;
            internal bool OriginalReturningGreetingPending;
            internal float OriginalReturningGreetingRetryAt;
            internal bool OriginalAppraisalsSuspended;
        }

        private sealed class ActorCandidateCounts
        {
            internal int Seen, Inactive, MissingPerson, MissingResident, Dead, QuestProtected;
            internal int NonAdult, Sleeping, Seated, Conversing, ActiveAction, ActiveTask, NonNeutralMood, Excluded, Eligible;
            internal bool StoreAvailable, RequireNeutral;
            internal string ExcludedPersonId;
            internal float NearestResidentDistance = float.MaxValue;

            public override string ToString()
            {
                return "seen=" + Seen + "; inactive=" + Inactive + "; missing-person=" + MissingPerson +
                    "; missing-resident=" + MissingResident + "; dead=" + Dead + "; quest-protected=" + QuestProtected +
                    "; non-adult=" + NonAdult + "; sleeping=" + Sleeping + "; seated=" + Seated +
                    "; conversing=" + Conversing + "; active-action=" + ActiveAction + "; active-task=" + ActiveTask +
                    "; non-neutral-mood=" + NonNeutralMood + "; excluded=" + Excluded +
                    "; eligible=" + Eligible + "; store-available=" + StoreAvailable +
                    "; require-neutral=" + RequireNeutral + "; excluded-person=" + (ExcludedPersonId ?? "none") +
                    "; nearest-resident-distance=" + (NearestResidentDistance == float.MaxValue ? "none" :
                        NearestResidentDistance.ToString("0.00", CultureInfo.InvariantCulture)) + ".";
            }
        }

        // Called only by the mood-enabled production request path. Keep hook
        // state inside this opt-in probe so normal replies retain exact behavior.
        internal static Action<string> WrapReply(DaggerfallTalkWindow talk, DialogueRequest request,
            Action<string> complete)
        {
            if(NpcMoodCauseDialogueProbe.Observing)return NpcMoodCauseDialogueProbe.Observe(request,complete);
            if (SocialDuplicateReplyProbe.IsEnabled)
                return SocialDuplicateReplyProbe.WrapReply(talk, request, complete);
            string[] args = Environment.GetCommandLineArgs();
            if (complete == null || Array.IndexOf(args, RunFlag) < 0 || phase == ReplyPhase.PassThrough)
                return complete;

            if (phase == ReplyPhase.ObserveLive)
            {
                lastRequest = request;
                return answer =>
                {
                    lastLiveReply = answer;
                    complete(answer);
                };
            }

            if (phase == ReplyPhase.ObserveFreeText)
            {
                lastRequest = request;
                RecordFreeTextBranch(FreeTextBranch.MoodSocialCompletion);
                return answer =>
                {
                    lastLiveReply = answer;
                    complete(answer);
                };
            }

            // The backend callback is intentionally swallowed in control mode.
            // The probe cancels transport ownership, then invokes this exact
            // production handler with one controlled typed response.
            lastRequest = request;
            heldCompletion = complete;
            return answer => { };
        }

        // Called after production validates and displays a typed spontaneous
        // offer. It records only the native decision object, never its wording.
        internal static void ObserveSpontaneousOffer(NpcActionDecision proposal)
        {
            if (proposal == null || phase != ReplyPhase.ObserveFreeText ||
                Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) < 0) return;
            freeTextOffer = proposal;
            RecordFreeTextBranch(FreeTextBranch.SpontaneousOffer);
        }

        private static void RecordFreeTextBranch(FreeTextBranch branch)
        {
            freeTextBranchEvents++;
            freeTextBranchSequence.Add(branch);
            if (branch == FreeTextBranch.MoodSocialCompletion) freeTextMoodCompletionEvents++;
            else if (branch == FreeTextBranch.SpontaneousOffer) freeTextOfferEvents++;
            if (freeTextBranchEvents == 1) freeTextBranch = branch;
            else freeTextBranch = FreeTextBranch.Multiple;
        }

        private static bool IsMoodBackedSpontaneousOffer()
        {
            return freeTextBranchEvents == 2 && freeTextMoodCompletionEvents == 1 && freeTextOfferEvents == 1 &&
                freeTextBranchSequence.Count == 2 &&
                freeTextBranchSequence[0] == FreeTextBranch.MoodSocialCompletion &&
                freeTextBranchSequence[1] == FreeTextBranch.SpontaneousOffer;
        }

        private static string FreeTextBranchDescription()
        {
            if (freeTextBranchSequence.Count == 0) return FreeTextBranch.None.ToString();
            return string.Join("+", freeTextBranchSequence.ConvertAll(branch => branch.ToString()).ToArray());
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            Require(OwnedCharacter == character && BaselineSave == fixtureSave,
                "Conversation mood probe requires the owned disposable character and exact baseline save.");
            if (!host || !streets || !GameManager.Instance || !GameManager.Instance.IsReady || !streets.Ready)
                throw new InvalidOperationException("Conversation mood probe requires the ready owned native fixture.");

            Evidence evidence = new Evidence();
            evidence.OriginalReturningGreetingPending = Convert.ToBoolean(
                RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending").GetValue(host),
                CultureInfo.InvariantCulture);
            evidence.OriginalReturningGreetingRetryAt = Convert.ToSingle(
                RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt").GetValue(host),
                CultureInfo.InvariantCulture);
            FieldInfo appraisalSuspension = RequireField(typeof(PeopleOfTheIliacBayMod), "experienceAppraisalsSuspended");
            evidence.OriginalAppraisalsSuspended = Convert.ToBoolean(appraisalSuspension.GetValue(host),
                CultureInfo.InvariantCulture);
            appraisalSuspension.SetValue(host, true); // Keep this historical pending row out of the autonomous consumer.
            Exception probeFailure = null;
            phase = ReplyPhase.PassThrough;
            heldCompletion = null;
            lastRequest = null;
            lastLiveReply = null;
            freeTextBranch = FreeTextBranch.None;
            freeTextBranchEvents = 0;
            freeTextMoodCompletionEvents = 0;
            freeTextOfferEvents = 0;
            freeTextBranchSequence.Clear();
            freeTextOffer = null;

            IEnumerator test = DriveEnumeratorTree(RunCases(host, streets, character, evidence));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try { more = test.MoveNext(); if (more) current = test.Current; }
                    catch (Exception error) { probeFailure = error; break; }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                Dispose(test, ref probeFailure);
                phase = ReplyPhase.PassThrough;
                heldCompletion = null;
                lastRequest = null;
                if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                LocalDialogueClient client = ReadHostField(host, "localDialogue") as LocalDialogueClient;
                if (client) client.Cancel();
            }

            Exception restoreFailure = null;
            Exception baselineFailure = null;
            Exception cleanupFailure = null;
            try
            {
                IEnumerator restore = DriveEnumeratorTree(LoadSave(host, streets, character, BaselineSave, false));
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
                finally { Dispose(restore, ref restoreFailure); }

                baselineFailure = restoreFailure;
                if (baselineFailure == null)
                {
                    try
                    {
                        WorldStateStore baselineStore = ReadHostField(host, "store") as WorldStateStore;
                        Require(baselineStore != null, "Exact baseline reload did not restore the mood world store.");
                        double restoredClock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                        Require(Math.Abs(restoredClock - evidence.BaselineClock) <= 2d,
                            "Exact baseline reload did not restore original game clock.");
                    Require(GameManager.Instance.PlayerMotor != null &&
                        Vector3.Distance(GameManager.Instance.PlayerMotor.transform.position, evidence.BaselinePlayerPosition) <= 0.05f,
                        "Exact baseline reload did not restore original player position and scene context.");
                        if (!string.IsNullOrEmpty(evidence.PersonId))
                            RequireMood(baselineStore, evidence.PersonId, evidence.InitialValue, evidence.InitialSequence,
                                "Exact baseline rollback did not remove all mood state created by the probe.");
                    }
                    catch (Exception error) { baselineFailure = error; }
                }
            }
            finally
            {
                try
                {
                    RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending")
                        .SetValue(host, evidence.OriginalReturningGreetingPending);
                }
                catch (Exception error) { cleanupFailure = error; }
                try { appraisalSuspension.SetValue(host, evidence.OriginalAppraisalsSuspended); }
                catch (Exception error)
                {
                    if (cleanupFailure == null) cleanupFailure = error;
                    else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_APPRAISAL_SUSPENSION_RESTORE_FAIL: " + error);
                }
                try
                {
                    RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt")
                        .SetValue(host, evidence.OriginalReturningGreetingRetryAt);
                }
                catch (Exception error)
                {
                    if (cleanupFailure == null) cleanupFailure = error;
                    else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_GREETING_RETRY_RESTORE_FAIL: " + error);
                }
                if (!string.IsNullOrEmpty(evidence.CheckpointTitle))
                {
                    try { DeleteCheckpoint(GameManager.Instance, character, evidence.CheckpointTitle); }
                    catch (Exception error)
                    {
                        if (cleanupFailure == null) cleanupFailure = error;
                        else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_CHECKPOINT_CLEANUP_FAIL: " + error);
                    }
                }
                if (!string.IsNullOrEmpty(evidence.StagingCheckpointTitle))
                {
                    try { DeleteCheckpoint(GameManager.Instance, character, evidence.StagingCheckpointTitle); }
                    catch (Exception error)
                    {
                        if (cleanupFailure == null) cleanupFailure = error;
                        else Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_STAGING_CLEANUP_FAIL: " + error);
                    }
                }
            }

            if (restoreFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_BASELINE_RESTORE_FAIL: " + restoreFailure);
                if (cleanupFailure != null)
                    Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_CLEANUP_FAIL_AFTER_RESTORE_FAILURE: " + cleanupFailure);
                throw new InvalidOperationException("Conversation mood probe could not restore exact baseline.", restoreFailure);
            }
            if (probeFailure != null)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_MOOD_BASELINE_CLEANUP_AFTER_FAILURE: restored=true; checkpoint-deleted=" +
                    !string.IsNullOrEmpty(evidence.CheckpointTitle) + ".");
                if (baselineFailure != null && baselineFailure != probeFailure)
                    Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_BASELINE_VERIFY_FAIL_AFTER_PROBE_FAILURE: " + baselineFailure);
                if (cleanupFailure != null)
                    Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_CLEANUP_FAIL_AFTER_PROBE_FAILURE: " + cleanupFailure);
                throw new InvalidOperationException("Conversation mood probe failed after exact baseline restore.", probeFailure);
            }
            if (baselineFailure != null)
            {
                if (cleanupFailure != null)
                    Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_CLEANUP_FAIL_AFTER_BASELINE_FAILURE: " + cleanupFailure);
                throw new InvalidOperationException("Conversation mood probe baseline verification failed.", baselineFailure);
            }
            if (cleanupFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_CLEANUP_FAIL: " + cleanupFailure);
                throw new InvalidOperationException("Conversation mood probe cleanup failed.", cleanupFailure);
            }


            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOOD_BASELINE_RESTORED: character=" + character +
                "; save=" + BaselineSave + "; checkpoint-deleted=" + !string.IsNullOrEmpty(evidence.CheckpointTitle) + ".");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOOD_LIVE_SOCIAL_COMPLETION_PASS: npc=" + evidence.PersonId +
                "; route=SubmitSocialText; full-SubmitFreeText-before-controls=" + evidence.FreeTextBeforeControlsBranch +
                "; full-SubmitFreeText-after-controls=" + evidence.FreeTextAfterControlsBranch + "; actual-production-request=true; requests=" + evidence.LiveRequests +
                "; fresh-completions=" + evidence.LiveCompletions + "; typed-result=true; delta=" + evidence.LiveDelta +
                "; mood=" + evidence.LiveMoodBefore + "->" + evidence.LiveMoodAfter + ".");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOOD_CONTROLLED_CALLBACK_PASS: accepted-delta=3,-2,0; " +
                "invalid-no-mutation=true; replay-no-mutation=true; stale-session-no-mutation=true; same-person-reopen=true; " +
                "next-request-mood-grounding=true; verifier-replacement-no-mood-commit=true.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOOD_DISK_ROLLBACK_PASS: title=" + evidence.CheckpointTitle +
                "; checkpoint-value=" + evidence.SavedValue + "; checkpoint-sequence=" + evidence.SavedSequence +
                "; disk-reload=true; baseline-rollback=true.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, Evidence evidence)
        {
            VerifyEnumeratorTreeDriver();
            GameManager game = GameManager.Instance;
            SaveLoadManager saves = game.SaveLoadManager;
            saves.EnumerateSaves();
            if (saves.FindSaveFolderByNames(character, BaselineSave) < 0)
                throw new InvalidOperationException("Exact mood baseline fixture is missing; refusing probe.");
            evidence.BaselineClock = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            evidence.BaselinePlayerPosition = game.PlayerMotor.transform.position;

            WorldStateStore store = ReadHostField(host, "store") as WorldStateStore;
            if (store == null) throw new InvalidOperationException("Conversation mood probe cannot read loaded store.");
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                game.PlayerEnterExit.TransitionExterior();
                DaggerfallUI.Instance.PopToHUD();
                float exteriorDeadline = Time.realtimeSinceStartup + 30f;
                while (game.PlayerEnterExit.IsPlayerInsideBuilding && Time.realtimeSinceStartup < exteriorDeadline)
                    yield return new WaitForSecondsRealtime(0.25f);
                Require(!game.PlayerEnterExit.IsPlayerInsideBuilding,
                    "Mood probe could not enter the saved Gothway Garden exterior scene.");
            }

            ResidentActor actor = null;
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            ActorCandidateCounts candidateCounts = null;
            while (!actor && Time.realtimeSinceStartup < actorDeadline)
            {
                store = ReadHostField(host, "store") as WorldStateStore;
                candidateCounts = new ActorCandidateCounts { StoreAvailable = store != null, RequireNeutral = true };
                actor = store == null ? null : FindUnusedManagedAdult(store, game.PlayerMotor.transform.position, candidateCounts);
                if (!actor) yield return new WaitForSecondsRealtime(0.25f);
            }
            if (!actor)
            {
                string diagnostics = candidateCounts == null ? "store unavailable." : candidateCounts.ToString();
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_ACTOR_SELECTION_FAIL: " + diagnostics);
                throw new InvalidOperationException("No eligible active adult resident was available after 45 seconds. " + diagnostics);
            }
            float distanceBeforeStage = Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOOD_ACTOR_SELECTED: npc=" + actor.Resident.Id +
                "; distance-before-stage=" + distanceBeforeStage.ToString("0.00", CultureInfo.InvariantCulture) +
                "; " + candidateCounts + ".");
            StagePlayerNearActor(streets, actor, game);
            evidence.PersonId = actor.Resident.Id;
            ConversationMoodView initial = store.CaptureConversationMood(evidence.PersonId);
            evidence.InitialValue = initial.Value;
            evidence.InitialSequence = initial.AcceptedTurnSequence;
            Require(initial.Value == 0 && initial.AcceptedTurnSequence == 0,
                "Owned baseline must start selected NPC at neutral mood and sequence zero.");

            // This older checkpoint tests a historical obligation event with
            // authored IDs. Import before Talk, then reload an owned temporary
            // save so all native actor and conversation bindings are reacquired.
            var relationshipEvidence = NpcExperienceEvent.FromNativeOutcome(evidence.PersonId, evidence.PersonId,
                "probe-receipt:relationship", "probe-episode:relationship", "probe-outcome:relationship",
                NpcExperienceEventKind.ObligationFulfilled, (double)DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                NpcResponsibleActorAttribution.Known, NpcDirectedRelationship.PlayerTargetId);
            NpcProbeLegacyExperienceFixture.Import(store, relationshipEvidence);
            actor = null; // The pre-import ResidentActor belongs to the old binding epoch.
            evidence.StagingCheckpointTitle = "People - IMM09 Seed " + Guid.NewGuid().ToString("N").Substring(0, 8);
            yield return SaveCheckpoint(game, character, evidence.StagingCheckpointTitle);
            yield return LoadSave(host, streets, character, evidence.StagingCheckpointTitle, true);
            DeleteCheckpoint(game, character, evidence.StagingCheckpointTitle);
            evidence.StagingCheckpointTitle = null;
            store = ReadHostField(host, "store") as WorldStateStore;
            Require(Convert.ToBoolean(RequireField(typeof(PeopleOfTheIliacBayMod),
                "experienceAppraisalsSuspended").GetValue(host), CultureInfo.InvariantCulture),
                "Historical mood fixture lost appraisal suspension on owned reload.");
            Require(store != null && store.ReadPendingNpcExperiences(evidence.PersonId, 100).Exists(
                item => item.DeduplicationKey == relationshipEvidence.DeduplicationKey),
                "Historical relationship experience did not survive owned staging reload.");
            float reboundDeadline = Time.realtimeSinceStartup + 45f;
            while (!actor && Time.realtimeSinceStartup < reboundDeadline)
            {
                actor = FindActor(streets, evidence.PersonId);
                if (!actor || actor.Resident.Id != evidence.PersonId || !actor.Person) actor = null;
                if (!actor) yield return new WaitForSecondsRealtime(0.25f);
            }
            Require(actor != null, "Owned staging reload did not rebind the same mood actor.");

            DaggerfallTalkWindow talk = null;
            yield return OpenTalk(host, game, actor, value => talk = value);
            Require(talk != null, "Native Talk did not open for selected managed resident.");
            yield return AgenticDialogueUiProbe.Verify(host, talk);

            // One normal end-to-end turn supplies real local-model evidence.
            LocalDialogueClient client = ReadHostField(host, "localDialogue") as LocalDialogueClient;
            if (!client) throw new InvalidOperationException("Local dialogue client is unavailable.");
            long requestsBefore = ReadLongProperty(client, "ReplyRequestCount");
            long completionsBefore = ReadLongProperty(client, "ReplyCompletionCount");
            long sequenceBefore = store.CaptureConversationMood(evidence.PersonId).AcceptedTurnSequence;
            phase = ReplyPhase.ObserveLive;
            RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy").SetValue(host, false);
            const string livePlayerText = "Hi, may I ask what you are up to?";
            object liveSnapshot = CaptureSnapshot(host, talk, livePlayerText);
            MethodInfo readHistory = typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory", Members);
            Require(readHistory != null && readHistory.IsStatic,
                "Required native mood history reader is missing for live social completion.");
            string liveHistory = Convert.ToString(readHistory.Invoke(null, new object[] { talk }),
                CultureInfo.InvariantCulture) ?? string.Empty;
            MethodInfo submit = RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitSocialText", 9);
            submit.Invoke(host, new object[] { talk, livePlayerText, liveHistory, null, string.Empty,
                null, false, null, liveSnapshot });
            float liveDeadline = Time.realtimeSinceStartup + 30f;
            while ((ReadLongProperty(client, "ReplyRequestCount") <= requestsBefore ||
                ReadLongProperty(client, "ReplyCompletionCount") <= completionsBefore ||
                store.CaptureConversationMood(evidence.PersonId).AcceptedTurnSequence <= sequenceBefore) &&
                Time.realtimeSinceStartup < liveDeadline)
                yield return null;
            phase = ReplyPhase.PassThrough;
            evidence.LiveRequests = ReadLongProperty(client, "ReplyRequestCount") - requestsBefore;
            evidence.LiveCompletions = ReadLongProperty(client, "ReplyCompletionCount") - completionsBefore;
            if (evidence.LiveRequests != 1 || evidence.LiveCompletions != 1)
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_LIVE_REQUEST_DIAGNOSTIC: requests=" + evidence.LiveRequests +
                    "; completions=" + evidence.LiveCompletions + "; busy=" + ReadHostField(host, "conversationBusy") +
                    "; session-open=" + ReadHostField(host, "conversationSessionOpen") +
                    "; memory-window-matches=" + ReferenceEquals(ReadHostField(host, "memoryWindow"), talk) +
                    "; conversation-person=" + ReadHostField(host, "conversationPersonId") +
                    "; memory-person=" + ReadHostField(host, "memoryPersonId") +
                    "; context-session-id=" + ReadHostField(host, "conversationContextSessionId") +
                    "; context-session-speaker-bound=" + ((ReadHostField(host, "conversationContextSessionSpeaker") as UnityEngine.Object) != null) +
                    "; wrapper-observed=" + (lastRequest != null) + ".");
            Require(evidence.LiveRequests == 1 && evidence.LiveCompletions == 1,
                "Live social turn did not produce exactly one fresh LocalDialogue request and completion.");
            Require(lastRequest != null,
                "Mood-enabled production callback wrapper was not observed within 30 seconds; runtime integration is still red.");
            SocialDialogueResult liveResult = null;
            Require(lastRequest != null && ReadBoolMember(lastRequest, "MoodEnabled") &&
                ReadIntMember(lastRequest, "MoodValue") == initial.Value &&
                SocialDialogueResult.TryParse(lastLiveReply, out liveResult),
                "Live model callback did not contain a typed result grounded in captured baseline mood.");
            ConversationMoodView afterLive = store.CaptureConversationMood(evidence.PersonId);
            evidence.LiveDelta = liveResult.MoodDelta;
            evidence.LiveMoodBefore = afterLive.Value - liveResult.MoodDelta;
            evidence.LiveMoodAfter = afterLive.Value;
            Require(afterLive.AcceptedTurnSequence == sequenceBefore + 1 &&
                afterLive.Value >= -100 && afterLive.Value <= 100,
                "Live accepted social reply did not advance exactly one mood sequence.");
            Require(CountModelExchange(store, evidence.PersonId, livePlayerText, liveResult.Reply) == 1,
                "Live accepted social callback did not persist exactly one model exchange row.");

            Require(lastRequest.ConversationReaction.Contains("Current physical location: outdoors"),
                "Live outdoor reply omitted authoritative physical location.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_GROUNDING_REPLY: "+liveResult.Reply);
            string journey;
            Require(((ConversationContextSnapshot)liveSnapshot).CurrentJourney.TryGetVerified(out journey),"Managed speaker lacks captured journey.");
            Require(lastRequest.ConversationReaction.Contains("Journey when this conversation began:"),
                "Captured journey was omitted from social prompt.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_JOURNEY_GROUNDING_CONTEXT: "+journey);
            yield return VerifyPendingOfferGrounding(host,streets,talk,client,store,evidence);
            yield return RunFullSubmitFreeText(host, talk, client, store, evidence, "before-controls",
                "What has been the best part of your day so far?");
            ConversationMoodView afterFullRoute = store.CaptureConversationMood(evidence.PersonId);

            // Inject controlled results through the same production callback
            // captured for an ordinary mood-enabled request.
            long before = afterFullRoute.AcceptedTurnSequence;
            int beforeValue = afterFullRoute.Value;
            Action<string> staleCallback = StartControlledReply(host, talk, "IMM09-invalid-control", out string invalidRequestText);
            CancelTransport(host);
            staleCallback(invalidRequestText);
            ConversationMoodView afterInvalid = store.CaptureConversationMood(evidence.PersonId);
            Require(afterInvalid.Value == beforeValue && afterInvalid.AcceptedTurnSequence == before,
                "Invalid typed callback changed persistent mood or accepted sequence.");
            Require(CountModelExchange(store, evidence.PersonId, "IMM09-invalid-control", "Invalid control must not commit.") == 0,
                "Invalid typed callback persisted a model exchange row.");

            Action<string> positive = StartControlledReply(host, talk, "IMM09-controlled-positive", out _);
            CancelTransport(host);
            positive("{\"reply\":\"I am glad we spoke.\",\"moodDelta\":3}");
            ConversationMoodView afterPositive = store.CaptureConversationMood(evidence.PersonId);
            Require(afterPositive.Value == beforeValue + 3 && afterPositive.AcceptedTurnSequence == before + 1,
                "Controlled positive social reply did not commit mood and accepted sequence once.");
            Require(CountModelExchange(store, evidence.PersonId, "IMM09-controlled-positive", "I am glad we spoke.") == 1,
                "Controlled positive callback did not persist exactly one model exchange row.");

            // Same closure replay uses its original captured sequence. It must no-op.
            positive("{\"reply\":\"I am glad we spoke.\",\"moodDelta\":3}");
            RequireMood(store, evidence.PersonId, afterPositive.Value, afterPositive.AcceptedTurnSequence,
                "Replayed accepted social callback changed mood or sequence.");
            Require(CountModelExchange(store, evidence.PersonId, "IMM09-controlled-positive", "I am glad we spoke.") == 1,
                "Replayed accepted callback duplicated its model exchange row.");

            Action<string> negative = StartControlledReply(host, talk, "IMM09-controlled-negative", out _);
            CancelTransport(host);
            negative("{\"reply\":\"That was difficult to hear.\",\"moodDelta\":-2}");
            ConversationMoodView afterNegative = store.CaptureConversationMood(evidence.PersonId);
            Require(afterNegative.Value == afterPositive.Value - 2 &&
                afterNegative.AcceptedTurnSequence == afterPositive.AcceptedTurnSequence + 1,
                "Controlled negative social reply did not commit exact delta and sequence.");
            Require(CountModelExchange(store, evidence.PersonId, "IMM09-controlled-negative", "That was difficult to hear.") == 1,
                "Controlled negative callback did not persist exactly one model exchange row.");

            Action<string> neutral = StartControlledReply(host, talk, "IMM09-controlled-neutral", out _);
            CancelTransport(host);
            neutral("{\"reply\":\"Thank you for telling me.\",\"moodDelta\":0}");
            ConversationMoodView afterNeutral = store.CaptureConversationMood(evidence.PersonId);
            Require(afterNeutral.Value == afterNegative.Value &&
                afterNeutral.AcceptedTurnSequence == afterNegative.AcceptedTurnSequence + 1,
                "Controlled zero-delta reply did not advance sequence exactly once without changing mood.");
            Require(CountModelExchange(store, evidence.PersonId, "IMM09-controlled-neutral", "Thank you for telling me.") == 1,
                "Controlled zero-delta callback did not persist exactly one model exchange row.");

            const string replacementPlayerText = "IMM09-verified-replacement";
            const string trustedReplacement = "That request is outside what I can verify, so I will not act on it.";
            string visibleReplacement = null;
            Action<string> verifiedReplacement = StartControlledReply(host, talk, replacementPlayerText, out _,
                ignored => trustedReplacement, answer => visibleReplacement = answer);
            Require(lastRequest != null && ReadBoolMember(lastRequest, "MoodEnabled"),
                "Verifier replacement case did not use a mood-enabled social request.");
            CancelTransport(host);
            const string replacedModelProposal = "The unverified model proposal must not be shown.";
            const string replacedModelPayload = "{\"reply\":\"The unverified model proposal must not be shown.\",\"moodDelta\":9}";
            verifiedReplacement(replacedModelPayload);
            ConversationMoodView afterVerifiedReplacement = store.CaptureConversationMood(evidence.PersonId);
            Require(afterVerifiedReplacement.Value == afterNeutral.Value &&
                afterVerifiedReplacement.AcceptedTurnSequence == afterNeutral.AcceptedTurnSequence,
                "Verifier-replaced typed reply changed persistent mood or accepted sequence.");
            Require(string.Equals(visibleReplacement, trustedReplacement, StringComparison.Ordinal),
                "Verifier replacement callback did not expose the trusted replacement text.");
            Require(CountExchange(store, evidence.PersonId, replacementPlayerText, trustedReplacement, "action") == 1 &&
                CountExchange(store, evidence.PersonId, replacementPlayerText, replacedModelProposal, "model") == 0 &&
                CountExchange(store, evidence.PersonId, replacementPlayerText, replacedModelPayload, "model") == 0 &&
                CountExchange(store, evidence.PersonId, replacementPlayerText, replacedModelPayload, "action") == 0,
                "Verifier replacement was not journaled exactly once as trusted text or raw model payload leaked into journal.");

            // Preserve a callback from the old session, then reopen the same NPC.
            Action<string> staleSession = StartControlledReply(host, talk, "IMM09-stale-session", out _);
            CancelTransport(host);
            yield return CloseTalk(host);
            actor = FindActor(streets, evidence.PersonId);
            if (!actor) throw new InvalidOperationException("Same registered NPC disappeared before Talk reopen.");
            yield return OpenTalk(host, game, actor, value => talk = value);
            Require(ReadConversationPerson(host) == evidence.PersonId,
                "Native Talk reopen bound a different resident.");
            ConversationMoodView beforeStale = store.CaptureConversationMood(evidence.PersonId);
            Require(beforeStale.Value == afterNeutral.Value &&
                beforeStale.AcceptedTurnSequence == afterNeutral.AcceptedTurnSequence,
                "Closing and reopening the same NPC reset persistent mood or accepted sequence.");
            staleSession("{\"reply\":\"This old reply must be discarded.\",\"moodDelta\":9}");
            RequireMood(store, evidence.PersonId, beforeStale.Value, beforeStale.AcceptedTurnSequence,
                "Stale callback from closed same-person session changed mood or accepted sequence.");
            Require(CountModelExchange(store, evidence.PersonId, "IMM09-stale-session", "This old reply must be discarded.") == 0,
                "Stale callback from closed session persisted a model exchange row.");

            // Next immutable social request must carry persisted mood, after reopen.
            Action<string> grounding = StartControlledReply(host, talk, "IMM09-grounding-control", out _);
            Require(lastRequest != null && ReadBoolMember(lastRequest, "MoodEnabled") &&
                ReadIntMember(lastRequest, "MoodValue") == beforeStale.Value,
                "Reopened same-person next social request did not carry persisted mood value.");
            DialoguePlan groundedPlan = LocalDialogue.Build(lastRequest);
            string expectedMoodInstruction = "Current persistent mood value: " +
                beforeStale.Value.ToString(CultureInfo.InvariantCulture);
            Require(groundedPlan != null && groundedPlan.Prompt != null &&
                groundedPlan.Prompt.IndexOf(expectedMoodInstruction, StringComparison.Ordinal) >= 0,
                "Next reopened-person model prompt did not ground itself in persisted mood.");
            CancelTransport(host);
            grounding("{\"reply\":\"I remember our conversation.\",\"moodDelta\":0}");
            ConversationMoodView atCheckpoint = store.CaptureConversationMood(evidence.PersonId);
            var personalityAtCheckpoint = store.ReadNpcPersonality(evidence.PersonId);
            Require(personalityAtCheckpoint != null && personalityAtCheckpoint.Version == 3 &&
                personalityAtCheckpoint.SelectedTraits.Count == 3,
                "Native managed speaker did not materialize its stable personality profile.");
            string savedPersonality = personalityAtCheckpoint.Description();
            Require(store.ReadNpcRelationship(evidence.PersonId, NpcDirectedRelationship.PlayerTargetId) == null,
                "Owned relationship fixture must start without a directed row.");
            var beforeRelationship = (ConversationContextSnapshot)CaptureSnapshot(host, talk, "IMM09-before-relationship-fixture");
            // Controlled typed evidence tests storage and native serialization.
            // This is not evidence that an actual appointment/action producer ran.
            var relationshipProposal = new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = store.NpcRelationshipLedgerRevision,
                ObserverId = evidence.PersonId,
                TargetId = NpcDirectedRelationship.PlayerTargetId,
                RelationshipUpdate = new NpcRelationshipUpdateProposal
                {
                    ExpectedRevision = 0, Evidence = relationshipEvidence,
                    TrustDelta = 5, ResentmentDelta = 2, InterpersonalFearDelta = 1
                }
            };
            Require(host.IsCurrentConversationContext(beforeRelationship, talk), "Pre-event snapshot was already stale.");
            Require(store.TryCommitNpcRelationship(relationshipProposal, relationshipProposal.ExpectedLedgerRevision),
                "Controlled relationship fixture did not commit.");
            Require(!host.IsCurrentConversationContext(beforeRelationship, talk),
                "Intervening relationship event did not invalidate a captured conversation snapshot.");
            var savedRelationship = store.ReadNpcRelationship(evidence.PersonId, NpcDirectedRelationship.PlayerTargetId);
            Require(savedRelationship != null && savedRelationship.TrustKnown && savedRelationship.ReliabilityTrust == 55,
                "Controlled relationship fixture did not record a separate reliability trust value.");
            Require(!store.TryEnqueueNpcExperience(relationshipEvidence) &&
                store.ReadPendingNpcExperiences(evidence.PersonId, 100).Exists(
                    item => item.DeduplicationKey == relationshipEvidence.DeduplicationKey),
                "Historical experience inbox lost its canonical outcome or accepted a public replay.");
            evidence.SavedValue = atCheckpoint.Value;
            evidence.SavedSequence = atCheckpoint.AcceptedTurnSequence;
            yield return CloseTalk(host);

            yield return SaveCheckpoint(game, character, evidence);
            actor = FindActor(streets, evidence.PersonId);
            if (!actor) throw new InvalidOperationException("Same registered NPC disappeared before checkpoint mutation.");
            yield return OpenTalk(host, game, actor, value => talk = value);
            SetGreetingSuppressed(host);
            Action<string> afterCheckpoint = StartControlledReply(host, talk, "IMM09-post-checkpoint-mutation", out _);
            CancelTransport(host);
            afterCheckpoint("{\"reply\":\"I am feeling better.\",\"moodDelta\":4}");
            ConversationMoodView mutated = store.CaptureConversationMood(evidence.PersonId);
            Require(mutated.Value == evidence.SavedValue + 4 && mutated.AcceptedTurnSequence == evidence.SavedSequence + 1,
                "Post-checkpoint controlled exchange did not mutate mood before disk rollback check.");

            yield return RunFullSubmitFreeText(host, talk, client, store, evidence, "after-controls",
                "What is something you enjoy about this place?");
            yield return CloseTalk(host);

            yield return LoadSave(host, streets, character, evidence.CheckpointTitle, true);
            WorldStateStore restored = ReadHostField(host, "store") as WorldStateStore;
            Require(restored != null, "Native disk reload did not restore the world store.");
            RequireMood(restored, evidence.PersonId, evidence.SavedValue, evidence.SavedSequence,
                "Actual disk reload did not restore checkpoint mood and accepted sequence.");
            Require(restored.ReadNpcPersonality(evidence.PersonId) != null &&
                restored.ReadNpcPersonality(evidence.PersonId).Description() == savedPersonality,
                "Actual disk reload changed the saved personality traits.");
            var restoredRelationship = restored.ReadNpcRelationship(evidence.PersonId, NpcDirectedRelationship.PlayerTargetId);
            Require(restoredRelationship != null && restoredRelationship.ReliabilityTrust == savedRelationship.ReliabilityTrust &&
                restoredRelationship.TrustConfidence == savedRelationship.TrustConfidence &&
                restoredRelationship.TrustEvidenceCount == savedRelationship.TrustEvidenceCount &&
                restoredRelationship.Resentment == savedRelationship.Resentment &&
                restoredRelationship.InterpersonalFear == savedRelationship.InterpersonalFear &&
                restoredRelationship.Revision == savedRelationship.Revision,
                "Native disk serialization did not preserve directed relationship dimensions and evidence.");
            relationshipProposal.ExpectedLedgerRevision = restored.NpcRelationshipLedgerRevision;
            relationshipProposal.RelationshipUpdate.ExpectedRevision = restoredRelationship.Revision;
            Require(!restored.TryCommitNpcRelationship(relationshipProposal, restored.NpcRelationshipLedgerRevision) &&
                restored.NpcRelationshipLedgerRevision == relationshipProposal.ExpectedLedgerRevision,
                "Native disk reload lost canonical outcome replay protection.");
            int matchingPending = 0;
            foreach (var pendingEvent in restored.ReadPendingNpcExperiences(evidence.PersonId, 100))
                if (pendingEvent.DeduplicationKey == relationshipEvidence.DeduplicationKey &&
                    pendingEvent.OutcomeId == relationshipEvidence.OutcomeId &&
                    pendingEvent.ResponsibleActorId == NpcDirectedRelationship.PlayerTargetId)
                    matchingPending++;
            Require(matchingPending == 1 && !restored.TryEnqueueNpcExperience(relationshipEvidence),
                "Native disk reload lost pending experience identity or allowed public caller-constructed native admission.");
            Require(CountModelExchange(restored, evidence.PersonId, "IMM09-post-checkpoint-mutation", "I am feeling better.") == 0 &&
                CountModelExchange(restored, evidence.PersonId, "IMM09-grounding-control", "I remember our conversation.") == 1,
                "Actual disk reload did not roll back post-checkpoint memory or preserve checkpoint exchange.");

            actor = FindActor(streets, evidence.PersonId);
            Require(actor != null, "Same registered NPC disappeared before timed mood acceptance.");
            yield return OpenTalk(host, game, actor, value => talk = value);
            SetGreetingSuppressed(host);
            for (int recoveryTurn = 0; recoveryTurn < 2; recoveryTurn++)
            {
                Action<string> seedRecovery = StartControlledReply(host, talk,
                    "IMM09-recovery-anchor-" + recoveryTurn, out _);
                CancelTransport(host);
                seedRecovery(recoveryTurn == 0
                    ? "{\"reply\":\"Your kindness cheers me.\",\"moodDelta\":10}"
                    : "{\"reply\":\"I appreciate that.\",\"moodDelta\":10}");
            }
            double beforeRecoveryAt = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            ConversationMoodView recoveryAnchor = restored.CaptureConversationMood(evidence.PersonId);
            // Advance the actual owned GAME clock while Talk keeps the same speaker bound.
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)restored.ConversationMoodHalfLifeSeconds(evidence.PersonId));
            double recoveredAt = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            Require(recoveredAt > beforeRecoveryAt + 10000d, "Native game clock did not advance for mood recovery.");
            object recoverySnapshot = CaptureSnapshot(host, talk, "IMM09-timed-mood-context");
            var timedSnapshot = (ConversationContextSnapshot)recoverySnapshot;
            var capturedMood = timedSnapshot.PersistentMood;
            Require(timedSnapshot.DirectedRelationship != null && timedSnapshot.DirectedRelationship.ReliabilityTrust == 55,
                "Native context did not project its saved directed relationship.");
            // Production snapshots retain the native fractional second. At an
            // exact half-life an odd integer mood can round differently if the
            // expectation uses ToSeconds(), which truncates that fraction.
            int expectedRecovered = MoodRecovery.ValueAt(recoveryAnchor.Value, recoveryAnchor.UpdatedAt, timedSnapshot.CapturedGameTime,
                restored.ConversationMoodHalfLifeSeconds(evidence.PersonId));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TIMED_MOOD_DIAGNOSTIC: raw=" + recoveryAnchor.Value +
                "; captured=" + (capturedMood == null ? "null" : capturedMood.Value.ToString(CultureInfo.InvariantCulture)) +
                "; expected=" + expectedRecovered + "; anchor-at=" + recoveryAnchor.UpdatedAt.ToString("R", CultureInfo.InvariantCulture) +
                "; integral-clock=" + recoveredAt.ToString("R", CultureInfo.InvariantCulture) +
                "; snapshot-clock=" + timedSnapshot.CapturedGameTime.ToString("R", CultureInfo.InvariantCulture) +
                "; revision=" + (capturedMood == null ? -1 : capturedMood.StateRevision) + "/" + recoveryAnchor.Revision +
                "; sequence=" + (capturedMood == null ? -1 : capturedMood.AcceptedTurnSequence) + "/" + recoveryAnchor.AcceptedTurnSequence + ".");
            Require(capturedMood != null && capturedMood.Value == expectedRecovered &&
                Math.Abs(capturedMood.Value) < Math.Abs(recoveryAnchor.Value) &&
                capturedMood.StateRevision == recoveryAnchor.Revision &&
                capturedMood.AcceptedTurnSequence == recoveryAnchor.AcceptedTurnSequence,
                "Actual game-time snapshot did not carry the cooled mood and unchanged evidence revision.");
            var afterRecoveryRead = restored.CaptureConversationMood(evidence.PersonId);
            Require(afterRecoveryRead.Value == recoveryAnchor.Value && afterRecoveryRead.UpdatedAt == recoveryAnchor.UpdatedAt &&
                afterRecoveryRead.Revision == recoveryAnchor.Revision &&
                restored.ReadNpcPersonality(evidence.PersonId).Description() == savedPersonality,
                "Timed snapshot read mutated the saved mood anchor or personality.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TIMED_MOOD_PERSONALITY_PASS: raw=" + recoveryAnchor.Value +
                "; recovered=" + capturedMood.Value + "; revision=" + capturedMood.StateRevision +
                "; personality-traits=3; disk-reload=true; pure-read=true; game-time=true.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RELATIONSHIP_STORAGE_PASS: source=controlled-typed-fixture; trust=55; evidence=1; resentment=2; fear=1; stale-snapshot-rejected=true; native-disk-roundtrip=true; replay-rejected=true; gameplay-producer=false.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_EXPERIENCE_INBOX_STORAGE_PASS: source=controlled-typed-fixture; schema=39; pending-outcomes=1; native-disk-roundtrip=true; replay-rejected=true; appraisal-consumer=not-exercised.");
            NpcExperienceCommitProbe.Verify();
            NpcFrameworkPersistenceProbe.Verify();
            yield return CloseTalk(host);
        }

        private static IEnumerator VerifyPendingOfferGrounding(PeopleOfTheIliacBayMod host,StreetPopulation streets,
            DaggerfallTalkWindow talk,LocalDialogueClient client,WorldStateStore store,Evidence evidence)
        {
            var offerField=RequireField(typeof(PeopleOfTheIliacBayMod),"pendingConversationAction");
            var historyField=RequireField(typeof(PeopleOfTheIliacBayMod),"pendingConversationActionHistory");
            Require(offerField.GetValue(host)==null,"Grounding fixture unexpectedly has a pending offer.");
            NpcActionOption follow=null;
            foreach(var option in streets.ConversationActionCandidates(evidence.PersonId))
                if(option.Kind==NpcActionKind.Follow && option.Target!=null){follow=option;break;}
            Require(follow!=null,"Grounding fixture needs an available Follow option.");
            int session=Convert.ToInt32(ReadHostField(host,"memorySession"));
            var offer=new NpcActionOffer(evidence.PersonId,session,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                new NpcActionDecision {Option=follow,DurationSeconds=60});
            var history=typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory",Members);
            offerField.SetValue(host,offer);historyField.SetValue(host,history.Invoke(null,new object[]{talk}));
            var priorTrace=client.ForegroundClassificationTrace;
            int unrelated=0;string raw=null;
            client.ForegroundClassificationTrace=(request,response,classification)=>
            {
                if(priorTrace!=null)priorTrace(request,response,classification);
                if(request!=null && request.Contains("Exact pending offer:")){unrelated++;raw=classification;}
            };
            try
            {
                foreach(string question in new[]{"Are you on your way home?","We are outdoors in town. Are you inside your house right now?","Where are you heading, and why?"})
                {
                    unrelated=0;raw=null;
                    yield return RunFullSubmitFreeText(host,talk,client,store,evidence,"pending-offer-grounding",question);
                    Require(unrelated==0 || unrelated==1 && raw!=null && raw.Trim()=="NONE",
                        "Unrelated question did not pass the pending-offer interpreter as NONE: "+raw);
                    Require(ReferenceEquals(offerField.GetValue(host),offer) &&
                        Equals(historyField.GetValue(host),history.Invoke(null,new object[]{talk})),
                        "Unrelated conversation lost the offer or failed to refresh its transcript binding.");
                    NpcActionOption stillOffered;int stillDuration;
                    Require(offer.TryGetSemanticCandidate(evidence.PersonId,session,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                        out stillOffered,out stillDuration) && stillOffered.Kind==follow.Kind && stillOffered.Id==follow.Id && stillDuration==60,
                        "Unrelated conversation consumed or changed the pending offer.");
                    Require(lastRequest!=null && lastRequest.ConversationReaction.Contains("Current physical location: outdoors"),
                        "Grounding follow-up omitted the observed outdoor location.");
                    SocialDialogueResult reply;
                    Require(SocialDialogueResult.TryParse(lastLiveReply,out reply),"Grounding reply was not typed social dialogue.");
                    Require(reply.Reply.IndexOf("agreed to do anything new",StringComparison.OrdinalIgnoreCase)<0,
                        "Unrelated question received the action refusal fallback.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PENDING_OFFER_GROUNDING_REPLY: "+question+" => "+reply.Reply);
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PENDING_OFFER_GROUNDING_PASS: realRouting=true; unrelated=NONE-or-social-topic; offerPreserved=true; outdoorContext=true; turns=3");
            }
            finally
            {
                client.ForegroundClassificationTrace=priorTrace;
                if(ReferenceEquals(offerField.GetValue(host),offer)){offerField.SetValue(host,null);historyField.SetValue(host,null);}
            }
        }

        private static IEnumerator RunFullSubmitFreeText(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk,
            LocalDialogueClient client, WorldStateStore store, Evidence evidence, string scenario, string freeTextPlayerText)
        {
            // Exercise actual free-text entry point. Routing may produce a
            // mood-backed offer, standalone offer, or standalone social reply.
            // Accept only exact branch sequence and one matching durable outcome.
            phase = ReplyPhase.ObserveFreeText;
            freeTextBranch = FreeTextBranch.None;
            freeTextBranchEvents = 0;
            freeTextMoodCompletionEvents = 0;
            freeTextOfferEvents = 0;
            freeTextBranchSequence.Clear();
            freeTextOffer = null;
            heldCompletion = null;
            lastRequest = null;
            lastLiveReply = null;
            var transcriptField = typeof(DaggerfallTalkWindow).GetField("listboxConversation", Members);
            var transcriptBefore = transcriptField == null ? null : transcriptField.GetValue(talk) as ListBox;
            Require(transcriptBefore != null, "Full free-text mood probe cannot read native conversation transcript.");
            int freeTextRowsBefore = transcriptBefore.Count;
            long freeTextRequestsBefore = ReadLongProperty(client, "ReplyRequestCount");
            long freeTextCompletionsBefore = ReadLongProperty(client, "ReplyCompletionCount");
            ConversationMoodView freeTextMoodBefore = store.CaptureConversationMood(evidence.PersonId);
            object pendingOfferBefore = ReadHostField(host, "pendingConversationAction");
            int memorySession = Convert.ToInt32(ReadHostField(host, "memorySession"), CultureInfo.InvariantCulture);
            RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy").SetValue(host, false);
            MethodInfo submitFreeText = RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitFreeText", 2);
            submitFreeText.Invoke(host, new object[] { talk, freeTextPlayerText });
            float freeTextDeadline = Time.realtimeSinceStartup + 90f;
            while (freeTextBranch == FreeTextBranch.None && Time.realtimeSinceStartup < freeTextDeadline)
                yield return null;
            while (freeTextBranch == FreeTextBranch.MoodSocialCompletion &&
                (ReadLongProperty(client, "ReplyRequestCount") <= freeTextRequestsBefore ||
                 ReadLongProperty(client, "ReplyCompletionCount") <= freeTextCompletionsBefore) &&
                Time.realtimeSinceStartup < freeTextDeadline)
                yield return null;
            phase = ReplyPhase.PassThrough;
            evidence.FreeTextBranch = FreeTextBranchDescription();
            evidence.FreeTextRequests = ReadLongProperty(client, "ReplyRequestCount") - freeTextRequestsBefore;
            evidence.FreeTextCompletions = ReadLongProperty(client, "ReplyCompletionCount") - freeTextCompletionsBefore;
            var transcriptAfter = transcriptField.GetValue(talk) as ListBox;
            evidence.FreeTextVisibleRows = transcriptAfter == null ? 0 : transcriptAfter.Count - freeTextRowsBefore;
            bool freeTextBusy = Convert.ToBoolean(ReadHostField(host, "conversationBusy"), CultureInfo.InvariantCulture);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOOD_FULL_ROUTING_BRANCH: scenario=" + scenario + "; branch=" + evidence.FreeTextBranch +
                "; branch-events=" + freeTextBranchEvents + "; mood-completion-events=" + freeTextMoodCompletionEvents +
                "; offer-events=" + freeTextOfferEvents + "; requests=" + evidence.FreeTextRequests +
                "; completions=" + evidence.FreeTextCompletions + "; busy=" + freeTextBusy +
                "; visible-rows=" + evidence.FreeTextVisibleRows + ".");
            if (freeTextBranch == FreeTextBranch.SpontaneousOffer)
            {
                object pendingValue = ReadHostField(host, "pendingConversationAction");
                NpcActionOffer pendingOffer = pendingValue as NpcActionOffer;
                NpcActionOption pendingOption = null;
                int pendingDuration = 0;
                bool pendingValid = pendingOffer != null && pendingOffer.TryGetSemanticCandidate(
                    evidence.PersonId, memorySession, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                    out pendingOption, out pendingDuration);
                string offeredReply = freeTextOffer == null || freeTextOffer.Option == null ? null :
                    "I could " + freeTextOffer.Option.Label + ". Would you like me to?";
                ConversationMoodView offerMoodAfter = store.CaptureConversationMood(evidence.PersonId);
                Require(freeTextBranchEvents == 1 && freeTextMoodCompletionEvents == 0 && freeTextOfferEvents == 1 &&
                    freeTextBranchSequence.Count == 1 && freeTextOffer != null && freeTextOffer.Option != null &&
                    pendingValid && pendingOption != null && pendingOption.Id == freeTextOffer.Option.Id &&
                    pendingOption.Kind == freeTextOffer.Option.Kind &&
                    pendingDuration == freeTextOffer.DurationSeconds && !freeTextBusy &&
                    offerMoodAfter.Value == freeTextMoodBefore.Value &&
                    offerMoodAfter.AcceptedTurnSequence == freeTextMoodBefore.AcceptedTurnSequence &&
                    evidence.FreeTextRequests == 0 && evidence.FreeTextCompletions == 0 &&
                    transcriptAfter != null && evidence.FreeTextVisibleRows == 2 && transcriptAfter.GetItem(freeTextRowsBefore) != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore + 1) != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore).textLabel != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore + 1).textLabel != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore).textLabel.Text == freeTextPlayerText &&
                    transcriptAfter.GetItem(freeTextRowsBefore + 1).textLabel.Text == offeredReply &&
                    CountExchange(store, evidence.PersonId, freeTextPlayerText, offeredReply, "action") == 1,
                    "Full free-text spontaneous offer was not typed, visible, journaled once, and idle.");
            }
            else if (freeTextBranch == FreeTextBranch.MoodSocialCompletion)
            {
                SocialDialogueResult freeTextResult = null;
                ConversationMoodView freeTextMoodAfter = store.CaptureConversationMood(evidence.PersonId);
                Require(freeTextBranchEvents == 1 && freeTextMoodCompletionEvents == 1 && freeTextOfferEvents == 0 &&
                    freeTextBranchSequence.Count == 1 && lastRequest != null &&
                    ReadBoolMember(lastRequest, "MoodEnabled") &&
                    ReadIntMember(lastRequest, "MoodValue") == freeTextMoodBefore.Value &&
                    SocialDialogueResult.TryParse(lastLiveReply, out freeTextResult) &&
                    evidence.FreeTextRequests == 1 && evidence.FreeTextCompletions == 1 &&
                    freeTextMoodAfter.AcceptedTurnSequence == freeTextMoodBefore.AcceptedTurnSequence + 1 &&
                    !freeTextBusy && transcriptAfter != null && evidence.FreeTextVisibleRows == 2 && transcriptAfter.GetItem(freeTextRowsBefore) != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore + 1) != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore).textLabel != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore + 1).textLabel != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore).textLabel.Text == freeTextPlayerText &&
                    transcriptAfter.GetItem(freeTextRowsBefore + 1).textLabel.Text == freeTextResult.Reply &&
                    ReferenceEquals(ReadHostField(host, "pendingConversationAction"), pendingOfferBefore) &&
                    CountModelExchange(store, evidence.PersonId, freeTextPlayerText, freeTextResult.Reply) == 1,
                    "Full free-text mood reply was not typed, visible, journaled once, accepted once, and idle.");
            }
            else if (IsMoodBackedSpontaneousOffer())
            {
                SocialDialogueResult freeTextResult = null;
                object pendingValue = ReadHostField(host, "pendingConversationAction");
                NpcActionOffer pendingOffer = pendingValue as NpcActionOffer;
                NpcActionOption pendingOption = null;
                int pendingDuration = 0;
                bool pendingValid = pendingOffer != null && pendingOffer.TryGetSemanticCandidate(
                    evidence.PersonId, memorySession, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                    out pendingOption, out pendingDuration);
                ConversationMoodView freeTextMoodAfter = store.CaptureConversationMood(evidence.PersonId);
                bool acceptedModelAnswer = lastRequest != null && ReadBoolMember(lastRequest, "MoodEnabled") &&
                    ReadIntMember(lastRequest, "MoodValue") == freeTextMoodBefore.Value &&
                    SocialDialogueResult.TryParse(lastLiveReply, out freeTextResult);
                Require(IsMoodBackedSpontaneousOffer() && freeTextBranchSequence.Count == 2 &&
                    acceptedModelAnswer && freeTextOffer != null && freeTextOffer.Option != null &&
                    pendingValid && pendingOption != null && pendingOption.Id == freeTextOffer.Option.Id &&
                    pendingOption.Kind == freeTextOffer.Option.Kind && pendingDuration == freeTextOffer.DurationSeconds &&
                    !ReferenceEquals(pendingValue, pendingOfferBefore) && evidence.FreeTextRequests == 1 &&
                    evidence.FreeTextCompletions == 1 &&
                    freeTextMoodAfter.AcceptedTurnSequence == freeTextMoodBefore.AcceptedTurnSequence + 1 &&
                    freeTextMoodAfter.Value >= -100 && freeTextMoodAfter.Value <= 100 && !freeTextBusy &&
                    transcriptAfter != null && evidence.FreeTextVisibleRows == 2 &&
                    transcriptAfter.GetItem(freeTextRowsBefore) != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore + 1) != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore).textLabel != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore + 1).textLabel != null &&
                    transcriptAfter.GetItem(freeTextRowsBefore).textLabel.Text == freeTextPlayerText &&
                    transcriptAfter.GetItem(freeTextRowsBefore + 1).textLabel.Text == freeTextResult.Reply &&
                    CountModelExchange(store, evidence.PersonId, freeTextPlayerText, freeTextResult.Reply) == 1,
                    "Full free-text mood-backed offer did not commit one typed offer and one accepted model exchange.");
            }
            else
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_FULL_ROUTING_DIAGNOSTIC: scenario=" + scenario + "; branch=" + evidence.FreeTextBranch +
                    "; branch-events=" + freeTextBranchEvents + "; mood-completion-events=" + freeTextMoodCompletionEvents +
                    "; offer-events=" + freeTextOfferEvents + "; requests=" + evidence.FreeTextRequests +
                    "; completions=" + evidence.FreeTextCompletions + "; busy=" + freeTextBusy +
                    "; visible-rows=" + evidence.FreeTextVisibleRows + ".");
                throw new InvalidOperationException("Full SubmitFreeText " + scenario + " did not reach one accepted typed offer or mood-enabled social completion.");
            }
            Require(!freeTextBusy, "Full SubmitFreeText " + scenario + " left conversationBusy set after accepted route completion.");
            if (scenario == "before-controls") evidence.FreeTextBeforeControlsBranch = evidence.FreeTextBranch;
            else if (scenario == "after-controls") evidence.FreeTextAfterControlsBranch = evidence.FreeTextBranch;
            string acceptedBranch = IsMoodBackedSpontaneousOffer() ? "mood-backed-spontaneous-offer" :
                freeTextBranch == FreeTextBranch.SpontaneousOffer ? "validated-spontaneous-offer" : "accepted-mood-social-completion";
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOOD_FULL_SUBMIT_FREE_TEXT_PASS: scenario=" + scenario + "; branch=" +
                acceptedBranch + "; branch-events=" + freeTextBranchEvents +
                "; mood-completion-events=" + freeTextMoodCompletionEvents + "; offer-events=" + freeTextOfferEvents +
                "; reply-requests=" + evidence.FreeTextRequests +
                "; reply-completions=" + evidence.FreeTextCompletions + "; visible-rows=" +
                evidence.FreeTextVisibleRows + "; journaled-once=true; busy=false.");

        }

        private static Action<string> StartControlledReply(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, string playerText, out string invalidPayload,
            Func<string, string> verifyAnswer = null, Action<string> onAnswer = null)
        {
            phase = ReplyPhase.Control;
            heldCompletion = null;
            lastRequest = null;
            object snapshot = CaptureSnapshot(host, talk, playerText);
            MethodInfo submit = RequireMethod(typeof(PeopleOfTheIliacBayMod), "SubmitSocialText", 9);
            MethodInfo readHistory = typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory", Members);
            if (readHistory == null || !readHistory.IsStatic)
                throw new InvalidOperationException("Required native mood history reader is missing.");
            string history = Convert.ToString(readHistory.Invoke(null, new object[] { talk }), CultureInfo.InvariantCulture) ?? string.Empty;
            RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy").SetValue(host, false);
            submit.Invoke(host, new object[] { talk, playerText, history, onAnswer, string.Empty, verifyAnswer, false, null, snapshot });
            Action<string> callback = heldCompletion;
            Require(callback != null && lastRequest != null && ReadBoolMember(lastRequest, "MoodEnabled"),
                "Mood-enabled production social callback was not captured for controlled response.");
            invalidPayload = "{\"reply\":\"Invalid control must not commit.\",\"moodDelta\":11}";
            phase = ReplyPhase.PassThrough;
            return callback;
        }

        internal static object CaptureSnapshot(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk, string playerText)
        {
            FieldInfo revision = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationContextRequestRevision");
            long requestRevision = Convert.ToInt64(revision.GetValue(host), CultureInfo.InvariantCulture) + 1;
            MethodInfo capture = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CaptureConversationContext", 3);
            return capture.Invoke(host, new object[] { talk, playerText, requestRevision });
        }

        internal static ResidentActor FindUnusedManagedAdult(WorldStateStore store, Vector3 playerPosition,
            bool requireNeutral = true, string excludedPersonId = null)
        { return FindUnusedManagedAdult(store, playerPosition, new ActorCandidateCounts(), requireNeutral, excludedPersonId); }

        internal static IEnumerator WaitForUnusedManagedAdult(PeopleOfTheIliacBayMod host,
            Vector3 playerPosition, Action<ResidentActor> completed, bool requireNeutral = true,
            string excludedPersonId = null)
        {
            const float timeoutSeconds = 45f;
            const float pollSeconds = 0.25f;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            ActorCandidateCounts candidateCounts = null;
            ResidentActor actor = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                WorldStateStore store = host ? ReadHostField(host, "store") as WorldStateStore : null;
                candidateCounts = new ActorCandidateCounts
                {
                    StoreAvailable = store != null,
                    RequireNeutral = requireNeutral,
                    ExcludedPersonId = excludedPersonId
                };
                actor = store == null ? null : FindUnusedManagedAdult(store, playerPosition,
                    candidateCounts, requireNeutral, excludedPersonId);
                if (actor)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOOD_ACTOR_SELECTED: npc=" + actor.Resident.Id +
                        "; " + candidateCounts + ".");
                    if (completed != null) completed(actor);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(pollSeconds);
            }

            string diagnostics = candidateCounts == null ? "candidate enumeration did not run." : candidateCounts.ToString();
            Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_MOOD_ACTOR_SELECTION_FAIL: " + diagnostics);
            throw new InvalidOperationException("No eligible active adult resident was available after " +
                timeoutSeconds.ToString("0", CultureInfo.InvariantCulture) + " seconds. " + diagnostics);
        }

        private static ResidentActor FindUnusedManagedAdult(WorldStateStore store, Vector3 playerPosition,
            ActorCandidateCounts counts, bool requireNeutral = true, string excludedPersonId = null)
        {
            counts.StoreAvailable = store != null;
            counts.RequireNeutral = requireNeutral;
            counts.ExcludedPersonId = excludedPersonId;
            if (store == null) return null;
            ResidentActor best = null;
            float bestDistance = float.MaxValue;
            foreach (ResidentActor candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                counts.Seen++;
                if (!candidate || !candidate.gameObject.activeInHierarchy) { counts.Inactive++; continue; }
                if (!candidate.Person) { counts.MissingPerson++; continue; }
                if (candidate.Resident == null) { counts.MissingResident++; continue; }
                float distance = Vector3.Distance(candidate.transform.position, playerPosition);
                if (distance < counts.NearestResidentDistance) counts.NearestResidentDistance = distance;
                if (!candidate.Resident.IsAlive) { counts.Dead++; continue; }
                if (candidate.Resident.IsQuestProtected) { counts.QuestProtected++; continue; }
                if (candidate.Resident.AgeGroup != ResidentAgeGroup.Adult) { counts.NonAdult++; continue; }
                if (!string.IsNullOrEmpty(excludedPersonId) && candidate.Resident.Id == excludedPersonId)
                { counts.Excluded++; continue; }
                if (candidate.IsSleeping) { counts.Sleeping++; continue; }
                if (candidate.IsSeated) { counts.Seated++; continue; }
                if (candidate.IsConversing) { counts.Conversing++; continue; }
                if (store.ActiveNpcAction(candidate.Resident.Id) != null) { counts.ActiveAction++; continue; }
                if (store.ActiveNpcTask(candidate.Resident.Id) != null) { counts.ActiveTask++; continue; }
                ConversationMoodView mood = store.CaptureConversationMood(candidate.Resident.Id);
                if (mood.Value != 0 || mood.AcceptedTurnSequence != 0)
                {
                    counts.NonNeutralMood++;
                    if (requireNeutral) continue;
                }
                counts.Eligible++;
                if (distance < bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        internal static void StagePlayerNearActor(StreetPopulation streets, ResidentActor actor, GameManager game)
        {
            Vector3 actorGround = actor.transform.position - Vector3.up;
            Vector3 playerPosition = game.PlayerMotor.transform.position;
            if (Math.Abs(Vector3.Distance(playerPosition, actor.transform.position) - 2.5f) <= 0.1f) return;

            Vector3 safeObserver;
            if (!ConversationReplyOutcomeProbe.TrySurveyedOutdoorObserver(streets, actorGround, out safeObserver))
                throw new InvalidOperationException("No surveyed safe outdoor observer point is available beside the selected mood resident.");
            Vector3 direction = safeObserver - actorGround;
            direction.y = 0;
            if (direction.sqrMagnitude < 0.01f)
                throw new InvalidOperationException("Surveyed mood resident observer point has no horizontal offset.");
            Vector3 target = actorGround + direction.normalized * 2.5f + Vector3.up;

            CharacterController controller = game.PlayerMotor.GetComponent<CharacterController>();
            bool motorEnabled = game.PlayerMotor.enabled;
            bool controllerEnabled = controller && controller.enabled;
            game.PlayerMotor.enabled = false;
            if (controller) controller.enabled = false;
            try
            {
                game.PlayerMotor.transform.position = target;
                Physics.SyncTransforms();
            }
            finally
            {
                if (controller) controller.enabled = controllerEnabled;
                game.PlayerMotor.enabled = motorEnabled;
            }

            float distance = Vector3.Distance(game.PlayerMotor.transform.position, actor.transform.position);
            if (distance < 1.5f || distance > 3.5f)
                throw new InvalidOperationException("Native mood Talk staging did not place player at a safe 2.5m contact distance. distance=" + distance);
            if (game.PlayerMouseLook) game.PlayerMouseLook.SetHorizontalFacing(actor.transform.position - game.PlayerMotor.transform.position);
        }

        private static ResidentActor FindActor(StreetPopulation streets, string personId)
        {
            ResidentActor actor = streets.FindActor(personId);
            return actor && actor.Resident != null && actor.Resident.IsAlive ? actor : null;
        }

        internal static IEnumerator OpenTalk(PeopleOfTheIliacBayMod host, GameManager game,
            ResidentActor actor, Action<DaggerfallTalkWindow> completed)
        {
            FieldInfo busy = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationBusy");
            FieldInfo greetingPending = RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending");
            FieldInfo retry = RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt");
            bool oldBusy = Convert.ToBoolean(busy.GetValue(host));
            bool oldGreeting = Convert.ToBoolean(greetingPending.GetValue(host));
            float oldRetry = Convert.ToSingle(retry.GetValue(host), CultureInfo.InvariantCulture);
            busy.SetValue(host, true);
            greetingPending.SetValue(host, false);
            retry.SetValue(host, Time.unscaledTime + 86400f);
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(actor.Person);
            float deadline = Time.realtimeSinceStartup + 25f;
            DaggerfallTalkWindow talk = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if (talk != null && talk.IsSetup && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                    game.TalkManager.MobileNPC == actor.Person)
                {
                    busy.SetValue(host, oldBusy);
                    retry.SetValue(host, oldRetry);
                    SetGreetingSuppressed(host);
                    IEnumerator binding = WaitForConversationBinding(host, talk, actor);
                    while (binding.MoveNext()) yield return binding.Current;
                    completed(talk);
                    yield break;
                }
                yield return null;
            }
            busy.SetValue(host, oldBusy);
            greetingPending.SetValue(host, oldGreeting);
            retry.SetValue(host, oldRetry);
            throw new InvalidOperationException("Native Talk did not bind selected mood fixture resident.");
        }

        private static IEnumerator WaitForConversationBinding(PeopleOfTheIliacBayMod host,
            DaggerfallTalkWindow talk, ResidentActor actor)
        {
            FieldInfo session = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen");
            if (!Convert.ToBoolean(session.GetValue(host)))
                RequireMethod(typeof(PeopleOfTheIliacBayMod), "BeginGroundedConversation", 0).Invoke(host, null);

            float deadline = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < deadline)
            {
                bool ready = Convert.ToBoolean(session.GetValue(host)) &&
                    ReferenceEquals(ReadHostField(host, "memoryWindow"), talk) &&
                    string.Equals(Convert.ToString(ReadHostField(host, "conversationPersonId"), CultureInfo.InvariantCulture),
                        actor.Resident.Id, StringComparison.Ordinal) &&
                    string.Equals(Convert.ToString(ReadHostField(host, "memoryPersonId"), CultureInfo.InvariantCulture),
                        actor.Resident.Id, StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(Convert.ToString(ReadHostField(host, "conversationContextSessionId"), CultureInfo.InvariantCulture)) &&
                    (ReadHostField(host, "conversationContextSessionSpeaker") as UnityEngine.Object) != null &&
                    actor.IsConversing && DaggerfallUI.UIManager.TopWindow == talk &&
                    GameManager.Instance.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                    GameManager.Instance.TalkManager.MobileNPC == actor.Person;
                if (ready) yield break;
                yield return null;
            }

            throw new InvalidOperationException("Native Talk became visible before conversation memory bound selected resident. " +
                "session-open=" + ReadHostField(host, "conversationSessionOpen") +
                "; memory-window-matches=" + ReferenceEquals(ReadHostField(host, "memoryWindow"), talk) +
                "; conversation-person=" + ReadHostField(host, "conversationPersonId") +
                "; memory-person=" + ReadHostField(host, "memoryPersonId") +
                "; context-session-id=" + ReadHostField(host, "conversationContextSessionId") +
                "; context-session-speaker-bound=" + ((ReadHostField(host, "conversationContextSessionSpeaker") as UnityEngine.Object) != null) +
                "; actor-conversing=" + actor.IsConversing + ".");
        }

        private static void SetGreetingSuppressed(PeopleOfTheIliacBayMod host)
        {
            RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingPending").SetValue(host, false);
            RequireField(typeof(PeopleOfTheIliacBayMod), "returningGreetingRetryAt")
                .SetValue(host, Time.unscaledTime + 86400f);
        }

        internal static IEnumerator CloseTalk(PeopleOfTheIliacBayMod host)
        {
            DaggerfallUI.Instance.PopToHUD();
            FieldInfo session = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen");
            FieldInfo window = RequireField(typeof(PeopleOfTheIliacBayMod), "memoryWindow");
            float deadline = Time.realtimeSinceStartup + 15f;
            while ((DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow ||
                Convert.ToBoolean(session.GetValue(host)) || window.GetValue(host) != null) &&
                Time.realtimeSinceStartup < deadline)
                yield return null;
            Require(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow) &&
                !Convert.ToBoolean(session.GetValue(host)) && window.GetValue(host) == null,
                "Native Talk close did not end the mood probe session.");
        }

        private static IEnumerator SaveCheckpoint(GameManager game, string character, Evidence evidence)
        {
            evidence.CheckpointTitle = "People - IMM09 Mood " + Guid.NewGuid().ToString("N").Substring(0, 8);
            return SaveCheckpoint(game, character, evidence.CheckpointTitle);
        }

        private static IEnumerator SaveCheckpoint(GameManager game, string character, string title)
        {
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(character, title) < 0,
                "Generated mood checkpoint title already exists; refusing overwrite.");
            bool saved = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnSave += onSave;
            try
            {
                game.SaveLoadManager.Save(character, title);
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                Require(saved, "Native mood disk checkpoint save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                Require(game.SaveLoadManager.FindSaveFolderByNames(character, title) >= 0,
                    "Native mood checkpoint missing after save.");
            }
            finally { SaveLoadManager.OnSave -= onSave; }
        }

        internal static IEnumerator LoadSave(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string title, bool requireTalkClosed)
        {
            GameManager game = GameManager.Instance;
            bool loaded = false;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.EnumerateSaves();
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, title);
                if (slot < 0) throw new InvalidOperationException("Exact mood save missing: " + title);
                game.SaveLoadManager.Load(slot);
                DaggerfallUI.Instance.PopToHUD();
                FieldInfo session = RequireField(typeof(PeopleOfTheIliacBayMod), "conversationSessionOpen");
                float deadline = Time.realtimeSinceStartup + 90f;
                while ((!loaded || !streets.Ready || (requireTalkClosed && Convert.ToBoolean(session.GetValue(host)))) &&
                    Time.realtimeSinceStartup < deadline)
                    yield return null;
                Require(loaded && streets.Ready, "Native mood disk load timed out: " + title);
                Require(!requireTalkClosed || !Convert.ToBoolean(session.GetValue(host)),
                    "Talk session remained open after mood disk load.");
            }
            finally { SaveLoadManager.OnLoad -= onLoad; }
            yield return null;
        }

        internal static void DeleteCheckpoint(GameManager game, string character, string title)
        {
            game.SaveLoadManager.EnumerateSaves();
            int slot = game.SaveLoadManager.FindSaveFolderByNames(character, title);
            if (slot >= 0) game.SaveLoadManager.DeleteSaveFolder(slot);
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(character, title) < 0,
                "Owned temporary mood checkpoint remained after cleanup.");
        }

        internal static void CancelTransport(PeopleOfTheIliacBayMod host)
        {
            LocalDialogueClient client = ReadHostField(host, "localDialogue") as LocalDialogueClient;
            if (client) client.Cancel();
        }

        private static void RequireMood(WorldStateStore store, string personId, int value, long sequence, string message)
        {
            ConversationMoodView current = store.CaptureConversationMood(personId);
            Require(current.Value == value && current.AcceptedTurnSequence == sequence,
                message + " expected=" + value + "/" + sequence + "; actual=" + current.Value + "/" + current.AcceptedTurnSequence);
        }

        private static int CountModelExchange(WorldStateStore store, string personId, string playerText, string npcText)
        { return CountExchange(store, personId, playerText, npcText, "model"); }

        internal static int CountExchange(WorldStateStore store, string personId, string playerText,
            string npcText, string source)
        {
            int count = 0;
            double cutoff = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 86400000d;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(personId, cutoff))
                if (entry != null && string.Equals(entry.Source, source, StringComparison.Ordinal) &&
                    string.Equals(entry.PlayerText, playerText, StringComparison.Ordinal) &&
                    string.Equals(entry.NpcText, npcText, StringComparison.Ordinal)) count++;
            return count;
        }

        private static string ReadConversationPerson(PeopleOfTheIliacBayMod host)
        { return Convert.ToString(ReadHostField(host, "conversationPersonId"), CultureInfo.InvariantCulture); }

        internal static object ReadHostField(PeopleOfTheIliacBayMod host, string name)
        { return RequireField(typeof(PeopleOfTheIliacBayMod), name).GetValue(host); }

        internal static long ReadLongProperty(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, Members);
            if (property == null) throw new InvalidOperationException("Required mood probe property is missing: " + name);
            return Convert.ToInt64(property.GetValue(target, null), CultureInfo.InvariantCulture);
        }

        internal static bool ReadBoolMember(object target, string name)
        { return Convert.ToBoolean(ReadMember(target, name), CultureInfo.InvariantCulture); }

        internal static int ReadIntMember(object target, string name)
        { return Convert.ToInt32(ReadMember(target, name), CultureInfo.InvariantCulture); }

        private static object ReadMember(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, Members);
            if (property != null) return property.GetValue(target, null);
            FieldInfo field = target.GetType().GetField(name, Members);
            if (field != null) return field.GetValue(target);
            throw new InvalidOperationException("Required mood probe member is missing: " + name);
        }

        internal static MethodInfo RequireMethod(Type type, string name, int parameterCount, bool instance = true)
        {
            foreach (MethodInfo method in type.GetMethods(Members))
                if (method.Name == name && method.GetParameters().Length == parameterCount &&
                    (!instance || !method.IsStatic)) return method;
            throw new InvalidOperationException("Required mood probe method is missing: " + type.Name + "." + name);
        }

        internal static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Required mood probe field is missing: " + name);
            return field;
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

                    object yielded = current.Current;
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

        private static IEnumerator YieldNestedThrow(Action finalized)
        { yield return ThrowFromNested(finalized); }

        private static IEnumerator ThrowFromNested(Action finalized)
        {
            try
            {
                yield return null;
                throw new InvalidOperationException("conversation-mood-probe-nested-driver-check");
            }
            finally
            {
                if (finalized != null) finalized();
            }
        }

        private static void VerifyEnumeratorTreeDriver()
        {
            bool nestedFinallyRan = false;
            bool nestedFailureObserved = false;
            IEnumerator driver = DriveEnumeratorTree(YieldNestedThrow(() => nestedFinallyRan = true));
            try
            {
                while (driver.MoveNext()) { }
            }
            catch (InvalidOperationException error)
            {
                nestedFailureObserved = error.Message == "conversation-mood-probe-nested-driver-check";
            }
            finally
            {
                Exception disposeFailure = null;
                Dispose(driver, ref disposeFailure);
            }
            Require(nestedFailureObserved && nestedFinallyRan,
                "Nested iterator driver failed to propagate child exception and run child finally.");
        }

        internal static void Dispose(IEnumerator iterator, ref Exception failure)
        {
            IDisposable disposable = iterator as IDisposable;
            if (disposable == null) return;
            try { disposable.Dispose(); }
            catch (Exception error) { if (failure == null) failure = error; }
        }

        internal static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
