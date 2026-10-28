using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class TravelerIdentityAdapter
    {
        private static int cachedTown=-1,cachedRegion=-1;
        private static List<TravelerOrigin> origins;
        private static readonly Dictionary<int,IReadOnlyList<RoutePoint>> originRoads=new Dictionary<int,IReadOnlyList<RoutePoint>>();
        private static readonly Dictionary<int,DFLocation> originLocations=new Dictionary<int,DFLocation>();
        public static bool OriginsReady(int town)
        {return origins!=null && cachedTown==town && cachedRegion==GameManager.Instance.PlayerGPS.CurrentRegionIndex;}
        public static bool HasVerifiedRoad(int origin)
        {return originRoads.ContainsKey(origin);}
        public static IEnumerator PrepareOrigins(int town,Func<bool> stillCurrent)
        {
            if(OriginsReady(town))yield break;
            int region=GameManager.Instance.PlayerGPS.CurrentRegionIndex;
            var next=new List<TravelerOrigin>();var paths=new Dictionary<int,IReadOnlyList<RoutePoint>>();
            yield return ReadOrigins(town,next,paths,stillCurrent);
            if(!stillCurrent())yield break;
            origins=next;cachedTown=town;cachedRegion=region;originRoads.Clear();
            foreach(var pair in paths)originRoads.Add(pair.Key,pair.Value);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ORIGINS: town="+town+"; origins="+origins.Count+"; verifiedBasicRoads="+originRoads.Count);
        }
        public static TravelerProposal Create(uint seed,int town,int tavern,int day,int slot,TownGeometry.TravelerAccess access)
        {
            var game=GameManager.Instance;
            if(!OriginsReady(town))throw new InvalidOperationException("Traveler road origins are not prepared.");
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
        public static TravelerProposal Create(uint seed,int town,int tavern,int day,int slot,TownGeometry.TravelerAccess access,WorldStateStore store)
        {
            var proposal=Create(seed,town,tavern,day,slot,access);
            DFLocation origin;
            if(!originLocations.TryGetValue(proposal.Person.HomeSettlement,out origin)) return null;
            var journey=TravelerJourney.Create(proposal.Person.AppearanceSeed,proposal.Visit.DesiredArrival,proposal.Visit.Nights,
                proposal.Visit.CommuteSeconds,proposal.Visit.Walled,access.Points);
            var resident=UnloadedPopulation.ReserveTravelerOrigin(origin,store,proposal.Person.AppearanceSeed,journey.EntryAt,journey.ExitAt+proposal.Visit.NativeReturnSeconds);
            if(resident==null) return null;
            uint arrivalSeed=proposal.Person.AppearanceSeed;
            TravelerDemand.BindOrigin(store,proposal,resident);
            proposal.Visit.CommuteSeconds=proposal.Visit.CommuteSeconds*ResidentMovement.WalkSpeedFor(arrivalSeed)/
                ResidentMovement.WalkSpeedFor(resident.AppearanceSeed);
            var actual=TravelerJourney.Create(resident.AppearanceSeed,proposal.Visit.DesiredArrival,proposal.Visit.Nights,
                proposal.Visit.CommuteSeconds,proposal.Visit.Walled,access.Points);
            if(store.IsResidentAwayDuring(resident.Id,actual.EntryAt,actual.ExitAt+proposal.Visit.NativeReturnSeconds)) return null;
            return proposal;
        }
        private static IEnumerator ReadOrigins(int town,List<TravelerOrigin> result,
            Dictionary<int,IReadOnlyList<RoutePoint>> paths,Func<bool> stillCurrent)
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
            var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            OverlandRoadNetwork network=null;
            bool useRoads=host && host.RoadNetwork.TryGet(out network);
            var destinationPixel=MapsFile.LongitudeLatitudeToMapPixel(destination.Longitude,destination.Latitude);
            int attempted=0;
            foreach(int index in candidates)
            {
                if(!stillCurrent())yield break;
                if(attempted++>=32)break;
                DFLocation location;
                if(!reader.GetLocation(region.Name,region.MapNames[index],out location)) continue;
                if(useRoads)
                {
                    var pixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
                    var search=network.BeginSearch(pixel.X,pixel.Y,destinationPixel.X,destinationPixel.Y,4096);
                    try
                    {
                        while(!search.Complete)
                        {
                            if(!stillCurrent())yield break;
                            search.Advance(64);
                            if(!search.Complete)yield return null;
                        }
                        if(search.Result.Count<2)continue;
                        paths.Add(location.MapTableData.MapId,search.Result);
                    }
                    finally {search.Cancel();}
                }
                var people=location.Climate.People;
                var returnPixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
                var nativeTime=new TravelTimeCalculator();int returnMinutes=nativeTime.CalculateTravelTime(returnPixel,true,false,false,false,false);
                if(returnMinutes<1 || nativeTime.OceanPixels>0)continue;
                result.Add(new TravelerOrigin { SettlementId=location.MapTableData.MapId,
                    NativeReturnSeconds=returnMinutes*60d,
                    Race=(int)(people==FactionFile.FactionRaces.Redguard ? Races.Redguard :
                        people==FactionFile.FactionRaces.Nord ? Races.Nord : Races.Breton) });
                originLocations[location.MapTableData.MapId]=location;
                if(result.Count==16) break;
                yield return null;
            }
        }
    }
}
