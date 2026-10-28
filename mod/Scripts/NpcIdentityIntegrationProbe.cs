using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Opt-in native integration probe for event-only personality persistence and post-action
    /// conversation snapshots. All mutations to the live save are rolled back by reloading the
    /// exact owned fixture save in the iterator cleanup path.
    /// </summary>
    internal static class NpcIdentityIntegrationProbe
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic;
        private const string RunFlag = "-peopleoftheiliacbay-npc-identity-integration-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";
        private const string ControlledFixtureSource = "controlled-detached-pre-conversation-native-health-fixture";

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0,
                "NPC identity integration probe requires its explicit opt-in flag.");
            Require(OwnedCharacter == character && BaselineSave == fixtureSave,
                "NPC identity integration probe requires the owned disposable character and exact baseline save.");
            Require(host && streets && GameManager.Instance && GameManager.Instance.IsReady && streets.Ready,
                "NPC identity integration probe requires the ready owned native fixture.");

            Exception probeFailure = null;
            Exception restoreFailure = null;
            IEnumerator cases = MemoryArchitectureProbe.DriveEnumeratorTree(RunCases(host, streets, character));
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

            IEnumerator restore = MemoryArchitectureProbe.DriveEnumeratorTree(
                ConversationMoodProbe.LoadSave(host, streets, character, BaselineSave, false));
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
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_NPC_IDENTITY_BASELINE_RESTORE_FAIL: " + restoreFailure);
                throw new InvalidOperationException("NPC identity probe could not restore the exact baseline save.", restoreFailure);
            }
            if (probeFailure != null)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NPC_IDENTITY_BASELINE_RESTORED_AFTER_FAILURE: true.");
                throw new InvalidOperationException("NPC identity integration probe failed after exact baseline restore.", probeFailure);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_IDENTITY_BASELINE_RESTORED: character=" + character +
                "; save=" + BaselineSave + "; pending events, profiles, commitments and action state reverted=true.");
        }

        private static IEnumerator RunCases(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character)
        {
            GameManager game = GameManager.Instance;
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(character, BaselineSave) >= 0,
                "Exact NPC identity baseline fixture is missing; refusing probe.");

            WorldStateStore store = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
            Require(store != null, "NPC identity probe cannot read the loaded native world store.");
            Require(!game.PlayerEnterExit.IsPlayerInsideBuilding,
                "NPC identity probe requires the exterior fixture scene to avoid changing building state.");
            VerifyNativeFactionBackgroundSentinels();

            ResidentActor profileActor = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host, game.PlayerMotor.transform.position,
                selectedActor => profileActor = selectedActor, requireNeutral: false);
            store = ConversationMoodProbe.ReadHostField(host, "store") as WorldStateStore;
            Require(store != null && profileActor, "Resident actor/store disappeared after bounded startup wait.");
            LogNativeStatusCapability(profileActor);
            VerifyEventOnlyObserverCreatesPersistedThreeTraitProfile(host, store, profileActor);
            PersonalityV3NativeProbe.Verify(store, profileActor);
            // This detached-store fixture restores the exact native player currency in its own finally.
            // The enclosing owned-save probe additionally reloads the baseline even when an assertion fails.
            NpcFrameworkPersistenceProbe.Verify();

            // Exercise snapshot-null legacy prompt helpers against an actual native
            // prefab clone that has no ResidentActor or BuildingDirectory owner.
            // This does not submit a dialogue request or persist the clone.
            IEnumerator weatherProbe = LegacyWeatherContextProbe.Run(host);
            try
            {
                while (true)
                {
                    bool more = weatherProbe.MoveNext();
                    if (!more) break;
                    yield return weatherProbe.Current;
                }
            }
            finally
            {
                IDisposable disposable = weatherProbe as IDisposable;
                if (disposable != null) disposable.Dispose();
            }

            ResidentActor actor = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host, game.PlayerMotor.transform.position,
                value => actor = value, requireNeutral: false);
            Require(actor && actor.Resident != null, "No eligible managed adult resident is available for the action-result snapshot probe.");
            ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
            string personId = actor.Resident.Id;
            DaggerfallTalkWindow talk = null;
            yield return ConversationMoodProbe.OpenTalk(host, game, actor, value => talk = value);
            Require(talk != null, "Native Talk did not open for the selected action-result resident.");

            const string playerText = "Wait here for a moment.";
            ConversationContextSnapshot before = ConversationMoodProbe.CaptureSnapshot(host, talk, playerText)
                as ConversationContextSnapshot;
            Require(before != null && before.Binding != null && before.Binding.PersonId == personId,
                "Pre-action context snapshot is not bound to the selected native resident.");
            string sessionId = before.Binding.SessionId;
            long beforeRequestRevision = before.Binding.RequestRevision;
            double beforeCapturedAt = before.CapturedGameTime;

            string taskId;
            AcceptNativeWaitTask(streets, store, personId, out taskId);
            NpcTaskPlan task = store.ActiveNpcTask(personId);
            Require(task != null && task.Id == taskId,
                "Controlled task acceptance did not create the expected active native commitment.");

            MethodInfo captureResult = RequireMethod(typeof(PeopleOfTheIliacBayMod), "CaptureActionResultContext", 4);
            var fresh = captureResult.Invoke(host, new object[] { talk, playerText, before, true }) as ConversationContextSnapshot;
            Require(fresh != null, "Post-action result did not receive a fresh authoritative conversation snapshot.");
            Require(fresh.Binding.PersonId == personId && fresh.Binding.SessionId == sessionId &&
                fresh.Binding.RequestRevision > beforeRequestRevision && fresh.CapturedGameTime >= beforeCapturedAt,
                "Post-action snapshot changed actor/session, failed to advance request revision, or moved game time backwards.");
            Require(ReferenceEquals(fresh, ConversationMoodProbe.ReadHostField(host, "conversationContextCurrentSnapshot")),
                "Post-action snapshot was not installed as the current production conversation snapshot.");

            VerifyMoodAndRelationshipProjection(store, fresh, personId);
            VerifyTaskCommitmentProjection(store, fresh, task);

            var stale = captureResult.Invoke(host, new object[] { talk, playerText, before, false });
            Require(stale == null, "A superseded pre-action callback snapshot was accepted after native task mutation.");

            yield return ConversationMoodProbe.CloseTalk(host);
            ResidentActor foreignActor = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host, game.PlayerMotor.transform.position,
                value => foreignActor = value, requireNeutral: false, excludedPersonId: personId);
            Require(foreignActor && foreignActor.Resident.Id != personId,
                "A second distinct resident is required to test rejection of a foreign action callback.");
            ConversationMoodProbe.StagePlayerNearActor(streets, foreignActor, game);
            DaggerfallTalkWindow foreignTalk = null;
            yield return ConversationMoodProbe.OpenTalk(host, game, foreignActor, value => foreignTalk = value);
            ConversationContextSnapshot foreignCurrent = ConversationMoodProbe.CaptureSnapshot(host, foreignTalk,
                "Hello there.") as ConversationContextSnapshot;
            Require(foreignCurrent != null && foreignCurrent.Binding.PersonId == foreignActor.Resident.Id,
                "Second conversation did not bind to its different native resident.");
            var crossed = captureResult.Invoke(host, new object[] { foreignTalk, "A result from the first resident.", fresh, false });
            Require(crossed == null, "A callback snapshot bound to another resident/session was accepted.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_IDENTITY_ACTION_SNAPSHOT_PASS: npc=" + personId +
                "; task=" + task.Id + "; request-revision=" + beforeRequestRevision + "->" + fresh.Binding.RequestRevision +
                "; stale-rejected=true; foreign-session-rejected=true; mood/relationship/commitment projection=typed-store-equality.");

            yield return ConversationMoodProbe.CloseTalk(host);
        }

        private static void VerifyEventOnlyObserverCreatesPersistedThreeTraitProfile(
            PeopleOfTheIliacBayMod host, WorldStateStore liveStore, ResidentActor selected)
        {
            WorldState originalState = liveStore.Snapshot();
            Require(selected && selected.gameObject.activeInHierarchy && selected.Resident != null &&
                selected.Resident.IsAlive && !selected.Resident.IsQuestProtected,
                "Bounded startup wait returned an ineligible resident for the controlled detached fixture.");

            string personId = selected.Resident.Id;
            NpcPersonalityProfile originalLiveProfile = liveStore.ReadNpcPersonality(personId);
            uint identitySeed;
            int nativeFaction;
            bool questProtected;
            Require(liveStore.TryReadNpcPersonalityIdentity(personId, out identitySeed, out nativeFaction, out questProtected) &&
                !questProtected, "Selected native resident lost stable saved identity eligibility.");

            // Active residents may already have both a saved personality and vitals. Build an
            // explicit pre-conversation state in this detached snapshot; never alter the live save.
            WorldState detachedSetup = CreateDetachedPreConversationFixture(originalState, personId);
            var isolated = new WorldStateStore(originalState.WorldSeed);
            isolated.Restore(detachedSetup);
            Require(isolated.ReadNpcPersonality(personId) == null,
                "Detached event-only fixture unexpectedly began with a saved personality.");
            NpcActorProgress existingProgress = originalState.NpcActors.FirstOrDefault(row => row != null && row.PersonId == personId);
            NpcArchetype fixtureArchetype = existingProgress == null ? NpcArchetype.Citizen : existingProgress.Archetype;
            NpcVitalsView baseline = isolated.ReadNpcVitals(personId, fixtureArchetype);
            Require(!baseline.IsPersisted && isolated.ReadPendingNpcExperiences(personId, 10).Count == 0,
                "Detached event-only fixture did not clear prior observer vitals and experience history.");
            double capturedAt = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            int initialHealth = Math.Max(2, baseline.MaximumHealth - 2);
            Require(isolated.TryCaptureNpcVitals(personId, fixtureArchetype, initialHealth,
                baseline.MaximumMagicka, capturedAt, baseline.Revision),
                "Controlled native health fixture could not establish its native health baseline.");
            NpcVitalsView full = isolated.ReadNpcVitals(personId, fixtureArchetype);
            Require(full.IsPersisted && full.CurrentHealth > 1 && isolated.TryCaptureNpcVitals(personId, fixtureArchetype,
                full.CurrentHealth - 1, full.CurrentMagicka, capturedAt + 1, full.Revision),
                "Controlled native health fixture could not record its typed health change.");
            double flushAt = capturedAt + 61;
            List<NpcHealthEpisodeSummary> flushed = isolated.FlushNpcHealthExperiences(flushAt, 60, 10);
            Require(flushed.Count == 1 && flushed[0].ObserverId == personId && flushed[0].DamageTotal == 1,
                "Native health episode producer did not emit the controlled observer-owned event.");
            List<NpcExperienceEvent> events = isolated.ReadPendingNpcExperiences(personId, 10);
            Require(events.Count == 1 && events[0].Origin == NpcExperienceEventOrigin.NativeOutcome &&
                events[0].EventKind == NpcExperienceEventKind.Injury &&
                events[0].ResponsibleActorAttribution == NpcResponsibleActorAttribution.Unknown,
                "Event-only fixture must remain native injury evidence with unknown responsible actor.");

            NpcSocialBackground? resolvedBackground = ResolveBackground(host, nativeFaction);
            NpcExperienceAppraisalTicket ticket;
            Require(isolated.TryCaptureNpcExperienceAppraisal(personId, flushAt, resolvedBackground, out ticket) &&
                ticket != null && ticket.Request.ObserverId == personId && ticket.Request.Personality.Version == 3 &&
                ticket.Request.Personality.SelectedTraits.Count == 3,
                "Event-only appraisal capture did not create the stable three-trait personality for the saved observer.");
            RequireLivePersonalityUnchanged(liveStore, personId, originalLiveProfile,
                "Event appraisal capture changed the live personality profile.");

            WorldState saved = isolated.Snapshot();
            string serialized = SaveLoadManager.Serialize(typeof(WorldState), saved);
            Require(!string.IsNullOrEmpty(serialized), "Native FullSerializer returned empty WorldState JSON.");
            WorldState decoded = SaveLoadManager.Deserialize(typeof(WorldState), serialized, true) as WorldState;
            Require(decoded != null, "Native FullSerializer did not deserialize the detached WorldState JSON.");
            var restored = new WorldStateStore(saved.WorldSeed);
            restored.Restore(decoded);
            NpcPersonalityProfile profile = restored.ReadNpcPersonality(personId);
            Require(profile != null && profile.Version == 3 && profile.SelectedTraits.Count == 3 &&
                profile.PersonId == personId && profile.Seed == identitySeed,
                "Save-state round trip did not retain the event-created three-trait profile identity.");
            NpcExperienceEvent restoredEvent = restored.ReadPendingNpcExperiences(personId, 10).SingleOrDefault();
            Require(restoredEvent != null && restoredEvent.DeduplicationKey == events[0].DeduplicationKey &&
                restoredEvent.Origin == NpcExperienceEventOrigin.NativeOutcome,
                "Save-state round trip did not retain the pending native event paired with the profile.");
            foreach (KeyValuePair<string, string> trait in ticket.Request.Personality.SelectedTraits)
                Require(profile.SelectedTraits[trait.Key] == trait.Value,
                    "Saved profile changed stable trait axis " + trait.Key + ".");
            RequireLivePersonalityUnchanged(liveStore, personId, originalLiveProfile,
                "Detached native-serializer fixture changed the live personality profile.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_EVENT_ONLY_PROFILE_PASS: npc=" + personId +
                "; fixtureSource=" + ControlledFixtureSource +
                "; detachedSetup=live-profile-vitals-and-observer-events-cleared" +
                "; originalLiveProfile=" + (originalLiveProfile == null ? "absent" : "preserved") +
                "; event=NativeOutcome/Injury/unknown-attribution" +
                "; profileTraits=" + profile.SelectedTraits.Count +
                "; native-FullSerializer-memory-roundtrip=true; disk-save=false; live-save-unchanged=true.");
        }

        private static void LogNativeStatusCapability(ResidentActor selected)
        {
            Require(selected && selected.gameObject.activeInHierarchy && selected.Resident != null &&
                !string.IsNullOrWhiteSpace(selected.Resident.Id),
                "Native status capability diagnostic requires an active managed resident with stable identity.");

            DaggerfallEntityBehaviour host = selected.GetComponent<DaggerfallEntityBehaviour>();
            Require(host && host.gameObject == selected.gameObject && host.Entity != null &&
                ReferenceEquals(host.Entity.EntityBehaviour, host),
                "Selected resident's native entity is not bound back to its DaggerfallEntityBehaviour host.");

            EntityEffectManager effects = selected.GetComponent<EntityEffectManager>();
            if (effects)
                Require(effects.gameObject == host.gameObject,
                    "Resident EntityEffectManager is attached to a different host than the native entity.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_STATUS_CAPABILITY: person=" + selected.Resident.Id +
                "; entityType=" + host.EntityType +
                "; entityClass=" + host.Entity.GetType().FullName +
                "; entityHostConsistent=true" +
                "; effectManagerPresent=" + (effects ? "true" : "false") +
                "; effectCount=" + (effects ? effects.EffectCount.ToString(CultureInfo.InvariantCulture) : "unavailable") +
                "; statusApplication=notAttempted.");
        }

        private static WorldState CreateDetachedPreConversationFixture(WorldState source, string personId)
        {
            var copier = new WorldStateStore(source.WorldSeed);
            copier.Restore(source);
            WorldState detached = copier.Snapshot();

            NpcActorProgress progress = detached.NpcActors.FirstOrDefault(row => row != null && row.PersonId == personId);
            if (progress != null)
            {
                if (progress.Skills == null || progress.Skills.Count == 0)
                    detached.NpcActors.Remove(progress);
                else
                {
                    progress.VitalsInitialized = false;
                    progress.CurrentHealth = 0;
                    progress.CurrentMagicka = 0;
                    progress.VitalsCapturedAt = 0;
                    progress.VitalsRevision = 0;
                    progress.HasPendingHealthEpisode = false;
                    progress.HealthEpisodeStartedAt = 0;
                    progress.HealthEpisodeLastChangedAt = 0;
                    progress.HealthEpisodeInitialHealth = 0;
                    progress.HealthEpisodeFinalHealth = 0;
                    progress.HealthEpisodeMaximumHealth = 0;
                    progress.HealthEpisodeDamageTotal = 0;
                    progress.HealthEpisodeHealingTotal = 0;
                    progress.HealthEpisodeChangeCount = 0;
                    progress.HealthEpisodeFirstVitalsRevision = 0;
                    progress.HealthEpisodeLastVitalsRevision = 0;
                }
            }

            detached.NpcPersonalities.RemoveAll(profile => profile != null && profile.PersonId == personId);

            if (detached.NpcExperienceInbox == null || detached.NpcExperienceInbox.Entries == null)
                throw new InvalidOperationException("Detached event-only fixture has no valid experience inbox.");
            // Controlled pre-conversation reset, not a retention acceptance:
            // keep v5 absolute enqueue ordinals and account for removed fixture detail.
            int removed = detached.NpcExperienceInbox.Entries.RemoveAll(entry =>
                entry != null && entry.ObserverId == personId);
            if (removed > 0)
            {
                detached.NpcExperienceInbox.RetiredCount += removed;
                detached.NpcExperienceInbox.DetailRevision++;
            }
            return detached;
        }

        private static void RequireLivePersonalityUnchanged(WorldStateStore liveStore, string personId,
            NpcPersonalityProfile before, string failure)
        {
            NpcPersonalityProfile after = liveStore.ReadNpcPersonality(personId);
            Require((before == null) == (after == null), failure + " Profile presence changed.");
            if (before == null) return;
            Require(before.PersonId == after.PersonId && before.Version == after.Version &&
                before.Seed == after.Seed && before.Background == after.Background &&
                before.SelectedTraits.Count == after.SelectedTraits.Count,
                failure + " Profile identity or metadata changed.");
            foreach (KeyValuePair<string, string> trait in before.SelectedTraits)
                Require(after.SelectedTraits.ContainsKey(trait.Key) && after.SelectedTraits[trait.Key] == trait.Value,
                    failure + " Trait changed: " + trait.Key + ".");
        }

        private static NpcSocialBackground? ResolveBackground(PeopleOfTheIliacBayMod host, int factionId)
        {
            if (factionId < 0) return null;
            MethodInfo method = typeof(PeopleOfTheIliacBayMod).GetMethod("TryResolveNpcPersonalityBackground", Members);
            Require(method != null && method.IsStatic, "Native personality-background resolver is unavailable.");
            object[] args = { factionId, NpcSocialBackground.Unknown };
            bool resolved = Convert.ToBoolean(method.Invoke(null, args), CultureInfo.InvariantCulture);
            Require(resolved && args[1] is NpcSocialBackground,
                "Native personality background could not resolve the selected resident faction.");
            return (NpcSocialBackground)args[1];
        }

        private static void VerifyNativeFactionBackgroundSentinels()
        {
            int unresolvedPositiveFaction = 1;
            DaggerfallConnect.Arena2.FactionFile.FactionData faction;
            while (unresolvedPositiveFaction < 100000 &&
                GameManager.Instance.PlayerEntity.FactionData.GetFactionData(unresolvedPositiveFaction, out faction))
                unresolvedPositiveFaction++;
            Require(unresolvedPositiveFaction > 0 && unresolvedPositiveFaction < 100000 &&
                !GameManager.Instance.PlayerEntity.FactionData.GetFactionData(unresolvedPositiveFaction, out faction),
                "Could not select a positive faction ID absent from the active native faction table.");

            bool zeroResolved;
            NpcSocialBackground zeroBackground;
            bool negativeResolved;
            NpcSocialBackground negativeBackground;
            bool positiveResolved;
            NpcSocialBackground positiveBackground;
            InvokeBackgroundResolver(0, out zeroResolved, out zeroBackground);
            InvokeBackgroundResolver(-1, out negativeResolved, out negativeBackground);
            InvokeBackgroundResolver(unresolvedPositiveFaction, out positiveResolved, out positiveBackground);

            Require(zeroResolved && zeroBackground == NpcSocialBackground.Unknown,
                "Native faction ID 0 must resolve as the no-faction Unknown background.");
            Require(negativeResolved && negativeBackground == NpcSocialBackground.Unknown,
                "Negative no-faction identity must continue to resolve as Unknown.");
            Require(!positiveResolved,
                "An unresolved positive faction ID must remain deferred instead of becoming Unknown.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_IDENTITY_FACTION_SENTINELS: faction0=Unknown; negative=Unknown; unresolvedPositive=" +
                unresolvedPositiveFaction.ToString(CultureInfo.InvariantCulture) + ":deferred.");
        }

        private static void InvokeBackgroundResolver(int factionId, out bool resolved, out NpcSocialBackground background)
        {
            MethodInfo method = typeof(PeopleOfTheIliacBayMod).GetMethod("TryResolveNpcPersonalityBackground", Members);
            Require(method != null && method.IsStatic, "Native personality-background resolver is unavailable.");
            object[] args = { factionId, NpcSocialBackground.Unknown };
            resolved = Convert.ToBoolean(method.Invoke(null, args), CultureInfo.InvariantCulture);
            Require(args[1] is NpcSocialBackground, "Native personality-background resolver returned an invalid enum value.");
            background = (NpcSocialBackground)args[1];
        }

        private static void AcceptNativeWaitTask(StreetPopulation streets, WorldStateStore store,
            string personId, out string taskId)
        {
            taskId = null;
            string reason = "No compatible native wait/follow options.";
            NpcActionOption wait = null;
            NpcActionOption follow = null;
            foreach (NpcActionOption option in streets.ConversationActionOptions(personId, "Wait here."))
                if (option != null && option.Kind == NpcActionKind.Wait) { wait = option; break; }
            foreach (NpcActionOption option in streets.ConversationActionOptions(personId, "Follow me."))
                if (option != null && option.Kind == NpcActionKind.Follow) { follow = option; break; }
            Require(wait != null && follow != null,
                "Native action adapter did not expose the controlled wait/follow task options.");
            string proposal = "npc-identity-probe:" + Guid.NewGuid().ToString("N");
            bool accepted = NpcProbeCommandSource.TryAcceptTask(streets,personId, proposal,
                new List<NpcActionDecision>
                {
                    new NpcActionDecision { Option = wait, DurationSeconds = 300 },
                    new NpcActionDecision { Option = follow, DurationSeconds = 10 }
                }, out reason);
            NpcTaskPlan task = accepted ? store.ActiveNpcTask(personId) : null;
            Require(accepted && task != null,
                "Controlled native task mutation was rejected: " + reason);
            taskId = task.Id;
        }

        private static void VerifyMoodAndRelationshipProjection(WorldStateStore store,
            ConversationContextSnapshot snapshot, string personId)
        {
            ConversationMoodView mood = store.CaptureConversationMood(personId, snapshot.CapturedGameTime);
            Require(snapshot.PersistentMood != null && snapshot.PersistentMood.Value == mood.Value &&
                snapshot.PersistentMood.AcceptedTurnSequence == mood.AcceptedTurnSequence &&
                snapshot.PersistentMood.StateRevision == mood.Revision,
                "Post-action snapshot did not carry the current typed persistent mood values and revision.");

            NpcDirectedRelationshipView expected = store.ReadNpcRelationship(personId,
                NpcDirectedRelationship.PlayerTargetId, snapshot.CapturedGameTime);
            NpcDirectedRelationshipView actual = snapshot.DirectedRelationship;
            if (expected == null)
            {
                Require(actual == null, "Snapshot invented a directed relationship absent from the native store.");
                return;
            }
            Require(actual != null && actual.ObserverId == expected.ObserverId && actual.TargetId == expected.TargetId &&
                actual.Revision == expected.Revision && actual.LastGameTime == expected.LastGameTime &&
                actual.TrustKnown == expected.TrustKnown && actual.ReliabilityTrust == expected.ReliabilityTrust &&
                actual.TrustConfidence == expected.TrustConfidence && actual.TrustEvidenceCount == expected.TrustEvidenceCount &&
                actual.Resentment == expected.Resentment && actual.InterpersonalFear == expected.InterpersonalFear &&
                actual.ProjectedInterpersonalFear == expected.ProjectedInterpersonalFear &&
                actual.HasFearRecoveryAnchor == expected.HasFearRecoveryAnchor &&
                actual.FearRecoveryAnchorGameTime == expected.FearRecoveryAnchorGameTime &&
                actual.FearRecoveryHalfLifeSeconds == expected.FearRecoveryHalfLifeSeconds,
                "Post-action snapshot did not preserve the complete directed relationship state from the native store.");
        }

        private static void VerifyTaskCommitmentProjection(WorldStateStore store,
            ConversationContextSnapshot snapshot, NpcTaskPlan task)
        {
            NpcTaskPlan current = store.ActiveNpcTask(task.ResidentId);
            Require(current != null && current.Id == task.Id,
                "Accepted native task disappeared before snapshot projection verification.");
            ConversationContextCommitment projection = snapshot.Commitments.SingleOrDefault(value =>
                value.Id == current.Id && value.Kind == "task");
            Require(projection != null && projection.Status == current.Phase.ToString().ToLowerInvariant() &&
                projection.Revision == current.Revision && projection.StartsAtGameTime == current.AcceptedAt &&
                projection.EndsAtGameTime == current.DeadlineAt,
                "Fresh snapshot did not carry the accepted task ID, phase, revision and native timing.");
        }

        private static MethodInfo RequireMethod(Type type, string name, int parameterCount)
        {
            MethodInfo method = type.GetMethods(Members).FirstOrDefault(candidate => candidate.Name == name &&
                candidate.GetParameters().Length == parameterCount);
            Require(method != null, "Required native integration method is missing: " + type.Name + "." + name + ".");
            return method;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
