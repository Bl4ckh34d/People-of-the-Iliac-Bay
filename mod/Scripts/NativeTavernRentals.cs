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
        // Shared by exterior and remote preparation. No NPC admission is allowed
        // until native paid receipts and permanent occupants have been reconciled.
        internal static void RegisterInventory(WorldStateStore store,int town,DFLocation location,StaticDoor entry,
            TavernRoomSurvey.Result inventory,RoomRental_v1 receipt,double now)
        {
            if(store==null || inventory==null || inventory.Rooms==null || inventory.Markers==null || inventory.Occupants==null)
                throw new ArgumentException("Incomplete inn inventory.");
            string allocated=null;
            if(receipt!=null && receipt.expiryTime>now &&
                (receipt.mapID!=town || receipt.buildingKey!=entry.buildingKey || !inventory.Markers.TryGetValue(receipt.allocatedBedIndex,out allocated)))
                throw new InvalidOperationException("Native paid rental has no matching destination room.");
            // Remote and loaded surveys must agree on rooms without native
            // rental markers (staff/common rooms), before comparing inventories.
            var eligible=new HashSet<string>(inventory.Markers.Values);
            foreach(var room in inventory.Rooms) room.NonRentable=!eligible.Contains(room.Id);
            store.RegisterLodging(town,entry.buildingKey,inventory.Rooms);
            if(allocated!=null && !store.ReconcilePlayerLodging(town,entry.buildingKey,allocated,now,receipt.expiryTime))
                throw new InvalidOperationException("Native paid rental conflicts with destination inventory.");
            NativeInnOccupants.Claim(store,town,location,entry,inventory.Rooms,inventory.Occupants,now,inventory.KeeperHash);
        }
        private DaggerfallInterior surveyed;
        private List<LodgingRoom> rooms;
        private Dictionary<int,string> markers;
        private Dictionary<int,string> roomOccupants;
        private Dictionary<int,string> bedroomOccupants;
        public string BedroomFor(DaggerfallInterior room,int hash)
        {
            string id;
            return surveyed==room && bedroomOccupants!=null && bedroomOccupants.TryGetValue(hash,out id) &&
                rooms!=null && rooms.Exists(r=>r.Id==id && !r.NonRentable) ? id : null;
        }
        private string lastFailure;
        private Func<WorldStateStore> getStore;
        private Func<bool> available;
        private IDisposable hooks;
        private BuildingDirectory inventoryDirectory;
        private readonly List<int> inventoryBuildings=new List<int>();
        private readonly Dictionary<int,Inventory> inventory=new Dictionary<int,Inventory>();
        private int nextInventory;
        private System.Collections.IEnumerator surveyOperation;
        private TavernRoomSurvey.Result surveyResult;
        private Inventory surveyInventory;
        private int surveyKey;
        private float surveyStartsAt;
        private bool surveyStarted;
        private sealed class Inventory
        {
            public List<LodgingRoom> Rooms;
            public Dictionary<int,string> Markers;
            public Dictionary<int,string> Occupants;
            public StaticDoor Entry;
            public string Failure;
        }

        private void RefreshExterior(WorldStateStore store,BuildingDirectory directory)
        {
            if(inventoryDirectory!=directory)
            {
                CancelSurvey();
                inventoryDirectory=directory; inventory.Clear(); inventoryBuildings.Clear(); nextInventory=0;
                foreach(var building in directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern)) inventoryBuildings.Add(building.buildingKey);
                inventoryBuildings.Sort();
            }
            if(inventoryBuildings.Count==0) return;
            if(surveyOperation!=null) return;
            int key=inventoryBuildings[nextInventory++%inventoryBuildings.Count];
            if(nextInventory>=inventoryBuildings.Count) nextInventory=0;
            Inventory item;
            if(!inventory.TryGetValue(key,out item))
            {
                item=new Inventory(); inventory.Add(key,item);
                try
                {
                    DaggerfallStaticDoors owner=null; StaticDoor entry=default(StaticDoor);
                    foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                        foreach(var door in candidate.Doors)
                            if(door.doorType==DoorTypes.Building && door.buildingKey==key) { owner=candidate; entry=door; break; }
                    if(!owner) throw new InvalidOperationException("Tavern entrance unavailable.");
                    item.Entry=entry;
                    surveyResult=new TavernRoomSurvey.Result(); surveyInventory=item; surveyKey=key;
                    surveyOperation=TavernRoomSurvey.ReadUnloadedSteps(owner.transform,entry,surveyResult);
                    surveyStartsAt=Time.unscaledTime+0.1f;
                    surveyStarted=false;
                    return;
                }
                catch(Exception error)
                {
                    item.Failure=error.Message;
                    Debug.LogWarning("LIVING_CITIES_TAVERN_SURVEY_FAILED: "+directory.MapID+":"+key+": "+item.Failure);
                }
            }
            if(item.Rooms==null) return; // Retry geometry only after the settlement layout reloads.
            ReconcileInventory(store,directory,key,item);
        }
        private void ReconcileInventory(WorldStateStore store,BuildingDirectory directory,int key,Inventory item)
        {
            try
            {
                var receipt=GameManager.Instance.PlayerEntity.GetRentedRoom(directory.MapID,key);
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                RegisterInventory(store,directory.MapID,directory.LocationData,item.Entry,
                    new TavernRoomSurvey.Result {Rooms=item.Rooms,Markers=item.Markers,Occupants=item.Occupants},receipt,now);
                item.Failure=null;
                store.AdvanceInnAdmissions(directory.MapID,now,8,key);
            }
            catch(Exception error)
            {
                if(item.Failure!=error.Message) Debug.LogWarning("LIVING_CITIES_RENTAL_CONFLICT: "+directory.MapID+":"+key+": "+error.Message);
                item.Failure=error.Message;
            }
        }

        public void Install(Func<WorldStateStore> store,Func<bool> enabled)
        {
            getStore=store; available=enabled;
            var type=Type.GetType("LivingCities.QuestHooks.NativeTavernRentalHooks, LivingCities.QuestHooks",true);
            hooks=(IDisposable)Activator.CreateInstance(type,new object[] {
                new Func<int,int,int,Action,bool>(Rent),new Func<bool,string>(RestBlocked) });
        }
        public void Dispose() { CancelSurvey(); if(hooks!=null) hooks.Dispose(); hooks=null; }
        private void CancelSurvey()
        {
            if(surveyOperation==null) return;
            (surveyOperation as IDisposable)?.Dispose(); surveyOperation=null;
            inventory.Remove(surveyKey); surveyInventory=null; surveyResult=null;
        }
        public void AdvanceSurvey()
        {
            if(surveyOperation==null || Time.unscaledTime<surveyStartsAt) return;
            if(available==null || !available() || !inventoryDirectory ||
                GameManager.Instance.IsPlayerInside || GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory()!=inventoryDirectory)
            { CancelSurvey(); return; }
            if(!surveyStarted)
            {
                if(!BackgroundWork.TryStart()) return;
                surveyStarted=true;
            }
            long started=RuntimeTiming.Start();
            try
            {
                if(surveyOperation.MoveNext()) return;
                (surveyOperation as IDisposable)?.Dispose(); surveyOperation=null;
                surveyInventory.Markers=surveyResult.Markers; surveyInventory.Occupants=surveyResult.Occupants;
                var eligible=new HashSet<string>(surveyResult.Markers.Values);
                foreach(var room in surveyResult.Rooms) room.NonRentable=!eligible.Contains(room.Id);
                surveyInventory.Rooms=surveyResult.Rooms;
                ReconcileInventory(getStore(),inventoryDirectory,surveyKey,surveyInventory);
                Debug.Log("LIVING_CITIES_TAVERN_INVENTORY: "+inventoryDirectory.MapID+":"+surveyKey+"; rooms="+surveyInventory.Rooms.Count);
                surveyResult=null; surveyInventory=null;
            }
            catch(Exception error)
            {
                var item=surveyInventory;
                if(surveyOperation!=null) (surveyOperation as IDisposable)?.Dispose();
                surveyOperation=null; surveyResult=null; surveyInventory=null;
                if(item!=null) item.Failure=error.Message;
                Debug.LogWarning("LIVING_CITIES_TAVERN_SURVEY_FAILED: "+surveyKey+": "+error.Message);
            }
            finally { RuntimeTiming.End("tavern-survey-slice",started); }
        }

        private string RestBlocked(bool stayHere)
        {
            if(available==null || !available()) return null;
            var game=GameManager.Instance; var interior=game.PlayerEnterExit.Interior;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory(); BuildingSummary summary;
            if(!game.IsPlayerInside || !interior || !directory ||
                !directory.GetBuildingSummary(interior.EntryDoor.buildingKey,out summary) || summary.BuildingType!=DFLocation.BuildingTypes.Tavern) return null;
            var store=getStore(); int town=directory.MapID,building=interior.EntryDoor.buildingKey;
            var lodging=store.Snapshot().Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==building);
            if(lodging==null) return null;
            const string occupied="This bed is occupied by another guest.";
            try
            {
                Prepare(store,directory,interior);
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var receipt=game.PlayerEntity.GetRentedRoom(town,building);
                if(!stayHere && receipt!=null && receipt.expiryTime>now)
                {
                    string room;
                    if(!markers.TryGetValue(receipt.allocatedBedIndex,out room)) return "Your rented bed is unavailable.";
                    var target=rooms.Find(r=>r.Id==room);
                    return store.IsLodgingBedReserved(town,building,target.Beds[0],now,"player") ? occupied : null;
                }
                // Rest-here keeps the player's position instead of using the paid marker.
                // Match only the mattress footprint and height, never beds on another floor.
                Vector3 position=interior.transform.InverseTransformPoint(game.PlayerMotor.transform.position);
                foreach(var bed in BedGeometry.Read(interior))
                {
                    Vector3 offset=position-bed.LocalCenter;
                    if(offset.y<-.35f || offset.y>2.2f ||
                        Mathf.Abs(Vector3.Dot(offset,bed.LocalHeadDirection))>bed.Length*.5f+.15f ||
                        Mathf.Abs(Vector3.Dot(offset,bed.LocalSideDirection))>bed.Width*.5f+.15f) continue;
                    string id="bed:"+bed.RecordIndex;
                    if(rooms.Exists(r=>r.Beds.Contains(id)) && store.IsLodgingBedReserved(town,building,id,now,"player")) return occupied;
                }
                return null;
            }
            catch(Exception error)
            {
                Debug.LogWarning("LIVING_CITIES_REST_REFUSED: "+town+":"+building+": "+error.Message);
                return "The keeper cannot confirm access to this bed.";
            }
        }

        private void Prepare(WorldStateStore store,BuildingDirectory directory,DaggerfallInterior interior)
        {
            if(surveyed!=interior) { surveyed=interior; rooms=null; markers=null; lastFailure=null; }
            if(rooms==null)
            {
                Dictionary<int,string> mapping;
                var all=TavernRoomSurvey.Read(interior,out mapping,out roomOccupants,out bedroomOccupants);
                var eligible=new HashSet<string>(mapping.Values);
                foreach(var room in all) room.NonRentable=!eligible.Contains(room.Id);
                rooms=all; markers=mapping;
            }
            store.RegisterLodging(directory.MapID,interior.EntryDoor.buildingKey,rooms);
            var guests=new Dictionary<int,string>();
            if(bedroomOccupants!=null) foreach(var person in bedroomOccupants)
                if(rooms.Exists(r=>r.Id==person.Value && !r.NonRentable)) guests.Add(person.Key,person.Value);
            BedroomFlatReplacement.Apply(store,interior,DFLocation.BuildingTypes.Tavern,guests);
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
                // Earlier registered arrivals get first choice. Keep this UI action
                // bounded; a full batch asks the player to wait for catch-up.
                NativeInnOccupants.Claim(store,directory,interior.EntryDoor,rooms,roomOccupants,now);
                AdvanceResidentInside(store,town,building,now);
                if(store.HasDueTravelerAdmission(town,building,now) || store.HasDueResidentInnAdmission(town,building,now))
                {
                    DaggerfallUI.MessageBox("The keeper is serving arriving guests. Please try again shortly.");
                    return true;
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

        private static void AdvanceResidentInside(WorldStateStore store,int town,int building,double now)
        {
            var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            bool ready=streets && streets.PrepareResidentInnReceptions(store,town,building,now);
            if(!ready && store.HasUnpreparedResidentInnAdmission(town,building,now)) return;
            store.AdvanceInnAdmissions(town,now,8,building);
        }
        public void Refresh(WorldStateStore store,BuildingDirectory directory)
        {
            var game=GameManager.Instance;
            var interior=game.IsPlayerInside ? game.PlayerEnterExit.Interior : null;
            if(surveyed!=interior) { surveyed=interior; rooms=null; markers=null; lastFailure=null; }
            if(!interior) { if(!game.IsPlayerInside) RefreshExterior(store,directory); return; }
            BuildingSummary summary;
            int building=interior.EntryDoor.buildingKey;
            if(!directory.GetBuildingSummary(building,out summary) || summary.BuildingType!=DFLocation.BuildingTypes.Tavern) return;
            var receipt=game.PlayerEntity.GetRentedRoom(directory.MapID,building);
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            try
            {
                Prepare(store,directory,interior);
                // Guest capacity exists independently of whether the player rented.
                // Reconcile any native payment before future NPC admission uses it.
                if(receipt==null || receipt.expiryTime<=now)
                {
                    NativeInnOccupants.Claim(store,directory,interior.EntryDoor,rooms,roomOccupants,now);
                    AdvanceResidentInside(store,directory.MapID,building,now);
                    lastFailure=null; return;
                }
                string allocated;
                if(!markers.TryGetValue(receipt.allocatedBedIndex,out allocated))
                    throw new InvalidOperationException("Native allocated bed marker has no surveyed guest room.");
                if(!store.ReconcilePlayerLodging(directory.MapID,building,allocated,now,receipt.expiryTime))
                    throw new InvalidOperationException("Native paid rental conflicts with saved lodging; receipt and existing bookings retained.");
                lastFailure=null;
                NativeInnOccupants.Claim(store,directory,interior.EntryDoor,rooms,roomOccupants,now);
                AdvanceResidentInside(store,directory.MapID,building,now);
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
