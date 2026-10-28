using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Disposable native fixture. Occupancy is controlled; bed geometry,
    // identities, rental handler, UI, price, receipt and payment are actual.
    internal static class NativeTavernCapacityProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            if(!host || !streets || !streets.Ready || !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name))
                throw new InvalidOperationException("Inn capacity needs the ready owned disposable fixture.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory)throw new InvalidOperationException("Inn capacity has no actual town directory.");
            if(game.IsPlayerInside){game.PlayerEnterExit.TransitionExterior();yield return new WaitForSecondsRealtime(1);}
            Transform nonInnOwner=null;StaticDoor nonInnDoor=default(StaticDoor);
            foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())foreach(var door in doors.Doors)
            {
                BuildingSummary summary;
                if(door.doorType==DoorTypes.Building && directory.GetBuildingSummary(door.buildingKey,out summary) &&
                    summary.BuildingType>=DFLocation.BuildingTypes.House1 && summary.BuildingType<=DFLocation.BuildingTypes.House6)
                {nonInnOwner=doors.transform;nonInnDoor=door;break;}
                if(nonInnOwner)break;
            }
            if(!nonInnOwner)throw new InvalidOperationException("Inn marker boundary check found no actual non-Inn house.");
            TownSmokeHarness.EnterInterior(nonInnOwner,nonInnDoor);yield return new WaitForSecondsRealtime(1);DaggerfallUI.Instance.PopToHUD();
            var nonInn=game.PlayerEnterExit.Interior;
            if(!nonInn || nonInn.EntryDoor.buildingKey!=nonInnDoor.buildingKey || nonInn.BuildingData.BuildingType==DFLocation.BuildingTypes.Tavern)
                throw new InvalidOperationException("Inn marker boundary check did not load its actual non-Inn house.");
            AssertSameRest(OriginalRest(nonInn),nonInn.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest),"Non-Inn Rest markers changed.");
            game.PlayerEnterExit.TransitionExterior();yield return new WaitForSecondsRealtime(1);DaggerfallUI.Instance.PopToHUD();
            if(!game.IsPlayerInside)
            {
                Transform owner=null;StaticDoor selected=default(StaticDoor);float nearest=float.PositiveInfinity;
                foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    for(int i=0;i<doors.Doors.Length;i++)
                    {
                        var door=doors.Doors[i];BuildingSummary summary;
                        if(door.doorType!=DoorTypes.Building || !directory.GetBuildingSummary(door.buildingKey,out summary) ||
                            summary.BuildingType!=DFLocation.BuildingTypes.Tavern)continue;
                        var block=DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlock(door.blockIndex);
                        var records=block.RmbBlock.SubRecords[door.recordIndex].Interior;
                        int sourceBeds=0,sourceMarkers=0;
                        foreach(var model in records.Block3dObjectRecords)if(model.ModelIdNum>=41000 && model.ModelIdNum<=41002)sourceBeds++;
                        foreach(var flat in records.BlockFlatObjectRecords)
                            if(flat.TextureArchive==TextureReader.EditorFlatsTextureArchive && flat.TextureRecord==(int)DaggerfallInterior.InteriorMarkerTypes.Rest)sourceMarkers++;
                        if(sourceBeds<=sourceMarkers)continue;
                        float distance=(doors.GetDoorPosition(i)-game.PlayerMotor.transform.position).sqrMagnitude;
                        if(distance<nearest){nearest=distance;owner=doors.transform;selected=door;}
                    }
                if(!owner)throw new InvalidOperationException("Inn capacity found no actual tavern with a physical bed lacking an original native Rest marker.");
                TownSmokeHarness.EnterInterior(owner,selected);
                yield return new WaitForSecondsRealtime(2);DaggerfallUI.Instance.PopToHUD();
            }
            var room=game.PlayerEnterExit.Interior;BuildingSummary building;
            if(!room || !directory.GetBuildingSummary(room.EntryDoor.buildingKey,out building) ||
                building.BuildingType!=DFLocation.BuildingTypes.Tavern)
                throw new InvalidOperationException("Inn capacity did not enter an actual native tavern.");
            int town=directory.MapID,key=room.EntryDoor.buildingKey;
            var sourceRest=OriginalRest(room);
            var completedRest=room.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest);
            var repeatedRest=room.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest);
            var actualBeds=BedGeometry.Read(room);
            if(completedRest.Length!=actualBeds.Count || completedRest.Length<=sourceRest.Count || repeatedRest.Length!=completedRest.Length)
                throw new InvalidOperationException("Native Inn Rest array does not cover the actual formerly markerless beds.");
            for(int i=0;i<completedRest.Length;i++)
                if(completedRest[i]!=repeatedRest[i] || i<sourceRest.Count && completedRest[i]!=sourceRest[i])
                    throw new InvalidOperationException("Native Rest completion changed an original marker/index or its deterministic order.");
            if(game.PlayerEntity.GetRentedRoom(town,key)!=null)
                throw new InvalidOperationException("Inn capacity requires no prior native player rental at this Inn.");
            var rentals=(NativeTavernRentals)typeof(PeopleOfTheIliacBayMod).GetField("nativeRentals",Private).GetValue(host);
            var live=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(host);
            var getter=typeof(NativeTavernRentals).GetField("getStore",Private);
            var originalGetter=getter.GetValue(rentals);
            var enabledField=typeof(NativeTavernRentals).GetField("available",Private);
            var originalEnabled=enabledField.GetValue(rentals);
            try
            {
                enabledField.SetValue(rentals,new Func<bool>(()=>false));
                AssertSameRest(sourceRest,room.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest),"Disabled mod changed native Inn Rest markers.");
            }
            finally{enabledField.SetValue(rentals,originalEnabled);}
            var prepare=typeof(NativeTavernRentals).GetMethod("Prepare",Private);
            var rent=typeof(NativeTavernRentals).GetMethod("Rent",Private);
            bool hostEnabled=host.enabled,streetsEnabled=streets.enabled;
            int coins=game.PlayerEntity.GoldPieces,total=game.PlayerEntity.GetGoldAmount(),nativeReceipts=game.PlayerEntity.RentedRooms.Count;
            int price=FormulaHelper.CalculateTradePrice(FormulaHelper.CalculateRoomCost(1),game.PlayerEnterExit.BuildingDiscoveryData.quality,false);
            if(price<1 || coins<price)throw new InvalidOperationException("Inn capacity needs the actual native one-night price in loose coins.");
            string sceneName=DaggerfallInterior.GetSceneName(town,key);
            bool wasPermanent=SaveLoadManager.StateManager.ContainsPermanentScene(sceneName);
            try
            {
                host.enabled=false;streets.enabled=false;
                var fixture=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());fixture.Restore(live.Snapshot());
                prepare.Invoke(rentals,new object[]{fixture,directory,room});
                rentals.Refresh(fixture,directory);
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),end=now+2*86400;
                var before=fixture.Snapshot();var lodging=before.Lodgings.Find(x=>x.SettlementId==town && x.BuildingKey==key);
                if(lodging==null || lodging.Beds.Count==0)throw new InvalidOperationException("Inn capacity has no real surveyed beds.");
                var nativeMarkers=(Dictionary<int,string>)typeof(NativeTavernRentals).GetField("markers",Private).GetValue(rentals);
                var candidates=new List<Resident>();
                foreach(var origin in before.Settlements)
                    if(origin.SettlementId!=town)foreach(var home in origin.Households)foreach(var person in home.Residents)
                        if(person.IsAlive && person.AgeGroup==ResidentAgeGroup.Adult && !person.IsQuestProtected)candidates.Add(person);
                int next=0,added=0;string releasedBooking=null,releasedBed=null;
                foreach(var bed in lodging.Beds)
                {
                    if(lodging.Occupants.Exists(x=>x.BedId==bed.Id && x.StartsAt<now+86400) ||
                        lodging.Bookings.Exists(x=>x.BedIds.Contains(bed.Id) && x.StartsAt<now+86400 && x.EndsAt>now))continue;
                    bool booked=false;
                    while(next<candidates.Count)
                    {
                        var person=candidates[next++];int origin=-1;
                        foreach(var population in before.Settlements)
                            if(population.SettlementId!=town && population.Households.Exists(h=>h.Residents.Exists(p=>p.Id==person.Id)))
                            {origin=population.SettlementId;break;}
                        string booking="capacity-probe:"+key+":"+added;
                        if(origin>=0 && fixture.TryBookLodging(town,key,booking,person.Id,origin,false,1,now,end,bed.Id))
                        {
                            foreach(var marker in nativeMarkers)
                                if(marker.Key>=sourceRest.Count && marker.Value==bed.Id){releasedBooking=booking;releasedBed=bed.Id;}
                            added++;booked=true;break;
                        }
                    }
                    if(!booked)throw new InvalidOperationException("Inn capacity lacks enough actual recorded foreign identities for the real free beds.");
                }
                if(releasedBooking==null || fixture.AvailableLodgingBeds(town,key,1,now,now+86400)!=null)
                    throw new InvalidOperationException("Inn capacity needs one controlled guest booking that can later release a real bed.");
                getter.SetValue(rentals,new Func<WorldStateStore>(()=>fixture));
                int closes=0;Action close=()=>closes++;
                bool handled=(bool)rent.Invoke(rentals,new object[]{key,1,price,close});
                yield return null;
                var box=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
                var label=box==null?null:(MultiFormatTextLabel)typeof(DaggerfallMessageBox).GetField("label",Private).GetValue(box);
                string text="";if(label!=null)foreach(var part in label.TextLabels)text+=part.Text+" ";
                if(!handled || closes!=1 || !text.Contains("No bed is available for that stay.") ||
                    game.PlayerEntity.GetGoldAmount()!=total || game.PlayerEntity.RentedRooms.Count!=nativeReceipts ||
                    game.PlayerEntity.GetRentedRoom(town,key)!=null || fixture.AvailableLodgingBeds(town,key,1,now,now+86400)!=null)
                    throw new InvalidOperationException("Full native Inn did not reject player rental without payment/receipt: message="+text);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_CAPACITY_BLOCKED_PASS: actualBeds="+lodging.Beds.Count+
                    "; controlledGuestBookings="+added+"; actualForeignIdentities=true; productionRent=true; nativeMessage=true; goldUnchanged=true; playerReceipt=false; naturalOccupancy=false");
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                // Only the controlled booking is removed from the detached
                // current state. Existing native occupants/bookings remain.
                var released=fixture.Snapshot();var target=released.Lodgings.Find(x=>x.SettlementId==town && x.BuildingKey==key);
                if(target.Bookings.RemoveAll(x=>x.Id==releasedBooking)!=1)
                    throw new InvalidOperationException("Controlled capacity booking could not release its exact bed.");
                fixture.Restore(released);now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var available=fixture.AvailableLodgingBeds(town,key,1,now,now+86400);
                if(available==null || available.Count!=1 || available[0]!=releasedBed)
                    throw new InvalidOperationException("Capacity release did not expose the exact controlled real bed.");
                handled=(bool)rent.Invoke(rentals,new object[]{key,1,price,close});
                var receipt=game.PlayerEntity.GetRentedRoom(town,key);
                var actual=fixture.Snapshot().Lodgings.Find(x=>x.SettlementId==town && x.BuildingKey==key);
                var playerBooking=actual.Bookings.Find(x=>x.Player && x.GuestId=="player" && x.EndsAt>now);
                var markers=(Dictionary<int,string>)typeof(NativeTavernRentals).GetField("markers",Private).GetValue(rentals);
                string receiptBed=null;
                if(!handled || closes!=2 || receipt==null || !markers.TryGetValue(receipt.allocatedBedIndex,out receiptBed) ||
                    receipt.allocatedBedIndex<sourceRest.Count ||
                    receiptBed!=releasedBed || playerBooking==null || playerBooking.BedIds.Count!=1 || playerBooking.BedIds[0]!=releasedBed ||
                    game.PlayerEntity.GetGoldAmount()!=total-price || game.PlayerEntity.RentedRooms.Count!=nativeReceipts+1)
                    throw new InvalidOperationException("Freed native Inn bed did not produce its exact paid player rental.");
                var assignedPoint=room.FindMarkers(DaggerfallInterior.InteriorMarkerTypes.Rest)[receipt.allocatedBedIndex];
                BedAnchor assigned=null;foreach(var bed in actualBeds)if("bed:"+bed.RecordIndex==releasedBed){assigned=bed;break;}
                if(assigned==null || Vector3.Distance(assignedPoint,room.transform.TransformPoint(assigned.LocalCenter)+Vector3.up)>.001f)
                    throw new InvalidOperationException("Native rental Rest index does not address the exact measured mattress.");
                var rest=new DaggerfallRestWindow(DaggerfallUI.UIManager,false);
                DaggerfallUI.UIManager.PushWindow(rest);yield return null;
                bool canRest=(bool)typeof(DaggerfallRestWindow).GetMethod("CanRest",Private).Invoke(rest,new object[]{false});
                var nativeAllocated=(Vector3)typeof(DaggerfallRestWindow).GetField("allocatedBed",Private).GetValue(rest);
                if(!canRest || Vector3.Distance(nativeAllocated,assignedPoint)>.001f)
                    throw new InvalidOperationException("Native rest admission did not select the paid formerly markerless physical bed.");
                DaggerfallUI.Instance.PopToHUD();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_CAPACITY_NATIVE_PASS: realBed="+releasedBed+"; productionRent=true; nativePrice="+price+
                    "; actualCharge=true; nativeReceipt=true; exactBed=true; originallyMarkerless=true; nativeRestIndex=true; nativeCanRest=true; originalMarkersPreserved=true; stableOrder=true; nonInnUnchanged=true; disabledUnchanged=true; controlledOccupancy=true; naturalDemand=false");
            }
            finally
            {
                getter.SetValue(rentals,originalGetter);
                game.PlayerEntity.RentedRooms.RemoveAll(x=>x.mapID==town && x.buildingKey==key);
                game.PlayerEntity.GoldPieces=coins;
                if(!wasPermanent)SaveLoadManager.StateManager.RemovePermanentScene(sceneName);
                host.enabled=hostEnabled;streets.enabled=streetsEnabled;
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            }
        }
        private static List<Vector3> OriginalRest(DaggerfallInterior room)
        {
            var rows=(List<DaggerfallInterior.InteriorEditorMarker>)typeof(DaggerfallInterior).GetField("markers",Private).GetValue(room);
            var result=new List<Vector3>();
            foreach(var row in rows)if(row.type==DaggerfallInterior.InteriorMarkerTypes.Rest)result.Add(row.gameObject.transform.position);
            return result;
        }
        private static void AssertSameRest(List<Vector3> original,Vector3[] actual,string message)
        {
            if(original.Count!=actual.Length)throw new InvalidOperationException(message);
            for(int i=0;i<actual.Length;i++)if(original[i]!=actual[i])throw new InvalidOperationException(message);
        }
    }
}
