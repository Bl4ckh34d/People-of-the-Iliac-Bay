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
        private Coroutine travelerOriginPreparation;
        private System.Collections.IEnumerator PrepareTravelerOrigins(int town,BuildingDirectory owner,WorldStateStore store,object epoch)
        {
            try
            {
                yield return TravelerIdentityAdapter.PrepareOrigins(town,()=>this && directory==owner &&
                    ReferenceEquals(worldStore,store) && store.IsNpcVitalsWriteEpochCurrent(epoch));
            }
            finally {travelerOriginPreparation=null;}
        }
        private readonly List<int> travelerDemandInns=new List<int>();
        private readonly HashSet<int> travelerDemandCompleted=new HashSet<int>();
        private readonly Dictionary<int,List<TownGeometry.TravelerAccess>> travelerDemandRoutes=
            new Dictionary<int,List<TownGeometry.TravelerAccess>>();
        private static readonly bool travelerDemandProbeEnabled=
            Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="dungeon-follow" &&
            Environment.GetEnvironmentVariable("POTIB_DUNGEON_FOLLOW_PREPARE")=="1";
        private sealed class TravelerDemandProbeCounts
        {internal int GateDenials,RouteAttempts,EmptyRoutes;}
        private readonly Dictionary<int,TravelerDemandProbeCounts> travelerDemandProbeCounts=
            new Dictionary<int,TravelerDemandProbeCounts>();

        // Observes only existing scheduled work. Entries are bounded by actual Inns
        // and cleared with the demand owner; disabled outside explicit fixture prep.
        private TravelerDemandProbeCounts ObserveTravelerDemand(int key)
        {
            if(!travelerDemandProbeEnabled)return null;
            TravelerDemandProbeCounts counts;
            if(!travelerDemandProbeCounts.TryGetValue(key,out counts))
            {counts=new TravelerDemandProbeCounts();travelerDemandProbeCounts.Add(key,counts);}
            return counts;
        }

        internal string TravelerDemandProbeStatus(int key)
        {
            int denied=0,attempted=0,empty=0;
            foreach(var counts in travelerDemandProbeCounts.Values)
            {denied+=counts.GateDenials;attempted+=counts.RouteAttempts;empty+=counts.EmptyRoutes;}
            TravelerDemandProbeCounts target;
            travelerDemandProbeCounts.TryGetValue(key,out target);
            return "; demandCountersEnabled="+travelerDemandProbeEnabled+
                "; gateDenials="+denied+"; routeAttempts="+attempted+"; emptyRoutes="+empty+
                "; targetGateDenials="+(target==null ? 0 : target.GateDenials)+
                "; targetRouteAttempts="+(target==null ? 0 : target.RouteAttempts)+
                "; targetEmptyRoutes="+(target==null ? 0 : target.EmptyRoutes);
        }

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
                travelerDemandProbeCounts.Clear();
                foreach(var inn in directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern))
                    travelerDemandInns.Add(inn.buildingKey);
                travelerDemandInns.Sort();
            }
            int day=(int)Math.Floor(seconds/86400d);
            if(!TravelerIdentityAdapter.OriginsReady(population.SettlementId))
            {
                if(travelerOriginPreparation==null)
                    travelerOriginPreparation=StartCoroutine(PrepareTravelerOrigins(population.SettlementId,directory,worldStore,travelerDemandEpoch));
                return;
            }
            if(travelerDemandDay!=day)
            {travelerDemandDay=day;travelerDemandCompleted.Clear();travelerDemandNextInn=0;}
            if(travelerDemandInns.Count==0 || travelerDemandCompleted.Count==travelerDemandInns.Count)return;
            int key=travelerDemandInns[travelerDemandNextInn++%travelerDemandInns.Count];
            if(travelerDemandCompleted.Contains(key))return;
            List<TownGeometry.TravelerAccess> routes;
            if(!travelerDemandRoutes.TryGetValue(key,out routes))
            {
                var counts=ObserveTravelerDemand(key);
                if(!BackgroundWork.TryStart())
                {if(counts!=null)counts.GateDenials++;return;}
                if(counts!=null)counts.RouteAttempts++;
                routes=geometry.SurveyTravelerRoutes(key);
                // Closed gates can temporarily remove native routes. Retry on
                // later refreshes rather than save a fabricated approach.
                if(routes.Count==0)
                {if(counts!=null)counts.EmptyRoutes++;return;}
                travelerDemandRoutes.Add(key,routes);
            }
            try
            {
                TravelerDemand.EnsureInnTravelers(worldStore,population.SettlementId,key,day,(arrivalDay,slot)=>
                    TravelerIdentityAdapter.Create(worldStore.WorldSeed,population.SettlementId,key,arrivalDay,slot,
                        routes[(int)(StableSeed.Combine(worldStore.WorldSeed,(uint)key,(uint)arrivalDay,(uint)slot)%(uint)routes.Count)],worldStore));
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
