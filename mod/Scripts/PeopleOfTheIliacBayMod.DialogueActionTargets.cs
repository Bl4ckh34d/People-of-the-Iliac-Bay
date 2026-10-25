using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private void SelectSemanticActionCandidates(DaggerfallTalkWindow window,string text,string history,
            IList<NpcActionOption> candidates,Action<IList<NpcActionOption>> selected,ConversationContextSnapshot snapshot,string originalText=null,
            NpcActionDecision previousStep=null,int stepIndex=0)
        {
            if(snapshot==null || selected==null)return;
            if(!IsCurrentConversationContext(snapshot,window))
            {HandleRejectedConversationStage(snapshot,window,0,originalText??text,"No new action was accepted.");return;}
            IList<NpcActionOption> compact;int renderedCharacters;
            if(NpcActionCandidateCatalog.TryCreateCompactForStep(candidates,stepIndex,previousStep,out compact,out renderedCharacters))
            {
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-chat-probe")>=0)
                    UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_CATEGORY: step="+
                        (stepIndex>0?stepIndex:1).ToString(System.Globalization.CultureInfo.InvariantCulture)+"; result=compact; candidates="+
                        compact.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; chars="+
                        renderedCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; category-model=skipped.");
                selected(compact);
                return;
            }
            var catalog=new NpcActionCandidateCatalog(candidates);
            int session=memorySession;string person=conversationPersonId;
            string prompt=catalog.Prompt(text,previousStep,originalText);
            if(stepIndex>0)
                UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_CATEGORY: step="+
                    stepIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; result=classifying; groups="+
                    catalog.GroupCount.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; candidates="+
                    (candidates==null?0:candidates.Count).ToString(System.Globalization.CultureInfo.InvariantCulture)+".");
            long stageToken=BeginConversationContextStage(snapshot,window);
            if(stageToken<=0){HandleRejectedConversationStage(snapshot,window,stageToken,originalText??text,"No new action was accepted.");return;}
            if(!localDialogue.Classify(NewDialogueConnection(),prompt,history,output=>{
                if(!CurrentMemorySession(window,session))return;
                if(conversationPersonId!=person)
                {HandleRejectedConversationStage(snapshot,window,stageToken,originalText??text,null);return;}
                if(!TryConsumeConversationContextStage(snapshot,window,stageToken))
                {HandleRejectedConversationStage(snapshot,window,stageToken,originalText??text,"No new action was accepted.");return;}
                if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                {conversationBusy=false;AppendRememberedDialoguePair(window,originalText??text,DialogueReplyFallback.UnavailableReply,"unavailable");DaggerfallUI.AddHUDText("Local dialogue unavailable. No action was accepted.");return;}
                var options=catalog.Select(output==null?null:output.Trim());
                if(stepIndex>0)
                {
                    string result="invalid";
                    string trimmed=output==null?string.Empty:output.Trim();
                    int groupId;
                    if(options!=null && trimmed.StartsWith("GROUP|",StringComparison.Ordinal) &&
                        int.TryParse(trimmed.Substring(6),System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,out groupId))result="group-"+
                                groupId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    else if(options==null && trimmed=="NONE")result="none";
                    UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_CATEGORY: step="+
                        stepIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; result="+result+
                        "; targets="+(options==null?0:options.Count).ToString(System.Globalization.CultureInfo.InvariantCulture)+".");
                }
                if(options==null)
                {
                    SubmitUnresolvedSemanticAction(window,originalText??text,history,"No unambiguous available action category was selected. No action was accepted. Ask naturally which source, recipient or kind of action the player intends; do not invent a target.",snapshot);
                    return;
                }
                selected(options);
            },catalog.Grammar(),classificationTrace:SemanticActionModelCaptureProbe.ForStage(
                "category-step-"+(stepIndex>0?stepIndex:1).ToString(System.Globalization.CultureInfo.InvariantCulture))))
            {if(!TryConsumeConversationContextStage(snapshot,window,stageToken))
                {HandleRejectedConversationStage(snapshot,window,stageToken,originalText??text,"No new action was accepted.");return;}
             if(stepIndex>0)UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_CATEGORY: step="+stepIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; result=dispatch-rejected; targets=0.");
             conversationBusy=false;AppendRememberedDialoguePair(window,originalText??text,DialogueReplyFallback.UnavailableReply,"unavailable");DaggerfallUI.AddHUDText("Waiting for the current reply. No action was accepted.");}
        }
    }
}
