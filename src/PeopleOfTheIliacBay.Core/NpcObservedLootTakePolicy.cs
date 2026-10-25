namespace PeopleOfTheIliacBay.Core
{
    // Runtime fills this from one loaded DaggerfallLoot plus its current claim.
    // It is a validation boundary, not an inventory or search system.
    public sealed class NpcObservedLootTake
    {
        public string ContainerId;
        public string ItemId;
        public int Quantity;
        public bool Active;
        public bool UniqueContainer;
        public bool ExactItemAvailable;
        public bool ItemQuestProtected;
        public bool ContainerQuestProtected;
        public bool OwnerAuthorized;
        public bool LawfulDefenseClaim;
        public bool ObservedLocally;
    }

    public static class NpcObservedLootTakePolicy
    {
        public static bool Allows(NpcObservedLootTake source)
        {
            return source!=null && Id(source.ContainerId) && Id(source.ItemId) && source.Quantity>0 &&
                source.Active && source.UniqueContainer && source.ExactItemAvailable && source.ObservedLocally &&
                !source.ItemQuestProtected && !source.ContainerQuestProtected &&
                (source.OwnerAuthorized || source.LawfulDefenseClaim);
        }

        private static bool Id(string value) {return !string.IsNullOrWhiteSpace(value) && value.Length<=240;}
    }
}
