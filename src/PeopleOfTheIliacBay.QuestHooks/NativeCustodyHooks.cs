using System;
using System.Reflection;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using HarmonyLib;

namespace PeopleOfTheIliacBay.QuestHooks
{
    // Only the final native prison day advances the calendar by the sentence.
    // Ordinary rest, waiting and other time changes never enter this hook.
    public sealed class NativeCustodyHooks : IDisposable
    {
        private const string Owner="peopleoftheiliacbay.custodial-absence";
        private static NativeCustodyHooks active;
        private readonly Harmony harmony=new Harmony(Owner);
        private readonly Func<Action> begin;
        public NativeCustodyHooks(Func<Action> begin)
        {
            if(active!=null)throw new InvalidOperationException("Custody hook already installed.");
            this.begin=begin??throw new ArgumentNullException(nameof(begin));active=this;
            try
            {
                var method=AccessTools.Method(typeof(DaggerfallCourtWindow),"UpdatePrisonScreen");
                if(method==null)throw new MissingMethodException("Native prison time API changed.");
                harmony.Patch(method,prefix:new HarmonyMethod(typeof(NativeCustodyHooks).GetMethod(nameof(Before),BindingFlags.Static|BindingFlags.NonPublic)),
                    postfix:new HarmonyMethod(typeof(NativeCustodyHooks).GetMethod(nameof(After),BindingFlags.Static|BindingFlags.NonPublic)));
            }
            catch{Dispose();throw;}
        }
        private static void Before(int ___daysInPrisonLeft,PlayerEntity ___playerEntity,out Action __state)
        {
            __state=active!=null && ___daysInPrisonLeft==1 && ___playerEntity!=null && ___playerEntity.InPrison?
                active.begin():null;
        }
        private static void After(Action __state){if(__state!=null)__state();}
        public void Dispose(){if(active!=this)return;harmony.UnpatchAll(Owner);active=null;}
    }
}
