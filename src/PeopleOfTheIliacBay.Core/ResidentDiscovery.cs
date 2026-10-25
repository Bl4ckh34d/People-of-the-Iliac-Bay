using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public static class ResidentDiscovery
    {
        public static List<int> CandidateDepartureDays(double now,double roadSeconds)
        {
            if(double.IsNaN(now) || double.IsInfinity(now) || now<0 || now/86400>int.MaxValue-2 ||
                double.IsNaN(roadSeconds) || double.IsInfinity(roadSeconds) || roadSeconds<=0)
                throw new ArgumentException("Invalid discovery interval.");
            double first=Math.Floor((now+12*3600-roadSeconds)/86400);
            int today=(int)(now/86400);var days=new List<int>();
            for(int offset=0;offset<2;offset++)
            {
                double day=first+offset;
                if(day>=0 && day<=today) days.Add((int)day);
            }
            return days;
        }
    }
    public sealed partial class WorldStateStore
    {
        public bool CanDiscoverResidentTrips(int town)
        {
            if(town<0) throw new ArgumentException("Invalid discovery hometown.");
            var saved=state.Settlements.Find(s=>s.SettlementId==town);
            return saved==null || saved.IsPartial;
        }
        // Discovery may fill an unobserved origin's past departure, never alter a
        // known resident's history or invent a completed service/inn transaction.
        // Ordinary scheduling retains its separate future-commit rules.
        public bool DiscoverOutboundResidentTrip(ResidentTrip trip,double now)
        {
            if(!DefenseTime(now)) throw new ArgumentException("Invalid discovery time.");
            var copy=ResidentTrip.CopyValidated(trip);
            var home=state.Settlements.Find(s=>s.SettlementId==copy.HomeSettlement);
            if(home==null || !home.IsPartial || residentTripIndex.ContainsKey(copy.ResidentId) ||
                copy.OutboundStops.Count!=0 || copy.ReturnStops.Count!=0 ||
                copy.HomeRoute.Count<2 || copy.HomeWalkSeconds<=0 || copy.Destination==null ||
                copy.ServiceExits.Count!=0 || copy.InnStays.Count!=0 || copy.Evaluate(now).Phase!=ResidentTripPhase.Outbound)
                return false;
            var person=TripResident(state,copy.HomeSettlement,copy.ResidentId);
            if(person==null || !person.IsAlive || person.AgeGroup!=ResidentAgeGroup.Adult ||
                person.IsQuestProtected || person.NativeIdentity!=null) return false;
            ScheduleResidentTrip(copy);
            return true;
        }
    }
}
