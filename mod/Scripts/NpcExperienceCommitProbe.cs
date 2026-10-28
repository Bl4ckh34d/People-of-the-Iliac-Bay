using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Controlled Core-store fixture for appraisal commit and native FullSerializer
    /// persistence. Verify never touches the player's active save, native NPCs,
    /// dialogue transport, or a model. Transport probe requires an explicit flag.
    /// </summary>
    internal static class NpcExperienceCommitProbe
    {
        private const string TransportProbeCharacter = "Hakon Ironhand";
        private const string TransportProbeSave = "People - Gothway Garden";

        // Called by TownSmokeHarness only after it loads the exact owned fixture.
        // The model request and commit stay in an isolated in-memory store.
        internal static IEnumerator RunTransportProbe(string character, string fixtureSave,
            LocalDialogueClient client, LocalDialogueConnection connection)
        {
            Require(character == TransportProbeCharacter && fixtureSave == TransportProbeSave,
                "Appraisal transport probe requires exact owned fixture character and save.");
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-experience-appraisal-transport-probe") >= 0,
                "Appraisal transport probe requires its explicit command-line flag.");
            Require(client != null && connection != null, "Appraisal transport client is unavailable.");
            GameManager game = GameManager.Instance;
            Require(game != null && game.IsReady && game.IsPlayingGame() && game.PlayerEntity != null &&
                game.PlayerEntity.Name == TransportProbeCharacter,
                "Appraisal transport probe requires loaded owned fixture character.");
            Require(game.SaveLoadManager.FindSaveFolderByNames(character, fixtureSave) >= 0,
                "Appraisal transport probe requires the exact owned fixture save.");

            WorldStateStore store;
            NpcExperienceAppraisalTicket ticket;
            CreateTransportFixture(out store, out ticket);
            DialogueCompletion expected = LocalDialogueCompletion.BuildAppraisal(connection, ticket.Request);
            DialogueCompletion oldGeneric = LocalDialogueCompletion.Classify(connection, ticket.Request.Prompt,
                string.Empty, null, 512);
            string expectedGrammarMarker = GrammarMarker(expected.JsonBody);
            string genericGrammarMarker = GrammarMarker(oldGeneric.JsonBody);
            bool traceReceived = false;
            bool exactBody = false;
            bool boundGrammar = false;
            bool genericGrammarAbsent = false;
            bool responseParsed = false;
            bool committed = false;
            int backgroundTicket = 0;

            backgroundTicket = client.TryAppraiseNpcExperienceBackground(connection, ticket.Request,
                response =>
                {
                    NpcExperienceAppraisal appraisal;
                    responseParsed = !string.IsNullOrWhiteSpace(response) &&
                        NpcExperienceAppraisalContract.TryParseResponse(ticket.Request, response, out appraisal) && appraisal != null;
                    committed = responseParsed && store.TryCommitNpcExperienceAppraisal(ticket, response);
                    Debug.Log("EXPERIENCE_APPRAISAL_TRANSPORT_PROBE: stage=response; parsed=" + responseParsed +
                        "; committedToIsolatedStore=" + committed + "; playerSaveWrites=false.");
                },
                (requestJson, responseBody, extracted) =>
                {
                    traceReceived = true;
                    exactBody = string.Equals(expected.JsonBody, requestJson, StringComparison.Ordinal);
                    boundGrammar = expectedGrammarMarker.Length > 0 &&
                        requestJson.IndexOf(expectedGrammarMarker, StringComparison.Ordinal) >= 0;
                    genericGrammarAbsent = genericGrammarMarker.Length > 0 &&
                        requestJson.IndexOf(genericGrammarMarker, StringComparison.Ordinal) < 0;
                    Debug.Log("EXPERIENCE_APPRAISAL_TRANSPORT_PROBE: stage=request; exactBoundBody=" + exactBody +
                        "; requestBoundGrammar=" + boundGrammar + "; genericClassifierGrammarAbsent=" + genericGrammarAbsent +
                        "; bodySha256=" + Sha256(requestJson) + "; expectedBodySha256=" + Sha256(expected.JsonBody) +
                        "; responseCharacters=" + (extracted == null ? 0 : extracted.Length) + ".");
                });
            Require(backgroundTicket != 0, "Appraisal transport request could not take background dispatch slot.");
            float deadline = Time.realtimeSinceStartup + Math.Max(5f, connection.TimeoutMilliseconds / 1000f + 5f);
            while (!responseParsed && !committed && Time.realtimeSinceStartup < deadline &&
                client.IsBackgroundCurrent(backgroundTicket))
                yield return null;
            if (client.IsBackgroundCurrent(backgroundTicket)) client.CancelBackground(backgroundTicket);
            Require(traceReceived && exactBody && boundGrammar && genericGrammarAbsent,
                "Actual appraisal transport did not send exact request-bound completion body.");
            Require(responseParsed && committed,
                "Actual appraisal response did not parse and commit to isolated fixture store.");
            Debug.Log("EXPERIENCE_APPRAISAL_TRANSPORT_PROBE_PASS: ownedFixture=true; exactBoundBody=true; " +
                "requestBoundGrammar=true; genericClassifierGrammarAbsent=true; modelParsed=true; isolatedCommit=true; playerSaveWrites=false.");
        }

        internal static void Verify()
        {
            const uint seed = 0x4E504358;
            const int settlementId = 71001;
            const int buildingKey = 81001;
            const int nativeHash = 91001;
            const double observedAt = 12345d;
            const string playerId = NpcDirectedRelationship.PlayerTargetId;
            string personId = string.Format(CultureInfo.InvariantCulture, "npc:{0}:{1}:{2}", settlementId, buildingKey, nativeHash);
            string tempPath = Path.Combine(Path.GetTempPath(), "poti-experience-appraisal-" + Guid.NewGuid().ToString("N") + ".json");

            try
            {
                // This store is a fresh controlled fixture, independent of any human save.
                var store = new WorldStateStore(seed);
                store.GetOrCreate(settlementId, new[] { buildingKey }, 1);
                var native = store.RegisterNativePerson(settlementId, buildingKey, nativeHash, "Fixture NPC",
                    ResidentSex.Female, 1, 1, 0, 0);
                Require(native.Id == personId, "Controlled native identity was not stable.");
                store.GetOrCreateNpcPersonality(personId, seed, NpcSocialBackground.Commoner);
                store.SetConversationBaseline(personId, 0, observedAt);
                store.RecordConversationEncounter(personId, observedAt);

                var evidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                    "fixture-receipt-001", "fixture-episode-001", "fixture-outcome-001",
                    NpcExperienceEventKind.ObligationFulfilled, observedAt,
                    NpcResponsibleActorAttribution.Known, playerId);
                NpcProbeLegacyExperienceFixture.Import(store, evidence);
                NpcExperienceAppraisalTicket ticket;
                Require(store.TryCaptureNpcExperienceAppraisal(personId, observedAt, out ticket) && ticket != null,
                    "Controlled appraisal ticket was not captured.");
                Require(ticket.Request.RelationshipTargetId == playerId,
                    "Controlled ticket did not bind the player as the known responsible actor.");

                string response = ControlledResponse(ticket.Request, moodDelta: 2, affinityDelta: 1, trustDelta: 2);
                Require(store.TryCommitNpcExperienceAppraisal(ticket, response), "Controlled typed appraisal did not commit.");
                Require(!store.TryCommitNpcExperienceAppraisal(ticket, response), "Consumed appraisal ticket applied more than once.");
                RequireCommittedEffects(store, personId, "first commit");

                WorldState savedState = store.Snapshot();
                Require(savedState.NpcExperienceInbox.Entries.Count == 1 &&
                    savedState.NpcExperienceInbox.Entries[0].Status == NpcExperienceInboxStatus.Consumed,
                    "Committed native outcome was not retained as consumed evidence.");

                // DFU SaveLoadManager uses its native FullSerializer fsSerializer/fsJsonParser.
                string serialized = SaveLoadManager.Serialize(typeof(WorldState), savedState);
                File.WriteAllText(tempPath, serialized);
                string jsonFromDisk = File.ReadAllText(tempPath);
                var decoded = SaveLoadManager.Deserialize(typeof(WorldState), jsonFromDisk, true) as WorldState;
                Require(decoded != null, "Native FullSerializer did not deserialize the controlled WorldState JSON.");
                var restored = new WorldStateStore(seed);
                restored.Restore(decoded);

                RequireCommittedEffects(restored, personId, "FullSerializer restore");
                var restoredEntry = restored.Snapshot().NpcExperienceInbox.Entries[0];
                Require(restoredEntry.Appraisal != null && restoredEntry.Appraisal.MoodDelta == 2 &&
                    restoredEntry.Appraisal.AffinityDelta == 1 && restoredEntry.Appraisal.TrustDelta == 2 &&
                    restoredEntry.Appraisal.AppliedMoodDelta == 2 && restoredEntry.Appraisal.AppliedAffinityDelta == 1,
                    "Native FullSerializer did not preserve the consumed appraisal receipt.");
                Require(restored.ReadPendingNpcExperiences(personId, 10).Count == 0,
                    "Consumed outcome reappeared as pending after restore.");
                Require(!restored.TryCaptureNpcExperienceAppraisal(personId, observedAt, out _),
                    "Consumed outcome produced a duplicate appraisal ticket after restore.");
                Require(!restored.TryEnqueueNpcExperience(evidence),
                    "Public native-event admission accepted a caller-constructed outcome after restore.");
                Require(!restored.TryCommitNpcExperienceAppraisal(ticket, response),
                    "Pre-restore ticket was accepted by the restored store.");
                RequireCommittedEffects(restored, personId, "duplicate and stale-ticket rejection");

                Debug.Log("EXPERIENCE_APPRAISAL_NATIVE_SERIALIZATION_PASS: source=controlled-Core-fixture; independent-save=true; " +
                    "typed-mood-affinity-trust-commit=true; apply-once=true; chat-sequence-preserved=true; " +
                    "native-FullSerializer-tempfile-roundtrip=true; consumed-appraisal-ticket=unavailable; public-native-admission=rejected; old-ticket=rejected; " +
                    "physical-event=false; model-quality=false.");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        internal static void VerifyDeferredInboxSerialization()
        {
            const uint seed = 0x4E504358;
            const int settlementId = 71011;
            const int buildingKey = 81011;
            const int nativeHash = 91011;
            const double firstObservedAt = 22345d;
            string personId = string.Format(CultureInfo.InvariantCulture, "npc:{0}:{1}:{2}", settlementId, buildingKey, nativeHash);
            string tempPath = Path.Combine(Path.GetTempPath(), "poti-experience-deferred-inbox-" + Guid.NewGuid().ToString("N") + ".json");

            try
            {
                var store = new WorldStateStore(seed);
                store.GetOrCreate(settlementId, new[] { buildingKey }, 1);
                store.RegisterNativePerson(settlementId, buildingKey, nativeHash, "Deferred Fixture NPC",
                    ResidentSex.Female, 1, 1, 0, 0);
                store.GetOrCreateNpcPersonality(personId, seed, NpcSocialBackground.Commoner);
                store.SetConversationBaseline(personId, 0, firstObservedAt);
                store.RecordConversationEncounter(personId, firstObservedAt);

                NpcExperienceEvent first = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                    "fixture-deferred-receipt-001", "fixture-deferred-episode-001", "fixture-deferred-outcome-001",
                    NpcExperienceEventKind.Healing, firstObservedAt,
                    NpcResponsibleActorAttribution.Known, NpcDirectedRelationship.PlayerTargetId);
                NpcExperienceEvent second = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                    "fixture-deferred-receipt-002", "fixture-deferred-episode-002", "fixture-deferred-outcome-002",
                    NpcExperienceEventKind.Healing, firstObservedAt + 1,
                    NpcResponsibleActorAttribution.Known, NpcDirectedRelationship.PlayerTargetId);
                NpcProbeLegacyExperienceFixture.Import(store, first, second);

                NpcExperienceAppraisalTicket firstTicket;
                Require(store.TryCaptureNpcExperienceAppraisal(personId, firstObservedAt + 1, out firstTicket) &&
                    firstTicket != null && firstTicket.Request.CanonicalEvidenceId == "fixture-deferred-outcome-001",
                    "Deferred serialization fixture did not capture the oldest event.");
                Require(store.TryDeferNpcExperienceAppraisal(personId, firstTicket, firstObservedAt + 2),
                    "Deferred serialization fixture did not persist the terminal deferral.");

                WorldState beforeRoundTrip = store.Snapshot();
                Require(beforeRoundTrip.NpcExperienceInbox.Revision == 2,
                    "Deferred fixture changed retained canonical inbox history revision.");
                NpcExperienceInboxEntry originalDeferred = FindExperienceEntry(beforeRoundTrip.NpcExperienceInbox,
                    "fixture-deferred-outcome-001");
                RequireDeferredRecord(originalDeferred, first, firstObservedAt + 2, "before FullSerializer");
                Require(beforeRoundTrip.ConversationMoods.Find(x => x.PersonId == personId) == null ||
                    beforeRoundTrip.ConversationMoods.Find(x => x.PersonId == personId).Value == 0,
                    "Deferring the first event applied mood.");

                string serialized = SaveLoadManager.Serialize(typeof(WorldState), beforeRoundTrip);
                File.WriteAllText(tempPath, serialized);
                string jsonFromDisk = File.ReadAllText(tempPath);
                var decoded = SaveLoadManager.Deserialize(typeof(WorldState), jsonFromDisk, true) as WorldState;
                Require(decoded != null, "FullSerializer did not deserialize deferred inbox fixture.");
                var restored = new WorldStateStore(seed);
                restored.Restore(decoded);

                WorldState afterRoundTrip = restored.Snapshot();
                Require(afterRoundTrip.NpcExperienceInbox.Revision == 2,
                    "FullSerializer changed retained canonical inbox history revision.");
                NpcExperienceInboxEntry restoredDeferred = FindExperienceEntry(afterRoundTrip.NpcExperienceInbox,
                    "fixture-deferred-outcome-001");
                RequireDeferredRecord(restoredDeferred, first, firstObservedAt + 2, "after FullSerializer restore");
                var pending = restored.ReadPendingNpcExperiences(personId, 10);
                Require(pending.Count == 1 && pending[0].OutcomeId == "fixture-deferred-outcome-002",
                    "Deferred receipt blocked or rearmed ahead of the later pending event.");

                NpcExperienceAppraisalTicket nextTicket;
                Require(restored.TryCaptureNpcExperienceAppraisal(personId, firstObservedAt + 3, out nextTicket) &&
                    nextTicket != null && nextTicket.Request.CanonicalEvidenceId == "fixture-deferred-outcome-002",
                    "Later pending event did not produce an appraisal ticket after restore.");
                Require(restored.ReadNpcRelationship(personId, NpcDirectedRelationship.PlayerTargetId) == null &&
                    restored.CaptureConversationMood(personId, firstObservedAt + 3).Value == 0,
                    "Deferred event changed relationship or mood during FullSerializer roundtrip.");

                Debug.Log("EXPERIENCE_APPRAISAL_DEFERRED_SERIALIZATION_PASS: ownedFixtureGate=true; detachedCoreStore=true; " +
                    "nativeFullSerializerRoundtrip=true; deferredReceiptAndEvidencePreserved=true; reason=AppraisalAttemptLimit; " +
                    "attempts=5; revision=2; appraisal=null; laterPendingCaptured=true; socialDeltas=none; modelCalls=0; playerSaveWrites=0.");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private static NpcExperienceInboxEntry FindExperienceEntry(NpcExperienceInbox inbox, string outcomeId)
        {
            foreach (NpcExperienceInboxEntry entry in inbox.Entries)
                if (entry.OutcomeId == outcomeId) return entry;
            throw new InvalidOperationException("Expected experience inbox row missing: " + outcomeId);
        }

        private static void RequireDeferredRecord(NpcExperienceInboxEntry entry, NpcExperienceEvent evidence,
            double updatedAt, string phase)
        {
            Require(entry != null && entry.Status == NpcExperienceInboxStatus.Deferred &&
                entry.DeferralReason == NpcExperienceDeferralReason.AppraisalAttemptLimit && entry.DeferralAttempts == 5 &&
                entry.Revision == 2 && entry.UpdatedAt == updatedAt && entry.Appraisal == null &&
                entry.ObserverId == evidence.ObserverId && entry.SubjectPersonId == evidence.SubjectPersonId &&
                entry.ReceiptId == evidence.ReceiptId && entry.EpisodeId == evidence.EpisodeId &&
                entry.OutcomeId == evidence.OutcomeId && entry.EventKind == evidence.EventKind &&
                entry.ObservedGameTime == evidence.ObservedGameTime &&
                entry.ResponsibleActorAttribution == evidence.ResponsibleActorAttribution &&
                entry.ResponsibleActorId == evidence.ResponsibleActorId &&
                entry.DeduplicationKey == evidence.DeduplicationKey,
                phase + " lost or changed deferred status or canonical evidence.");
        }

        private static void CreateTransportFixture(out WorldStateStore store, out NpcExperienceAppraisalTicket ticket)
        {
            const uint seed = 0x4E504358;
            const int settlementId = 71001;
            const int buildingKey = 81001;
            const int nativeHash = 91001;
            const double observedAt = 12345d;
            const string playerId = NpcDirectedRelationship.PlayerTargetId;
            string personId = string.Format(CultureInfo.InvariantCulture, "npc:{0}:{1}:{2}", settlementId, buildingKey, nativeHash);

            store = new WorldStateStore(seed);
            store.GetOrCreate(settlementId, new[] { buildingKey }, 1);
            store.RegisterNativePerson(settlementId, buildingKey, nativeHash, "Fixture NPC",
                ResidentSex.Female, 1, 1, 0, 0);
            store.GetOrCreateNpcPersonality(personId, seed, NpcSocialBackground.Commoner);
            store.SetConversationBaseline(personId, 0, observedAt);
            store.RecordConversationEncounter(personId, observedAt);
            var evidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                "fixture-transport-receipt-001", "fixture-transport-episode-001", "fixture-transport-outcome-001",
                NpcExperienceEventKind.ObligationFulfilled, observedAt,
                NpcResponsibleActorAttribution.Known, playerId);
            NpcProbeLegacyExperienceFixture.Import(store, evidence);
            if (!store.TryCaptureNpcExperienceAppraisal(personId, observedAt, out ticket) || ticket == null)
                throw new InvalidOperationException("Could not create isolated transport appraisal ticket.");
        }

        private static string Sha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var text = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) text.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return text.ToString();
            }
        }

        private static string GrammarMarker(string body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            const string property = "\"grammar\":\"";
            int start = body.IndexOf(property, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            int end = body.IndexOf("\",\"n_predict\"", start + property.Length, StringComparison.Ordinal);
            return end < 0 ? string.Empty : body.Substring(start, end - start + 1);
        }

        private static string ControlledResponse(NpcExperienceAppraisalRequest request,
            int moodDelta, int affinityDelta, int trustDelta)
        {
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"saveBranchId\":\"" + request.SaveBranchId + "\",\"observerId\":\"" + request.ObserverId +
                "\",\"subjectPersonId\":\"" + request.SubjectPersonId + "\",\"evidenceDeduplicationKey\":\"" +
                request.EvidenceDeduplicationKey + "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId +
                "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? string.Empty) +
                "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"moodDelta\":" + moodDelta.ToString(CultureInfo.InvariantCulture) +
                ",\"affinityDelta\":" + affinityDelta.ToString(CultureInfo.InvariantCulture) +
                ",\"trustDelta\":" + trustDelta.ToString(CultureInfo.InvariantCulture) +
                ",\"resentmentDelta\":0,\"fearDelta\":0,\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
        }

        private static void RequireCommittedEffects(WorldStateStore store, string personId, string phase)
        {
            var mood = store.CaptureConversationMood(personId);
            Require(mood.Value == 2 && mood.Revision == 1 && mood.AcceptedTurnSequence == 0,
                phase + " changed mood incorrectly or advanced the chat sequence.");
            Require(store.ConversationDisposition(personId) == 1,
                phase + " did not preserve the one-point affinity event effect.");
            var relationship = store.ReadNpcRelationship(personId, NpcDirectedRelationship.PlayerTargetId);
            Require(relationship != null && relationship.TrustKnown && relationship.ReliabilityTrust == 52 &&
                relationship.TrustEvidenceCount == 1 && relationship.Revision == 1,
                phase + " did not preserve the separate trust event effect.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
