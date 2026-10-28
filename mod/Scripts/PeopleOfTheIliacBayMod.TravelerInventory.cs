using DaggerfallWorkshop.Game;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        internal bool HasPendingInnInventory(BuildingDirectory directory)
        {return nativeRentals.HasPendingExteriorInventory(directory);}
        internal int CompletedInnInventoryCount(BuildingDirectory directory)
        {return nativeRentals.CompletedExteriorInventoryCount(directory);}
    }
}
