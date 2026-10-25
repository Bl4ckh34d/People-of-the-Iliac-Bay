namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Inn travelers contain a small seeded adventurer minority; ordinary households are never sampled.</summary>
    public static class TravelerAdventurerPolicy
    {
        public static NpcArchetype Archetype(uint appearanceSeed)
        {
            uint seed=StableSeed.Combine(appearanceSeed,0x6164766eu,0,0);
            if(seed%10!=0)return NpcArchetype.Citizen;
            switch(StableSeed.Combine(seed,0x636c6173u,0,0)%3)
            {
                case 0:return NpcArchetype.Warrior;
                case 1:return NpcArchetype.Mage;
                default:return NpcArchetype.Thief;
            }
        }

        public static bool IsAdventurer(NpcArchetype archetype)
        {return archetype==NpcArchetype.Warrior || archetype==NpcArchetype.Mage || archetype==NpcArchetype.Thief;}
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>Resolve generated identity before any native inventory, vitals or skill capture can pin a profile.</summary>
        public NpcArchetype ResolveNpcArchetype(string id,NpcArchetype fallback=NpcArchetype.Citizen)
        {
            var saved=IndexedNpcProgress(id);
            if(saved!=null)return saved.Archetype;
            var traveler=Traveler(id);
            return traveler==null?fallback:TravelerAdventurerPolicy.Archetype(traveler.AppearanceSeed);
        }
    }
}
