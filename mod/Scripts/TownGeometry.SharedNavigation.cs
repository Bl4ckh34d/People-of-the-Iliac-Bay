using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class TownGeometry
    {
        private const double ClearanceCellSize=0.5;
        private SharedClearanceMap sharedClearance;
        private readonly SharedRouteCache sharedRoutes=new SharedRouteCache(1024);
        private readonly HashSet<string> pendingSharedRoutes=new HashSet<string>();
        private byte[] sharedGridCosts,sharedHorizontalEdges,sharedVerticalEdges;
        private int outdoorTopologyRevision;
        private readonly Dictionary<GridCell,float> terrainHeights=new Dictionary<GridCell,float>();
        private readonly Collider[] sharedOverlap=new Collider[64];
        private readonly RaycastHit[] sharedFloorHits=new RaycastHit[32];
        public long ClearanceCellSurveys {get{return sharedClearance==null?0:sharedClearance.Surveys;}}
        public long ClearanceCacheHits {get{return sharedClearance==null?0:sharedClearance.Hits;}}
        public long ClearanceExactChecks {get{return sharedClearance==null?0:sharedClearance.ExactChecks;}}
        public int ClearanceCellCount {get{return sharedClearance==null?0:sharedClearance.CellCount;}}
        public long SharedRouteHits {get{return sharedRoutes.Hits;}}

        private bool SharedLaneClear(RoutePoint from,RoutePoint to)
        {
            if(sharedClearance==null)
                sharedClearance=new SharedClearanceMap(ClearanceCellSize,262144,SurveyClearanceSquare,LaneSegmentClearCore);
            // Doors and selected-seat access are not static cache permissions.
            // Gate checks stay native, including when a gate changed since survey.
            if(SegmentTouchesGate(from,to))return LaneSegmentClearCore(from,to);
            var a=ScenePoint(from);var b=ScenePoint(to);
            if(FurnitureSeatCatalog.Blocks(a,b,CrowdMotion.BodyRadius))return false;
            if(Math.Abs(a.y-b.y)>0.02f)return LaneSegmentClearCore(from,to);
            return sharedClearance.Clear(from,to);
        }
        private bool SegmentTouchesGate(RoutePoint from,RoutePoint to)
        {
            if(streetGateAprons==null)StreetGateApproach(from);
            foreach(var bounds in streetGateAprons)
                if(NavigationBounds.IntersectsSegment(from,to,bounds.min.x,bounds.min.z,bounds.max.x,bounds.max.z))return true;
            return false;
        }

        private bool SurveyClearanceSquare(int x,int z)
        {
            var local=new RoutePoint((x+0.5)*ClearanceCellSize,(z+0.5)*ClearanceCellSize);
            var center=ScenePoint(local);
            if(!FlatTerrainSquare(center))return false;
            // A square prism contains the capsule at EVERY position within this
            // square. Testing only a capsule at its center would cut wall corners.
            float half=(float)(ClearanceCellSize*0.5)+LaneBodyRadius;
            const float bottom=0.23f,top=LaneBodyHeight+0.01f;
            int count=Physics.OverlapBoxNonAlloc(center+Vector3.up*((bottom+top)*0.5f),
                new Vector3(half,(top-bottom)*0.5f,half),sharedOverlap,origin.rotation,~0,QueryTriggerInteraction.Ignore);
            if(count>=sharedOverlap.Length)return false;
            for(int i=0;i<count;i++)if(IsStaticBlocker(sharedOverlap[i]))return false;
            if(FurnitureSeatCatalog.Blocks(center,center,half*1.414214f))return false;
            // Certify flat terrain-backed squares only. Bridges, stairs, ramps,
            // narrow doorways and missing support retain the exact native path.
            if(!terrain)return false;
            for(int i=0;i<5;i++)
            {
                double ox=i==0?0:((i&1)==0?-0.25:0.25);
                double oz=i==0?0:((i&2)==0?-0.25:0.25);
                var p=ScenePoint(new RoutePoint(local.X+ox,local.Z+oz));
                if(Math.Abs(p.y-center.y)>0.005f)return false;
                int hits=Physics.RaycastNonAlloc(p+Vector3.up*0.45f,Vector3.down,sharedFloorHits,0.65f,~0,QueryTriggerInteraction.Ignore);
                if(hits>=sharedFloorHits.Length)return false;
                bool supported=false;
                for(int j=0;j<hits;j++)
                {
                    var hit=sharedFloorHits[j];
                    if(hit.collider is TerrainCollider && hit.normal.y>=0.9999f && Math.Abs(hit.point.y-p.y)<=0.02f)
                    {supported=true;break;}
                }
                if(!supported)return false;
            }
            return true;
        }

        private bool FlatTerrainSquare(Vector3 center)
        {
            if(!terrain||!terrain.terrainData || (origin.lossyScale-Vector3.one).sqrMagnitude>0.000001f ||
                Vector3.Dot(origin.up,Vector3.up)<0.999999f ||
                (terrain.transform.lossyScale-Vector3.one).sqrMagnitude>0.000001f ||
                Quaternion.Angle(terrain.transform.rotation,Quaternion.identity)>0.001f)return false;
            var data=terrain.terrainData;var scale=data.heightmapScale;var position=terrain.transform.position;
            // Every terrain triangle covering the square has vertices in this
            // rectangle. Bounding all vertices proves the interior height bound;
            // five point samples alone cannot detect a subcell peak or hollow.
            double half=ClearanceCellSize*0.707107;
            int x0=(int)Math.Floor((center.x-position.x-half)/scale.x),x1=(int)Math.Ceiling((center.x-position.x+half)/scale.x);
            int z0=(int)Math.Floor((center.z-position.z-half)/scale.z),z1=(int)Math.Ceiling((center.z-position.z+half)/scale.z);
            if(x0<0||z0<0||x1>=data.heightmapResolution||z1>=data.heightmapResolution||(x1-x0+1)*(z1-z0+1)>64)return false;
            float min=float.PositiveInfinity,max=float.NegativeInfinity;
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
            {
                var key=new GridCell(x,z);float height;
                if(!terrainHeights.TryGetValue(key,out height))
                {
                    height=data.GetHeight(x,z);
                    if(terrainHeights.Count<65536)terrainHeights.Add(key,height);
                }
                min=Math.Min(min,height);max=Math.Max(max,height);
            }
            return max-min<=0.005f;
        }

        // Scene providers call this after a static obstacle changes. Expand by
        // footprint before invalidating cells; routes may depend on those cells.
        public void InvalidateOutdoorRegion(double minX,double minZ,double maxX,double maxZ)
        {
            if(sharedClearance!=null)sharedClearance.Invalidate(minX-1,minZ-1,maxX+1,maxZ+1);
            terrainHeights.Clear();
            outdoorTopologyRevision++;sharedRoutes.Clear();sharedGridCosts=null;sharedHorizontalEdges=null;sharedVerticalEdges=null;
            preparedStreetLanes.Clear();preparedStreetLaneOrder.Clear();
            streetCrossSections.Clear();streetCrossSectionOrder.Clear();
        }

        private IEnumerator SharedRouteSteps(RoutePoint from,RoutePoint to,RouteResult result,Func<bool> valid,
            Func<IEnumerator> create)
        {
            bool native=UsesNativeNavigation;
            if(native!=sharedRoutesUseNative)
            {sharedRoutesUseNative=native;sharedRoutes.Clear();outdoorTopologyRevision++;}
            result.Points=null;string key=SharedRouteCache.Key(from,to),reverse=SharedRouteCache.Key(to,from);
            while(pendingSharedRoutes.Contains(key)||pendingSharedRoutes.Contains(reverse))
            {if(!valid())yield break;yield return null;}
            if(!valid())yield break;
            List<SavedRoutePoint> cached;
            if(sharedRoutes.TryGet(from,to,out cached)){result.Points=cached;yield break;}
            pendingSharedRoutes.Add(key);IEnumerator operation=null;int revision=outdoorTopologyRevision;
            try
            {
                operation=create();
                while(operation.MoveNext()){if(!valid())yield break;yield return null;}
                if(revision!=outdoorTopologyRevision){result.Points=new List<SavedRoutePoint>();yield break;}
                if(valid()&&result.Points!=null&&result.Points.Count>=2)
                {
                    bool gate=false;
                    for(int i=1;i<result.Points.Count;i++)
                        if(SegmentTouchesGate(new RoutePoint(result.Points[i-1].X,result.Points[i-1].Z),
                            new RoutePoint(result.Points[i].X,result.Points[i].Z))){gate=true;break;}
                    if(!gate)sharedRoutes.Put(from,to,result.Points);
                }
            }
            finally
            {
                var disposable=operation as IDisposable;if(disposable!=null)disposable.Dispose();
                pendingSharedRoutes.Remove(key);
            }
        }
        private bool sharedRoutesUseNative;
    }
}
