using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class TavernPaymentProbe
    {
        public static void Run(DaggerfallInterior interior,int town)
        {
            var host=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var player=GameManager.Instance.PlayerEntity;
            var before=(WorldState)host.GetSaveData(); int gold=player.GoldPieces;
            var receipts=new List<RoomRental_v1>(player.RentedRooms);
            int building=interior.EntryDoor.buildingKey;
            string scene=DaggerfallInterior.GetSceneName(town,building);
            bool permanent=SaveLoadManager.StateManager.ContainsPermanentScene(scene);
            try
            {
                player.RentedRooms.RemoveAll(r=>r.mapID==town && r.buildingKey==building);
                player.GoldPieces=5000;
                Dictionary<int,string> markers; var all=TavernRoomSurvey.Read(interior,out markers);
                foreach(var room in all) room.NonRentable=!new HashSet<string>(markers.Values).Contains(room.Id);
                var rooms=all.FindAll(r=>new HashSet<string>(markers.Values).Contains(r.Id));
                var store=Store(host); store.RegisterLodging(town,building,all);
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                foreach(var room in rooms) store.TryBookLodging(town,building,"full:"+room.Id,"guest:"+room.Id,town+1,false,1,now,now+172800,room.Id);
                Confirm(1,5);
                if(player.GoldPieces!=5000 || player.GetRentedRoom(town,building)!=null)
                    throw new InvalidOperationException("Full tavern charged gold or allocated a room.");
                DaggerfallUI.Instance.PopToHUD();
                Confirm(1,0,true);
                if(player.GoldPieces!=5000 || player.GetRentedRoom(town,building)!=null)
                    throw new InvalidOperationException("Free rental bypassed full tavern capacity.");
                DaggerfallUI.Instance.PopToHUD();
                host.RestoreSaveData(before); store=Store(host); store.RegisterLodging(town,building,all);
                for(int i=0;i<rooms.Count-1;i++) store.TryBookLodging(town,building,"held:"+rooms[i].Id,"guest:"+i,town+1,false,1,now,now+172800,rooms[i].Id);
                Confirm(1,player.GetGoldAmount()+1);
                if(player.GoldPieces!=5000 || player.GetRentedRoom(town,building)!=null)
                    throw new InvalidOperationException("Insufficient funds created rental or charged gold.");
                DaggerfallUI.Instance.PopToHUD(); Confirm(1,5,false,false);
                if(player.GoldPieces!=5000 || player.GetRentedRoom(town,building)!=null)
                    throw new InvalidOperationException("Declined offer created rental or charged gold.");
                Confirm(1,5);
                var receipt=player.GetRentedRoom(town,building);
                if(receipt==null || player.GoldPieces!=4995 || markers[receipt.allocatedBedIndex]!=rooms[rooms.Count-1].Id)
                    throw new InvalidOperationException("Paid confirmation did not select sole available room with exact price.");
                var booked=store.Snapshot().Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==building).Bookings.Find(b=>b.Player);
                if(booked==null || booked.EndsAt!=receipt.expiryTime) throw new InvalidOperationException("Paid receipt not committed to ledger.");
                int marker=receipt.allocatedBedIndex; ulong originalExpiry=receipt.expiryTime;
                Confirm(1,5);
                if(receipt.expiryTime!=originalExpiry+86400 || receipt.allocatedBedIndex!=marker || player.GoldPieces!=4990)
                    throw new InvalidOperationException("Valid extension failed exact room price or expiry.");
                store.TryBookLodging(town,building,"next","next",town+1,false,1,receipt.expiryTime,receipt.expiryTime+172800,booked.RoomId);
                ulong expiry=receipt.expiryTime; Confirm(1,5);
                if(receipt.expiryTime!=expiry || player.GoldPieces!=4990) throw new InvalidOperationException("Conflicting extension charged or displaced next guest.");
                DaggerfallUI.Instance.PopToHUD();
                host.RestoreSaveData(before); player.RentedRooms.RemoveAll(r=>r.mapID==town && r.buildingKey==building);
                Confirm(1,0,true);
                if(player.GetRentedRoom(town,building)==null || player.GoldPieces!=4990)
                    throw new InvalidOperationException("Available free room was not granted without charge.");
                Debug.Log("LIVING_CITIES_RENTAL_PAYMENT_PASS: building="+building+"; full/free-full/declined/insufficient=no-charge; sole-vacancy/extension=exact-price; conflicting-extension=no-charge; free-vacancy=granted.");
            }
            finally
            {
                player.GoldPieces=gold; player.RentedRooms.Clear(); player.RentedRooms.AddRange(receipts);
                if(!permanent) SaveLoadManager.StateManager.RemovePermanentScene(scene);
                host.RestoreSaveData(before); DaggerfallUI.Instance.PopToHUD();
            }
        }
        private static WorldStateStore Store(LivingCitiesMod host)
        { return (WorldStateStore)typeof(LivingCitiesMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host); }
        private static void Confirm(int days,int price,bool free=false,bool accept=true)
        {
            var type=typeof(DaggerfallTavernWindow); var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var window=new DaggerfallTavernWindow(DaggerfallUI.UIManager,null);
            type.GetField("daysToRent",flags).SetValue(window,days); type.GetField("tradePrice",flags).SetValue(window,price);
            var game=GameManager.Instance;
            type.GetField("rentedRoom",flags).SetValue(window,game.PlayerEntity.GetRentedRoom(game.PlayerGPS.CurrentLocation.MapTableData.MapId,game.PlayerEnterExit.Interior.EntryDoor.buildingKey));
            if(free) type.GetMethod("RentRoom",flags).Invoke(window,null);
            else type.GetMethod("ConfirmRenting_OnButtonClick",flags).Invoke(window,new object[] { null,
                accept ? DaggerfallMessageBox.MessageBoxButtons.Yes : DaggerfallMessageBox.MessageBoxButtons.No });
        }
    }
}
