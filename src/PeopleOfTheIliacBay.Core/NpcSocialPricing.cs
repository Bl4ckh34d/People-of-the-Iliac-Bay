using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcSocialPricing
    {
        // Native merchant pricing already includes Mercantile and Personality.
        // Trust and liking dominate; familiarity amplifies an established bond.
        public static int Favour(int? trust,int affinity,double knownDays,int sharedExchanges,int mood,int memory)
        {
            if(trust.HasValue && (trust<0 || trust>100) || affinity< -100 || affinity>100 ||
                double.IsNaN(knownDays) || double.IsInfinity(knownDays) || knownDays<0 || sharedExchanges<0)
                throw new ArgumentException("Invalid social price inputs.");
            double bond=(trust.HasValue?(trust.Value-50)*0.4:0)+affinity*0.15;
            double familiarity=Math.Min(1,knownDays/30)*Math.Min(1,sharedExchanges/20d);
            double result=bond*(1+0.3*familiarity)+Math.Max(-3,Math.Min(3,mood/10d))+Math.Max(-3,Math.Min(3,memory));
            return (int)Math.Round(Math.Max(-45,Math.Min(45,result)),MidpointRounding.AwayFromZero);
        }
        public static int Apply(int nativePrice,bool buying,int favour)
        {
            if(nativePrice<0 || favour< -45 || favour>45)throw new ArgumentException("Invalid social price.");
            if(nativePrice==0)return 0;
            long adjusted=((long)nativePrice*(buying?100-favour:100+favour)+50)/100;
            return (int)Math.Min(int.MaxValue,Math.Max(1,adjusted));
        }
        public static int MerchantQuote(int nativeBuy,int nativeSell,bool buying,int favour)
        {
            int purchase=Apply(nativeBuy,true,favour);
            int resale=Apply(nativeSell,false,favour);
            // A social discount and a generous buyback share one price boundary.
            // For the same item and social state, selling cannot repay more than buying.
            return buying?purchase:Math.Min(purchase,resale);
        }
    }
    public sealed partial class WorldStateStore
    {
        public int PersonalTradeFavour(string person,double now)
        {
            Require(Finite(now) && now>=0,"Invalid trade time.");
            var attitude=SocialAttitude(person);int affinity;
            if(!TryGetActualConversationDisposition(person,out affinity))affinity=0;
            var relation=ReadNpcRelationship(person,NpcDirectedRelationship.PlayerTargetId,now);
            double first=now;var journal=MemoryFor(person,false);
            if(journal!=null)foreach(var entry in journal.Entries)
                if(entry.At<=now && ConversationMemoryProvenance.IsSuccessfulSharedExchange(entry))first=Math.Min(first,entry.At);
            int impressions=0;var memories=SceneMemoriesFor(person,false);
            if(memories!=null)foreach(var entry in memories)
                if(entry.ResponsibleId==NpcDirectedRelationship.PlayerTargetId && now>=entry.At && now<entry.ExpiresAt)
                    impressions+=entry.Valence;
            return NpcSocialPricing.Favour(relation!=null && relation.TrustKnown?(int?)relation.ReliabilityTrust:null,
                affinity,(now-first)/86400,attitude==null?0:attitude.SharedExchangeCount,
                CaptureConversationMood(person,now).Value,Math.Max(-3,Math.Min(3,impressions)));
        }
    }
}
