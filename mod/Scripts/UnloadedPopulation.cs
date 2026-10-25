using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
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
            var dfu=DaggerfallUnity.Instance;var profiles=new List<HomeProfile>();var batch=new List<BuildingSummary>();
            foreach(var building in town.Buildings)
            {
                if(building.BuildingType<DFLocation.BuildingTypes.House1 || building.BuildingType>DFLocation.BuildingTypes.House6) continue;
                batch.Add(building);if(batch.Count<8) continue;
                profiles.AddRange(HousingSurvey.Read(dfu.ContentReader,location,batch,dfu.MeshReader));batch.Clear();
                yield return null;if(!valid()) yield break;
            }
            if(batch.Count>0) profiles.AddRange(HousingSurvey.Read(dfu.ContentReader,location,batch,dfu.MeshReader));
            if(!valid()) yield break;
            result.Proposals=store.ReserveResidentTravelProposals(town.SettlementId,day,profiles,destinations,chancePerThousand,maximumDepartures);
            if(store.HasSettlement(town.SettlementId))
            {
                result.Population=store.GetOrCreate(town.SettlementId,new int[0]);
                RegionalIdentityAdapter.Initialize(result.Population,location);
            }
        }
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
