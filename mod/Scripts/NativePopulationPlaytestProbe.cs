using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Owned disposable editor fixture only. No input injection or save writes.
    internal static class NativePopulationPlaytestProbe
    {
        internal static IEnumerator Run(StreetPopulation streets)
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((12*3600-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();GameManager.Instance.PauseGame(false);
            var actors=new List<ResidentActor>();float deadline=Time.realtimeSinceStartup+45;
            while(actors.Count<6 && Time.realtimeSinceStartup<deadline)
            {
                actors.Clear();
                foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(actor.Resident.IsAlive && actor.gameObject.activeInHierarchy && actor.GetComponent<CrowdMotion>() &&
                        actor.GetComponent<CrowdMotion>().HasPlacement)actors.Add(actor);
                yield return null;
            }
            if(actors.Count<6)throw new InvalidOperationException("Crowd test requires six actual exterior residents.");
            actors.RemoveRange(6,actors.Count-6);
            var directory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            Vector3 center=actors[0].transform.position-Vector3.up;
            var starts=new List<Vector3>();var targets=new List<Vector3>();
            foreach(var actor in actors)starts.Add(actor.transform.position);
            for(int i=0;i<actors.Count;i++)
            {
                float angle=i*Mathf.PI*2/actors.Count;NavMeshHit end,blocked;
                var desired=center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*3;
                if(!NavMesh.SamplePosition(desired,out end,0.6f,NavMesh.AllAreas) ||
                    Mathf.Abs(end.position.y-center.y)>0.4f || NavMesh.Raycast(center,end.position,out blocked,NavMesh.AllAreas))
                    throw new InvalidOperationException("Crowd fixture needs a clear native street apron.");
                targets.Add(end.position);
            }
            bool enabled=streets.enabled;streets.enabled=false;
            try
            {
                foreach(var actor in actors)
                {
                    var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
                    actor.GetComponent<CrowdMotion>().RestorePlacement(center);
                }
                float maximumStep=0;deadline=Time.realtimeSinceStartup+12;
                while(Time.realtimeSinceStartup<deadline)
                {
                    for(int i=0;i<actors.Count;i++)
                    {
                        var actor=actors[i];var before=actor.transform.position;
                        actor.GetComponent<CrowdMotion>().ResumeFromPause();
                        actor.Place(targets[i],targets[i]-(before-Vector3.up),navigationDestination:targets[i]);
                        maximumStep=Mathf.Max(maximumStep,Vector3.Distance(before,actor.transform.position));
                        if(!actor.gameObject.activeInHierarchy)throw new InvalidOperationException("Overlap recovery hid an existing body.");
                    }
                    yield return null;
                }
                float minimum=float.MaxValue;
                for(int i=0;i<actors.Count;i++)for(int j=i+1;j<actors.Count;j++)
                    minimum=Mathf.Min(minimum,Vector3.Distance(actors[i].transform.position,actors[j].transform.position));
                if(minimum<0.50f || maximumStep>0.55f)
                    throw new InvalidOperationException("Crowd recovery failed: minimumGap="+minimum+"; maximumStep="+maximumStep);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CROWD_RECOVERY_PASS: actors=6; minimumGap="+minimum+"; maximumStep="+maximumStep);

                // Measure the rendered body on the already verified clear apron.
                // A resumed physical route must retain its urgent pace rather
                // than silently inheriting the ordinary walking cap.
                var runner=actors[0];float measuredTime=0,measuredDistance=0;
                double intendedSpeed=ResidentMovement.FleeSpeed(runner.Resident.AppearanceSeed);
                deadline=Time.realtimeSinceStartup+3;
                while(Vector3.Distance(runner.transform.position-Vector3.up,center)>0.6f && Time.realtimeSinceStartup<deadline)
                {
                    float frame=Time.unscaledDeltaTime;
                    var before=runner.transform.position;
                    var ground=before-Vector3.up;
                    runner.GetComponent<CrowdMotion>().ResumeFromPause();
                    runner.Place(Vector3.MoveTowards(ground,center,(float)intendedSpeed*frame),center-ground,fleeing:true,preferNative:false);
                    // Long-frame collision caps are tested above, not mistaken
                    // for the configured unobstructed movement speed here.
                    if(frame>0 && frame<=0.1f){measuredTime+=frame;measuredDistance+=Vector3.Distance(before,runner.transform.position);}
                    yield return null;
                }
                float measuredSpeed=measuredTime>0?measuredDistance/measuredTime:0;
                if(measuredSpeed*3.6f<3.5f || measuredSpeed<intendedSpeed*0.9)
                    throw new InvalidOperationException("Urgent rendered pace is capped: measuredKmh="+measuredSpeed*3.6f+"; intendedKmh="+intendedSpeed*3.6);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_URGENT_SPEED_PASS: measuredKmh="+measuredSpeed*3.6f+"; intendedKmh="+intendedSpeed*3.6+"; minimumKmh=3.5");

                // A sign-sized solid collider is directly between the NPC and an
                // otherwise safer destination. Distance from danger alone is insufficient.
                var from=actors[0].transform.position-Vector3.up;
                var direction=(center-from).normalized;direction.y=0;direction.Normalize();
                var preferred=from+direction*3.5f;var danger=from-direction*3;
                var sign=GameObject.CreatePrimitive(PrimitiveType.Cube);sign.name="Owned flight obstruction probe";
                try
                {
                    sign.transform.position=from+direction*1.2f+Vector3.up;
                    sign.transform.localScale=new Vector3(0.5f,2,0.5f);Physics.SyncTransforms();
                    Vector3 ignored;
                    if(CivilianFlightSteering.TryDirectRetreat(from,preferred,out ignored))
                        throw new InvalidOperationException("Flight fixture did not obstruct the preferred corridor.");
                    var steering=actors[0].GetComponent<CivilianFlightSteering>();
                    if(!steering)steering=actors[0].gameObject.AddComponent<CivilianFlightSteering>();
                    var actual=steering.Target(from,preferred,danger,geometry,directory.transform);
                    if((actual-from).sqrMagnitude<0.01f || !CivilianFlightSteering.TryDirectRetreat(from,actual,out ignored))
                        throw new InvalidOperationException("Flight steering chose the blocked sign corridor.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FLIGHT_OBSTRUCTION_PASS: blocked-preferred-corridor-rejected=true");
                }
                finally {UnityEngine.Object.DestroyImmediate(sign);}
            }
            finally
            {
                for(int i=0;i<actors.Count;i++)if(actors[i])
                {
                    var native=actors[i].GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
                    actors[i].GetComponent<CrowdMotion>().RestorePlacement(starts[i]-Vector3.up);
                }
                streets.enabled=enabled;
            }
        }
    }
}
