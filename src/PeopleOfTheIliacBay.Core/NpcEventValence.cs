using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcEventValence
    {
        // This is the observer's remembered evaluation, not another mood write.
        // Native experience appraisals already own changes to mood/relationships.
        public static int PersonalOutcome(int outcome,bool self,int? affinity)
        {
            if(outcome< -5||outcome>5||affinity.HasValue && (affinity< -100||affinity>100))
                throw new ArgumentException("Invalid event valuation.");
            if(self)return outcome;
            if(!affinity.HasValue || affinity.Value==0)return 0;
            return affinity.Value>0?outcome:-outcome;
        }
        public static int Trade(int actualNet,int expectedNet)
        {
            long difference=(long)actualNet-expectedNet;if(difference==0)return 0;
            double ratio=Math.Abs(difference)/(double)Math.Max(1,Math.Abs((long)expectedNet));
            int weight=(int)Math.Min(5,Math.Max(1,Math.Ceiling(ratio*2)));
            return difference>0?weight:-weight;
        }
        public static int Default(NpcSceneEventKind kind)
        {
            switch(kind)
            {
                case NpcSceneEventKind.ClosedShop:return -1;
                case NpcSceneEventKind.Conversation:return 1;
                case NpcSceneEventKind.Assault:case NpcSceneEventKind.Injury:case NpcSceneEventKind.Illness:return -3;
                case NpcSceneEventKind.Fight:case NpcSceneEventKind.Burglary:
                case NpcSceneEventKind.GuardPursuit:case NpcSceneEventKind.Theft:return -2;
                case NpcSceneEventKind.Healing:case NpcSceneEventKind.Recovery:case NpcSceneEventKind.Help:return 3;
                case NpcSceneEventKind.Gift:return 2;
                case NpcSceneEventKind.Murder:return -5;
                case NpcSceneEventKind.Corpse:return -4;
                default:return 0;
            }
        }
    }
}
