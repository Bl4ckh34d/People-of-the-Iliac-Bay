using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private void SaveResidentRoadExit(string id,ResidentTrip original,ResidentServiceExit exit)
        {
            var scoped=original.ResolveScopedVisit(exit.VisitId,exit.SettlementId,exit.BuildingKey,exit.VisitEnterAt);
            exit.VisitId=scoped.Id;exit.SettlementId=scoped.Settlement;
            Require(TripResident(state,original.HomeSettlement,id).IsAlive,"Dead resident cannot leave an inn.");
            if(original.ServiceExits.Exists(e=>e.VisitId==exit.VisitId)) return;
            Require(!original.ServiceExits.Exists(e=>e.VisitEnterAt>exit.VisitEnterAt) &&
                !original.InnStays.Exists(s=>s.EnterAt>exit.VisitEnterAt),"Cannot retime completed future visits.");
            var copy=ResidentTrip.CopyValidated(original);
            var receptions=copy.InnReceptions;copy.InnReceptions=new List<ResidentInnReception>();
            copy.ServiceExits.Add(exit);copy=ResidentTrip.CopyValidated(copy);
            foreach(var reception in receptions)
            {
                if(reception.VisitEnterAt>exit.VisitEnterAt)
                {
                    var next=copy.VisitById(reception.VisitId);
                    double offset=reception.StartedAt-reception.VisitEnterAt;
                    reception.VisitEnterAt=next.Visit.EnterAt;reception.VisitLeaveAt=next.Visit.LeaveAt;
                    reception.StartedAt=reception.VisitEnterAt+offset;
                }
                copy.InnReceptions.Add(reception);
            }
            copy=ResidentTrip.CopyValidated(copy);ValidateResidentInnBookings(state,copy);
            state.ResidentTrips.RemoveAll(t=>t.ResidentId==id);state.ResidentTrips.Add(copy);residentTripIndex[id]=copy;
        }
    }
    public sealed partial class ResidentTrip
    {
        internal ScopedVisit VisitById(string id)
        {
            foreach(var visit in AllScheduledVisits()) if(visit.Id==id) return visit;
            throw new ArgumentException("Visit ID lacks matching itinerary entry.");
        }
    }
}
