using System;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using System.Collections.Generic;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterface;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class LivingCitiesMod
    {
        private DaggerfallTalkWindow memoryWindow;
        private string memoryPersonId, memoryPlace, memoryDate;
        private double memoryOpenedAt;
        private int memoryCursor, memorySession;
        private readonly StringBuilder memoryQuestion = new StringBuilder();
        private readonly StringBuilder memoryAnswer = new StringBuilder();

        private void BeginDialogueMemory(DaggerfallTalkWindow window)
        {
            memorySession++;
            memoryWindow=window; memoryPersonId=PersistentMemoryIdentity();
            var now=DaggerfallUnity.Instance.WorldTime.Now;
            memoryOpenedAt=now.ToSeconds(); memoryDate=now.DateString()+", "+now.Year;
            memoryPlace=ConversationMemoryPlace();
            memoryQuestion.Length=memoryAnswer.Length=0;
            var transcript=window==null ? null : conversationListField.GetValue(window) as ListBox;
            memoryCursor=transcript==null ? 0 : transcript.Count;
            if(!saveFault && store!=null && !string.IsNullOrEmpty(memoryPersonId))
            {
                store.RecordConversationEncounter(conversationPersonId,memoryOpenedAt);
                conversationEncounterRecorded=true;
            }
        }

        private bool CurrentMemorySession(DaggerfallTalkWindow window,int session)
        { return !saveFault && conversationSessionOpen && memorySession==session && memoryWindow==window &&
            DaggerfallUI.UIManager.ContainsWindow(window); }

        private void EndDialogueMemory(bool capture)
        {
            if(capture && memoryWindow!=null && !saveFault && store!=null && !string.IsNullOrEmpty(memoryPersonId))
            {
                CaptureNativeDialogueMemory();
                FlushNativeMemoryPair();
                store.RememberObservation(memoryPersonId,memoryOpenedAt,memoryDate,memoryPlace,"Met and spoke with the player here.");
            }
            memorySession++; memoryWindow=null; memoryPersonId=null;
            memoryQuestion.Length=memoryAnswer.Length=0;
        }

        private string CurrentConversationMemory(string query)
        {
            if(saveFault || store==null || string.IsNullOrEmpty(memoryPersonId))
                return " No durable identity is available for this person; do not claim remembered meetings.";
            CaptureNativeDialogueMemory();
            return " "+store.RecallConversation(memoryPersonId,query,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),2000)+" "+ConversationRoomPresence();
        }

        private string PersistentMemoryIdentity()
        {
            var manager=GameManager.Instance.TalkManager;
            var npc=manager.CurrentNPCType==TalkManager.NPCType.Static ? manager.StaticNPC : null;
            if(!npc || npc.Data.context==StaticNPC.Context.Building) return conversationPersonId;
            // Generated quest actors can reuse a layout position. A new quest at
            // that position must never inherit the previous person's memories.
            var resource=npc.GetComponent<QuestResourceBehaviour>();
            if(resource && resource.QuestUID!=0 && resource.TargetSymbol!=null)
                return "quest-person:"+resource.QuestUID+":"+resource.TargetSymbol.Name;
            // Unowned custom/dungeon actors need an explicit stable identity adapter.
            return null;
        }

        private void CaptureNativeDialogueMemory()
        {
            if(memoryWindow==null || saveFault || store==null || string.IsNullOrEmpty(memoryPersonId)) return;
            var transcript=conversationListField.GetValue(memoryWindow) as ListBox;
            if(transcript==null) return;
            if(transcript.Count<memoryCursor) { memoryCursor=0; memoryQuestion.Length=memoryAnswer.Length=0; }
            for(;memoryCursor<transcript.Count;memoryCursor++)
            {
                var item=transcript.GetItem(memoryCursor);
                if(item==null || item.textLabel==null || string.IsNullOrWhiteSpace(item.textLabel.Text)) continue;
                string text=item.textLabel.Text.Replace('\n',' ').Replace('\r',' ');
                if(item.textColor==DaggerfallUI.DaggerfallQuestionTextColor)
                {
                    if(memoryAnswer.Length>0) FlushNativeMemoryPair();
                    memoryQuestion.Append(text).Append(' ');
                }
                else if(memoryQuestion.Length>0) memoryAnswer.Append(text).Append(' ');
            }
            FlushNativeMemoryPair();
        }

        private void FlushNativeMemoryPair()
        {
            if(memoryQuestion.Length==0 || memoryAnswer.Length==0) return;
            RememberDialogueExchange(memoryQuestion.ToString(),memoryAnswer.ToString(),"native");
            memoryQuestion.Length=memoryAnswer.Length=0;
        }

        private void RememberDialogueExchange(string question,string answer,string source)
        {
            if(saveFault || store==null || memoryWindow==null || string.IsNullOrEmpty(memoryPersonId)) return;
            var now=DaggerfallUnity.Instance.WorldTime.Now;
            store.RememberConversation(memoryPersonId,now.ToSeconds(),now.DateString()+", "+now.Year,
                ConversationMemoryPlace(),question,answer,source);
        }

        private static string ConversationMemoryPlace()
        {
            var game=GameManager.Instance;
            string town=game.PlayerGPS.HasCurrentLocation ? game.PlayerGPS.CurrentLocation.Name : game.PlayerGPS.CurrentRegionName;
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding) return "Outdoors in "+town;
            string building=ConversationBuildingName(game.PlayerEnterExit.BuildingDiscoveryData.buildingKey);
            return (string.IsNullOrWhiteSpace(building) ? "Inside a building" : building)+", "+town;
        }

        private void AppendRememberedDialoguePair(DaggerfallTalkWindow talk,string question,string answer,string source)
        {
            if(talk==null || appendConversationPair==null) return;
            CaptureNativeDialogueMemory();
            appendConversationPair.Invoke(talk,new object[]{question,answer});
            if(memoryWindow==talk) RememberDialogueExchange(question,answer,source);
            var transcript=conversationListField.GetValue(talk) as ListBox;
            if(transcript!=null)
            {
                conversationLastTranscriptCount=transcript.Count;
                if(memoryWindow==talk) memoryCursor=transcript.Count;
            }
        }

        private string ConversationRoomPresence()
        {
            var game=GameManager.Instance;
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding) return "";
            // Only nearby visible people, once per submitted turn. No global census
            // and no private conversations/quests are shared between NPCs.
            var names=new List<string>();
            var mobile=game.TalkManager.CurrentNPCType==TalkManager.NPCType.Mobile ? game.TalkManager.MobileNPC : null;
            var stationary=game.TalkManager.CurrentNPCType==TalkManager.NPCType.Static ? game.TalkManager.StaticNPC : null;
            var origin=mobile ? mobile.transform.position : stationary ? stationary.transform.position : game.PlayerObject.transform.position;
            var target=mobile ? mobile.transform : stationary ? stationary.transform : null;
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<MobilePersonNPC>())
                if(npc.transform!=target && !string.IsNullOrWhiteSpace(npc.NameNPC) &&
                    CanSeeMemoryPerson(origin,npc.transform,target) && names.Count<8) names.Add(npc.NameNPC);
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<StaticNPC>())
                if(npc.transform!=target && !string.IsNullOrWhiteSpace(npc.DisplayName) &&
                    CanSeeMemoryPerson(origin,npc.transform,target) && names.Count<8) names.Add(npc.DisplayName);
            return names.Count==0 ? " No other nearby visible people identified; this does not prove the whole building is empty." :
                " Other people currently visible nearby: "+string.Join(", ",names.ToArray())+". You do not know their private thoughts or conversations.";
        }

        private static bool CanSeeMemoryPerson(Vector3 origin,Transform person,Transform speaker)
        {
            if(!person.gameObject.activeInHierarchy || Vector3.SqrMagnitude(origin-person.position)>64) return false;
            var hits=Physics.RaycastAll(origin+Vector3.up*0.7f,person.position-origin,
                Vector3.Distance(origin,person.position),~0,QueryTriggerInteraction.Ignore);
            foreach(var hit in hits)
                if(hit.transform!=person && !hit.transform.IsChildOf(person) &&
                    (speaker==null || (hit.transform!=speaker && !hit.transform.IsChildOf(speaker)))) return false;
            return true;
        }
    }
}
