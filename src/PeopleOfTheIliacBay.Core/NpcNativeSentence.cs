using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class NpcNativeSentenceResult
    {
        public int FineGold;
        public int PrisonDays;
    }

    public static class NpcNativeSentence
    {
        // Exact DaggerfallCourtWindow values, indexed by (Crimes - 1).
        private static readonly int[] PenaltyPerLegalRepPoint={5,5,6,6,10,5,5,3,8,8,9,6,0,8,0};
        private static readonly int[] BasePenaltyAmounts={300,200,600,1000,10000,200,500,100,500,500,1200,200,200,1000,100};
        private static readonly int[] MinimumPenaltyAmounts={50,10,80,10,9000,10,10,2,10,10,160,5,5,10,4};
        private static readonly int[] MaximumPenaltyAmounts={1000,800,1200,1500,12000,12000,1500,700,1500,1500,2000,1000,1000,1500,700};

        public static NpcNativeSentenceResult Calculate(int nativeCrime,int legalReputation,int walletGold,int[] nativeRandomValues)
        {
            int crimeIndex=ValidateCrime(nativeCrime);
            if(walletGold<0)throw new ArgumentOutOfRangeException(nameof(walletGold));
            int units=PenaltyUnits(crimeIndex,legalReputation);
            if(nativeRandomValues==null || nativeRandomValues.Length!=units)
                throw new ArgumentException("One native random value is required for every penalty unit.",nameof(nativeRandomValues));
            var sentence=new NpcNativeSentenceResult();
            for(int i=0;i<units;i++)
                if((nativeRandomValues[i]&1)!=0)sentence.FineGold+=40;
                else sentence.PrisonDays+=3;
            if(walletGold<sentence.FineGold)
            {
                sentence.PrisonDays+=(sentence.FineGold-walletGold)/40;
                sentence.FineGold=walletGold;
            }
            return sentence;
        }

        public static bool TryCalculate(int nativeCrime,int legalReputation,int walletGold,int[] nativeRandomValues,
            bool verbalOnly,out NpcNativeSentenceResult sentence)
        {
            if(verbalOnly){sentence=new NpcNativeSentenceResult();return false;}
            sentence=Calculate(nativeCrime,legalReputation,walletGold,nativeRandomValues);return true;
        }
        public static int RequiredRandomDraws(int nativeCrime,int legalReputation)
        {return PenaltyUnits(ValidateCrime(nativeCrime),legalReputation);}

        private static int ValidateCrime(int nativeCrime)
        {
            if(nativeCrime<1 || nativeCrime>PenaltyPerLegalRepPoint.Length)
                throw new ArgumentOutOfRangeException(nameof(nativeCrime));
            return nativeCrime-1;
        }
        private static int PenaltyUnits(int crimeIndex,int legalReputation)
        {
            long amount=BasePenaltyAmounts[crimeIndex]+(long)PenaltyPerLegalRepPoint[crimeIndex]*Math.Abs((long)legalReputation);
            if(amount<MinimumPenaltyAmounts[crimeIndex])amount=MinimumPenaltyAmounts[crimeIndex];
            if(amount>MaximumPenaltyAmounts[crimeIndex])amount=MaximumPenaltyAmounts[crimeIndex];
            return (int)(amount/40);
        }
    }
}
