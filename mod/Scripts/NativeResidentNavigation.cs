using System;
using DaggerfallWorkshop.Game;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // A native ground-level agent drives the existing NPC root (ground + 1 m).
    // Keeping the proxy separate preserves DFU's native body and sprite offsets.
    public sealed class NativeResidentNavigation : MonoBehaviour
    {
        private NavMeshAgent agent;
        private GameObject proxy;
        private Vector3 target;
        private bool hasTarget;
        private bool pathInvalidated;
        private int commanded=-100;
        private string scene;
        private readonly Collider[] nearby=new Collider[32];
        private GuardActor guardOrigin;
        private void Awake(){guardOrigin=GetComponent<GuardActor>();}
        private Vector3 OriginOffset
        {get {return guardOrigin?transform.position-guardOrigin.GroundPosition:Vector3.up;}}
        public bool Active {get{return agent && agent.enabled && agent.isOnNavMesh;}}
        public Vector3 Destination {get{return target;}}
        public bool HasDestination {get{return hasTarget && NativeNavigationWorld.Current && NativeNavigationWorld.Current.Scene==scene;}}
        public float DistanceWalked {get;private set;}
        public string FailureReason {get;private set;}
        public string PathState {get{return Active?"hasPath="+agent.hasPath+",pending="+agent.pathPending+
            ",remaining="+agent.remainingDistance.ToString("F5")+",velocity="+agent.velocity.ToString("F5")+
            ",steering="+agent.steeringTarget.ToString("F5")+",status="+agent.pathStatus+
            ",destination="+agent.destination.ToString("F5")+",proxy="+agent.transform.position.ToString("F5")+
            ",stoppingDistance="+agent.stoppingDistance+",radius="+agent.radius:"inactive";}}
        private bool Fail(string reason){FailureReason=reason;Pause();return false;}
        public bool TryMove(Vector3 initialGround,Vector3 requested,double speed,Func<Vector3,bool> permitted)
        {
            var world=NativeNavigationWorld.Current;
            if(!world || !world.Ready || GameManager.IsGamePaused)return Fail("world-unavailable-or-paused");
            var airborne=GetComponent<NativeDungeonJumpTraversal>();
            if(airborne && airborne.Active){airborne.Advance();return true;}
            var surface=GetComponent<NativeDungeonSurfaceTraversal>();
            if(surface && surface.Active){surface.Advance();return true;}
            if(surface && surface.HandledThisFrame)return true;
            if(agent && (scene!=world.Scene || (agent.transform.position-(transform.position-OriginOffset)).sqrMagnitude>9))Pause();
            if(!Active)
            {
                NavMeshHit start;
                var current=hasTarget?transform.position-OriginOffset:initialGround;
                var placement=GetComponent<CrowdMotion>();
                if(placement && !placement.HasPlacement)
                {
                    Vector3 separated;
                    if(!placement.Place(current,out separated,placementAllowed:permitted))return Fail("spawn-occupied");
                    current=separated;
                }
                if(!NavMesh.SamplePosition(current,out start,2,NavMesh.AllAreas))return Fail("start-off-mesh "+current);
                var adjustment=start.position-current;
                if(new Vector2(adjustment.x,adjustment.z).magnitude>0.4f || Mathf.Abs(adjustment.y)>0.6f)
                    return Fail("start-requires-snap "+current);
                // Projection onto the mesh must not undo crowd-safe placement
                // by snapping several newly reconstructed bodies to one point.
                if(Occupied(start.position))return Fail("start-occupied");
                if(!proxy)
                {
                    proxy=new GameObject("Native route "+name);proxy.transform.SetParent(transform.parent,true);
                    proxy.SetActive(false);
                    agent=proxy.AddComponent<NavMeshAgent>();agent.enabled=false;
                    agent.radius=0.30f;agent.height=1.9f;agent.acceleration=6;agent.angularSpeed=180;agent.stoppingDistance=0.10f;
                    agent.autoTraverseOffMeshLink=false;
                    agent.updateRotation=false;agent.obstacleAvoidanceType=ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                    var actor=GetComponent<ResidentActor>();agent.avoidancePriority=actor?20+(int)(actor.Resident.AppearanceSeed%60):50;
                }
                proxy.transform.position=start.position;proxy.SetActive(true);agent.enabled=true;
                if(!agent.isOnNavMesh)return Fail("agent-off-mesh");
                scene=world.Scene;hasTarget=false;transform.position=start.position+OriginOffset;
            }
            commanded=Time.frameCount;agent.speed=(float)speed;
            if(!hasTarget || pathInvalidated || (requested-target).sqrMagnitude>0.09f)
            {
                NavMeshHit end;
                // Sampling plus stopping distance must remain inside the 0.55 m
                // physical door threshold; otherwise the actor can never enter.
                if(!NavMesh.SamplePosition(requested,out end,0.4f,NavMesh.AllAreas))return Fail("destination-off-mesh "+requested);
                // Preserve the caller's exact endpoint identity. Sampling is only
                // for a reachable native standing position near a door/activity.
                var path=new NavMeshPath();
                if(!agent.CalculatePath(end.position,path)||path.status!=NavMeshPathStatus.PathComplete ||
                    !world.ClearCityGatePath(path.corners))
                {
                    if(GetComponent<ResidentActor>() && NativeDungeonJumpTraversal.TryStart(this,end.position,speed,permitted))
                    {target=requested;hasTarget=true;pathInvalidated=true;FailureReason=null;return true;}
                    if(permitted==null && NativeDungeonSurfaceTraversal.TryMove(GetComponent<ResidentActor>(),requested))
                    {target=requested;hasTarget=true;pathInvalidated=true;FailureReason=null;return true;}
                    return Fail("path-incomplete "+requested);
                }
                if(!agent.SetPath(path))return Fail("path-incomplete "+requested);
                target=requested;hasTarget=true;pathInvalidated=false;
            }
            agent.isStopped=false;
            NativeNpcNavigationDoors.OpenAhead(transform.position-OriginOffset,agent.steeringTarget);
            var next=agent.transform.position;
            var bodyGround=transform.position-OriginOffset;
            bool crossing=agent.isOnOffMeshLink;
            Vector3 crossingEnd=crossing?agent.currentOffMeshLinkData.endPos:Vector3.zero;
            if(crossing)
                next=Vector3.MoveTowards(bodyGround,crossingEnd,Mathf.Min(0.5f,(float)speed*Time.deltaTime));
            else if((next-bodyGround).sqrMagnitude>0.25f)
            {
                // Unity integrates the proxy during a long frame too. Keep the
                // visible body within the same bounded step as CrowdMotion.
                next=Vector3.MoveTowards(bodyGround,next,0.5f);
                NavMeshHit edge;
                if(NavMesh.Raycast(bodyGround,next,out edge,NavMesh.AllAreas))next=edge.position;
                agent.Warp(next);pathInvalidated=true;
            }
            bool permissionRejected=permitted!=null&&!permitted(next);
            // A native city gate can close after this route was admitted. Its
            // collider remains authoritative even during an off-mesh crossing.
            if(!permissionRejected && !world.ClearCityGateStep(bodyGround,next))permissionRejected=true;
            bool occupied=!permissionRejected && Occupied(next);
            if(permissionRejected || occupied)
            {
                FailureReason=permissionRejected?"permission-rejected":"occupied";
                // Rejecting every occupied candidate also rejects escape from an
                // existing overlap. Recover locally before resuming native avoidance.
                var separation=GetComponent<CrowdMotion>();
                if(!permissionRejected && separation)separation.TrySeparate(permitted);
                agent.Warp(transform.position-OriginOffset);agent.isStopped=true;pathInvalidated=true;return true;
            }
            float moved=Vector3.Distance(transform.position-OriginOffset,next);
            DistanceWalked+=moved;transform.position=next+OriginOffset;
            if(crossing)
            {
                // Warping or NavMesh.Raycast while crossing a baked gap projects
                // back onto the departure polygon. Advance the native link at
                // walking speed, then return ownership to the native path.
                agent.transform.position=next;agent.nextPosition=next;
                if(Vector3.Distance(next,crossingEnd)<0.01f)agent.CompleteOffMeshLink();
            }
            FailureReason=null;
            var crowd=GetComponent<CrowdMotion>();if(crowd)crowd.RestorePlacement(next);
            return true;
        }
        private bool Occupied(Vector3 ground)
        {
            var crowd=GetComponent<CrowdMotion>();var partner=crowd?crowd.WalkingPartner:null;
            int count=Physics.OverlapCapsuleNonAlloc(ground+Vector3.up*0.8f,ground+Vector3.up*1.6f,0.24f,nearby,~0,QueryTriggerInteraction.Ignore);
            for(int i=0;i<count;i++)
            {
                var collider=nearby[i];
                if(!collider || collider.transform.IsChildOf(transform))continue;
                // Physics.IgnoreCollision does not filter overlap queries.
                // Match the active pair's scoped permission in CrowdMotion.
                if(partner && collider.transform.IsChildOf(partner.transform))continue;
                if(collider.GetComponentInParent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>() ||
                    collider.GetComponentInParent<PlayerMotor>() || collider.GetComponentInParent<DaggerfallWorkshop.DaggerfallActionDoor>())return true;
            }
            return false;
        }
        public void Pause()
        {
            if(agent && agent.enabled)agent.enabled=false;
            var crowd=GetComponent<CrowdMotion>();if(crowd && hasTarget)crowd.RestorePlacement(transform.position-OriginOffset);
        }
        public void Hold()
        {
            if(!Active)return;
            // Preserve this stationary body in native avoidance. Warping the
            // proxy back to its observed body invalidates its path, not its
            // exact requested destination. Recalculate when walking resumes.
            commanded=Time.frameCount;
            agent.Warp(transform.position-OriginOffset);agent.isStopped=true;pathInvalidated=true;
        }
        public void ResetPlacement()
        {
            Pause();hasTarget=false;commanded=-100;
            var crowd=GetComponent<CrowdMotion>();if(crowd)crowd.ResetPlacement();
        }
        private void LateUpdate()
        {
            if(commanded<Time.frameCount || GameManager.IsGamePaused)Pause();
        }
        private void OnDisable(){Pause();}
        private void OnDestroy(){if(proxy)Destroy(proxy);}
    }
}
