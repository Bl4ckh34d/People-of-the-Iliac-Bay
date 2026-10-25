using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum ConversationSpeechBoundaryAction { Continue, EndConversation, EndAndReport }

    public static class ConversationSpeechBoundaryPolicy
    {
        public static double RefusalSeconds(NpcPersonalityProfile personality,bool repeated)
        {
            if(personality!=null)NpcPersonalityProfile.CopyValidated(personality);
            int days=3;
            if(personality!=null)
            {
                if(personality.Sensitivity=="highly sensitive")days++;
                if(personality.Sensitivity=="thick-skinned")days--;
                if(personality.Forgiveness=="vindictive")days+=2;
                if(personality.Forgiveness=="forgiving")days--;
                if(personality.Patience=="impatient")days++;
                if(personality.Patience=="patient")days--;
            }
            days=Math.Max(2,Math.Min(7,days));
            return repeated?days*86400d:(3+days*3)*3600d;
        }

        // Use existing relationship values; no separate insult counter or friendship subsystem.
        public static bool ToleratesOrdinaryInsult(NpcPersonalityProfile personality, bool familiar,
            int disposition, bool trustKnown, int trust, int resentment, int fear)
        {
            if (!familiar) return false;
            int threshold = 65;
            if (personality != null)
            {
                if (personality.Sensitivity == "highly sensitive") threshold += 10;
                if (personality.Sensitivity == "thick-skinned") threshold -= 10;
                if (personality.Patience == "impatient") threshold += 5;
                if (personality.Patience == "patient") threshold -= 5;
                if (personality.Forgiveness == "vindictive") threshold += 5;
                if (personality.Forgiveness == "forgiving") threshold -= 5;
            }
            // Missing reliability evidence stays unknown in the relationship;
            // it must not erase familiarity and affinity earned through talk.
            return (disposition + (trustKnown ? trust : 50)) / 2 - resentment - fear / 2 >= threshold;
        }

        public static ConversationSpeechBoundaryAction Decide(object source, NpcSpeechActClassification classification,
            NpcPersonalityProfile personality = null)
        {
            if (source == null || classification == null || classification.Request == null ||
                !ReferenceEquals(source, classification.Request.Source))
                return ConversationSpeechBoundaryAction.Continue;

            var context = source as ConversationContextSnapshot;
            bool familiar;
            var relationship = context == null ? null : context.DirectedRelationship;
            if (classification.Kind == NpcExperienceSpeechActKind.DirectInsult &&
                !classification.Request.ListenerRefusesFurtherCommunication && context != null &&
                context.PersonalState.FamiliarWithPlayer.TryGetVerified(out familiar) &&
                ToleratesOrdinaryInsult(personality, familiar, context.EffectiveDisposition,
                    relationship!=null && relationship.TrustKnown, relationship==null?0:relationship.ReliabilityTrust,
                    relationship==null?0:relationship.ProjectedResentment, relationship==null?0:relationship.ProjectedInterpersonalFear))
                return ConversationSpeechBoundaryAction.Continue;

            switch (classification.Kind)
            {
                case NpcExperienceSpeechActKind.DirectThreat:
                    return ConversationSpeechBoundaryAction.EndAndReport;
                case NpcExperienceSpeechActKind.SevereAbuse:
                case NpcExperienceSpeechActKind.PersistentAbuse:
                case NpcExperienceSpeechActKind.DirectInsult:
                case NpcExperienceSpeechActKind.DirectHarassment:
                    return ConversationSpeechBoundaryAction.EndConversation;
                default:
                    return classification.Request.ListenerRefusesFurtherCommunication &&
                        classification.Kind!=NpcExperienceSpeechActKind.RepairAttempt ?
                        ConversationSpeechBoundaryAction.EndConversation:ConversationSpeechBoundaryAction.Continue;
            }
        }
    }
}
