using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcAcuteFearTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Acute fear decays on game-hour scale and expires across days", HourAndDayDecay));
            cases.Add(("Repeated visible threat observations maintain fear without accumulation", RepeatedObservationDoesNotInflate));
            cases.Add(("Stronger same-time observation raises acute fear immediately", StrongerObservationRaisesFear));
            cases.Add(("Changing observed source replaces provenance without stacking", SourceChangeDoesNotStack));
            cases.Add(("Acute fear uses stable saved personality traits", StableTraitAdjustment));
            cases.Add(("Acute half-life uses severity-adjusted two-hour policy and directed fear stays day-scale", RecoveryPolicySeparation));
            cases.Add(("Unknown and dead NPCs cannot receive acute fear", UnknownAndDeadRejected));
            cases.Add(("Invalid and backward observations reject without mutation", InvalidAndBackwardRejected));
            cases.Add(("Acute fear reads are detached and immutable", DetachedRead));
            cases.Add(("Malformed and duplicate acute fear records reject restore", MalformedAndDuplicateRecords));
            cases.Add(("Duplicate acute fear observers reject even when sources differ", DuplicateObserverDifferentSource));
            cases.Add(("Acute fear save fields and saved recovery rate survive personality change", SaveFieldsAndSavedRateRoundTrip));
            cases.Add(("Every acute fear save field is validated", PersistedFieldsAreValidated));
            cases.Add(("Failed acute fear restore preserves live state", FailedRestoreIsAtomic));
            cases.Add(("Restored acute fear records are deep copied", RestoreDeepCopiesRecords));
            cases.Add(("Acute fear observation does not mutate relationship trust", ObservationLeavesRelationshipsAlone));
        }

        private static void HourAndDayDecay()
        {
            var store = KnownStore(out string observer);
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:hours", NpcFearSeverity.Grave, 0));
            var observed = store.ReadNpcAcuteFear(observer, 0);
            Check.True(observed != null);
            var afterOneHalfLife = store.ReadNpcAcuteFear(observer, observed.RecoveryHalfLifeSeconds);
            Check.True(afterOneHalfLife != null);
            Check.Near(observed.CurrentFear * 0.5, afterOneHalfLife.CurrentFear, 1.0);
            var afterOneDay = store.ReadNpcAcuteFear(observer, 24 * 60 * 60);
            Check.True(afterOneDay != null && afterOneDay.CurrentFear < afterOneHalfLife.CurrentFear * 0.01);
        }

        private static void RepeatedObservationDoesNotInflate()
        {
            var store = KnownStore(out string observer);
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:repeat", NpcFearSeverity.Severe, 0));
            var initial = store.ReadNpcAcuteFear(observer, 0);
            Check.True(initial != null);
            Check.True(!store.TryObserveNpcAcuteFear(observer, "hostile:repeat", NpcFearSeverity.Severe, 30),
                "Observation inside 60 game seconds must be rate limited.");
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:repeat", NpcFearSeverity.Severe, 60));
            var refreshed = store.ReadNpcAcuteFear(observer, 60);
            Check.True(refreshed != null);
            Check.True(refreshed.CurrentFear <= initial.CurrentFear + 0.000001,
                "A repeated sighting may maintain decayed baseline but must not add fear.");
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:repeat", NpcFearSeverity.Severe, 120));
            var repeated = store.ReadNpcAcuteFear(observer, 120);
            Check.True(repeated != null && repeated.CurrentFear <= initial.CurrentFear + 0.000001);
        }

        private static void StrongerObservationRaisesFear()
        {
            var store = KnownStore(out string observer);
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:stronger", NpcFearSeverity.Mild, 10));
            double mild = store.ReadNpcAcuteFear(observer, 10).CurrentFear;
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:stronger", NpcFearSeverity.Grave, 10));
            double grave = store.ReadNpcAcuteFear(observer, 10).CurrentFear;
            Check.True(grave > mild, "Stronger observation must bypass refresh throttle and raise fear.");
        }

        private static void SourceChangeDoesNotStack()
        {
            var store = KnownStore(out string observer);
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:first", NpcFearSeverity.Moderate, 10));
            double before = store.ReadNpcAcuteFear(observer, 10).CurrentFear;
            Check.True(!store.TryObserveNpcAcuteFear(observer, "hostile:second", NpcFearSeverity.Moderate, 10));
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:second", NpcFearSeverity.Moderate, 70));
            var changed = store.ReadNpcAcuteFear(observer, 70);
            Check.Equal("hostile:second", changed.SourceId);
            Check.Near(before, changed.CurrentFear, 0.000001);
        }

        private static void StableTraitAdjustment()
        {
            var sensitive = KnownStore(out string sensitiveId);
            var sensitiveProfile = new NpcPersonalityProfile { PersonId=sensitiveId, SelfEsteem="insecure" };
            sensitiveProfile.Sensitivity = "highly sensitive";
            sensitiveProfile.EmotionalStability = "volatile";
            ReplacePersonality(sensitive, sensitiveProfile);

            var resilient = KnownStore(out string resilientId);
            var resilientProfile = new NpcPersonalityProfile { PersonId=resilientId };
            resilientProfile.Sensitivity = "thick-skinned";
            resilientProfile.EmotionalStability = "resilient";
            resilientProfile.SelfEsteem = "confident";
            ReplacePersonality(resilient, resilientProfile);

            Check.True(sensitive.TryObserveNpcAcuteFear(sensitiveId, "hostile:traits", NpcFearSeverity.Moderate, 1));
            Check.True(resilient.TryObserveNpcAcuteFear(resilientId, "hostile:traits", NpcFearSeverity.Moderate, 1));
            Check.True(sensitive.ReadNpcAcuteFear(sensitiveId, 1).CurrentFear >
                resilient.ReadNpcAcuteFear(resilientId, 1).CurrentFear);
            double sensitiveHalfLife = sensitive.ReadNpcAcuteFear(sensitiveId, 1).RecoveryHalfLifeSeconds;
            double resilientHalfLife = resilient.ReadNpcAcuteFear(resilientId, 1).RecoveryHalfLifeSeconds;
            Check.True(sensitiveHalfLife > resilientHalfLife);
        }

        private static void RecoveryPolicySeparation()
        {
            var store = KnownStore(out string observer);
            var profile = store.GetOrCreateNpcPersonality(observer, 1, NpcSocialBackground.Commoner);
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:policy", NpcFearSeverity.Severe, 1));
            double expectedAcute = NpcFearRecoveryPolicy.Create(7200, 0.70, 1.50)
                .HalfLifeSeconds(profile, NpcFearSeverity.Severe);
            Check.Near(expectedAcute, store.ReadNpcAcuteFear(observer, 1).RecoveryHalfLifeSeconds);

            Check.Near(7.0 * 24 * 60 * 60, NpcFearRecoveryPolicy.ProvisionalDefaultHalfLifeSeconds);
            double directed = NpcFearRecoveryPolicy.ProvisionalDefault.HalfLifeSeconds(profile, NpcFearSeverity.Severe);
            Check.Near(NpcFearRecoveryPolicy.ProvisionalDefaultHalfLifeSeconds *
                (expectedAcute / 7200), directed);
        }

        private static void UnknownAndDeadRejected()
        {
            var store = KnownStore(out string observer);
            int personalityCount = store.Snapshot().NpcPersonalities.Count;
            Check.True(!store.TryObserveNpcAcuteFear("npc:unknown", "hostile:unknown", NpcFearSeverity.Severe, 0));
            Check.True(store.ReadNpcAcuteFear("npc:unknown", 0) == null);
            Check.Equal(personalityCount, store.Snapshot().NpcPersonalities.Count);

            var resident = store.ResolveResident(observer);
            resident.IsAlive = false;
            Check.True(!store.TryObserveNpcAcuteFear(observer, "hostile:dead", NpcFearSeverity.Severe, 0));
            Check.True(store.ReadNpcAcuteFear(observer, 0) == null);
        }

        private static void InvalidAndBackwardRejected()
        {
            var store = KnownStore(out string observer);
            Check.True(!store.TryObserveNpcAcuteFear(observer, "hostile:nan", NpcFearSeverity.Severe, double.NaN));
            Check.True(!store.TryObserveNpcAcuteFear(observer, "hostile:negative", NpcFearSeverity.Severe, -1));
            Check.True(!store.TryObserveNpcAcuteFear(observer, null, NpcFearSeverity.Severe, 0));
            Check.True(!store.TryObserveNpcAcuteFear(observer, "hostile:enum", (NpcFearSeverity)99, 0));
            Check.True(store.ReadNpcAcuteFear(observer, 0) == null);

            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:clock", NpcFearSeverity.Moderate, 100));
            var before = store.ReadNpcAcuteFear(observer, 100);
            Check.True(!store.TryObserveNpcAcuteFear(observer, "hostile:clock", NpcFearSeverity.Grave, 99));
            Check.Throws<ArgumentOutOfRangeException>(() => store.ReadNpcAcuteFear(observer, 99));
            var after = store.ReadNpcAcuteFear(observer, 100);
            Check.Equal(before.Revision, after.Revision);
            Check.Near(before.CurrentFear, after.CurrentFear);
        }

        private static void DetachedRead()
        {
            var store = KnownStore(out string observer);
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:copy", NpcFearSeverity.Severe, 100));
            var first = store.ReadNpcAcuteFear(observer, 100);
            var second = store.ReadNpcAcuteFear(observer, 100);
            Check.True(first != null && second != null && !ReferenceEquals(first, second));
            Check.True(typeof(NpcAcuteFearView).GetProperty("CurrentFear").GetSetMethod(false) == null);
            Check.True(typeof(NpcAcuteFearView).GetProperty("ObserverId").GetSetMethod(false) == null);

            var snapshot = store.Snapshot();
            IList<NpcAcuteFearState> saved = AcuteFearRecords(snapshot);
            Check.True(saved != null && saved.Count == 1);
            saved[0].Intensity = 0;
            Check.True(store.ReadNpcAcuteFear(observer, 100).CurrentFear > 0,
                "Changing exported save DTO must not mutate live acute fear.");
        }

        private static void LegacySchemaMigration()
        {
            var store = KnownStore(out string observer);
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:legacy", NpcFearSeverity.Moderate, 10));
            var legacy = store.Snapshot();
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(legacy, 40);
            SetAcuteFearRecords(legacy, new List<NpcAcuteFearState> { State(observer, "hostile:legacy") });
            var restored = new WorldStateStore(0);
            restored.Restore(legacy);
            Check.True(restored.ReadNpcAcuteFear(observer, 10) == null,
                "Schema 40 must migrate without acute-fear records.");
        }

        private static void MalformedAndDuplicateRecords()
        {
            var store = KnownStore(out string observer);
            var duplicate = store.Snapshot();
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(duplicate, 42);
            SetAcuteFearRecords(duplicate, new List<NpcAcuteFearState>
            {
                State(observer, "hostile:duplicate"), State(observer, "hostile:duplicate")
            });
            Check.Throws<ArgumentException>(() => new WorldStateStore(0).Restore(duplicate));

            var malformed = store.Snapshot();
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(malformed, 42);
            var invalid = State(observer, "hostile:invalid");
            invalid.Intensity = double.NaN;
            SetAcuteFearRecords(malformed, new List<NpcAcuteFearState> { invalid });
            Check.Throws<ArgumentException>(() => new WorldStateStore(0).Restore(malformed));
        }

        private static void DuplicateObserverDifferentSource()
        {
            var store = KnownStore(out string observer);
            var duplicate = store.Snapshot();
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(duplicate, 42);
            SetAcuteFearRecords(duplicate, new List<NpcAcuteFearState>
            {
                State(observer, "hostile:first"), State(observer, "hostile:second")
            });
            Check.Throws<ArgumentException>(() => new WorldStateStore(0).Restore(duplicate));
        }

        private static void SaveFieldsAndSavedRateRoundTrip()
        {
            var source = KnownStore(out string observer);
            source.GetOrCreateNpcPersonality(observer, 7, NpcSocialBackground.Commoner);
            Check.True(source.TryObserveNpcAcuteFear(observer, "hostile:roundtrip", NpcFearSeverity.Moderate, 500));
            var sourceView = source.ReadNpcAcuteFear(observer, 900);
            var saved = source.Snapshot();
            var restored = new WorldStateStore(0);
            restored.Restore(saved);
            var restoredView = restored.ReadNpcAcuteFear(observer, 900);

            Check.Equal(sourceView.ObserverId, restoredView.ObserverId);
            Check.Equal(sourceView.SourceId, restoredView.SourceId);
            Check.Near(sourceView.Intensity, restoredView.Intensity);
            Check.Near(sourceView.ObservedGameTime, restoredView.ObservedGameTime);
            Check.Near(sourceView.RecoveryHalfLifeSeconds, restoredView.RecoveryHalfLifeSeconds);
            Check.Equal(sourceView.Severity, restoredView.Severity);
            Check.Equal(sourceView.Revision, restoredView.Revision);
            Check.Near(sourceView.CurrentFear, restoredView.CurrentFear);

            double capturedHalfLife = restoredView.RecoveryHalfLifeSeconds;
            var changedPersonality = restored.Snapshot();
            changedPersonality.NpcPersonalities[0] = new NpcPersonalityProfile { PersonId = observer,
                Sensitivity = "highly sensitive", EmotionalStability = "volatile", SelfEsteem = "insecure" };
            restored.Restore(changedPersonality);
            var afterTraitChange = restored.ReadNpcAcuteFear(observer, 900);
            Check.Near(capturedHalfLife, afterTraitChange.RecoveryHalfLifeSeconds);
            Check.Equal(restoredView.Revision, afterTraitChange.Revision);
        }

        private static void PersistedFieldsAreValidated()
        {
            var store = KnownStore(out string observer);
            AssertInvalidRecord(store, observer, "null source", state => state.SourceId = null);
            AssertInvalidRecord(store, observer, "invalid severity", state => state.Severity = (NpcFearSeverity)99);
            AssertInvalidRecord(store, observer, "nonfinite intensity", state => state.Intensity = double.PositiveInfinity);
            AssertInvalidRecord(store, observer, "negative intensity", state => state.Intensity = -0.01);
            AssertInvalidRecord(store, observer, "negative observation time", state => state.ObservedGameTime = -1);
            AssertInvalidRecord(store, observer, "zero revision", state => state.Revision = 0);
            AssertInvalidRecord(store, observer, "nonfinite half-life", state => state.RecoveryHalfLifeSeconds = double.NaN);

            var unknown = store.Snapshot();
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(unknown, 42);
            SetAcuteFearRecords(unknown, new List<NpcAcuteFearState> { State("npc:unknown", "hostile:unknown") });
            Check.Throws<ArgumentException>(() => new WorldStateStore(0).Restore(unknown));
        }

        private static void FailedRestoreIsAtomic()
        {
            var store = KnownStore(out string observer);
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:live", NpcFearSeverity.Severe, 100));
            long relationshipRevision = store.NpcRelationshipLedgerRevision;
            var before = store.ReadNpcAcuteFear(observer, 100);
            var invalid = store.Snapshot();
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(invalid, 42);
            var bad = State(observer, "hostile:bad");
            bad.Revision = 0;
            SetAcuteFearRecords(invalid, new List<NpcAcuteFearState> { bad });
            Check.Throws<ArgumentException>(() => store.Restore(invalid));
            var after = store.ReadNpcAcuteFear(observer, 100);
            Check.Equal(before.Revision, after.Revision);
            Check.Near(before.CurrentFear, after.CurrentFear);
            Check.Equal(relationshipRevision, store.NpcRelationshipLedgerRevision);
        }

        private static void RestoreDeepCopiesRecords()
        {
            var source = KnownStore(out string observer);
            Check.True(source.TryObserveNpcAcuteFear(observer, "hostile:copy", NpcFearSeverity.Severe, 100));
            var saved = source.Snapshot();
            var restored = new WorldStateStore(0);
            restored.Restore(saved);
            saved.NpcAcuteFears[0].Intensity = 0;
            Check.True(restored.ReadNpcAcuteFear(observer, 100).CurrentFear > 0);
        }

        private static void ObservationLeavesRelationshipsAlone()
        {
            var store = KnownStore(out string observer);
            long revision = store.NpcRelationshipLedgerRevision;
            Check.True(store.ReadNpcRelationship(observer, NpcDirectedRelationship.PlayerTargetId) == null);
            Check.True(store.TryObserveNpcAcuteFear(observer, "hostile:unrelated", NpcFearSeverity.Grave, 100));
            Check.Equal(revision, store.NpcRelationshipLedgerRevision);
            Check.True(store.ReadNpcRelationship(observer, NpcDirectedRelationship.PlayerTargetId) == null);
        }

        private static void AssertInvalidRecord(WorldStateStore store, string observer, string name,
            Action<NpcAcuteFearState> invalidate)
        {
            var malformed = store.Snapshot();
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(malformed, 42);
            var invalid = State(observer, "hostile:" + name.Replace(' ', '-'));
            invalidate(invalid);
            SetAcuteFearRecords(malformed, new List<NpcAcuteFearState> { invalid });
            Check.Throws<ArgumentException>(() => new WorldStateStore(0).Restore(malformed));
        }

        private static WorldStateStore KnownStore(out string observer)
        {
            var store = new WorldStateStore(42);
            observer = store.GetOrCreate(10, new[] { 20 }, 1).Households[0].Residents[0].Id;
            return store;
        }

        private static void ReplacePersonality(WorldStateStore store, NpcPersonalityProfile replacement)
        {
            var snapshot = store.Snapshot();
            snapshot.NpcPersonalities.Clear();
            snapshot.NpcPersonalities.Add(NpcPersonalityProfile.CopyValidated(replacement));
            store.Restore(snapshot);
        }

        private static NpcAcuteFearState State(string observer, string source)
        {
            return new NpcAcuteFearState { ObserverId = observer, SourceId = source, Intensity = 45,
                ObservedGameTime = 10, RecoveryHalfLifeSeconds = 7200, Severity = NpcFearSeverity.Moderate,
                Revision = 1 };
        }

        private static IList<NpcAcuteFearState> AcuteFearRecords(WorldState state)
        {
            FieldInfo field = typeof(WorldState).GetField("NpcAcuteFears");
            Check.True(field != null, "WorldState must expose schema 42 acute-fear records.");
            return field.GetValue(state) as IList<NpcAcuteFearState>;
        }

        private static void SetAcuteFearRecords(WorldState state, IList<NpcAcuteFearState> records)
        {
            FieldInfo field = typeof(WorldState).GetField("NpcAcuteFears");
            Check.True(field != null, "WorldState must expose schema 42 acute-fear records.");
            field.SetValue(state, records);
        }
    }
}
