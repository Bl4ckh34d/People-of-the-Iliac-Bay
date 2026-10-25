using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class HauntingPatrolProbe
    {
        private static double Now()
        {
            var time=DaggerfallUnity.Instance.WorldTime.Now;
            return (double)time.ToSeconds()+time.Second-Math.Floor(time.Second);
        }

        public static IEnumerator Run(BuildingDirectory directory,TownGeometry geometry,List<RoutePoint> route)
        {
            var game=GameManager.Instance;
            bool oldCurse=game.PlayerEntity.GlobalVars.GetGlobalVar(0);
            var oldCrime=game.PlayerEntity.CrimeCommitted;
            Vector3 oldPlayerPosition=game.PlayerMotor.transform.position;
            var root=new GameObject("People of the Iliac Bay haunting patrol probe");
            GameObject foe=null;
            var controller=root.AddComponent<GuardPopulation>();
            controller.enabled=false;
            controller.EnemyDefense=false;
            controller.NightPatrols=false;
            controller.MaximumPatrolPairs=1;
            try
            {
                double now=Now();
                // Lift the flag only while preparing the surveyed patrol route. The
                // native probe runs its actual hostile observation under active curse.
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,true);
                if(SettlementHaunting.StreetsDangerous(directory.MapID,true,now))
                    throw new InvalidOperationException("Patrol probe setup must begin before the haunting-night fixture.");
                if(route==null || route.Count<6)
                    throw new InvalidOperationException("Real patrol probe needs six surveyed post points.");
                var posts=new List<SavedRoutePoint>();
                for(int i=0;i<6;i++) posts.Add(new SavedRoutePoint { X=route[i].X,Z=route[i].Z });
                var roster=GuardRoster.Create(directory.MapID,42,6,posts);
                var type=typeof(GuardPopulation);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                type.GetProperty("Roster").GetSetMethod(true).Invoke(controller,new object[] { roster });
                type.GetField("directory",flags).SetValue(controller,directory);
                type.GetField("geometry",flags).SetValue(controller,geometry);
                // The surveyed patrol fixture owns no gate stations. Gate staffing
                // would otherwise recruit/reassign its small controlled roster.
                type.GetField("gateDutySurveyed",flags).SetValue(controller,true);

                // Survey real native town routes before entering the cursed night.
                controller.enabled=true;
                yield return (IEnumerator)type.GetMethod("SurveyPatrols",flags).Invoke(controller,null);
                controller.enabled=false;
                var templates=(List<PatrolPairPlan>)type.GetField("patrolTemplates",flags).GetValue(controller);
                if(templates.Count==0) throw new InvalidOperationException("No real city patrol circuit surveyed.");

                double night=Math.Floor(Now()/86400)*86400+19*3600;
                if(night<Now()) night+=86400;
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(night-Now()));
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,false); // LiftedCurse=false: active haunting.
                now=Now();
                if(!SettlementHaunting.StreetsDangerous(directory.MapID,false,now))
                    throw new InvalidOperationException("Patrol response probe requires active haunting at night.");
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;

                var template=templates[0];
                var plan=PatrolPairPlan.Create(template.LeaderId,template.FollowerId,template.LeaderJoin,
                    template.FollowerJoin,template.Circuit,now,now+3600,1.3,12,2);
                roster.StartPatrol(plan,now);
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(plan.CircuitStartsAt-Now()+24));
                var guard=roster.Guards.Find(g=>g.Id==plan.LeaderId);
                var before=roster.Position(guard.Id,Now());
                if(!roster.IsPatrolMoving(guard.Id,Now()))
                    throw new InvalidOperationException("Fixture guard is not moving on the surveyed patrol.");

                game.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(before.X,before.Z))+Vector3.up*2;
                foe=GameObjectHelper.CreateFoeGameObjects(geometry.ScenePoint(new RoutePoint(before.X,before.Z))+Vector3.up,
                    MobileTypes.Wraith,1,MobileReactions.Hostile)[0];
                foe.GetComponent<EnemyMotor>().enabled=false;
                foe.GetComponent<EnemyAttack>().enabled=false;
                foe.SetActive(true);
                yield return null;
                foe.GetComponent<EnemyMotor>().IsHostile=true;
                controller.NightPatrols=true;
                controller.enabled=true;

                float observeUntil=Time.realtimeSinceStartup+2;
                while(Time.realtimeSinceStartup<observeUntil) yield return null;
                if(guard.Refuge!=null || guard.Duty!=GuardDuty.Posted || !roster.IsOnPatrol(guard.Id) ||
                    !guard.IsAlive || !roster.IsPatrolMoving(guard.Id,Now()))
                    throw new InvalidOperationException("Wraith observation detached or stopped a healthy moving patrol: duty="+
                        guard.Duty+"; patrol="+roster.IsOnPatrol(guard.Id)+"; refuge="+(guard.Refuge!=null));

                string json=SaveLoadManager.Serialize(typeof(GuardRoster),roster);
                var copy=GuardRoster.CopyValidated((GuardRoster)SaveLoadManager.Deserialize(typeof(GuardRoster),json));
                var savedLeader=copy.Guards.Find(g=>g.Id==plan.LeaderId);
                var savedFollower=copy.Guards.Find(g=>g.Id==plan.FollowerId);
                if(copy.Patrols.Count!=roster.Patrols.Count || !copy.IsOnPatrol(plan.LeaderId) ||
                    !copy.IsOnPatrol(plan.FollowerId) || savedLeader==null || savedFollower==null ||
                    savedLeader.Refuge!=null || savedFollower.Refuge!=null ||
                    copy.Patrols.Exists(p=>p.InterruptedAt.HasValue || p.LeaderDetached || p.FollowerDetached))
                    throw new InvalidOperationException("Full native serializer did not retain both attached patrol members and their plan.");

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_PATROL_PASS: active haunting Wraith left both members attached to the moving patrol; full native roster serialization retained it.");
            }
            finally
            {
                if(foe) UnityEngine.Object.Destroy(foe);
                if(root) UnityEngine.Object.Destroy(root);
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,oldCurse);
                game.PlayerEntity.CrimeCommitted=oldCrime;
                game.PlayerMotor.transform.position=oldPlayerPosition;
            }
            yield return null;
        }
    }
}
