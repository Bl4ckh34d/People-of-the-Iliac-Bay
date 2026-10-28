using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Real, finite possessions backed by the exact goods flat observed at a
    // stand. Native message distinguishes stacks across the one custom template.
    public sealed class NativeMarketGoodsItem : DaggerfallUnityItem
    {
        public new const int TemplateIndex = 0x4C430100;
        public NativeMarketGoodsItem() : base(ItemGroups.MiscItems, TemplateIndex) { }
        public NativeMarketGoodsItem(int archive, int record) : this()
        {
            string name; int price;
            if (!Describe(archive, record, out name, out price)) throw new ArgumentException("Unsupported observed market goods.");
            message = archive * 1000 + record; shortName = name; value = price;
            weightInKg = archive == 213 ? 0.2f : archive == 200 ? 0.15f : 0.5f;
            WorldTextureArchive = archive; WorldTextureRecord = record;
        }
        public int GoodsArchive { get { return message / 1000; } }
        public int GoodsRecord { get { return message % 1000; } }
        public override string ItemName { get { string name; int price;return Describe(GoodsArchive, GoodsRecord, out name, out price) ? name : "Market goods"; } }
        public override string LongName { get { return ItemName; } }
        public override int InventoryTextureArchive { get { return GoodsArchive; } }
        public override int InventoryTextureRecord { get { return GoodsRecord; } }
        public override bool IsStackable() { return !IsEquipped && !IsQuestItem && !IsEnchanted; }
        public override ItemData_v1 GetSaveData()
        { var data = base.GetSaveData(); data.className = typeof(NativeMarketGoodsItem).ToString(); return data; }
        public static void Register()
        {
            var helper = DaggerfallUnity.Instance.ItemHelper; Type prior;
            if (helper.GetCustomItemClass(TemplateIndex, out prior))
            { if (prior != typeof(NativeMarketGoodsItem)) throw new InvalidOperationException("Market goods template collision."); return; }
            helper.RegisterCustomItem(TemplateIndex, ItemGroups.None, typeof(NativeMarketGoodsItem));
        }
        internal static bool Describe(int archive, int record, out string name, out int price)
        {
            name = null; price = 0;
            if (archive == 211 && record >= 8 && record <= 11) { name = "Fish"; price = 3; }
            else if (archive == 211 && (record == 41 || record == 42)) { name = "Fish fillet"; price = 4; }
            else if (archive == 211 && record == 40) { name = "Meat"; price = 5; }
            else if (archive == 211 && record == 31) { name = "Bread"; price = 2; }
            else if (archive == 213 && record == 0) { name = "Orange"; price = 1; }
            else if (archive == 213 && record == 1) { name = "Apple"; price = 1; }
            else if (archive == 200 && record >= 0 && record <= 6) { name = "Cup"; price = 4; }
            else if (archive == 205 && record >= 11 && record <= 16) { name = "Bottle"; price = 5; }
            return name != null;
        }
    }
}
