using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private DaggerfallInterior shopBedroomRoom;
        private Dictionary<int,string> shopBedrooms;
        private void ReplaceShopBedrooms()
        {
            if(shopBedroomRoom!=interior)
            {
                shopBedroomRoom=interior;shopBedrooms=null;
                try
                {
                    Dictionary<int,string> markers,occupants;
                    TavernRoomSurvey.Read(interior,out markers,out occupants,out shopBedrooms);
                }
                catch(Exception error)
                { Debug.LogWarning("LIVING_CITIES_SHOP_BEDROOM_REPLACEMENT_SKIPPED: "+interior.EntryDoor.buildingKey+"; "+error.Message); }
            }
            BuildingSummary building;
            if(shopBedrooms!=null && directory.GetBuildingSummary(interior.EntryDoor.buildingKey,out building))
                BedroomFlatReplacement.Apply(worldStore,interior,building.BuildingType,shopBedrooms);
        }
    }
}
