using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardActor
    {
        private List<Vector3> welfareRoute;
        private int welfareStep;
        private Vector3 welfareDestination;
        private float welfareRepath;
        private string welfareIncident;

        public bool? ApproachWelfare(string incident,Vector3 destination,DaggerfallInterior room,double now)
        {
            if(welfareIncident!=incident) { welfareIncident=incident; welfareRoute=null; }
            var guard=roster.Guards.Find(g=>g.Id==GuardId);
            if(!guard.IsAlive || entity.IsParalyzed) return null;
            EndCombat();
            if(guard.Duty!=GuardDuty.Engaged) roster.Engage(GuardId,now);
            if(Vector3.Distance(transform.position,destination)<2.5f &&
                CivilianResponses.Sees(transform.position,destination,4))
            {
                if(mobile.EnemyState!=MobileStates.Idle) mobile.ChangeEnemyState(MobileStates.Idle);
                Capture(now); return true;
            }
            if(welfareRoute==null || Time.time>=welfareRepath &&
                (welfareStep>=welfareRoute.Count || (destination-welfareDestination).sqrMagnitude>1))
            {
                welfareDestination=destination; welfareRepath=Time.time+1; welfareStep=1;
                welfareRoute=new List<Vector3>();
                var capsule=GetComponent<CharacterController>();
                if(room)
                {
                    var navigation=new InteriorGeometry(room,capsule.radius+capsule.skinWidth,
                        capsule.height-capsule.radius+capsule.skinWidth);
                    var from=room.transform.InverseTransformPoint(transform.position-Vector3.up*groundOffset);
                    var to=room.transform.InverseTransformPoint(destination-Vector3.up);
                    var path=navigation.RouteBetween(from,to);
                    if(path.Count<2)
                    {
                        var candidates=new List<Vector3>(navigation.LocalAnchors);
                        candidates.Sort((a,b)=>(a-to).sqrMagnitude.CompareTo((b-to).sqrMagnitude));
                        foreach(var candidate in candidates)
                        {
                            if((candidate-to).sqrMagnitude>4) break;
                            path=navigation.RouteBetween(from,candidate);
                            if(path.Count>=2) break;
                        }
                    }
                    foreach(var point in path)
                        welfareRoute.Add(room.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y+groundOffset,(float)point.Z)));
                }
                else
                {
                    var from=transform.parent.InverseTransformPoint(transform.position);
                    var to=transform.parent.InverseTransformPoint(destination);
                    foreach(var point in geometry.RouteBetweenPoints(new SavedRoutePoint { X=from.x,Z=from.z },new SavedRoutePoint { X=to.x,Z=to.z }))
                        welfareRoute.Add(geometry.ScenePoint(new RoutePoint(point.X,point.Z))+Vector3.up*groundOffset);
                }
            }
            if(welfareRoute.Count<2) return null;
            if(welfareStep<welfareRoute.Count)
            {
                var target=welfareRoute[welfareStep];
                if(room) InteriorGeometry.OpenOrdinaryDoors(transform.position-Vector3.up*groundOffset,target-Vector3.up*groundOffset);
                var step=Vector3.MoveTowards(transform.position,target,2.8f*Time.deltaTime)-transform.position;
                GetComponent<CharacterController>().Move(step);
                var facing=step; facing.y=0;
                if(facing.sqrMagnitude>0.00001f) transform.forward=facing.normalized;
                if(mobile.EnemyState!=MobileStates.Move) mobile.ChangeEnemyState(MobileStates.Move);
                if(Vector3.Distance(transform.position,target)<0.15f) welfareStep++;
            }
            Capture(now); return false;
        }
    }
}
