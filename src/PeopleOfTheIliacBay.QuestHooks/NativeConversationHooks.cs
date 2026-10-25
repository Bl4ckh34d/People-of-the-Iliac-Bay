using System;
using DaggerfallWorkshop.Game;
using HarmonyLib;

namespace PeopleOfTheIliacBay.QuestHooks
{
    // Applies personal attitude after vanilla personality, etiquette/streetwise and
    // topic checks. No faction reputation or player attributes are modified.
    public sealed class NativeConversationHooks : IDisposable
    {
        private const string Owner = "peopleoftheiliacbay.personal-conversation";
        private static NativeConversationHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<int, int> adjust;
        private readonly Func<string> answerOverride;
        private readonly Func<MobilePersonNPC, bool> allowMobileTalk;
        private readonly Func<StaticNPC, bool> allowStaticTalk;
        public bool HasQuestGreeting { get; private set; }

        public NativeConversationHooks(Func<int, int> adjust, Func<string> answerOverride = null,
            Func<MobilePersonNPC, bool> allowMobileTalk = null, Func<StaticNPC, bool> allowStaticTalk = null)
        {
            if (adjust == null) throw new ArgumentNullException(nameof(adjust));
            if (active != null) throw new InvalidOperationException("Conversation hooks already installed.");
            this.adjust = adjust; this.answerOverride = answerOverride; this.allowMobileTalk = allowMobileTalk;
            this.allowStaticTalk = allowStaticTalk; active = this;
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
                var staticTalk = AccessTools.Method(typeof(TalkManager), "TalkToStaticNPC");
                if (staticTalk == null) throw new MissingMethodException("DFU static conversation API changed.");
                harmony.Patch(staticTalk, prefix: new HarmonyMethod(typeof(NativeConversationHooks).GetMethod(nameof(BeforeStaticTalk), AccessTools.all)));
                // DFU caches one last target per kind, but name, portrait and
                // NPCData are shared. A -> B of another kind -> A must rebuild
                // A instead of reusing B's shared presentation and knowledge.
                var signature = new[] { typeof(MobilePersonNPC), typeof(DaggerfallConnect.Arena2.FactionFile.FactionData), typeof(bool).MakeByRefType() };
                var mobileTarget = AccessTools.Method(typeof(TalkManager), "SetTargetNPC", signature);
                signature[0] = typeof(StaticNPC);
                var staticTarget = AccessTools.Method(typeof(TalkManager), "SetTargetNPC", signature);
                if(mobileTarget==null || staticTarget==null) throw new MissingMethodException("DFU conversation target API changed.");
                harmony.Patch(mobileTarget, prefix: new HarmonyMethod(typeof(NativeConversationHooks).GetMethod(nameof(BeforeMobileTarget), AccessTools.all)));
                harmony.Patch(staticTarget, prefix: new HarmonyMethod(typeof(NativeConversationHooks).GetMethod(nameof(BeforeStaticTarget), AccessTools.all)));
                var greeting = AccessTools.Method(typeof(TalkManager), "GetNPCQuestGreeting");
                if(greeting==null) throw new MissingMethodException("DFU quest greeting API changed.");
                harmony.Patch(greeting, postfix: new HarmonyMethod(typeof(NativeConversationHooks).GetMethod(nameof(AfterQuestGreeting), AccessTools.all)));
            }
            catch { active = null; harmony.UnpatchAll(Owner); throw; }
        }

        private static void BeforeMobileTarget(ref StaticNPC ___lastTargetStaticNPC)
        { ___lastTargetStaticNPC = null; if(active!=null) active.HasQuestGreeting=false; }

        private static void BeforeStaticTarget(ref MobilePersonNPC ___lastTargetMobileNPC)
        { ___lastTargetMobileNPC = null; if(active!=null) active.HasQuestGreeting=false; }

        private static void AfterQuestGreeting(string __result)
        { if(active!=null) active.HasQuestGreeting=!string.IsNullOrEmpty(__result); }

        private static bool BeforeMobileTalk(MobilePersonNPC targetNPC)
        {
            if (active == null || active.allowMobileTalk == null) return true;
            try { return active.allowMobileTalk(targetNPC); }
            catch (Exception error)
            { UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_MOBILE_TALK_HOOK_FAILED: " + error); return true; }
        }

        private static bool BeforeStaticTalk(StaticNPC targetNPC)
        {
            if (active == null || active.allowStaticTalk == null) return true;
            try { return active.allowStaticTalk(targetNPC); }
            catch (Exception error)
            { UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_STATIC_TALK_HOOK_FAILED: " + error); return true; }
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
            { UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_CONVERSATION_ANSWER_HOOK_FAILED: " + error); return true; }
        }

        private static void AfterReaction(ref int __result)
        {
            if (active == null) return;
            try { __result = Math.Max(0, Math.Min(2, active.adjust(__result))); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_CONVERSATION_HOOK_FAILED: " + error); }
        }

        public void Dispose()
        { if (active == this) active = null; harmony.UnpatchAll(Owner); }
    }
}
