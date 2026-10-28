using System;
using System.Globalization;
using System.IO;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Controlled current-schema framework persistence acceptance using DFU's native FullSerializer.
    /// Fixture actors and outcomes are controlled test data. The funding case uses the current native
    /// player purse for one synchronous debit, then restores its exact coins and LOC objects in finally.
    /// This is not gameplay hiring and the probe never opens a human save.
    /// </summary>
    internal static class NpcFrameworkPersistenceProbe
    {
        private sealed class OperationalRetentionFixture
        {
            internal string PersonId;
            internal string[] TaskIds;
            internal NpcConversationCommandTurn FirstTurn;
        }

        private sealed class SpeechAffinityFixture
        {
            internal NpcObservedSpeechReceipt Supplement;
            internal NpcObservedSpeechTicket SupplementTicket;
            internal string FamilyReceiptId;
            internal string LegacyReceiptId;
        }

        internal static void Verify()
        {
            CorpseDeparturePersistenceProbe.Verify();
            const uint seed = 0x4E504652;
            const int settlementId = 71002;
            const int buildingKey = 81002;
            const int nativeHash = 91002;
            const double gameTime = 24680d;
            const string homeReference = "fixture-home-1";
            const string contractId = "fixture-contract-1";
            const string fundingReceiptId = "fixture-funding-1";
            string personId = string.Format(CultureInfo.InvariantCulture, "npc:{0}:{1}:{2}", settlementId, buildingKey, nativeHash);
            string tempPath = Path.Combine(Path.GetTempPath(), "poti-framework-persistence-" + Guid.NewGuid().ToString("N") + ".json");

            try
            {
                var store = new WorldStateStore(seed);
                SettlementPopulation population = store.GetOrCreate(settlementId, new[] { buildingKey }, 1);
                Require(population.Households.Count == 1 && population.Households[0].Residents.Count == 1,
                    "Controlled managed-resident fixture was not created.");
                Resident managedResident = population.Households[0].Residents[0];
                Require(managedResident.IsAlive && !managedResident.IsQuestProtected &&
                    managedResident.AgeGroup == ResidentAgeGroup.Adult && managedResident.Id != personId,
                    "Controlled health-motivation owner is not a separate eligible resident.");
                string managedPersonId = managedResident.Id;
                var native = store.RegisterNativePerson(settlementId, buildingKey, nativeHash, "Persistence Fixture",
                    ResidentSex.Female, 1, 1, 0, 0);
                Require(native.Id == personId, "Controlled native identity was not stable.");
                store.GetOrCreateNpcPersonality(personId, seed, NpcSocialBackground.Commoner);

                var fearEvidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                    "fixture-fear-receipt", "fixture-fear-episode", "fixture-fear-outcome",
                    NpcExperienceEventKind.Injury, gameTime, NpcResponsibleActorAttribution.Known,
                    NpcDirectedRelationship.PlayerTargetId);
                var fearUpdate = new NpcRelationshipLedgerUpdateProposal
                {
                    ExpectedLedgerRevision = 0, ObserverId = personId,
                    TargetId = NpcDirectedRelationship.PlayerTargetId,
                    RelationshipUpdate = new NpcRelationshipUpdateProposal
                    {
                        ExpectedRevision = 0, Evidence = fearEvidence,
                        TrustDelta = -4, ResentmentDelta = 3, InterpersonalFearDelta = 10
                    }
                };
                Require(store.TryCommitNpcRelationship(fearUpdate, 0), "Controlled fear fixture was not committed.");
                var calmingEvidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                    "fixture-calming-receipt", "fixture-calming-episode", "fixture-calming-outcome",
                    NpcExperienceEventKind.Healing, gameTime + 24 * 60 * 60,
                    NpcResponsibleActorAttribution.Known, NpcDirectedRelationship.PlayerTargetId);
                var calmingUpdate = new NpcRelationshipLedgerUpdateProposal
                {
                    ExpectedLedgerRevision = 1, ObserverId = personId,
                    TargetId = NpcDirectedRelationship.PlayerTargetId,
                    RelationshipUpdate = new NpcRelationshipUpdateProposal
                    { ExpectedRevision = 1, Evidence = calmingEvidence, InterpersonalFearDelta = -1 }
                };
                Require(store.TryCommitNpcRelationship(calmingUpdate, 1), "Controlled calming fixture was not committed.");
                var fearBeforeSave = store.ReadNpcRelationship(personId, NpcDirectedRelationship.PlayerTargetId);
                double fearReadTime = gameTime + 7 * 24 * 60 * 60;
                var projectedFearBeforeSave = store.ReadNpcRelationship(personId,
                    NpcDirectedRelationship.PlayerTargetId, fearReadTime);
                Require(fearBeforeSave.HasFearRecoveryAnchor && projectedFearBeforeSave.ProjectedInterpersonalFear < 10,
                    "Controlled fear fixture did not recover over supplied in-game days.");
                Require(fearBeforeSave.FearRecoveryAnchorGameTime == gameTime &&
                    Math.Abs(fearBeforeSave.FearRecoveryBaselineRemainder) > 0.000000001,
                    "Controlled calming fixture did not retain the original fear clock and fractional baseline.");

                double acuteFearObservedAt = gameTime + 1800;
                double acuteFearReadTime = gameTime + 3600;
                Require(store.TryObserveNpcAcuteFear(personId, "fixture-hostile-threat-1",
                    NpcFearSeverity.Severe, acuteFearObservedAt),
                    "Controlled acute-fear fixture was not observed.");
                NpcAcuteFearView acuteFearBeforeSave = store.ReadNpcAcuteFear(personId, acuteFearReadTime);
                Require(acuteFearBeforeSave != null && acuteFearBeforeSave.CurrentFear > 0,
                    "Controlled acute-fear fixture did not project positive fear.");

                // One verified native-observation-shaped ledger row. Fixture provenance is not a claim of an actual observation.
                NpcKnowledgeWriteTicket knowledgeTicket;
                Require(store.TryCaptureNpcKnowledgeWrite(personId, out knowledgeTicket) && knowledgeTicket != null,
                    "Controlled knowledge write ticket was not captured.");
                NpcKnowledgeUpdate knowledge = NpcKnowledgeUpdate.FromNativeEvidence(personId, "fixture-knowledge-1",
                    "fixture.home", "Lives in the fixture home", NpcDisclosureProtection.Private,
                    NpcKnowledgeEvidenceOrigin.NativeObservation, "fixture-observation-1", "fixture-knowledge-receipt-1",
                    1, gameTime);
                Require(store.TryCommitNpcKnowledge(knowledgeTicket, knowledge), "Controlled knowledge row was not committed.");
                Require(!store.TryCommitNpcKnowledge(knowledgeTicket, knowledge), "Knowledge write replay was accepted.");

                // The v2 agenda accepts actual saved native-health observations, not authored generic rows.
                NpcVitalsView managedBaseline = store.ReadNpcVitals(managedPersonId, NpcArchetype.Citizen);
                Require(managedBaseline != null && managedBaseline.MaximumHealth >= 8,
                    "Controlled resident has insufficient health for distinct injury thresholds.");
                int managedInjuryHealth = managedBaseline.MaximumHealth / 2;
                Require(store.TryCaptureNpcVitals(managedPersonId, NpcArchetype.Citizen, managedInjuryHealth,
                    managedBaseline.MaximumMagicka, gameTime + 4, managedBaseline.Revision),
                    "Controlled managed-resident injury was not saved.");
                NpcVitalsView managedInjury = store.ReadNpcVitals(managedPersonId, NpcArchetype.Citizen);
                Require(managedInjury.IsPersisted && managedInjury.Revision > managedBaseline.Revision &&
                    managedInjury.CurrentHealth == managedInjuryHealth,
                    "Controlled managed-resident injury lacks saved native vitals.");
                Require(NpcHealthNeedLifecycle.Observe(store, managedPersonId, NpcArchetype.Citizen, gameTime + 5),
                    "Saved native injury did not create its health need and recovery goal.");
                NpcMotivationStateView motivationBeforeSave = store.ReadNpcMotivationState(managedPersonId);
                VerifyMotivation(store, managedPersonId, motivationBeforeSave, managedInjury);
                Require(!NpcHealthNeedLifecycle.Observe(store, managedPersonId, NpcArchetype.Citizen, gameTime + 5),
                    "Repeated unchanged native-health observation was accepted.");
                VerifyMotivation(store, managedPersonId, motivationBeforeSave, managedInjury);
                NpcMotivationUpdateTicket motivationTicket;
                Require(store.TryCaptureNpcMotivationUpdate(managedPersonId, out motivationTicket) &&
                    motivationTicket != null && motivationTicket.ExpectedRevision == motivationBeforeSave.Revision,
                    "Controlled pre-restore motivation ticket was not captured.");

                NpcServiceContractWriteTicket serviceTicket = store.CaptureNpcServiceContractWriteTicket();
                NpcServiceContractTerms terms;
                Require(NpcServiceContractTerms.TryCreate(contractId, NpcDirectedRelationship.PlayerTargetId, personId,
                    homeReference, NpcServiceRole.Fighter, NpcServiceCoverage.Follow, 120, false,
                    NpcServiceRiskScope.None, new NpcServiceProtectedTarget[0], NpcServiceRestTreatment.RestNotIncluded,
                    NpcServiceResupplyResponsibility.SeparatelyNegotiated, new NpcServiceReservedObligation[0],
                    id => id == NpcDirectedRelationship.PlayerTargetId || id == personId,
                    id => id == homeReference, (id, category, revision) => false,
                    (id, revision, kind, starts, ends) => false, out terms),
                    "Controlled service terms were rejected.");
                NpcServiceTransferReceipt funding;
                NpcServiceContractLedger serviceCandidate;
                VerifyNativeServiceFunding(store, terms, serviceTicket, personId, gameTime,
                    fundingReceiptId, out funding, out serviceCandidate);
                string serviceError;

                // Capture a pending health episode with fixture vitals to exercise save-bound transfer metadata.
                NpcVitalsView vitals = store.ReadNpcVitals(personId, NpcArchetype.Warrior);
                Require(store.TryCaptureNpcVitals(personId, NpcArchetype.Warrior, vitals.MaximumHealth - 1,
                    vitals.MaximumMagicka, gameTime + 2, vitals.Revision), "Initial controlled vitals were not captured.");
                NpcVitalsView afterInitialCapture = store.ReadNpcVitals(personId, NpcArchetype.Warrior);
                Require(store.TryCaptureNpcVitals(personId, NpcArchetype.Warrior, afterInitialCapture.CurrentHealth - 3,
                    afterInitialCapture.CurrentMagicka, gameTime + 3, afterInitialCapture.Revision),
                    "Controlled follow-up health change was not captured.");
                NpcActorProgress pendingBeforeSave = store.ReadNpcProgress(personId);
                Require(pendingBeforeSave != null && pendingBeforeSave.HasPendingHealthEpisode &&
                    pendingBeforeSave.HealthEpisodeDamageTotal == 3 && pendingBeforeSave.HealthEpisodeChangeCount == 1,
                    "Controlled pending health episode was not prepared.");

                SpeechAffinityFixture speechFixture = PrepareSpeechAffinityFixture(store, personId, gameTime + 2 * 24 * 60 * 60);
                OperationalRetentionFixture retention = PrepareOperationalRetentionFixture(store,
                    settlementId + 1, buildingKey + 1, gameTime);
                string acceptedCommandTaskId;
                NpcConversationCommandTurn pendingCommand = PrepareConversationCommandFixture(store,
                    managedPersonId, gameTime, out acceptedCommandTaskId);
                WorldState saved = store.Snapshot();
                Require(saved.Version == WorldState.CurrentVersion, "Fixture did not use the current framework save schema.");
                string json = SaveLoadManager.Serialize(typeof(WorldState), saved);
                File.WriteAllText(tempPath, json);
                string serializedFromDisk = File.ReadAllText(tempPath);
                var decoded = SaveLoadManager.Deserialize(typeof(WorldState), serializedFromDisk, true) as WorldState;
                Require(decoded != null, "DFU FullSerializer did not deserialize the controlled framework save.");
                Require(decoded.Version == WorldState.CurrentVersion, "DFU FullSerializer changed the current framework save version.");

                var restored = new WorldStateStore(seed);
                restored.Restore(decoded);
                VerifyOperationalRetentionFixture(restored, retention, gameTime);
                VerifyConversationCommandFixture(restored, managedPersonId, pendingCommand,
                    acceptedCommandTaskId, gameTime);
                VerifySpeechAffinityFixture(restored, personId, gameTime + 2 * 24 * 60 * 60, speechFixture);
                VerifyKnowledge(restored, personId);
                VerifyMotivation(restored, managedPersonId, motivationBeforeSave, managedInjury);
                VerifyService(restored, contractId, fundingReceiptId);
                VerifyPendingHealth(restored, personId, pendingBeforeSave);
                var restoredFear = restored.ReadNpcRelationship(personId,
                    NpcDirectedRelationship.PlayerTargetId, fearReadTime);
                Debug.Log("NPC_FRAMEWORK_FEAR_ROUNDTRIP_VALUES: before=" + FearValues(fearBeforeSave) +
                    "; expectedProjected=" + projectedFearBeforeSave.ProjectedInterpersonalFear +
                    "; after=" + FearValues(restoredFear) + ".");
                // Native FullSerializer prints doubles with fewer significant digits.
                // Keep visible fear and integer state exact; tolerate only sub-unit numeric noise.
                Require(restoredFear != null && restoredFear.HasFearRecoveryAnchor &&
                    restoredFear.StoredInterpersonalFear == fearBeforeSave.StoredInterpersonalFear &&
                    restoredFear.ProjectedInterpersonalFear == projectedFearBeforeSave.ProjectedInterpersonalFear &&
                    restoredFear.FearRecoveryAnchorGameTime == fearBeforeSave.FearRecoveryAnchorGameTime &&
                    Math.Abs(restoredFear.FearRecoveryHalfLifeSeconds - fearBeforeSave.FearRecoveryHalfLifeSeconds) <= 0.000001 &&
                    Math.Abs(restoredFear.FearRecoveryBaselineRemainder - fearBeforeSave.FearRecoveryBaselineRemainder) <= 0.000000000001 &&
                    restoredFear.ReliabilityTrust == fearBeforeSave.ReliabilityTrust &&
                    restoredFear.Resentment == fearBeforeSave.Resentment &&
                    restoredFear.Revision == fearBeforeSave.Revision,
                    "DFU FullSerializer changed saved fear timing, projected residual or other relationship dimensions.");
                NpcAcuteFearView restoredAcuteFear = restored.ReadNpcAcuteFear(personId, acuteFearReadTime);
                Require(restoredAcuteFear != null &&
                    restoredAcuteFear.ObserverId == acuteFearBeforeSave.ObserverId &&
                    restoredAcuteFear.SourceId == acuteFearBeforeSave.SourceId &&
                    restoredAcuteFear.Revision == acuteFearBeforeSave.Revision &&
                    restoredAcuteFear.Severity == acuteFearBeforeSave.Severity &&
                    Math.Abs(restoredAcuteFear.Intensity - acuteFearBeforeSave.Intensity) <= 0.001 &&
                    Math.Abs(restoredAcuteFear.ObservedGameTime - acuteFearBeforeSave.ObservedGameTime) <= 0.000001 &&
                    Math.Abs(restoredAcuteFear.RecoveryHalfLifeSeconds - acuteFearBeforeSave.RecoveryHalfLifeSeconds) <= 0.000001 &&
                    Math.Abs(restoredAcuteFear.ProjectedGameTime - acuteFearBeforeSave.ProjectedGameTime) <= 0.000001 &&
                    Math.Abs(restoredAcuteFear.CurrentFear - acuteFearBeforeSave.CurrentFear) <= 0.001,
                    "DFU FullSerializer changed acute fear identity, revision, saved timing/rate or projected fear.");

                // Schema 40 had directed fear but no separate acute-fear ledger. Use a separate
                // narrow fear-migration fixture: newer motivation and classified operation
                // metadata are out of scope here, not historical schema-40 evidence.
                var schema40FearFixture = SaveLoadManager.Deserialize(typeof(WorldState), serializedFromDisk, true) as WorldState;
                Require(schema40FearFixture != null && schema40FearFixture.NpcMotivations != null &&
                    schema40FearFixture.NpcObservedSpeechSources != null &&
                    schema40FearFixture.NpcExperienceInbox != null &&
                    schema40FearFixture.NpcExperienceInbox.Entries != null,
                    "Separate schema-40 fear-migration fixture was not decoded.");
                // The current save contains two schema-51 speech observations. This
                // narrow fear fixture predates their source cursors and detail.
                Require(schema40FearFixture.NpcObservedSpeechSources.Count == 1,
                    "Separate schema-40 fear fixture did not contain its expected newer speech cursor.");
                int newerSpeechRows = schema40FearFixture.NpcExperienceInbox.Entries.RemoveAll(entry =>
                    entry != null && (entry.ReceiptId == speechFixture.Supplement.Evidence.ReceiptId ||
                        entry.ReceiptId == speechFixture.FamilyReceiptId));
                Require(newerSpeechRows == 2,
                    "Separate schema-40 fear fixture did not contain its expected newer speech rows.");
                schema40FearFixture.NpcObservedSpeechSources.Clear();
                // This isolated fear-migration input must have a genuine pre-v5
                // inbox shape. The current decoded save above remains unchanged.
                foreach (NpcExperienceInboxEntry legacy in schema40FearFixture.NpcExperienceInbox.Entries)
                {
                    legacy.EnqueueOrdinal = 0;
                    legacy.SourceVitalsRevision = 0;
                    legacy.SourceKind = NpcExperienceSourceKind.LegacyUnknown;
                }
                schema40FearFixture.NpcExperienceInbox.Version = 4;
                schema40FearFixture.NpcExperienceInbox.Revision = schema40FearFixture.NpcExperienceInbox.Entries.Count;
                schema40FearFixture.NpcExperienceInbox.RetiredCount = 0;
                schema40FearFixture.NpcExperienceInbox.DetailRevision = 0;
                schema40FearFixture.NpcExperienceInbox.LegacyImportedRevision = 0;
                schema40FearFixture.NpcMoodCauseHistoryFloors.Clear();
                schema40FearFixture.ConversationCueArchive = null;
                schema40FearFixture.NpcMotivations.Clear();
                schema40FearFixture.NpcTasks.Clear();
                schema40FearFixture.NpcActions.Clear();
                schema40FearFixture.NpcConversationCommandCursors.Clear();
                schema40FearFixture.Version = 40;
                var restoredSchema40 = new WorldStateStore(seed);
                restoredSchema40.Restore(schema40FearFixture);
                Require(decoded.Version == WorldState.CurrentVersion,
                    "Fear-migration fixture changed the current-schema decoded save.");
                Require(restoredSchema40.Snapshot().NpcAcuteFears.Count == 0,
                    "Schema-40 migration retained acute fear records.");
                var migratedDirectedFear = restoredSchema40.ReadNpcRelationship(personId,
                    NpcDirectedRelationship.PlayerTargetId, fearReadTime);
                Require(migratedDirectedFear != null && migratedDirectedFear.HasFearRecoveryAnchor &&
                    migratedDirectedFear.StoredInterpersonalFear == fearBeforeSave.StoredInterpersonalFear &&
                    migratedDirectedFear.ProjectedInterpersonalFear == projectedFearBeforeSave.ProjectedInterpersonalFear &&
                    migratedDirectedFear.FearRecoveryAnchorGameTime == fearBeforeSave.FearRecoveryAnchorGameTime &&
                    Math.Abs(migratedDirectedFear.FearRecoveryHalfLifeSeconds - fearBeforeSave.FearRecoveryHalfLifeSeconds) <= 0.000001 &&
                    Math.Abs(migratedDirectedFear.FearRecoveryBaselineRemainder - fearBeforeSave.FearRecoveryBaselineRemainder) <= 0.000000000001 &&
                    migratedDirectedFear.ReliabilityTrust == fearBeforeSave.ReliabilityTrust &&
                    migratedDirectedFear.Resentment == fearBeforeSave.Resentment &&
                    migratedDirectedFear.Revision == fearBeforeSave.Revision,
                    "Schema-40 migration changed directed fear, trust, resentment or fear recovery state.");
                fearUpdate.ExpectedLedgerRevision = restored.NpcRelationshipLedgerRevision;
                fearUpdate.RelationshipUpdate.ExpectedRevision = restoredFear.Revision;
                Require(!restored.TryCommitNpcRelationship(fearUpdate, restored.NpcRelationshipLedgerRevision),
                    "Restored fear fixture accepted its original source event again.");

                Require(!restored.TryCommitNpcKnowledge(knowledgeTicket, knowledge),
                    "Pre-restore knowledge ticket was accepted by restored store.");
                VerifyStaleMotivationSource(restored, managedPersonId, managedInjury,
                    motivationBeforeSave, motivationTicket, gameTime + 7);
                Require(!restored.TryCommitNpcServiceContracts(serviceTicket, serviceCandidate,
                    new[] { funding }, new NpcServiceRenewalAuthorization[0], out serviceError),
                    "Pre-restore service ticket was accepted by restored store.");
                NpcServiceSettlementNativeProbe.Verify();
                Debug.Log("NPC_FRAMEWORK_PERSISTENCE_NATIVE_PASS: source=controlled-current-schema-fixture; schema=" + WorldState.CurrentVersion + "; " +
                    "FullSerializer-tempfile-roundtrip=true; knowledge=true; source-bound-health-need-and-recovery-goal=true; " +
                    "conversation-source-cursors-and-task=true; stale-live-command-rejected=true; " +
                    "operational-retention-old-task-groups-and-steps-pruned=true; retained-dependencies-and-command-cursor-roundtrip=true; " +
                    "funded-reserved-contract=controlled-player-gold+LOC-debit; player-currency-restored=true; native-service-settlement-fixture=true; pending-health-episode=true; " +
                    "directed-fear-recovery-supplied-game-days=true; fear-clock-and-replay-retained=true; " +
                    "acute-fear-exact-identity-and-supplied-time-roundtrip=true; schema40-acute-empty-directed-fear-retained=true; " +
                    "speech-source-cursors-and-stale-ticket=true; speech-affinity-provenance-and-supplement=true; family-floor-excluded=true; legacy-affinity-unknown=true; " +
                    "idempotent-observation-and-stale-source-and-pre-restore-ticket-rejected=true; human-save=false; gameplay-receipts=false; model-quality=false.");
            }
            finally
            {
                // Delete only the exact GUID-named fixture file created by this probe.
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private static SpeechAffinityFixture PrepareSpeechAffinityFixture(WorldStateStore store, string personId, double now)
        {
            NpcObservedSpeechSession session;
            Require(store.TryBeginNpcObservedSpeechSession(personId, "framework-speech-fixture", out session),
                "Controlled registered native speaker could not begin a speech session.");
            store.SetConversationBaseline(personId, 60, now);
            int immediate = store.ApplyConversationTone(personId, "insult", "Controlled fixture insult.", now);
            Require(immediate == -10, "Controlled ordinary insult did not apply its expected offset delta.");
            NpcObservedSpeechTicket speechSource;
            Require(store.TryIssueNpcObservedSpeech(session, 1, "Controlled fixture insult.", now, out speechSource),
                "Controlled insult source was not issued.");
            var speech = speechSource.Receipt.WithAppliedAffinity(immediate);
            Require(store.TryAcceptNpcObservedSpeechAfterAppend(speechSource, speech, NpcExperienceAppliedEffectsMask.Affinity)
                == NpcObservedSpeechAcceptanceResult.Projected,
                "Controlled speech provenance was not queued.");
            NpcExperienceAppraisalTicket ticket;
            Require(store.TryCaptureNpcExperienceAppraisal(personId, now, out ticket) &&
                ticket.Request.ImmediateAffinityDelta == -10,
                "Controlled appraisal did not capture exact immediate affinity provenance.");
            BindSpeechClassification(store, ticket, "DirectInsult");
            Require(store.TryCommitNpcExperienceAppraisal(ticket, SpeechAffinityResult(ticket.Request, -12, "DirectInsult")),
                "Controlled severe-speech supplement was not committed.");
            Require(store.ConversationDisposition(personId) == -22,
                "Controlled supplement repeated or replaced the ordinary affinity effect.");

            int beforeFloor = store.ConversationDisposition(personId);
            int floorOffset = store.SetConversationThreatDisposition(personId, 60, now + 1);
            NpcObservedSpeechTicket familySource;
            Require(store.TryIssueNpcObservedSpeech(session, 2, "Controlled fixture family threat.", now + 1, out familySource),
                "Controlled family-threat source was not issued.");
            var family = familySource.Receipt.WithAppliedAffinity(floorOffset - beforeFloor,
                NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor);
            Require(store.TryAcceptNpcObservedSpeechAfterAppend(familySource, family, NpcExperienceAppliedEffectsMask.Affinity)
                == NpcObservedSpeechAcceptanceResult.Projected,
                "Controlled family-floor provenance was not queued.");
            Require(store.TryCloseNpcObservedSpeechSession(session), "Controlled speech session did not close.");
            var legacy = NpcObservedSpeechReceipt.Create(personId, 102, "Controlled legacy speech.", now + 2);
            // This row deliberately represents historical unknown provenance. It
            // is imported only into this detached serializer fixture, not admitted
            // as a newly observed speech source through a production entry point.
            WorldState historical = store.Snapshot();
            var historicalEntry = NpcExperienceInboxEntry.FromObservedSpeech(
                legacy.Evidence, legacy.Turn, NpcExperienceAppliedEffectsMask.Affinity);
            historicalEntry.EnqueueOrdinal = historical.NpcExperienceInbox.Revision + 1;
            historical.NpcExperienceInbox.Entries.Add(historicalEntry);
            historical.NpcExperienceInbox.Revision++;
            historical.NpcExperienceInbox.DetailRevision++;
            store.Restore(historical);
            return new SpeechAffinityFixture { Supplement = speech, SupplementTicket = speechSource,
                FamilyReceiptId = family.Evidence.ReceiptId, LegacyReceiptId = legacy.Evidence.ReceiptId };
        }

        private static void VerifySpeechAffinityFixture(WorldStateStore store, string personId, double now,
            SpeechAffinityFixture fixture)
        {
            WorldState savedSpeech = store.Snapshot();
            var source = savedSpeech.NpcObservedSpeechSources.Find(x => x.PersonId == personId);
            Require(source != null && source.LastIssuedOrdinal == 2 && source.LastAcceptedOrdinal == 2 &&
                source.LastProjectedOrdinal == 2 && source.Pending == null,
                "Native serializer changed the two accepted speech-source cursors.");
            var entries = savedSpeech.NpcExperienceInbox.Entries;
            var supplement = entries.Find(x => x.ObserverId == personId && x.ReceiptId == fixture.Supplement.Evidence.ReceiptId);
            var family = entries.Find(x => x.ObserverId == personId && x.ReceiptId == fixture.FamilyReceiptId);
            var legacy = entries.Find(x => x.ObserverId == personId && x.ReceiptId == fixture.LegacyReceiptId);
            Require(supplement != null && supplement.HasKnownImmediateAffinityDelta && supplement.ImmediateAffinityDelta == -10 &&
                supplement.Status == NpcExperienceInboxStatus.Consumed && supplement.Appraisal != null &&
                supplement.Appraisal.AppliedAffinityDelta == -12,
                "Native serializer changed consumed supplemental-affinity evidence.");
            Require(family != null && family.HasKnownImmediateAffinityDelta && family.ImmediateAffinityDelta == -138 &&
                family.AffinityExclusion == NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor,
                "Native serializer changed the exact family-floor offset delta or exclusion.");
            Require(legacy != null && !legacy.HasKnownImmediateAffinityDelta && legacy.ImmediateAffinityDelta == 0 &&
                legacy.AffinityExclusion == NpcObservedSpeechAffinityExclusion.None,
                "Native serializer invented an immediate affinity amount for legacy evidence.");
            Require(!store.TryEnqueueNpcObservedSpeech(fixture.Supplement, NpcExperienceAppliedEffectsMask.Affinity),
                "Generic speech entry point accepted a caller-supplied receipt.");
            Require(store.TryAcceptNpcObservedSpeechAfterAppend(fixture.SupplementTicket, fixture.Supplement,
                NpcExperienceAppliedEffectsMask.Affinity) == NpcObservedSpeechAcceptanceResult.Rejected,
                "Restored speech source accepted its pre-restore ticket again.");
            NpcExperienceAppraisalTicket ticket;
            Require(store.TryCaptureNpcExperienceAppraisal(personId, now + 2, out ticket) &&
                ticket.Request.AffinityExclusion == NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor,
                "Restored family-floor receipt was not the next captured speech.");
            BindSpeechClassification(store, ticket, "DirectThreat");
            Require(!store.TryCommitNpcExperienceAppraisal(ticket, SpeechAffinityResult(ticket.Request, -1, "DirectThreat")),
                "Restored family-floor receipt accepted another affinity penalty.");
            Require(store.TryCommitNpcExperienceAppraisal(ticket, SpeechAffinityResult(ticket.Request, 0, "DirectThreat")),
                "Family-floor receipt could not finish its separate appraisal.");
            int actual;
            Require(store.TryGetActualConversationDisposition(personId, out actual) && actual == -100,
                "Family-floor appraisal changed the native -100 disposition.");
        }

        private static void BindSpeechClassification(WorldStateStore store,
            NpcExperienceAppraisalTicket ticket, string speechAct)
        {
            NpcSpeechActClassification classification;
            Require(NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.ForAppraisal(ticket.Request),
                speechAct, out classification), "Controlled speech classification was not accepted.");
            Require(store.TryBindNpcExperienceSpeechClassification(ticket, classification),
                "Controlled speech classification was not bound to its appraisal request.");
        }

        private static string SpeechAffinityResult(NpcExperienceAppraisalRequest r, int affinity, string speechAct)
        {
            // These identifiers come only from this controlled native fixture, not model or player input.
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
                ",\"saveBranchId\":\"" + r.SaveBranchId + "\",\"observerId\":\"" + r.ObserverId +
                "\",\"subjectPersonId\":\"" + r.SubjectPersonId + "\",\"evidenceDeduplicationKey\":\"" + r.EvidenceDeduplicationKey +
                "\",\"canonicalEvidenceId\":\"" + r.CanonicalEvidenceId + "\",\"relationshipTargetId\":\"" + r.RelationshipTargetId +
                "\",\"expectedMoodRevision\":" + r.ExpectedMoodRevision + ",\"expectedAffinityRevision\":" + r.ExpectedAffinityRevision +
                ",\"expectedRelationshipRevision\":" + r.ExpectedRelationshipRevision +
                ",\"moodDelta\":0,\"affinityDelta\":" + affinity + ",\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":0," +
                "\"speechActKind\":\"" + speechAct + "\",\"speechTargetPersonId\":\"" + r.ObserverId + "\",\"conversationOption\":\"NoAction\"}";
        }

        private static string FearValues(NpcDirectedRelationshipView value)
        {
            if(value == null)return "null";
            return "stored="+value.StoredInterpersonalFear+",projected="+value.ProjectedInterpersonalFear+
                ",anchorKnown="+value.HasFearRecoveryAnchor+",anchor="+
                value.FearRecoveryAnchorGameTime.ToString("R",CultureInfo.InvariantCulture)+",halfLife="+
                value.FearRecoveryHalfLifeSeconds.ToString("R",CultureInfo.InvariantCulture)+",remainder="+
                value.FearRecoveryBaselineRemainder.ToString("R",CultureInfo.InvariantCulture)+
                ",trust="+value.ReliabilityTrust+",resentment="+value.Resentment+",revision="+value.Revision;
        }

        private static void VerifyKnowledge(WorldStateStore store, string personId)
        {
            NpcKnowledgeSnapshot snapshot;
            Require(store.TryCaptureNpcKnowledgeSnapshot(personId, out snapshot) && snapshot != null && snapshot.Revision == 1,
                "Restored knowledge ledger revision differs.");
            NpcKnowledgeFactView fact;
            Require(snapshot.TryGet("fixture.home", out fact) && fact != null &&
                fact.UpdateReceiptId == "fixture-knowledge-receipt-1" && fact.Statement.ValueId == "Lives in the fixture home" &&
                fact.EvidenceOrigin == NpcKnowledgeEvidenceOrigin.NativeObservation,
                "Restored knowledge fact or provenance differs.");
        }

        private static OperationalRetentionFixture PrepareOperationalRetentionFixture(WorldStateStore store,
            int settlementId, int buildingKey, double now)
        {
            // Separate managed resident keeps the existing two-turn command fixture unchanged.
            SettlementPopulation population = store.GetOrCreate(settlementId, new[] { buildingKey }, 1);
            Require(population.Households.Count == 1 && population.Households[0].Residents.Count == 1,
                "Controlled operational-retention resident was not created.");
            string personId = population.Households[0].Residents[0].Id;
            string sessionId = "fixture-retention-session";
            NpcConversationCommandSession session;
            Require(store.TryBeginNpcConversationCommandSession(personId, sessionId, out session),
                "Controlled retention command session did not open.");
            var fixture = new OperationalRetentionFixture { PersonId = personId, TaskIds = new string[10] };
            for (int index = 0; index < fixture.TaskIds.Length; index++)
            {
                double acceptedAt = now + 100 + index * 20;
                NpcConversationCommandTurn turn;
                Require(store.TryCaptureNpcConversationCommandTurn(session,
                    new ConversationContextBinding(ConversationActorCategory.Mobile, personId, null,
                        sessionId, index + 1, 1, store.NpcFrameworkScopeId, "", "fixture-retention-scene", 1),
                    "Wait here for this controlled fixture task.", out turn),
                    "Controlled retention command turn was not captured.");
                if (index == 0) fixture.FirstTurn = turn;
                string proposalId = "fixture-retention-task-" + index.ToString(CultureInfo.InvariantCulture);
                var proposed = new NpcTaskPlan { Id = proposalId, ProposalId = proposalId,
                    ResidentId = personId, Phase = NpcTaskPhase.Ready, Revision = 1,
                    AcceptedAt = acceptedAt, UpdatedAt = acceptedAt, DeadlineAt = acceptedAt + 500 };
                proposed.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
                    Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
                NpcTaskPlan task;
                Require(store.TryPrepareNpcConversationTask(turn, proposed, out task),
                    "Controlled retention task lacked classified source.");
                NpcConversationCommandCapability capability;
                Require(store.TryIssueNpcConversationTask(turn, task, out capability),
                    "Controlled retention task capability was not issued.");
                string reason;
                Require(store.TryAcceptNpcTaskFromConversation(capability, task, true, out reason),
                    "Controlled retention task was not admitted: " + reason);
                fixture.TaskIds[index] = task.Id;
                double stepAt = acceptedAt + 1;
                string stepId = task.Id + ":step:0";
                var step = new NpcActionJob { Id = stepId, ProposalId = stepId,
                    ResidentId = personId, Kind = NpcActionKind.Wait,
                    Target = NpcActionExecutor.CopyTarget(task.Steps[0].Target),
                    Phase = NpcActionPhase.Accepted, Revision = 1,
                    AcceptedAt = stepAt, UpdatedAt = stepAt, FinishAt = stepAt + 10,
                    DeadlineAt = stepAt + 100 };
                Require(store.TryDispatchNpcTaskStep(task.Id, task.Revision, stepAt, step,
                    true, true, true, out reason),
                    "Controlled retention task step did not dispatch: " + reason);
                NpcTaskPlan running = store.ReadNpcTask(task.Id);
                Require(running != null && running.Phase == NpcTaskPhase.Running &&
                    running.ActiveActionId == stepId && store.ReadNpcAction(stepId) != null,
                    "Controlled retention task and step were not bound.");
                Require(store.CancelNpcTask(task.Id, running.Revision, acceptedAt + 2,
                    "Controlled fixture task stopped."),
                    "Controlled retention task and step did not reach terminal state.");
                Require(store.ReadNpcTask(task.Id).Phase == NpcTaskPhase.Cancelled &&
                    store.ReadNpcAction(stepId).Phase == NpcActionPhase.Cancelled,
                    "Controlled retention terminal task/step pair was not saved.");
            }
            Require(store.TryCloseNpcConversationCommandSession(session),
                "Controlled retention command session did not close.");
            NpcOperationalHistoryRetentionResult sweep = store.MaintainNpcOperationalHistory(
                now + 31d * 86400d, 64, 32);
            Require(sweep.Pressure == NpcOperationalHistoryPressure.None &&
                sweep.RemovedTaskGroups == 2 && sweep.RemovedStepActions == 2 &&
                sweep.RemovedStandaloneActions == 0,
                "Controlled retention did not prune exactly two eligible old task/step groups.");
            VerifyOperationalRetentionRows(store, fixture);
            return fixture;
        }

        private static void VerifyOperationalRetentionFixture(WorldStateStore restored,
            OperationalRetentionFixture fixture, double now)
        {
            VerifyOperationalRetentionRows(restored, fixture);
            var cursor = restored.Snapshot().NpcConversationCommandCursors.Find(x => x.PersonId == fixture.PersonId);
            Require(cursor != null && cursor.LastIssuedOrdinal == fixture.TaskIds.Length &&
                cursor.LastAcceptedOrdinal == fixture.TaskIds.Length,
                "DFU FullSerializer lost consumed command-source cursor after task detail eviction.");
            NpcConversationCommandCapability stale;
            Require(!restored.TryIssueNpcConversationTask(fixture.FirstTurn,
                ConversationCommandFixtureTask(fixture.PersonId, now), out stale),
                "Restored store accepted old live turn after its source detail was pruned.");
            NpcConversationCommandSession newSession;
            Require(restored.TryBeginNpcConversationCommandSession(fixture.PersonId,
                "fixture-retention-reopened", out newSession),
                "Restored retention resident could not open a fresh command session.");
            NpcConversationCommandTurn nextTurn;
            Require(restored.TryCaptureNpcConversationCommandTurn(newSession,
                new ConversationContextBinding(ConversationActorCategory.Mobile, fixture.PersonId, null,
                    newSession.SessionId, 1, 1, restored.NpcFrameworkScopeId, "", "fixture-retention-scene", 1),
                "A new command after operational retention.", out nextTurn) &&
                nextTurn.Ordinal == fixture.TaskIds.Length + 1,
                "Pruned command source ordinal was reused after DFU FullSerializer restore.");
            Require(restored.TryCloseNpcConversationCommandSession(newSession),
                "Restored retention command session did not close.");
        }

        private static void VerifyOperationalRetentionRows(WorldStateStore store,
            OperationalRetentionFixture fixture)
        {
            for (int index = 0; index < fixture.TaskIds.Length; index++)
            {
                string taskId = fixture.TaskIds[index];
                NpcTaskPlan task = store.ReadNpcTask(taskId);
                NpcActionJob step = store.ReadNpcAction(taskId + ":step:0");
                if (index < 2)
                    Require(task == null && step == null,
                        "Evicted operational task retained its detail or orphaned step action.");
                else
                    Require(task != null && step != null && task.Phase == NpcTaskPhase.Cancelled &&
                        task.ActiveActionId == step.Id && step.Phase == NpcActionPhase.Cancelled &&
                        step.Admission != null && step.Admission.Kind == NpcOperationAdmissionKind.TaskStep &&
                        step.Admission.SourceId == task.Id,
                        "Retained operational task lost its classified step dependency.");
            }
        }

        private static NpcConversationCommandTurn PrepareConversationCommandFixture(WorldStateStore store,
            string personId, double now, out string acceptedTaskId)
        {
            acceptedTaskId = null;
            NpcConversationCommandSession session;
            Require(store.TryBeginNpcConversationCommandSession(personId, "fixture-command-session", out session),
                "Controlled command session did not open.");
            NpcConversationCommandTurn first;
            Require(store.TryCaptureNpcConversationCommandTurn(session,
                new ConversationContextBinding(ConversationActorCategory.Mobile, personId, null,
                    session.SessionId, 1, 1, new string('a', 32), "", "fixture-command-scene", 1),
                "Wait here.", out first), "Controlled first command turn was not captured.");
            NpcTaskPlan task;
            Require(store.TryPrepareNpcConversationTask(first, ConversationCommandFixtureTask(personId, now), out task),
                "Controlled command task was not source-stamped.");
            NpcConversationCommandCapability capability;
            Require(store.TryIssueNpcConversationTask(first, task, out capability), "Controlled command was not issued.");
            string reason;
            Require(store.TryAcceptNpcTaskFromConversation(capability, task, true, out reason),
                "Controlled command was not accepted: " + reason);
            acceptedTaskId = task.Id;
            NpcConversationCommandTurn pending;
            Require(store.TryCaptureNpcConversationCommandTurn(session,
                new ConversationContextBinding(ConversationActorCategory.Mobile, personId, null,
                    session.SessionId, 2, 1, new string('a', 32), "", "fixture-command-scene", 1),
                "Wait a little longer.", out pending), "Controlled pending command turn was not captured.");
            return pending;
        }

        private static NpcTaskPlan ConversationCommandFixtureTask(string personId, double now)
        {
            var task = new NpcTaskPlan { Id = "fixture-command-task", ProposalId = "fixture-command-task",
                ResidentId = personId, Phase = NpcTaskPhase.Ready, Revision = 1,
                AcceptedAt = now, UpdatedAt = now, DeadlineAt = now + 500 };
            task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 10,
                Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" } });
            return task;
        }

        private static void VerifyConversationCommandFixture(WorldStateStore restored, string personId,
            NpcConversationCommandTurn oldTurn, string acceptedTaskId, double now)
        {
            var cursor = restored.Snapshot().NpcConversationCommandCursors.Find(x => x.PersonId == personId);
            Require(cursor != null && cursor.LastIssuedOrdinal == 2 && cursor.LastAcceptedOrdinal == 1 &&
                acceptedTaskId != null && restored.ReadNpcTask(acceptedTaskId) != null,
                "DFU FullSerializer changed command source cursors or the admitted task.");
            NpcConversationCommandCapability capability;
            Require(!restored.TryIssueNpcConversationTask(oldTurn, ConversationCommandFixtureTask(personId, now), out capability),
                "Restored store accepted a pre-load live command capability.");
        }

        private static void VerifyMotivation(WorldStateStore store, string personId,
            NpcMotivationStateView expected, NpcVitalsView sourceVitals)
        {
            NpcMotivationStateView actual = store.ReadNpcMotivationState(personId);
            int intensity = (int)((100L * (sourceVitals.MaximumHealth - sourceVitals.CurrentHealth) +
                sourceVitals.MaximumHealth - 1) / sourceVitals.MaximumHealth);
            string sourceSuffix = sourceVitals.Revision.ToString(CultureInfo.InvariantCulture) + ":" +
                sourceVitals.CurrentHealth.ToString(CultureInfo.InvariantCulture) + ":" +
                sourceVitals.MaximumHealth.ToString(CultureInfo.InvariantCulture);
            Require(expected != null && actual != null && actual.OwnerPersonId == personId &&
                actual.Revision == 2 && actual.Revision == expected.Revision &&
                actual.LastGameTime == expected.LastGameTime && actual.Needs.Count == 1 &&
                actual.Goals.Count == 1 && actual.Priorities.Count == 0 && actual.AcceptedUpdates.Count == 2,
                "Source-bound motivation owner, counts, receipt count, time or revision differ.");
            NpcMotivationNeedView need = actual.Needs[0];
            NpcMotivationNeedView expectedNeed = expected.Needs[0];
            Require(need.Id == NpcHealthNeedLifecycle.HealthNeedId && need.Kind == NpcMotivationNeedKind.Health &&
                need.KnowledgeState == NpcMotivationKnowledgeState.Known && need.Intensity == intensity &&
                need.EvidenceKind == NpcMotivationStateEvidenceKind.NativeObservation &&
                need.EvidenceId == "native-health:v1:need:" + sourceSuffix &&
                need.Id == expectedNeed.Id && need.Kind == expectedNeed.Kind &&
                need.KnowledgeState == expectedNeed.KnowledgeState && need.Intensity == expectedNeed.Intensity &&
                need.Revision == expectedNeed.Revision && need.UpdatedAtGameTime == expectedNeed.UpdatedAtGameTime &&
                need.EvidenceId == expectedNeed.EvidenceId && need.EvidenceKind == expectedNeed.EvidenceKind,
                "Saved native-health need or exact source evidence changed.");
            NpcMotivationGoalView goal = actual.Goals[0];
            NpcMotivationGoalView expectedGoal = expected.Goals[0];
            Require(goal.Id == NpcHealthNeedLifecycle.RecoveryGoalId && goal.Kind == NpcMotivationGoalKind.Recovery &&
                goal.Status == NpcMotivationGoalStatus.Active && goal.Priority == NpcMotivationPriorityLevel.Urgent &&
                goal.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                goal.EvidenceKind == NpcMotivationStateEvidenceKind.NativeObservation &&
                goal.EvidenceId == "native-health:v1:goal:" + sourceSuffix &&
                goal.Id == expectedGoal.Id && goal.Kind == expectedGoal.Kind &&
                goal.Status == expectedGoal.Status && goal.Priority == expectedGoal.Priority &&
                goal.KnowledgeState == expectedGoal.KnowledgeState && goal.Revision == expectedGoal.Revision &&
                goal.GoalStartRevision == expectedGoal.GoalStartRevision &&
                goal.UpdatedAtGameTime == expectedGoal.UpdatedAtGameTime &&
                goal.EvidenceId == expectedGoal.EvidenceId && goal.EvidenceKind == expectedGoal.EvidenceKind,
                "Saved native-recovery goal or exact source evidence changed.");
            for (int i = 0; i < expected.AcceptedUpdates.Count; i++)
            {
                NpcAcceptedMotivationUpdateView receipt = actual.AcceptedUpdates[i];
                NpcAcceptedMotivationUpdateView before = expected.AcceptedUpdates[i];
                Require(receipt.OperationId == before.OperationId && receipt.Revision == before.Revision &&
                    receipt.AppliedAtGameTime == before.AppliedAtGameTime && receipt.RowId == before.RowId &&
                    receipt.EvidenceId == before.EvidenceId && receipt.ChangeKind == before.ChangeKind,
                    "Native-health motivation receipt changed across observation or serialization.");
            }
            Require(actual.AcceptedUpdates[0].OperationId == "motivation:native-health:v1:need:" +
                    sourceVitals.Revision.ToString(CultureInfo.InvariantCulture) &&
                actual.AcceptedUpdates[1].OperationId == "motivation:native-health:v1:goal:" +
                    sourceVitals.Revision.ToString(CultureInfo.InvariantCulture) &&
                actual.AcceptedUpdates[0].RowId == NpcHealthNeedLifecycle.HealthNeedId &&
                actual.AcceptedUpdates[1].RowId == NpcHealthNeedLifecycle.RecoveryGoalId,
                "Motivation receipts were not bound to the observed saved vitals revision.");
        }

        private static void VerifyStaleMotivationSource(WorldStateStore restored, string personId,
            NpcVitalsView originalVitals, NpcMotivationStateView expected,
            NpcMotivationUpdateTicket preRestoreTicket, double now)
        {
            // Change the saved source while retaining the pre-change proposal. The new source
            // requires both a stronger health need and an emergency recovery priority.
            int newHealth = Math.Max(1, originalVitals.MaximumHealth / 4);
            Require(newHealth < originalVitals.CurrentHealth &&
                restored.TryCaptureNpcVitals(personId, NpcArchetype.Citizen, newHealth,
                    originalVitals.CurrentMagicka, now - 1, originalVitals.Revision),
                "Controlled later managed-resident vitals were not saved.");
            NpcVitalsView currentVitals = restored.ReadNpcVitals(personId, NpcArchetype.Citizen);
            Require(currentVitals.Revision > originalVitals.Revision && currentVitals.CurrentHealth == newHealth,
                "Controlled later managed-resident vitals did not advance the source revision.");
            string oldRevision = originalVitals.Revision.ToString(CultureInfo.InvariantCulture);
            string oldSuffix = oldRevision + ":" + originalVitals.CurrentHealth.ToString(CultureInfo.InvariantCulture) +
                ":" + originalVitals.MaximumHealth.ToString(CultureInfo.InvariantCulture);
            int oldIntensity = (int)((100L * (originalVitals.MaximumHealth - originalVitals.CurrentHealth) +
                originalVitals.MaximumHealth - 1) / originalVitals.MaximumHealth);
            var staleNeedEvidence = new NpcMotivationStateEvidence(personId,
                "native-health:v1:need:" + oldSuffix, NpcMotivationStateEvidenceKind.NativeObservation,
                expected.Revision + 1, now);
            var staleGoalEvidence = new NpcMotivationStateEvidence(personId,
                "native-health:v1:goal:" + oldSuffix, NpcMotivationStateEvidenceKind.NativeObservation,
                expected.Revision + 2, now);
            var staleNeed = NpcMotivationStateUpdateProposal.ForNeed(personId, expected.Revision,
                "motivation:native-health:v1:need:" + oldRevision, now,
                NpcHealthNeedLifecycle.HealthNeedId, NpcMotivationNeedKind.Health,
                NpcMotivationKnowledgeState.Known, oldIntensity, staleNeedEvidence);
            var staleGoal = NpcMotivationStateUpdateProposal.ForGoal(personId, expected.Revision + 1,
                "motivation:native-health:v1:goal:" + oldRevision, now,
                NpcHealthNeedLifecycle.RecoveryGoalId, NpcMotivationGoalKind.Recovery,
                NpcMotivationGoalStatus.Active, NpcMotivationPriorityLevel.Urgent,
                NpcMotivationKnowledgeState.Known, staleGoalEvidence);
            Require(!restored.TryCommitNativeHealthMotivationUpdatePair(preRestoreTicket,
                    NpcArchetype.Citizen, staleNeed, staleGoal),
                "Pre-restore motivation ticket was accepted by restored store.");
            NpcMotivationUpdateTicket freshTicket;
            Require(restored.TryCaptureNpcMotivationUpdate(personId, out freshTicket) &&
                freshTicket.ExpectedRevision == expected.Revision,
                "Fresh restored motivation ticket was not captured.");
            Require(!restored.TryCommitNativeHealthMotivationUpdatePair(freshTicket,
                    NpcArchetype.Citizen, staleNeed, staleGoal),
                "Current-store ticket accepted stale native-health source evidence.");
            VerifyMotivation(restored, personId, expected, originalVitals);
        }

        private static void VerifyService(WorldStateStore store, string contractId, string receiptId)
        {
            NpcServiceContract contract = store.ReadNpcServiceContracts().Read(contractId);
            Require(contract != null && contract.Status == NpcServiceContractStatus.Reserved &&
                contract.FundedGold == 120 && contract.ReleasedGold == 120 && contract.EarnedGold == 0 &&
                contract.TransferReceipts.Count == 1 && contract.TransferReceipts[0].ReceiptId == receiptId,
                "Restored funded/reserved service ledger differs.");
            NpcServiceContractWriteTicket ticket = store.CaptureNpcServiceContractWriteTicket();
            Require(ticket.SourceLedger.HasReceiptId(receiptId), "Restored save-owned service evidence receipt is missing.");
        }

        private sealed class CreditSnapshot
        {
            internal DaggerfallUnityItem Item;
            internal ulong Uid;
            internal int Value;
        }

        private static void VerifyNativeServiceFunding(WorldStateStore store, NpcServiceContractTerms terms,
            NpcServiceContractWriteTicket ticket, string personId, double gameTime, string receiptId,
            out NpcServiceTransferReceipt committedReceipt, out NpcServiceContractLedger committedCandidate)
        {
            committedReceipt = null;
            committedCandidate = null;
            GameManager game = GameManager.Instance;
            Require(game != null && game.PlayerEntity != null, "Native player currency is unavailable to funding fixture.");
            var player = game.PlayerEntity;
            int originalGoldPieces = player.GoldPieces;
            int originalGoldAmount = player.GetGoldAmount();
            int creditGroup = DaggerfallUnity.Instance.ItemHelper.GetGroupIndex(ItemGroups.MiscItems, (int)MiscItems.Letter_of_credit);
            var originalCredits = new System.Collections.Generic.List<CreditSnapshot>();
            for (int i = 0; i < player.Items.Count; i++)
            {
                DaggerfallUnityItem item = player.Items.GetItem(i);
                if (item.ItemGroup == ItemGroups.MiscItems && item.GroupIndex == creditGroup)
                    originalCredits.Add(new CreditSnapshot { Item = item, Uid = item.UID, Value = item.value });
            }

            Require(store.InitializeNpcInventory(personId,0,new NpcInventoryItem[0]),
                "Controlled service wallet could not be opened.");

            DaggerfallUnityItem fixtureCredit = ItemBuilder.CreateItem(ItemGroups.MiscItems, (int)MiscItems.Letter_of_credit);
            fixtureCredit.value = 200;
            try
            {
                player.GoldPieces = 10;
                foreach (CreditSnapshot credit in originalCredits) credit.Item.value = 0;
                fixtureCredit.value = 110;
                player.Items.AddItem(fixtureCredit, noStack: true);
                ulong fixtureUid = fixtureCredit.UID;

                // Force a store refusal after a debit that fully consumes and removes this LOC.
                bool observedFullyConsumedDebit = false;
                NativeNpcServiceContractFunding.CommitReservation rejectCommit =
                    delegate(WorldStateStore ignoredStore, NpcServiceContractWriteTicket ignoredTicket,
                        NpcServiceContractLedger ignoredCandidate, NpcServiceTransferReceipt ignoredReceipt,
                        out string error)
                    {
                        observedFullyConsumedDebit = player.GoldPieces == 0 && player.GetGoldAmount() == 0 &&
                            !ContainsExactItem(player.Items, fixtureCredit);
                        error = "controlled post-debit rejection";
                        return false;
                    };
                NpcServiceTransferReceipt forcedReceipt;
                NpcServiceContractLedger forcedCandidate;
                Exception forcedError;
                Require(!NativeNpcServiceContractFunding.TryReserveFromPlayerWithCommitter(store, ticket, player,
                    terms, gameTime, receiptId + "-forced-reject", rejectCommit,
                    out forcedReceipt, out forcedCandidate, out forcedError) && forcedError != null &&
                    observedFullyConsumedDebit,
                    "Forced post-debit rejection did not observe the fully consumed LOC before rollback.");
                Require(player.GoldPieces == 10 && player.GetGoldAmount() == 120 &&
                    fixtureCredit.UID == fixtureUid && fixtureCredit.value == 110 &&
                    ContainsExactItem(player.Items, fixtureCredit),
                    "Forced post-debit rejection did not restore the removed LOC object, UID, value and ownership.");
                Require(store.ReadNpcServiceContracts().Read(terms.ContractId) == null,
                    "Forced post-debit rejection unexpectedly committed contract state.");

                fixtureCredit.value = 200;

                // A ticket from another store has the same revision but wrong owner. Preflight rejects it before debit.
                var sibling = new WorldStateStore(store.Snapshot().WorldSeed);
                NpcServiceTransferReceipt rejectedReceipt;
                NpcServiceContractLedger rejectedCandidate;
                Exception rejectedError;
                Require(!NativeNpcServiceContractFunding.TryReserveFromPlayer(store, sibling.CaptureNpcServiceContractWriteTicket(),
                    player, terms, gameTime, receiptId + "-foreign-ticket", out rejectedReceipt, out rejectedCandidate, out rejectedError),
                    "Foreign-owner service ticket was accepted.");
                Require(rejectedError != null && player.GoldPieces == 10 && ContainsExactItem(player.Items, fixtureCredit) &&
                    fixtureCredit.UID == fixtureUid && fixtureCredit.value == 200 && player.GetGoldAmount() == 210,
                    "Rejected foreign-ticket funding did not restore exact player currency.");
                Require(store.ReadNpcServiceContracts().Read(terms.ContractId) == null,
                    "Foreign-ticket attempt changed current service ledger.");

                Exception fundingError;
                Require(NativeNpcServiceContractFunding.TryReserveFromPlayer(store, ticket, player, terms,
                    gameTime, receiptId, out committedReceipt, out committedCandidate, out fundingError),
                    "Controlled native service funding failed: " + (fundingError == null ? "unknown" : fundingError.ToString()));
                Require(committedReceipt != null && committedCandidate != null &&
                    committedReceipt.Amount == terms.RatePer24GameHours && committedReceipt.FromOwnerId == NpcDirectedRelationship.PlayerTargetId &&
                    committedReceipt.ToOwnerId == personId &&
                    player.GoldPieces == 10 && fixtureCredit.UID == fixtureUid && fixtureCredit.value == 80 &&
                    player.GetGoldAmount() == 90,
                    "Native funding did not debit exact mixed gold/LOC amount and preserve remaining LOC identity.");

                NpcServiceContractWriteTicket replayTicket = store.CaptureNpcServiceContractWriteTicket();
                Require(!NativeNpcServiceContractFunding.TryReserveFromPlayer(store, replayTicket, player, terms,
                    gameTime, receiptId, out rejectedReceipt, out rejectedCandidate, out rejectedError),
                    "Service funding replay was accepted.");
                Require(player.GoldPieces == 10 && ContainsExactItem(player.Items, fixtureCredit) &&
                    fixtureCredit.UID == fixtureUid && fixtureCredit.value == 80 && player.GetGoldAmount() == 90,
                    "Rejected funding replay changed player currency.");

                NpcServiceContractTerms insufficientTerms;
                Require(NpcServiceContractTerms.TryCreate(terms.ContractId + "-insufficient", NpcDirectedRelationship.PlayerTargetId,
                    personId, terms.HomeReference, terms.Role, terms.Coverage, 120, false, NpcServiceRiskScope.None,
                    new NpcServiceProtectedTarget[0], NpcServiceRestTreatment.RestNotIncluded,
                    NpcServiceResupplyResponsibility.SeparatelyNegotiated, new NpcServiceReservedObligation[0],
                    id => id == NpcDirectedRelationship.PlayerTargetId || id == personId,
                    id => id == terms.HomeReference, (id, category, revision) => false,
                    (id, revision, kind, starts, ends) => false, out insufficientTerms),
                    "Insufficient-funds terms were rejected before test.");
                player.GoldPieces = 0;
                fixtureCredit.value = 0;
                NpcServiceContractWriteTicket insufficientTicket = store.CaptureNpcServiceContractWriteTicket();
                Require(!NativeNpcServiceContractFunding.TryReserveFromPlayer(store, insufficientTicket, player,
                    insufficientTerms, gameTime, receiptId + "-insufficient", out rejectedReceipt,
                    out rejectedCandidate, out rejectedError), "Insufficient native funds were accepted.");
                Require(player.GoldPieces == 0 && fixtureCredit.UID == fixtureUid && fixtureCredit.value == 0 &&
                    player.GetGoldAmount() == 0 && store.ReadNpcServiceContracts().Read(insufficientTerms.ContractId) == null,
                    "Insufficient-funds rejection changed currency or service ledger.");
            }
            finally
            {
                player.Items.RemoveItem(fixtureCredit);
                player.GoldPieces = originalGoldPieces;
                foreach (CreditSnapshot credit in originalCredits)
                {
                    credit.Item.value = credit.Value;
                    if (!ContainsExactItem(player.Items, credit.Item)) player.Items.AddItem(credit.Item, noStack: true);
                    Require(credit.Item.UID == credit.Uid && ContainsExactItem(player.Items, credit.Item),
                        "Native funding fixture could not restore original LOC identity/ownership.");
                }
                Require(!ContainsExactItem(player.Items, fixtureCredit) && player.GoldPieces == originalGoldPieces &&
                    player.GetGoldAmount() == originalGoldAmount,
                    "Native funding fixture did not restore exact player currency after acceptance checks.");
            }
            NpcServiceContract funded = store.ReadNpcServiceContracts().Read(terms.ContractId);
            Require(funded != null && funded.Status == NpcServiceContractStatus.Reserved &&
                funded.FundedGold == terms.RatePer24GameHours && funded.ReleasedGold == terms.RatePer24GameHours &&
                funded.TransferReceipts.Count == 1 && funded.TransferReceipts[0].ReceiptId == receiptId,
                "Actual controlled player debit did not persist exact reserved escrow evidence.");
        }

        private static void VerifyPendingHealth(WorldStateStore store, string personId, NpcActorProgress expected)
        {
            NpcActorProgress actual = store.ReadNpcProgress(personId);
            Require(actual != null && actual.HasPendingHealthEpisode &&
                actual.HealthEpisodeInitialHealth == expected.HealthEpisodeInitialHealth &&
                actual.HealthEpisodeFinalHealth == expected.HealthEpisodeFinalHealth &&
                actual.HealthEpisodeDamageTotal == expected.HealthEpisodeDamageTotal &&
                actual.HealthEpisodeFirstVitalsRevision == expected.HealthEpisodeFirstVitalsRevision &&
                actual.HealthEpisodeLastVitalsRevision == expected.HealthEpisodeLastVitalsRevision &&
                actual.VitalsRevision == expected.VitalsRevision,
                "Restored pending health episode or vitals revision differs.");
        }

        private static bool ContainsExactItem(ItemCollection items, DaggerfallUnityItem expected)
        {
            if (items == null || expected == null) return false;
            for (int i = 0; i < items.Count; i++)
                if (ReferenceEquals(items.GetItem(i), expected)) return true;
            return false;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
