using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Opt-in native regression. A temporary local schedule arrives before its
    // existing actor can walk a saved exterior route. No save data changes.
    public static class OutdoorArrivalContinuityProbe
    {
        private const float DoorRadius = 0.55f;

        public static IEnumerator Run(LivingCitiesMod adapter, StreetPopulation streets)
        {
            var cornerRoute = new List<SavedRoutePoint>
            {
                new SavedRoutePoint { X = 0, Z = 0 },
                new SavedRoutePoint { X = 1, Z = 0 },
                new SavedRoutePoint { X = 1, Z = 1 }
            };
            RoutePoint cornerTarget;
            if (!StreetPopulation.TryNextArrivalRoutePoint(cornerRoute, new Vector3(1, 0, 0), out cornerTarget) ||
                Math.Abs(cornerTarget.X - 1) > 0.0001 || Math.Abs(cornerTarget.Z - 1) > 0.0001)
                throw new InvalidOperationException("Outdoor arrival route follower failed to advance through a shared corner.");

            if (!adapter || !streets) throw new InvalidOperationException("Outdoor arrival probe requires live town components.");
            var game = GameManager.Instance;
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                game.PlayerEnterExit.TransitionExterior();
                float exteriorDeadline = Time.realtimeSinceStartup + 60f;
                while ((game.PlayerEnterExit.IsPlayerInsideBuilding || !game.StreamingWorld.IsReady ||
                    game.StreamingWorld.IsRepositioningPlayer) && Time.realtimeSinceStartup < exteriorDeadline)
                    yield return null;
                if (game.PlayerEnterExit.IsPlayerInsideBuilding || !game.StreamingWorld.IsReady ||
                    game.StreamingWorld.IsRepositioningPlayer)
                    throw new InvalidOperationException("Outdoor arrival probe could not load its exterior scene.");
            }
            if (GameManager.IsGamePaused)
                throw new InvalidOperationException("Outdoor arrival probe requires an unpaused exterior scene.");
            float populationDeadline = Time.realtimeSinceStartup + 60f;
            while ((!streets.Ready || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer) &&
                Time.realtimeSinceStartup < populationDeadline) yield return null;
            if (!streets.Ready || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer)
                throw new InvalidOperationException("Outdoor arrival probe requires a ready exterior population.");

            var directory = (BuildingDirectory)GetField(typeof(StreetPopulation), "directory").GetValue(streets);
            var geometry = (TownGeometry)GetField(typeof(StreetPopulation), "geometry").GetValue(streets);
            var plans = (IList)GetField(typeof(StreetPopulation), "plans").GetValue(streets);
            var snapshot = adapter.GetSaveData() as WorldState;
            var town = snapshot == null ? null : snapshot.Routines.Find(item => item.SettlementId == directory.MapID);
            if (town == null) throw new InvalidOperationException("Outdoor arrival probe found no saved town routines.");
            var tripIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var trip in snapshot.ResidentTrips ?? new List<ResidentTrip>())
                if (trip != null && !string.IsNullOrEmpty(trip.ResidentId)) tripIds.Add(trip.ResidentId);

            object chosenPlan = null;
            ResidentActor chosenActor = null;
            ResidentVisitStop sourceStop = null;
            foreach (var routine in town.Residents)
            {
                if (routine == null || tripIds.Contains(routine.ResidentId)) continue;
                var candidateActor = streets.FindActor(routine.ResidentId);
                if (!candidateActor || !candidateActor.gameObject.activeInHierarchy || candidateActor.transform.parent != directory.transform) continue;
                var plan = FindPlan(plans, routine.ResidentId);
                if (plan == null) continue;
                if (routine.Itinerary != null && routine.Itinerary.Stops != null)
                {
                    foreach (var stop in routine.Itinerary.Stops)
                    {
                        if (stop == null || stop.Route == null || stop.Route.Count < 2 || RouteLength(stop.Route) < 3.5) continue;
                        var endpoint = stop.Route[stop.Route.Count - 1];
                        Vector3 door = geometry.ScenePoint(new RoutePoint(endpoint.X, endpoint.Z));
                        if ((door - game.PlayerMotor.transform.position).sqrMagnitude > 165f * 165f) continue;
                        chosenPlan = plan; chosenActor = candidateActor; sourceStop = stop;
                        break;
                    }
                }
                if (!chosenActor && routine.DestinationKey >= 0 && routine.Route != null &&
                    routine.Route.Count >= 2 && RouteLength(routine.Route) >= 3.5)
                {
                    var endpoint = routine.Route[routine.Route.Count - 1];
                    Vector3 door = geometry.ScenePoint(new RoutePoint(endpoint.X, endpoint.Z));
                    if ((door - game.PlayerMotor.transform.position).sqrMagnitude <= 165f * 165f)
                    {
                        chosenPlan = plan; chosenActor = candidateActor;
                        sourceStop = LegacyRoutineStop(routine);
                    }
                }
                if (chosenActor) break;
            }
            if (!chosenActor)
                throw new InvalidOperationException("No nearby active local resident has a long saved itinerary or legacy route.");

            var planType = chosenPlan.GetType();
            var routineField = GetField(planType, "Routine");
            var scheduleField = GetField(planType, "Schedule");
            var hauntedField = GetField(planType, "HauntedSchedule");
            object oldRoutine = routineField.GetValue(chosenPlan);
            object oldSchedule = scheduleField.GetValue(chosenPlan);
            object oldHauntedSchedule = hauntedField.GetValue(chosenPlan);
            var actor = chosenActor;
            string residentId = actor.Resident.Id;
            Vector3 savedPosition = actor.transform.position;
            Quaternion savedRotation = actor.transform.rotation;
            var motion = actor.GetComponent<CrowdMotion>();

            try
            {
                double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                double nowOfDay = now % 86400d;
                int wake = ReadHour(streets, "wakeHour", 6), sleep = ReadHour(streets, "sleepHour", 22);
                double departure = nowOfDay - 0.3;
                if (departure < wake * 3600d || nowOfDay + 300.3 >= sleep * 3600d)
                    throw new InvalidOperationException("Outdoor arrival probe requires at least 300.3 waking seconds on this day.");

                var stopCopy = CopyStopRoute(sourceStop);
                stopCopy.OpenMinute = stopCopy.CloseMinute = 0;
                stopCopy.WalkSeconds = 0.001;
                stopCopy.DwellSeconds = 300;
                var itinerary = new ResidentDestination
                {
                    Stops = new List<ResidentVisitStop> { stopCopy },
                    ExitWalkSeconds = 0.001,
                    ExitRoute = Reverse(stopCopy.Route)
                };
                var temporaryRoutine = new ResidentRoutine
                {
                    ResidentId = actor.Resident.Id,
                    Itinerary = itinerary,
                    ItineraryDepartureSeconds = departure
                };
                var temporarySchedule = DailySchedule.ItineraryDay(actor.Resident.HomeKey, itinerary,
                    departure, wake * 60, sleep * 60);
                var journey = ResidentVisitJourney.Create(departure, itinerary);
                var visitEnumerator = journey.ScheduledVisits().GetEnumerator();
                if (!visitEnumerator.MoveNext()) throw new InvalidOperationException("Probe itinerary has no visit.");
                double enterAt = visitEnumerator.Current.EnterAt;
                if (temporarySchedule.EvaluateSeconds(now).Activity != Activity.Visit)
                    throw new InvalidOperationException("Probe itinerary did not enter before current game time.");

                routineField.SetValue(chosenPlan, temporaryRoutine);
                scheduleField.SetValue(chosenPlan, temporarySchedule);
                hauntedField.SetValue(chosenPlan, temporarySchedule);

                var doorPoint = stopCopy.Route[stopCopy.Route.Count - 1];
                Vector3 doorGround = geometry.ScenePoint(new RoutePoint(doorPoint.X, doorPoint.Z));
                double back = Math.Min(3.0, RouteLength(stopCopy.Route) * 0.75);
                var delayedPoint = PointFromEnd(stopCopy.Route, back);
                Vector3 delayedGround = geometry.ScenePoint(new RoutePoint(delayedPoint.X, delayedPoint.Z));
                float initialGap = Vector3.Distance(delayedGround, doorGround);
                if (initialGap <= DoorRadius + 1.5f)
                    throw new InvalidOperationException("Probe route cannot hold delayed walker beyond door threshold.");

                if (motion) motion.ResetPlacement();
                actor.transform.position = delayedGround + Vector3.up;
                actor.Place(delayedGround, doorGround - delayedGround, true);
                Vector3 startPosition = actor.transform.position - Vector3.up;
                int initialActors = CountResidentActors(actor.Resident.Id);
                if (initialActors != 1) throw new InvalidOperationException("Probe resident already has duplicate actor instances.");

                // Real StreetPopulation.Update runs each frame. Wait for the same
                // body to reach the surveyed door, spanning real and game time.
                float startedReal = Time.realtimeSinceStartup;
                float deadline = startedReal + 8f;
                int frames = 0;
                float gap = Vector3.Distance(actor.transform.position - Vector3.up, doorGround);
                Vector3 previousGround = actor.transform.position - Vector3.up;
                Vector3 lastBeforeRemoval = previousGround;
                double startedGame = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                bool reachedDoor = gap <= DoorRadius;
                bool sameActorUntilDoor = true;
                float largestFrameStep = 0;
                while (Time.realtimeSinceStartup < deadline && frames < 1200 &&
                    !(reachedDoor && Time.realtimeSinceStartup - startedReal >= 0.2f &&
                      DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() - startedGame >= 2.0))
                {
                    yield return null;
                    frames++;
                    if (!actor)
                    {
                        if (gap > DoorRadius) throw new InvalidOperationException("Outdoor actor disappeared before reaching doorway.");
                        reachedDoor = true;
                    }
                    var state = streets.GetState(residentId, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                    if (state.Activity != Activity.Visit || state.BuildingKey != stopCopy.BuildingKey)
                        throw new InvalidOperationException("Probe schedule left visit before multi-frame arrival check completed.");
                    if (actor)
                    {
                        if (streets.FindActor(residentId) != actor) sameActorUntilDoor = false;
                        int currentCount = CountResidentActors(residentId);
                        if (currentCount > 1 || !reachedDoor && currentCount != 1)
                            throw new InvalidOperationException("Outdoor arrival changed actor identity or created a duplicate before the doorway.");
                        Vector3 currentGround = actor.transform.position - Vector3.up;
                        float frameStep = Vector3.Distance(previousGround, currentGround);
                        largestFrameStep = Mathf.Max(largestFrameStep, frameStep);
                        if (frameStep > 0.55f)
                            throw new InvalidOperationException("Outdoor arrival moved farther than one collision-checked CrowdMotion step.");
                        previousGround = currentGround;
                        lastBeforeRemoval = currentGround;
                        gap = Vector3.Distance(currentGround, doorGround);
                        if (gap <= DoorRadius) reachedDoor = true;
                    }
                }

                double elapsed = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() - startedGame;
                float elapsedReal = Time.realtimeSinceStartup - startedReal;
                double scheduleElapsed = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() % 86400d - enterAt;
                float moved = Vector3.Distance(startPosition, lastBeforeRemoval);
                int finalCount = CountResidentActors(residentId);
                if (frames < 4 || elapsedReal < 0.2f || elapsed < 2 || scheduleElapsed <= 0.05 || !reachedDoor ||
                    !sameActorUntilDoor || finalCount > 1 || moved <= 0.5f || largestFrameStep > 0.55f)
                    throw new InvalidOperationException("Outdoor actor did not follow the same body along the route to its actual doorway across repeated updates.");

                Debug.Log("LIVING_CITIES_OUTDOOR_ARRIVAL_CONTINUITY_PASS: resident=" + residentId +
                    "; scheduled=Visit; frames=" + frames + "; elapsedReal=" + elapsedReal.ToString("F3") +
                    "; elapsedGame=" + elapsed.ToString("F3") +
                    "; scheduleElapsed=" + scheduleElapsed.ToString("F3") +
                    "; initialGap=" + initialGap.ToString("F2") + "; doorwayGap=" + gap.ToString("F2") +
                    "; moved=" + moved.ToString("F2") + "; largestFrameStep=" + largestFrameStep.ToString("F2") +
                    "; sameActorUntilDoor=true; duplicateCount=" + finalCount);
            }
            finally
            {
                routineField.SetValue(chosenPlan, oldRoutine);
                scheduleField.SetValue(chosenPlan, oldSchedule);
                hauntedField.SetValue(chosenPlan, oldHauntedSchedule);
                if (actor)
                {
                    actor.transform.position = savedPosition;
                    actor.transform.rotation = savedRotation;
                    if (motion) motion.ResetPlacement();
                }
            }
            yield break;
        }

        private static object FindPlan(IList plans, string residentId)
        {
            foreach (var plan in plans)
            {
                if (plan == null) continue;
                var resident = GetField(plan.GetType(), "Resident").GetValue(plan) as Resident;
                if (resident != null && resident.Id == residentId) return plan;
            }
            return null;
        }

        private static int CountResidentActors(string id)
        {
            int count = 0;
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if (actor && actor.Resident != null && actor.Resident.Id == id) count++;
            return count;
        }

        private static FieldInfo GetField(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static int ReadHour(StreetPopulation streets, string name, int fallback)
        {
            var field = GetField(typeof(StreetPopulation), name);
            return field.GetValue(streets) is int ? (int)field.GetValue(streets) : fallback;
        }

        private static ResidentVisitStop CopyStopRoute(ResidentVisitStop source)
        {
            var result = new ResidentVisitStop
            {
                BuildingKey = source.BuildingKey,
                Purpose = source.Purpose,
                OpenMinute = source.OpenMinute,
                CloseMinute = source.CloseMinute,
                WalkSeconds = source.WalkSeconds,
                DwellSeconds = source.DwellSeconds,
                Route = new List<SavedRoutePoint>()
            };
            foreach (var point in source.Route)
                result.Route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            return result;
        }

        private static ResidentVisitStop LegacyRoutineStop(ResidentRoutine routine)
        {
            var route = new List<SavedRoutePoint>();
            foreach (var point in routine.Route)
                route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            return new ResidentVisitStop
            {
                BuildingKey = routine.DestinationKey,
                Purpose = ResidentVisitPurpose.Social,
                Route = route
            };
        }

        private static List<SavedRoutePoint> Reverse(IList<SavedRoutePoint> route)
        {
            var result = new List<SavedRoutePoint>();
            for (int i = route.Count - 1; i >= 0; i--)
                result.Add(new SavedRoutePoint { X = route[i].X, Z = route[i].Z });
            return result;
        }

        private static double RouteLength(IList<SavedRoutePoint> route)
        {
            double length = 0;
            for (int i = 1; i < route.Count; i++)
            {
                double dx = route[i].X - route[i - 1].X, dz = route[i].Z - route[i - 1].Z;
                length += Math.Sqrt(dx * dx + dz * dz);
            }
            return length;
        }

        private static SavedRoutePoint PointFromEnd(IList<SavedRoutePoint> route, double fromEnd)
        {
            double remaining = fromEnd;
            for (int i = route.Count - 1; i > 0; i--)
            {
                var end = route[i]; var start = route[i - 1];
                double dx = end.X - start.X, dz = end.Z - start.Z;
                double segment = Math.Sqrt(dx * dx + dz * dz);
                if (segment <= 0) continue;
                if (remaining <= segment)
                {
                    double fraction = remaining / segment;
                    return new SavedRoutePoint { X = end.X - dx * fraction, Z = end.Z - dz * fraction };
                }
                remaining -= segment;
            }
            return route[0];
        }
    }
}
