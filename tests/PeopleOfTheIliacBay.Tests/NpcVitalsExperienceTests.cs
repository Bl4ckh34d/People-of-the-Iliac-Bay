using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcVitalsExperienceTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Observed player healing retains its healer without duplicating prior injury or later health sampling", PlayerHealingHasNativeSource));
            cases.Add(("Player healing rejects stale and non-healing observations before changing existing injury evidence", InvalidPlayerHealingDoesNotFlushInjury));
            cases.Add(("First live health binding is a baseline, not invented injury", FirstBindingIsBaseline));
            cases.Add(("First observed change from sparse full vitals baseline creates injury episode", FirstObservedChangeFromSparseBaselineCreatesInjury));
            cases.Add(("Observed health capture rejects mismatched prior without mutation", ObservedCaptureRejectsMismatchedPrior));
            cases.Add(("Observed health capture matches initialized prior and rejects stale time", InitializedObservedCaptureValidatesPriorAndTime));
            cases.Add(("Observed health capture keeps sparse no-change reads sparse and refreshes timestamps", ObservedNoChangePreservesSparseBaselineAndRefreshesTimestamp));
            cases.Add(("Observed health capture rejects invalid previous resource bounds", ObservedCaptureRejectsInvalidPreviousBounds));
            cases.Add(("First observed magicka-only change creates no health episode", FirstObservedMagickaOnlyChangeCreatesNoEpisode));
            cases.Add(("Observed health capture rejects stale epoch and revision", ObservedCaptureRejectsStaleEpochAndRevision));
            cases.Add(("Health changes aggregate to one unknown-attribution episode after quiet time", HealthEpisodeAggregatesAndFlushes));
            cases.Add(("Healing-only episode is classified as healing", HealingEpisodeIsClassified));
            cases.Add(("Magicka-only changes do not create health experience", MagickaOnlyDoesNotCreateEpisode));
            cases.Add(("Vitals replay after save restore does not emit a health episode twice", SaveRestoreDoesNotReplayFlushedEpisode));
            cases.Add(("Pending health episode survives save and restore", PendingEpisodeSurvivesRestore));
            cases.Add(("Stale or invalid vitals do not change health or queue evidence", StaleAndInvalidCapturesAreSideEffectFree));
        }

        private static void FirstBindingIsBaseline()
        {
            var store = Create(NpcArchetype.Warrior, out var id, out var maximumHealth, out var maximumMagicka);
            Check.True(maximumHealth > 30);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Warrior, 30, maximumMagicka, 100, 0));
            var vitals = store.ReadNpcVitals(id, NpcArchetype.Warrior);
            Check.True(vitals.IsPersisted);
            Check.Equal(30, vitals.CurrentHealth);
            Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
            Check.Equal(0, store.Snapshot().NpcExperienceInbox.Entries.Count);
        }

        private static void FirstObservedChangeFromSparseBaselineCreatesInjury()
        {
            var store = Create(NpcArchetype.Warrior, out var id, out var maximumHealth, out var maximumMagicka);
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Warrior);
            Check.True(!baseline.IsPersisted);
            Check.Equal(maximumHealth, baseline.CurrentHealth);
            Check.Equal(maximumMagicka, baseline.CurrentMagicka);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Warrior, maximumHealth, maximumMagicka, 99, 0));
            Check.True(!store.ReadNpcVitals(id, NpcArchetype.Warrior).IsPersisted);

            object epoch = store.CaptureNpcVitalsWriteEpoch();
            Check.True(store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Warrior,
                baseline.CurrentHealth, baseline.CurrentMagicka, baseline.CurrentHealth - 1,
                baseline.CurrentMagicka, 100, baseline.Revision));

            store.Restore(store.Snapshot());
            var flushed = store.FlushNpcHealthExperiences(160, 60, 10);
            Check.Equal(1, flushed.Count);
            Check.Equal(maximumHealth, flushed[0].InitialHealth);
            Check.Equal(maximumHealth - 1, flushed[0].FinalHealth);
            Check.Equal(1L, flushed[0].DamageTotal);
            var evidence = store.ReadPendingNpcExperiences(id, 10);
            Check.Equal(1, evidence.Count);
            Check.Equal(NpcExperienceEventKind.Injury, evidence[0].EventKind.Value);
            Check.Equal(NpcResponsibleActorAttribution.Unknown, evidence[0].ResponsibleActorAttribution);
            Check.Equal(1L, evidence[0].NativeImpactDetails.TotalDamage);
            Check.Equal(0, store.FlushNpcHealthExperiences(220, 60, 10).Count);
        }

        private static void ObservedCaptureRejectsMismatchedPrior()
        {
            var store = Create(NpcArchetype.Guard, out var id, out var maximumHealth, out var maximumMagicka);
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Guard);
            object epoch = store.CaptureNpcVitalsWriteEpoch();
            string before = SnapshotText(store);
            Check.True(!store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Guard,
                maximumHealth - 1, maximumMagicka, maximumHealth - 2, maximumMagicka, 100, baseline.Revision));
            Check.Equal(before, SnapshotText(store));
            var after = store.ReadNpcVitals(id, NpcArchetype.Guard);
            Check.True(!after.IsPersisted);
            Check.Equal(maximumHealth, after.CurrentHealth);
            Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
        }

        private static void InitializedObservedCaptureValidatesPriorAndTime()
        {
            var store = Create(NpcArchetype.Mage, out var id, out _, out var maximumMagicka);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Mage, 30, maximumMagicka, 100, 0));
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Mage);
            object epoch = store.CaptureNpcVitalsWriteEpoch();
            string before = SnapshotText(store);
            Check.True(!store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Mage,
                baseline.CurrentHealth - 1, baseline.CurrentMagicka, baseline.CurrentHealth - 2,
                baseline.CurrentMagicka, 101, baseline.Revision));
            Check.True(!store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Mage,
                baseline.CurrentHealth, baseline.CurrentMagicka - 1, baseline.CurrentHealth - 2,
                baseline.CurrentMagicka, 101, baseline.Revision));
            Check.Equal(before, SnapshotText(store));
            Check.Equal(baseline.CurrentHealth, store.ReadNpcVitals(id, NpcArchetype.Mage).CurrentHealth);
            Check.True(store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Mage,
                baseline.CurrentHealth, baseline.CurrentMagicka, baseline.CurrentHealth - 2,
                baseline.CurrentMagicka, 101, baseline.Revision));
            var changed = store.ReadNpcVitals(id, NpcArchetype.Mage);
            string beforeStale = SnapshotText(store);
            Check.True(!store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Mage,
                changed.CurrentHealth, changed.CurrentMagicka, changed.CurrentHealth - 1,
                changed.CurrentMagicka, 100, changed.Revision));
            Check.Equal(beforeStale, SnapshotText(store));
            Check.Equal(changed.CurrentHealth, store.ReadNpcVitals(id, NpcArchetype.Mage).CurrentHealth);
            Check.Equal(1, store.FlushNpcHealthExperiences(161, 60, 10).Count);
            var evidence = store.ReadPendingNpcExperiences(id, 10);
            Check.Equal(1, evidence.Count);
            Check.Equal(2L, evidence[0].NativeImpactDetails.TotalDamage);
        }

        private static void ObservedNoChangePreservesSparseBaselineAndRefreshesTimestamp()
        {
            var store = Create(NpcArchetype.Warrior, out var id, out var maximumHealth, out var maximumMagicka);
            object epoch = store.CaptureNpcVitalsWriteEpoch();
            Check.True(store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Warrior,
                maximumHealth, maximumMagicka, maximumHealth, maximumMagicka, 100, 0));
            Check.True(!store.ReadNpcVitals(id, NpcArchetype.Warrior).IsPersisted);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Warrior, 30, maximumMagicka, 101, 0));
            var before = store.ReadNpcVitals(id, NpcArchetype.Warrior);
            Check.True(store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Warrior,
                before.CurrentHealth, before.CurrentMagicka, before.CurrentHealth, before.CurrentMagicka,
                102, before.Revision));
            var after = store.ReadNpcVitals(id, NpcArchetype.Warrior);
            Check.Equal(before.Revision, after.Revision);
            Check.Equal(102d, after.CapturedAt);
            Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
        }

        private static void ObservedCaptureRejectsInvalidPreviousBounds()
        {
            var store = Create(NpcArchetype.Guard, out var id, out var maximumHealth, out var maximumMagicka);
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Guard);
            object epoch = store.CaptureNpcVitalsWriteEpoch();
            Check.True(!store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Guard,
                0, maximumMagicka, maximumHealth - 1, maximumMagicka, 100, baseline.Revision));
            Check.True(!store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Guard,
                maximumHealth, maximumMagicka + 1, maximumHealth - 1, maximumMagicka, 100, baseline.Revision));
            Check.True(!store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Guard,
                maximumHealth, maximumMagicka, maximumHealth + 1, maximumMagicka, 100, baseline.Revision));
            Check.True(!store.ReadNpcVitals(id, NpcArchetype.Guard).IsPersisted);
            Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
        }

        private static void FirstObservedMagickaOnlyChangeCreatesNoEpisode()
        {
            var store = Create(NpcArchetype.Healer, out var id, out var maximumHealth, out var maximumMagicka);
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Healer);
            object epoch = store.CaptureNpcVitalsWriteEpoch();
            Check.True(store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Healer,
                baseline.CurrentHealth, baseline.CurrentMagicka, maximumHealth, maximumMagicka - 1,
                100, baseline.Revision));
            Check.Equal(0, store.FlushNpcHealthExperiences(160, 60, 10).Count);
            Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
        }

        private static void ObservedCaptureRejectsStaleEpochAndRevision()
        {
            var store = Create(NpcArchetype.Thief, out var id, out var maximumHealth, out var maximumMagicka);
            object staleEpoch = store.CaptureNpcVitalsWriteEpoch();
            var saved = store.Snapshot();
            store.Restore(saved);
            object currentEpoch = store.CaptureNpcVitalsWriteEpoch();
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Thief);
            Check.True(!store.TryCaptureObservedNpcVitalsChange(staleEpoch, id, NpcArchetype.Thief,
                maximumHealth, maximumMagicka, maximumHealth - 1, maximumMagicka, 100, baseline.Revision));
            Check.True(!store.TryCaptureObservedNpcVitalsChange(currentEpoch, id, NpcArchetype.Thief,
                maximumHealth, maximumMagicka, maximumHealth - 1, maximumMagicka, 100, baseline.Revision + 1));
            Check.True(!store.ReadNpcVitals(id, NpcArchetype.Thief).IsPersisted);
            Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
        }

        private static void HealthEpisodeAggregatesAndFlushes()
        {
            var store = Create(NpcArchetype.Warrior, out var id, out var maximumHealth, out var maximumMagicka);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Warrior, 30, maximumMagicka, 100, 0));
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Warrior);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Warrior, 23, maximumMagicka, 101, baseline.Revision));
            var afterDamage = store.ReadNpcVitals(id, NpcArchetype.Warrior);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Warrior, 28, maximumMagicka, 102, afterDamage.Revision));
            var afterHealing = store.ReadNpcVitals(id, NpcArchetype.Warrior);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Warrior, 25, maximumMagicka, 103, afterHealing.Revision));
            var afterLastHealthChange = store.ReadNpcVitals(id, NpcArchetype.Warrior);
            if(maximumMagicka>0)
                Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Warrior, 25, maximumMagicka-1, 104,
                    afterLastHealthChange.Revision));

            Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
            Check.Equal(0, store.FlushNpcHealthExperiences(162.99, 60, 10).Count);
            var flushed = store.FlushNpcHealthExperiences(163, 60, 10);
            Check.Equal(1, flushed.Count);
            var summary = flushed[0];
            Check.Equal(id, summary.ObserverId);
            Check.Equal(30, summary.InitialHealth);
            Check.Equal(25, summary.FinalHealth);
            Check.Equal(maximumHealth, summary.MaximumHealth);
            Check.Equal(10L, summary.DamageTotal);
            Check.Equal(5L, summary.HealingTotal);
            Check.Equal(3, summary.ChangeCount);
            Check.Equal(101d, summary.StartedAt);
            Check.Equal(103d, summary.LastChangedAt);

            var events = store.ReadPendingNpcExperiences(id, 10);
            Check.Equal(1, events.Count);
            Check.Equal(NpcExperienceEventKind.Injury, events[0].EventKind.Value);
            Check.Equal(id, events[0].ObserverId);
            Check.Equal(id, events[0].SubjectPersonId);
            Check.Equal(NpcResponsibleActorAttribution.Unknown, events[0].ResponsibleActorAttribution);
            Check.True(events[0].ResponsibleActorId == null);
            Check.Equal(103d, events[0].ObservedGameTime);
            Check.Equal(summary.OutcomeId, events[0].OutcomeId);
            Check.Equal(NpcExperienceEventOrigin.NativeOutcome, events[0].Origin);
            Check.True(events[0].NativeImpactDetails != null);
            Check.Equal(30, events[0].NativeImpactDetails.InitialHealth);
            Check.Equal(25, events[0].NativeImpactDetails.FinalHealth);
            Check.Equal(maximumHealth, events[0].NativeImpactDetails.MaximumHealth);
            Check.Equal(10L, events[0].NativeImpactDetails.TotalDamage);
            Check.Equal(5L, events[0].NativeImpactDetails.TotalHealing);
            Check.Equal(3, events[0].NativeImpactDetails.ChangeCount);
            Check.Equal(0, store.FlushNpcHealthExperiences(1000, 60, 10).Count);
            Check.Equal(1, store.Snapshot().NpcExperienceInbox.Entries.Count);
        }

        private static void HealingEpisodeIsClassified()
        {
            var store = Create(NpcArchetype.Mage, out var id, out var maximumHealth, out var maximumMagicka);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Mage, 30, maximumMagicka, 10, 0));
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Mage);
            object epoch = store.CaptureNpcVitalsWriteEpoch();
            Check.True(store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Mage,
                baseline.CurrentHealth, baseline.CurrentMagicka, 35, maximumMagicka, 12, baseline.Revision));
            var healing = store.FlushNpcHealthExperiences(72, 60, 10);
            Check.Equal(1, healing.Count);
            Check.Equal(30, healing[0].InitialHealth);
            Check.Equal(35, healing[0].FinalHealth);
            Check.Equal(maximumHealth, healing[0].MaximumHealth);
            Check.Equal(0L, healing[0].DamageTotal);
            Check.Equal(5L, healing[0].HealingTotal);
            Check.Equal(NpcExperienceEventKind.Healing,
                store.ReadPendingNpcExperiences(id, 10)[0].EventKind.Value);
        }

        private static void MagickaOnlyDoesNotCreateEpisode()
        {
            var store = Create(NpcArchetype.Healer, out var id, out _, out var maximumMagicka);
            Check.True(maximumMagicka > 0);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Healer, 30, maximumMagicka, 20, 0));
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Healer);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Healer, 30, maximumMagicka - 1, 21, baseline.Revision));
            Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
            Check.Equal(0, store.FlushNpcHealthExperiences(100, 60, 10).Count);
        }

        private static void SaveRestoreDoesNotReplayFlushedEpisode()
        {
            var store = Create(NpcArchetype.Thief, out var id, out var maximumHealth, out var maximumMagicka);
            var sparseBaseline = store.ReadNpcVitals(id, NpcArchetype.Thief);
            object epoch = store.CaptureNpcVitalsWriteEpoch();
            Check.True(store.TryCaptureObservedNpcVitalsChange(epoch, id, NpcArchetype.Thief,
                sparseBaseline.CurrentHealth, sparseBaseline.CurrentMagicka, maximumHealth - 6,
                maximumMagicka, 105, sparseBaseline.Revision));
            var restored = new WorldStateStore(0);
            restored.Restore(store.Snapshot());
            var summary = restored.FlushNpcHealthExperiences(165, 60, 10);
            Check.Equal(1, summary.Count);
            var reloaded = new WorldStateStore(0);
            reloaded.Restore(restored.Snapshot());
            var after = reloaded.ReadNpcVitals(id, NpcArchetype.Thief);
            Check.True(reloaded.TryCaptureNpcVitals(id, NpcArchetype.Thief, after.CurrentHealth,
                after.CurrentMagicka, 166, after.Revision));
            Check.Equal(0, reloaded.FlushNpcHealthExperiences(500, 60, 10).Count);
            var evidence = reloaded.ReadPendingNpcExperiences(id, 10);
            Check.Equal(1, evidence.Count);
            Check.Equal(summary[0].OutcomeId, evidence[0].OutcomeId);
        }

        private static void PendingEpisodeSurvivesRestore()
        {
            var store = Create(NpcArchetype.Guard, out var id, out _, out var maximumMagicka);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Guard, 30, maximumMagicka, 300, 0));
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Guard);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Guard, 24, maximumMagicka, 305, baseline.Revision));
            var saved = store.Snapshot();
            var restored = new WorldStateStore(0);
            restored.Restore(saved);
            var flushed = restored.FlushNpcHealthExperiences(365, 60, 10);
            Check.Equal(1, flushed.Count);
            Check.Equal(6L, flushed[0].DamageTotal);
            var evidence = restored.ReadPendingNpcExperiences(id, 10);
            Check.Equal(1, evidence.Count);
            Check.Equal(flushed[0].OutcomeId, evidence[0].OutcomeId);
        }

        private static void StaleAndInvalidCapturesAreSideEffectFree()
        {
            var store = Create(NpcArchetype.Guard, out var id, out _, out var maximumMagicka);
            Check.True(store.TryCaptureNpcVitals(id, NpcArchetype.Guard, 30, maximumMagicka, 200, 0));
            var baseline = store.ReadNpcVitals(id, NpcArchetype.Guard);
            Check.True(!store.TryCaptureNpcVitals(id, NpcArchetype.Guard, 25, maximumMagicka, 199, baseline.Revision));
            Check.True(!store.TryCaptureNpcVitals(id, NpcArchetype.Guard, 25, maximumMagicka, double.NaN, baseline.Revision));
            Check.True(!store.TryCaptureNpcVitals(id, NpcArchetype.Guard, 25, maximumMagicka,
                201, baseline.Revision - 1));
            Check.Equal(30, store.ReadNpcVitals(id, NpcArchetype.Guard).CurrentHealth);
            Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
            Check.Equal(0, store.Snapshot().NpcExperienceInbox.Entries.Count);
        }

        private static void PlayerHealingHasNativeSource()
        {
            var store=Create(NpcArchetype.Guard,out var id,out var maximum,out var magicka);
            var epoch=store.CaptureNpcVitalsWriteEpoch();
            Check.True(store.TryCaptureObservedNpcVitalsChange(epoch,id,NpcArchetype.Guard,
                maximum,magicka,maximum-20,magicka,100,0));
            long revision=store.ReadNpcVitals(id,NpcArchetype.Guard).Revision;
            Check.True(store.TryCaptureObservedPlayerHealing(epoch,id,NpcArchetype.Guard,
                maximum-20,magicka,maximum-12,magicka,101,revision));
            var events=store.ReadPendingNpcExperiences(id,10);
            Check.Equal(2,events.Count);
            var injury=events.Find(e=>e.EventKind==NpcExperienceEventKind.Injury);
            var healing=events.Find(e=>e.EventKind==NpcExperienceEventKind.Healing);
            Check.Equal(NpcResponsibleActorAttribution.Unknown,injury.ResponsibleActorAttribution);
            Check.Equal(20L,injury.NativeImpactDetails.TotalDamage);
            Check.Equal(NpcResponsibleActorAttribution.Known,healing.ResponsibleActorAttribution);
            Check.Equal(NpcDirectedRelationship.PlayerTargetId,healing.ResponsibleActorId);
            Check.Equal(8L,healing.NativeImpactDetails.TotalHealing);
            string recollection=store.RecallSceneEvents(id,102,2000);
            Check.True(recollection.Contains("injured") && recollection.Contains("player") && recollection.Contains("healed"));
            Check.Equal(2,store.Snapshot().SceneMemories.Count);
            Check.Equal(0L,healing.NativeImpactDetails.TotalDamage);
            Check.Equal(0,store.FlushNpcHealthExperiences(200,0,10).Count);
            string before=SnapshotText(store);
            Check.True(!store.TryCaptureObservedPlayerHealing(epoch,id,NpcArchetype.Guard,
                maximum-20,magicka,maximum-12,magicka,101,revision));
            Check.Equal(before,SnapshotText(store));
            var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
            Check.Equal(2,restored.ReadPendingNpcExperiences(id,10).Count);
            Check.Equal(NpcDirectedRelationship.PlayerTargetId,restored.ReadPendingNpcExperiences(id,10)
                .Find(e=>e.EventKind==NpcExperienceEventKind.Healing).ResponsibleActorId);
            Check.Equal(0,restored.FlushNpcHealthExperiences(200,0,10).Count);
        }

        private static void InvalidPlayerHealingDoesNotFlushInjury()
        {
            var store=Create(NpcArchetype.Guard,out var id,out var maximum,out var magicka);
            var epoch=store.CaptureNpcVitalsWriteEpoch();
            Check.True(store.TryCaptureObservedNpcVitalsChange(epoch,id,NpcArchetype.Guard,
                maximum,magicka,maximum-20,magicka,100,0));
            long revision=store.ReadNpcVitals(id,NpcArchetype.Guard).Revision;
            string before=SnapshotText(store);
            Check.True(!store.TryCaptureObservedPlayerHealing(epoch,id,NpcArchetype.Guard,
                maximum-20,magicka,maximum-20,magicka,101,revision));
            Check.True(!store.TryCaptureObservedPlayerHealing(epoch,id,NpcArchetype.Guard,
                maximum-20,magicka,maximum+1,magicka,101,revision));
            Check.True(!store.TryCaptureObservedPlayerHealing(epoch,id,NpcArchetype.Guard,
                maximum-20,magicka,maximum-10,magicka,99,revision));
            Check.True(!store.TryCaptureObservedPlayerHealing(epoch,id,NpcArchetype.Guard,
                maximum-20,magicka,maximum-10,magicka,101,revision-1));
            Check.Equal(before,SnapshotText(store));
            store.Restore(store.Snapshot());before=SnapshotText(store);
            Check.True(!store.TryCaptureObservedPlayerHealing(epoch,id,NpcArchetype.Guard,
                maximum-20,magicka,maximum-10,magicka,101,revision));
            Check.Equal(before,SnapshotText(store));
        }

        private static string SnapshotText(WorldStateStore store)
        {
            return JsonSerializer.Serialize(store.Snapshot(), new JsonSerializerOptions { IncludeFields = true });
        }

        private static WorldStateStore Create(NpcArchetype archetype, out string id, out int maximumHealth, out int maximumMagicka)
        {
            var store = new WorldStateStore(9042);
            id = store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0].Id;
            var profile = NpcProfileGenerator.Generate(store.WorldSeed, id, archetype);
            maximumHealth = profile.MaximumHealth;
            maximumMagicka = profile.MaximumMagicka;
            return store;
        }
    }
}
