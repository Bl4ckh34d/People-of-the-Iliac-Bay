using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private string ConversationEnterpriseContext(string person,double now)
        {
            if(store==null || saveFault || string.IsNullOrEmpty(person))return string.Empty;
            var current=store.ReadCompanionEnterprise(person);var action=store.ActiveNpcAction(person);
            if(current==null || action==null || !current.Active || now>=action.DeadlineAt || action.FinishAt>0 && now>=action.FinishAt)
                return string.Empty;
            return "Current verified companion enterprise: "+current.Kind+"; own-defeated-enemy loot right="+
                current.OwnDefeatedEnemies+"; separately granted container/other loot right="+current.Containers+
                ". These rights apply only to this enterprise. They do not establish criminal willingness, combat agreement, kills, ownership transfers or completed looting.";
        }
        private void SubmitCompanionEnterpriseConversation(DaggerfallTalkWindow talk,string text,string history,
            ConversationContextSnapshot snapshot)
        {
            if(store==null || saveFault || string.IsNullOrEmpty(conversationPersonId))
            {SubmitOrdinarySocialText(talk,text,history,snapshot);return;}
            string person=conversationPersonId;int session=memorySession;
            var current=store.ReadCompanionEnterprise(person);
            var action=store.ActiveNpcAction(person);
            long stage=BeginConversationContextStage(snapshot,talk);
            if(stage<=0){HandleRejectedConversationStage(snapshot,talk,stage,text);return;}
            bool started=localDialogue.Classify(NewDialogueConnection(),
                ConversationContextFormatter.ForSemanticClassifier(snapshot,2048)+"\n"+
                NpcCompanionEnterpriseChat.Prompt(text,history,current),history,output=>
            {
                if(!CurrentMemorySession(talk,session))return;
                if(conversationPersonId!=person || !TryConsumeConversationContextStage(snapshot,talk,stage))
                {HandleRejectedConversationStage(snapshot,talk,stage,text);return;}
                var decision=NpcCompanionEnterpriseChat.Parse(output,current);
                bool saved=false;
                string request="enterprise-turn:"+snapshot.Binding.RequestRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+Guid.NewGuid().ToString("N");
                double now=ResidentActor.CompanionNow();
                if(decision!=null && decision.Operation=="PLAN")
                {
                    NpcCompanionEnterprise created;
                    if(action!=null && store.TryBeginCompanionEnterprise(person,action.Revision,request,decision.PlanKind,now,out created))
                    {
                        var plannedSnapshot=CaptureActionResultContext(talk,text,snapshot,true);
                        if(plannedSnapshot==null)
                        {ReportActionDialogueUnavailable(talk,text,"Enterprise planning was recorded without loot rights, but its current context is unavailable.");return;}
                        // This question is only loot consent. It cannot grant criminal willingness.
                        string enterpriseName=decision.PlanKind==NpcCompanionEnterpriseKind.DungeonCrawl?"dungeon crawl":
                            decision.PlanKind==NpcCompanionEnterpriseKind.Raid?"raid":
                            decision.PlanKind==NpcCompanionEnterpriseKind.Burglary?"burglary":"murder plan";
                        var confirm=new PersonalCommerceConfirmBox("For this "+enterpriseName+
                            ", may I keep loot from enemies I personally defeat? This applies only to this enterprise. " +
                            "Containers and other people's loot require separate express permission. " +
                            "This grants no agreement to commit a crime.",accepted=>
                        {
                            if(!CurrentMemorySession(talk,session) || conversationPersonId!=person ||
                                !IsCurrentConversationContext(plannedSnapshot,talk))return;
                            bool granted=store.TryGrantCompanionLoot(person,created.Id,created.Revision,request+":answer",
                                accepted,false,ResidentActor.CompanionNow());
                            conversationResponseCue+=" Enterprise planning recorded; no crime or physical action was accepted. "+
                                (granted?(accepted?"Player grants only this companion's own-defeated-enemy loot for this enterprise. Containers remain forbidden.":
                                "Player declined loot rights. No automatic looting is permitted."):"Loot consent could not be saved. No new loot right was granted.");
                            SubmitActionSocialResult(talk,text,history,conversationResponseCue,plannedSnapshot,nativeStateChanged:granted);
                        });
                        DaggerfallUI.UIManager.PushWindow(confirm);return;
                    }
                }
                else if(decision!=null && current!=null)
                {
                    if(decision.Operation=="END")saved=store.TryEndCompanionEnterprise(person,current.Id,now);
                    else saved=store.TryGrantCompanionLoot(person,current.Id,current.Revision,request,
                        decision.Operation=="REFUSE"?false:decision.Operation=="OWN" || current.OwnDefeatedEnemies,
                        decision.Operation=="REFUSE"?false:decision.Operation=="CONTAINERS" || current.Containers,now);
                }
                var resulting=store.ReadCompanionEnterprise(person);
                conversationResponseCue+=" Enterprise loot result: "+(saved?
                    "Only the exact current-enterprise permission decision was saved. No crime or physical action was accepted. Current active="+
                    (resulting!=null && resulting.Active)+"; own-defeated-enemy right="+(resulting!=null && resulting.OwnDefeatedEnemies)+
                    "; separate container right="+(resulting!=null && resulting.Containers)+".":
                    "No new enterprise loot permission was saved. An existing active Follow companion is required; do not claim agreement or a crime was accepted.");
                SubmitActionSocialResult(talk,text,history,conversationResponseCue,snapshot,nativeStateChanged:saved);
            },NpcCompanionEnterpriseChat.Grammar(current),responseTokens:128);
            if(!started && TryConsumeConversationContextStage(snapshot,talk,stage))
            {
                conversationResponseCue+=" No new enterprise or loot permission was saved.";
                SubmitSocialText(talk,text,history,snapshot:snapshot);
            }
        }
    }
}
