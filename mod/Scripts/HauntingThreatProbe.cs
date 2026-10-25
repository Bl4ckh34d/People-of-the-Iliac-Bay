using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class HauntingThreatProbe
    {
        public static IEnumerator Run(BuildingDirectory directory,TownGeometry geometry)
        {
            var game=GameManager.Instance;
            System.Collections.Generic.List<RoutePoint> route=null;
            foreach(var wall in geometry.Entrances.Values)
            {
                BuildingSummary building;
                if(!directory.GetBuildingSummary(wall.BuildingKey,out building) ||
                    building.BuildingType!=DaggerfallConnect.DFLocation.BuildingTypes.Town23) continue;
                foreach(var other in geometry.Entrances.Values)
                {
                    if(other.BuildingKey==wall.BuildingKey || Vector3.Distance(other.LocalDoor,wall.LocalDoor)>60) continue;
                    var candidate=geometry.RouteBetween(other,wall);
                    if(candidate.Count>5) { route=candidate; break; }
                }
                if(route!=null) break;
            }
            if(route==null) throw new InvalidOperationException("Haunting probe needs a real nearby wall route.");
            bool oldCurse=game.PlayerEntity.GlobalVars.GetGlobalVar(0);
            try
            {
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,false);
                if(!SettlementHaunting.StreetsDangerous(directory.MapID,
                    game.PlayerEntity.GlobalVars.GetGlobalVar(0),19*3600d))
                    throw new InvalidOperationException("Haunting guard regression requires an active curse at night in Daggerfall.");
                foreach(var kind in new[] { MobileTypes.Ghost,MobileTypes.Wraith })
                {
                    var root=new GameObject("People of the Iliac Bay haunting probe"); GameObject foe=null;
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
                        bool suppressed=(bool)observe.Invoke(controller,new object[] { target,19*3600d });
                        if(suppressed || !GuardPopulation.IsEnemyThreat(target))
                            throw new InvalidOperationException("Active haunting did not leave hostile threat on normal guard-defense path: "+kind+
                                "; suppressed="+suppressed+"; hostile="+foe.GetComponent<EnemyMotor>().IsHostile+
                                "; sight="+CivilianResponses.Sees(geometry.ScenePoint(route[0])+Vector3.up,target.transform.position,20)+
                                "; distance="+Vector3.Distance(geometry.ScenePoint(route[0])+Vector3.up,target.transform.position)+
                                "; map="+roster.SettlementId+"; curse="+game.PlayerEntity.GlobalVars.GetGlobalVar(0)+
                                "; id="+((EnemyEntity)target.Entity).MobileEnemy.ID+"; expected="+(int)kind+
                                "; team="+target.Entity.Team+"; health="+target.Entity.CurrentHealth);
                        string threatKey=controller.RegisterHostileThreat(target);
                        controller.EnemyDefense=false;
                        var survey=type.GetMethod("SurveyEnemyThreats",BindingFlags.Instance|BindingFlags.NonPublic);
                        survey.Invoke(controller,new object[] { 19*3600d });
                        Vector3 registeredPosition;
                        if(string.IsNullOrEmpty(threatKey) || !controller.TryHostileThreatPosition(threatKey,out registeredPosition) ||
                            Vector3.Distance(registeredPosition,target.transform.position)>0.01f)
                            throw new InvalidOperationException("Hostile haunting actor was not retained by normal threat survey: "+kind);
                        if(roster.Guards[0].Refuge!=null || roster.Guards[0].Duty!=GuardDuty.Posted ||
                            roster.Guards[0].IncidentId!=null || roster.Reports.Count!=0)
                            throw new InvalidOperationException("Normal haunting threat classification forced refuge or guard dispatch while defense was disabled: "+kind);
                        GuardRoster.CopyValidated(roster);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_THREAT_PASS: "+kind+"; normal hostile-threat survey; no forced refuge.");
                    }
                    finally { if(foe) UnityEngine.Object.Destroy(foe); UnityEngine.Object.Destroy(root); }
                    yield return null;
                }
                yield return HauntingFlightProbe.Run(directory,geometry,route);
                yield return HauntingFlightProbe.Run(directory,geometry,route,true);
                yield return HauntingPatrolProbe.Run(directory,geometry,route);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_ROLE_PASS: active-curse ghost/wraith threat registration, healthy post/combat/patrol no forced refuge.");
            }
            finally { game.PlayerEntity.GlobalVars.SetGlobalVar(0,oldCurse); }
        }
    }
}
