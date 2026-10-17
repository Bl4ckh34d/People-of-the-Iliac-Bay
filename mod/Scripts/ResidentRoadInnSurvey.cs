using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using LivingCities.Core;
namespace LivingCities.DFU
{
    // Produces model-clear local geometry only. No household, booking or trip mutation.
    public static class ResidentRoadInnSurvey
    {
        public sealed class Result
        {
            public int SettlementId;
            public RoutePoint EntryRoad,ExitRoad;
            public ResidentDestination Visit;
            public UnloadedSettlementSurvey.Entrance Door;
        }
        public static IEnumerator Prepare(UnloadedSettlementSurvey town,IReadOnlyList<RoutePoint> road,int node,
            double secondsPerMetre,double dwell,Result result,Func<bool> valid)
        {
            if(town==null || road==null || result==null || valid==null || node<1 || node>=road.Count-1 ||
                road[node].X!=town.MapPixelX || road[node].Z!=town.MapPixelY ||
                double.IsNaN(secondsPerMetre) || double.IsInfinity(secondsPerMetre) || secondsPerMetre<=0 ||
                double.IsNaN(dwell) || double.IsInfinity(dwell) || dwell<=0)
                throw new ArgumentException("Invalid road inn survey.");
            result.Visit=null;result.Door=null;if(!valid()) yield break;
            var incoming=new UnloadedSettlementSurvey.RoadAccessResult();
            var outgoing=new UnloadedSettlementSurvey.RoadAccessResult();
            yield return town.ReadRoadAccessSteps(new[]{road[node],road[node-1]},incoming,valid);
            if(!valid()) yield break;
            yield return town.ReadRoadAccessSteps(new[]{road[node],road[node+1]},outgoing,valid);
            if(!valid() || incoming.Accesses==null || outgoing.Accesses==null || incoming.Accesses.Count==0 || outgoing.Accesses.Count==0) yield break;
            var inns=new HashSet<int>();
            foreach(var building in town.Buildings) if(building.BuildingType==DFLocation.BuildingTypes.Tavern) inns.Add(building.buildingKey);
            var doors=new List<UnloadedSettlementSurvey.Entrance>();
            foreach(var door in town.Entrances) if(inns.Contains(door.BuildingKey)) doors.Add(door);
            var origin=town.LocalCell(incoming.Accesses[0].StreetCell);
            doors.Sort((a,b)=>{
                int order=(town.LocalEntrance(a)-origin).sqrMagnitude.CompareTo((town.LocalEntrance(b)-origin).sqrMagnitude);
                return order!=0 ? order : a.BuildingKey.CompareTo(b.BuildingKey);
            });
            for(int d=0;d<Math.Min(8,doors.Count);d++)
            {
                if(!valid()) yield break;
                var entry=new UnloadedSettlementSurvey.BuildingRoadRoute();
                for(int i=0;i<Math.Min(4,incoming.Accesses.Count);i++)
                {
                    yield return town.ReadRoadBuildingRouteSteps(incoming.Accesses[i],doors[d],entry,valid);
                    if(!valid()) yield break;if(entry.Points!=null) break;
                }
                if(entry.Points==null) continue;
                var exit=new UnloadedSettlementSurvey.BuildingRoadRoute();
                for(int i=0;i<Math.Min(4,outgoing.Accesses.Count);i++)
                {
                    yield return town.ReadRoadBuildingRouteSteps(outgoing.Accesses[i],doors[d],exit,valid);
                    if(!valid()) yield break;if(exit.Points!=null) break;
                }
                if(exit.Points==null || entry.RoadPoint.X==exit.RoadPoint.X && entry.RoadPoint.Z==exit.RoadPoint.Z) continue;
                var visit=entry.Visit(ResidentVisitPurpose.Inn,0,0,dwell,secondsPerMetre);
                visit.ThroughRoute=true;visit.ExitRoute.Clear();
                for(int i=exit.Points.Count-1;i>=0;i--) visit.ExitRoute.Add(new SavedRoutePoint{X=exit.Points[i].X,Z=exit.Points[i].Z});
                visit.ExitWalkSeconds=exit.Metres*secondsPerMetre;
                visit.ExitGateSeconds=exit.Walled ? (exit.Metres-exit.EntryGateMetres)*secondsPerMetre : 0;
                result.Visit=ResidentVisitJourney.Copy(visit);result.Door=doors[d];result.SettlementId=town.SettlementId;
                result.EntryRoad=entry.RoadPoint;result.ExitRoad=exit.RoadPoint;yield break;
            }
        }
    }
}
