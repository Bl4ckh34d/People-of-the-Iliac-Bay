namespace PeopleOfTheIliacBay.Core
{
    public static class NpcVitalsBindingPolicy
    {
        public static bool Eligible(bool alive,bool adult,bool questProtected)
        { return alive && adult && !questProtected; }

        // No scene or visitation role is authoritative. Keep existing profile
        // identity; otherwise use explicit generic-citizen fallback.
        public static NpcArchetype ResolveArchetype(bool hasPinnedProfile,NpcArchetype pinned)
        { return hasPinnedProfile ? pinned : NpcArchetype.Citizen; }
    }
}
