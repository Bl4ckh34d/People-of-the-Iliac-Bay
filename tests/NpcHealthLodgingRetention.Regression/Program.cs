using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const double Now=72000d;

    private static int Main()
    {
        var cases=new List<(string,Action)> {
            ("health booking cannot gain unpaid nights",UnpaidExtensionRejected),
            ("terminal booking retires without replay charge",TerminalBookingRetiresSafely),
            ("active care and task pin expired booking",CareAndTaskPinBooking),
            ("returning action pins expired booking",ReturningActionPinsBooking),
            ("later rest attempt and return pin reused booking",LaterRestAttemptPinsReusedBooking),
            ("scan budget counts skipped ordinary bookings",ScanBudgetCountsSkippedRows)
        };
        int failed=0;
        foreach(var test in cases)
            try {test.Item2();Console.WriteLine("PASS "+test.Item1);}
            catch(Exception error) {failed++;Console.WriteLine("FAIL "+test.Item1+": "+error);}
        Console.WriteLine((cases.Count-failed)+"/"+cases.Count+" passed");
        return failed==0?0:1;
    }

    private static void UnpaidExtensionRejected()
    {
        Fixture f=Create();Reserve(f);
        double end=Now+86400d;
        Check(!f.Store.TryExtendLodging(BookingId(f),end+86400d),"unpaid health extension rejected");
        Check(f.Store.TryExtendLodging(BookingId(f),end),"same-end replay allowed");
        Equal(end,Booking(f.Store,f).EndsAt);
        Equal(1,Booking(f.Store,f).PaidNights);
        var restored=new WorldStateStore(0);restored.Restore(f.Store.Snapshot());
        Equal(end,Booking(restored,f).EndsAt);
    }

    private static void TerminalBookingRetiresSafely()
    {
        Fixture f=Create();Reserve(f);EndCare(f);
        int wallet=f.Store.ReadNpcInventory(f.ResidentId).WalletGold;
        NpcHealthLodgingRetirementResult result=f.Store.RetireExpiredNpcHealthRestLodging(Now+86400d,1);
        Equal(1,result.Scanned);Equal(1,result.Retired);
        Equal(0,f.Store.Snapshot().Lodgings[0].Bookings.Count);
        Equal(wallet,f.Store.ReadNpcInventory(f.ResidentId).WalletGold);
        var restored=new WorldStateStore(0);restored.Restore(f.Store.Snapshot());
        NpcHealthLodgingReservation replay;
        Check(!restored.TryReserveNpcHealthRestLodging(Request(f,restored),out replay),"terminal task cannot replay retired charge");
        Equal(wallet,restored.ReadNpcInventory(f.ResidentId).WalletGold);
        WorldState pruned=restored.Snapshot();
        pruned.NpcTasks.RemoveAll(task=>task.Id==f.Task.Id);
        restored.Restore(pruned);
        Check(!restored.TryReserveNpcHealthRestLodging(Request(f,restored),out replay),"pruned task cannot replay retired charge");
        Equal(wallet,restored.ReadNpcInventory(f.ResidentId).WalletGold);
    }

    private static void CareAndTaskPinBooking()
    {
        Fixture f=Create();Reserve(f);
        var first=f.Store.RetireExpiredNpcHealthRestLodging(Now+86400d,1);
        Equal(0,first.Retired);Equal(1,first.PinnedByTask);
        NpcTaskPlan task=f.Store.ReadNpcTask(f.Task.Id);
        Check(f.Store.FailNpcTask(task.Id,task.Revision,Now+1,"Fixture failure."),"task fails");
        var second=f.Store.RetireExpiredNpcHealthRestLodging(Now+86400d,1);
        Equal(0,second.Retired);Equal(1,second.PinnedByCare);
        NpcHealthCareRecord care=f.Store.ReadNpcHealthCare(f.ResidentId);
        Check(f.Store.TryBlockNpcHealthCare(f.ResidentId,care.Revision,Now+2,"Fixture failure."),"care releases task");
        var third=f.Store.RetireExpiredNpcHealthRestLodging(Now+86400d,1);
        Equal(1,third.Retired);
    }

    private static void ScanBudgetCountsSkippedRows()
    {
        Fixture f=Create();
        Check(f.Store.TryBookLodging(1,99,"old-player","player",1,true,1,0,100,"bed:4"),"older ordinary booking");
        Reserve(f);EndCare(f);
        var first=f.Store.RetireExpiredNpcHealthRestLodging(Now+86400d,1);
        Equal(1,first.Scanned);Equal(0,first.Retired);
        var second=f.Store.RetireExpiredNpcHealthRestLodging(Now+86400d,1);
        Equal(1,second.Scanned);Equal(1,second.Retired);
        Equal(1,f.Store.Snapshot().Lodgings[0].Bookings.Count);
    }

    private static void ReturningActionPinsBooking()
    {
        Fixture f=Create();Reserve(f);
        NpcTaskPlan task=f.Store.ReadNpcTask(f.Task.Id);
        var action=new NpcActionJob {Id=task.Id+":step:0",ProposalId=task.Id+":step:0",
            ResidentId=f.ResidentId,Kind=NpcActionKind.RestToHeal,Target=task.Steps[0].Target,
            LastPosition=task.Steps[0].Target,Phase=NpcActionPhase.Accepted,Revision=1,
            AcceptedAt=Now+1,UpdatedAt=Now+1,DeadlineAt=Now+8*3600d+500d,
            RestDurationSeconds=8*3600d,
            ExpectedVitalsRevision=f.Store.ReadNpcVitals(f.ResidentId,NpcArchetype.Citizen).Revision};
        string reason;
        Check(f.Store.TryDispatchNpcTaskStep(task.Id,task.Revision,Now+1,action,true,true,true,out reason),"dispatch: "+reason);
        NpcTaskPlan running=f.Store.ReadNpcTask(task.Id);
        Check(f.Store.CancelNpcTask(task.Id,running.Revision,Now+2,"Fixture cancellation."),"cancel running task");
        NpcActionJob cancelled=f.Store.ReadNpcAction(action.Id);
        Check(f.Store.SaveNpcActionPosition(action.Id,cancelled.Revision,task.Steps[0].Target,true,Now+3),"capture return position");
        NpcHealthCareRecord care=f.Store.ReadNpcHealthCare(f.ResidentId);
        Check(f.Store.TryBlockNpcHealthCare(f.ResidentId,care.Revision,Now+4,"Fixture cancellation."),"release care task");
        var pinned=f.Store.RetireExpiredNpcHealthRestLodging(Now+86400d,1);
        Equal(0,pinned.Retired);Equal(1,pinned.PinnedByAction);
        NpcActionJob returning=f.Store.ReadNpcAction(action.Id);
        Check(f.Store.SaveNpcActionPosition(action.Id,returning.Revision,task.Steps[0].Target,false,Now+5),"finish return");
        var retired=f.Store.RetireExpiredNpcHealthRestLodging(Now+86400d,1);
        Equal(1,retired.Retired);
    }

    private static void LaterRestAttemptPinsReusedBooking()
    {
        Fixture f=Create();
        NpcHealthLodgingReservation original;
        var firstRequest=Request(f,f.Store);firstRequest.Nights=2;firstRequest.Price=14;
        Check(f.Store.TryReserveNpcHealthRestLodging(firstRequest,out original),"original two-night booking");
        string bookingId=original.TaskId;
        EndCare(f);
        NpcHealthCareRecord blocked=f.Store.ReadNpcHealthCare(f.ResidentId);
        double retryAt=blocked.RetryAt;
        string nextId=NpcHealthCare.MakeTaskId(blocked);
        var next=new NpcTaskPlan {Id=nextId,ProposalId=nextId,ResidentId=f.ResidentId,
            Phase=NpcTaskPhase.Ready,Revision=1,AcceptedAt=retryAt,UpdatedAt=retryAt,
            DeadlineAt=retryAt+8*3600d+1800d,
            Steps=new List<NpcTaskStep>{new NpcTaskStep {Kind=NpcActionKind.RestToHeal,
                Target=f.Task.Steps[0].Target,DurationSeconds=8*3600d}}};
        string reason;
        Check(f.Store.TryAcceptAndBindNpcHealthCareTask(f.ResidentId,blocked.Revision,next,retryAt,out reason),
            "next rest task binds: "+reason);
        NpcHealthCareRecord acting=f.Store.ReadNpcHealthCare(f.ResidentId);
        var nextRequest=new NpcHealthLodgingReservationRequest {ResidentId=f.ResidentId,CareEpisodeId=acting.Id,
            TaskId=nextId,ExpectedCareRevision=acting.Revision,
            ExpectedInventoryRevision=f.Store.ReadNpcInventory(f.ResidentId).Revision,
            SettlementId=1,BuildingKey=99,BedId="bed:4",StartsAt=retryAt,Nights=1,Price=14};
        NpcHealthLodgingReservation reused;
        Check(f.Store.TryReserveNpcHealthRestLodging(nextRequest,out reused),"second task reuses paid bed");
        Equal(bookingId,reused.TaskId);Equal(86,f.Store.ReadNpcInventory(f.ResidentId).WalletGold);
        double expiry=original.EndsAt;
        var carePin=f.Store.RetireExpiredNpcHealthRestLodging(expiry,1);
        Equal(0,carePin.Retired);Equal(1,carePin.PinnedByCare);

        NpcTaskPlan accepted=f.Store.ReadNpcTask(nextId);
        var action=new NpcActionJob {Id=nextId+":step:0",ProposalId=nextId+":step:0",
            ResidentId=f.ResidentId,Kind=NpcActionKind.RestToHeal,Target=next.Steps[0].Target,
            LastPosition=next.Steps[0].Target,Phase=NpcActionPhase.Accepted,Revision=1,
            AcceptedAt=retryAt+1,UpdatedAt=retryAt+1,DeadlineAt=retryAt+8*3600d+500d,
            RestDurationSeconds=8*3600d,
            ExpectedVitalsRevision=f.Store.ReadNpcVitals(f.ResidentId,NpcArchetype.Citizen).Revision};
        Check(f.Store.TryDispatchNpcTaskStep(nextId,accepted.Revision,retryAt+1,action,true,true,true,out reason),
            "second rest dispatches: "+reason);
        NpcTaskPlan running=f.Store.ReadNpcTask(nextId);
        Check(f.Store.CancelNpcTask(nextId,running.Revision,retryAt+2,"Fixture cancellation."),"second task cancels");
        NpcActionJob cancelled=f.Store.ReadNpcAction(action.Id);
        Check(f.Store.SaveNpcActionPosition(action.Id,cancelled.Revision,next.Steps[0].Target,true,retryAt+3),
            "second action returns");
        acting=f.Store.ReadNpcHealthCare(f.ResidentId);
        Check(f.Store.TryBlockNpcHealthCare(f.ResidentId,acting.Revision,retryAt+4,"Fixture cancellation."),
            "second care task released");
        var actionPin=f.Store.RetireExpiredNpcHealthRestLodging(expiry,1);
        Equal(0,actionPin.Retired);Equal(1,actionPin.PinnedByAction);
        NpcActionJob returning=f.Store.ReadNpcAction(action.Id);
        Check(f.Store.SaveNpcActionPosition(action.Id,returning.Revision,next.Steps[0].Target,false,retryAt+5),
            "second return finishes");
        var retired=f.Store.RetireExpiredNpcHealthRestLodging(expiry,1);
        Equal(1,retired.Retired);
    }

    private static Fixture Create()
    {
        var store=new WorldStateStore(42);
        var town=store.GetOrCreate(1,new[]{10},1);
        Resident resident=town.Households[0].Residents.Find(value=>value.AgeGroup==ResidentAgeGroup.Adult);
        Check(resident!=null,"adult resident");
        Check(store.InitializeNpcInventory(resident.Id,100,new List<NpcInventoryItem>()),"wallet");
        NpcVitalsView vitals=store.ReadNpcVitals(resident.Id,NpcArchetype.Citizen);
        int threshold=(int)((long)vitals.MaximumHealth*2/3);
        Check(store.TryCaptureNpcVitals(resident.Id,NpcArchetype.Citizen,Math.Max(1,threshold-1),vitals.CurrentMagicka,Now-10,vitals.Revision),"injury");
        Check(store.TryBeginNpcHealthCare(resident.Id,NpcArchetype.Citizen,Now-5),"care");
        NpcHealthCareRecord care=store.ReadNpcHealthCare(resident.Id);
        string taskId=NpcHealthCare.MakeTaskId(care);
        var target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=99,ContainerId="bed:4",X=1,Y=0,Z=2};
        var task=new NpcTaskPlan {Id=taskId,ProposalId=taskId,ResidentId=resident.Id,Phase=NpcTaskPhase.Ready,Revision=1,
            AcceptedAt=Now-4,UpdatedAt=Now-4,DeadlineAt=Now+8*3600d+1800d,
            Steps=new List<NpcTaskStep> {new NpcTaskStep {Kind=NpcActionKind.RestToHeal,Target=target,DurationSeconds=8*3600d}}};
        string reason;
        Check(store.TryAcceptAndBindNpcHealthCareTask(resident.Id,care.Revision,task,Now-4,out reason),"accept and bind care: "+reason);
        care=store.ReadNpcHealthCare(resident.Id);
        store.RegisterLodgingBeds(1,99,new List<LodgingBed>{new LodgingBed {Id="bed:4"}});
        return new Fixture {Store=store,ResidentId=resident.Id,Care=care,Task=task};
    }

    private static NpcHealthLodgingReservationRequest Request(Fixture f,WorldStateStore store)
    {
        return new NpcHealthLodgingReservationRequest {ResidentId=f.ResidentId,CareEpisodeId=f.Care.Id,TaskId=f.Task.Id,
            ExpectedCareRevision=f.Care.Revision,ExpectedInventoryRevision=store.ReadNpcInventory(f.ResidentId).Revision,
            SettlementId=1,BuildingKey=99,BedId="bed:4",StartsAt=Now,Nights=1,Price=14};
    }

    private static void Reserve(Fixture f)
    {
        NpcHealthLodgingReservation reservation;
        Check(f.Store.TryReserveNpcHealthRestLodging(Request(f,f.Store),out reservation),"health reservation");
        Equal(86,f.Store.ReadNpcInventory(f.ResidentId).WalletGold);
    }

    private static void EndCare(Fixture f)
    {
        NpcTaskPlan task=f.Store.ReadNpcTask(f.Task.Id);
        Check(f.Store.FailNpcTask(task.Id,task.Revision,Now+1,"Fixture failure."),"task fails");
        NpcHealthCareRecord care=f.Store.ReadNpcHealthCare(f.ResidentId);
        Check(f.Store.TryBlockNpcHealthCare(f.ResidentId,care.Revision,Now+2,"Fixture failure."),"care releases task");
    }

    private static string BookingId(Fixture f) {return "health-rest-booking:"+f.Task.Id;}
    private static LodgingBooking Booking(WorldStateStore store,Fixture f)
    {return store.Snapshot().Lodgings[0].Bookings.Find(value=>value.Id==BookingId(f));}
    private static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    private static void Equal<T>(T expected,T actual)
    {if(!Equals(expected,actual))throw new InvalidOperationException("Expected "+expected+", got "+actual+".");}
    private sealed class Fixture {public WorldStateStore Store;public string ResidentId;public NpcHealthCareRecord Care;public NpcTaskPlan Task;}
}
