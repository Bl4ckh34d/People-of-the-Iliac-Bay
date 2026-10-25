using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class TravelerOrigin
    {
        public int SettlementId,Race;
    }
    public sealed class TravelerProposal
    {
        public TravelerPerson Person;
        public TravelerVisit Visit;
    }
    public static class TravelerDemand
    {
        // Two ordinary arrival slots per inn/day. Reconstruct only stays that
        // can still be present; elapsed world history is never generated.
        public static int EnsureInnTravelers(WorldStateStore store,int town,int tavern,int day,
            Func<int,int,TravelerProposal> create)
        {
            if(store==null || town<0 || tavern<0 || day<0 || create==null)
                throw new ArgumentException("Invalid inn traveler demand.");
            int added=0;
            for(int offset=Math.Min(4,day);offset>=0;offset--)
                for(int slot=0;slot<2;slot++)
                {
                    int arrivalDay=day-offset;
                    string key=IdentityKey(town,tavern,arrivalDay,slot);
                    if(store.Traveler("traveler:"+key)!=null)continue;
                    var proposal=create(arrivalDay,slot);
                    if(proposal==null || proposal.Person==null || proposal.Visit==null ||
                        proposal.Person.Id!="traveler:"+key || proposal.Visit.Id!="visit:"+key ||
                        proposal.Visit.PersonId!=proposal.Person.Id || proposal.Visit.SettlementId!=town || proposal.Visit.TavernKey!=tavern)
                        throw new ArgumentException("Inn demand returned a different traveler identity.");
                    store.RegisterTraveler(proposal.Person);store.RegisterTravelerVisit(proposal.Visit);added++;
                }
            return added;
        }

        private static string IdentityKey(int town,int tavern,int day,int slot)
        {return town.ToString(CultureInfo.InvariantCulture)+":"+tavern.ToString(CultureInfo.InvariantCulture)+":"+
            day.ToString(CultureInfo.InvariantCulture)+":"+slot.ToString(CultureInfo.InvariantCulture);}

        public static TravelerProposal Create(uint worldSeed,int town,int tavern,int day,int slot,
            IEnumerable<TravelerOrigin> origins,int minimumNights,int maximumNights,bool walled,
            IEnumerable<RoutePoint> route,double gameSecondsPerMeter,Func<TravelerOrigin,uint,ResidentSex,string> name)
        {
            if(town<0 || tavern<0 || day<0 || slot<0 || slot>=10000 || origins==null || route==null || name==null ||
                minimumNights<1 || maximumNights<minimumNights || maximumNights>350 ||
                double.IsNaN(gameSecondsPerMeter) || double.IsInfinity(gameSecondsPerMeter) || gameSecondsPerMeter<=0)
                throw new ArgumentException("Invalid traveler demand inputs.");
            var pool=new SortedDictionary<int,TravelerOrigin>();
            foreach(var origin in origins)
            {
                if(origin==null || origin.SettlementId<0 || origin.Race<0 || pool.ContainsKey(origin.SettlementId))
                    throw new ArgumentException("Invalid or duplicate traveler origin.");
                if(origin.SettlementId!=town) pool.Add(origin.SettlementId,new TravelerOrigin { SettlementId=origin.SettlementId,Race=origin.Race });
            }
            if(pool.Count==0) throw new ArgumentException("No nonlocal traveler origins.");
            uint seed=StableSeed.Combine(StableSeed.Combine(worldSeed,(uint)town,(uint)tavern,(uint)day),(uint)slot,0x76697369,0);
            var selected=new List<TravelerOrigin>(pool.Values)[(int)(seed%(uint)pool.Count)];
            var sex=(seed&256)==0 ? ResidentSex.Male : ResidentSex.Female;
            uint timing=StableSeed.Combine(seed,0x61727269,0,0);
            double arrival=(double)day*86400+(walled ? 21600+timing%43200 : timing%86400);
            int nights=minimumNights+(int)(StableSeed.Combine(seed,0x73746179,0,0)%(uint)(maximumNights-minimumNights+1));
            var points=new List<RoutePoint>(route); double length=0;
            for(int i=1;i<points.Count;i++)
            {
                double x=points[i].X-points[i-1].X,z=points[i].Z-points[i-1].Z;
                length+=Math.Sqrt(x*x+z*z);
            }
            double commute=length*gameSecondsPerMeter;
            TravelerJourney.Create(seed,arrival,nights,commute,walled,points); // Validate the whole proposed itinerary before returning it.
            string key=IdentityKey(town,tavern,day,slot);
            string fullName=name(selected,seed,sex);
            if(string.IsNullOrWhiteSpace(fullName) || fullName.Length>512) throw new ArgumentException("Invalid traveler name.");
            var person=new TravelerPerson { Id="traveler:"+key,Name=fullName,HomeSettlement=selected.SettlementId,
                AppearanceSeed=seed,Sex=sex,Race=selected.Race };
            var visit=new TravelerVisit { Id="visit:"+key,PersonId=person.Id,SettlementId=town,TavernKey=tavern,
                Nights=nights,Walled=walled,DesiredArrival=arrival,CommuteSeconds=commute };
            foreach(var point in points) visit.Route.Add(new SavedRoutePoint { X=point.X,Z=point.Z });
            return new TravelerProposal { Person=person,Visit=visit };
        }
    }
}
