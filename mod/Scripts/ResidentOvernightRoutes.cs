using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class ResidentTripPlanner
    {
        public static IEnumerator PrepareRoadInnReception(ResidentTrip trip,UnloadedSettlementSurvey town,DFLocation location,
            UnloadedSettlementSurvey.Entrance door,WorldStateStore store,double secondsPerMetre,uint seed,Result result,Func<bool> valid)
        {
            if(trip==null || town==null || door==null || store==null || result==null || valid==null ||
                location.MapTableData.MapId!=town.SettlementId)
                throw new ArgumentException("Invalid intermediate inn context.");
            result.Trip=null;result.InnDoor=null;result.Unavailable=null;
            if(!valid()) yield break;
            var dfu=DaggerfallUnity.Instance;
            if(!store.HasSettlement(town.SettlementId))
            {
                var homes=new List<HomeProfile>();var batch=new List<BuildingSummary>();
                foreach(var building in town.Buildings)
                {
                    if(building.BuildingType<DFLocation.BuildingTypes.House1 || building.BuildingType>DFLocation.BuildingTypes.House6) continue;
                    batch.Add(building);if(batch.Count<8) continue;
                    homes.AddRange(HousingSurvey.Read(dfu.ContentReader,location,batch,dfu.MeshReader));batch.Clear();
                    yield return null;if(!valid()) yield break;
                }
                if(batch.Count>0) homes.AddRange(HousingSurvey.Read(dfu.ContentReader,location,batch,dfu.MeshReader));
                if(!valid()) yield break;
                store.GetOrCreateFamilies(town.SettlementId,homes);
            }
            var inventory=new TavernRoomSurvey.Result();
            yield return TavernRoomSurvey.ReadRemoteSteps(door,location.Climate.ClimateType,inventory,valid);
            if(!valid()) yield break;
            if(inventory.Rooms==null || !inventory.KeeperHash.HasValue || inventory.ReceptionRoute==null)
            {result.Unavailable="Intermediate inn has no surveyed keeper route.";yield break;}
            double now=dfu.WorldTime.Now.ToSeconds();
            var receipt=GameManager.Instance.PlayerEntity.GetRentedRoom(town.SettlementId,door.BuildingKey);
            NativeTavernRentals.RegisterInventory(store,town.SettlementId,location,door.NativeDoor,inventory,receipt,now);
            var keeper=store.NativePeopleFor(town.SettlementId).Find(p=>p.WorkKey==door.BuildingKey && p.NativeHash==inventory.KeeperHash.Value);
            if(keeper==null || !keeper.IsAlive)
            {result.Unavailable="Intermediate inn keeper is unavailable.";yield break;}
            result.Trip=ResidentOvernightPlan.WithSettlementReceptions(trip,town.SettlementId,door.BuildingKey,
                inventory.ReceptionRoute,secondsPerMetre,(3+seed%4)*dfu.WorldTime.TimeScale,keeper.Id);
            result.InnDoor=door;
        }
        public static IEnumerator PrepareLodgedTrip(ResidentTrip dayTrip,int days,UnloadedSettlementSurvey town,DFLocation location,
            WorldStateStore store,double secondsPerMetre,uint seed,Result result,Func<bool> valid)
        {
            if(store==null || result==null || town==null || valid==null || dayTrip==null ||
                location.MapTableData.MapId!=town.SettlementId || dayTrip.DestinationSettlement!=town.SettlementId ||
                days<2 || days>4 || double.IsNaN(secondsPerMetre) || double.IsInfinity(secondsPerMetre) || secondsPerMetre<=0)
                throw new ArgumentException("Invalid destination lodging context.");
            var checkedTrip=ResidentTrip.CopyValidated(dayTrip);
            if(checkedTrip.Destination==null || checkedTrip.Destination.Stops.Count!=1)
                throw new ArgumentException("Lodging preparation requires one initial service.");
            result.Trip=null;result.InnDoor=null;result.Unavailable=null;
            if(!valid()) yield break;
            var dfu=DaggerfallUnity.Instance;
            if(!store.HasSettlement(town.SettlementId))
            {
                var homes=new List<HomeProfile>();var batch=new List<BuildingSummary>();
                foreach(var building in town.Buildings)
                {
                    if(building.BuildingType<DFLocation.BuildingTypes.House1 || building.BuildingType>DFLocation.BuildingTypes.House6) continue;
                    batch.Add(building);
                    if(batch.Count<8) continue;
                    homes.AddRange(HousingSurvey.Read(dfu.ContentReader,location,batch,dfu.MeshReader));batch.Clear();
                    yield return null;if(!valid()) yield break;
                }
                if(batch.Count>0) homes.AddRange(HousingSurvey.Read(dfu.ContentReader,location,batch,dfu.MeshReader));
                if(!valid()) yield break;
                store.GetOrCreateFamilies(town.SettlementId,homes);
            }
            var excluded=new HashSet<int>();
            for(int attempt=0;attempt<8;attempt++)
            {
                var candidate=new Result();
                yield return ExtendOvernight(dayTrip,days,town,secondsPerMetre,seed,candidate,valid,excluded);
                if(!valid()) yield break;
                if(candidate.Trip==null) {result.Unavailable=candidate.Unavailable;yield break;}
                var inventory=new TavernRoomSurvey.Result();
                yield return TavernRoomSurvey.ReadRemoteSteps(candidate.InnDoor,location.Climate.ClimateType,inventory,valid);
                if(!valid()) yield break;
                if(inventory.Rooms==null || !inventory.KeeperHash.HasValue || inventory.ReceptionRoute==null)
                {excluded.Add(candidate.InnDoor.BuildingKey);continue;}
                double now=dfu.WorldTime.Now.ToSeconds();
                var receipt=GameManager.Instance.PlayerEntity.GetRentedRoom(town.SettlementId,candidate.InnDoor.BuildingKey);
                NativeTavernRentals.RegisterInventory(store,town.SettlementId,location,candidate.InnDoor.NativeDoor,inventory,receipt,now);
                var keeper=store.NativePeopleFor(town.SettlementId).Find(p=>p.WorkKey==candidate.InnDoor.BuildingKey && p.NativeHash==inventory.KeeperHash.Value);
                if(keeper==null || !keeper.IsAlive) {excluded.Add(candidate.InnDoor.BuildingKey);continue;}
                try {candidate.Trip=ResidentOvernightPlan.WithReceptions(candidate.Trip,candidate.InnDoor.BuildingKey,
                    inventory.ReceptionRoute,secondsPerMetre,(3+seed%4)*dfu.WorldTime.TimeScale,keeper.Id);}
                catch(ArgumentException) {excluded.Add(candidate.InnDoor.BuildingKey);continue;}
                store.AdvanceInnAdmissions(town.SettlementId,now,8,candidate.InnDoor.BuildingKey);
                ResidentVisitSnapshot stay=default(ResidentVisitSnapshot);
                foreach(var visit in ResidentVisitJourney.Create(candidate.Trip.ArrivesAt,candidate.Trip.Destination).ScheduledVisits())
                    if(visit.Purpose==ResidentVisitPurpose.Inn) {stay=visit;break;}
                if(store.AvailableLodgingRoom(town.SettlementId,candidate.InnDoor.BuildingKey,1,candidate.Trip.InnReceptions[0].ServiceEndsAt,stay.RoomUntil)!=null)
                {
                    if(valid()) {result.Trip=candidate.Trip;result.InnDoor=candidate.InnDoor;}
                    yield break;
                }
                excluded.Add(candidate.InnDoor.BuildingKey);
                yield return null;
            }
            result.Unavailable="No connected inn has capacity for the planned stay.";
        }
        // Geometry and timing only: caller must reconcile inventory and admission.
        public static IEnumerator ExtendOvernight(ResidentTrip dayTrip,int days,UnloadedSettlementSurvey town,
            double secondsPerMetre,uint seed,Result result,Func<bool> valid,ISet<int> excludedInns=null)
        {
            if(result==null || town==null || valid==null || days<2 || days>4 ||
                double.IsNaN(secondsPerMetre) || double.IsInfinity(secondsPerMetre) || secondsPerMetre<=0)
                throw new ArgumentException("Invalid overnight route request.");
            result.Trip=null;result.InnDoor=null;result.Unavailable=null;
            var original=ResidentTrip.CopyValidated(dayTrip);
            if(original.DestinationSettlement!=town.SettlementId || original.Destination==null || original.Destination.Stops.Count!=1)
                throw new ArgumentException("Overnight routing requires one destination service.");
            var first=original.Destination.Stops[0];var endpoint=first.Route[first.Route.Count-1];
            UnloadedSettlementSurvey.Entrance firstDoor=null;
            foreach(var door in town.Entrances)
            {
                var normal=door.Normal;normal.y=0;var point=town.LocalEntrance(door)+normal.normalized*.6f;
                if(door.BuildingKey==first.BuildingKey && Math.Abs(point.x-endpoint.X)<.001 && Math.Abs(point.z-endpoint.Z)<.001)
                {firstDoor=door;break;}
            }
            if(firstDoor==null) {result.Unavailable="Initial service entrance is unavailable.";yield break;}
            var types=new Dictionary<int,DFLocation.BuildingTypes>();
            foreach(var building in town.Buildings) types[building.buildingKey]=building.BuildingType;
            if(types.ContainsKey(first.BuildingKey) && types[first.BuildingKey]==DFLocation.BuildingTypes.Tavern &&
                (first.Purpose==ResidentVisitPurpose.Social || first.Purpose==ResidentVisitPurpose.Inn) &&
                (excludedInns==null || !excludedInns.Contains(first.BuildingKey)))
            {
                if(valid()) {result.Trip=ResidentOvernightPlan.StayAtInn(original,days,seed);result.InnDoor=firstDoor;}
                yield break;
            }
            var inns=new List<UnloadedSettlementSurvey.Entrance>();
            foreach(var door in town.Entrances)
                if(door.BuildingKey!=first.BuildingKey && types.ContainsKey(door.BuildingKey) &&
                    types[door.BuildingKey]==DFLocation.BuildingTypes.Tavern && (excludedInns==null || !excludedInns.Contains(door.BuildingKey))) inns.Add(door);
            inns.Sort((a,b)=>DistanceOrder(town,firstDoor,a,b));
            bool lodgingFirst=ResidentOvernightPlan.NeedsLodgingFirst(original);
            for(int innIndex=0;innIndex<Math.Min(8,inns.Count);innIndex++)
            {
                if(!valid()) yield break;
                var inn=inns[innIndex];var trip=ResidentTrip.CopyValidated(original);var current=firstDoor;
                var used=new HashSet<int>{first.BuildingKey};bool complete=true;
                int firstDay=1;
                if(lodgingFirst)
                {
                    var arrivalRoute=new UnloadedSettlementSurvey.RouteResult();
                    yield return town.ReadBuildingRouteSteps(firstDoor,inn,arrivalRoute,valid);
                    if(!valid()) yield break;
                    if(arrivalRoute.Points==null) continue;
                    trip=ResidentOvernightPlan.PrependInn(original,inn.BuildingKey,arrivalRoute.Points,secondsPerMetre);
                    firstDay=2; // The arrival night consumes the first overnight stop.
                }
                for(int day=firstDay;day<days;day++)
                {
                    var toInn=new UnloadedSettlementSurvey.RouteResult();
                    yield return town.ReadBuildingRouteSteps(current,inn,toInn,valid);
                    if(!valid()) yield break;
                    if(toInn.Points==null) {complete=false;break;}
                    trip.Destination.Stops.Add(Stop(inn.BuildingKey,ResidentVisitPurpose.Inn,toInn.Points,secondsPerMetre,1,0,0));
                    var services=new List<UnloadedSettlementSurvey.Entrance>();
                    foreach(var door in town.Entrances)
                        if(door.BuildingKey!=inn.BuildingKey && types.ContainsKey(door.BuildingKey) && Matches(first.Purpose,types[door.BuildingKey])) services.Add(door);
                    services.Sort((a,b)=>{
                        int seen=used.Contains(a.BuildingKey).CompareTo(used.Contains(b.BuildingKey));
                        return seen!=0 ? seen : DistanceOrder(town,inn,a,b);
                    });
                    UnloadedSettlementSurvey.Entrance chosen=null;IReadOnlyList<RoutePoint> servicePath=null;
                    for(int option=0;option<Math.Min(8,services.Count);option++)
                    {
                        var route=new UnloadedSettlementSurvey.RouteResult();
                        yield return town.ReadBuildingRouteSteps(inn,services[option],route,valid);
                        if(!valid()) yield break;
                        if(route.Points!=null) {chosen=services[option];servicePath=route.Points;break;}
                    }
                    if(chosen==null) {complete=false;break;}
                    int type=(int)types[chosen.BuildingKey],open=PlayerActivate.openHours[type]*60,close=PlayerActivate.closeHours[type]*60;
                    if(close>=1440) open=close=0;
                    trip.Destination.Stops.Add(Stop(chosen.BuildingKey,first.Purpose,servicePath,secondsPerMetre,
                        600+StableSeed.Combine(seed,(uint)day,0,0)%1800,open,close));
                    used.Add(chosen.BuildingKey);current=chosen;
                }
                if(!complete) continue;
                if(current.BuildingKey!=firstDoor.BuildingKey || current.Position!=firstDoor.Position)
                {
                    var returnPath=new UnloadedSettlementSurvey.RouteResult();
                    yield return town.ReadBuildingRouteSteps(current,firstDoor,returnPath,valid);
                    if(!valid()) yield break;
                    if(returnPath.Points==null) continue;
                    var exit=new List<SavedRoutePoint>();
                    foreach(var p in returnPath.Points) exit.Add(new SavedRoutePoint {X=p.X,Z=p.Z});
                    foreach(var p in original.Destination.ExitRoute) exit.Add(new SavedRoutePoint {X=p.X,Z=p.Z});
                    double added=Length(returnPath.Points)*secondsPerMetre;
                    trip.Destination.ExitRoute=exit;trip.Destination.ExitWalkSeconds+=added;
                    if(trip.Destination.Walled) trip.Destination.ExitGateSeconds+=added;
                }
                var timed=ResidentOvernightPlan.Schedule(trip,seed);
                if(valid()) {result.Trip=timed;result.InnDoor=inn;}
                yield break;
            }
            result.Unavailable="No connected inn and multi-day service route.";
        }
        private static int DistanceOrder(UnloadedSettlementSurvey town,UnloadedSettlementSurvey.Entrance origin,
            UnloadedSettlementSurvey.Entrance a,UnloadedSettlementSurvey.Entrance b)
        {
            int order=(town.LocalEntrance(a)-town.LocalEntrance(origin)).sqrMagnitude.CompareTo((town.LocalEntrance(b)-town.LocalEntrance(origin)).sqrMagnitude);
            return order!=0 ? order : a.BuildingKey.CompareTo(b.BuildingKey);
        }
        private static double Length(IReadOnlyList<RoutePoint> points)
        {
            double sum=0;for(int i=1;i<points.Count;i++) {double x=points[i].X-points[i-1].X,z=points[i].Z-points[i-1].Z;sum+=Math.Sqrt(x*x+z*z);}return sum;
        }
        private static ResidentVisitStop Stop(int building,ResidentVisitPurpose purpose,IReadOnlyList<RoutePoint> route,double speed,double dwell,int open,int close)
        {
            var stop=new ResidentVisitStop {BuildingKey=building,Purpose=purpose,WalkSeconds=Length(route)*speed,DwellSeconds=dwell,OpenMinute=open,CloseMinute=close};
            foreach(var point in route) stop.Route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});return stop;
        }
    }
}
