using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        internal static void RegisterSurveyedInnCatalog(WorldStateStore store,int settlement,DFLocation location,
            StaticDoor entry,TavernRoomSurvey.Result inventory,double now)
        {
            var source=inventory.ServiceCatalog;
            if(source==null)return;
            if(location.MapTableData.MapId!=settlement || source.BuildingKey!=entry.buildingKey || source.BlockIndex!=entry.blockIndex ||
                source.RecordIndex!=entry.recordIndex || source.Climate!=(int)ClimateSwaps.FromAPIClimateBase(location.Climate.ClimateType) ||
                !inventory.KeeperHash.HasValue || inventory.KeeperHash.Value!=source.KeeperHash)
                throw new ArgumentException("Unloaded Inn service source does not match its destination.");
            var keeper=store.NativePeopleFor(settlement).Find(p=>p.WorkKey==entry.buildingKey && p.NativeHash==source.KeeperHash && p.IsAlive);
            string[] menu;byte[] prices;int opens,closes;int type=(int)DFLocation.BuildingTypes.Tavern;
            if(keeper==null || !TryReadNativeTavernMenuTemplate(out menu,out prices) ||
                !NativeInnCalendar.TryServiceHours(PlayerActivate.openHours[type],PlayerActivate.closeHours[type],out opens,out closes))return;
            var catalog=new NpcInnServiceCatalog{SettlementId=settlement,BuildingKey=source.BuildingKey,BlockIndex=source.BlockIndex,
                RecordIndex=source.RecordIndex,Climate=source.Climate,RegionIndex=location.RegionIndex,KeeperId=keeper.Id,
                KeeperHash=keeper.NativeHash,CapturedAt=now,OpenMinute=opens,CloseMinute=closes,
                EntryToCounter=source.EntryToCounter,Pairs=source.Pairs};
            for(int i=0;i<menu.Length;i++)catalog.Menu.Add(new NpcInnMenuItem{MenuIndex=i,ProductName=menu[i],BasePriceGold=prices[i],
                Kind=i<4?NpcInnConsumptionKind.Drink:NpcInnConsumptionKind.Food});
            store.RegisterNpcInnServiceCatalog(NpcInnServiceCatalog.Seal(catalog));
        }
        private IEnumerator innCatalogCapture;
        private ShopVisitGeometry innCatalogGeometry;
        private WorldStateStore innCatalogStore;
        private object innCatalogEpoch;
        private bool innCatalogAttempted;

        private void ResetInnServiceCatalogCapture()
        {
            (innCatalogCapture as IDisposable)?.Dispose();innCatalogCapture=null;
            innCatalogGeometry=null;innCatalogStore=null;innCatalogEpoch=null;innCatalogAttempted=false;
        }
        private void UpdateInnServiceCatalog(double now)
        {
            if(!ReferenceEquals(innCatalogGeometry,shopGeometry) || !ReferenceEquals(innCatalogStore,worldStore) ||
                worldStore!=null && !worldStore.IsNpcVitalsWriteEpochCurrent(innCatalogEpoch))
            {
                ResetInnServiceCatalogCapture();innCatalogGeometry=shopGeometry;innCatalogStore=worldStore;
                innCatalogEpoch=worldStore==null?null:worldStore.CaptureNpcVitalsWriteEpoch();
            }
            if(!interior || shopGeometry==null || !shopGeometry.IsTavern || worldStore==null || GameManager.IsGamePaused)return;
            if(!innCatalogAttempted)
            {
                var keeper=KeeperPerson(shopGeometry.TavernKeeper);
                if(keeper==null || !keeper.IsAlive)return;
                innCatalogAttempted=true;
                var room=interior;var activities=shopGeometry;var store=worldStore;var epoch=innCatalogEpoch;
                innCatalogCapture=CaptureInnServiceCatalog(store,room,activities,keeper,now,()=>this && enabled &&
                    ReferenceEquals(interior,room) && ReferenceEquals(shopGeometry,activities) && ReferenceEquals(worldStore,store) &&
                    store.IsNpcVitalsWriteEpochCurrent(epoch));
            }
            if(innCatalogCapture==null)return;
            try
            {
                if(!innCatalogCapture.MoveNext()){(innCatalogCapture as IDisposable)?.Dispose();innCatalogCapture=null;}
            }
            catch(Exception error)
            {
                (innCatalogCapture as IDisposable)?.Dispose();innCatalogCapture=null;
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_INN_CATALOG_UNAVAILABLE: "+error.Message);
            }
        }
        internal static IEnumerator CaptureInnServiceCatalog(WorldStateStore store,DaggerfallInterior room,
            ShopVisitGeometry activities,NativePerson keeper,double now,Func<bool> stillValid)
        {
            if(store==null || activities==null || stillValid==null || !stillValid() || !activities.OwnsInnServiceSource(room,keeper))yield break;
            object epoch=store.CaptureNpcVitalsWriteEpoch();
            var door=room.EntryDoor;int region=GameManager.Instance.PlayerGPS.CurrentRegionIndex;
            int climate=(int)ClimateSwaps.FromAPIClimateBase(GameManager.Instance.PlayerGPS.ClimateSettings.ClimateType);
            bool Current(){return stillValid() && store.IsNpcVitalsWriteEpochCurrent(epoch) && activities.OwnsInnServiceSource(room,keeper) &&
                room.EntryDoor.buildingKey==door.buildingKey && room.EntryDoor.blockIndex==door.blockIndex && room.EntryDoor.recordIndex==door.recordIndex &&
                GameManager.Instance.PlayerGPS.CurrentRegionIndex==region &&
                (int)ClimateSwaps.FromAPIClimateBase(GameManager.Instance.PlayerGPS.ClimateSettings.ClimateType)==climate;}
            string[] menu;byte[] prices;
            if(!TryReadNativeTavernMenu(activities.TavernKeeper,out menu,out prices))yield break;
            int type=(int)room.BuildingData.BuildingType;int opens,closes;
            if(type<0 || type>=PlayerActivate.openHours.Length || type>=PlayerActivate.closeHours.Length ||
                !NativeInnCalendar.TryServiceHours(PlayerActivate.openHours[type],PlayerActivate.closeHours[type],out opens,out closes))yield break;
            var draft=new NpcInnServiceCatalog{SettlementId=keeper.SettlementId,BuildingKey=room.EntryDoor.buildingKey,
                BlockIndex=room.EntryDoor.blockIndex,RecordIndex=room.EntryDoor.recordIndex,
                Climate=climate,RegionIndex=region,
                KeeperId=keeper.Id,KeeperHash=keeper.NativeHash,CapturedAt=now,OpenMinute=opens,CloseMinute=closes};
            for(int i=0;i<menu.Length;i++)draft.Menu.Add(new NpcInnMenuItem{MenuIndex=i,ProductName=menu[i],BasePriceGold=prices[i],
                Kind=i<4?NpcInnConsumptionKind.Drink:NpcInnConsumptionKind.Food});
            var capture=activities.CaptureInnServiceGeometry(draft,Current);
            try{while(capture.MoveNext())yield return capture.Current;}
            finally{(capture as IDisposable)?.Dispose();}
            if(!Current() || draft.Pairs.Count==0)yield break;
            store.RegisterNpcInnServiceCatalog(NpcInnServiceCatalog.Seal(draft));
        }
    }
}
