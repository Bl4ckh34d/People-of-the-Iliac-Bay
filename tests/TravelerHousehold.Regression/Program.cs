using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static void Need(bool condition,string message) {if(!condition) throw new Exception(message);}
    private static IEnumerable<HomeProfile> Homes()
    {for(int i=0;i<100;i++) {Reads++;yield return new HomeProfile(10+i,4,180);}}
    private static int Reads;
    private static int Main()
    {
        try
        {
            RegionalReturnReservation();
            BoundAlarm();
            HomeDeath();
            ReturnedContext();
            var sparse=new WorldStateStore(12);
            sparse.ReserveResidentTravelProposals(100,4,Homes(),new[]{200},1000,1);
            Need(Reads==1,"One traveler request surveyed unrelated origin homes: "+Reads);
            Console.WriteLine("PASS bounded origin housing demand");
            var store=new WorldStateStore(42);store.GetOrCreate(1,new int[0]);
            var family=store.ReserveHousehold(9,new HomeProfile(10,4,180));
            RegionalIdentity.Initialize(family,1,(seed,sex)=>"Person"+seed,seed=>"Stone");
            var person=family.Residents[0];
            var proposal=TravelerDemand.Create(42,1,7,10,0,new[]{new TravelerOrigin {SettlementId=9,Race=1}},1,2,false,
                new[]{new RoutePoint(0,0),new RoutePoint(10,0)},1,(origin,seed,sex)=>"Placeholder Stone");
            var bind=typeof(TravelerDemand).GetMethod("BindOrigin");
            Need(bind!=null,"Ordinary Inn travelers cannot bind their real origin resident.");
            bind.Invoke(null,new object[]{store,proposal,person});
            Need(proposal.Person.Id==person.Id && proposal.Person.AppearanceSeed==person.AppearanceSeed && proposal.Person.Name==person.FullName,
                "Guest replaced stable origin identity.");
            store.RegisterTraveler(proposal.Person);store.RegisterTravelerVisit(proposal.Visit);
            var journey=store.TravelerJourneyFor(proposal.Visit.Id);
            Need(store.IsResidentAway(person.Id,journey.CheckInAt),"Guest also remains present at origin.");
            Need(store.IsResidentAwayDuring(person.Id,journey.EntryAt-1,journey.ExitAt+1),"Origin sleep interval missed guest absence.");
            bool rejected=false;
            try {store.ScheduleResidentTrip(ResidentTrip.Create(person.Id,9,2,journey.CheckInAt,600,1,
                new[]{new RoutePoint(10,10),new RoutePoint(11,10)}));}
            catch(ArgumentException) {rejected=true;}
            Need(rejected,"Same origin resident started resident trip while staying as Inn guest.");
            var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
            Need(restored.TravelerVisitsFor(1)[0].Purpose==proposal.Visit.Purpose,"Save/load lost journey purpose.");
            var kept=restored.ResolveResident(9,person.Id);
            Need(kept!=null && kept.FullName==person.FullName && kept.HomeKey==10,"Save/load lost origin identity.");
            Need(restored.Traveler(person.Id)!=null,"Save/load lost guest projection.");
            var home=restored.GetOrCreateFamilies(9,new[]{new HomeProfile(10,4,180),new HomeProfile(20,2,100)}).Households.Find(h=>h.BuildingKey==10);
            Need(home.Surname=="Stone" && home.Residents.Count==family.Residents.Count && home.BedCapacity>=home.Residents.Count,
                "Completing origin replaced reserved family or beds.");
            for(int index=0;index<home.Residents.Count;index++)
                Need(home.Residents[index].PartnerId==family.Residents[index].PartnerId && home.Residents[index].MotherId==family.Residents[index].MotherId &&
                    home.Residents[index].FatherId==family.Residents[index].FatherId,"Save/load changed household relations.");
            var beds=HomeBeds.Assign(home,new[]{0,1,2,3});var occupied=new HashSet<int>();
            foreach(var bed in beds) Need(occupied.Add(bed.BedRecordIndex),"Two family members share origin bed.");
            Need(beds.Count==home.Residents.Count,"Family member lacks origin bed.");
            Need(!restored.IsResidentAway(person.Id,journey.ExitAt),"Completed guest journey never returns home.");
            var forged=restored.Snapshot();forged.Travelers[0].HomeKey=20;
            bool rejectedOrigin=false;try {new WorldStateStore(1).Restore(forged);} catch(ArgumentException) {rejectedOrigin=true;}
            Need(rejectedOrigin,"A forged origin household passed current save validation.");
            var context=TravelerConversationContext.IdentityAndLodging(restored,person.Id,1,journey.CheckInAt,k=>"Origin",k=>"Inn");
            Need(context.Contains("Stone") && !context.Contains("No permanent house"),"Guest context denies real family.");
            Need(restored.KillTraveler(person.Id,journey.CheckInAt+1) && !kept.IsAlive,"Guest death did not reach origin identity.");
            var dead=new WorldStateStore(1);dead.Restore(restored.Snapshot());
            Need(!dead.ResolveResident(9,person.Id).IsAlive,"Save/load revived dead origin resident.");
            Console.WriteLine("PASS real guest origin, one bed each, family, absence, identity, context, death and current save/load");
            KnownReturn();
            return 0;
        }
        catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
    private static (WorldStateStore Store,TravelerProposal Proposal,Resident Person) BoundGuest(double nativeReturnSeconds=0)
    {
        var store=new WorldStateStore(42);store.GetOrCreate(1,new[]{30},1);
        var family=store.ReserveHousehold(9,new HomeProfile(10,4,180));
        RegionalIdentity.Initialize(family,1,(seed,sex)=>"Person"+seed,seed=>"Stone");
        var proposal=TravelerDemand.Create(42,1,7,10,0,new[]{new TravelerOrigin {SettlementId=9,Race=1}},1,2,false,
            new[]{new RoutePoint(0,0),new RoutePoint(10,0)},1,(origin,seed,sex)=>"Placeholder Stone");
        TravelerDemand.BindOrigin(store,proposal,family.Residents[0]);
        proposal.Visit.NativeReturnSeconds=nativeReturnSeconds;
        store.RegisterTraveler(proposal.Person);store.RegisterTravelerVisit(proposal.Visit);
        return (store,proposal,family.Residents[0]);
    }
    private static void RegionalReturnReservation()
    {
        var fixture=BoundGuest(600);var store=fixture.Store;var person=fixture.Person;
        var journey=store.TravelerJourneyFor(fixture.Proposal.Visit.Id);
        Need(store.IsResidentAway(person.Id,journey.ExitAt+300),"Original household became occupied while its ordinary guest is still returning regionally.");
        Need(store.IsResidentAwayDuring(person.Id,journey.ExitAt+1,journey.ExitAt+599),"A new obligation overlaps the actual regional homeward tail.");
        var local=store.LocalResidentState(person.Id,journey.ExitAt+300,new ActivitySnapshot(Activity.Home,person.HomeKey,new RoutePoint(0,0),0));
        Need(local.Activity==Activity.Visit && local.BuildingKey<0,"Regional return exposed an ordinary home routine or indexed a nonexistent ResidentTrip.");
        var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
        Need(restored.IsResidentAway(person.Id,journey.ExitAt+599) && !restored.IsResidentAway(person.Id,journey.ExitAt+600),
            "Native return duration or exact home arrival boundary was lost on restore.");
        bool rejected=false;
        try{restored.ScheduleResidentTrip(ResidentTrip.Create(person.Id,9,2,journey.ExitAt+100,60,1,new[]{new RoutePoint(10,10),new RoutePoint(11,10)}));}
        catch(ArgumentException){rejected=true;}
        Need(rejected,"Returning ordinary guest started another trip before reaching its original home.");
        Console.WriteLine("PASS native-duration regional return reserves original household, routine and obligations through actual home time and restore");
    }
    private static void BoundAlarm()
    {
        var fixture=BoundGuest();var store=fixture.Store;var guest=fixture.Person;
        Need(ReferenceEquals(guest,store.ResolveResident(1,guest.Id)),"Destination projection dropped real guest household identity.");
        double now=store.TravelerJourneyFor(fixture.Proposal.Visit.Id).CheckInAt;
        var local=store.GetOrCreate(1,new int[0]).Households[0].Residents[0];
        var alarm=store.RecordViolence(1,local.Id,now);store.LocateViolence(alarm.Id,-1,1,0,2);
        Need(store.Witness(alarm.Id,guest.Id,now)!=null,"Bound guest could not witness local violence.");
        var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
        Need(restored.ActiveAlarm(guest.Id,now+1)!=null,"Bound guest alarm lost witness on current restore.");
        var murder=restored.RecordViolence(1,guest.Id,now+2);restored.LocateViolence(murder.Id,-1,2,0,3);
        restored.KillTraveler(guest.Id,now+3);restored.ConfirmCorpse(murder.Id,now+3,-1,2,0,3);
        var dead=new WorldStateStore(1);dead.Restore(restored.Snapshot());
        Need(!dead.ResolveResident(1,guest.Id).IsAlive && !dead.ResolveResident(9,guest.Id).IsAlive,
            "Guest murder restored different origin and destination life records.");
        Console.WriteLine("PASS guest witness and murder share origin identity through current alarm save/load");
    }
    private static void HomeDeath()
    {
        var fixture=BoundGuest();var store=fixture.Store;var guest=fixture.Person;
        double now=store.TravelerJourneyFor(fixture.Proposal.Visit.Id).ExitAt+10;
        var alarm=store.RecordViolence(9,guest.Id,now);store.LocateViolence(alarm.Id,guest.HomeKey,0,0,0);
        Population.MarkDead(store.GetOrCreate(9,new int[0]),guest.Id);
        store.ConfirmCorpse(alarm.Id,now+1,guest.HomeKey,0,0,0);
        Need(!store.Traveler(guest.Id).IsAlive && store.Traveler(guest.Id).DiedAt==now+1,
            "Confirmed household death left live guest identity.");
        var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
        Need(!restored.Traveler(guest.Id).IsAlive && !restored.ResolveResident(9,guest.Id).IsAlive,
            "Returned household death did not survive current restore.");
        Console.WriteLine("PASS confirmed household death synchronizes existing guest identity");
    }
    private static void ReturnedContext()
    {
        var fixture=BoundGuest();var store=fixture.Store;var guest=fixture.Person;
        var journey=store.TravelerJourneyFor(fixture.Proposal.Visit.Id);
        Need(TravelerConversationContext.IdentityAndLodging(store,guest.Id,9,journey.ExitAt+1,k=>"Origin",k=>"Inn")=="",
            "Returned resident still receives guest identity context at home.");
        Need(TravelerConversationContext.PlannedActivity(store,guest.Id,9,journey.ExitAt+1,k=>"Inn")=="",
            "Returned resident guest plan displaced ordinary home routine.");
        var current=typeof(TravelerConversationContext).GetMethod("IsCurrentVisit");
        Need(current!=null,"Dialogue role has no current guest visit boundary.");
        Need((bool)current.Invoke(null,new object[]{store,guest.Id,1,journey.CheckInAt,false}),"Active guest lost traveler role.");
        Need(!(bool)current.Invoke(null,new object[]{store,guest.Id,1,journey.ExitAt+1,false}),"Ended schedule retained active guest role.");
        Need((bool)current.Invoke(null,new object[]{store,guest.Id,1,journey.ExitAt+1,true}),"Physically delayed departure lost guest role.");
        Need(!(bool)current.Invoke(null,new object[]{store,guest.Id,9,journey.ExitAt+1,true}),"Returned home body retained traveler role.");
        Console.WriteLine("PASS current guest role and returned home context distinguish physical departure delays");
    }
    private static void KnownReturn()
    {
        var trip=ResidentTrip.Create("resident",9,1,100,600,1,new[]{new RoutePoint(10,10),new RoutePoint(11,10)});
        trip.Destination=new ResidentDestination {ExitWalkSeconds=10,ExitRoute=new List<SavedRoutePoint>{P(10,0),P(0,0)},
            Stops=new List<ResidentVisitStop>{new ResidentVisitStop {BuildingKey=20,Purpose=ResidentVisitPurpose.Shop,WalkSeconds=10,DwellSeconds=10,
                Route=new List<SavedRoutePoint>{P(0,0),P(10,0)}}}};
        var first=ResidentTrip.CopyValidated(trip);
        trip.Destination.Stops.Add(new ResidentVisitStop {BuildingKey=30,Purpose=ResidentVisitPurpose.Inn,WalkSeconds=5,DwellSeconds=10,
            Route=new List<SavedRoutePoint>{P(10,0),P(10,5)}});
        trip.Destination.Stops.Add(new ResidentVisitStop {BuildingKey=40,Purpose=ResidentVisitPurpose.Shop,WalkSeconds=5,DwellSeconds=10,
            Route=new List<SavedRoutePoint>{P(10,5),P(15,5)}});
        var method=typeof(ResidentOvernightPlan).GetMethod("ReturnViaInn");
        Need(method!=null,"Overnight planning still requires a new future return search.");
        var result=(ResidentTrip)method.Invoke(null,new object[]{trip,first,
            new[]{new RoutePoint(10,0),new RoutePoint(10,5)},new[]{new RoutePoint(10,5),new RoutePoint(15,5)},1d});
        Need(result.Destination.ExitWalkSeconds==20,"Known return route lost walking time.");
        var route=result.Destination.ExitRoute;
        Need(route[0].X==15 && route[0].Z==5 && route[route.Count-1].X==0 && route[route.Count-1].Z==0,
            "Known return route disconnected service from regional road.");
        Need(result.Evaluate(result.HomeAt).Phase==ResidentTripPhase.Home,"Known return route fails to reach home.");
        Console.WriteLine("PASS overnight return reuses already validated outward paths");
    }
    private static SavedRoutePoint P(double x,double z) {return new SavedRoutePoint {X=x,Z=z};}
}
