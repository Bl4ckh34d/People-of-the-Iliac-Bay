namespace PeopleOfTheIliacBay.DFU
{
    // The shared-visit owner refreshes both leases before advancing a member.
    // Physical movement still uses each seat's exact collision permission.
    public sealed class SelectedSeatPairAccess
    {
        public readonly SelectedSeatAccess First,Second;
        private readonly FurnitureSeatCatalog catalog;
        internal SelectedSeatPairAccess(FurnitureSeatCatalog catalog,SelectedSeatAccess first,SelectedSeatAccess second)
        {this.catalog=catalog;First=first;Second=second;}
        public bool Refresh()
        {return catalog && catalog.TryClaimPair(First.Owner,First.Slot.Id,Second.Owner,Second.Slot.Id);}
        public void Release(){First.Release();Second.Release();}
    }
}
