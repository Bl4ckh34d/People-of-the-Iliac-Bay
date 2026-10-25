using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Static layout visibility for cached activity anchors. Passing pedestrians
    // must not permanently invalidate a shelf or counter approach.
    public static class InteriorActivitySight
    {
        public static bool Visible(Vector3 from, Vector3 target, Transform targetOwner)
        {
            Vector3 delta=target-from;
            float length=delta.magnitude;
            if(length<0.0001f) return true;
            float closest=length+0.001f;
            bool blocked=false;
            foreach(var hit in Physics.RaycastAll(from,delta/length,length,~0,QueryTriggerInteraction.Ignore))
            {
                if(!hit.collider || hit.distance>=closest) continue;
                bool isTarget=targetOwner && (hit.transform==targetOwner || hit.transform.IsChildOf(targetOwner));
                if(!isTarget && (hit.collider.GetComponentInParent<CrowdMotion>() ||
                    hit.collider.GetComponentInParent<StaticNPC>() ||
                    hit.collider.GetComponentInParent<MobilePersonNPC>() ||
                    hit.collider.GetComponentInParent<DaggerfallEntityBehaviour>() ||
                    hit.collider.GetComponentInParent<PlayerMotor>())) continue;
                closest=hit.distance;
                blocked=!isTarget;
            }
            return !blocked;
        }
    }
}
