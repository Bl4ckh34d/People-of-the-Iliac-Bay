using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardActor
    {
        internal bool WallCombatActive { get; private set; }
        private DaggerfallEntityBehaviour wallCombatTarget;
        private bool wallCombatDisplaced;
        private Vector3[] wallReturnCorners;
        private int wallReturnIndex;
        private Vector3 wallReturnDestination;
        private float wallReturnRepath;

        // Combat uses EnemyMotor's native obstacle and fall checks. Once combat
        // ends, follow the existing elevated NavMesh back to the ordinary post;
        // never project this body onto the street or snap it back to its post.
        private bool ReturnToWallPost(Vector3 bodyTarget)
        {
            if(!wallCombatDisplaced)return false;
            var destination=bodyTarget-Vector3.up*groundOffset;
            if(Vector3.Distance(GroundPosition,destination)<.12f)
            {wallCombatDisplaced=false;wallReturnCorners=null;return false;}
            if(GameManager.IsGamePaused || entity.IsParalyzed)return true;
            if(wallReturnCorners==null || Time.time>=wallReturnRepath &&
                (destination-wallReturnDestination).sqrMagnitude>.01f)
            {
                wallReturnRepath=Time.time+.5f;wallReturnDestination=destination;
                NavMeshHit start,end;var path=new NavMeshPath();
                if(!NavMesh.SamplePosition(GroundPosition,out start,.6f,NavMesh.AllAreas) ||
                    Mathf.Abs(start.position.y-GroundPosition.y)>.4f ||
                    !NavMesh.SamplePosition(destination,out end,.5f,NavMesh.AllAreas) ||
                    Mathf.Abs(end.position.y-destination.y)>.4f ||
                    !NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) ||
                    path.status!=NavMeshPathStatus.PathComplete)return true;
                foreach(var corner in path.corners)
                    if(Mathf.Abs(corner.y-destination.y)>1.5f)return true;
                wallReturnCorners=path.corners;wallReturnIndex=0;
            }
            while(wallReturnIndex<wallReturnCorners.Length &&
                Vector3.Distance(GroundPosition,wallReturnCorners[wallReturnIndex])<.15f)wallReturnIndex++;
            if(wallReturnIndex>=wallReturnCorners.Length){wallReturnCorners=null;return true;}
            var step=Vector3.MoveTowards(GroundPosition,wallReturnCorners[wallReturnIndex],1.3f*Time.deltaTime)-GroundPosition;
            NavMeshHit edge;
            if(NavMesh.Raycast(GroundPosition,GroundPosition+step,out edge,NavMesh.AllAreas))return true;
            GetComponent<CharacterController>().Move(step);
            var facing=step;facing.y=0;
            if(facing.sqrMagnitude>.00001f)transform.forward=facing.normalized;
            if(mobile.EnemyState!=MobileStates.Move)mobile.ChangeEnemyState(MobileStates.Move);
            return true;
        }
    }
}
