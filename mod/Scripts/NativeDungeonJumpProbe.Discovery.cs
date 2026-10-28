using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonJumpProbe
    {
        // Test setup only. Sample the installed mesh and actual static collision;
        // never create a ledge, a link, or a production route to obtain a pass.
        private static IEnumerator DiscoverFloors(ResidentActor actor, NativeDungeonJumpTraversal jump,
            Action<Vector3, Vector3> found)
        {
            var mesh = NavMesh.CalculateTriangulation();
            var anchors = new List<Vector3>();
            var occupied = new HashSet<Vector3Int>();
            var root = GameManager.Instance.PlayerEnterExit.Dungeon.transform;
            for (int index = 0; index + 2 < mesh.indices.Length; index += 3)
            {
                if (index % 384 == 0) yield return null;
                var a = mesh.vertices[mesh.indices[index]];
                var b = mesh.vertices[mesh.indices[index + 1]];
                var c = mesh.vertices[mesh.indices[index + 2]];
                var center = (a + b + c) / 3;
                foreach (var point in new[] { center, Vector3.MoveTowards((a + b) / 2, center, 0.45f),
                    Vector3.MoveTowards((b + c) / 2, center, 0.45f), Vector3.MoveTowards((c + a) / 2, center, 0.45f) })
                {
                    var key = new Vector3Int(Mathf.RoundToInt(point.x / 0.35f), Mathf.RoundToInt(point.y / 0.25f), Mathf.RoundToInt(point.z / 0.35f));
                    NavMeshHit sampled; RaycastHit floor;
                    if (!occupied.Add(key) || !NavMesh.SamplePosition(point, out sampled, 0.2f, NavMesh.AllAreas) ||
                        !Physics.Raycast(sampled.position + Vector3.up * 0.2f, Vector3.down, out floor, 0.4f, ~0, QueryTriggerInteraction.Ignore) ||
                        !floor.collider.transform.IsChildOf(root) || NativeNavigationGeometry.IsDynamicNavigationBody(floor.collider) || floor.normal.y < 0.7f) continue;
                    anchors.Add(floor.point);
                }
            }
            // Spatial buckets keep the native path queries local and bounded.
            var buckets = new Dictionary<Vector2Int, List<Vector3>>();
            foreach (var point in anchors)
            {
                var key = new Vector2Int(Mathf.FloorToInt(point.x / 3), Mathf.FloorToInt(point.z / 3));
                List<Vector3> values;
                if (!buckets.TryGetValue(key, out values)) buckets.Add(key, values = new List<Vector3>());
                values.Add(point);
            }
            int pairs = 0, disconnected = 0;
            var refusals = new Dictionary<string, int>();
            var crowd = actor.GetComponent<CrowdMotion>();
            foreach (var start in anchors)
            {
                var bucket = new Vector2Int(Mathf.FloorToInt(start.x / 3), Mathf.FloorToInt(start.z / 3));
                for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                {
                    List<Vector3> ends;
                    if (!buckets.TryGetValue(bucket + new Vector2Int(x, z), out ends)) continue;
                    foreach (var end in ends)
                    {
                        var delta = end - start; float horizontal = new Vector2(delta.x, delta.z).magnitude;
                        if (horizontal < 0.6f || horizontal > 2.5f || delta.y < -0.5f || delta.y > 1.2f) continue;
                        NpcJumpTrajectory possible;
                        if (!NpcJumpTrajectory.TryCreate(horizontal, delta.y, 6.75, 20, actor.WalkingSpeed, out possible)) continue;
                        if (++pairs % 64 == 0) yield return null;
                        if (pairs > 20000) { ReportJumpSurvey(anchors.Count, pairs, disconnected, refusals, false); yield break; }
                        var path = new NavMeshPath();
                        if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) continue;
                        disconnected++;
                        crowd.RestorePlacement(start); Physics.SyncTransforms();
                        Vector3 landing; NpcJumpTrajectory trajectory; string refusal;
                        if (jump.TryPrepare(end, actor.WalkingSpeed, null, out landing, out trajectory, out refusal))
                        {
                            ReportJumpSurvey(anchors.Count, pairs, disconnected, refusals, true);
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_JUMP_SURVEY_FLOORS: localStart=" + root.InverseTransformPoint(start).ToString("F4") +
                                "; localEnd=" + root.InverseTransformPoint(landing).ToString("F4") + "; actualStaticFloors=true; setupBodyPlacement=true; generatedGeometry=false");
                            found(start, landing); yield break;
                        }
                        int count; refusals.TryGetValue(refusal, out count); refusals[refusal] = count + 1;
                    }
                }
            }
            ReportJumpSurvey(anchors.Count, pairs, disconnected, refusals, false);
        }

        private static void ReportJumpSurvey(int anchors, int pairs, int disconnected, Dictionary<string, int> refusals, bool found)
        {
            var reasons = new List<string>();
            foreach (var pair in refusals) reasons.Add(pair.Key + ":" + pair.Value);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_JUMP_SURVEY: anchors=" + anchors + "; pairs=" + pairs +
                "; disconnected=" + disconnected + "; prepared=" + found + "; refusals=" + string.Join(",", reasons.ToArray()));
        }
    }
}
