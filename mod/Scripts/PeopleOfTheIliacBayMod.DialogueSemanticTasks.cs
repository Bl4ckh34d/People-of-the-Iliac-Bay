using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private bool TryBeginSemanticActionDecomposition(DaggerfallTalkWindow window,string text,string history,
            Action<string> singleContinuation,ConversationContextSnapshot snapshot)
        {
            var actor=conversationActor;
            if(snapshot==null || !actor || !streets || !streets.Ready || store==null || singleContinuation==null) return false;

            string person=actor.Resident.Id;
            int session=memorySession;
            var candidates=streets.ConversationActionCandidates(person,text);
            // Counting newly requested effects needs only this message and the
            // available kinds. Resolve references and willingness in later steps.
            string context=DescribeSemanticActionCandidates(candidates);

            long stageToken=BeginConversationContextStage(snapshot,window);
            if(stageToken<=0){HandleRejectedConversationStage(snapshot,window,stageToken,text,"No new action was accepted.");return true;}
            bool started=localDialogue.Classify(NewDialogueConnection(),NpcActionDecomposition.Prompt(text,context),string.Empty,output=>
            {
                if(!CurrentMemorySession(window,session)) return;
                if(conversationPersonId!=person)
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,null);return;}
                if(!TryConsumeConversationContextStage(snapshot,window,stageToken))
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,"No new action was accepted.");return;}
                if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                {
                    conversationBusy=false;
                    AppendRememberedDialoguePair(window,text,DialogueReplyFallback.UnavailableReply,"unavailable");
                    DaggerfallUI.AddHUDText("Local dialogue unavailable. No action was accepted.");
                    return;
                }

                var decomposition=NpcActionDecomposition.Parse(output,text);
                if(decomposition==null)
                {
                    UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_DECOMPOSITION: result=invalid; spans=0; taskAttempted=false.");
                    SubmitUnresolvedSemanticAction(window,text,history,
                        "The action request could not be safely divided. No action was accepted.",snapshot);
                    return;
                }
                var decompositionRoute=NpcActionRouting.AfterDecomposition(decomposition);
                if(decompositionRoute==NpcActionConversationRoute.Single)
                {
                    if(decomposition.Kind==NpcActionDecompositionKind.None)
                        UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_DECOMPOSITION: output=none; route=single-willingness-fallback; spans=0; taskAttempted=false.");
                    else
                        UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_DECOMPOSITION: result=single; spans=1; taskAttempted=false.");
                    singleContinuation(text);
                    return;
                }
                if(decompositionRoute==NpcActionConversationRoute.None)
                {
                    UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_DECOMPOSITION: result=invalid-route; spans=0; taskAttempted=false.");
                    SubmitUnresolvedSemanticAction(window,text,history,
                        "The action request could not be safely routed. No action was accepted.",snapshot);
                    return;
                }
                UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_DECOMPOSITION: result=chain; spans="+
                    decomposition.Clauses.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; taskAttempted=true.");
                TrySemanticActionSequence(window,text,history,decomposition.Clauses,person,session,snapshot);
            },NpcActionDecomposition.Grammar(),responseTokens:192,
                classificationTrace:SemanticActionModelCaptureProbe.ForStage("action-decomposition"));

            if(!started)
            {
                if(!TryConsumeConversationContextStage(snapshot,window,stageToken))
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,"No new action was accepted.");return true;}
                conversationBusy=false;
                AppendRememberedDialoguePair(window,text,DialogueReplyFallback.UnavailableReply,"unavailable");
                DaggerfallUI.AddHUDText("Waiting for the current reply. No action was accepted.");
            }
            return true;
        }

        private bool TrySemanticActionSequence(DaggerfallTalkWindow window,string originalText,string history,
            IList<string> clauses,string person,int session,ConversationContextSnapshot snapshot)
        {
            const string rejected="No task or physical action was accepted. Every requested step needs its own verified target and NPC agreement."+
                " Do not claim any step or effect completed.";
            var actor=conversationActor;
            if(snapshot==null)return false;
            if(clauses==null || clauses.Count<2 || clauses.Count>NpcTaskContract.MaximumSteps || !actor ||
                actor.Resident.Id!=person || !streets || !streets.Ready || store==null)
            {
                SubmitUnresolvedSemanticAction(window,originalText,history,rejected,snapshot);
                return true;
            }

            var options=new List<IList<NpcActionOption>>(clauses.Count);
            var outputs=new List<string>(clauses.Count);
            var decisions=new List<NpcActionDecision>(clauses.Count);
            bool separateWillingness=true;
            var rawCandidates=streets.ConversationActionCandidates(person);
            if(rawCandidates==null || rawCandidates.Count==0)
            {
                SubmitUnresolvedSemanticAction(window,originalText,history,rejected+" No verified local action candidates are available.",snapshot);
                return true;
            }

            string context=ConversationContextFormatter.ForSemanticClassifier(snapshot,2048)+
                " The unchanged complete request is: "+originalText+
                " The original text is partitioned into ordered contiguous exact source spans. Accept no partial task.\nOrdered original-text spans:\n"+
                DescribeSemanticActionClauses(clauses);
            Action<int> classify=null;
            classify=index=>
            {
                if(!CurrentMemorySession(window,session)) return;
                if(conversationPersonId!=person)
                {HandleRejectedConversationStage(snapshot,window,0,originalText,null);return;}
                var outputOptions=new List<NpcActionOption>();
                foreach(var candidate in rawCandidates)
                    if(candidate!=null && candidate.Target!=null && NpcTaskContract.Supported(candidate.Kind))
                        outputOptions.Add(candidate);

                if(index>0)
                    outputOptions=NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(decisions[index-1],outputOptions);

                if(index>0 && index<NpcTaskContract.MaximumSteps)
                {
                    var dependency=NpcActionSequenceChat.AcquiredItemHandoverSemantic(index-1,decisions[index-1]);
                    if(dependency!=null)
                    {
                        int maxId=0;
                        foreach(var candidate in outputOptions) if(candidate.Id>maxId) maxId=candidate.Id;
                        if(maxId<int.MaxValue)
                        {
                            dependency.Id=maxId+1;
                            outputOptions.Add(dependency);
                        }
                    }
                }
                UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_SEQUENCE_CANDIDATES: step="+
                    (index+1).ToString(System.Globalization.CultureInfo.InvariantCulture)+"; raw="+
                    rawCandidates.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; offered="+
                    outputOptions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; dependency="+
                    outputOptions.Exists(option=>option!=null && option.ItemSourceStep>0)+".");
                if(outputOptions.Count==0)
                {
                    SubmitUnresolvedSemanticAction(window,originalText,history,rejected,snapshot);
                    return;
                }
                string clause=clauses[index];
                string stepContext=context+"\nCurrent required step: "+(index+1)+" of "+clauses.Count+
                    (separateWillingness?
                    ". Interpret only this exact original span as requested parameters. Whole-task willingness is decided later. ":
                    ". Classify only this exact original span as one direct request and willing agreement. ")+
                    "If this span contains multiple requested effects, contains unsupported intent, or does not preserve its part of the full request, output NONE.";
                if(index>0 && decisions[index-1]!=null && decisions[index-1].Option!=null && decisions[index-1].Option.Target!=null)
                {
                    var previous=decisions[index-1].Option;
                    stepContext+=" Previous selected step (not yet accepted or executed): "+previous.Kind+" — "+previous.Label+
                        "; chosen quantity="+previous.Target.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)+
                        ". Resolve references to an item from this proposed task using its exact listed handover candidate; it is not yet owned or delivered.";
                }
                SelectSemanticActionCandidates(window,clause,history,outputOptions,selectedOptions=>
                {
                    options.Add(selectedOptions);
                    long stepToken=BeginConversationContextStage(snapshot,window);
                    if(stepToken<=0){HandleRejectedConversationStage(snapshot,window,stepToken,originalText,"No task or physical action was accepted.");return;}
                    bool started=localDialogue.Classify(NewDialogueConnection(),NpcActionChat.PromptSemanticTaskStep(clause,stepContext,selectedOptions,separateWillingness),history,output=>
                    {
                        if(!CurrentMemorySession(window,session)) return;
                        if(conversationPersonId!=person)
                        {HandleRejectedConversationStage(snapshot,window,stepToken,originalText,null);return;}
                        if(!TryConsumeConversationContextStage(snapshot,window,stepToken))
                        {HandleRejectedConversationStage(snapshot,window,stepToken,originalText,"No task or physical action was accepted.");return;}
                        if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                        {
                            conversationBusy=false;
                            AppendRememberedDialoguePair(window,originalText,DialogueReplyFallback.UnavailableReply,"unavailable");
                            DaggerfallUI.AddHUDText("Local dialogue unavailable. No task was accepted.");
                            return;
                        }
                        var decision=NpcActionChat.ParseSemantic(output,selectedOptions);
                        if(decision==null)
                        {
                            UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_SEQUENCE_STEP: index="+
                                index.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; result=no-willing-decision.");
                            SubmitUnresolvedSemanticAction(window,originalText,history,rejected+
                                " Not every required step received a verified willing decision.",snapshot);
                            return;
                        }
                        UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_SEQUENCE_STEP: index="+
                            index.ToString(System.Globalization.CultureInfo.InvariantCulture)+"; result=decision; kind="+
                            decision.Option.Kind+"; option="+decision.Option.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)+".");
                        outputs.Add(output);
                        decisions.Add(decision);
                        if(index+1<clauses.Count)
                        {
                            classify(index+1);
                            return;
                        }

                        var bound=NpcActionSequenceChat.ParseSemantic(clauses,options,outputs);
                        string reason="The semantic sequence did not bind to supported task contracts.";
                        bool prepared=bound!=null;
                        if(prepared)
                        {
                            foreach(var step in bound)
                            {
                                if(!PrepareSemanticActionDecision(actor,step,out reason))
                                {prepared=false;break;}
                            }
                        }
                        if(!prepared)
                        {SubmitUnresolvedSemanticAction(window,originalText,history,rejected+" "+reason,snapshot);return;}
                        ResolveProposedActionWillingness(actor,window,originalText,history,snapshot,bound,agreed=>
                        {
                        string proposal="dialogue-semantic-task:"+Guid.NewGuid().ToString("N");
                        bool saved=streets.TryAcceptConversationTask(person,proposal,agreed,
                            CommandTurnFor(snapshot),out reason);
                        UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_SEQUENCE_COMMIT: result="+(saved?"saved":"rejected")+
                            "; steps="+clauses.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)+".");
                        string result=saved?
                            "ORDERED TASK ACCEPTED AND SAVED: "+agreed.Count+" original-text steps. "+reason+
                            " Acknowledge only accepted intent. Physical execution starts after conversation closes.":
                            rejected+" Executor reports: "+reason;
                        conversationResponseCue+=" "+result;
                        SubmitConversationActionReply(window,originalText,snapshot,
                            SavedConversationActionReply(snapshot,proposal,true,saved));
                        });
                    },NpcActionChat.GrammarSemantic(selectedOptions,false),classificationTrace:
                        SemanticActionModelCaptureProbe.ForStage("action-step-"+(index+1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    if(!started)
                    {
                        if(!TryConsumeConversationContextStage(snapshot,window,stepToken))
                        {HandleRejectedConversationStage(snapshot,window,stepToken,originalText,"No task or physical action was accepted.");return;}
                        conversationBusy=false;
                        AppendRememberedDialoguePair(window,originalText,DialogueReplyFallback.UnavailableReply,"unavailable");
                        DaggerfallUI.AddHUDText("Waiting for the current reply. No task was accepted.");
                    }
                },snapshot,originalText,index>0?decisions[index-1]:null,index+1);
            };

            classify(0);
            return true;
        }

        private static string DescribeSemanticActionCandidates(IList<NpcActionOption> options)
        {
            if(options==null || options.Count==0) return "No verified local candidates are currently available.";
            var lines=new List<string>();var kinds=new HashSet<NpcActionKind>();
            foreach(var option in options)
                if(option!=null && option.Target!=null && kinds.Add(option.Kind))
                    lines.Add(option.Kind+": native targets available; later target selection supplies exact identities");
            return lines.Count==0?"No verified local candidates are currently available.":string.Join("\n",lines);
        }

        private static string DescribeSemanticActionClauses(IList<string> clauses)
        {
            if(clauses==null) return string.Empty;
            var lines=new List<string>(clauses.Count);
            for(int i=0;i<clauses.Count;i++)
                lines.Add((i+1).ToString(System.Globalization.CultureInfo.InvariantCulture)+": "+clauses[i]);
            return string.Join("\n",lines);
        }

        private void SubmitUnresolvedSemanticAction(DaggerfallTalkWindow window,string originalText,string history,string reason,
            ConversationContextSnapshot snapshot)
        {
            conversationResponseCue+=" "+reason;
            SubmitConversationActionReply(window,originalText,snapshot,ConversationActionReply.NoChange());
        }
    }
}

