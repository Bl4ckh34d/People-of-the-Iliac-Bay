using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // All mutations are detached from the actual native recovery snapshot.
    internal static class NpcExperienceInboxV5NativeProbe
    {
        private const int SettlementId = 71091;
        private const int BuildingKey = 81091;
        private const string AppointmentId = "inbox-v5-controlled-attendance";

        internal static void Verify(WorldState nativeRecovery, string recoveredPersonId)
        {
            Require(nativeRecovery != null && nativeRecovery.Version == WorldState.CurrentVersion &&
                !string.IsNullOrWhiteSpace(recoveredPersonId), "V5 fixture has no actual current-schema recovery source.");
            var detached = new WorldStateStore(nativeRecovery.WorldSeed);
            detached.Restore(nativeRecovery);
            NpcActorProgress pending = detached.ReadNpcProgress(recoveredPersonId);
            Require(pending != null && pending.HasPendingHealthEpisode &&
                pending.HealthEpisodeFirstVitalsRevision > 0 &&
                pending.HealthEpisodeLastVitalsRevision == pending.VitalsRevision &&
                pending.HealthEpisodeLastVitalsRevision > pending.HealthExperienceProjectedThroughVitalsRevision &&
                pending.HealthEpisodeHealingTotal > 0 &&
                (long)pending.HealthEpisodeFinalHealth * 3 >= (long)pending.HealthEpisodeMaximumHealth * 2 &&
                pending.HealthEpisodeFinalHealth <= pending.HealthEpisodeMaximumHealth,
                "V5 fixture lost the actual native healing interval.");
            double appointmentAt = pending.HealthEpisodeLastChangedAt - 8d * 86400d;
            double healthFlushAt = pending.HealthEpisodeLastChangedAt + 61d;
            Require(Finite(appointmentAt) && appointmentAt > 500d && Finite(healthFlushAt) &&
                !detached.HasSettlement(SettlementId),
                "V5 controlled appointment has no safe distinct settlement or aged game time.");

            string appointmentPersonId = detached.GetOrCreate(SettlementId, new[] { BuildingKey }, 1)
                .Households[0].Residents[0].Id;
            Require(appointmentPersonId != recoveredPersonId, "V5 fixture appointment reused the native patient.");
            var booking = new AppointmentRecord {
                Id = AppointmentId, ResidentId = appointmentPersonId, Purpose = "inbox v5 retention fixture",
                StartsAt = appointmentAt, EndsAt = appointmentAt + 60d, GraceSeconds = 0d,
                PatienceSeconds = 600, Importance = 2, Status = "accepted",
                Place = new AppointmentPlace { Kind = "building", SettlementId = SettlementId,
                    BuildingKey = BuildingKey, Detail = "controlled fixture venue" }
            };
            string reason;
            Require(detached.TryReserveAppointment(booking, appointmentAt - 500d, out reason),
                "V5 controlled appointment reservation failed: " + reason);
            detached.ObserveAppointment(AppointmentId, appointmentAt, true, true, false);
            AppointmentRecord attended = detached.FindAppointment(AppointmentId);
            NpcExperienceEvent appointmentEvidence = null;
            Require(attended != null && attended.OutcomeRecorded && attended.Status == "attended" &&
                attended.ExperienceProjection == NpcAppointmentExperienceProjection.Projected &&
                NpcAppointmentExperience.TryCreate(attended, out appointmentEvidence),
                "V5 controlled appointment did not resolve through its verified Core source.");
            NpcExperienceInboxEntry appointmentPending = Find(detached.Snapshot(), appointmentPersonId,
                appointmentEvidence.OutcomeId);
            Require(appointmentPending != null && appointmentPending.Status == NpcExperienceInboxStatus.Pending,
                "V5 appointment source did not project one pending appraisal row.");

            detached.GetOrCreateNpcPersonality(appointmentPersonId, nativeRecovery.WorldSeed,
                NpcSocialBackground.Commoner);
            detached.SetConversationBaseline(appointmentPersonId, 0, appointmentAt + 1d);
            detached.RecordConversationEncounter(appointmentPersonId, appointmentAt + 1d);
            NpcExperienceAppraisalTicket ticket;
            Require(detached.TryCaptureNpcExperienceAppraisal(appointmentPersonId, appointmentAt + 1d,
                out ticket) && ticket != null &&
                ticket.Request.CanonicalEvidenceId == appointmentEvidence.OutcomeId,
                "V5 appointment did not issue an exact typed appraisal ticket.");
            string response = ControlledResponse(ticket.Request);
            Require(detached.TryCommitNpcExperienceAppraisal(ticket, response) &&
                !detached.TryCommitNpcExperienceAppraisal(ticket, response),
                "V5 deterministic appointment appraisal was rejected or replayed.");
            NpcExperienceInboxEntry consumed = Find(detached.Snapshot(), appointmentPersonId,
                appointmentEvidence.OutcomeId);
            Require(consumed != null && consumed.Status == NpcExperienceInboxStatus.Consumed &&
                consumed.Appraisal != null && consumed.Appraisal.AppliedMoodDelta == 2,
                "V5 appointment row was not consumed with its deterministic applied mood receipt.");

            List<NpcHealthEpisodeSummary> health = detached.FlushNpcHealthExperiences(healthFlushAt, 60d, 100);
            NpcHealthEpisodeSummary source = health.Find(x => x.ObserverId == recoveredPersonId &&
                x.FirstVitalsRevision == pending.HealthEpisodeFirstVitalsRevision &&
                x.LastVitalsRevision == pending.HealthEpisodeLastVitalsRevision);
            Require(source != null && source.HealingTotal == pending.HealthEpisodeHealingTotal &&
                source.FinalHealth == pending.HealthEpisodeFinalHealth,
                "V5 detached health flush did not project the actual native interval.");
            NpcActorProgress projected = detached.ReadNpcProgress(recoveredPersonId);
            NpcExperienceInboxEntry healthPending = Find(detached.Snapshot(), recoveredPersonId, source.OutcomeId);
            Require(projected != null && !projected.HasPendingHealthEpisode &&
                projected.HealthExperienceProjectedThroughVitalsRevision == source.LastVitalsRevision &&
                healthPending != null && healthPending.Status == NpcExperienceInboxStatus.Pending,
                "V5 actual health source cursor or pending appraisal is missing.");

            WorldState liveBeforeWire = detached.Snapshot();
            WorldState wireBefore = RoundTrip(liveBeforeWire, "before-retention");
            RequireSameWireFrontier(liveBeforeWire, wireBefore, appointmentPersonId,
                appointmentEvidence.OutcomeId, recoveredPersonId, source.OutcomeId);
            var working = new WorldStateStore(wireBefore.WorldSeed);
            working.Restore(wireBefore);
            double maintenanceAt = Math.Max(healthFlushAt, appointmentAt + 8d * 86400d);
            NpcMemoryReadSnapshot staleRead;
            Require(working.TryCaptureNpcMoodCauseRead(appointmentPersonId, maintenanceAt, 600, out staleRead) &&
                working.IsNpcMemoryReadCurrent(staleRead, appointmentPersonId, maintenanceAt),
                "V5 controlled mood read was stale before retention.");
            NpcExperienceInbox before = working.Snapshot().NpcExperienceInbox;
            NpcExperienceInboxEntry beforeHealth = Find(working.Snapshot(), recoveredPersonId, source.OutcomeId);
            NpcExperienceInboxEntry beforeAppointment = Find(working.Snapshot(), appointmentPersonId,
                appointmentEvidence.OutcomeId);
            Require(before.Version == NpcExperienceInbox.VersionCurrent &&
                beforeHealth != null && beforeAppointment != null &&
                beforeHealth.EnqueueOrdinal > 0 && beforeAppointment.EnqueueOrdinal > 0,
                "V5 pre-retention wire state lost source-derived ordinals.");

            int examined = 0;
            bool retired = false;
            int limit = Math.Max(2, before.Entries.Count * 2 + 2);
            for (int slice = 0; slice < limit; slice++)
            {
                NpcExperienceInboxRetentionResult report = working.MaintainNpcExperienceInboxHistory(
                    maintenanceAt, 16, 4, true);
                Require(report != null && report.Examined <= 16 && report.Retired <= 4,
                    "V5 maintenance exceeded its bounded candidate budget.");
                examined += report.Examined;
                WorldState current = working.Snapshot();
                Require(Find(current, recoveredPersonId, source.OutcomeId) != null,
                    "V5 maintenance retired the actual still-pending health appraisal.");
                if (Find(current, appointmentPersonId, appointmentEvidence.OutcomeId) == null)
                { retired = true; break; }
                if (report.ScanComplete && report.AllPinned) break;
            }
            Require(retired, "V5 maintenance did not retire the aged consumed appointment; examined=" +
                examined.ToString(CultureInfo.InvariantCulture));
            WorldState after = working.Snapshot();
            NpcExperienceInbox inbox = after.NpcExperienceInbox;
            NpcExperienceInboxEntry afterHealth = Find(after, recoveredPersonId, source.OutcomeId);
            long removedCount = before.Entries.Count - inbox.Entries.Count;
            Require(removedCount >= 1 && inbox.Revision == before.Revision &&
                inbox.RetiredCount == before.RetiredCount + removedCount &&
                inbox.DetailRevision > before.DetailRevision &&
                inbox.Revision == inbox.RetiredCount + inbox.Entries.Count &&
                afterHealth != null && afterHealth.Status == NpcExperienceInboxStatus.Pending &&
                afterHealth.EnqueueOrdinal == beforeHealth.EnqueueOrdinal &&
                !working.IsNpcMemoryReadCurrent(staleRead, appointmentPersonId, maintenanceAt),
                "V5 retirement changed the enqueue frontier, pending pin or stale read authority.");
            NpcMoodCauseHistoryRead historical = working.ReadNpcMoodCauseHistory(appointmentPersonId,
                appointmentAt + 1d, 600);
            NpcMoodCauseHistoryRead currentHistory = working.ReadNpcMoodCauseHistory(appointmentPersonId,
                maintenanceAt, 600);
            Require(historical.Availability == NpcMoodCauseHistoryAvailability.UnavailableHistoricalDetail &&
                historical.SupportedFromAt == maintenanceAt &&
                currentHistory.Availability == NpcMoodCauseHistoryAvailability.Available &&
                currentHistory.SupportedFromAt == maintenanceAt,
                "V5 retirement did not preserve typed historical mood-read availability.");
            NpcMemoryReadSnapshot freshRead;
            Require(working.TryCaptureNpcMoodCauseRead(appointmentPersonId, maintenanceAt, 600,
                out freshRead) && working.IsNpcMemoryReadCurrent(freshRead, appointmentPersonId, maintenanceAt),
                "V5 post-retirement mood read was not current.");
            Require(ContainsPending(working.ReadPendingNpcExperiences(recoveredPersonId, 100), source.OutcomeId) &&
                working.ReadPendingNpcExperiences(appointmentPersonId, 10).Count == 0,
                "V5 retirement changed pending source ownership.");

            WorldState wireAfter = RoundTrip(after, "after-retention");
            NpcExperienceInboxEntry wireHealth = Find(wireAfter, recoveredPersonId, source.OutcomeId);
            NpcMoodCauseHistoryFloor liveFloor = after.NpcMoodCauseHistoryFloors.Find(x =>
                x.PersonId == appointmentPersonId);
            NpcMoodCauseHistoryFloor wireFloor = wireAfter.NpcMoodCauseHistoryFloors == null ? null :
                wireAfter.NpcMoodCauseHistoryFloors.Find(x => x.PersonId == appointmentPersonId);
            Require(wireAfter.NpcExperienceInbox != null &&
                wireAfter.NpcExperienceInbox.Revision == inbox.Revision &&
                wireAfter.NpcExperienceInbox.RetiredCount == inbox.RetiredCount &&
                wireAfter.NpcExperienceInbox.DetailRevision == inbox.DetailRevision &&
                wireHealth != null && afterHealth != null &&
                wireHealth.OutcomeId == afterHealth.OutcomeId &&
                wireHealth.Status == afterHealth.Status &&
                wireHealth.EnqueueOrdinal == afterHealth.EnqueueOrdinal &&
                wireHealth.SourceVitalsRevision == afterHealth.SourceVitalsRevision &&
                Find(after, appointmentPersonId, appointmentEvidence.OutcomeId) == null &&
                Find(wireAfter, appointmentPersonId, appointmentEvidence.OutcomeId) == null &&
                liveFloor != null && wireFloor != null &&
                Math.Abs(liveFloor.SupportedFromAt - wireFloor.SupportedFromAt) <= .001d,
                "DFU FullSerializer changed v5 counters, retained health source, retired appointment or historical frontier.");
            CheckRestored(wireAfter, appointmentPersonId, appointmentEvidence, recoveredPersonId,
                source, beforeAppointment.EnqueueOrdinal, beforeHealth.EnqueueOrdinal,
                healthFlushAt, maintenanceAt, ticket, response);
            CheckRestored(wireAfter, appointmentPersonId, appointmentEvidence, recoveredPersonId,
                source, beforeAppointment.EnqueueOrdinal, beforeHealth.EnqueueOrdinal,
                healthFlushAt, maintenanceAt, ticket, response);
        }

        private static void CheckRestored(WorldState wire, string appointmentPersonId,
            NpcExperienceEvent appointmentEvidence, string recoveredPersonId, NpcHealthEpisodeSummary health,
            long retiredOrdinal, long retainedOrdinal, double healthFlushAt, double now,
            NpcExperienceAppraisalTicket staleTicket, string response)
        {
            var restored = new WorldStateStore(wire.WorldSeed);
            restored.Restore(wire);
            WorldState snapshot = restored.Snapshot();
            AppointmentRecord appointment = restored.FindAppointment(AppointmentId);
            NpcActorProgress progress = restored.ReadNpcProgress(recoveredPersonId);
            NpcExperienceInboxEntry pending = Find(snapshot, recoveredPersonId, health.OutcomeId);
            NpcExperienceEvent savedAppointmentEvidence = null;
            NpcMoodCauseHistoryFloor savedFloor = wire.NpcMoodCauseHistoryFloors.Find(x =>
                x.PersonId == appointmentPersonId);
            Require(appointment != null && appointment.ResidentId == appointmentPersonId &&
                appointment.OutcomeRecorded && appointment.Status == "attended" &&
                appointment.ExperienceProjection == NpcAppointmentExperienceProjection.Projected &&
                NpcAppointmentExperience.TryCreate(appointment, out savedAppointmentEvidence) &&
                savedAppointmentEvidence.OutcomeId == appointmentEvidence.OutcomeId &&
                Find(snapshot, appointmentPersonId, appointmentEvidence.OutcomeId) == null &&
                pending != null && pending.EnqueueOrdinal == retainedOrdinal &&
                pending.Status == NpcExperienceInboxStatus.Pending &&
                snapshot.NpcExperienceInbox.Revision == wire.NpcExperienceInbox.Revision &&
                snapshot.NpcExperienceInbox.RetiredCount == wire.NpcExperienceInbox.RetiredCount &&
                snapshot.NpcExperienceInbox.DetailRevision == wire.NpcExperienceInbox.DetailRevision &&
                !snapshot.NpcExperienceInbox.Entries.Exists(x => x.EnqueueOrdinal == retiredOrdinal) &&
                progress != null && !progress.HasPendingHealthEpisode &&
                progress.HealthExperienceProjectedThroughVitalsRevision == health.LastVitalsRevision &&
                savedFloor != null &&
                restored.ReadNpcMoodCauseHistory(appointmentPersonId, appointment.StartsAt, 600).Availability ==
                    NpcMoodCauseHistoryAvailability.UnavailableHistoricalDetail &&
                restored.ReadNpcMoodCauseHistory(appointmentPersonId, savedFloor.SupportedFromAt, 600).Availability ==
                    NpcMoodCauseHistoryAvailability.Available,
                "V5 FullSerializer restore changed source markers, retired detail or absolute ordinals.");
            long revision = snapshot.NpcExperienceInbox.Revision;
            long detail = snapshot.NpcExperienceInbox.DetailRevision;
            var moodBefore = restored.CaptureConversationMood(appointmentPersonId, now);
            int affinityBefore = restored.ConversationDisposition(appointmentPersonId);
            Require(restored.FlushNpcAppointmentExperiences(now, 64) == 0 &&
                restored.FlushNpcHealthExperiences(healthFlushAt, 60d, 100).Count == 0 &&
                !restored.TryCommitNpcExperienceAppraisal(staleTicket, response) &&
                !restored.TryEnqueueNpcExperience(appointmentEvidence) &&
                restored.Snapshot().NpcExperienceInbox.Revision == revision &&
                restored.Snapshot().NpcExperienceInbox.DetailRevision == detail &&
                restored.CaptureConversationMood(appointmentPersonId, now).Revision == moodBefore.Revision &&
                restored.CaptureConversationMood(appointmentPersonId, now).Value == moodBefore.Value &&
                restored.ConversationDisposition(appointmentPersonId) == affinityBefore &&
                ContainsPending(restored.ReadPendingNpcExperiences(recoveredPersonId, 100), health.OutcomeId),
                "V5 restored or branched source replayed after detail retirement.");
        }

        private static void RequireSameWireFrontier(WorldState before, WorldState wire,
            string appointmentPersonId, string appointmentOutcome,
            string healthPersonId, string healthOutcome)
        {
            NpcExperienceInbox a = before.NpcExperienceInbox, b = wire.NpcExperienceInbox;
            NpcExperienceInboxEntry appointmentA = Find(before, appointmentPersonId, appointmentOutcome);
            NpcExperienceInboxEntry appointmentB = Find(wire, appointmentPersonId, appointmentOutcome);
            NpcExperienceInboxEntry healthA = Find(before, healthPersonId, healthOutcome);
            NpcExperienceInboxEntry healthB = Find(wire, healthPersonId, healthOutcome);
            Require(a != null && b != null && a.Revision == b.Revision &&
                a.RetiredCount == b.RetiredCount && a.DetailRevision == b.DetailRevision &&
                appointmentA != null && appointmentB != null &&
                appointmentA.Status == appointmentB.Status &&
                appointmentA.EnqueueOrdinal == appointmentB.EnqueueOrdinal &&
                healthA != null && healthB != null && healthA.Status == healthB.Status &&
                healthA.EnqueueOrdinal == healthB.EnqueueOrdinal &&
                Math.Abs(appointmentA.ObservedGameTime - appointmentB.ObservedGameTime) <= .001d &&
                Math.Abs(healthA.ObservedGameTime - healthB.ObservedGameTime) <= .001d,
                "DFU FullSerializer changed the v5 source frontier beyond its 1ms wire precision.");
        }

        private static string ControlledResponse(NpcExperienceAppraisalRequest request)
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
                ",\"moodDelta\":2,\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":0," +
                "\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
        }

        private static NpcExperienceInboxEntry Find(WorldState state, string observerId, string outcomeId)
        {
            if (state == null || state.NpcExperienceInbox == null ||
                state.NpcExperienceInbox.Entries == null) return null;
            return state.NpcExperienceInbox.Entries.Find(x => x != null && x.ObserverId == observerId &&
                x.OutcomeId == outcomeId);
        }

        private static bool ContainsPending(List<NpcExperienceEvent> rows, string outcomeId)
        {
            return rows != null && rows.Exists(x => x.OutcomeId == outcomeId);
        }

        private static WorldState RoundTrip(WorldState world, string suffix)
        {
            string path = Path.Combine(Path.GetTempPath(), "poti-inbox-v5-" + suffix + "-" +
                Guid.NewGuid().ToString("N") + ".json");
            try
            {
                string json = SaveLoadManager.Serialize(typeof(WorldState), world);
                Require(!string.IsNullOrEmpty(json), "DFU FullSerializer produced an empty v5 save.");
                File.WriteAllText(path, json);
                WorldState decoded = SaveLoadManager.Deserialize(typeof(WorldState),
                    File.ReadAllText(path), true) as WorldState;
                Require(decoded != null && decoded.Version == WorldState.CurrentVersion,
                    "DFU FullSerializer lost the v5 current-schema state.");
                return decoded;
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
