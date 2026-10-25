using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Serializer acceptance only. The health source is copied from the actual
    // native recovery; every flush and controlled appointment is detached.
    // Detail retirement is proved separately by NpcExperienceInboxV5NativeProbe.
    internal static class NpcExperienceProjectionNativeProbe
    {
        internal static void Verify(WorldState nativeRecoverySnapshot, string recoveredPersonId)
        {
            Require(nativeRecoverySnapshot != null && nativeRecoverySnapshot.Version == WorldState.CurrentVersion &&
                !string.IsNullOrWhiteSpace(recoveredPersonId),
                "Native recovery did not provide a current-schema health source snapshot.");
            VerifyNativeHealth(nativeRecoverySnapshot, recoveredPersonId);
            VerifyControlledAppointment();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_EXPERIENCE_PROJECTION_MARKERS_PASS: " +
                "healthSource=actual-native-recovery; appointmentSource=controlled-Core-resolution; " +
                "nativeFullSerializerRoundtrip=true; savedMarkersSurvived=true; " +
                "sourceReplayWithoutRetirement=false; retirementCheckedByV5Gate=true; liveStoreMutated=false");
        }

        private static void VerifyNativeHealth(WorldState nativeSnapshot, string personId)
        {
            var detached = new WorldStateStore(nativeSnapshot.WorldSeed);
            detached.Restore(nativeSnapshot);
            NpcActorProgress pending = detached.ReadNpcProgress(personId);
            Require(pending != null && pending.HasPendingHealthEpisode &&
                pending.HealthEpisodeFirstVitalsRevision > 0 &&
                pending.HealthEpisodeLastVitalsRevision == pending.VitalsRevision &&
                pending.HealthEpisodeLastVitalsRevision > pending.HealthExperienceProjectedThroughVitalsRevision &&
                pending.HealthEpisodeHealingTotal > 0 &&
                (long)pending.HealthEpisodeFinalHealth * 3 >= (long)pending.HealthEpisodeMaximumHealth * 2 &&
                pending.HealthEpisodeFinalHealth <= pending.HealthEpisodeMaximumHealth,
                "Actual native recovery snapshot has no unprojected healing episode.");

            double flushAt = pending.HealthEpisodeLastChangedAt + 61d;
            Require(!double.IsNaN(flushAt) && !double.IsInfinity(flushAt),
                "Actual native health episode has an invalid quiet-window time.");
            List<NpcHealthEpisodeSummary> flushed = detached.FlushNpcHealthExperiences(flushAt, 60d, 100);
            NpcHealthEpisodeSummary source = flushed.Find(item => item.ObserverId == personId &&
                item.FirstVitalsRevision == pending.HealthEpisodeFirstVitalsRevision &&
                item.LastVitalsRevision == pending.HealthEpisodeLastVitalsRevision);
            Require(source != null && source.HealingTotal == pending.HealthEpisodeHealingTotal &&
                source.DamageTotal == pending.HealthEpisodeDamageTotal &&
                source.FinalHealth == pending.HealthEpisodeFinalHealth,
                "Detached flush did not project the exact actual native recovery interval.");
            NpcActorProgress projected = detached.ReadNpcProgress(personId);
            Require(projected != null && !projected.HasPendingHealthEpisode &&
                projected.HealthExperienceProjectedThroughVitalsRevision == source.LastVitalsRevision,
                "Native health projection did not persist its exact source revision cursor.");
            WorldState withDetail = RoundTrip(detached.Snapshot(), "health-projected");
            var restored = new WorldStateStore(withDetail.WorldSeed);
            restored.Restore(withDetail);
            RequireExactHealthMarker(restored, personId, source.LastVitalsRevision, source.OutcomeId, true);

            List<NpcHealthEpisodeSummary> replay = restored.FlushNpcHealthExperiences(flushAt, 60d, 100);
            Require(!replay.Exists(item => item.ObserverId == personId) &&
                restored.ReadNpcProgress(personId).HealthExperienceProjectedThroughVitalsRevision ==
                    source.LastVitalsRevision &&
                HasOutcome(restored.Snapshot(), personId, source.OutcomeId),
                "Native health source replayed or lost its saved marker after FullSerializer restore.");
        }

        private static void VerifyControlledAppointment()
        {
            const uint seed = 0x49585034;
            const int settlementId = 71091;
            const int buildingKey = 81091;
            const string appointmentId = "projection-marker-fixture-appointment";
            var store = new WorldStateStore(seed);
            string personId = store.GetOrCreate(settlementId, new[] { buildingKey }, 1)
                .Households[0].Residents[0].Id;
            var booking = new AppointmentRecord
            {
                Id = appointmentId, ResidentId = personId, Purpose = "projection marker fixture",
                StartsAt = 1000d, EndsAt = 1060d, GraceSeconds = 0d,
                PatienceSeconds = 600, Importance = 2, Status = "accepted",
                Place = new AppointmentPlace { Kind = "building", SettlementId = settlementId,
                    BuildingKey = buildingKey, Detail = "controlled fixture venue" }
            };
            string reason;
            Require(store.TryReserveAppointment(booking, 500d, out reason),
                "Controlled appointment source was not reserved: " + reason);
            store.ObserveAppointment(appointmentId, 1000d, true, true, false);
            AppointmentRecord resolved = store.FindAppointment(appointmentId);
            NpcExperienceEvent evidence = null;
            Require(resolved != null && resolved.OutcomeRecorded && resolved.Status == "attended" &&
                resolved.ExperienceProjection == NpcAppointmentExperienceProjection.Projected &&
                NpcAppointmentExperience.TryCreate(resolved, out evidence) &&
                HasOutcome(store.Snapshot(), personId, evidence.OutcomeId),
                "Confirmed controlled appointment did not project its real stored source.");

            WorldState withDetail = RoundTrip(store.Snapshot(), "appointment-projected");
            var restored = new WorldStateStore(withDetail.WorldSeed);
            restored.Restore(withDetail);
            RequireExactAppointmentMarker(restored, appointmentId, evidence.OutcomeId, true);
            Require(restored.FlushNpcAppointmentExperiences(1001d, 64) == 0 &&
                HasOutcome(restored.Snapshot(), personId, evidence.OutcomeId),
                "Confirmed appointment source replayed or lost its saved marker after FullSerializer restore.");
        }

        private static void RequireExactHealthMarker(WorldStateStore store, string personId,
            long expectedRevision, string outcomeId, bool detailExpected)
        {
            NpcActorProgress progress = store.ReadNpcProgress(personId);
            Require(progress != null && !progress.HasPendingHealthEpisode &&
                progress.HealthExperienceProjectedThroughVitalsRevision == expectedRevision &&
                HasOutcome(store.Snapshot(), personId, outcomeId) == detailExpected,
                "FullSerializer changed the native health source cursor or detail state.");
        }

        private static void RequireExactAppointmentMarker(WorldStateStore store, string appointmentId,
            string outcomeId, bool detailExpected)
        {
            AppointmentRecord record = store.FindAppointment(appointmentId);
            Require(record != null && record.OutcomeRecorded && record.Status == "attended" &&
                record.ExperienceProjection == NpcAppointmentExperienceProjection.Projected &&
                HasOutcome(store.Snapshot(), record.ResidentId, outcomeId) == detailExpected,
                "FullSerializer changed the confirmed appointment projection marker or detail state.");
        }

        private static bool HasOutcome(WorldState world, string observerId, string outcomeId)
        {
            foreach (NpcExperienceInboxEntry entry in world.NpcExperienceInbox.Entries)
                if (entry.ObserverId == observerId && entry.OutcomeId == outcomeId) return true;
            return false;
        }

        private static WorldState RoundTrip(WorldState world, string suffix)
        {
            string path = Path.Combine(Path.GetTempPath(), "poti-projection-" + suffix + "-" +
                Guid.NewGuid().ToString("N") + ".json");
            try
            {
                string serialized = SaveLoadManager.Serialize(typeof(WorldState), world);
                Require(!string.IsNullOrEmpty(serialized), "Native FullSerializer produced an empty projection save.");
                File.WriteAllText(path, serialized);
                string fromDisk = File.ReadAllText(path);
                WorldState decoded = SaveLoadManager.Deserialize(typeof(WorldState), fromDisk, true) as WorldState;
                Require(decoded != null && decoded.Version == WorldState.CurrentVersion,
                    "Native FullSerializer did not retain the current projection schema.");
                return decoded;
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
