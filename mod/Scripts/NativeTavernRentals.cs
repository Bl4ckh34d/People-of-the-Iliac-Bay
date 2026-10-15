using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class NativeTavernRentals : IDisposable
    {
        private DaggerfallInterior surveyed;
        private List<LodgingRoom> rooms;
        private Dictionary<int,string> markers;
        private string lastFailure;
        private Func<WorldStateStore> getStore;
        private Func<bool> available;
        private IDisposable hooks;

        public void Install(Func<WorldStateStore> store,Func<bool> enabled)
        {
            getStore=store; available=enabled;
            var type=Type.GetType("LivingCities.QuestHooks.NativeTavernRentalHooks, LivingCities.QuestHooks",true);
            hooks=(IDisposable)Activator.CreateInstance(type,new object[] { new Func<int,int,int,Action,bool>(Rent) });
        }
        public void Dispose() { if(hooks!=null) hooks.Dispose(); hooks=null; }

        private void Prepare(WorldStateStore store,BuildingDirectory directory,DaggerfallInterior interior)
        {
            if(surveyed!=interior) { surveyed=interior; rooms=null; markers=null; lastFailure=null; }
            if(rooms==null)
            {
                Dictionary<int,string> mapping;
                var all=TavernRoomSurvey.Read(interior,out mapping);
                var eligible=new HashSet<string>(mapping.Values);
                rooms=all.FindAll(r=>eligible.Contains(r.Id)); markers=mapping;
            }
            store.RegisterLodging(directory.MapID,interior.EntryDoor.buildingKey,rooms);
        }

        private bool Rent(int building,int days,int price,Action close)
        {
            if(available==null || !available()) return false;
            var game=GameManager.Instance; var interior=game.PlayerEnterExit.Interior;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory(); BuildingSummary summary;
            if(!game.IsPlayerInside || !interior || !directory || interior.EntryDoor.buildingKey!=building ||
                !directory.GetBuildingSummary(building,out summary) || summary.BuildingType!=DFLocation.BuildingTypes.Tavern) return false;
            var store=getStore(); int town=directory.MapID;
            if(!store.Snapshot().Settlements.Exists(s=>s.SettlementId==town)) return false;
            try { Prepare(store,directory,interior); }
            catch(Exception error)
            {
                // An unsupported layout without managed reservations remains vanilla.
                // Once bookings exist, falling through could sell another guest's room.
                bool managed=store.Snapshot().Lodgings.Exists(t=>t.SettlementId==town && t.BuildingKey==building);
                Debug.LogWarning("LIVING_CITIES_RENTAL_SURVEY: "+town+":"+building+": "+error.Message);
                if(!managed) return false;
                close(); DaggerfallUI.MessageBox("The keeper cannot confirm rooms in this changed building."); return true;
            }
            close();
            if(days<1 || days>350 || price<0) { DaggerfallUI.MessageBox("That stay cannot be booked."); return true; }
            var player=game.PlayerEntity;
            if(player.GetGoldAmount()<price) { DaggerfallUI.MessageBox(454); return true; }
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var receipt=player.GetRentedRoom(town,building);
            string room=null;
            try
            {
                if(receipt!=null)
                {
                    if(receipt.expiryTime<=now || !markers.TryGetValue(receipt.allocatedBedIndex,out room) ||
                        !store.ReconcilePlayerLodging(town,building,room,now,receipt.expiryTime))
                        throw new InvalidOperationException("Existing paid room needs reconciliation.");
                }
                double end=(receipt==null ? now : receipt.expiryTime)+days*86400d;
                if(end-now>351*86400d) throw new InvalidOperationException("Rental duration exceeds native limit.");
                if(receipt==null) room=store.AvailableLodgingRoom(town,building,1,now,end);
                if(room==null || !store.ReconcilePlayerLodging(town,building,room,now,end))
                {
                    var next=store.EarliestLodgingStart(town,building,1,now,days*86400d);
                    string message=receipt==null ? "No room is available for that stay." : "Your room cannot be extended for that stay.";
                    if(next.HasValue)
                    {
                        var date=new DaggerfallDateTime(); date.FromSeconds((ulong)Math.Ceiling(next.Value));
                        message+=" A room is available from "+date.DateTimeString()+".";
                    }
                    DaggerfallUI.MessageBox(message); return true;
                }
                if(receipt==null)
                {
                    int marker=-1; foreach(var pair in markers) if(pair.Value==room && (marker<0 || pair.Key<marker)) marker=pair.Key;
                    receipt=new RoomRental_v1 { name=game.PlayerEnterExit.BuildingDiscoveryData.displayName,mapID=town,
                        buildingKey=building,allocatedBedIndex=marker,expiryTime=(ulong)end };
                    player.RentedRooms.Add(receipt);
                    SaveLoadManager.StateManager.AddPermanentScene(DaggerfallInterior.GetSceneName(town,building));
                }
                else receipt.expiryTime=(ulong)end;
                player.DeductGoldAmount(price);
                return true;
            }
            catch(Exception error)
            {
                Debug.LogWarning("LIVING_CITIES_RENTAL_REFUSED: "+town+":"+building+": "+error.Message);
                DaggerfallUI.MessageBox("The keeper cannot confirm that room. Please try again later."); return true;
            }
        }

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
                Prepare(store,directory,interior);
                string allocated;
                if(!markers.TryGetValue(receipt.allocatedBedIndex,out allocated))
                    throw new InvalidOperationException("Native allocated bed marker has no surveyed guest room.");
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
