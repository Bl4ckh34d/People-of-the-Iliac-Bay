using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Reachable standing places and stair routes, surveyed from the actual exit.
    public sealed class InteriorGeometry
    {
        public readonly List<Vector3> LocalAnchors = new List<Vector3>();
        public Vector3? EntryGround { get; private set; }
        public bool IsComplete { get; private set; }
        public List<Vector3> StandingNear(Vector3 localTarget,float range)
        {
            var candidates=new List<Vector3>();
            foreach(var floor in floors.Values)
            {
                var local=interior.transform.InverseTransformPoint(floor);
                if(Mathf.Abs(local.y-localTarget.y)<2 && (local-localTarget).sqrMagnitude<range*range) candidates.Add(local);
            }
            candidates.Sort((a,b)=>{
                int d=(a-localTarget).sqrMagnitude.CompareTo((b-localTarget).sqrMagnitude);
                if(d!=0) return d;d=a.x.CompareTo(b.x);return d!=0?d:a.z.CompareTo(b.z);
            });
            var result=new List<Vector3>();
            foreach(var candidate in candidates)
            {
                bool near=false;foreach(var chosen in result) if((chosen-candidate).sqrMagnitude<CrowdMotion.Separation*CrowdMotion.Separation) {near=true;break;}
                if(!near) result.Add(candidate);if(result.Count>=12) break;
            }
            return result;
        }
        private readonly System.Func<Vector3,Vector3,bool> boundary;
        private readonly DaggerfallInterior interior;
        private readonly float Radius;
        // Reserve the native controller's 0.5 m step band below the body sweep.
        private float BodyBase { get { return 0.5f + Radius + 0.01f; } }
        private readonly float bodyTop;
        private const float GridStep = 0.2f;
        private const int RouteSimplifyLookahead = 16;
        private const int RouteSimplifyChecks = 24;
        private const float RouteSimplifyDistance = 2.4f;
        private const float RouteSimplifyHeight = 0.35f;
        private readonly Dictionary<Vector3Int, Vector3> floors = new Dictionary<Vector3Int, Vector3>();
        private readonly Dictionary<Vector3Int, Vector3Int> parents = new Dictionary<Vector3Int, Vector3Int>();
        private sealed class FloorEdge { public Vector3Int To; public Vector3? Corner; }
        private readonly Dictionary<Vector3Int, List<FloorEdge>> floorEdges = new Dictionary<Vector3Int, List<FloorEdge>>();
        private const float MaximumRise = 0.5f;
        private readonly Dictionary<Vector3Int, Vector3> stepCorners = new Dictionary<Vector3Int, Vector3>();
        private System.Collections.IEnumerator survey;
        private readonly FurnitureSeatCatalog seating;
        public InteriorGeometry(DaggerfallInterior interior,float bodyRadius=0.24f,float top=1.65f,
            Vector3? surveySeed=null,System.Func<Vector3,Vector3,bool> boundary=null,bool sampleFloorOnly=false,bool deferSurvey=false)
        {
            this.interior = interior;
            seating=FurnitureSeatCatalog.ForInterior(interior);
            this.boundary=boundary;
            Radius=bodyRadius; bodyTop=top;
            var markers = new List<Vector3>(interior.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Enter));
            markers.Sort((a, b) => { int c = a.x.CompareTo(b.x); return c != 0 ? c : a.z.CompareTo(b.z); });
            // Native enter markers can sit on an upper storey. Use the actual
            // interior exit door first, as PlayerEnterExit does for player landing.
            Vector3 door, normal;
            if (!surveySeed.HasValue && interior.FindClosestInteriorDoor(DaggerfallStaticDoors.GetDoorPosition(interior.EntryDoor), out door, out normal))
                markers.Insert(0, door + normal * 0.8f);
            if(surveySeed.HasValue) { markers.Clear(); markers.Add(surveySeed.Value); }
            Vector3 seed = Vector3.zero; bool found = false;
            foreach (var marker in markers)
            {
                for (int radius = 0; radius <= (surveySeed.HasValue ? 0 : 2) && !found; radius++)
                    for (int z = -radius; z <= radius && !found; z++)
                        for (int x = -radius; x <= radius && !found; x++)
                            // Enter markers identify the doorway's floor. Starting the
                            // ray two metres higher can select the attic above this room.
                            found = Floor(marker + new Vector3(x * 0.8f, 0, z * 0.8f), 0.2f, surveySeed.HasValue ? 0.4f : 5, out seed) && Clear(seed);
            }
            if (!found) return;
            EntryGround = seed;
            // Bed partitioning only needs the entrance floor and direct floor probes.
            // It builds separate door-bounded room graphs below; do not flood the
            // entire inn once more merely to use StandBesideBed.
            if(sampleFloorOnly) return;
            floors.Add(Vector3Int.zero, seed);
            floorEdges.Add(Vector3Int.zero,new List<FloorEdge>());
            survey=Survey(markers,seed);
            if(!deferSurvey) while(AdvanceSurvey()) { }
        }
        public bool AdvanceSurvey()
        {
            if(survey==null) return false;
            if(survey.MoveNext()) return true;
            survey=null; return false;
        }
        private System.Collections.IEnumerator Survey(List<Vector3> markers,Vector3 seed)
        {
            var queue = new Queue<Vector3>(); queue.Enqueue(seed);
            var seen = new HashSet<Vector3Int>(); seen.Add(Vector3Int.zero);
            var occupants = interior.GetComponentsInChildren<StaticNPC>();
            int visited = 0;
            long slice=System.Diagnostics.Stopwatch.GetTimestamp();
            while (queue.Count > 0 && visited++ < 12000)
            {
                if((visited&31)==0 && (System.Diagnostics.Stopwatch.GetTimestamp()-slice)*1000d/System.Diagnostics.Stopwatch.Frequency>=2)
                { yield return null; slice=System.Diagnostics.Stopwatch.GetTimestamp(); }
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
                    if (cell==Cell(point,seed) || Mathf.Abs(cell.x) > 100 || Mathf.Abs(cell.z) > 100 || Mathf.Abs(cell.y) > 150) continue;
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
                    if (!reachable) continue;
                    if (!seen.Contains(cell))
                    {
                        seen.Add(cell); floors.Add(cell, floor); floorEdges.Add(cell,new List<FloorEdge>());
                        parents.Add(cell, Cell(point, seed));
                        if (stepped) stepCorners.Add(cell, corner);
                        queue.Enqueue(floor);
                    }
                    AddFloorEdge(Cell(point,seed),cell,stepped ? (Vector3?)corner : null);
                }
            }
            if (queue.Count > 0) Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_INTERIOR_SURVEY_LIMIT: building=" + interior.EntryDoor.buildingKey + "; nodes=" + floors.Count);
            IsComplete=queue.Count==0;
        }
        private void AddFloorEdge(Vector3Int from,Vector3Int to,Vector3? corner)
        {
            List<FloorEdge> edges;
            if(floorEdges.TryGetValue(from,out edges) && !ContainsEdge(edges,to)) edges.Add(new FloorEdge { To=to,Corner=corner });
            if(floorEdges.TryGetValue(to,out edges) && !ContainsEdge(edges,from)) edges.Add(new FloorEdge { To=from,Corner=corner });
        }
        private static bool ContainsEdge(List<FloorEdge> edges,Vector3Int target)
        { foreach(var edge in edges) if(edge.To==target) return true; return false; }
        private sealed class DetourCell : System.IComparable<DetourCell>
        {
            public Vector3Int Cell;
            public float Cost,Score;
            public int CompareTo(DetourCell other)
            {
                int order=Score.CompareTo(other.Score);
                if(order==0) order=Cost.CompareTo(other.Cost);
                if(order==0) order=Cell.x.CompareTo(other.Cell.x);
                if(order==0) order=Cell.y.CompareTo(other.Cell.y);
                if(order==0) order=Cell.z.CompareTo(other.Cell.z);
                return order;
            }
        }
        private float DetourDistance(Vector3Int cell,Dictionary<Vector3Int,int> goals)
        {
            float nearest=float.MaxValue;
            foreach(var goal in goals.Keys) nearest=Mathf.Min(nearest,Vector3.Distance(floors[cell],floors[goal]));
            return nearest;
        }
        public List<GuardPoint3> FindLocalDetour(Vector3 ground,IList<Vector3> crowd,float separation,
            IList<GuardPoint3> rejoinRoute,out int rejoinIndex,int maximumVisited=160,float minimumRejoinDistance=0.65f)
        {
            rejoinIndex=-1;var empty=new List<GuardPoint3>();
            if(!EntryGround.HasValue || !Clear(ground) || crowd==null || separation<=0 || maximumVisited<1 ||
                float.IsNaN(minimumRejoinDistance) || float.IsInfinity(minimumRejoinDistance) || minimumRejoinDistance<0 || rejoinRoute==null) return empty;
            var goals=new Dictionary<Vector3Int,int>();float along=0;Vector3 prior=ground;
            for(int i=0;i<rejoinRoute.Count && along<4.5f;i++)
            {
                var point=rejoinRoute[i];var world=interior.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                along+=Vector3.Distance(prior,world);prior=world;
                var delta=world-ground;delta.y=0;
                if(delta.sqrMagnitude<minimumRejoinDistance*minimumRejoinDistance || delta.sqrMagnitude>4.5f*4.5f || Mathf.Abs(world.y-ground.y)>0.5f) continue;
                var cell=Cell(world,EntryGround.Value);
                if(floors.ContainsKey(cell) && !goals.ContainsKey(cell)) goals.Add(cell,i);
            }
            if(goals.Count==0) return empty;
            var starts=new List<KeyValuePair<Vector3Int,float>>();
            var centerCell=Cell(ground,EntryGround.Value);
            for(int y=-2;y<=2;y++) for(int z=-6;z<=6;z++) for(int x=-6;x<=6;x++)
            {
                var cell=centerCell+new Vector3Int(x,y,z);Vector3 floor;
                if(!floors.TryGetValue(cell,out floor)) continue;
                var delta=floor-ground;
                if(delta.sqrMagnitude>1.21f || Mathf.Abs(delta.y)>0.3f) continue;
                starts.Add(new KeyValuePair<Vector3Int,float>(cell,delta.sqrMagnitude));
            }
            starts.Sort((a,b)=>a.Value.CompareTo(b.Value));
            int visited=0,startsChecked=0;
            foreach(var startCandidate in starts)
            {
                if(visited>=maximumVisited || startsChecked++>=32) break;
                var start=startCandidate.Key;var startGround=floors[start];
                if(boundary!=null && !boundary(ground,startGround) || !SweepsClear(ground,startGround)) continue;
                bool supported=true;
                for(float t=0;t<=1;t+=0.2f)
                {
                    var sample=Vector3.Lerp(ground,startGround,t);Vector3 floor;
                    if(!Floor(sample,0.4f,0.8f,out floor) || Mathf.Abs(floor.y-sample.y)>0.3f) {supported=false;break;}
                }
                if(!supported || !CrowdSeparated(ground,startGround,crowd,separation)) continue;
                // Spend the same bounded work toward a usable rejoin, rather than
                // exhausting it in every direction around a blocked corridor.
                var queue=new SortedSet<DetourCell>();var previous=new Dictionary<Vector3Int,Vector3Int>();
                var costs=new Dictionary<Vector3Int,float>();var seen=new HashSet<Vector3Int>();
                costs[start]=0;queue.Add(new DetourCell {Cell=start,Score=DetourDistance(start,goals)});
                while(queue.Count>0 && visited++<maximumVisited)
                {
                    var current=queue.Min;queue.Remove(current);var cell=current.Cell;
                    if(seen.Contains(cell) || current.Cost>costs[cell]) continue;
                    seen.Add(cell);
                    int selectedGoal;
                    if(goals.TryGetValue(cell,out selectedGoal) &&
                        ShortcutSupportedAndClear(LocalPoint(floors[cell]),rejoinRoute[selectedGoal]) &&
                        CrowdSeparated(floors[cell],interior.transform.TransformPoint(Point(rejoinRoute[selectedGoal])),crowd,separation))
                    {
                        rejoinIndex=goals[cell];var cells=new List<Vector3Int>();var cursor=cell;cells.Add(cursor);
                        while(cursor!=start){cursor=previous[cursor];cells.Add(cursor);}cells.Reverse();
                        var route=new List<GuardPoint3> { LocalPoint(ground) };
                        foreach(var pathCell in cells)
                        {
                            if(pathCell!=start)
                            {
                                var from=previous[pathCell];
                                foreach(var edge in floorEdges[from]) if(edge.To==pathCell && edge.Corner.HasValue) {route.Add(LocalPoint(edge.Corner.Value));break;}
                            }
                            route.Add(LocalPoint(floors[pathCell]));
                        }
                        // Grid-cell membership is not the exact saved endpoint.
                        // Preserve the selected corner before attaching its tail.
                        var exact=rejoinRoute[rejoinIndex];
                        route.Add(new GuardPoint3 {X=exact.X,Y=exact.Y,Z=exact.Z});
                        return route;
                    }
                    List<FloorEdge> edges;
                    if(!floorEdges.TryGetValue(cell,out edges)) continue;
                    foreach(var edge in edges)
                    {
                        if(seen.Contains(edge.To)) continue;
                        Vector3 from=floors[cell],to=floors[edge.To];
                        if((to-ground).sqrMagnitude>3.5f*3.5f || Mathf.Abs(to.y-ground.y)>1.5f) continue;
                        Vector3 corner=edge.Corner.HasValue?edge.Corner.Value:Vector3.zero;
                        if(edge.Corner.HasValue && !CrowdSeparated(from,corner,crowd,separation)) continue;
                        if(!CrowdSeparated(edge.Corner.HasValue?corner:from,to,crowd,separation)) continue;
                        float cost=current.Cost+(edge.Corner.HasValue ? Vector3.Distance(from,corner)+Vector3.Distance(corner,to) : Vector3.Distance(from,to));
                        float priorCost;
                        if(costs.TryGetValue(edge.To,out priorCost) && priorCost<=cost) continue;
                        costs[edge.To]=cost;previous[edge.To]=cell;
                        queue.Add(new DetourCell {Cell=edge.To,Cost=cost,Score=cost+DetourDistance(edge.To,goals)});
                    }
                }
            }
            return empty;
        }
        private bool CrowdSeparated(Vector3 from,Vector3 to,IList<Vector3> crowd,float separation)
        {
            Vector3 delta=to-from;Vector3 horizontalDelta=delta;horizontalDelta.y=0;float square=horizontalDelta.sqrMagnitude;
            foreach(var other in crowd)
            {
                if(Mathf.Abs(other.y-from.y)>2.1f && Mathf.Abs(other.y-to.y)>2.1f) continue;
                var initial=from-other;initial.y=0;
                if(initial.sqrMagnitude<separation*separation-0.0001f)
                {
                    var final=to-other;final.y=0;
                    if(Vector3.Dot(initial,horizontalDelta)<-0.0001f || final.sqrMagnitude<initial.sqrMagnitude-0.0001f) return false;
                    continue;
                }
                var relative=other-from;relative.y=0;
                float t=square<0.000001f?0:Mathf.Clamp01(Vector3.Dot(relative,horizontalDelta)/square);
                var closest=from+delta*t-other;closest.y=0;
                if(closest.sqrMagnitude<separation*separation-0.0001f) return false;
            }
            return true;
        }
        public List<GuardPoint3> RouteToExit(Vector3 ground)
        { return SimplifySupportedRoute(RouteToExitRaw(ground)); }
        private List<GuardPoint3> RouteToExitRaw(Vector3 ground)
        {
            var route = new List<GuardPoint3>();
            if (!EntryGround.HasValue || !Clear(ground)) return route;
            float nearest = 2.25f; Vector3Int cell = Vector3Int.zero; bool found = false;
            foreach (var pair in floors)
            {
                var delta = pair.Value - ground;
                if (delta.sqrMagnitude > nearest || Mathf.Abs(delta.y) > 0.3f) continue;
                if(boundary!=null && !boundary(ground,pair.Value)) continue;
                bool blocked = false;
                foreach (var hit in Physics.CapsuleCastAll(ground + Vector3.up * BodyBase, ground + Vector3.up * bodyTop,
                    Radius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    if (SurveyBlocker(hit.collider)) { blocked = true; break; }
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
        public bool StandBesideBed(BedAnchor bed, out Vector3 ground, bool requireReachable=false)
        {
            Vector3 center = interior.transform.TransformPoint(bed.LocalCenter);
            Vector3 side = interior.transform.TransformDirection(bed.LocalSideDirection);
            Vector3 head = interior.transform.TransformDirection(bed.LocalHeadDirection);
            foreach (float sign in new[] { 1f,-1f })
                foreach (float along in new[] { 0f,-0.25f,0.25f })
                {
                    Vector3 candidate = center+side*sign*(bed.Width*0.5f+Radius+0.15f)+head*along*bed.Length;
                    if (Floor(candidate,0.1f,1.6f,out ground) && ground.y < center.y-0.1f && Clear(ground) && (!requireReachable || RouteToExit(ground).Count>=2)) return true;
                }
            foreach (float sign in new[] { -1f,1f })
            {
                Vector3 candidate = center+head*sign*(bed.Length*0.5f+Radius+0.15f);
                if (Floor(candidate,0.1f,1.6f,out ground) && ground.y < center.y-0.1f && Clear(ground) && (!requireReachable || RouteToExit(ground).Count>=2)) return true;
            }
            ground=Vector3.zero; return false;
        }
        public List<GuardPoint3> RouteBetween(Vector3 localFrom,Vector3 localTo)
        {
            // A clear room segment needs no detour through the exit survey tree.
            // Bound this one planning-time probe; keep stairs/obstacles on the graph.
            if((localTo-localFrom).sqrMagnitude<=400 && ShortcutSupportedAndClear(
                new GuardPoint3 {X=localFrom.x,Y=localFrom.y,Z=localFrom.z},new GuardPoint3 {X=localTo.x,Y=localTo.y,Z=localTo.z}))
                return new List<GuardPoint3> {new GuardPoint3 {X=localFrom.x,Y=localFrom.y,Z=localFrom.z},new GuardPoint3 {X=localTo.x,Y=localTo.y,Z=localTo.z}};
            // Preserve the complete parent chains until their shared ancestor is
            // found. Simplify only after the two surveyed paths have been joined.
            var a=RouteToExitRaw(interior.transform.TransformPoint(localFrom));
            var b=RouteToExitRaw(interior.transform.TransformPoint(localTo));
            if(a.Count<2 || b.Count<2) return new List<GuardPoint3>();
            int ai=a.Count-1,bi=b.Count-1;
            while(ai>0 && bi>0 && Vector3.Distance(Point(a[ai-1]),Point(b[bi-1]))<0.001f) { ai--; bi--; }
            var route=new List<GuardPoint3>();
            for(int i=0;i<=ai;i++) route.Add(a[i]);
            for(int i=bi-1;i>=0;i--) route.Add(b[i]);
            if(route.Count==1) route.Add(new GuardPoint3 { X=route[0].X,Y=route[0].Y,Z=route[0].Z });
            return SimplifySupportedRoute(route);
        }
        private List<GuardPoint3> SimplifySupportedRoute(IList<GuardPoint3> route)
        {
            if(route==null || route.Count<3) return route==null ? new List<GuardPoint3>() : new List<GuardPoint3>(route);
            return InteriorRouteSimplifier.Simplify(route,ShortcutSupportedAndClear,RouteSimplifyLookahead,
                RouteSimplifyChecks,RouteSimplifyDistance,RouteSimplifyHeight);
        }
        private bool ShortcutSupportedAndClear(GuardPoint3 fromLocal,GuardPoint3 toLocal)
        {
            Vector3 from=interior.transform.TransformPoint(Point(fromLocal));
            Vector3 to=interior.transform.TransformPoint(Point(toLocal));
            if(Mathf.Abs(to.y-from.y)>RouteSimplifyHeight || !SweepsClear(from,to)) return false;
            float distance=Vector3.Distance(from,to);
            int segments=Mathf.Max(1,Mathf.CeilToInt(distance/0.2f));
            for(int i=0;i<=segments;i++)
            {
                Vector3 sample=Vector3.Lerp(from,to,i/(float)segments),floor;
                if(!Floor(sample,0.4f,0.8f,out floor) || Mathf.Abs(floor.y-sample.y)>0.12f) return false;
            }
            return true;
        }
        public List<Vector3> ApproachPoints(Vector3 localTarget,float radius,float verticalTolerance)
        {
            var target=interior.transform.TransformPoint(localTarget);
            var points=new List<Vector3>();
            // Standing anchors are deliberately sparse and capped at 80. Interaction
            // routes must use the surveyed walkable floor, including upper rooms.
            foreach(var floor in floors.Values)
            {
                float x=floor.x-target.x,z=floor.z-target.z;
                if(x*x+z*z<=radius*radius && Mathf.Abs(floor.y-target.y)<=verticalTolerance)
                    points.Add(interior.transform.InverseTransformPoint(floor));
            }
            points.Sort((a,b)=>(a-localTarget).sqrMagnitude.CompareTo((b-localTarget).sqrMagnitude));
            return points;
        }
        public List<Vector3> NearbyApproachPoints(Vector3 localTarget,float radius,float verticalTolerance,int maximum)
        {
            var points=new List<Vector3>();
            if(!EntryGround.HasValue || radius<0 || verticalTolerance<0 || maximum<1) return points;
            Vector3 target=interior.transform.TransformPoint(localTarget);
            Vector3Int center=Cell(target,EntryGround.Value);
            int horizontalCells=Mathf.CeilToInt(radius/GridStep);
            int verticalCells=Mathf.CeilToInt(verticalTolerance/0.2f);
            for(int y=-verticalCells;y<=verticalCells;y++)
                for(int z=-horizontalCells;z<=horizontalCells;z++)
                    for(int x=-horizontalCells;x<=horizontalCells;x++)
                    {
                        Vector3 floor;
                        if(!floors.TryGetValue(center+new Vector3Int(x,y,z),out floor)) continue;
                        float dx=floor.x-target.x,dz=floor.z-target.z;
                        if(dx*dx+dz*dz>radius*radius || Mathf.Abs(floor.y-target.y)>verticalTolerance) continue;
                        points.Add(interior.transform.InverseTransformPoint(floor));
                    }
            points.Sort((a,b)=>(a-localTarget).sqrMagnitude.CompareTo((b-localTarget).sqrMagnitude));
            if(points.Count>maximum) points.RemoveRange(maximum,points.Count-maximum);
            return points;
        }
        private static Vector3 Point(GuardPoint3 point) { return new Vector3((float)point.X,(float)point.Y,(float)point.Z); }
        public bool HasClearLine(Vector3 from, Vector3 to)
        {
            if (boundary != null && !boundary(from, to)) return false;
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < 0.000001f) return true;
            foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (SurveyBlocker(hit.collider)) return false;
            return true;
        }
        // Rest markers can lie over mattresses; associate them with nearby visible
        // floor in this region without requiring a walkable path onto the mattress.
        public bool ContainsRestMarker(Vector3 marker)
        {
            foreach(var floor in floors.Values)
            {
                var delta=marker-floor;
                if(delta.y < -0.1f || delta.y>1.2f || delta.x*delta.x+delta.z*delta.z>2.25f) continue;
                if(boundary!=null && !boundary(floor,marker-Vector3.up*0.5f)) continue;
                var from=floor+Vector3.up; var to=marker+Vector3.up*0.5f;
                var direction=to-from; bool blocked=false;
                foreach(var hit in Physics.RaycastAll(from,direction.normalized,direction.magnitude,~0,QueryTriggerInteraction.Ignore))
                    if(SurveyBlocker(hit.collider)) { blocked=true; break; }
                if(!blocked) return true;
            }
            return false;
        }
        private GuardPoint3 LocalPoint(Vector3 world)
        { var p = interior.transform.InverseTransformPoint(world); return new GuardPoint3 { X = p.x, Y = p.y, Z = p.z }; }
        private static Vector3Int Cell(Vector3 point, Vector3 seed)
        { return new Vector3Int(Mathf.RoundToInt((point.x-seed.x)/GridStep), Mathf.RoundToInt((point.y-seed.y)/0.2f), Mathf.RoundToInt((point.z-seed.z)/GridStep)); }
        private bool SweepsClear(Vector3 from, Vector3 to)
        {
            if(seating.BlocksGeometry(from,to,Radius))return false;
            if(boundary!=null && !boundary(from,to)) return false;
            Vector3 delta = to-from;
            if (delta.sqrMagnitude < 0.000001f) return true;
            foreach (var hit in Physics.CapsuleCastAll(from+Vector3.up*BodyBase,from+Vector3.up*bodyTop,
                Radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if (SurveyBlocker(hit.collider)) return false;
            return true;
        }
        private bool Floor(Vector3 point, float up, float depth, out Vector3 floor)
        {
            var hits=Physics.RaycastAll(point+Vector3.up*up,Vector3.down,depth,~0,QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in hits)
                if(hit.collider.transform.IsChildOf(interior.transform) && hit.normal.y>0.65f)
                {floor=hit.point;return true;}
            floor = Vector3.zero; return false;
        }
        private bool Clear(Vector3 ground)
        {
            if(seating.BlocksGeometry(ground,ground,Radius))return false;
            if(boundary!=null && !boundary(ground,ground)) return false;
            foreach (var collider in Physics.OverlapCapsule(ground + Vector3.up * BodyBase, ground + Vector3.up * bodyTop,
                Radius, ~0, QueryTriggerInteraction.Ignore))
                if (SurveyBlocker(collider)) return false;
            return true;
        }
        private bool SurveyBlocker(Collider collider)
        {
            var other=collider.GetComponentInParent<DaggerfallInterior>();
            if(other && other!=interior)return false;
            return boundary!=null && collider.GetComponentInParent<DaggerfallActionDoor>() ? false : Blocker(collider);
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
