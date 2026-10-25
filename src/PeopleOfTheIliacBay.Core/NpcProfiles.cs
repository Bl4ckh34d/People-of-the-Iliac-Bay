using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcArchetype {Citizen,Merchant,Thief,Warrior,Mage,Healer,Guard,Innkeeper}
    public sealed class NpcBaseProfile
    {
        private readonly int[] attributes,skills,rates;
        public int MaximumHealth {get;private set;}
        public int MaximumMagicka {get;private set;}
        public int GenerationVersion {get;private set;}
        public NpcArchetype Archetype {get;private set;}
        internal NpcBaseProfile(int[] attributes,int[] skills,int[] rates,int health,int magicka,int version,NpcArchetype archetype)
        {this.attributes=attributes;this.skills=skills;this.rates=rates;MaximumHealth=health;MaximumMagicka=magicka;GenerationVersion=version;Archetype=archetype;}
        public int Attribute(int index) {Index(index,8);return attributes[index];}
        public int BaseSkill(int index) {Index(index,35);return skills[index];}
        public int TrainingRatePermille(int index) {Index(index,35);return rates[index];}
        private static void Index(int value,int count) {if(value<0 || value>=count)throw new ArgumentOutOfRangeException(nameof(value));}
    }
    public static class NpcProfileGenerator
    {
        public static NpcBaseProfile Generate(uint worldSeed,string personId,NpcArchetype archetype,int version=1)
        {
            if(string.IsNullOrWhiteSpace(personId) || personId.Length>240) throw new ArgumentException("Invalid NPC identity.");
            if(version!=1) throw new ArgumentOutOfRangeException(nameof(version));
            if((int)archetype<0 || (int)archetype>7) throw new ArgumentOutOfRangeException(nameof(archetype));
            uint hash=2166136261u;
            unchecked {foreach(char value in personId) hash=(hash^value)*16777619u;}
            uint seed=StableSeed.Combine(worldSeed,hash,(uint)archetype,(uint)version);
            var attributes=new int[8];var skills=new int[35];var rates=new int[35];
            for(int i=0;i<attributes.Length;i++) attributes[i]=30+(int)(StableSeed.Combine(seed,0x53544154u,(uint)i,1)%41);
            switch(archetype)
            {
                case NpcArchetype.Thief: attributes[3]+=10;attributes[6]+=10;break;
                case NpcArchetype.Warrior: case NpcArchetype.Guard: attributes[0]+=10;attributes[4]+=10;break;
                case NpcArchetype.Mage: attributes[1]+=15;attributes[2]+=10;break;
                case NpcArchetype.Healer: attributes[1]+=10;attributes[2]+=15;break;
                case NpcArchetype.Merchant: case NpcArchetype.Innkeeper: attributes[5]+=10;break;
            }
            ulong primary=Primary(archetype),secondary=Mask(0,1,2,3,14,17,18,20,21);
            for(int i=0;i<skills.Length;i++)
            {
                bool major=(primary&(1UL<<i))!=0,minor=(secondary&(1UL<<i))!=0;
                skills[i]=5+(int)(StableSeed.Combine(seed,0x534b494cu,(uint)i,1)%11)+(major?20:minor?10:0);
                rates[i]=major?1500:minor?1100:750;
            }
            int health=attributes[4]+20+(archetype==NpcArchetype.Warrior || archetype==NpcArchetype.Guard ? 20:0);
            int magicka=(archetype==NpcArchetype.Mage || archetype==NpcArchetype.Healer)?attributes[1]*2:attributes[1]/2;
            return new NpcBaseProfile(attributes,skills,rates,health,magicka,version,archetype);
        }
        // Skill indices match DFCareer.Skills, not alphabetical display order.
        private static ulong Primary(NpcArchetype archetype)
        {
            switch(archetype)
            {
                case NpcArchetype.Thief:return Mask(13,15,16,18,19,28);
                case NpcArchetype.Warrior:return Mask(20,21,29,30,31,32,34);
                case NpcArchetype.Guard:return Mask(20,21,29,33);
                case NpcArchetype.Mage:return Mask(22,23,24,25,26,27);
                case NpcArchetype.Healer:return Mask(0,23,27);
                case NpcArchetype.Merchant:case NpcArchetype.Innkeeper:return Mask(1,2,14);
                default:return Mask(0,1,2);
            }
        }
        private static ulong Mask(params int[] values) {ulong result=0;foreach(int value in values)result|=1UL<<value;return result;}
    }
    public static class NpcSkillProgression
    {
        public const long MaximumRawExperience=1000000000000L;
        public static int Value(int baseValue,long rawExperience,int ratePermille)
        {
            if(baseValue<1 || baseValue>100 || rawExperience<0 || rawExperience>MaximumRawExperience || ratePermille<250 || ratePermille>2500)
                throw new ArgumentOutOfRangeException("Invalid skill progression input.");
            long effective=rawExperience*ratePermille/1000;
            int low=0,high=100-baseValue;
            while(low<high)
            {
                int count=low+(high-low+1)/2;
                long cost=100L*count+10L*(count*baseValue+count*(count-1)/2);
                if(cost<=effective) low=count;else high=count-1;
            }
            return baseValue+low;
        }
    }
}
