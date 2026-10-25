using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit owned fixture only. Uses the native loiter path, without saving.
    internal static class LoiterPerformanceProbe
    {
        internal static IEnumerator Run(StreetPopulation streets)
        {
            IDisposable timing=null;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-profile-climate")>=0)
            {
                var type=Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeClimateTimingHooks, PeopleOfTheIliacBay.QuestHooks",true);
                timing=(IDisposable)Activator.CreateInstance(type,new object[]{new Func<string,long>(RuntimeTiming.Start),new Action<string,long>(RuntimeTiming.End)});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CLIMATE_TIMING_ACTIVE: native behavior unchanged; nested durations are inclusive.");
            }
            using(timing) {yield return RunObserved(streets);}
        }
        private static IEnumerator RunObserved(StreetPopulation streets)
        {
            var game=GameManager.Instance;
            if(!streets || !streets.Ready || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Loiter profiling requires the exterior morning fixture.");
            game.PlayerEntity.GodMode=true;DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROFILE_MODS: "+NativeNavigationWorld.Profile());
            if(!NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready)
                throw new InvalidOperationException("Native navigation is unavailable; refusing a fallback-motion benchmark.");
            yield return Sample("morning-before-loiter",20);
            bool overnight=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-profile-overnight")>=0;
            foreach(int hours in overnight?new[]{1,3,12}:new[]{1,3})
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROFILE_PHASE_START: loiter-"+hours+"h");
                RuntimeTiming.Reset();int gc=GC.CollectionCount(0);
                double before=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var frames=new List<double>();
                // Respect the user's native limit (normally three hours). A night
                // is several native loiters, not an invalid twelve-hour request.
                int remaining=hours;
                while(remaining>0)
                {
                    int chunk=Math.Min(remaining,Math.Max(1,DaggerfallUnity.Settings.LoiterLimitInHours));
                    double chunkStart=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROFILE_LOITER_CHUNK: hours="+chunk+"; remaining="+remaining);
                    DaggerfallUI.UIManager.PushWindow(new DaggerfallRestWindow(DaggerfallUI.UIManager,false));
                    yield return null;
                    var rest=DaggerfallUI.UIManager.TopWindow as DaggerfallRestWindow;
                    if(rest==null)throw new InvalidOperationException("Native loiter window failed to open.");
                    typeof(DaggerfallRestWindow).GetMethod("LoiterPrompt_OnGotUserInput",BindingFlags.Instance|BindingFlags.NonPublic)
                        .Invoke(rest,new object[]{null,chunk.ToString(CultureInfo.InvariantCulture)});
                    double last=Time.realtimeSinceStartup;float deadline=Time.realtimeSinceStartup+90;
                    while(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()-chunkStart<chunk*3600 && Time.realtimeSinceStartup<deadline)
                    {
                        yield return null;double at=Time.realtimeSinceStartup;
                        frames.Add((at-last)*1000);last=at;
                    }
                    if(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()-chunkStart<chunk*3600)
                        throw new InvalidOperationException("Native loiter interrupted: topWindow="+
                            DaggerfallUI.UIManager.TopWindow.GetType().Name+"; advancedSeconds="+
                            (DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()-chunkStart).ToString(CultureInfo.InvariantCulture));
                    DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);remaining-=chunk;
                }
                if(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()-before<hours*3600)
                    throw new InvalidOperationException("Native loiter failed to finish its requested hours.");
                Report("loiter-"+hours+"h",frames,gc);
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                yield return Sample("after-loiter-"+hours+"h",20);
            }
        }
        private static IEnumerator Sample(string phase,float seconds)
        {
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PROFILE_PHASE_START: "+phase);
            RuntimeTiming.Reset();int gc=GC.CollectionCount(0);var frames=new List<double>();
            double last=Time.realtimeSinceStartup;float deadline=Time.realtimeSinceStartup+seconds;
            while(Time.realtimeSinceStartup<deadline)
            {yield return null;double now=Time.realtimeSinceStartup;frames.Add((now-last)*1000);last=now;}
            Report(phase,frames,gc);
        }
        private static void Report(string phase,List<double> frames,int gc)
        {
            if(frames.Count==0)throw new InvalidOperationException("No frames recorded for "+phase);
            frames.Sort();RuntimeTiming.Report(phase);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_LOITER_PROFILE: phase="+phase+"; frames="+frames.Count+
                "; p50Ms="+frames[frames.Count/2].ToString("F2",CultureInfo.InvariantCulture)+
                "; p95Ms="+frames[Math.Min(frames.Count-1,(int)(frames.Count*.95))].ToString("F2",CultureInfo.InvariantCulture)+
                "; maxMs="+frames[frames.Count-1].ToString("F2",CultureInfo.InvariantCulture)+
                "; gen0="+(GC.CollectionCount(0)-gc)+"; residents="+UnityEngine.Object.FindObjectsOfType<ResidentActor>().Length+
                "; gameTime="+DaggerfallUnity.Instance.WorldTime.Now.ToSeconds().ToString("F0",CultureInfo.InvariantCulture));
        }
    }
}
