using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcPersonalityStoreTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Native story suppression persists and silences random memory and voice traits", () =>
            {
                var store = KnownNativeStore(); const string id = "npc:1:5:12";
                var original = store.GetOrCreateNpcPersonality(id, 5, NpcSocialBackground.Merchant);
                store.SetNpcRandomPersonalitySuppressed(id, true);
                Check.True(store.ReadNpcPersonality(id) == null);
                Check.Equal(0, NpcPresentationState.Capture(store, id, 0, false).SavedTraits.Count);
                Check.Throws<ArgumentException>(() => store.GetOrCreateNpcPersonality(id, 5, NpcSocialBackground.Merchant));
                var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.ReadNpcPersonality(id) == null);
                restored.SetNpcRandomPersonalitySuppressed(id, false);
                Check.Equal(original.Description(), restored.ReadNpcPersonality(id).Description());
            }));
            cases.Add(("A generic sidequest resident retains the same personality and recovery traits", () =>
            {
                var store = new WorldStateStore(42);
                var resident = store.GetOrCreate(10, new[] { 20 }).Households[0].Residents[0];
                store.GetOrCreateNpcPersonality(resident.Id, resident.AppearanceSeed, NpcSocialBackground.Commoner);
                var state = store.Snapshot();
                state.NpcPersonalities[0] = new NpcPersonalityProfile { PersonId=resident.Id,
                    EmotionalStability="volatile", Sensitivity="highly sensitive", SelfEsteem="insecure" };
                state.Settlements[0].Households[0].Residents[0].IsQuestProtected = true;
                store.Restore(state);
                Check.Equal(state.NpcPersonalities[0].Description(), store.ReadNpcPersonality(resident.Id).Description());
                Check.Equal(MoodRecovery.HalfLifeSeconds(42, state.NpcPersonalities[0]), store.ConversationMoodHalfLifeSeconds(resident.Id));
                Check.Equal(3, NpcPresentationState.Capture(store, resident.Id, 0, false).SavedTraits.Count);
            }));
            cases.Add(("NPC personality store returns detached copies and preserves saved traits", () =>
            {
                var store = KnownNativeStore();
                var created = store.GetOrCreateNpcPersonality("npc:1:5:12", 7, NpcSocialBackground.Noble);
                string description = created.Description();
                created.Warmth = "invalid";
                Check.Equal(description, store.ReadNpcPersonality("npc:1:5:12").Description());
                var snapshot = store.Snapshot();
                snapshot.NpcPersonalities[0].Patience = "invalid";
                Check.Equal(description, store.ReadNpcPersonality("npc:1:5:12").Description());
                Check.True(store.ReadNpcPersonality("npc:1:5:12") != created);
            }));
            cases.Add(("NPC personality store creates once and ignores later generation inputs", () =>
            {
                var store = KnownNativeStore();
                var first = store.GetOrCreateNpcPersonality("npc:1:5:12", 7, NpcSocialBackground.Noble);
                var later = store.GetOrCreateNpcPersonality("npc:1:5:12", 99, NpcSocialBackground.Commoner);
                Check.Equal(first.Description(), later.Description());
                Check.Equal(1, store.Snapshot().NpcPersonalities.Count);
                Check.Throws<ArgumentException>(() => store.GetOrCreateNpcPersonality("npc:1:5:999", 1, NpcSocialBackground.Unknown));
                Check.True(store.ReadNpcPersonality("npc:1:5:999") == null);
            }));
            cases.Add(("NPC personality store recognizes generated residents and travelers", () =>
            {
                var store = new WorldStateStore(42);
                var resident = store.GetOrCreate(10, new[] { 20 }).Households[0].Residents[0];
                var residentProfile = store.GetOrCreateNpcPersonality(resident.Id, resident.AppearanceSeed, NpcSocialBackground.Commoner);
                store.RegisterTraveler(new TravelerPerson { Id = "traveler:personality", Name = "Visitor",
                    HomeSettlement = 11, Race = 0, Sex = ResidentSex.Male });
                var travelerProfile = store.GetOrCreateNpcPersonality("traveler:personality", 8, NpcSocialBackground.Unknown);
                var restored = new WorldStateStore(0);
                restored.Restore(store.Snapshot());
                Check.Equal(residentProfile.Description(), restored.ReadNpcPersonality(resident.Id).Description());
                Check.Equal(travelerProfile.Description(), restored.ReadNpcPersonality("traveler:personality").Description());
            }));
            cases.Add(("Legacy personality restore selects three saved values once and preserves the subset on reload", () =>
            {
                var source = KnownNativeStore(); var legacy = source.Snapshot();
                var original = NpcPersonalityGenerator.GenerateLegacyV2("npc:1:5:12", 7, NpcSocialBackground.Noble);
                legacy.NpcPersonalities.Add(original);
                var restored = new WorldStateStore(0); restored.Restore(legacy);
                var migrated = restored.ReadNpcPersonality(original.PersonId);
                Check.Equal(3, migrated.Version); Check.Equal(3, migrated.SelectedTraits.Count);
                foreach (var trait in migrated.SelectedTraits) Check.Equal(original.SelectedTraits[trait.Key], trait.Value);
                var again = new WorldStateStore(0); again.Restore(restored.Snapshot());
                Check.Equal(migrated.Description(), again.ReadNpcPersonality(original.PersonId).Description());
                Check.Equal(migrated.Description(), again.GetOrCreateNpcPersonality(original.PersonId, 999,
                    NpcSocialBackground.Unknown).Description());
            }));
            cases.Add(("Generic sidequest residents can receive an ordinary personality", () =>
            {
                var store = new WorldStateStore(42);
                var resident = store.GetOrCreate(10, new[] { 20 }).Households[0].Residents[0];
                var state = store.Snapshot(); state.Settlements[0].Households[0].Residents[0].IsQuestProtected = true;
                store.Restore(state);
                var created = store.GetOrCreateNpcPersonality(resident.Id, resident.AppearanceSeed, NpcSocialBackground.Commoner);
                Check.Equal(3, created.SelectedTraits.Count);
                Check.Equal(created.Description(), store.ReadNpcPersonality(resident.Id).Description());
            }));
            cases.Add(("Story suppression cannot create a duplicate profile through experience capture", () =>
            {
                var store = new WorldStateStore(42);
                var resident = store.GetOrCreate(10, new[] { 20 }).Households[0].Residents[0];
                store.GetOrCreateNpcPersonality(resident.Id, resident.AppearanceSeed, NpcSocialBackground.Commoner);
                store.SetNpcRandomPersonalitySuppressed(resident.Id, true);
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, NpcExperienceEvent.FromNativeOutcome(
                    resident.Id, resident.Id, "receipt:suppressed", "episode:suppressed", "observed",
                    NpcExperienceEventKind.Healing, 10, NpcResponsibleActorAttribution.Unknown)));
                NpcExperienceAppraisalTicket ticket;
                Check.True(!store.TryCaptureNpcExperienceAppraisal(resident.Id, 100, out ticket));
                Check.Equal(1, store.Snapshot().NpcPersonalities.Count);
                Check.True(store.ReadNpcPersonality(resident.Id) == null);
                Check.Equal(1, store.ReadPendingNpcExperienceObservers(10, null).Count);
            }));
            cases.Add(("Malformed and duplicate personality records reject restore atomically", () =>
            {
                var store = KnownNativeStore();
                var original = store.GetOrCreateNpcPersonality("npc:1:5:12", 7, NpcSocialBackground.Noble);
                var duplicate = store.Snapshot();
                duplicate.NpcPersonalities.Add(NpcPersonalityProfile.CopyValidated(duplicate.NpcPersonalities[0]));
                Check.Throws<ArgumentException>(() => store.Restore(duplicate));
                Check.Equal(original.Description(), store.ReadNpcPersonality("npc:1:5:12").Description());

                var malformed = store.Snapshot();
                malformed.NpcPersonalities[0].Vocabulary = "invented";
                Check.Throws<ArgumentException>(() => store.Restore(malformed));
                Check.Equal(original.Description(), store.ReadNpcPersonality("npc:1:5:12").Description());

                var nullProfile = store.Snapshot();
                nullProfile.NpcPersonalities[0] = null;
                Check.Throws<ArgumentException>(() => store.Restore(nullProfile));
                Check.Equal(original.Description(), store.ReadNpcPersonality("npc:1:5:12").Description());

                var missingLedger = store.Snapshot();
                missingLedger.NpcPersonalities = null;
                Check.Throws<ArgumentException>(() => store.Restore(missingLedger));
                Check.Equal(original.Description(), store.ReadNpcPersonality("npc:1:5:12").Description());

                var unknown = store.Snapshot();
                unknown.NpcPersonalities[0].PersonId = "npc:1:5:999";
                Check.Throws<ArgumentException>(() => store.Restore(unknown));
                Check.Equal(original.Description(), store.ReadNpcPersonality("npc:1:5:12").Description());
            }));
        }

        private static WorldStateStore KnownNativeStore()
        {
            var store = new WorldStateStore(42);
            store.GetOrCreate(1, new[] { 5 });
            store.RegisterNativePerson(1, 5, 12, "Keeper Kinghart", ResidentSex.Male, 1, 0, 182, 2);
            return store;
        }
    }
}
