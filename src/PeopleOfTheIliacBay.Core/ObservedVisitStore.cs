using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private readonly Dictionary<string, ObservedVisit> observedVisitIndex =
            new Dictionary<string, ObservedVisit>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> observedVisitEndsAt =
            new Dictionary<string, double>(StringComparer.Ordinal);

        public void StoreObservedVisit(ObservedVisit observed)
        {
            PublishObservedVisit(PrepareObservedVisit(observed));
        }
        public void StoreHouseholdInnVisits(ObservedVisit first,ObservedVisit second)
        {
            var a=PrepareObservedVisit(first);var b=PrepareObservedVisit(second);
            Require(a.HouseholdDayId!=null && a.HouseholdDayId==b.HouseholdDayId && a.HouseholdSourceFingerprint==b.HouseholdSourceFingerprint &&
                a.ResidentId!=b.ResidentId && a.ObservedAt==b.ObservedAt,"Paired Inn checkpoint requires one exact shared source and observation.");
            Require(a.HouseholdSeatId==null || b.HouseholdSeatId==null || a.HouseholdSeatId!=b.HouseholdSeatId,
                "Shared parents cannot retain the same chair.");
            Require((a.HouseholdSeatId==null)==(b.HouseholdSeatId==null),"Shared chair intent must retain both parents.");
            // Both copies, sources and preserved receipts are validated before
            // either index changes. A failed partner cannot publish half a tick.
            PublishObservedVisit(a);PublishObservedVisit(b);
        }
        private ObservedVisit PrepareObservedVisit(ObservedVisit observed)
        {
            var copy = ObservedVisit.Copy(observed);
            var previous = IndexedObservedVisit(copy.ResidentId);
            if(previous!=null && previous.CounterPurchaseResolved && NpcInnConsumption.SameVisit(copy,previous))
                copy.CounterPurchaseResolved=true;
            if (copy.InnConsumption == null && previous != null && NpcInnConsumption.SameVisit(copy, previous))
                copy.InnConsumption = NpcInnConsumption.CopyReceipt(previous.InnConsumption, copy);
            ValidateObservedVisitOwnerAndTarget(state, copy);
            return copy;
        }
        private void PublishObservedVisit(ObservedVisit copy)
        {
            state.ObservedVisits.RemoveAll(item => item.ResidentId == copy.ResidentId);
            state.ObservedVisits.Add(copy);
            observedVisitIndex[copy.ResidentId] = copy;
            observedVisitEndsAt[copy.ResidentId] = copy.EndsAt;
        }

        public ObservedVisit GetObservedVisit(string residentId)
        {
            if (string.IsNullOrWhiteSpace(residentId)) return null;
            ObservedVisit value;
            return observedVisitIndex.TryGetValue(residentId, out value) ? ObservedVisit.Copy(value) : null;
        }

        internal ObservedVisit IndexedObservedVisit(string residentId)
        {
            if (string.IsNullOrWhiteSpace(residentId)) return null;
            ObservedVisit value;
            return observedVisitIndex.TryGetValue(residentId, out value) ? value : null;
        }

        public bool RemoveObservedVisit(string residentId)
        {
            if (string.IsNullOrWhiteSpace(residentId)) return false;
            ObservedVisit value;
            if (!observedVisitIndex.TryGetValue(residentId, out value)) return false;
            observedVisitIndex.Remove(residentId);
            observedVisitEndsAt.Remove(residentId);
            state.ObservedVisits.RemoveAll(item => item.ResidentId == residentId);
            return true;
        }

        public bool TryObservedVisitState(string residentId, double now, out ObservedVisitSnapshot snapshot)
        {
            Require(ValidTime(now), "Invalid observed visit query time.");
            ObservedVisit value;
            if (string.IsNullOrWhiteSpace(residentId) || !observedVisitIndex.TryGetValue(residentId, out value))
            {
                snapshot = default(ObservedVisitSnapshot);
                return false;
            }
            snapshot = value.Evaluate(now);
            return true;
        }

        public bool TryObservedVisitMetadata(string residentId, out int settlementId, out int buildingKey,
            out double observedAt, out double endsAt, out double plannedEndAt, out double resumeUntil)
        {
            ObservedVisit value;
            if (string.IsNullOrWhiteSpace(residentId) || !observedVisitIndex.TryGetValue(residentId, out value))
            {
                settlementId = buildingKey = -1;
                observedAt = endsAt = plannedEndAt = resumeUntil = 0;
                return false;
            }
            settlementId = value.SettlementId;
            buildingKey = value.BuildingKey;
            observedAt = value.ObservedAt;
            endsAt = observedVisitEndsAt[residentId];
            plannedEndAt = value.PlannedEndAt;
            resumeUntil = value.ResumeUntil;
            return true;
        }

        private void IndexObservedVisits()
        {
            observedVisitIndex.Clear();
            observedVisitEndsAt.Clear();
            foreach (var value in state.ObservedVisits)
            {
                observedVisitIndex.Add(value.ResidentId, value);
                observedVisitEndsAt.Add(value.ResidentId, value.EndsAt);
            }
        }

        private static List<ObservedVisit> CopyObservedVisits(WorldState source)
        {
            var result = new List<ObservedVisit>();
            var residents = new HashSet<string>(StringComparer.Ordinal);
            if (source.ObservedVisits == null) return result;
            foreach (var value in source.ObservedVisits)
            {
                var copy = ObservedVisit.Copy(value);
                Require(residents.Add(copy.ResidentId), "Duplicate observed visit resident.");
                Require(source.Version>=70 || copy.HouseholdDayId==null && copy.HouseholdSourceFingerprint==null,
                    "Old schema contains future shared Inn visit fields.");
                Require(source.Version>=71 || copy.HouseholdSeatId==null,"Old schema contains future shared Inn chair intent.");
                // Family cross-references are checked after both dated plans
                // and movement records have been copied and validated.
                if(copy.HouseholdDayId==null)ValidateObservedVisitOwnerAndTarget(source, copy);
                result.Add(copy);
            }
            return result;
        }

        private static void ValidateObservedVisitOwnerAndTarget(WorldState world, ObservedVisit observed)
        {
            if(observed.HouseholdDayId!=null){ValidateHouseholdInnVisit(world,observed,true);return;}
            Require(!HasEnteredHouseholdInnOwner(world,observed),"An entered family Inn visit requires its shared event provenance.");
            Resident owner = null;
            foreach (var town in world.Settlements)
            {
                foreach (var home in town.Households)
                {
                    owner = home.Residents.Find(resident => resident.Id == observed.ResidentId);
                    if (owner != null) break;
                }
                if (owner != null) break;
            }
            Require(owner != null, "Observed visit needs a known resident.");

            var targetTown = world.Settlements.Find(town => town.SettlementId == observed.SettlementId);
            Require(targetTown != null, "Observed visit has an unknown target settlement.");
            var routines = world.Routines.Find(town => town.SettlementId == observed.SettlementId);
            var routine = routines == null ? null : routines.Residents.Find(item => item.ResidentId == observed.ResidentId);
            bool knownTarget = routine != null && routine.DestinationKey == observed.BuildingKey;
            if (!knownTarget && routine != null && routine.Itinerary != null)
                knownTarget = routine.Itinerary.Stops.Exists(stop => stop.BuildingKey == observed.BuildingKey);
            if (!knownTarget && routine != null && routine.AdditionalOutings != null)
            {
                foreach (var outing in routine.AdditionalOutings)
                {
                    if (outing.Itinerary.Stops.Exists(stop => stop.BuildingKey == observed.BuildingKey))
                    { knownTarget = true; break; }
                }
            }
            if (!knownTarget && world.ResidentTrips != null)
            {
                foreach (var trip in world.ResidentTrips)
                {
                    if (trip.ResidentId != observed.ResidentId) continue;
                    if (trip.DestinationSettlement == observed.SettlementId && trip.Destination != null &&
                        trip.Destination.Stops.Exists(stop => stop.BuildingKey == observed.BuildingKey &&
                            (stop.Purpose == ResidentVisitPurpose.Shop || stop.Purpose == ResidentVisitPurpose.Social)))
                    { knownTarget = true; break; }
                }
            }
            Require(knownTarget, "Observed visit target is not in the resident's scheduled routine or trip.");
        }
    }
}
