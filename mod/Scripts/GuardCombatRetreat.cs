using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private const double CityWatchMinimumShelterSeconds = 3600;
        private const double CityWatchRetreatRetrySeconds = 60;
        private const double CityWatchRetreatSpeed = 3.8;
        private readonly Dictionary<string, double> cityWatchRetreatRetryAt = new Dictionary<string, double>();

        internal static Vector3? ResolveCityWallSafeWaypoint(DaggerfallEntityBehaviour body)
        {
            if (!body) return null;
            var actor = body.GetComponent<GuardActor>();
            return actor ? actor.ResolveCityWallSafeWaypoint(body) : (Vector3?)null;
        }

        private void BindCityWatchCombatRole(GuardActor actor)
        {
            DaggerfallEntityBehaviour behaviour;
            if (!actor || actor.IsInterior || worldStore == null || !TryGetCityWatchBody(actor, out behaviour)) return;

            string source = "native:guard-duty:" + actor.GuardId;
            var role = new NpcRoleState
            {
                PersonId = actor.GuardId,
                CombatRole = NpcCombatRole.Guard,
                CombatSourceId = source,
                ServiceBinding = NpcServiceBinding.NobleKingdom,
                ServiceSourceId = source,
                Revision = 1
            };
            NativeNpcCombatRoleBinding.Bind(behaviour, role, worldStore.WorldSeed, actor.GuardId,
                StableCombatTargetIdentity, ResolveCityWallSafeWaypoint);
        }

        internal bool HasActiveCombatRefuge(GuardActor actor)
        {
            DaggerfallEntityBehaviour behaviour;
            if (!actor || Roster == null || !TryGetCityWatchBody(actor, out behaviour)) return false;
            GuardActor owned;
            if (!actors.TryGetValue(actor.GuardId, out owned) || owned != actor) return false;
            var guard = Roster.Guards.Find(candidate => candidate.Id == actor.GuardId);
            return guard != null && guard.IsAlive && guard.Duty == GuardDuty.Refuge && guard.Refuge != null &&
                guard.Refuge.Evaluate(Now()).Phase != RefugePhase.Complete &&
                IsTown23(guard.Refuge.BuildingKey);
        }

        private static bool TryGetCityWatchBody(GuardActor actor, out DaggerfallEntityBehaviour behaviour)
        {
            behaviour = actor ? actor.GetComponent<DaggerfallEntityBehaviour>() : null;
            var entity = behaviour ? behaviour.Entity as EnemyEntity : null;
            return behaviour && entity != null && entity.MobileEnemy.ID == (int)MobileTypes.Knight_CityWatch;
        }

        private static string StableCombatTargetIdentity(DaggerfallEntityBehaviour target)
        {
            if (!target || target.EntityType == EntityTypes.Player) return null;
            var enemy = target.GetComponent<DaggerfallEnemy>();
            if (!enemy || enemy.LoadID == 0) return null;
            return "native-enemy:" + enemy.LoadID.ToString(CultureInfo.InvariantCulture);
        }

        internal Vector3? TryStartCityWallRefuge(GuardActor actor)
        {
            if (!actor || !OwnsExterior || Roster == null || geometry == null || directory == null ||
                actor.IsInterior || !actor.gameObject.activeInHierarchy) return null;

            GuardActor owned;
            if (!actors.TryGetValue(actor.GuardId, out owned) || owned != actor) return null;
            var guard = Roster.Guards.Find(candidate => candidate.Id == actor.GuardId);
            if (guard == null || !guard.IsAlive || guard.Interior != null) return null;

            double now = Now();
            if (guard.Refuge != null)
                return IsTown23(guard.Refuge.BuildingKey) ? ExistingWallRouteSignal(guard, now) : (Vector3?)null;
            if (guard.Duty != GuardDuty.Engaged || guard.IncidentId == null || !actor.CombatReady) return null;
            double retryAt;
            if (cityWatchRetreatRetryAt.TryGetValue(guard.Id, out retryAt) && now < retryAt) return null;

            actor.Capture(now);
            if (!guard.IsAlive || guard.Duty != GuardDuty.Engaged || guard.Refuge != null ||
                guard.HealthFraction > NpcRolePolicy.RetreatMaximumHealthRatio) return null;

            if (refugeGeometry == null)
            {
                try { refugeGeometry = new TownGeometry(directory, GameManager.Instance.StreamingWorld.GetCurrentCityNavigation()); }
                catch (ArgumentException) { return MarkCityWallRouteUnavailable(guard.Id, now); }
            }

            var origin = Roster.Position(guard.Id, now);
            var home = refugeGeometry.RouteBetweenPoints(origin, guard.Post);
            if (!MatchesRouteEnds(home, origin, guard.Post)) return MarkCityWallRouteUnavailable(guard.Id, now);

            var candidates = new List<TownGeometry.Entrance>();
            foreach (var entrance in refugeGeometry.Entrances.Values)
            {
                BuildingSummary building;
                if (directory.GetBuildingSummary(entrance.BuildingKey, out building) &&
                    building.BuildingType == DFLocation.BuildingTypes.Town23)
                    candidates.Add(entrance);
            }
            candidates.Sort((a, b) =>
            {
                double ax = a.LocalDoor.x - origin.X, az = a.LocalDoor.z - origin.Z;
                double bx = b.LocalDoor.x - origin.X, bz = b.LocalDoor.z - origin.Z;
                int order = (ax * ax + az * az).CompareTo(bx * bx + bz * bz);
                return order != 0 ? order : a.BuildingKey.CompareTo(b.BuildingKey);
            });

            double timeScale = DaggerfallUnity.Instance.WorldTime.TimeScale;
            if (double.IsNaN(timeScale) || double.IsInfinity(timeScale) || timeScale <= 0)
                return MarkCityWallRouteUnavailable(guard.Id, now);
            for (int i = 0; i < Math.Min(8, candidates.Count); i++)
            {
                var entrance = candidates[i];
                if (!IsTown23(entrance.BuildingKey)) continue;
                var door = new SavedRoutePoint { X = entrance.LocalDoor.x, Z = entrance.LocalDoor.z };
                var outside = refugeGeometry.RouteBetweenPoints(origin, door);
                if (!MatchesRouteEnds(outside, origin, door)) continue;

                List<List<GuardPoint3>> rooms;
                if (!TryGetTown23RefugeRooms(entrance, out rooms)) continue;
                var exterior = new List<GuardPoint3>(outside.Count);
                foreach (var point in outside)
                    exterior.Add(new GuardPoint3 { X = point.X, Z = point.Z });

                foreach (var room in rooms)
                {
                    if (room == null || room.Count < 2) continue;
                    GuardRefugeJourney plan;
                    try { plan = GuardRefugeJourney.Create(entrance.BuildingKey, exterior, room, now, CityWatchRetreatSpeed, timeScale); }
                    catch (ArgumentException) { continue; }

                    double arrival = now + (RouteLength(exterior) + RouteLength(room)) * timeScale / CityWatchRetreatSpeed;
                    double release = CityWatchReleaseAt(arrival);
                    try { Roster.TakeRefuge(guard.Id, plan, now, home); }
                    catch (ArgumentException) { continue; }

                    var saved = Roster.Guards.Find(candidate => candidate.Id == guard.Id);
                    if (saved == null || saved.Refuge == null || !IsTown23(saved.Refuge.BuildingKey))
                        return MarkCityWallRouteUnavailable(guard.Id, now);
                    saved.Refuge.Release(release);
                    cityWatchRetreatRetryAt.Remove(guard.Id);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CITYWATCH_RETREAT: " + guard.Id + "; building=" + entrance.BuildingKey);
                    var next = exterior[1];
                    return refugeGeometry.ScenePoint(new RoutePoint(next.X, next.Z));
                }
            }
            return MarkCityWallRouteUnavailable(guard.Id, now);
        }

        private Vector3? MarkCityWallRouteUnavailable(string guardId, double now)
        {
            cityWatchRetreatRetryAt[guardId] = now + CityWatchRetreatRetrySeconds;
            return null;
        }

        private bool TryGetTown23RefugeRooms(TownGeometry.Entrance entrance, out List<List<GuardPoint3>> rooms)
        {
            if (refugeInteriors.TryGetValue(entrance.BuildingKey, out rooms))
                return rooms != null && rooms.Count > 0;

            DaggerfallStaticDoors selectedOwner = null;
            StaticDoor selected = default(StaticDoor);
            float best = float.MaxValue;
            foreach (var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if (owner.Doors == null) continue;
                for (int d = 0; d < owner.Doors.Length; d++)
                {
                    var door = owner.Doors[d];
                    if (door.doorType != DoorTypes.Building || door.buildingKey != entrance.BuildingKey) continue;
                    float gap = (owner.GetDoorPosition(d) - refugeGeometry.SceneDoor(entrance)).sqrMagnitude;
                    if (gap >= best) continue;
                    selectedOwner = owner;
                    selected = door;
                    best = gap;
                }
            }
            if (!selectedOwner)
            {
                rooms = null;
                return false;
            }

            rooms = RefugeGeometry.Survey(selectedOwner.transform, selected);
            if (rooms == null || rooms.Count == 0) return false;
            refugeInteriors[entrance.BuildingKey] = rooms;
            return true;
        }

        private Vector3? ExistingWallRouteSignal(GuardRecord guard, double now)
        {
            var refuge = guard.Refuge;
            if (refugeGeometry == null || refuge == null || !IsTown23(refuge.BuildingKey) || refuge.Exterior == null || refuge.Exterior.Count < 2)
                return null;
            if (refuge.Evaluate(now).Phase == RefugePhase.Complete) return null;
            var point = refuge.Evaluate(now).Position;
            return refugeGeometry.ScenePoint(new RoutePoint(point.X, point.Z));
        }

        private bool IsTown23(int buildingKey)
        {
            BuildingSummary building;
            return directory != null && directory.GetBuildingSummary(buildingKey, out building) &&
                building.BuildingType == DFLocation.BuildingTypes.Town23;
        }

        private static double CityWatchReleaseAt(double arrival)
        {
            double shelteredUntil = arrival + CityWatchMinimumShelterSeconds;
            double release = Math.Floor(shelteredUntil / 86400) * 86400 + DaggerfallDateTime.DawnHour * 3600;
            if (release < shelteredUntil) release += 86400;
            return release;
        }

        private static bool MatchesRouteEnds(IList<SavedRoutePoint> route, SavedRoutePoint start, SavedRoutePoint end)
        {
            return route != null && route.Count >= 2 && AtPoint(route[0], start) && AtPoint(route[route.Count - 1], end);
        }

        private static double RouteLength(IList<GuardPoint3> route)
        {
            double total = 0;
            for (int i = 1; i < route.Count; i++)
            {
                var a = route[i - 1];
                var b = route[i];
                double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
                total += Math.Sqrt(x * x + y * y + z * z);
            }
            return total;
        }
    }

    public sealed partial class GuardActor
    {
        private GuardPopulation populationOwner;

        internal void AttachPopulation(GuardPopulation owner)
        {
            populationOwner = owner;
        }

        internal Vector3? ResolveCityWallSafeWaypoint(DaggerfallEntityBehaviour behaviour)
        {
            return behaviour && behaviour == GetComponent<DaggerfallEntityBehaviour>() && populationOwner ?
                populationOwner.TryStartCityWallRefuge(this) : (Vector3?)null;
        }

        internal bool HasActiveCombatRefuge(DaggerfallEntityBehaviour behaviour)
        {
            return behaviour && behaviour == GetComponent<DaggerfallEntityBehaviour>() && populationOwner &&
                populationOwner.HasActiveCombatRefuge(this);
        }
    }
}
