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
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// One real poison-onset receipt through the normal background Qwen scheduler and Core commit.
    /// Separate from the deterministic, model-free NativeNpcPoisonExperienceProbe.
    /// </summary>
    public static class NativeNpcPoisonAppraisalProbe
    {
        public const string Flag = "-peopleoftheiliacbay-native-poison-appraisal-probe";
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class RunEvidence
        {
            internal WorldStateStore Store;
            internal ResidentActor Actor;
            internal string ResidentId;
            internal uint SavedIdentitySeed;
            internal int SavedNativeFactionId;
            internal bool SavedQuestProtected;
            internal NpcSocialBackground ExpectedBackground;
            internal bool BeforeProfilePresent;
            internal bool ProfileBound;
            internal string EffectKey;
            internal long Sequence;
            internal string DeduplicationKey;
            internal string CanonicalEvidenceId;
            internal string PromptSha256;
            internal string JournalPath;
            internal float StartRealtime;
            internal string Failure;
            internal long BeforeInboxRevision;
            internal int BeforeInboxCount;
            internal int BeforeMoodValue;
            internal long BeforeMoodRevision;
            internal string BeforeRelationship;
            internal int BeforeDisposition;
            internal bool HadBeforeDisposition;
            internal long InitialCompletionCount;
            internal int BackgroundTicketCount;
            internal int RequestMood;
            internal int TraceCount;
            internal string TraceFailure;
            internal string RequestJson;
            internal string RawResponseBody;
            internal string ParsedOrUnavailable;
            internal object OriginalTrace;
            internal bool OriginalSchedulerSuspended;
            internal bool TraceAttached;
            internal bool Injected;
            internal bool Consumed;
            internal int AppraisedMoodDelta;
            internal int AppliedMoodDelta;
            internal int AffinityDelta;
            internal int AppliedAffinityDelta;
            internal int TrustDelta;
            internal int ResentmentDelta;
            internal int FearDelta;
            internal long AfterMoodRevision;
            internal int AfterMoodValue;
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            GameManager game = GameManager.Instance;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave &&
                ArgumentValue("-peopleoftheiliacbay-fixture-character") == OwnedCharacter &&
                ArgumentValue("-peopleoftheiliacbay-fixture-save") == BaselineSave,
                "Native poison appraisal requires its explicit flag, owned disposable character, and exact baseline save.");
            Require(host && streets && game && game.IsReady && game.PlayerEntity != null &&
                game.PlayerEntity.Name == OwnedCharacter && streets.Ready,
                "Native poison appraisal requires the ready owned disposable fixture.");

            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, BaselineSave) >= 0,
                "Native poison appraisal cannot find the exact owned baseline save.");
            Require(!game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.Name == "Gothway Garden",
                "Native poison appraisal must start at the Gothway Garden exterior baseline.");

            var evidence = new RunEvidence();
            evidence.StartRealtime = Time.realtimeSinceStartup;
            evidence.OriginalSchedulerSuspended = ReadBoolField(host, "experienceAppraisalsSuspended");
            Require(!evidence.OriginalSchedulerSuspended,
                "Native poison appraisal requires the normal production appraisal scheduler to be enabled.");
            bool restored = false;
            Exception probeFailure = null;
            Exception restoreFailure = null;
            bool baselineLoadAttempted = false;
            try
            {
                evidence.Store = ReadHostField(host, "store") as WorldStateStore;
                Require(evidence.Store != null, "Native poison appraisal requires the active save-owned Core store.");
                SetSchedulerSuspended(host, true);

                baselineLoadAttempted = true;
                var baselineLoad = new NestedEnumeratorDriver(
                    ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, BaselineSave, true));
                bool baselineLoaded = false;
                try
                {
                    object yielded;
                    while (baselineLoad.TryMoveNext(out yielded)) yield return yielded;
                    if (baselineLoad.Failure != null) probeFailure = baselineLoad.Failure;
                    else baselineLoaded = true;
                }
                finally { baselineLoad.Dispose(); }

                if (baselineLoaded)
                {
                    SetSchedulerSuspended(host, false);
                    var core = new NestedEnumeratorDriver(RunCore(host, streets, evidence));
                    bool coreCompleted = false;
                    try
                    {
                        object yielded;
                        while (core.TryMoveNext(out yielded)) yield return yielded;
                        probeFailure = core.Failure;
                        coreCompleted = true;
                    }
                    finally
                    {
                        core.Dispose();
                        if (!coreCompleted) SetSchedulerSuspended(host, true);
                    }
                }
            }
            finally
            {
                SetSchedulerSuspended(host, true);
                DetachTrace(host, evidence);
            }

            if (baselineLoadAttempted)
            {
                var restore = new NestedEnumeratorDriver(RestoreBaseline(host, streets));
                bool restoreCompleted = false;
                try
                {
                    object yielded;
                    while (restore.TryMoveNext(out yielded)) yield return yielded;
                    restoreFailure = restore.Failure;
                    restoreCompleted = restore.Failure == null;
                }
                finally
                {
                    restore.Dispose();
                    if (!restoreCompleted)
                    {
                        if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                        SetSchedulerSuspended(host, true);
                        Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_APPRAISAL_BASELINE_RESTORE_SKIPPED_EXTERNAL_CANCEL: restore iterator did not finish.");
                    }
                }

                if (restoreFailure == null)
                {
                    if (!string.IsNullOrWhiteSpace(evidence.JournalPath))
                    {
                        try
                        {
                            AppendJournal(evidence, "baseline_restored", "\"character\":\"" + EscapeJson(OwnedCharacter) +
                                "\",\"save\":\"" + EscapeJson(BaselineSave) + "\",\"verifiedExterior\":true");
                        }
                        catch (Exception error) { restoreFailure = error; }
                    }
                    if (restoreFailure == null)
                    {
                        restored = true;
                        SetSchedulerSuspended(host, evidence.OriginalSchedulerSuspended);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_APPRAISAL_BASELINE_RESTORED: character=" +
                            OwnedCharacter + "; save=" + BaselineSave + "; resident=" + (evidence.ResidentId ?? "unselected") + ".");
                    }
                }
            }

            if (probeFailure != null && restoreFailure != null)
                throw new AggregateException("Native poison appraisal and baseline restoration failed.", probeFailure, restoreFailure);
            if (probeFailure != null) throw probeFailure;
            if (restoreFailure != null) throw restoreFailure;
            Require(restored && evidence.Consumed,
                "Native poison appraisal did not commit its single receipt and restore the exact baseline.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_APPRAISAL_PASS: resident=" + evidence.ResidentId +
                "; effectKey=" + evidence.EffectKey + "; sequence=" + evidence.Sequence +
                "; consumedOnce=true; moodRevision=" + evidence.BeforeMoodRevision + "->" + evidence.AfterMoodRevision +
                "; proposedMood=" + evidence.AppraisedMoodDelta + "; appliedMood=" + evidence.AppliedMoodDelta +
                "; affinityDelta=" + evidence.AffinityDelta + "; relationshipDeltas=" + evidence.TrustDelta + "/" +
                evidence.ResentmentDelta + "/" + evidence.FearDelta + "; cause=unknown; playerModified=false; " +
                "rawEvidence=" + (evidence.JournalPath ?? "unavailable") + "; responseWasNotRewritten=true.");
        }

        private static IEnumerator RunCore(PeopleOfTheIliacBayMod host, StreetPopulation streets, RunEvidence evidence)
        {
            Require(!ReadBoolField(host, "experienceAppraisalsSuspended"),
                "Production appraisal scheduler was suspended before poison injection.");
            Require(ExperienceRuntimeReady(host, evidence.Store),
                "Production appraisal prerequisites are not ready; parent must preflight the existing Qwen server.");
            var client = ReadHostField(host, "localDialogue") as LocalDialogueClient;
            Require(client && !client.IsBusy && ReadIntField(host, "activeExperienceBackgroundTicket") == 0,
                "Native poison appraisal requires an idle production transport and scheduler.");
            Require(ReadBoolField(host, "localDialogueEnabled"),
                "Native poison appraisal requires the explicitly enabled local Qwen feature.");

            WorldState before = evidence.Store.Snapshot();
            Require(before != null && before.NpcExperienceInbox != null && before.NpcExperienceInbox.Entries != null,
                "Owned save has no readable native experience inbox.");
            Require(CountPending(before) == 0,
                "Owned save has unrelated pending appraisal evidence; refusing to consume it.");
            evidence.BeforeInboxRevision = before.NpcExperienceInbox.Revision;
            evidence.BeforeInboxCount = before.NpcExperienceInbox.Entries.Count;
            evidence.InitialCompletionCount = ReadLongProperty(client, "ReplyCompletionCount");

            float idleUntil = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < idleUntil)
            {
                Require(ExperienceRuntimeReady(host, evidence.Store) && !client.IsBusy &&
                    ReadIntField(host, "activeExperienceBackgroundTicket") == 0 &&
                    CountPending(evidence.Store.Snapshot()) == 0,
                    "Scheduler or transport stopped being idle before native poison injection.");
                yield return null;
            }

            evidence.Actor = FindCandidate(streets, evidence.Store);
            if (!evidence.Actor)
            {
                string diagnostics = CandidateDiagnostics(streets, evidence.Store);
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_APPRAISAL_UNAVAILABLE: " + diagnostics + ".");
                throw new InvalidOperationException("No eligible existing saved resident with a poison binding and clean status history.");
            }

            evidence.ResidentId = evidence.Actor.Resident.Id;
            Require(evidence.Store.TryReadNpcPersonalityIdentity(evidence.ResidentId, out evidence.SavedIdentitySeed,
                    out evidence.SavedNativeFactionId, out evidence.SavedQuestProtected) && !evidence.SavedQuestProtected &&
                evidence.SavedIdentitySeed == evidence.Actor.Resident.AppearanceSeed &&
                TryResolveBackground(evidence.SavedNativeFactionId, out evidence.ExpectedBackground),
                "Selected resident lacks a resolvable saved identity seed, native background, or unprotected Core status.");
            NpcPersonalityProfile profileBefore = evidence.Store.ReadNpcPersonality(evidence.ResidentId);
            evidence.BeforeProfilePresent = profileBefore != null;
            if (profileBefore != null)
                Require(profileBefore.PersonId == evidence.ResidentId && profileBefore.Seed == evidence.SavedIdentitySeed &&
                    profileBefore.Background == evidence.ExpectedBackground,
                    "Existing saved resident profile does not match its stable native identity.");
            evidence.JournalPath = CreateJournal(evidence, before);
            AppendJournal(evidence, "resident_identity_baseline", "\"personId\":\"" + EscapeJson(evidence.ResidentId) +
                "\",\"savedAppearanceSeed\":" + evidence.SavedIdentitySeed.ToString(CultureInfo.InvariantCulture) +
                ",\"savedNativeFactionId\":" + evidence.SavedNativeFactionId.ToString(CultureInfo.InvariantCulture) +
                ",\"resolvedBackground\":\"" + EscapeJson(evidence.ExpectedBackground.ToString()) + "\"" +
                ",\"savedQuestProtected\":" + evidence.SavedQuestProtected.ToString().ToLowerInvariant() +
                ",\"beforeProfilePresent\":" + evidence.BeforeProfilePresent.ToString().ToLowerInvariant());
            var native = evidence.Actor.GetComponent<DaggerfallEntityBehaviour>();
            var manager = evidence.Actor.GetComponent<EntityEffectManager>();
            Require(native && native.Entity != null && native.Entity.CurrentHealth > 0 && manager &&
                manager.gameObject == evidence.Actor.gameObject && evidence.Actor.GetComponent<NativeNpcPoisonOnsetBinding>(),
                "Selected resident lost its eligible native effect-manager or poison-binding identity.");

            ConversationMood beforeMood = FindMood(before, evidence.ResidentId);
            evidence.BeforeMoodValue = beforeMood == null ? 0 : beforeMood.Value;
            evidence.BeforeMoodRevision = beforeMood == null ? 0 : beforeMood.Revision;
            evidence.BeforeRelationship = RelationshipFingerprint(evidence.Store.ReadNpcRelationship(
                evidence.ResidentId, NpcDirectedRelationship.PlayerTargetId));
            evidence.HadBeforeDisposition = evidence.Store.TryGetActualConversationDisposition(
                evidence.ResidentId, out evidence.BeforeDisposition);

            object priorTrace;
            Require(AttachTrace(host, evidence, out priorTrace),
                "Production appraiser trace seam is unavailable; cannot retain exact request/response evidence.");
            evidence.OriginalTrace = priorTrace;
            evidence.TraceAttached = true;

            EntityEffectBundle bundle = manager.CreatePoison(Poisons.Nux_Vomica);
            Require(bundle != null, "DFU could not create the controlled classic Nux Vomica poison bundle.");
            manager.AssignBundle(bundle, AssignBundleFlags.BypassSavingThrows | AssignBundleFlags.BypassChance);
            evidence.Injected = true;

            NativeStatusEntry created = FindOnlyNewStatus(before, evidence.Store.Snapshot(), evidence.ResidentId);
            if (created != null)
            {
                evidence.EffectKey = created.EffectKey;
                evidence.Sequence = created.Sequence;
                evidence.DeduplicationKey = created.DeduplicationKey;
                evidence.CanonicalEvidenceId = created.CanonicalEvidenceId;
            }
            LiveEffectBundle exactActivePoison = FindActivePoisonBundle(manager);
            Require(exactActivePoison != null,
                "The assigned test poison did not remain as an exact active bundle for immediate fixture cleanup.");
            manager.RemoveBundle(exactActivePoison);
            Require(!ContainsBundleReference(manager, exactActivePoison) && FindActivePoisonBundle(manager) == null,
                "Could not remove only the exact controlled poison bundle after its application receipt was observed.");
            AppendJournal(evidence, "test_effect_removed_after_application",
                "\"effectRemovedAfterApplication\":true,\"exactBundleReferenceRemoved\":true," +
                "\"meaning\":\"historical accepted application; no cure semantics or active-status-persistence claim\"");
            if (created != null)
                AppendJournal(evidence, "native_receipt", "\"observerId\":\"" + EscapeJson(evidence.ResidentId) +
                    "\",\"deduplicationKey\":\"" + EscapeJson(created.DeduplicationKey) +
                    "\",\"canonicalEvidenceId\":\"" + EscapeJson(created.CanonicalEvidenceId) +
                    "\",\"effectKey\":\"" + EscapeJson(created.EffectKey) + "\",\"sequence\":" +
                    created.Sequence.ToString(CultureInfo.InvariantCulture) + ",\"responsibleActorAttribution\":\"Unknown\",\"responsibleActorId\":null," +
                    "\"status\":\"" + EscapeJson(created.Status.ToString()) + "\"");
            Require(created != null && created.StatusKind == (int)NpcExperienceStatusKind.Poison &&
                created.Sequence > 0 && !string.IsNullOrWhiteSpace(created.EffectKey) &&
                created.Status == NpcExperienceInboxStatus.Pending &&
                created.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Unknown &&
                created.ResponsibleActorId == null,
                "Native observer did not create exactly one observer-bound poison receipt with unknown cause.");

            float deadline = Time.realtimeSinceStartup + 240f;
            int lastTicket = 0;
            var seenTickets = new HashSet<int>();
            bool sawRequest = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                int ticket = ReadIntField(host, "activeExperienceBackgroundTicket");
                if (ticket != 0)
                {
                    sawRequest = true;
                    if (seenTickets.Add(ticket))
                    {
                        evidence.BackgroundTicketCount++;
                        Require(seenTickets.Count == 1,
                            "Production scheduler started a second appraisal attempt; probe stops without reroll.");
                        lastTicket = ticket;
                        CaptureActiveRequest(host, evidence, created);
                    }
                }

                WorldState current = evidence.Store.Snapshot();
                NativeStatusEntry row = FindEntry(current, evidence.ResidentId, created.DeduplicationKey);
                Require(row != null && row.StatusKind == created.StatusKind && row.Sequence == created.Sequence &&
                    string.Equals(row.EffectKey, created.EffectKey, StringComparison.Ordinal) &&
                    row.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Unknown && row.ResponsibleActorId == null,
                    "Native poison receipt changed identity or attribution while awaiting appraisal.");

                if (row.Status == NpcExperienceInboxStatus.Consumed)
                {
                    evidence.Consumed = true;
                    SetSchedulerSuspended(host, true);
                    NpcExperienceInboxEntry committedRow = FindInboxRow(current, evidence.ResidentId, created.DeduplicationKey);
                    Require(committedRow != null && committedRow.Status == NpcExperienceInboxStatus.Consumed,
                        "Consumed status wrapper has no matching full Core inbox row.");
                    yield return VerifyCommittedEffects(host, evidence, created, current, committedRow);
                    yield return NpcMoodCauseDialogueProbe.Run(host,evidence.Actor,evidence.Store,evidence.ResidentId);
                    break;
                }
                if (row.Status == NpcExperienceInboxStatus.Deferred)
                {
                    SetSchedulerSuspended(host, true);
                    evidence.Failure = "Production scheduler deferred first appraisal attempt.";
                    break;
                }

                if (sawRequest && ticket == 0 && !client.IsBusy)
                {
                    // The production runtime schedules retries after a delay. Pause its scheduler now so this
                    // opt-in acceptance records only the first completed attempt and never rerolls it.
                    SetSchedulerSuspended(host, true);
                    evidence.Failure = "First production appraisal attempt ended without consuming the native receipt.";
                    break;
                }
                if (!ExperienceRuntimeReady(host, evidence.Store))
                {
                    SetSchedulerSuspended(host, true);
                    if (lastTicket != 0 && client.IsBackgroundCurrent(lastTicket)) client.CancelBackground(lastTicket);
                    evidence.Failure = "Production appraisal became unavailable before receipt commit.";
                    break;
                }
                yield return null;
            }

            if (!evidence.Consumed && evidence.Failure == null)
            {
                SetSchedulerSuspended(host, true);
                if (lastTicket != 0 && client.IsBackgroundCurrent(lastTicket)) client.CancelBackground(lastTicket);
                evidence.Failure = "Timed out waiting for the first production appraisal attempt.";
            }

            // Keep scheduler stopped until the exact baseline reload completes. The captured ticket becomes
            // stale when that load restores the Core store; no fixture event survives into the next session.
            SetSchedulerSuspended(host, true);
            Require(evidence.Consumed && evidence.Failure == null,
                evidence.Failure ?? "Native poison receipt was not committed by the production scheduler.");
        }

        private static IEnumerator VerifyCommittedEffects(PeopleOfTheIliacBayMod host, RunEvidence evidence,
            NativeStatusEntry source, WorldState after, NpcExperienceInboxEntry row)
        {
            Require(row.Status == NpcExperienceInboxStatus.Consumed && row.Revision == 2 && row.Appraisal != null,
                "Production Core did not retain one typed consumed appraisal for the poison receipt.");
            Require(row.Origin == NpcExperienceEventOrigin.NativeOutcome && row.EventKind == NpcExperienceEventKind.StatusOnset &&
                row.StatusKind == (int)NpcExperienceStatusKind.Poison && row.StatusOnsetSequence == evidence.Sequence &&
                row.StatusNativeEffectKey == evidence.EffectKey && row.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Unknown &&
                row.ResponsibleActorId == null && row.AlreadyAppliedEffectsMask == NpcExperienceAppliedEffectsMask.None,
                "Consumed appraisal row no longer matches exact unknown-cause native poison evidence.");
            Require(after.NpcExperienceInbox.Revision == evidence.BeforeInboxRevision + 1 &&
                after.NpcExperienceInbox.Entries.Count == evidence.BeforeInboxCount + 1 &&
                CountPending(after) == 0,
                "Appraisal consumed anything beyond the single newly queued poison receipt.");
            Require(evidence.TraceCount == 1 && evidence.TraceFailure == null &&
                !string.IsNullOrWhiteSpace(evidence.RequestJson) && !string.IsNullOrWhiteSpace(evidence.RawResponseBody) &&
                !string.IsNullOrWhiteSpace(evidence.ParsedOrUnavailable),
                "Production transport did not journal exactly one raw request and response body.");
            Require(evidence.PromptSha256 != null,
                "Source-bound appraisal request prompt was not captured before production completion.");
            var client = ReadHostField(host, "localDialogue") as LocalDialogueClient;
            Require(client && evidence.BackgroundTicketCount == 1 &&
                ReadLongProperty(client, "ReplyCompletionCount") == evidence.InitialCompletionCount,
                "Expected one distinct production background ticket and no foreground reply-counter change.");
            Require(evidence.RequestJson.IndexOf(EscapeJson(evidence.EffectKey), StringComparison.Ordinal) >= 0,
                "Raw transport request does not retain the native poison key from the typed Core prompt.");
            evidence.AppraisedMoodDelta = row.Appraisal.MoodDelta;
            evidence.AppliedMoodDelta = row.Appraisal.AppliedMoodDelta;
            evidence.AffinityDelta = row.Appraisal.AffinityDelta;
            evidence.AppliedAffinityDelta = row.Appraisal.AppliedAffinityDelta;
            evidence.TrustDelta = row.Appraisal.TrustDelta;
            evidence.ResentmentDelta = row.Appraisal.ResentmentDelta;
            evidence.FearDelta = row.Appraisal.FearDelta;

            Require(evidence.AffinityDelta == 0 && evidence.AppliedAffinityDelta == 0 &&
                evidence.TrustDelta == 0 && evidence.ResentmentDelta == 0 && evidence.FearDelta == 0,
                "Unknown-cause status evidence changed affinity or directed relationship values.");
            Require(RelationshipFingerprint(evidence.Store.ReadNpcRelationship(evidence.ResidentId,
                    NpcDirectedRelationship.PlayerTargetId)) == evidence.BeforeRelationship,
                "Poison appraisal changed the observer's directed relationship toward the player.");
            var mood = FindMood(after, evidence.ResidentId);
            Require(mood != null && mood.Revision == evidence.BeforeMoodRevision + 1 &&
                mood.Value == evidence.RequestMood + evidence.AppliedMoodDelta,
                "Mood appraisal did not apply once from the exact captured mood baseline.");
            evidence.AfterMoodRevision = mood.Revision;
            evidence.AfterMoodValue = mood.Value;
            int disposition;
            bool hasDisposition = evidence.Store.TryGetActualConversationDisposition(evidence.ResidentId, out disposition);
            Require(hasDisposition == evidence.HadBeforeDisposition &&
                (!hasDisposition || disposition == evidence.BeforeDisposition),
                "Unknown-cause poison appraisal changed the observer's actual disposition.");

            long inboxRevision = after.NpcExperienceInbox.Revision;
            long moodRevision = mood.Revision;
            float settleUntil = Time.realtimeSinceStartup + 1.25f;
            while (Time.realtimeSinceStartup < settleUntil)
            {
                WorldState stable = evidence.Store.Snapshot();
                NativeStatusEntry stableRow = FindEntry(stable, evidence.ResidentId, source.DeduplicationKey);
                ConversationMood stableMood = FindMood(stable, evidence.ResidentId);
                Require(stableRow != null && stableRow.Status == NpcExperienceInboxStatus.Consumed &&
                    stable.NpcExperienceInbox.Revision == inboxRevision && stableMood != null &&
                    stableMood.Revision == moodRevision,
                    "Consumed receipt or mood revision changed during the once-only settle window.");
                yield return null;
            }
            AppendJournal(evidence, "core_commit", "\"status\":\"Consumed\",\"moodDelta\":" +
                evidence.AppraisedMoodDelta.ToString(CultureInfo.InvariantCulture) + ",\"appliedMoodDelta\":" +
                evidence.AppliedMoodDelta.ToString(CultureInfo.InvariantCulture) + ",\"affinityDelta\":0,\"appliedAffinityDelta\":0,\"relationshipDeltas\":\"0/0/0\",\"moodRevision\":" +
                "\"" + evidence.BeforeMoodRevision.ToString(CultureInfo.InvariantCulture) + "->" +
                evidence.AfterMoodRevision.ToString(CultureInfo.InvariantCulture) + "\"");
        }

        private static void CaptureActiveRequest(PeopleOfTheIliacBayMod host, RunEvidence evidence, NativeStatusEntry source)
        {
            object ticket = ReadHostField(host, "activeExperienceTicket");
            Require(ticket != null, "Production scheduler exposed an active ticket ID without its Core ticket.");
            object request = ReadProperty(ticket, "Request");
            Require(request != null, "Active production ticket has no typed Core appraisal request.");
            string prompt = Convert.ToString(ReadProperty(request, "Prompt"), CultureInfo.InvariantCulture);
            string deduplicationKey = Convert.ToString(ReadProperty(request, "EvidenceDeduplicationKey"), CultureInfo.InvariantCulture);
            string canonicalId = Convert.ToString(ReadProperty(request, "CanonicalEvidenceId"), CultureInfo.InvariantCulture);
            evidence.RequestMood = Convert.ToInt32(ReadProperty(request, "CurrentMood"), CultureInfo.InvariantCulture);
            object target = ReadProperty(request, "RelationshipTargetId");
            Require(!string.IsNullOrEmpty(prompt) && deduplicationKey == source.DeduplicationKey &&
                canonicalId == source.CanonicalEvidenceId && target == null,
                "Production Qwen request is not bound to this native receipt with unknown actor attribution.");
            NpcPersonalityProfile profile = evidence.Store.ReadNpcPersonality(evidence.ResidentId);
            Require(profile != null && profile.PersonId == evidence.ResidentId &&
                profile.Seed == evidence.SavedIdentitySeed && profile.Background == evidence.ExpectedBackground &&
                profile.Version == 3 && profile.SelectedTraits != null && profile.SelectedTraits.Count == 3,
                "Production scheduler did not create or bind the matching saved resident profile before dispatch.");
            if (!evidence.ProfileBound)
            {
                AppendJournal(evidence, "resident_profile_bound_before_dispatch", "\"personId\":\"" + EscapeJson(profile.PersonId) +
                    "\",\"seed\":" + profile.Seed.ToString(CultureInfo.InvariantCulture) +
                    ",\"background\":\"" + EscapeJson(profile.Background.ToString()) +
                    "\",\"expectedBackground\":\"" + EscapeJson(evidence.ExpectedBackground.ToString()) +
                    "\",\"traitCount\":" + profile.SelectedTraits.Count.ToString(CultureInfo.InvariantCulture) +
                    ",\"version\":" + profile.Version.ToString(CultureInfo.InvariantCulture) +
                    ",\"beforeProfilePresent\":" + evidence.BeforeProfilePresent.ToString().ToLowerInvariant() +
                    ",\"productionCreatedOrBound\":true");
                evidence.ProfileBound = true;
            }
            string marker = "nativeStatus={kind=Poison,key=\"" + source.EffectKey + "\",onsetSequence=" +
                source.Sequence.ToString(CultureInfo.InvariantCulture) + ",responsibleActor=Unknown}";
            Require(prompt.IndexOf(marker, StringComparison.Ordinal) >= 0 &&
                prompt.IndexOf("relationshipTargetId=\"\"", StringComparison.Ordinal) >= 0,
                "Production request prompt does not preserve exact poison identity and unknown cause.");
            evidence.PromptSha256 = Sha256(prompt);
            AppendJournal(evidence, "core_request", "\"evidenceDeduplicationKey\":\"" + EscapeJson(deduplicationKey) +
                "\",\"canonicalEvidenceId\":\"" + EscapeJson(canonicalId) + "\",\"relationshipTargetId\":null,\"promptSha256\":\"" +
                evidence.PromptSha256 + "\",\"currentMood\":" + evidence.RequestMood.ToString(CultureInfo.InvariantCulture) +
                ",\"promptContainsExactStatus\":true");
        }

        private static bool AttachTrace(PeopleOfTheIliacBayMod host, RunEvidence evidence, out object original)
        {
            original = null;
            PropertyInfo property = typeof(PeopleOfTheIliacBayMod).GetProperty("ExperienceAppraisalTrace", Members);
            if (property == null || property.PropertyType != typeof(Action<string, string, string>) || !property.CanRead || !property.CanWrite)
                return false;
            original = property.GetValue(host, null);
            if (original != null) return false;
            evidence.OriginalTrace = original;
            Action<string, string, string> observer = (requestJson, rawBody, parsed) =>
            {
                evidence.TraceCount++;
                evidence.RequestJson = requestJson;
                evidence.RawResponseBody = rawBody;
                evidence.ParsedOrUnavailable = parsed;
                try
                {
                    AppendJournal(evidence, "transport", "\"requestJson\":\"" + EscapeJson(requestJson ?? "") +
                        "\",\"rawResponseBody\":\"" + EscapeJson(rawBody ?? "") + "\",\"parsedOrUnavailable\":\"" +
                        EscapeJson(parsed ?? "") + "\"");
                }
                catch (Exception error)
                {
                    evidence.TraceFailure = error.GetType().Name + ": " + error.Message;
                    Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_APPRAISAL_TRACE_FAILED: " + evidence.TraceFailure);
                }
            };
            property.SetValue(host, observer, null);
            return true;
        }

        private static void DetachTrace(PeopleOfTheIliacBayMod host, RunEvidence evidence)
        {
            if (!evidence.TraceAttached) return;
            PropertyInfo property = typeof(PeopleOfTheIliacBayMod).GetProperty("ExperienceAppraisalTrace", Members);
            if (property != null && property.CanWrite && property.PropertyType == typeof(Action<string, string, string>))
                property.SetValue(host, evidence.OriginalTrace, null);
            evidence.TraceAttached = false;
        }

        private static string CreateJournal(RunEvidence evidence, WorldState before)
        {
            string directory = Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "native-poison-appraisal-" + Guid.NewGuid().ToString("N") + ".jsonl");
            evidence.JournalPath = path;
            string line = "{\"event\":\"run_start\",\"utc\":\"" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                "\",\"character\":\"" + EscapeJson(OwnedCharacter) + "\",\"save\":\"" + EscapeJson(BaselineSave) +
                "\",\"inboxRevisionBefore\":" + before.NpcExperienceInbox.Revision.ToString(CultureInfo.InvariantCulture) +
                ",\"inboxCountBefore\":" + before.NpcExperienceInbox.Entries.Count.ToString(CultureInfo.InvariantCulture) +
                ",\"qwenModelReadiness\":\"required-parent-preflight\"}";
            File.WriteAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            return path;
        }

        private static void AppendJournal(RunEvidence evidence, string eventName, string fields)
        {
            if (string.IsNullOrWhiteSpace(evidence.JournalPath)) throw new InvalidOperationException("Probe journal is not initialized.");
            string line = "{\"event\":\"" + EscapeJson(eventName) + "\",\"utc\":\"" +
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\",\"elapsedRealtimeSeconds\":" +
                (Time.realtimeSinceStartup - evidence.StartRealtime).ToString("R", CultureInfo.InvariantCulture) + "," + fields + "}";
            File.AppendAllText(evidence.JournalPath, line + Environment.NewLine, new UTF8Encoding(false));
        }

        private sealed class NativeStatusEntry
        {
            internal string DeduplicationKey;
            internal string CanonicalEvidenceId;
            internal string EffectKey;
            internal int StatusKind;
            internal long Sequence;
            internal NpcResponsibleActorAttribution ResponsibleActorAttribution;
            internal string ResponsibleActorId;
            internal NpcExperienceInboxStatus Status;
        }

        private static NativeStatusEntry FindOnlyNewStatus(WorldState before, WorldState after, string personId)
        {
            if (before == null || after == null || before.NpcExperienceInbox == null || after.NpcExperienceInbox == null ||
                before.NpcExperienceInbox.Entries == null || after.NpcExperienceInbox.Entries == null ||
                after.NpcExperienceInbox.Revision != before.NpcExperienceInbox.Revision + 1 ||
                after.NpcExperienceInbox.Entries.Count != before.NpcExperienceInbox.Entries.Count + 1)
                return null;
            var oldKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcExperienceInboxEntry old in before.NpcExperienceInbox.Entries)
                if (old != null) oldKeys.Add(old.ObserverId + "\n" + old.DeduplicationKey);
            var added = new List<NpcExperienceInboxEntry>();
            foreach (NpcExperienceInboxEntry row in after.NpcExperienceInbox.Entries)
                if (row != null && !oldKeys.Contains(row.ObserverId + "\n" + row.DeduplicationKey)) added.Add(row);
            if (added.Count != 1 || added[0].Origin != NpcExperienceEventOrigin.NativeOutcome ||
                added[0].EventKind != NpcExperienceEventKind.StatusOnset || added[0].ObserverId != personId ||
                added[0].SubjectPersonId != personId) return null;
            NpcExperienceInboxEntry entry = added[0];
            return new NativeStatusEntry { DeduplicationKey = entry.DeduplicationKey,
                CanonicalEvidenceId = entry.OutcomeId, EffectKey = entry.StatusNativeEffectKey,
                StatusKind = entry.StatusKind, Sequence = entry.StatusOnsetSequence,
                ResponsibleActorAttribution = entry.ResponsibleActorAttribution,
                ResponsibleActorId = entry.ResponsibleActorId, Status = entry.Status };
        }

        private static NativeStatusEntry FindEntry(WorldState state, string personId, string deduplicationKey)
        {
            if (state == null || state.NpcExperienceInbox == null || state.NpcExperienceInbox.Entries == null) return null;
            foreach (NpcExperienceInboxEntry entry in state.NpcExperienceInbox.Entries)
                if (entry != null && entry.ObserverId == personId && entry.DeduplicationKey == deduplicationKey)
                    return new NativeStatusEntry { DeduplicationKey = entry.DeduplicationKey,
                        CanonicalEvidenceId = entry.OutcomeId, EffectKey = entry.StatusNativeEffectKey,
                        StatusKind = entry.StatusKind, Sequence = entry.StatusOnsetSequence,
                        ResponsibleActorAttribution = entry.ResponsibleActorAttribution,
                        ResponsibleActorId = entry.ResponsibleActorId, Status = entry.Status };
            return null;
        }

        private static NpcExperienceInboxEntry FindInboxRow(WorldState state, string personId, string deduplicationKey)
        {
            if (state == null || state.NpcExperienceInbox == null || state.NpcExperienceInbox.Entries == null) return null;
            foreach (NpcExperienceInboxEntry entry in state.NpcExperienceInbox.Entries)
                if (entry != null && entry.ObserverId == personId && entry.DeduplicationKey == deduplicationKey)
                    return entry;
            return null;
        }

        private static ResidentActor FindCandidate(StreetPopulation streets, WorldStateStore store)
        {
            if (!streets || !streets.Ready || store == null) return null;
            WorldState state = store.Snapshot();
            if (state == null) return null;
            ResidentActor[] actors = UnityEngine.Object.FindObjectsOfType<ResidentActor>();
            foreach (ResidentActor actor in actors)
            {
                if (!actor || !actor.gameObject.activeInHierarchy || actor.Resident == null || !actor.Resident.IsAlive ||
                    actor.Resident.AgeGroup != ResidentAgeGroup.Adult || actor.Resident.IsQuestProtected) continue;
                DaggerfallEntityBehaviour native = actor.GetComponent<DaggerfallEntityBehaviour>();
                EntityEffectManager manager = actor.GetComponent<EntityEffectManager>();
                uint identitySeed;
                int factionId;
                bool savedQuestProtected;
                NpcSocialBackground expectedBackground;
                if (!native || native.Entity == null || native.Entity.CurrentHealth <= 0 ||
                    native.Entity == GameManager.Instance.PlayerEntity || !manager || manager.gameObject != actor.gameObject ||
                    !actor.GetComponent<NativeNpcPoisonOnsetBinding>() || FindActivePoisonBundle(manager) != null ||
                    !store.TryReadNpcPersonalityIdentity(actor.Resident.Id, out identitySeed, out factionId, out savedQuestProtected) ||
                    savedQuestProtected || identitySeed != actor.Resident.AppearanceSeed ||
                    !TryResolveBackground(factionId, out expectedBackground) || HasStatusHistory(state, actor.Resident.Id)) continue;
                return actor;
            }
            return null;
        }

        private static string CandidateDiagnostics(StreetPopulation streets, WorldStateStore store)
        {
            int seen = 0, missingManager = 0, levelOne = 0, missingBinding = 0, missingIdentity = 0,
                unresolvedBackground = 0, savedProtected = 0, profileAbsent = 0, statusHistory = 0;
            WorldState state = store == null ? null : store.Snapshot();
            if (streets && streets.Ready)
                foreach (ResidentActor actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                {
                    if (!actor || !actor.gameObject.activeInHierarchy || actor.Resident == null || !actor.Resident.IsAlive ||
                        actor.Resident.AgeGroup != ResidentAgeGroup.Adult || actor.Resident.IsQuestProtected) continue;
                    seen++;
                    DaggerfallEntityBehaviour native = actor.GetComponent<DaggerfallEntityBehaviour>();
                    EntityEffectManager manager = actor.GetComponent<EntityEffectManager>();
                    uint identitySeed = 0;
                    int factionId = -1;
                    bool questProtected = false;
                    if (!manager || manager.gameObject != actor.gameObject) missingManager++;
                    if (!actor.GetComponent<NativeNpcPoisonOnsetBinding>()) missingBinding++;
                    if (native && native.Entity != null && native.Entity.Level == 1) levelOne++;
                    if (store != null && store.ReadNpcPersonality(actor.Resident.Id) == null) profileAbsent++;
                    if (store != null)
                    {
                        if (!store.TryReadNpcPersonalityIdentity(actor.Resident.Id, out identitySeed, out factionId, out questProtected)) missingIdentity++;
                        else
                        {
                            if (questProtected) savedProtected++;
                            NpcSocialBackground background;
                            if (!TryResolveBackground(factionId, out background)) unresolvedBackground++;
                        }
                    }
                    if (state != null && HasStatusHistory(state, actor.Resident.Id)) statusHistory++;
                }
            return "adultAliveResidents=" + seen + "; missingSameObjectManager=" + missingManager +
                "; nativeLevelOneObserved=" + levelOne + "; missingPoisonBinding=" + missingBinding +
                "; missingSavedIdentity=" + missingIdentity + "; savedQuestProtected=" + savedProtected +
                "; unresolvedNativeBackground=" + unresolvedBackground +
                "; profileAbsentAllowed=" + profileAbsent + "; priorStatusHistory=" + statusHistory;
        }

        private static bool HasStatusHistory(WorldState state, string personId)
        {
            if (state == null || state.NpcExperienceInbox == null || state.NpcExperienceInbox.Entries == null) return false;
            foreach (NpcExperienceInboxEntry entry in state.NpcExperienceInbox.Entries)
                if (entry != null && entry.ObserverId == personId && entry.EventKind == NpcExperienceEventKind.StatusOnset)
                    return true;
            return false;
        }

        private static LiveEffectBundle FindActivePoisonBundle(EntityEffectManager manager)
        {
            if (!manager) return null;
            LiveEffectBundle[] bundles = manager.EffectBundles;
            if (bundles == null) return null;
            foreach (LiveEffectBundle bundle in bundles)
            {
                if (bundle == null || bundle.bundleType != BundleTypes.Poison || bundle.liveEffects == null) continue;
                foreach (IEntityEffect effect in bundle.liveEffects)
                    if (effect is PoisonEffect poison && !poison.HasEnded) return bundle;
            }
            return null;
        }

        private static bool ContainsBundleReference(EntityEffectManager manager, LiveEffectBundle target)
        {
            if (!manager || target == null || manager.EffectBundles == null) return false;
            foreach (LiveEffectBundle bundle in manager.EffectBundles)
                if (ReferenceEquals(bundle, target)) return true;
            return false;
        }

        private static int CountPending(WorldState state)
        {
            if (state == null || state.NpcExperienceInbox == null || state.NpcExperienceInbox.Entries == null) return -1;
            int count = 0;
            foreach (NpcExperienceInboxEntry entry in state.NpcExperienceInbox.Entries)
                if (entry != null && entry.Status == NpcExperienceInboxStatus.Pending) count++;
            return count;
        }

        private static ConversationMood FindMood(WorldState state, string personId)
        {
            if (state == null || state.ConversationMoods == null) return null;
            foreach (ConversationMood mood in state.ConversationMoods)
                if (mood != null && mood.PersonId == personId) return mood;
            return null;
        }

        private static string RelationshipFingerprint(NpcDirectedRelationshipView relationship)
        {
            if (relationship == null) return "null";
            return relationship.ObserverId + "|" + relationship.TargetId + "|" +
                relationship.Revision.ToString(CultureInfo.InvariantCulture) + "|" +
                relationship.LastGameTime.ToString("R", CultureInfo.InvariantCulture) + "|" +
                relationship.TrustKnown + "|" + relationship.ReliabilityTrust + "|" + relationship.TrustConfidence + "|" +
                relationship.TrustEvidenceCount + "|" + relationship.Resentment + "|" + relationship.InterpersonalFear;
        }

        private static bool ExperienceRuntimeReady(PeopleOfTheIliacBayMod host, WorldStateStore store)
        {
            MethodInfo method = typeof(PeopleOfTheIliacBayMod).GetMethod("ExperienceAppraisalRuntimeReady", Members);
            if (method == null) return false;
            try { return Convert.ToBoolean(method.Invoke(host, new object[] { store }), CultureInfo.InvariantCulture); }
            catch (TargetInvocationException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private static bool TryResolveBackground(int factionId, out NpcSocialBackground background)
        {
            background = NpcSocialBackground.Unknown;
            MethodInfo method = typeof(PeopleOfTheIliacBayMod).GetMethod(
                "TryResolveNpcPersonalityBackground", BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) return false;
            object[] arguments = { factionId, NpcSocialBackground.Unknown };
            try
            {
                bool resolved = Convert.ToBoolean(method.Invoke(null, arguments), CultureInfo.InvariantCulture);
                if (arguments[1] is NpcSocialBackground value) background = value;
                return resolved;
            }
            catch (TargetInvocationException) { return false; }
            catch (ArgumentException) { return false; }
        }

        private static IEnumerator RestoreBaseline(PeopleOfTheIliacBayMod host, StreetPopulation streets)
        {
            if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
            yield return ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, BaselineSave, true);
            GameManager game = GameManager.Instance;
            Require(game && game.PlayerEntity != null && game.PlayerEntity.Name == OwnedCharacter &&
                !game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.Name == "Gothway Garden" && streets && streets.Ready,
                "Native poison appraisal did not restore the exact owned exterior baseline.");
        }

        private static void SetSchedulerSuspended(PeopleOfTheIliacBayMod host, bool value)
        { SetField(host, "experienceAppraisalsSuspended", value); }

        private static bool ReadBoolField(object host, string name)
        { return Convert.ToBoolean(ReadField(host, name), CultureInfo.InvariantCulture); }

        private static int ReadIntField(object host, string name)
        { return Convert.ToInt32(ReadField(host, name), CultureInfo.InvariantCulture); }

        private static long ReadLongProperty(object target, string name)
        { return Convert.ToInt64(ReadProperty(target, name), CultureInfo.InvariantCulture); }

        private static object ReadHostField(PeopleOfTheIliacBayMod host, string name)
        { return ReadField(host, name); }

        private static object ReadField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Required native poison appraisal field is missing: " + name);
            return field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Required native poison appraisal field is missing: " + name);
            field.SetValue(target, value);
        }

        private static object ReadProperty(object target, string name)
        {
            if (target == null) return null;
            PropertyInfo property = target.GetType().GetProperty(name, Members);
            if (property == null) throw new InvalidOperationException("Required native poison appraisal property is missing: " + name);
            return property.GetValue(target, null);
        }

        private static string ArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
            return null;
        }

        private static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        private static string EscapeJson(string value)
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

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private sealed class NestedEnumeratorDriver : IDisposable
        {
            private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
            internal Exception Failure { get; private set; }
            internal NestedEnumeratorDriver(IEnumerator root) { stack.Push(root); }
            internal bool TryMoveNext(out object yielded)
            {
                yielded = null;
                while (Failure == null && stack.Count > 0)
                {
                    IEnumerator top = stack.Peek();
                    bool more;
                    object current = null;
                    try { more = top.MoveNext(); if (more) current = top.Current; }
                    catch (Exception error) { Failure = error; DisposeStack(); return false; }
                    if (!more)
                    {
                        stack.Pop();
                        try { (top as IDisposable)?.Dispose(); }
                        catch (Exception error) { Failure = error; DisposeStack(); return false; }
                    }
                    else if (current is IEnumerator nested) stack.Push(nested);
                    else { yielded = current; return true; }
                }
                return false;
            }
            public void Dispose() { DisposeStack(); }
            private void DisposeStack()
            {
                while (stack.Count > 0)
                {
                    IEnumerator item = stack.Pop();
                    try { (item as IDisposable)?.Dispose(); }
                    catch (Exception error) { if (Failure == null) Failure = error; }
                }
            }
        }
    }
}
