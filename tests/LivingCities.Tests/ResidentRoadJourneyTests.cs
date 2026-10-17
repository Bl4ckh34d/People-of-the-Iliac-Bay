using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class ResidentRoadJourneyTests
    {
        private static ResidentRoadStop Inn(int node,int town,double dwell=20)
        {
            return new ResidentRoadStop {RoadNode=node,SettlementId=town,Visit=new ResidentDestination {
                Stops=new List<ResidentVisitStop> {new ResidentVisitStop {BuildingKey=7,Purpose=ResidentVisitPurpose.Inn,
                    WalkSeconds=2,DwellSeconds=dwell,Route=new List<SavedRoutePoint> {new SavedRoutePoint {X=1000},new SavedRoutePoint {X=1002}}}},
                ExitWalkSeconds=2,ExitRoute=new List<SavedRoutePoint> {new SavedRoutePoint {X=1002},new SavedRoutePoint {X=1000}}}};
        }
        private static List<RoutePoint> Road() {return new List<RoutePoint> {new RoutePoint(0,0),new RoutePoint(1,0),new RoutePoint(2,0),new RoutePoint(3,0),new RoutePoint(4,0)};}
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Through-town inn route replaces regional segments between distinct joins",()=>{
                var stop=Inn(1,20);stop.ExitRoadNode=3;stop.Visit.ThroughRoute=true;
                stop.Visit.ExitRoute[1].X=1010;
                var journey=ResidentRoadJourney.Create(Road(),100,10,new[]{stop});
                Check.Near(144,journey.ArrivesAt);
                Check.Equal(ResidentRoadPhase.Visiting,journey.Evaluate(133).Phase);
                Check.Near(1006,journey.Evaluate(133).Visit.Position.X);
                Check.Near(3,journey.Evaluate(134).MapPosition.X);Check.Near(3.5,journey.Evaluate(139).MapPosition.X);
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,10,new[]{stop,Inn(2,30)}));
                stop.ExitRoadNode=1;Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,10,new[]{stop}));
            }));
            cases.Add(("Interrupted roadside stay recomputes gate waits instead of shifting all times equally",()=>{
                double start=17*3600;var stop=Inn(1,20,7200);stop.Id="return/0";stop.Visit.Walled=true;
                var exit=new ResidentServiceExit {VisitId="return/0/0",SettlementId=20,BuildingKey=7,
                    VisitEnterAt=start+12,VisitLeaveAt=start+7212,StartedAt=start+15,SecondsPerMetre=1,
                    Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3 {X=3}}};
                var original=ResidentRoadJourney.Create(Road(),start,10,new[]{stop});
                var changed=ResidentRoadJourney.Create(Road(),start,10,new[]{stop},new[]{exit});
                Check.Near(30*3600+32,original.ArrivesAt);Check.Near(start+50,changed.ArrivesAt);
                Check.Equal(ResidentRoadPhase.Traveling,changed.Evaluate(start+21).Phase);
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),start,10,new[]{stop},new[]{exit,exit}));
                exit.VisitId="outbound/0/0";
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),start,10,new[]{stop},new[]{exit}));
            }));
            cases.Add(("Interrupted road inn uses its interior exit before onward road travel",()=>{
                var first=Inn(1,20);first.Id="outbound/0";var second=Inn(3,30,30);second.Id="outbound/1";
                var exit=new ResidentServiceExit {VisitId="outbound/0/0",SettlementId=20,BuildingKey=7,
                    VisitEnterAt=112,VisitLeaveAt=132,StartedAt=115,SecondsPerMetre=1,
                    Route=new List<GuardPoint3>{new GuardPoint3 {X=5,Y=2},new GuardPoint3 {X=8,Y=2}}};
                var journey=ResidentRoadJourney.Create(Road(),100,10,new[]{first,second},new[]{exit});
                var leaving=journey.Evaluate(116);Check.Equal(ResidentVisitPhase.ExitingService,leaving.Visit.Phase);
                Check.Equal(20,leaving.SettlementId);Check.Near(1,leaving.MapPosition.X);
                Check.Near(6,leaving.Visit.Position.X);Check.Near(2,leaving.InteriorY);
                Check.Equal(ResidentRoadPhase.Visiting,journey.Evaluate(119).Phase);
                Check.Equal(ResidentRoadPhase.Traveling,journey.Evaluate(120).Phase);
                Check.Near(142,new List<ResidentRoadSnapshot>(journey.ScheduledVisits())[1].Visit.EnterAt);
                Check.Near(184,journey.ArrivesAt);
                exit.Route[0].X=999;Check.Near(6,journey.Evaluate(116).Visit.Position.X);
                exit.SettlementId=30;
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,10,new[]{first,second},new[]{exit}));
            }));
            cases.Add(("Road inn timeline resumes each leg without losing intermediate settlement identity",()=>{
                var journey=ResidentRoadJourney.Create(Road(),100,10,new[]{Inn(1,20),Inn(3,30,30)});
                Check.Near(198,journey.ArrivesAt);
                Check.Equal(ResidentRoadPhase.Before,journey.Evaluate(99).Phase);
                Check.Near(.5,journey.Evaluate(105).MapPosition.X);
                var first=journey.Evaluate(112);
                Check.Equal(ResidentRoadPhase.Visiting,first.Phase);Check.Equal(20,first.SettlementId);
                Check.Near(1,first.MapPosition.X);Check.Near(1002,first.Visit.Position.X);
                Check.Equal(7,first.Visit.BuildingKey);Check.Equal(0,first.StopIndex);
                Check.Equal(ResidentRoadPhase.Traveling,journey.Evaluate(134).Phase);
                Check.Near(1.5,journey.Evaluate(139).MapPosition.X);
                var second=journey.Evaluate(156);Check.Equal(30,second.SettlementId);Check.Equal(1,second.StopIndex);
                Check.Equal(7,second.Visit.BuildingKey);Check.Near(3,second.MapPosition.X);
                var visits=new List<ResidentRoadSnapshot>(journey.ScheduledVisits());
                Check.Equal(2,visits.Count);Check.Equal(20,visits[0].SettlementId);Check.Equal(30,visits[1].SettlementId);
                Check.Near(112,visits[0].Visit.EnterAt);Check.Near(156,visits[1].Visit.EnterAt);
                Check.Equal(ResidentRoadPhase.Finished,journey.Evaluate(198).Phase);
                Check.Near(4,journey.Evaluate(1000).MapPosition.X);
            }));
            cases.Add(("Return road inn timeline follows the reversed road with independent visits",()=>{
                var road=Road();road.Reverse();
                var journey=ResidentRoadJourney.Create(road,300,10,new[]{Inn(1,30,30),Inn(3,20)});
                Check.Near(3.5,journey.Evaluate(305).MapPosition.X);
                Check.Equal(30,journey.Evaluate(312).SettlementId);
                Check.Near(3,journey.Evaluate(312).MapPosition.X);
                Check.Equal(20,journey.Evaluate(366).SettlementId);
                Check.Near(398,journey.ArrivesAt);Check.Near(0,journey.Evaluate(398).MapPosition.X);
            }));
            cases.Add(("Road inn timeline includes closed city gates before continuing travel",()=>{
                var stop=Inn(1,20,60);stop.Visit.Walled=true;
                var journey=ResidentRoadJourney.Create(new[]{new RoutePoint(0,0),new RoutePoint(1,0),new RoutePoint(2,0)},17*3600,3600,new[]{stop});
                Check.Equal(ResidentVisitPhase.WaitingAtGate,journey.Evaluate(18*3600).Visit.Phase);
                Check.Equal(ResidentVisitPhase.Inside,journey.Evaluate(30*3600+2).Visit.Phase);
                Check.Near(31*3600+64,journey.ArrivesAt);
            }));
            cases.Add(("Road inn timeline copies input and reconstructs deterministic boundaries",()=>{
                var road=Road();var stops=new[]{Inn(1,20),Inn(3,30,30)};
                var a=ResidentRoadJourney.Create(road,100,10,stops);var b=ResidentRoadJourney.Create(road,100,10,stops);
                road[1]=new RoutePoint(999,999);stops[0].Visit.Stops[0].DwellSeconds=999;stops[1].SettlementId=99;
                foreach(double now in new[]{99d,105,110,112,133,134,145,154,156,187,188,198,300})
                {var x=a.Evaluate(now);var y=b.Evaluate(now);Check.Equal(x.Phase,y.Phase);Check.Equal(x.SettlementId,y.SettlementId);Check.Near(x.MapPosition.X,y.MapPosition.X);}
                var direct=ResidentRoadJourney.Create(Road(),100,10,Array.Empty<ResidentRoadStop>());
                Check.Near(140,direct.ArrivesAt);Check.Near(2.5,direct.Evaluate(125).MapPosition.X);
            }));
            cases.Add(("Road inn timeline rejects invalid joins, ordering, purposes and time",()=>{
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,10,new[]{Inn(3,20),Inn(1,30)}));
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,10,new[]{Inn(1,20),Inn(1,30)}));
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,10,new[]{Inn(4,20)}));
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,10,new[]{Inn(1,-1)}));
                var bad=Inn(1,20);bad.Visit.ExitRoute[1].X=2000;
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,10,new[]{bad}));
                bad=Inn(1,20);bad.Visit.Stops[0].Purpose=ResidentVisitPurpose.Shop;
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,10,new[]{bad}));
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),double.NaN,10,Array.Empty<ResidentRoadStop>()));
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(Road(),100,0,Array.Empty<ResidentRoadStop>()));
                Check.Throws<ArgumentException>(()=>ResidentRoadJourney.Create(new[]{new RoutePoint(0,0),new RoutePoint(0,0)},100,10,Array.Empty<ResidentRoadStop>()));
                var good=ResidentRoadJourney.Create(Road(),100,10,Array.Empty<ResidentRoadStop>());
                Check.Throws<ArgumentException>(()=>good.Evaluate(double.PositiveInfinity));
            }));
        }
    }
}
