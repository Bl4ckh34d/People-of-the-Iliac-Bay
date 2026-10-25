using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private sealed class ConversationGuideTarget
        {
            public ConversationTopic Topic;
            public TalkManager.ListItem Native;
            public int BuildingKey;
            public string Name;
            public NpcActionOption Option;
        }

        private readonly List<TalkManager.ListItem> conversationSuggestions = new List<TalkManager.ListItem>();
        private static readonly MethodInfo nativeKnowledge = typeof(TalkManager).GetMethod("GetNPCKnowledgeAboutItem", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo nativeBrowseReaction = typeof(TalkManager).GetMethod("GetReactionToPlayer_0_1_2", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo nativeNpcData = typeof(TalkManager).GetField("npcData", BindingFlags.Instance | BindingFlags.NonPublic);

        private bool TryGuideConversation(DaggerfallTalkWindow window,string text,string history,
            List<ConversationTopic> topics,List<ConversationTopic> ranked,
            Dictionary<int,TalkManager.ListItem> native,ConversationContextSnapshot snapshot)
        {
            InvalidatePendingConversationAction();
            const string noAction="No guide action was accepted. Do not promise movement or claim that the destination was found.";
            if(snapshot==null || !IsCurrentConversationContext(snapshot,window) || !conversationActor || !streets ||
                !streets.Ready || topics==null || native==null)
            {
                conversationResponseCue+=" "+noAction;
                SubmitActionSocialResult(window,text,history,noAction,snapshot,
                    "I cannot lead you there right now.");
                return true;
            }

            var targets=new List<ConversationGuideTarget>();
            foreach(var topic in ranked.OrderByDescending(t=>native.ContainsKey(t.Id) && (ReferenceEquals(native[t.Id],conversationLastTopic) || conversationSuggestions.Contains(native[t.Id]))))
            {
                TalkManager.ListItem item;
                ConversationGuideTarget target;
                if(topic==null || topic.Group || !native.TryGetValue(topic.Id,out item) ||
                    !TryResolveCurrentGuideTarget(topic,item,out target))continue;
                targets.Add(target);
                if(targets.Count>=99)break;
            }
            int homeSettlement,homeKey;string homeName;NpcActionOption homeOption;
            var currentDirectory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            if(TryConversationHome(out homeSettlement,out homeKey,out homeName) && currentDirectory &&
                !GameManager.Instance.IsPlayerInside && homeSettlement==currentDirectory.MapID &&
                streets.TryCreateConversationBuildingMoveOption(conversationActor.Resident.Id,homeSettlement,homeKey,
                    string.IsNullOrWhiteSpace(homeName)?"my home":homeName,out homeOption))
                targets.Add(new ConversationGuideTarget {Topic=new ConversationTopic(topics.Max(t=>t.Id)+1,
                    "This NPC's home","Verified residence",false),BuildingKey=homeKey,Name=homeName,Option=homeOption});
            if(targets.Count==0)
            {
                string result=noAction+" No currently offered native destination is both known to this NPC and physically reachable in this settlement. Ask for a more exact place; do not invent a target.";
                conversationResponseCue+=" "+result;
                SubmitActionSocialResult(window,text,history,result,snapshot,
                    "I do not know a suitable place well enough to lead you there. Which place do you mean?");
                return true;
            }
            var eligible=targets.Select(value=>value.Topic).ToList();
            int session=memorySession;string person=conversationPersonId;
            long stage=BeginConversationContextStage(snapshot,window);
            if(stage<=0){HandleRejectedConversationStage(snapshot,window,stage,text,noAction);return true;}
            string prompt=ConversationContextFormatter.ForRouting(snapshot,2048)+"\n"+
                NativeConversationReferenceContext(topics,native,-1)+"\n"+ConversationKnowledge.TopicSelectionPrompt("topic",text,eligible,history);
            if(!localDialogue.Classify(NewDialogueConnection(),prompt,history,output=>
            {
                if(!CurrentMemorySession(window,session))return;
                if(conversationPersonId!=person || !TryConsumeConversationContextStage(snapshot,window,stage))
                {HandleRejectedConversationStage(snapshot,window,stage,text,noAction);return;}
                if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                {ReportActionDialogueUnavailable(window,text,"Local dialogue could not bind a guide destination. No action was accepted.");return;}
                var binding=ConversationKnowledge.ParseTopicSelection(output,"topic",eligible);
                var selected=binding==null?null:targets.Find(value=>value.Topic.Id==binding.TopicId);
                ConversationGuideTarget current;
                current=selected;
                if(selected==null || selected.Native!=null && !TryResolveCurrentGuideTarget(selected.Topic,selected.Native,out current))
                {
                    string result=noAction+" No unique current destination matched the request.";
                    conversationResponseCue+=" "+result;
                    SubmitActionSocialResult(window,text,history,result,snapshot,
                        "Which exact place would you like me to lead you to?");
                    return;
                }
                TrySemanticActionSingleConversation(window,text,history,snapshot,new[]{current.Option});
            },ConversationKnowledge.TopicSelectionGrammar("topic",eligible),
                classificationTrace:SemanticActionModelCaptureProbe.ForStage("native-guide-binding")))
            {
                if(TryConsumeConversationContextStage(snapshot,window,stage))
                    ReportActionDialogueUnavailable(window,text,"Waiting for the current reply. No guide action was accepted.");
            }
            return true;
        }

        private string NativeConversationReferenceContext(IList<ConversationTopic> topics,
            Dictionary<int,TalkManager.ListItem> native,int previousId)
        {
            var facts=new System.Text.StringBuilder("Native conversation references (not new requests):\n");
            foreach(var topic in topics)
            {
                TalkManager.ListItem item;
                if(!native.TryGetValue(topic.Id,out item))continue;
                if(topic.Id==previousId || ReferenceEquals(item,conversationLastTopic))
                    facts.Append("Previously answered target ID: ").Append(topic.Id).Append("; ").Append(topic.Caption).Append('\n');
                if(conversationSuggestions.Contains(item))
                    facts.Append("Previously suggested target ID: ").Append(topic.Id).Append("; ").Append(topic.Caption).Append('\n');
            }
            return facts.ToString();
        }

        private bool TryResolveCurrentGuideTarget(ConversationTopic topic,TalkManager.ListItem item,
            out ConversationGuideTarget result)
        {
            result=null;
            var game=GameManager.Instance;var manager=game.TalkManager;
            var currentDirectory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(topic==null || item==null || item.type==TalkManager.ListItemType.ItemGroup ||
                !CurrentNativeItem(manager,item) || nativeKnowledge==null || !currentDirectory || game.IsPlayerInside ||
                conversationActor==null)return false;
            var selectedNative=item;
            if(item.questionType==TalkManager.QuestionType.QuestPerson)
            {
                TalkManager.ListItem wherePerson;
                if(!TryNativeQuestPersonLocation(manager,item,out wherePerson))return false;
                item=wherePerson;
            }
            if(item.npcKnowledgeAboutItem==TalkManager.NPCKnowledgeAboutItem.NotSet)
            {
                DFRandom.SaveSeed();
                try{item.npcKnowledgeAboutItem=(TalkManager.NPCKnowledgeAboutItem)nativeKnowledge.Invoke(manager,new object[]{item});}
                catch{return false;}
                finally{DFRandom.RestoreSeed();}
            }
            if(item.npcKnowledgeAboutItem!=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem && !manager.NPCsKnowEverything())return false;
            int buildingKey;
            if(!TryResolveGuideBuilding(manager,item,out buildingKey))return false;
            BuildingSummary summary;
            if(buildingKey<0 || !currentDirectory.GetBuildingSummary(buildingKey,out summary))return false;
            string name=string.IsNullOrWhiteSpace(item.caption)?manager.GetBuildingNameForBuildingKey(buildingKey):item.caption;
            NpcActionOption option;
            if(string.IsNullOrWhiteSpace(name) || !streets.TryCreateConversationBuildingMoveOption(
                conversationActor.Resident.Id,currentDirectory.MapID,buildingKey,name,out option))return false;
            result=new ConversationGuideTarget {Topic=topic,Native=selectedNative,BuildingKey=buildingKey,Name=name,Option=option};
            return true;
        }

        private static bool TryResolveGuideBuilding(TalkManager manager,TalkManager.ListItem item,out int buildingKey)
        {
            buildingKey=-1;
            try
            {
                if(item.questionType==TalkManager.QuestionType.LocalBuilding)
                {buildingKey=item.buildingKey;return buildingKey>=0;}
                if(item.questionType==TalkManager.QuestionType.Person || item.questionType==TalkManager.QuestionType.QuestPerson)
                {
                    Person person;manager.GetPersonResource(item.questID,item.key,out person);
                    if(person.IsQuestor?person.QuestorData.mapID!=GameManager.Instance.PlayerGPS.CurrentMapID:
                        manager.GetPersonSiteDetails(ref person).mapId!=GameManager.Instance.PlayerGPS.CurrentMapID)return false;
                    buildingKey=manager.GetPersonBuildingKey(ref person);return buildingKey>=0;
                }
                if(item.questionType==TalkManager.QuestionType.QuestLocation)
                {
                    var quest=GameManager.Instance.QuestMachine.GetQuest(item.questID);
                    var place=quest==null?null:quest.GetResource(item.key) as Place;
                    if(place==null || place.SiteDetails.mapId!=GameManager.Instance.PlayerGPS.CurrentMapID)return false;
                    buildingKey=place.SiteDetails.buildingKey;return buildingKey>=0;
                }
            }
            catch{return false;}
            return false;
        }

        private bool TryBrowseConversation(DaggerfallTalkWindow window, string text, string history,
            ConversationSelection choice, List<ConversationTopic> topics, Dictionary<int, TalkManager.ListItem> native,
            ConversationContextSnapshot snapshot=null)
        {
            if(snapshot!=null && !IsCurrentConversationContext(snapshot,window))
            {HandleRejectedConversationStage(snapshot,window,0,text,"No browse response or map action was accepted.");return true;}
            if(choice==null || (choice.Intent!="topic" && choice.Intent!="map")) return false;
            if(snapshot==null || topics==null || native==null) return false;

            TalkManager.ListItem selectedGroup;
            List<ConversationTopic> selectedRows=topics.FindAll(t=>t!=null && t.Id==choice.TopicId);
            if(selectedRows.Count!=1 || !selectedRows[0].Group ||
                !native.TryGetValue(choice.TopicId,out selectedGroup) ||
                selectedGroup==null || selectedGroup.type!=TalkManager.ListItemType.ItemGroup)
                return false; // A bound leaf follows the existing exact native answer path.

            var manager=GameManager.Instance.TalkManager;
            if(!CurrentNativeItem(manager,selectedGroup))
            {FailBoundBrowse(window,text,"The selected native topic is no longer available. No map action was accepted.");return true;}
            conversationLastTopic=null;
            List<TalkManager.ListItem> descendants=GroupDescendants(selectedGroup);
            var requested=topics.Where(t=>t!=null && !t.Group && native.ContainsKey(t.Id) &&
                IsReferenceIn(descendants,native[t.Id]) && native[t.Id].type!=TalkManager.ListItemType.ItemGroup &&
                native[t.Id].questionType==TalkManager.QuestionType.LocalBuilding && native[t.Id].questID==0)
                .Select(t=>native[t.Id]).ToList();
            var known=KnownBrowsePlaces(requested);
            if(choice.Intent=="map")
            {
                // Bind a follow-up from the current native candidates plus
                // actual conversation history; generated wording is not proof
                // that any particular candidate was spoken.
                return BeginBoundGroupMapSelection(window,text,history,choice.TopicId,selectedGroup,topics,native,known,snapshot);
            }

            var offered=known.Take(3).ToList();
            var tone=SelectedConversationTone(window);
            if(offered.Count>0 && !BrowseWilling(manager,offered[0],tone))
            {
                conversationResponseCue=" You are unwilling to share directions with this player. Refuse in your own voice; do not name destinations or claim a map marker.";
                SubmitBrowseText(window,text,history,"I would rather not give you directions.",snapshot);
                return true;
            }
            conversationResponseCue=offered.Count==0 ?
                " You do not know any local destination in this native topic group. Admit that naturally; do not invent a name or marker." :
                " KNOWLEDGE SEARCH RESULT: You personally know these real local places: "+
                string.Join("; ",offered.Select(BrowsePlaceFact).ToArray())+
                ". Suggest these places naturally, using their exact names. Give up to three choices. These are the only verified destinations for this reply. Do not invent another place, distance, service or marker. No map markers were placed.";
            string recommendation=offered.Count==0 ? "I don't know a place around here well enough to recommend one." :
                "You could try "+string.Join(" or ",offered.Select(t=>t.caption+" ("+manager.GetBuildingCompassDirection(t.buildingKey)+")").ToArray())+".";
            SubmitBrowseText(window,text,history,recommendation,snapshot,
                offered.Select(place=>place.caption).ToArray(),
                known.Skip(offered.Count).Select(place=>place.caption).ToArray(),offered);
            return true;
        }

        private bool BeginBoundGroupMapSelection(DaggerfallTalkWindow window,string text,string history,int groupId,
            TalkManager.ListItem selectedGroup,List<ConversationTopic> topics,Dictionary<int,TalkManager.ListItem> native,
            List<TalkManager.ListItem> known,ConversationContextSnapshot snapshot)
        {
            if(known.Count==0)
            {FailBoundBrowse(window,text,"No currently known local building is offered under that native topic. No map marker was placed.");return true;}
            var eligible=BoundMapCandidates(topics,native,known);
            if(eligible.Count==0)
            {FailBoundBrowse(window,text,"No bounded native map candidate matched that topic. No map marker was placed.");return true;}
            if(eligible.Count==1)
            {
                // The player permits any destination in this exact category, or
                // only one is known. No language-model tie-break is needed.
                var target=native[eligible[0].Id];
                var manager=GameManager.Instance.TalkManager;
                string answer=BrowseWilling(manager,target,SelectedConversationTone(window))?
                    AnswerNativeConversationTopic(window,target,true):"I would rather not mark a destination on your map.";
                conversationBusy=false;
                AppendDialoguePair(window,text,answer);
                return true;
            }
            if(localDialogue==null)
            {FailBoundBrowse(window,text,"Local dialogue is unavailable. No map marker was placed.");return true;}

            long stage=BeginConversationContextStage(snapshot,window);
            if(stage<=0)
            {HandleRejectedConversationStage(snapshot,window,stage,text,"No map marker was placed.");return true;}
            int session=memorySession;
            string person=conversationPersonId;
            string prompt=ConversationContextFormatter.ForRouting(snapshot,2048)+"\n"+
                ConversationKnowledge.TopicSelectionPrompt("map",text,eligible,history);
            bool started=localDialogue.Classify(NewDialogueConnection(),prompt,history,output=>{
                if(!CurrentMemorySession(window,session))return;
                if(conversationPersonId!=person)
                {HandleRejectedConversationStage(snapshot,window,stage,text,"No map marker was placed.");return;}
                if(!TryConsumeConversationContextStage(snapshot,window,stage))
                {HandleRejectedConversationStage(snapshot,window,stage,text,"No map marker was placed.");return;}
                if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                {FailBoundBrowse(window,text,"Local dialogue could not bind a map target. No map marker was placed.");return;}
                var binding=ConversationKnowledge.ParseTopicSelection(output,"map",eligible);
                if(binding==null || binding.Intent!="map")
                {FailBoundBrowse(window,text,"No offered native building matched. No map marker was placed.");return;}

                ConversationTopic row=eligible.Find(t=>t.Id==binding.TopicId);
                TalkManager.ListItem target;
                if(row==null || row.Group || !native.TryGetValue(binding.TopicId,out target) ||
                    !IsCurrentBoundGroupLeaf(GameManager.Instance.TalkManager,selectedGroup,target))
                {FailBoundBrowse(window,text,"The bound native map target is no longer current. No map marker was placed.");return;}
                var currentlyKnown=KnownBrowsePlaces(new[]{target});
                var tone=SelectedConversationTone(window);
                if(!currentlyKnown.Any(item=>object.ReferenceEquals(item,target)))
                {FailBoundBrowse(window,text,"The bound native map target is no longer known. No map marker was placed.");return;}
                if(!BrowseWilling(GameManager.Instance.TalkManager,target,tone))
                {
                    conversationBusy=false;
                    AppendRememberedDialoguePair(window,text,"I would rather not mark a destination on your map.","action");
                    return;
                }
                // This native answer path rechecks map setting, outdoor state,
                // item knowledge and reaction before touching the map.
                conversationLastTopic=null;
                string answer=AnswerNativeConversationTopic(window,target,true);
                conversationBusy=false;
                AppendDialoguePair(window,text,answer);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KNOWLEDGE_MAP_BOUND: group="+groupId+"; building="+
                    target.buildingKey+"; name="+target.caption+".");
            },ConversationKnowledge.TopicSelectionGrammar("map",eligible),
                classificationTrace:SemanticActionModelCaptureProbe.ForStage("native-topic-binding"));
            if(!started)HandleRejectedConversationStage(snapshot,window,stage,text,"No map marker was placed.");
            return true;
        }

        private void FailBoundBrowse(DaggerfallTalkWindow window,string text,string diagnostic)
        {
            conversationBusy=false;
            AppendRememberedDialoguePair(window,text,DialogueReplyFallback.UnavailableReply,"unavailable");
            DaggerfallUI.AddHUDText(diagnostic);
        }

        private static DaggerfallTalkWindow.TalkTone SelectedConversationTone(DaggerfallTalkWindow window)
        {
            return selectedToneField==null?DaggerfallTalkWindow.TalkTone.Normal:
                (DaggerfallTalkWindow.TalkTone)selectedToneField.GetValue(window);
        }

        private static List<TalkManager.ListItem> GroupDescendants(TalkManager.ListItem group)
        {
            var result=new List<TalkManager.ListItem>();
            var pending=new Stack<TalkManager.ListItem>();
            var visited=new HashSet<TalkManager.ListItem>();
            if(group!=null && group.listChildItems!=null)
                for(int i=group.listChildItems.Count-1;i>=0;i--)pending.Push(group.listChildItems[i]);
            while(pending.Count>0)
            {
                var item=pending.Pop();
                if(item==null || !visited.Add(item))continue;
                result.Add(item);
                if(item.type==TalkManager.ListItemType.ItemGroup && item.listChildItems!=null)
                    for(int i=item.listChildItems.Count-1;i>=0;i--)pending.Push(item.listChildItems[i]);
            }
            return result;
        }

        private static bool IsReferenceIn(IEnumerable<TalkManager.ListItem> items,TalkManager.ListItem target)
        { return items!=null && items.Any(item=>object.ReferenceEquals(item,target)); }

        private static bool CurrentNativeItem(TalkManager manager,TalkManager.ListItem target)
        {
            if(manager==null || target==null)return false;
            return NativeTreeContains(manager.ListTopicLocation,target) || NativeTreeContains(manager.ListTopicPerson,target) ||
                NativeTreeContains(manager.ListTopicThings,target) || NativeTreeContains(manager.ListTopicTellMeAbout,target);
        }

        private static bool NativeTreeContains(IEnumerable<TalkManager.ListItem> items,TalkManager.ListItem target)
        {
            if(items==null)return false;
            foreach(var item in items)
            {
                if(object.ReferenceEquals(item,target))return true;
                if(item!=null && item.type==TalkManager.ListItemType.ItemGroup &&
                    NativeTreeContains(item.listChildItems,target))return true;
            }
            return false;
        }

        private static bool IsCurrentBoundGroupLeaf(TalkManager manager,TalkManager.ListItem group,TalkManager.ListItem leaf)
        {
            return group!=null && leaf!=null && group.type==TalkManager.ListItemType.ItemGroup &&
                CurrentNativeItem(manager,group) && IsReferenceIn(GroupDescendants(group),leaf) && CurrentNativeItem(manager,leaf) &&
                leaf.type!=TalkManager.ListItemType.ItemGroup && leaf.questionType==TalkManager.QuestionType.LocalBuilding &&
                leaf.questID==0;
        }

        private static List<ConversationTopic> BoundMapCandidates(List<ConversationTopic> topics,
            Dictionary<int,TalkManager.ListItem> native,List<TalkManager.ListItem> known)
        {
            var result=new List<ConversationTopic>();
            var seen=new HashSet<int>();
            int chars=0;
            if(topics==null || native==null || known==null)return result;
            foreach(var item in known)
            {
                foreach(var topic in topics)
                {
                    TalkManager.ListItem mapped;
                    if(topic==null || topic.Group || seen.Contains(topic.Id) || !native.TryGetValue(topic.Id,out mapped) ||
                        !object.ReferenceEquals(mapped,item))continue;
                    int size=(topic.Caption??"").Length+(topic.Path??"").Length+24;
                    if(result.Count>=100 || chars+size>8000)break;
                    result.Add(topic);seen.Add(topic.Id);chars+=size;
                    break;
                }
                if(result.Count>=100 || chars>=8000)break;
            }
            return result;
        }

        private void SubmitBrowseText(DaggerfallTalkWindow window,string text,string history,string factual,
            ConversationContextSnapshot snapshot=null,string[] requiredNames=null,string[] excludedNames=null,
            IList<TalkManager.ListItem> offeredPlaces=null)
        {
            if(snapshot!=null && !IsCurrentConversationContext(snapshot,window))
            {HandleRejectedConversationStage(snapshot,window,0,text,"No browse response or map action was accepted.");return;}
            // The model receives verified native facts. Only validated native candidate IDs
            // can bind later map work; prose never authorizes a marker.
            SubmitSocialText(window,text,history,null,conversationResponseCue,prose=>
            {
                string reply=string.IsNullOrWhiteSpace(prose)?factual:prose;
                conversationSuggestions.Clear();
                if(offeredPlaces!=null)conversationSuggestions.AddRange(offeredPlaces);
                return reply;
            },snapshot:snapshot);
        }

        private static bool BrowseWilling(TalkManager manager, TalkManager.ListItem subject, DaggerfallTalkWindow.TalkTone tone)
        {
            if (nativeBrowseReaction == null || nativeNpcData == null || reactionTierField == null) return false;
            manager.GetQuestionText(subject, tone);
            if (lastConversationTone == null || (int)lastConversationTone.GetValue(manager) != DaggerfallTalkWindow.TalkToneToIndex(tone))
            {
                var data = nativeNpcData.GetValue(manager) as TalkManager.NPCData;
                if (data == null) return false;
                DFRandom.SaveSeed();
                try { reactionTierField.SetValue(manager, nativeBrowseReaction.Invoke(manager, new object[] { subject.questionType, data.socialGroup })); }
                finally { DFRandom.RestoreSeed(); }
            }
            return (int)reactionTierField.GetValue(manager) > 0;
        }

        private static List<TalkManager.ListItem> KnownBrowsePlaces(IEnumerable<TalkManager.ListItem> requested)
        {
            var game = GameManager.Instance;
            var manager = game.TalkManager;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var result = new List<TalkManager.ListItem>();
            if (!directory || nativeKnowledge == null) return result;
            foreach (var topic in requested)
            {
                BuildingSummary summary;
                if (topic.questionType != TalkManager.QuestionType.LocalBuilding || topic.questID != 0 ||
                    !directory.GetBuildingSummary(topic.buildingKey, out summary)) continue;
                if (topic.npcKnowledgeAboutItem == TalkManager.NPCKnowledgeAboutItem.NotSet)
                {
                    DFRandom.SaveSeed();
                    try { topic.npcKnowledgeAboutItem = (TalkManager.NPCKnowledgeAboutItem)nativeKnowledge.Invoke(manager, new object[] { topic }); }
                    finally { DFRandom.RestoreSeed(); }
                }
                if (topic.npcKnowledgeAboutItem == TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem || manager.NPCsKnowEverything()) result.Add(topic);
            }
            // BuildingSummary.Position is local to its RMB block. Native door
            // transforms already include block layout and streaming offsets.
            var origin=game.PlayerMotor.transform.position;
            var positions=new Dictionary<int,Vector3>();
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>(true))
                if(doors.Doors!=null)
                    for(int index=0;index<doors.Doors.Length;index++)
                        if(doors.Doors[index].doorType==DoorTypes.Building && !positions.ContainsKey(doors.Doors[index].buildingKey))
                            positions.Add(doors.Doors[index].buildingKey,doors.GetDoorPosition(index));
            Vector3 home;
            if(game.IsPlayerInside && positions.TryGetValue(game.PlayerEnterExit.BuildingDiscoveryData.buildingKey,out home))origin=home;
            return result.GroupBy(t => t.buildingKey).Select(g => g.First()).OrderBy(t => {
                Vector3 point;if(!positions.TryGetValue(t.buildingKey,out point))return float.PositiveInfinity;
                var gap=point-origin;gap.y=0;return gap.sqrMagnitude;
            }).ThenBy(t => t.buildingKey).ToList();
        }

        private static string BrowsePlaceFact(TalkManager.ListItem topic)
        { return topic.caption + " (" + GameManager.Instance.TalkManager.GetBuildingCompassDirection(topic.buildingKey) + ")"; }
    }
}
