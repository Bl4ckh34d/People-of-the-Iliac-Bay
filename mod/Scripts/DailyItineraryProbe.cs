using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Opt-in native acceptance for real, saved daily itineraries. This probe never
    // creates routes, changes world time, enters a building, or writes a save.
    public static class DailyItineraryProbe
    {
        private sealed class Candidate
        {
            public Household Home;
            public Resident Resident;
            public ResidentRoutine Routine;
            public ResidentVisitJourney Journey;
            public List<ResidentVisitSnapshot> Visits;
            public double TravelSampleSeconds;
        }

        public static IEnumerator Run(LivingCitiesMod adapter, StreetPopulation streets)
        {
            if (!adapter || !streets) throw new InvalidOperationException("Daily itinerary probe requires live town components.");
            var game = GameManager.Instance;
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                game.PlayerEnterExit.TransitionExterior();
                float exteriorDeadline = Time.realtimeSinceStartup + 60;
                while ((game.PlayerEnterExit.IsPlayerInsideBuilding || !game.StreamingWorld.IsReady ||
                    game.StreamingWorld.IsRepositioningPlayer) && Time.realtimeSinceStartup < exteriorDeadline) yield return null;
                if (game.PlayerEnterExit.IsPlayerInsideBuilding || !game.StreamingWorld.IsReady ||
                    game.StreamingWorld.IsRepositioningPlayer)
                    throw new InvalidOperationException("Daily itinerary probe could not exit preview interior for routine upgrade.");
            }

            // A legacy town can report Ready while it keeps existing actors visible.
            // Read the full world snapshot only every half-second while upgrade runs.
            float deadline = Time.realtimeSinceStartup + 240;
            BuildingDirectory directory = null;
            WorldState snapshot = null;
            SettlementPopulation population = null;
            TownRoutineState town = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (streets.Ready && game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer)
                {
                    directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                    if (directory)
                    {
                        var current = adapter.GetSaveData() as WorldState;
                        var currentTown = current == null ? null : current.Routines.Find(item => item.SettlementId == directory.MapID);
                        if (currentTown != null && currentTown.ItineraryVersion == 1)
                        {
                            snapshot = current;
                            town = currentTown;
                            population = snapshot.Settlements.Find(item => item.SettlementId == directory.MapID);
                            break;
                        }
                    }
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (directory == null || snapshot == null || town == null || population == null)
                throw new InvalidOperationException("Daily itinerary probe timed out waiting for exterior town routine upgrade version 1.");
            var geometryField = typeof(StreetPopulation).GetField("geometry", BindingFlags.Instance | BindingFlags.NonPublic);
            var geometry = geometryField == null ? null : geometryField.GetValue(streets) as TownGeometry;
            if (geometry == null) throw new InvalidOperationException("Daily itinerary probe found no surveyed town geometry.");
            if (snapshot.Version != new WorldState().Version) throw new InvalidOperationException("Daily itinerary probe requires current schema; got " + snapshot.Version + ".");

            int wakeHour = ReadHour(streets, "wakeHour", 6);
            int sleepHour = ReadHour(streets, "sleepHour", 22);
            int eligibleAdults = 0, plannedAdults = 0, multiStopAdults = 0;
            var serviceBuildings = new HashSet<int>();
            Candidate selected = null;
            var nativeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var person in snapshot.NativePeople)
                if (person != null && !string.IsNullOrEmpty(person.Id)) nativeIds.Add(person.Id);
            var tripIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var trip in snapshot.ResidentTrips)
                if (trip != null && !string.IsNullOrEmpty(trip.ResidentId)) tripIds.Add(trip.ResidentId);

            foreach (var home in population.Households)
            {
                foreach (var resident in home.Residents)
                {
                    if (resident == null || !resident.IsAlive || resident.AgeGroup != ResidentAgeGroup.Adult ||
                        resident.IsQuestProtected || nativeIds.Contains(resident.Id)) continue;
                    eligibleAdults++;
                    var routine = town.Residents.Find(item => item != null && item.ResidentId == resident.Id);
                    if (routine == null || routine.Itinerary == null) continue;
                    plannedAdults++;
                    var itinerary = routine.Itinerary;
                    if (itinerary.Stops == null || itinerary.Stops.Count < 2) continue;
                    multiStopAdults++;
                    var uniqueStops = new HashSet<int>();
                    foreach (var stop in itinerary.Stops)
                    {
                        if (stop == null || !uniqueStops.Add(stop.BuildingKey))
                            throw new InvalidOperationException("Saved itinerary repeats or omits a service stop for " + resident.Id + ".");
                        BuildingSummary summary;
                        if (!directory.GetBuildingSummary(stop.BuildingKey, out summary) ||
                            !StreetPopulation.IsVisitDestination(summary.BuildingType))
                            throw new InvalidOperationException("Saved itinerary stop is not a current service building: " + stop.BuildingKey + ".");
                        TownGeometry.Entrance entrance;
                        if (!geometry.Entrances.TryGetValue(stop.BuildingKey, out entrance) || stop.Route == null || stop.Route.Count < 2)
                            throw new InvalidOperationException("Saved itinerary has no surveyed destination doorway: " + stop.BuildingKey + ".");
                        var endpoint = stop.Route[stop.Route.Count - 1];
                        if (Math.Abs(endpoint.X - entrance.LocalDoor.x) > 0.01 ||
                            Math.Abs(endpoint.Z - entrance.LocalDoor.z) > 0.01)
                            throw new InvalidOperationException("Saved itinerary route ends at the wrong destination doorway: " + stop.BuildingKey + ".");
                        serviceBuildings.Add(stop.BuildingKey);
                    }

                    var schedule = DailySchedule.ItineraryDay(resident.HomeKey, itinerary,
                        routine.ItineraryDepartureSeconds, wakeHour * 60, sleepHour * 60);
                    var journey = ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds, itinerary);
                    var visits = new List<ResidentVisitSnapshot>(journey.ScheduledVisits());
                    if (visits.Count != itinerary.Stops.Count)
                        throw new InvalidOperationException("Saved itinerary visit count changed during evaluation for " + resident.Id + ".");
                    for (int i = 0; i < visits.Count; i++)
                    {
                        var visit = visits[i];
                        var stop = itinerary.Stops[i];
                        double startsAt, endsAt;
                        if (visit.BuildingKey != stop.BuildingKey || visit.EnterAt < stop.OpenMinute * 60d - 0.001 ||
                            visit.LeaveAt > stop.CloseMinute * 60d + 0.001 ||
                            visit.EnterAt < wakeHour * 3600d || visit.LeaveAt > sleepHour * 3600d ||
                            !schedule.ContainsVisitBuilding(stop.BuildingKey) ||
                            !schedule.TryVisitWindowForBuilding(0, stop.BuildingKey, out startsAt, out endsAt) ||
                            Math.Abs(startsAt - visit.EnterAt) > 0.001 || Math.Abs(endsAt - visit.LeaveAt) > 0.001)
                            throw new InvalidOperationException("Saved itinerary violates service or waking window for " + resident.Id + ".");
                    }

                    // Choose a live local schedule with no traveler override and a
                    // complete household, so GetState and lock checks use real plans.
                    double travelSample;
                    if (selected == null && uniqueStops.Count >= 2 && journey.EndsAt + 120 < sleepHour * 3600d &&
                        TryTravelSample(routine, visits, out travelSample) &&
                        !tripIds.Contains(resident.Id) &&
                        CompleteHouseholdHasPlans(home, town, nativeIds, tripIds))
                    {
                        selected = new Candidate { Home = home, Resident = resident, Routine = routine,
                            Journey = journey, Visits = visits, TravelSampleSeconds = travelSample };
                    }
                }
            }

            if (plannedAdults == 0 || multiStopAdults == 0 || serviceBuildings.Count < 2)
                throw new InvalidOperationException("No saved multi-stop adult itineraries cover multiple service buildings.");
            if (selected == null) throw new InvalidOperationException("No multi-stop itinerary has a complete household suitable for native state checks.");

            double dayStart = Math.Floor(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() / 86400d) * 86400d;
            double departure = dayStart + selected.Routine.ItineraryDepartureSeconds;
            var beforeDeparture = streets.GetState(selected.Resident.Id, departure - 1);
            var leaving = streets.GetState(selected.Resident.Id, departure + 61);
            var repeatedLeaving = streets.GetState(selected.Resident.Id, departure + 61);
            if (beforeDeparture.BuildingKey != selected.Home.BuildingKey || beforeDeparture.Activity == Activity.Travel ||
                leaving.Activity == Activity.Home || leaving.Activity == Activity.Sleep ||
                !SameState(leaving, repeatedLeaving))
                throw new InvalidOperationException("Daily itinerary did not preserve home-before-departure or repeatable departure state.");

            int sampledVisits = 0;
            foreach (var visit in selected.Visits)
            {
                double at = dayStart + (visit.EnterAt + visit.LeaveAt) / 2;
                var state = streets.GetState(selected.Resident.Id, at);
                if (state.Activity != Activity.Visit || state.BuildingKey != visit.BuildingKey)
                    throw new InvalidOperationException("Daily itinerary GetState missed saved service visit " + visit.BuildingKey + ".");
                sampledVisits++;
            }

            // Sample first route halfway through its saved walk. Two identical
            // time queries must return exact same route position.
            double travelAt = dayStart + selected.TravelSampleSeconds;
            var travel = streets.GetState(selected.Resident.Id, travelAt);
            var repeatedTravel = streets.GetState(selected.Resident.Id, travelAt);
            if (travel.Activity != Activity.Travel || travel.BuildingKey >= 0 || !SameState(travel, repeatedTravel) ||
                !Finite(travel.Position.X) || !Finite(travel.Position.Z))
                throw new InvalidOperationException("Daily itinerary route position is not stable at identical time.");

            double returnedAt = dayStart + Math.Ceiling(selected.Journey.EndsAt / 60d) * 60d + 60;
            var returned = streets.GetState(selected.Resident.Id, returnedAt);
            if (returned.BuildingKey != selected.Home.BuildingKey || returned.Activity != Activity.Home)
                throw new InvalidOperationException("Daily itinerary did not return resident to original home before sleep.");
            double sleepAt = dayStart + 23.5 * 3600;
            var sleeping = streets.GetState(selected.Resident.Id, sleepAt);
            if (sleeping.BuildingKey != selected.Home.BuildingKey || sleeping.Activity != Activity.Sleep)
                throw new InvalidOperationException("Daily itinerary resident is not home and asleep at 23:30.");
            bool awakeHomeLocked = HouseholdAccess.RoutineLocked(selected.Home, resident =>
                streets.GetState(resident.Id, returnedAt));
            bool asleepHomeLocked = HouseholdAccess.RoutineLocked(selected.Home, resident =>
                streets.GetState(resident.Id, sleepAt));
            if (awakeHomeLocked || !asleepHomeLocked)
                throw new InvalidOperationException("Household lock did not follow generated home presence and sleep state.");

            var original = selected.Resident;
            var json = SaveLoadManager.Serialize(typeof(WorldState), snapshot);
            LogRoutinePayloadSizes(json, town);
            var decoded = SaveLoadManager.Deserialize(typeof(WorldState), json) as WorldState;
            if (decoded == null || decoded.Version != snapshot.Version) throw new InvalidOperationException("FullSerializer changed world state schema.");
            var restoredTown = decoded.Routines.Find(item => item.SettlementId == directory.MapID);
            var restoredPopulation = decoded.Settlements.Find(item => item.SettlementId == directory.MapID);
            var restoredRoutine = restoredTown == null ? null : restoredTown.Residents.Find(item => item.ResidentId == original.Id);
            var restoredResident = FindResident(restoredPopulation, original.Id);
            string itineraryDifference = DescribeItineraryDifference(selected.Routine, restoredRoutine);
            string identityDifference = DescribeIdentityDifference(original, restoredResident);
            if (restoredTown == null || restoredTown.ItineraryVersion != 1 || itineraryDifference != null || identityDifference != null)
                throw new InvalidOperationException("FullSerializer round-trip mismatch: townVersion=" +
                    (restoredTown == null ? "missing" : restoredTown.ItineraryVersion.ToString()) +
                    "; itinerary=" + (itineraryDifference ?? "pass") + "; identity=" + (identityDifference ?? "pass") +
                    "; jsonChars=" + json.Length + "; resident=" + original.Id + ".");
            var restoredStore = new WorldStateStore(0,LivingCitiesMod.ConversationArchiveRoot());
            restoredStore.Restore(decoded);
            var storeRoutine = restoredStore.GetRoutines(directory.MapID).Residents.Find(item => item.ResidentId == original.Id);
            if (!SameItinerary(selected.Routine, storeRoutine))
                throw new InvalidOperationException("WorldStateStore validation changed native itinerary after FullSerializer round-trip.");

            Debug.Log("LIVING_CITIES_DAILY_ITINERARY_PASS: map=" + directory.MapID + "; eligibleAdults=" + eligibleAdults +
                "; adultsWithItineraries=" + plannedAdults + "; multiStopAdults=" + multiStopAdults +
                "; distinctServiceBuildings=" + serviceBuildings.Count + "; sampledVisits=" + sampledVisits +
                "; sampleResident=" + original.Id + "; stops=" + selected.Visits.Count +
                "; originalIdentity=" + original.FullName + "; nativeFullSerializer=pass; stablePosition=pass; homeReturnSleepLock=pass");
        }

        private static bool CompleteHouseholdHasPlans(Household home, TownRoutineState town,
            HashSet<string> nativeIds, HashSet<string> tripIds)
        {
            foreach (var member in home.Residents)
            {
                if (member == null || !member.IsAlive) continue;
                if (member.IsQuestProtected || nativeIds.Contains(member.Id) || tripIds.Contains(member.Id) ||
                    !town.Residents.Exists(item => item != null && item.ResidentId == member.Id)) return false;
            }
            return true;
        }

        private static bool TryTravelSample(ResidentRoutine routine, List<ResidentVisitSnapshot> visits, out double seconds)
        {
            double start = routine.ItineraryDepartureSeconds;
            for (int i = 0; i < routine.Itinerary.Stops.Count; i++)
            {
                var stop = routine.Itinerary.Stops[i];
                if (stop.WalkSeconds >= 180)
                {
                    seconds = start + stop.WalkSeconds / 2;
                    return true;
                }
                start = visits[i].LeaveAt;
            }
            if (routine.Itinerary.ExitWalkSeconds >= 180)
            {
                seconds = visits[visits.Count - 1].LeaveAt + routine.Itinerary.ExitWalkSeconds / 2;
                return true;
            }
            seconds = 0;
            return false;
        }

        private static Resident FindResident(SettlementPopulation population, string id)
        {
            if (population == null) return null;
            foreach (var home in population.Households)
            {
                var resident = home.Residents.Find(item => item != null && item.Id == id);
                if (resident != null) return resident;
            }
            return null;
        }

        private static int ReadHour(StreetPopulation streets, string fieldName, int fallback)
        {
            var field = typeof(StreetPopulation).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) return fallback;
            return (int)field.GetValue(streets);
        }

        private static bool SameItinerary(ResidentRoutine left, ResidentRoutine right)
        { return DescribeItineraryDifference(left, right) == null; }

        private static string DescribeItineraryDifference(ResidentRoutine left, ResidentRoutine right)
        {
            if (left == null || right == null) return left == right ? null : "routine null mismatch";
            if (left.ResidentId != right.ResidentId) return "ResidentId " + left.ResidentId + " != " + right.ResidentId;
            string difference = NumericDifference("ItineraryDepartureSeconds", left.ItineraryDepartureSeconds, right.ItineraryDepartureSeconds, 0.000001);
            if (difference != null) return difference;
            if (left.Itinerary == null || right.Itinerary == null)
                return left.Itinerary == right.Itinerary ? null : "Itinerary null mismatch";
            if (left.Itinerary.Stops == null || right.Itinerary.Stops == null)
                return left.Itinerary.Stops == right.Itinerary.Stops ? null : "Itinerary.Stops null mismatch";
            if (left.Itinerary.Stops.Count != right.Itinerary.Stops.Count)
                return "Itinerary.Stops.Count " + left.Itinerary.Stops.Count + " != " + right.Itinerary.Stops.Count;
            difference = PointDifference("ExitRoute", left.Itinerary.ExitRoute, right.Itinerary.ExitRoute);
            if (difference != null) return difference;
            difference = NumericDifference("ExitWalkSeconds", left.Itinerary.ExitWalkSeconds, right.Itinerary.ExitWalkSeconds, 0.000001);
            if (difference != null) return difference;
            difference = NumericDifference("EntryGateSeconds", left.Itinerary.EntryGateSeconds, right.Itinerary.EntryGateSeconds, 0.000001);
            if (difference != null) return difference;
            difference = NumericDifference("ExitGateSeconds", left.Itinerary.ExitGateSeconds, right.Itinerary.ExitGateSeconds, 0.000001);
            if (difference != null) return difference;
            if (left.Itinerary.Walled != right.Itinerary.Walled) return "Walled " + left.Itinerary.Walled + " != " + right.Itinerary.Walled;
            if (left.Itinerary.ThroughRoute != right.Itinerary.ThroughRoute)
                return "ThroughRoute " + left.Itinerary.ThroughRoute + " != " + right.Itinerary.ThroughRoute;
            for (int i = 0; i < left.Itinerary.Stops.Count; i++)
            {
                var a = left.Itinerary.Stops[i]; var b = right.Itinerary.Stops[i];
                if (a == null || b == null) return a == b ? null : "Stops[" + i + "] null mismatch";
                string prefix = "Stops[" + i + "].";
                if (a.BuildingKey != b.BuildingKey) return prefix + "BuildingKey " + a.BuildingKey + " != " + b.BuildingKey;
                if (a.Purpose != b.Purpose) return prefix + "Purpose " + a.Purpose + " != " + b.Purpose;
                if (a.OpenMinute != b.OpenMinute) return prefix + "OpenMinute " + a.OpenMinute + " != " + b.OpenMinute;
                if (a.CloseMinute != b.CloseMinute) return prefix + "CloseMinute " + a.CloseMinute + " != " + b.CloseMinute;
                difference = NumericDifference(prefix + "WalkSeconds", a.WalkSeconds, b.WalkSeconds, 0.000001);
                if (difference != null) return difference;
                difference = NumericDifference(prefix + "DwellSeconds", a.DwellSeconds, b.DwellSeconds, 0.000001);
                if (difference != null) return difference;
                difference = NumericDifference(prefix + "KeepRoomUntil", a.KeepRoomUntil, b.KeepRoomUntil, 0.000001);
                if (difference != null) return difference;
                difference = PointDifference(prefix + "Route", a.Route, b.Route);
                if (difference != null) return difference;
            }
            return null;
        }

        private static bool SamePoints(List<SavedRoutePoint> left, List<SavedRoutePoint> right)
        { return PointDifference("Route", left, right) == null; }

        private static string PointDifference(string name, List<SavedRoutePoint> left, List<SavedRoutePoint> right)
        {
            if (left == null || right == null) return left == right ? null : name + " null mismatch";
            if (left.Count != right.Count) return name + ".Count " + left.Count + " != " + right.Count;
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] == null || right[i] == null)
                    return left[i] == right[i] ? null : name + "[" + i + "] null mismatch";
                // Native JSON decimal formatting changes doubles by ~2e-13 metres.
                // Permit sub-nanometre formatting drift, not meaningful route changes.
                if (!Finite(right[i].X) || Math.Abs(left[i].X - right[i].X) > 0.000000001)
                    return name + "[" + i + "].X " + Number(left[i].X) + " != " + Number(right[i].X) +
                        " (delta=" + Number(left[i].X - right[i].X) + ")";
                if (!Finite(right[i].Z) || Math.Abs(left[i].Z - right[i].Z) > 0.000000001)
                    return name + "[" + i + "].Z " + Number(left[i].Z) + " != " + Number(right[i].Z) +
                        " (delta=" + Number(left[i].Z - right[i].Z) + ")";
            }
            return null;
        }

        private static string DescribeIdentityDifference(Resident before, Resident after)
        {
            if (after == null) return "resident missing after round-trip";
            var fields = new List<string>();
            if (before.Id != after.Id) fields.Add("Id=" + before.Id + "/" + after.Id);
            if (before.FullName != after.FullName) fields.Add("FullName=" + before.FullName + "/" + after.FullName);
            if (before.HomeKey != after.HomeKey) fields.Add("HomeKey=" + before.HomeKey + "/" + after.HomeKey);
            if (before.AgeGroup != after.AgeGroup) fields.Add("AgeGroup=" + before.AgeGroup + "/" + after.AgeGroup);
            if (before.Sex != after.Sex) fields.Add("Sex=" + before.Sex + "/" + after.Sex);
            if (before.Race != after.Race) fields.Add("Race=" + before.Race + "/" + after.Race);
            if (before.AppearanceSeed != after.AppearanceSeed) fields.Add("AppearanceSeed=" + before.AppearanceSeed + "/" + after.AppearanceSeed);
            return fields.Count == 0 ? null : string.Join(",", fields.ToArray());
        }

        private static string NumericDifference(string field, double before, double after, double tolerance)
        {
            if (Math.Abs(before - after) <= tolerance) return null;
            return field + " " + Number(before) + " != " + Number(after) + " (delta=" + Number(before - after) +
                "; tolerance=" + Number(tolerance) + ")";
        }

        private static string Number(double value)
        { return value.ToString("R", CultureInfo.InvariantCulture); }

        private static void LogRoutinePayloadSizes(string worldJson, TownRoutineState town)
        {
            int routineCount = 0, itineraryCount = 0, legacyRoutePoints = 0, itineraryRoutePoints = 0, maxPathPoints = 0;
            foreach (var routine in town.Residents)
            {
                if (routine == null) continue;
                routineCount++;
                CountPath(routine.Route, ref legacyRoutePoints, ref maxPathPoints);
                if (routine.Itinerary == null) continue;
                itineraryCount++;
                foreach (var stop in routine.Itinerary.Stops)
                    if (stop != null) CountPath(stop.Route, ref itineraryRoutePoints, ref maxPathPoints);
                CountPath(routine.Itinerary.ExitRoute, ref itineraryRoutePoints, ref maxPathPoints);
            }

            string townJson = SaveLoadManager.Serialize(typeof(TownRoutineState), town);
            var legacyTown = SaveLoadManager.Deserialize(typeof(TownRoutineState), townJson) as TownRoutineState;
            if (legacyTown == null) throw new InvalidOperationException("Could not clone town routines for legacy payload size diagnostic.");
            StripItineraries(legacyTown);
            string rawLegacyOnlyJson = SaveLoadManager.Serialize(typeof(TownRoutineState), legacyTown);

            var diagnosticTown = SaveLoadManager.Deserialize(typeof(TownRoutineState), townJson) as TownRoutineState;
            if (diagnosticTown == null) throw new InvalidOperationException("Could not clone town routines for payload size diagnostic.");
            foreach (var routine in diagnosticTown.Residents)
            {
                if (routine == null) continue;
                if (routine.Route != null) routine.Route = SavedRouteSimplifier.Simplify(routine.Route);
                if (routine.Itinerary != null)
                {
                    foreach (var stop in routine.Itinerary.Stops)
                        if (stop != null && stop.Route != null) stop.Route = SavedRouteSimplifier.Simplify(stop.Route);
                    if (routine.Itinerary.ExitRoute != null)
                        routine.Itinerary.ExitRoute = SavedRouteSimplifier.Simplify(routine.Itinerary.ExitRoute);
                }
            }
            int compactedStateSamples = VerifyCompactionEquivalence(town, diagnosticTown);
            string compactedTownJson = SaveLoadManager.Serialize(typeof(TownRoutineState), diagnosticTown);
            StripItineraries(diagnosticTown);
            string compactedLegacyOnlyJson = SaveLoadManager.Serialize(typeof(TownRoutineState), diagnosticTown);
            Debug.Log("LIVING_CITIES_DAILY_ITINERARY_SIZE: totalJsonChars=" + worldJson.Length +
                "; townRoutineJsonChars=" + townJson.Length + "; compactedTownJsonChars=" + compactedTownJson.Length +
                "; legacyOnlyRoutineJsonChars=" + rawLegacyOnlyJson.Length +
                "; compactedLegacyOnlyRoutineJsonChars=" + compactedLegacyOnlyJson.Length +
                "; rawItineraryJsonChars=" + (townJson.Length - rawLegacyOnlyJson.Length) +
                "; compactedItineraryJsonChars=" + (compactedTownJson.Length - compactedLegacyOnlyJson.Length) +
                "; compactionStateSamples=" + compactedStateSamples + "; compactionToleranceMetersSeconds=1e-6" +
                "; routineCount=" + routineCount + "; itineraryCount=" + itineraryCount +
                "; legacyRoutePoints=" + legacyRoutePoints + "; itineraryRoutePoints=" + itineraryRoutePoints +
                "; maxPathPoints=" + maxPathPoints);
        }

        private static void StripItineraries(TownRoutineState town)
        {
            town.ItineraryVersion = 0;
            foreach (var routine in town.Residents)
            {
                if (routine == null) continue;
                routine.Itinerary = null;
                routine.ItineraryDepartureSeconds = 0;
            }
        }

        private static void CountPath(List<SavedRoutePoint> points, ref int total, ref int maximum)
        {
            if (points == null) return;
            total += points.Count;
            if (points.Count > maximum) maximum = points.Count;
        }

        private static int VerifyCompactionEquivalence(TownRoutineState original, TownRoutineState compacted)
        {
            int samples = 0;
            foreach (var before in original.Residents)
            {
                if (before == null || before.Itinerary == null) continue;
                var after = compacted.Residents.Find(item => item != null && item.ResidentId == before.ResidentId);
                if (after == null || after.Itinerary == null || before.ResidentId != after.ResidentId ||
                    Math.Abs(before.ItineraryDepartureSeconds - after.ItineraryDepartureSeconds) > 0.000001)
                    throw new InvalidOperationException("Route compaction changed resident identity or departure: " + before.ResidentId + ".");
                var beforeJourney = ResidentVisitJourney.Create(before.ItineraryDepartureSeconds, before.Itinerary);
                var afterJourney = ResidentVisitJourney.Create(after.ItineraryDepartureSeconds, after.Itinerary);
                if (Math.Abs(beforeJourney.EndsAt - afterJourney.EndsAt) > 0.000001)
                    throw new InvalidOperationException("Route compaction changed return time for " + before.ResidentId + ": " +
                        Number(beforeJourney.EndsAt) + " != " + Number(afterJourney.EndsAt) + ".");

                var beforeVisits = new List<ResidentVisitSnapshot>(beforeJourney.ScheduledVisits());
                var afterVisits = new List<ResidentVisitSnapshot>(afterJourney.ScheduledVisits());
                if (beforeVisits.Count != afterVisits.Count)
                    throw new InvalidOperationException("Route compaction changed visit count for " + before.ResidentId + ".");
                for (int i = 0; i < beforeVisits.Count; i++)
                {
                    var a = beforeVisits[i]; var b = afterVisits[i];
                    RequireTimeEquivalent(before.ResidentId, "visit " + i + " arrival", a.ArriveAt, b.ArriveAt);
                    RequireTimeEquivalent(before.ResidentId, "visit " + i + " opening", a.EnterAt, b.EnterAt);
                    RequireTimeEquivalent(before.ResidentId, "visit " + i + " leave", a.LeaveAt, b.LeaveAt);
                    if (a.BuildingKey != b.BuildingKey || a.Purpose != b.Purpose)
                        throw new InvalidOperationException("Route compaction changed visit identity for " + before.ResidentId + ".");
                }

                double routeStart = before.ItineraryDepartureSeconds;
                for (int i = 0; i < before.Itinerary.Stops.Count; i++)
                {
                    double walk = before.Itinerary.Stops[i].WalkSeconds;
                    for (int fraction = 1; fraction <= 3; fraction++)
                    {
                        RequireStateEquivalent(before.ResidentId, beforeJourney, afterJourney,
                            routeStart + walk * fraction / 4d, "stop route " + i);
                        samples++;
                    }
                    var visit = beforeVisits[i];
                    double dwell = visit.LeaveAt - visit.EnterAt;
                    for (int fraction = 1; fraction <= 3; fraction++)
                    {
                        RequireStateEquivalent(before.ResidentId, beforeJourney, afterJourney,
                            visit.EnterAt + dwell * fraction / 4d, "service visit " + i);
                        samples++;
                    }
                    routeStart = visit.LeaveAt;
                }
                double exitStart = beforeJourney.EndsAt - before.Itinerary.ExitWalkSeconds;
                for (int fraction = 1; fraction <= 3; fraction++)
                {
                    RequireStateEquivalent(before.ResidentId, beforeJourney, afterJourney,
                        exitStart + before.Itinerary.ExitWalkSeconds * fraction / 4d, "home return route");
                    samples++;
                }
                RequireStateEquivalent(before.ResidentId, beforeJourney, afterJourney, beforeJourney.EndsAt + 0.0001, "home arrival");
                samples++;
            }
            return samples;
        }

        private static void RequireTimeEquivalent(string residentId, string phase, double before, double after)
        {
            if (Math.Abs(before - after) > 0.000001)
                throw new InvalidOperationException("Route compaction changed " + phase + " time for " + residentId + ": " +
                    Number(before) + " != " + Number(after) + ".");
        }

        private static void RequireStateEquivalent(string residentId, ResidentVisitJourney before,
            ResidentVisitJourney after, double at, string phase)
        {
            var a = before.Evaluate(at); var b = after.Evaluate(at);
            double dx = a.Position.X - b.Position.X, dz = a.Position.Z - b.Position.Z;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            if (a.Phase != b.Phase || a.BuildingKey != b.BuildingKey || a.TargetBuildingKey != b.TargetBuildingKey ||
                distance > 0.000001)
                throw new InvalidOperationException("Route compaction changed " + phase + " state for " + residentId +
                    " at " + Number(at) + ": phase=" + a.Phase + "/" + b.Phase + "; building=" +
                    a.BuildingKey + "/" + b.BuildingKey + "; position delta=" + Number(distance) + " m.");
        }

        private static bool SameState(ActivitySnapshot left, ActivitySnapshot right)
        {
            return left.Activity == right.Activity && left.BuildingKey == right.BuildingKey &&
                left.Position.X == right.Position.X && left.Position.Z == right.Position.Z &&
                left.RouteProgress == right.RouteProgress;
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
