using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit disposable-save acceptance. Uses the real roster, scene physics,
    // native guard bodies, routes and Halt audio path; never runs in ordinary play.
    internal static class GuardPursuitAcceptanceProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator Run(bool hearing)
        {
            var game=GameManager.Instance;
            Need(NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) && !game.PlayerEnterExit.IsPlayerInside,
                "Pursuit acceptance requires an owned exterior save.");
            var owner=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            float until=Time.realtimeSinceStartup+90;
            while((!owner || !owner.OwnsExterior || owner.Roster.Guards.Count<3) && Time.realtimeSinceStartup<until)yield return null;
            Need(owner && owner.OwnsExterior,"No native guard roster ready.");
            var geometry=(TownGeometry)typeof(GuardPopulation).GetField("geometry",Private).GetValue(owner);
            var roster=owner.Roster;double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var free=roster.Guards.FindAll(g=>g.IsAlive && g.Duty==GuardDuty.Posted && g.Interior==null && g.Refuge==null);
            Need(free.Count>=3,"Insufficient available existing guards.");
            var originalCrime=game.PlayerEntity.CrimeCommitted;
            var originalPosition=game.PlayerMotor.transform.position;
            game.PlayerEntity.GodMode=true;DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            try
            {
                GuardRecord source=null,candidate=null;SavedRoutePoint player=null;
                foreach(var a in free)
                {
                    foreach(var b in free)
                    {
                        if(a==b)continue;
                        float separation=Vector3.Distance(Scene(geometry,a.Post),Scene(geometry,b.Post));
                        if(separation<15 || separation>30)continue;
                        var midpoint=new SavedRoutePoint{X=(a.Post.X+b.Post.X)/2,Z=(a.Post.Z+b.Post.Z)/2};
                        if(!CivilianResponses.Sees(Scene(geometry,a.Post),Scene(geometry,b.Post),40) ||
                            !CivilianResponses.Sees(Scene(geometry,a.Post),Scene(geometry,midpoint),20) ||
                            !CivilianResponses.Sees(Scene(geometry,b.Post),Scene(geometry,midpoint),20) ||
                            geometry.RouteBetweenPoints(a.Post,midpoint).Count<2 || geometry.RouteBetweenPoints(b.Post,midpoint).Count<2)continue;
                        if(hearing && !free.Exists(g=>Vector3.Distance(Scene(geometry,g.Post),Scene(geometry,a.Post))>205))continue;
                        source=a;candidate=b;player=midpoint;break;
                    }
                    if(source!=null)break;
                }
                Need(source!=null,"No real guard posts support the visible pursuit fixture and requested distance controls.");
                game.PlayerMotor.transform.position=Scene(geometry,player);Physics.SyncTransforms();
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Assault;
                var dispatched=roster.Dispatch("acceptance-pursuit",now,new Dictionary<string,List<SavedRoutePoint>>{
                    {source.Id,geometry.RouteBetweenPoints(source.Post,player)}},1,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
                Need(dispatched.Count==1,"Could not dispatch the existing source guard.");
                var caller=View(owner,source,now);caller.gameObject.SetActive(true);
                var observer=View(owner,candidate,now);observer.gameObject.SetActive(true);
                if(hearing)
                {
                    roster.Engage(source.Id,now);source.LastSeenSuspect=player;
                    var expected=new List<GuardRecord>();var outside=new List<GuardRecord>();int occluded=0;
                    foreach(var g in free)
                    {
                        if(g==source)continue;
                        var from=roster.Position(g.Id,now);
                        float distance=Vector3.Distance(Scene(geometry,from)-Vector3.up,caller.transform.position);
                        if(distance>200){outside.Add(g);continue;}
                        if(geometry.RouteBetweenPoints(from,player).Count<2)continue;
                        expected.Add(g);
                        if(!CivilianResponses.Sees(Scene(geometry,from),caller.transform.position,1000))occluded++;
                    }
                    Need(expected.Count>0 && outside.Count>0 && occluded>0,"Halt fixture lacks listeners, occlusion or a guard outside 200 m.");
                    typeof(GuardActor).GetMethod("SayHaltOnce",Private).Invoke(caller,new object[]{game.PlayerEntityBehaviour});
                    Need(caller.HaltWarnings==1,"Actual native Halt was not emitted.");
                    foreach(var g in expected)Need(g.Duty==GuardDuty.Responding && g.IncidentId.StartsWith("halt:",StringComparison.Ordinal),"Audible guard did not join: "+g.Id);
                    foreach(var g in outside)Need(g.Duty==GuardDuty.Posted,"Guard beyond 200 m heard the Halt: "+g.Id);
                    Need(!observer.CombatReady,"Hearing alone granted immediate combat sight.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HALT_200M_NATIVE_PASS: actualHalt=true; listeners="+expected.Count+"; occluded="+occluded+"; outsideUnchanged="+outside.Count+"; noInstantCombat=true");
                }
                else
                {
                    // Suppress only the initial call so this case proves sight,
                    // independently of the separately tested audible Halt.
                    roster.TryGuardHalt(source.Id);
                    typeof(GuardActor).GetField("nextPursuitHalt",Private).SetValue(caller,Time.time+60);
                    caller.BeginCombat(now);
                    yield return null;yield return null;
                    Need(caller.CombatReady && caller.HaltWarnings==0,"Silent pursuit fixture did not enter native combat.");
                    typeof(GuardPopulation).GetMethod("RecruitNearbyPursuitGuards",Private).Invoke(owner,new object[]{DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()});
                    Need(candidate.Duty==GuardDuty.Engaged && candidate.IncidentId.StartsWith("pursuit:",StringComparison.Ordinal),"Visible stationary guard did not join beyond contact distance.");
                    yield return null;yield return null;
                    Need(observer.CombatReady && observer.GetComponent<EnemyMotor>().IsHostile,"Observer did not enter native pursuit.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SIGHT_PURSUIT_NATIVE_PASS: beyond12m=true; sourceHaltSuppressed=true; nativeObserverHostile=true; fixedRoster="+roster.Guards.Count);
                }
            }
            finally {game.PlayerEntity.CrimeCommitted=originalCrime;game.PlayerMotor.transform.position=originalPosition;}
        }
        private static GuardActor View(GuardPopulation owner,GuardRecord guard,double now)
        {return (GuardActor)typeof(GuardPopulation).GetMethod("View",Private).Invoke(owner,new object[]{guard,now});}
        private static Vector3 Scene(TownGeometry geometry,SavedRoutePoint point)
        {return geometry.ScenePoint(new RoutePoint(point.X,point.Z))+Vector3.up;}
        private static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
    }
}
