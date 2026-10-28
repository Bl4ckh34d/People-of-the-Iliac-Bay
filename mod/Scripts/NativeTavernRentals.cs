using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class NativeTavernRentals : IDisposable
    {
        // Shared by exterior and remote preparation. No NPC admission is allowed
        // until native paid receipts and permanent occupants have been reconciled.
        internal static void RegisterInventory(WorldStateStore store,int town,DFLocation location,StaticDoor entry,
            TavernBedSurvey.Result inventory,RoomRental_v1 receipt,double now)
        {
            if(store==null || inventory==null || inventory.Beds==null || inventory.Markers==null || inventory.Occupants==null)
                throw new ArgumentException("Incomplete inn inventory.");
            string allocated=null;
            if(receipt!=null && receipt.expiryTime>now &&
                (receipt.mapID!=town || receipt.buildingKey!=entry.buildingKey || !inventory.Markers.TryGetValue(receipt.allocatedBedIndex,out allocated)))
                throw new InvalidOperationException("Native paid rental has no matching destination bed.");
            store.RegisterLodgingBeds(town,entry.buildingKey,inventory.Beds);
            if(allocated!=null && !store.ReconcilePlayerLodging(town,entry.buildingKey,allocated,now,receipt.expiryTime))
                throw new InvalidOperationException("Native paid rental conflicts with destination inventory.");
            NativeInnOccupants.Claim(store,town,location,entry,inventory.Beds,inventory.Occupants,now,inventory.KeeperHash);
            StreetPopulation.RegisterSurveyedInnCatalog(store,town,location,entry,inventory,now);
        }
        private DaggerfallInterior surveyed;
        private List<LodgingBed> beds;
        private Dictionary<int,string> markers;
        private Dictionary<int,string> bedOccupants;
        private Dictionary<int,string> assignedBedOccupants;
        public string BedFor(DaggerfallInterior loadedInterior,int hash)
        {
            string id;
            return surveyed==loadedInterior && assignedBedOccupants!=null && assignedBedOccupants.TryGetValue(hash,out id) &&
                beds!=null && beds.Exists(b=>b.Id==id) ? id : null;
        }
        private string lastFailure;
        private Func<WorldStateStore> getStore;
        private Func<bool> available;
        private IDisposable hooks;
        private BuildingDirectory inventoryDirectory;
        private readonly List<int> inventoryBuildings=new List<int>();
        private readonly Dictionary<int,Vector3> inventoryDoors=new Dictionary<int,Vector3>();
        private readonly Dictionary<int,Inventory> inventory=new Dictionary<int,Inventory>();
        private int nextInventory;
        private System.Collections.IEnumerator surveyOperation;
        private TavernBedSurvey.Result surveyResult;
        private Inventory surveyInventory;
        private int surveyKey;
        private float surveyStartsAt;
        private bool surveyStarted;
        private WorldStateStore surveyStore;
        private object surveyEpoch;
        private sealed class Inventory
        {
            public TavernBedSurvey.Result Source;
            public List<LodgingBed> Beds;
            public Dictionary<int,string> Markers;
            public Dictionary<int,string> Occupants;
            public StaticDoor Entry;
            public string Failure;
        }

        internal bool HasPendingExteriorInventory(BuildingDirectory directory)
        {
            if(inventoryDirectory!=directory || surveyOperation!=null)return true;
            foreach(int key in inventoryBuildings)if(!inventory.ContainsKey(key))return true;
            return false;
        }

        internal int CompletedExteriorInventoryCount(BuildingDirectory directory)
        {
            if(inventoryDirectory!=directory)return 0;
            int count=0;foreach(var item in inventory.Values)if(item.Beds!=null || item.Failure!=null)count++;
            return count;
        }

        private void RefreshExterior(WorldStateStore store,BuildingDirectory directory)
        {
            if(assignmentAudit)return;
            if(inventoryDirectory!=directory)
            {
                CancelSurvey();
                inventoryDirectory=directory; inventory.Clear(); inventoryBuildings.Clear();inventoryDoors.Clear(); nextInventory=0;
                foreach(var building in directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern)) inventoryBuildings.Add(building.buildingKey);
                inventoryBuildings.Sort();
                foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    for(int i=0;i<owner.Doors.Length;i++)
                    {
                        var door=owner.Doors[i];
                        if(door.doorType==DoorTypes.Building && inventoryBuildings.Contains(door.buildingKey) && !inventoryDoors.ContainsKey(door.buildingKey))
                            inventoryDoors.Add(door.buildingKey,directory.transform.InverseTransformPoint(owner.GetDoorPosition(i)));
                    }
            }
            if(inventoryBuildings.Count==0) return;
            int key=inventoryBuildings[nextInventory++%inventoryBuildings.Count];
            if(nextInventory>=inventoryBuildings.Count) nextInventory=0;
            // Continue serving prepared Inns while another building is surveyed.
            // Starting every remote survey before revisiting receipts/admissions
            // left already-arrived guests waiting outside for the whole census.
            Inventory prepared;
            if(inventory.TryGetValue(key,out prepared) && prepared.Beds!=null)
                ReconcileInventory(store,directory,key,prepared);
            if(surveyOperation!=null)return;
            // Prepare visible accommodation first; distant Inns remain ordinary
            // background work. Use actual doors in settlement space so floating
            // origin shifts do not invalidate the cache. BuildingSummary positions
            // are relative to RMB blocks, not to the whole settlement.
            float nearest=float.PositiveInfinity;
            var player=directory.transform.InverseTransformPoint(GameManager.Instance.PlayerMotor.transform.position);
            foreach(int candidate in inventoryBuildings)
            {
                if(inventory.ContainsKey(candidate))continue;
                Vector3 door;
                if(!inventoryDoors.TryGetValue(candidate,out door))continue;
                float distance=(door-player).sqrMagnitude;
                if(distance<nearest){nearest=distance;key=candidate;}
            }
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
                    surveyResult=new TavernBedSurvey.Result(); surveyInventory=item; surveyKey=key;
                    var epoch=store.CaptureNpcVitalsWriteEpoch();
                    surveyStore=store;surveyEpoch=epoch;
                    surveyOperation=TavernBedSurvey.ReadServiceSteps(owner.transform,entry,directory.LocationData.Climate.ClimateType,surveyResult,
                        ()=>inventoryDirectory==directory && ReferenceEquals(getStore(),store) && store.IsNpcVitalsWriteEpochCurrent(epoch));
                    surveyStartsAt=Time.unscaledTime+0.1f;
                    surveyStarted=false;
                    return;
                }
                catch(Exception error)
                {
                    item.Failure=error.Message;
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_SURVEY_FAILED: "+directory.MapID+":"+key+": "+item.Failure);
                }
            }
            if(item.Beds==null) return; // Retry geometry only after the settlement layout reloads.
            if(!ReferenceEquals(item,prepared))ReconcileInventory(store,directory,key,item);
        }
        private void ReconcileInventory(WorldStateStore store,BuildingDirectory directory,int key,Inventory item)
        {
            try
            {
                var receipt=GameManager.Instance.PlayerEntity.GetRentedRoom(directory.MapID,key);
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                RegisterInventory(store,directory.MapID,directory.LocationData,item.Entry,
                    item.Source??new TavernBedSurvey.Result {Beds=item.Beds,Markers=item.Markers,Occupants=item.Occupants},receipt,now);
                item.Failure=null;
                store.AdvanceInnAdmissions(directory.MapID,now,8,key);
            }
            catch(Exception error)
            {
                if(item.Failure!=error.Message) Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_RENTAL_CONFLICT: "+directory.MapID+":"+key+": "+error.Message);
                item.Failure=error.Message;
            }
        }

        public void Install(Func<WorldStateStore> store,Func<bool> enabled)
        {
            getStore=store; available=enabled;
            var type=Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeTavernRentalHooks, PeopleOfTheIliacBay.QuestHooks",true);
            hooks=(IDisposable)Activator.CreateInstance(type,new object[] {
                new Func<int,int,int,Action,bool>(Rent),new Func<bool,string>(RestBlocked),
                new Func<DaggerfallInterior,Vector3[],Vector3[]>((room,original)=>available!=null && available()
                    ? TavernBedSurvey.CompleteInnRestMarkers(room,original) : original) });
        }
        public void Dispose() { CancelSurvey(); if(hooks!=null) hooks.Dispose(); hooks=null; }
        private void CancelSurvey()
        {
            if(surveyOperation!=null)
            {
                (surveyOperation as IDisposable)?.Dispose();surveyOperation=null;
                inventory.Remove(surveyKey);
            }
            surveyInventory=null;surveyResult=null;surveyStore=null;surveyEpoch=null;
        }
        public void AdvanceSurvey()
        {
            if(surveyOperation==null || Time.unscaledTime<surveyStartsAt) return;
            if(available==null || !available() || !inventoryDirectory ||
                GameManager.Instance.IsPlayerInside || GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory()!=inventoryDirectory ||
                getStore==null || surveyStore==null || !ReferenceEquals(getStore(),surveyStore) || !surveyStore.IsNpcVitalsWriteEpochCurrent(surveyEpoch))
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
                // Cancellation intentionally publishes no partial result. Drop
                // its cache entry so the current save branch can survey again.
                if(!surveyStore.IsNpcVitalsWriteEpochCurrent(surveyEpoch) || surveyResult==null || surveyResult.Beds==null ||
                    surveyResult.Markers==null || surveyResult.Occupants==null)
                {CancelSurvey();return;}
                (surveyOperation as IDisposable)?.Dispose(); surveyOperation=null;
                surveyInventory.Source=surveyResult;
                surveyInventory.Markers=surveyResult.Markers; surveyInventory.Occupants=surveyResult.Occupants;
                surveyInventory.Beds=surveyResult.Beds;
                ReconcileInventory(getStore(),inventoryDirectory,surveyKey,surveyInventory);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_INVENTORY: "+inventoryDirectory.MapID+":"+surveyKey+"; beds="+surveyInventory.Beds.Count);
                surveyResult=null; surveyInventory=null;
                surveyStore=null;surveyEpoch=null;
            }
            catch(Exception error)
            {
                var item=surveyInventory;
                if(surveyOperation!=null) (surveyOperation as IDisposable)?.Dispose();
                surveyOperation=null; surveyResult=null; surveyInventory=null;
                surveyStore=null;surveyEpoch=null;
                if(item!=null) item.Failure=error.Message;
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_SURVEY_FAILED: "+surveyKey+": "+error);
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
                    string bed;
                    if(!markers.TryGetValue(receipt.allocatedBedIndex,out bed)) return "Your rented bed is unavailable.";
                    return store.IsLodgingBedReserved(town,building,bed,now,"player") ? occupied : null;
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
                    if(beds.Exists(b=>b.Id==id) && store.IsLodgingBedReserved(town,building,id,now,"player")) return occupied;
                }
                return null;
            }
            catch(Exception error)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_REST_REFUSED: "+town+":"+building+": "+error.Message);
                return "The keeper cannot confirm access to this bed.";
            }
        }

        private void Prepare(WorldStateStore store,BuildingDirectory directory,DaggerfallInterior interior)
        {
            long started=RuntimeTiming.Start("inn-loaded-prepare");
            try
            {
            if(surveyed!=interior) { surveyed=interior; beds=null; markers=null; lastFailure=null; }
            if(beds==null)
            {
                Dictionary<int,string> mapping;
                beds=TavernBedSurvey.Read(interior,out mapping,out bedOccupants,out assignedBedOccupants);
                markers=mapping;
            }
            store.RegisterLodgingBeds(directory.MapID,interior.EntryDoor.buildingKey,beds);
            var guests=new Dictionary<int,string>();
            if(assignedBedOccupants!=null) foreach(var person in assignedBedOccupants)
                if(beds.Exists(b=>b.Id==person.Value)) guests.Add(person.Key,person.Value);
            BedroomFlatReplacement.Apply(store,interior,DFLocation.BuildingTypes.Tavern,guests);
            }
            finally { RuntimeTiming.End("inn-loaded-prepare",started); }
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
                // Once bookings exist, falling through could sell another guest's bed.
                bool managed=store.Snapshot().Lodgings.Exists(t=>t.SettlementId==town && t.BuildingKey==building);
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_RENTAL_SURVEY: "+town+":"+building+": "+error.Message);
                if(!managed) return false;
                close(); DaggerfallUI.MessageBox("The keeper cannot confirm beds in this changed building."); return true;
            }
            close();
            if(days<1 || days>350 || price<0) { DaggerfallUI.MessageBox("That stay cannot be booked."); return true; }
            var player=game.PlayerEntity;
            if(player.GetGoldAmount()<price) { DaggerfallUI.MessageBox(454); return true; }
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var receipt=player.GetRentedRoom(town,building);
            string bed=null;
            try
            {
                if(receipt!=null)
                {
                    if(receipt.expiryTime<=now || !markers.TryGetValue(receipt.allocatedBedIndex,out bed) ||
                        !store.ReconcilePlayerLodging(town,building,bed,now,receipt.expiryTime))
                        throw new InvalidOperationException("Existing paid bed needs reconciliation.");
                }
                // Earlier registered arrivals get first choice. Keep this UI action
                // bounded; a full batch asks the player to wait for catch-up.
                NativeInnOccupants.Claim(store,directory,interior.EntryDoor,beds,bedOccupants,now);
                AdvanceResidentInside(store,town,building,now);
                if(store.HasDueTravelerAdmission(town,building,now) || store.HasDueResidentInnAdmission(town,building,now))
                {
                    DaggerfallUI.MessageBox("The keeper is serving arriving guests. Please try again shortly.");
                    return true;
                }
                double end=(receipt==null ? now : receipt.expiryTime)+days*86400d;
                if(end-now>351*86400d) throw new InvalidOperationException("Rental duration exceeds native limit.");
                if(receipt==null)
                {
                    var availableBeds=store.AvailableLodgingBeds(town,building,1,now,end);
                    if(availableBeds!=null)
                        foreach(var pair in markers)
                            if(availableBeds.Contains(pair.Value) && (bed==null || pair.Key<MarkerFor(markers,bed)))bed=pair.Value;
                }
                if(bed==null || !store.ReconcilePlayerLodging(town,building,bed,now,end))
                {
                    var next=store.EarliestLodgingStart(town,building,1,now,days*86400d);
                    string message=receipt==null ? "No bed is available for that stay." : "Your bed cannot be extended for that stay.";
                    if(next.HasValue)
                    {
                        var date=new DaggerfallDateTime(); date.FromSeconds((ulong)Math.Ceiling(next.Value));
                        message+=" A bed is available from "+date.DateTimeString()+".";
                    }
                    DaggerfallUI.MessageBox(message); return true;
                }
                if(receipt==null)
                {
                    int marker=MarkerFor(markers,bed);
                    if(marker<0)throw new InvalidOperationException("Selected bed has no native rental marker.");
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
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_RENTAL_REFUSED: "+town+":"+building+": "+error.Message);
                DaggerfallUI.MessageBox("The keeper cannot confirm that bed. Please try again later."); return true;
            }
        }

        private static void AdvanceResidentInside(WorldStateStore store,int town,int building,double now)
        {
            long started=RuntimeTiming.Start("inn-loaded-admissions");
            try
            {
            var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            bool ready=streets && streets.PrepareResidentInnReceptions(store,town,building,now);
            if(!ready && store.HasUnpreparedResidentInnAdmission(town,building,now)) return;
            store.AdvanceInnAdmissions(town,now,8,building);
            }
            finally { RuntimeTiming.End("inn-loaded-admissions",started); }
        }
        private static int MarkerFor(Dictionary<int,string> markerBeds,string bedId)
        {
            int selected=-1;
            foreach(var pair in markerBeds)
                if(pair.Value==bedId && (selected<0 || pair.Key<selected))selected=pair.Key;
            return selected;
        }
        public void Refresh(WorldStateStore store,BuildingDirectory directory)
        {
            var game=GameManager.Instance;
            var interior=game.IsPlayerInside ? game.PlayerEnterExit.Interior : null;
            if(surveyed!=interior) { surveyed=interior; beds=null; markers=null; lastFailure=null; }
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
                    NativeInnOccupants.Claim(store,directory,interior.EntryDoor,beds,bedOccupants,now);
                    AdvanceResidentInside(store,directory.MapID,building,now);
                    lastFailure=null; return;
                }
                string allocated;
                if(!markers.TryGetValue(receipt.allocatedBedIndex,out allocated))
                    throw new InvalidOperationException("Native allocated bed marker has no surveyed bed.");
                if(!store.ReconcilePlayerLodging(directory.MapID,building,allocated,now,receipt.expiryTime))
                    throw new InvalidOperationException("Native paid rental conflicts with saved lodging; receipt and existing bookings retained.");
                lastFailure=null;
                NativeInnOccupants.Claim(store,directory,interior.EntryDoor,beds,bedOccupants,now);
                AdvanceResidentInside(store,directory.MapID,building,now);
            }
            catch(Exception error)
            {
                string failure=directory.MapID+":"+building+": "+error.Message;
                if(lastFailure!=failure) Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_RENTAL_CONFLICT: "+failure);
                lastFailure=failure;
            }
        }
    }
}
