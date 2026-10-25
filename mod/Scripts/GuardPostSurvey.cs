using System;
using System.Collections.Generic;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class TownGeometry
    {
        public sealed class GateGuardPostSites
        {
            public int GateIndex;
            public SavedRoutePoint InsideLeft, InsideRight, InsideReturnLeft, InsideReturnRight, OutsideLeft, OutsideRight;
            public Vector3 InsideFacing, OutsideFacing;
        }
        private sealed class PostSite { public SavedRoutePoint Point; public double Rank; }
        private sealed class GateFacingSite { public SavedRoutePoint Point; public Vector3 Facing; }
        private readonly List<GateFacingSite> gateFacingSites=new List<GateFacingSite>();
        private readonly List<SavedRoutePoint[]> gateCrossings=new List<SavedRoutePoint[]>();
        private readonly Dictionary<SavedRoutePoint,Vector3> postFacings=new Dictionary<SavedRoutePoint,Vector3>();
        public int PublicPostsAcrossStreetWidth {get;private set;}
        public sealed class GuardPostResult {public List<SavedRoutePoint> Posts;}
        public List<SavedRoutePoint> SurveyGuardPosts(int maximum)
        {
            long started=RuntimeTiming.Start();
            var result=new GuardPostResult();
            var operation=SurveyGuardPostSteps(maximum,result);
            try {while(operation.MoveNext()) {} return result.Posts;}
            finally {RuntimeTiming.End("guard-public-post-survey",started);}
        }
        public System.Collections.IEnumerator SurveyGuardPostSteps(int maximum,GuardPostResult result)
        {
            if(result==null) throw new ArgumentNullException(nameof(result));
            if(maximum<1) throw new ArgumentOutOfRangeException(nameof(maximum));
            result.Posts=null;
            var doors=new List<Vector3>();
            foreach(var owner in origin.GetComponentsInChildren<DaggerfallStaticDoors>())
                if(owner.Doors!=null) for(int i=0;i<owner.Doors.Length;i++)
                    if(owner.Doors[i].doorType==DoorTypes.Building) doors.Add(owner.GetDoorPosition(i));
            var gates=origin.GetComponentsInChildren<DaggerfallCityGate>();
            var candidates=new List<PostSite>();
            int work=0;
            for(int z=4;z<Height-4;z+=2) for(int x=4;x<Width-4;x+=2)
            {
                if(++work%256==0) yield return null;
                var scene=SceneCell(new GridCell(x,z));
                float gateDistance=float.PositiveInfinity;
                foreach(var gate in gates)
                { var gap=scene-gate.transform.position; gap.y=0; gateDistance=Mathf.Min(gateDistance,gap.sqrMagnitude); }
                bool gateSite=gateDistance>=36 && gateDistance<=400 && navigation.GetNavGridWeightLocal(x,z)==15;
                if(!gateSite && !GuardPostSites.IsJunction(x,z,navigation.GetNavGridWeightLocal,8) &&
                    !GuardPostSites.IsBroadStreet(x,z,navigation.GetNavGridWeightLocal,4,2)) continue;
                float doorDistance=float.PositiveInfinity;
                foreach(var door in doors) { var gap=scene-door; gap.y=0; doorDistance=Mathf.Min(doorDistance,gap.sqrMagnitude); }
                if(doorDistance<25) continue;
                var local=origin.InverseTransformPoint(scene);
                // Rank public spaces and broad streets. The final point is
                // distributed inside its physically verified paved cross-section.
                candidates.Add(new PostSite { Point=new SavedRoutePoint { X=local.x,Z=local.z },
                    Rank=(gateSite ? -10000 : 0)+doorDistance });
            }
            candidates.Sort((a,b)=>{ int n=a.Rank.CompareTo(b.Rank); if(n!=0) return n;
                n=a.Point.X.CompareTo(b.Point.X); return n!=0 ? n : a.Point.Z.CompareTo(b.Point.Z); });
            var posts=new List<SavedRoutePoint>();
            PublicPostsAcrossStreetWidth=0;
            foreach(var site in candidates)
            {
                yield return null;
                if(posts.Exists(p=>Math.Pow(p.X-site.Point.X,2)+Math.Pow(p.Z-site.Point.Z,2)<36)) continue;
                var original=site.Point;
                var sceneOriginal=ScenePoint(new RoutePoint(original.X,original.Z));
                var grid=navigation.WorldToNavGridPosition(navigation.SceneToWorldPosition(sceneOriginal));
                int alongX=0,alongZ=0;
                for(int step=-8;step<=8;step++)
                {
                    int wx=navigation.GetNavGridWeightLocal(grid.X+step,grid.Y);
                    int wz=navigation.GetNavGridWeightLocal(grid.X,grid.Y+step);
                    if(wx==15 || wx==4) alongX++;if(wz==15 || wz==4) alongZ++;
                }
                var normal=alongX>=alongZ ? new RoutePoint(0,1) : new RoutePoint(1,0);
                var originPoint=new RoutePoint(original.X,original.Z);
                var width=SurveyStreetCrossSection(originPoint,normal);
                uint seed;unchecked {seed=(uint)((int)original.X*73856093 ^ (int)original.Z*19349663 ^ directory.MapID);}
                double lateral=width.Center-width.Right+CorridorLaneRoutes.Percentile(seed)*(width.Left+width.Right);
                var shifted=new SavedRoutePoint {X=original.X+normal.X*lateral,Z=original.Z+normal.Z*lateral};
                if(LaneSegmentClear(originPoint,new RoutePoint(shifted.X,shifted.Z))) site.Point=shifted;
                if(posts.Exists(p=>Math.Pow(p.X-site.Point.X,2)+Math.Pow(p.Z-site.Point.Z,2)<36)) continue;
                var ground=ScenePoint(new RoutePoint(site.Point.X,site.Point.Z)); bool blocked=false;
                foreach(var collider in Physics.OverlapCapsule(ground+Vector3.up*0.7f,ground+Vector3.up*1.7f,
                    0.4f,~0,QueryTriggerInteraction.Ignore))
                    if(IsStaticBlocker(collider)) { blocked=true; break; }
                if(blocked || RouteBetweenPoints(site.Point,site.Point).Count<2) continue;
                posts.Add(site.Point);
                if(width.Left+width.Right>=3 && Math.Pow(site.Point.X-original.X,2)+Math.Pow(site.Point.Z-original.Z,2)>=0.25)
                    PublicPostsAcrossStreetWidth++;
                if(posts.Count>=maximum) break;
            }
            result.Posts=posts;
        }
        public Vector3 GuardPostFacing(SavedRoutePoint point)
        {
            Vector3 gateFacing;
            if(TryGetGateFacing(point,out gateFacing)) return gateFacing;
            Vector3 cached;
            if(postFacings.TryGetValue(point,out cached)) return cached;
            Vector3 from=ScenePoint(new RoutePoint(point.X,point.Z));
            Vector3 best=Vector3.forward; float longest=-1;
            foreach(var direction in new[] { Vector3.forward,Vector3.right,Vector3.back,Vector3.left })
            {
                float distance=12;
                foreach(var hit in Physics.SphereCastAll(from+Vector3.up,0.4f,direction,12,~0,QueryTriggerInteraction.Ignore))
                    if(IsStaticBlocker(hit.collider)) distance=Mathf.Min(distance,hit.distance);
                if(distance>longest) { longest=distance; best=direction; }
            }
            postFacings[point]=best;
            return best;
        }

        private List<GateGuardPostSites> gatePostCache;
        public List<GateGuardPostSites> SurveyGateGuardPosts()
        {
            if(gatePostCache==null)
            {
                var steps=SurveyGatePostSteps();
                try {while(steps.MoveNext()) {}}
                finally {(steps as IDisposable)?.Dispose();}
            }
            return new List<GateGuardPostSites>(gatePostCache);
        }
        public System.Collections.IEnumerator SurveyGatePostSteps()
        {
            if(gatePostCache!=null) yield break;
            var facings=new List<GateFacingSite>();
            var crossings=new List<SavedRoutePoint[]>();
            var result=new List<GateGuardPostSites>();
            var gates=origin.GetComponentsInChildren<DaggerfallCityGate>();
            var center=SceneCell(new GridCell(Width/2,Height/2));
            Array.Sort(gates,(a,b)=>{
                var pa=origin.InverseTransformPoint(a.transform.position);
                var pb=origin.InverseTransformPoint(b.transform.position);
                int order=pa.x.CompareTo(pb.x); return order!=0 ? order : pa.z.CompareTo(pb.z);
            });
            var occupied=new List<SavedRoutePoint>();
            for(int index=0;index<gates.Length;index++)
            {
                yield return null;
                var gate=gates[index]; var mesh=gate.GetComponent<MeshFilter>();
                if(!mesh || !mesh.sharedMesh)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_GATE_SURVEY_GATE: gate="+index+" name="+gate.name+" missing MeshFilter/sharedMesh"); continue; }
                var bounds=mesh.sharedMesh.bounds;
                bool normalAlongX=bounds.size.x<bounds.size.z;
                var localNormal=normalAlongX ? Vector3.right : Vector3.forward;
                var localAxis=normalAlongX ? Vector3.forward : Vector3.right;
                var worldNormal=gate.transform.TransformDirection(localNormal); worldNormal.y=0;
                // Native 446/447 include the wall frame. The opening is offset from its bounds center.
                // ARCH3D leaf inner edges: X=-298 and X=43, scaled by MeshReader.GlobalScale (.025).
                bool nativeFrame=Mathf.Abs(bounds.size.x-25.6f)<0.05f && Mathf.Abs(bounds.size.z-9.6f)<0.05f;
                var apertureCenter=bounds.center;
                if(nativeFrame) apertureCenter.x=-3.1875f;
                var middle=gate.transform.TransformPoint(apertureCenter);
                if(worldNormal.sqrMagnitude<0.0001f)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_GATE_SURVEY_GATE: gate="+index+" degenerate wall normal; localNormal="+localNormal+" transform="+gate.transform.rotation); continue; }
                if(Vector3.Dot(worldNormal,center-middle)<0) worldNormal=-worldNormal;
                worldNormal.Normalize();
                var worldAxis=gate.transform.TransformDirection(localAxis); worldAxis.y=0;
                if(worldAxis.sqrMagnitude<0.0001f)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_GATE_SURVEY_GATE: gate="+index+" degenerate aperture axis; localAxis="+localAxis+" transform="+gate.transform.rotation); continue; }
                worldAxis.Normalize();
                var localCenter=origin.InverseTransformPoint(middle);
                var localCity=origin.InverseTransformDirection(worldNormal); localCity.y=0;
                var localAxisDirection=origin.InverseTransformDirection(worldAxis); localAxisDirection.y=0;
                float halfDepth=normalAlongX ? bounds.extents.x*gate.transform.lossyScale.x :
                    bounds.extents.z*gate.transform.lossyScale.z;
                float halfOpening=normalAlongX ? bounds.extents.z*gate.transform.lossyScale.z :
                    bounds.extents.x*gate.transform.lossyScale.x;
                if(nativeFrame) halfOpening=4.2625f*Mathf.Abs(gate.transform.lossyScale.x);
                var inner=origin.InverseTransformPoint(middle+worldNormal*(halfDepth+2));
                var outer=origin.InverseTransformPoint(middle-worldNormal*(halfDepth+2));
                crossings.Add(new[] { new SavedRoutePoint { X=inner.x,Z=inner.z },
                    new SavedRoutePoint { X=outer.x,Z=outer.z } });
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GATE_SURVEY_GATE: index="+index+" name="+gate.name+" gates="+gates.Length+
                    " nav="+Width+"x"+Height+" terrain="+(terrain ? terrain.name : "none")+
                    " boundsCenter="+bounds.center+" boundsSize="+bounds.size+
                    " scale="+gate.transform.lossyScale+" position="+gate.transform.position+
                    " middle="+middle+" cityCenter="+center+" centerDot="+Vector3.Dot(worldNormal,center-middle)+
                    " normalAxis="+(normalAlongX ? "localX" : "localZ")+" worldNormal="+worldNormal+
                    " worldAxis="+worldAxis+" localCenter="+localCenter+" localCity="+localCity+
                    " localAxis="+localAxisDirection+" halfDepth="+halfDepth+" halfOpening="+halfOpening);
                GateGuardPostPlan plan;
                try { plan=GateGuardPosts.Build(localCenter.x,localCenter.z,localCity.x,localCity.z,
                    localAxisDirection.x,localAxisDirection.z,halfDepth,halfOpening,1.5,1.25); }
                catch(ArgumentException ex)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_GATE_SURVEY_GATE: index="+index+" plan rejected: "+ex.Message); continue; }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GATE_SURVEY_PLAN: gate="+index+" insideFacing="+plan.InsideFacing+
                    " outsideFacing="+plan.OutsideFacing+" desired="+DescribeGatePoint("IL",plan.InsideLeft)+";"+
                    DescribeGatePoint("IR",plan.InsideRight)+";"+DescribeGatePoint("RL",plan.InsideReturnLeft)+";"+
                    DescribeGatePoint("RR",plan.InsideReturnRight)+";"+DescribeGatePoint("OL",plan.OutsideLeft)+";"+
                    DescribeGatePoint("OR",plan.OutsideRight)+" occupiedFromPriorGates="+occupied.Count);
                SavedRoutePoint insideLeft,insideRight,insideReturnLeft,insideReturnRight,outsideLeft,outsideRight;
                var gateOccupied=new List<SavedRoutePoint>();
                if(!TryResolveGatePost(plan.InsideLeft,occupied,out insideLeft,out var failure))
                { LogGateStationFailure(index,"IL",plan.InsideLeft,occupied,failure); continue; }
                yield return null;
                gateOccupied.Add(insideLeft);
                if(!TryResolveGatePost(plan.InsideRight,Combine(occupied,gateOccupied),out insideRight,out failure))
                { LogGateStationFailure(index,"IR",plan.InsideRight,Combine(occupied,gateOccupied),failure); continue; }
                yield return null;
                gateOccupied.Add(insideRight);
                if(!TryResolveGatePost(plan.InsideReturnLeft,Combine(occupied,gateOccupied),out insideReturnLeft,out failure))
                { LogGateStationFailure(index,"RL",plan.InsideReturnLeft,Combine(occupied,gateOccupied),failure); continue; }
                yield return null;
                gateOccupied.Add(insideReturnLeft);
                if(!TryResolveGatePost(plan.InsideReturnRight,Combine(occupied,gateOccupied),out insideReturnRight,out failure))
                { LogGateStationFailure(index,"RR",plan.InsideReturnRight,Combine(occupied,gateOccupied),failure); continue; }
                yield return null;
                gateOccupied.Add(insideReturnRight);
                if(!TryResolveGatePost(plan.OutsideLeft,Combine(occupied,gateOccupied),out outsideLeft,out failure))
                { LogGateStationFailure(index,"OL",plan.OutsideLeft,Combine(occupied,gateOccupied),failure); continue; }
                yield return null;
                gateOccupied.Add(outsideLeft);
                if(!TryResolveGatePost(plan.OutsideRight,Combine(occupied,gateOccupied),out outsideRight,out failure))
                { LogGateStationFailure(index,"OR",plan.OutsideRight,Combine(occupied,gateOccupied),failure); continue; }
                yield return null;
                gateOccupied.Add(outsideRight);
                occupied.AddRange(gateOccupied);
                var insideFacing=origin.TransformDirection(new Vector3((float)plan.InsideFacing.X,0,(float)plan.InsideFacing.Z)).normalized;
                var outsideFacing=origin.TransformDirection(new Vector3((float)plan.OutsideFacing.X,0,(float)plan.OutsideFacing.Z)).normalized;
                facings.Add(new GateFacingSite { Point=insideLeft,Facing=insideFacing });
                facings.Add(new GateFacingSite { Point=insideRight,Facing=insideFacing });
                facings.Add(new GateFacingSite { Point=insideReturnLeft,Facing=insideFacing });
                facings.Add(new GateFacingSite { Point=insideReturnRight,Facing=insideFacing });
                facings.Add(new GateFacingSite { Point=outsideLeft,Facing=outsideFacing });
                facings.Add(new GateFacingSite { Point=outsideRight,Facing=outsideFacing });
                result.Add(new GateGuardPostSites { GateIndex=index, InsideLeft=insideLeft, InsideRight=insideRight,
                    InsideReturnLeft=insideReturnLeft,InsideReturnRight=insideReturnRight,
                    OutsideLeft=outsideLeft, OutsideRight=outsideRight,InsideFacing=insideFacing,OutsideFacing=outsideFacing });
            }
            gateFacingSites.Clear();gateFacingSites.AddRange(facings);
            gateCrossings.Clear();gateCrossings.AddRange(crossings);
            gatePostCache=result;
        }
        private static List<SavedRoutePoint> Combine(List<SavedRoutePoint> a,List<SavedRoutePoint> b)
        { var result=new List<SavedRoutePoint>(a); result.AddRange(b); return result; }
        public List<SavedRoutePoint> GateRouteBetweenPoints(SavedRoutePoint from,SavedRoutePoint to)
        {
            var direct=RouteBetweenPoints(from,to);
            if(direct.Count>=2) return direct;
            // Gate navigation tiles can be disconnected even while the physical gate is open.
            // Bridge only the actual aperture, with the same collision checks as street routes.
            foreach(var crossing in gateCrossings)
            for(int direction=0;direction<2;direction++)
            {
                var entry=crossing[direction]; var exit=crossing[1-direction];
                double dx=exit.X-entry.X,dz=exit.Z-entry.Z;
                double midX=(entry.X+exit.X)*0.5,midZ=(entry.Z+exit.Z)*0.5;
                if((from.X-midX)*dx+(from.Z-midZ)*dz>=0 ||
                    (to.X-midX)*dx+(to.Z-midZ)*dz<=0) continue;
                var a=ScenePoint(new RoutePoint(entry.X,entry.Z));
                var b=ScenePoint(new RoutePoint(exit.X,exit.Z));
                var delta=b-a; bool blocked=false;
                foreach(float height in new[] { 0.4f,1f })
                {
                    foreach(var point in new[] { a,b })
                        foreach(var collider in Physics.OverlapSphere(point+Vector3.up*height,0.2f,~0,QueryTriggerInteraction.Ignore))
                            if(IsStaticBlocker(collider)) blocked=true;
                    foreach(var hit in Physics.SphereCastAll(a+Vector3.up*height,0.2f,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                        if(IsStaticBlocker(hit.collider)) blocked=true;
                }
                if(blocked) continue;
                var before=RouteBetweenPoints(from,entry);
                var after=RouteBetweenPoints(exit,to);
                if(before.Count<2 || after.Count<2) continue;
                before.AddRange(after); return before;
            }
            return direct;
        }
        public bool TryGetGateFacing(SavedRoutePoint point,out Vector3 facing)
        {
            foreach(var site in gateFacingSites)
                if(Math.Pow(site.Point.X-point.X,2)+Math.Pow(site.Point.Z-point.Z,2)<0.01)
                { facing=site.Facing; return true; }
            facing=Vector3.zero; return false;
        }
        private static string DescribeGatePoint(string name,SavedRoutePoint point)
        { return name+"=("+point.X.ToString("F2")+","+point.Z.ToString("F2")+")"; }
        private void LogGateStationFailure(int gate,string station,SavedRoutePoint desired,
            List<SavedRoutePoint> occupied,string diagnostic)
        {
            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_GATE_SURVEY_STATION_FAIL: gate="+gate+" station="+station+
                " desired="+DescribeGatePoint("",desired)+" occupied="+occupied.Count+" "+diagnostic);
        }
        private bool TryResolveGatePost(SavedRoutePoint desired,List<SavedRoutePoint> occupied,
            out SavedRoutePoint resolved,out string diagnostic)
        {
            resolved=null;
            var desiredScene=ScenePoint(new RoutePoint(desired.X,desired.Z));
            float best=float.PositiveInfinity;
            int eligibleWeight=0,withinRadius=0,heightClear=0,spacingClear=0,terrainClear=0,routeClear=0;
            var nearest=new List<string>(); var nearestDistances=new List<float>();
            var blockedExamples=new List<string>();
            // Search only the five-metre neighborhood, not every cell in a large city for each post.
            var gridOrigin=SceneCell(new GridCell(0,0));
            var gridX=SceneCell(new GridCell(1,0))-gridOrigin; gridX.y=0;
            var gridZ=SceneCell(new GridCell(0,1))-gridOrigin; gridZ.y=0;
            var offset=desiredScene-gridOrigin; offset.y=0;
            int cx=Mathf.RoundToInt(Vector3.Dot(offset,gridX)/gridX.sqrMagnitude);
            int cz=Mathf.RoundToInt(Vector3.Dot(offset,gridZ)/gridZ.sqrMagnitude);
            int rx=Mathf.CeilToInt(5/gridX.magnitude)+1,rz=Mathf.CeilToInt(5/gridZ.magnitude)+1;
            for(int z=Mathf.Max(0,cz-rz);z<=Mathf.Min(Height-1,cz+rz);z++)
            for(int x=Mathf.Max(0,cx-rx);x<=Mathf.Min(Width-1,cx+rx);x++)
            {
                int weight=navigation.GetNavGridWeightLocal(x,z);
                var scene=SceneCell(new GridCell(x,z)); var gap=scene-desiredScene; gap.y=0;
                float distance=gap.sqrMagnitude;
                if(nearest.Count<5 || distance<nearestDistances[nearestDistances.Count-1])
                {
                    float terrainHeight=terrain ? terrain.SampleHeight(scene)+GameManager.Instance.StreamingWorld.WorldCompensation.y : float.NaN;
                    string sample=distance.ToString("F2")+"|cell="+x+","+z+" weight="+weight+
                        " sceneY="+scene.y.ToString("F2")+" desiredY="+desiredScene.y.ToString("F2")+
                        " dy="+(scene.y-desiredScene.y).ToString("F2")+" terrainY="+terrainHeight.ToString("F2")+
                        " navTerrainDelta="+(scene.y-terrainHeight).ToString("F2");
                    int insert=nearest.Count;
                    while(insert>0 && distance<nearestDistances[insert-1]) insert--;
                    nearest.Insert(insert,sample);
                    nearestDistances.Insert(insert,distance);
                    if(nearest.Count>5) { nearest.RemoveAt(5); nearestDistances.RemoveAt(5); }
                }
                if(weight<=0) continue;
                eligibleWeight++;
                if(distance>25) continue;
                withinRadius++;
                if(Math.Abs(scene.y-desiredScene.y)>1.5f) continue;
                heightClear++;
                if(distance>=best) continue;
                var local=origin.InverseTransformPoint(scene);
                var candidate=new SavedRoutePoint { X=local.x,Z=local.z };
                bool overlaps=false;
                foreach(var point in occupied)
                    if(Math.Pow(point.X-candidate.X,2)+Math.Pow(point.Z-candidate.Z,2)<2.25) { overlaps=true; break; }
                if(overlaps) continue;
                spacingClear++;
                var ground=scene;
                bool blocked=false;
                foreach(var collider in Physics.OverlapCapsule(ground+Vector3.up*0.7f,ground+Vector3.up*1.7f,
                    0.4f,~0,QueryTriggerInteraction.Ignore))
                    if(IsStaticBlocker(collider))
                    {
                        blocked=true;
                        if(blockedExamples.Count<5) blockedExamples.Add("cell="+x+","+z+" blocker="+collider.name+
                            " y="+scene.y.ToString("F2")+" terrainY="+(terrain ? terrain.SampleHeight(scene)+
                                GameManager.Instance.StreamingWorld.WorldCompensation.y : float.NaN).ToString("F2"));
                    }
                if(blocked) continue;
                terrainClear++;
                var route=RouteBetweenPoints(candidate,candidate);
                if(route.Count<2) continue;
                routeClear++;
                best=distance; resolved=candidate;
            }
            var nearestSummary=new StringBuilder();
            foreach(var item in nearest) { if(nearestSummary.Length>0) nearestSummary.Append(" || "); nearestSummary.Append(item.Substring(item.IndexOf('|')+1)); }
            diagnostic="desiredScene="+desiredScene+" counts(walkableWeight="+eligibleWeight+",within5m="+withinRadius+
                ",heightClear="+heightClear+",spacingClear="+spacingClear+",capsuleClear="+terrainClear+
                ",routeClear="+routeClear+") nearest="+nearestSummary+" blocked="+string.Join(" || ",blockedExamples.ToArray());
            if(resolved!=null) diagnostic+=" resolved="+DescribeGatePoint("",resolved)+" distance="+best.ToString("F2");
            return resolved!=null;
        }
    }
}
