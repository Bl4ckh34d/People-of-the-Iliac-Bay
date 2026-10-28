using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        public bool NightPatrols = true;
        public bool DayPatrols = true;
        public int MaximumPatrolPairs = 8;
        private Coroutine patrolSurvey;
        private readonly List<PatrolPairPlan> patrolTemplates = new List<PatrolPairPlan>();
        private float nextPatrolTick;
        private static double RouteLength(List<SavedRoutePoint> route)
        {
            double total = 0;
            for (int i = 1; i < route.Count; i++) total += Gap(route[i - 1], route[i]); return total;
        }
        private static double Gap(SavedRoutePoint a, SavedRoutePoint b)
        { double x = a.X - b.X, z = a.Z - b.Z; return Math.Sqrt(x*x + z*z); }
        private static SavedRoutePoint RouteAt(List<SavedRoutePoint> route, double along)
        {
            for (int i = 1; i < route.Count; i++)
            {
                double length = Gap(route[i - 1], route[i]);
                if (length <= 0) continue;
                if (along <= length) return new SavedRoutePoint { X = route[i - 1].X + (route[i].X-route[i-1].X)*along/length,
                    Z = route[i - 1].Z + (route[i].Z-route[i-1].Z)*along/length };
                along -= length;
            }
            return route[route.Count - 1];
        }
        private List<SavedRoutePoint> ExactSurvey(SavedRoutePoint from, SavedRoutePoint to)
        {
            var route = geometry.RouteBetweenPoints(from, to);
            if (route.Count < 2 || Gap(route[0], from) > 0.05 || Gap(route[route.Count - 1], to) > 0.05) return null;
            route[0] = new SavedRoutePoint { X = from.X, Z = from.Z };
            route[route.Count - 1] = new SavedRoutePoint { X = to.X, Z = to.Z }; return route;
        }
        private IEnumerator SurveyPatrols()
        {
            yield return null;
            var used = new HashSet<string>();
            foreach (var plan in patrolTemplates) { used.Add(plan.LeaderId); used.Add(plan.FollowerId); }
            var guards = new List<GuardRecord>(Roster.Guards);
            guards.Sort((a,b) => string.CompareOrdinal(a.Id,b.Id));
            int shiftCap = GuardShifts.PatrolPairTarget(Math.Max(3,baseGuardTarget),Mathf.Clamp(MaximumPatrolPairs,0,16));
            foreach (var leader in guards)
            {
                int shiftTemplates = patrolTemplates.FindAll(p => guards.Exists(g => g.Id == p.LeaderId && g.Shift == leader.Shift)).Count;
                if (!leader.IsAlive || used.Contains(leader.Id) || IsGateGuard(leader.Id) ||
                    shiftTemplates >= shiftCap) continue;
                GuardRecord partner = null; double distance = double.MaxValue;
                foreach (var candidate in guards)
                    if (candidate.IsAlive && candidate != leader && candidate.Shift == leader.Shift && !used.Contains(candidate.Id) &&
                        !IsGateGuard(candidate.Id) && Gap(leader.Post, candidate.Post) < distance)
                    { partner = candidate; distance = Gap(leader.Post,candidate.Post); }
                if (partner == null) continue;
                while (!OwnsExterior || GameManager.IsGamePaused) yield return null;
                var navigation = GameManager.Instance.StreamingWorld.GetCurrentCityNavigation();
                var cellPosition = navigation.WorldToNavGridPosition(navigation.SceneToWorldPosition(geometry.ScenePoint(new RoutePoint(leader.Post.X,leader.Post.Z))));
                var center = new GridCell(cellPosition.X,cellPosition.Y); bool found = false; int attempts = 0;
                foreach (int offsetZ in new[] { 0, 8, -8, 16, -16 })
                foreach (int offsetX in new[] { 0, 8, -8, 16, -16 })
                foreach (int radius in new[] { 4, 8, 12 })
                {
                    if (found || attempts++ >= 64) continue;
                    while (!OwnsExterior || GameManager.IsGamePaused) yield return null;
                    var cells = new[] { new GridCell(center.X+offsetX-radius,center.Z+offsetZ-radius),
                        new GridCell(center.X+offsetX+radius,center.Z+offsetZ-radius),
                        new GridCell(center.X+offsetX+radius,center.Z+offsetZ+radius),
                        new GridCell(center.X+offsetX-radius,center.Z+offsetZ+radius) };
                    bool usable = true;
                    foreach (var cell in cells) if (cell.X < 0 || cell.Z < 0 || cell.X >= geometry.Width || cell.Z >= geometry.Height ||
                        geometry.Cost(cell.X, cell.Z) <= 0) usable = false;
                    if (!usable) { yield return null; continue; }
                    var corners = new List<SavedRoutePoint>(); foreach (var cell in cells) corners.Add(Local(geometry.SceneCell(cell)));
                    var circuit = new List<SavedRoutePoint>();
                    for (int i = 0; i < 4; i++)
                    {
                        while (!OwnsExterior || GameManager.IsGamePaused) yield return null;
                        var edge = ExactSurvey(corners[i], corners[(i+1)%4]);
                        if (edge == null) { usable = false; break; }
                        foreach (var point in edge) if (circuit.Count == 0 || Gap(circuit[circuit.Count-1],point) > 0.000001) circuit.Add(point);
                        yield return null;
                    }
                    if (!usable || circuit.Count < 4 || circuit.Count > 512 || RouteLength(circuit) > 240 || RouteCrossesGate(circuit)) continue;
                    var leaderJoin = ExactSurvey(leader.Post, circuit[0]); yield return null;
                    while (!OwnsExterior || GameManager.IsGamePaused) yield return null;
                    var followerJoin = ExactSurvey(partner.Post, RouteAt(circuit, 2)); yield return null;
                    if (leaderJoin == null || followerJoin == null || RouteCrossesGate(leaderJoin) || RouteCrossesGate(followerJoin)) continue;
                    try
                    {
                        var plan = PatrolPairPlan.Create(leader.Id, partner.Id, leaderJoin, followerJoin, circuit, 0, 86400, 1.3, DaggerfallUnity.Instance.WorldTime.TimeScale, 2);
                        patrolTemplates.Add(plan); used.Add(leader.Id); used.Add(partner.Id); found = true;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PATROL_SURVEY: pair=" + leader.Id + "/" + partner.Id + "; length=" + RouteLength(circuit));
                    }
                    catch (ArgumentException) { /* Reject crossed circuits or unsafe formation geometry. */ }
                }
            }
            patrolSurvey = null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PATROL_SURVEY_DONE: pairs=" + patrolTemplates.Count);
        }
        private void UpdateNightPatrols(double now)
        {
            bool enabled = !HauntingShelterNow(now) && MaximumPatrolPairs > 0 &&
                (GuardShifts.At(now) == GuardShift.Night ? NightPatrols : DayPatrols);
            if (!enabled)
            {
                foreach (var pair in new List<GuardPatrolAssignment>(Roster.Patrols))
                {
                    if (!pair.InterruptedAt.HasValue)
                        Roster.StopPatrol(pair.Plan.LeaderId, now);
                }
                return;
            }
            if (!OwnsExterior || GameManager.Instance.PlayerEntity.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None) return;
            int staff = Roster.Guards.FindAll(g => GuardShifts.OnDuty(g,now) && !IsGateGuard(g.Id)).Count;
            int cap = Math.Min(GuardShifts.PatrolPairTarget(Math.Max(3,baseGuardTarget),Mathf.Clamp(MaximumPatrolPairs,0,16)),
                Math.Max(0,(staff-1)/2));
            int active = 0;
            foreach (var pair in new List<GuardPatrolAssignment>(Roster.Patrols))
                if (!pair.InterruptedAt.HasValue && ++active > cap) Roster.StopPatrol(pair.Plan.LeaderId,now);
            double shiftEnd = GuardShifts.NextChange(now);
            foreach (var template in patrolTemplates)
            {
                if (Roster.Patrols.FindAll(p => !p.InterruptedAt.HasValue).Count >= cap) break;
                int free = Roster.Guards.FindAll(g => GuardShifts.OnDuty(g,now) && g.Duty == GuardDuty.Posted &&
                    g.Refuge == null && !Roster.IsOnPatrol(g.Id) && !IsGateGuard(g.Id)).Count;
                if (free < 3) break;
                var a = Roster.Guards.Find(g => g.Id == template.LeaderId);
                var b = Roster.Guards.Find(g => g.Id == template.FollowerId);
                if (!GuardShifts.OnDuty(a,now) || !GuardShifts.OnDuty(b,now) || a.Duty != GuardDuty.Posted || b.Duty != GuardDuty.Posted ||
                    Roster.IsOnPatrol(a.Id) || Roster.IsOnPatrol(b.Id) || IsGateGuard(a.Id) || IsGateGuard(b.Id)) continue;
                try
                {
                    var plan = PatrolPairPlan.Create(a.Id,b.Id,template.LeaderJoin,template.FollowerJoin,template.Circuit,now,shiftEnd,1.3,DaggerfallUnity.Instance.WorldTime.TimeScale,2);
                    Roster.StartPatrol(plan,now);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PATROL_START: " + a.Id + "/" + b.Id + "; shift=" + a.Shift);
                }
                catch (ArgumentException) { /* Not enough night left to join, or a member has not returned. */ }
            }
        }
    }
}
