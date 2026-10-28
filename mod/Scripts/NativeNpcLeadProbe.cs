using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in, read-only witness for a Lead accepted through the real conversation.
    // The operator must choose a native-known local building and move the player.
    public static class NativeNpcLeadProbe
    {
        public const string RunFlag = "-peopleoftheiliacbay-lead-probe";

        public static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            var game = GameManager.Instance;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) < 0 || !host || !streets ||
                game == null || game.PlayerEntity == null || game.PlayerMotor == null ||
                string.IsNullOrEmpty(character) ||
                !NativeProbeText.IsTestCharacter(character) ||
                game.PlayerEntity.Name != character || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Lead witness requires the caller-owned exterior fixture.");

            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            if (!directory) throw new InvalidOperationException("Lead witness has no current native building directory.");
            var store = typeof(StreetPopulation).GetField("worldStore", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(streets) as WorldStateStore;
            if (store == null) throw new InvalidOperationException("Lead witness has no restored action store.");
            int settlement = directory.MapID;
            var preexisting = new HashSet<string>(StringComparer.Ordinal);
            foreach (var existing in store.PendingNpcActions()) preexisting.Add(existing.Id);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LEAD_PROBE_READY: use native Talk with a managed adult; ask for a known local building and accept Lead. Remain within 8m until movement starts.");

            NpcActionJob job = null;
            NpcLeadRuntimeSnapshot first = null;
            float until = Time.realtimeSinceStartup + 180;
            while (Time.realtimeSinceStartup < until)
            {
                foreach (var candidate in store.PendingNpcActions())
                    {
                        if (candidate.Kind != NpcActionKind.Lead || candidate.Target == null ||
                            preexisting.Contains(candidate.Id) ||
                            candidate.Target.Kind != NpcActionTargetKind.Building ||
                            candidate.Target.SettlementId != settlement ||
                            candidate.Phase == NpcActionPhase.Completed || candidate.Phase == NpcActionPhase.Failed ||
                            candidate.Phase == NpcActionPhase.Cancelled) continue;
                        var actor = streets.FindActor(candidate.ResidentId);
                        if (!actor || !actor.gameObject.activeInHierarchy || actor.Resident == null ||
                            !actor.Resident.IsAlive || actor.Resident.IsQuestProtected) continue;
                        try
                        {
                            var observation = streets.CaptureLeadRuntimeSnapshot(candidate.ResidentId);
                            if (observation.GoalDistance < 12 || observation.PlayerDistance > 8) continue;
                            job = candidate; first = observation; break;
                        }
                        catch (InvalidOperationException) { }
                    }
                if (job != null) break;
                yield return null;
            }
            if (job == null) throw new InvalidOperationException("No accepted live Lead with an exact local building at least 12m away appeared.");
            string actionId = job.Id, residentId = job.ResidentId;
            int buildingKey = job.Target.BuildingKey;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LEAD_ACCEPTED: action=" + actionId + "; resident=" + residentId +
                "; settlement=" + settlement + "; building=" + buildingKey + "; goal=" + first.GoalGround);

            // Movement must be observed before testing pause. A stationary NPC is no proof.
            Vector3 start = first.ActorGround;
            until = Time.realtimeSinceStartup + 90;
            while (Time.realtimeSinceStartup < until)
            {
                var current = RequireActive(streets, residentId, actionId, settlement, buildingKey);
                if (current.PlayerDistance <= 8 && Vector3.Distance(start, current.ActorGround) > 0.5f) break;
                yield return null;
            }
            if (Time.realtimeSinceStartup >= until) throw new InvalidOperationException("Lead NPC did not physically start walking while player was nearby.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LEAD_MOVING: now move the player more than 8m from the NPC.");

            until = Time.realtimeSinceStartup + 90;
            NpcLeadRuntimeSnapshot far = null;
            while (Time.realtimeSinceStartup < until)
            {
                var current = RequireActive(streets, residentId, actionId, settlement, buildingKey);
                if (current.PlayerDistance > 9 && current.Progress == NpcLeadProgress.WaitingForPlayer)
                { far = current; break; }
                yield return null;
            }
            if (far == null) throw new InvalidOperationException("Player never crossed the Lead pause distance.");
            var pauseEnd = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < pauseEnd)
            {
                var current = RequireActive(streets, residentId, actionId, settlement, buildingKey);
                if (current.PlayerDistance <= 8 || current.Progress != NpcLeadProgress.WaitingForPlayer ||
                    Vector3.Distance(far.ActorGround, current.ActorGround) > 0.2f)
                    throw new InvalidOperationException("Lead NPC advanced or changed state during the far-player pause.");
                yield return null;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LEAD_PAUSED: return within 8m and accompany the NPC to the doorway.");

            until = Time.realtimeSinceStartup + 90;
            bool resumed = false;
            while (Time.realtimeSinceStartup < until)
            {
                var current = RequireActive(streets, residentId, actionId, settlement, buildingKey);
                if (current.PlayerDistance <= 8 && Vector3.Distance(far.ActorGround, current.ActorGround) > 0.5f)
                { resumed = true; break; }
                yield return null;
            }
            if (!resumed) throw new InvalidOperationException("Lead NPC did not resume physical walking with player nearby.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LEAD_RESUMED: action=" + actionId);

            until = Time.realtimeSinceStartup + 180;
            while (Time.realtimeSinceStartup < until)
            {
                var current = store.ReadNpcAction(actionId);
                if (current == null) throw new InvalidOperationException("Lead action vanished before completion.");
                if (current.Phase == NpcActionPhase.Failed || current.Phase == NpcActionPhase.Cancelled)
                    throw new InvalidOperationException("Lead ended without arrival: " + current.FailureReason);
                if (current.Phase == NpcActionPhase.Completed)
                {
                    if (current.Target == null || current.Target.SettlementId != settlement ||
                        current.Target.BuildingKey != buildingKey || string.IsNullOrWhiteSpace(current.CompletedEffectId) ||
                        current.LastPosition == null || current.LastPosition.Kind != NpcActionTargetKind.WorldPoint ||
                        current.LastPosition.SettlementId != settlement ||
                        current.LastPosition.WorldSpaceId != "settlement:" + settlement ||
                        Vector3.Distance(directory.transform.TransformPoint(new Vector3((float)current.LastPosition.X,
                            (float)current.LastPosition.Y, (float)current.LastPosition.Z)), first.GoalGround) > 0.5f ||
                        Vector3.Distance(game.PlayerMotor.transform.position - Vector3.up, first.GoalGround) > 8.5f ||
                        NpcActionFeedback.Describe(current, "The resident", null, null) == null)
                        throw new InvalidOperationException("Completed Lead lacks the exact target or truthful feedback.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LEAD_PROBE_PASS: action=" + actionId +
                        "; settlement=" + settlement + "; building=" + buildingKey +
                        "; physical-start pause resume completion feedback");
                    yield break;
                }
                yield return null;
            }
            throw new InvalidOperationException("Lead did not complete at the observed doorway before probe timeout.");
        }

        private static NpcLeadRuntimeSnapshot RequireActive(StreetPopulation streets, string residentId,
            string actionId, int settlement, int buildingKey)
        {
            var current = streets.CaptureLeadRuntimeSnapshot(residentId);
            if (current.ActionId != actionId || current.SettlementId != settlement ||
                current.BuildingKey != buildingKey || current.Phase == NpcActionPhase.Failed ||
                current.Phase == NpcActionPhase.Cancelled || current.Progress == NpcLeadProgress.Invalid ||
                current.Progress == NpcLeadProgress.Expired)
                throw new InvalidOperationException("Lead identity, destination, or validity changed before arrival.");
            return current;
        }
    }
}
