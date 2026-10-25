using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public static class HauntingFlightProbe
    {
        private static double Now()
        {
            var time=DaggerfallUnity.Instance.WorldTime.Now;
            return (double)time.ToSeconds()+time.Second-Math.Floor(time.Second);
        }

        private static void AdvanceToNight()
        {
            double now=Now();
            double night=Math.Floor(now/86400)*86400+19*3600;
            if(night<now) night+=86400;
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(night-now));
        }

        public static IEnumerator Run(BuildingDirectory directory,TownGeometry geometry,List<RoutePoint> route,bool combat=false)
        {
            var game=GameManager.Instance;
            bool oldCurse=game.PlayerEntity.GlobalVars.GetGlobalVar(0);
            var oldCrime=game.PlayerEntity.CrimeCommitted;
            Vector3 oldPlayerPosition=game.PlayerMotor.transform.position;
            var root=new GameObject("People of the Iliac Bay haunting guard response probe");
            GameObject foe=null,opponent=null;
            var controller=root.AddComponent<GuardPopulation>();
            controller.enabled=false;
            controller.EnemyDefense=false;
            try
            {
                AdvanceToNight();
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,false); // S0000977 LiftedCurse=false means the haunting remains active.
                double now=Now();
                if(!SettlementHaunting.StreetsDangerous(directory.MapID,false,now))
                    throw new InvalidOperationException("Guard response probe requires the active haunting after 19:00.");
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                game.PlayerMotor.transform.position=geometry.ScenePoint(route[0])+Vector3.up*2;

                var roster=GuardRoster.Create(directory.MapID,42,1,new[] {
                    new SavedRoutePoint { X=route[0].X,Z=route[0].Z }
                });
                var guard=roster.Guards[0];
                var type=typeof(GuardPopulation);
                type.GetProperty("Roster").GetSetMethod(true).Invoke(controller,new object[] { roster });
                type.GetField("directory",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,directory);
                type.GetField("geometry",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,geometry);
                // This isolated post/engagement fixture owns no municipal gate posts.
                // Keep unrelated gate staffing from reassigning its single test guard.
                type.GetField("gateDutySurveyed",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,true);

                GuardActor fighter=null;
                DaggerfallEntityBehaviour expectedTarget=null;
                if(combat)
                {
                    roster.Dispatch("probe-existing-defense",now,new Dictionary<string,List<SavedRoutePoint>> {
                        { guard.Id,new List<SavedRoutePoint> {
                            new SavedRoutePoint { X=route[0].X,Z=route[0].Z },
                            new SavedRoutePoint { X=route[4].X,Z=route[4].Z }
                        } }
                    },1,2.8,12);
                    // A current player assault keeps this fixture's existing engagement alive
                    // through the ordinary controller's arrest-expiry branch.
                    game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Assault;
                    opponent=GameObjectHelper.CreateFoeGameObjects(geometry.ScenePoint(route[4])+Vector3.up,
                        MobileTypes.Rat,1,MobileReactions.Hostile)[0];
                    opponent.GetComponent<EnemyMotor>().enabled=false;
                    opponent.GetComponent<EnemyAttack>().enabled=false;
                    opponent.SetActive(true);
                    fighter=GuardActor.Create(roster,guard.Id,directory.transform,geometry,now);
                    var views=(Dictionary<string,GuardActor>)type.GetField("actors",BindingFlags.Instance|BindingFlags.NonPublic)
                        .GetValue(controller);
                    views.Add(guard.Id,fighter);
                    yield return null;
                    expectedTarget=opponent.GetComponent<DaggerfallEntityBehaviour>();
                    fighter.BeginDefenseCombat(expectedTarget,now);
                    float ready=Time.realtimeSinceStartup+5;
                    while(!fighter.CombatReady && Time.realtimeSinceStartup<ready) yield return null;
                    if(!fighter.CombatReady || fighter.DefenseTarget!=expectedTarget ||
                        !fighter.GetComponent<EnemyMotor>().enabled || !fighter.GetComponent<EnemyAttack>().enabled ||
                        fighter.GetComponent<EnemySenses>().Target!=expectedTarget || guard.Duty!=GuardDuty.Engaged)
                        throw new InvalidOperationException("Native combat fixture failed to reach ready state: ready="+fighter.CombatReady+
                            "; attack="+fighter.GetComponent<EnemyAttack>().enabled+"; duty="+guard.Duty+
                            "; active="+fighter.gameObject.activeInHierarchy+"; target="+(fighter.DefenseTarget==expectedTarget));
                }

                // Exercise a Ghost on a healthy post guard and a Wraith while the guard
                // already fights. Both threats remain ordinary hostile-world threats.
                MobileTypes threatKind=combat ? MobileTypes.Wraith : MobileTypes.Ghost;
                foe=GameObjectHelper.CreateFoeGameObjects(geometry.ScenePoint(route[2])+Vector3.up,
                    threatKind,1,MobileReactions.Hostile)[0];
                foe.GetComponent<EnemyMotor>().enabled=false;
                foe.GetComponent<EnemyAttack>().enabled=false;
                foe.SetActive(true);
                yield return null;
                foe.GetComponent<EnemyMotor>().IsHostile=true;
                if(combat)
                {
                    controller.EnemyDefense=true;
                    var wraith=foe.GetComponent<DaggerfallEntityBehaviour>();
                    string threatId=controller.RegisterHostileThreat(wraith);
                    var report=roster.Reports.Find(item=>item.Id==guard.IncidentId);
                    if(threatId==null || report==null)
                        throw new InvalidOperationException("Wraith threat did not enter the active-defense fixture.");
                    report.ThreatId=threatId;
                }
                controller.enabled=true;

                // Give the real population update and threat survey time to run.
                float observeUntil=Time.realtimeSinceStartup+2;
                while(Time.realtimeSinceStartup<observeUntil) yield return null;
                if(guard.Refuge!=null)
                    throw new InvalidOperationException("Haunting threat forced a guard into refuge: combat="+combat+"; foe="+threatKind);
                if(!guard.IsAlive)
                    throw new InvalidOperationException("Haunting threat killed the acceptance guard fixture.");

                if(combat)
                {
                    var motor=fighter.GetComponent<EnemyMotor>();
                    var attack=fighter.GetComponent<EnemyAttack>();
                    var senses=fighter.GetComponent<EnemySenses>();
                    if(guard.Duty!=GuardDuty.Engaged || !fighter.CombatReady || fighter.DefenseTarget!=expectedTarget ||
                        !motor.enabled || !attack.enabled || senses.Target!=expectedTarget)
                        throw new InvalidOperationException("Wraith observation replaced existing native combat controls or target: duty="+
                            guard.Duty+"; ready="+fighter.CombatReady+"; motor="+motor.enabled+"; attack="+attack.enabled+
                            "; defenseTarget="+(fighter.DefenseTarget==expectedTarget)+"; sensesTarget="+(senses.Target==expectedTarget));
                    if(roster.Reports.Count!=1 || guard.IncidentId==null)
                        throw new InvalidOperationException("Existing combat report changed during Wraith observation.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_COMBAT_PASS: active haunting Wraith left an existing native combat target and controls intact.");
                }
                else if(guard.Duty!=GuardDuty.Posted || guard.IncidentId!=null || roster.Reports.Count!=0)
                    throw new InvalidOperationException("Ghost observation altered the healthy posted guard's assignment: duty="+
                        guard.Duty+"; incident="+guard.IncidentId+"; reports="+roster.Reports.Count);

                if(!combat)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HAUNTING_FLIGHT_PASS: active haunting Ghost did not force a healthy posted guard into refuge.");
            }
            finally
            {
                if(foe) UnityEngine.Object.Destroy(foe);
                if(opponent) UnityEngine.Object.Destroy(opponent);
                if(root) UnityEngine.Object.Destroy(root);
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,oldCurse);
                game.PlayerEntity.CrimeCommitted=oldCrime;
                game.PlayerMotor.transform.position=oldPlayerPosition;
            }
            yield return null;
        }
    }
}
