namespace PeopleOfTheIliacBay.Core
{
    public static class GuardArchery
    {
        public static void EquipWallWatch(GuardRecord guard)
        {
            if (!guard.HasBow) guard.ArrowsRemaining=24;
            guard.HasBow=true; guard.ArcheryAssigned=true;
        }
        public static void EnsureLoadout(GuardRecord guard)
        {
            if(guard.ArcheryAssigned) return;
            guard.HasBow=guard.AppearanceSeed%5==0;
            guard.ArrowsRemaining=guard.HasBow ? 24 : 0;
            guard.ArcheryAssigned=true;
        }
    }
}
