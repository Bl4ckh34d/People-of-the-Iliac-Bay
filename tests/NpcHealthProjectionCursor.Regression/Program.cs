using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("verified health projection advances cursor across restore and retired detail", ProjectionSurvivesRetiredDetail),
            ("magicka revision gap permits the next health episode", NextEpisodeAfterGap),
            ("only an exact retained health outcome reconciles a pending episode", ExactLegacyReconciliation),
            ("conflicting retained source leaves pending health and inbox unchanged", ConflictingOutcomeDoesNotClear),
            ("schema 49 rejects cursor overflow and pending overlap", InvalidCursorRejected),
            ("schema 48 imports pending health without synthesizing cursor authority", LegacyMigration)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void ProjectionSurvivesRetiredDetail()
    {
        var store = PendingHealth(out string person);
        var before = store.Snapshot().NpcActors[0];
        long last = before.HealthEpisodeLastVitalsRevision;
        Equal(1, store.FlushNpcHealthExperiences(160, 60, 1).Count);
        var saved = store.Snapshot();
        Equal(last, saved.NpcActors[0].HealthExperienceProjectedThroughVitalsRevision);
        True(!saved.NpcActors[0].HasPendingHealthEpisode, "Successful projection left source pending.");
        Equal(1, saved.NpcExperienceInbox.Entries.Count);

        // Model a later inbox-detail retirement. The source cursor is the replay guard.
        saved.NpcExperienceInbox.Entries.Clear();
        saved.NpcExperienceInbox.Revision = 0;
        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        Equal(0, restored.FlushNpcHealthExperiences(220, 60, 1).Count);
        Equal(last, restored.ReadNpcProgress(person).HealthExperienceProjectedThroughVitalsRevision);
        var stale = restored.Snapshot();
        var staleActor = stale.NpcActors[0];
        CopyPendingEpisode(before, staleActor);
        RejectRestore(stale, "Already projected interval regained pending authority after detail retirement.");
    }

    private static void NextEpisodeAfterGap()
    {
        var store = PendingHealth(out string person);
        Equal(1, store.FlushNpcHealthExperiences(160, 60, 1).Count);
        var vitals = store.ReadNpcVitals(person, NpcArchetype.Mage);
        True(vitals.MaximumMagicka > 0, "Fixture needs magicka for a revision gap.");
        True(store.TryCaptureNpcVitals(person, NpcArchetype.Mage, vitals.CurrentHealth,
            vitals.CurrentMagicka - 1, 170, vitals.Revision), "Magicka-only capture failed.");
        var afterMagicka = store.ReadNpcVitals(person, NpcArchetype.Mage);
        True(store.TryCaptureNpcVitals(person, NpcArchetype.Mage, afterMagicka.CurrentHealth - 1,
            afterMagicka.CurrentMagicka, 171, afterMagicka.Revision), "Next health capture failed.");
        var pending = store.ReadNpcProgress(person);
        True(pending.HealthEpisodeFirstVitalsRevision > pending.HealthExperienceProjectedThroughVitalsRevision + 1,
            "Fixture did not create a revision gap.");
        Equal(1, store.FlushNpcHealthExperiences(231, 60, 1).Count);
        Equal(2, store.Snapshot().NpcExperienceInbox.Entries.Count);
        Equal(pending.HealthEpisodeLastVitalsRevision,
            store.ReadNpcProgress(person).HealthExperienceProjectedThroughVitalsRevision);
    }

    private static void ExactLegacyReconciliation()
    {
        var pending = PendingHealth(out string person);
        var source = pending.Snapshot();
        var projected = new WorldStateStore(0);
        projected.Restore(source);
        Equal(1, projected.FlushNpcHealthExperiences(160, 60, 1).Count);
        source.NpcExperienceInbox.Entries.Add(projected.Snapshot().NpcExperienceInbox.Entries[0]);
        source.NpcExperienceInbox.Revision = 1;
        var restored = new WorldStateStore(0);
        restored.Restore(source);
        Equal(1, restored.FlushNpcHealthExperiences(160, 60, 1).Count);
        Equal(1, restored.Snapshot().NpcExperienceInbox.Entries.Count);
        True(!restored.ReadNpcProgress(person).HasPendingHealthEpisode, "Exact duplicate remained pending.");
        Equal(source.NpcActors[0].HealthEpisodeLastVitalsRevision,
            restored.ReadNpcProgress(person).HealthExperienceProjectedThroughVitalsRevision);
    }

    private static void ConflictingOutcomeDoesNotClear()
    {
        var pending = PendingHealth(out _);
        var source = pending.Snapshot();
        var projected = new WorldStateStore(0);
        projected.Restore(source);
        Equal(1, projected.FlushNpcHealthExperiences(160, 60, 1).Count);
        var conflicting = projected.Snapshot().NpcExperienceInbox.Entries[0];
        conflicting.ObservedGameTime += 1;
        conflicting.UpdatedAt += 1;
        source.NpcExperienceInbox.Entries.Add(conflicting);
        source.NpcExperienceInbox.Revision = 1;
        var restored = new WorldStateStore(0);
        restored.Restore(source);
        string before = SnapshotText(restored);
        Equal(0, restored.FlushNpcHealthExperiences(160, 60, 1).Count);
        Equal(before, SnapshotText(restored));
    }

    private static void InvalidCursorRejected()
    {
        var pending = PendingHealth(out _).Snapshot();
        pending.NpcActors[0].HealthExperienceProjectedThroughVitalsRevision =
            pending.NpcActors[0].HealthEpisodeFirstVitalsRevision;
        RejectRestore(pending, "Cursor overlapped pending source.");
        var overflow = PendingHealth(out _).Snapshot();
        overflow.NpcActors[0].HealthExperienceProjectedThroughVitalsRevision =
            overflow.NpcActors[0].VitalsRevision + 1;
        RejectRestore(overflow, "Cursor exceeded vitals revision.");
    }

    private static void LegacyMigration()
    {
        var legacy = PendingHealth(out string person).Snapshot();
        legacy.Version = 48; legacy.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        var restored = new WorldStateStore(0);
        restored.Restore(legacy);
        Equal(0L, restored.ReadNpcProgress(person).HealthExperienceProjectedThroughVitalsRevision);
        True(restored.ReadNpcProgress(person).HasPendingHealthEpisode, "Legacy pending source was discarded.");
        Equal(1, restored.FlushNpcHealthExperiences(160, 60, 1).Count);
        var futureField = PendingHealth(out _).Snapshot();
        futureField.Version = 48; futureField.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        futureField.NpcActors[0].HealthExperienceProjectedThroughVitalsRevision = 1;
        RejectRestore(futureField, "Legacy envelope supplied future cursor authority.");
    }

    private static WorldStateStore PendingHealth(out string person)
    {
        var store = new WorldStateStore(109);
        person = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        var baseline = store.ReadNpcVitals(person, NpcArchetype.Mage);
        True(store.TryCaptureObservedNpcVitalsChange(store.CaptureNpcVitalsWriteEpoch(), person,
            NpcArchetype.Mage, baseline.CurrentHealth, baseline.CurrentMagicka,
            baseline.CurrentHealth - 1, baseline.CurrentMagicka, 100, baseline.Revision),
            "Native health observation failed.");
        return store;
    }

    private static void CopyPendingEpisode(NpcActorProgress from, NpcActorProgress to)
    {
        to.HasPendingHealthEpisode = from.HasPendingHealthEpisode;
        to.HealthEpisodeStartedAt = from.HealthEpisodeStartedAt;
        to.HealthEpisodeLastChangedAt = from.HealthEpisodeLastChangedAt;
        to.HealthEpisodeInitialHealth = from.HealthEpisodeInitialHealth;
        to.HealthEpisodeFinalHealth = from.HealthEpisodeFinalHealth;
        to.HealthEpisodeMaximumHealth = from.HealthEpisodeMaximumHealth;
        to.HealthEpisodeDamageTotal = from.HealthEpisodeDamageTotal;
        to.HealthEpisodeHealingTotal = from.HealthEpisodeHealingTotal;
        to.HealthEpisodeChangeCount = from.HealthEpisodeChangeCount;
        to.HealthEpisodeFirstVitalsRevision = from.HealthEpisodeFirstVitalsRevision;
        to.HealthEpisodeLastVitalsRevision = from.HealthEpisodeLastVitalsRevision;
    }

    private static void RejectRestore(WorldState candidate, string message)
    {
        string inputBefore = JsonSerializer.Serialize(candidate, Json);
        var store = new WorldStateStore(0);
        bool rejected = false;
        try { store.Restore(candidate); }
        catch (ArgumentException) { rejected = true; }
        catch (InvalidOperationException) { rejected = true; }
        True(rejected, message);
        Equal(inputBefore, JsonSerializer.Serialize(candidate, Json));
    }

    private static string SnapshotText(WorldStateStore store)
    { return JsonSerializer.Serialize(store.Snapshot(), Json); }

    private static void True(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
}
