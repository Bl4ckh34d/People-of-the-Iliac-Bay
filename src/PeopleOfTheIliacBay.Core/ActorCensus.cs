using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Diagnostic data only. Never persisted into WorldState or used to adopt actors.
    public sealed class ActorCensusObservation
    {
        public string Identity, Instance, Signature;
        public bool PersistentIdentity, Active;
        public string Source = "unknown", Appearance = "unknown", AppearanceProvider = "unknown";
        public string Role = "unknown", Building = "unknown", Quest = "unknown", Service = "unknown";
        public string Home = "unknown", Bed = "unknown", Disposition = "not adopted: read-only census", Unknown = "";
        public ActorCensusObservation Copy() { return (ActorCensusObservation)MemberwiseClone(); }
    }
    public sealed class ActorCensusRecord
    {
        public string Identity;
        // Multiple representations are retained, never mistaken for one physical actor.
        public bool IdentityCollision;
        public List<ActorCensusObservation> Observations = new List<ActorCensusObservation>();
    }
    public sealed class ActorCensus
    {
        private readonly int limit;
        private readonly SortedDictionary<string, SortedDictionary<string, ActorCensusObservation>> records =
            new SortedDictionary<string, SortedDictionary<string, ActorCensusObservation>>(StringComparer.Ordinal);
        public int ObservationCount { get; private set; }
        public bool Truncated { get; private set; }
        public ActorCensus(int maximumObservations = 8192)
        { if (maximumObservations < 1) throw new ArgumentOutOfRangeException(nameof(maximumObservations)); limit = maximumObservations; }
        public bool Observe(ActorCensusObservation source)
        {
            if (source == null || string.IsNullOrEmpty(source.Instance)) throw new ArgumentException("Census requires a survey-local instance token.");
            var observation = source.Copy();
            if (string.IsNullOrEmpty(observation.Identity))
            {
                observation.Identity = "session:" + observation.Instance;
                observation.PersistentIdentity = false;
            }
            SortedDictionary<string, ActorCensusObservation> instances;
            bool known = records.TryGetValue(observation.Identity, out instances) && instances.ContainsKey(observation.Instance);
            if (!known && ObservationCount >= limit) { Truncated = true; return false; }
            if (instances == null)
            {
                instances = new SortedDictionary<string, ActorCensusObservation>(StringComparer.Ordinal);
                records.Add(observation.Identity, instances);
            }
            instances[observation.Instance] = observation;
            if (!known) ObservationCount++;
            return true;
        }
        public List<ActorCensusRecord> Snapshot()
        {
            var result = new List<ActorCensusRecord>();
            foreach (var pair in records)
            {
                var row = new ActorCensusRecord { Identity = pair.Key, IdentityCollision = pair.Value.Count > 1 };
                foreach (var observation in pair.Value.Values) row.Observations.Add(observation.Copy());
                result.Add(row);
            }
            return result;
        }
    }
    public sealed class ActorCensusClaim
    {
        public string Identity, Kind, Home = "unknown", Bed = "unknown";
    }
    public sealed partial class WorldStateStore
    {
        // Detached, incremental ledger inspection. Null entries provide a scheduling point
        // even for empty containers. Captures the branch; a restore invalidates the scan.
        public IEnumerable<ActorCensusClaim> ReadActorCensusClaims(double now)
        {
            var source = state;
            for (int n = 0; n < source.NativePeople.Count; n++)
            {
                if (!ReferenceEquals(source, state)) throw new InvalidOperationException("World restored during actor census.");
                var p = source.NativePeople[n];
                yield return new ActorCensusClaim { Identity = p.Id,
                    Kind = p.RetiredBedroomFlat ? "registered retired bedroom flat" : "registered native person",
                    Home = p.HomeKey < 0 ? "none recorded" : p.SettlementId + ":" + p.HomeKey,
                    Bed = p.BedSlot < 0 ? "none recorded" : "slot:" + p.BedSlot + "/" + p.BedCapacity };
            }
            for (int t = 0; t < source.Settlements.Count; t++)
            {
                var town = source.Settlements[t];
                for (int h = 0; h < town.Households.Count; h++)
                {
                    var home = town.Households[h];
                    for (int r = 0; r < home.Residents.Count; r++)
                    {
                        if (!ReferenceEquals(source, state)) throw new InvalidOperationException("World restored during actor census.");
                        var p = home.Residents[r];
                        yield return new ActorCensusClaim { Identity = p.Id, Kind = "registered household resident",
                            Home = town.SettlementId + ":" + p.HomeKey,
                            Bed = "slot:" + (p.BedSlot >= 0 ? p.BedSlot : r) + "/" + home.BedCapacity };
                    }
                    yield return null;
                }
                yield return null;
            }
            for (int n = 0; n < source.InnLodgerHomes.Count; n++)
            {
                var p = source.InnLodgerHomes[n];
                yield return new ActorCensusClaim { Identity = p.PersonId, Kind = "permanent inn-lodger home claim",
                    Home = p.HomeSettlement + ":" + p.HomeKey, Bed = "slot:" + p.BedSlot + "/" + p.BedCapacity };
            }
            for (int n = 0; n < source.Travelers.Count; n++)
            {
                var p = source.Travelers[n];
                yield return new ActorCensusClaim { Identity = p.Id, Kind = "registered traveler", Home = "settlement:" + p.HomeSettlement + "; dwelling unknown" };
            }
            for (int n = 0; n < source.Lodgings.Count; n++)
            {
                var lodging = source.Lodgings[n];
                for (int p = 0; p < lodging.Occupants.Count; p++)
                {
                    var occupant = lodging.Occupants[p];
                    yield return new ActorCensusClaim { Identity = occupant.PersonId, Kind = "native lodging occupancy",
                        Bed = lodging.SettlementId + ":" + lodging.BuildingKey + ":" + occupant.BedId };
                }
                for (int p = 0; p < lodging.Bookings.Count; p++)
                {
                    var booking = lodging.Bookings[p];
                    yield return now >= booking.StartsAt && now < booking.EndsAt ? new ActorCensusClaim {
                        Identity = booking.GuestId, Kind = "current room booking; individual bed not inferred",
                        Bed = lodging.SettlementId + ":" + lodging.BuildingKey + ":room:" + booking.RoomId } : null;
                }
                yield return null;
            }
            if (!ReferenceEquals(source, state)) throw new InvalidOperationException("World restored during actor census.");
        }
    }
}
