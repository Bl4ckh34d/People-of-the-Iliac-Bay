using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class GuardPopulation
    {
        private Coroutine dayRestSurvey;
        private float nextDayRestAttempt;

        private void UpdateDayRest(double now)
        {
            if (dayRestSurvey != null || Time.unscaledTime < nextDayRestAttempt || !OwnsExterior || GameManager.IsGamePaused) return;
            var guard = Roster.Guards.Find(g => g.IsAlive && g.DayRestUntil > now &&
                g.Duty == GuardDuty.Posted && g.Interior == null && g.Refuge == null &&
                !Roster.IsOnPatrol(g.Id) && g.IncidentId == null && !IsGateGuard(g.Id));
            if (guard != null)
            {
                // Replacement interiors can omit wall beds. Retry at a bounded
                // cadence instead of resurveying every gameplay tick.
                nextDayRestAttempt = Time.unscaledTime + 4f;
                dayRestSurvey = StartCoroutine(PrepareDayRest(guard, now));
            }
        }

        private IEnumerator PrepareDayRest(GuardRecord guard, double started)
        {
            var roster = Roster; var town = directory;
            Func<bool> valid = () => OwnsExterior && Roster == roster && directory == town &&
                guard.IsAlive && guard.DayRestUntil > started && guard.Duty == GuardDuty.Posted &&
                guard.Refuge == null && guard.Interior == null && !Roster.IsOnPatrol(guard.Id) && guard.IncidentId == null;
            try
            {
                if (refugeGeometry == null)
                {
                    var mapped = new TownGeometry(directory, GameManager.Instance.StreamingWorld.GetCurrentCityNavigation(), false);
                    yield return mapped.ReadEntranceSteps();
                    if (!valid()) yield break;
                    refugeGeometry = mapped;
                }
                var candidates = new List<TownGeometry.Entrance>();
                foreach (var entrance in refugeGeometry.Entrances.Values)
                {
                    BuildingSummary building;
                    if (directory.GetBuildingSummary(entrance.BuildingKey, out building) &&
                        building.BuildingType == DFLocation.BuildingTypes.Town23)
                        candidates.Add(entrance);
                }
                candidates.Sort((a, b) => {
                    double ax = a.LocalDoor.x - guard.Post.X, az = a.LocalDoor.z - guard.Post.Z;
                    double bx = b.LocalDoor.x - guard.Post.X, bz = b.LocalDoor.z - guard.Post.Z;
                    int order = (ax * ax + az * az).CompareTo(bx * bx + bz * bz);
                    return order != 0 ? order : a.BuildingKey.CompareTo(b.BuildingKey);
                });
                foreach (var entrance in candidates)
                {
                    if (!valid()) yield break;
                    var route = new TownGeometry.RouteResult();
                    yield return refugeGeometry.ReadRouteSteps(guard.Post,
                        new SavedRoutePoint { X = entrance.LocalDoor.x, Z = entrance.LocalDoor.z }, route, valid);
                    if (!valid() || route.Points == null || route.Points.Count < 2) continue;
                    List<List<GuardPoint3>> rooms;
                    if (!refugeInteriors.TryGetValue(entrance.BuildingKey, out rooms) ||
                        !wallBedRoutes.ContainsKey(entrance.BuildingKey))
                    {
                        DaggerfallStaticDoors owner = null; StaticDoor selected = default(StaticDoor); float closest = float.MaxValue;
                        foreach (var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                        for (int index = 0; index < (doors.Doors == null ? 0 : doors.Doors.Length); index++)
                        {
                            var door = doors.Doors[index];
                            if (door.doorType != DoorTypes.Building || door.buildingKey != entrance.BuildingKey) continue;
                            float distance = (doors.GetDoorPosition(index) - refugeGeometry.SceneDoor(entrance)).sqrMagnitude;
                            if (distance < closest) { closest = distance; owner = doors; selected = door; }
                        }
                        if (!owner) continue;
                        rooms = new List<List<GuardPoint3>>(); var beds = new List<WallQuarterBeds.SurveyedBed>();
                        var survey = RefugeGeometry.SurveySteps(owner.transform, selected, rooms, beds);
                        try { while (survey.MoveNext()) { yield return null; if (!valid()) yield break; } }
                        finally { (survey as IDisposable)?.Dispose(); }
                        refugeInteriors[entrance.BuildingKey] = rooms; wallBedRoutes[entrance.BuildingKey] = beds;
                    }
                    foreach (var bed in wallBedRoutes[entrance.BuildingKey])
                    {
                        if (!WallAvailable(entrance.BuildingKey, guard.Id, null, bed.Anchor.RecordIndex)) continue;
                        var exterior = route.Points.ConvertAll(p => new GuardPoint3 { X = p.X, Z = p.Z });
                        var plan = GuardRefugeJourney.Create(entrance.BuildingKey, exterior, bed.EntranceRoute, started, 1.3, DaggerfallUnity.Instance.WorldTime.TimeScale);
                        plan.WallDuty = true; plan.RestBedRecord = bed.Anchor.RecordIndex;
                        try
                        {
                            Roster.TakeRefuge(guard.Id, plan, started);
                            guard.Refuge.Release(guard.DayRestUntil);
                            Debug.Log("LIVING_CITIES_GUARD_DAY_REST: " + guard.Id + "; building=" + entrance.BuildingKey);
                            yield break;
                        }
                        catch (ArgumentException) { /* Another guard can claim this bed during a yielded survey. */ }
                    }
                }
            }
            finally { dayRestSurvey = null; }
        }
    }
}
