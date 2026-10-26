using System;
using System.Collections.Generic;
using System.IO;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int assertions;
    private static SavedRoutePoint P(double x) { return new SavedRoutePoint { X=x,Z=0 }; }
    private static void Check(bool value,string message) { assertions++;if(!value)throw new Exception(message); }
    private static WorldStateStore DiskRoundTrip(WorldStateStore store)
    {
        var serializer=new FullSerializer.fsSerializer();FullSerializer.fsData encoded;
        Check(serializer.TrySerialize(store.Snapshot(),out encoded).Succeeded,"Native serializer rejected pursuit save.");
        string path=Path.Combine(AppContext.BaseDirectory,"witnessed-door-save.json");
        File.WriteAllText(path,FullSerializer.fsJsonPrinter.CompressedJson(encoded));
        WorldState state=null;
        try {Check(serializer.TryDeserialize(FullSerializer.fsJsonParser.Parse(File.ReadAllText(path)),ref state).Succeeded,"Native JSON restore failed.");}
        finally {File.Delete(path);}
        var loaded=new WorldStateStore(0);loaded.Restore(state);return loaded;
    }
    private static WorldStateStore MurderCase(out GuardRecord guard)
    {
        var store=new WorldStateStore(13);var town=store.GetOrCreate(1,new[]{7,8},1);
        var victim=town.Households[0].Residents[0];var witness=town.Households[1].Residents[0];
        var alarm=store.RecordViolence(1,victim.Id,100);store.LocateViolence(alarm.Id,-1,0,0,0);
        Population.MarkDead(town,victim.Id);store.ConfirmCorpse(alarm.Id,100,-1,0,0,0);
        alarm.KillerThreatId="resident";store.Witness(alarm.Id,witness.Id,110);
        var roster=store.GetOrCreateGuards(1,1,new[]{P(0)});guard=roster.Guards[0];
        roster.DispatchOutdoorInvestigation("outdoor:"+alarm.Id,200,
            new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{P(0),P(0)}}},
            new List<SavedRoutePoint>{P(0),P(20),P(0)},1,1,1);
        roster.Advance(200);Check(store.ClearOutdoorCorpse(alarm.Id,guard.Id,0,0,200),"Cleanup fixture failed.");
        bool statement;store.TryQuestionCorpseCase(alarm.Id,guard.Id,witness.Id,false,2,true,200,out statement);
        Check(store.TryLearnCorpseSuspect(alarm.Id,guard.Id,witness.Id,200),"Witness fixture failed.");
        roster.Engage(guard.Id,201);
        roster.ObserveEngagement(guard.Id,P(0),1,201,true,P(5));
        Check(roster.PursueWitnessedDoor(guard.Id,"resident",7,"actual",201,true,
            new List<SavedRoutePoint>{P(0),P(5)},new List<SavedRoutePoint>{P(5),P(0)},1,1),"Corpse investigator could not pursue entry.");
        return store;
    }
    private static void Main()
    {
        var roster=GuardRoster.Create(1,12,2,new[] { P(0),P(2) });
        string witness=roster.Guards[0].Id,other=roster.Guards[1].Id;
        roster.DispatchDefense("assault","resident",10,new Dictionary<string,List<SavedRoutePoint>> {
            {witness,new List<SavedRoutePoint>{P(0),P(10)}},
            {other,new List<SavedRoutePoint>{P(2),P(10)}}},2,1,1);
        roster.Engage(witness,11);
        var path=new List<SavedRoutePoint>{P(1),P(5)};var home=new List<SavedRoutePoint>{P(5),P(0)};
        Check(!roster.PursueWitnessedDoor(witness,"resident",7,"door:1:7:2:3:4",11,false,path,home,1,1),"Unseen entry admitted.");
        Check(!roster.PursueWitnessedDoor(witness,"stranger",7,"door:1:7:2:3:4",11,true,path,home,1,1),"Unrelated identity admitted.");
        Check(roster.PursueWitnessedDoor(witness,"resident",7,"door:1:7:2:3:4",11,true,path,home,1,1),"Witnessed entry rejected.");
        Check(roster.Guards[1].DoorPursuit==null && roster.Reports[0].BuildingKey==null,"Door knowledge leaked to shared report.");
        Check(roster.Position(witness,11).X==1 && roster.Position(witness,13).X==3,"Guard teleported to doorway.");
        Check(!roster.EnterWitnessedDoor(witness,7,"door:1:7:2:3:4",13,new GuardPoint3()),"Entered before arrival.");
        roster=GuardRoster.CopyValidated(roster);
        Check(!roster.EnterWitnessedDoor(witness,8,"door:1:7:2:3:4",15,new GuardPoint3()),"Wrong building admitted.");
        Check(!roster.EnterWitnessedDoor(witness,7,"door:1:7:2:3:5",15,new GuardPoint3()),"Different door in same building admitted.");
        bool directRejected=false;try {roster.EnterBuilding(witness,7,15,new GuardPoint3());}catch(ArgumentException){directRejected=true;}
        Check(directRejected,"Legacy building entry bypassed exact witnessed door.");
        Check(roster.EnterWitnessedDoor(witness,7,"door:1:7:2:3:4",15,new GuardPoint3 {X=20,Y=1,Z=30}),"Exact doorway did not enter.");
        var guard=roster.Guards[0];
        Check(guard.Interior.Position.X==20 && guard.Interior.LastSeenSuspect==null,"Entry disclosed unknown room position.");
        Check(guard.SearchUntil==1811,"Lost track must retain a bounded 30-minute search.");
        roster=GuardRoster.CopyValidated(roster);guard=roster.Guards[0];
        roster.Advance(1810);Check(!guard.Interior.Returning,"Search expired early.");
        roster.Advance(1811);Check(guard.Interior==null && guard.Duty==GuardDuty.Returning,"Search failed to return after expiry.");
        roster.Advance(1816);Check(guard.Duty==GuardDuty.Posted && guard.DoorPursuit==null,"Completed pursuit retained entry authority.");
        Check(roster.Reports[0].ThreatId=="resident" && roster.Reports[0].GuardIds.Count==2,"Expiry erased identity or recruited extra guards.");
        GuardRoster.CopyValidated(roster);
        var store=MurderCase(out guard);roster=store.GetGuards(1);string id=guard.Id;
        store=DiskRoundTrip(store);roster=store.GetGuards(1);guard=roster.Guards[0];
        Check(roster.EnterWitnessedDoor(id,7,"actual",206,new GuardPoint3{Y=1}),"Saved murderer door did not enter.");
        roster.ObserveInterior(id,new GuardPoint3{X=2,Y=1},1,300,true,new GuardPoint3{X=4,Y=1});
        Check(guard.SearchUntil==2100,"Actual later sight must restart 30-minute lost-track window.");
        store.Restore(store.Snapshot());roster=store.GetGuards(1);guard=roster.Guards[0];
        roster.Advance(2099);Check(guard.Interior!=null && !guard.Interior.Returning,"Later sight deadline expired early.");
        roster.Advance(2100);Check(guard.Interior.Returning,"Interior lost-track did not return.");
        store.Restore(store.Snapshot());roster=store.GetGuards(1);roster.Advance(2200);
        Check(store.GuardKnowsHostileMurder(1,id,"resident"),"Search expiry erased lasting murder identity.");
        store=MurderCase(out guard);roster=store.GetGuards(1);roster.Kill(guard.Id,203);store.Restore(store.Snapshot());
        Check(!store.GetGuards(1).Guards[0].IsAlive,"Death restored a pursuing guard.");
        store=MurderCase(out guard);roster=store.GetGuards(1);
        Check(roster.EnterWitnessedDoor(guard.Id,7,"actual",206,new GuardPoint3{Y=1}),"Death fixture entry failed.");
        roster.Kill(guard.Id,207);store.Restore(store.Snapshot());
        Check(!store.GetGuards(1).Guards[0].IsAlive && store.GetGuards(1).Guards[0].Interior!=null,
            "Indoor death lost its actual room or revived a pursuing guard.");
        store=MurderCase(out guard);roster=store.GetGuards(1);roster.Advance(206);
        roster.RedirectMurdererCorpseCleanup(guard.Id,"resident","cleanup:new","corpse:new",206,
            new List<SavedRoutePoint>{P(5),P(8)},new List<SavedRoutePoint>{P(8),P(0)},1,1);
        Check(guard.DoorPursuit==null,"Replacement cleanup incident retained doorway authority.");
        GuardRoster.CopyValidated(roster);
        store=MurderCase(out guard);roster=store.GetGuards(1);guard.DoorPursuit.ThreatId="stranger";
        bool forgedRejected=false;try{GuardRoster.CopyValidated(roster);}catch(ArgumentException){forgedRejected=true;}
        Check(forgedRejected,"Saved door evidence changed its assigned target identity.");
        store=MurderCase(out guard);roster=store.GetGuards(1);
        roster.ReassignDoorAssault(guard.Id,"player-assault",203);
        Check(guard.DoorPursuit==null && roster.Reports[0].CorpseSuspect.Identity=="resident" &&
            guard.IncidentId=="player-assault" && roster.Reports[1].ThreatId==null,"Player assault rewrote shared suspect knowledge.");
        GuardRoster.CopyValidated(roster);
        store=MurderCase(out guard);roster=store.GetGuards(1);roster.EnterWitnessedDoor(guard.Id,7,"actual",206,new GuardPoint3{Y=1});
        roster.ReassignDoorAssault(guard.Id,"player-assault-inside",207);GuardRoster.CopyValidated(roster);
        Check(guard.DoorPursuit==null && guard.Interior.BuildingKey==7 && guard.Interior.LastSeenSuspect==null,
            "Interior assault retained the old target position.");
        store=new WorldStateStore(31);var resident=store.GetOrCreate(1,new[]{7},1).Households[0].Residents[0];
        var reaction=new AlarmPerson {ResidentId=resident.Id,Reaction=CivilianReaction.Flee,HasPosition=true,
            ReachedRefuge=true,BuildingKey=7,X=40,Y=3,Z=50};
        store.RecordDefenseShelter(resident.Id,1,"actual-door",reaction,500);
        store=DiskRoundTrip(store);var shelter=store.ResidentDefenseShelter(resident.Id,1);
        Check(shelter!=null && !shelter.LandingObserved && shelter.Reaction.X==40,"Reload lost private resident refuge.");
        Check(shelter.ActivityContext(7,"the bank",false)==null,"Unloaded refuge claimed an observed indoor activity.");
        Check(store.Snapshot().AlarmIncidents.Count==0 && store.Snapshot().Garrisons.Count==0,
            "Private refuge invented an alarm or guard knowledge.");
        Check(!store.ObserveDefenseShelterLanding(resident.Id,1,7,"other-door",1,2,3,501),"Other door disclosed landing.");
        Check(store.ObserveDefenseShelterLanding(resident.Id,1,7,"actual-door",1,2,3,501),"Actual loaded landing rejected.");
        shelter.Reaction.X=4;store.Restore(store.Snapshot());shelter=store.ResidentDefenseShelter(resident.Id,1);
        Check(shelter.LandingObserved && !shelter.Reaction.ReachedRefuge && shelter.Reaction.X==4,
            "Reload lost actual indoor movement or reinterpreted exterior coordinates.");
        Check(shelter.ActivityContext(8,"another shop",true)==null,"Context claimed another room as current refuge.");
        Check(shelter.ActivityContext(7,"the bank",false)==
            "I took shelter in the bank after fleeing a guard. My ordinary routine is interrupted. I do not currently see a pursuing guard.",
            "Unseen guard gained a known position or pursuit state in refuge context.");
        store.ReleaseDefenseShelter(resident.Id,2299);Check(store.ResidentDefenseShelter(resident.Id,1)!=null,"Early release accepted.");
        store.ReleaseDefenseShelter(resident.Id,2300);Check(store.ResidentDefenseShelter(resident.Id,1)==null,"Expired private refuge did not release.");
        Console.WriteLine("Guard doorway boundary, route, restore, refuge and context checks passed: "+assertions+" assertions.");
    }
}
