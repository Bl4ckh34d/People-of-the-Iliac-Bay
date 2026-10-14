using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class HauntingFlightProbe
    {
        private static double Now() { var t=DaggerfallUnity.Instance.WorldTime.Now; return (double)t.ToSeconds()+t.Second-Math.Floor(t.Second); }
        private static void AdvanceTo(double when) { DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(0,when-Now())); }
        private static double Length(List<GuardPoint3> route)
        { double n=0; for(int i=1;i<route.Count;i++) { var a=route[i-1]; var b=route[i]; n+=Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2)+Math.Pow(a.Z-b.Z,2)); } return n; }
        public static IEnumerator Run(BuildingDirectory directory,TownGeometry geometry,List<RoutePoint> route)
        {
            var game=GameManager.Instance;
            AdvanceTo(Math.Floor(Now()/86400)*86400+19*3600+(Now()%86400>=19*3600 ? 86400 : 0));
            game.PlayerEntity.GlobalVars.SetGlobalVar(0,false);
            game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
            game.PlayerMotor.transform.position=geometry.ScenePoint(route[0])+Vector3.up*2;
            var root=new GameObject("Living Cities automatic refuge flight probe"); GameObject foe=null;
            var controller=root.AddComponent<GuardPopulation>(); controller.enabled=false; controller.EnemyDefense=false;
            try
            {
                var roster=GuardRoster.Create(directory.MapID,42,1,new[] { new SavedRoutePoint { X=route[0].X,Z=route[0].Z } });
                var guard=roster.Guards[0]; var type=typeof(GuardPopulation);
                type.GetProperty("Roster").GetSetMethod(true).Invoke(controller,new object[] { roster });
                type.GetField("directory",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,directory);
                type.GetField("geometry",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(controller,geometry);
                foe=GameObjectHelper.CreateFoeGameObjects(geometry.ScenePoint(route[2])+Vector3.up,MobileTypes.Ghost,1,MobileReactions.Hostile)[0];
                foe.GetComponent<EnemyMotor>().enabled=false; foe.GetComponent<EnemyAttack>().enabled=false; foe.SetActive(true);
                yield return null; foe.GetComponent<EnemyMotor>().IsHostile=true; controller.enabled=true;
                float deadline=Time.realtimeSinceStartup+10;
                while(guard.Refuge==null && Time.realtimeSinceStartup<deadline) yield return null;
                if(guard.Refuge==null) throw new InvalidOperationException("Automatic update did not start refuge flight.");
                var journey=guard.Refuge;
                double outside=Length(journey.Exterior)*journey.SecondsPerMetre, inside=Length(journey.Interior)*journey.SecondsPerMetre;
                AdvanceTo(journey.StartsAt+outside/2); yield return new WaitForSecondsRealtime(0.2f);
                if(controller.ActiveCount!=1) throw new InvalidOperationException("Automatic exterior refuge actor missing.");
                AdvanceTo(journey.StartsAt+outside+inside/2); yield return new WaitForSecondsRealtime(0.2f);
                if(controller.ActiveCount!=0) throw new InvalidOperationException("Refuge actor remained outside after doorway transition.");
                DaggerfallStaticDoors owner=null; StaticDoor selected=default(StaticDoor);
                foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    if(!owner && candidate.Doors!=null) foreach(var door in candidate.Doors)
                        if(door.buildingKey==journey.BuildingKey && door.doorType==DoorTypes.Building)
                        { owner=candidate; selected=door; break; }
                if(!owner) throw new InvalidOperationException("Automatic flight destination door missing.");
                TownSmokeHarness.EnterInterior(owner.transform,selected); yield return new WaitForSecondsRealtime(0.5f);
                AdvanceTo(journey.StartsAt+outside+inside+1); yield return new WaitForSecondsRealtime(0.2f);
                if(controller.ActiveCount!=1 || journey.Evaluate(Now()).Phase!=RefugePhase.Sheltered)
                    throw new InvalidOperationException("Automatic sheltered actor missing inside destination.");
                UnityEngine.Object.Destroy(foe); foe=null;
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,true); yield return new WaitForSecondsRealtime(0.2f);
                if(!journey.ReleasedAt.HasValue) throw new InvalidOperationException("Quest resolution did not release sheltered guard.");
                AdvanceTo(journey.ReleasedAt.Value+inside+1); yield return new WaitForSecondsRealtime(0.2f);
                if(controller.ActiveCount!=0) throw new InvalidOperationException("Departed guard remained visible inside refuge.");
                game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(0.5f);
                AdvanceTo(journey.ReleasedAt.Value+inside+outside+1); yield return new WaitForSecondsRealtime(0.2f);
                if(guard.Duty!=GuardDuty.Posted || guard.Refuge!=null || roster.Reports.Count!=0 || !guard.IsAlive)
                    throw new InvalidOperationException("Automatic refuge return did not restore the living post guard.");
                Debug.Log("LIVING_CITIES_HAUNTING_FLIGHT_PASS: normal Update detection, exterior actor, doorway hiding, interior shelter, quest release and post return.");
            }
            finally { if(foe) UnityEngine.Object.Destroy(foe); UnityEngine.Object.Destroy(root); }
            yield return null;
        }
    }
}
