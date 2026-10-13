using System;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using HarmonyLib;

namespace LivingCities.QuestHooks
{
    // Loaded from the mod bundle. No file patching or engine assembly replacement.
    public sealed class NativeQuestOfferHooks : IDisposable
    {
        private const string Owner = "local.livingcities.quest-offers";
        private static NativeQuestOfferHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<Quest, bool> canStart;
        private readonly Action<Quest> started, discard;
        private readonly Func<StaticNPC, string> residentName;
        public NativeQuestOfferHooks(Func<Quest, bool> canStart, Action<Quest> started, Action<Quest> discard, Func<StaticNPC, string> residentName)
        {
            if (active != null) throw new InvalidOperationException("Quest offer hooks already installed.");
            this.canStart = canStart ?? throw new ArgumentNullException(nameof(canStart));
            this.started = started ?? throw new ArgumentNullException(nameof(started));
            this.discard = discard ?? throw new ArgumentNullException(nameof(discard));
            this.residentName = residentName ?? throw new ArgumentNullException(nameof(residentName));
            active = this;
            try
            {
                var response = typeof(DaggerfallQuestPopupWindow).GetMethod("OfferQuest_OnButtonClick", BindingFlags.Instance | BindingFlags.NonPublic);
                var start = typeof(QuestMachine).GetMethod("StartQuest", new[] { typeof(Quest) });
                if (response == null || start == null) throw new MissingMethodException("Native quest offer boundaries changed.");
                harmony.Patch(response, prefix: Patch(nameof(BeforeResponse)));
                harmony.Patch(start, prefix: Patch(nameof(BeforeStart)), postfix: Patch(nameof(AfterStart)));
                harmony.Patch(typeof(StaticNPC).GetProperty("DisplayName").GetGetMethod(), postfix: Patch(nameof(AfterName)));
            }
            catch { Dispose(); throw; }
        }
        private static HarmonyMethod Patch(string name)
        { return new HarmonyMethod(typeof(NativeQuestOfferHooks).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)); }
        private static void BeforeResponse(Quest ___offeredQuest, ref DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            if (active == null) return;
            if (messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Yes && !active.canStart(___offeredQuest))
                messageBoxButton = DaggerfallMessageBox.MessageBoxButtons.No;
            // Native refusal removes temporary topics/rumors and closes the prompt.
            if (messageBoxButton != DaggerfallMessageBox.MessageBoxButtons.Yes) active.discard(___offeredQuest);
        }
        private static bool BeforeStart(Quest quest, out bool __state)
        {
            __state = active == null || active.canStart(quest);
            return __state;
        }
        private static void AfterStart(Quest quest, bool __state)
        {
            if (__state && active != null && quest != null && QuestMachine.Instance.GetQuest(quest.UID) == quest)
                active.started(quest);
        }
        private static void AfterName(StaticNPC __instance, ref string __result)
        {
            string name = active == null ? null : active.residentName(__instance);
            if (name != null) __result = name;
        }
        public void Dispose()
        {
            if (active != this) return;
            harmony.UnpatchAll(Owner); active = null;
        }
    }
}
