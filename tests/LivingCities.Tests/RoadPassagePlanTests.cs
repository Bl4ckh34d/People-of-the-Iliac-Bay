using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class RoadPassagePlanTests
    {
        private static ResidentDestination Visit() {return new ResidentDestination {ThroughRoute=true,ExitWalkSeconds=3,
            ExitRoute=new List<SavedRoutePoint>{new SavedRoutePoint{X=2},new SavedRoutePoint{X=5}},
            Stops=new List<ResidentVisitStop>{new ResidentVisitStop{BuildingKey=7,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=2,DwellSeconds=20,
                Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=2}}}}};}
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Adding another passage remaps reverse indices without renaming the earlier stop",()=>{
                var points=new List<RoutePoint>();for(int i=0;i<7;i++) points.Add(new RoutePoint(i,0));
                var source=ResidentTrip.Create("person",100,200,100,40,10,points);
                var one=ResidentRoadPassage.Attach(source,2,300,new RoutePoint(1.75,0),new RoutePoint(2.25,0),Visit(),30);
                var two=ResidentRoadPassage.Attach(one,5,400,new RoutePoint(3.75,0),new RoutePoint(4.25,0),Visit(),30);
                Check.Equal("outbound/0",two.OutboundStops[0].Id);Check.Equal("outbound/1",two.OutboundStops[1].Id);
                Check.Equal("return/1",two.ReturnStops[0].Id);Check.Equal("return/0",two.ReturnStops[1].Id);
                Check.Equal(5,two.ReturnStops[1].RoadNode);Check.Equal((int?)6,two.ReturnStops[1].ExitRoadNode);
                Check.Equal(1,one.OutboundStops.Count);Check.Equal(8,one.Road.Count);
                two.ReturnStops.Clear();Check.Throws<ArgumentException>(()=>ResidentTrip.CopyValidated(two));
            }));
            cases.Add(("Surveyed inn insertion preserves source and maps both travel directions",()=>{
                var source=ResidentTrip.Create("person",100,200,100,40,10,new[]{new RoutePoint(0,0),new RoutePoint(1,0),new RoutePoint(2,0),new RoutePoint(3,0),new RoutePoint(4,0)});
                var result=ResidentRoadPassage.Attach(source,2,300,new RoutePoint(1.75,0),new RoutePoint(2.25,0),Visit(),30);
                Check.Equal(6,result.Road.Count);Check.Equal(5,source.Road.Count);Check.Equal(0,source.OutboundStops.Count);
                Check.Equal(2,result.OutboundStops[0].RoadNode);Check.Equal((int?)3,result.OutboundStops[0].ExitRoadNode);
                Check.Equal(2,result.ReturnStops[0].RoadNode);Check.Equal((int?)3,result.ReturnStops[0].ExitRoadNode);
                Check.Near(160,result.ArrivesAt);Check.Near(270,result.HomeAt);
                Check.Equal(300,result.Evaluate(120).SettlementId);Check.Equal(300,result.Evaluate(221).SettlementId);
                Check.Near(5,result.ReturnStops[0].Visit.Stops[0].Route[0].X);Check.Near(0,result.ReturnStops[0].Visit.ExitRoute[1].X);
                Check.Throws<ArgumentException>(()=>ResidentRoadPassage.Attach(source,2,300,new RoutePoint(1.75,1),new RoutePoint(2.25,0),Visit(),30));
                Check.Throws<ArgumentException>(()=>ResidentRoadPassage.Attach(result,2,400,new RoutePoint(1.5,0),new RoutePoint(2,0),Visit(),30));
            }));
        }
    }
}
