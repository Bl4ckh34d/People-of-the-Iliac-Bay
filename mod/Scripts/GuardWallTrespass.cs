using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private readonly List<DaggerfallEntityBehaviour> wallIntruders=new List<DaggerfallEntityBehaviour>();
        private float nextWallIntruderSurvey;

        private void SurveyWallIntruders(double now)
        {
            if(Time.unscaledTime<nextWallIntruderSurvey)return;
            nextWallIntruderSurvey=Time.unscaledTime+1;
            wallIntruders.Clear();
            if(!GuardShifts.WallTrespassEnforced(now,HauntingShelterNow(now)))return;
            foreach(var target in Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if(IsWallIntruder(target))wallIntruders.Add(target);
        }

        internal bool IsWallIntruder(DaggerfallEntityBehaviour target)
        {
            if(!target || !target.gameObject.activeInHierarchy || target.Entity==null || target.Entity.CurrentHealth<=0 ||
                target.GetComponent<GuardActor>())return false;
            var enemy=target.Entity as EnemyEntity;
            if(enemy!=null && enemy.MobileEnemy.ID==(int)MobileTypes.Knight_CityWatch)return false;
            var controller=target.GetComponent<CharacterController>();
            if(!controller)return false;
            // A real floor immediately below the body establishes its storey.
            // The same x/z position on the street must never qualify.
            var foot=target.transform.TransformPoint(controller.center)-Vector3.up*(controller.height*.5f);
            RaycastHit floor;
            if(!Physics.Raycast(foot+Vector3.up*.5f,Vector3.down,out floor,1.3f,~0,QueryTriggerInteraction.Ignore) || floor.normal.y<.65f)return false;
            var block=floor.collider.GetComponentInParent<DaggerfallRMBBlock>();
            if(!block)return false;
            var local=directory.transform.InverseTransformPoint(floor.point);
            if(floor.point.y-geometry.ScenePoint(new RoutePoint(local.x,local.z)).y<3)return false;
            NavMeshHit navigation;
            if(!NavMesh.SamplePosition(floor.point,out navigation,.5f,NavMesh.AllAreas) || Mathf.Abs(navigation.position.y-floor.point.y)>.4f)return false;
            // Only an upper surface connected to a surveyed native wall access
            // qualifies. A nearby roof, stair interior or lower street does not.
            if(wallWatchBlocks.Contains(block))foreach(var site in wallWatchSites)
            {
                var point=site.Wall[site.Wall.Count-1];
                var post=directory.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                if(Mathf.Abs(post.y-navigation.position.y)>1.5f || Vector3.Distance(post,navigation.position)>25)continue;
                var path=new NavMeshPath();
                if(NavMesh.CalculatePath(post,navigation.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete)return true;
            }
            // A current-save load retains the surveyed journey and reached post,
            // while the transient whole-town survey caches rebuild separately.
            // Revalidate that one actual post against this loaded floor and mesh;
            // never make correct wall law wait for every wall site to be surveyed.
            foreach(var guard in Roster.Guards)
            {
                var journey=guard.Refuge;
                if(!guard.IsAlive || journey==null || journey.WallRoute==null ||
                    journey.Evaluate(Now()).Phase!=RefugePhase.WallPosted)continue;
                var point=journey.WallRoute[journey.WallRoute.Count-1];
                var post=directory.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                if(Mathf.Abs(post.y-navigation.position.y)>1.5f || Vector3.Distance(post,navigation.position)>25)continue;
                RaycastHit support;NavMeshHit actualPost;
                if(!Physics.Raycast(post+Vector3.up*.5f,Vector3.down,out support,1.3f,~0,QueryTriggerInteraction.Ignore) ||
                    support.normal.y<.65f || support.collider.GetComponentInParent<DaggerfallRMBBlock>()!=block ||
                    Mathf.Abs(support.point.y-post.y)>.4f || !NavMesh.SamplePosition(support.point,out actualPost,.5f,NavMesh.AllAreas) ||
                    Mathf.Abs(actualPost.position.y-support.point.y)>.4f)continue;
                var path=new NavMeshPath();
                if(NavMesh.CalculatePath(actualPost.position,navigation.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete)return true;
            }
            return false;
        }

        internal bool WallGuardSees(GuardActor actor,DaggerfallEntityBehaviour target)
        {
            if(!target || target.Entity.IsInvisible)return false;
            var senses=actor.GetComponent<EnemySenses>();
            var mobile=actor.GetComponentInChildren<MobileUnit>();
            var delta=target.transform.position-actor.transform.position;
            if(delta.magnitude>=senses.SightRadius+mobile.Enemy.SightModifier ||
                Vector3.Angle(delta,actor.transform.forward)>=senses.FieldOfView*.5f)return false;
            return CivilianResponses.Sees(actor.transform.position,target.transform.position,senses.SightRadius);
        }
    }
}
