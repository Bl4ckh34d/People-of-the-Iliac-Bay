using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in check against the active quest/person/place made by QuestLedgerProbe.
    public static class QuestPersonLocationProbe
    {
        private const BindingFlags Private=BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags PrivateStatic=BindingFlags.NonPublic | BindingFlags.Static;
        private static readonly Symbol Recipient=new Symbol("_recipient_");

        public static IEnumerator Run(Quest quest)
        {
            var game=GameManager.Instance;
            Require(game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal),
                "Disposable character required.");
            Require(quest!=null && QuestMachine.Instance.GetQuest(quest.UID)==quest,
                "Current native ledger quest required.");
            var person=quest.GetPerson(Recipient);
            var home=person==null?null:person.GetHomePlace();
            Require(home!=null && person.GetAssignedPlaceSymbol()!=null &&
                person.GetAssignedPlaceSymbol().Name==home.Symbol.Name,
                "Recipient must currently be assigned to its real quest residence.");
            var site=home.SiteDetails;
            Require(site.mapId==game.PlayerGPS.CurrentMapID && site.buildingKey>=0,
                "Quest residence is not in the current settlement.");
            var adapter=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets=adapter?adapter.GetComponent<StreetPopulation>():null;
            Require(adapter && streets && streets.Ready,"Resident conversation host is unavailable.");
            var manager=game.TalkManager;
            var helper=typeof(PeopleOfTheIliacBayMod).GetMethod("TryNativeQuestPersonLocation",PrivateStatic);
            var answer=typeof(PeopleOfTheIliacBayMod).GetMethod("AnswerNativeConversationTopic",Private);
            var lastTopic=typeof(PeopleOfTheIliacBayMod).GetField("conversationLastTopic",Private);
            var nativeKnowledge=typeof(TalkManager).GetMethod("GetNPCKnowledgeAboutItem",Private);
            var nativeBuilding=typeof(TalkManager).GetField("currentKeySubjectBuildingKey",Private);
            var resolveBuilding=typeof(PeopleOfTheIliacBayMod).GetMethod("TryResolveGuideBuilding",PrivateStatic);
            Require(helper!=null && answer!=null && lastTopic!=null && nativeKnowledge!=null &&
                nativeBuilding!=null && resolveBuilding!=null,"Native location adapter boundary missing.");
            // This ledger fixture has no anyInfo line. Add information for the
            // same active native Person resource, using its existing message.
            var originalInfo=quest.GetMessage(1000);
            Require(originalInfo!=null,"Ledger quest information message missing.");
            manager.AddQuestTopicWithInfoAndRumors(quest.UID,person,person.Symbol.Name,
                TalkManager.QuestInfoResourceType.Person,
                new List<TextFile.Token[]> {originalInfo.GetTextTokens()},null);
            try
            {
                if(game.PlayerEnterExit.IsPlayerInside)game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(1);
                ResidentActor chosen=null;
                DaggerfallTalkWindow window=null;
                TalkManager.ListItem biography=null,where=null;
                float speakerDeadline=Time.realtimeSinceStartup+90;
                while(chosen==null && Time.realtimeSinceStartup<speakerDeadline)
                {
                    foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    {
                        if(!actor || !actor.gameObject.activeInHierarchy || !actor.Person || !actor.Resident.IsAlive ||
                            actor.Resident.IsQuestProtected || actor.IsConversing ||
                            Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>8)continue;
                        DaggerfallUI.Instance.PopToHUD();
                        manager.TalkToMobileNPC(actor.Person);
                        window=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                        if(window==null)continue;
                        biography=Find(manager.ListTopicTellMeAbout,quest.UID,person.Symbol.Name,
                            TalkManager.QuestionType.QuestPerson);
                        where=Find(manager.ListTopicPerson,quest.UID,person.Symbol.Name,
                            TalkManager.QuestionType.Person);
                        if(biography==null || where==null)continue;
                        DFRandom.SaveSeed();
                        TalkManager.NPCKnowledgeAboutItem knowledge;
                        try{knowledge=(TalkManager.NPCKnowledgeAboutItem)nativeKnowledge.Invoke(manager,new object[]{where});}
                        finally{DFRandom.RestoreSeed();}
                        if(knowledge!=TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem &&
                            !manager.NPCsKnowEverything())continue;
                        chosen=actor;break;
                    }
                    if(chosen==null)yield return null;
                }
                Require(chosen && window!=null && biography!=null && where!=null,
                    "No loaded nearby ordinary native speaker both offers and knows this quest person.");
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                BuildingSummary summary;
                Require(directory && directory.MapID==site.mapId &&
                    directory.GetBuildingSummary(site.buildingKey,out summary),
                    "Current quest residence has no real building summary.");
                var bound=Pair(helper,manager,biography);
                Require(object.ReferenceEquals(bound,where),"Native person leaf was not bound by quest UID and symbol.");

                string biographyReply=(string)answer.Invoke(adapter,new object[]{window,biography,false});
                Require(!string.IsNullOrWhiteSpace(biographyReply) &&
                    object.ReferenceEquals(lastTopic.GetValue(adapter),biography) &&
                    object.ReferenceEquals(manager.CurrentQuestionListItem,biography),
                    "Ordinary biography was replaced with a location question.");

                string mapReply=(string)answer.Invoke(adapter,new object[]{window,biography,true});
                Require(!string.IsNullOrWhiteSpace(mapReply) &&
                    object.ReferenceEquals(lastTopic.GetValue(adapter),where) &&
                    object.ReferenceEquals(manager.CurrentQuestionListItem,where),
                    "Explicit map request did not use the permitted native Where Is person.");
                manager.GetKeySubjectPersonHint();
                Require((int)nativeBuilding.GetValue(manager)==site.buildingKey,
                    "Native person hint lost the actual quest residence building key.");
                object[] guideArgs={manager,where,-1};
                Require((bool)resolveBuilding.Invoke(null,guideArgs) && (int)guideArgs[2]==site.buildingKey,
                    "Lead building resolver lost the actual quest residence key.");

                var previousKnowledge=where.npcKnowledgeAboutItem;
                try
                {
                    Require(!manager.NPCsKnowEverything(),"Knowledge-negative control requires ordinary native rules.");
                    where.npcKnowledgeAboutItem=TalkManager.NPCKnowledgeAboutItem.DoesNotKnowAboutItem;
                    Require(Pair(helper,manager,biography)==null,
                        "Native ignorance was bypassed for a quest person.");
                    answer.Invoke(adapter,new object[]{window,biography,true});
                    Require(object.ReferenceEquals(lastTopic.GetValue(adapter),biography),
                        "Unknown person was rebound for a map request.");
                }
                finally{where.npcKnowledgeAboutItem=previousKnowledge;}

                var unoffered=new TalkManager.ListItem {type=TalkManager.ListItemType.Item,
                    questionType=TalkManager.QuestionType.QuestPerson,questID=quest.UID,key=person.Symbol.Name,
                    caption=biography.caption};
                Require(Pair(helper,manager,unoffered)==null,
                    "Unlisted quest person was accepted as a current native topic.");
                var originalSite=home.SiteDetails;
                try
                {
                    var foreign=originalSite;
                    foreign.mapId=originalSite.mapId+1;
                    home.SiteDetails=foreign;
                    Require(Pair(helper,manager,biography)==null,
                        "Another settlement's quest person was accepted as local.");
                }
                finally{home.SiteDetails=originalSite;}
                yield return VerifyKnownQuestLead(streets,chosen,site,manager);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_QUEST_PERSON_LOCATION_PASS: native active person="+
                    person.Symbol.Name+"; uid="+quest.UID+"; residence="+site.mapId+":"+site.buildingKey+
                    "; biography=true; map-native-person=true; marker/lead-building=true; physical-lead=true; unknown/unoffered/other-map=rejected");
            }
            finally
            {
                DaggerfallUI.Instance.PopToHUD();
                manager.RemoveQuestInfoTopicsForSpecificQuest(quest.UID);
                manager.AddQuestTopicWithInfoAndRumors(quest);
            }
            yield return QuestOfferProbe.Enter(site.buildingKey);
        }

        private static IEnumerator VerifyKnownQuestLead(StreetPopulation streets,ResidentActor actor,SiteDetails site,
            TalkManager manager)
        {
            var game=GameManager.Instance;
            Require(actor && actor.gameObject.activeInHierarchy && actor.Resident!=null &&
                Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)<=8,
                "Quest Lead needs the same loaded ordinary speaker near the player.");
            string destination=manager.GetBuildingNameForBuildingKey(site.buildingKey);
            NpcActionOption option;
            Require(streets.TryCreateConversationBuildingMoveOption(actor.Resident.Id,site.mapId,site.buildingKey,
                destination,out option) && option!=null && option.Kind==NpcActionKind.Lead &&
                option.Target!=null && option.Target.SettlementId==site.mapId && option.Target.BuildingKey==site.buildingKey,
                "Native-known quest residence did not yield an exact Lead option.");
            string reason;
            Require(NpcProbeCommandSource.TryAcceptAction(streets,actor.Resident.Id,"quest-person-lead:"+
                Guid.NewGuid().ToString("N"),new NpcActionDecision {Option=Copy(option),DurationSeconds=900},out reason),
                "Known quest Lead was not accepted: "+reason);
            var store=Store(streets);
            var accepted=store==null?null:store.ActiveNpcAction(actor.Resident.Id);
            Require(accepted!=null && accepted.Kind==NpcActionKind.Lead && accepted.Target!=null &&
                accepted.Target.SettlementId==site.mapId && accepted.Target.BuildingKey==site.buildingKey,
                "Accepted Lead lost the known quest residence.");
            var first=streets.CaptureLeadRuntimeSnapshot(actor.Resident.Id);
            Require(first.ActionId==accepted.Id && first.GoalDistance>=12,"Quest Lead starts without a witnessed route to its exact native door.");
            DaggerfallUI.Instance.PopToHUD();
            float until=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<until)
            {
                var current=streets.CaptureLeadRuntimeSnapshot(actor.Resident.Id);
                if(current.PlayerDistance<=8 && Vector3.Distance(first.ActorGround,current.ActorGround)>0.5f)break;
                yield return null;
            }
            Require(Time.realtimeSinceStartup<until,"Known quest Lead did not begin physical movement while player remained nearby.");
            until=Time.realtimeSinceStartup+90;NpcLeadRuntimeSnapshot paused=null;
            while(Time.realtimeSinceStartup<until)
            {
                var current=streets.CaptureLeadRuntimeSnapshot(actor.Resident.Id);
                if(current.PlayerDistance>9 && current.Progress==NpcLeadProgress.WaitingForPlayer){paused=current;break;}
                yield return null;
            }
            Require(paused!=null,"Quest Lead needs a real player-distance pause; no player or NPC teleport is used by this probe.");
            float holdUntil=Time.realtimeSinceStartup+2;
            while(Time.realtimeSinceStartup<holdUntil)
            {
                var current=streets.CaptureLeadRuntimeSnapshot(actor.Resident.Id);
                Require(current.PlayerDistance>8 && current.Progress==NpcLeadProgress.WaitingForPlayer &&
                    Vector3.Distance(paused.ActorGround,current.ActorGround)<=.2f,"Quest Lead advanced during its distant-player wait.");
                yield return null;
            }
            until=Time.realtimeSinceStartup+90;bool resumed=false;
            while(Time.realtimeSinceStartup<until)
            {
                var current=streets.CaptureLeadRuntimeSnapshot(actor.Resident.Id);
                if(current.PlayerDistance<=8 && Vector3.Distance(paused.ActorGround,current.ActorGround)>.5f){resumed=true;break;}
                yield return null;
            }
            Require(resumed,"Quest Lead did not resume after player returned within native distance.");
            until=Time.realtimeSinceStartup+180;
            while(Time.realtimeSinceStartup<until)
            {
                var completed=store.ReadNpcAction(accepted.Id);
                if(completed==null)break;
                if(completed.Phase==NpcActionPhase.Failed || completed.Phase==NpcActionPhase.Cancelled)
                    throw new InvalidOperationException("Quest person location probe: Quest Lead ended without arrival: "+completed.FailureReason);
                if(completed.Phase==NpcActionPhase.Completed)
                {
                    Require(completed.Target!=null && completed.Target.SettlementId==site.mapId &&
                        completed.Target.BuildingKey==site.buildingKey && completed.LastPosition!=null &&
                        completed.LastPosition.Kind==NpcActionTargetKind.WorldPoint &&
                        Vector3.Distance(QuestLeadPosition(game,completed.LastPosition),first.GoalGround)<=.5f &&
                        Vector3.Distance(game.PlayerMotor.transform.position-Vector3.up,first.GoalGround)<=8.5f,
                        "Quest Lead completion did not retain the exact known door arrival.");
                    yield break;
                }
                yield return null;
            }
            throw new InvalidOperationException("Quest person location probe: Quest Lead did not complete at the known door.");
        }

        private static Vector3 QuestLeadPosition(GameManager game,NpcActionTarget position)
        {return game.StreamingWorld.GetCurrentBuildingDirectory().transform.TransformPoint(new Vector3((float)position.X,(float)position.Y,(float)position.Z));}
        private static NpcActionOption Copy(NpcActionOption source)
        {return new NpcActionOption {Id=source.Id,Kind=source.Kind,Label=source.Label,ItemSourceStep=source.ItemSourceStep,
            MaximumQuantity=source.MaximumQuantity,Target=NpcActionExecutor.CopyTarget(source.Target)};}
        private static WorldStateStore Store(StreetPopulation streets)
        {var field=typeof(StreetPopulation).GetField("worldStore",Private);return field==null?null:field.GetValue(streets) as WorldStateStore;}

        private static TalkManager.ListItem Pair(MethodInfo helper,TalkManager manager,TalkManager.ListItem topic)
        {
            object[] args={manager,topic,null};
            return (bool)helper.Invoke(null,args)?args[2] as TalkManager.ListItem:null;
        }

        private static TalkManager.ListItem Find(IEnumerable<TalkManager.ListItem> items,ulong uid,string key,
            TalkManager.QuestionType type)
        {
            if(items==null)return null;
            foreach(var item in items)
                if(item!=null && item.type==TalkManager.ListItemType.Item && item.questionType==type &&
                    item.questID==uid && string.Equals(item.key,key,StringComparison.Ordinal))return item;
            return null;
        }

        private static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException("Quest person location probe: "+message);}
    }
}
