using System;
using System.Text;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private string lastVisibilityScene;
        private float visibilityDiagnosticAt;
        private string visibilityDiagnosticReason;

        private static string GuardVisibilitySceneKey(GameManager game, BuildingDirectory owner)
        {
            int map = game.PlayerGPS.HasCurrentLocation ? game.PlayerGPS.CurrentLocation.MapTableData.MapId : -1;
            bool inside = game.PlayerEnterExit.IsPlayerInside;
            int building = inside && game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior ?
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey : -1;
            return map + ":" + (inside ? building >= 0 ? "building:" + building : "interior" : "exterior") +
                ";owner=" + (owner ? owner.MapID : -1);
        }

        private void LogGuardVisibilityOnSceneChange(GameManager game)
        {
            if (Roster == null || directory == null || geometry == null || game == null) return;
            string scene = GuardVisibilitySceneKey(game, directory);
            if (scene == lastVisibilityScene) return;
            lastVisibilityScene = scene;
            ScheduleSettledGuardVisibility("scene:" + scene);
        }

        private void ScheduleSettledGuardVisibility(string reason)
        {
            visibilityDiagnosticReason = reason + ":settled";
            visibilityDiagnosticAt = Time.unscaledTime + 2f;
        }

        private void TickSettledGuardVisibility(GameManager game)
        {
            if (string.IsNullOrEmpty(visibilityDiagnosticReason) || Time.unscaledTime < visibilityDiagnosticAt ||
                Roster == null || directory == null || geometry == null || game == null) return;
            string reason = visibilityDiagnosticReason;
            visibilityDiagnosticReason = null;
            LogGuardVisibility(reason, game);
        }

        private void LogGuardVisibility(string reason, GameManager game)
        {
            if (Roster == null || directory == null || geometry == null || game == null) return;
            var player = game.PlayerMotor.transform.position;
            var local = directory.transform.InverseTransformPoint(player);
            int radius = Mathf.Clamp(ActorRadius, 64, 256);
            int alive = 0, spawned = 0, activeSelf = 0, activeHierarchy = 0;
            int close = 0, closeMissing = 0, outside = 0, interior = 0, refuge = 0, searching = 0;
            double now = Now();
            foreach (var pair in actors)
            {
                if (!pair.Value) continue;
                spawned++;
                if (pair.Value.gameObject.activeSelf) activeSelf++;
                if (pair.Value.gameObject.activeInHierarchy) activeHierarchy++;
            }
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive) continue;
                alive++;
                GuardActor actor;
                bool hasActiveActor = actors.TryGetValue(guard.Id, out actor) && actor && actor.gameObject.activeInHierarchy;
                if (guard.Interior != null) { interior++; continue; }
                if (GuardActor.RefugeIndoors(guard, now)) { refuge++; continue; }
                var investigation = Roster.Reports.Find(r => r.Id == guard.IncidentId && r.Investigation);
                if (investigation != null && investigation.BuildingKey.HasValue && guard.Duty == GuardDuty.Searching)
                { searching++; continue; }
                var position = Roster.Position(guard.Id, now);
                var scenePosition = geometry.ScenePoint(new RoutePoint(position.X, position.Z));
                bool withinRadius = (scenePosition - player).sqrMagnitude < radius * radius;
                if (withinRadius || guard.Duty == GuardDuty.Engaged)
                {
                    close++;
                    if (!hasActiveActor) closeMissing++;
                }
                else outside++;
            }

            var line = new StringBuilder(1100);
            line.Append("PEOPLE_OF_THE_ILIAC_BAY_GUARD_VISIBILITY: reason=").Append(reason)
                .Append("; map=").Append(directory.MapID)
                .Append("; playerMap=").Append(game.PlayerGPS.HasCurrentLocation ? game.PlayerGPS.CurrentLocation.MapTableData.MapId : -1)
                .Append("; playerWorld=").Append(Format(player))
                .Append("; playerLocal=").Append(Format(local))
                .Append("; radius=").Append(radius)
                .Append("; roster=").Append(Roster.Guards.Count).Append(" alive=").Append(alive)
                .Append(" spawned=").Append(spawned).Append(" activeSelf=").Append(activeSelf)
                .Append(" activeHierarchy=").Append(activeHierarchy)
                .Append(" nearOrEngaged=").Append(close).Append(" nearMissingActor=").Append(closeMissing)
                .Append(" outsideRadius=").Append(outside).Append(" interior=").Append(interior)
                .Append(" refuge=").Append(refuge).Append(" sceneSearchHidden=").Append(searching)
                .Append("; setup=").Append(publicPostSurveyPending ? "deferred-post-survey" : "ready")
                .Append("; setupSurvey=").Append(setupSurvey == null ? "idle" : "running")
                .Append("; gateSurveyed=").Append(gateDutySurveyed)
                .Append("; gateWork=").Append(gateDutyWork.Count)
                .Append("; gates=[");
            bool first = true;
            foreach (var duty in GateGuardDutySnapshots(now))
            {
                if (!first) line.Append(',');
                first = false;
                var world = geometry.ScenePoint(new RoutePoint(duty.Position.X, duty.Position.Z));
                float distance = Vector3.Distance(world, player);
                line.Append(duty.GateIndex).Append(duty.ExteriorAssignment ? 'O' : 'I')
                    .Append(duty.Side).Append(':').Append(duty.GuardId ?? "-")
                    .Append('/').Append(duty.Duty)
                    .Append('@').Append(Format(duty.Position))
                    .Append('/').Append(distance.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))
                    .Append(duty.GuardId != null && distance < radius ? '*' : '-');
            }
            line.Append(']');
            Debug.Log(line.ToString());
        }

        private static string Format(Vector3 point)
        {
            return "(" + point.x.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "," +
                point.y.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "," +
                point.z.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        private static string Format(SavedRoutePoint point)
        {
            return "(" + point.X.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "," +
                point.Z.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }
}
