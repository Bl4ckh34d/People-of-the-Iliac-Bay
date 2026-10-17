using System;
using System.Collections.Generic;
using LivingCities.Core;
using UnityEngine;
using DaggerfallWorkshop.Game;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private const float OutdoorDoorArrivalRadius = 0.55f;
        private sealed class CachedOutdoorArrivalJourney
        {
            public ResidentRoutine Routine;
            public ResidentDestination Itinerary;
            public double Departure;
            public ResidentVisitJourney Journey;
        }
        private sealed class CachedDestinationArrivalRoute
        {
            public RoutePoint Door;
            public List<SavedRoutePoint> Route;
            public float RetryAt;
        }
        private readonly Dictionary<string, CachedOutdoorArrivalJourney> outdoorArrivalJourneys =
            new Dictionary<string, CachedOutdoorArrivalJourney>();
        private readonly Dictionary<string, CachedDestinationArrivalRoute> destinationArrivalRoutes =
            new Dictionary<string, CachedDestinationArrivalRoute>();

        private bool ContinueLocalOutdoorArrival(Plan plan, ResidentActor actor, ActivitySnapshot current, double seconds)
        {
            if (plan == null || plan.Resident == null || !plan.Resident.IsAlive ||
                current.Activity != Activity.Visit && current.Activity != Activity.Home ||
                plan.Routine == null || plan.Routine.Itinerary == null) return false;

            ResidentVisitJourney journey;
            if (!TryGetOutdoorArrivalJourney(plan, out journey)) return false;
            IList<SavedRoutePoint> route;
            if (current.Activity == Activity.Visit)
            {
                ResidentVisitSnapshot visit;
                visit = journey.Evaluate(seconds % 86400d);
                if (visit.Phase != ResidentVisitPhase.Inside || visit.VisitIndex < 0 ||
                    visit.VisitIndex >= plan.Routine.Itinerary.Stops.Count ||
                    visit.BuildingKey != current.BuildingKey) return false;
                route = plan.Routine.Itinerary.Stops[visit.VisitIndex].Route;
            }
            else
            {
                double timeOfDay = seconds % 86400d;
                if (timeOfDay < journey.EndsAt) return false;
                route = plan.Routine.Itinerary.ExitRoute;
            }
            if (route == null || route.Count < 2) return false;
            var endpoint = route[route.Count - 1];
            return ContinueOutdoorArrival(actor, new RoutePoint(endpoint.X, endpoint.Z), route);
        }

        private bool ContinueDestinationOutdoorArrival(ResidentActor actor, ActivitySnapshot state)
        {
            if (!actor || !actor.Resident.IsAlive || !directory || geometry == null || interior ||
                GameManager.Instance.IsPlayerInside || state.Activity != Activity.Visit || state.BuildingKey < 0) return false;
            var id = actor.Resident.Id;
            Vector3 doorGround = geometry.ScenePoint(state.Position);
            Vector3 player = GameManager.Instance.PlayerMotor.transform.position;
            if ((doorGround - player).sqrMagnitude > 165f * 165f) return false;
            if (Vector3.Distance(actor.transform.position - Vector3.up, doorGround) <= OutdoorDoorArrivalRadius)
            { destinationArrivalRoutes.Remove(id); return false; }
            CachedDestinationArrivalRoute cached;
            if (!destinationArrivalRoutes.TryGetValue(id, out cached) || !SamePoint(cached.Door, state.Position))
            {
                cached = new CachedDestinationArrivalRoute { Door = state.Position };
                destinationArrivalRoutes[id] = cached;
            }
            if ((cached.Route == null || cached.Route.Count < 2) && Time.unscaledTime >= cached.RetryAt)
            {
                var local = directory.transform.InverseTransformPoint(actor.transform.position - Vector3.up);
                var route = geometry.RouteBetweenPoints(new SavedRoutePoint { X = local.x, Z = local.z },
                    new SavedRoutePoint { X = state.Position.X, Z = state.Position.Z });
                cached.Route = route.Count >= 2 ? route : null;
                cached.RetryAt = Time.unscaledTime + 0.75f;
            }
            if (cached.Route == null || cached.Route.Count < 2) return true;
            bool retained = ContinueOutdoorArrival(actor, state.Position, cached.Route);
            if (!retained) destinationArrivalRoutes.Remove(id);
            return retained;
        }

        private bool TryGetOutdoorArrivalJourney(Plan plan, out ResidentVisitJourney journey)
        {
            journey = null;
            var routine = plan.Routine;
            var itinerary = routine.Itinerary;
            CachedOutdoorArrivalJourney cached;
            if (outdoorArrivalJourneys.TryGetValue(plan.Resident.Id, out cached) &&
                ReferenceEquals(cached.Routine, routine) && ReferenceEquals(cached.Itinerary, itinerary))
            {
                if (Math.Abs(cached.Departure - routine.ItineraryDepartureSeconds) < 0.000001)
                {
                    journey = cached.Journey;
                    return journey != null;
                }
            }
            try { journey = ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds, itinerary); }
            catch (ArgumentException) { return false; }
            if (outdoorArrivalJourneys.Count >= 128) outdoorArrivalJourneys.Clear();
            outdoorArrivalJourneys[plan.Resident.Id] = new CachedOutdoorArrivalJourney
            { Routine = routine, Itinerary = itinerary, Departure = routine.ItineraryDepartureSeconds, Journey = journey };
            return true;
        }

        private bool ContinueOutdoorArrival(ResidentActor actor, RoutePoint door, IList<SavedRoutePoint> route)
        {
            if (!actor || !actor.Resident.IsAlive || !directory || interior || GameManager.Instance.IsPlayerInside ||
                actor.transform.parent != directory.transform || geometry == null)
                return false;

            Vector3 doorTarget = geometry.ScenePoint(door);
            Vector3 player = GameManager.Instance.PlayerMotor.transform.position;
            if ((doorTarget - player).sqrMagnitude > 165f * 165f) return false;

            Vector3 current = actor.transform.position - Vector3.up;
            Vector3 remaining = doorTarget - current;
            if (remaining.sqrMagnitude <= OutdoorDoorArrivalRadius * OutdoorDoorArrivalRadius)
                return false;

            // Keep ownership while paused; resume as a physical walk after unpause.
            if (GameManager.IsGamePaused) return true;

            Vector3 localCurrent = directory.transform.InverseTransformPoint(current);
            RoutePoint nextPoint;
            if (!TryNextArrivalRoutePoint(route, localCurrent, out nextPoint)) return true;
            Vector3 stepTarget = geometry.ScenePoint(nextPoint);
            // CrowdMotion advances along one collision-checked step of the surveyed route.
            var motion = actor.GetComponent<CrowdMotion>();
            if (motion) motion.ResumeFromPause();
            actor.Place(stepTarget, stepTarget - current);
            return true;
        }

        internal static bool TryNextArrivalRoutePoint(IList<SavedRoutePoint> route, Vector3 current, out RoutePoint target)
        {
            target = default(RoutePoint);
            if (route == null || route.Count < 2) return false;
            int nearestSegment = -1;
            double nearestDistance = double.PositiveInfinity, nearestX = 0, nearestZ = 0;
            for (int i = 0; i < route.Count - 1; i++)
            {
                var a = route[i]; var b = route[i + 1];
                double dx = b.X - a.X, dz = b.Z - a.Z;
                double lengthSquared = dx * dx + dz * dz;
                if (lengthSquared < 0.000001) continue;
                double t = Math.Max(0, Math.Min(1, ((current.x - a.X) * dx + (current.z - a.Z) * dz) / lengthSquared));
                double x = a.X + dx * t, z = a.Z + dz * t;
                double ox = current.x - x, oz = current.z - z;
                double distance = ox * ox + oz * oz;
                if (distance < nearestDistance - 0.000001 ||
                    Math.Abs(distance - nearestDistance) <= 0.0001 && i > nearestSegment)
                { nearestDistance = distance; nearestSegment = i; nearestX = x; nearestZ = z; }
            }
            if (nearestSegment < 0) return false;
            if (nearestDistance > 0.04)
                target = new RoutePoint(nearestX, nearestZ);
            else
                target = new RoutePoint(route[nearestSegment + 1].X, route[nearestSegment + 1].Z);
            return true;
        }

        private static bool SamePoint(RoutePoint left, RoutePoint right)
        { return Math.Abs(left.X - right.X) < 0.001 && Math.Abs(left.Z - right.Z) < 0.001; }
    }
}
