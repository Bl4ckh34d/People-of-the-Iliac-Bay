using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class ResidentTravelDemandTests
    {
        private static List<Resident> People(int count)
        {var list=new List<Resident>(); for(int i=0;i<count;i++) list.Add(new Resident {Id="resident:"+i,HomeKey=i,AppearanceSeed=(uint)i}); return list;}
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Unvisited demand samples bounded real homes and preserves selected families on completion",()=>{
                var homes=new List<HomeProfile>();for(int i=1;i<=100;i++) homes.Add(new HomeProfile(i,3,180));
                var full=new WorldStateStore(12);full.GetOrCreateFamilies(100,homes.GetRange(0,8));
                var expected=full.ResidentTravelProposals(100,4,new[]{200,300},1000,8);
                var sparse=new WorldStateStore(12);var actual=sparse.ReserveResidentTravelProposals(100,4,homes,new[]{300,200},1000,8);
                Check.Equal(expected.Count,actual.Count);Check.True(actual.Count>0);
                var reserved=sparse.Snapshot().Settlements[0];Check.True(reserved.IsPartial);
                Check.True(reserved.Households.Count<=actual.Count && reserved.Households.Count<homes.Count);
                for(int i=0;i<actual.Count;i++)
                {
                    Check.Equal(expected[i].ResidentId,actual[i].ResidentId);Check.Near(expected[i].DesiredDeparture,actual[i].DesiredDeparture);
                    Check.Equal(expected[i].DestinationSettlement,actual[i].DestinationSettlement);
                }
                sparse.Restore(sparse.Snapshot());var completed=sparse.GetOrCreateFamilies(100,homes);
                Check.Equal(100,completed.Households.Count);Check.True(!completed.IsPartial);
                foreach(var proposal in actual) Check.True(sparse.ResolveResident(100,proposal.ResidentId)!=null);
                var empty=new WorldStateStore(12);Check.Equal(0,empty.ReserveResidentTravelProposals(100,4,homes,new[]{200},0,8).Count);
                Check.True(!empty.HasSettlement(100));
            }));
            cases.Add(("Unvisited demand respects reserved deaths and trips without filling unrelated homes",()=>{
                var homes=new List<HomeProfile>();for(int i=1;i<=50;i++) homes.Add(new HomeProfile(i,3,180));
                var store=new WorldStateStore(12);var initial=store.ReserveResidentTravelProposals(100,4,homes,new[]{200},1000,8);
                Check.True(initial.Count>=2);
                var state=store.Snapshot();foreach(var h in state.Settlements[0].Households)
                    foreach(var p in h.Residents) if(p.Id==initial[0].ResidentId) p.IsAlive=false;
                store.Restore(state);
                store.ScheduleResidentTrip(ResidentTrip.Create(initial[1].ResidentId,100,200,initial[1].DesiredDeparture,5*86400,60,
                    new[]{new RoutePoint(10,10),new RoutePoint(11,10)}));
                var repeated=store.ReserveResidentTravelProposals(100,4,homes,new[]{200},1000,8);
                Check.True(!repeated.Exists(p=>p.ResidentId==initial[0].ResidentId || p.ResidentId==initial[1].ResidentId));
                Check.True(store.Snapshot().Settlements[0].IsPartial);
            }));
            cases.Add(("Reserved traveler household survives save and later full hometown generation",()=>{
                var store=new WorldStateStore(12);var profile=new HomeProfile(10,3,180);
                var reserved=store.ReserveHousehold(100,profile);var person=reserved.Residents[0];
                person.FullName="Reserved Kinghart";reserved.Surname="Kinghart";
                Check.True(store.Snapshot().Settlements[0].IsPartial);
                Check.Equal(1,store.Snapshot().Settlements[0].Households.Count);
                store.ScheduleResidentTrip(ResidentTrip.Create(person.Id,100,200,100,600,60,
                    new[]{new RoutePoint(10,10),new RoutePoint(11,10)}));
                var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
                var complete=restored.GetOrCreateFamilies(100,new[]{profile,new HomeProfile(20,2,100)});
                Check.True(!complete.IsPartial);Check.Equal(2,complete.Households.Count);
                var kept=complete.Households.Find(h=>h.BuildingKey==10);
                Check.Equal(person.Id,kept.Residents[0].Id);Check.Equal("Reserved Kinghart",kept.Residents[0].FullName);
                Check.Equal("Kinghart",kept.Surname);Check.Equal(reserved.Residents.Count,kept.Residents.Count);
                Check.True(restored.IsResidentAway(person.Id,200));
                kept.Residents[0].IsAlive=false;
                Check.True(!restored.ReserveHousehold(100,profile).Residents[0].IsAlive);
                Check.Equal(2,restored.Snapshot().Settlements[0].Households.Count);
            }));
            cases.Add(("Reserved households reject missing homes and capacity loss without partial completion",()=>{
                var store=new WorldStateStore(12);var profile=new HomeProfile(10,3,180);
                store.ReserveHousehold(100,profile);
                Check.Throws<ArgumentException>(()=>store.GetOrCreateFamilies(100,new[]{new HomeProfile(20,2,100)}));
                Check.True(store.Snapshot().Settlements[0].IsPartial);
                Check.Throws<ArgumentException>(()=>store.GetOrCreateFamilies(100,new[]{new HomeProfile(10,0,180)}));
                Check.True(store.Snapshot().Settlements[0].IsPartial);
                Check.Throws<ArgumentException>(()=>store.ReserveHousehold(200,new HomeProfile(30,0,100)));
                Check.True(!store.HasSettlement(200));
            }));
            cases.Add(("Resident departure demand is sparse, capped and independent of enumeration order",()=>{
                var people=People(10000); var a=ResidentTravelDemand.Select(12,100,4,people,new[]{300,200},10,1000);
                Check.True(a.Count>0 && a.Count<200); people.Reverse();
                var b=ResidentTravelDemand.Select(12,100,4,people,new[]{200,300},10,1000);
                Check.Equal(a.Count,b.Count); var times=new HashSet<double>();
                for(int i=0;i<a.Count;i++)
                {
                    Check.Equal(a[i].ResidentId,b[i].ResidentId); Check.Near(a[i].DesiredDeparture,b[i].DesiredDeparture);
                    Check.Equal(a[i].DestinationSettlement,b[i].DestinationSettlement);
                    Check.True(a[i].DesiredDeparture>=4*86400+6*3600 && a[i].DesiredDeparture<4*86400+14*3600);
                    Check.True(a[i].VisitDays>=1 && a[i].VisitDays<=4); times.Add(a[i].DesiredDeparture);
                }
                Check.True(times.Count>a.Count/2);
                Check.Equal(3,ResidentTravelDemand.Select(12,100,4,people,new[]{200},1000,3).Count);
            }));
            cases.Add(("Departure demand excludes children, deaths, quest bindings and native service staff",()=>{
                var people=People(5); people[0].AgeGroup=ResidentAgeGroup.Child; people[1].IsAlive=false;
                people[2].IsQuestProtected=true; people[3].NativeIdentity=new NativePerson();
                var proposals=ResidentTravelDemand.Select(12,100,4,people,new[]{100,200},1000,20);
                Check.Equal(1,proposals.Count); Check.Equal(people[4].Id,proposals[0].ResidentId);
                Check.Equal(200,proposals[0].DestinationSettlement);
                Check.Equal(0,ResidentTravelDemand.Select(12,100,4,people,new[]{100},1000,20).Count);
            }));
            cases.Add(("Store departure proposals never create visitors or overlap an existing resident trip",()=>{
                var store=new WorldStateStore(12); var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                var proposal=store.ResidentTravelProposals(100,4,new[]{200},1000,8)[0];
                store.ScheduleResidentTrip(ResidentTrip.Create(person.Id,100,200,proposal.DesiredDeparture,5*86400,3600,
                    new[]{new RoutePoint(10,10),new RoutePoint(11,10)}));
                var restored=new WorldStateStore(1); restored.Restore(store.Snapshot());
                Check.Equal(0,restored.ResidentTravelProposals(100,4,new[]{200},1000,8).Count);
                Check.Equal(1,restored.ResidentTravelProposals(100,11,new[]{200},1000,8).Count);
                Check.Equal(0,restored.Snapshot().Travelers.Count); Check.Equal(1,restored.Snapshot().ResidentTrips.Count);
            }));
        }
    }
}
