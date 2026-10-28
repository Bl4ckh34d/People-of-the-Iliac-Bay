using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum TravelerPhase { NotArrived, Arriving, AtInn, Sleeping, WaitingForGate, Leaving, Departed }
    public struct TravelerSnapshot
    {
        public readonly TravelerPhase Phase;
        public readonly RoutePoint Position;
        public readonly bool WithinStay;
        public TravelerSnapshot(TravelerPhase phase,RoutePoint position,bool withinStay)
        { Phase=phase; Position=position; WithinStay=withinStay; }
    }
    public static class TravelerExteriorMaterialization
    {
        public static bool ShouldExist(TravelerAdmission admission,TravelerPhase phase,bool observedOutdoorArrival,bool catchingUp=false)
        {
            if(phase==TravelerPhase.NotArrived || phase==TravelerPhase.Departed) return false;
            if(phase==TravelerPhase.Arriving || phase==TravelerPhase.Leaving) return true;
            if(admission==TravelerAdmission.Refused) return true;
            return admission==TravelerAdmission.Pending && observedOutdoorArrival && !catchingUp;
        }
    }
    // An itinerary is a proposed visit, not a payment or admission. The lodging ledger
    // must accept its stay interval before a runtime actor can occupy the room.
    public sealed class TravelerJourney
    {
        public double EntryAt { get; private set; }
        public double CheckInAt { get; private set; }
        public double CheckOutAt { get; private set; }
        public double LeaveInnAt { get; private set; }
        public double ExitAt { get; private set; }
        private RoutePoint[] route;
        private double[] distance;
        private double commute;
        private int wakeMinute,sleepMinute;
        private TravelerJourney originalApproach,diversionApproach,campDeparture;
        private double diversionAt,diversionSeconds;
        private bool walled;

        public static TravelerJourney CreateDiverted(TravelerJourney original,IEnumerable<RoutePoint> diversion,double at,double seconds,bool camping=false)
        {
            if(original==null || !Finite(at) || !Finite(seconds) || seconds<=0 || seconds>=86400 || at<original.CheckInAt || at+seconds>=original.CheckOutAt)
                throw new ArgumentException("Invalid traveler diversion interval.");
            var branch=Create(0,at,1,seconds,false,diversion);
            var joined=new List<RoutePoint>(original.route);
            var door=joined[joined.Count-1];var start=branch.route[0];
            if(Math.Abs(door.X-start.X)>0.001 || Math.Abs(door.Z-start.Z)>0.001)throw new ArgumentException("Disconnected traveler diversion.");
            for(int i=1;i<branch.route.Length;i++)joined.Add(branch.route[i]);
            var result=Create(0,original.EntryAt,1,original.commute+seconds,false,joined);
            result.EntryAt=original.EntryAt;result.CheckInAt=at+seconds;result.CheckOutAt=original.CheckOutAt;
            result.commute=original.commute+seconds;
            result.walled=original.walled;
            if(camping)
            {
                int boundary=-1;var exit=original.route[0];
                for(int i=1;i<branch.route.Length-1;i++)
                    if(Math.Abs(branch.route[i].X-exit.X)<0.001 && Math.Abs(branch.route[i].Z-exit.Z)<0.001){boundary=i;break;}
                if(boundary<0)throw new ArgumentException("Camp route does not cross the measured settlement exit.");
                var outside=new List<RoutePoint>();for(int i=boundary;i<branch.route.Length;i++)outside.Add(branch.route[i]);
                double fraction=(branch.distance[branch.distance.Length-1]-branch.distance[boundary])/branch.distance[branch.distance.Length-1];
                result.commute=seconds*fraction;
                result.campDeparture=Create(0,at,1,result.commute,false,outside);
                // A camper already outside does not wait for a closed city gate
                // or re-enter town merely to walk out again.
                result.walled=false;
            }
            result.ExitAt=result.walled?NextOpen(result.CheckOutAt+result.commute):result.CheckOutAt+result.commute;
            result.LeaveInnAt=result.ExitAt-result.commute;
            result.wakeMinute=original.wakeMinute;result.sleepMinute=original.sleepMinute;
            result.originalApproach=original;result.diversionApproach=branch;result.diversionAt=at;result.diversionSeconds=seconds;
            return result;
        }

        public static TravelerJourney Create(uint seed,double desiredArrival,int nights,double commuteSeconds,bool walled,IEnumerable<RoutePoint> route)
        {
            if(!Finite(desiredArrival) || desiredArrival<0 || nights<1 || nights>350 ||
                !Finite(commuteSeconds) || commuteSeconds<=0 || commuteSeconds>=86400 || route==null)
                throw new ArgumentException("Invalid traveler itinerary.");
            var points=new List<RoutePoint>(route).ToArray();
            if(points.Length<2) throw new ArgumentException("Traveler route requires entry and inn endpoints.");
            var lengths=new double[points.Length];
            for(int i=0;i<points.Length;i++)
            {
                if(!Finite(points[i].X) || !Finite(points[i].Z)) throw new ArgumentException("Invalid traveler route point.");
                if(i==0) continue;
                double dx=points[i].X-points[i-1].X,dz=points[i].Z-points[i-1].Z;
                lengths[i]=lengths[i-1]+Math.Sqrt(dx*dx+dz*dz);
            }
            if(lengths[lengths.Length-1]<=0 || !Finite(lengths[lengths.Length-1])) throw new ArgumentException("Traveler route has no finite length.");
            uint variation=StableSeed.Combine(seed,0x74726176,0x696e6e,0);
            var result=new TravelerJourney { route=points,distance=lengths,commute=commuteSeconds,walled=walled,
                wakeMinute=360+(int)(variation%120),sleepMinute=1320+(int)((variation>>8)%120) };
            result.EntryAt=walled ? NextOpen(desiredArrival) : desiredArrival;
            result.CheckInAt=result.EntryAt+commuteSeconds;
            // Arrivals before dawn use the current night's checkout; later arrivals
            // buy the following night. Checkout varies from 09:00 through 10:59.
            double checkoutDay=Math.Floor((result.CheckInAt+18*3600)/86400)+nights-1;
            result.CheckOutAt=checkoutDay*86400+(540+(variation>>16)%120)*60;
            result.ExitAt=walled ? NextOpen(result.CheckOutAt+commuteSeconds) : result.CheckOutAt+commuteSeconds;
            result.LeaveInnAt=result.ExitAt-commuteSeconds;
            if(!Finite(result.ExitAt) || result.EntryAt>=result.CheckInAt || result.CheckOutAt<=result.CheckInAt ||
                result.LeaveInnAt<result.CheckOutAt || result.ExitAt<=result.LeaveInnAt)
                throw new ArgumentException("Traveler times cannot be represented safely.");
            return result;
        }
        public TravelerSnapshot Evaluate(double now)
        {
            if(!Finite(now) || now<0) throw new ArgumentOutOfRangeException(nameof(now));
            if(originalApproach!=null && now<CheckInAt)
            {
                if(now<diversionAt)return originalApproach.Evaluate(now);
                var sample=diversionApproach.Sample(TravelerPhase.Arriving,(now-diversionAt)/diversionSeconds,false);
                return sample;
            }
            if(now<EntryAt) return Sample(TravelerPhase.NotArrived,0,false);
            if(now<CheckInAt) return Sample(TravelerPhase.Arriving,(now-EntryAt)/commute,false);
            if(now<CheckOutAt)
            {
                double minute=now%86400/60;
                return Sample(minute<wakeMinute || minute>=sleepMinute ? TravelerPhase.Sleeping : TravelerPhase.AtInn,1,true);
            }
            if(now<LeaveInnAt) return Sample(TravelerPhase.WaitingForGate,1,false);
            if(now<ExitAt) return (campDeparture??this).Sample(TravelerPhase.Leaving,1-(now-LeaveInnAt)/commute,false);
            return Sample(TravelerPhase.Departed,0,false);
        }
        public bool TryTravelEndpoint(TravelerPhase phase,out RoutePoint endpoint)
        {
            if(phase==TravelerPhase.Arriving)
            { endpoint=route[route.Length-1];return true; }
            if(phase==TravelerPhase.Leaving)
            { endpoint=route[0];return true; }
            endpoint=default(RoutePoint);return false;
        }
        public static TravelerJourney CreateRefused(uint seed,double desiredArrival,double commuteSeconds,bool walled,
            IEnumerable<RoutePoint> route,double refusedAt)
        {
            var result=Create(seed,desiredArrival,1,commuteSeconds,walled,route);
            if(!Finite(refusedAt) || refusedAt+0.001<result.CheckInAt) throw new ArgumentException("Refusal precedes arrival.");
            result.CheckOutAt=Math.Max(result.CheckInAt,refusedAt);
            result.ExitAt=walled ? NextOpen(result.CheckOutAt+commuteSeconds) : result.CheckOutAt+commuteSeconds;
            result.LeaveInnAt=result.ExitAt-commuteSeconds;
            if(!Finite(result.ExitAt) || result.ExitAt<=result.LeaveInnAt) throw new ArgumentException("Invalid refused departure.");
            return result;
        }
        private TravelerSnapshot Sample(TravelerPhase phase,double progress,bool withinStay)
        {
            double along=distance[distance.Length-1]*Math.Max(0,Math.Min(1,progress));
            for(int i=1;i<route.Length;i++)
            {
                double length=distance[i]-distance[i-1];
                if(length<=0 || along>distance[i]) continue;
                double f=(along-distance[i-1])/length;
                return new TravelerSnapshot(phase,new RoutePoint(route[i-1].X+(route[i].X-route[i-1].X)*f,
                    route[i-1].Z+(route[i].Z-route[i-1].Z)*f),withinStay);
            }
            return new TravelerSnapshot(phase,route[route.Length-1],withinStay);
        }
        private static double NextOpen(double time)
        {
            double day=Math.Floor(time/86400)*86400,hour=(time-day)/3600;
            if(hour<6) return day+6*3600;
            if(hour>=18) return day+30*3600;
            return time;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
