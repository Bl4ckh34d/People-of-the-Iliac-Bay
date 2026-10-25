using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private WorldStateStore travelerDemandStore;
        private object travelerDemandEpoch;
        private BuildingDirectory travelerDemandDirectory;
        private int travelerDemandDay=-1,travelerDemandNextInn;
        private float nextTravelerDemandAt;
        private readonly List<int> travelerDemandInns=new List<int>();
        private readonly HashSet<int> travelerDemandCompleted=new HashSet<int>();
        private readonly Dictionary<int,List<TownGeometry.TravelerAccess>> travelerDemandRoutes=
            new Dictionary<int,List<TownGeometry.TravelerAccess>>();

        private void EnsureInnTravelerDemand(double seconds)
        {
            if(!EntranceSurveyReady || !directory || geometry==null || worldStore==null || population==null ||
                interior || GameManager.Instance.IsPlayerInside || Time.unscaledTime<nextTravelerDemandAt)return;
            nextTravelerDemandAt=Time.unscaledTime+1;
            if(!ReferenceEquals(travelerDemandStore,worldStore) || travelerDemandDirectory!=directory ||
                !worldStore.IsNpcVitalsWriteEpochCurrent(travelerDemandEpoch))
            {
                travelerDemandStore=worldStore;travelerDemandDirectory=directory;
                travelerDemandEpoch=worldStore.CaptureNpcVitalsWriteEpoch();travelerDemandDay=-1;travelerDemandNextInn=0;
                travelerDemandInns.Clear();travelerDemandCompleted.Clear();travelerDemandRoutes.Clear();
                foreach(var inn in directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern))
                    travelerDemandInns.Add(inn.buildingKey);
                travelerDemandInns.Sort();
            }
            int day=(int)Math.Floor(seconds/86400d);
            if(travelerDemandDay!=day)
            {travelerDemandDay=day;travelerDemandCompleted.Clear();travelerDemandNextInn=0;}
            if(travelerDemandInns.Count==0 || travelerDemandCompleted.Count==travelerDemandInns.Count)return;
            int key=travelerDemandInns[travelerDemandNextInn++%travelerDemandInns.Count];
            if(travelerDemandCompleted.Contains(key))return;
            List<TownGeometry.TravelerAccess> routes;
            if(!travelerDemandRoutes.TryGetValue(key,out routes))
            {
                if(!BackgroundWork.TryStart())return;
                routes=geometry.SurveyTravelerRoutes(key);
                // Closed gates can temporarily remove native routes. Retry on
                // later refreshes rather than save a fabricated approach.
                if(routes.Count==0)return;
                travelerDemandRoutes.Add(key,routes);
            }
            try
            {
                TravelerDemand.EnsureInnTravelers(worldStore,population.SettlementId,key,day,(arrivalDay,slot)=>
                    TravelerIdentityAdapter.Create(worldStore.WorldSeed,population.SettlementId,key,arrivalDay,slot,
                        routes[(int)(StableSeed.Combine(worldStore.WorldSeed,(uint)key,(uint)arrivalDay,(uint)slot)%(uint)routes.Count)]));
                travelerDemandCompleted.Add(key);
            }
            catch(Exception error)
            {
                travelerDemandCompleted.Add(key);
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_DEMAND_UNAVAILABLE: "+population.SettlementId+":"+key+": "+error.Message);
            }
        }
    }
}
