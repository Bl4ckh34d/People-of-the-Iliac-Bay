using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    // Local rendered-body steering. Absolute-time schedules still own destinations.
    public sealed class CrowdMotion : MonoBehaviour
    {
        private static readonly HashSet<CrowdMotion> bodies=new HashSet<CrowdMotion>();
        public const float BodyRadius=0.25f;
        public const float Separation=BodyRadius*2+0.02f;
        private float offset;
        private double walkingSpeed=ResidentMovement.WalkSpeed;
        private const float radius=BodyRadius;
        private bool placed;
        private Vector3 lastTarget;
        public bool HasPlacement { get { return placed; } }
        internal CrowdMotion WalkingPartner;
        private bool resumeWithoutReconstruction;
        private int comfortPassingSide;
        private double comfortLateralOffset;
        private SelectedSeatAccess occupiedSeat;
        public bool TrySeparate(Func<Vector3,bool> placementAllowed=null)
        {
            if(!placed || occupiedSeat!=null || GameManager.IsGamePaused)return false;
            var from=Ground;
            // The current paired movement frame explicitly permits its two
            // bodies to converge at doors. Recovery must honour that same
            // exception; otherwise it pushes an arriving partner back forever.
            var result=CrowdSteering.Separate(Point(from),Neighbours(from),Separation,
                Math.Min(0.08,walkingSpeed*Time.deltaTime),(GetInstanceID()&15)*Math.PI/8,
                p=>(placementAllowed==null || placementAllowed(Vector(p))) && Clear(from,Vector(p)));
            var next=Vector(result);
            if((next-from).sqrMagnitude<0.00000001f)return false;
            transform.position=next+Vector3.up*offset;return true;
        }
        public void ResumeFromPause() { resumeWithoutReconstruction=true; }
        public void Configure(float groundOffset,double speed=ResidentMovement.WalkSpeed) { offset=groundOffset;walkingSpeed=speed; }
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
        private void OnDestroy() {if(occupiedSeat!=null)occupiedSeat.Release();bodies.Remove(this);}
        private void OnDisable() {if(occupiedSeat!=null)occupiedSeat.Release();}
        private Vector3 Ground { get { return transform.position-Vector3.up*offset; } }
        private static GuardPoint3 Point(Vector3 p) { return new GuardPoint3 { X=p.x,Y=p.y,Z=p.z }; }
        private static Vector3 Vector(GuardPoint3 p) { return new Vector3((float)p.X,(float)p.Y,(float)p.Z); }
        private List<GuardPoint3> Neighbours(Vector3 center)
        {
            var result=new List<GuardPoint3>();
            foreach(var body in bodies)
            {
                if(!body || body==this || body==WalkingPartner || !body.placed || !body.gameObject.activeInHierarchy) continue;
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
        // A bounded route-rejoin query, using the same physical checks as a
        // movement step. It does not move the body or change passing state.
        public bool CanTraverseTo(Vector3 target)
        {
            if(!placed || float.IsNaN(target.x) || float.IsInfinity(target.x) ||
                float.IsNaN(target.y) || float.IsInfinity(target.y) ||
                float.IsNaN(target.z) || float.IsInfinity(target.z))return false;
            var from=Ground;float distance=Vector3.Distance(from,target);
            if(distance>4.5f || Mathf.Abs(target.y-from.y)>0.5f)return false;
            return CrowdSteering.CanTraverse(Point(from),Point(target),Neighbours(from),Separation,candidate=>{
                int samples=Math.Max(1,Mathf.CeilToInt(distance/0.25f));
                Vector3 previous=from;
                for(int i=1;i<=samples;i++)
                {
                    var point=Vector3.Lerp(from,target,i/(float)samples);
                    if(!Clear(previous,point))return false;
                    previous=point;
                }
                return true;
            });
        }
        public bool Place(Vector3 target,out Vector3 actual,bool comfortWalking=true,bool waitAtOccupiedTarget=false,SelectedSeatAccess seatAccess=null,Func<Vector3,bool> placementAllowed=null)
        {
            if(seatAccess!=null){var owner=GetComponent<ResidentActor>();if(!owner||seatAccess.Owner!=owner.Resident.Id)seatAccess=null;}
            var permission=seatAccess!=null&&seatAccess.Valid?seatAccess:null;
            SeatFurniture egress=null;
            if(occupiedSeat!=null && permission!=occupiedSeat)
            {
                if(permission==null)occupiedSeat.Release();
                if(occupiedSeat.Slot.Furniture.Blocks(Ground,Ground,radius))egress=occupiedSeat.Slot.Furniture;
                else occupiedSeat=null;
            }
            if(permission!=null)occupiedSeat=permission;
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
                    if(!occupied && (placementAllowed==null || placementAllowed(candidate)) && Clear(target,candidate,permission,egress))
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
            if(permission==null && TrySeparate(placementAllowed))
            {actual=Ground;return true;}
            double maximum=physicalTarget ? walkingSpeed*Time.deltaTime :
                Math.Max(walkingSpeed*Time.deltaTime,change.magnitude);
            // Long frame stalls must not sweep through a whole crowd in one step.
            maximum=Math.Min(maximum,0.5);
            if(!comfortWalking) { comfortPassingSide=0; comfortLateralOffset=0; }
            var neighbours=Neighbours(from);
            bool waiting=waitAtOccupiedTarget && CrowdSteering.Occupied(Point(target),neighbours,Separation);
            var result=comfortWalking || waiting ? CrowdSteering.MoveComfort(Point(from),Point(target),neighbours,Separation,1.1,1.8,
                    maximum,ref comfortPassingSide,ref comfortLateralOffset,Point(change),candidate=>
                        (placementAllowed==null || placementAllowed(Vector(candidate))) && Clear(from,Vector(candidate),permission,egress),true,waitAtOccupiedTarget) :
                CrowdSteering.Move(Point(from),Point(target),neighbours,Separation,maximum,
                    candidate=>(placementAllowed==null || placementAllowed(Vector(candidate))) && Clear(from,Vector(candidate),permission,egress));
            actual=Vector(result); transform.position=actual+Vector3.up*offset; return true;
        }
        private bool Clear(Vector3 from,Vector3 to,SelectedSeatAccess permission=null,SeatFurniture egress=null)
        {
            SeatFurniture allowed=permission!=null&&permission.Permits(from,to)?permission.Slot.Furniture:null;
            if(allowed==null&&egress!=null)
            {
                var center=egress.Matrix.MultiplyPoint3x4(egress.Bounds.center);center.y=from.y;
                if((to-center).sqrMagnitude+0.0001f>=(from-center).sqrMagnitude)allowed=egress;
            }
            if(FurnitureSeatCatalog.Blocks(from,to,radius,allowed))return false;
            bool supported=false;
            foreach(var hit in Physics.RaycastAll(to+Vector3.up*(allowed==null?0.55f:0.15f),Vector3.down,allowed==null?1.1f:0.35f,~0,QueryTriggerInteraction.Ignore))
            {
                if(Ignore(hit.collider) || hit.normal.y<0.65f || Mathf.Abs(hit.point.y-to.y)>(allowed==null?0.5f:0.15f) || allowed!=null&&allowed.IsSurface(hit)) continue;
                supported=true; break;
            }
            if(!supported) return false;
            Vector3 bottom=to+Vector3.up*(0.51f+radius),top=to+Vector3.up*1.65f;
            foreach(var collider in Physics.OverlapCapsule(bottom,top,radius,~0,QueryTriggerInteraction.Ignore))
                if(!Ignore(collider) && (allowed==null || !allowed.Allows(collider,from,to,radius,permission!=null))) return false;
            var delta=to-from;
            if(delta.sqrMagnitude>0.000001f)
                foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*(0.51f+radius),from+Vector3.up*1.65f,
                    radius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                    if(!Ignore(hit.collider) && (allowed==null || !allowed.Allows(hit.collider,from,to,radius,permission!=null))) return false;
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
