using System;
using System.Collections.Generic;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Scene adapter only. Persistent identities and route evaluation stay in Core.
    public sealed partial class TownGeometry : IWalkEdgeGrid
    {
        public sealed class Entrance
        {
            public int BuildingKey;
            public GridCell Cell;
            public Vector3 LocalDoor;
            internal bool NativeStreetConnected;
        }
        private readonly CityNavigation navigation;
        private readonly Transform origin;
        private readonly Terrain terrain;
        private readonly BuildingDirectory directory;
        public readonly SortedDictionary<int, Entrance> Entrances = new SortedDictionary<int, Entrance>();
        public int RejectedDoors { get; private set; }
        public int Width { get { return navigation.NavGridWidth; } }
        public int Height { get { return navigation.NavGridHeight; } }
        public bool UsesNativeNavigation {get{return NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready &&
            NativeNavigationWorld.Current.Scene=="city-"+directory.MapID;}}
        public TownGeometry(BuildingDirectory directory, CityNavigation navigation, bool mapEntrances = true)
        {
            if (directory == null || navigation == null) throw new ArgumentNullException();
            this.navigation = navigation;
            this.directory = directory;
            origin = directory.transform;
            var location = directory.GetComponent<DaggerfallLocation>();
            var terrainObject = GameManager.Instance.StreamingWorld.GetTerrainFromPixel(location.Summary.MapPixelX, location.Summary.MapPixelY);
            terrain = terrainObject ? terrainObject.GetComponent<Terrain>() : null;
            if (!mapEntrances) return;
            var operation=ReadEntranceSteps();
            while(operation.MoveNext()) {}
        }
        public System.Collections.IEnumerator ReadEntranceSteps()
        {
            var found=new SortedDictionary<int,Entrance>();
            Vector3 street=Vector3.zero;bool hasStreet=false;
            if(UsesNativeNavigation)
                foreach(var gate in origin.GetComponentsInChildren<DaggerfallCityGate>())
                {
                    NativeCityGateNavigation.Aperture aperture;
                    if(!NativeCityGateNavigation.TryAperture(gate,SceneCell(new GridCell(Width/2,Height/2)),out aperture))continue;
                    var local=origin.InverseTransformPoint(aperture.Inner);NavMeshHit sample;
                    if(!NavMesh.SamplePosition(ScenePoint(new RoutePoint(local.x,local.z)),out sample,0.4f,NavMesh.AllAreas))continue;
                    street=sample.position;hasStreet=true;break;
                }
            int work=0;
            foreach (var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if (owner.Doors == null) continue;
                for (int i = 0; i < owner.Doors.Length; i++)
                {
                    if(++work%8==0) yield return null;
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
                    bool connected=false;
                    if(hasStreet)
                    {
                        NavMeshHit end;var path=new NavMeshPath();
                        connected=NavMesh.SamplePosition(ScenePoint(new RoutePoint(local.x,local.z)),out end,0.4f,NavMesh.AllAreas) &&
                            NavMesh.CalculatePath(street,end.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete;
                    }
                    Entrance existing;
                    // Prefer the actual exterior-connected door over disconnected
                    // door islands, retaining stable ordering and fallback elsewhere.
                    if (found.TryGetValue(key, out existing) &&
                        (existing.NativeStreetConnected && !connected || existing.NativeStreetConnected==connected && Compare(local,existing.LocalDoor)>=0))continue;
                    found[key] = new Entrance { BuildingKey = key, Cell = cell, LocalDoor = local,NativeStreetConnected=connected };
                }
            }
            Entrances.Clear();
            foreach(var pair in found) Entrances.Add(pair.Key,pair.Value);
        }
        public int Cost(int x, int z)
        {
            if(x<0||z<0||x>=Width||z>=Height)return 0;
            if(sharedGridCosts==null)sharedGridCosts=new byte[Width*Height];
            int key=z*Width+x,stored=sharedGridCosts[key];
            if(stored!=0)return stored-1;
            int cost=ReadCost(x,z);sharedGridCosts[key]=(byte)(cost+1);return cost;
        }
        private int ReadCost(int x,int z)
        {
            int weight = navigation.GetNavGridWeightLocal(x, z);
            if(weight<=0) return 0;
            var point=SceneCell(new GridCell(x,z));
            if(FurnitureSeatCatalog.Blocks(point,point,CrowdMotion.BodyRadius)) return 0;
            // DFU weights are preference (higher is better); A* consumes cost.
            return weight <= 0 ? 0 : Math.Max(1, 16 - weight);
        }
        public bool CanStep(GridCell from,GridCell to)
        {
            if(sharedHorizontalEdges==null){sharedHorizontalEdges=new byte[Width*Height];sharedVerticalEdges=new byte[Width*Height];}
            int key=Math.Min(from.Z,to.Z)*Width+Math.Min(from.X,to.X);
            var edges=from.Z==to.Z?sharedHorizontalEdges:sharedVerticalEdges;
            if(edges[key]!=0)return edges[key]==1;
            bool clear=!FurnitureSeatCatalog.Blocks(SceneCell(from),SceneCell(to),CrowdMotion.BodyRadius);
            edges[key]=(byte)(clear?1:2);return clear;
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
            List<SavedRoutePoint> native;
            if(TryNativeRoute(from.LocalDoor,to.LocalDoor,out native))
            {
                var route=new List<RoutePoint>();
                foreach(var point in native)route.Add(new RoutePoint(point.X,point.Z));
                return route;
            }
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
            // Sphere casts do not detect blockers already overlapping their origin.
            // Check every waypoint, including the terminal doorway, before segments.
            foreach (var point in points)
            {
                var ground=ScenePoint(point);
                if(FurnitureSeatCatalog.Blocks(ground,ground,CrowdMotion.BodyRadius)) return new List<RoutePoint>();
                foreach (float height in new[] { 0.4f, 1.0f })
                    foreach (var collider in Physics.OverlapSphere(ScenePoint(point) + Vector3.up * height,
                        0.2f, ~0, QueryTriggerInteraction.Ignore))
                        if (IsStaticBlocker(collider)) return new List<RoutePoint>();
            }
            for (int i = 1; i < points.Count; i++)
            {
                Vector3 a = ScenePoint(points[i - 1]), b = ScenePoint(points[i]);
                if(FurnitureSeatCatalog.Blocks(a,b,CrowdMotion.BodyRadius)) return new List<RoutePoint>();
                Vector3 delta = b - a;
                float distance = delta.magnitude;
                if (distance < 0.001f) continue;
                foreach (float height in new[] { 0.4f, 1.0f })
                    foreach (var hit in Physics.SphereCastAll(a + Vector3.up * height, 0.2f, delta.normalized,
                        distance, ~0, QueryTriggerInteraction.Ignore))
                        if (IsStaticBlocker(hit.collider)) return new List<RoutePoint>();
            }
            var saved=new List<SavedRoutePoint>();
            foreach(var point in points) saved.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
            // Reactive synchronous callers cannot drain a full lane survey or
            // thousands of physics callbacks. Daily plans use ReadRouteSteps for
            // the complete sliced smoothing, curves and corridor preparation.
            var finish=OutdoorRouteSmoother.Begin(saved,OutdoorShortcutClear,8);
            while(!finish.IsComplete) finish.Step(2);
            points.Clear();foreach(var point in finish.Points) points.Add(new RoutePoint(point.X,point.Z));
            return points;
        }
        public List<SavedRoutePoint> RouteBetweenPoints(SavedRoutePoint from, SavedRoutePoint to)
        {
            Vector3 a = ScenePoint(new RoutePoint(from.X, from.Z));
            Vector3 b = ScenePoint(new RoutePoint(to.X, to.Z));
            List<SavedRoutePoint> native;
            if(TryNativeRoute(origin.InverseTransformPoint(a),origin.InverseTransformPoint(b),out native))return native;
            GridCell start, end;
            var result = new List<SavedRoutePoint>();
            if (!FindEntranceCell(a, out start) || !FindEntranceCell(b, out end)) return result;
            var points = RouteBetween(new Entrance { Cell = start, LocalDoor = origin.InverseTransformPoint(a) },
                new Entrance { Cell = end, LocalDoor = origin.InverseTransformPoint(b) });
            foreach (var point in points) result.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            return result;
        }
        private bool TryNativeRoute(Vector3 localFrom,Vector3 localTo,out List<SavedRoutePoint> result)
        {
            result=null;
            if(!UsesNativeNavigation)return false;
            var from=ScenePoint(new RoutePoint(localFrom.x,localFrom.z));
            var to=ScenePoint(new RoutePoint(localTo.x,localTo.z));
            NavMeshHit start,end;
            result=new List<SavedRoutePoint>();
            if(!NavMesh.SamplePosition(from,out start,0.4f,NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to,out end,0.4f,NavMesh.AllAreas))return true;
            var path=new NavMeshPath();
            if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) ||
                path.status!=NavMeshPathStatus.PathComplete)return true;
            var corners=path.corners;
            if(!NativeNavigationWorld.Current.ClearCityGatePath(corners))return true;
            // Store the same route used by the native mover. Grid detours would
            // otherwise inflate walking times and leave early arrivals at doors.
            result=new List<SavedRoutePoint>{new SavedRoutePoint {X=localFrom.x,Z=localFrom.z}};
            foreach(var corner in corners)
            {
                var local=origin.InverseTransformPoint(corner);var last=result[result.Count-1];
                if(Math.Abs(last.X-local.x)+Math.Abs(last.Z-local.z)>0.01)
                    result.Add(new SavedRoutePoint {X=local.x,Z=local.z});
            }
            result.Add(new SavedRoutePoint {X=localTo.x,Z=localTo.z});
            return true;
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
                if(FurnitureSeatCatalog.Blocks(start-Vector3.up,target-Vector3.up,CrowdMotion.BodyRadius)) continue;
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
