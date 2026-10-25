using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceObserverPagingTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Experience observer cursor reaches beyond a blocked first batch", () =>
            {
                var store = Create();
                var first = store.ReadPendingNpcExperienceObservers(32, null);
                Check.Equal(32, first.Count);
                foreach (var id in first)
                {
                    // These actors have native events but unresolved factions, so the consumer must
                    // leave their events pending while the observer cursor continues past this page.
                    store.ResolveResident(id).NativeIdentity = new NativePerson { Id = id, FactionId = 42 };
                    NpcExperienceAppraisalTicket ticket;
                    Check.True(!store.TryCaptureNpcExperienceAppraisal(id, 100, out ticket));
                }
                var second = store.ReadPendingNpcExperienceObservers(32, first[first.Count - 1]);
                Check.Equal(8, second.Count);
                Check.True(!first.Intersect(second).Any());
                Check.Equal(0, store.ReadPendingNpcExperienceObservers(32, second[second.Count - 1]).Count);
                Check.Equal(40, first.Concat(second).Distinct().Count());
            }));
            cases.Add(("Experience observer pages are detached and restart without consuming evidence", () =>
            {
                var store = Create();
                var first = store.ReadPendingNpcExperienceObservers(5, null);
                var expected = first.ToArray();
                first.Clear();
                Check.Equal(string.Join("|", expected), string.Join("|", store.ReadPendingNpcExperienceObservers(5, null)));
                Check.Equal(40, store.Snapshot().NpcExperienceInbox.Entries.Count);
            }));
            cases.Add(("Experience observer cursor rejects invalid bounds", () =>
            {
                var store = Create();
                bool threw = false;
                try { store.ReadPendingNpcExperienceObservers(0, null); }
                catch (ArgumentException) { threw = true; }
                Check.True(threw);
                threw = false;
                try { store.ReadPendingNpcExperienceObservers(32, new string('x', 241)); }
                catch (ArgumentException) { threw = true; }
                Check.True(threw);
            }));
        }

        private static WorldStateStore Create()
        {
            var store = new WorldStateStore(1709);
            var town = store.GetOrCreate(20, Enumerable.Range(100, 40).ToArray(), 1);
            foreach (var household in town.Households)
            {
                var id = household.Residents[0].Id;
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, NpcExperienceEvent.FromNativeOutcome(
                    id, id, "receipt:paging", "episode:paging", "observed", NpcExperienceEventKind.Healing,
                    10, NpcResponsibleActorAttribution.Unknown)));
            }
            return store;
        }
    }
}
