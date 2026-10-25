using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private readonly Stack<IEnumerator> travelWork=new Stack<IEnumerator>();
        private readonly HashSet<int> travelPlannedTowns=new HashSet<int>();
        private int travelPlanningDay=-1,travelEpoch;
        private Func<bool> travelValid;
        private bool? travelFixture;
        private VisitorOriginWeights visitorOriginWeights=new VisitorOriginWeights();

        private void ResetTravelPlanning()
        {
            travelEpoch++;
            while(travelWork.Count>0) (travelWork.Pop() as IDisposable)?.Dispose();
            travelValid=null; travelPlanningDay=-1; travelPlannedTowns.Clear();
        }
        private void AdvanceTravelPlanning()
        {
            if(travelWork.Count==0) return;
            if(travelValid==null || !travelValid())
            {
                while(travelWork.Count>0) (travelWork.Pop() as IDisposable)?.Dispose();
                travelValid=null; return;
            }
            // Native loitering pauses rendered actors, but is an opportunity to
            // prepare tomorrow's routes. Keep the same single bounded work step.
            if(GameManager.IsGamePaused && !GameManager.Instance.PlayerEntity.IsLoitering) return;
            long started=RuntimeTiming.Start();
            string stepName=RuntimeTiming.Enabled ? "travel-step:"+travelWork.Peek().GetType().Name : null;
            try
            {
                var step=travelWork.Peek();
                if(!step.MoveNext()) {travelWork.Pop(); (step as IDisposable)?.Dispose();}
                else if(step.Current is IEnumerator) travelWork.Push((IEnumerator)step.Current);
                // Every ordinary yield returns control until a later frame.
            }
            catch(Exception error)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_TRAVEL_PLANNING_FAILED: "+error);
                while(travelWork.Count>0) (travelWork.Pop() as IDisposable)?.Dispose();
                travelValid=null;
            }
            finally {RuntimeTiming.End("resident-travel-planning-slice",started);if(stepName!=null) RuntimeTiming.End(stepName,started);}
        }
        private void BeginDailyTravel(BuildingDirectory directory)
        {
            // Scripted probes own and restore their fixture state explicitly.
            if(!travelFixture.HasValue) travelFixture=UnityEngine.Object.FindObjectOfType<TownSmokeHarness>()!=null;
            if((travelFixture.Value && !RuntimeTiming.Enabled) || travelWork.Count>0 || !streets || !streets.Ready || GameManager.IsGamePaused) return;
            int day=(int)(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()/86400)+1;
            if(travelPlanningDay!=day) {travelPlanningDay=day; travelPlannedTowns.Clear();}
            if(travelPlannedTowns.Contains(directory.MapID)) return;
            OverlandRoadNetwork available;
            if(!RoadNetwork.TryGet(out available)) return;
            var population=current; var sourceStore=store; int epoch=travelEpoch;
            travelValid=()=>travelEpoch==epoch && !saveFault && options.Enabled && streetPopulationEnabled &&
                ReferenceEquals(current,population) && ReferenceEquals(store,sourceStore) &&
                GameManager.Instance.IsPlayingGame() && GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory()==directory;
            travelWork.Push(PlanTravelNeighborhood(directory,population,sourceStore,day,travelValid));
            // Avoid retrying a permanently unsupported layout every refresh. A
            // cancelled batch may be retried on a later day or after save/load.
            travelPlannedTowns.Add(directory.MapID);
        }
        private IEnumerator PlanTravelDay(BuildingDirectory directory,SettlementPopulation population,WorldStateStore sourceStore,int day,Func<bool> valid)
        {
            yield return BackgroundWork.Wait(valid);
            if(!valid()) yield break;
            var reader=DaggerfallUnity.Instance.ContentReader.MapFileReader;
            int regionIndex=GameManager.Instance.PlayerGPS.CurrentRegionIndex;
            var targets=NearbyTravelLocations(directory.LocationData,regionIndex);
            var proposals=sourceStore.ResidentTravelProposals(directory.MapID,day,targets.Keys);
            if(proposals.Count==0) yield break;
            yield return PlanTravelBatch(directory.LocationData,population,sourceStore,proposals,
                id=>reader.GetLocation(regionIndex,targets[id]),valid);
        }
        private static Dictionary<int,int> NearbyTravelLocations(DFLocation source,int regionIndex)
        {
            var reader=DaggerfallUnity.Instance.ContentReader.MapFileReader;
            var region=reader.GetRegion(regionIndex);var origin=source.MapTableData;
            var nearby=new List<int>();
            for(int i=0;i<region.LocationCount;i++)
                if(region.MapTable[i].MapId!=origin.MapId && SettlementClassification.SupportsPopulation((int)region.MapTable[i].LocationType)) nearby.Add(i);
            nearby.Sort((a,b)=>{
                double ax=(double)region.MapTable[a].Longitude-origin.Longitude,az=(double)region.MapTable[a].Latitude-origin.Latitude;
                double bx=(double)region.MapTable[b].Longitude-origin.Longitude,bz=(double)region.MapTable[b].Latitude-origin.Latitude;
                int order=(ax*ax+az*az).CompareTo(bx*bx+bz*bz);
                return order!=0 ? order : region.MapTable[a].MapId.CompareTo(region.MapTable[b].MapId);
            });
            var targets=new Dictionary<int,int>();
            foreach(int index in nearby) {targets[region.MapTable[index].MapId]=index; if(targets.Count>=16) break;}
            return targets;
        }
        private IEnumerator PlanTravelNeighborhood(BuildingDirectory directory,SettlementPopulation population,WorldStateStore sourceStore,int day,Func<bool> valid)
        {
            yield return PlanTravelDay(directory,population,sourceStore,day,valid);
            if(!valid()) yield break;
            var catalog=new TravelOriginCatalog.Result();yield return TravelOriginCatalog.Read(catalog,valid);
            if(!valid() || catalog.Sites==null) yield break;
            var destination=catalog.Sites.Find(s=>s.Settlement==directory.MapID);
            if(destination==null) yield break;
            var pool=new VisitorOriginPool(destination,catalog.Sites);
            var reader=DaggerfallUnity.Instance.ContentReader.MapFileReader;
            var considered=new HashSet<int>();
            for(int attempt=0;attempt<16;attempt++)
            {
                if(!valid()) yield break;
                VisitorOriginBand band;
                var origin=pool.Pick(StableSeed.Combine(sourceStore.WorldSeed,(uint)directory.MapID,(uint)day,(uint)attempt),visitorOriginWeights,out band,considered);
                if(origin==null) continue;considered.Add(origin.Settlement);
                if(travelPlannedTowns.Contains(origin.Settlement)) continue;
                IReadOnlyList<RoutePoint> road=null;
                yield return RoadNetwork.FindRoute((int)origin.X,(int)origin.Z,(int)destination.X,(int)destination.Z,30000,valid,path=>road=path);
                if(!valid()) yield break;
                if(road==null || road.Count<2) continue;
                travelPlannedTowns.Add(origin.Settlement);
                var source=reader.GetLocation(origin.Region,origin.LocationIndex);
                var home=new UnloadedSettlementSurvey.Result();yield return UnloadedSettlementSurvey.ReadSteps(source,home);
                if(!valid()) yield break;
                if(sourceStore.CanDiscoverResidentTrips(origin.Settlement))
                {
                    double length=0;
                    for(int point=1;point<road.Count;point++)
                    {double x=road[point].X-road[point-1].X,z=road[point].Z-road[point-1].Z;length+=Math.Sqrt(x*x+z*z);}
                    // Discovery must look back far enough for the slowest visitor.
                    double roadSeconds=length*MapsFile.WorldMapTerrainDim*MeshReader.GlobalScale*DaggerfallUnity.Instance.WorldTime.TimeScale/(ResidentMovement.WalkSpeed*0.85);
                    foreach(int departureDay in ResidentDiscovery.CandidateDepartureDays(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),roadSeconds))
                    {
                        yield return PlanUnloadedTravelDay(source,sourceStore,departureDay,new[]{directory.MapID},id=>directory.LocationData,
                            valid,10,8,road,home.Grid,true);
                        if(!valid()) yield break;
                    }
                }
                yield return PlanUnloadedTravelDay(source,sourceStore,day,new[]{directory.MapID},id=>directory.LocationData,valid,10,8,road,home.Grid);
            }
        }
        private IEnumerator PlanUnloadedTravelDay(DFLocation source,WorldStateStore sourceStore,int day,IEnumerable<int> targets,
            Func<int,DFLocation> locationFor,Func<bool> valid,int chancePerThousand=10,int maximumDepartures=8,IReadOnlyList<RoutePoint> preparedRoad=null,
            UnloadedSettlementSurvey preparedHome=null,bool discover=false)
        {
            yield return BackgroundWork.Wait(valid);if(!valid()) yield break;
            var home=new UnloadedSettlementSurvey.Result {Grid=preparedHome};
            if(home.Grid==null) yield return UnloadedSettlementSurvey.ReadSteps(source,home);
            if(!valid()) yield break;
            var demand=new UnloadedPopulation.DemandResult();
            yield return UnloadedPopulation.ReserveDemand(source,home.Grid,sourceStore,day,targets,demand,valid,chancePerThousand,maximumDepartures);
            if(!valid() || demand.Proposals==null || demand.Proposals.Count==0) yield break;
            yield return PlanTravelBatch(source,demand.Population,sourceStore,demand.Proposals,locationFor,valid,home.Grid,preparedRoad,discover);
        }
        private IEnumerator PlanTravelBatch(DFLocation source,SettlementPopulation population,WorldStateStore sourceStore,
            IReadOnlyList<ResidentTravelProposal> proposals,Func<int,DFLocation> locationFor,Func<bool> valid,UnloadedSettlementSurvey preparedHome=null,IReadOnlyList<RoutePoint> preparedRoad=null,bool discover=false)
        {
            var home=new UnloadedSettlementSurvey.Result {Grid=preparedHome};
            if(home.Grid==null) yield return UnloadedSettlementSurvey.ReadSteps(source,home);
            if(!valid()) yield break;
            var destinationCache=new Dictionary<int,UnloadedSettlementSurvey>();
            int committed=0;
            foreach(var proposal in proposals)
            {
                if(!valid()) yield break;
                Resident person=null;
                foreach(var household in population.Households)
                    foreach(var candidate in household.Residents) if(candidate.Id==proposal.ResidentId) person=candidate;
                if(person==null || !person.IsAlive) continue;
                var location=locationFor(proposal.DestinationSettlement);
                var pixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
                IReadOnlyList<RoutePoint> road=preparedRoad;
                if(road==null) yield return RoadNetwork.FindRoute(home.Grid.MapPixelX,home.Grid.MapPixelY,pixel.X,pixel.Y,30000,valid,path=>road=path);
                if(!valid()) yield break;
                if(road==null || road.Count<2) continue;
                UnloadedSettlementSurvey destination;
                if(!destinationCache.TryGetValue(proposal.DestinationSettlement,out destination))
                {
                    var surveyed=new UnloadedSettlementSurvey.Result();
                    yield return UnloadedSettlementSurvey.ReadSteps(location,surveyed);
                    if(!valid()) yield break;
                    destination=surveyed.Grid; destinationCache.Add(proposal.DestinationSettlement,destination);
                }
                var result=new ResidentTripPlanner.Result();
                // Prepare the initial service geometry without shortening the
                // requested stay. Only a complete lodging plan may be committed.
                var firstVisit=new ResidentTravelProposal {ResidentId=proposal.ResidentId,HomeSettlement=proposal.HomeSettlement,
                    DestinationSettlement=proposal.DestinationSettlement,DesiredDeparture=proposal.DesiredDeparture,
                    Purpose=proposal.Purpose,VisitDays=1};
                double secondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(person.AppearanceSeed);
                yield return ResidentTripPlanner.Prepare(firstVisit,person,home.Grid,destination,road,secondsPerMetre,result,valid);
                if(!valid()) yield break;
                if(discover && result.Trip!=null && result.Trip.Evaluate(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Phase!=ResidentTripPhase.Outbound)
                    continue;
                int visitDays=result.Trip==null ? proposal.VisitDays : ResidentOvernightPlan.RequiredVisitDays(result.Trip,proposal.VisitDays);
                if(result.Trip!=null && visitDays>1)
                {
                    var lodged=new ResidentTripPlanner.Result();
                    uint seed=StableSeed.Combine(person.AppearanceSeed,(uint)proposal.DestinationSettlement,
                        (uint)(proposal.DesiredDeparture/86400),(uint)visitDays);
                    yield return ResidentTripPlanner.PrepareLodgedTrip(result.Trip,visitDays,destination,location,
                        sourceStore,secondsPerMetre,seed,lodged,valid);
                    if(!valid()) yield break;
                    result=lodged;
                }
                if(result.Trip!=null && valid())
                {
                    double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                    if(discover ? sourceStore.DiscoverOutboundResidentTrip(result.Trip,now) : ResidentTripPlanner.Commit(sourceStore,result.Trip,valid,now)) committed++;
                }
                yield return null;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DAILY_TRAVEL: settlement="+population.SettlementId+"; proposals="+proposals.Count+"; discovered="+discover+"; committed="+committed);
        }
        internal IEnumerator ProbeUnloadedTravelDay(DFLocation source,DFLocation destination,WorldStateStore fixtureStore,int day,Func<bool> valid,bool discover=false)
        {
            if(!UnityEngine.Object.FindObjectOfType<TownSmokeHarness>() || travelWork.Count>0)
                throw new InvalidOperationException("Unloaded travel probe requires an idle scripted fixture.");
            DaggerfallUI.Instance.PopToHUD();GameManager.Instance.PauseGame(false);
            travelValid=valid;
            travelWork.Push(PlanUnloadedTravelDay(source,fixtureStore,day,new[]{destination.MapTableData.MapId},id=>destination,valid,1000,1,null,null,discover));
            float deadline=Time.unscaledTime+120;
            while(travelWork.Count>0)
            {
                if(Time.unscaledTime>deadline) throw new InvalidOperationException("Unloaded travel batch timed out.");
                yield return null;
            }
            travelValid=null;
        }
        internal IEnumerator ProbeTravelBatch(DFLocation source,DFLocation destination,WorldStateStore fixtureStore,
            SettlementPopulation population,ResidentTravelProposal proposal,Func<bool> valid)
        {
            if(!UnityEngine.Object.FindObjectOfType<TownSmokeHarness>() || travelWork.Count>0)
                throw new InvalidOperationException("Travel batch probe requires an idle scripted fixture.");
            DaggerfallUI.Instance.PopToHUD();
            GameManager.Instance.PauseGame(false);
            travelValid=valid;
            travelWork.Push(PlanTravelBatch(source,population,fixtureStore,new[]{proposal},id=>destination,valid));
            float deadline=Time.unscaledTime+120;
            while(travelWork.Count>0)
            {
                if(Time.unscaledTime>deadline)
                    throw new InvalidOperationException("Travel batch runner timed out: paused="+GameManager.IsGamePaused+"; depth="+travelWork.Count+"; step="+travelWork.Peek().GetType().Name);
                yield return null;
            }
            travelValid=null;
        }
    }
}
