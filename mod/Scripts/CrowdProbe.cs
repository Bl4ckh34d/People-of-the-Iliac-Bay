using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class CrowdProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            if(game.PlayerEnterExit.IsPlayerInsideBuilding) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation());
            Vector3 from=Vector3.zero,to=Vector3.zero; bool found=false;
            foreach(var entrance in geometry.Entrances.Values)
            {
                foreach(var other in geometry.Entrances.Values)
                {
                    if(entrance.BuildingKey==other.BuildingKey || Vector3.Distance(entrance.LocalDoor,other.LocalDoor)>60) continue;
                    var route=geometry.RouteBetween(entrance,other);
                    for(int i=0;i<route.Count-1 && !found;i++)
                    {
                        var a=geometry.ScenePoint(route[i]); var heading=(geometry.ScenePoint(route[i+1])-a).normalized;
                        for(int j=i+1;j<route.Count;j++)
                        {
                            var b=geometry.ScenePoint(route[j]);
                            if(Vector3.Dot((b-a).normalized,heading)<0.99f) break;
                            if(Vector3.Distance(a,b)<8) continue;
                            var middle=(a+b)*0.5f; var direction=(b-a).normalized;
                            var side=Vector3.Cross(Vector3.up,direction).normalized;
                            if(!Wide(middle-direction*3,side) || !Wide(middle,side) || !Wide(middle+direction*3,side)) continue;
                            from=middle-direction*3; to=middle+direction*3; found=true; break;
                        }
                    }
                    if(found) break;
                }
                if(found) break;
            }
            if(!found) throw new InvalidOperationException("No long street segment for crowd probe.");
            var mod=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>(); var streets=mod.GetComponent<StreetPopulation>(); var guards=mod.GetComponent<GuardPopulation>();
            VerifyHaltBystanderCadence(streets,from,to);
            bool modEnabled=mod.enabled,streetEnabled=streets.enabled,guardEnabled=guards.enabled;
            mod.enabled=false; streets.enabled=false; guards.enabled=false; streets.Clear(); guards.Clear();
            var vanilla=directory.GetComponent<PopulationManager>(); bool vanillaEnabled=vanilla && vanilla.enabled;
            if(vanilla) vanilla.enabled=false;
            var hidden=new Dictionary<GameObject,bool>();
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<MobilePersonNPC>())
            { hidden[npc.gameObject]=npc.gameObject.activeSelf; npc.gameObject.SetActive(false); }
            GameObject first=null,second=null,wall=null;
            try
            {
                var delta=to-from;
                game.PlayerMotor.transform.position=(from+to)*0.5f+Vector3.Cross(Vector3.up,delta.normalized)*5+Vector3.up*2;
                first=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,directory.transform);
                second=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,directory.transform);
                var a=first.AddComponent<ResidentActor>(); var b=second.AddComponent<ResidentActor>();
                a.Initialize(new Resident { Id="crowd-a",FullName="Crowd Walker A",AppearanceSeed=1 },1,game.StreamingWorld.GetCurrentCityNavigation());
                b.Initialize(new Resident { Id="crowd-b",FullName="Crowd Walker B",AppearanceSeed=2 },2,game.StreamingWorld.GetCurrentCityNavigation());
                a.Place(from,delta); b.Place(to,-delta);
                float started=Time.realtimeSinceStartup; float minimum=10,earlyAvoidanceGap=0;
                var crossingSide=Vector3.Cross(Vector3.up,delta.normalized);
                while(Time.realtimeSinceStartup-started<12)
                {
                    float progress=Mathf.Min(1,(Time.realtimeSinceStartup-started)*1.3f/6);
                    a.Place(Vector3.Lerp(from,to,progress),delta,progress==1);
                    b.Place(Vector3.Lerp(to,from,progress),-delta,progress==1);
                    if(!first.activeSelf || !second.activeSelf) throw new InvalidOperationException("Crowd actors lost valid placement.");
                    float distance=Vector3.Distance(first.transform.position,second.transform.position);
                    if(earlyAvoidanceGap==0 && (Mathf.Abs(Vector3.Dot(first.transform.position-from,crossingSide))>0.02f ||
                        Mathf.Abs(Vector3.Dot(second.transform.position-to,crossingSide))>0.02f)) earlyAvoidanceGap=distance;
                    minimum=Mathf.Min(minimum,distance);
                    if(distance<0.50f) throw new InvalidOperationException("Crowd actors overlapped: "+distance);
                    yield return null;
                }
                if(Vector3.Distance(first.transform.position,to+Vector3.up)>0.3f || Vector3.Distance(second.transform.position,from+Vector3.up)>0.3f)
                    throw new InvalidOperationException("Opposing street walkers did not pass: "+first.transform.position+" / "+second.transform.position);
                if(earlyAvoidanceGap<=1.1f) throw new InvalidOperationException("Walkers waited until close range to evade: "+earlyAvoidanceGap);
                Debug.Log("LIVING_CITIES_COMFORT_AVOIDANCE_PASS: lateral correction began at separation="+earlyAvoidanceGap);
                Debug.Log("LIVING_CITIES_CROWD_PASS: actual opposing street walkers pass; minimum separation="+minimum);
                first.GetComponent<CrowdMotion>().ResetPlacement(); second.GetComponent<CrowdMotion>().ResetPlacement();
                a.Place(from,delta); b.Place(to,-delta);
                wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position=(from+to)*0.5f+Vector3.up;
                wall.transform.rotation=Quaternion.LookRotation(delta);
                wall.transform.localScale=new Vector3(1.5f,2,0.3f);
                Physics.SyncTransforms();
                started=Time.realtimeSinceStartup;
                while(Time.realtimeSinceStartup-started<15)
                {
                    float progress=Mathf.Min(1,(Time.realtimeSinceStartup-started)*1.3f/6);
                    a.Place(Vector3.Lerp(from,to,progress),delta,progress==1);
                    b.Place(Vector3.Lerp(to,from,progress),-delta,progress==1);
                    Vector3 direction; float penetration;
                    foreach(var body in new[] { first,second })
                        if(Physics.ComputePenetration(body.GetComponent<CapsuleCollider>(),body.transform.position,body.transform.rotation,
                            wall.GetComponent<Collider>(),wall.transform.position,wall.transform.rotation,out direction,out penetration) && penetration>0.01f)
                            throw new InvalidOperationException("Crowd body penetrated a wall: "+penetration);
                    yield return null;
                }
                // Local steering guarantees collision safety, not a new route around
                // arbitrary obstructions. Route replanning remains a separate task.
                Debug.Log("LIVING_CITIES_CROWD_WALL_PASS: actual walkers remain outside an inserted wall; automatic rerouting is not asserted.");
            }
            finally
            {
                if(first) UnityEngine.Object.Destroy(first); if(second) UnityEngine.Object.Destroy(second);
                if(wall) UnityEngine.Object.Destroy(wall);
                foreach(var pair in hidden) if(pair.Key) pair.Key.SetActive(pair.Value);
                if(vanilla) vanilla.enabled=vanillaEnabled;
                mod.enabled=modEnabled; streets.enabled=streetEnabled; guards.enabled=guardEnabled;
            }
        }
        private static bool Wide(Vector3 point,Vector3 side)
        {
            foreach(float offset in new[] { -1.5f,0,1.5f })
            {
                var target=point+side*offset;
                foreach(var collider in Physics.OverlapCapsule(target+Vector3.up*0.91f,target+Vector3.up*1.65f,0.4f,~0,QueryTriggerInteraction.Ignore))
                    if(Static(collider)) return false;
                if(offset!=0)
                    foreach(var hit in Physics.CapsuleCastAll(point+Vector3.up*0.91f,point+Vector3.up*1.65f,0.4f,
                        side*Mathf.Sign(offset),Mathf.Abs(offset),~0,QueryTriggerInteraction.Ignore))
                        if(Static(hit.collider)) return false;
            }
            return true;
        }
        private static void VerifyHaltBystanderCadence(StreetPopulation streets,Vector3 guardPosition,Vector3 playerPosition)
        {
            if(GuardActor.PursuitHaltDue(19.9f,20f) || !GuardActor.PursuitHaltDue(20f,20f) ||
                GuardActor.PursuitHaltDue(39.9f,40f) || !GuardActor.PursuitHaltDue(40f,40f))
                throw new InvalidOperationException("Pursuit Halt cadence did not retain its twenty-second boundary.");
            if(BystanderResponse.Evaluate(8,11,BystanderPosture.Resume)!=BystanderPosture.Retreat ||
                BystanderResponse.Evaluate(15,11,BystanderPosture.Resume)!=BystanderPosture.Watch ||
                BystanderResponse.Evaluate(21,11,BystanderPosture.Resume)!=BystanderPosture.Resume ||
                BystanderResponse.Evaluate(10,11,BystanderPosture.Retreat)!=BystanderPosture.Retreat)
                throw new InvalidOperationException("Live bystander posture thresholds or hysteresis changed.");
            GuardActor.PublishPlayerHalt(guardPosition,playerPosition,20f);
            var type=typeof(StreetPopulation); var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            var heard=(Vector3)type.GetField("lastHaltGuardPosition",flags).GetValue(streets);
            var radius=(float)type.GetField("lastHaltAudibleRange",flags).GetValue(streets);
            if(Vector3.Distance(heard,guardPosition)>0.01f || Math.Abs(radius-20f)>0.01f)
                throw new InvalidOperationException("StreetPopulation did not receive the guard Halt event with its hearing range.");
            Debug.Log("LIVING_CITIES_CROWD_HALT_PASS: listener receives guard origin/range; twenty-second repeats; stable retreat/watch thresholds and hysteresis pass.");
        }
        private static bool Static(Collider collider)
        {
            return !collider.GetComponentInParent<MobilePersonNPC>() && !collider.GetComponentInParent<StaticNPC>() &&
                !collider.GetComponentInParent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>() && !collider.GetComponentInParent<PlayerMotor>();
        }
    }
}
