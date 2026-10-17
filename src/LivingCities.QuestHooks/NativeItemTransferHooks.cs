using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Items;
using HarmonyLib;

namespace LivingCities.QuestHooks
{
    /// <summary>Reports completed, non-stacking transfers without changing native inventory rules.</summary>
    public sealed class NativeItemTransferHooks : IDisposable
    {
        private const string Owner = "livingcities.corpse-key-transfer";
        private static NativeItemTransferHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<DaggerfallUnityItem, ItemCollection, ItemCollection, bool> observe;
        private readonly Action<DaggerfallUnityItem, ItemCollection, ItemCollection> transferred;

        public NativeItemTransferHooks(
            Func<DaggerfallUnityItem, ItemCollection, ItemCollection, bool> observe,
            Action<DaggerfallUnityItem, ItemCollection, ItemCollection> transferred)
        {
            if (observe == null || transferred == null) throw new ArgumentNullException();
            if (active != null) throw new InvalidOperationException("Item transfer hooks already installed.");
            this.observe = observe; this.transferred = transferred;
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
                harmony.Patch(all,
                    prefix: new HarmonyMethod(typeof(NativeItemTransferHooks), nameof(BeforeAll)),
                    postfix: new HarmonyMethod(typeof(NativeItemTransferHooks), nameof(AfterAll)));
            }
            catch { active = null; harmony.UnpatchAll(Owner); throw; }
        }

        private static void Before(ItemCollection __instance, DaggerfallUnityItem __0,
            ItemCollection __1, out bool __state)
        {
            __state = false;
            var self = active;
            if (self == null || __0 == null || __1 == null || ReferenceEquals(__instance, __1) ||
                !__1.Contains(__0) || __instance.Contains(__0)) return;
            try { __state = self.observe(__0, __1, __instance); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("LIVING_CITIES_TRANSFER_OBSERVER_FAILED: " + error); }
        }

        private static void After(ItemCollection __instance, DaggerfallUnityItem __0,
            ItemCollection __1, bool __state)
        {
            var self = active;
            if (!__state || self == null || __1.Contains(__0) || !__instance.Contains(__0)) return;
            try { self.transferred(__0, __1, __instance); }
            catch (Exception error) { UnityEngine.Debug.LogError("LIVING_CITIES_TRANSFER_COMMIT_FAILED: " + error); }
        }

        public void Dispose()
        {
            if (active == this) active = null;
            harmony.UnpatchAll(Owner);
        }

        private static void BeforeAll(ItemCollection __instance, ItemCollection __0,
            out List<DaggerfallUnityItem> __state)
        {
            __state = null;
            if (active == null || __0 == null || ReferenceEquals(__instance, __0)) return;
            for (int i = 0; i < __0.Count; i++)
            {
                var item = __0.GetItem(i);
                bool selected;
                Before(__instance, item, __0, out selected);
                if (!selected) continue;
                if (__state == null) __state = new List<DaggerfallUnityItem>();
                __state.Add(item);
            }
        }

        private static void AfterAll(ItemCollection __instance, ItemCollection __0,
            List<DaggerfallUnityItem> __state)
        {
            if (__state == null) return;
            foreach (var item in __state) After(__instance, item, __0, true);
        }
    }
}
