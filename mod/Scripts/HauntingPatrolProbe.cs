using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class HauntingPatrolProbe
    {
        private static double Now() { return DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(); }
        public static IEnumerator Run(BuildingDirectory directory,TownGeometry geometry,List<RoutePoint> route)
        {
            var game=GameManager.Instance; var root=new GameObject("Living Cities haunting patrol probe"); GameObject foe=null;
            var controller=root.AddComponent<GuardPopulation>(); controller.enabled=false; controller.EnemyDefense=false; controller.NightPatrols=false;
            try
            {
                var roster=GuardRoster.Create(directory.MapID,42,2,new[] {
                    new SavedRoutePoint { X=route[0].X,Z=route[0].Z },new SavedRoutePoint { X=route[2].X,Z=route[2].Z } });
                var type=typeof(GuardPopulation); var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                type.GetProperty("Roster").GetSetMethod(true).Invoke(controller,new object[] { roster });
                type.GetField("directory",flags).SetValue(controller,directory);
                type.GetField("geometry",flags).SetValue(controller,geometry);
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,true); controller.enabled=true;
                yield return (IEnumerator)type.GetMethod("SurveyPatrols",flags).Invoke(controller,null);
                controller.enabled=false;
                var templates=(List<PatrolPairPlan>)type.GetField("patrolTemplates",flags).GetValue(controller);
                if(templates.Count==0) throw new InvalidOperationException("No real city patrol circuit surveyed.");
                double night=Math.Floor(Now()/86400)*86400+19*3600;
                if(night<Now()) night+=86400;
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(night-Now()));
                var template=templates[0];
                var plan=PatrolPairPlan.Create(template.LeaderId,template.FollowerId,template.LeaderJoin,template.FollowerJoin,template.Circuit,Now(),Now()+3600,1.3,12,2);
                roster.StartPatrol(plan,Now());
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(plan.CircuitStartsAt-Now()+24));
                var guard=roster.Guards.Find(g=>g.Id==plan.LeaderId);
                var before=roster.Position(guard.Id,Now());
                if(!roster.IsPatrolMoving(guard.Id,Now())) throw new InvalidOperationException("Fixture guard is not moving on patrol.");
                game.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(before.X,before.Z))+Vector3.up*2;
                foe=GameObjectHelper.CreateFoeGameObjects(geometry.ScenePoint(new RoutePoint(before.X,before.Z))+Vector3.up,MobileTypes.Ghost,1,MobileReactions.Hostile)[0];
                foe.GetComponent<EnemyMotor>().enabled=false; foe.GetComponent<EnemyAttack>().enabled=false; foe.SetActive(true);
                yield return null; foe.GetComponent<EnemyMotor>().IsHostile=true;
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,false); controller.enabled=true;
                float deadline=Time.realtimeSinceStartup+10;
                while(guard.Refuge==null && Time.realtimeSinceStartup<deadline) yield return null;
                if(guard.Refuge==null || roster.IsOnPatrol(guard.Id)) throw new InvalidOperationException("Moving patrol did not detach into refuge.");
                var start=guard.Refuge.Exterior[0];
                if(Math.Sqrt(Math.Pow(start.X-before.X,2)+Math.Pow(start.Z-before.Z,2))>2)
                    throw new InvalidOperationException("Patrol refuge departure jumped from observed position.");
                string json=SaveLoadManager.Serialize(typeof(GuardRoster),roster);
                var copy=GuardRoster.CopyValidated((GuardRoster)SaveLoadManager.Deserialize(typeof(GuardRoster),json));
                if(copy.IsOnPatrol(guard.Id) || copy.Guards.Find(g=>g.Id==guard.Id).Refuge.ReturnExterior==null)
                    throw new InvalidOperationException("Native serializer lost patrol refuge return.");
                UnityEngine.Object.Destroy(foe); foe=null; game.PlayerEntity.GlobalVars.SetGlobalVar(0,true);
                yield return new WaitForSecondsRealtime(0.2f);
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime(3600);
                yield return new WaitForSecondsRealtime(0.2f);
                var end=roster.Position(guard.Id,Now());
                if(guard.Refuge!=null || guard.Duty!=GuardDuty.Posted || Math.Abs(end.X-guard.Post.X)>0.01 || Math.Abs(end.Z-guard.Post.Z)>0.01)
                    throw new InvalidOperationException("Patrol refugee failed to return to original post.");
                Debug.Log("LIVING_CITIES_HAUNTING_PATROL_PASS: real surveyed moving patrol, automatic ghost detection, native serialized return and original post recovery.");
            }
            finally { if(foe) UnityEngine.Object.Destroy(foe); UnityEngine.Object.Destroy(root); }
            yield return null;
        }
    }
}
