using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private sealed class CrimeSceneMotion
        {
            public List<GuardPoint3> Route;
            public int Segment;
            public bool AtCorpse;
            public bool Wandering;
            public bool Inspecting;
            public int WanderCount;
        }
        private readonly Dictionary<string, CrimeSceneMotion> crimeSceneMotions = new Dictionary<string, CrimeSceneMotion>();
        private const float InteriorGuardSpeed = 2.8f;

        private void UpdateCrimeSceneInterior(GuardRecord guard, GuardReport report, GuardActor actor, double now)
        {
            if (worldStore == null || interiorNavigation == null || guard.Interior == null || guard.Interior.Returning) return;
            const string discoveryPrefix = "discovery:";
            const string neutralPrefix = "inn-alarm:";
            const string witnessedPrefix = "civilian:";
            string incidentId = report.Id.StartsWith(discoveryPrefix, StringComparison.Ordinal) ? report.Id.Substring(discoveryPrefix.Length) :
                report.Id.StartsWith(neutralPrefix,StringComparison.Ordinal) ? report.Id.Substring(neutralPrefix.Length) :
                report.CrimeScene && report.Id.StartsWith(witnessedPrefix,StringComparison.Ordinal) ? report.Id.Substring(witnessedPrefix.Length) : null;
            if (incidentId == null) return;
            worldStore.AssignCorpseInspector(incidentId,report.Id,now);
            var corpse = worldStore.GetCrimeSceneCorpseSnapshot(incidentId);
            if (corpse == null || corpse.BuildingKey != guard.Interior.BuildingKey || !report.HasArrival) return;
            var motion = GetCrimeSceneMotion(guard.Id);
            bool inspecting = !corpse.Cleared && corpse.InspectionGuardId == guard.Id;
            if(motion.Inspecting!=inspecting)
            {
                motion.Inspecting=inspecting; motion.Route=null; motion.Segment=0;
                motion.AtCorpse=false; motion.Wandering=false;
            }
            var enemy = actor.GetComponent<DaggerfallWorkshop.Game.EnemyMotor>();
            bool combat = actor.CombatReady || enemy != null && enemy.IsHostile;
            if (GameManager.IsGamePaused || combat)
            {
                if (combat)
                {
                    motion.Route = null;
                    motion.Segment = 0;
                    if (inspecting && motion.AtCorpse)
                        motion.AtCorpse = CanInspectCorpse(actor.transform.position, corpse);
                }
                return;
            }
            if (inspecting)
            {
                if (motion.AtCorpse)
                {
                    if (!CanInspectCorpse(actor.transform.position, corpse))
                    {
                        motion.AtCorpse = false;
                        motion.Route = null;
                        motion.Segment = 0;
                    }
                    else
                    {
                        Vector3 localCorpse = new Vector3((float)corpse.X, (float)corpse.Y, (float)corpse.Z);
                        Vector3 face = interior.transform.TransformPoint(localCorpse) - actor.transform.position;
                        face.y = 0;
                        if (face.sqrMagnitude > 0.0001f) actor.SetInteriorPose(actor.transform.position, face, false);
                        worldStore.AdvanceCrimeSceneInspection(incidentId, guard.Id, Time.unscaledDeltaTime,
                            false, false, true, now);
                        return;
                    }
                }
                if (motion.Route == null && !TryRouteToCorpse(guard, actor, corpse, motion)) return;
                if (MoveAlongInteriorRoute(actor, motion, Time.unscaledDeltaTime))
                {
                    motion.AtCorpse = CanInspectCorpse(actor.transform.position, corpse);
                    if (motion.AtCorpse)
                    {
                        Vector3 local = new Vector3((float)corpse.X, (float)corpse.Y, (float)corpse.Z);
                        Vector3 face = interior.transform.TransformPoint(local) - actor.transform.position;
                        face.y = 0;
                        actor.SetInteriorPose(actor.transform.position, face, false);
                    }
                    else { motion.Route = null; motion.Segment = 0; }
                }
                return;
            }

            if (!motion.Wandering)
            {
                motion.Wandering = true;
                motion.Route = null;
                motion.Segment = 0;
                motion.AtCorpse = false;
            }
            if (motion.Route == null && !TryRouteToWanderPoint(guard, motion)) return;
            if (MoveAlongInteriorRoute(actor, motion, Time.unscaledDeltaTime))
            {
                motion.Route = null;
                motion.Segment = 0;
                motion.WanderCount++;
            }
        }

        private CrimeSceneMotion GetCrimeSceneMotion(string guardId)
        {
            CrimeSceneMotion motion;
            if (!crimeSceneMotions.TryGetValue(guardId, out motion))
            { motion = new CrimeSceneMotion(); crimeSceneMotions.Add(guardId, motion); }
            return motion;
        }

        private bool TryRouteToCorpse(GuardRecord guard, GuardActor actor, CorpseState corpse, CrimeSceneMotion motion)
        {
            if (!corpse.HasPosition || corpse.BuildingKey != guard.Interior.BuildingKey) return false;
            var from = interior.transform.InverseTransformPoint(actor.transform.position) - Vector3.up;
            var body = new Vector3((float)corpse.X, (float)corpse.Y, (float)corpse.Z);
            double best = double.MaxValue;
            foreach (var anchor in interiorNavigation.ApproachPoints(body,1.5f,2.0f))
            {
                float dy = Mathf.Abs(anchor.y - body.y);
                float dx = anchor.x - body.x, dz = anchor.z - body.z;
                float bodyDistance = Mathf.Sqrt(dx * dx + dz * dz);
                if (dy > 2.0f || bodyDistance > 1.5f) continue;
                var anchorWorld = interior.transform.TransformPoint(anchor + Vector3.up);
                if (!interiorNavigation.HasClearLine(anchorWorld + Vector3.up * 0.25f,
                    interior.transform.TransformPoint(body + Vector3.up * 0.5f))) continue;
                var route = interiorNavigation.RouteBetween(from, anchor);
                if (route.Count < 2) continue;
                double length = RouteLength(route) + bodyDistance * 2;
                if (length >= best) continue;
                best = length; motion.Route = route; motion.Segment = 1;
            }
            return motion.Route != null;
        }

        private bool CanInspectCorpse(Vector3 actorPosition, CorpseState corpse)
        {
            Vector3 body = interior.transform.TransformPoint(new Vector3((float)corpse.X, (float)corpse.Y, (float)corpse.Z));
            Vector3 horizontal = actorPosition - body;
            horizontal.y = 0;
            return horizontal.sqrMagnitude <= 2.25f && Mathf.Abs(actorPosition.y - body.y) <= 2.0f &&
                interiorNavigation.HasClearLine(actorPosition + Vector3.up * 0.25f, body + Vector3.up * 0.5f);
        }

        private bool TryRouteToWanderPoint(GuardRecord guard, CrimeSceneMotion motion)
        {
            if (interiorNavigation.LocalAnchors.Count == 0) return false;
            var actor = actors.ContainsKey(guard.Id) ? actors[guard.Id] : null;
            if (!actor) return false;
            var from = interior.transform.InverseTransformPoint(actor.transform.position) - Vector3.up;
            int start = (int)(StableSeed.Combine(guard.AppearanceSeed, (uint)guard.Interior.BuildingKey,
                (uint)motion.WanderCount, 0x4352494d) % (uint)interiorNavigation.LocalAnchors.Count);
            for (int offset = 0; offset < interiorNavigation.LocalAnchors.Count; offset++)
            {
                int index = (start + offset) % interiorNavigation.LocalAnchors.Count;
                var route = interiorNavigation.RouteBetween(from, interiorNavigation.LocalAnchors[index]);
                if (route.Count < 2) continue;
                motion.Route = route; motion.Segment = 1;
                return true;
            }
            return false;
        }

        private bool MoveAlongInteriorRoute(GuardActor actor, CrimeSceneMotion motion, float elapsed)
        {
            if (motion.Route == null || motion.Route.Count < 2) return true;
            elapsed = Mathf.Clamp(elapsed, 0, 0.25f);
            float remaining = InteriorGuardSpeed * elapsed;
            while (motion.Segment < motion.Route.Count)
            {
                var point = motion.Route[motion.Segment];
                Vector3 localTarget = new Vector3((float)point.X, (float)point.Y + 1f, (float)point.Z);
                Vector3 target = interior.transform.TransformPoint(localTarget);
                Vector3 delta = target - actor.transform.position;
                if (delta.magnitude <= remaining + 0.01f)
                {
                    InteriorGeometry.OpenOrdinaryDoors(actor.transform.position, target);
                    actor.SetInteriorPose(target, delta, true);
                    remaining = Mathf.Max(0, remaining - delta.magnitude);
                    motion.Segment++;
                    continue;
                }
                Vector3 next = actor.transform.position + delta.normalized * remaining;
                InteriorGeometry.OpenOrdinaryDoors(actor.transform.position, next);
                actor.SetInteriorPose(next, delta, true);
                return false;
            }
            actor.SetInteriorPose(actor.transform.position, Vector3.zero, false);
            return true;
        }

        private static double RouteLength(List<GuardPoint3> route)
        {
            double length = 0;
            for (int i = 1; i < route.Count; i++)
            {
                double x = route[i].X - route[i - 1].X, y = route[i].Y - route[i - 1].Y, z = route[i].Z - route[i - 1].Z;
                length += Math.Sqrt(x * x + y * y + z * z);
            }
            return length;
        }
    }
}
