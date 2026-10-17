using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    internal static class InnLodgerHomeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        public static IEnumerator Run(LivingCitiesMod host,StreetPopulation streets,string character)
        {
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            int slot=game.SaveLoadManager.FindSaveFolderByNames(character,"People - Shop entrance");
            if(slot<0) throw new InvalidOperationException("Lodger home probe lacks fixture.");
            bool loaded=false;SaveLoadManager.OnLoadEventHandler handler=data=>loaded=true;SaveLoadManager.OnLoad+=handler;
            try
            {
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();float until=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<until) yield return null;
                if(!loaded || !streets.Ready) throw new InvalidOperationException("Fixture load timed out.");
            }
            finally {SaveLoadManager.OnLoad-=handler;}
            var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",Private).GetValue(host);
            var rentals=(NativeTavernRentals)typeof(LivingCitiesMod).GetField("nativeRentals",Private).GetValue(host);
            var allocator=host.GetComponent<InnLodgerHomeAllocator>();allocator.enabled=false;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();var doors=new List<KeyValuePair<Transform,StaticDoor>>();
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>()) foreach(var door in owner.Doors)
            {
                BuildingSummary summary;
                if(door.doorType==DoorTypes.Building && directory.GetBuildingSummary(door.buildingKey,out summary) &&
                    summary.BuildingType==DFLocation.BuildingTypes.Tavern) doors.Add(new KeyValuePair<Transform,StaticDoor>(owner.transform,door));
            }
            InnLodgerHomeReservation chosen=null;NativePerson person=null;int mainHall=0;
            foreach(var entry in doors)
            {
                if(game.PlayerEnterExit.IsPlayerInside) {game.PlayerEnterExit.TransitionExterior();yield return new WaitForSecondsRealtime(1);}
                TownSmokeHarness.EnterInterior(entry.Key,entry.Value);yield return new WaitForSecondsRealtime(2);DaggerfallUI.Instance.PopToHUD();
                var room=game.PlayerEnterExit.Interior;rentals.Refresh(store,directory);
                var hallIds=new List<string>();
                foreach(var npc in room.GetComponentsInChildren<StaticNPC>(true))
                    if(rentals.BedroomFor(room,npc.Data.hash)==null)
                        hallIds.Add("npc:"+npc.Data.mapID+":"+npc.Data.buildingKey+":"+npc.Data.hash);
                yield return allocator.Allocate(store,directory,room);
                foreach(var id in hallIds) {if(store.InnLodgerHome(id)!=null) throw new InvalidOperationException("Main-hall person acquired a foreign home.");mainHall++;}
                foreach(var candidate in store.NativePeopleFor(directory.MapID))
                {
                    var home=store.InnLodgerHome(candidate.Id);
                    if(home!=null && home.InnKey==entry.Value.buildingKey) {chosen=home;person=candidate;break;}
                }
                if(chosen!=null) break;
            }
            if(chosen==null || person==null) throw new InvalidOperationException("No ordinary inn-bedroom lodger obtained a permanent household reservation.");
            var catalog=new TravelOriginCatalog.Result();yield return TravelOriginCatalog.Read(catalog,()=>true);
            var origin=catalog.Sites.Find(s=>s.Settlement==chosen.HomeSettlement);
            DFLocation location;if(origin==null || !DaggerfallUnity.Instance.ContentReader.GetLocation(origin.Region,origin.LocationIndex,out location))
                throw new InvalidOperationException("Reserved origin not found in native map.");
            var dfu=DaggerfallUnity.Instance;var blocks=RMBLayout.GetLocationBuildingData(location);int width=location.Exterior.ExteriorData.Width;
            var homes=new List<HomeProfile>();BuildingSummary address=default(BuildingSummary);bool found=false;
            for(int i=0;i<blocks.Length;i++)
            {
                var summaries=RMBLayout.GetBuildingData(blocks[i],i%width,i/width);var batch=new List<BuildingSummary>();
                foreach(var summary in summaries)
                    if(summary.BuildingType>=DFLocation.BuildingTypes.House1 && summary.BuildingType<=DFLocation.BuildingTypes.House6)
                    {batch.Add(summary);if(summary.buildingKey==chosen.HomeKey) {address=summary;found=true;}}
                homes.AddRange(HousingSurvey.Read(dfu.ContentReader,location,batch,dfu.MeshReader));yield return null;
            }
            if(!found || !HousingSurvey.AvailableForResidents(location,address)) throw new InvalidOperationException("Lodger address is not an available residential building.");
            var deeds=DaggerfallBankManager.Houses;var previous=deeds[location.RegionIndex];
            try
            {
                deeds[location.RegionIndex]=new HouseData_v1 {regionIndex=location.RegionIndex,mapID=chosen.HomeSettlement,buildingKey=chosen.HomeKey};
                if(HousingSurvey.AvailableForResidents(location,address)) throw new InvalidOperationException("Player-owned property accepted residents.");
            }
            finally {deeds[location.RegionIndex]=previous;}
            var forSale=address;forSale.BuildingType=DFLocation.BuildingTypes.HouseForSale;
            if(HousingSurvey.AvailableForResidents(location,forSale)) throw new InvalidOperationException("Bank-sale property accepted residents.");
            store.GetOrCreateFamilies(chosen.HomeSettlement,homes);RegionalIdentityAdapter.Initialize(store.CurrentPopulation(chosen.HomeSettlement),location);
            var saved=store.Snapshot();var json=SaveLoadManager.Serialize(typeof(WorldState),saved);
            var reload=new WorldStateStore(1);reload.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
            var restored=reload.InnLodgerHome(person.Id);
            if(restored==null || restored.HomeKey!=chosen.HomeKey || restored.BedSlot!=chosen.BedSlot ||
                reload.NativePeopleFor(chosen.InnSettlement).Find(p=>p.Id==person.Id).Name!=person.Name ||
                !reload.IsResidentAway(person.Id,dfu.WorldTime.Now.ToSeconds()) || reload.CurrentPopulation(chosen.HomeSettlement).IsPartial)
                throw new InvalidOperationException("Native serialization or hometown completion lost the reservation.");
            foreach(var family in reload.CurrentPopulation(chosen.HomeSettlement).Households)
                if(family.Residents.Exists(r=>r.Id==person.Id)) throw new InvalidOperationException("An inn lodger also exists as a hometown resident actor.");
            typeof(LivingCitiesMod).GetField("conversationPersonId",Private).SetValue(host,person.Id);
            int town=0,key=0;string name=null;object[] args={town,key,name};
            if(!(bool)typeof(LivingCitiesMod).GetMethod("TryConversationHome",Private).Invoke(host,args) || (int)args[0]!=chosen.HomeSettlement || (int)args[1]!=chosen.HomeKey)
                throw new InvalidOperationException("Conversation does not know the exact reserved hometown and house.");
            Debug.Log("LIVING_CITIES_INN_LODGER_HOME_PASS: "+person.Name+"; native="+person.Id+"; hometown="+chosen.HomeSettlement+"; home="+chosen.HomeKey+"; slot="+chosen.BedSlot+
                "; mainHallExcluded="+mainHall+"; native serialize/restore; completed hometown; no duplicate home resident; home context; bank/player property excluded.");
        }
    }
}
