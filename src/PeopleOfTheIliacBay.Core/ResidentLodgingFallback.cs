using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    [Serializable] public sealed class ResidentLodgingDetour
    {
        public int BuildingKey;
        public bool Camping;
        public string BookingId;
        public double DecidedAt,WalkSeconds;
        public List<SavedRoutePoint> Route=new List<SavedRoutePoint>();
        public double ArriveAt {get{return DecidedAt+WalkSeconds;}}
        internal static ResidentLodgingDetour Copy(ResidentLodgingDetour value,ResidentServiceExit owner)
        {
            if(value==null)return null;
            if(!owner.SeekingLodging || (value.Camping?value.BuildingKey!=-1 || value.BookingId!=null:
                value.BuildingKey<0 || value.BuildingKey==owner.BuildingKey || string.IsNullOrWhiteSpace(value.BookingId)))
                throw new ArgumentException("Invalid resident lodging destination.");
            if(double.IsNaN(value.DecidedAt) || double.IsInfinity(value.DecidedAt) || value.DecidedAt+0.001<owner.WalkEndsAt ||
                double.IsNaN(value.WalkSeconds) || double.IsInfinity(value.WalkSeconds) || value.WalkSeconds<=0 ||
                value.DecidedAt+2*value.WalkSeconds>=owner.VisitLeaveAt || value.Route==null || value.Route.Count<2 || value.Route.Count>10000)
                throw new ArgumentException("Resident lodging detour exceeds original service window.");
            var copy=new ResidentLodgingDetour {BuildingKey=value.BuildingKey,Camping=value.Camping,BookingId=value.BookingId,
                DecidedAt=value.DecidedAt,WalkSeconds=value.WalkSeconds};double length=0;
            foreach(var point in value.Route)
            {
                if(point==null || double.IsNaN(point.X) || double.IsInfinity(point.X) || double.IsNaN(point.Z) || double.IsInfinity(point.Z))
                    throw new ArgumentException("Invalid resident lodging route.");
                if(copy.Route.Count>0){var previous=copy.Route[copy.Route.Count-1];double x=point.X-previous.X,z=point.Z-previous.Z;length+=Math.Sqrt(x*x+z*z);}
                copy.Route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
            }
            if(length<=0 || double.IsNaN(length) || double.IsInfinity(length) ||
                Math.Abs(copy.Route[0].X-owner.ExteriorDoor.X)>0.001 || Math.Abs(copy.Route[0].Z-owner.ExteriorDoor.Z)>0.001)
                throw new ArgumentException("Resident lodging route is disconnected from actual source door.");
            return copy;
        }
        internal RoutePoint Position(double progress)
        {
            double total=0;for(int i=1;i<Route.Count;i++)total+=Distance(Route[i-1],Route[i]);
            double remaining=Math.Max(0,Math.Min(1,progress))*total;
            for(int i=1;i<Route.Count;i++){var a=Route[i-1];var b=Route[i];double length=Distance(a,b);
                if(length>0 && remaining<=length){double f=remaining/length;return new RoutePoint(a.X+(b.X-a.X)*f,a.Z+(b.Z-a.Z)*f);}remaining-=length;}
            var end=Route[Route.Count-1];return new RoutePoint(end.X,end.Z);
        }
        private static double Distance(SavedRoutePoint a,SavedRoutePoint b){double x=b.X-a.X,z=b.Z-a.Z;return Math.Sqrt(x*x+z*z);}
    }
    public sealed class ResidentLodgingRequest
    {public string PersonId,VisitId;public int BuildingKey;public SavedRoutePoint Door;}
    public sealed partial class ResidentTrip
    {
        internal string LodgingPlanContext(double now,Func<int,string> buildingName)
        {
            foreach(var exit in ServiceExits)
            {
                if(!exit.SeekingLodging || now<exit.StartedAt || now>=exit.VisitLeaveAt)continue;
                if(now<exit.WalkEndsAt)return "The recorded route leaves the refused Inn through its actual doorway; no replacement bed is confirmed.";
                var detour=exit.LodgingDetour;
                if(detour==null || now<detour.DecidedAt)return "The refused Inn supplied no bed; you are seeking another usable Inn or lawful shelter from its exterior doorway.";
                if(now<detour.ArriveAt)return detour.Camping?"Your measured route leads to an outside camp; arrival is not yet confirmed.":
                    "Your measured route leads to another Inn with a booked bed; arrival is not yet confirmed.";
                if(now>=exit.VisitLeaveAt-detour.WalkSeconds)return "Your measured return route rejoins the original Inn doorway before resuming the existing onward itinerary.";
                if(detour.Camping)return "Your lodging plan uses an outside camp until the measured return route resumes; no Inn bed is booked.";
                string name=buildingName==null?null:buildingName(detour.BuildingKey);
                return "Your replacement booked bed is at "+(string.IsNullOrWhiteSpace(name)?"another actual Inn":name)+" until the existing onward itinerary resumes.";
            }
            return null;
        }
        internal bool TryLodgingState(double now,out ResidentTripSnapshot result,out RoutePoint? target)
        {
            result=default(ResidentTripSnapshot);target=null;
            foreach(var exit in ServiceExits)
            {
                if(!exit.SeekingLodging || now<exit.WalkEndsAt || now>=exit.VisitLeaveAt)continue;
                var detour=exit.LodgingDetour;bool atShelter=false;var point=new RoutePoint(exit.ExteriorDoor.X,exit.ExteriorDoor.Z);
                if(detour!=null && now>=detour.DecidedAt)
                {
                    double returnAt=exit.VisitLeaveAt-detour.WalkSeconds;
                    if(now<detour.ArriveAt){point=detour.Position((now-detour.DecidedAt)/detour.WalkSeconds);target=detour.Position(1);}
                    else if(now<returnAt){point=detour.Position(1);atShelter=true;}
                    else{point=detour.Position(1-(now-returnAt)/detour.WalkSeconds);target=detour.Position(0);}
                }
                bool indoors=atShelter && !detour.Camping;
                result=new ResidentTripSnapshot {VisitId=exit.VisitId,Phase=ResidentTripPhase.Visiting,SettlementId=exit.SettlementId.Value,
                    BuildingKey=indoors?detour.BuildingKey:-1,VisitPurpose=ResidentVisitPurpose.Inn,Position=point,
                    VisitPhase=indoors?ResidentVisitPhase.Inside:target.HasValue?ResidentVisitPhase.Walking:ResidentVisitPhase.WaitingToLeave,
                    VisitEnterAt=detour==null?exit.VisitEnterAt:detour.ArriveAt,VisitLeaveAt=detour==null?exit.VisitLeaveAt:exit.VisitLeaveAt-detour.WalkSeconds};
                return true;
            }
            return false;
        }
    }
    public sealed partial class WorldStateStore
    {
        public bool HasResidentLodgingSourceExit(string id,int building,double now)
        {
            var exit=ResidentLodgingExit(id,now);
            return exit!=null && exit.BuildingKey==building;
        }
        public List<ResidentLodgingRequest> PendingResidentLodging(int settlement,double now)
        {
            var result=new List<ResidentLodgingRequest>();
            foreach(var trip in state.ResidentTrips)foreach(var exit in trip.ServiceExits)
                if(exit.SeekingLodging && exit.LodgingDetour==null && exit.SettlementId==settlement && now>=exit.WalkEndsAt && now<exit.VisitLeaveAt &&
                    TripResident(state,trip.HomeSettlement,trip.ResidentId).IsAlive)
                    result.Add(new ResidentLodgingRequest {PersonId=trip.ResidentId,VisitId=exit.VisitId,BuildingKey=exit.BuildingKey,
                        Door=new SavedRoutePoint {X=exit.ExteriorDoor.X,Z=exit.ExteriorDoor.Z}});
            return result;
        }
        public bool TryResolveResidentLodging(string personId,string visitId,int building,bool camping,
            IEnumerable<SavedRoutePoint> route,double walkSeconds,double now)
        {
            ResidentTrip trip;if(!residentTripIndex.TryGetValue(personId,out trip) || route==null || !DefenseTime(now))return false;
            var exit=trip.ServiceExits.Find(value=>value.VisitId==visitId && value.SeekingLodging && value.LodgingDetour==null);
            if(exit==null || now<exit.WalkEndsAt || now>=exit.VisitLeaveAt || !TripResident(state,trip.HomeSettlement,personId).IsAlive)return false;
            var inn=camping?null:state.Lodgings.Find(value=>value.SettlementId==exit.SettlementId && value.BuildingKey==building);
            var owned=inn==null?null:inn.Bookings.Find(value=>!value.Player && value.GuestId==personId && value.HomeSettlement==trip.HomeSettlement &&
                value.PartySize==1 && value.StartsAt<=now+walkSeconds && value.EndsAt>=exit.VisitLeaveAt-walkSeconds);
            if(!camping)foreach(var lodging in state.Lodgings)foreach(var claim in lodging.Bookings)
                if(claim!=owned && !claim.Player && claim.GuestId==personId && claim.StartsAt<exit.VisitLeaveAt-walkSeconds && now+walkSeconds<claim.EndsAt)return false;
            string booking=camping?null:owned==null?"resident-shelter:"+personId+":"+visitId:owned.Id;
            var proposed=new ResidentLodgingDetour {BuildingKey=building,Camping=camping,BookingId=booking,DecidedAt=now,WalkSeconds=walkSeconds,
                Route=new List<SavedRoutePoint>(route)};
            ResidentTrip copy;
            try{copy=ResidentTrip.CopyValidated(trip);copy.ServiceExits.Find(value=>value.VisitId==visitId).LodgingDetour=ResidentLodgingDetour.Copy(proposed,exit);copy=ResidentTrip.CopyValidated(copy);}
            catch(ArgumentException){return false;}
            if(!camping && (!HasLodgingInventory(exit.SettlementId.Value,building) || InnUnderInvestigation(exit.SettlementId.Value,building,now) ||
                owned==null && !TryBookLodging(exit.SettlementId.Value,building,booking,personId,trip.HomeSettlement,false,1,now+walkSeconds,exit.VisitLeaveAt-walkSeconds)))return false;
            state.ResidentTrips[state.ResidentTrips.IndexOf(trip)]=copy;residentTripIndex[personId]=copy;return true;
        }
        private ResidentServiceExit ResidentLodgingExit(string id,double now)
        {
            ResidentTrip trip;return residentTripIndex.TryGetValue(id,out trip)?trip.ServiceExits.Find(value=>value.SeekingLodging && now>=value.WalkEndsAt && now<value.VisitLeaveAt):null;
        }
        private ResidentInnStay ResidentDetourStay(string id,double now)
        {
            var exit=ResidentLodgingExit(id,now);var detour=exit==null?null:exit.LodgingDetour;
            if(detour==null || detour.Camping || now<detour.ArriveAt || now>=exit.VisitLeaveAt-detour.WalkSeconds)return null;
            return new ResidentInnStay {VisitId=exit.VisitId,SettlementId=exit.SettlementId,BuildingKey=detour.BuildingKey,
                EnterAt=detour.ArriveAt,AttemptedAt=detour.ArriveAt,LeaveAt=exit.VisitLeaveAt-detour.WalkSeconds,
                Admission=TravelerAdmission.Admitted,BookingId=detour.BookingId};
        }
        private static void ValidateResidentLodgingDetours(WorldState world,ResidentTrip trip)
        {
            foreach(var exit in trip.ServiceExits)
            {
                if(!exit.SeekingLodging)continue;
                Require(trip.InnStays.Exists(stay=>stay.VisitId==exit.VisitId && stay.Admission==TravelerAdmission.Refused),"Lodging hold lacks actual refusal.");
                var source=trip.VisitById(exit.VisitId).Visit.Position;
                Require(Math.Abs(source.X-exit.ExteriorDoor.X)<0.001 && Math.Abs(source.Z-exit.ExteriorDoor.Z)<0.001,
                    "Lodging hold lacks its actual source door.");
                var detour=exit.LodgingDetour;if(detour==null || detour.Camping)continue;
                var inn=world.Lodgings.Find(value=>value.SettlementId==exit.SettlementId && value.BuildingKey==detour.BuildingKey);
                var bed=inn==null?null:inn.Bookings.Find(value=>value.Id==detour.BookingId);
                Require(bed!=null && !bed.Player && bed.GuestId==trip.ResidentId && bed.PartySize==1 && bed.HomeSettlement==trip.HomeSettlement &&
                    bed.StartsAt<=detour.ArriveAt+0.001 && bed.EndsAt+0.001>=exit.VisitLeaveAt-detour.WalkSeconds,
                    "Resident shelter lacks its actual bed booking.");
            }
        }
    }
}
