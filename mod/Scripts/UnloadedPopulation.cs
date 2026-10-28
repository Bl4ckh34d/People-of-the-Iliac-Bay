using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.DFU
{
    public static class UnloadedPopulation
    {
        public sealed class Result {public SettlementPopulation Population;}
        public sealed class DemandResult {public SettlementPopulation Population;public List<ResidentTravelProposal> Proposals;}
        public static IEnumerator ReserveDemand(DFLocation location,UnloadedSettlementSurvey town,WorldStateStore store,int day,
            IEnumerable<int> destinations,DemandResult result,Func<bool> valid,int chancePerThousand=10,int maximumDepartures=8)
        {
            if(town==null || store==null || result==null || valid==null || location.MapTableData.MapId!=town.SettlementId)
                throw new ArgumentException("Invalid reserved departure context.");
            result.Population=null;result.Proposals=null;
            yield return BackgroundWork.Wait(valid);if(!valid()) yield break;
            if(store.HasSettlement(town.SettlementId))
            {
                var existing=store.GetOrCreate(town.SettlementId,new int[0]);
                if(!existing.IsPartial)
                {
                    result.Population=existing;RegionalIdentityAdapter.Initialize(existing,location);
                    result.Proposals=store.ResidentTravelProposals(town.SettlementId,day,destinations,chancePerThousand,maximumDepartures);
                    yield break;
                }
            }
            var dfu=DaggerfallUnity.Instance;var profiles=new List<HomeProfile>();var candidates=new List<BuildingSummary>();
            foreach(var building in town.Buildings)
            {
                if(building.BuildingType<DFLocation.BuildingTypes.House1 || building.BuildingType>DFLocation.BuildingTypes.House6) continue;
                candidates.Add(building);
            }
            uint sample=StableSeed.Combine(store.WorldSeed,(uint)town.SettlementId,(uint)day,0x686f6d65);
            candidates.Sort((a,b)=>{int order=StableSeed.Combine(sample,(uint)a.buildingKey,0,0).CompareTo(StableSeed.Combine(sample,(uint)b.buildingKey,0,0));
                return order!=0 ? order : a.buildingKey.CompareTo(b.buildingKey);});
            for(int index=0;index<Math.Min(maximumDepartures,candidates.Count);index++)
            {
                profiles.AddRange(HousingSurvey.Read(dfu.ContentReader,location,new[]{candidates[index]},dfu.MeshReader));
                yield return null;if(!valid()) yield break;
            }
            if(!valid()) yield break;
            result.Proposals=store.ReserveResidentTravelProposals(town.SettlementId,day,profiles,destinations,chancePerThousand,maximumDepartures);
            if(store.HasSettlement(town.SettlementId))
            {
                result.Population=store.GetOrCreate(town.SettlementId,new int[0]);
                RegionalIdentityAdapter.Initialize(result.Population,location);
            }
        }
        // Inn guests need one actual family, not a collision survey of their
        // entire origin. Read at most eight native blocks and eight interiors.
        public static Resident ReserveTravelerOrigin(DFLocation location,WorldStateStore store,uint seed,double from,double until)
        {
            int town=location.MapTableData.MapId;
            if(store.HasSettlement(town))
            {
                var existing=store.GetOrCreate(town,new int[0]);
                var available=new List<Resident>();
                foreach(var home in existing.Households) foreach(var person in home.Residents)
                    if(EligibleOrigin(store,person,from,until)) available.Add(person);
                if(available.Count>0) {RegionalIdentityAdapter.Initialize(existing,location);return available[(int)(seed%(uint)available.Count)];}
                if(!existing.IsPartial) return null;
            }
            var dfu=DaggerfallUnity.Instance;var reader=dfu.ContentReader;
            int width=location.Exterior.ExteriorData.Width,height=location.Exterior.ExteriorData.Height;
            if(width<1 || height<1) return null;
            int count=width*height,start=(int)(seed%(uint)count),interiors=0;
            var cached=IncrementalLocationBlocks.Cached(location);
            var blockName=reader.MapFileReader.GetType().GetMethod("GetRmbBlockName");
            if(blockName==null) throw new MissingMethodException("DFU block-name API changed.");
            for(int attempt=0;attempt<Math.Min(8,count) && interiors<8;attempt++)
            {
                int index=(start+attempt)%count,x=index%width,y=index/width;
                DFBlock block;
                if(cached!=null) block=cached[index];
                else
                {
                    string name=reader.BlockFileReader.CheckName((string)blockName.Invoke(reader.MapFileReader,new object[]{location,x,y}));
                    reader.GetBlock(name,out block);
                }
                var summaries=RMBLayout.GetBuildingData(block,x,y);if(summaries==null) continue;
                var homes=new List<BuildingSummary>();
                foreach(var home in summaries)
                    if(home.BuildingType>=DFLocation.BuildingTypes.House1 && home.BuildingType<=DFLocation.BuildingTypes.House6) homes.Add(home);
                homes.Sort((a,b)=>StableSeed.Combine(seed,(uint)a.buildingKey,0,0).CompareTo(StableSeed.Combine(seed,(uint)b.buildingKey,0,0)));
                foreach(var home in homes)
                {
                    if(interiors++>=8) break;
                    var profiles=HousingSurvey.Read(reader,location,new[]{home},dfu.MeshReader);
                    if(profiles.Count!=1 || profiles[0].BedCount<=0 || !profiles[0].AvailableForResidents) continue;
                    Household reserved;
                    try {reserved=store.ReserveHousehold(town,profiles[0]);}
                    catch(ArgumentException) {continue;}
                    RegionalIdentityAdapter.Initialize(new SettlementPopulation {SettlementId=town,Households=new List<Household>{reserved}},location);
                    foreach(var person in reserved.Residents) if(EligibleOrigin(store,person,from,until)) return person;
                }
            }
            return null;
        }
        private static bool EligibleOrigin(WorldStateStore store,Resident person,double from,double until)
        {return person.IsAlive && !person.IsQuestProtected && person.NativeIdentity==null && person.AgeGroup==ResidentAgeGroup.Adult &&
            !store.IsResidentAwayDuring(person.Id,from,until);}
        public static IEnumerator ReserveHome(DFLocation location,UnloadedSettlementSurvey town,int buildingKey,WorldStateStore store,Result result,Func<bool> valid)
        {
            if(town==null || store==null || result==null || valid==null || location.MapTableData.MapId!=town.SettlementId)
                throw new ArgumentException("Invalid reserved hometown context.");
            result.Population=null;
            yield return BackgroundWork.Wait(valid);
            if(!valid()) yield break;
            foreach(var building in town.Buildings)
            {
                if(building.buildingKey!=buildingKey || building.BuildingType<DFLocation.BuildingTypes.House1 ||
                    building.BuildingType>DFLocation.BuildingTypes.House6) continue;
                var dfu=DaggerfallUnity.Instance;
                var profiles=HousingSurvey.Read(dfu.ContentReader,location,new[]{building},dfu.MeshReader);
                if(profiles.Count!=1 || profiles[0].BedCount<=0) yield break;
                if(!valid()) yield break;
                store.ReserveHousehold(town.SettlementId,profiles[0]);
                // The existing partial population is returned without completing
                // unrelated households or changing the player's loaded scene.
                result.Population=store.GetOrCreate(town.SettlementId,new int[0]);
                RegionalIdentityAdapter.Initialize(result.Population,location);
                yield break;
            }
        }
    }
}
