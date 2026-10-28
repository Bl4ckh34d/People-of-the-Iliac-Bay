using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class RoadTravelActors
    {
        private IEnumerator PrepareCompanionOriginTravel(WorldStateStore store,DungeonFollower binding)
        {
            var first=store.ReadNpcAction(binding.ActionId);var recovery=first==null?null:first.CompanionRecovery;
            if(recovery==null || recovery.ExteriorEntrance==null || recovery.RecruitmentRoute!=null || recovery.RecruitmentArrival!=null)yield break;
            int sourceMap=recovery.ExteriorEntrance.SettlementId;var origin=NpcActionExecutor.CopyTarget(recovery.RecruitmentOrigin);
            var game=GameManager.Instance;
            Func<bool> valid=()=>source!=null && ReferenceEquals(source(),store) && store.ReadNpcAction(binding.ActionId)!=null &&
                game;
            var reader=DaggerfallUnity.Instance.ContentReader;
            var pixel=MapsFile.GetPixelFromPixelID(origin.SettlementId & 0x000fffff);
            DaggerfallWorkshop.Utility.ContentReader.MapSummary summary;
            if(!reader.HasLocation(pixel.X,pixel.Y,out summary) || summary.MapID!=origin.SettlementId)yield break;
            // Native Clock/fast-travel rules supply a conservative, terrain-aware
            // on-foot duration. This reads metadata only and never advances the player.
            double seconds=recovery.NativeReturnEstimateSeconds;
            if(seconds==0)
            {
                var sourcePixel=game.PlayerGPS.CurrentMapPixel;
                if(MapsFile.GetMapPixelID(sourcePixel.X,sourcePixel.Y)!=(sourceMap & 0x000fffff))yield break;
                var estimate=new TravelTimeCalculator();int minutes=estimate.CalculateTravelTime(pixel,true,false,false,false,false);
                if(minutes<1 || estimate.OceanPixels>0)yield break;seconds=minutes*60d;
                // Save this cheap native estimate before the route survey yields,
                // so loading elsewhere can resume preparation without using the player's new origin.
                var current=store.ReadNpcAction(binding.ActionId);
                if(!store.TryCaptureCompanionNativeReturnEstimate(current.Id,current.Revision,DungeonNow(),seconds))yield break;
            }
            DaggerfallConnect.DFLocation location;
            if(!reader.GetLocation(summary.RegionIndex,summary.MapIndex,out location) || location.MapTableData.MapId!=origin.SettlementId)yield break;
            // This is the previously visited recruitment location, not a new
            // household or town simulation. Reuse its native exterior route survey.
            var read=new UnloadedSettlementSurvey.Result();yield return UnloadedSettlementSurvey.ReadSteps(location,read);
            if(!valid() || read.Grid==null)yield break;
            var grid=read.Grid;Vector3 arrival;GridCell goal;
            if(origin.Kind==NpcActionTargetKind.IndoorPoint)
            {
                UnloadedSettlementSurvey.Entrance chosen=null;
                foreach(var entrance in grid.Entrances)if(entrance.BuildingKey==origin.BuildingKey){chosen=entrance;break;}
                if(chosen==null || !grid.TryApproach(chosen,out goal))yield break;
                arrival=grid.LocalEntrance(chosen)+chosen.Normal.normalized*0.6f;
            }
            else
            {
                arrival=new Vector3((float)origin.X,(float)origin.Y,(float)origin.Z);
                if(!ClosestSurveyCell(grid,arrival,out goal) || !grid.ClearWalk(grid.LocalCell(goal),arrival,true))yield break;
            }
            var starts=new List<GridCell>();var prefixes=new List<List<Vector3>>();var gateMetres=new List<double>();
            foreach(var gate in grid.Gates)
            {
                GridCell cell;if(!grid.TryGateAccess(gate,out cell))continue;
                starts.Add(cell);prefixes.Add(new List<Vector3>{gate.Outside,gate.Center,gate.Inside,grid.LocalCell(cell)});
                gateMetres.Add(Vector3.Distance(gate.Outside,gate.Center)+Vector3.Distance(gate.Center,gate.Inside));
            }
            if(starts.Count==0)
            {
                var edges=new List<GridCell>();
                for(int x=0;x<grid.Width;x++)foreach(int z in new[]{0,grid.Height-1})if(grid.Cost(x,z)>0)edges.Add(new GridCell(x,z));
                for(int z=1;z<grid.Height-1;z++)foreach(int x in new[]{0,grid.Width-1})if(grid.Cost(x,z)>0)edges.Add(new GridCell(x,z));
                edges.Sort((a,b)=>(grid.LocalCell(a)-arrival).sqrMagnitude.CompareTo((grid.LocalCell(b)-arrival).sqrMagnitude));
                for(int index=0;index<Math.Min(8,edges.Count);index++)
                {starts.Add(edges[index]);prefixes.Add(new List<Vector3>{grid.LocalCell(edges[index])});gateMetres.Add(0);}
            }
            for(int index=0;index<Math.Min(8,starts.Count);index++)
            {
                var route=new UnloadedSettlementSurvey.RouteResult();yield return grid.ReadRouteSteps(starts[index],goal,route,valid,gatesOpen:true);
                if(!valid())yield break;if(route.Points==null || route.Points.Count<1)continue;
                var points=new List<Vector3>(prefixes[index]);var simplified=new List<SavedRoutePoint>();
                foreach(var point in route.Points)simplified.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                foreach(var point in SavedRouteSimplifier.Simplify(simplified))points.Add(new Vector3((float)point.X,0,(float)point.Z));
                points.Add(arrival);if(points.Count>128)continue;
                var approach=new List<NpcActionTarget>();
                foreach(var point in points)approach.Add(new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=origin.SettlementId,
                    WorldSpaceId="settlement:"+origin.SettlementId,X=point.x,Y=point.y,Z=point.z});
                var endpoint=NpcActionExecutor.CopyTarget(approach[approach.Count-1]);endpoint.BuildingKey=origin.BuildingKey;
                var current=store.ReadNpcAction(binding.ActionId);
                if(current==null || current.CompanionRecovery.ExteriorEntrance.SettlementId!=sourceMap)yield break;
                double rate=DaggerfallUnity.Instance.WorldTime.TimeScale/(binding.Actor?binding.Actor.WalkingSpeed:ResidentMovement.WalkSpeedFor(store.ResolveResident(current.ResidentId).AppearanceSeed));
                store.TryCaptureCompanionOverlandReturn(current.Id,current.Revision,DungeonNow(),endpoint,approach,seconds,rate,gateMetres[index]*rate);
                yield break;
            }
        }
        private static bool ClosestSurveyCell(UnloadedSettlementSurvey grid,Vector3 point,out GridCell selected)
        {
            selected=default(GridCell);float best=float.MaxValue;
            var origin=grid.LocalCell(new GridCell(0,0));float tile=64*MeshReader.GlobalScale;
            int centerX=Mathf.RoundToInt((point.x-origin.x)/tile),centerZ=Mathf.RoundToInt((origin.z-point.z)/tile);
            for(int z=Math.Max(0,centerZ-8);z<=Math.Min(grid.Height-1,centerZ+8);z++)
                for(int x=Math.Max(0,centerX-8);x<=Math.Min(grid.Width-1,centerX+8);x++)
                {
                    if(grid.Cost(x,z)==0)continue;var cell=new GridCell(x,z);float distance=(grid.LocalCell(cell)-point).sqrMagnitude;
                    if(distance>=best)continue;best=distance;selected=cell;
                }
            return best<float.MaxValue;
        }

        private bool DepartVisibleCompanionOverland(WorldStateStore store,DungeonFollower binding,NpcActionJob job,double now)
        {
            var actor=binding.Actor;var player=GameManager.Instance.PlayerMotor.transform.position;
            var actual=actor.transform.position-Vector3.up;
            bool observed=Vector3.Distance(actor.transform.position,player)<=165 && CivilianResponses.Sees(player,actor.transform.position+Vector3.up*0.3f,165);
            if(!observed)
            {
                if(!store.TryBeginCompanionOverlandReturn(job.Id,job.Revision,now,now))return false;
                actor.CaptureState();actor.gameObject.SetActive(false);Destroy(actor.gameObject);binding.Actor=null;return true;
            }
            // Walk out of view on actual supported exterior ground. A wall or
            // player following the departure can delay the unobserved leg.
            var away=actual-player;away.y=0;if(away.sqrMagnitude<0.1f)away=actor.transform.forward;
            var next=actual+away.normalized*Mathf.Min(0.12f,(float)actor.WalkingSpeed*Time.deltaTime);RaycastHit floor;
            if(!Physics.Raycast(next+Vector3.up*0.5f,Vector3.down,out floor,1f,~0,QueryTriggerInteraction.Ignore) ||
                floor.normal.y<0.7f || !ClearNativeDoorApproach(actor,actual,floor.point))return true;
            actor.Place(floor.point,away,preferNative:false);return true;
        }
    }
}
