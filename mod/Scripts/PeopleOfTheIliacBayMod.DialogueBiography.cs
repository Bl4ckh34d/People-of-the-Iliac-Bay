using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private void SubmitBiographicalSocialText(DaggerfallTalkWindow talk,string text,string history,
            ConversationContextSnapshot snapshot)
        {
            if(snapshot==null || store==null || saveFault || string.IsNullOrEmpty(conversationPersonId))
            {SubmitOrdinarySocialText(talk,text,history,snapshot);return;}
            var existing=store.ReadNpcBiography(conversationPersonId);
            var anchor=store.ReadNpcBiographyAnchor(conversationPersonId);
            if(anchor==null || existing!=null && (existing.FamilyId!=null || anchor.FamilyId==null))
            {SubmitOrdinarySocialText(talk,text,history,snapshot);return;}
            var family=store.ReadNpcFamilyBiography(anchor.FamilyId);
            if(existing!=null && family!=null)
            {
                existing.FamilyId=anchor.FamilyId;existing.IdentityAnchor=anchor.IdentityAnchor;existing.FamilyFacts=family;
                store.TryCommitNpcBiography(anchor,existing);
                SubmitOrdinarySocialText(talk,text,history,snapshot);return;
            }
            int session=memorySession;string person=conversationPersonId;
            long stage=BeginConversationContextStage(snapshot,talk);
            if(stage<=0){HandleRejectedConversationStage(snapshot,talk,stage,text);return;}
            string prompt=ConversationContextFormatter.ForSemanticClassifier(snapshot,3072)+"\n"+
                (existing==null?string.Empty:"Existing immutable personal biography: "+NpcBiographyChat.Context(existing)+
                    " Keep these three personal sentences unchanged; only the newly verified family's shared past is missing.\n")+
                NpcBiographyChat.Prompt(text,history,anchor,family);
            bool started=localDialogue.Classify(NewDialogueConnection(),prompt,history,output=>
            {
                if(!CurrentMemorySession(talk,session))return;
                if(conversationPersonId!=person || !TryConsumeConversationContextStage(snapshot,talk,stage))
                {HandleRejectedConversationStage(snapshot,talk,stage,text);return;}
                var record=NpcBiographyChat.Parse(output,anchor,snapshot.CapturedGameTime);
                if(record!=null)store.TryCommitNpcBiography(anchor,record);
                // Failure/irrelevance creates no facts. Existing memories remain authoritative.
                SubmitOrdinarySocialText(talk,text,history,snapshot);
            },NpcBiographyChat.Grammar(anchor.FamilyId!=null && family==null),responseTokens:512);
            if(!started && TryConsumeConversationContextStage(snapshot,talk,stage))
                SubmitOrdinarySocialText(talk,text,history,snapshot);
        }
        private string ConversationBiographyContext(string person)
        {
            if(store==null || saveFault || string.IsNullOrEmpty(person))return string.Empty;
            var own=store.ReadNpcBiography(person);
            string personal=NpcBiographyChat.Context(own);
            if(own!=null && own.FamilyId!=null)return personal;
            var anchor=store.ReadNpcBiographyAnchor(person);
            var shared=anchor==null?null:store.ReadNpcFamilyBiography(anchor.FamilyId);
            return personal+(shared==null?string.Empty:" Fixed shared family past (no new navigable entities or quests): "+string.Join(" ",shared.ToArray()));
        }
    }
}
