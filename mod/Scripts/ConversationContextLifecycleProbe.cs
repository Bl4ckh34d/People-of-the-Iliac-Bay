using System;
using System.Collections;
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
    // Native lifecycle acceptance for the detached conversation snapshot.
    // The probe initiates no conversational completion request and suppresses
    // returning greetings. Update's separately configured prewarm is not controlled.
    internal static class ConversationContextLifecycleProbe
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMembers = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string BaselineSave = "People - Gothway Garden";
        private static readonly FieldInfo TalkTranscriptField = typeof(DaggerfallTalkWindow).GetField(
            "listboxConversation", InstanceMembers);

        private sealed class Evidence
        {
            internal string SaveTitle;
            internal string ArchiveId;
            internal string OldHead;
            internal string NewHead;
            internal string BeforeSaveState;
            internal string AfterSaveState;
            internal long BeforeLoadEpoch;
            internal long AfterLoadEpoch;
            internal bool SettingsCancel;
            internal bool AppraisalWithoutD3;
            internal bool AcuteFearFreshness;
            internal string AcuteFearObserverId;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            if (string.IsNullOrEmpty(character) || character != "PeopleOfTheIliacBayTest-9a5f197b")
                throw new InvalidOperationException("Context lifecycle probe requires owned disposable character PeopleOfTheIliacBayTest-9a5f197b.");

            Exception testFailure = null;
            Evidence evidence = new Evidence();
            IEnumerator test = DriveFlattened(RunCases(host, streets, character, evidence));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try
                    {
                        more = test.MoveNext();
                        if (more) current = test.Current;
                    }
                    catch (Exception error)
                    {
                        testFailure = error;
                        break;
                    }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                try
                {
                    var disposable = test as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
                catch (Exception error) { if (testFailure == null) testFailure = error; }
            }

            Exception restoreFailure = null;
            IEnumerator restore = DriveFlattened(LoadSave(host, streets, character, BaselineSave, false));
            try
            {
                while (true)
                {
                    bool more;
                    object current = null;
                    try
                    {
                        more = restore.MoveNext();
                        if (more) current = restore.Current;
                    }
                    catch (Exception error)
                    {
                        restoreFailure = error;
                        break;
                    }
                    if (!more) break;
                    yield return current;
                }
            }
            finally
            {
                try
                {
                    var disposable = restore as IDisposable;
                    if (disposable != null) disposable.Dispose();
                }
                catch (Exception error) { if (restoreFailure == null) restoreFailure = error; }
            }

            if (restoreFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_BASELINE_RESTORE_FAIL: " + restoreFailure);
                throw new InvalidOperationException("Context lifecycle probe could not restore the exact baseline fixture.", restoreFailure);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_BASELINE_RESTORED: character=" + character + "; save=" + BaselineSave + ".");

            if (evidence.AcuteFearFreshness)
            {
                var restoredStore = RequireHostField("store").GetValue(host) as WorldStateStore;
                if (restoredStore == null || restoredStore.ReadNpcAcuteFear(evidence.AcuteFearObserverId,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()) != null)
                    throw new InvalidOperationException("Baseline reload retained the controlled acute-fear observation.");
            }

            if (testFailure != null)
                throw new InvalidOperationException("Context lifecycle probe failed after baseline restore.", testFailure);

            // Markers appear only after every assertion and baseline restoration pass.
            // The TownTest runner can therefore require all markers without accepting
            // a partial run whose later load or cleanup failed.
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_REOPEN_PASS: same-person=true; new-session=true; stale-callback-effects=0.");
            if (evidence.AcuteFearFreshness)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_ACUTE_FEAR_FRESHNESS_PASS: stale-context=false; stale-stage=false; recaptured-current=true; trust-ledger-unchanged=true; ai-completion-requests=0.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SAME_BRANCH_SAVE_PASS: archive=" + evidence.ArchiveId +
                "; old-head=" + evidence.OldHead + "; new-head=" + evidence.NewHead + "; current-stage=true.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_DISK_RESTORE_INVALIDATION_PASS: title=" + evidence.SaveTitle +
                "; epoch-before=" + evidence.BeforeLoadEpoch + "; epoch-after=" + evidence.AfterLoadEpoch +
                "; stale-effects=0; probe-completion-request=false; returning-greeting=suppressed; background-prewarm=not-asserted.");
            if (evidence.SettingsCancel)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SETTINGS_CANCEL_PASS: exact-stage=true; busy-cleared=true; unanswered-player-visible=true; archive-effects=0; stale-callback=false.");
            if (evidence.AppraisalWithoutD3)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_EXPERIENCE_APPRAISAL_D3_INDEPENDENCE_PASS: qwen-client-present=true; d3-route-absent=true.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, Evidence evidence)
        {
            if (!host || !streets || !GameManager.Instance || !GameManager.Instance.IsReady ||
                GameManager.Instance.PlayerMotor == null || !streets.Ready)
                throw new InvalidOperationException("Context lifecycle probe requires the ready owned native fixture.");

            GameManager game = GameManager.Instance;
            SaveLoadManager saves = game.SaveLoadManager;
            saves.EnumerateSaves();
            if (saves.FindSaveFolderByNames(character, BaselineSave) < 0)
                throw new InvalidOperationException("Exact baseline fixture save is missing; refusing lifecycle probe.");

            MethodInfo capture = RequireInstanceMethod("CaptureConversationContext");
            MethodInfo current = RequireInstanceMethod("IsCurrentConversationContext");
            MethodInfo beginStage = RequireInstanceMethod("BeginConversationContextStage");
            MethodInfo consumeStage = RequireInstanceMethod("TryConsumeConversationContextStage");
            MethodInfo rejectStage = RequireInstanceMethod("HandleRejectedConversationStage");
            MethodInfo appendPair = RequireInstanceMethod("AppendRememberedDialoguePair");
            FieldInfo storeField = typeof(PeopleOfTheIliacBayMod).GetField("store", InstanceMembers);
            FieldInfo sessionField = typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", InstanceMembers);
            MethodInfo beginConversation = RequireInstanceMethod("BeginGroundedConversation");
            MethodInfo historyMethod = typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory", StaticMembers);
            if (storeField == null || sessionField == null || historyMethod == null)
                throw new InvalidOperationException("Context lifecycle probe could not bind current native runtime APIs.");

            var store = storeField.GetValue(host) as WorldStateStore;
            if (store == null) throw new InvalidOperationException("Context lifecycle probe cannot read the loaded fixture store.");
            ResidentActor actor = null;
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            while (!actor && Time.realtimeSinceStartup < actorDeadline)
            {
                actor = FindUnusedManagedAdult(store);
                if (!actor) yield return new WaitForSecondsRealtime(0.25f);
            }
            if (!actor) throw new InvalidOperationException("No unused managed adult resident is available in the loaded fixture.");

            DaggerfallTalkWindow talk = null;
            yield return OpenTalk(host, game, actor, value => talk = value);
            if (talk == null) throw new InvalidOperationException("Initial native Talk window did not open.");
            if (!(bool)sessionField.GetValue(host)) beginConversation.Invoke(host, null);
            string personId = actor.Resident.Id;
            string playerText = "IMM02-LIFECYCLE-CONTROL-" + Guid.NewGuid().ToString("N");
            long request = 0;
            object initialContext = capture.Invoke(host, new object[] { talk, playerText, ++request });
            if (!(bool)current.Invoke(host, new object[] { initialContext, talk }))
                throw new InvalidOperationException("Initial same-person lifecycle binding was not current.");
            string oldSession = Convert.ToString(Read(Read(initialContext, "Binding"), "SessionId"));

            // A fresh, controlled native receipt for this bound NPC must invalidate
            // both an already-issued stage and the older context. This exercises
            // the live store/capture path without advancing time or invoking AI.
            double observationTime = Convert.ToDouble(Read(initialContext, "CapturedGameTime"));
            if (Read(initialContext, "AcuteFear") != null)
                throw new InvalidOperationException("Acute-fear freshness fixture is not unused for the selected resident.");
            long relationshipLedgerBeforeFear = store.NpcRelationshipLedgerRevision;
            NpcDirectedRelationshipView relationshipBeforeFear = store.ReadNpcRelationship(
                personId, NpcDirectedRelationship.PlayerTargetId, observationTime);
            long initialStage = Convert.ToInt64(beginStage.Invoke(host, new object[] { initialContext, talk }));
            if (initialStage <= 0) throw new InvalidOperationException("Could not issue pre-observation callback token.");
            string sourceId = "probe:context-lifecycle:" + Guid.NewGuid().ToString("N");
            if (!store.TryObserveNpcAcuteFear(personId, sourceId, NpcFearSeverity.Grave, observationTime))
                throw new InvalidOperationException("Could not record the controlled bound-NPC acute-fear receipt.");
            bool initialContextStillCurrent = (bool)current.Invoke(host, new object[] { initialContext, talk });
            bool initialStageConsumed = (bool)consumeStage.Invoke(host, new object[] { initialContext, talk, initialStage });
            if (initialContextStillCurrent || initialStageConsumed)
                throw new InvalidOperationException("A context/stage remained current after a fresh acute-fear receipt.");
            object beforeClose = capture.Invoke(host, new object[] { talk, playerText, ++request });
            NpcAcuteFearView capturedFear = Read(beforeClose, "AcuteFear") as NpcAcuteFearView;
            NpcDirectedRelationshipView relationshipAfterFear = store.ReadNpcRelationship(
                personId, NpcDirectedRelationship.PlayerTargetId, observationTime);
            if (!(bool)current.Invoke(host, new object[] { beforeClose, talk }) || capturedFear == null ||
                capturedFear.ObserverId != personId || capturedFear.SourceId != sourceId ||
                capturedFear.ObservedGameTime != observationTime ||
                capturedFear.ProjectedGameTime != Convert.ToDouble(Read(beforeClose, "CapturedGameTime")) ||
                capturedFear.Revision != 1 || !Convert.ToBoolean(Read(beforeClose, "VerifiedPlayerContact")) ||
                relationshipLedgerBeforeFear != store.NpcRelationshipLedgerRevision ||
                !SameRelationship(relationshipBeforeFear, relationshipAfterFear))
                throw new InvalidOperationException("Fresh acute-fear recapture was not bound/current or changed the directed trust ledger.");
            evidence.AcuteFearFreshness = true;
            evidence.AcuteFearObserverId = personId;
            long oldToken = Convert.ToInt64(beginStage.Invoke(host, new object[] { beforeClose, talk }));
            if (oldToken <= 0) throw new InvalidOperationException("Could not issue refreshed pre-close callback token.");
            SetProbeBusy(host, true);

            DaggerfallUI.Instance.PopToHUD();
            yield return null;
            yield return null;
            evidence.AppraisalWithoutD3 = VerifyAppraisalReadinessWithoutD3(host, store);
            if ((bool)current.Invoke(host, new object[] { beforeClose, talk }))
                throw new InvalidOperationException("Closed Talk session retained a current snapshot.");

            talk = null;
            yield return OpenTalk(host, game, actor, value => talk = value);
            if (talk == null) throw new InvalidOperationException("Same-person native Talk window did not reopen.");
            if (!(bool)sessionField.GetValue(host)) beginConversation.Invoke(host, null);
            object afterReopen = capture.Invoke(host, new object[] { talk, playerText, ++request });
            object newBinding = Read(afterReopen, "Binding");
            if (!(bool)current.Invoke(host, new object[] { afterReopen, talk }) ||
                !string.Equals(Convert.ToString(Read(newBinding, "PersonId")), personId, StringComparison.Ordinal) ||
                string.Equals(Convert.ToString(Read(newBinding, "SessionId")), oldSession, StringComparison.Ordinal))
                throw new InvalidOperationException("Same-person reopen did not create a fresh, current session binding.");
            string historyBeforeOldCallback = ReadHistory(historyMethod, talk);
            if ((bool)consumeStage.Invoke(host, new object[] { beforeClose, talk, oldToken }) ||
                (bool)rejectStage.Invoke(host, new object[] { beforeClose, talk, oldToken, playerText, null }) ||
                !string.Equals(historyBeforeOldCallback, ReadHistory(historyMethod, talk), StringComparison.Ordinal))
                throw new InvalidOperationException("Old same-person callback consumed or changed the reopened transcript.");
            SetProbeBusy(host, false);

            // Settings can disable Qwen while a foreground request is in flight.
            // That abort has no LocalDialogue completion callback, so production
            // must reject this exact stage and retain only the unanswered turn.
            string cancelledPlayerText = "IMM02-AI-OFF-CANCEL-" + Guid.NewGuid().ToString("N");
            object cancelledSnapshot = capture.Invoke(host, new object[] { talk, cancelledPlayerText, ++request });
            long cancelledStage = Convert.ToInt64(beginStage.Invoke(host, new object[] { cancelledSnapshot, talk }));
            if (cancelledStage <= 0) throw new InvalidOperationException("Could not bind settings-cancel lifecycle stage.");
            SetProbeBusy(host, true);
            string historyBeforeSettingsCancel = ReadHistory(historyMethod, talk);
            ConversationArchiveCheckpointView archiveBeforeSettingsCancel = store.CaptureConversationArchiveCheckpoint();
            ConversationMoodView moodBeforeSettingsCancel = store.CaptureConversationMood(personId);
            NpcDirectedRelationshipView relationshipBeforeSettingsCancel =
                store.ReadNpcRelationship(personId, NpcDirectedRelationship.PlayerTargetId);
            long relationshipLedgerRevisionBeforeSettingsCancel = store.NpcRelationshipLedgerRevision;
            RequireInstanceMethod("CancelForegroundDialogueForSettings").Invoke(host, null);
            if (TalkTranscriptField == null)
                throw new InvalidOperationException("Could not locate the native talk transcript field for settings-cancel verification.");
            var transcriptAfterSettingsCancel = TalkTranscriptField.GetValue(talk) as ListBox;
            var finalSettingsCancelRow = transcriptAfterSettingsCancel == null || transcriptAfterSettingsCancel.Count == 0
                ? null : transcriptAfterSettingsCancel.GetItem(transcriptAfterSettingsCancel.Count - 1);
            MethodInfo isPendingRow = typeof(PeopleOfTheIliacBayMod).GetMethod("IsPendingTranscriptItem", StaticMembers);
            bool pendingRowsRemain = false;
            if (transcriptAfterSettingsCancel != null && isPendingRow != null)
                for (int index = 0; index < transcriptAfterSettingsCancel.Count; index++)
                    if (Convert.ToBoolean(isPendingRow.Invoke(null, new object[] { transcriptAfterSettingsCancel.GetItem(index) })))
                        pendingRowsRemain = true;
            bool busyAfterSettingsCancel = (bool)RequireHostField("conversationBusy").GetValue(host);
            string finalRowText = finalSettingsCancelRow == null || finalSettingsCancelRow.textLabel == null
                ? null : finalSettingsCancelRow.textLabel.Text;
            bool finalRowIsPlayer = finalSettingsCancelRow != null && finalSettingsCancelRow.textLabel != null &&
                finalSettingsCancelRow.textColor == DaggerfallUI.DaggerfallQuestionTextColor &&
                string.Equals(finalRowText, cancelledPlayerText, StringComparison.Ordinal);
            string visibleHistoryAfterSettingsCancel = ReadHistory(historyMethod, talk);
            string safePlayerHistoryText = cancelledPlayerText.Replace("\r", " ").Replace("\n", " ");
            string expectedVisibleHistoryAfterSettingsCancel = historyBeforeSettingsCancel +
                "Player: " + safePlayerHistoryText + Environment.NewLine;
            bool visibleHistoryMatchesPreservedPlayerTurn = string.Equals(expectedVisibleHistoryAfterSettingsCancel,
                visibleHistoryAfterSettingsCancel, StringComparison.Ordinal);
            ConversationArchiveCheckpointView archiveAfterSettingsCancel = store.CaptureConversationArchiveCheckpoint();
            bool archiveCheckpointUnchanged = string.Equals(archiveBeforeSettingsCancel.ArchiveId,
                    archiveAfterSettingsCancel.ArchiveId, StringComparison.Ordinal) &&
                string.Equals(archiveBeforeSettingsCancel.Head, archiveAfterSettingsCancel.Head, StringComparison.Ordinal);
            ConversationMoodView moodAfterSettingsCancel = store.CaptureConversationMood(personId);
            bool moodStateUnchanged = moodBeforeSettingsCancel.Value == moodAfterSettingsCancel.Value &&
                moodBeforeSettingsCancel.AcceptedTurnSequence == moodAfterSettingsCancel.AcceptedTurnSequence &&
                moodBeforeSettingsCancel.Revision == moodAfterSettingsCancel.Revision &&
                moodBeforeSettingsCancel.UpdatedAt.Equals(moodAfterSettingsCancel.UpdatedAt);
            NpcDirectedRelationshipView relationshipAfterSettingsCancel =
                store.ReadNpcRelationship(personId, NpcDirectedRelationship.PlayerTargetId);
            bool relationshipUnchanged = relationshipBeforeSettingsCancel == null
                ? relationshipAfterSettingsCancel == null
                : relationshipAfterSettingsCancel != null &&
                    relationshipBeforeSettingsCancel.Revision == relationshipAfterSettingsCancel.Revision &&
                    relationshipBeforeSettingsCancel.TrustKnown == relationshipAfterSettingsCancel.TrustKnown &&
                    relationshipBeforeSettingsCancel.ReliabilityTrust == relationshipAfterSettingsCancel.ReliabilityTrust &&
                    relationshipBeforeSettingsCancel.TrustConfidence == relationshipAfterSettingsCancel.TrustConfidence &&
                    relationshipBeforeSettingsCancel.TrustEvidenceCount == relationshipAfterSettingsCancel.TrustEvidenceCount &&
                    relationshipBeforeSettingsCancel.Resentment == relationshipAfterSettingsCancel.Resentment &&
                    relationshipBeforeSettingsCancel.InterpersonalFear == relationshipAfterSettingsCancel.InterpersonalFear &&
                    relationshipBeforeSettingsCancel.LastGameTime.Equals(relationshipAfterSettingsCancel.LastGameTime);
            bool relationshipLedgerUnchanged = relationshipLedgerRevisionBeforeSettingsCancel == store.NpcRelationshipLedgerRevision;
            object binding = Read(cancelledSnapshot, "Binding");
            long issuedStageBeforeStaleCheck = Convert.ToInt64(RequireHostField("conversationContextIssuedStageToken").GetValue(host));
            bool snapshotStillCurrentBeforeStaleCheck = (bool)current.Invoke(host, new object[] { cancelledSnapshot, talk });
            bool ownerMatchesBeforeStaleCheck = ReferenceEquals(RequireHostField("memoryWindow").GetValue(host), talk);
            bool prerequisitesHold = !busyAfterSettingsCancel && finalRowIsPlayer && isPendingRow != null &&
                !pendingRowsRemain && visibleHistoryMatchesPreservedPlayerTurn && archiveCheckpointUnchanged &&
                moodStateUnchanged && relationshipUnchanged && relationshipLedgerUnchanged;
            bool staleStageConsumed = prerequisitesHold &&
                (bool)consumeStage.Invoke(host, new object[] { cancelledSnapshot, talk, cancelledStage });
            bool staleStageRejected = prerequisitesHold && !staleStageConsumed &&
                (bool)rejectStage.Invoke(host, new object[] { cancelledSnapshot, talk, cancelledStage, cancelledPlayerText, null });
            if (!prerequisitesHold || staleStageConsumed || staleStageRejected)
            {
                string failure = "Disabling AI settings-cancel diagnostics: busy=" + busyAfterSettingsCancel +
                    "; issuedStage=" + issuedStageBeforeStaleCheck + "; testStage=" + cancelledStage +
                    "; snapshotCurrent=" + snapshotStillCurrentBeforeStaleCheck +
                    "; memoryWindowMatches=" + ownerMatchesBeforeStaleCheck +
                    "; playerRow=" + finalRowIsPlayer + "; finalText=" + (finalRowText ?? "<null>") +
                    "; finalColor=" + (finalSettingsCancelRow == null ? "<null>" : finalSettingsCancelRow.textColor.ToString()) +
                    "; pendingPredicatePresent=" + (isPendingRow != null) + "; pendingRowsRemain=" + pendingRowsRemain +
                    "; visibleHistoryMatchesPlayerRow=" + visibleHistoryMatchesPreservedPlayerTurn +
                    "; archiveCheckpointUnchanged=" + archiveCheckpointUnchanged +
                    "; moodUnchanged=" + moodStateUnchanged + "; relationshipUnchanged=" + relationshipUnchanged +
                    "; relationshipLedgerUnchanged=" + relationshipLedgerUnchanged +
                    "; staleConsume=" + (prerequisitesHold ? staleStageConsumed.ToString() : "not-run") +
                    "; staleReject=" + (prerequisitesHold && !staleStageConsumed ? staleStageRejected.ToString() : "not-run") +
                    "; person=" + Convert.ToString(Read(binding, "PersonId")) +
                    "; session=" + Convert.ToString(Read(binding, "SessionId")) +
                    "; capturedPlayerText=" + Convert.ToString(RequireHostField("conversationContextCapturedPlayerText").GetValue(host));
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_CONTEXT_SETTINGS_CANCEL_DIAGNOSTIC: " + failure);
                throw new InvalidOperationException(failure);
            }
            evidence.SettingsCancel = true;

            int rowsBeforeNoStageCancel = transcriptAfterSettingsCancel.Count;
            string historyBeforeNoStageCancel = ReadHistory(historyMethod, talk);
            RequireHostField("conversationContextIssuedStageToken").SetValue(host, 0L);
            SetProbeBusy(host, true);
            RequireInstanceMethod("CancelForegroundDialogueForSettings").Invoke(host, null);
            if ((bool)RequireHostField("conversationBusy").GetValue(host) ||
                transcriptAfterSettingsCancel.Count != rowsBeforeNoStageCancel ||
                !string.Equals(historyBeforeNoStageCancel, ReadHistory(historyMethod, talk), StringComparison.Ordinal))
                throw new InvalidOperationException("Settings cancellation left a busy latch without an issued stage or changed the transcript.");

            // Verify save title uniqueness before any save write.
            saves.EnumerateSaves();
            evidence.SaveTitle = "People of the Iliac Bay IMM02 lifecycle " + Guid.NewGuid().ToString("N");
            if (saves.FindSaveFolderByNames(character, evidence.SaveTitle) >= 0)
                throw new InvalidOperationException("Generated lifecycle save title already exists; refusing overwrite.");

            object beforeSave = capture.Invoke(host, new object[] { talk, playerText, ++request });
            SetReturningGreetingPending(host, false);
            long saveStage = Convert.ToInt64(beginStage.Invoke(host, new object[] { beforeSave, talk }));
            if (saveStage <= 0 || !(bool)current.Invoke(host, new object[] { beforeSave, talk }))
                throw new InvalidOperationException("Could not bind current snapshot/stage before native save.");
            SetProbeBusy(host, true);
            SetReturningGreetingPending(host, false);
            object beforeBinding = Read(beforeSave, "Binding");
            evidence.ArchiveId = Convert.ToString(Read(beforeBinding, "ArchiveId"));
            evidence.OldHead = Convert.ToString(Read(beforeBinding, "ArchiveHead"));
            evidence.BeforeSaveState = DescribeRuntimeState(host, game, talk, beforeSave,
                evidence.ArchiveId, evidence.OldHead, saveStage);

            string taggedPlayer = "IMM02-SAVE-CONTROL-" + Guid.NewGuid().ToString("N");
            string taggedNpc = "Lifecycle probe stored this controlled test exchange.";
            appendPair.Invoke(host, new object[] { talk, taggedPlayer, taggedNpc, "native" });
            if (CountExactExchange(store, personId, taggedPlayer, taggedNpc) != 1)
                throw new InvalidOperationException("Controlled test exchange was not recorded exactly once before native save.");
            yield return SaveAndWait(game, character, evidence.SaveTitle);

            var savedStore = storeField.GetValue(host) as WorldStateStore;
            if (savedStore == null) throw new InvalidOperationException("Native save removed the active store unexpectedly.");
            ConversationArchiveCheckpointView afterSave = savedStore.CaptureConversationArchiveCheckpoint();
            evidence.NewHead = afterSave.Head;
            evidence.AfterSaveState = DescribeRuntimeState(host, game, talk, beforeSave,
                afterSave.ArchiveId, afterSave.Head, saveStage);
            if (!string.Equals(afterSave.ArchiveId, evidence.ArchiveId, StringComparison.Ordinal) ||
                string.IsNullOrEmpty(evidence.NewHead) || string.Equals(evidence.NewHead, evidence.OldHead, StringComparison.Ordinal))
                throw new InvalidOperationException("Native save did not advance the existing same-branch archive head. before=" +
                    evidence.BeforeSaveState + "; after=" + evidence.AfterSaveState);
            bool currentAfterSave = (bool)current.Invoke(host, new object[] { beforeSave, talk });
            bool consumedAfterSave = currentAfterSave && (bool)consumeStage.Invoke(host, new object[] { beforeSave, talk, saveStage });
            if (!currentAfterSave || !consumedAfterSave)
                throw new InvalidOperationException("Same-branch archive-head advancement invalidated a current snapshot stage. before=" +
                    evidence.BeforeSaveState + "; after=" + evidence.AfterSaveState +
                    "; currentAfterSave=" + currentAfterSave + "; consumedAfterSave=" + consumedAfterSave);
            SetProbeBusy(host, false);

            object beforeLoad = capture.Invoke(host, new object[] { talk, playerText, ++request });
            SetReturningGreetingPending(host, false);
            long loadStage = Convert.ToInt64(beginStage.Invoke(host, new object[] { beforeLoad, talk }));
            if (loadStage <= 0 || !(bool)current.Invoke(host, new object[] { beforeLoad, talk }))
                throw new InvalidOperationException("Could not bind pre-load snapshot/stage.");
            SetProbeBusy(host, true);
            evidence.BeforeLoadEpoch = Convert.ToInt64(Read(Read(beforeLoad, "Binding"), "SaveEpoch"));
            yield return LoadSave(host, streets, character, evidence.SaveTitle, true);

            var restoredStore = storeField.GetValue(host) as WorldStateStore;
            if (restoredStore == null) throw new InvalidOperationException("Native load did not restore the People of the Iliac Bay store.");
            if (CountExactExchange(restoredStore, personId, taggedPlayer, taggedNpc) != 1)
                throw new InvalidOperationException("Native disk restore did not retain the one controlled save-branch exchange.");
            evidence.AfterLoadEpoch = ReadSaveEpoch(host);
            if (evidence.AfterLoadEpoch <= evidence.BeforeLoadEpoch ||
                (bool)sessionField.GetValue(host) ||
                (bool)current.Invoke(host, new object[] { beforeLoad, talk }) ||
                (bool)consumeStage.Invoke(host, new object[] { beforeLoad, talk, loadStage }))
                throw new InvalidOperationException("Actual native save/load did not invalidate the pending old snapshot and stage.");

            int rowsBeforeOldCallback = CountTranscript(restoredStore, personId);
            bool staleHandled = (bool)rejectStage.Invoke(host,
                new object[] { beforeLoad, talk, loadStage, playerText, null });
            int rowsAfterOldCallback = CountTranscript(restoredStore, personId);
            if (staleHandled || rowsAfterOldCallback != rowsBeforeOldCallback)
                throw new InvalidOperationException("Old post-load callback changed restored transcript or memory.");
        }

        private static ResidentActor FindUnusedManagedAdult(WorldStateStore store)
        {
            foreach (ResidentActor candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!candidate || !candidate.Person || candidate.Resident == null || !candidate.Resident.IsAlive ||
                    candidate.Resident.AgeGroup != ResidentAgeGroup.Adult || candidate.IsSleeping || candidate.IsConversing)
                    continue;
                if (store.ConversationTranscript(candidate.Resident.Id,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Count != 0) continue;
                return candidate;
            }
            return null;
        }

        private static IEnumerator OpenTalk(PeopleOfTheIliacBayMod host, GameManager game, ResidentActor actor,
            Action<DaggerfallTalkWindow> completed)
        {
            FieldInfo pendingField = RequireHostField("returningGreetingPending");
            FieldInfo retryField = RequireHostField("returningGreetingRetryAt");
            FieldInfo busyField = RequireHostField("conversationBusy");
            bool priorPending = (bool)pendingField.GetValue(host);
            bool priorBusy = (bool)busyField.GetValue(host);
            float priorRetry = Convert.ToSingle(retryField.GetValue(host));
            bool opened = false;
            try
            {
                // BeginDialogueMemory may schedule a returning greeting when it
                // restores transcript. Gate it before Talk opens. BeginDialogueMemory
                // resets pending, but leaves this retry gate intact.
                pendingField.SetValue(host, false);
                retryField.SetValue(host, Time.unscaledTime + 86400f);
                busyField.SetValue(host, true);
                DaggerfallUI.Instance.PopToHUD();
                game.TalkManager.TalkToMobileNPC(actor.Person);
                float deadline = Time.realtimeSinceStartup + 25f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    DaggerfallTalkWindow talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                    if (talk != null && talk.IsSetup && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                        game.TalkManager.MobileNPC == actor.Person)
                    {
                        pendingField.SetValue(host, priorPending);
                        retryField.SetValue(host, priorRetry);
                        busyField.SetValue(host, priorBusy);
                        opened = true;
                        completed(talk);
                        yield break;
                    }
                    yield return null;
                }
                throw new InvalidOperationException("Native Talk did not bind the selected managed resident.");
            }
            finally
            {
                if (!opened)
                {
                    pendingField.SetValue(host, priorPending);
                    retryField.SetValue(host, priorRetry);
                    busyField.SetValue(host, priorBusy);
                }
            }
        }

        private static IEnumerator SaveAndWait(GameManager game, string character, string title)
        {
            bool saved = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnSave += onSave;
            try
            {
                game.SaveLoadManager.Save(character, title);
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Native lifecycle checkpoint save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                if (game.SaveLoadManager.FindSaveFolderByNames(character, title) < 0)
                    throw new InvalidOperationException("Unique lifecycle save title was not found after native save.");
            }
            finally { SaveLoadManager.OnSave -= onSave; }
        }

        private static IEnumerator LoadSave(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string title, bool requireTalkClosed)
        {
            if (!host || !streets || !GameManager.Instance || !GameManager.Instance.IsReady)
                throw new InvalidOperationException("Native lifecycle load requires the owned ready fixture.");
            GameManager game = GameManager.Instance;
            bool loaded = false;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.EnumerateSaves();
                int slot = game.SaveLoadManager.FindSaveFolderByNames(character, title);
                if (slot < 0) throw new InvalidOperationException("Exact lifecycle save is missing: " + title);
                game.SaveLoadManager.Load(slot);
                DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 75f;
                while ((!loaded || !streets.Ready ||
                    (requireTalkClosed && (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", InstanceMembers).GetValue(host))) &&
                    Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (!loaded || !streets.Ready)
                    throw new InvalidOperationException("Native lifecycle disk load or population restoration timed out: " + title);
                if (requireTalkClosed && (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", InstanceMembers).GetValue(host))
                    throw new InvalidOperationException("Talk session remained open after native disk load.");
            }
            finally { SaveLoadManager.OnLoad -= onLoad; }
            yield return null;
        }

        private static MethodInfo RequireInstanceMethod(string name)
        {
            MethodInfo method = typeof(PeopleOfTheIliacBayMod).GetMethod(name, InstanceMembers);
            if (method == null) throw new InvalidOperationException("Required native lifecycle API is missing: " + name);
            return method;
        }

        private static object Read(object value, string name)
        {
            if (value == null) throw new InvalidOperationException("Cannot read " + name + " from null lifecycle value.");
            PropertyInfo property = value.GetType().GetProperty(name, InstanceMembers);
            if (property != null) return property.GetValue(value, null);
            FieldInfo field = value.GetType().GetField(name, InstanceMembers);
            if (field != null) return field.GetValue(value);
            throw new InvalidOperationException("Lifecycle value has no member " + name + ".");
        }

        private static bool SameRelationship(NpcDirectedRelationshipView left, NpcDirectedRelationshipView right)
        {
            if (left == null || right == null) return left == right;
            return left.Revision == right.Revision && left.TrustKnown == right.TrustKnown &&
                left.ReliabilityTrust == right.ReliabilityTrust && left.TrustConfidence == right.TrustConfidence &&
                left.TrustEvidenceCount == right.TrustEvidenceCount && left.Resentment == right.Resentment &&
                left.InterpersonalFear == right.InterpersonalFear && left.LastGameTime.Equals(right.LastGameTime);
        }

        private static string ReadHistory(MethodInfo method, DaggerfallTalkWindow talk)
        { return Convert.ToString(method.Invoke(null, new object[] { talk })) ?? string.Empty; }

        private static bool VerifyAppraisalReadinessWithoutD3(PeopleOfTheIliacBayMod host,
            WorldStateStore store)
        {
            FieldInfo freeText = RequireHostField("localDialogueEnabled");
            FieldInfo clientField = RequireHostField("localDialogue");
            FieldInfo optionsField = RequireHostField("options");
            FieldInfo routingField = RequireHostField("decisionRoutingSession");
            MethodInfo ready = RequireInstanceMethod("ExperienceAppraisalRuntimeReady");
            LocalDialogueClient client = clientField.GetValue(host) as LocalDialogueClient;
            RuntimeOptions options = optionsField.GetValue(host) is RuntimeOptions
                ? (RuntimeOptions)optionsField.GetValue(host) : null;
            if (!Convert.ToBoolean(freeText.GetValue(host)) || client == null || options == null || !options.Enabled)
                return false;

            object oldRouting = routingField.GetValue(host);
            var probeRouting = new LocalDecisionSession { Enabled = true };
            try
            {
                routingField.SetValue(host, probeRouting);
                bool withD3GateSatisfied = Convert.ToBoolean(ready.Invoke(host, new object[] { store }));
                if (!withD3GateSatisfied) return false;
                routingField.SetValue(host, null);
                if (!Convert.ToBoolean(ready.Invoke(host, new object[] { store })))
                    throw new InvalidOperationException("Qwen appraisal readiness still requires D3 routing when all other native workload gates pass.");
                return true;
            }
            finally
            {
                routingField.SetValue(host, oldRouting);
                probeRouting.Dispose();
            }
        }

        private static int CountTranscript(WorldStateStore store, string personId)
        {
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            return store.ConversationTranscript(personId, now + 86400000d).Count;
        }

        private static int CountExactExchange(WorldStateStore store, string personId,
            string playerText, string npcText)
        {
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int count = 0;
            foreach (NpcMemoryEntry entry in store.ConversationTranscript(personId, now + 86400000d))
                if (entry != null && entry.Source != "observed" && entry.PlayerText == playerText && entry.NpcText == npcText)
                    count++;
            return count;
        }

        private static long ReadSaveEpoch(PeopleOfTheIliacBayMod host)
        {
            FieldInfo field = typeof(PeopleOfTheIliacBayMod).GetField("conversationSaveEpoch", InstanceMembers);
            if (field == null) throw new InvalidOperationException("Runtime save epoch field is missing.");
            return Convert.ToInt64(field.GetValue(host));
        }

        private static void SetProbeBusy(PeopleOfTheIliacBayMod host, bool value)
        { RequireHostField("conversationBusy").SetValue(host, value); }

        private static void SetReturningGreetingPending(PeopleOfTheIliacBayMod host, bool value)
        { RequireHostField("returningGreetingPending").SetValue(host, value); }

        private static FieldInfo RequireHostField(string name)
        {
            FieldInfo field = typeof(PeopleOfTheIliacBayMod).GetField(name, InstanceMembers);
            if (field == null) throw new InvalidOperationException("Required lifecycle runtime field is missing: " + name);
            return field;
        }

        private static string DescribeRuntimeState(PeopleOfTheIliacBayMod host, GameManager game,
            DaggerfallTalkWindow talk, object snapshot, string archiveId, string archiveHead, long stageToken)
        {
            object binding = Read(snapshot, "Binding");
            FieldInfo requestField = RequireHostField("conversationContextRequestRevision");
            FieldInfo sessionField = RequireHostField("conversationContextSessionId");
            FieldInfo currentField = RequireHostField("conversationContextCurrentSnapshot");
            FieldInfo speakerField = RequireHostField("conversationContextSessionSpeaker");
            FieldInfo issuedField = RequireHostField("conversationContextIssuedStageToken");
            FieldInfo busyField = RequireHostField("conversationBusy");
            FieldInfo pendingField = RequireHostField("returningGreetingPending");
            MethodInfo dispositionMethod = typeof(PeopleOfTheIliacBayMod).GetMethod("CurrentDialogueDisposition", InstanceMembers);
            if (dispositionMethod == null) throw new InvalidOperationException("Native disposition diagnostic method is missing.");

            object nativeSpeaker = null;
            string speakerType = game.TalkManager.CurrentNPCType.ToString();
            if (game.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile)
            {
                nativeSpeaker = game.TalkManager.MobileNPC;
                speakerType += ":mobile";
            }
            else if (game.TalkManager.CurrentNPCType == TalkManager.NPCType.Static)
            {
                nativeSpeaker = game.TalkManager.StaticNPC;
                speakerType += ":static";
            }
            UnityEngine.Object capturedSpeaker = speakerField.GetValue(host) as UnityEngine.Object;
            string nativeSpeakerId = nativeSpeaker is UnityEngine.Object && (UnityEngine.Object)nativeSpeaker
                ? ((UnityEngine.Object)nativeSpeaker).GetInstanceID().ToString(CultureInfo.InvariantCulture) : "null";
            string capturedSpeakerId = capturedSpeaker
                ? capturedSpeaker.GetInstanceID().ToString(CultureInfo.InvariantCulture) : "null";
            object activeSnapshot = currentField.GetValue(host);
            string activeRequest = activeSnapshot == null ? "null" :
                Convert.ToString(Read(Read(activeSnapshot, "Binding"), "RequestRevision"), CultureInfo.InvariantCulture);

            return "request=" + Convert.ToString(requestField.GetValue(host), CultureInfo.InvariantCulture) +
                "; snapshotRequest=" + Convert.ToString(Read(binding, "RequestRevision"), CultureInfo.InvariantCulture) +
                "; activeSnapshotRequest=" + activeRequest +
                "; session=" + Convert.ToString(sessionField.GetValue(host)) +
                "; snapshotSession=" + Convert.ToString(Read(binding, "SessionId")) +
                "; saveEpoch=" + Convert.ToString(Read(binding, "SaveEpoch"), CultureInfo.InvariantCulture) +
                "; issuedToken=" + Convert.ToString(issuedField.GetValue(host), CultureInfo.InvariantCulture) +
                "; stageToken=" + stageToken.ToString(CultureInfo.InvariantCulture) +
                "; nativeSpeaker=" + speakerType + ":" + nativeSpeakerId +
                "; capturedSpeaker=" + capturedSpeakerId +
                "; sameSpeaker=" + ReferenceEquals(nativeSpeaker, capturedSpeaker) +
                "; disposition=" + Convert.ToString(dispositionMethod.Invoke(host, null), CultureInfo.InvariantCulture) +
                "; busy=" + Convert.ToString(busyField.GetValue(host)) +
                "; returningGreetingPending=" + Convert.ToString(pendingField.GetValue(host)) +
                "; topIsTalk=" + ReferenceEquals(DaggerfallUI.UIManager.TopWindow, talk) +
                "; talkContained=" + DaggerfallUI.UIManager.ContainsWindow(talk) +
                "; archive=" + archiveId + "; archiveHead=" + archiveHead;
        }

        // Unity runs yielded IEnumerator children outside the parent iterator.
        // Flatten ordinary coroutine iterators here so child exceptions reach Run,
        // which can then restore the baseline. Keep Unity wait instructions outside
        // the stack: CustomYieldInstruction itself implements IEnumerator, but must
        // be yielded back to Unity for time/frame progress.
        private static IEnumerator DriveFlattened(IEnumerator root)
        {
            var stack = new System.Collections.Generic.Stack<IEnumerator>();
            if (root == null) throw new ArgumentNullException("root");
            stack.Push(root);
            Exception failure = null;
            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator currentIterator = stack.Peek();
                    bool more;
                    object current = null;
                    try
                    {
                        more = currentIterator.MoveNext();
                        if (more) current = currentIterator.Current;
                    }
                    catch (Exception error)
                    {
                        failure = error;
                        break;
                    }

                    if (!more)
                    {
                        stack.Pop();
                        DisposeIterator(currentIterator, ref failure);
                        if (failure != null) break;
                        continue;
                    }

                    IEnumerator child = current as IEnumerator;
                    if (child != null && !(current is YieldInstruction) && !(current is CustomYieldInstruction))
                    {
                        stack.Push(child);
                        continue;
                    }
                    yield return current;
                }
            }
            finally
            {
                while (stack.Count > 0) DisposeIterator(stack.Pop(), ref failure);
            }
            if (failure != null)
                throw new InvalidOperationException("Nested native lifecycle coroutine failed.", failure);
        }

        private static void DisposeIterator(IEnumerator iterator, ref Exception failure)
        {
            var disposable = iterator as IDisposable;
            if (disposable == null) return;
            try { disposable.Dispose(); }
            catch (Exception error) { if (failure == null) failure = error; }
        }
    }
}
