using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    // Local rendered-body steering. Absolute-time schedules still own destinations.
    public sealed class CrowdMotion : MonoBehaviour
    {
        private static readonly HashSet<CrowdMotion> bodies=new HashSet<CrowdMotion>();
        private float offset,radius=0.4f;
        private bool placed;
        private Vector3 lastTarget;
        public bool HasPlacement { get { return placed; } }
        public void Configure(float groundOffset) { offset=groundOffset; }
        public void ResetPlacement() { placed=false; }
        private void Awake() { bodies.Add(this); }
        private void OnDestroy() { bodies.Remove(this); }
        private Vector3 Ground { get { return transform.position-Vector3.up*offset; } }
        private static GuardPoint3 Point(Vector3 p) { return new GuardPoint3 { X=p.x,Y=p.y,Z=p.z }; }
        private static Vector3 Vector(GuardPoint3 p) { return new Vector3((float)p.X,(float)p.Y,(float)p.Z); }
        private List<GuardPoint3> Neighbours(Vector3 center)
        {
            var result=new List<GuardPoint3>();
            foreach(var body in bodies)
            {
                if(!body || body==this || !body.placed || !body.gameObject.activeInHierarchy) continue;
                var resident=body.GetComponent<ResidentActor>();
                if(resident && resident.IsSleeping) continue;
                var ground=body.Ground;
                if((ground-center).sqrMagnitude<16) result.Add(Point(ground));
            }
            return result;
        }
        public bool Place(Vector3 target,out Vector3 actual)
        {
            var change=placed ? target-lastTarget : Vector3.zero;
            bool reconstruct=!placed || change.sqrMagnitude>4;
            lastTarget=target;
            if(reconstruct)
            {
                var others=Neighbours(target);
                for(int ring=0;ring<=3;ring++) for(int direction=0;direction<(ring==0 ? 1 : 8);direction++)
                {
                    float angle=direction*Mathf.PI*0.25f;
                    var candidate=target+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(ring*0.85f);
                    bool occupied=false;
                    foreach(var other in others)
                    {
                        var delta=candidate-Vector(other); delta.y=0;
                        if(Math.Abs(candidate.y-other.Y)<2.1 && delta.sqrMagnitude<0.82f*0.82f) { occupied=true; break; }
                    }
                    if(!occupied && Clear(target,candidate))
                    {
                        actual=candidate; placed=true; transform.position=actual+Vector3.up*offset; return true;
                    }
                }
                actual=Ground; return false;
            }
            var from=Ground;
            double maximum=Math.Max(ResidentMovement.WalkSpeed*Time.deltaTime,change.magnitude);
            // Long frame stalls must not sweep through a whole crowd in one step.
            maximum=Math.Min(maximum,0.5);
            var result=CrowdSteering.Move(Point(from),Point(target),Neighbours(from),0.82,maximum,
                candidate=>Clear(from,Vector(candidate)));
            actual=Vector(result); transform.position=actual+Vector3.up*offset; return true;
        }
        private bool Clear(Vector3 from,Vector3 to)
        {
            bool supported=false;
            foreach(var hit in Physics.RaycastAll(to+Vector3.up*0.55f,Vector3.down,1.1f,~0,QueryTriggerInteraction.Ignore))
            {
                if(Ignore(hit.collider) || hit.normal.y<0.65f || Mathf.Abs(hit.point.y-to.y)>0.5f) continue;
                supported=true; break;
            }
            if(!supported) return false;
            Vector3 bottom=to+Vector3.up*(0.51f+radius),top=to+Vector3.up*1.65f;
            foreach(var collider in Physics.OverlapCapsule(bottom,top,radius,~0,QueryTriggerInteraction.Ignore))
                if(!Ignore(collider)) return false;
            var delta=to-from;
            if(delta.sqrMagnitude>0.000001f)
                foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*(0.51f+radius),from+Vector3.up*1.65f,
                    radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                    if(!Ignore(hit.collider)) return false;
            return true;
        }
        private bool Ignore(Collider collider)
        {
            if(collider.transform.IsChildOf(transform) || collider.GetComponentInParent<CrowdMotion>()) return true;
            if(collider.GetComponentInParent<StaticNPC>() || collider.GetComponentInParent<MobilePersonNPC>() ||
                collider.GetComponentInParent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>()) return false;
            // Existing door activation owns opening and lock/quest permissions.
            return collider.GetComponentInParent<DaggerfallActionDoor>() || collider.GetComponentInParent<DaggerfallAction>();
        }
    }
}
