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
            IDisposable nativeTiming=null;
            bool nativeScopes=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-profile-climate")>=0;
            if(nativeScopes)
            {
                var type=Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeClimateTimingHooks, PeopleOfTheIliacBay.QuestHooks",true);
                nativeTiming=(IDisposable)Activator.CreateInstance(type,new object[]{new Func<string,long>(RuntimeTiming.Start),new Action<string,long>(RuntimeTiming.End)});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_NATIVE_SCOPES: active=true; existingClimateHooks=true; nativeBehaviorUnchanged=true; nestedDurationsInclusive=true");
            }
            using(nativeTiming) {yield return RunObserved(nativeScopes);}
        }

        private static IEnumerator RunObserved(bool nativeScopes)
        {
            var game=GameManager.Instance;
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            if(game.PlayerEnterExit.IsPlayerInsideBuilding)game.PlayerEnterExit.TransitionExterior();
            var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            float readinessStarted=Time.realtimeSinceStartup,readinessDeadline=readinessStarted+30;
            while((!game.IsReady || game.PlayerEnterExit.IsPlayerInside || !game.StreamingWorld.IsReady ||
                game.StreamingWorld.IsRepositioningPlayer || !streets || !streets.Ready) && Time.realtimeSinceStartup<readinessDeadline)yield return null;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_READINESS: gameReady="+game.IsReady+"; inside="+game.PlayerEnterExit.IsPlayerInside+
                "; worldReady="+game.StreamingWorld.IsReady+"; repositioning="+game.StreamingWorld.IsRepositioningPlayer+
                "; streetsReady="+(streets && streets.Ready)+"; directory="+(directory!=null)+
                "; nativeNavigationReady="+(NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready)+
                "; waitSeconds="+(Time.realtimeSinceStartup-readinessStarted).ToString("F2")+
                "; gateAndItineraryCompletionRequired=false; readinessOutsideSamples=true");
            if(!game.IsReady || game.PlayerEnterExit.IsPlayerInside || !game.StreamingWorld.IsReady ||
                game.StreamingWorld.IsRepositioningPlayer || !streets || !streets.Ready || !directory)
                throw new InvalidOperationException("Performance fixture lacks its native loaded exterior after 30 seconds; inspect PERF_READINESS.");
            game.PlayerEntity.GodMode=true;
            game.PlayerEntity.CrimeCommitted=DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            var clock=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((12*3600-clock.ToSeconds()%86400+86400)%86400));
            int resumed=0,oneSecondFrames=0,fourSecondFrames=0,phases=0;
            double maximumFrame=0;
            foreach(string phase in new[] { "city-initial-surveys", "city-later-sample", "city-approach-sample" })
            {
                RuntimeTiming.Reset();
                int collections=GC.CollectionCount(0);
                int phaseOneSecond=0,phaseFourSecond=0;
                var frames=new List<double>();
                double previous=Time.realtimeSinceStartup;
                float deadline=Time.realtimeSinceStartup+60;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_PHASE_START: "+phase+"; seconds=60; nativeScopes="+nativeScopes);
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
                    double frameMs=(now-previous)*1000;frames.Add(frameMs);previous=now;
                    if(frameMs>=1000)
                    {
                        phaseOneSecond++;oneSecondFrames++;
                        if(frameMs>=4000){phaseFourSecond++;fourSecondFrames++;}
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_LONG_FRAME: phase="+phase+"; frameMs="+frameMs.ToString("F2")+
                            "; atLeastOneSecond=true; atLeastFourSeconds="+(frameMs>=4000)+"; gameSeconds="+clock.ToSeconds()+
                            "; existingSpikeAndSectionScopes=true; samplesContinue=true");
                    }
                }
                if(frames.Count==0)throw new InvalidOperationException("Performance phase collected no unpaused frames: "+phase);
                frames.Sort();
                phases++;maximumFrame=Math.Max(maximumFrame,frames[frames.Count-1]);
                RuntimeTiming.Report(phase);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_FRAMES: "+phase+"; frames="+frames.Count+
                    "; p50Ms="+frames[frames.Count/2].ToString("F2")+
                    "; p95Ms="+frames[(int)((frames.Count-1)*.95)].ToString("F2")+
                    "; p99Ms="+frames[(int)((frames.Count-1)*.99)].ToString("F2")+
                    "; maxMs="+frames[frames.Count-1].ToString("F2")+
                    "; gen0="+(GC.CollectionCount(0)-collections)+"; atLeastOneSecondFrames="+phaseOneSecond+
                    "; atLeastFourSecondFrames="+phaseFourSecond+"; nativeScopes="+nativeScopes);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_THRESHOLDS: phases="+phases+"; secondsPerPhase=60; maxFrameMs="+maximumFrame.ToString("F2")+
                "; oneSecondQualityThresholdMs=1000; oneSecondFrames="+oneSecondFrames+"; reportedHangThresholdMs=4000; reportedHangFrames="+fourSecondFrames+
                "; nativeScopes="+nativeScopes+"; oneSecondQualityPass="+(oneSecondFrames==0)+"; reportedHangPass="+(fourSecondFrames==0)+
                "; allSamplesCollected=true; noGeneralPerformanceGuarantee=true");
            if(Environment.GetEnvironmentVariable("POTIB_ACCEPTANCE_CHECKPOINT")=="1")
            {
                if(!NativeProbeText.IsTestCharacter(game.PlayerEntity.Name))
                    throw new InvalidOperationException("Acceptance checkpoint requires the owned disposable character.");
                const string title="People - Daggerfall acceptance ready";
                yield return ConversationMoodProbe.SaveCheckpoint(game,game.PlayerEntity.Name,title);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACCEPTANCE_CHECKPOINT_SAVED: title="+title+
                    "; actualObservedState=true; injectedActors=false; injectedBookings=false; extraWarmup=false");
            }
            if(fourSecondFrames>0)
                throw new InvalidOperationException("Reported four-to-six-second performance stall reproduced; all three phases and native scopes were retained for attribution.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERFORMANCE_PASS: three 60-second city samples collected; reported four-second hang absent in these samples; "+
                "oneSecondQualityPass="+(oneSecondFrames==0)+"; nativeScopes="+nativeScopes+"; broader or loiter-specific performance remains outside this result.");
        }
    }
}
