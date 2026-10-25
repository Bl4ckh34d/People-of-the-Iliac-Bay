using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class InnCounterProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,DaggerfallInterior room,
            InteriorGeometry geometry,ShopVisitGeometry activities,InnActivityLayout layout)
        {
            if(layout.KeeperApproaches.Count<2) throw new InvalidOperationException("Inn counter lacks two lateral customer slots.");
            var normal=(Vector3)typeof(InnActivityLayout).GetField("customerDirection",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(layout);
            var origin=layout.KeeperApproaches[0];
            foreach(var slot in layout.KeeperApproaches)
                if(Math.Abs(Vector3.Dot(slot-origin,normal))>0.211f)
                    throw new InvalidOperationException("Inn counter positions still form rows behind each other.");
            bool enabled=host.enabled,streetEnabled=streets.enabled;host.enabled=false;streets.enabled=false;
            var hidden=new List<GameObject>();var actors=new List<ResidentActor>();var targets=new List<Vector3>();
            var routes=new List<InteriorRoomFollower>();var arrived=new List<bool>();
            foreach(var existing in UnityEngine.Object.FindObjectsOfType<ResidentActor>()) if(existing.gameObject.activeSelf)
            {hidden.Add(existing.gameObject);existing.gameObject.SetActive(false);}
            try
            {
                // The hidden real visitors may still own live leases. Isolate
                // test patrons with a fresh instance of the production allocator.
                var keeper=activities.TavernKeeper;
                activities=new ShopVisitGeometry(room,geometry,npc=>npc==keeper);
                layout=(InnActivityLayout)typeof(ShopVisitGeometry).GetField("innLayout",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(activities);
                var game=GameManager.Instance;int count=Math.Min(3,layout.KeeperApproaches.Count);
                for(int i=0;i<count;i++)
                {
                    var body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,room.transform);
                    var actor=body.AddComponent<ResidentActor>();actors.Add(actor);
                    actor.Initialize(new Resident {Id="counter-probe-"+i,FullName="Counter Patron "+i,AppearanceSeed=(uint)(42+i)},1,
                        game.StreamingWorld.GetCurrentCityNavigation());
                    actor.Place(geometry.EntryGround.Value,Vector3.forward,true);
                    Vector3 chosen;
                    if(!activities.TryCounterPosition(actor,origin,out chosen)) throw new InvalidOperationException("Concurrent counter claim failed for patron "+i);
                    foreach(var previous in targets) if((chosen-previous).sqrMagnitude<0.63f)
                        throw new InvalidOperationException("Concurrent patrons share counter frontage.");
                    targets.Add(chosen);arrived.Add(false);
                    var actual=room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                    var route=activities.CounterRouteBetween(actual,chosen);
                    routes.Add(new InteriorRoomFollower(route,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed));
                }
                float deadline=Time.realtimeSinceStartup+45;bool complete=false;
                while(Time.realtimeSinceStartup<deadline)
                {
                    complete=true;
                    for(int i=0;i<actors.Count;i++)
                    {
                        var actor=actors[i];Vector3 renewed;
                        if(!activities.TryCounterPosition(actor,targets[i],out renewed) || (renewed-targets[i]).sqrMagnitude>0.001f)
                            throw new InvalidOperationException("Live counter claim changed while patron was approaching.");
                        if(!arrived[i]) arrived[i]=routes[i].Advance(actor,room,geometry,Time.unscaledTime);
                        if(arrived[i])
                        {
                            actor.GetComponent<CrowdMotion>().ResumeFromPause();
                            actor.Place(room.transform.TransformPoint(targets[i]),room.transform.TransformDirection(activities.Face(targets[i],ShopVisitPhase.Haggling)),true,waitAtOccupiedTarget:true);
                        }
                        complete&=arrived[i];
                    }
                    if(complete) break;
                    yield return null;
                }
                if(!complete) throw new InvalidOperationException("Concurrent patrons did not physically reach the counter.");
                foreach(var actor in actors)
                    if(Vector3.Dot(actor.transform.forward,(activities.TavernKeeper.transform.position-actor.transform.position).normalized)<0.5f)
                        throw new InvalidOperationException("Counter patron faces away from the barkeeper.");
                var center=Vector3.zero;foreach(var actor in actors) center+=actor.transform.position;center/=actors.Count;
                var outward=room.transform.TransformDirection(layout.CustomerDirection);float cameraDistance=3;
                while(cameraDistance>0.8f && !geometry.HasClearLine(center+Vector3.up*0.5f,center+outward*cameraDistance+Vector3.up*0.5f))cameraDistance-=0.25f;
                game.PlayerMotor.transform.position=center+outward*cameraDistance;
                SleepProbe.AimCamera(center);
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"People-counter-side-by-side.png"));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_COUNTER_PASS: slots="+layout.KeeperApproaches.Count+"; concurrentPatrons="+actors.Count+
                    "; lateral frontage; distinct live claims; physical arrival and keeper-facing bodies.");
            }
            finally
            {
                foreach(var actor in actors) if(actor) {activities.ReleaseCounter(actor.Resident.Id);actor.gameObject.SetActive(false);UnityEngine.Object.Destroy(actor.gameObject);}
                foreach(var body in hidden) if(body) body.SetActive(true);
                host.enabled=enabled;streets.enabled=streetEnabled;
            }
        }
    }
}
