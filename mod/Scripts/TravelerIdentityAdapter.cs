using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using LivingCities.Core;

namespace LivingCities.DFU
{
    public static class TravelerIdentityAdapter
    {
        private static int cachedTown=-1,cachedRegion=-1;
        private static List<TravelerOrigin> origins;
        public static TravelerProposal Create(uint seed,int town,int tavern,int day,int slot,TownGeometry.TravelerAccess access)
        {
            var game=GameManager.Instance;
            if(origins==null || cachedTown!=town || cachedRegion!=game.PlayerGPS.CurrentRegionIndex)
            {
                var next=ReadOrigins(town);
                origins=next; cachedTown=town; cachedRegion=game.PlayerGPS.CurrentRegionIndex;
            }
            uint previous=DFRandom.Seed;
            try
            {
                var proposal=TravelerDemand.Create(seed,town,tavern,day,slot,origins,1,4,access.Walled,access.Points,
                    DaggerfallUnity.Instance.WorldTime.TimeScale/1.3,(origin,appearance,sex)=> {
                        var bank=origin.Race==(int)Races.Redguard ? NameHelper.BankTypes.Redguard :
                            origin.Race==(int)Races.Nord ? NameHelper.BankTypes.Nord : NameHelper.BankTypes.Breton;
                        DFRandom.Seed=appearance;
                        var names=DaggerfallUnity.Instance.NameHelper;
                        return names.FirstName(bank,sex==ResidentSex.Male ? Genders.Male : Genders.Female)+" "+names.Surname(bank);
                    });
                proposal.Visit.CommuteSeconds*=ResidentMovement.WalkSpeed/ResidentMovement.WalkSpeedFor(proposal.Person.AppearanceSeed);
                return proposal;
            }
            finally { DFRandom.Seed=previous; }
        }
        private static List<TravelerOrigin> ReadOrigins(int town)
        {
            var game=GameManager.Instance;
            var reader=DaggerfallUnity.Instance.ContentReader;
            var region=reader.MapFileReader.GetRegion(game.PlayerGPS.CurrentRegionIndex);
            var destination=game.PlayerGPS.CurrentLocation.MapTableData;
            var candidates=new List<int>();
            for(int i=0;i<region.LocationCount;i++)
                if(region.MapTable[i].MapId!=town && SettlementClassification.SupportsPopulation((int)region.MapTable[i].LocationType)) candidates.Add(i);
            candidates.Sort((a,b)=> {
                double ax=(double)region.MapTable[a].Longitude-destination.Longitude,az=(double)region.MapTable[a].Latitude-destination.Latitude;
                double bx=(double)region.MapTable[b].Longitude-destination.Longitude,bz=(double)region.MapTable[b].Latitude-destination.Latitude;
                int order=(ax*ax+az*az).CompareTo(bx*bx+bz*bz);
                return order!=0 ? order : region.MapTable[a].MapId.CompareTo(region.MapTable[b].MapId);
            });
            var result=new List<TravelerOrigin>();
            foreach(int index in candidates)
            {
                DFLocation location;
                if(!reader.GetLocation(region.Name,region.MapNames[index],out location)) continue;
                var people=location.Climate.People;
                result.Add(new TravelerOrigin { SettlementId=location.MapTableData.MapId,
                    Race=(int)(people==FactionFile.FactionRaces.Redguard ? Races.Redguard :
                        people==FactionFile.FactionRaces.Nord ? Races.Nord : Races.Breton) });
                if(result.Count==16) break;
            }
            if(result.Count==0) throw new InvalidOperationException("No nonlocal settlement origin found in region.");
            return result;
        }
    }
}
