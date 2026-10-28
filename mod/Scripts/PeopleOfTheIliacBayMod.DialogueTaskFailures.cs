using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private void ResolveTaskFailureSemantics(ResidentActor actor,DaggerfallTalkWindow talk,string text,string history,
            ConversationContextSnapshot snapshot,IList<NpcActionDecision> proposed,Action<List<NpcActionDecision>> accept)
        {
            List<NpcActionDecision> frozen;
            try {frozen=new LocalActionWillingnessProposal(proposed).CopyDecisions();}
            catch(ArgumentException error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TASK_FAILURE_PROPOSAL_REJECTED: "+error.Message);
                SubmitUnconfirmedWillingness(talk,text,history,snapshot,null);return;
            }
            long stage=BeginConversationContextStage(snapshot,talk);
            if(stage<=0){HandleRejectedConversationStage(snapshot,talk,stage,text);return;}
            int session=memorySession;string person=conversationPersonId;
            bool started=localDialogue.Classify(NewDialogueConnection(),NpcTaskFailureChat.Prompt(text,frozen),history,output=>
            {
                if(!CurrentMemorySession(talk,session))return;
                if(conversationPersonId!=person || !TryConsumeConversationContextStage(snapshot,talk,stage))
                {HandleRejectedConversationStage(snapshot,talk,stage,text);return;}
                bool valid=NpcTaskFailureChat.Apply(output,frozen);
                UnityEngine.Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TASK_FAILURE_DEPENDENCY_RESULT: choice="+
                    (output??"<null>")+"; valid="+valid+"; steps="+frozen.Count);
                if(!valid)
                {SubmitUnconfirmedWillingness(talk,text,history,snapshot,null);return;}
                ResolveProposedActionWillingnessCore(actor,talk,text,history,snapshot,frozen,accept);
            },NpcTaskFailureChat.Grammar(frozen),responseTokens:64);
            if(!started && TryConsumeConversationContextStage(snapshot,talk,stage))
                SubmitUnconfirmedWillingness(talk,text,history,snapshot,null);
        }
    }
}
