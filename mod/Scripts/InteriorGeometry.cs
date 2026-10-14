using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;
using LivingCities.Core;

namespace LivingCities.DFU
{
    // Conservative reachable standing places for the first interior-presence slice.
    // Furniture activities, stairs and sleeping anchors are separate later adapters.
    public sealed class InteriorGeometry
    {
        public readonly List<Vector3> LocalAnchors = new List<Vector3>();
        public Vector3? EntryGround { get; private set; }
        private readonly DaggerfallInterior interior;
        private const float Radius = 0.24f;
        private const float GridStep = 0.4f;
        private readonly Dictionary<Vector2Int, Vector3> floors = new Dictionary<Vector2Int, Vector3>();
        private readonly Dictionary<Vector2Int, Vector2Int> parents = new Dictionary<Vector2Int, Vector2Int>();
        public InteriorGeometry(DaggerfallInterior interior)
        {
            this.interior = interior;
            var markers = new List<Vector3>(interior.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Enter));
            markers.Sort((a, b) => { int c = a.x.CompareTo(b.x); return c != 0 ? c : a.z.CompareTo(b.z); });
            // Native enter markers can sit on an upper storey. Use the actual
            // interior exit door first, as PlayerEnterExit does for player landing.
            Vector3 door, normal;
            if (interior.FindClosestInteriorDoor(DaggerfallStaticDoors.GetDoorPosition(interior.EntryDoor), out door, out normal))
                markers.Insert(0, door + normal * 0.8f);
            Vector3 seed = Vector3.zero; bool found = false;
            foreach (var marker in markers)
            {
                for (int radius = 0; radius <= 2 && !found; radius++)
                    for (int z = -radius; z <= radius && !found; z++)
                        for (int x = -radius; x <= radius && !found; x++)
                            // Enter markers identify the doorway's floor. Starting the
                            // ray two metres higher can select the attic above this room.
                            found = Floor(marker + new Vector3(x * 0.8f, 0, z * 0.8f), 0.2f, 5, out seed) && Clear(seed);
                if (found) break;
            }
            if (!found) return;
            EntryGround = seed;
            floors.Add(Vector2Int.zero, seed);
            var queue = new Queue<Vector3>(); queue.Enqueue(seed);
            var seen = new HashSet<Vector2Int>(); seen.Add(Vector2Int.zero);
            var occupants = interior.GetComponentsInChildren<StaticNPC>();
            int visited = 0;
            while (queue.Count > 0 && visited++ < 3600)
            {
                Vector3 point = queue.Dequeue();
                bool suitable = true;
                foreach (var marker in markers)
                    if (Vector3.Distance(point, marker) < 2) { suitable = false; break; }
                foreach (var npc in occupants)
                    if (Vector3.Distance(point + Vector3.up, npc.transform.position) < 1.2f) { suitable = false; break; }
                foreach (var anchor in LocalAnchors)
                    if (Vector3.Distance(point, interior.transform.TransformPoint(anchor)) < 1.2f) { suitable = false; break; }
                if (suitable && LocalAnchors.Count < 80) LocalAnchors.Add(interior.transform.InverseTransformPoint(point));
                foreach (var direction in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                {
                    Vector3 candidate = point + direction * GridStep;
                    var cell = new Vector2Int(Mathf.RoundToInt((candidate.x - seed.x) / GridStep), Mathf.RoundToInt((candidate.z - seed.z) / GridStep));
                    if (seen.Contains(cell) || Mathf.Abs(cell.x) > 50 || Mathf.Abs(cell.y) > 50) continue;
                    Vector3 floor;
                    if (!Floor(candidate, 0.5f, 1, out floor) || Mathf.Abs(floor.y - point.y) > 0.3f || !Clear(floor)) continue;
                    Vector3 delta = floor - point; bool blocked = false;
                    foreach (var hit in Physics.CapsuleCastAll(point + Vector3.up * 0.3f, point + Vector3.up * 1.65f,
                        Radius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                        if (Blocker(hit.collider)) { blocked = true; break; }
                    if (!blocked)
                    {
                        seen.Add(cell); floors.Add(cell, floor);
                        parents.Add(cell, new Vector2Int(Mathf.RoundToInt((point.x - seed.x) / GridStep), Mathf.RoundToInt((point.z - seed.z) / GridStep)));
                        queue.Enqueue(floor);
                    }
                }
            }
        }
        public List<GuardPoint3> RouteToExit(Vector3 ground)
        {
            var route = new List<GuardPoint3>();
            if (!EntryGround.HasValue || !Clear(ground)) return route;
            float nearest = 2.25f; Vector2Int cell = Vector2Int.zero; bool found = false;
            foreach (var pair in floors)
            {
                var delta = pair.Value - ground;
                if (delta.sqrMagnitude > nearest || Mathf.Abs(delta.y) > 0.3f) continue;
                bool blocked = false;
                foreach (var hit in Physics.CapsuleCastAll(ground + Vector3.up * 0.3f, ground + Vector3.up * 1.65f,
                    Radius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    if (Blocker(hit.collider)) { blocked = true; break; }
                // Check support between an arbitrary resident position and the surveyed graph.
                for (float t = 0; !blocked && t <= 1; t += 0.2f)
                {
                    Vector3 sample = Vector3.Lerp(ground, pair.Value, t), floor;
                    if (!Floor(sample, 0.4f, 0.8f, out floor) || Mathf.Abs(floor.y - sample.y) > 0.3f) blocked = true;
                }
                if (blocked) continue;
                nearest = delta.sqrMagnitude; cell = pair.Key; found = true;
            }
            if (!found) return route;
            route.Add(LocalPoint(ground));
            while (true)
            {
                route.Add(LocalPoint(floors[cell]));
                if (cell == Vector2Int.zero) break;
                cell = parents[cell];
            }
            return route;
        }
        private GuardPoint3 LocalPoint(Vector3 world)
        { var p = interior.transform.InverseTransformPoint(world); return new GuardPoint3 { X = p.x, Y = p.y, Z = p.z }; }
        private bool Floor(Vector3 point, float up, float depth, out Vector3 floor)
        {
            RaycastHit hit;
            if (Physics.Raycast(point + Vector3.up * up, Vector3.down, out hit, depth, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.transform.IsChildOf(interior.transform) && hit.normal.y > 0.9f)
            { floor = hit.point; return true; }
            floor = Vector3.zero; return false;
        }
        private static bool Clear(Vector3 ground)
        {
            foreach (var collider in Physics.OverlapCapsule(ground + Vector3.up * 0.3f, ground + Vector3.up * 1.65f,
                Radius, ~0, QueryTriggerInteraction.Ignore))
                if (Blocker(collider)) return false;
            return true;
        }
        private static bool Blocker(Collider collider)
        {
            var door = collider.GetComponentInParent<DaggerfallActionDoor>();
            if (door && !door.IsLocked && !door.GetComponent<DaggerfallAction>()) return false;
            return collider.GetComponentInParent<DaggerfallEntityBehaviour>() == null &&
                collider.GetComponentInParent<StaticNPC>() == null &&
                collider.GetComponentInParent<MobilePersonNPC>() == null && collider.GetComponentInParent<PlayerMotor>() == null;
        }
        public static void OpenOrdinaryDoors(Vector3 from, Vector3 to)
        {
            if (GameManager.IsGamePaused) return;
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.000001f) return;
            var hits = Physics.SphereCastAll(from + Vector3.up, Radius, delta.normalized,
                Mathf.Min(2, delta.magnitude + Radius), ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var door = hit.collider.GetComponentInParent<DaggerfallActionDoor>();
                if (door && !door.IsLocked && !door.IsOpen && !door.GetComponent<DaggerfallAction>())
                    door.SetOpen(true, true);
                else if (Blocker(hit.collider)) break;
            }
        }
    }
}
