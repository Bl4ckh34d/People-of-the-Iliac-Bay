using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEditor.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

// Opt-in editor play-mode capture. All settings and saves live in a copied fixture.
[InitializeOnLoad]
public static class PeopleOfTheIliacBayEditorProfile
{
    private const string Flag="-peopleoftheiliacbay-editor-profile";
    private static bool finishing;
    private static float nextSizeCheck;
    private static string activeCapture;
    static PeopleOfTheIliacBayEditorProfile()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),Flag)<0)return;
        Application.logMessageReceived+=OnLog;
        EditorApplication.update+=Tick;
    }
    private static string Root
    {get{return Environment.GetEnvironmentVariable("POTIB_EDITOR_PROFILE_ROOT")??
        Path.GetFullPath(Path.Combine(Application.dataPath,"../../LivingCities/work/playtest-2026-10-26/editor"));}}

    // Read an existing native capture without entering play mode or loading a save.
    public static void Analyze()
    {
        Profiler.enabled=false;
        foreach(string path in Directory.GetFiles(Root,"*.raw"))
        {
            ProfilerDriver.ClearAllFrames();
            if(!ProfilerDriver.LoadProfile(path,false))throw new InvalidOperationException("Cannot load "+path);
            using(var output=new StreamWriter(path+".csv"))
            {
                output.WriteLine("frame,frameMs,totalMs,selfMs,gcBytes,calls,path");
                for(int frame=ProfilerDriver.firstFrameIndex;frame<=ProfilerDriver.lastFrameIndex;frame++)
                using(var view=ProfilerDriver.GetHierarchyFrameDataView(frame,0,HierarchyFrameDataView.ViewModes.Default,
                    HierarchyFrameDataView.columnTotalTime,false))
                {
                    if(!view.valid || view.frameTimeMs<100)continue;
                    WriteItem(output,view,view.GetRootItemID(),"");
                }
            }
        }
        EditorApplication.Exit(0);
    }
    private static void WriteItem(StreamWriter output,HierarchyFrameDataView view,int item,string parent)
    {
        double total=view.GetItemColumnDataAsDouble(item,HierarchyFrameDataView.columnTotalTime);
        if(total<10)return;
        string path=parent+"/"+view.GetItemName(item);
        output.WriteLine(view.frameIndex+","+view.frameTimeMs.ToString(CultureInfo.InvariantCulture)+","+
            total.ToString(CultureInfo.InvariantCulture)+","+
            view.GetItemColumnDataAsDouble(item,HierarchyFrameDataView.columnSelfTime).ToString(CultureInfo.InvariantCulture)+","+
            view.GetItemColumnDataAsDouble(item,HierarchyFrameDataView.columnGcMemory).ToString(CultureInfo.InvariantCulture)+","+
            view.GetItemColumnDataAsDouble(item,HierarchyFrameDataView.columnCalls).ToString(CultureInfo.InvariantCulture)+
            ",\""+path.Replace("\"","\"\"")+"\"");
        var children=new List<int>();view.GetItemChildren(item,children);
        foreach(int child in children)WriteItem(output,view,child,path);
    }

    public static void Run()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),Flag)<0)throw new InvalidOperationException("Explicit editor profile flag required.");
        if(!Directory.Exists(Path.Combine(Root,"data/Saves")))throw new InvalidOperationException("Copied profile fixture missing.");
        var api=new System.Text.StringBuilder();
        foreach(var method in typeof(ProfilerDriver).GetMethods(BindingFlags.Static|BindingFlags.Public))
            if(method.Name.Contains("Frame") || method.Name.Contains("Profile"))api.AppendLine(method.ToString());
        var raw=typeof(ProfilerDriver).Assembly.GetType("UnityEditor.Profiling.RawFrameDataView");
        if(raw!=null)foreach(var method in raw.GetMethods(BindingFlags.Instance|BindingFlags.Public))api.AppendLine(method.ToString());
        File.WriteAllText(Path.Combine(Root,"profiler-api.txt"),api.ToString());
        SessionState.SetFloat("potib-profile-start",(float)EditorApplication.timeSinceStartup);
        SessionState.SetInt("potib-profile-exit",-1);
        SessionState.SetBool("potib-profile-running",true);
        EditorSceneManager.OpenScene("Assets/Scenes/DaggerfallUnityStartup.unity");
        ProfilerDriver.deepProfiling=false;ProfilerDriver.profileEditor=false;
        Profiler.enabled=false;Profiler.enableBinaryLog=false;
        EditorApplication.isPlaying=true;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void IsolateRuntimeData()
    {
        if(Array.IndexOf(Environment.GetCommandLineArgs(),Flag)<0)return;
        typeof(DaggerfallUnityApplication).GetField("persistentDataPath",BindingFlags.Static|BindingFlags.NonPublic)
            .SetValue(null,Path.Combine(Root,"data"));
        Application.runInBackground=true;
        Application.SetStackTraceLogType(LogType.Log,StackTraceLogType.None);
    }
    private static void OnLog(string text,string stack,LogType kind)
    {
        if(!SessionState.GetBool("potib-profile-running",false))return;
        const string start="PEOPLE_OF_THE_ILIAC_BAY_PROFILE_PHASE_START: ";
        if(text.StartsWith(start,StringComparison.Ordinal))
        {
            Profiler.enabled=false;Profiler.enableBinaryLog=false;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-profile-counters-only")>=0)return;
            activeCapture=Path.Combine(Root,text.Substring(start.Length)+".raw");
            Profiler.logFile=activeCapture;Profiler.enableBinaryLog=true;Profiler.enabled=true;
        }
        else if(text.StartsWith("PEOPLE_OF_THE_ILIAC_BAY_LOITER_PROFILE:",StringComparison.Ordinal))
        {Profiler.enabled=false;Profiler.enableBinaryLog=false;activeCapture=null;}
        else if(text.Contains("PEOPLE_OF_THE_ILIAC_BAY_EDITOR_PROFILE_COMPLETE"))Finish("complete");
        else if(kind==LogType.Exception)Finish("exception: "+text+"\n"+stack);
    }
    private static void Tick()
    {
        int exit=SessionState.GetInt("potib-profile-exit",-1);
        if(exit>=0 && !EditorApplication.isPlayingOrWillChangePlaymode)
        {SessionState.SetInt("potib-profile-exit",-1);EditorApplication.Exit(exit);return;}
        if(activeCapture!=null && EditorApplication.timeSinceStartup>=nextSizeCheck)
        {
            nextSizeCheck=(float)EditorApplication.timeSinceStartup+1;
            if(File.Exists(activeCapture) && new FileInfo(activeCapture).Length>256L*1024*1024)
            {
                Profiler.enabled=false;Profiler.enableBinaryLog=false;
                File.AppendAllText(Path.Combine(Root,"capture-limits.txt"),activeCapture+" reached 256 MiB; phase counters continue.\n");
                activeCapture=null;
            }
        }
        // Full family acceptance includes ordinary day preparation, a bank trip,
        // two physically walked exterior legs and observed Inn service.
        double limit=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-verify-family-inn")>=0?1200:600;
        if(SessionState.GetBool("potib-profile-running",false) &&
            EditorApplication.timeSinceStartup-SessionState.GetFloat("potib-profile-start",0)>limit)Finish("timeout");
    }
    private static void Finish(string result)
    {
        if(finishing)return;finishing=true;
        File.WriteAllText(Path.Combine(Root,"result.txt"),result);
        SessionState.SetBool("potib-profile-running",false);
        SessionState.SetInt("potib-profile-exit",result=="complete"?0:1);
        Profiler.enabled=false;Profiler.enableBinaryLog=false;
        EditorApplication.delayCall+=()=>EditorApplication.isPlaying=false;
    }
}
