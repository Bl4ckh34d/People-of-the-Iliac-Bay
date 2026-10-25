using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ClosedShopLeisureTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("A later open visit moves ahead of a closed shop without duplication or a missed return",()=>
            {
                var source=TwoStops();double departure=9*3600,now=departure+60,opening=10*3600;
                var original=ResidentVisitJourney.Create(departure,source);
                var result=ClosedShopReorder.TryCreate(source,0,now,opening+300,original.EndsAt,
                    new List<SavedRoutePoint>{new SavedRoutePoint{X=10},new SavedRoutePoint{X=0}},60);
                Check.True(result!=null);Check.Equal(30,result.Itinerary.Stops[0].BuildingKey);
                Check.Equal(20,result.Itinerary.Stops[1].BuildingKey);Check.Equal(2,result.Itinerary.Stops.Count);
                var journey=ResidentVisitJourney.Create(result.DepartureSeconds,result.Itinerary);
                Check.Equal(30,journey.Evaluate(now+61).BuildingKey);
                Check.Equal(20,journey.Evaluate(opening+301).BuildingKey);
                Check.True(journey.EndsAt<=original.EndsAt);
                Check.Equal(20,source.Stops[0].BuildingKey);
                var store=new WorldStateStore(1);
                store.SaveClosedShopVisit(new ClosedShopVisit {PersonId="alice",SettlementId=1,Building=20,StartedAt=now,Opening=opening,
                    ReturnAt=opening+300,EndsAt=original.EndsAt,Pastime=ClosedShopPastime.Stroll,Reordered=result});
                var restored=new WorldStateStore(2);restored.Restore(store.Snapshot());
                var saved=restored.ReadClosedShopVisit("alice",1,now+1);
                Check.Equal(30,saved.Reordered.Itinerary.Stops[0].BuildingKey);
                result.Itinerary.Stops[0].BuildingKey=999;
                Check.Equal(30,restored.ReadClosedShopVisit("alice",1,now+1).Reordered.Itinerary.Stops[0].BuildingKey);
                result=saved.Reordered;
                var normal=DailySchedule.ItineraryDay(10,source,departure,360,1320);
                var temporary=DailySchedule.ItineraryDay(10,result.Itinerary,result.DepartureSeconds,360,1320);
                var changed=DailySchedule.Interlude(normal,temporary,now,original.EndsAt);
                int laterVisits=0;foreach(var visit in changed.ScheduledVisits(now))if(visit.BuildingKey==30)laterVisits++;
                Check.Equal(1,laterVisits);
                Check.Equal(Activity.Home,changed.EvaluateSeconds(journey.EndsAt+1).Activity);
                Check.Equal(20,changed.EvaluateSeconds(86400+opening+301).BuildingKey);
            }));
            cases.Add(("A closed or infeasible later visit is not advanced",()=>
            {
                var source=TwoStops();source.Stops[1].OpenMinute=11*60;
                Check.True(ClosedShopReorder.TryCreate(source,0,9*3600+60,10*3600+300,13*3600,
                    new List<SavedRoutePoint>{new SavedRoutePoint{X=10},new SavedRoutePoint{X=0}},60)==null);
                source=TwoStops();
                Check.True(ClosedShopReorder.TryCreate(source,0,9*3600+60,10*3600+300,10*3600+360,
                    new List<SavedRoutePoint>{new SavedRoutePoint{X=10},new SavedRoutePoint{X=0}},60)==null);
            }));
            cases.Add(("Closed shop intention survives save restore and never extends beyond its original visit",()=>
            {
                var store=new WorldStateStore(1);
                var plan=new ClosedShopVisit {PersonId="alice",SettlementId=1,Building=4,StartedAt=100,Opening=1000,
                    ReturnAt=1300,EndsAt=2000,Pastime=ClosedShopPastime.Browse};
                store.SaveClosedShopVisit(plan);plan.ReturnAt=1500;
                var restored=new WorldStateStore(2);restored.Restore(store.Snapshot());
                Check.Equal(1300d,restored.ReadClosedShopVisit("alice",1,1200).ReturnAt);
                Check.True(restored.ReadClosedShopVisit("alice",2,1200)==null && restored.ReadClosedShopVisit("alice",1,2000)==null);
                restored.ForgetClosedShopVisit("alice");Check.True(restored.ReadClosedShopVisit("alice",1,1200)==null);
            }));
            cases.Add(("Temporary Inn visit preserves original shop and subsequent routine", () =>
            {
                var original=Visit(20,600,660);
                var inn=Visit(30,580,600);
                var day=DailySchedule.Interlude(original,inn,570*60,610*60);
                Check.Equal(30,day.EvaluateSeconds(590*60).BuildingKey);
                Check.Equal(20,day.EvaluateSeconds(620*60).BuildingKey);
                Check.Equal(Activity.Home,day.EvaluateSeconds(700*60).Activity);
                Check.True(day.ContainsVisitBuilding(30,590*60));
                double start,end;
                Check.True(day.TryVisitWindowForBuilding(590*60,30,out start,out end));
                Check.Near(580*60,start);Check.Near(600*60,end);
                Check.Equal(20,day.EvaluateSeconds(86400+620*60).BuildingKey);
                Check.Equal(Activity.Travel,day.EvaluateSeconds(86400+590*60).Activity);
            }));
            cases.Add(("Closed shop returns are stable, varied and no later than 45 minutes after opening", () =>
            {
                var times=new HashSet<double>();
                for(uint seed=0;seed<100;seed++)
                {
                    double time=ClosedShopLeisure.ReturnAt(seed,42,90000,95000);
                    Check.True(time>=90000 && time<=92700);
                    Check.Equal(time,ClosedShopLeisure.ReturnAt(seed,42,90000,95000));
                    times.Add(time);
                }
                Check.True(times.Count>20);
                Check.True(ClosedShopLeisure.ReturnAt(2,42,90000,90300)<=90180);
            }));
            cases.Add(("Closed shop activity choice never invents an Inn or display", () =>
            {
                var choices=new HashSet<ClosedShopPastime>();
                for(uint seed=0;seed<100;seed++)
                {
                    var choice=ClosedShopLeisure.Choose(seed,42,1,false,false);
                    Check.True(choice==ClosedShopPastime.Conversation || choice==ClosedShopPastime.Stroll);
                    choices.Add(ClosedShopLeisure.Choose(seed,42,1,true,true));
                }
                Check.Equal(4,choices.Count);
            }));
            cases.Add(("Waiting context distinguishes closed shop, continued pastime and return", () =>
            {
                string closed=ClosedShopLeisure.Context("The Bell",90000,91200,89900,"talking with two neighbours outside the shop");
                Check.True(closed.Contains("still closed") && closed.Contains("two neighbours"));
                string open=ClosedShopLeisure.Context("The Bell",90000,91200,90600,"browsing a market display");
                Check.True(open.Contains("now open") && !open.Contains("still closed"));
                string returning=ClosedShopLeisure.Context("The Bell",90000,91200,91201,"walking back to its entrance");
                Check.True(returning.Contains("walking back") && returning.Contains("The Bell"));
            }));
        }
        private static DailySchedule Visit(int key,int start,int end)
        {
            var data=new ResidentDestination {ExitWalkSeconds=600,
                ExitRoute=new List<SavedRoutePoint>{new SavedRoutePoint{X=10,Z=0},new SavedRoutePoint{X=0,Z=0}}};
            data.Stops.Add(new ResidentVisitStop {BuildingKey=key,Purpose=ResidentVisitPurpose.Social,
                OpenMinute=0,CloseMinute=0,WalkSeconds=600,DwellSeconds=(end-start)*60,
                Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=0,Z=0},new SavedRoutePoint{X=10,Z=0}}});
            return DailySchedule.ItineraryDay(10,data,(start-10)*60,360,1320);
        }
        private static ResidentDestination TwoStops()
        {
            var data=new ResidentDestination {ExitWalkSeconds=120,ExitRoute=new List<SavedRoutePoint>{new SavedRoutePoint{X=20},new SavedRoutePoint{X=0}}};
            data.Stops.Add(new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Shop,OpenMinute=600,CloseMinute=1200,
                WalkSeconds=60,DwellSeconds=1200,Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=0},new SavedRoutePoint{X=10}}});
            data.Stops.Add(new ResidentVisitStop {BuildingKey=30,Purpose=ResidentVisitPurpose.Shop,OpenMinute=480,CloseMinute=1200,
                WalkSeconds=60,DwellSeconds=1800,Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=10},new SavedRoutePoint{X=20}}});
            return data;
        }
    }
}
