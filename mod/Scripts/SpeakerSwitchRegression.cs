using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class SpeakerSwitchRegression
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, Transform room, StaticNPC keeper)
        {
            var game = GameManager.Instance;
            var fixture = UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, room);
            var staticFixture=new GameObject("Speaker switch static fixture"); staticFixture.transform.SetParent(room,false);
            try
            {
                // A neutral authored-style actor avoids random quest offers or
                // keeper refusal intercepting the target-cache regression.
                var data=keeper.Data; data.hash+=100001; data.factionID=0; data.nameSeed=975310;
                keeper=staticFixture.AddComponent<StaticNPC>();
                typeof(StaticNPC).GetField("npcData",Private).SetValue(keeper,data);
                var actor = fixture.AddComponent<ResidentActor>();
                var streets=(StreetPopulation)typeof(PeopleOfTheIliacBayMod).GetField("streets",Private).GetValue(host);
                var plans=(IList)typeof(StreetPopulation).GetField("plans",Private).GetValue(streets);
                Resident resident=null;
                foreach(var plan in plans)
                {
                    var candidate=(Resident)plan.GetType().GetField("Resident").GetValue(plan);
                    if(candidate.IsAlive && candidate.AgeGroup==ResidentAgeGroup.Adult && !candidate.IsQuestProtected)
                    { resident=candidate; break; }
                }
                if(resident==null) throw new InvalidOperationException("Speaker fixture needs a real scheduled adult identity.");
                actor.Initialize(resident,1,game.StreamingWorld.GetCurrentCityNavigation());
                actor.Person.NameNPC="Isabella Speaker Test";
                // Real public entry points, including the reused native Talk window.
                // These sequences failed despite CurrentNPCType being correct.
                for(int pass=0; pass<2; pass++)
                {
                    yield return Open(host, actor.Person, null, false, pass>0 ? "violet scarf" : null);
                    if(pass==0)
                    {
                        Append(host,"My violet scarf.","I remember your scarf.");
                        var journalStore=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(host);
                        var yesterday=new DaggerfallWorkshop.Utility.DaggerfallDateTime(DaggerfallUnity.Instance.WorldTime.Now);
                        double at=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()-86400;
                        yesterday.FromSeconds((ulong)at);
                        journalStore.RememberConversation(actor.Resident.Id,at,yesterday.DateString()+", "+yesterday.Year,
                            "Earlier meeting","We spoke yesterday.","I remember our earlier talk.","model");
                    }
                    yield return Open(host, null, keeper, false, pass>0 ? "copper kettle" : null);
                    if(pass==0) Append(host,"My copper kettle.","I remember your kettle.");
                    yield return Open(host, actor.Person, null, false, "violet scarf");
                    yield return Open(host, null, keeper, false, "copper kettle");
                }
                yield return Open(host, null, keeper, true, "copper kettle");
                DaggerfallUI.Instance.PopToHUD(); yield return null; yield return null;
                var store=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(host);
                var entries=store.ConversationTranscript(actor.Resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                if(entries.FindAll(e=>e.PlayerText.Contains("violet scarf")).Count!=1)
                    throw new InvalidOperationException("History replay duplicated saved exchanges.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SPEAKER_SWITCH_PASS: mobile/static/mobile and static/mobile/static; native name, portrait, identity and memory; consecutive cache retained.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRANSCRIPT_REOPEN_PASS: each speaker resumes their own history without cross-speaker leakage or duplicate journal entries.");
            }
            finally { DaggerfallUI.Instance.PopToHUD(); UnityEngine.Object.Destroy(fixture); UnityEngine.Object.Destroy(staticFixture); }
        }

        private static void Append(PeopleOfTheIliacBayMod host,string question,string answer)
        {
            typeof(PeopleOfTheIliacBayMod).GetMethod("AppendDialoguePair",Private).Invoke(host,
                new object[]{DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow,question,answer});
        }

        private static IEnumerator Open(PeopleOfTheIliacBayMod host, MobilePersonNPC mobile, StaticNPC stationary, bool consecutive=false, string history=null)
        {
            DaggerfallUI.Instance.PopToHUD(); yield return null; yield return null;
            var manager = GameManager.Instance.TalkManager;
            if(mobile) manager.TalkToMobileNPC(mobile); else manager.TalkToStaticNPC(stationary, false);
            // The fixture's reputation may legitimately refuse service. This
            // regression tests target caching, not willingness or prices.
            if(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow))
            {
                DaggerfallUI.Instance.PopToHUD();
                typeof(TalkManager).GetField("reactionToPlayer",Private).SetValue(manager,75);
                typeof(TalkManager).GetField("alreadyRejectedOnce",Private).SetValue(manager,false);
                typeof(TalkManager).GetMethod("TalkToNpc",Private).Invoke(manager,null);
            }
            yield return null; yield return null;
            var window = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            string expected = mobile ? mobile.NameNPC : stationary.DisplayName;
            if(window==null || manager.NameNPC!=expected)
                throw new InvalidOperationException("Speaker switch selected wrong native name: expected "+expected+", got "+manager.NameNPC);
            var label = (TextLabel)typeof(DaggerfallTalkWindow).GetField("labelNameNPC",Private).GetValue(window);
            if(label.Text!=expected) throw new InvalidOperationException("Speaker switch retained stale visible name.");
            if(consecutive && !(bool)typeof(TalkManager).GetField("sameTalkTargetAsBefore",Private).GetValue(manager))
                throw new InvalidOperationException("Consecutive same-speaker native cache was unnecessarily reset.");
            string identity = mobile ? mobile.GetComponent<ResidentActor>().Resident.Id :
                "npc:"+stationary.Data.mapID+":"+stationary.Data.buildingKey+":"+stationary.Data.hash;
            foreach(string field in new[]{"conversationPersonId","memoryPersonId"})
                if((string)typeof(PeopleOfTheIliacBayMod).GetField(field,Private).GetValue(host)!=identity)
                    throw new InvalidOperationException("Speaker switch retained stale "+field);
            if(history!=null)
            {
                string visible=(string)typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory",BindingFlags.Static|BindingFlags.NonPublic)
                    .Invoke(null,new object[]{window});
                if(!visible.Contains(history)) throw new InvalidOperationException("Reopened dialogue lost saved history: "+history);
                if(visible.Contains(mobile ? "copper kettle" : "violet scarf"))
                    throw new InvalidOperationException("Reopened dialogue leaked another NPC's transcript.");
                float deadline=Time.realtimeSinceStartup+45;
                while((bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private).GetValue(host) &&
                    Time.realtimeSinceStartup<deadline) yield return null;
                var transcriptField=(FieldInfo)typeof(PeopleOfTheIliacBayMod).GetField("conversationListField",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                var transcript=(ListBox)transcriptField.GetValue(window);
                if(transcript.GetItem(transcript.Count-1).textLabel.Name!="PeopleOfTheIliacBayReturnGreeting")
                    throw new InvalidOperationException("Reopened dialogue lacks a fresh returning greeting after saved history.");
                if(!transcript.ListItems.Exists(i=>i.textLabel.Name=="PeopleOfTheIliacBayHistoryDay"))
                    throw new InvalidOperationException("Reopened dialogue lacks a native-font conversation date separator.");
                foreach(var row in transcript.ListItems)
                {
                    if(row.textLabel.Name=="PeopleOfTheIliacBayHistoryRule") throw new InvalidOperationException("Date separator still contains a dash row.");
                    if(row.textLabel.Name=="PeopleOfTheIliacBayHistoryDay" && row.textLabel.TextScale>=transcript.TextScale)
                        throw new InvalidOperationException("Date separator is not smaller than ordinary conversation text.");
                }
                var latestHeader=transcript.ListItems.FindLast(i=>i.textLabel.Name=="PeopleOfTheIliacBayHistoryDay");
                string latestDate=latestHeader==null ? "" : latestHeader.tag as string;
                if(latestDate==null || latestDate.Length<7 || latestDate[5]!=',' ||
                    !DateTime.TryParseExact(latestDate.Substring(0,5),"HH:mm",System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,out _))
                    throw new InvalidOperationException("Current conversation timestamp is missing its compact clock/date.");
                if(mobile && transcript.ListItems.FindAll(i=>i.textLabel.Name=="PeopleOfTheIliacBayHistoryDay").Count<2)
                    throw new InvalidOperationException("Yesterday's conversation was not separated from today's greeting.");
                if(mobile)
                {
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,"People-conversation-reopen-dates.png"));
                }
            }
            // Portrait must match a fresh native render for this exact target.
            var portraitField=typeof(DaggerfallTalkWindow).GetField("texturePortrait",Private);
            var actual=(Texture2D)portraitField.GetValue(window);
            var bytes=actual.GetPixels32();
            typeof(TalkManager).GetField(mobile ? "lastTargetMobileNPC" : "lastTargetStaticNPC",Private).SetValue(manager,null);
            var npcData=typeof(TalkManager).GetField("npcData",Private).GetValue(manager) as TalkManager.NPCData;
            bool same=false;
            if(mobile) manager.SetTargetNPC(mobile,npcData.factionData,ref same);
            else manager.SetTargetNPC(stationary,npcData.factionData,ref same);
            var fresh=((Texture2D)portraitField.GetValue(window)).GetPixels32();
            if(bytes.Length!=fresh.Length) throw new InvalidOperationException("Stale portrait dimensions.");
            for(int i=0;i<bytes.Length;i++) if(!bytes[i].Equals(fresh[i]))
                throw new InvalidOperationException("Speaker switch retained the other NPC's portrait.");
        }
    }
}
