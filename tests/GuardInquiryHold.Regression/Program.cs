using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int checks;
    private static void Check(bool value,string label)
    {checks++;if(!value)throw new InvalidOperationException(label);Console.WriteLine("PASS "+label);}
    private static void Near(double expected,double actual,string label)
    {Check(Math.Abs(expected-actual)<0.00001,label+" expected="+expected+" actual="+actual);}
    private static void Main()
    {
        // If the pause advances or resets the patrol, the held and resumed positions differ.
        var method=typeof(GuardRoster).GetMethod("PauseSearchForInquiry",BindingFlags.Public|BindingFlags.Instance);
        Check(method!=null,"searching investigators support a physical conversation hold");
        bool Pause(GuardRoster roster,string id,double at,double seconds)=>(bool)method.Invoke(roster,new object[]{id,at,seconds});
        var contactRoster=GuardRoster.Create(1,13,1,new[]{new SavedRoutePoint()});var contact=contactRoster.Guards[0];
        contactRoster.Dispatch("spoken-threat",100,new Dictionary<string,List<SavedRoutePoint>>{
            {contact.Id,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint()}}},1,2,12);
        Check(contactRoster.PauseResponseForReport(contact.Id,100,36),"a report at the incident position still receives three real seconds of conversation");
        Near(0,contactRoster.Position(contact.Id,120).X,"zero-distance receiver stays physically at contact");
        Check(contact.Duty==GuardDuty.Responding,"zero-distance receiver cannot start searching before conversation ends");
        var store=new WorldStateStore(13);var town=store.GetOrCreate(1,new[]{7,8},1);
        var victim=town.Households[0].Residents[0];var alarm=store.RecordViolence(1,victim.Id,100);
        store.LocateViolence(alarm.Id,-1,20,0,0);Population.MarkDead(town,victim.Id);
        store.ConfirmCorpse(alarm.Id,100,-1,20,0,0);
        var roster=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()});var guard=roster.Guards[0];
        var target=new SavedRoutePoint{X=20};
        roster.DispatchOutdoorInvestigation("outdoor:"+alarm.Id,200,
            new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,new List<SavedRoutePoint>{guard.Post,target}}},
            new List<SavedRoutePoint>{target,new SavedRoutePoint{X=30},target},1,2,12);
        roster.Advance(330);Check(store.ClearOutdoorCorpse(alarm.Id,guard.Id,20,0,330),"real arrival admits bounded inquiry");
        double arrival=guard.ArrivesAt,deadline=guard.SearchUntil;var before=roster.Position(guard.Id,336);
        Check(Pause(roster,guard.Id,336,36),"three real seconds at native twelve game seconds per real second admitted");
        Check(!Pause(roster,guard.Id,336,36),"repeated callback cannot extend current hold");
        Near(before.X,roster.Position(guard.Id,350).X,"guard stays at actual patrol point during hold");
        Near(before.X,roster.Position(guard.Id,372).X,"guard resumes from held point without route restart");
        Near(arrival,guard.ArrivesAt,"actual arrival evidence remains unchanged");
        Near(deadline,guard.SearchUntil,"inquiry cannot extend fixed deadline");
        store.Restore(store.Snapshot());roster=store.GetGuards(1);guard=roster.Guards[0];
        Near(before.X,roster.Position(guard.Id,350).X,"current-save restore retains physical hold position");
        Near(before.X,roster.Position(guard.Id,372).X,"current-save restore retains resume point");
        Check(Math.Abs(before.X-roster.Position(guard.Id,378).X)>0.1,"patrol continues after hold");
        Check(Pause(roster,guard.Id,378,36),"later distinct inquiry can hold current route phase");
        var cancelled=roster.Position(guard.Id,390);roster.EndSearchInquiryPause(guard.Id,390);
        Near(cancelled.X,roster.Position(guard.Id,390).X,"interrupted hold keeps actual point");
        Check(Math.Abs(cancelled.X-roster.Position(guard.Id,396).X)>0.1,"interrupted hold resumes movement without waiting unused seconds");
        store.Restore(store.Snapshot());roster=store.GetGuards(1);guard=roster.Guards[0];
        var forged=store.Snapshot();forged.Garrisons[0].Guards[0].SearchDelaySeconds=1801;
        bool rejected=false;try{new WorldStateStore(13).Restore(forged);}catch(ArgumentException){rejected=true;}
        Check(rejected,"forged unbounded search delay is rejected");
        Check(!Pause(roster,guard.Id,deadline-12,36),"hold cannot outlive inquiry deadline");
        var atEnd=roster.Position(guard.Id,deadline-0.000001);var afterEnd=roster.Position(guard.Id,deadline);
        Near(atEnd.X,afterEnd.X,"deadline return starts at physically reached point");
        Check(guard.Duty==GuardDuty.Returning,"fixed deadline still ends search");
        Check(!Pause(roster,guard.Id,deadline,36),"returning guard cannot start inquiry hold");
        GuardRoster.CopyValidated(roster);
        Console.WriteLine(checks+" checks passed");
    }
}
