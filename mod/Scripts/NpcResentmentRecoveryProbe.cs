using System;
using System.Collections;
using System.Collections.Generic;
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
    /// <summary>
    /// Controlled native persistence and context probe for independent resentment recovery.
    /// All recovery days are supplied to a detached store; no gameplay days or natural event producer are claimed.
    /// </summary>
    internal static class NpcResentmentRecoveryProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-npc-resentment-recovery-probe";
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class Evidence
        {
            internal string PersonId;
            internal bool TalkProjection;
            internal bool SerializerRoundTrip;
            internal bool Schema41Migration;
            internal bool TrustUnchanged;
            internal int FearAtProjection;
            internal int ResentmentAtProjection;
            internal double FearHalfLife;
            internal double ResentmentHalfLife;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave && host && streets &&
                GameManager.Instance && GameManager.Instance.IsReady && streets.Ready,
                "NPC resentment recovery probe requires opt-in and the exact ready owned Gothway Garden fixture.");

            Exception probeFailure = null;
            Exception restoreFailure = null;
            var evidence = new Evidence();
            IEnumerator cases = RunCases(host, streets, evidence);
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
                ConversationMoodProbe.Dispose(cases, ref probeFailure);
                if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                ConversationMoodProbe.CancelTransport(host);
            }

            IEnumerator restore = ConversationMoodProbe.LoadSave(host, streets, character, BaselineSave, false);
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
            finally { ConversationMoodProbe.Dispose(restore, ref restoreFailure); }

            if (restoreFailure != null)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_NPC_RESENTMENT_BASELINE_RESTORE_FAIL: " + restoreFailure);
                throw new InvalidOperationException("NPC resentment recovery probe could not reload the exact baseline.", restoreFailure);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_RESENTMENT_BASELINE_RESTORED: character=" + character +
                "; save=" + BaselineSave + "; fixture-state-reloaded=true; settings-written=false.");
            if (probeFailure != null)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NPC_RESENTMENT_FAILED_AFTER_BASELINE_RESTORE: true.");
                throw new InvalidOperationException("NPC resentment recovery probe failed after baseline restore.", probeFailure);
            }

            Require(evidence.TalkProjection && evidence.SerializerRoundTrip && evidence.Schema41Migration &&
                evidence.TrustUnchanged, "NPC resentment recovery evidence is incomplete.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_RESENTMENT_RECOVERY_NATIVE_PASS: person=" + evidence.PersonId +
                "; supplied-game-days=16; actual-gameplay-days=not-tested; controlled-source-bound-events=true; " +
                "FullSerializer-anchor-rate-fraction=true; cold-real-Talk-projection=true; " +
                "schema42-roundtrip=true; schema41-unknown-resentment-clock-migrated=true; trust-unchanged=true; " +
                "fear-projection=" + evidence.FearAtProjection + "; resentment-projection=" + evidence.ResentmentAtProjection +
                "; fear-half-life=" + evidence.FearHalfLife.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                "; resentment-half-life=" + evidence.ResentmentHalfLife.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                "; native-time-tolerance-game-seconds=0.001; model-calls=0; audio=0.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets, Evidence evidence)
        {
            Require(!Convert.ToBoolean(RequireField(typeof(PeopleOfTheIliacBayMod), "localDialogueEnabled").GetValue(host)) &&
                !Convert.ToBoolean(RequireField(typeof(PeopleOfTheIliacBayMod), "npcVoiceEnabled").GetValue(host)) &&
                !Convert.ToBoolean(RequireField(typeof(PeopleOfTheIliacBayMod), "dialoguePrewarmEnabled").GetValue(host)),
                "NPC resentment recovery probe requires free-text, NPC voice, and model prewarm disabled.");
            GameManager game = GameManager.Instance;
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, BaselineSave) >= 0,
                "Exact owned resentment recovery baseline save is missing.");
            FieldInfo storeField = RequireField(typeof(PeopleOfTheIliacBayMod), "store");
            var liveStore = storeField.GetValue(host) as WorldStateStore;
            Require(liveStore != null, "Resentment recovery probe cannot read the live fixture store.");
            string liveRelationshipJsonBefore = SaveRelationships(liveStore.Snapshot());

            ResidentActor actor = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host, game.PlayerMotor.transform.position,
                selected => actor = selected, requireNeutral: false);
            Require(actor && actor.Resident != null && actor.Resident.IsAlive && !actor.Resident.IsQuestProtected,
                "No eligible managed adult resident was available for the isolated recovery fixture.");
            evidence.PersonId = actor.Resident.Id;
            ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
            DaggerfallTalkWindow talk = null;
            yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
            Require(talk != null, "Native Talk did not open for the selected recovery fixture resident.");
            var initialContext = ConversationMoodProbe.CaptureSnapshot(host, talk, "Controlled recovery context.")
                as ConversationContextSnapshot;
            Require(initialContext != null && initialContext.Binding.PersonId == evidence.PersonId,
                "Initial real Talk context was not bound to the selected resident.");
            const double secondsPerGameDay = 24 * 60 * 60;
            double resentmentTime = initialContext.CapturedGameTime - 16 * secondsPerGameDay;
            Require(resentmentTime >= 0,
                "Owned fixture game time is too early to test a past resentment event over sixteen supplied game-days.");

            uint seed;
            int factionId;
            bool questProtected;
            Require(liveStore.TryReadNpcPersonalityIdentity(evidence.PersonId, out seed, out factionId, out questProtected) &&
                !questProtected, "Selected resident has no eligible stable native personality identity.");
            NpcSocialBackground background;
            Require(PeopleOfTheIliacBayMod.TryResolveNpcPersonalityBackground(factionId, out background),
                "Native faction did not resolve to a personality background.");

            WorldState isolatedSeed = SaveLoadManager.Deserialize(typeof(WorldState),
                SaveLoadManager.Serialize(typeof(WorldState), liveStore.Snapshot()), true) as WorldState;
            Require(isolatedSeed != null && isolatedSeed.Version == WorldState.CurrentVersion,
                "FullSerializer clone did not retain the current schema.");
            RemoveRelationshipFixture(isolatedSeed, evidence.PersonId);
            isolatedSeed.NpcPersonalities.RemoveAll(savedProfile => savedProfile != null && savedProfile.PersonId == evidence.PersonId);
            var isolated = new WorldStateStore(isolatedSeed.WorldSeed);
            isolated.Restore(isolatedSeed);
            NpcPersonalityProfile profile = isolated.GetOrCreateNpcPersonality(evidence.PersonId, seed, background);
            Require(profile != null && profile.Version == 3 && profile.SelectedTraits.Count == 3 && profile.Seed == seed,
                "Isolated native profile was not generated from the resident's stable seed and faction.");

            const string targetId = NpcDirectedRelationship.PlayerTargetId;
            Require(isolated.ReadNpcRelationship(evidence.PersonId, targetId) == null,
                "Detached relationship fixture was not empty before its controlled receipt.");
            for (int pulse = 1; pulse <= 4; pulse++)
                Require(Commit(isolated, evidence.PersonId, targetId,
                    "controlled-resentment-positive-" + pulse.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    resentmentTime, resentmentDelta: 10, fearDelta: 0),
                    "Controlled source-bound resentment event was rejected at pulse " + pulse + ".");
            NpcDirectedRelationshipView resentmentOnly = isolated.ReadNpcRelationship(evidence.PersonId, targetId);
            Require(resentmentOnly != null && resentmentOnly.HasResentmentRecoveryAnchor &&
                !resentmentOnly.HasFearRecoveryAnchor && !resentmentOnly.TrustKnown &&
                resentmentOnly.Resentment == 40 && resentmentOnly.InterpersonalFear == 0,
                "Resentment-only event did not establish only its own recovery clock.");

            WorldStateStore previousStore = storeField.GetValue(host) as WorldStateStore;
            object talkProjection = null;
            try
            {
                // Route one real, cold Talk capture through the detached store. No reply or audio is submitted.
                storeField.SetValue(host, isolated);
                talkProjection = ConversationMoodProbe.CaptureSnapshot(host, talk,
                    "Controlled detached resentment projection.");
            }
            finally { storeField.SetValue(host, previousStore); }
            var captured = talkProjection as ConversationContextSnapshot;
            NpcDirectedRelationshipView capturedRelation = captured == null ? null : captured.DirectedRelationship;
            NpcDirectedRelationshipView timedAtCapture = captured == null ? null : isolated.ReadNpcRelationship(
                evidence.PersonId, targetId, captured.CapturedGameTime);
            Require(captured != null && captured.Binding.PersonId == evidence.PersonId && capturedRelation != null &&
                timedAtCapture != null &&
                capturedRelation.HasResentmentRecoveryAnchor &&
                capturedRelation.ProjectedResentment < capturedRelation.StoredResentment &&
                capturedRelation.ProjectedResentment == timedAtCapture.ProjectedResentment &&
                capturedRelation.Resentment == timedAtCapture.Resentment,
                "Real cold Talk context did not show aged resentment from the supplied past event time.");
            evidence.TalkProjection = true;

            double fearTime = resentmentTime + secondsPerGameDay;
            Require(Commit(isolated, evidence.PersonId, targetId, "controlled-fear-only", fearTime,
                resentmentDelta: 0, fearDelta: 10), "Controlled fear-only event was rejected.");
            NpcDirectedRelationshipView afterFearOnly = isolated.ReadNpcRelationship(evidence.PersonId, targetId);
            Require(afterFearOnly != null && afterFearOnly.ResentmentRecoveryAnchorGameTime == resentmentTime &&
                afterFearOnly.FearRecoveryAnchorGameTime == fearTime,
                "Fear-only event restarted resentment recovery or failed to establish fear recovery.");

            double reductionTime = fearTime + secondsPerGameDay;
            Require(Commit(isolated, evidence.PersonId, targetId, "controlled-resentment-reduction", reductionTime,
                resentmentDelta: -1, fearDelta: 0), "Controlled resentment reduction event was rejected.");
            NpcDirectedRelationshipView beforeSave = isolated.ReadNpcRelationship(evidence.PersonId, targetId);
            Require(beforeSave != null && beforeSave.ResentmentRecoveryAnchorGameTime == resentmentTime &&
                beforeSave.FearRecoveryAnchorGameTime == fearTime &&
                Math.Abs(beforeSave.ResentmentRecoveryBaselineRemainder) > 0.000001 &&
                !beforeSave.TrustKnown && beforeSave.ReliabilityTrust == 0 && beforeSave.TrustEvidenceCount == 0,
                "Signed resentment update lost fractional carry, changed fear clock, or changed trust.");
            VerifyControlledReceipts(isolated.Snapshot(), evidence.PersonId, targetId);
            evidence.TrustUnchanged = true;

            double projectionTime = reductionTime + 14 * secondsPerGameDay;
            NpcDirectedRelationshipView projected = isolated.ReadNpcRelationship(evidence.PersonId, targetId, projectionTime);
            int expectedResentment = NpcResentmentRecoveryPolicy.ProvisionalDefault.Project(
                beforeSave.StoredResentment + beforeSave.ResentmentRecoveryBaselineRemainder,
                beforeSave.ResentmentRecoveryAnchorGameTime, projectionTime,
                beforeSave.ResentmentRecoveryHalfLifeSeconds);
            int expectedFear = NpcFearRecoveryPolicy.ProvisionalDefault.Project(
                beforeSave.StoredInterpersonalFear + beforeSave.FearRecoveryBaselineRemainder,
                beforeSave.FearRecoveryAnchorGameTime, projectionTime,
                beforeSave.FearRecoveryHalfLifeSeconds);
            Require(projected != null && projected.ProjectedResentment == expectedResentment &&
                projected.ProjectedInterpersonalFear == expectedFear &&
                Math.Abs(beforeSave.ResentmentRecoveryHalfLifeSeconds /
                    beforeSave.FearRecoveryHalfLifeSeconds - 2.0) < 0.000001,
                "Supplied +16-day projection did not preserve distinct resentment/fear rates and ages.");
            evidence.FearAtProjection = projected.ProjectedInterpersonalFear;
            evidence.ResentmentAtProjection = projected.ProjectedResentment;
            evidence.FearHalfLife = beforeSave.FearRecoveryHalfLifeSeconds;
            evidence.ResentmentHalfLife = beforeSave.ResentmentRecoveryHalfLifeSeconds;

            WorldState saved = isolated.Snapshot();
            Require(saved.Version == WorldState.CurrentVersion, "Isolated save did not use the current schema.");
            NpcDirectedRelationship savedRow = FindRelationship(saved, evidence.PersonId, targetId);
            Require(savedRow != null && savedRow.HasResentmentRecoveryAnchor &&
                savedRow.ResentmentRecoveryAnchorGameTime == beforeSave.ResentmentRecoveryAnchorGameTime &&
                savedRow.ResentmentRecoveryHalfLifeSeconds == beforeSave.ResentmentRecoveryHalfLifeSeconds &&
                savedRow.ResentmentRecoveryBaselineRemainder == beforeSave.ResentmentRecoveryBaselineRemainder,
                "Schema-42 saved row lacks resentment anchor, selected rate, or fractional carry.");
            string json = SaveLoadManager.Serialize(typeof(WorldState), saved);
            WorldState decoded = SaveLoadManager.Deserialize(typeof(WorldState), json, true) as WorldState;
            Require(decoded != null && decoded.Version == WorldState.CurrentVersion,
                "Native FullSerializer did not round-trip the current schema.");
            var restored = new WorldStateStore(saved.WorldSeed);
            restored.Restore(decoded);
            NpcDirectedRelationshipView afterRoundTrip = restored.ReadNpcRelationship(evidence.PersonId, targetId, projectionTime);
            Debug.Log("NPC_RESENTMENT_RECOVERY_SERIALIZER_COMPARE: " + RecoveryDiagnostics(projected, afterRoundTrip) + ".");
            Require(SameRecovery(projected, afterRoundTrip) &&
                afterRoundTrip.ProjectedResentment == projected.ProjectedResentment &&
                afterRoundTrip.ProjectedInterpersonalFear == projected.ProjectedInterpersonalFear &&
                afterRoundTrip.TrustKnown == projected.TrustKnown &&
                afterRoundTrip.ReliabilityTrust == projected.ReliabilityTrust,
                "FullSerializer changed the saved resentment/fear recovery or trust state.");
            evidence.SerializerRoundTrip = true;

            // Schema 41 had no trustworthy resentment clock. Emulate its row and ledger versions,
            // serialize with the native serializer, then require migration to retain value but not invent time.
            WorldState legacy41 = SaveLoadManager.Deserialize(typeof(WorldState), json, true) as WorldState;
            Require(legacy41 != null, "Could not create the schema-41 compatibility fixture.");
            // Separate historical relationship fixture: do not send v5 inbox
            // counters, source classifications or schema-54 cue/floor state
            // through a schema-41 envelope. Keep the current decoded save intact.
            Require(legacy41.NpcExperienceInbox != null && legacy41.NpcExperienceInbox.Entries != null &&
                legacy41.NpcMoodCauseHistoryFloors != null,
                "Schema-41 relationship fixture has no editable historical inbox boundary.");
            foreach (NpcExperienceInboxEntry legacy in legacy41.NpcExperienceInbox.Entries)
            {
                legacy.EnqueueOrdinal = 0;
                legacy.SourceVitalsRevision = 0;
                legacy.SourceKind = NpcExperienceSourceKind.LegacyUnknown;
            }
            legacy41.NpcExperienceInbox.Version = 4;
            legacy41.NpcExperienceInbox.Revision = legacy41.NpcExperienceInbox.Entries.Count;
            legacy41.NpcExperienceInbox.RetiredCount = 0;
            legacy41.NpcExperienceInbox.DetailRevision = 0;
            legacy41.NpcExperienceInbox.LegacyImportedRevision = 0;
            legacy41.NpcMoodCauseHistoryFloors.Clear();
            legacy41.ConversationCueArchive = null;
            legacy41.Version = 41;
            legacy41.NpcRelationships.Version = 2;
            NpcDirectedRelationship legacyRow = FindRelationship(legacy41, evidence.PersonId, targetId);
            Require(legacyRow != null, "Schema-41 fixture lost controlled relationship row.");
            legacyRow.Version = 2;
            legacyRow.HasResentmentRecoveryAnchor = false;
            legacyRow.ResentmentRecoveryAnchorGameTime = -1;
            legacyRow.ResentmentRecoveryHalfLifeSeconds = 0;
            legacyRow.ResentmentRecoveryBaselineRemainder = 0;
            string legacyJson = SaveLoadManager.Serialize(typeof(WorldState), legacy41);
            WorldState legacyDecoded = SaveLoadManager.Deserialize(typeof(WorldState), legacyJson, true) as WorldState;
            Require(legacyDecoded != null && legacyDecoded.Version == 41,
                "Native FullSerializer did not round-trip the schema-41 compatibility fixture.");
            var migrated = new WorldStateStore(legacy41.WorldSeed);
            migrated.Restore(legacyDecoded);
            NpcDirectedRelationshipView migratedRow = migrated.ReadNpcRelationship(evidence.PersonId, targetId);
            Require(migratedRow != null && !migratedRow.HasResentmentRecoveryAnchor &&
                migratedRow.ResentmentRecoveryAnchorGameTime == -1 &&
                migratedRow.ResentmentRecoveryHalfLifeSeconds == 0 &&
                migratedRow.ResentmentRecoveryBaselineRemainder == 0 &&
                migratedRow.Resentment == legacyRow.Resentment &&
                migratedRow.HasFearRecoveryAnchor == legacyRow.HasFearRecoveryAnchor &&
                migratedRow.FearRecoveryAnchorGameTime == legacyRow.FearRecoveryAnchorGameTime &&
                migratedRow.FearRecoveryHalfLifeSeconds == legacyRow.FearRecoveryHalfLifeSeconds &&
                migratedRow.FearRecoveryBaselineRemainder == legacyRow.FearRecoveryBaselineRemainder &&
                migratedRow.TrustKnown == legacyRow.TrustKnown &&
                migratedRow.ReliabilityTrust == legacyRow.ReliabilityTrust,
                "Schema-41 migration invented resentment age or changed fear/trust.");
            evidence.Schema41Migration = true;

            Require(liveRelationshipJsonBefore == SaveRelationships(liveStore.Snapshot()),
                "Detached resentment acceptance changed live fixture relationship state.");
        }

        private static bool Commit(WorldStateStore store, string personId, string targetId,
            string caseId, double eventTime, int resentmentDelta, int fearDelta)
        {
            string unique = caseId + ":" + Guid.NewGuid().ToString("N");
            var evidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                "controlled-receipt:" + unique, "controlled-episode:" + unique,
                "controlled-outcome:" + unique, NpcExperienceEventKind.Injury, eventTime,
                NpcResponsibleActorAttribution.Known, targetId);
            NpcDirectedRelationshipView current = store.ReadNpcRelationship(personId, targetId);
            var relationshipUpdate = new NpcRelationshipUpdateProposal
            {
                ExpectedRevision = current == null ? 0 : current.Revision,
                Evidence = evidence,
                ResentmentDelta = resentmentDelta,
                InterpersonalFearDelta = fearDelta,
                FearSeverity = NpcFearSeverity.Moderate
            };
            var proposal = new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = store.NpcRelationshipLedgerRevision,
                ObserverId = personId,
                TargetId = targetId,
                RelationshipUpdate = relationshipUpdate
            };
            return store.TryCommitNpcRelationship(proposal, store.NpcRelationshipLedgerRevision);
        }

        private static void RemoveRelationshipFixture(WorldState world, string personId)
        {
            world.NpcRelationships.Relationships.RemoveAll(row => row != null && row.ObserverId == personId &&
                row.TargetId == NpcDirectedRelationship.PlayerTargetId);
            world.NpcRelationships.AcceptedOutcomes.RemoveAll(outcome => outcome != null && outcome.ObserverId == personId &&
                outcome.TargetId == NpcDirectedRelationship.PlayerTargetId);
            world.NpcRelationships.Revision = world.NpcRelationships.AcceptedOutcomes.Count;
        }

        private static NpcDirectedRelationship FindRelationship(WorldState world, string personId, string targetId)
        {
            return world.NpcRelationships.Relationships.Find(row => row != null &&
                row.ObserverId == personId && row.TargetId == targetId);
        }

        private static void VerifyControlledReceipts(WorldState world, string personId, string targetId)
        {
            var outcomes = new List<NpcAcceptedRelationshipOutcome>();
            foreach (NpcAcceptedRelationshipOutcome outcome in world.NpcRelationships.AcceptedOutcomes)
                if (outcome != null && outcome.ObserverId == personId && outcome.TargetId == targetId)
                    outcomes.Add(outcome);
            Require(outcomes.Count == 6 && outcomes[0].ResentmentDelta == 10 && outcomes[0].FearDelta == 0 &&
                outcomes[1].ResentmentDelta == 10 && outcomes[1].FearDelta == 0 &&
                outcomes[2].ResentmentDelta == 10 && outcomes[2].FearDelta == 0 &&
                outcomes[3].ResentmentDelta == 10 && outcomes[3].FearDelta == 0 &&
                outcomes[4].ResentmentDelta == 0 && outcomes[4].FearDelta == 10 &&
                outcomes[5].ResentmentDelta == -1 && outcomes[5].FearDelta == 0,
                "Accepted ledger does not contain exactly four bounded resentment receipts, one fear receipt, and one reduction receipt.");
            foreach (NpcAcceptedRelationshipOutcome outcome in outcomes)
                Require(outcome.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                    outcome.EventKind == NpcExperienceEventKind.Injury &&
                    outcome.SubjectPersonId == personId && outcome.AttributedSpeakerId == null &&
                    !string.IsNullOrEmpty(outcome.ReceiptId) &&
                    !string.IsNullOrEmpty(outcome.EpisodeId) && !string.IsNullOrEmpty(outcome.OutcomeId) &&
                    !string.IsNullOrEmpty(outcome.DeduplicationKey),
                    "Controlled relationship receipt lost canonical source-bound outcome provenance.");
        }

        private static string SaveRelationships(WorldState world)
        { return SaveLoadManager.Serialize(typeof(NpcRelationshipLedger), world.NpcRelationships); }

        private static bool SameRecovery(NpcDirectedRelationshipView left, NpcDirectedRelationshipView right)
        {
            return left != null && right != null && left.Revision == right.Revision &&
                SameSerializedTime(left.LastGameTime, right.LastGameTime) && left.Resentment == right.Resentment &&
                left.StoredResentment == right.StoredResentment && left.ProjectedResentment == right.ProjectedResentment &&
                left.HasResentmentRecoveryAnchor == right.HasResentmentRecoveryAnchor &&
                SameSerializedTime(left.ResentmentRecoveryAnchorGameTime, right.ResentmentRecoveryAnchorGameTime) &&
                Math.Abs(left.ResentmentRecoveryHalfLifeSeconds - right.ResentmentRecoveryHalfLifeSeconds) <= 0.000001 &&
                Math.Abs(left.ResentmentRecoveryBaselineRemainder - right.ResentmentRecoveryBaselineRemainder) <= 0.000000000001 &&
                left.InterpersonalFear == right.InterpersonalFear &&
                left.StoredInterpersonalFear == right.StoredInterpersonalFear &&
                left.ProjectedInterpersonalFear == right.ProjectedInterpersonalFear &&
                left.HasFearRecoveryAnchor == right.HasFearRecoveryAnchor &&
                SameSerializedTime(left.FearRecoveryAnchorGameTime, right.FearRecoveryAnchorGameTime) &&
                Math.Abs(left.FearRecoveryHalfLifeSeconds - right.FearRecoveryHalfLifeSeconds) <= 0.000001 &&
                Math.Abs(left.FearRecoveryBaselineRemainder - right.FearRecoveryBaselineRemainder) <= 0.000000000001;
        }

        // Native FullSerializer rounds double timestamps at this game epoch to about 0.0001 seconds.
        // Compare within one game millisecond; integer projections and saved rates remain independently checked.
        private static bool SameSerializedTime(double left, double right)
        { return Math.Abs(left - right) <= 0.001; }

        private static string RecoveryDiagnostics(NpcDirectedRelationshipView left, NpcDirectedRelationshipView right)
        {
            if (left == null || right == null) return "before=" + (left == null ? "null" : "present") +
                "; after=" + (right == null ? "null" : "present");
            return "revision=" + left.Revision + "/" + right.Revision +
                "; lastGameTime=" + Number(left.LastGameTime) + "/" + Number(right.LastGameTime) +
                "; trustKnown=" + left.TrustKnown + "/" + right.TrustKnown +
                "; reliabilityTrust=" + left.ReliabilityTrust + "/" + right.ReliabilityTrust +
                "; resentment=" + left.Resentment + "/" + right.Resentment +
                "; storedResentment=" + left.StoredResentment + "/" + right.StoredResentment +
                "; projectedResentment=" + left.ProjectedResentment + "/" + right.ProjectedResentment +
                "; hasResentmentAnchor=" + left.HasResentmentRecoveryAnchor + "/" + right.HasResentmentRecoveryAnchor +
                "; resentmentAnchorTime=" + Number(left.ResentmentRecoveryAnchorGameTime) + "/" + Number(right.ResentmentRecoveryAnchorGameTime) +
                "; resentmentHalfLife=" + Number(left.ResentmentRecoveryHalfLifeSeconds) + "/" + Number(right.ResentmentRecoveryHalfLifeSeconds) +
                "; resentmentRemainder=" + Number(left.ResentmentRecoveryBaselineRemainder) + "/" + Number(right.ResentmentRecoveryBaselineRemainder) +
                "; interpersonalFear=" + left.InterpersonalFear + "/" + right.InterpersonalFear +
                "; storedFear=" + left.StoredInterpersonalFear + "/" + right.StoredInterpersonalFear +
                "; projectedFear=" + left.ProjectedInterpersonalFear + "/" + right.ProjectedInterpersonalFear +
                "; hasFearAnchor=" + left.HasFearRecoveryAnchor + "/" + right.HasFearRecoveryAnchor +
                "; fearAnchorTime=" + Number(left.FearRecoveryAnchorGameTime) + "/" + Number(right.FearRecoveryAnchorGameTime) +
                "; fearHalfLife=" + Number(left.FearRecoveryHalfLifeSeconds) + "/" + Number(right.FearRecoveryHalfLifeSeconds) +
                "; fearRemainder=" + Number(left.FearRecoveryBaselineRemainder) + "/" + Number(right.FearRecoveryBaselineRemainder);
        }

        private static string Number(double value)
        { return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Required resentment probe field is missing: " + name);
            return field;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
