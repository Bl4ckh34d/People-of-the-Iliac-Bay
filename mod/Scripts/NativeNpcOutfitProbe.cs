using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeNpcOutfitProbe
    {
        internal static string Run()
        {
            var styles=new HashSet<int>();var colors=new HashSet<int>();
            for(uint seed=1;seed<=32;seed++)
            {
                var world=new WorldStateStore(seed);
                var family=world.GetOrCreate(725,new[]{10},1).Households[0];
                foreach(var resident in family.Residents)
                {
                    if(resident.AgeGroup==ResidentAgeGroup.Child)continue;
                    var body=new GameObject("Isolated outfit probe");
                    try
                    {
                        var entity=new CivilianEntity(null);
                        var owner=body.AddComponent<NativeNpcInventoryOwner>();owner.Bind(entity,world,resident);
                        var top=entity.ItemEquipTable.GetItem(EquipSlots.ChestClothes);
                        Require(top!=null,"Every resident needs an equipped top, dress or robe.");
                        Require(entity.ItemEquipTable.GetItem(EquipSlots.Feet)!=null,"Every resident needs equipped native footwear.");
                        if(resident.Sex==ResidentSex.Male)
                            Require(entity.ItemEquipTable.GetItem(EquipSlots.LegsClothes)!=null,"Male outfit is missing trousers.");
                        styles.Add(top.TemplateIndex);colors.Add((int)top.dyeColor);
                        for(int i=0;i<entity.Items.Count;i++)
                        {
                            var item=entity.Items.GetItem(i);
                            if(item.ItemGroup!=ItemGroups.MensClothing && item.ItemGroup!=ItemGroups.WomensClothing)continue;
                            Require(item.IsEquipped,"An initial garment was not equipped and could leak into trade.");
                        }
                    }
                    finally {UnityEngine.Object.DestroyImmediate(body);}
                }
            }
            Require(styles.Count>=4 && colors.Count>=5,"Native outfits lack item/color variation.");
            return "PEOPLE_OF_THE_ILIAC_BAY_OUTFIT_PASS: complete-native-outfits; equipped=true; styles="+styles.Count+"; colors="+colors.Count;
        }
        private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    }
}
