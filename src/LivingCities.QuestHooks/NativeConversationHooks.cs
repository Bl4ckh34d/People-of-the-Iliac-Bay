using System;
using DaggerfallWorkshop.Game;
using HarmonyLib;

namespace LivingCities.QuestHooks
{
    // Applies personal attitude after vanilla personality, etiquette/streetwise and
    // topic checks. No faction reputation or player attributes are modified.
    public sealed class NativeConversationHooks : IDisposable
    {
        private const string Owner = "livingcities.personal-conversation";
        private static NativeConversationHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<int, int> adjust;
        private readonly Func<string> answerOverride;
        private readonly Func<MobilePersonNPC, bool> allowMobileTalk;

        public NativeConversationHooks(Func<int, int> adjust, Func<string> answerOverride = null,
            Func<MobilePersonNPC, bool> allowMobileTalk = null)
        {
            if (adjust == null) throw new ArgumentNullException(nameof(adjust));
            if (active != null) throw new InvalidOperationException("Conversation hooks already installed.");
            this.adjust = adjust; this.answerOverride = answerOverride; this.allowMobileTalk = allowMobileTalk; active = this;
            try
            {
                var method = AccessTools.Method(typeof(TalkManager), "GetReactionToPlayer_0_1_2");
                if (method == null) throw new MissingMethodException("DFU conversation reaction API changed.");
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(NativeConversationHooks).GetMethod(nameof(AfterReaction), AccessTools.all)));
                var answer = AccessTools.Method(typeof(TalkManager), "GetAnswerText");
                if (answer == null) throw new MissingMethodException("DFU native conversation answer API changed.");
                harmony.Patch(answer, prefix: new HarmonyMethod(typeof(NativeConversationHooks).GetMethod(nameof(BeforeAnswer), AccessTools.all)));
                var mobileTalk = AccessTools.Method(typeof(TalkManager), "TalkToMobileNPC");
                if (mobileTalk == null) throw new MissingMethodException("DFU mobile conversation API changed.");
                harmony.Patch(mobileTalk, prefix: new HarmonyMethod(typeof(NativeConversationHooks).GetMethod(nameof(BeforeMobileTalk), AccessTools.all)));
            }
            catch { active = null; harmony.UnpatchAll(Owner); throw; }
        }

        private static bool BeforeMobileTalk(MobilePersonNPC targetNPC)
        {
            if (active == null || active.allowMobileTalk == null) return true;
            try { return active.allowMobileTalk(targetNPC); }
            catch (Exception error)
            { UnityEngine.Debug.LogWarning("LIVING_CITIES_MOBILE_TALK_HOOK_FAILED: " + error); return true; }
        }

        private static bool BeforeAnswer(ref string __result)
        {
            if (active == null || active.answerOverride == null) return true;
            try
            {
                string replacement = active.answerOverride();
                if (replacement == null) return true;
                __result = replacement;
                return false;
            }
            catch (Exception error)
            { UnityEngine.Debug.LogWarning("LIVING_CITIES_CONVERSATION_ANSWER_HOOK_FAILED: " + error); return true; }
        }

        private static void AfterReaction(ref int __result)
        {
            if (active == null) return;
            try { __result = Math.Max(0, Math.Min(2, active.adjust(__result))); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("LIVING_CITIES_CONVERSATION_HOOK_FAILED: " + error); }
        }

        public void Dispose()
        { if (active == this) active = null; harmony.UnpatchAll(Owner); }
    }
}
