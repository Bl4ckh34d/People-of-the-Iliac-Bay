using System;
using System.Collections;
using System.Collections.Generic;
using LivingCities.Core;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class TavernSurveyProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            if(game.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var taverns=directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern);
            if(taverns.Count==0) throw new InvalidOperationException("No taverns to survey.");
            int totalBeds=0;
            foreach(var tavern in taverns)
            {
                DaggerfallStaticDoors owner=null; StaticDoor entry=default(StaticDoor);
                foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var door in candidate.Doors)
                        if(door.doorType==DoorTypes.Building && door.buildingKey==tavern.buildingKey)
                        { owner=candidate; entry=door; break; }
                if(!owner) throw new InvalidOperationException("Tavern entrance missing.");
                TownSmokeHarness.EnterInterior(owner.transform,entry);
                yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
                var room=game.PlayerEnterExit.Interior;
                if(!room || room.EntryDoor.buildingKey!=tavern.buildingKey) throw new InvalidOperationException("Wrong tavern loaded.");
                var beds=BedGeometry.Read(room); totalBeds+=beds.Count;
                var rest=room.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest);
                for(int index=0;index<rest.Length;index++)
                    Debug.Log("LIVING_CITIES_TAVERN_REST: building="+tavern.buildingKey+"; index="+index+
                        "; position="+room.transform.InverseTransformPoint(rest[index]).ToString("F3"));
                var navigation=new InteriorGeometry(room); int reachable=0;
                var bedIds=new HashSet<int>();
                foreach(var bed in beds)
                {
                    if(!bedIds.Add(bed.RecordIndex)) throw new InvalidOperationException("Duplicate tavern bed identity.");
                    Vector3 ground; bool access=navigation.StandBesideBed(bed,out ground);
                    bool route=access && navigation.RouteToExit(ground).Count>=2; if(route) reachable++;
                    Debug.Log("LIVING_CITIES_TAVERN_BED: building="+tavern.buildingKey+"; bed="+bed.RecordIndex+
                        "; position="+bed.LocalCenter.ToString("F3")+"; accessible="+route);
                }
                var doors=room.GetComponentsInChildren<DaggerfallActionDoor>();
                VerifyRooms(room,beds,doors);
                yield return VerifyUnrentedInventory(room,directory.MapID);
                yield return VerifyNativeRental(room,directory.MapID);
                TavernPaymentProbe.Run(room,directory.MapID);
                TavernRestProbe.Run(room,directory.MapID);
                foreach(var door in doors)
                    Debug.Log("LIVING_CITIES_TAVERN_DOOR: building="+tavern.buildingKey+"; id="+door.LoadID+
                        "; position="+room.transform.InverseTransformPoint(door.transform.position).ToString("F3")+
                        "; closedRotation="+door.ClosedRotation.eulerAngles.ToString("F3"));
                foreach(var npc in room.GetComponentsInChildren<StaticNPC>())
                    Debug.Log("LIVING_CITIES_TAVERN_NPC: building="+tavern.buildingKey+"; hash="+npc.Data.hash+
                        "; faction="+npc.Data.factionID+"; position="+room.transform.InverseTransformPoint(npc.transform.position).ToString("F3"));
                Debug.Log("LIVING_CITIES_TAVERN_SURVEY: building="+tavern.buildingKey+"; beds="+beds.Count+
                    "; reachableBeds="+reachable+"; actionDoors="+doors.Length);
                game.PlayerEnterExit.TransitionExterior(); yield return new WaitForSecondsRealtime(1);
            }
            Debug.Log("LIVING_CITIES_TAVERN_SURVEY_PASS: taverns="+taverns.Count+"; beds="+totalBeds+"; native interior census complete.");
        }
        private static IEnumerator VerifyUnrentedInventory(DaggerfallInterior interior,int town)
        {
            var player=GameManager.Instance.PlayerEntity;
            var host=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var priorLodgings=((WorldState)host.GetSaveData()).Lodgings;
            var receipts=new List<DaggerfallWorkshop.Game.Serialization.RoomRental_v1>(player.RentedRooms);
            int building=interior.EntryDoor.buildingKey,gold=player.GetGoldAmount();
            try
            {
                player.RentedRooms.RemoveAll(r=>r.mapID==town && r.buildingKey==building);
                var clean=(WorldState)host.GetSaveData();
                clean.Lodgings.RemoveAll(t=>t.SettlementId==town && t.BuildingKey==building);
                host.RestoreSaveData(clean);
                TavernLodging lodging=null; float deadline=Time.realtimeSinceStartup+15;
                while(Time.realtimeSinceStartup<deadline)
                {
                    lodging=((WorldState)host.GetSaveData()).Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==building);
                    if(lodging!=null) break;
                    yield return null;
                }
                Dictionary<int,string> mapping; TavernRoomSurvey.Read(interior,out mapping);
                if(lodging==null || lodging.Rooms.Count!=new HashSet<string>(mapping.Values).Count || lodging.Bookings.Count!=0 ||
                    player.GetGoldAmount()!=gold || player.GetRentedRoom(town,building)!=null)
                    throw new InvalidOperationException("Unrented tavern did not register guest inventory without payment or bookings.");
                Debug.Log("LIVING_CITIES_UNRENTED_INVENTORY_PASS: building="+building+"; guestRooms="+lodging.Rooms.Count+"; no receipt, booking or charge.");
            }
            finally
            {
                player.RentedRooms.Clear(); player.RentedRooms.AddRange(receipts);
                var restored=(WorldState)host.GetSaveData(); restored.Lodgings=priorLodgings; host.RestoreSaveData(restored);
            }
        }
        private static IEnumerator VerifyNativeRental(DaggerfallInterior interior,int town)
        {
            var player=GameManager.Instance.PlayerEntity;
            var host=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var priorLodgings=((WorldState)host.GetSaveData()).Lodgings;
            var prior=new List<DaggerfallWorkshop.Game.Serialization.RoomRental_v1>(player.RentedRooms);
            var receipt=new DaggerfallWorkshop.Game.Serialization.RoomRental_v1 {
                name="Living Cities rental probe",mapID=town,buildingKey=interior.EntryDoor.buildingKey,
                allocatedBedIndex=1,expiryTime=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()+86400 };
            int gold=player.GetGoldAmount(); ulong expiry=receipt.expiryTime;
            try
            {
                player.RentedRooms.RemoveAll(r=>r.mapID==town && r.buildingKey==receipt.buildingKey);
                player.RentedRooms.Add(receipt);
                TavernLodging lodging=null; float deadline=Time.realtimeSinceStartup+15;
                while(Time.realtimeSinceStartup<deadline)
                {
                    lodging=((WorldState)host.GetSaveData()).Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==receipt.buildingKey);
                    if(lodging!=null && lodging.Bookings.Exists(b=>b.Player && b.EndsAt==expiry)) break;
                    yield return null;
                }
                if(lodging==null || lodging.Bookings.Count!=1 || lodging.Bookings[0].EndsAt!=expiry)
                    throw new InvalidOperationException("Native paid rental was not imported by running controller.");
                Dictionary<int,string> mapping; TavernRoomSurvey.Read(interior,out mapping);
                var booking=lodging.Bookings[0];
                if(booking.RoomId!=mapping[1] || receipt.allocatedBedIndex!=1 || receipt.expiryTime!=expiry || player.GetGoldAmount()!=gold)
                    throw new InvalidOperationException("Native rental import changed allocation or payment.");
                receipt.expiryTime+=86400; deadline=Time.realtimeSinceStartup+15;
                while(Time.realtimeSinceStartup<deadline)
                {
                    lodging=((WorldState)host.GetSaveData()).Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==receipt.buildingKey);
                    if(lodging.Bookings[0].EndsAt==receipt.expiryTime) break;
                    yield return null;
                }
                if(lodging.Bookings.Count!=1 || lodging.Bookings[0].Id!=booking.Id || lodging.Bookings[0].StartsAt!=booking.StartsAt ||
                    lodging.Bookings[0].EndsAt!=receipt.expiryTime || player.GetGoldAmount()!=gold)
                    throw new InvalidOperationException("Native rental extension duplicated or charged booking.");
                Debug.Log("LIVING_CITIES_NATIVE_RENTAL_PASS: building="+receipt.buildingKey+"; room="+booking.RoomId+"; paid marker preserved, repeat/extension no charge.");
            }
            finally
            {
                player.RentedRooms.Clear(); player.RentedRooms.AddRange(prior);
                var current=(WorldState)host.GetSaveData(); current.Lodgings=priorLodgings;
                host.RestoreSaveData(current);
            }
        }
        private static void VerifyRooms(DaggerfallInterior interior,List<BedAnchor> beds,DaggerfallActionDoor[] doors)
        {
            var original=new List<bool>(); foreach(var door in doors) original.Add(door.IsOpen);
            try
            {
                foreach(var door in doors) door.SetOpen(false,true,true); Physics.SyncTransforms();
                Dictionary<int,string> closedMarkers,openMarkers;
                var closed=TavernRoomSurvey.Read(interior,out closedMarkers);
                foreach(var door in doors) door.SetOpen(true,true,true); Physics.SyncTransforms();
                var opened=TavernRoomSurvey.Read(interior,out openMarkers);
                Func<List<LodgingRoom>,string> signature=rooms=> {
                    var parts=new List<string>(); foreach(var room in rooms) parts.Add(room.Id+"="+string.Join(",",room.Beds.ToArray()));
                    return string.Join(";",parts.ToArray()); };
                if(signature(closed)!=signature(opened)) throw new InvalidOperationException("Opening tavern doors changed private room partition.");
                var assigned=new HashSet<string>();
                foreach(var room in closed) foreach(var bed in room.Beds)
                    if(!assigned.Add(bed)) throw new InvalidOperationException("Bed belongs to multiple tavern rooms.");
                foreach(var bed in beds) if(!assigned.Contains("bed:"+bed.RecordIndex)) throw new InvalidOperationException("Tavern bed missing from room partition: "+bed.RecordIndex);
                if(closed.Count<2) throw new InvalidOperationException("Known multi-room tavern collapsed into one room.");
                var markers=interior.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest);
                if(closedMarkers.Count!=markers.Length || openMarkers.Count!=markers.Length)
                    throw new InvalidOperationException("Native rental markers are missing room mappings.");
                for(int index=0;index<markers.Length;index++)
                {
                    if(!closedMarkers.ContainsKey(index) || !openMarkers.ContainsKey(index) || closedMarkers[index]!=openMarkers[index] ||
                        !closed.Exists(r=>r.Id==closedMarkers[index])) throw new InvalidOperationException("Rental marker mapping changed with open doors.");
                    Debug.Log("LIVING_CITIES_TAVERN_MARKER_PASS: building="+interior.EntryDoor.buildingKey+"; marker="+index+"; room="+closedMarkers[index]);
                }
                Debug.Log("LIVING_CITIES_TAVERN_ROOMS_PASS: building="+interior.EntryDoor.buildingKey+"; "+signature(closed)+"; open/closed identical.");
            }
            finally { for(int i=0;i<doors.Length;i++) doors[i].SetOpen(original[i],true,true); Physics.SyncTransforms(); }
        }
    }
}
