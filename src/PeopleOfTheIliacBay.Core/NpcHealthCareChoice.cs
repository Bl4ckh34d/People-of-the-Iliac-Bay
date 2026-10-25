using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcHealthCareCandidateKind { Rest=0, OwnedHealingItem=1, BuyHealingItem=2, OwnedHealingSpell=3 }

    /// <summary>Verified facts for one feasible treatment plan offered by runtime discovery.</summary>
    public sealed class NpcHealthCareCandidate
    {
        public string StableId;
        public NpcHealthCareCandidateKind Kind;
        public int CostGold,CostMagicka;
        public double ExpectedTimeSeconds;
        public int ExpectedHealthGain;
        public bool IsSafe,IsAccessible,SupplyKnown;
    }

    public sealed class NpcHealthCareChoiceContext
    {
        public int CurrentHealth,MaximumHealth,AvailableGold,CurrentMagicka;
        public bool UrgentThreat,HasTimeBudget;
        public double TimeBudgetSeconds;
    }

    /// <summary>Exposed scoring weights make care priorities tunable without NPC-specific rules.</summary>
    public sealed class NpcHealthCareChoicePolicy
    {
        public double CriticalHealthFraction=0.25;
        public double HealthBenefitWeight=100;
        public double GoldPenaltyPerGold=0.25;
        public double MagickaPenaltyPerPoint=0.25;
        public double OrdinaryTimePenaltyPerGameHour=0.5;
        public double UrgentTimePenaltyPerGameHour=150;
        public double OrdinaryRestPreference=40;
        public double OrdinaryOwnedItemPreference=10;
        public double OrdinaryBuyItemPreference=1;
        public double OrdinarySpellPreference=20;
    }

    public sealed class NpcHealthCareChoiceResult
    {
        public NpcHealthCareCandidate Candidate {get;private set;}
        public double Score {get;private set;}
        public bool IsUrgent {get;private set;}
        public int EffectiveHealthGain {get;private set;}

        internal NpcHealthCareChoiceResult(NpcHealthCareCandidate candidate,double score,bool urgent,int effectiveHealthGain)
        {
            Candidate=candidate;Score=score;IsUrgent=urgent;EffectiveHealthGain=effectiveHealthGain;
        }
    }

    /// <summary>Pure, deterministic ranking of runtime-discovered health treatment candidates.</summary>
    public static class NpcHealthCareChoice
    {
        public const int MaximumCandidates=128;
        public const double MaximumCandidateTimeSeconds=86400;
        private const double SecondsPerGameHour=3600;
        private const double MaximumWeight=1000000;

        public static NpcHealthCareChoiceResult Select(NpcHealthCareChoiceContext context,
            IEnumerable<NpcHealthCareCandidate> candidates,NpcHealthCareChoicePolicy policy=null)
        {
            var ranked=Rank(context,candidates,policy);
            return ranked.Count==0?null:ranked[0];
        }

        public static List<NpcHealthCareChoiceResult> Rank(NpcHealthCareChoiceContext context,
            IEnumerable<NpcHealthCareCandidate> candidates,NpcHealthCareChoicePolicy policy=null)
        {
            ValidateContext(context);
            policy=policy??new NpcHealthCareChoicePolicy();
            ValidatePolicy(policy);
            if(candidates==null)throw new ArgumentNullException(nameof(candidates));

            var proposed=new List<NpcHealthCareCandidate>();
            var stableIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var candidate in candidates)
            {
                if(proposed.Count>=MaximumCandidates)throw new ArgumentException("Too many health-care candidates.",nameof(candidates));
                ValidateCandidate(candidate);
                if(!stableIds.Add(candidate.StableId))throw new ArgumentException("Duplicate health-care candidate ID.",nameof(candidates));
                proposed.Add(candidate);
            }

            var result=new List<NpcHealthCareChoiceResult>();
            if(context.CurrentHealth==0 || context.CurrentHealth==context.MaximumHealth)return result;
            bool urgent=context.UrgentThreat || (double)context.CurrentHealth/context.MaximumHealth<=policy.CriticalHealthFraction;
            int missing=context.MaximumHealth-context.CurrentHealth;
            foreach(var candidate in proposed)
            {
                // Potions cover immediate recovery, not the final points that free rest can restore.
                if((candidate.Kind==NpcHealthCareCandidateKind.OwnedHealingItem ||
                    candidate.Kind==NpcHealthCareCandidateKind.BuyHealingItem) &&
                    (long)context.CurrentHealth*3>=(long)context.MaximumHealth*2)continue;
                int effectiveGain=Math.Min(candidate.ExpectedHealthGain,missing);
                if(!candidate.IsSafe || !candidate.IsAccessible || !candidate.SupplyKnown ||
                    candidate.CostGold>context.AvailableGold || candidate.CostMagicka>context.CurrentMagicka || effectiveGain<=0 ||
                    context.HasTimeBudget && candidate.ExpectedTimeSeconds>context.TimeBudgetSeconds)
                    continue;
                double hours=candidate.ExpectedTimeSeconds/SecondsPerGameHour;
                double preference=urgent?0:OrdinaryPreference(policy,candidate.Kind);
                double score=policy.HealthBenefitWeight*((double)effectiveGain/missing)+preference-
                    policy.GoldPenaltyPerGold*candidate.CostGold-policy.MagickaPenaltyPerPoint*candidate.CostMagicka-
                    (urgent?policy.UrgentTimePenaltyPerGameHour:policy.OrdinaryTimePenaltyPerGameHour)*hours;
                if(!Finite(score))throw new ArgumentException("Health-care candidate score is not finite.",nameof(candidates));
                result.Add(new NpcHealthCareChoiceResult(Copy(candidate),score,urgent,effectiveGain));
            }
            result.Sort((a,b)=>
            {
                int score=b.Score.CompareTo(a.Score);
                return score!=0?score:StringComparer.Ordinal.Compare(a.Candidate.StableId,b.Candidate.StableId);
            });
            return result;
        }

        private static void ValidateContext(NpcHealthCareChoiceContext value)
        {
            if(value==null || value.MaximumHealth<1 || value.CurrentHealth<0 ||
                value.CurrentHealth>value.MaximumHealth || value.AvailableGold<0 || value.CurrentMagicka<0 ||
                value.AvailableGold>NpcInventoryLimits.MaximumWalletGold || value.HasTimeBudget &&
                (!Finite(value.TimeBudgetSeconds) || value.TimeBudgetSeconds<0 ||
                 value.TimeBudgetSeconds>MaximumCandidateTimeSeconds))
                throw new ArgumentException("Invalid health-care choice context.",nameof(value));
        }

        private static void ValidatePolicy(NpcHealthCareChoicePolicy value)
        {
            if(value==null || !Finite(value.CriticalHealthFraction) || value.CriticalHealthFraction<=0 ||
                value.CriticalHealthFraction>1 || !Weight(value.HealthBenefitWeight) || !Weight(value.MagickaPenaltyPerPoint) ||
                !Weight(value.GoldPenaltyPerGold) || !Weight(value.OrdinaryTimePenaltyPerGameHour) ||
                !Weight(value.UrgentTimePenaltyPerGameHour) || !Weight(value.OrdinaryRestPreference) ||
                !Weight(value.OrdinaryOwnedItemPreference) || !Weight(value.OrdinaryBuyItemPreference) || !Weight(value.OrdinarySpellPreference))
                throw new ArgumentException("Invalid health-care choice policy.",nameof(value));
        }

        private static void ValidateCandidate(NpcHealthCareCandidate value)
        {
            if(value==null || string.IsNullOrWhiteSpace(value.StableId) || value.StableId.Length>240 ||
                (int)value.Kind<0 || (int)value.Kind>3 || value.CostGold<0 || value.CostMagicka<0 ||
                value.CostGold>NpcInventoryLimits.MaximumWalletGold ||
                !Finite(value.ExpectedTimeSeconds) || value.ExpectedTimeSeconds<0 ||
                value.ExpectedTimeSeconds>MaximumCandidateTimeSeconds || value.ExpectedHealthGain<0 ||
                value.ExpectedHealthGain>10000 ||
                value.Kind==NpcHealthCareCandidateKind.OwnedHealingItem && value.CostGold!=0 ||
                value.Kind==NpcHealthCareCandidateKind.OwnedHealingSpell && (value.CostGold!=0 || value.CostMagicka<=0) ||
                value.Kind!=NpcHealthCareCandidateKind.OwnedHealingSpell && value.CostMagicka!=0)
                throw new ArgumentException("Invalid health-care candidate.",nameof(value));
        }

        private static double OrdinaryPreference(NpcHealthCareChoicePolicy policy,NpcHealthCareCandidateKind kind)
        {
            switch(kind)
            {
                case NpcHealthCareCandidateKind.Rest:return policy.OrdinaryRestPreference;
                case NpcHealthCareCandidateKind.OwnedHealingItem:return policy.OrdinaryOwnedItemPreference;
                case NpcHealthCareCandidateKind.OwnedHealingSpell:return policy.OrdinarySpellPreference;
                default:return policy.OrdinaryBuyItemPreference;
            }
        }

        private static NpcHealthCareCandidate Copy(NpcHealthCareCandidate value)
        {
            return new NpcHealthCareCandidate {StableId=value.StableId,Kind=value.Kind,CostGold=value.CostGold,
                CostMagicka=value.CostMagicka,
                ExpectedTimeSeconds=value.ExpectedTimeSeconds,ExpectedHealthGain=value.ExpectedHealthGain,
                IsSafe=value.IsSafe,IsAccessible=value.IsAccessible,SupplyKnown=value.SupplyKnown};
        }

        private static bool Weight(double value)
        {return Finite(value) && value>0 && value<=MaximumWeight;}
        private static bool Finite(double value)
        {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
