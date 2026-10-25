using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Utility.AssetInjection;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using HarmonyLib;

namespace PeopleOfTheIliacBay.QuestHooks
{
    // Explicit profiling fixture only. Observe native work without replacing it.
    public sealed class NativeClimateTimingHooks : IDisposable
    {
        private const string Owner="local.peopleoftheiliacbay.climate-timing";
        private static NativeClimateTimingHooks active;
        private readonly Harmony harmony=new Harmony(Owner);
        private readonly Dictionary<MethodBase,string> names=new Dictionary<MethodBase,string>();
        private readonly Func<string,long> begin;
        private readonly Action<string,long> end;
        public NativeClimateTimingHooks(Func<string,long> begin,Action<string,long> end)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-profile-climate")<0)
                throw new InvalidOperationException("Native climate timing requires the explicit profiling flag.");
            if(active!=null)throw new InvalidOperationException("Native climate timing is already active.");
            this.begin=begin??throw new ArgumentNullException(nameof(begin));
            this.end=end??throw new ArgumentNullException(nameof(end));active=this;
            try
            {
                Add(typeof(DaggerfallLocation).GetMethod("ApplyClimateSettings"),"native-location-climate");
                Add(typeof(DaggerfallMesh).GetMethod("SetClimate"),"native-mesh-climate");
                Add(typeof(MaterialReader).GetMethod("ChangeClimate"),"native-material-climate");
                Add(typeof(DaggerfallGroundPlane).GetMethod("SetClimate",new[]{typeof(DaggerfallUnity),typeof(ClimateBases),typeof(ClimateSeason)}),"native-ground-climate");
                Add(typeof(DaggerfallBillboard).GetMethod("SetMaterial",new[]{typeof(int),typeof(int),typeof(int)}),"native-billboard-material");
                Add(typeof(TextureReplacement).GetMethod("GetStaticBillboardMaterial"),"native-billboard-replacement");
                const BindingFlags privateStatic=BindingFlags.Static|BindingFlags.NonPublic;
                Add(typeof(TextureReplacement).GetMethod("LoadFromCacheOrImport",privateStatic),"native-billboard-texture-load");
                Add(typeof(TextureReplacement).GetMethod("TryImportTextureFromDisk",privateStatic),"native-texture-disk");
                Add(typeof(Mod).GetMethod("HasAsset"),"native-mod-has-asset");
                Add(typeof(DaggerfallBillboardBatch).GetMethod("SetMaterial",new[]{typeof(int),typeof(bool)}),"native-billboard-batch-material");
                Add(typeof(DaggerfallBillboardBatch).GetMethod("Apply"),"native-billboard-batch-apply");
            }
            catch {Dispose();throw;}
        }
        private void Add(MethodInfo method,string name)
        {
            if(method==null)throw new MissingMethodException(name);
            names.Add(method,name);
            const BindingFlags flags=BindingFlags.Static|BindingFlags.NonPublic;
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(NativeClimateTimingHooks).GetMethod("Before",flags)),
                finalizer:new HarmonyMethod(typeof(NativeClimateTimingHooks).GetMethod("After",flags)));
        }
        private static void Before(MethodBase __originalMethod,out long __state)
        {__state=active==null?0:active.begin(active.names[__originalMethod]);}
        private static Exception After(MethodBase __originalMethod,long __state,Exception __exception)
        {
            if(active!=null && __state!=0)active.end(active.names[__originalMethod],__state);
            return __exception;
        }
        public void Dispose()
        {if(active!=this)return;harmony.UnpatchAll(Owner);active=null;}
    }
}
