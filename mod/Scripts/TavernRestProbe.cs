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
    public static class TavernRestProbe
    {
        public static void Run(DaggerfallInterior interior,int town)
        {
            var host=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>(); var game=GameManager.Instance;
            var before=(WorldState)host.GetSaveData(); var receipts=new List<RoomRental_v1>(game.PlayerEntity.RentedRooms);
            var position=game.PlayerMotor.transform.position;
            int building=interior.EntryDoor.buildingKey; string scene=DaggerfallInterior.GetSceneName(town,building);
            bool permanent=SaveLoadManager.StateManager.ContainsPermanentScene(scene);
            try
            {
                Dictionary<int,string> markers; var rooms=TavernRoomSurvey.Read(interior,out markers);
                rooms=rooms.FindAll(r=>new HashSet<string>(markers.Values).Contains(r.Id));
                var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
                store.RegisterLodging(town,building,rooms);
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                store.TryBookLodging(town,building,"rest-guest","guest",town+1,false,1,now,now+86400,markers[0]);
                game.PlayerEntity.RentedRooms.RemoveAll(r=>r.mapID==town && r.buildingKey==building);
                var receipt=new RoomRental_v1 { mapID=town,buildingKey=building,allocatedBedIndex=0,expiryTime=(ulong)now+86400 };
                game.PlayerEntity.RentedRooms.Add(receipt); SaveLoadManager.StateManager.AddPermanentScene(scene);
                if(CanRest(false)) throw new InvalidOperationException("Rest accepted native receipt pointing at another guest's room.");
                DaggerfallUI.Instance.PopToHUD();
                receipt.allocatedBedIndex=1;
                if(!CanRest(false)) throw new InvalidOperationException("Rest blocked valid unoccupied player room.");
                DaggerfallUI.Instance.PopToHUD();
                var guestRoom=rooms.Find(r=>r.Id==markers[0]);
                int bedId=int.Parse(guestRoom.Beds[0].Substring(4));
                var bed=BedGeometry.Read(interior).Find(b=>b.RecordIndex==bedId);
                game.PlayerMotor.transform.position=interior.transform.TransformPoint(bed.LocalCenter)+Vector3.up;
                if(CanRest(true)) throw new InvalidOperationException("Rest-here bypassed occupied physical bed.");
                DaggerfallUI.Instance.PopToHUD();
                if(!CanRest(false)) throw new InvalidOperationException("Allocated own bed blocked because player stands at another bed.");
                DaggerfallUI.Instance.PopToHUD();
                game.PlayerMotor.transform.position+=Vector3.up*4;
                if(!CanRest(true)) throw new InvalidOperationException("Occupied bed blocked rest on a different storey.");
                DaggerfallUI.Instance.PopToHUD();
                receipt.allocatedBedIndex=999;
                if(CanRest(false)) throw new InvalidOperationException("Invalid paid marker silently fell back to another bed.");
                DaggerfallUI.Instance.PopToHUD();
                Debug.Log("LIVING_CITIES_TAVERN_REST_PASS: building="+building+"; conflicting-target/rest-here/invalid-marker rejected; own allocated room/different-storey allowed.");
            }
            finally
            {
                game.PlayerEntity.RentedRooms.Clear(); game.PlayerEntity.RentedRooms.AddRange(receipts);
                game.PlayerMotor.transform.position=position;
                if(!permanent) SaveLoadManager.StateManager.RemovePermanentScene(scene);
                host.RestoreSaveData(before); DaggerfallUI.Instance.PopToHUD();
            }
        }
        private static bool CanRest(bool here)
        {
            var window=new DaggerfallRestWindow(DaggerfallUI.UIManager,here);
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(DaggerfallRestWindow).GetField("playerEntity",flags).SetValue(window,GameManager.Instance.PlayerEntity);
            return (bool)typeof(DaggerfallRestWindow).GetMethod("CanRest",flags).Invoke(window,new object[] { false });
        }
    }
}
