using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // A Tell Me About person is a location target only when DFU also offers
        // that exact quest resource in the current Where Is / People list.
        private static bool TryNativeQuestPersonLocation(TalkManager manager,TalkManager.ListItem topic,
            out TalkManager.ListItem wherePerson)
        {
            wherePerson=null;
            var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(manager==null || topic==null || topic.questionType!=TalkManager.QuestionType.QuestPerson ||
                topic.type!=TalkManager.ListItemType.Item || !CurrentNativeItem(manager,topic) ||
                string.IsNullOrEmpty(topic.key) || directory==null || nativeKnowledge==null)
                return false;
            var people=manager.ListTopicPerson;
            if(people==null)return false;
            foreach(var candidate in people)
            {
                if(candidate==null || candidate.type!=TalkManager.ListItemType.Item ||
                    candidate.questionType!=TalkManager.QuestionType.Person || candidate.questID!=topic.questID ||
                    !string.Equals(candidate.key,topic.key,StringComparison.Ordinal))continue;
                try
                {
                    Person person;
                    manager.GetPersonResource(candidate.questID,candidate.key,out person);
                    if(person==null)return false;
                    int mapId=person.IsQuestor?person.QuestorData.mapID:manager.GetPersonSiteDetails(ref person).mapId;
                    if(mapId!=directory.MapID || mapId!=game.PlayerGPS.CurrentMapID)return false;
                    int buildingKey=manager.GetPersonBuildingKey(ref person);
                    BuildingSummary summary;
                    if(buildingKey<0 || !directory.GetBuildingSummary(buildingKey,out summary))return false;
                    if(candidate.npcKnowledgeAboutItem==TalkManager.NPCKnowledgeAboutItem.NotSet)
                    {
                        DFRandom.SaveSeed();
                        try{candidate.npcKnowledgeAboutItem=(TalkManager.NPCKnowledgeAboutItem)nativeKnowledge.Invoke(manager,new object[]{candidate});}
                        finally{DFRandom.RestoreSeed();}
                    }
                    if(candidate.npcKnowledgeAboutItem!=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem &&
                        !manager.NPCsKnowEverything())return false;
                    wherePerson=candidate;
                    return true;
                }
                catch{return false;}
            }
            return false;
        }
    }
}
