using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;
using LivingCities.Core;

namespace LivingCities.DFU
{
    // Reachable standing places and stair routes, surveyed from the actual exit.
    public sealed class InteriorGeometry
    {
        public readonly List<Vector3> LocalAnchors = new List<Vector3>();
        public Vector3? EntryGround { get; private set; }
        private readonly DaggerfallInterior interior;
        private readonly float Radius;
        // Reserve the native controller's 0.5 m step band below the body sweep.
        private float BodyBase { get { return 0.5f + Radius + 0.01f; } }
        private readonly float bodyTop;
        private const float GridStep = 0.2f;
        private readonly Dictionary<Vector3Int, Vector3> floors = new Dictionary<Vector3Int, Vector3>();
        private readonly Dictionary<Vector3Int, Vector3Int> parents = new Dictionary<Vector3Int, Vector3Int>();
        private const float MaximumRise = 0.5f;
        private readonly Dictionary<Vector3Int, Vector3> stepCorners = new Dictionary<Vector3Int, Vector3>();
        public InteriorGeometry(DaggerfallInterior interior,float bodyRadius=0.24f,float top=1.65f)
        {
            this.interior = interior;
            Radius=bodyRadius; bodyTop=top;
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
            floors.Add(Vector3Int.zero, seed);
            var queue = new Queue<Vector3>(); queue.Enqueue(seed);
            var seen = new HashSet<Vector3Int>(); seen.Add(Vector3Int.zero);
            var occupants = interior.GetComponentsInChildren<StaticNPC>();
            int visited = 0;
            while (queue.Count > 0 && visited++ < 12000)
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
                    Vector3 floor;
                    if (!Floor(candidate, 0.55f, 1.1f, out floor) || Mathf.Abs(floor.y - point.y) > MaximumRise + 0.001f || !Clear(floor)) continue;
                    var cell = Cell(floor, seed);
                    if (seen.Contains(cell) || Mathf.Abs(cell.x) > 100 || Mathf.Abs(cell.z) > 100 || Mathf.Abs(cell.y) > 150) continue;
                    Vector3 corner = Vector3.zero; bool stepped = false;
                    bool reachable = false;
                    if (Mathf.Abs(floor.y-point.y) > 0.01f)
                    {
                        // A native-sized step needs vertical clearance before crossing
                        // the riser. Save the corner so the actor follows that checked path.
                        corner = floor.y > point.y ? new Vector3(point.x,floor.y,point.z) : new Vector3(floor.x,point.y,floor.z);
                        stepped = reachable = Clear(corner) && SweepsClear(point,corner) && SweepsClear(corner,floor);
                    }
                    else reachable = SweepsClear(point, floor);
                    if (reachable)
                    {
                        seen.Add(cell); floors.Add(cell, floor);
                        parents.Add(cell, Cell(point, seed));
                        if (stepped) stepCorners.Add(cell, corner);
                        queue.Enqueue(floor);
                    }
                }
            }
            if (queue.Count > 0) Debug.LogWarning("LIVING_CITIES_INTERIOR_SURVEY_LIMIT: building=" + interior.EntryDoor.buildingKey + "; nodes=" + floors.Count);
        }
        public List<GuardPoint3> RouteToExit(Vector3 ground)
        {
            var route = new List<GuardPoint3>();
            if (!EntryGround.HasValue || !Clear(ground)) return route;
            float nearest = 2.25f; Vector3Int cell = Vector3Int.zero; bool found = false;
            foreach (var pair in floors)
            {
                var delta = pair.Value - ground;
                if (delta.sqrMagnitude > nearest || Mathf.Abs(delta.y) > 0.3f) continue;
                bool blocked = false;
                foreach (var hit in Physics.CapsuleCastAll(ground + Vector3.up * BodyBase, ground + Vector3.up * bodyTop,
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
                if (cell == Vector3Int.zero) break;
                Vector3 corner;
                if (stepCorners.TryGetValue(cell, out corner)) route.Add(LocalPoint(corner));
                cell = parents[cell];
            }
            return route;
        }
        public bool StandBesideBed(BedAnchor bed, out Vector3 ground)
        {
            Vector3 center = interior.transform.TransformPoint(bed.LocalCenter);
            Vector3 side = interior.transform.TransformDirection(bed.LocalSideDirection);
            Vector3 head = interior.transform.TransformDirection(bed.LocalHeadDirection);
            foreach (float sign in new[] { 1f,-1f })
                foreach (float along in new[] { 0f,-0.25f,0.25f })
                {
                    Vector3 candidate = center+side*sign*(bed.Width*0.5f+Radius+0.15f)+head*along*bed.Length;
                    if (Floor(candidate,0.1f,1.6f,out ground) && ground.y < center.y-0.1f && Clear(ground)) return true;
                }
            foreach (float sign in new[] { -1f,1f })
            {
                Vector3 candidate = center+head*sign*(bed.Length*0.5f+Radius+0.15f);
                if (Floor(candidate,0.1f,1.6f,out ground) && ground.y < center.y-0.1f && Clear(ground)) return true;
            }
            ground=Vector3.zero; return false;
        }
        public List<GuardPoint3> RouteBetween(Vector3 localFrom,Vector3 localTo)
        {
            var a=RouteToExit(interior.transform.TransformPoint(localFrom));
            var b=RouteToExit(interior.transform.TransformPoint(localTo));
            if(a.Count<2 || b.Count<2) return new List<GuardPoint3>();
            int ai=a.Count-1,bi=b.Count-1;
            while(ai>0 && bi>0 && Vector3.Distance(Point(a[ai-1]),Point(b[bi-1]))<0.001f) { ai--; bi--; }
            var route=new List<GuardPoint3>();
            for(int i=0;i<=ai;i++) route.Add(a[i]);
            for(int i=bi-1;i>=0;i--) route.Add(b[i]);
            if(route.Count==1) route.Add(new GuardPoint3 { X=route[0].X,Y=route[0].Y,Z=route[0].Z });
            return route;
        }
        private static Vector3 Point(GuardPoint3 point) { return new Vector3((float)point.X,(float)point.Y,(float)point.Z); }
        private GuardPoint3 LocalPoint(Vector3 world)
        { var p = interior.transform.InverseTransformPoint(world); return new GuardPoint3 { X = p.x, Y = p.y, Z = p.z }; }
        private static Vector3Int Cell(Vector3 point, Vector3 seed)
        { return new Vector3Int(Mathf.RoundToInt((point.x-seed.x)/GridStep), Mathf.RoundToInt((point.y-seed.y)/0.2f), Mathf.RoundToInt((point.z-seed.z)/GridStep)); }
        private bool SweepsClear(Vector3 from, Vector3 to)
        {
            Vector3 delta = to-from;
            if (delta.sqrMagnitude < 0.000001f) return true;
            foreach (var hit in Physics.CapsuleCastAll(from+Vector3.up*BodyBase,from+Vector3.up*bodyTop,
                Radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if (Blocker(hit.collider)) return false;
            return true;
        }
        private bool Floor(Vector3 point, float up, float depth, out Vector3 floor)
        {
            RaycastHit hit;
            if (Physics.Raycast(point + Vector3.up * up, Vector3.down, out hit, depth, ~0, QueryTriggerInteraction.Ignore) &&
                hit.collider.transform.IsChildOf(interior.transform) && hit.normal.y > 0.65f)
            { floor = hit.point; return true; }
            floor = Vector3.zero; return false;
        }
        private bool Clear(Vector3 ground)
        {
            foreach (var collider in Physics.OverlapCapsule(ground + Vector3.up * BodyBase, ground + Vector3.up * bodyTop,
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
            var hits = Physics.SphereCastAll(from + Vector3.up, 0.24f, delta.normalized,
                Mathf.Min(2, delta.magnitude + 0.24f), ~0, QueryTriggerInteraction.Ignore);
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
