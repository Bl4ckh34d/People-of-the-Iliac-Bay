using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class HauntingThreatProbe
    {
        public static IEnumerator Run(BuildingDirectory directory,TownGeometry geometry)
        {
            var game=GameManager.Instance;
            var wall=geometry.Entrances[256];
            System.Collections.Generic.List<RoutePoint> route=null;
            foreach(var other in geometry.Entrances.Values)
            {
                if(other.BuildingKey==wall.BuildingKey || Vector3.Distance(other.LocalDoor,wall.LocalDoor)>60) continue;
                var candidate=geometry.RouteBetween(other,wall);
                if(candidate.Count>5) { route=candidate; break; }
            }
            if(route==null) throw new InvalidOperationException("Haunting probe needs a real nearby wall route.");
            bool oldCurse=game.PlayerEntity.GlobalVars.GetGlobalVar(0);
            try
            {
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,false);
                foreach(var kind in new[] { MobileTypes.Ghost,MobileTypes.Wraith })
                {
                    var root=new GameObject("Living Cities haunting probe"); GameObject foe=null;
                    var controller=root.AddComponent<GuardPopulation>(); controller.enabled=false;
                    try
                    {
                        var roster=GuardRoster.Create(directory.MapID,42,1,new[] { new SavedRoutePoint { X=route[0].X,Z=route[0].Z } });
                        var type=typeof(GuardPopulation);
                        type.GetProperty("Roster").GetSetMethod(true).Invoke(controller,new object[] { roster });
                        type.GetField("directory",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,directory);
                        type.GetField("geometry",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,geometry);
                        foe=GameObjectHelper.CreateFoeGameObjects(geometry.ScenePoint(route[2])+Vector3.up,kind,1,MobileReactions.Hostile)[0];
                        foe.GetComponent<EnemyMotor>().enabled=false; foe.GetComponent<EnemyAttack>().enabled=false;
                        foe.SetActive(true);
                        yield return null;
                        // The probe disables the motor before Start; explicitly set
                        // the hostile state that its normal startup would establish.
                        foe.GetComponent<EnemyMotor>().IsHostile=true;
                        var observe=type.GetMethod("ObserveHauntingThreat",BindingFlags.Instance|BindingFlags.NonPublic);
                        var target=foe.GetComponent<DaggerfallEntityBehaviour>();
                        bool classified=(bool)observe.Invoke(controller,new object[] { target,19*3600d });
                        if(!classified || roster.Guards[0].Refuge==null)
                            throw new InvalidOperationException("Visible haunting threat did not assign refuge: "+kind+"; classified="+classified+
                                "; hostile="+foe.GetComponent<EnemyMotor>().IsHostile+"; sight="+
                                CivilianResponses.Sees(geometry.ScenePoint(route[0])+Vector3.up,target.transform.position,20)+
                                "; distance="+Vector3.Distance(geometry.ScenePoint(route[0])+Vector3.up,target.transform.position)+
                                "; map="+roster.SettlementId+"; curse="+game.PlayerEntity.GlobalVars.GetGlobalVar(0)+
                                "; id="+((EnemyEntity)target.Entity).MobileEnemy.ID+"; expected="+(int)kind+
                                "; team="+target.Entity.Team+"; health="+target.Entity.CurrentHealth);
                        if(roster.Reports.Count!=0) throw new InvalidOperationException("Haunting flight created a crime report.");
                        game.PlayerEntity.GlobalVars.SetGlobalVar(0,true);
                        if((bool)observe.Invoke(controller,new object[] { target,19*3600d }))
                            throw new InvalidOperationException("Resolved curse still classifies haunting threat.");
                        game.PlayerEntity.GlobalVars.SetGlobalVar(0,false);
                        var release=type.GetMethod("ReleaseSafeRefuges",BindingFlags.Instance|BindingFlags.NonPublic);
                        release.Invoke(controller,new object[] { 3*86400d+22*3600 });
                        if(roster.Guards[0].Refuge.ReleasedAt!=86400+5*3600)
                            throw new InvalidOperationException("Unloaded guard did not depart at its first safe dawn.");
                        roster.Advance(3*86400d+22*3600);
                        if(roster.Guards[0].Duty!=GuardDuty.Posted || roster.Guards[0].Refuge!=null)
                            throw new InvalidOperationException("Unloaded refuge journey failed to complete its return.");
                        Debug.Log("LIVING_CITIES_HAUNTING_THREAT_PASS: "+kind+"; actual exterior and unloaded interior route; no crime report.");
                    }
                    finally { if(foe) UnityEngine.Object.Destroy(foe); UnityEngine.Object.Destroy(root); }
                    yield return null;
                }
                yield return HauntingFlightProbe.Run(directory,geometry,route);
            }
            finally { game.PlayerEntity.GlobalVars.SetGlobalVar(0,oldCurse); }
        }
    }
}
