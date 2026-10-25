using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;

namespace PeopleOfTheIliacBay.DFU
{
    // Native CivilianEntity has no armor initialization. Zero means unusually
    // strong armor in DFU; ordinary unclothed body parts start at 100 instead.
    public static class NativeNpcCivilianArmor
    {
        public static void Initialize(DaggerfallEntity entity)
        {
            if(!(entity is CivilianEntity))return;
            for(int part=0;part<entity.ArmorValues.Length;part++)entity.ArmorValues[part]=100;
            for(int index=0;index<entity.Items.Count;index++)
            {
                var item=entity.Items.GetItem(index);
                if(item!=null && item.ItemGroup==ItemGroups.Armor && item.IsEquipped)
                    entity.UpdateEquippedArmorValues(item,true);
            }
        }
    }
}
