using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Loaded-scene leases only. Durable intent belongs to the visit/action.
    public sealed class SeatOccupancy
    {
        private sealed class Claim {public string Owner;public double Until;}
        private readonly HashSet<string> slots;
        private readonly Dictionary<string,Claim> claims=new Dictionary<string,Claim>(StringComparer.Ordinal);
        public SeatOccupancy(IEnumerable<string> ids) {slots=new HashSet<string>(ids,StringComparer.Ordinal);}
        public static List<double> Offsets(double length,bool bench)
        {
            var result=new List<double>();
            if(double.IsNaN(length) || double.IsInfinity(length) || length<0.5) return result;
            int count=bench ? Math.Min(8,1+(int)Math.Floor((length-0.5)/0.8)) : 1;
            for(int i=0;i<count;i++) result.Add((i-(count-1)*0.5)*0.8);
            return result;
        }
        public bool TryClaim(string owner,string slot,double now)
        {
            if(string.IsNullOrWhiteSpace(owner) || double.IsNaN(now) || double.IsInfinity(now)) throw new ArgumentException("Invalid seat owner/time.");
            if(slot==null || !slots.Contains(slot)) return false;
            Claim claim;
            if(claims.TryGetValue(slot,out claim) && claim.Until>=now && claim.Owner!=owner) return false;
            Release(owner);
            foreach(var id in new List<string>(claims.Keys)) if(claims[id].Until<now) claims.Remove(id);
            claims[slot]=new Claim {Owner=owner,Until=now+2};return true;
        }
        // Validate both leases before releasing either previous seat. In this
        // scene-thread store the two writes form one atomic pair reservation.
        public bool TryClaimPair(string firstOwner,string firstSlot,string secondOwner,string secondSlot,double now)
        {
            if(string.IsNullOrWhiteSpace(firstOwner) || string.IsNullOrWhiteSpace(secondOwner) ||
                double.IsNaN(now) || double.IsInfinity(now))throw new ArgumentException("Invalid seat owner/time.");
            if(firstOwner==secondOwner || firstSlot==secondSlot ||
                !Available(firstOwner,firstSlot,now) || !Available(secondOwner,secondSlot,now))return false;
            Release(firstOwner);Release(secondOwner);
            foreach(var id in new List<string>(claims.Keys))if(claims[id].Until<now)claims.Remove(id);
            claims[firstSlot]=new Claim{Owner=firstOwner,Until=now+2};
            claims[secondSlot]=new Claim{Owner=secondOwner,Until=now+2};return true;
        }
        public bool Owns(string owner,string slot,double now)
        {Claim claim;return slot!=null && claims.TryGetValue(slot,out claim) && claim.Owner==owner && claim.Until>=now;}
        public bool Available(string owner,string slot,double now)
        {Claim claim;return slot!=null&&slots.Contains(slot)&&(!claims.TryGetValue(slot,out claim)||claim.Until<now||claim.Owner==owner);}
        public void Release(string owner)
        {foreach(var slot in new List<string>(claims.Keys)) if(claims[slot].Owner==owner) claims.Remove(slot);}
    }
}
