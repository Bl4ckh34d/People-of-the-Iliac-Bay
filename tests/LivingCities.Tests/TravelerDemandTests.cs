using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class TravelerDemandTests
    {
        private static readonly TravelerOrigin[] Origins={ new TravelerOrigin { SettlementId=9,Race=1 },new TravelerOrigin { SettlementId=3,Race=2 } };
        private static TravelerProposal Make(int day=100,int slot=0,bool walled=true,TravelerOrigin[] origins=null)
        { return TravelerDemand.Create(42,1,7,day,slot,origins??Origins,1,4,walled,
            new[] { new RoutePoint(0,0),new RoutePoint(130,0) },12/1.3,
            (origin,seed,sex)=>origin.SettlementId+"-"+seed+"-"+sex); }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Traveler demand is stable across origin ordering and creates unique daily identities",()=>{
                var first=Make(); var second=Make(origins:new[] { Origins[1],Origins[0] });
                Check.Equal(first.Person.Id,second.Person.Id); Check.Equal(first.Person.Name,second.Person.Name);
                Check.Equal(first.Visit.DesiredArrival,second.Visit.DesiredArrival);
                Check.True(first.Person.Id!=Make(slot:1).Person.Id && first.Person.Id!=Make(day:101).Person.Id);
                Check.True(first.Person.HomeSettlement!=1); Check.Equal(first.Person.Id,first.Visit.PersonId);
                Check.Equal(1200d,Math.Round(first.Visit.CommuteSeconds));
            }));
            cases.Add(("Traveler demand varies stays and arrivals within city gate hours",()=>{
                var arrivals=new HashSet<double>(); var stays=new HashSet<int>(); bool night=false;
                for(int i=0;i<64;i++)
                {
                    var visit=Make(slot:i).Visit; var time=visit.DesiredArrival%86400;
                    Check.True(time>=21600 && time<64800); Check.True(visit.Nights>=1 && visit.Nights<=4);
                    arrivals.Add(time); stays.Add(visit.Nights);
                    var free=Make(slot:i,walled:false).Visit.DesiredArrival%86400;
                    night|=free<21600 || free>=64800;
                }
                Check.True(arrivals.Count>50 && stays.Count==4 && night);
            }));
            cases.Add(("Traveler demand rejects empty or local origin pools and invalid days",()=>{
                Check.Throws<ArgumentException>(()=>Make(origins:new TravelerOrigin[0]));
                Check.Throws<ArgumentException>(()=>Make(origins:new[] { new TravelerOrigin { SettlementId=1,Race=1 } }));
                Check.Throws<ArgumentException>(()=>Make(day:-1));
            }));
        }
    }
}
