using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Items;
using HarmonyLib;

namespace PeopleOfTheIliacBay.QuestHooks
{
    /// <summary>Reports native transfers, including a consumed UID merging into an existing stack.</summary>
    public sealed class NativeItemTransferHooks : IDisposable
    {
        private const string Owner = "peopleoftheiliacbay.corpse-key-transfer";
        private static NativeItemTransferHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<DaggerfallUnityItem, ItemCollection, ItemCollection, bool> observe;
        private readonly Func<DaggerfallUnityItem, ItemCollection, ItemCollection, bool> persist;
        private readonly Action<DaggerfallUnityItem, ItemCollection, ItemCollection> committed;
        private readonly Action<DaggerfallUnityItem, ItemCollection, ItemCollection> rolledBack;
        private static readonly System.Reflection.MethodInfo findStack=AccessTools.Method(typeof(ItemCollection),"FindExistingStack");
        private sealed class Observation
        {
            public DaggerfallUnityItem Item;
            public DaggerfallUnityItem MergeTarget;
            public int Count;
            public int TargetCount;
        }

        public NativeItemTransferHooks(
            Func<DaggerfallUnityItem, ItemCollection, ItemCollection, bool> observe,
            Func<DaggerfallUnityItem, ItemCollection, ItemCollection, bool> persist,
            Action<DaggerfallUnityItem, ItemCollection, ItemCollection> committed,
            Action<DaggerfallUnityItem, ItemCollection, ItemCollection> rolledBack)
        {
            if (observe == null || persist == null || committed == null || rolledBack == null) throw new ArgumentNullException();
            if (active != null) throw new InvalidOperationException("Item transfer hooks already installed.");
            this.observe = observe; this.persist = persist; this.committed = committed; this.rolledBack = rolledBack;
            active = this;
            try
            {
                var method = AccessTools.Method(typeof(ItemCollection), "Transfer", new[] {
                    typeof(DaggerfallUnityItem), typeof(ItemCollection), typeof(ItemCollection.AddPosition) });
                if (method == null) throw new MissingMethodException("Native item transfer API changed.");
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(NativeItemTransferHooks), nameof(Before)),
                    postfix: new HarmonyMethod(typeof(NativeItemTransferHooks), nameof(After)));
                var all = AccessTools.Method(typeof(ItemCollection), "TransferAll", new[] { typeof(ItemCollection) });
                if (all == null) throw new MissingMethodException("Native bulk transfer API changed.");
                harmony.Patch(all,prefix:new HarmonyMethod(typeof(NativeItemTransferHooks),nameof(BeforeAll)));
            }
            catch { active = null; harmony.UnpatchAll(Owner); throw; }
        }

        private static void Before(ItemCollection __instance, DaggerfallUnityItem __0,
            ItemCollection __1, out Observation __state)
        {
            __state = null;
            var self = active;
            if (self == null || __0 == null || __1 == null || ReferenceEquals(__instance, __1) ||
                !__1.Contains(__0) || __instance.Contains(__0)) return;
            try
            {
                if(!self.observe(__0,__1,__instance))return;
                var stack=findStack==null?null:(DaggerfallUnityItem)findStack.Invoke(__instance,new object[]{__0});
                __state=new Observation{Item=__0,MergeTarget=stack,Count=__0.stackCount,TargetCount=stack==null?0:stack.stackCount};
            }
            catch (Exception error) { throw new InvalidOperationException("Native transfer ownership check failed.",error); }
        }

        private static void After(ItemCollection __instance, DaggerfallUnityItem __0,
            ItemCollection __1, Observation __state)
        {
            var self = active;
            if (__state==null || self == null) return;
            bool moved = !__1.Contains(__0) && (__instance.Contains(__0) ||
                __state.MergeTarget != null && __instance.Contains(__state.MergeTarget) &&
                (long)__state.MergeTarget.stackCount >= (long)__state.TargetCount + __state.Count);
            if(!moved)return;
            bool saved=false;
            try
            {
                saved=self.persist(__0,__1,__instance);
            }
            catch (Exception error) { UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TRANSFER_PERSIST_REJECTED: " + error); }
            if(!saved)
            {
                RollBack(__state,__instance,__1);
                try { self.rolledBack(__0,__1,__instance); }
                catch(Exception error) { throw new InvalidOperationException("Native item transfer rolled back, but source resnapshot failed.",error); }
                return;
            }
            try { self.committed(__0,__1,__instance); }
            catch(Exception error) { UnityEngine.Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TRANSFER_POST_COMMIT_FAILED: " + error); }
        }

        private static void RollBack(Observation state,ItemCollection destination,ItemCollection source)
        {
            var item=state.Item;
            if(destination.Contains(item)) destination.RemoveItem(item);
            else if(state.MergeTarget!=null && destination.Contains(state.MergeTarget) &&
                (long)state.MergeTarget.stackCount >= (long)state.TargetCount + state.Count)
                state.MergeTarget.stackCount-=state.Count;
            else throw new InvalidOperationException("Native item transfer failed persistence and destination state cannot be rolled back.");

            if(!source.Contains(item)) source.AddItem(item,noStack:true);
            if(!source.Contains(item) || destination.Contains(item))
                throw new InvalidOperationException("Native item transfer rollback did not restore source ownership.");
        }

        public void Dispose()
        {
            if (active == this) active = null;
            harmony.UnpatchAll(Owner);
        }

        private static bool BeforeAll(ItemCollection __instance, ItemCollection __0)
        {
            var self=active;
            if(self==null || __0==null || ReferenceEquals(__instance,__0))return true;
            var observed=new List<DaggerfallUnityItem>();
            for(int i=0;i<__0.Count;i++)
            {
                var item=__0.GetItem(i);
                if(item==null)continue;
                try {if(self.observe(item,__0,__instance))observed.Add(item);}
                catch(Exception error){throw new InvalidOperationException("Native bulk transfer ownership check failed.",error);}
            }
            if(observed.Count==0)return true;
            // Native bulk transfer mutates every item before a postfix can persist
            // ownership. Route tracked items through the single-item transaction.
            foreach(var item in observed)
            {
                if(!__0.Contains(item))continue;
                __instance.Transfer(item,__0);
                if(__0.Contains(item))break;
            }
            return false;
        }
    }
}
