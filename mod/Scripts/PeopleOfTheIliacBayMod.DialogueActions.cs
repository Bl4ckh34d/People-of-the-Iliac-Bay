using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private NpcActionOffer pendingConversationAction;
        private string pendingConversationActionHistory;
        private void InvalidatePendingConversationAction()
        {
            var offer=pendingConversationAction;
            pendingConversationAction=null;
            pendingConversationActionHistory=null;
            if(offer!=null)offer.Invalidate();
        }
        private static double ConversationActionTime()
        {return DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();}
        private ConversationContextSnapshot CaptureActionResultContext(DaggerfallTalkWindow window,string text,
            ConversationContextSnapshot snapshot,bool nativeStateChanged)
        {
            var source=snapshot??conversationContextCurrentSnapshot;
            if(source==null || !IsBoundSnapshotOwnershipCurrent(source,window))return null;
            if(!nativeStateChanged && IsCurrentConversationContext(source,window))return source;
            try{return CaptureConversationContext(window,text,checked(conversationContextRequestRevision+1));}
            catch(InvalidOperationException){return null;}
        }
        private void SubmitActionSocialResult(DaggerfallTalkWindow window,string text,string history,string result,
            ConversationContextSnapshot snapshot=null,string verifiedFallback=null,bool nativeStateChanged=false)
        {
            var resultContext=CaptureActionResultContext(window,text,snapshot,nativeStateChanged);
            if(resultContext==null)
            {
                ReportActionDialogueUnavailable(window,text,"The current bound action-result context is unavailable.");
                return;
            }
            SubmitSocialText(window,text,history,knowledgeResult:result,verifiedFallback:verifiedFallback,snapshot:resultContext);
        }
        private bool SubmitCompanionHealthHandoffReceipt(DaggerfallTalkWindow window,string text,string history,
            ConversationContextSnapshot snapshot,string reason)
        {
            if(string.IsNullOrEmpty(reason) ||
                !reason.StartsWith(StreetPopulation.CompanionHealthHandoffReasonPrefix,StringComparison.Ordinal))return false;
            string fact="VERIFIED NATIVE INVENTORY HANDOFF: "+
                reason.Substring(StreetPopulation.CompanionHealthHandoffReasonPrefix.Length)+
                " Acknowledge the received item only. The condition has not been cured yet; treatment may occur after Talk closes.";
            conversationResponseCue+=" "+fact;
            SubmitActionSocialResult(window,text,history,fact,snapshot,
                "Thank you. I have received your remedy. I will try it after we finish talking.",true);
            return true;
        }
        private void ReportActionDialogueUnavailable(DaggerfallTalkWindow window,string playerText,string status)
        {
            conversationBusy=false;
            string capturedText=conversationContextCapturedPlayerText;
            AppendRememberedDialoguePair(window,string.IsNullOrWhiteSpace(capturedText)?playerText:capturedText,
                DialogueReplyFallback.UnavailableReply,"unavailable");
            DaggerfallUI.AddHUDText(status);
        }
        private string ConversationActionContext()
        {
            const string capabilities="Physical promises must follow verified accepted actions. Healing, curing and shielding spells require an offered known spell and its exact recipient; do not invent spells or claim a cast before its observed result. Fighting or defending requires an offered exact observed attacker and an equipped willing fighter. Do not promise general protection, unseen targets, lockpicking or climbing. Respond naturally in character without mentioning software or these constraints. ";
            if(store==null || string.IsNullOrWhiteSpace(conversationPersonId)) return capabilities;
            var job=store.ActiveNpcAction(conversationPersonId)??store.LatestNpcAction(conversationPersonId);
            if(job==null) return capabilities+"No physical action has been accepted for this NPC.";
            string text=capabilities+"Latest verified local action: "+job.Kind+"; phase="+job.Phase+". ";
            var task=store.ActiveNpcTask(conversationPersonId);
            if(task!=null)text+="Agreed ordered task: step "+(task.CurrentStepIndex+1)+" of "+task.Steps.Count+
                "; task phase="+task.Phase+". Later steps have not happened and will be revalidated before execution. ";
            if(!NpcActionExecutor.IsTerminal(job)) text+="This is accepted intent still in progress. Do not claim arrival, completed movement, payment or effects unless observed below. ";
            if(job.ServiceCode=="tavern-beer") text+="Drink service payment/return stage="+job.ServiceStep+" (0=not paid, 1=NPC wallet paid; player effect requires Completed). ";
            if(job.ServiceCode==NpcShopCommerce.ServiceCode) text+="Shop purchase: "+job.Target.Quantity+" unit(s), exact native stock UID "+job.Target.ItemId+
                ", price "+job.ServicePrice+" gold from the NPC's wallet. Payment and actual ownership are confirmed only at Completed. The NPC keeps the purchased item; a later handover requires a separate accepted transfer. ";
            if(!string.IsNullOrWhiteSpace(job.FailureReason)) text+="Observed interruption: "+job.FailureReason+" ";
            if(job.Kind==NpcActionKind.Interact) text+="This adapter only approaches and faces the other person; it does not generate their speech. ";
            if(job.Kind==NpcActionKind.ReportMurder) text+="You agreed to carry the player's anonymous murder report to a guard. It is hearsay, not your eyewitness knowledge. Delivery is confirmed only at Completed. No crime location, suspect, arrest or investigation is established by this action. ";
            return text;
        }
        private bool TrySemanticActionConversation(DaggerfallTalkWindow window,string text,string history,
            ConversationContextSnapshot snapshot)
        {
            if(snapshot==null || !IsCurrentConversationContext(snapshot,window))return false;
            if(TryPendingSemanticOfferConversation(window,text,history,snapshot))return true;
            bool active=HasActiveSemanticCommitment(snapshot);
            if(!active && decisionPlannerEnabled && !DecisionWillingnessEnabled)
            {
                SubmitUnconfirmedWillingness(window,text,history,snapshot,null);
                return true;
            }
            if(!active && GoalExtractionEnabled)
                return TryBeginGoalActionConversation(window,text,history,snapshot);
            if(NpcActionRouting.Choose("action",false,active)==NpcActionConversationRoute.Single)
                return TrySemanticActionSingleConversation(window,text,history,snapshot);
            return TryBeginSemanticActionDecomposition(window,text,history,
                singleText=>TrySemanticActionSingleConversation(window,singleText,history,snapshot),snapshot);
        }
        private bool TryPendingSemanticOfferConversation(DaggerfallTalkWindow window,string text,string history,
            ConversationContextSnapshot snapshot)
        {
            if(snapshot==null || !IsCurrentConversationContext(snapshot,window))return false;
            if(pendingConversationAction==null)return false;
            var capturedOffer=pendingConversationAction;
            var actor=conversationActor;
            NpcActionOption offered=null;int duration=0;
            bool valid=actor && streets && store!=null && history==pendingConversationActionHistory &&
                capturedOffer.TryGetSemanticCandidate(conversationPersonId,memorySession,ConversationActionTime(),out offered,out duration);
            if(!valid)
            {pendingConversationAction=null;pendingConversationActionHistory=null;return false;}
            if(NpcActionRouting.Choose("social",true,false)!=NpcActionConversationRoute.Offer)return false;
            return TrySemanticOfferResponse(window,text,history,actor,conversationPersonId,memorySession,offered,duration,snapshot,capturedOffer);
        }
        private static bool HasActiveSemanticCommitment(ConversationContextSnapshot snapshot)
        {
            return snapshot!=null && NpcActionChat.HasActiveSemanticCommitment(snapshot.Commitments);
        }
        private bool CurrentActionMatchesSnapshotCommitment(ConversationContextSnapshot snapshot,string person)
        {
            if(snapshot==null || store==null || string.IsNullOrWhiteSpace(person) || person!=conversationPersonId)return false;
            var currentTask=store.ActiveNpcTask(person);
            if(currentTask!=null && !NpcTaskContract.IsTerminal(currentTask))
            {
                return NpcActionChat.MatchesCurrentCommitment(snapshot.Commitments,"task",currentTask.Id,currentTask.Revision);
            }
            var currentAction=store.ActiveNpcAction(person);
            if(currentAction==null || NpcActionExecutor.IsTerminal(currentAction))return false;
            return NpcActionChat.MatchesCurrentCommitment(snapshot.Commitments,"action-task",currentAction.Id,currentAction.Revision,currentAction.Kind);
        }
        private bool TrySemanticActionSingleConversation(DaggerfallTalkWindow window,string text,string history,
            ConversationContextSnapshot snapshot,IList<NpcActionOption> selectedOptions=null)
        {
            if(snapshot==null || !IsCurrentConversationContext(snapshot,window))return false;
            const string noAction="No physical action was accepted. Do not promise movement, departure, purchase, handover, combat, magic, or a completed effect.";
            var actor=conversationActor;
            if(!actor || !streets || !streets.Ready || store==null)
            {conversationResponseCue+=" "+noAction;SubmitConversationActionReply(window,text,snapshot,ConversationActionReply.NoChange());return true;}
            int session=memorySession;string person=actor.Resident.Id;
            if(pendingConversationAction!=null)
            {
                var capturedOffer=pendingConversationAction;
                NpcActionOption offered;int offeredDuration;
                if(history==pendingConversationActionHistory && capturedOffer.TryGetSemanticCandidate(person,session,ConversationActionTime(),out offered,out offeredDuration))
                    return TrySemanticOfferResponse(window,text,history,actor,person,session,offered,offeredDuration,snapshot,capturedOffer);
                // A stale offer cannot authorize this turn. Drop it and evaluate
                // the current direct request against fresh native capabilities.
                pendingConversationAction=null;pendingConversationActionHistory=null;
            }
            var options=selectedOptions??streets.ConversationActionCandidates(actor.Resident.Id);
            if(options==null) options=new List<NpcActionOption>();
            // An active commitment still needs the cancellation interpreter even without a new target category.
            bool active=HasActiveSemanticCommitment(snapshot);
            if(selectedOptions==null && options.Count>0 && !active)
            {SelectSemanticActionCandidates(window,text,history,options,chosen=>TrySemanticActionSingleConversation(window,text,history,snapshot,chosen),snapshot);return true;}
            if(selectedOptions==null && active)options=new List<NpcActionOption>();
            string proposalId="dialogue:"+Guid.NewGuid().ToString("N");
            string context=ConversationContextFormatter.ForSemanticClassifier(snapshot,2048);
            if(options.Count==0 && !active) context+=" No verified local action target is currently available; do not accept a physical action.";
            bool separateWillingness=DecisionWillingnessEnabled || QwenWillingnessEnabled;
            long stageToken=BeginConversationContextStage(snapshot,window);
            if(stageToken<=0){HandleRejectedConversationStage(snapshot,window,stageToken,text,"No new action was accepted.");return true;}
            if(!localDialogue.Classify(NewDialogueConnection(),NpcActionChat.PromptSemantic(text,context,options,active,separateWillingness),history,output=>
            {
                if(!CurrentMemorySession(window,session)) return;
                if(conversationPersonId!=person)
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,null);return;}
                if(!TryConsumeConversationContextStage(snapshot,window,stageToken))
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,"No new action was accepted.");return;}
                if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                {ReportActionDialogueUnavailable(window,text,"Local dialogue unavailable. No action was accepted.");return;}
                if(output!=null && output.Trim()=="NONE")
                {
                    // A coarse action classification can be corrected by the
                    // concrete interpreter. Answer the question without claiming
                    // refusal of a request the player never made.
                    SubmitOrdinarySocialText(window,text,history,snapshot);
                    return;
                }
                if(output!=null && output.Trim()=="CANCEL")
                {
                    string cancelResult="No active action or task could be stopped.";
                    bool belongsToCapturedCommitment=CurrentActionMatchesSnapshotCommitment(snapshot,person);
                    bool stopped=belongsToCapturedCommitment && streets.TryStopConversationAction(person,out cancelResult);
                    if(!belongsToCapturedCommitment && HasActiveSemanticCommitment(snapshot))
                        cancelResult="The earlier task is no longer active. I did not stop another task.";
                    if(stopped)
                        cancelResult="The current action was cancelled: "+cancelResult;
                    else if(belongsToCapturedCommitment)cancelResult="No active action or task could be stopped. No physical action was cancelled.";
                    CloseConversationCommandTurn(snapshot);
                    conversationResponseCue+=" "+cancelResult;
                    SubmitConversationActionReply(window,text,snapshot,SavedConversationCancellationReply(snapshot,stopped));return;
                }
                var decision=NpcActionChat.ParseSemantic(output,options);
                string result=noAction;
                var reply=ConversationActionReply.NoChange();
                if(decision!=null)
                {
                    ResolveProposedActionWillingness(actor,window,text,history,snapshot,separateWillingness,
                        new List<NpcActionDecision>{decision},agreed=>
                    {
                    var accepted=agreed[0];
                    if(TryBeginPaidCompanionConsent(window,text,snapshot,session,person,proposalId,accepted))return;
                    string reason;
                    bool committed=PrepareSemanticActionDecision(actor,accepted,out reason) &&
                        streets.TryAcceptConversationAction(person,proposalId,accepted,CommandTurnFor(snapshot),out reason);
                    if(committed && SubmitCompanionHealthHandoffReceipt(window,text,history,snapshot,reason))return;
                    if(committed)
                    {
                        result="ACTION ACCEPTED AND SAVED: "+SemanticAcceptedActionLabel(accepted)+". "+reason+
                            " Acknowledge only accepted intent. The person will move physically when this conversation closes. Never claim arrival or effects already happened.";
                    }
                    else result+=" Executor reports: "+reason;
                    if(!committed)
                        UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_REJECTED: kind="+accepted.Option.Kind+"; reason="+reason);
                    reply=SavedConversationActionReply(snapshot,proposalId,false,committed,accepted.Option.Kind);
                    conversationResponseCue+=" "+result;
                    SubmitConversationActionReply(window,text,snapshot,reply);
                    });
                    return;
                }
                else if(output!=null && (output.Trim()=="DECLINE" || output.Trim()=="NONE" ||
                    output.Trim()=="NEGATED" || output.Trim()=="REPORTED" || output.Trim()=="HYPOTHETICAL" || output.Trim()=="CONDITIONAL"))
                    result+=" The NPC did not accept an action; explain naturally in your own voice.";
                conversationResponseCue+=" "+result;
                SubmitConversationActionReply(window,text,snapshot,reply);
            },NpcActionChat.GrammarSemantic(options,active),classificationTrace:SemanticActionModelCaptureProbe.ForStage("action-single")))
            {if(!TryConsumeConversationContextStage(snapshot,window,stageToken)){HandleRejectedConversationStage(snapshot,window,stageToken,text,"No new action was accepted.");return true;}ReportActionDialogueUnavailable(window,text,"Waiting for the current reply. No action was accepted.");}
            return true;
        }

        private bool TrySemanticOfferResponse(DaggerfallTalkWindow window,string text,string history,ResidentActor actor,string person,int session,
            NpcActionOption offered,int offeredDuration,ConversationContextSnapshot snapshot,
            NpcActionOffer capturedOffer=null)
        {
            if(snapshot==null || capturedOffer==null || !IsCurrentConversationContext(snapshot,window))return false;
            var options=new List<NpcActionOption>{offered};
            string noAccept="No previously offered action was accepted. Do not promise movement or effects.";
            string context=ConversationContextFormatter.ForSemanticClassifier(snapshot,2048)+
                " A verified NPC offer is awaiting the player's response: "+offered.Label+"; exact duration "+
                offeredDuration.ToString(System.Globalization.CultureInfo.InvariantCulture)+" seconds. This offer has not been accepted or executed.";
            bool separateWillingness=DecisionWillingnessEnabled || QwenWillingnessEnabled;
            long stageToken=BeginConversationContextStage(snapshot,window);
            if(stageToken<=0){HandleRejectedConversationStage(snapshot,window,stageToken,text,"The offered action remains unaccepted.");return true;}
            if(!localDialogue.Classify(NewDialogueConnection(),NpcActionChat.PromptPendingOfferResponse(text,context,offered,offeredDuration,separateWillingness),history,output=>
            {
                if(!CurrentMemorySession(window,session)) return;
                if(conversationPersonId!=person || !actor || conversationActor!=actor || actor.Resident==null || actor.Resident.Id!=person ||
                    pendingConversationAction!=capturedOffer)
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,null);return;}
                if(!TryConsumeConversationContextStage(snapshot,window,stageToken))
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,"The offered action remains unaccepted.");return;}
                if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                {ReportActionDialogueUnavailable(window,text,"Local dialogue unavailable. The offered action remains unaccepted.");return;}
                // NONE means this turn does not answer the offer. Keep it pending
                // and answer the player's actual question as ordinary dialogue.
                if(output!=null && output.Trim()=="NONE")
                {
                    SubmitOrdinarySocialText(window,text,history,snapshot);
                    return;
                }
                string result=noAccept;
                var reply=ConversationActionReply.NoChange();
                NpcActionDecision requested=NpcActionChat.ParseSemantic(output,options),accepted=null;
                bool consumed=requested!=null && capturedOffer.TryAcceptSemantic(person,session,ConversationActionTime(),requested,out accepted);
                pendingConversationAction=null;pendingConversationActionHistory=null;
                if(output!=null && output.Trim()=="NEW_REQUEST")
                {TrySemanticActionConversation(window,text,history,snapshot);return;}
                if(consumed)
                {
                    ResolveProposedActionWillingness(actor,window,text,history,snapshot,separateWillingness,
                        new List<NpcActionDecision>{accepted},agreed=>
                    {
                    var agreedAction=agreed[0];
                    string reason="The speaker or local action is no longer available.";
                    string proposalId="dialogue:"+Guid.NewGuid().ToString("N");
                    if(TryBeginPaidCompanionConsent(window,text,snapshot,session,person,proposalId,agreedAction))return;
                    bool committed=streets.TryAcceptConversationAction(person,proposalId,agreedAction,
                        CommandTurnFor(snapshot),out reason);
                    if(committed && SubmitCompanionHealthHandoffReceipt(window,text,history,snapshot,reason))return;
                    if(committed)
                    {
                        result="ACTION ACCEPTED AND SAVED: "+SemanticAcceptedActionLabel(agreedAction)+". "+reason+
                            " Acknowledge only the accepted intent. Movement and effects have not happened; execution starts when conversation closes.";
                    }
                    else result+=" The offered action could not be accepted: "+reason;
                    reply=SavedConversationActionReply(snapshot,proposalId,false,committed,agreedAction.Option.Kind);
                    conversationResponseCue+=" "+result;
                    SubmitConversationActionReply(window,text,snapshot,reply);
                    });
                    return;
                }
                else if(output!=null && output.Trim()=="CANCEL")
                {
                    string cancelled="There was no in-progress action to cancel.";
                    bool belongsToCapturedCommitment=CurrentActionMatchesSnapshotCommitment(snapshot,person);
                    bool stopped=belongsToCapturedCommitment && streets.TryStopConversationAction(person,out cancelled);
                    if(!belongsToCapturedCommitment && HasActiveSemanticCommitment(snapshot))
                        cancelled="The earlier task is no longer active. I did not stop another task.";
                    else if(!belongsToCapturedCommitment)cancelled="There was no in-progress action to cancel.";
                    result=stopped?"The current accepted action was cancelled: "+cancelled:
                        cancelled+" The pending offer is now closed; no action was accepted.";
                    CloseConversationCommandTurn(snapshot);
                    reply=SavedConversationCancellationReply(snapshot,stopped);
                }
                else if(output!=null && output.Trim()=="DECLINE")
                {result="The pending offer was declined or withdrawn. No action was accepted; no running action was cancelled.";}
                conversationResponseCue+=" "+result;SubmitConversationActionReply(window,text,snapshot,reply);
            },NpcActionChat.GrammarOfferResponse(options,snapshot.Commitments)))
            {if(!TryConsumeConversationContextStage(snapshot,window,stageToken)){HandleRejectedConversationStage(snapshot,window,stageToken,text,"The offered action remains unaccepted.");return true;}ReportActionDialogueUnavailable(window,text,"Waiting for the current reply. The offered action remains unaccepted.");}
            return true;
        }

        private bool PrepareSemanticActionDecision(ResidentActor actor,NpcActionDecision decision,out string reason)
        {
            reason="The selected native target is no longer available.";
            if(!actor || decision==null || decision.Option==null || decision.Option.Target==null) return false;
            if(decision.Option.Kind!=NpcActionKind.Purchase || decision.Option.Target.ContainerId=="tavern-beer")
            {reason=null;return true;}
            return streets.TryQuoteSemanticShopPurchase(actor,decision.Option.Target,out reason);
        }
        private static string SemanticAcceptedActionLabel(NpcActionDecision decision)
        {
            if(decision==null || decision.Option==null || decision.Option.Target==null) return "the validated action";
            if(decision.Option.Kind==NpcActionKind.Transfer || decision.Option.Kind==NpcActionKind.Purchase)
                return decision.Option.Kind+"; exact selected quantity="+decision.Option.Target.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)+
                    "; option="+decision.Option.Label;
            return decision.Option.Label;
        }
        private bool TryProposeConversationAction(DaggerfallTalkWindow window,string text,string history,
            ConversationContextSnapshot snapshot)
        {
            if(snapshot==null || !IsCurrentConversationContext(snapshot,window) || !conversationActor || !streets || !streets.Ready || store==null) return false;
            // A terminal action may still own physical routine recovery. As with
            // direct requests, that recovery must not prevent a fresh offer.
            if(HasActiveSemanticCommitment(snapshot)) return false;
            var options=streets.ConversationActionProposals(conversationPersonId);
            bool familiar=false;
            bool familiarityKnown=snapshot.PersonalState!=null &&
                snapshot.PersonalState.FamiliarWithPlayer.TryGetVerified(out familiar);
            var relationship=snapshot.DirectedRelationship;
            bool allowTravel=NpcConversationFavorPolicy.MaySpontaneouslyOfferTravel(
                familiarityKnown,familiar,snapshot.EffectiveDisposition,
                relationship!=null && relationship.TrustKnown,
                relationship==null?0:relationship.ReliabilityTrust,
                relationship==null?0:relationship.ProjectedResentment,
                relationship==null?0:relationship.ProjectedInterpersonalFear,
                NpcConversationFavorPolicy.HasHigherPriority(snapshot.MotivationContext));
            if(!allowTravel)
                options.RemoveAll(option=>option!=null && (option.Kind==NpcActionKind.Follow ||
                    option.Kind==NpcActionKind.Wait || option.Kind==NpcActionKind.MoveTo || option.Kind==NpcActionKind.Lead));
            if(options.Count==0) return false;
            int session=memorySession;string person=conversationPersonId;
            string context=ConversationContextFormatter.ForSemanticClassifier(snapshot,2048);
            long stageToken=BeginConversationContextStage(snapshot,window);
            if(stageToken<=0){HandleRejectedConversationStage(snapshot,window,stageToken,text,null);return true;}
            if(!localDialogue.Classify(NewDialogueConnection(),NpcActionChat.ProposalPrompt(text,context,options),history,output=>{
                if(!CurrentMemorySession(window,session)) return;
                if(conversationPersonId!=person)
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,null);return;}
                if(!TryConsumeConversationContextStage(snapshot,window,stageToken))
                {HandleRejectedConversationStage(snapshot,window,stageToken,text,null);return;}
                if(output==LocalDialogueClient.Unavailable || output==LocalDialogueClient.ContextLimit)
                {ReportActionDialogueUnavailable(window,text,"Local dialogue unavailable. No action was offered.");return;}
                var proposal=NpcActionChat.ParseProposal(output,options);
                if(proposal==null)
                {
                    // A classifier outage or NONE is no offer and no authorization.
                    // Preserve the ordinary reply's bounded own-memory/knowledge read.
                    SubmitOrdinarySocialText(window,text,history,snapshot);return;
                }
                // Selection authorizes an offer, never its execution. Use the same
                // grounded reply/receipt path as ordinary conversation, and expose
                // pending consent only after that reply has actually been accepted.
                string offerContext="VERIFIED AVAILABLE OFFER: "+proposal.Option.Label+
                    ". Respond to the player's current message naturally, then offer only this option and ask for consent."+
                    " No action has been accepted, started or completed. Do not claim otherwise.";
                SubmitSocialText(window,text,history,onAnswer:answer=>
                {
                    if(!CurrentMemorySession(window,session) || conversationPersonId!=person)return;
                    pendingConversationAction=new NpcActionOffer(person,session,ConversationActionTime(),proposal);
                    pendingConversationActionHistory=ReadConversationHistory(window);
                    ConversationMoodProbe.ObserveSpontaneousOffer(proposal);
                },knowledgeResult:offerContext,snapshot:snapshot);
            },NpcActionChat.ProposalGrammar(options)))
            {if(!TryConsumeConversationContextStage(snapshot,window,stageToken)){HandleRejectedConversationStage(snapshot,window,stageToken,text,null);return true;}ReportActionDialogueUnavailable(window,text,"Waiting for the current reply. No action was offered.");}
            return true;
        }
    }
}

