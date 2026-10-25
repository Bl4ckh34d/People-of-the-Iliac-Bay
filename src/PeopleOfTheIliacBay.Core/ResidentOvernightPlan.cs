using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    // Routes and service opening windows come from the geometry planner.
    // This stage adds realistic night durations and one continuous room hold.
    public static class ResidentOvernightPlan
    {
        public static int RequiredVisitDays(ResidentTrip source,int requestedDays)
        {
            if(requestedDays<1 || requestedDays>4) throw new ArgumentException("Visit duration must be one to four days.");
            return requestedDays==1 && NeedsLodgingFirst(source) ? 2 : requestedDays;
        }
        public static bool NeedsLodgingFirst(ResidentTrip source)
        {
            var trip=ResidentTrip.CopyValidated(source);
            if(trip.Destination==null || trip.Destination.Stops.Count!=1) throw new ArgumentException("Initial service required.");
            foreach(var visit in ResidentVisitJourney.Create(trip.ArrivesAt,trip.Destination).ScheduledVisits())
                return visit.Purpose!=ResidentVisitPurpose.Inn && visit.EnterAt-visit.ArriveAt>=2*3600 &&
                    (Math.Floor(visit.EnterAt/86400)>Math.Floor(visit.ArriveAt/86400) || visit.ArriveAt%86400<6*3600);
            return false;
        }
        public static ResidentTrip PrependInn(ResidentTrip source,int building,IReadOnlyList<RoutePoint> serviceToInn,double secondsPerMetre)
        {
            var trip=ResidentTrip.CopyValidated(source);
            if(trip.Destination==null || trip.Destination.Stops.Count!=1 || trip.InnReceptions.Count!=0 || trip.InnStays.Count!=0 || trip.ServiceExits.Count!=0 ||
                building<0 || serviceToInn==null || serviceToInn.Count<2 || double.IsNaN(secondsPerMetre) || double.IsInfinity(secondsPerMetre) || secondsPerMetre<=0)
                throw new ArgumentException("Invalid lodging-first route.");
            var service=trip.Destination.Stops[0];
            if(service.BuildingKey==building || service.Purpose==ResidentVisitPurpose.Inn) throw new ArgumentException("Distinct service and inn required.");
            var approach=new List<SavedRoutePoint>(service.Route);double metres=0;
            for(int i=0;i<serviceToInn.Count;i++)
            {
                var p=serviceToInn[i];approach.Add(new SavedRoutePoint {X=p.X,Z=p.Z});
                if(i>0) {double x=p.X-serviceToInn[i-1].X,z=p.Z-serviceToInn[i-1].Z;metres+=Math.Sqrt(x*x+z*z);}
            }
            var inn=new ResidentVisitStop {BuildingKey=building,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=service.WalkSeconds+metres*secondsPerMetre,
                DwellSeconds=1,Route=approach};
            service.Route=new List<SavedRoutePoint>();service.WalkSeconds=metres*secondsPerMetre;
            for(int i=serviceToInn.Count-1;i>=0;i--) service.Route.Add(new SavedRoutePoint {X=serviceToInn[i].X,Z=serviceToInn[i].Z});
            trip.Destination.Stops.Insert(0,inn);
            return ResidentTrip.CopyValidated(trip);
        }
        public static ResidentTrip StayAtInn(ResidentTrip source,int days,uint seed)
        {
            var trip=ResidentTrip.CopyValidated(source);
            if(days<2 || days>4 || trip.Destination==null || trip.Destination.Stops.Count!=1 ||
                (trip.Destination.Stops[0].Purpose!=ResidentVisitPurpose.Social && trip.Destination.Stops[0].Purpose!=ResidentVisitPurpose.Inn))
                throw new ArgumentException("Inn-only stay requires one social or lodging destination and two to four visit days.");
            var inn=trip.Destination.Stops[0];inn.Purpose=ResidentVisitPurpose.Inn;inn.OpenMinute=inn.CloseMinute=0;
            trip=Schedule(trip,seed);inn=trip.Destination.Stops[0];
            inn.DwellSeconds+=(days-2)*86400;inn.KeepRoomUntil+=(days-2)*86400;
            return ResidentTrip.CopyValidated(trip);
        }
        public static ResidentTrip WithReceptions(ResidentTrip source,int building,List<GuardPoint3> route,double secondsPerMetre,double serviceSeconds,string keeperId=null)
        {
            var trip=ResidentTrip.CopyValidated(source);
            if(trip.Destination==null || trip.InnStays.Count!=0 || trip.ServiceExits.Count!=0 || trip.InnReceptions.Count!=0)
                throw new ArgumentException("Reception planning requires an uncommitted itinerary.");
            foreach(var visit in ResidentVisitJourney.Create(trip.ArrivesAt,trip.Destination).ScheduledVisits())
            {
                if(visit.Purpose!=ResidentVisitPurpose.Inn) continue;
                if(visit.BuildingKey!=building) throw new ArgumentException("Reception geometry belongs to another inn.");
                trip.InnReceptions.Add(ResidentInnReception.Copy(new ResidentInnReception {SettlementId=trip.DestinationSettlement,KeeperId=keeperId,BuildingKey=building,
                    VisitEnterAt=visit.EnterAt,VisitLeaveAt=visit.LeaveAt,StartedAt=visit.EnterAt,
                    SecondsPerMetre=secondsPerMetre,ServiceSeconds=serviceSeconds,Route=route}));
            }
            if(trip.InnReceptions.Count==0) throw new ArgumentException("No inn visits to prepare.");
            return ResidentTrip.CopyValidated(trip);
        }
        // Prepare only this inn's scoped visits, retaining other towns' planned service.
        // Admission remains first-come when the traveler finishes counter service.
        public static ResidentTrip WithSettlementReceptions(ResidentTrip source,int settlement,int building,List<GuardPoint3> route,double secondsPerMetre,double serviceSeconds,string keeperId=null)
        {
            var trip=ResidentTrip.CopyValidated(source);
            if(trip.InnStays.Count!=0 || trip.ServiceExits.Count!=0)
                throw new ArgumentException("Reception planning requires an uncommitted itinerary.");
            int added=0;
            foreach(var visit in trip.ScheduledInnVisits())
            {
                if(visit.SettlementId!=settlement || visit.BuildingKey!=building) continue;
                foreach(var existing in trip.InnReceptions)
                    if(existing.VisitId==visit.VisitId) throw new ArgumentException("Visit reception is already prepared.");
                trip.InnReceptions.Add(ResidentInnReception.Copy(new ResidentInnReception {
                    VisitId=visit.VisitId,SettlementId=settlement,BuildingKey=building,KeeperId=keeperId,
                    VisitEnterAt=visit.VisitEnterAt,VisitLeaveAt=visit.VisitLeaveAt,StartedAt=visit.VisitEnterAt,
                    SecondsPerMetre=secondsPerMetre,ServiceSeconds=serviceSeconds,Route=route}));
                added++;
            }
            if(added==0) throw new ArgumentException("No matching inn visits to prepare.");
            return ResidentTrip.CopyValidated(trip);
        }
        public static ResidentTrip Schedule(ResidentTrip skeleton,uint seed)
        {
            var trip=ResidentTrip.CopyValidated(skeleton);
            if(trip.Destination==null || trip.InnStays.Count!=0 || trip.InnReceptions.Count!=0 || trip.ServiceExits.Count!=0)
                throw new ArgumentException("Overnight timing requires an uncommitted itinerary.");
            var inns=new List<int>(); int building=-1;
            for(int i=0;i<trip.Destination.Stops.Count;i++)
            {
                var stop=trip.Destination.Stops[i];
                if(stop.Purpose!=ResidentVisitPurpose.Inn) continue;
                if(building>=0 && building!=stop.BuildingKey) throw new ArgumentException("One stay cannot hold rooms in different inns.");
                building=stop.BuildingKey; inns.Add(i); stop.KeepRoomUntil=0;
            }
            if(inns.Count<1 || inns.Count>7) throw new ArgumentException("Overnight itinerary needs one to seven nights.");
            double checkout=0;
            foreach(int index in inns)
            {
                var journey=ResidentVisitJourney.Create(trip.ArrivesAt,trip.Destination);
                var visits=new List<ResidentVisitSnapshot>(journey.ScheduledVisits());
                double arrival=visits[index].EnterAt;
                // Arrivals before dawn use that morning; later arrivals use the
                // following morning. Never schedule less than two hours' rest.
                int wakeMinute=9*60+(int)(StableSeed.Combine(seed,(uint)index,0x6e696768,0)%120);
                checkout=Math.Floor(arrival/86400)*86400+wakeMinute*60;
                if(arrival%86400>=6*3600 || checkout-arrival<2*3600) checkout+=86400;
                var inn=trip.Destination.Stops[index];
                if(inn.OpenMinute!=inn.CloseMinute) throw new ArgumentException("Inn overnight stay needs all-day interior access.");
                inn.DwellSeconds=checkout-arrival;
            }
            trip.Destination.Stops[inns[0]].KeepRoomUntil=checkout;
            return ResidentTrip.CopyValidated(trip);
        }
    }
}
