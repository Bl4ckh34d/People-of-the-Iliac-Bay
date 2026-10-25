using System;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        try
        {
            // Identity must never collapse actors merely because their sprites match.
            var census = new ActorCensus(3);
            var first = new ActorCensusObservation { Identity = "native:1:2:3:4", Instance = "one", Signature = "hash:3", Appearance = "182:1" };
            census.Observe(first); census.Observe(first);
            census.Observe(new ActorCensusObservation { Identity = "native:1:2:5:4", Instance = "two", Signature = "hash:5", Appearance = "182:1" });
            Require(census.Snapshot().Count == 2 && census.ObservationCount == 2, "Repeated survey duplicates or identical sprite conflation");
            first.Appearance = "changed outside census";
            Require(census.Snapshot()[0].Observations[0].Appearance == "182:1", "Census retained mutable caller record");
            var detached = census.Snapshot(); detached[0].Observations[0].Appearance = "changed snapshot";
            Require(census.Snapshot()[0].Observations[0].Appearance == "182:1", "Snapshot exposes internal records");
            census.Observe(new ActorCensusObservation { Identity = "native:1:2:3:4", Instance = "three", Signature = "hash:999" });
            Require(census.Snapshot()[0].IdentityCollision && census.Snapshot()[0].Observations.Count == 2, "Colliding identities were silently conflated");
            Require(!census.Observe(new ActorCensusObservation { Identity = "overflow", Instance = "four" }) && census.Truncated, "Observation cap ignored");
            Require(census.Observe(new ActorCensusObservation { Identity = "native:1:2:5:4", Instance = "two", Appearance = "updated" }), "Cap prevents updating known observation");
            Require(census.ObservationCount == 3, "Known observation consumes capacity twice");
            var reversed = new ActorCensus(10);
            reversed.Observe(new ActorCensusObservation { Identity = "z", Instance = "later" });
            reversed.Observe(new ActorCensusObservation { Identity = "a", Instance = "earlier" });
            Require(reversed.Snapshot()[0].Identity == "a", "Output order depends on provider creation order");
            var reloaded = new ActorCensus(10);
            reloaded.Observe(new ActorCensusObservation { Identity = "a", Instance = "new runtime object" });
            Require(reloaded.Snapshot()[0].Identity == reversed.Snapshot()[0].Identity, "Stable identity depends on runtime object");
            var unknowns = new ActorCensus(10);
            unknowns.Observe(new ActorCensusObservation { Instance = "s:1", Appearance = "182:1" });
            unknowns.Observe(new ActorCensusObservation { Instance = "s:2", Appearance = "182:1" });
            Require(unknowns.Snapshot().Count == 2 && unknowns.Snapshot().All(r => !r.Observations[0].PersistentIdentity), "Unknown actors share identity or claim persistence");
            var store = new WorldStateStore(42);
            store.GetOrCreate(1, new[] { 8 }, 1);
            var native = store.RegisterNativePerson(1, 4, 11, "Keeper Kinghart", ResidentSex.Male, 1, 41, 182, 1);
            store.AssignNativeBed(native.Id, new HomeProfile(4, 2), new HomeProfile[0]);
            var options = new JsonSerializerOptions { IncludeFields = true };
            string before = JsonSerializer.Serialize(store.Snapshot(), options);
            var claims = store.ReadActorCensusClaims(100).Where(c => c != null).ToList();
            Require(claims.Any(c => c.Identity == native.Id && c.Home == "1:4" && c.Bed == "slot:0/2"), "Existing native home/bed not inventoried");
            claims[0].Home = "tampered";
            Require(before == JsonSerializer.Serialize(store.Snapshot(), options), "Census changed world state");
            var scan = store.ReadActorCensusClaims(100).GetEnumerator();
            Require(scan.MoveNext(), "Fixture must contain a native claim");
            store.Restore(store.Snapshot());
            bool aborted = false;
            try { while (scan.MoveNext()) { } } catch (InvalidOperationException) { aborted = true; }
            Require(aborted, "Restore allowed census to mix save branches");
            store.RegisterLodging(1, 4, new[] { new LodgingRoom { Id = "room-a", Beds = new System.Collections.Generic.List<string> { "bed-a" } } });
            Require(store.TryBookLodging(1, 4, "booking", "guest", 2, false, 1, 10, 20), "Fixture booking failed");
            Require(store.ReadActorCensusClaims(15).Any(c => c != null && c.Identity == "guest" && c.Bed == "1:4:room:room-a"), "Current booking omitted");
            Require(!store.ReadActorCensusClaims(20).Any(c => c != null && c.Identity == "guest"), "Expired booking reported as current");
            Console.WriteLine("PASS actor census: identity, duplicate sprites, collisions, detached records, caps, provider order, reload keys, unknowns, read-only claims");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
