using System;
using System.Collections.Generic;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Scene adapter only. Persistent identities and route evaluation stay in Core.
    public sealed class TownGeometry : IWalkGrid
    {
        public sealed class Entrance
        {
            public int BuildingKey;
            public GridCell Cell;
            public Vector3 LocalDoor;
        }
        private readonly CityNavigation navigation;
        private readonly Transform origin;
        private readonly Terrain terrain;
        public readonly SortedDictionary<int, Entrance> Entrances = new SortedDictionary<int, Entrance>();
        public int RejectedDoors { get; private set; }
        public int Width { get { return navigation.NavGridWidth; } }
        public int Height { get { return navigation.NavGridHeight; } }
        public TownGeometry(BuildingDirectory directory, CityNavigation navigation, bool mapEntrances = true)
        {
            if (directory == null || navigation == null) throw new ArgumentNullException();
            this.navigation = navigation;
            origin = directory.transform;
            var location = directory.GetComponent<DaggerfallLocation>();
            var terrainObject = GameManager.Instance.StreamingWorld.GetTerrainFromPixel(location.Summary.MapPixelX, location.Summary.MapPixelY);
            terrain = terrainObject ? terrainObject.GetComponent<Terrain>() : null;
            if (!mapEntrances) return;
            foreach (var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if (owner.Doors == null) continue;
                for (int i = 0; i < owner.Doors.Length; i++)
                {
                    if (owner.Doors[i].doorType != DoorTypes.Building) continue;
                    int key = owner.Doors[i].buildingKey;
                    BuildingSummary building;
                    if (!directory.GetBuildingSummary(key, out building)) continue;
                    Vector3 normal = owner.GetDoorNormal(i); normal.y = 0;
                    if (normal.sqrMagnitude < 0.01f) { RejectedDoors++; continue; }
                    Vector3 door = owner.GetDoorPosition(i) + normal.normalized * 0.6f;
                    GridCell cell;
                    if (!FindEntranceCell(door, out cell)) { RejectedDoors++; continue; }
                    Vector3 local = origin.InverseTransformPoint(door);
                    Entrance existing;
                    // Multiple entrances: stable geometric ordering, never hierarchy order.
                    if (Entrances.TryGetValue(key, out existing) && Compare(local, existing.LocalDoor) >= 0) continue;
                    Entrances[key] = new Entrance { BuildingKey = key, Cell = cell, LocalDoor = local };
                }
            }
        }
        public int Cost(int x, int z)
        {
            int weight = navigation.GetNavGridWeightLocal(x, z);
            // DFU weights are preference (higher is better); A* consumes cost.
            return weight <= 0 ? 0 : Math.Max(1, 16 - weight);
        }
        public Vector3 SceneCell(GridCell cell)
        { return navigation.WorldToScenePosition(navigation.NavGridToWorldPosition(new DFPosition(cell.X, cell.Z))); }
        public Vector3 SceneDoor(Entrance entrance) { return origin.TransformPoint(entrance.LocalDoor); }
        public Vector3 ScenePoint(RoutePoint point)
        {
            Vector3 result = origin.TransformPoint(new Vector3((float)point.X, 0, (float)point.Z));
            if (terrain) result.y = terrain.SampleHeight(result) + GameManager.Instance.StreamingWorld.WorldCompensation.y;
            return result;
        }
        public List<RoutePoint> RouteBetween(Entrance from, Entrance to)
        {
            var cells = GridPathfinder.Find(this, from.Cell, to.Cell, 30000);
            var points = new List<RoutePoint>();
            if (cells.Count == 0) return points;
            points.Add(new RoutePoint(from.LocalDoor.x, from.LocalDoor.z));
            foreach (var cell in cells)
            {
                var local = origin.InverseTransformPoint(SceneCell(cell));
                points.Add(new RoutePoint(local.x, local.z));
            }
            points.Add(new RoutePoint(to.LocalDoor.x, to.LocalDoor.z));
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 a = ScenePoint(points[i - 1]), b = ScenePoint(points[i]);
                Vector3 delta = b - a;
                float distance = delta.magnitude;
                if (distance < 0.001f) continue;
                foreach (float height in new[] { 0.4f, 1.0f })
                    foreach (var hit in Physics.SphereCastAll(a + Vector3.up * height, 0.2f, delta.normalized,
                        distance, ~0, QueryTriggerInteraction.Ignore))
                        if (IsStaticBlocker(hit.collider)) return new List<RoutePoint>();
            }
            return points;
        }
        private bool FindEntranceCell(Vector3 door, out GridCell result)
        {
            var near = navigation.WorldToNavGridPosition(navigation.SceneToWorldPosition(door));
            float best = float.PositiveInfinity;
            result = default(GridCell);
            for (int z = Math.Max(0, near.Y - 8); z <= Math.Min(Height - 1, near.Y + 8); z++)
            for (int x = Math.Max(0, near.X - 8); x <= Math.Min(Width - 1, near.X + 8); x++)
            {
                if (Cost(x, z) == 0) continue;
                var cell = new GridCell(x, z);
                Vector3 target = SceneCell(cell) + Vector3.up;
                Vector3 start = new Vector3(door.x, target.y, door.z);
                // This first adapter handles ground-level entrances only. Do not snap
                // an upper-floor doorway vertically down onto an unrelated street.
                if (Math.Abs(door.y - target.y) > 1.5f) continue;
                float distance = Vector3.Distance(start, target);
                if (distance > 8 || distance >= best) continue;
                bool blocked = false;
                // SphereCast does not report a collider already overlapping its origin.
                foreach (var collider in Physics.OverlapSphere(start, 0.2f, ~0, QueryTriggerInteraction.Ignore))
                    if (IsStaticBlocker(collider)) { blocked = true; break; }
                if (blocked) continue;
                if (distance > 0.01f)
                    foreach (var hit in Physics.SphereCastAll(start, 0.2f, (target - start).normalized, distance, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (!IsStaticBlocker(hit.collider)) continue;
                        blocked = true; break;
                    }
                if (blocked) continue;
                best = distance; result = cell;
            }
            return !float.IsPositiveInfinity(best);
        }
        private static bool IsStaticBlocker(Collider collider)
        {
            return collider.GetComponentInParent<DaggerfallEntityBehaviour>() == null &&
                collider.GetComponentInParent<MobilePersonNPC>() == null &&
                collider.GetComponentInParent<PlayerMotor>() == null;
        }
        private static int Compare(Vector3 a, Vector3 b)
        {
            int order = a.x.CompareTo(b.x);
            if (order == 0) order = a.z.CompareTo(b.z);
            return order == 0 ? a.y.CompareTo(b.y) : order;
        }
    }
}
