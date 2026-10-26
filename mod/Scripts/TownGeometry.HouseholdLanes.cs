using System;
using System.Collections;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class TownGeometry
    {
        public sealed class HouseholdLaneResult {public List<SavedRoutePoint> First,Second;}
        public IEnumerator PrepareHouseholdLanes(IList<SavedRoutePoint> source,HouseholdLaneResult result,Func<bool> valid)
        {
            if(source==null || source.Count<2 || source.Count>10000)yield break;
            bool probe=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-verify-family-cash")>=0;
            var controls=new List<RoutePoint>();foreach(var point in source)controls.Add(new RoutePoint(point.X,point.Z));
            for(int attempt=0;attempt<7;attempt++)
            {
                double width=attempt==6?0:0.36/Math.Pow(2,attempt);
                var first=new List<SavedRoutePoint>();var second=new List<SavedRoutePoint>();
                for(int i=0;i<controls.Count;i++)
                {
                    if(!valid())yield break;
                    double offset=i==0 || i==controls.Count-1?0:width;
                    var normal=offset==0?new RoutePoint(0,0):CorridorLaneRoutes.NormalAt(controls,i);
                    first.Add(new SavedRoutePoint{X=controls[i].X+normal.X*offset,Z=controls[i].Z+normal.Z*offset});
                    second.Add(new SavedRoutePoint{X=controls[i].X-normal.X*offset,Z=controls[i].Z-normal.Z*offset});
                    if(i%16==0)yield return null;
                }
                bool clear=true;
                for(int i=1;i<first.Count;i++)
                {
                    if(!valid())yield break;
                    if(!HouseholdLaneSegmentClear(first[i-1],first[i],i==1,i==first.Count-1) ||
                        !HouseholdLaneSegmentClear(second[i-1],second[i],i==1,i==second.Count-1))
                    {if(attempt==6 && probe)LogHouseholdLaneFailure(first[i-1],first[i],i,first.Count);clear=false;break;}
                    if(i%2==0)yield return null;
                }
                if(clear){result.First=first;result.Second=second;yield break;}
                yield return null;
            }
        }

        private bool HouseholdLaneSegmentClear(SavedRoutePoint from,SavedRoutePoint to,bool startsAtDoor,bool endsAtDoor)
        {
            var a=new RoutePoint(from.X,from.Z);var b=new RoutePoint(to.X,to.Z);
            if(!UsesNativeNavigation)return LaneSegmentClear(a,b);
            // Native corners can span more than the old 24 m grid-lane limit.
            // Use the same baked walk surface as ordinary routes and the mover.
            // Only actual door anchors retain the native route's 0.4 m snap;
            // an offset interior corner may not be rescued across a mesh edge.
            Vector3 startPoint=ScenePoint(a),endPoint=ScenePoint(b);NavMeshHit start,end,blocked;
            if(!NavMesh.SamplePosition(startPoint,out start,0.4f,NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(endPoint,out end,0.4f,NavMesh.AllAreas))return false;
            // ScenePoint is terrain height; a baked surface may be vertically
            // offset. Limit horizontal rescue without rejecting that height.
            var startShift=start.position-startPoint;var endShift=end.position-endPoint;
            if(!startsAtDoor && startShift.x*startShift.x+startShift.z*startShift.z>0.0025f ||
                !endsAtDoor && endShift.x*endShift.x+endShift.z*endShift.z>0.0025f)return false;
            if(!NavMesh.Raycast(start.position,end.position,out blocked,NavMesh.AllAreas))return true;
            // A ray cannot cross the links between baked city modules. Admit
            // a complete native path only when it stays in this lane corridor;
            // a reachable destination via a distant detour is not this segment.
            var path=new NavMeshPath();
            if(!NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) ||
                path.status!=NavMeshPathStatus.PathComplete)return false;
            var delta=end.position-start.position;delta.y=0;
            float lengthSquared=delta.sqrMagnitude;
            float tolerance=(float)HouseholdJointMovement.ArrivalTolerance;
            foreach(var corner in path.corners)
            {
                var offset=corner-start.position;offset.y=0;
                float t=lengthSquared<0.000001f?0:Mathf.Clamp01(Vector3.Dot(offset,delta)/lengthSquared);
                if((offset-delta*t).sqrMagnitude>tolerance*tolerance)return false;
            }
            return true;
        }

        private void LogHouseholdLaneFailure(SavedRoutePoint from,SavedRoutePoint to,int segment,int count)
        {
            var a=ScenePoint(new RoutePoint(from.X,from.Z));var b=ScenePoint(new RoutePoint(to.X,to.Z));
            NavMeshHit start,end,hit;bool sampledA=NavMesh.SamplePosition(a,out start,0.4f,NavMesh.AllAreas);
            bool sampledB=NavMesh.SamplePosition(b,out end,0.4f,NavMesh.AllAreas);
            var path=new NavMeshPath();bool complete=sampledA && sampledB && NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_LANE_REJECT: native="+UsesNativeNavigation+"; segment="+segment+"; points="+count+
                "; length="+Vector3.Distance(a,b)+"; startSample="+sampledA+"; endSample="+sampledB+
                "; startShift="+(start.position-a).ToString("F4")+"; endShift="+(end.position-b).ToString("F4")+
                "; rayBlocked="+(sampledA && sampledB && NavMesh.Raycast(start.position,end.position,out hit,NavMesh.AllAreas))+
                "; completePath="+complete+"; corners="+path.corners.Length);
        }
    }
}
