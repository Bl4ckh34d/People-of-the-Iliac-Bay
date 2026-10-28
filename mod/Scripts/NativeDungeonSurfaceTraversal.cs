using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Bounded ledges and real native translation platforms. No generated links
    // or path claims: every physical step needs support or a measured climb arc.
    internal sealed class NativeDungeonSurfaceTraversal : MonoBehaviour
    {
        private ResidentActor actor;
        private DaggerfallEntity entity;
        private Vector3 raised,landing;
        private float speed,retryAt,fallVelocity,peak;
        private bool crossing,falling;
        private Transform support;
        private Vector3 supportPoint;
        private int advancedFrame=-1;
        public bool Active {get;private set;}
        public string Outcome {get;private set;}
        internal bool HandledThisFrame {get;private set;}

        internal static bool TryMove(ResidentActor actor,Vector3 target)
        {
            if(!actor || GameManager.IsGamePaused || !GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon)return false;
            var traversal=actor.GetComponent<NativeDungeonSurfaceTraversal>();
            if(!traversal)traversal=actor.gameObject.AddComponent<NativeDungeonSurfaceTraversal>();
            traversal.actor=actor;var body=actor.GetComponent<DaggerfallEntityBehaviour>();traversal.entity=body?body.Entity:null;
            if(traversal.entity==null || traversal.entity.CurrentHealth<=0 || traversal.entity.IsParalyzed)return false;
            if(traversal.Active){traversal.Advance();return true;}
            if(traversal.TryRideOrBoard(target)){traversal.advancedFrame=Time.frameCount;traversal.HandledThisFrame=true;return true;}
            if(Time.unscaledTime<traversal.retryAt)return false;
            traversal.retryAt=Time.unscaledTime+5;
            return traversal.TryClimb(target);
        }
        private bool TryClimb(Vector3 target)
        {
            var from=transform.position-Vector3.up;var flat=target-from;flat.y=0;
            if(flat.magnitude<0.35f || flat.magnitude>1.2f || target.y-from.y<0.55f || target.y-from.y>3f)return false;
            RaycastHit wall,floor;
            if(!Physics.Raycast(from+Vector3.up*0.5f,flat.normalized,out wall,flat.magnitude,~0,QueryTriggerInteraction.Ignore) ||
                Mathf.Abs(wall.normal.y)>0.2f || !StaticDungeon(wall.collider) || !Floor(target,out floor) || !StaticDungeon(floor.collider))return false;
            NavMeshHit mesh;
            if(!NavMesh.SamplePosition(floor.point,out mesh,0.2f,NavMesh.AllAreas) || Vector3.Distance(mesh.position,floor.point)>0.2f)return false;
            landing=floor.point;raised=new Vector3(from.x,landing.y,from.z);
            if(!ClearRoute(from,raised) || !ClearRoute(raised,landing))return false;
            int skill=entity.Skills.GetLiveSkillValue(DFCareer.Skills.Climbing);
            int chance=NpcClimbingCapability.StartChance(skill,entity.Stats.GetLiveStatValue(DFCareer.Stats.Luck),entity.IsEnhancedClimbing);
            if(Dice100.FailedRoll(chance)){Outcome="native-climbing-skill-failed";return false;}
            var native=GetComponent<NativeResidentNavigation>();if(native)native.Pause();
            speed=(float)NpcClimbingCapability.Speed(actor.WalkingSpeed,entity.IsEnhancedClimbing);
            peak=from.y;crossing=false;falling=false;fallVelocity=0;Active=true;Outcome="climbing";Advance();return true;
        }
        internal void Advance()
        {
            if(!Active || GameManager.IsGamePaused || advancedFrame==Time.frameCount)return;advancedFrame=Time.frameCount;
            if(!GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon || entity==null || entity.CurrentHealth<=0)
            {Active=false;return;}
            var from=transform.position-Vector3.up;float dt=Mathf.Min(Time.deltaTime,0.05f);Vector3 next;
            if(entity.IsParalyzed)falling=true;
            if(falling)
            {
                fallVelocity+=AcrobatMotor.defaultGravity*dt;next=from-Vector3.up*Mathf.Min(0.25f,fallVelocity*dt);
                RaycastHit floor;
                if(Physics.Raycast(from+Vector3.up*0.05f,Vector3.down,out floor,from.y-next.y+0.08f,~0,QueryTriggerInteraction.Ignore) && floor.normal.y>=0.7f)
                {Move(floor.point);Finish(false);return;}
                if(ClearRoute(from,next))Move(next);return;
            }
            next=Vector3.MoveTowards(from,crossing?landing:raised,Mathf.Min(0.12f,speed*dt));
            if(!ClearRoute(from,next)){falling=true;Outcome="climb-interrupted-fall";return;}
            Move(next);peak=Mathf.Max(peak,next.y);
            if(Vector3.Distance(next,crossing?landing:raised)<=0.01f)
            {if(!crossing)crossing=true;else Finish(true);}
        }
        private void Finish(bool reached)
        {
            Active=false;Outcome=reached?"climbed":"climb-fall-landed";
            float fall=peak-(transform.position.y-1);if(fall>5)entity.DecreaseHealth((int)(5*(fall-5)));
            if(reached)
            {
                int gain;
                if(NativeNpcCompanionExperience.TryAwardSuccessfulUse(actor.CompanionStateStore,actor.Resident.Id,
                    (int)DFCareer.Skills.Climbing,ResidentActor.CompanionNow(),out gain) && gain>0)
                    entity.Skills.SetPermanentSkillValue(DFCareer.Skills.Climbing,(short)Math.Min(100,entity.Skills.GetPermanentSkillValue(DFCareer.Skills.Climbing)+gain));
            }
            actor.HoldPhysicalPosition();
        }
        private bool TryRideOrBoard(Vector3 target)
        {
            var from=transform.position-Vector3.up;RaycastHit floor;
            if(!Floor(from,out floor)){support=null;return false;}
            var action=floor.collider.GetComponentInParent<DaggerfallAction>();
            if(action && NativeNavigationGeometry.IsDynamicNavigationBody(floor.collider))
            {
                if(support==action.transform)
                {
                    var carried=support.TransformPoint(supportPoint);var delta=carried-from;
                    if(delta.magnitude>0.5f || !ClearRoute(from,carried)){support=null;return false;}
                    Move(carried);from=carried;
                }
                support=action.transform;supportPoint=support.InverseTransformPoint(from);
                var toward=target-from;toward.y=0;
                var next=from+Vector3.ClampMagnitude(toward,Mathf.Min(0.12f,(float)actor.WalkingSpeed*Time.deltaTime));
                RaycastHit nextFloor;
                if(toward.magnitude>0.7f && Floor(next,out nextFloor) && Mathf.Abs(nextFloor.point.y-from.y)<0.15f && ClearRoute(from,nextFloor.point))
                {Move(nextFloor.point);supportPoint=support.InverseTransformPoint(nextFloor.point);}
                Outcome="riding-native-platform";return true;
            }
            support=null;
            // Boarding happens only when the platform is physically alongside
            // the floor; no unsupported step or trigger-driven movement.
            var deltaTarget=target-from;deltaTarget.y=0;if(deltaTarget.magnitude>4)return false;
            var step=from+Vector3.ClampMagnitude(deltaTarget,Mathf.Min(0.12f,(float)actor.WalkingSpeed*Time.deltaTime));
            RaycastHit ahead;if(!Floor(step,out ahead) || !ahead.collider.GetComponentInParent<DaggerfallAction>() ||
                !NativeNavigationGeometry.IsDynamicNavigationBody(ahead.collider) || Mathf.Abs(ahead.point.y-from.y)>0.15f || !ClearRoute(from,ahead.point))return false;
            Move(ahead.point);Outcome="boarding-native-platform";return true;
        }
        private bool Floor(Vector3 point,out RaycastHit hit)
        {return Physics.Raycast(point+Vector3.up*0.20f,Vector3.down,out hit,0.4f,~0,QueryTriggerInteraction.Ignore) && hit.normal.y>=0.7f &&
            hit.collider && !hit.collider.GetComponentInParent<DaggerfallEntityBehaviour>();}
        private static bool StaticDungeon(Collider collider)
        {var dungeon=GameManager.Instance.PlayerEnterExit.Dungeon;return dungeon && collider.transform.IsChildOf(dungeon.transform) && !NativeNavigationGeometry.IsDynamicNavigationBody(collider);}
        private bool ClearRoute(Vector3 from,Vector3 to)
        {
            var delta=to-from;
            foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*0.34f,from+Vector3.up*1.7f,0.28f,delta.normalized,
                delta.magnitude,~0,QueryTriggerInteraction.Collide))if(Blocks(hit.collider))return false;
            foreach(var collider in Physics.OverlapCapsule(to+Vector3.up*0.34f,to+Vector3.up*1.7f,0.28f,~0,QueryTriggerInteraction.Collide))if(Blocks(collider))return false;
            return true;
        }
        private bool Blocks(Collider collider)
        {return collider && !collider.transform.IsChildOf(transform) && (!collider.isTrigger || collider.GetComponentInParent<DaggerfallEntityBehaviour>() || collider.GetComponentInParent<PlayerMotor>());}
        private void Move(Vector3 ground)
        {transform.position=ground+Vector3.up;var crowd=GetComponent<CrowdMotion>();if(crowd)crowd.RestorePlacement(ground);}
        private void LateUpdate(){Advance();HandledThisFrame=false;}
        private void OnDisable(){Active=false;support=null;}
    }
}
