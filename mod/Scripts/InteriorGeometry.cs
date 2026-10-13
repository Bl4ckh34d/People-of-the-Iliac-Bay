using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;

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
        public InteriorGeometry(DaggerfallInterior interior)
        {
            this.interior = interior;
            var markers = new List<Vector3>(interior.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Enter));
            markers.Sort((a, b) => { int c = a.x.CompareTo(b.x); return c != 0 ? c : a.z.CompareTo(b.z); });
            Vector3 seed = Vector3.zero; bool found = false;
            foreach (var marker in markers)
            {
                for (int radius = 0; radius <= 2 && !found; radius++)
                    for (int z = -radius; z <= radius && !found; z++)
                        for (int x = -radius; x <= radius && !found; x++)
                            found = Floor(marker + new Vector3(x * 0.8f, 0, z * 0.8f), 2, 5, out seed) && Clear(seed);
                if (found) break;
            }
            if (!found) return;
            EntryGround = seed;
            var queue = new Queue<Vector3>(); queue.Enqueue(seed);
            var seen = new HashSet<Vector2Int>(); seen.Add(Vector2Int.zero);
            var occupants = interior.GetComponentsInChildren<StaticNPC>();
            int visited = 0;
            while (queue.Count > 0 && visited++ < 900 && LocalAnchors.Count < 80)
            {
                Vector3 point = queue.Dequeue();
                bool suitable = true;
                foreach (var marker in markers)
                    if (Vector3.Distance(point, marker) < 2) { suitable = false; break; }
                foreach (var npc in occupants)
                    if (Vector3.Distance(point + Vector3.up, npc.transform.position) < 1.2f) { suitable = false; break; }
                foreach (var anchor in LocalAnchors)
                    if (Vector3.Distance(point, interior.transform.TransformPoint(anchor)) < 1.2f) { suitable = false; break; }
                if (suitable) LocalAnchors.Add(interior.transform.InverseTransformPoint(point));
                foreach (var direction in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                {
                    Vector3 candidate = point + direction * 0.8f;
                    var cell = new Vector2Int(Mathf.RoundToInt((candidate.x - seed.x) / 0.8f), Mathf.RoundToInt((candidate.z - seed.z) / 0.8f));
                    if (!seen.Add(cell) || Mathf.Abs(cell.x) > 25 || Mathf.Abs(cell.y) > 25) continue;
                    Vector3 floor;
                    if (!Floor(candidate, 0.5f, 1, out floor) || Mathf.Abs(floor.y - point.y) > 0.3f || !Clear(floor)) continue;
                    Vector3 delta = floor - point; bool blocked = false;
                    foreach (var hit in Physics.CapsuleCastAll(point + Vector3.up * 0.3f, point + Vector3.up * 1.65f,
                        Radius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                        if (Blocker(hit.collider)) { blocked = true; break; }
                    if (!blocked) queue.Enqueue(floor);
                }
            }
        }
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
            return collider.GetComponentInParent<DaggerfallEntityBehaviour>() == null &&
                collider.GetComponentInParent<MobilePersonNPC>() == null && collider.GetComponentInParent<PlayerMotor>() == null;
        }
    }
}
