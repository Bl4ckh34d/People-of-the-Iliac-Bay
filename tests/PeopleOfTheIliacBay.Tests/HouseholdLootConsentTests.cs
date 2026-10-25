using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdLootConsentTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Household consent uses actual saved reaction and revokes below threshold", () => {
                var store = new WorldStateStore(1);
                Check.True(!store.TryGetActualConversationDisposition("resident:owner", out var actual));
                store.SetConversationBaseline("resident:owner", 72, 100, 43);
                Check.Equal(72, store.TryGetActualConversationDisposition("resident:owner", out actual) ? actual : -1);
                int baseline, offset, factionId;
                Check.True(store.TryGetConversationBaseline("resident:owner", out baseline, out offset, out factionId));
                Check.Equal(72, baseline); Check.Equal(0, offset); Check.Equal(43, factionId);
                Check.Equal(1, store.ApplyConversationTone("resident:owner", "polite", "Good day", 101));
                Check.True(store.TryGetActualConversationDisposition("resident:owner", out actual));
                Check.Equal(73, actual);
                Check.True(!Allows(store, Home(12, "resident:owner"), 12));
                for (int i = 0; i < 4; i++) store.ApplyConversationTone("resident:owner", "polite", "Kind words " + i, 102 + i);
                Check.Equal(77, store.TryGetActualConversationDisposition("resident:owner", out actual) ? actual : -1);
                var snapshot = store.Snapshot();
                var restored = new WorldStateStore(2);
                restored.Restore(snapshot);
                Check.True(Allows(restored, Home(12, "resident:owner"), 12));
                Check.True(restored.TryGetConversationBaseline("resident:owner", out baseline, out offset, out factionId));
                Check.Equal(43, factionId);
                Check.Equal(-2, restored.ApplyConversationTone("resident:owner", "rude", "You are slow", 107));
                Check.True(Allows(restored, Home(12, "resident:owner"), 12));
                Check.Equal(-2, restored.ApplyConversationTone("resident:owner", "rude", "You are still slow", 108));
                Check.True(!Allows(restored, Home(12, "resident:owner"), 12));
            }));

            cases.Add(("Household consent needs exact living home resident and excludes non-home stock", () => {
                Func<string, int?> disposition = id => id == "resident:owner" ? 100 : (int?)null;
                var home = Home(12, "resident:owner");
                Check.True(HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, true, false, false,
                    false, false, false, false, disposition));
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 13, home, true, false, false,
                    false, false, false, false, disposition));
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, true, true, false,
                    false, false, false, false, disposition));
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, true, false, true,
                    false, false, false, false, disposition));
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, true, false, false,
                    true, false, false, false, disposition));
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, true, false, false,
                    false, true, false, false, disposition));
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, true, false, false,
                    false, false, true, false, disposition));
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, true, false, false,
                    false, false, false, true, disposition));
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, false, false, false,
                    false, false, false, false, disposition));
                home.Residents[0].IsAlive = false;
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, true, false, false,
                    false, false, false, false, disposition));
                home.Residents[0].IsAlive = true;
                home.Residents[0].HomeKey = 99;
                Check.True(!HouseholdLootConsent.AllowsPrivateHomeContainer(42, 12, home, true, false, false,
                    false, false, false, false, disposition));
            }));
        }

        private static bool Allows(WorldStateStore store, Household household, int building)
        {
            return HouseholdLootConsent.AllowsPrivateHomeContainer(42, building, household, true,
                false, false, false, false, false, false, id => {
                    int actual;
                    return store.TryGetActualConversationDisposition(id, out actual) ? (int?)actual : null;
                });
        }

        private static Household Home(int building, string residentId)
        {
            return new Household { BuildingKey = building, HeadId = residentId,
                Residents = new List<Resident> { new Resident { Id = residentId, HomeKey = building, IsAlive = true } } };
        }
    }
}
