using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    internal static class DialogueMemoryProbe
    {
        private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        public static IEnumerator Run(LivingCitiesMod host,DaggerfallTalkWindow talk,MobilePersonNPC person)
        {
            var type=typeof(LivingCitiesMod);
            var store=(WorldStateStore)type.GetField("store",Flags).GetValue(host);
            string id=(string)type.GetField("conversationPersonId",Flags).GetValue(host);
            var append=type.GetMethod("AppendDialoguePair",Flags);
            var read=type.GetMethod("ReadConversationHistory",BindingFlags.Static|BindingFlags.NonPublic);
            append.Invoke(host,new object[]{talk,"My sister's name is Mirabelle.","You told me your sister is Mirabelle."});
            // Exercise the native topic transcript path, not only our append helper.
            typeof(DaggerfallTalkWindow).GetMethod("SetQuestionAnswerPairInConversationListbox",Flags).Invoke(talk,
                new object[]{"I left my blue scarf at the inn.","You may wish to look for it there."});
            yield return null; yield return null;
            var before=store.Snapshot().ConversationMemories.Find(m=>m.PersonId==id);
            if(before==null || !before.Entries.Exists(e=>e.Source=="native" && e.PlayerText.Contains("blue scarf")))
                throw new InvalidOperationException("Native topic exchange was not journaled.");
            DaggerfallUI.Instance.PopToHUD();
            yield return null; yield return null;
            var serialized=SaveLoadManager.Serialize(typeof(WorldState),host.GetSaveData());
            store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),serialized));
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            string recalled=store.RecallConversation(id,"sister",now,2000);
            if(!recalled.Contains("Mirabelle") || !recalled.Contains("Observed event"))
                throw new InvalidOperationException("Native serialized NPC journal lost meeting or exchange.");
            if(store.RecallConversation("unrelated-test-person","sister",now,2000).Contains("Mirabelle"))
                throw new InvalidOperationException("NPC memories leaked to unrelated identity.");
            GameManager.Instance.TalkManager.TalkToMobileNPC(person);
            yield return new WaitForSecondsRealtime(0.3f);
            var reopened=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            if(reopened==null) throw new InvalidOperationException("Memory probe could not reopen conversation.");
            string fresh=(string)read.Invoke(null,new object[]{reopened});
            if(fresh.Contains("Mirabelle")) throw new InvalidOperationException("Memory test retained the old visible transcript.");
            type.GetMethod("SubmitFreeText",Flags).Invoke(host,new object[]{reopened,"What was my sister's name, from our earlier conversation?"});
            var busy=type.GetField("conversationBusy",Flags);
            float deadline=Time.realtimeSinceStartup+45f;
            while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<deadline) yield return null;
            string result=(string)read.Invoke(null,new object[]{reopened});
            if((bool)busy.GetValue(host) || !result.Contains("Mirabelle"))
                throw new InvalidOperationException("Live model did not recall saved memory in reopened conversation: "+result);
            Debug.Log("LIVING_CITIES_DIALOGUE_MEMORY_PASS: production/native exchanges; real close/reopen; native serializer restore; distinct NPC isolation; live model recalled Mirabelle with empty prior transcript.");
        }
    }
}
