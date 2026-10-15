using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Observe existing native receipts. New payments remain native until atomic vacancy hooks exist.
    public sealed class NativeTavernRentals
    {
        private DaggerfallInterior surveyed;
        private List<LodgingRoom> rooms;
        private Dictionary<int,string> markers;
        private string lastFailure;

        public void Refresh(WorldStateStore store,BuildingDirectory directory)
        {
            var game=GameManager.Instance;
            var interior=game.IsPlayerInside ? game.PlayerEnterExit.Interior : null;
            if(surveyed!=interior) { surveyed=interior; rooms=null; markers=null; lastFailure=null; }
            if(!interior) return;
            BuildingSummary summary;
            int building=interior.EntryDoor.buildingKey;
            if(!directory.GetBuildingSummary(building,out summary) || summary.BuildingType!=DFLocation.BuildingTypes.Tavern) return;
            var receipt=game.PlayerEntity.GetRentedRoom(directory.MapID,building);
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            if(receipt==null || receipt.expiryTime<=now) return;
            try
            {
                if(rooms==null)
                {
                    Dictionary<int,string> mapping;
                    var all=TavernRoomSurvey.Read(interior,out mapping);
                    var eligible=new HashSet<string>(mapping.Values);
                    rooms=all.FindAll(r=>eligible.Contains(r.Id)); markers=mapping;
                }
                string allocated;
                if(!markers.TryGetValue(receipt.allocatedBedIndex,out allocated))
                    throw new InvalidOperationException("Native allocated bed marker has no surveyed guest room.");
                store.RegisterLodging(directory.MapID,building,rooms);
                if(!store.ReconcilePlayerLodging(directory.MapID,building,allocated,now,receipt.expiryTime))
                    throw new InvalidOperationException("Native paid rental conflicts with saved lodging; receipt and existing bookings retained.");
                lastFailure=null;
            }
            catch(Exception error)
            {
                string failure=directory.MapID+":"+building+": "+error.Message;
                if(lastFailure!=failure) Debug.LogWarning("LIVING_CITIES_RENTAL_CONFLICT: "+failure);
                lastFailure=failure;
            }
        }
    }
}
