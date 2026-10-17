using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class LivingCitiesMod
    {
        private readonly List<TalkManager.ListItem> conversationSuggestions = new List<TalkManager.ListItem>();
        private static readonly MethodInfo nativeKnowledge = typeof(TalkManager).GetMethod("GetNPCKnowledgeAboutItem", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo nativeBrowseReaction = typeof(TalkManager).GetMethod("GetReactionToPlayer_0_1_2", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo nativeNpcData = typeof(TalkManager).GetField("npcData", BindingFlags.Instance | BindingFlags.NonPublic);

        private bool TryBrowseConversation(DaggerfallTalkWindow window, string text, string history,
            ConversationSelection choice, List<ConversationTopic> topics, Dictionary<int, TalkManager.ListItem> native)
        {
            // An explicitly named leaf keeps the existing exact-topic path.
            if (topics.Any(t => !t.Group && ConversationKnowledge.NamesTopic(text, t))) return false;
            var groups = ConversationBrowsing.RequestedGroups(text, topics);
            bool remembered = groups.Count == 0 && ConversationBrowsing.IsMapChoice(text) && conversationSuggestions.Count > 0;
            TalkManager.ListItem selectedGroup;
            if (groups.Count == 0 && !remembered && choice.Intent == "topic" &&
                native.TryGetValue(choice.TopicId, out selectedGroup) && selectedGroup.type == TalkManager.ListItemType.ItemGroup)
                groups.Add(choice.TopicId);
            if (groups.Count == 0 && !remembered) return false;
            if (groups.Count > 0) conversationSuggestions.Clear();

            var current = native.Values.Where(t => t.questionType == TalkManager.QuestionType.LocalBuilding &&
                t.type != TalkManager.ListItemType.ItemGroup && t.questID == 0).Distinct().ToList();
            var requested = remembered ? current.Where(conversationSuggestions.Contains).ToList() :
                topics.Where(t => !t.Group && groups.Any(id => {
                    var group = topics.Find(g => g.Id == id);
                    string path = group.Path + " / " + group.Caption;
                    return t.Path == path || t.Path.StartsWith(path + " / ", StringComparison.Ordinal);
                })).Select(t => native[t.Id]).Where(current.Contains).Distinct().ToList();
            conversationLastTopic = null;
            var manager = GameManager.Instance.TalkManager;
            var tone = selectedToneField == null ? DaggerfallTalkWindow.TalkTone.Normal :
                (DaggerfallTalkWindow.TalkTone)selectedToneField.GetValue(window);
            if (requested.Count == 0 || !BrowseWilling(manager, requested[0], tone))
            {
                conversationSuggestions.Clear();
                conversationResponseCue = requested.Count == 0 ?
                    " You have no verified local destination for this request. Say naturally that you cannot suggest one; do not invent a name or marker." :
                    " You are unwilling to share directions with this player. Refuse in your own voice; do not name destinations or claim a map marker.";
                SubmitBrowseText(window, text, history, requested.Count==0 ? "I don't know a place to suggest." :
                    "I would rather not give you directions.",new TalkManager.ListItem[0],null); return true;
            }
            var known = KnownBrowsePlaces(requested);
            bool mark = ConversationKnowledge.WantsMap(text);
            if (mark)
            {
                int key = ConversationBrowsing.Pick(known.Select(t => t.buildingKey), known.Select(t => t.buildingKey),
                    count => UnityEngine.Random.Range(0, count));
                var chosen = known.Find(t => t.buildingKey == key);
                if (chosen != null)
                {
                    conversationLastTopic = chosen;
                    manager.GetQuestionText(chosen, tone);
                    bool permitted = conversationMapRequestsEnabled && !GameManager.Instance.IsPlayerInside;
                    if (permitted) manager.GetKeySubjectBuildingOnMap();
                    bool marked = permitted && GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(chosen.buildingKey);
                    conversationResponseCue = " Verified chosen destination: " + BrowsePlaceFact(chosen) +
                        (marked ? ". The game has successfully marked ONLY this destination on the player's map. Confirm it in character, naming it." :
                        ". No map marker was placed. Explain the direction naturally; " +
                        (GameManager.Instance.IsPlayerInside ? "you cannot mark the map indoors." : "map requests are disabled."));
                    Debug.Log("LIVING_CITIES_KNOWLEDGE_MAP: building=" + key + "; name=" + chosen.caption + "; marked=" + marked);
                    string factual=marked ? "I've marked " + chosen.caption + " on your map." :
                        "I cannot mark " + chosen.caption + " on your map " +
                        (GameManager.Instance.IsPlayerInside ? "from inside here." : "at the moment.");
                    SubmitBrowseText(window, text, history, factual,new[]{chosen},marked, answer => {
                        if (!remembered && ConversationKnowledge.NamesTopic(answer, new ConversationTopic(0, chosen.caption, "", false)))
                            conversationSuggestions.Add(chosen);
                    }); return true;
                }
            }

            conversationSuggestions.Clear();
            var offered = known.Take(3).ToList();
            conversationResponseCue = offered.Count == 0 ?
                " You do not know any local destination matching this request. Admit that naturally; do not invent a place or marker." :
                " KNOWLEDGE SEARCH RESULT: You personally know these real local places: " +
                string.Join("; ", offered.Select(BrowsePlaceFact).ToArray()) +
                ". Suggest these places naturally, using their exact names. Give up to three choices rather than asking the player to name one. " +
                "These are the only verified destinations for this reply. Do not invent another place, distance, service or marker. " +
                "No map markers were placed. You may offer to mark one if asked.";
            Debug.Log("LIVING_CITIES_KNOWLEDGE_BROWSE: requested=" + requested.Count + "; known=" + known.Count + "; offered=" + offered.Count);
            string recommendation=offered.Count==0 ? "I don't know a place around here well enough to recommend one." :
                "You could try " + string.Join(" or ",offered.Select(t=>t.caption+" ("+manager.GetBuildingCompassDirection(t.buildingKey)+")").ToArray()) + ".";
            SubmitBrowseText(window, text, history,recommendation,offered,null, answer => {
                // Remember what the NPC actually said, not an undisclosed candidate.
                foreach (var item in offered)
                    if (ConversationKnowledge.NamesTopic(answer, new ConversationTopic(0, item.caption, "", false)))
                        conversationSuggestions.Add(item);
            });
            return true;
        }

        private void SubmitBrowseText(DaggerfallTalkWindow window, string text, string history, string factual,
            IEnumerable<TalkManager.ListItem> named,bool? marked,Action<string> complete = null)
        {
            var names=named.Select(t=>t.caption).Distinct().ToArray();
            var excluded=new List<string>();
            CollectBrowseCaptions(GameManager.Instance.TalkManager.ListTopicLocation,names,excluded);
            SubmitSocialText(window,text,history,complete,conversationResponseCue,
                prose=>ConversationBrowsing.SelectReply(prose,factual,names,excluded,marked));
        }
        private static void CollectBrowseCaptions(IEnumerable<TalkManager.ListItem> items,string[] allowed,List<string> excluded)
        {
            foreach(var item in items)
                if(item.type==TalkManager.ListItemType.ItemGroup && item.listChildItems!=null) CollectBrowseCaptions(item.listChildItems,allowed,excluded);
                else if(item.questionType==TalkManager.QuestionType.LocalBuilding && !allowed.Contains(item.caption)) excluded.Add(item.caption);
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
            var origin = directory.transform.InverseTransformPoint(game.PlayerMotor.transform.position);
            BuildingSummary indoors;
            if (game.IsPlayerInside && directory.GetBuildingSummary(game.PlayerEnterExit.BuildingDiscoveryData.buildingKey, out indoors)) origin = indoors.Position;
            return result.GroupBy(t => t.buildingKey).Select(g => g.First()).OrderBy(t => {
                BuildingSummary building; directory.GetBuildingSummary(t.buildingKey, out building);
                var gap = building.Position - origin; gap.y = 0; return gap.sqrMagnitude;
            }).ThenBy(t => t.buildingKey).ToList();
        }

        private static string BrowsePlaceFact(TalkManager.ListItem topic)
        { return topic.caption + " (" + GameManager.Instance.TalkManager.GetBuildingCompassDirection(topic.buildingKey) + ")"; }
    }
}
