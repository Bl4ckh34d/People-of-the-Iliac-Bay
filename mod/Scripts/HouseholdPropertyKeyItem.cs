using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;

namespace LivingCities.DFU
{
    /// <summary>
    /// A non-magical key bound to one property's settlement and building identity.
    /// </summary>
    public sealed class HouseholdPropertyKeyItem : DaggerfallUnityItem
    {
        // LC marker + version slot keeps this above vanilla and away from low custom indexes.
        public new const int TemplateIndex = 0x4C430001;

        // DFU creates custom item classes through Activator during inventory restore.
        public HouseholdPropertyKeyItem() : base(ItemGroups.MiscItems, TemplateIndex) { }

        public HouseholdPropertyKeyItem(int settlementId, int buildingKey)
            : base(ItemGroups.MiscItems, TemplateIndex)
        {
            if (settlementId <= 0) throw new ArgumentOutOfRangeException(nameof(settlementId));
            if (buildingKey < 0) throw new ArgumentOutOfRangeException(nameof(buildingKey));

            // ItemData_v1 persists these native integer fields for this custom item.
            enchantmentPoints = settlementId;
            message = buildingKey;
        }

        public static void Register()
        {
            var itemHelper = DaggerfallUnity.Instance.ItemHelper;
            Type registeredType;
            if (itemHelper.GetCustomItemClass(TemplateIndex, out registeredType))
            {
                if (registeredType != typeof(HouseholdPropertyKeyItem))
                    throw new InvalidOperationException("Property key template is registered to another custom item.");
                return;
            }

            // Do not add this key to any random loot or shop-stock group.
            itemHelper.RegisterCustomItem(TemplateIndex, ItemGroups.None, typeof(HouseholdPropertyKeyItem));
        }

        public static bool IsMatching(DaggerfallUnityItem item, int settlementId, int buildingKey)
        {
            return item is HouseholdPropertyKeyItem &&
                item.ItemGroup == ItemGroups.MiscItems &&
                item.TemplateIndex == TemplateIndex &&
                settlementId > 0 &&
                buildingKey >= 0 &&
                item.enchantmentPoints == settlementId &&
                item.message == buildingKey;
        }

        public override ItemData_v1 GetSaveData()
        {
            var data = base.GetSaveData();
            data.className = typeof(HouseholdPropertyKeyItem).ToString();
            return data;
        }

        // Keys to different properties must never collapse into a stack.
        public override bool IsStackable() { return false; }
    }
}
