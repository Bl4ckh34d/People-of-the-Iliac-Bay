using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Builds a complete original-resident day visit before the caller commits it.
    // Overnight proposals require lodging planning and are deliberately not shortened.
    public sealed partial class ResidentTripPlanner
    {
        public sealed class Result { public ResidentTrip Trip; public string Unavailable; public UnloadedSettlementSurvey.Entrance InnDoor; }
        public static bool Commit(WorldStateStore store,ResidentTrip trip,Func<bool> stillValid,double now)
        {
            if(store==null || trip==null || stillValid==null) throw new ArgumentNullException();
            if(double.IsNaN(now) || double.IsInfinity(now) || now<0) throw new ArgumentException("Invalid commit time.");
            if(!stillValid() || now>=trip.DepartsAt) return false;
            try {store.ScheduleResidentTrip(trip); return true;}
            catch(ArgumentException) {return false;} // A death or another trip can invalidate work while it yields.
        }
        public static IEnumerator Prepare(ResidentTravelProposal proposal,Resident person,
            UnloadedSettlementSurvey home,UnloadedSettlementSurvey destination,
            IReadOnlyList<RoutePoint> road,double secondsPerMetre,Result result,Func<bool> stillValid)
        {
            if(proposal==null || person==null || home==null || destination==null || result==null || stillValid==null)
                throw new ArgumentNullException();
            result.Trip=null; result.Unavailable=null;
            if(proposal.ResidentId!=person.Id || proposal.HomeSettlement!=home.SettlementId || proposal.DestinationSettlement!=destination.SettlementId ||
                !person.IsAlive || person.AgeGroup!=ResidentAgeGroup.Adult || person.IsQuestProtected || person.NativeIdentity!=null)
                throw new ArgumentException("Resident does not match eligible trip ownership.");
            if(double.IsNaN(secondsPerMetre) || double.IsInfinity(secondsPerMetre) || secondsPerMetre<=0)
                throw new ArgumentException("Invalid walking time.");
            if(proposal.VisitDays!=1) {result.Unavailable="Overnight itinerary requires lodging planning."; yield break;}
            if(road==null || road.Count<2) {result.Unavailable="Insufficient regional road nodes."; yield break;}
            var reversed=new List<RoutePoint>();
            for(int i=road.Count-1;i>=0;i--) reversed.Add(road[i]);
            var homeAccess=new UnloadedSettlementSurvey.RoadAccessResult();
            var awayAccess=new UnloadedSettlementSurvey.RoadAccessResult();
            yield return home.ReadRoadAccessSteps(road,homeAccess,stillValid);
            if(!stillValid()) yield break;
            yield return destination.ReadRoadAccessSteps(reversed,awayAccess,stillValid);
            if(!stillValid()) yield break;
            if(homeAccess.Accesses==null || awayAccess.Accesses==null || homeAccess.Accesses.Count==0 || awayAccess.Accesses.Count==0)
            {result.Unavailable="No street/road connection."; yield break;}
            UnloadedSettlementSurvey.BuildingRoadRoute fromHome=null,toService=null;
            foreach(var access in homeAccess.Accesses)
            {
                foreach(var entrance in home.Entrances)
                {
                    if(entrance.BuildingKey!=person.HomeKey) continue;
                    var candidate=new UnloadedSettlementSurvey.BuildingRoadRoute();
                    yield return home.ReadRoadBuildingRouteSteps(access,entrance,candidate,stillValid);
                    if(!stillValid()) yield break;
                    if(candidate.Points!=null) {fromHome=candidate; break;}
                }
                if(fromHome!=null) break;
            }
            if(fromHome==null) {result.Unavailable="Home has no connected departure."; yield break;}
            var types=new Dictionary<int,DFLocation.BuildingTypes>();
            foreach(var building in destination.Buildings)
                if(Matches(proposal.Purpose,building.BuildingType)) types[building.buildingKey]=building.BuildingType;
            foreach(var access in awayAccess.Accesses)
            {
                var entrances=new List<UnloadedSettlementSurvey.Entrance>();
                foreach(var entrance in destination.Entrances) if(types.ContainsKey(entrance.BuildingKey)) entrances.Add(entrance);
                var start=destination.LocalCell(access.StreetCell);
                entrances.Sort((a,b)=>{
                    int order=(destination.LocalEntrance(a)-start).sqrMagnitude.CompareTo((destination.LocalEntrance(b)-start).sqrMagnitude);
                    return order!=0 ? order : a.BuildingKey.CompareTo(b.BuildingKey);
                });
                for(int i=0;i<Math.Min(8,entrances.Count);i++)
                {
                    var candidate=new UnloadedSettlementSurvey.BuildingRoadRoute();
                    yield return destination.ReadRoadBuildingRouteSteps(access,entrances[i],candidate,stillValid);
                    if(!stillValid()) yield break;
                    if(candidate.Points!=null) {toService=candidate; break;}
                }
                if(toService!=null) break;
            }
            if(toService==null) {result.Unavailable="No connected service matching visit purpose."; yield break;}
            var trimmed=new List<RoutePoint> {fromHome.RoadPoint};
            for(int i=1;i<road.Count-1;i++) trimmed.Add(road[i]);
            trimmed.Add(toService.RoadPoint);
            var trip=ResidentTrip.Create(person.Id,home.SettlementId,destination.SettlementId,proposal.DesiredDeparture,600,
                DaggerfallConnect.Arena2.MapsFile.WorldMapTerrainDim*MeshReader.GlobalScale*secondsPerMetre,trimmed);
            for(int i=fromHome.Points.Count-1;i>=0;i--) trip.HomeRoute.Add(new SavedRoutePoint {X=fromHome.Points[i].X,Z=fromHome.Points[i].Z});
            trip.HomeWalkSeconds=fromHome.Metres*secondsPerMetre; trip.HomeWalled=fromHome.Walled;
            trip.HomeGateWalkSeconds=fromHome.Walled ? (fromHome.Metres-fromHome.EntryGateMetres)*secondsPerMetre : 0;
            int type=(int)types[toService.BuildingKey];
            int open=PlayerActivate.openHours[type]*60,close=PlayerActivate.closeHours[type]*60;
            if(close>=1440) {open=0; close=0;}
            trip.Destination=toService.Visit(proposal.Purpose,open,close,600+(person.AppearanceSeed%1800),secondsPerMetre);
            if(stillValid()) result.Trip=ResidentTrip.CopyValidated(trip);
        }
        public static bool Matches(ResidentVisitPurpose purpose,DFLocation.BuildingTypes type)
        {
            if(purpose==ResidentVisitPurpose.Temple) return type==DFLocation.BuildingTypes.Temple;
            if(purpose==ResidentVisitPurpose.Guild) return type==DFLocation.BuildingTypes.GuildHall;
            if(purpose==ResidentVisitPurpose.Castle) return type==DFLocation.BuildingTypes.Palace;
            if(purpose==ResidentVisitPurpose.Social || purpose==ResidentVisitPurpose.Inn) return type==DFLocation.BuildingTypes.Tavern;
            return purpose==ResidentVisitPurpose.Shop && (type==DFLocation.BuildingTypes.Alchemist || type==DFLocation.BuildingTypes.Armorer ||
                type==DFLocation.BuildingTypes.Bookseller || type==DFLocation.BuildingTypes.ClothingStore || type==DFLocation.BuildingTypes.FurnitureStore ||
                type==DFLocation.BuildingTypes.GemStore || type==DFLocation.BuildingTypes.GeneralStore || type==DFLocation.BuildingTypes.PawnShop || type==DFLocation.BuildingTypes.WeaponSmith);
        }
    }
}
