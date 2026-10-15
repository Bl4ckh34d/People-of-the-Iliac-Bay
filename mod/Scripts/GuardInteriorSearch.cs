using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class GuardActor
    {
        private List<GuardPoint3> indoorSearchRoute;
        private int indoorSearchIndex;

        public void SearchInterior(DaggerfallInterior room,double now)
        {
            var guard=roster.Guards.Find(g=>g.Id==GuardId);
            if(!IsInterior || guard.Interior==null || guard.Interior.Returning || CombatReady || entity.IsParalyzed) return;
            var destination=guard.Interior.LastSeenSuspect;
            if(destination==null) return;
            if(indoorSearchRoute==null)
            {
                var capsule=GetComponent<CharacterController>();
                var geometry=new InteriorGeometry(room,capsule.radius+capsule.skinWidth,
                    capsule.height-capsule.radius+capsule.skinWidth);
                var from=room.transform.InverseTransformPoint(transform.position-Vector3.up*groundOffset);
                var to=new Vector3((float)destination.X,(float)destination.Y-1,(float)destination.Z);
                indoorSearchRoute=geometry.RouteBetween(from,to); indoorSearchIndex=1;
                Debug.Log("LIVING_CITIES_GUARD_INDOOR_SEARCH: "+GuardId+"; points="+indoorSearchRoute.Count+"; from="+from+"; to="+to);
            }
            if(indoorSearchRoute.Count<2 || indoorSearchIndex>=indoorSearchRoute.Count) return;
            var point=indoorSearchRoute[indoorSearchIndex];
            var target=room.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y+groundOffset,(float)point.Z));
            InteriorGeometry.OpenOrdinaryDoors(transform.position-Vector3.up*groundOffset,target-Vector3.up*groundOffset);
            var step=Vector3.MoveTowards(transform.position,target,2.8f*Time.deltaTime)-transform.position;
            var controller=GetComponent<CharacterController>();
            controller.Move(step);
            var facing=step; facing.y=0;
            if(facing.sqrMagnitude>0.00001f) transform.forward=facing.normalized;
            if(mobile.EnemyState!=MobileStates.Move) mobile.ChangeEnemyState(MobileStates.Move);
            if(Vector3.Distance(transform.position,target)<0.15f) indoorSearchIndex++;
            Capture(now);
        }
    }
}
