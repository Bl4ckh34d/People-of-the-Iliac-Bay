using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Command-line diagnostic only; normal play performs no timing or allocations.
    public static class RuntimeTiming
    {
        public static readonly bool Enabled=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-performance-probe")>=0;
        private sealed class Sample { public int Calls; public double Total,Maximum; }
        private static readonly Dictionary<string,Sample> samples=new Dictionary<string,Sample>();
        public static long Start() { return Enabled ? Stopwatch.GetTimestamp() : 0; }
        public static void End(string name,long started)
        {
            if(!Enabled) return;
            double elapsed=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
            Sample sample;
            if(!samples.TryGetValue(name,out sample)) { sample=new Sample(); samples.Add(name,sample); }
            sample.Calls++; sample.Total+=elapsed; sample.Maximum=Math.Max(sample.Maximum,elapsed);
            if(elapsed>=30) UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_SPIKE: "+name+"; ms="+elapsed.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
        }
        public static void RecordDuration(string name,long started)
        {
            if(!Enabled) return;
            double elapsed=(Stopwatch.GetTimestamp()-started)*1000d/Stopwatch.Frequency;
            Sample sample;
            if(!samples.TryGetValue(name,out sample)) { sample=new Sample(); samples.Add(name,sample); }
            sample.Calls++; sample.Total+=elapsed; sample.Maximum=Math.Max(sample.Maximum,elapsed);
        }
        public static void Reset() { samples.Clear(); }
        public static int Calls(string name) { Sample sample;return samples.TryGetValue(name,out sample) ? sample.Calls : 0; }
        public static void Report(string phase)
        {
            foreach(var pair in samples)
                UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERF_SECTION: "+phase+"; name="+pair.Key+"; calls="+pair.Value.Calls+
                    "; totalMs="+pair.Value.Total.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                    "; maxMs="+pair.Value.Maximum.ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
