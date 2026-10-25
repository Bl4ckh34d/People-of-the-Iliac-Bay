using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class NativeNpcMedicalService
    {
        internal BuildingSummary Building;
        internal bool DiseaseCure,Potions;

        // Read the same RMB people/service factions DFU uses to construct this interior.
        // A generic guild hall is not evidence that it sells antidotes.
        internal static List<NativeNpcMedicalService> Survey(BuildingDirectory directory)
        {
            var result=new List<NativeNpcMedicalService>();
            var reader=DaggerfallUnity.Instance.ContentReader;var location=directory.LocationData;
            var method=reader.MapFileReader.GetType().GetMethod("GetRmbBlockName");
            var blocks=new Dictionary<string,DFBlock>();
            // DFU also places guild services (notably Brotherhood) in House2.
            // Still require an actual native service NPC; an ordinary house is no clinic.
            foreach(var type in new[] {DFLocation.BuildingTypes.Temple,DFLocation.BuildingTypes.GuildHall,DFLocation.BuildingTypes.House2})
            foreach(var building in directory.GetBuildingsOfType(type))
            {
                int x,y,record;BuildingDirectory.ReverseBuildingKey(building.buildingKey,out x,out y,out record);
                string name=reader.BlockFileReader.CheckName((string)method.Invoke(reader.MapFileReader,new object[] {location,x,y}));
                DFBlock block;if(!blocks.TryGetValue(name,out block)) {reader.GetBlock(name,out block);blocks.Add(name,block);}
                if(block.RmbBlock.SubRecords==null || record<0 || record>=block.RmbBlock.SubRecords.Length)continue;
                var people=block.RmbBlock.SubRecords[record].Interior.BlockPeopleRecords;if(people==null)continue;
                var service=new NativeNpcMedicalService {Building=building};
                foreach(var person in people)
                {
                    if(!Services.HasGuildService(person.FactionID))continue;
                    var kind=Services.GetService((GuildNpcServices)person.FactionID);
                    service.DiseaseCure|=kind==GuildServices.CureDisease;service.Potions|=kind==GuildServices.BuyPotions;
                }
                if(service.DiseaseCure || service.Potions)result.Add(service);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEDICAL_PROVIDERS: map="+directory.MapID+
                "; disease="+result.FindAll(service=>service.DiseaseCure).Count+"; potions="+result.FindAll(service=>service.Potions).Count);
            return result;
        }

        internal bool TryQuote(WorldStateStore store,ResidentActor actor,string key,int recipe,out int price)
        {
            price=0;
            // House2 guilds have no public opening-hour table. The existing NPC
            // membership exception permits their service; never index that table with a house type.
            if(Building.BuildingType!=DFLocation.BuildingTypes.House2 && !PlayerActivate.IsBuildingOpen(Building.BuildingType))return false;
            int cost;
            if(recipe<0)
            {
                var manager=actor.GetComponent<EntityEffectManager>();
                if(!DiseaseCure || key!=CureDisease.EffectKey || !manager || manager.DiseaseCount<1)return false;
                uint minutes=DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime();
                int holiday=FormulaHelper.GetHolidayId(minutes,GameManager.Instance.PlayerGPS.CurrentRegionIndex);
                if(holiday==(int)DFLocation.Holidays.South_Winds_Prayer || holiday==(int)DFLocation.Holidays.First_Harvest ||
                    holiday==(int)DFLocation.Holidays.Second_Harvest)return true;
                cost=FormulaHelper.CalculateCost(250*manager.DiseaseCount,Building.Quality);
                if(holiday==(int)DFLocation.Holidays.North_Winds_Festival)cost/=2;
            }
            else
            {
                if(!Potions || RecipeFor(key)!=recipe)return false;
                cost=FormulaHelper.CalculateCost(ItemBuilder.CreatePotion(recipe).value,Building.Quality);
            }
            return NpcShopCommerce.TryPriceForBuyer(store,actor.Resident.Id,cost,1,Building.Quality,false,out price);
        }

        internal static int RecipeFor(string key)
        {
            var broker=GameManager.Instance.EntityEffectBroker;
            foreach(int id in broker.GetPotionRecipeKeys())
            {
                var recipe=broker.GetPotionRecipe(id);
                if(recipe==null || recipe.SecondaryEffects!=null && recipe.SecondaryEffects.Count>0)continue;
                var effect=broker.GetPotionRecipeEffect(recipe);
                if(effect!=null && effect.Key==key)return id;
            }
            return -1;
        }

        internal bool Treat(WorldStateStore store,ResidentActor actor,NpcTreatmentVisit visit)
        {
            var condition=actor.GetComponent<EntityEffectManager>();
            if(condition && (visit.EffectKey==CurePoison.EffectKey?condition.PoisonCount:condition.DiseaseCount)==0)return true;
            int price;if(!TryQuote(store,actor,visit.EffectKey,visit.RecipeKey,out price))return false;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();var manager=actor.GetComponent<EntityEffectManager>();
            if(!owner || owner.Binding==null || !manager)return false;
            owner.Capture();var inventory=store.ReadNpcInventory(actor.Resident.Id);
            if(inventory.WalletGold<price)return false;
            var before=manager.GetInstancedBundlesSaveData();
            try
            {
                if(visit.RecipeKey<0)manager.CureAllDiseases();
                else
                {
                    // Native potion service offers recipes, not a keeper inventory.
                    // NPC membership exception; buy one actual potion and use it immediately.
                    var potion=ItemBuilder.CreatePotion(visit.RecipeKey);
                    owner.Binding.Items.AddItem(potion,noStack:true);
                    try {manager.DrinkPotion(potion);} finally {owner.Binding.Items.RemoveItem(potion);}
                }
                if(!store.CommitNpcInventoryTransaction("medical:"+actor.Resident.Id+":"+inventory.Revision,
                    new[] {new NpcInventoryUpdate {PersonId=actor.Resident.Id,ExpectedRevision=inventory.Revision,
                        WalletGold=inventory.WalletGold-price,Items=NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items)}}))
                    throw new InvalidOperationException("The NPC medical payment changed.");
                owner.Binding.Revision++;
            }
            catch {manager.RestoreInstancedBundleSaveData(before);throw;}
            var binding=actor.GetComponent<NativeNpcConditionBinding>();if(binding)binding.Capture();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEDICAL_SERVICE: resident="+actor.Resident.Id+"; building="+Building.buildingKey+
                "; effect="+visit.EffectKey+"; recipe="+visit.RecipeKey+"; price="+price+
                "; poison="+manager.PoisonCount+"; disease="+manager.DiseaseCount);
            return true;
        }
    }
}
