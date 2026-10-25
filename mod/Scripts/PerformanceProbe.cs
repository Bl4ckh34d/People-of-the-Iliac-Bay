using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class PerformanceProbe
    {
        public static IEnumerator Run()
        {
            if(GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                GameManager.Instance.PlayerEnterExit.TransitionExterior();
                float exteriorDeadline=Time.realtimeSinceStartup+60;
                while((GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding ||
                    !GameManager.Instance.StreamingWorld.IsReady) && Time.realtimeSinceStartup<exteriorDeadline) yield return null;
                if(GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding || !GameManager.Instance.StreamingWorld.IsReady)
                    throw new InvalidOperationException("Performance fixture could not reach exterior.");
            }
            GameManager.Instance.PlayerEntity.GodMode=true;
            GameManager.Instance.PlayerEntity.CrimeCommitted=DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            var clock=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((12*3600-clock.ToSeconds()%86400+86400)%86400));
            int resumed=0;
            int planningSlices=0;
            int approachSlices=0;
            int migrationSlices=0;
            bool restoreRegression=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-fixture-save")>=0;
            foreach(string phase in new[] { "city-initial-surveys", "city-later-sample", "city-approach-sample" })
            {
                RuntimeTiming.Reset();
                int collections=GC.CollectionCount(0);
                var frames=new List<double>();
                double previous=Time.realtimeSinceStartup;
                float deadline=Time.realtimeSinceStartup+60;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(GameManager.IsGamePaused || !GameManager.Instance.IsPlayingGame())
                    {
                        if(++resumed>3) throw new InvalidOperationException("Performance fixture repeatedly pauses; sample rejected.");
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_RESUME: excluding paused sample; window="+
                            (DaggerfallUI.UIManager.TopWindow==null ? "none" : DaggerfallUI.UIManager.TopWindow.GetType().Name));
                        DaggerfallUI.Instance.PopToHUD(); GameManager.Instance.PauseGame(false);
                        yield return null;
                        previous=Time.realtimeSinceStartup;
                        continue;
                    }
                    yield return null;
                    double now=Time.realtimeSinceStartup;
                    frames.Add((now-previous)*1000); previous=now;
                }
                frames.Sort();
                RuntimeTiming.Report(phase);
                planningSlices+=RuntimeTiming.Calls("resident-travel-planning-slice");
                approachSlices+=RuntimeTiming.Calls("guard-approach-route-slice");
                migrationSlices+=RuntimeTiming.Calls("guard-post-migration-slice");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_FRAMES: "+phase+"; frames="+frames.Count+
                    "; p50Ms="+frames[frames.Count/2].ToString("F2")+
                    "; p95Ms="+frames[(int)((frames.Count-1)*.95)].ToString("F2")+
                    "; p99Ms="+frames[(int)((frames.Count-1)*.99)].ToString("F2")+
                    "; maxMs="+frames[frames.Count-1].ToString("F2")+
                    "; gen0="+(GC.CollectionCount(0)-collections));
                if(restoreRegression && frames[frames.Count-1]>=1000)
                    throw new InvalidOperationException("Saved city restore blocked a sampled frame for at least one second.");
            }
            if(restoreRegression && migrationSlices==0) throw new InvalidOperationException("Saved city restore did not exercise post migration.");
            if(planningSlices==0) throw new InvalidOperationException("Performance fixture did not exercise automatic visitor planning.");
            if(approachSlices==0) throw new InvalidOperationException("Performance fixture did not exercise guard approach preparation.");
            var guards=UnityEngine.Object.FindObjectOfType<GuardPopulation>();
            if(!guards) throw new InvalidOperationException("Performance fixture lost guard controller.");
            var directory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory || !guards.GateDutySurveyComplete)
                throw new InvalidOperationException("Performance sample ended before the native gate survey completed.");
            int nativeGateCount=directory.GetComponentsInChildren<DaggerfallWorkshop.DaggerfallCityGate>().Length;
            var posts=guards.GateGuardDutySnapshots(clock.ToSeconds());
            if((nativeGateCount>0 && posts.Count==0) || posts.Exists(p=>string.IsNullOrEmpty(p.GuardId)))
                throw new InvalidOperationException("Performance sample ended before gate assignments completed.");
            if(nativeGateCount==0 && posts.Count!=0)
                throw new InvalidOperationException("A settlement without native gates retained gate assignments.");
            int wallPlans=guards.Roster.Guards.FindAll(g=>g.PostRecall!=null && g.PostRecall.WallDuty!=null).Count;
            if(wallPlans<posts.FindAll(p=>p.ExteriorAssignment).Count)
                throw new InvalidOperationException("Performance sample ended before wall plans completed.");
            if(nativeGateCount==0)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_GATE_NOT_APPLICABLE: nativeGates=0; surveyComplete=true; posts=0.");
            else
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_GATE_SETUP_COMPLETE: posts="+posts.Count+"; wallPlans="+wallPlans);
            var adapter=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var state=adapter ? adapter.GetSaveData() as PeopleOfTheIliacBay.Core.WorldState : null;
            var routines=state==null || !directory ? null : state.Routines.Find(t=>t.SettlementId==directory.MapID);
            if(routines==null || routines.ItineraryVersion!=1 || !routines.Residents.Exists(r=>r.Itinerary!=null))
                throw new InvalidOperationException("Performance sample ended before daily itineraries became active.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_DAILY_ACTIVE: routines="+routines.Residents.Count+
                "; itineraries="+routines.Residents.FindAll(r=>r.Itinerary!=null).Count);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERFORMANCE_PASS: city timing samples collected; automatic planning slices="+planningSlices+"; not a world-scale performance guarantee.");
        }
    }
}
