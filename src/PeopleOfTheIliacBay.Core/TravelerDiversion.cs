using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable] public sealed class TravelerDiversion
    {
        public int PreferredTavernKey;
        public bool Camping;
        public double DecidedAt,WalkSeconds,OriginalCommuteSeconds;
        public List<SavedRoutePoint> OriginalRoute=new List<SavedRoutePoint>();
        public List<SavedRoutePoint> Route=new List<SavedRoutePoint>();
    }
    public sealed partial class WorldStateStore
    {
        public bool HasLodgingInventory(int settlement,int building)
        {return state.Lodgings.Exists(value=>value.SettlementId==settlement && value.BuildingKey==building);}
        public bool TravelerAwaitingLodgingDecision(string visitId,double now)
        {
            var visit=state.TravelerVisits.Find(value=>value.Id==visitId);
            if(visit==null || visit.Admission!=TravelerAdmission.Refused || visit.Diversion!=null || now<visit.AttemptedAt)return false;
            var person=Traveler(visit.PersonId);
            return person.IsAlive && now<TravelerJourney.Create(person.AppearanceSeed,visit.DesiredArrival,visit.Nights,
                visit.CommuteSeconds,visit.Walled,TravelerRoute(visit.Route)).CheckOutAt;
        }
        private static TravelerJourney TravelerReservedJourney(TravelerPerson person,TravelerVisit visit)
        {
            // Refusal starts a shelter search, not an early home arrival. Retain
            // the intended absence while actual fallback geometry is prepared.
            return visit.Admission==TravelerAdmission.Refused && visit.Diversion==null?
                TravelerJourney.Create(person.AppearanceSeed,visit.DesiredArrival,visit.Nights,visit.CommuteSeconds,visit.Walled,TravelerRoute(visit.Route)):
                Journey(person,visit);
        }
        // The adapter must supply an actually open reachable Inn, or a route whose
        // final camp point is verified outside the actual settlement rectangle.
        public bool TryRedirectTraveler(string visitId,int building,bool camping,IEnumerable<SavedRoutePoint> route,double walkSeconds,double now)
        {
            var visit=state.TravelerVisits.Find(v=>v.Id==visitId);
            if(visit==null || visit.Admission!=TravelerAdmission.Refused || visit.Diversion!=null ||
                !DefenseTime(now) || !DefenseTime(walkSeconds) || walkSeconds<=0 || walkSeconds>=86400 || route==null ||
                (camping?building!=-1:building<0 || building==visit.TavernKey))return false;
            var person=Traveler(visit.PersonId);var prior=TravelerJourney.Create(person.AppearanceSeed,visit.DesiredArrival,visit.Nights,
                visit.CommuteSeconds,visit.Walled,TravelerRoute(visit.Route));
            if(!person.IsAlive || now<visit.AttemptedAt || now<prior.CheckInAt || now+walkSeconds>=prior.CheckOutAt)return false;
            var path=new List<SavedRoutePoint>();
            foreach(var point in route)
            {if(point==null || !DefenseCoordinate(point.X) || !DefenseCoordinate(point.Z))return false;path.Add(new SavedRoutePoint {X=point.X,Z=point.Z});}
            if(path.Count<2 || path.Count>10000 || visit.Route.Count+path.Count>10000)return false;
            var door=visit.Route[visit.Route.Count-1];
            if(Math.Abs(path[0].X-door.X)>0.001 || Math.Abs(path[0].Z-door.Z)>0.001)return false;
            // Keep the accepted native sample anchored to its exact saved door.
            path[0]=new SavedRoutePoint {X=door.X,Z=door.Z};
            // Check route has real length before any booking mutation.
            TravelerJourney diverted;
            try{diverted=TravelerJourney.CreateDiverted(prior,TravelerRoute(path),now,walkSeconds,camping);}
            catch(ArgumentException){return false;}
            foreach(var other in state.TravelerVisits)
            {
                if(other==visit || other.PersonId!=visit.PersonId)continue;
                var trip=Journey(person,other);
                if(diverted.EntryAt<TravelerVisitConflictUntil(person,other) && trip.EntryAt<
                    Math.Max(diverted.ExitAt,visit.ReturnDepartureNotBefore)+visit.NativeReturnSeconds)return false;
            }
            foreach(var trip in state.ResidentTrips)
                if(trip.ResidentId==person.Id && diverted.EntryAt<trip.HomeAt && trip.HomeLeavesAt<
                    Math.Max(diverted.ExitAt,visit.ReturnDepartureNotBefore)+visit.NativeReturnSeconds)return false;
            string booking="traveler-booking:"+visit.Id;
            if(!camping && (!HasLodgingInventory(visit.SettlementId,building) || InnUnderInvestigation(visit.SettlementId,building,now) ||
                !TryBookLodging(visit.SettlementId,building,booking,person.Id,person.HomeSettlement,false,1,diverted.CheckInAt,diverted.CheckOutAt)))return false;
            var diversion=new TravelerDiversion {PreferredTavernKey=visit.TavernKey,Camping=camping,DecidedAt=now,
                WalkSeconds=walkSeconds,OriginalCommuteSeconds=visit.CommuteSeconds,OriginalRoute=visit.Route,Route=path};
            var combined=new List<SavedRoutePoint>(visit.Route);
            for(int i=1;i<path.Count;i++)combined.Add(new SavedRoutePoint {X=path[i].X,Z=path[i].Z});
            visit.Diversion=diversion;visit.Route=combined;visit.AttemptedAt=now;
            visit.Admission=camping?TravelerAdmission.Refused:TravelerAdmission.Admitted;
            visit.BookingId=camping?null:booking;
            if(!camping)visit.TavernKey=building;
            TravelerVisitRevision++;return true;
        }
        private static IEnumerable<RoutePoint> TravelerRoute(IEnumerable<SavedRoutePoint> source)
        {foreach(var point in source)yield return new RoutePoint(point.X,point.Z);}
        private static TravelerJourney DivertedTravelerJourney(TravelerPerson person,TravelerVisit visit)
        {
            var value=visit.Diversion;
            var original=TravelerJourney.Create(person.AppearanceSeed,visit.DesiredArrival,visit.Nights,value.OriginalCommuteSeconds,
                visit.Walled,TravelerRoute(value.OriginalRoute));
            return TravelerJourney.CreateDiverted(original,TravelerRoute(value.Route),value.DecidedAt,value.WalkSeconds,value.Camping);
        }
        private static TravelerDiversion CopyTravelerDiversion(TravelerDiversion value)
        {
            if(value==null)return null;
            Require(value.OriginalRoute!=null && value.Route!=null && value.OriginalRoute.Count>=2 && value.Route.Count>=2 &&
                value.OriginalRoute.Count+value.Route.Count-1<=10000,"Missing or excessive traveler diversion routes.");
            var copy=new TravelerDiversion {PreferredTavernKey=value.PreferredTavernKey,Camping=value.Camping,DecidedAt=value.DecidedAt,
                WalkSeconds=value.WalkSeconds,OriginalCommuteSeconds=value.OriginalCommuteSeconds};
            foreach(var point in value.OriginalRoute)
            {Require(point!=null && DefenseCoordinate(point.X) && DefenseCoordinate(point.Z),"Invalid original traveler route.");copy.OriginalRoute.Add(new SavedRoutePoint {X=point.X,Z=point.Z});}
            foreach(var point in value.Route)
            {Require(point!=null && DefenseCoordinate(point.X) && DefenseCoordinate(point.Z),"Invalid traveler diversion route.");copy.Route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});}
            return copy;
        }
        private static void ValidateTravelerDiversion(TravelerVisit visit,TravelerPerson person)
        {
            var value=visit.Diversion;
            Require(value.PreferredTavernKey>=0 && DefenseTime(value.DecidedAt) && value.Route.Count>=2 && value.OriginalRoute.Count>=2 &&
                value.Route.Count+value.OriginalRoute.Count-1==visit.Route.Count &&
                (value.Camping?visit.Admission==TravelerAdmission.Refused && visit.TavernKey==value.PreferredTavernKey:
                visit.Admission==TravelerAdmission.Admitted && visit.TavernKey!=value.PreferredTavernKey),"Invalid traveler diversion.");
            int index=0;
            foreach(var point in value.OriginalRoute)
            {Require(visit.Route[index].X==point.X && visit.Route[index].Z==point.Z,"Changed original traveler approach.");index++;}
            var door=value.OriginalRoute[value.OriginalRoute.Count-1];
            Require(door.X==value.Route[0].X && door.Z==value.Route[0].Z,"Disconnected traveler diversion.");
            for(int i=1;i<value.Route.Count;i++,index++)
                Require(visit.Route[index].X==value.Route[i].X && visit.Route[index].Z==value.Route[i].Z,"Changed traveler diversion.");
            DivertedTravelerJourney(person,visit);
        }
    }
}
