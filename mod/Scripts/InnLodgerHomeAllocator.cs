using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    // A reservation adapter only: authored flats retain native identity and
    // placement. No second resident actor, checkout teleport or remote scene.
    public sealed class InnLodgerHomeAllocator : MonoBehaviour
    {
        private Func<WorldStateStore> getStore;
        private Func<bool> available;
        private NativeTavernRentals rentals;
        private bool running;
        private float nextCheck;
        public void Install(Func<WorldStateStore> store,Func<bool> enabled,NativeTavernRentals nativeRentals)
        {getStore=store;available=enabled;rentals=nativeRentals;}
        private void OnDisable() {StopAllCoroutines();running=false;}
        private void Update()
        {
            if(getStore==null || available==null || !available() || running || Time.unscaledTime<nextCheck) return;
            nextCheck=Time.unscaledTime+10;
            var game=GameManager.Instance;if(!game || !game.IsReady || !game.IsPlayingGame() || !game.PlayerEnterExit.IsPlayerInsideBuilding) return;
            var room=game.PlayerEnterExit.Interior;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();BuildingSummary building;
            if(!room || !directory || !directory.GetBuildingSummary(room.EntryDoor.buildingKey,out building) || building.BuildingType!=DFLocation.BuildingTypes.Tavern) return;
            StartCoroutine(Allocate(getStore(),directory,room));
        }
        public IEnumerator Allocate(WorldStateStore store,BuildingDirectory directory,DaggerfallInterior room)
        {
            if(running || store==null || !directory || !room) yield break;
            running=true;
            try
            {
                var game=GameManager.Instance;
                Func<bool> valid=()=>available()!=false && getStore()==store && room && directory && game.PlayerEnterExit.Interior==room &&
                    game.StreamingWorld.GetCurrentBuildingDirectory()==directory;
                var candidates=new List<StaticNPC>(room.GetComponentsInChildren<StaticNPC>(true));
                candidates.Sort((a,b)=>a.Data.hash.CompareTo(b.Data.hash));
                bool any=false;
                foreach(var npc in candidates) if(Eligible(store,directory,room,npc)!=null) {any=true;break;}
                if(!any) yield break;
                var catalog=new TravelOriginCatalog.Result();yield return TravelOriginCatalog.Read(catalog,valid);
                if(!valid() || catalog.Sites==null) yield break;
                var target=catalog.Sites.Find(s=>s.Settlement==directory.MapID);if(target==null) yield break;
                var pool=new VisitorOriginPool(target,catalog.Sites);
                foreach(var npc in candidates)
                {
                    if(!valid()) yield break;
                    var person=Eligible(store,directory,room,npc);if(person==null) continue;
                    string bedroom=rentals.BedroomFor(room,npc.Data.hash);
                    uint seed=StableSeed.Combine(store.WorldSeed,(uint)directory.MapID,(uint)room.EntryDoor.buildingKey,unchecked((uint)npc.Data.hash));
                    var excluded=new HashSet<int>();bool reserved=false;
                    for(uint attempt=0;attempt<6 && !reserved;attempt++)
                    {
                        if(!valid() || !ReferenceEquals(person,store.NativePeopleFor(directory.MapID).Find(p=>p.Id==person.Id))) yield break;
                        VisitorOriginBand band;var origin=pool.Pick(StableSeed.Combine(seed,attempt,0x686f6d65,0),new VisitorOriginWeights(),out band,excluded);
                        if(origin==null) continue;excluded.Add(origin.Settlement);
                        yield return BackgroundWork.Wait(valid);if(!valid()) yield break;
                        DFLocation location;
                        if(!DaggerfallUnity.Instance.ContentReader.GetLocation(origin.Region,origin.LocationIndex,out location)) continue;
                        var blocks=RMBLayout.GetLocationBuildingData(location);
                        int width=location.Exterior.ExteriorData.Width;
                        var homes=new List<BuildingSummary>();
                        for(int i=0;i<blocks.Length;i++)
                        {
                            var summaries=RMBLayout.GetBuildingData(blocks[i],i%width,i/width);
                            if(summaries!=null) foreach(var home in summaries)
                                if(home.BuildingType>=DFLocation.BuildingTypes.House1 && home.BuildingType<=DFLocation.BuildingTypes.House6 &&
                                    HousingSurvey.AvailableForResidents(location,home)) homes.Add(home);
                            yield return null;if(!valid()) yield break;
                        }
                        homes.Sort((a,b)=>StableSeed.Combine(seed,(uint)a.buildingKey,attempt,0).CompareTo(StableSeed.Combine(seed,(uint)b.buildingKey,attempt,0)));
                        foreach(var home in homes)
                        {
                            if(!valid() || !person.IsAlive) yield break;
                            if(store.HousingReservedForRecovery(origin.Settlement,home.buildingKey)) continue;
                            var known=store.CurrentPopulation(origin.Settlement);
                            var existing=known?.Households.Find(h=>h.BuildingKey==home.buildingKey);
                            if(existing!=null && existing.Residents.Count+store.HouseholdInnLodgers(origin.Settlement,home.buildingKey).Count>=existing.BedCapacity) continue;
                            yield return BackgroundWork.Wait(valid);if(!valid()) yield break;
                            var dfu=DaggerfallUnity.Instance;
                            var profiles=HousingSurvey.Read(dfu.ContentReader,location,new[]{home},dfu.MeshReader);
                            if(profiles.Count==1 && profiles[0].BedCount>0 && Eligible(store,directory,room,npc)==person)
                            {
                                var result=store.TryReserveInnLodgerHome(person.Id,bedroom,origin.Settlement,profiles[0],dfu.WorldTime.Now.ToSeconds());
                                if(result!=null)
                                {
                                    RegionalIdentityAdapter.Initialize(store.CurrentPopulation(origin.Settlement),location);
                                    Debug.Log("LIVING_CITIES_LODGER_HOME: "+person.Id+"; origin="+origin.Settlement+"; home="+result.HomeKey+"; slot="+result.BedSlot);
                                    reserved=true;break;
                                }
                            }
                            yield return null;
                        }
                    }
                    if(!reserved) Debug.LogWarning("LIVING_CITIES_LODGER_HOME_PENDING: "+person.Id+"; no available surveyed household slot.");
                }
            }
            finally {running=false;}
        }
        private NativePerson Eligible(WorldStateStore store,BuildingDirectory directory,DaggerfallInterior room,StaticNPC npc)
        {
            if(!npc || npc.Data.context!=StaticNPC.Context.Building || npc.Data.mapID!=directory.MapID || npc.Data.buildingKey!=room.EntryDoor.buildingKey ||
                npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>() ||
                QuestMachine.Instance.HasFactionListener(npc.Data.factionID) || NativeBusinessServices.IsKeeper(npc,DFLocation.BuildingTypes.Tavern) ||
                rentals.BedroomFor(room,npc.Data.hash)==null) return null;
            FactionFile.FactionData faction;
            if(GameManager.Instance.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction) && faction.type==(int)FactionFile.FactionTypes.Individual) return null;
            var person=store.NativePeopleFor(directory.MapID).Find(p=>p.WorkKey==npc.Data.buildingKey && p.NativeHash==npc.Data.hash);
            return person!=null && person.IsAlive && person.HomeKey<0 && store.InnLodgerHome(person.Id)==null ? person : null;
        }
    }
}
