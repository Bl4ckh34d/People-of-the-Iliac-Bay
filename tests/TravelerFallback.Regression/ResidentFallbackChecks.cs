using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class ResidentFallbackChecks
{
    private static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
    private static List<SavedRoutePoint> Path(double from,double to)
    {return new List<SavedRoutePoint>{new SavedRoutePoint {X=from},new SavedRoutePoint {X=to}};}
    internal static void Run()
    {
        foreach(bool road in new[]{false,true})foreach(bool camping in new[]{false,true})
        {
            var store=new WorldStateStore(12);var person=store.GetOrCreate(100,new[]{1},1).Households[0].Residents[0];
            int town=road?300:200;store.GetOrCreate(town,new int[0]);
            store.RegisterLodgingBeds(town,7,new[]{new LodgingBed {Id="bed"}});store.RegisterLodgingBeds(town,8,new[]{new LodgingBed {Id="bed"}});
            var trip=ResidentTrip.Create(person.Id,100,200,100,100,10,new[]{new RoutePoint(0,0),new RoutePoint(1,0),new RoutePoint(2,0)});
            var inn=new ResidentDestination {ExitWalkSeconds=10,ExitRoute=Path(10,0),Stops=new List<ResidentVisitStop>{
                new ResidentVisitStop {BuildingKey=7,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=10,DwellSeconds=1000,Route=Path(0,10)}}};
            if(road)trip.OutboundStops.Add(new ResidentRoadStop {RoadNode=1,SettlementId=town,Visit=inn});
            else
            {
                inn.Stops.Add(new ResidentVisitStop {BuildingKey=9,Purpose=ResidentVisitPurpose.Shop,WalkSeconds=10,DwellSeconds=50,Route=Path(10,20)});
                inn.ExitRoute=Path(20,0);inn.ExitWalkSeconds=20;trip.Destination=inn;
            }
            trip=ResidentTrip.CopyValidated(trip);double entered=(road?trip.RoadLeavesAt+10:trip.ArrivesAt)+10;
            double homeAt=trip.HomeAt;store.ScheduleResidentTrip(trip);
            var scheduled=store.ResidentTripAt(person.Id,entered);double leave=scheduled.VisitLeaveAt;
            store.TryBookLodging(town,7,"full","player",town,true,1,entered,leave);
            Need(!store.TryAdmitResidentInn(person.Id,entered+1),"Preferred resident Inn is full");
            store.SaveResidentServiceExit(person.Id,new ResidentServiceExit {VisitId=scheduled.VisitId,SettlementId=town,BuildingKey=7,
                VisitEnterAt=entered,VisitLeaveAt=leave,StartedAt=entered+1,SecondsPerMetre=1,
                Route=new List<GuardPoint3>{new GuardPoint3 {X=2},new GuardPoint3()}});
            double now=entered+4;
            Need(store.ResidentTripAt(person.Id,now).BuildingKey==-1,"After actual counter exit resident seeks shelter outside");
            Need(Math.Abs(store.GetResidentDialogueTrip(person.Id).HomeAt-homeAt)<0.001,"Refusal preserves original future timeline");
            var route=camping?new List<SavedRoutePoint>{new SavedRoutePoint {X=10},new SavedRoutePoint(),new SavedRoutePoint {X=-20}}:Path(10,30);
            double walk=camping?30:20;
            Need(!store.TryResolveResidentLodging(person.Id,scheduled.VisitId,99,false,Path(10,30),20,now),"Unknown resident Inn cannot invent capacity");
            Need(!store.TryResolveResidentLodging(person.Id,scheduled.VisitId,8,false,Path(11,30),20,now),"Resident detour must start at its exact actual source door");
            Need(!store.TryResolveResidentLodging(person.Id,scheduled.VisitId,8,false,Path(10,30),1000,now),"Detour cannot erase next scheduled service");
            if(road && !camping)Need(store.TryBookLodging(town,8,"existing-owned-bed",person.Id,100,false,1,now-1,leave),"Existing real bed fixture");
            Need(store.TryResolveResidentLodging(person.Id,scheduled.VisitId,camping?-1:8,camping,route,walk,now),"Measured resident fallback accepted");
            Need(store.Snapshot().Travelers.Count==0,"Resident fallback must not register a second TravelerPerson owner");
            Need(store.ResidentInnBed(person.Id,now+walk-1)==null,"No resident fallback bed before actual arrival");
            var arrival=store.ResidentTripAt(person.Id,now+walk+1);
            Need(arrival.BuildingKey==(camping?-1:8),"Resident fallback uses actual chosen shelter");
            string context=ConversationLifeContext.TripStatus(store.GetResidentDialogueTrip(person.Id),now+walk+1,arrival,key=>"Actual Town",key=>"Actual Inn");
            Need(context!=null && context.Contains(camping?"outside camp":"booked bed"),"Resident shelter context names actual rest plan without claiming original Inn");
            Need(camping?store.ResidentInnBed(person.Id,now+walk+1)==null:store.ResidentInnBed(person.Id,now+walk+1)=="bed","Only actual alternate Inn supplies one real bed");
            if(!camping)Need(store.Snapshot().Lodgings.Find(value=>value.SettlementId==town && value.BuildingKey==8).Bookings.Count==1,"Fallback must not duplicate an already owned bed");
            var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
            Need(restored.ResidentTripAt(person.Id,now+walk+1).BuildingKey==arrival.BuildingKey,"Resident shelter survives current save");
            Need(restored.ResolveResident(100,person.Id).HomeKey==person.HomeKey,"Fallback retains actual origin identity/home");
            Need(Math.Abs(restored.GetResidentDialogueTrip(person.Id).HomeAt-homeAt)<0.001,"Fallback leaves original home arrival unchanged");
            var returning=restored.ResidentTripAt(person.Id,leave-1);
            Need(returning.BuildingKey==-1 && Math.Abs(returning.Position.X-10)<=1.1,"Detour physically rejoins original source door before next leg");
            Need(!store.TryResolveResidentLodging(person.Id,scheduled.VisitId,8,false,Path(10,30),20,now+1),"No double resident fallback/bed claim");
            if(!road)Need(restored.ResidentTripAt(person.Id,leave+15).BuildingKey==9,"Independent following service retains its original time");
            var invalid=store.Snapshot();invalid.ResidentTrips[0].ServiceExits[0].ExteriorDoor.X+=1;
            bool rejected=false;try{restored.Restore(invalid);}catch(ArgumentException){rejected=true;}
            Need(rejected,"Current save rejects forged source doorway");
        }
        Console.WriteLine("PASS resident destination/road alternate Inn and outside camp preserve timeline, identity, bed and current save");
    }
}
