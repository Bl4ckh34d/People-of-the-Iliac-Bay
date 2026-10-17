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
        public const float BodyRadius=0.25f;
        public const float Separation=BodyRadius*2+0.02f;
        private float offset;
        private const float radius=BodyRadius;
        private bool placed;
        private Vector3 lastTarget;
        public bool HasPlacement { get { return placed; } }
        private bool resumeWithoutReconstruction;
        private int comfortPassingSide;
        private double comfortLateralOffset;
        public void ResumeFromPause() { resumeWithoutReconstruction=true; }
        public void Configure(float groundOffset) { offset=groundOffset; }
        public void ResetPlacement() { placed=false; comfortPassingSide=0; comfortLateralOffset=0; }
        // A saved body already has a known location. Do not run the new-spawn
        // ring search, which can teleport it when another body reconstructs first.
        public void RestorePlacement(Vector3 ground)
        {
            transform.position=ground+Vector3.up*offset;
            lastTarget=ground;placed=true;resumeWithoutReconstruction=true;
            comfortPassingSide=0;comfortLateralOffset=0;
        }
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
        public List<Vector3> NeighbourPositions(Vector3 center)
        {
            var result=new List<Vector3>();
            foreach(var point in Neighbours(center)) result.Add(Vector(point));
            return result;
        }
        public bool Place(Vector3 target,out Vector3 actual,bool comfortWalking=true)
        {
            var change=placed ? target-lastTarget : Vector3.zero;
            bool physicalTarget=resumeWithoutReconstruction;
            bool reconstruct=!placed || change.sqrMagnitude>4 && !resumeWithoutReconstruction;
            resumeWithoutReconstruction=false;
            lastTarget=target;
            if(reconstruct)
            {
                comfortPassingSide=0;
                comfortLateralOffset=0;
                var others=Neighbours(target);
                for(int ring=0;ring<=3;ring++) for(int direction=0;direction<(ring==0 ? 1 : 8);direction++)
                {
                    float angle=direction*Mathf.PI*0.25f;
                    var candidate=target+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*(ring*(Separation+0.03f));
                    bool occupied=false;
                    foreach(var other in others)
                    {
                        var delta=candidate-Vector(other); delta.y=0;
                        if(Math.Abs(candidate.y-other.Y)<2.1 && delta.sqrMagnitude<Separation*Separation) { occupied=true; break; }
                    }
                    if(!occupied && Clear(target,candidate))
                    {
                        actual=candidate; placed=true; transform.position=actual+Vector3.up*offset; return true;
                    }
                }
                // A failed first placement has no valid body to show. A visible
                // body already on supported ground must not vanish merely because
                // the schedule's new target cannot currently be reconstructed.
                actual=Ground; return placed;
            }
            var from=Ground;
            double maximum=physicalTarget ? ResidentMovement.WalkSpeed*Time.deltaTime :
                Math.Max(ResidentMovement.WalkSpeed*Time.deltaTime,change.magnitude);
            // Long frame stalls must not sweep through a whole crowd in one step.
            maximum=Math.Min(maximum,0.5);
            if(!comfortWalking) { comfortPassingSide=0; comfortLateralOffset=0; }
            var neighbours=Neighbours(from);
            var result=comfortWalking ? CrowdSteering.MoveComfort(Point(from),Point(target),neighbours,Separation,1.1,1.8,
                maximum,ref comfortPassingSide,ref comfortLateralOffset,Point(change),candidate=>Clear(from,Vector(candidate)),true) :
                CrowdSteering.Move(Point(from),Point(target),neighbours,Separation,maximum,
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
