using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Opt-in acceptance for the production resident poison-effect application observer. It mutates only an existing,
    /// eligible resident in the exact disposable character and restores that character's exact baseline save.
    /// It does not apply effects to the player or claim that resident effect bundles survive actor streaming.
    /// </summary>
    public static class NativeNpcPoisonExperienceProbe
    {
        public const string Flag = "-peopleoftheiliacbay-native-poison-experience-probe";
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";
        private const string SavePrefix = "People of the Iliac Bay - Native poison onset ";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class RunEvidence
        {
            internal string ResidentId;
            internal string PoisonKey;
            internal string FirstCheckpoint;
            internal string FinalCheckpoint;
            internal int FirstActorInstance;
            internal int NativeLevel;
            internal int ProjectedAssignments;
        }

        private sealed class NativeStatusRow
        {
            internal string Key;
            internal int StatusKind;
            internal long Sequence;
            internal NpcResponsibleActorAttribution Attribution;
            internal string ResponsibleActorId;
            internal NpcExperienceEventOrigin Origin;
            internal NpcExperienceEventKind Kind;
        }

        private sealed class CandidateDiagnostics
        {
            internal int AdultAliveResidentsSeen;
            internal int NoEffectManager;
            internal int NativeLevelOne;
            internal int MissingPoisonBinding;

            public override string ToString()
            {
                return "adultAliveResidentsSeen=" + AdultAliveResidentsSeen + "; noEffectManager=" + NoEffectManager +
                    "; nativeLevelOne=" + NativeLevelOne + "; missingPoisonBinding=" + MissingPoisonBinding;
            }
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            GameManager game = GameManager.Instance;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave &&
                ArgumentValue("-peopleoftheiliacbay-fixture-character") == OwnedCharacter &&
                ArgumentValue("-peopleoftheiliacbay-fixture-save") == BaselineSave,
                "Native poison probe requires its explicit flag, owned disposable character, and exact baseline save.");
            Require(host && streets && game && game.IsReady && game.PlayerEntity != null &&
                game.PlayerEntity.Name == OwnedCharacter && streets.Ready,
                "Native poison probe requires the ready owned disposable fixture.");

            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, BaselineSave) >= 0,
                "Native poison probe cannot find the exact owned baseline save.");
            Require(!game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.Name == "Gothway Garden",
                "Native poison probe must start at the Gothway Garden exterior baseline.");

            // Establish the known disposable baseline before any actor-side test setup.
            yield return ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, BaselineSave, true);
            Exception probeFailure = null;
            Exception restoreFailure = null;
            Exception checkpointCleanupFailure = null;
            bool testEnumeratorCompleted = false;
            var evidence = new RunEvidence();
            var probe = new NestedEnumeratorDriver(RunCore(host, streets, evidence));
            try
            {
                object yielded;
                while (probe.TryMoveNext(out yielded)) yield return yielded;
                probeFailure = probe.Failure;
                testEnumeratorCompleted = true;
            }
            finally
            {
                probe.Dispose();
                if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                if (!testEnumeratorCompleted)
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_BASELINE_RESTORE_SKIPPED_EXTERNAL_CANCEL: iterator was externally stopped; asynchronous save reload cannot run.");
            }

            if (testEnumeratorCompleted)
            {
                var restore = new NestedEnumeratorDriver(RestoreBaseline(host, streets));
                bool restoreCompleted = false;
                try
                {
                    object yielded;
                    while (restore.TryMoveNext(out yielded)) yield return yielded;
                    restoreFailure = restore.Failure;
                    restoreCompleted = true;
                }
                finally
                {
                    restore.Dispose();
                    if (!restoreCompleted && DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
                }
            }

            try
            {
                DeleteCheckpointIfPresent(GameManager.Instance, evidence.FinalCheckpoint);
                if (!string.IsNullOrEmpty(evidence.FirstCheckpoint) &&
                    !string.Equals(evidence.FirstCheckpoint, evidence.FinalCheckpoint, StringComparison.Ordinal))
                    DeleteCheckpointIfPresent(GameManager.Instance, evidence.FirstCheckpoint);
            }
            catch (Exception error) { checkpointCleanupFailure = error; }

            if (restoreFailure == null && testEnumeratorCompleted)
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_BASELINE_RESTORED: character=" + OwnedCharacter +
                    "; save=" + BaselineSave + "; resident=" + (evidence.ResidentId ?? "unselected") + ".");
            }
            if (probeFailure == null && restoreFailure == null && checkpointCleanupFailure == null && testEnumeratorCompleted)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_PASS: resident=" + evidence.ResidentId +
                    "; nativeLevel=" + evidence.NativeLevel + "; poisonKey=" + evidence.PoisonKey +
                    "; retainedSequence=1.." + evidence.ProjectedAssignments + "; incumbent=ignored; nonPoison=ignored; baseline=notReplayed; " +
                    "streamHistory=retained; diskHistory=retained; staleEpoch=rejected; cause=unknown; " +
                    "assignment=directCreatePoisonAssignBundle; combatInfliction=notTested; " +
                    "evidence=acceptedEffectApplication; symptoms=notTested; playerModified=false");
            var failures = new List<Exception>();
            if (probeFailure != null) failures.Add(probeFailure);
            if (restoreFailure != null) failures.Add(restoreFailure);
            if (checkpointCleanupFailure != null) failures.Add(checkpointCleanupFailure);
            if (failures.Count > 1) throw new AggregateException("Native poison probe and/or cleanup failed.", failures);
            if (probeFailure != null) throw probeFailure;
            if (restoreFailure != null) throw restoreFailure;
            if (checkpointCleanupFailure != null) throw checkpointCleanupFailure;
        }

        private static IEnumerator RunCore(PeopleOfTheIliacBayMod host, StreetPopulation streets, RunEvidence evidence)
        {
            GameManager game = GameManager.Instance;
            WorldStateStore store = ReadStore(host);
            Require(store != null, "Native poison probe cannot access the loaded fixture store.");

            ResidentActor actor = null;
            var excludedResidentsWithStatus = new HashSet<string>(StringComparer.Ordinal);
            float deadline = Time.realtimeSinceStartup + 120f;
            while (Time.realtimeSinceStartup < deadline)
            {
                store = ReadStore(host);
                actor = FindEligibleActor(streets, excludedResidentsWithStatus);
                if (actor)
                {
                    if (StatusRows(store, actor.Resident.Id).Count == 0) break;
                    excludedResidentsWithStatus.Add(actor.Resident.Id);
                    actor = null;
                }
                yield return null;
            }
            if (!actor)
            {
                CandidateDiagnostics diagnostics = CountCandidates(streets);
                throw new InvalidOperationException("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_UNAVAILABLE: no naturally materialized living, unprotected adult with positive native health, an effect manager, production poison binding, no active poison, and no prior status-onset rows was found; no actor was created. " + diagnostics + ".");
            }

            DaggerfallEntityBehaviour behavior = actor.GetComponent<DaggerfallEntityBehaviour>();
            EntityEffectManager manager = actor.GetComponent<EntityEffectManager>();
            Require(behavior && behavior.Entity != null && manager && manager.gameObject == actor.gameObject &&
                behavior.Entity.CurrentHealth > 0 && behavior.Entity != game.PlayerEntity,
                "Selected native poison subject lost its resident binding, positive-health, or player-separation precondition.");
            Require(actor.Resident != null && actor.Resident.IsAlive && actor.Resident.AgeGroup == ResidentAgeGroup.Adult &&
                !actor.Resident.IsQuestProtected && GetComponent<NativeNpcPoisonOnsetBinding>(actor),
                "Selected resident is no longer eligible or lacks the production poison observer.");

            evidence.ResidentId = actor.Resident.Id;
            evidence.FirstActorInstance = actor.GetInstanceID();
            evidence.NativeLevel = behavior.Entity.Level;
            List<NativeStatusRow> initialRows = StatusRows(store, evidence.ResidentId);
            Require(initialRows.Count == 0,
                "PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_UNAVAILABLE: selected resident already has status-onset history; probe requires a clean sequence baseline.");
            Require(NoActivePoison(manager), "Selected resident acquired a poison before the controlled assignment.");

            EntityEffectBundle firstBundle = manager.CreatePoison(Poisons.Nux_Vomica);
            Require(firstBundle != null, "DFU could not create the controlled classic Nux Vomica poison bundle.");
            manager.AssignBundle(firstBundle, AssignBundleFlags.BypassSavingThrows | AssignBundleFlags.BypassChance);
            LiveEffectBundle activePoison = FindActivePoisonBundle(manager, null, out evidence.PoisonKey);
            Require(activePoison != null && !string.IsNullOrWhiteSpace(evidence.PoisonKey),
                "DFU did not retain a live controlled classic poison bundle on the eligible resident.");
            evidence.ProjectedAssignments = 1;
            AssertStatusRows(ReadStore(host), evidence.ResidentId, evidence.ProjectedAssignments, evidence.PoisonKey);
            AssertPoisonSourceState(ReadStore(host), evidence.ResidentId, evidence.ProjectedAssignments);

            // Re-enable the production component while poison remains active. Its bind-time baseline must not
            // replay the already active poison; the duplicate incumbent callback must also remain unobserved.
            NativeNpcPoisonOnsetBinding binding = GetComponent<NativeNpcPoisonOnsetBinding>(actor);
            binding.enabled = false;
            yield return null;
            binding.enabled = true;
            yield return null;
            Require(IsExactActive(manager, activePoison), "Controlled poison disappeared during observer baseline check.");
            EntityEffectBundle duplicatePoison = manager.CreatePoison(Poisons.Nux_Vomica);
            Require(duplicatePoison != null, "DFU could not recreate the duplicate classic poison bundle.");
            manager.AssignBundle(duplicatePoison,
                AssignBundleFlags.BypassSavingThrows | AssignBundleFlags.BypassChance);
            LiveEffectBundle distinctDuplicate = FindActivePoisonBundle(manager, activePoison, out string duplicateKey);
            if (distinctDuplicate != null)
            {
                Require(!ReferenceEquals(distinctDuplicate, activePoison) &&
                    string.Equals(duplicateKey, evidence.PoisonKey, StringComparison.Ordinal),
                    "DFU retained a distinct duplicate-key poison with a different native effect key.");
                evidence.ProjectedAssignments++; // Distinct active bundle is a genuine second assignment.
            }
            AssertStatusRows(ReadStore(host), evidence.ResidentId, evidence.ProjectedAssignments, evidence.PoisonKey);

            manager.RemoveBundle(activePoison);
            Require(!IsExactActive(manager, activePoison), "DFU did not remove the exact retained controlled poison bundle.");
            if (distinctDuplicate != null && IsExactActive(manager, distinctDuplicate))
                manager.RemoveBundle(distinctDuplicate);
            Require(NoActivePoison(manager), "Controlled duplicate poison remained active before real reassignment.");
            EntityEffectBundle nextPoison = manager.CreatePoison(Poisons.Nux_Vomica);
            Require(nextPoison != null, "DFU could not recreate the poison bundle after removal.");
            manager.AssignBundle(nextPoison,
                AssignBundleFlags.BypassSavingThrows | AssignBundleFlags.BypassChance);
            LiveEffectBundle reapplied = FindActivePoisonBundle(manager, activePoison, out string reappliedKey);
            Require(reapplied != null && string.Equals(reappliedKey, evidence.PoisonKey, StringComparison.Ordinal),
                "DFU did not retain the second live controlled poison after removal.");
            evidence.ProjectedAssignments++;
            AssertStatusRows(ReadStore(host), evidence.ResidentId, evidence.ProjectedAssignments, evidence.PoisonKey);

            // Light is a benign, real non-poison effect. Verify DFU retained it so this tests the observer's
            // rejection path rather than a no-op assignment.
            IEntityEffect lightTemplate = game.EntityEffectBroker == null ? null : game.EntityEffectBroker.GetEffectTemplate("Light");
            Require(lightTemplate != null && !(lightTemplate is PoisonEffect),
                "PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_UNAVAILABLE: DFU has no benign Light effect template.");
            List<LiveEffectBundle> priorBundles = new List<LiveEffectBundle>(manager.EffectBundles ?? new LiveEffectBundle[0]);
            EntityEffectBundle lightSource = manager.CreateSpellBundle(lightTemplate.Key);
            Require(lightSource != null, "DFU could not create the benign Light test bundle.");
            manager.AssignBundle(lightSource, AssignBundleFlags.BypassSavingThrows | AssignBundleFlags.BypassChance);
            LiveEffectBundle activeLight = FindRetainedNonPoison(manager, lightTemplate.Key, priorBundles);
            Require(activeLight != null, "DFU did not retain the benign Light effect needed for the non-poison rejection check.");
            AssertStatusRows(ReadStore(host), evidence.ResidentId, evidence.ProjectedAssignments, evidence.PoisonKey);
            manager.RemoveBundle(activeLight);

            // Stream out the actual resident. Its event remains in the Core store, while any actor-attached
            // effect bundle is deliberately not asserted to survive the actor rebuild.
            MethodInfo clearActors = typeof(StreetPopulation).GetMethod("ClearActors", PrivateInstance);
            Require(clearActors != null, "Production resident stream teardown API changed; stream/rebind case is unavailable.");
            clearActors.Invoke(streets, null);
            AssertStatusRows(ReadStore(host), evidence.ResidentId, evidence.ProjectedAssignments, evidence.PoisonKey);
            deadline = Time.realtimeSinceStartup + 150f;
            ResidentActor rebound = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                rebound = streets.FindActor(evidence.ResidentId);
                if (streets.Ready && rebound && rebound.gameObject.activeInHierarchy &&
                    rebound.GetInstanceID() != evidence.FirstActorInstance) break;
                yield return null;
            }
            Require(rebound && rebound.gameObject.activeInHierarchy && rebound.GetInstanceID() != evidence.FirstActorInstance,
                "Production stream teardown did not naturally rebuild the same resident identity.");
            AssertStatusRows(ReadStore(host), evidence.ResidentId, evidence.ProjectedAssignments, evidence.PoisonKey);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_STREAM_PASS: resident=" + evidence.ResidentId +
                "; historicalReceipts=" + evidence.ProjectedAssignments + "; newActor=" + rebound.GetInstanceID() +
                "; activeEffectPersistence=not_asserted; replayCount=0");

            evidence.FirstCheckpoint = SavePrefix + Guid.NewGuid().ToString("N");
            int preLoadActorInstance = rebound.GetInstanceID();
            yield return SaveCheckpoint(game, evidence.FirstCheckpoint);
            yield return ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, evidence.FirstCheckpoint, true);
            store = ReadStore(host);
            AssertStatusRows(store, evidence.ResidentId, evidence.ProjectedAssignments, evidence.PoisonKey);
            AssertPoisonSourceState(store, evidence.ResidentId, evidence.ProjectedAssignments);
            yield return WaitForActor(streets, evidence.ResidentId, preLoadActorInstance);
            actor = streets.FindActor(evidence.ResidentId);
            manager = actor ? actor.GetComponent<EntityEffectManager>() : null;
            Require(actor && actor.gameObject.activeInHierarchy && manager,
                "Reloaded resident did not naturally receive a native effect manager.");

            // If this DFU build did persist the native effect through its own save system, remove that exact
            // test effect before the new assignment. The probe validates historical sequence only.
            RemoveActivePoisonBundles(manager);
            EntityEffectBundle afterReloadPoison = manager.CreatePoison(Poisons.Nux_Vomica);
            Require(afterReloadPoison != null, "DFU could not recreate the classic poison bundle after disk reload.");
            manager.AssignBundle(afterReloadPoison,
                AssignBundleFlags.BypassSavingThrows | AssignBundleFlags.BypassChance);
            LiveEffectBundle finalNativeBundle = FindActivePoisonBundle(manager, null, out string finalKey);
            Require(finalNativeBundle != null && string.Equals(finalKey, evidence.PoisonKey, StringComparison.Ordinal),
                "DFU did not retain the post-reload native poison assignment.");
            evidence.ProjectedAssignments++;
            AssertStatusRows(ReadStore(host), evidence.ResidentId, evidence.ProjectedAssignments, evidence.PoisonKey);

            evidence.FinalCheckpoint = SavePrefix + Guid.NewGuid().ToString("N");
            int preFinalLoadActorInstance = actor.GetInstanceID();
            yield return SaveCheckpoint(GameManager.Instance, evidence.FinalCheckpoint);
            yield return ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, evidence.FinalCheckpoint, true);
            store = ReadStore(host);
            AssertStatusRows(store, evidence.ResidentId, evidence.ProjectedAssignments, evidence.PoisonKey);
            AssertPoisonSourceState(store, evidence.ResidentId, evidence.ProjectedAssignments);
            yield return WaitForActor(streets, evidence.ResidentId, preFinalLoadActorInstance);
            actor = streets.FindActor(evidence.ResidentId);

            VerifyDetachedProjectionPersistence(store.Snapshot(), evidence.ResidentId,
                evidence.PoisonKey, evidence.ProjectedAssignments);
            // Restore advances the epoch. Test rejection only in a detached copy,
            // never invalidating the live actor's native binding before baseline cleanup.
            var detached = new WorldStateStore(store.Snapshot().WorldSeed);
            detached.Restore(store.Snapshot());
            object staleEpoch = detached.CaptureNpcVitalsWriteEpoch();
            detached.Restore(detached.Snapshot());
            long revision = detached.Snapshot().NpcExperienceInbox.Revision;
            NpcPoisonAssignmentTicket staleTicket = detached.TryIssueNpcPoisonAssignment(staleEpoch,
                evidence.ResidentId, actor.GetComponent<NativeNpcPoisonOnsetBinding>(), finalNativeBundle);
            Require(staleTicket == null && detached.Snapshot().NpcExperienceInbox.Revision == revision,
                "Core accepted a stale native poison assignment epoch or mutated the detached inbox.");
        }

        private static ResidentActor FindEligibleActor(StreetPopulation streets, HashSet<string> excludedResidentsWithStatus)
        {
            if (!streets || !streets.Ready) return null;
            ResidentActor[] actors = UnityEngine.Object.FindObjectsOfType<ResidentActor>();
            foreach (ResidentActor candidate in actors)
            {
                if (!candidate || !candidate.gameObject.activeInHierarchy || candidate.Resident == null ||
                    !candidate.Resident.IsAlive || candidate.Resident.AgeGroup != ResidentAgeGroup.Adult ||
                    candidate.Resident.IsQuestProtected || excludedResidentsWithStatus.Contains(candidate.Resident.Id))
                    continue;
                DaggerfallEntityBehaviour native = candidate.GetComponent<DaggerfallEntityBehaviour>();
                EntityEffectManager manager = candidate.GetComponent<EntityEffectManager>();
                if (!native || native.Entity == null || native.Entity.CurrentHealth <= 0 ||
                    native.Entity == GameManager.Instance.PlayerEntity || !manager || manager.gameObject != candidate.gameObject ||
                    !candidate.GetComponent<NativeNpcPoisonOnsetBinding>() || !NoActivePoison(manager))
                    continue;
                return candidate;
            }
            return null;
        }

        private static CandidateDiagnostics CountCandidates(StreetPopulation streets)
        {
            var counts = new CandidateDiagnostics();
            if (!streets || !streets.Ready) return counts;
            ResidentActor[] actors = UnityEngine.Object.FindObjectsOfType<ResidentActor>();
            foreach (ResidentActor candidate in actors)
            {
                if (!candidate || !candidate.gameObject.activeInHierarchy || candidate.Resident == null || !candidate.Resident.IsAlive ||
                    candidate.Resident.AgeGroup != ResidentAgeGroup.Adult || candidate.Resident.IsQuestProtected)
                    continue;
                counts.AdultAliveResidentsSeen++;
                DaggerfallEntityBehaviour native = candidate.GetComponent<DaggerfallEntityBehaviour>();
                EntityEffectManager manager = candidate.GetComponent<EntityEffectManager>();
                if (!manager || manager.gameObject != candidate.gameObject) counts.NoEffectManager++;
                if (!candidate.GetComponent<NativeNpcPoisonOnsetBinding>()) counts.MissingPoisonBinding++;
                if (native && native.Entity != null && native.Entity.Level == 1) counts.NativeLevelOne++;
            }
            return counts;
        }

        private static List<NativeStatusRow> StatusRows(WorldStateStore store, string residentId)
        {
            var result = new List<NativeStatusRow>();
            if (store == null || string.IsNullOrWhiteSpace(residentId)) return result;
            WorldState snapshot = store.Snapshot();
            if (snapshot == null || snapshot.NpcExperienceInbox == null || snapshot.NpcExperienceInbox.Entries == null)
                return result;
            foreach (NpcExperienceInboxEntry row in snapshot.NpcExperienceInbox.Entries)
            {
                if (row == null || row.Origin != NpcExperienceEventOrigin.NativeOutcome ||
                    row.EventKind != NpcExperienceEventKind.StatusOnset ||
                    !string.Equals(row.SubjectPersonId, residentId, StringComparison.Ordinal)) continue;
                result.Add(new NativeStatusRow
                {
                    Key = row.StatusNativeEffectKey,
                    StatusKind = row.StatusKind,
                    Sequence = row.StatusOnsetSequence,
                    Attribution = row.ResponsibleActorAttribution,
                    ResponsibleActorId = row.ResponsibleActorId,
                    Origin = row.Origin,
                    Kind = row.EventKind
                });
            }
            result.Sort((left, right) => left.Sequence.CompareTo(right.Sequence));
            return result;
        }

        private static void AssertStatusRows(WorldStateStore store, string residentId, int count, string key)
        {
            List<NativeStatusRow> rows = StatusRows(store, residentId);
            Require(rows.Count == count, "Expected exactly " + count + " poison status receipts for " + residentId +
                "; observed " + rows.Count + ".");
            for (int i = 0; i < rows.Count; i++)
            {
                NativeStatusRow row = rows[i];
                Require(row.Kind == NpcExperienceEventKind.StatusOnset && row.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                    row.StatusKind == (int)NpcExperienceStatusKind.Poison && row.Sequence == i + 1 &&
                    string.Equals(row.Key, key, StringComparison.Ordinal) &&
                    row.Attribution == NpcResponsibleActorAttribution.Unknown && row.ResponsibleActorId == null,
                    "Poison application row must carry the exact native key, monotonic sequence, and unknown cause; row=" + i + ".");
            }
        }

        private static void AssertPoisonSourceState(WorldStateStore store, string residentId, int sequence)
        {
            NpcPoisonOnsetState source = store.ReadNpcPoisonOnsetState(residentId);
            Require(source != null && source.LastIssuedSequence == sequence &&
                source.LastProjectedSequence == sequence && source.Pending == null &&
                source.DegradedCount == 0 && !source.SequenceExhausted,
                "Native poison source sequence or projection marker was not retained with the real assignments.");
        }

        private static void VerifyDetachedProjectionPersistence(WorldState nativeSnapshot,
            string residentId, string nativeKey, int expectedSequence)
        {
            Require(nativeSnapshot != null && nativeSnapshot.Version == WorldState.CurrentVersion &&
                expectedSequence >= 2, "Native poison projection fixture needs real current-schema assignments.");
            WorldState sourceWire = RoundTripWorld(nativeSnapshot, "source-copy");
            NpcExperienceInboxEntry oldest = FindPoisonRow(sourceWire, residentId, 1);
            NpcExperienceInboxEntry newest = FindPoisonRow(sourceWire, residentId, expectedSequence);
            Require(oldest != null && newest != null && newest.ObservedGameTime >= oldest.ObservedGameTime &&
                newest.StatusNativeEffectKey == nativeKey,
                "Native poison projection fixture lost an exact real source receipt.");
            var sourceRestored = new WorldStateStore(sourceWire.WorldSeed);
            sourceRestored.Restore(sourceWire);
            AssertPoisonSourceState(sourceRestored, residentId, expectedSequence);
            Require(FindPoisonRow(sourceRestored.Snapshot(), residentId, 1) != null &&
                FindPoisonRow(sourceRestored.Snapshot(), residentId, expectedSequence) != null,
                "FullSerializer lost one of the real native poison assignment receipts.");
            VerifyControlledPendingAtCapacity(nativeKey);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_POISON_PROJECTION_MARKERS_PASS: " +
                "source=actual-native-assignment; nativeCursor=" + expectedSequence +
                "; pending=controlled-source-admission-at-capacity; nativeFullSerializer=true; " +
                "pendingDrainedOnce=true; liveQueuePressure=not-tested; liveStoreMutated=false");
        }

        private static void VerifyControlledPendingAtCapacity(string nativeKey)
        {
            const uint seed = 0x504F4953;
            const int settlementId = 71092;
            const int buildingKey = 81092;
            var fixture = new WorldStateStore(seed);
            string personId = fixture.GetOrCreate(settlementId, new[] { buildingKey }, 1)
                .Households[0].Residents[0].Id;
            for (int i = 0; i < NpcExperienceInbox.MaximumRetainedPerObserver; i++)
            {
                double at = 1000d + i * 120d;
                string id = "poison-capacity-appointment-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var booking = new AppointmentRecord {
                    Id = id, ResidentId = personId, Purpose = "poison pending capacity fixture",
                    StartsAt = at, EndsAt = at + 60d, GraceSeconds = 0d,
                    PatienceSeconds = 600, Importance = 2, Status = "accepted",
                    Place = new AppointmentPlace { Kind = "building", SettlementId = settlementId,
                        BuildingKey = buildingKey, Detail = "controlled capacity fixture" }
                };
                string reason;
                Require(fixture.TryReserveAppointment(booking, at - 100d, out reason),
                    "Controlled capacity appointment reservation failed at " + i + ": " + reason);
                fixture.ObserveAppointment(id, at, true, true, false);
                AppointmentRecord resolved = fixture.FindAppointment(id);
                Require(resolved != null && resolved.Status == "attended" &&
                    resolved.ExperienceProjection == NpcAppointmentExperienceProjection.Projected,
                    "Controlled capacity appointment did not project its exact verified source at " + i + ".");
            }
            WorldState full = fixture.Snapshot();
            Require(full.NpcExperienceInbox.Entries.FindAll(x => x.ObserverId == personId).Count ==
                NpcExperienceInbox.MaximumRetainedPerObserver &&
                full.NpcExperienceInbox.Entries.TrueForAll(x => x.Status == NpcExperienceInboxStatus.Pending),
                "Controlled source-backed appointment rows did not reach the pinned observer cap.");
            object epoch = fixture.CaptureNpcVitalsWriteEpoch();
            var owner = new object();
            var bundle = new object();
            NpcPoisonAssignmentTicket assignment = fixture.TryIssueNpcPoisonAssignment(epoch, personId, owner, bundle);
            NpcExperienceEvent queued;
            Require(assignment != null && fixture.TryRecordNpcPoisonAssignment(assignment, nativeKey, 40000d,
                out queued) == NpcPoisonCaptureDisposition.Pending && queued == null,
                "Exact controlled poison assignment did not persist pending at the real Inbox cap.");
            WorldState pendingWire = RoundTripWorld(fixture.Snapshot(), "capacity-pending");
            var restored = new WorldStateStore(pendingWire.WorldSeed);
            restored.Restore(pendingWire);
            NpcPoisonOnsetState pending = restored.ReadNpcPoisonOnsetState(personId);
            Require(pending != null && pending.LastIssuedSequence == 1 &&
                pending.LastProjectedSequence == 0 && pending.Pending != null &&
                pending.Pending.Sequence == 1 && pending.Pending.NativeEffectKey == nativeKey &&
                !restored.TryProjectPendingNpcPoisonOnset(personId, out _),
                "FullSerializer changed the capacity-deferred poison source or bypassed the cap.");

            restored.GetOrCreateNpcPersonality(personId, seed, NpcSocialBackground.Commoner);
            restored.SetConversationBaseline(personId, 0, 40001d);
            restored.RecordConversationEncounter(personId, 40001d);
            NpcExperienceAppraisalTicket appraisal;
            Require(restored.TryCaptureNpcExperienceAppraisal(personId, 40001d, out appraisal) &&
                appraisal != null && appraisal.Request.CanonicalEvidenceId ==
                    "poison-capacity-appointment-0:attendance",
                "Controlled capacity fixture did not select its oldest genuine appointment.");
            string response = ControlledAppointmentResponse(appraisal.Request);
            Require(restored.TryCommitNpcExperienceAppraisal(appraisal, response),
                "Controlled capacity appointment appraisal did not consume one pinned row.");
            long beforeRetirementRevision = restored.Snapshot().NpcExperienceInbox.Revision;
            bool madeRoom = false;
            for (int slice = 0; slice < 40; slice++)
            {
                NpcExperienceInboxRetentionResult report = restored.MaintainNpcExperienceInboxHistory(800000d, 16, 4, true);
                Require(report.Examined <= 16 && report.Retired <= 4,
                    "Controlled poison capacity retirement exceeded bounded budgets.");
                if (restored.Snapshot().NpcExperienceInbox.Entries.Count <
                    NpcExperienceInbox.MaximumRetainedPerObserver) { madeRoom = true; break; }
            }
            Require(madeRoom && restored.Snapshot().NpcExperienceInbox.Revision == beforeRetirementRevision,
                "Genuine maintenance did not free one consumed appointment without rewinding enqueue revision.");
            NpcExperienceEvent projected;
            Require(restored.TryProjectPendingNpcPoisonOnset(personId, out projected) &&
                projected != null && projected.EventKind == NpcExperienceEventKind.StatusOnset &&
                projected.NativeImpactDetails != null &&
                projected.NativeImpactDetails.NativeEffectKey == nativeKey &&
                projected.NativeImpactDetails.StatusOnsetSequence == 1 &&
                !restored.TryProjectPendingNpcPoisonOnset(personId, out _),
                "Saved capacity-deferred poison source did not drain exactly once after real retirement.");
            WorldState projectedWire = RoundTripWorld(restored.Snapshot(), "capacity-drained");
            var replay = new WorldStateStore(projectedWire.WorldSeed);
            replay.Restore(projectedWire);
            NpcPoisonOnsetState finalSource = replay.ReadNpcPoisonOnsetState(personId);
            Require(finalSource != null && finalSource.Pending == null &&
                finalSource.LastIssuedSequence == 1 && finalSource.LastProjectedSequence == 1 &&
                FindPoisonRow(replay.Snapshot(), personId, 1) != null &&
                !replay.TryProjectPendingNpcPoisonOnset(personId, out _),
                "FullSerializer replay changed the drained controlled poison source.");
        }

        private static string ControlledAppointmentResponse(NpcExperienceAppraisalRequest request)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion.ToString(culture) +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion.ToString(culture) +
                ",\"saveBranchId\":\"" + request.SaveBranchId + "\",\"observerId\":\"" + request.ObserverId +
                "\",\"subjectPersonId\":\"" + request.SubjectPersonId + "\",\"evidenceDeduplicationKey\":\"" +
                request.EvidenceDeduplicationKey + "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId +
                "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? string.Empty) +
                "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision.ToString(culture) +
                ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision.ToString(culture) +
                ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision.ToString(culture) +
                ",\"moodDelta\":2,\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":0," +
                "\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
        }

        private static NpcExperienceInboxEntry FindPoisonRow(WorldState state, string personId, long sequence)
        {
            foreach (NpcExperienceInboxEntry row in state.NpcExperienceInbox.Entries)
                if (row != null && row.ObserverId == personId && row.SubjectPersonId == personId &&
                    row.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                    row.EventKind == NpcExperienceEventKind.StatusOnset &&
                    row.StatusKind == (int)NpcExperienceStatusKind.Poison &&
                    row.StatusOnsetSequence == sequence) return row;
            return null;
        }

        private static WorldState RoundTripWorld(WorldState source, string suffix)
        {
            string path = Path.Combine(Path.GetTempPath(), "poti-native-poison-projection-" + suffix + "-" +
                Guid.NewGuid().ToString("N") + ".json");
            try
            {
                string json = SaveLoadManager.Serialize(typeof(WorldState), source);
                Require(!string.IsNullOrEmpty(json), "Native FullSerializer produced an empty poison projection state.");
                File.WriteAllText(path, json);
                WorldState decoded = SaveLoadManager.Deserialize(typeof(WorldState),
                    File.ReadAllText(path), true) as WorldState;
                Require(decoded != null && decoded.Version == WorldState.CurrentVersion,
                    "Native FullSerializer did not retain the current poison source schema.");
                return decoded;
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static bool NoActivePoison(EntityEffectManager manager)
        { return manager && FindActivePoisonBundle(manager, null, out _) == null; }

        private static LiveEffectBundle FindActivePoisonBundle(EntityEffectManager manager,
            LiveEffectBundle exclude, out string key)
        {
            key = null;
            if (!manager) return null;
            LiveEffectBundle[] bundles = manager.EffectBundles;
            if (bundles == null) return null;
            foreach (LiveEffectBundle bundle in bundles)
            {
                if (bundle == null || ReferenceEquals(bundle, exclude) || bundle.bundleType != BundleTypes.Poison ||
                    bundle.liveEffects == null) continue;
                foreach (IEntityEffect effect in bundle.liveEffects)
                {
                    PoisonEffect poison = effect as PoisonEffect;
                    if (poison != null && !poison.HasEnded)
                    {
                        key = poison.Key;
                        return bundle;
                    }
                }
            }
            return null;
        }

        private static bool IsExactActive(EntityEffectManager manager, LiveEffectBundle wanted)
        {
            if (!manager || wanted == null) return false;
            LiveEffectBundle[] bundles = manager.EffectBundles;
            if (bundles == null) return false;
            foreach (LiveEffectBundle bundle in bundles) if (ReferenceEquals(bundle, wanted)) return true;
            return false;
        }

        private static LiveEffectBundle FindRetainedNonPoison(EntityEffectManager manager, string expectedEffectKey,
            IList<LiveEffectBundle> priorBundles)
        {
            if (!manager || string.IsNullOrWhiteSpace(expectedEffectKey)) return null;
            LiveEffectBundle[] bundles = manager.EffectBundles;
            if (bundles == null) return null;
            foreach (LiveEffectBundle bundle in bundles)
            {
                if (bundle == null || bundle.bundleType == BundleTypes.Poison || bundle.liveEffects == null) continue;
                bool wasPresent = false;
                if (priorBundles != null)
                    foreach (LiveEffectBundle prior in priorBundles)
                        if (ReferenceEquals(bundle, prior)) { wasPresent = true; break; }
                if (wasPresent) continue;
                bool hasLiveNonPoison = false;
                foreach (IEntityEffect effect in bundle.liveEffects)
                    if (effect != null && !(effect is PoisonEffect) && !effect.HasEnded &&
                        string.Equals(effect.Key, expectedEffectKey, StringComparison.Ordinal))
                    { hasLiveNonPoison = true; break; }
                if (hasLiveNonPoison) return bundle;
            }
            return null;
        }

        private static void RemoveActivePoisonBundles(EntityEffectManager manager)
        {
            if (!manager) return;
            LiveEffectBundle[] bundles = manager.EffectBundles;
            if (bundles == null) return;
            var remove = new List<LiveEffectBundle>();
            foreach (LiveEffectBundle bundle in bundles)
                if (bundle != null && bundle.bundleType == BundleTypes.Poison && bundle.liveEffects != null)
                    foreach (IEntityEffect effect in bundle.liveEffects)
                        if (effect is PoisonEffect && !effect.HasEnded) { remove.Add(bundle); break; }
            foreach (LiveEffectBundle bundle in remove) manager.RemoveBundle(bundle);
        }

        private static IEnumerator WaitForActor(StreetPopulation streets, string residentId, int priorInstanceId)
        {
            float deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline)
            {
                ResidentActor actor = streets ? streets.FindActor(residentId) : null;
                if (streets && streets.Ready && actor && actor.gameObject.activeInHierarchy &&
                    actor.GetInstanceID() != priorInstanceId) yield break;
                yield return null;
            }
            throw new InvalidOperationException("Resident did not rebuild after the owned poison checkpoint load.");
        }

        private static IEnumerator SaveCheckpoint(GameManager game, string title)
        {
            bool saved = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnSave += onSave;
            try
            {
                game.SaveLoadManager.Save(OwnedCharacter, title);
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                Require(saved, "Owned native poison checkpoint save timed out: " + title);
                game.SaveLoadManager.EnumerateSaves();
                Require(game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, title) >= 0,
                    "Owned native poison checkpoint was not enumerated after save: " + title);
            }
            finally { SaveLoadManager.OnSave -= onSave; }
        }

        private static IEnumerator RestoreBaseline(PeopleOfTheIliacBayMod host, StreetPopulation streets)
        {
            if (DaggerfallUI.Instance) DaggerfallUI.Instance.PopToHUD();
            yield return ConversationMoodProbe.CloseTalk(host);
            yield return ConversationMoodProbe.LoadSave(host, streets, OwnedCharacter, BaselineSave, true);
            GameManager game = GameManager.Instance;
            Require(game && game.PlayerEntity != null && game.PlayerEntity.Name == OwnedCharacter &&
                game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.Name == "Gothway Garden" &&
                !game.PlayerEnterExit.IsPlayerInsideBuilding,
                "Native poison probe did not restore the exact owned exterior baseline.");
        }

        private static void DeleteCheckpointIfPresent(GameManager game, string title)
        {
            if (game == null || string.IsNullOrWhiteSpace(title)) return;
            game.SaveLoadManager.EnumerateSaves();
            int slot = game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, title);
            if (slot >= 0) game.SaveLoadManager.DeleteSaveFolder(slot);
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(OwnedCharacter, title) < 0,
                "Owned temporary native poison checkpoint remains after cleanup: " + title);
        }

        private static T GetComponent<T>(ResidentActor actor) where T : Component
        { return actor ? actor.GetComponent<T>() : null; }

        private static T GetComponent<T>(GameObject owner) where T : Component
        { return owner ? owner.GetComponent<T>() : null; }

        private static WorldStateStore ReadStore(PeopleOfTheIliacBayMod host)
        {
            FieldInfo field = typeof(PeopleOfTheIliacBayMod).GetField("store",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field == null || !host ? null : field.GetValue(host) as WorldStateStore;
        }

        private static string ArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }

        private sealed class NestedEnumeratorDriver : IDisposable
        {
            private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
            private bool complete;
            internal Exception Failure { get; private set; }
            internal NestedEnumeratorDriver(IEnumerator root) { stack.Push(root); }
            internal bool TryMoveNext(out object yielded)
            {
                yielded = null;
                while (!complete && Failure == null && stack.Count > 0)
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
                if (stack.Count == 0 && Failure == null) complete = true;
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
