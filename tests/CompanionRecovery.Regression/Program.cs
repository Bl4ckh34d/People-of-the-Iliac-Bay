using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var policy=typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcCompanionWaitPolicy");
        if(policy==null) { Console.Error.WriteLine("FAIL: missing relationship-based companion wait policy.");return 1; }
        var select=policy.GetMethod("Hours");
        Require(select!=null,"Wait bands must use actual trust, disposition and familiarity.");
        var paid=(int[])select.Invoke(null,new object[]{false,0,100,20});
        var friendly=(int[])select.Invoke(null,new object[]{true,60,50,3});
        var intimate=(int[])select.Invoke(null,new object[]{true,80,75,5});
        Equal(paid[0],3);Equal(paid[1],6);Equal(friendly[0],12);Equal(friendly[1],24);Equal(intimate[0],72);Equal(intimate[1],72);
        var stranger=(int[])select.Invoke(null,new object[]{true,100,100,1});Equal(stranger[0],3);
        FastTravelPrice();PaidGuestAvailability();RecoveryLifecycle();EnterpriseConsent();MeasuredRecovery();LongAbsence();OverlandRecovery();NativeCapability();
        Console.WriteLine("PASS: companion wait bands, measured recovery persistence/catch-up, physical arrival, native capability and enterprise permission boundaries.");return 0;
    }
    private static void FastTravelPrice()
    {
        int total,pieces;
        Require(NpcCompanionFastTravel.TryPrice(30,5,0,out total,out pieces) && total==30 && pieces==5,"Solo native travel price unchanged");
        Require(NpcCompanionFastTravel.TryPrice(30,5,1,out total,out pieces) && total==60 && pieces==10,"One companion doubles actual native total and coins requirement");
        Require(NpcCompanionFastTravel.TryPrice(30,5,2,out total,out pieces) && total==90 && pieces==15,"Two companions triple actual native total and coins requirement");
        Require(NpcCompanionFastTravel.TryPrice(0,0,2,out total,out pieces) && total==0 && pieces==0,"Native free journey remains free");
        Require(!NpcCompanionFastTravel.TryPrice(int.MaxValue,1,1,out total,out pieces),"Overflow cannot yield free or negative journey");
        Require(!NpcCompanionFastTravel.TryPrice(10,11,1,out total,out pieces) && !NpcCompanionFastTravel.TryPrice(10,1,-1,out total,out pieces),"Invalid native quote or party rejected");
        NpcActionJob job;var store=Fixture(out job);double finish=job.FinishAt,deadline=job.DeadlineAt;
        Require(NpcCompanionFastTravel.CanFollow(store,job,101),"Existing accepted follow may fast travel");
        Require(store.TrySetFastTravelFollowOwner(job.Id,job.Revision,101,true),"Existing follow transport owner marked without a new action");
        store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
        Require(job.FastTravelFollowOwner,"Exact existing follow transport ownership persists through current save");
        Require(!NpcCompanionFastTravel.CanFollow(store,job,finish),"Arrival after original expiry does not silently renew follow");
        Equal(store.ReadNpcAction(job.Id).FinishAt,finish);Equal(store.ReadNpcAction(job.Id).DeadlineAt,deadline);
        Console.WriteLine("PASS native solo fare scales once by actual party; free/coins/overflow and original consent deadline preserved");
    }
    private static void PaidGuestAvailability()
    {
        var check=typeof(WorldStateStore).GetMethod("IsResidentAwayDuring",new[]{typeof(string),typeof(double),typeof(double),typeof(string)});
        Require(check!=null,"Paid hire must distinguish its actual current guest stay from another journey.");
        var store=new WorldStateStore(1);store.GetOrCreate(100,new int[0]);
        store.RegisterTraveler(new TravelerPerson {Id="traveler:paid",Name="Real Guest",HomeSettlement=200});
        store.RegisterTravelerVisit(new TravelerVisit {Id="current",PersonId="traveler:paid",SettlementId=100,TavernKey=10,Nights=1,
            DesiredArrival=100,CommuteSeconds=10,Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=10}}});
        Require(store.IsResidentAwayDuring("traveler:paid",110,86510),"Actual guest still reserves its origin household.");
        Require(!(bool)check.Invoke(store,new object[]{"traveler:paid",110d,86510d,"current"}),"Current guest stay cannot block its own paid hire.");
        Require((bool)check.Invoke(store,new object[]{"traveler:paid",110d,86510d,"different"}),"Only the exact current visit may be excluded.");
        store.RegisterTravelerVisit(new TravelerVisit {Id="later",PersonId="traveler:paid",SettlementId=100,TavernKey=10,Nights=1,
            DesiredArrival=50000,CommuteSeconds=10,Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=10}}});
        Require((bool)check.Invoke(store,new object[]{"traveler:paid",110d,86510d,"current"}),"A different future visit must still block paid hire.");
    }
    private static NpcActionTarget Point(bool inside,double x=0) {return new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=100,WorldSpaceId=(inside?"dungeon:":"settlement:")+100,X=x};}
    private static WorldStateStore Fixture(out NpcActionJob job,NpcActionTarget origin=null)
    {
        var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);
        job=new NpcActionJob {Id="follow:1",ProposalId="turn:1",ResidentId=town.Households[0].Residents[0].Id,
            Kind=NpcActionKind.Follow,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
            Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=86400,FinishAt=80000,LastPosition=origin??Point(false,17)};
        string reason;Require(PeopleOfTheIliacBay.Tests.ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason),reason);
        job=store.ReadNpcAction(job.Id);return store;
    }
    private static void RecoveryLifecycle()
    {
        NpcActionJob job;var store=Fixture(out job);Equal(job.CompanionRecovery.RecruitmentOrigin.X,17d);
        Require(store.TryCaptureCompanionDungeonEntrance(job.Id,job.Revision,110,Point(true),Point(false)),"Capture crossed entrance");job=store.ReadNpcAction(job.Id);
        Require(store.SaveNpcActionPosition(job.Id,job.Revision,Point(true,7),false,111),"Save observed dungeon position");job=store.ReadNpcAction(job.Id);
        Require(store.TryBeginCompanionObstacleWait(job.Id,job.Revision,112),"Start obstacle wait");job=store.ReadNpcAction(job.Id);
        Equal(job.CompanionRecovery.WaitUntil,112d+10800);
        Require(!store.TryAdvanceCompanionRecovery(job.Id,job.Revision,113,NpcCompanionRecoveryPhase.ReturningToExit),"No early timeout");
        store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);Equal(job.CompanionRecovery.WaitUntil,10912d);
        Require(!store.TryBeginCompanionObstacleWait(job.Id,job.Revision,120),"Reload never restarts wait");
        Require(store.TryAdvanceCompanionRecovery(job.Id,job.Revision,10912,NpcCompanionRecoveryPhase.ReturningToExit),"Wait ends on game clock");job=store.ReadNpcAction(job.Id);
        Require(!store.TryAdvanceCompanionRecovery(job.Id,job.Revision,10913,NpcCompanionRecoveryPhase.WaitingOutside,Point(false)),"Cannot exit before physically reaching entrance");
        Require(store.SaveNpcActionPosition(job.Id,job.Revision,Point(true),false,10914),"Observe doorway arrival");job=store.ReadNpcAction(job.Id);
        Require(store.TryAdvanceCompanionRecovery(job.Id,job.Revision,10914,NpcCompanionRecoveryPhase.WaitingOutside,Point(false)),"Cross actual paired doorway");job=store.ReadNpcAction(job.Id);
        Require(store.ObserveNpcAction(job.Id,job.Revision,10915,NpcActionObservation.Interrupted("Companion service ended.")),"End service without ending return");job=store.ReadNpcAction(job.Id);
        Require(job.ReturningToRoutine,"Terminal companion recovery must keep physical ownership");
        Require(store.PendingNpcActions().Exists(value=>value.Id==job.Id),"Recovery remains pending");
        Require(store.TryAdvanceCompanionRecovery(job.Id,job.Revision,32514,NpcCompanionRecoveryPhase.ReturningToOrigin),"Outside wait ends");job=store.ReadNpcAction(job.Id);
        Require(!store.TryAdvanceCompanionRecovery(job.Id,job.Revision,32515,NpcCompanionRecoveryPhase.ResumedRoutine,Point(false,99)),"No fabricated origin arrival");
        Require(!store.TryAdvanceCompanionRecovery(job.Id,job.Revision,32515,NpcCompanionRecoveryPhase.ResumedRoutine,Point(false,17)),"Origin target alone is not observed arrival");
        Require(store.SaveNpcActionPosition(job.Id,job.Revision,Point(false,17),true,32515),"Observe physical recruitment origin");job=store.ReadNpcAction(job.Id);
        Require(store.TryAdvanceCompanionRecovery(job.Id,job.Revision,32515,NpcCompanionRecoveryPhase.ResumedRoutine,Point(false,17)),"Observed recruitment origin arrival");job=store.ReadNpcAction(job.Id);
        Require(!job.ReturningToRoutine,"Routine released only at real recruitment origin");
    }
    private static void EnterpriseConsent()
    {
        NpcActionJob job;var store=Fixture(out job);NpcCompanionEnterprise enterprise;
        Require(store.TryBeginCompanionEnterprise(job.ResidentId,job.Revision,"plan:1",NpcCompanionEnterpriseKind.DungeonCrawl,110,out enterprise),"Plan enterprise");
        Require(!enterprise.AllowsOwnKill(enterprise.Id,job.ResidentId,job.ResidentId,true),"No automatic loot rights");
        Require(store.TryGrantCompanionLoot(job.ResidentId,enterprise.Id,enterprise.Revision,"grant:1",true,false,111),"Explicit own-kill grant");
        enterprise=store.ReadCompanionEnterprise(job.ResidentId);
        Require(enterprise.AllowsOwnKill(enterprise.Id,job.ResidentId,job.ResidentId,true),"Own actual defeated enemy permitted");
        Require(!enterprise.AllowsOwnKill(enterprise.Id,job.ResidentId,"other",true),"Other killer excluded");
        Require(!enterprise.AllowsOwnKill(enterprise.Id,job.ResidentId,job.ResidentId,false),"Containers excluded");
        Require(!enterprise.AllowsContainer(enterprise.Id),"Own-kill grant never grants containers");
        Require(!enterprise.AllowsLoadedLoot(enterprise.Id,false,true),"Own-kill permission cannot execute a native container take");
        Require(enterprise.AllowsLoadedLoot(enterprise.Id,true,false),"Actual own-corpse receipt authorizes native loot");
        store.Restore(store.Snapshot());enterprise=store.ReadCompanionEnterprise(job.ResidentId);
        Require(enterprise.OwnDefeatedEnemies && !enterprise.Containers,"Scope survives save restore");
        job=store.ReadNpcAction(job.Id);NpcCompanionEnterprise next;
        Require(store.TryBeginCompanionEnterprise(job.ResidentId,job.Revision,"plan:2",NpcCompanionEnterpriseKind.Raid,112,out next),"New enterprise");
        Require(!next.OwnDefeatedEnemies,"New enterprise resets permission");
        Require(!store.TryGrantCompanionLoot(job.ResidentId,enterprise.Id,enterprise.Revision,"stale",true,true,113),"Old enterprise cannot authorize new enterprise");
        Require(store.TryGrantCompanionLoot(job.ResidentId,next.Id,next.Revision,"container:1",false,true,113),"Separate express container grant");
        next=store.ReadCompanionEnterprise(job.ResidentId);Require(next.AllowsContainer(next.Id),"Container scope explicit");
        Require(next.AllowsLoadedLoot(next.Id,false,true),"Separate permission authorizes actual unowned dungeon treasure");
        Require(!next.AllowsLoadedLoot(next.Id,false,false),"A grant does not invent native access or ownership");
        Require(!next.AllowsLoadedLoot("old-enterprise",false,true),"Container execution remains enterprise bound");
        Require(!next.AllowsOwnKill(next.Id,job.ResidentId,job.ResidentId,true),"Container grant does not imply own-kill grant");
        Require(store.TryEndCompanionEnterprise(job.ResidentId,next.Id,114),"End enterprise");Require(!store.ReadCompanionEnterprise(job.ResidentId).Active,"No permanent permission");
    }
    private static bool Call(WorldStateStore store,string method,params object[] args)
    {var found=typeof(WorldStateStore).GetMethod(method);Require(found!=null,"Missing measured unobserved recovery: "+method);try{return (bool)found.Invoke(store,args);}catch(TargetInvocationException error){throw error.InnerException;}}
    private static void MeasuredRecovery()
    {
        NpcActionJob job;var store=Fixture(out job);
        Require(store.TryCaptureCompanionDungeonEntrance(job.Id,job.Revision,110,Point(true),Point(false)),"Capture native portal");job=store.ReadNpcAction(job.Id);
        Require(store.SaveNpcActionPosition(job.Id,job.Revision,Point(true,7),false,111),"Observed wait point");job=store.ReadNpcAction(job.Id);
        Require(store.TryBeginCompanionObstacleWait(job.Id,job.Revision,112),"Begin wait");job=store.ReadNpcAction(job.Id);
        Require(store.TryMarkCompanionRecoveryNotice(job.Id,job.Revision,112),"Claim one notice");job=store.ReadNpcAction(job.Id);
        Require(!Call(store,"TryAdvanceUnobservedCompanionRecovery",job.Id,job.Revision,10920d),"No route means no invented outside arrival");
        Require(!Call(store,"TryCaptureCompanionRecoveryRoute",job.Id,job.Revision,113d,new List<NpcActionTarget>{Point(true,99),Point(true)},Point(false),2d),"No disconnected measured source");
        Require(Call(store,"TryCaptureCompanionRecoveryRoute",job.Id,job.Revision,113d,new List<NpcActionTarget>{Point(true,7),Point(true)},Point(false),2d),"Capture actual return path");job=store.ReadNpcAction(job.Id);
        store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
        Require(!Call(store,"TryAdvanceUnobservedCompanionRecovery",job.Id,job.Revision,10911d),"Cannot walk before inside wait ends");
        Require(Call(store,"TryAdvanceUnobservedCompanionRecovery",job.Id,job.Revision,10919d),"Elapsed measured route advances");job=store.ReadNpcAction(job.Id);
        Equal(job.CompanionRecovery.Phase,NpcCompanionRecoveryPhase.ReturningToExit);Equal(job.LastPosition.X,3.5d);
        Require(job.CompanionRecovery.NoticeDelivered,"Stage catch-up preserves one-shot notice");
        Require(Call(store,"TryAdvanceUnobservedCompanionRecovery",job.Id,job.Revision,10926d),"Measured portal crossing");job=store.ReadNpcAction(job.Id);
        Equal(job.LastPosition.WorldSpaceId,"settlement:100");Equal(job.CompanionRecovery.WaitUntil,32526d);
        Require(Call(store,"TryCaptureCompanionRecoveryRoute",job.Id,job.Revision,10927d,new List<NpcActionTarget>{Point(false),Point(false,17)},Point(false,17),2d),"Capture actual origin route");job=store.ReadNpcAction(job.Id);
        Require(store.ObserveNpcAction(job.Id,job.Revision,10928,NpcActionObservation.Interrupted("Service finished.")),"Keep unobserved recovery after service");job=store.ReadNpcAction(job.Id);
        store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
        Require(Call(store,"TryAdvanceUnobservedCompanionRecovery",job.Id,job.Revision,32543d),"Return route interpolation");job=store.ReadNpcAction(job.Id);
        Equal(job.LastPosition.X,8.5d);Equal(job.CompanionRecovery.Phase,NpcCompanionRecoveryPhase.ReturningToOrigin);
        Require(Call(store,"TryAdvanceUnobservedCompanionRecovery",job.Id,job.Revision,40000d),"Long absence completes only measured route");job=store.ReadNpcAction(job.Id);
        Equal(job.LastPosition.X,17d);Equal(job.CompanionRecovery.Phase,NpcCompanionRecoveryPhase.ResumedRoutine);Require(!job.ReturningToRoutine,"Release actual origin routine");
        Require(!store.TryMarkCompanionRecoveryNotice(job.Id,job.Revision,40001),"Native Talk cannot reopen for same recovery");
    }
    private static void LongAbsence()
    {
        NpcActionJob job;var store=Fixture(out job);
        Require(store.TryCaptureCompanionDungeonEntrance(job.Id,job.Revision,110,Point(true),Point(false)),"Actual portal");job=store.ReadNpcAction(job.Id);
        Require(store.TryCaptureCompanionRecruitmentRoute(job.Id,job.Revision,110.25,new[]{Point(false),Point(false,17)}),"Retain actual source-space origin evidence before dungeon entry");job=store.ReadNpcAction(job.Id);
        Require(store.SaveNpcActionPosition(job.Id,job.Revision,Point(true),false,111),"Already standing at actual portal");job=store.ReadNpcAction(job.Id);
        Require(store.TryBeginCompanionObstacleWait(job.Id,job.Revision,112),"Wait");job=store.ReadNpcAction(job.Id);
        Require(Call(store,"TryCaptureCompanionRecoveryRoute",job.Id,job.Revision,113d,new[]{Point(true),Point(true)},Point(false),2d),"Measured zero-length portal approach");job=store.ReadNpcAction(job.Id);
        store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
        Require(Call(store,"TryAdvanceUnobservedCompanionRecovery",job.Id,job.Revision,40000d),"Single catch-up spans both waits and both measured paths");job=store.ReadNpcAction(job.Id);
        Equal(job.CompanionRecovery.WaitStartedAt,10912.5d);Equal(job.CompanionRecovery.WaitUntil,32512.5d);
        Equal(job.CompanionRecovery.WalkSecondsPerUnit,2d);Equal(job.LastPosition.X,17d);Equal(job.CompanionRecovery.Phase,NpcCompanionRecoveryPhase.ResumedRoutine);
        store=Fixture(out job);
        Require(store.TryCaptureCompanionDungeonEntrance(job.Id,job.Revision,110,Point(true),Point(false)),"New actual portal");job=store.ReadNpcAction(job.Id);
        Require(store.SaveNpcActionPosition(job.Id,job.Revision,Point(true),false,111),"Observed dungeon point");job=store.ReadNpcAction(job.Id);
        Require(store.TryBeginCompanionObstacleWait(job.Id,job.Revision,112),"Wait for returning player");job=store.ReadNpcAction(job.Id);
        double finish=job.FinishAt;
        Require(store.TryResumeCompanionFollow(job.Id,job.Revision,113),"Native reachable player may resume active service");job=store.ReadNpcAction(job.Id);
        Equal(job.FinishAt,finish);Equal(job.CompanionRecovery.Phase,NpcCompanionRecoveryPhase.Following);
        NpcCompanionEnterprise enterprise;
        Require(store.TryBeginCompanionEnterprise(job.ResidentId,job.Revision,"fractional-plan",NpcCompanionEnterpriseKind.Raid,40000.5,out enterprise),"Full-precision game clock accepted");
        Require(!store.TryGrantCompanionLoot(job.ResidentId,enterprise.Id,enterprise.Revision,"rounded-down",true,false,40000),"Rounded-down native clock cannot mutate newer state");
        Require(store.TryGrantCompanionLoot(job.ResidentId,enterprise.Id,enterprise.Revision,"fractional-grant",true,false,40000.5),"Paused full-precision consent accepted");
        enterprise=store.ReadCompanionEnterprise(job.ResidentId);
        Require(store.TryRecordCompanionDefeat(job.ResidentId,enterprise.Id,"native:123",0,40000.5),"Actual damage adapter may record pending native defeat");
        Require(store.TryRecordCompanionDefeat(job.ResidentId,enterprise.Id,"native:123",456,40000.5),"Actual death adapter binds corpse UID");
        Require(!store.TryRecordCompanionDefeat(job.ResidentId,enterprise.Id,"native:123",789,40000.5),"Bound corpse cannot be substituted");
        store.Restore(store.Snapshot());enterprise=store.ReadCompanionEnterprise(job.ResidentId);Equal(enterprise.Defeats[0].CorpseLoadId,456UL);
    }
    private static void NativeCapability()
    {
        Equal(NpcClimbingCapability.StartChance(50,50,false),90);
        Equal(NpcClimbingCapability.StartChance(50,50,true),103);
        Equal(NpcClimbingCapability.Speed(3,false),1d);Equal(NpcClimbingCapability.Speed(3,true),2d);
        var timing=typeof(NpcCompanionWaitPolicy).GetMethod("UnpausedStallSeconds");
        Require(timing!=null,"Obstacle timer must count active frames, not paused wall time");
        Equal((double)timing.Invoke(null,new object[]{3d,600d,false,false}),3d);
        Equal((double)timing.Invoke(null,new object[]{3d,1d,true,false}),4d);
        Equal((double)timing.Invoke(null,new object[]{3d,1d,true,true}),0d);
        var gate=typeof(NpcCompanionWaitPolicy).GetMethod("GateApproachStart");Require(gate!=null,"Unobserved native gate schedule required");
        Equal((double)gate.Invoke(null,new object[]{22*3600d,6d}),30*3600d);
        Equal((double)gate.Invoke(null,new object[]{5*3600d,6d}),6*3600d);
        Equal((double)gate.Invoke(null,new object[]{18*3600d-3,6d}),30*3600d);
        Equal((double)gate.Invoke(null,new object[]{17*3600d,6d}),17*3600d);
    }
    private static void OverlandRecovery()
    {
        NpcActionJob job;var origin=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,X=17};
        var store=Fixture(out job,origin);
        var inside=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=200,WorldSpaceId="dungeon:200"};
        var outside=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=200,WorldSpaceId="settlement:200"};
        var door=Point(false);door.BuildingKey=10;
        Require(store.TryCaptureCompanionDungeonEntrance(job.Id,job.Revision,110,inside,outside),"Capture actual different-location dungeon");job=store.ReadNpcAction(job.Id);
        Require(store.TryCaptureCompanionNativeReturnEstimate(job.Id,job.Revision,110.25,3600),"Persist native estimate before any yielding route survey");
        store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);Equal(job.CompanionRecovery.NativeReturnEstimateSeconds,3600d);
        Require(store.TryCaptureCompanionOverlandReturn(job.Id,job.Revision,110.5,door,new[]{Point(false,-30),door},3600,1),"Native travel estimate plus actual origin entrance/approach");job=store.ReadNpcAction(job.Id);
        Require(store.SaveNpcActionPosition(job.Id,job.Revision,inside,false,111),"Actual dungeon position");job=store.ReadNpcAction(job.Id);
        Require(store.TryBeginCompanionObstacleWait(job.Id,job.Revision,112),"Wait inside");job=store.ReadNpcAction(job.Id);
        Require(store.TryAdvanceCompanionRecovery(job.Id,job.Revision,10912,NpcCompanionRecoveryPhase.ReturningToExit),"Return to exit");job=store.ReadNpcAction(job.Id);
        Require(store.TryAdvanceCompanionRecovery(job.Id,job.Revision,10912,NpcCompanionRecoveryPhase.WaitingOutside,outside),"Actual portal crossed");job=store.ReadNpcAction(job.Id);
        var outsideSave=store.Snapshot();
        Require(!store.TryBeginCompanionOverlandReturn(job.Id,job.Revision,10913,10913),"No departure before promised outside wait");
        Require(store.TryBeginCompanionOverlandReturn(job.Id,job.Revision,32512,32512),"Timed ordinary on-foot return starts");job=store.ReadNpcAction(job.Id);
        Equal(job.LastPosition.SettlementId,200);
        Require(!store.TryArriveCompanionOverlandReturn(job.Id,job.Revision,36111),"No instant world jump");
        store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
        Require(store.TryArriveCompanionOverlandReturn(job.Id,job.Revision,36112),"Native estimate ends at real origin approach");job=store.ReadNpcAction(job.Id);
        Equal(job.LastPosition.SettlementId,100);Equal(job.LastPosition.X,-30d);Equal(job.CompanionRecovery.Phase,NpcCompanionRecoveryPhase.ReturningToOrigin);
        Require(store.TryAdvanceUnobservedCompanionRecovery(job.Id,job.Revision,36127),"Measured final street approach");job=store.ReadNpcAction(job.Id);Equal(job.LastPosition.X,-15d);
        Require(store.TryAdvanceUnobservedCompanionRecovery(job.Id,job.Revision,36142),"Return reaches actual recruitment entrance");job=store.ReadNpcAction(job.Id);
        Equal(job.LastPosition.BuildingKey,10);Equal(job.CompanionRecovery.Phase,NpcCompanionRecoveryPhase.ResumedRoutine);
        Equal(job.CompanionRecovery.RecruitmentOrigin.Kind,NpcActionTargetKind.IndoorPoint);Equal(job.CompanionRecovery.RecruitmentOrigin.X,17d);
        store.Restore(outsideSave);job=store.ReadNpcAction(job.Id);
        Require(store.TryCaptureCompanionOverlandReturn(job.Id,job.Revision,10913,door,new[]{Point(false,-30),door},22*3600d-32512d,1,6),"Actual gate prefix duration");job=store.ReadNpcAction(job.Id);
        Require(store.TryBeginCompanionOverlandReturn(job.Id,job.Revision,32512,32512),"Night-due journey");job=store.ReadNpcAction(job.Id);
        Require(store.TryArriveCompanionOverlandReturn(job.Id,job.Revision,22*3600),"Native arrival remains outside shut gate");job=store.ReadNpcAction(job.Id);
        Equal(job.LastPosition.X,-30d);Equal(job.CompanionRecovery.MeasuredRoute.StartedAt,30*3600d);
        store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
        Require(!store.TryAdvanceUnobservedCompanionRecovery(job.Id,job.Revision,23*3600),"Unobserved night cannot cross closed gate");
        Require(store.TryAdvanceUnobservedCompanionRecovery(job.Id,job.Revision,30*3600+15),"Actual next native opening permits measured progress");job=store.ReadNpcAction(job.Id);Equal(job.LastPosition.X,-15d);
    }
    internal static void Require(bool value,string reason) {if(!value)throw new Exception(reason);}
    internal static void Equal<T>(T actual,T expected) {Require(Equals(actual,expected),"Expected "+expected+"; actual "+actual);}
}
