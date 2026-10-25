using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class ClosedShopVisit
    {
        public string PersonId;
        public int SettlementId,Building,Inn=-1;
        public double StartedAt,Opening,ReturnAt,EndsAt,WalkSeconds;
        public ClosedShopPastime Pastime;
        public List<SavedRoutePoint> InnRoute=new List<SavedRoutePoint>();
        public ResidentDailyOuting Reordered;
        public static ClosedShopVisit Copy(ClosedShopVisit source)
        {
            if(source==null || string.IsNullOrWhiteSpace(source.PersonId) || source.PersonId.Length>240 ||
                source.SettlementId<0 || source.Building<0 || source.Inn< -1 || !Enum.IsDefined(typeof(ClosedShopPastime),source.Pastime) ||
                !Finite(source.StartedAt)||!Finite(source.Opening)||!Finite(source.ReturnAt)||!Finite(source.EndsAt)||!Finite(source.WalkSeconds) ||
                source.StartedAt<0 || source.StartedAt>=source.Opening || source.Opening>source.ReturnAt || source.ReturnAt>source.EndsAt ||
                source.ReturnAt-source.Opening>2700 || source.EndsAt-source.StartedAt>86400 || source.WalkSeconds<0 ||
                source.InnRoute==null || source.InnRoute.Count>1024 ||
                source.Inn>=0 && (source.Pastime!=ClosedShopPastime.Inn || source.InnRoute.Count<2 || source.WalkSeconds<=0))
                throw new ArgumentException("Invalid closed-shop diversion.");
            var copy=new ClosedShopVisit {PersonId=source.PersonId,SettlementId=source.SettlementId,Building=source.Building,
                Inn=source.Inn,StartedAt=source.StartedAt,Opening=source.Opening,ReturnAt=source.ReturnAt,EndsAt=source.EndsAt,
                WalkSeconds=source.WalkSeconds,Pastime=source.Pastime};
            foreach(var point in source.InnRoute)
            {
                if(point==null || !Finite(point.X)||!Finite(point.Z))throw new ArgumentException("Invalid diversion route.");
                copy.InnRoute.Add(new SavedRoutePoint{X=point.X,Z=point.Z});
            }
            if(source.Reordered!=null)
            {
                var data=ResidentVisitJourney.Copy(source.Reordered.Itinerary);
                double departure=source.Reordered.DepartureSeconds;
                if(source.Inn>=0 || !Finite(departure) || departure>source.StartedAt ||
                    Math.Floor(departure/86400)!=Math.Floor(source.StartedAt/86400) || data.Stops.Count<2 ||
                    data.Stops[1].BuildingKey!=source.Building || data.Stops[0].BuildingKey==source.Building)
                    throw new ArgumentException("Invalid advanced planned visit.");
                var journey=ResidentVisitJourney.Create(departure,data);
                if(journey.EndsAt>source.EndsAt || journey.Evaluate(source.ReturnAt).BuildingKey!=source.Building)
                    throw new ArgumentException("Advanced visit misses the original shop or outing boundary.");
                copy.Reordered=new ResidentDailyOuting {DepartureSeconds=departure,Itinerary=data};
            }
            return copy;
        }
        private static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
    }
    public sealed partial class WorldStateStore
    {
        private WorldState closedShopVisitOwner;
        private Dictionary<string,ClosedShopVisit> closedShopVisitIndex;
        public void SaveClosedShopVisit(ClosedShopVisit visit)
        {
            var copy=ClosedShopVisit.Copy(visit);
            state.ClosedShopVisits.RemoveAll(x=>x.PersonId==copy.PersonId || x.EndsAt<=copy.StartedAt && !ClosedShopHasPhysicalVisit(x));
            state.ClosedShopVisits.Add(copy);
            closedShopVisitOwner=null;
        }
        public ClosedShopVisit ReadClosedShopVisit(string person,int settlement,double now)
        {
            if(closedShopVisitOwner!=state)
            {
                closedShopVisitIndex=new Dictionary<string,ClosedShopVisit>(StringComparer.Ordinal);
                foreach(var entry in state.ClosedShopVisits)closedShopVisitIndex.Add(entry.PersonId,entry);
                closedShopVisitOwner=state;
            }
            ClosedShopVisit found;
            return person!=null && closedShopVisitIndex.TryGetValue(person,out found) && found.SettlementId==settlement &&
                now>=found.StartedAt && now<found.EndsAt?ClosedShopVisit.Copy(found):null;
        }
        private bool ClosedShopHasPhysicalVisit(ClosedShopVisit visit)
        {
            var observed=IndexedObservedVisit(visit.PersonId);
            return observed!=null && ClosedShopVisitOwnsObservation(visit,observed);
        }
        public void ForgetClosedShopVisit(string person)
        {
            // Schedule expiry does not end a physically observed Inn visit.
            // Keep its provenance until the checkpoint is released, including
            // when a player exits the building or skips time after the deadline.
            state.ClosedShopVisits.RemoveAll(x=>x.PersonId==person && !ClosedShopHasPhysicalVisit(x));
            closedShopVisitOwner=null;
        }
        private static List<ClosedShopVisit> CopyClosedShopVisits(WorldState source)
        {
            var result=new List<ClosedShopVisit>();var ids=new HashSet<string>(StringComparer.Ordinal);
            if(source.ClosedShopVisits!=null)foreach(var visit in source.ClosedShopVisits)
            {var copy=ClosedShopVisit.Copy(visit);Require(ids.Add(copy.PersonId),"Duplicate shop diversion.");result.Add(copy);}
            return result;
        }
    }
}
