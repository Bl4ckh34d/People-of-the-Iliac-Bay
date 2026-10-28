using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
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
                    TavernBedSurvey.Read(interior,out markers,out occupants,out shopBedrooms);
                }
                catch(Exception error)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SHOP_BEDROOM_REPLACEMENT_SKIPPED: "+interior.EntryDoor.buildingKey+"; "+error.Message); }
            }
            BuildingSummary building;
            if(shopBedrooms!=null && directory.GetBuildingSummary(interior.EntryDoor.buildingKey,out building))
                BedroomFlatReplacement.Apply(worldStore,interior,building.BuildingType,shopBedrooms);
        }
    }
}
