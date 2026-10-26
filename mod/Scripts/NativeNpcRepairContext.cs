using DaggerfallWorkshop.Game.Items;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeNpcRepairContext
    {
        // The item remains in native inventory as its repair receipt. Ownership
        // does not mean availability. Time may advance without an inventory edit.
        // Do not query IsBeingRepairedHere: the speaker may be outdoors, and the
        // current scene is not evidence of which shop holds this item.
        internal static string Describe(DaggerfallUnityItem item)
        {
            if (item == null || !item.RepairData.IsBeingRepaired()) return null;
            return item.RepairData.IsRepairFinished()
                ? "Owned item held at its repair shop; ready for collection, still unavailable until collected there."
                : "Owned item held at its repair shop; repair pending, unavailable until later collection there.";
        }
    }
}
