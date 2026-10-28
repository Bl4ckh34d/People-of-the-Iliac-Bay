using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        private static IEnumerator VerifyEnterpriseDialogue(PeopleOfTheIliacBayMod host,Evidence evidence)
        {
            var enabled=typeof(PeopleOfTheIliacBayMod).GetField("localDialogueEnabled",Private);
            bool previousEnabled=(bool)enabled.GetValue(host);
            var client=(LocalDialogueClient)typeof(PeopleOfTheIliacBayMod).GetField("localDialogue",Private).GetValue(host);
            Need(client,"Native enterprise dialogue needs its existing local dialogue client.");
            var speech=host.GetComponent<BreezeTtsTrialBridge>();
            bool previousVoice=speech && speech.VoiceEnabled,previousStreaming=speech && speech.SocialStreamingEnabled;
            // This probe verifies semantic consent and visible text, without an audio service.
            // Use the existing live setting boundary and restore it on every exit.
            if(speech)speech.SetFeatureOptions(false,false);
            var previousTrace=client.ForegroundClassificationTrace;var choices=new List<string>();
            client.ForegroundClassificationTrace=(request,response,choice)=>
            {
                choices.Add(choice);Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTERPRISE_CLASSIFICATION: "+choice);
                if(previousTrace!=null)previousTrace(request,response,choice);
            };
            enabled.SetValue(host,true);
            try
            {
                DaggerfallTalkWindow talk=null;
                yield return ConversationMoodProbe.OpenTalk(host,GameManager.Instance,evidence.Actor,value=>talk=value);
                Need(talk!=null,"Native enterprise Talk did not open.");
                yield return EnterpriseDialogueTurn(host,client,talk,evidence,choices,
                    "Let's plan our dungeon crawl together.","plan-decline",false,"PLAN|DungeonCrawl");
                var first=evidence.Store.ReadCompanionEnterprise(evidence.Id);
                Need(first!=null && first.Active && !first.OwnDefeatedEnemies && !first.Containers,
                    "Declining the native enterprise question granted loot rights.");
                yield return EnterpriseDialogueTurn(host,client,talk,evidence,choices,
                    "Let's start a new dungeon crawl together.","plan-accept",true,"PLAN|DungeonCrawl");
                var current=evidence.Store.ReadCompanionEnterprise(evidence.Id);
                Need(current!=null && current.Active && current.Id!=first.Id && current.OwnDefeatedEnemies && !current.Containers,
                    "Accepting the native question failed to grant only own-kill rights for the new enterprise.");
                string id=current.Id;
                yield return EnterpriseDialogueTurn(host,client,talk,evidence,choices,
                    "During this dungeon crawl, you also have my express permission to loot containers.","container-grant",null,"CONTAINERS|"+id);
                current=evidence.Store.ReadCompanionEnterprise(evidence.Id);
                Need(current!=null && current.Id==id && current.OwnDefeatedEnemies && current.Containers,
                    "Express free-text container permission did not bind to the current actual enterprise.");
                yield return EnterpriseDialogueTurn(host,client,talk,evidence,choices,
                    "I withdraw all permission to take loot on this expedition. Do not take any more loot.","revoke",null,"REFUSE|"+id);
                current=evidence.Store.ReadCompanionEnterprise(evidence.Id);
                Need(current!=null && current.Id==id && !current.OwnDefeatedEnemies && !current.Containers,
                    "Free-text revocation retained native loot permission.");
                evidence.CheckIdentity();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTERPRISE_DIALOGUE_PASS: actualSubmitFreeText=true; actualModel=true; nativeQuestion=true; nativeNo=true; nativeYes=true; ownKillOnly=true; separateContainerPermission=true; explicitRevocation=true; sameBodyInventoryAndContract=true; consentInjected=false; speech=false");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_FOLLOW_PROBE_PASS: enterpriseDialogue=true; physicalLoot=false; semanticConsent=true");
            }
            finally
            {
                client.ForegroundClassificationTrace=previousTrace;client.Cancel();enabled.SetValue(host,previousEnabled);
                DaggerfallUI.Instance.PopToHUD();
                if(speech)speech.SetFeatureOptions(previousVoice,previousStreaming);
            }
        }

        private static IEnumerator EnterpriseDialogueTurn(PeopleOfTheIliacBayMod host,LocalDialogueClient client,
            DaggerfallTalkWindow talk,Evidence evidence,List<string> choices,string message,string label,bool? answer,string expected)
        {
            var busy=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private);
            Need(!client.IsBusy && !(bool)busy.GetValue(host),"Previous enterprise dialogue turn is still busy.");
            var rows=(ListBox)typeof(DaggerfallTalkWindow).GetField("listboxConversation",Private).GetValue(talk);
            int before=rows.Count,choiceStart=choices.Count;long completed=client.ReplyCompletionCount;
            typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText",Private).Invoke(host,new object[]{talk,message});
            float deadline=Time.realtimeSinceStartup+150,nextDiagnostic=Time.realtimeSinceStartup+10;bool answered=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                var box=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
                if(box!=null && box.IsSetup && box.GetType()==typeof(PeopleOfTheIliacBayMod).GetNestedType("PersonalCommerceConfirmBox",Private))
                {
                    Need(answer.HasValue && !answered,"Unexpected repeated enterprise confirmation.");
                    var current=evidence.Store.ReadCompanionEnterprise(evidence.Id);
                    Need(current!=null && current.Active && !current.OwnDefeatedEnemies && !current.Containers,
                        "Planning granted rights before the native confirmation answer.");
                    var text=(MultiFormatTextLabel)typeof(DaggerfallMessageBox).GetField("label",Private).GetValue(box);
                    string question=string.Join(" ",text.TextLabels.ConvertAll(line=>line.Text).ToArray());
                    Need(question.Contains("enemies I personally defeat") && question.Contains("Containers") &&
                        question.Contains("separate express permission"),"Native question omits the actual loot scope: "+question);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTERPRISE_NATIVE_QUESTION: case="+label+"; text="+question+"; answer="+answer.Value);
                    var buttons=(List<Button>)typeof(DaggerfallMessageBox).GetField("buttons",Private).GetValue(box);
                    Button selected=null;foreach(var button in buttons)
                        if((DaggerfallMessageBox.MessageBoxButtons)button.Tag==(answer.Value?DaggerfallMessageBox.MessageBoxButtons.Yes:DaggerfallMessageBox.MessageBoxButtons.No))selected=button;
                    Need(selected!=null,"Actual enterprise confirmation button is missing.");
                    typeof(DaggerfallMessageBox).GetMethod("ButtonClickHandler",Private).Invoke(box,new object[]{selected,Vector2.zero});
                    var after=evidence.Store.ReadCompanionEnterprise(evidence.Id);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTERPRISE_BUTTON_RETURN: top="+DaggerfallUI.UIManager.TopWindow.GetType().Name+
                        "; containsBox="+DaggerfallUI.UIManager.ContainsWindow(box)+
                        "; callbackPending="+(box.GetType().GetField("completed",Private).GetValue(box)!=null)+
                        "; enterpriseRevision="+current.Revision+"/"+after.Revision+
                        "; hostBusy="+busy.GetValue(host)+"; clientBusy="+client.IsBusy+
                        "; replies="+client.ReplyCompletionCount+"/"+completed);
                    answered=true;
                }
                if(!client.IsBusy && !(bool)busy.GetValue(host) && client.ReplyCompletionCount>completed)break;
                if(Time.realtimeSinceStartup>=nextDiagnostic)
                {
                    nextDiagnostic=Time.realtimeSinceStartup+30;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTERPRISE_WAIT: case="+label+"; answered="+answered+
                        "; hostBusy="+busy.GetValue(host)+"; clientBusy="+client.IsBusy+
                        "; clientActive="+client.isActiveAndEnabled+"; requests="+client.ReplyRequestCount+
                        "; replies="+client.ReplyCompletionCount+"; top="+DaggerfallUI.UIManager.TopWindow.GetType().Name);
                }
                yield return null;
            }
            Need(!client.IsBusy && !(bool)busy.GetValue(host) && client.ReplyCompletionCount>completed &&
                answered==answer.HasValue,"Native enterprise dialogue did not complete its expected question/reply; case="+label);
            bool interpreted=false;for(int index=choiceStart;index<choices.Count;index++)if(choices[index]==expected)interpreted=true;
            Need(interpreted,"Real classifier did not return the expected enterprise decision; case="+label+"; expected="+expected);
            deadline=Time.realtimeSinceStartup+60;
            while(Time.realtimeSinceStartup<deadline && !NativeBiographyDialogueProbe.AllNewAnswersRevealed(rows,before))yield return null;
            Need(NativeBiographyDialogueProbe.AllNewAnswersRevealed(rows,before),"Native enterprise answer did not finish revealing.");
            var visible=new StringBuilder();for(int index=before;index<rows.Count;index++)
            {
                var row=rows.GetItem(index);if(row!=null && row.textLabel!=null && row.textColor!=DaggerfallUI.DaggerfallQuestionTextColor)
                    visible.Append(row.textLabel.Text).Append(' ');
            }
            Need(visible.Length>0,"Native enterprise answer is empty.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTERPRISE_VISIBLE_REPLY: case="+label+"; player="+message+"; answer="+visible);
            evidence.CheckIdentity();
        }
    }
}
