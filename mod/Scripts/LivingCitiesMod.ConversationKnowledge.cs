using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class LivingCitiesMod
    {
        private bool conversationBusy;
        private bool conversationSessionOpen;
        private bool conversationDispositionEnabled = true, conversationMapRequestsEnabled = true;
        private string conversationPersonId;
        private bool conversationEncounterRecorded;
        private int conversationEphemeralOffset;
        private int conversationNativeDisposition, conversationEphemeralInsults;
        private int conversationDispositionBeforeMessage;
        private string conversationResponseCue = "";
        private TalkManager.ListItem conversationLastTopic;
        private int conversationLastTranscriptCount;
        private IDisposable conversationHooks;
        private static readonly FieldInfo lastConversationTone = typeof(TalkManager).GetField("lastToneIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo selectedToneField = typeof(DaggerfallTalkWindow).GetField("selectedTalkTone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo reactionTierField = typeof(TalkManager).GetField("reactionToPlayer_0_1_2", BindingFlags.Instance | BindingFlags.NonPublic);

        private void BeginGroundedConversation()
        {
            if (conversationSessionOpen) return;
            InstallConversationHooks();
            conversationSessionOpen = true;
            conversationBusy = false;
            conversationEncounterRecorded = false;
            conversationLastTopic = null;
            var talkWindow = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            var transcript = talkWindow == null || conversationListField == null ? null : conversationListField.GetValue(talkWindow) as ListBox;
            conversationLastTranscriptCount = transcript == null ? 0 : transcript.Count;
            conversationEphemeralOffset = 0;
            conversationEphemeralInsults = 0;
            conversationResponseCue = "";
            var manager = GameManager.Instance.TalkManager;
            conversationNativeDisposition = CurrentDialogueDisposition();
            var mobile = manager.CurrentNPCType==TalkManager.NPCType.Mobile ? manager.MobileNPC : null;
            var actor = mobile ? mobile.GetComponent<ResidentActor>() : null;
            conversationPersonId = actor && actor.Resident != null ? actor.Resident.Id : null;
            if (manager.CurrentNPCType==TalkManager.NPCType.Static && manager.StaticNPC)
            {
                var data = manager.StaticNPC.Data;
                // Match the core's native identity when available, including shop heads.
                conversationPersonId = "npc:" + data.mapID + ":" + data.buildingKey + ":" + data.hash;
            }
            conversationHomeTopicAvailable = false;
            if (conversationDispositionEnabled && conversationPersonId != null && !saveFault)
                store.SetConversationBaseline(conversationPersonId, conversationNativeDisposition,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(), CaptureDialogueFactionId(manager));
            // Unmanaged vanilla wanderers have no durable identity. Never merge
            // unrelated NPCs merely because they happen to share a generated name.
            if (conversationDispositionEnabled && conversationPersonId != null && reactionToPlayerField != null)
                reactionToPlayerField.SetValue(manager, ConversationEffects.Reaction(conversationNativeDisposition,
                    store.ConversationDisposition(conversationPersonId)));
            if (lastConversationTone != null) lastConversationTone.SetValue(manager, -1);
            BeginDialogueMemory(talkWindow);
        }

        private void InstallConversationHooks()
        {
            if (conversationHooks != null) return;
            var hookType = Type.GetType("LivingCities.QuestHooks.NativeConversationHooks, LivingCities.QuestHooks", true);
            conversationHooks = (IDisposable)Activator.CreateInstance(hookType, new object[] {
                new Func<int, int>(native => conversationSessionOpen && conversationDispositionEnabled && !saveFault ?
                    ConversationEffects.AdjustReactionTier(native, ConversationOffset()) : native),
                new Func<string>(NativeConversationAnswerOverride),
                new Func<MobilePersonNPC, bool>(CanStartMobileConversation) });
        }

        private bool CanStartMobileConversation(MobilePersonNPC person)
        {
            var actor = person ? person.GetComponent<ResidentActor>() : null;
            if (!actor || actor.Resident == null || !actor.Resident.IsAlive) return true;
            string id = actor.Resident.Id;
            bool reporting = streets && streets.HasDialogueComplaint(id);
            bool alarmed = streets && streets.IsAlarmed(id);
            bool grudge = !saveFault && store.HoldsPartnerGrudge(actor.Resident,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            if (ConversationAccess.CanStart(reporting, alarmed, grudge)) return true;
            DaggerfallUI.AddHUDText(reporting ? "They hurry toward the guards and refuse to speak." :
                "They keep their distance and refuse to speak.");
            return false;
        }

        private void SubmitFreeText(DaggerfallTalkWindow window, string playerText)
        {
            if (conversationBusy) { DaggerfallUI.AddHUDText("Waiting for the current reply."); return; }
            if (!conversationEncounterRecorded && conversationPersonId != null && store != null && !saveFault)
            {
                store.RecordConversationEncounter(conversationPersonId, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                conversationEncounterRecorded = true;
            }
            conversationBusy = true;
            var changedTranscript=conversationListField.GetValue(window) as ListBox;
            if(changedTranscript!=null && changedTranscript.Count>conversationLastTranscriptCount)
                conversationHomeTopicAvailable=false;
            if (ConversationClock.IsQuestion(playerText))
            {
                conversationHomeTopicAvailable=false;
                conversationResponseCue = "";
                conversationDispositionBeforeMessage = CurrentDialogueDisposition();
                if (HandleConversationHostility(window, playerText, "neutral", true)) return;
                conversationBusy = false;
                AppendDialoguePair(window, playerText, CurrentConversationClock());
                Debug.Log("LIVING_CITIES_DIALOGUE_CLOCK: answered from WorldTime.Now");
                return;
            }
            conversationDispositionBeforeMessage = CurrentDialogueDisposition();
            if (HandleConversationHostility(window, playerText, "neutral", true)) return;
            if (TryHandleConversationHomeRequest(window, playerText)) return;
            conversationHomeTopicAvailable=false;
            var manager = GameManager.Instance.TalkManager;
            var native = new Dictionary<int, TalkManager.ListItem>();
            var all = new List<ConversationTopic>();
            var seen = new HashSet<TalkManager.ListItem>();
            AddConversationTopics(manager.ListTopicLocation, "Where is / Location", all, native, seen);
            AddConversationTopics(manager.ListTopicPerson, "Where is / People", all, native, seen);
            AddConversationTopics(manager.ListTopicThings, "Where is / Things", all, native, seen);
            AddConversationTopics(manager.ListTopicTellMeAbout, "Tell me about", all, native, seen);
            int workId = all.Count;
            native[workId] = new TalkManager.ListItem { caption = "Work", questionType = TalkManager.QuestionType.Work };
            all.Add(new ConversationTopic(workId, "Work / jobs", "Work", false));
            var transcript = conversationListField == null ? null : conversationListField.GetValue(window) as ListBox;
            // Native topic clicks can change the subject between typed messages.
            // A previewed/default topic is not a previously answered question.
            if (transcript != null && transcript.Count > conversationLastTranscriptCount)
                conversationLastTopic = manager.CurrentQuestionListItem;
            var previous = conversationLastTopic;
            int previousId = -1;
            foreach (var pair in native) if (pair.Value == previous) { previousId = pair.Key; break; }
            // Groups remain available even in huge cities. Relevant named subjects
            // rank above unrelated buildings; no answer getters run during retrieval.
            var words = Regex.Matches(playerText.ToLowerInvariant(), @"[\p{L}\p{N}]{3,}").Cast<Match>().Select(m => m.Value).Distinct().ToArray();
            var ranked = all.OrderByDescending(t => TopicRank(t, words, previousId)).ThenBy(t => t.Id);
            var candidates = new List<ConversationTopic>();
            int length = 0;
            foreach (var topic in ranked)
            {
                int size = topic.Caption.Length + topic.Path.Length + 24;
                if (length + size > 8000 || candidates.Count >= 100) continue;
                candidates.Add(topic); length += size;
            }
            string history = ReadConversationHistory(window);
            string prompt = ConversationKnowledge.Prompt(playerText, candidates, previousId, history);
            int requestSession=memorySession;
            if (!localDialogue.Classify(NewDialogueConnection(), prompt, history, output =>
            {
                if (!CurrentMemorySession(window,requestSession)) return;
                if (output == LocalDialogueClient.ContextLimit) { conversationBusy = false; return; }
                if (output == LocalDialogueClient.Unavailable) { conversationBusy = false;
                    DaggerfallUI.AddHUDText("Local dialogue unavailable. Check the local model server."); return; }
                var choice = ConversationKnowledge.Parse(output, candidates, playerText, previousId) ??
                    ConversationKnowledge.ExactFallback(playerText, candidates, previousId);
                // A clearly direct threat cannot be softened by an inconsistent model label.
                if (ConversationKnowledge.IsDirectFamilyThreat(playerText))
                    choice = new ConversationSelection("social", -1, "family_threat");
                Debug.Log("LIVING_CITIES_DIALOGUE_INTENT: " + choice.Intent + "; topic=" + choice.TopicId + "; tone=" + choice.Tone);
                conversationResponseCue = "";
                if(choice.Tone == "flirt" && ConversationPartnerIsChild())
                {
                    conversationBusy = false;
                    AppendDialoguePair(window, playerText, "Leave me alone! Guards!");
                    ReportChildDialogueOffense();
                    DaggerfallUI.Instance.PopToHUD();
                    DaggerfallUI.AddHUDText("Leave me alone! Guards!");
                    return;
                }
                string effectiveTone = choice.Tone == "flirt" ? ResolveConversationFlirt() : choice.Tone;
                conversationDispositionBeforeMessage = CurrentDialogueDisposition();
                if (effectiveTone == "family_threat") ApplyFamilyThreatDisposition();
                else ApplyConversationDisposition(effectiveTone, playerText);
                if (HandleConversationHostility(window, playerText, effectiveTone, choice.Intent != "social")) return;
                if (choice.Intent == "social" || choice.Intent == "unknown")
                {
                    if (choice.Intent == "unknown" && ConversationKnowledge.WantsMap(playerText))
                        conversationResponseCue = " No verified map destination is available. Do not claim to have marked the map.";
                    SubmitSocialText(window, playerText, history); return;
                }
                TalkManager.ListItem selected;
                if (!native.TryGetValue(choice.TopicId, out selected))
                { SubmitSocialText(window, playerText, history); return; }
                conversationBusy = false;
                AppendDialoguePair(window, playerText, AnswerNativeConversationTopic(window, selected, choice.Intent == "map"));
            }))
            {
                conversationBusy = false;
                DaggerfallUI.AddHUDText("Waiting for the current reply.");
            }
        }

        private static int TopicRank(ConversationTopic topic, string[] words, int previous)
        {
            int score = topic.Id == previous ? 10000 : topic.Group ? 30 : 0;
            string caption = topic.Caption.ToLowerInvariant(), path = topic.Path.ToLowerInvariant();
            foreach (string word in words)
            {
                string stem = word.EndsWith("s") && word.Length > 3 ? word.Substring(0, word.Length - 1) : word;
                if (caption.Contains(stem)) score += 100;
                else if (path.Contains(stem)) score += 40;
            }
            return score;
        }

        private static void AddConversationTopics(List<TalkManager.ListItem> items, string path,
            List<ConversationTopic> topics, Dictionary<int, TalkManager.ListItem> native, HashSet<TalkManager.ListItem> seen)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null || item.type == TalkManager.ListItemType.NavigationBack || !seen.Add(item)) continue;
                int id = topics.Count;
                native[id] = item;
                bool group = item.type == TalkManager.ListItemType.ItemGroup;
                topics.Add(new ConversationTopic(id, item.caption, path, group));
                if (group) AddConversationTopics(item.listChildItems, path + " / " + item.caption, topics, native, seen);
            }
        }

        private int ConversationOffset()
        { return conversationPersonId == null ? conversationEphemeralOffset : store.ConversationDisposition(conversationPersonId); }

        private bool ConversationPartnerIsChild()
        {
            var manager = GameManager.Instance.TalkManager;
            var actor = manager.CurrentNPCType==TalkManager.NPCType.Mobile && manager.MobileNPC ? manager.MobileNPC.GetComponent<ResidentActor>() : null;
            return actor && actor.Resident != null && actor.Resident.AgeGroup == ResidentAgeGroup.Child ||
                manager.CurrentNPCType==TalkManager.NPCType.Static && manager.StaticNPC && manager.StaticNPC.IsChildNPC;
        }

        private void ReportChildDialogueOffense()
        {
            var player = GameManager.Instance.PlayerEntity;
            // Preserve existing charges, particularly Murder, rather than reducing them.
            if(player.CrimeCommitted == DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None)
                player.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Trespassing;
            var guards = GetComponent<GuardPopulation>();
            if(guards) guards.ReportDialogueOffense();
        }

        private string ResolveConversationFlirt()
        {
            var manager = GameManager.Instance.TalkManager;
            var mobile = manager.CurrentNPCType==TalkManager.NPCType.Mobile ? manager.MobileNPC : null;
            var actor = mobile ? mobile.GetComponent<ResidentActor>() : null;
            var stationary = manager.CurrentNPCType==TalkManager.NPCType.Static ? manager.StaticNPC : null;
            bool child = ConversationPartnerIsChild();
            string gender = stationary ? stationary.Data.gender.ToString() : mobile ? mobile.Gender.ToString() : "unknown";
            string identity = conversationPersonId ?? (mobile ? "unmanaged:" + mobile.GetInstanceID() : null);
            string tone = ConversationFlirting.Tone(identity, GameManager.Instance.PlayerEntity.Name,
                gender, GameManager.Instance.PlayerEntity.Gender.ToString(), child, ConversationOffset());
            conversationResponseCue = child ? "Do not engage in romance. Redirect to ordinary conversation." :
                tone == "polite" ? "This romantic approach is welcome. Feel flattered and respond warmly, without promising a relationship." :
                tone == "rude" ? "This romantic approach is unwelcome. Decline clearly and show mild displeasure; do not reciprocate." :
                "Do not reciprocate romance; redirect politely to ordinary conversation.";
            Debug.Log("LIVING_CITIES_DIALOGUE_FLIRT: reception=" + tone + "; child=" + child);
            return tone;
        }

        private void ApplyConversationDisposition(string tone, string text)
        {
            if (!conversationDispositionEnabled || saveFault) return;
            int change;
            if (conversationPersonId != null)
                change = store.ApplyConversationTone(conversationPersonId, tone == "apology" ? "polite" : tone, text, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            else
            {
                int before = conversationEphemeralOffset;
                int penalty = tone == "insult" ? (conversationEphemeralInsults++ > 0 ? -25 : -10) : tone == "rude" ? -2 : 0;
                conversationEphemeralOffset = Mathf.Clamp(before + penalty, -200, 0);
                change = conversationEphemeralOffset - before;
            }
            if (change != 0 && reactionToPlayerField != null)
            {
                reactionToPlayerField.SetValue(GameManager.Instance.TalkManager, ConversationEffects.Reaction(conversationNativeDisposition, ConversationOffset()));
                if (lastConversationTone != null) lastConversationTone.SetValue(GameManager.Instance.TalkManager, -1);
                Debug.Log("LIVING_CITIES_DIALOGUE_DISPOSITION: delta=" + change + "; offset=" + ConversationOffset());
            }
        }

        private void ApplyFamilyThreatDisposition()
        {
            int offset = Mathf.Clamp(-100 - conversationNativeDisposition, -200, 5);
            if (conversationPersonId != null && !saveFault)
                store.SetConversationThreatDisposition(conversationPersonId, conversationNativeDisposition,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            else conversationEphemeralOffset = offset;
            if (reactionToPlayerField != null)
                reactionToPlayerField.SetValue(GameManager.Instance.TalkManager,
                    ConversationEffects.Reaction(conversationNativeDisposition, offset));
            if (lastConversationTone != null) lastConversationTone.SetValue(GameManager.Instance.TalkManager, -1);
            Debug.Log("LIVING_CITIES_FAMILY_THREAT: baseline=" + conversationNativeDisposition + "; offset=" + offset +
                "; actual=-100");
        }

        private string AnswerNativeConversationTopic(DaggerfallTalkWindow window, TalkManager.ListItem topic, bool mark)
        {
            var manager = GameManager.Instance.TalkManager;
            if (topic.type == TalkManager.ListItemType.ItemGroup)
            {
                conversationLastTopic = null;
                var names = topic.listChildItems == null ? new string[0] : topic.listChildItems
                    .Where(t => t.type != TalkManager.ListItemType.NavigationBack).Select(t => t.caption).Take(10).ToArray();
                return names.Length == 0 ? "Which place did you have in mind?" :
                    "Which place did you mean: " + string.Join("; ", names) + "?";
            }
            conversationLastTopic = topic;
            var tone = selectedToneField == null ? DaggerfallTalkWindow.TalkTone.Normal : (DaggerfallTalkWindow.TalkTone)selectedToneField.GetValue(window);
            manager.GetQuestionText(topic, tone); // Sets the subject used by native answer macros.
            string answer = manager.GetAnswerText(topic);
            if (!mark) return answer;
            if (!conversationMapRequestsEnabled) return answer + " I can describe the way, though I cannot mark your map.";
            if (topic.questionType != TalkManager.QuestionType.LocalBuilding)
                return answer; // Quest/person/regional reveals remain entirely native-owned.
            bool knows = topic.npcKnowledgeAboutItem == TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem || manager.NPCsKnowEverything();
            int reaction = reactionTierField == null ? 0 : (int)reactionTierField.GetValue(manager);
            if (!knows || reaction == 0) return answer;
            if (GameManager.Instance.IsPlayerInside) return answer; // Preserve native indoor direction-only rule.
            manager.GetKeySubjectBuildingOnMap(); // Native %loc macro discovers and records quest hints.
            return GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(topic.buildingKey) ?
                "I've marked " + topic.caption + " on your map." : answer;
        }
    }
}
