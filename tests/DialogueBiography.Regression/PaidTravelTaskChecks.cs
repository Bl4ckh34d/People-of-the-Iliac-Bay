using System;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

static class PaidTravelTaskChecks
{
    static void Need(bool value,string message) {if(!value)throw new Exception(message);}
    static NpcActionTarget Point(double x=0) => new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,
        SettlementId=100,WorldSpaceId="settlement:100",X=x};

    static WorldStateStore Fixture(out NpcActionJob follow,bool paid=true,bool travel=true)
    {
        var store=new WorldStateStore(42);var resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
        follow=new NpcActionJob {Id="paid-guidance-follow",ProposalId="paid-guidance-turn",ResidentId=resident.Id,
            Kind=NpcActionKind.Follow,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
            Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=400,FinishAt=300,LastPosition=Point()};
        string error;
        Need(ClassifiedAdmissionFixture.TryAcceptAction(store,follow,true,true,true,out error),error);
        if(paid)
        {
            const string contract="contract:paid-guidance";NpcServiceContractTerms terms;
            string person=resident.Id;
            Need(NpcServiceContractTerms.TryCreate(contract,"player",person,"home:100:10",NpcServiceRole.Fighter,
                NpcServiceCoverage.Follow|(travel?NpcServiceCoverage.Travel:NpcServiceCoverage.None),120,false,NpcServiceRiskScope.None,
                new NpcServiceProtectedTarget[0],NpcServiceRestTreatment.RestNotIncluded,
                NpcServiceResupplyResponsibility.SeparatelyNegotiated,new NpcServiceReservedObligation[0],
                id=>id=="player" || id==person,id=>id=="home:100:10",(id,category,revision)=>false,
                (id,revision,kind,start,end)=>false,out terms),"paid terms");
            var funding=NpcServiceTransferReceipt.Create("fund:paid-guidance",contract,NpcServiceTransferKind.Funding,"player",person,120,100);
            var ticket=store.CaptureNpcServiceContractWriteTicket();NpcServiceContractLedger reserved;
            Need(NpcServiceContractLedger.TryReserve(ticket.SourceLedger,ticket.ExpectedLedgerRevision,terms,funding,out reserved),"reserve");
            Need(NpcDailyPaymentFixture.Commit(store,ticket,reserved,funding,out error),error);
            Need(store.TryBindPaidFollowAction(follow.Id,follow.Revision,contract,out error),error);
            follow=store.ReadNpcAction(follow.Id);
        }
        Need(store.ObserveNpcAction(follow.Id,follow.Revision,101,NpcActionObservation.Arrival()),"start Follow");
        follow=store.ReadNpcAction(follow.Id);return store;
    }

    static NpcTaskPlan Plan(NpcActionJob follow,double now=102) => new NpcTaskPlan {
        Id="travel-guidance",ProposalId="travel-guidance",ResidentId=follow.ResidentId,
        AcceptedAt=now,UpdatedAt=now,DeadlineAt=now+1000,
        Steps={new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=Point(5)},
            new NpcTaskStep {Kind=NpcActionKind.Wait,Target=Point(5),DurationSeconds=10}}};

    static NpcTaskPlan Accept(WorldStateStore store,NpcActionJob follow,double now=102)
    {
        NpcTaskPlan bound;string error;
        Need(store.TryBindPaidFollowTravelTask(Plan(follow,now),out bound,out error),error);
        Need(bound.PaidFollowActionId==follow.Id,"task links original paid Follow");
        Need(ClassifiedAdmissionFixture.TryAcceptTask(store,bound,true,out error),error);
        Need(store.ActiveNpcAction(follow.ResidentId)==null,"paid Follow held without a second movement owner");
        return store.ReadNpcTask(bound.Id);
    }

    static NpcActionJob Dispatch(WorldStateStore store,NpcTaskPlan task,double now)
    {
        var step=task.Steps[task.CurrentStepIndex];
        var job=new NpcActionJob {Id=NpcTaskContract.ActionId(task),ProposalId=NpcTaskContract.ActionId(task),ResidentId=task.ResidentId,
            Kind=step.Kind,Target=NpcActionExecutor.CopyTarget(step.Target),Phase=NpcActionPhase.Accepted,Revision=1,
            AcceptedAt=now,UpdatedAt=now,DeadlineAt=task.DeadlineAt,FinishAt=step.DurationSeconds>0?now+step.DurationSeconds:0,
            LastPosition=Point(task.CurrentStepIndex==0?2:5)};
        string error;
        Need(store.TryDispatchNpcTaskStep(task.Id,task.Revision,now,job,true,true,true,out error),error);
        Need(store.ObserveNpcAction(job.Id,1,now,NpcActionObservation.Arrival()),"start task child");
        return store.ReadNpcAction(job.Id);
    }

    internal static int Run()
    {
        NpcActionJob follow;var store=Fixture(out follow);string contract=NpcPaidCompanionAction.ContractId(follow);
        int wallet=store.ReadNpcInventory(follow.ResidentId).WalletGold;long ledger=store.ReadNpcServiceContracts().Revision;
        var task=Accept(store,follow);var move=Dispatch(store,task,103);
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());store=restored;
        Need(store.ActiveNpcAction(follow.ResidentId).Id==move.Id,"restore retains only current child movement");
        Need(store.ObserveNpcAction(move.Id,move.Revision,104,NpcActionObservation.Success("actual-move")),"move effect");
        task=store.ReadNpcTask(task.Id);Need(store.ReconcileNpcTask(task.Id,task.Revision,104),"reconcile MoveTo");
        task=store.ReadNpcTask(task.Id);var wait=Dispatch(store,task,104);
        Need(store.ObserveNpcAction(wait.Id,wait.Revision,114,NpcActionObservation.Success("actual-wait")),"wait effect");
        task=store.ReadNpcTask(task.Id);Need(store.ReconcileNpcTask(task.Id,task.Revision,114),"reconcile Wait");
        var resumed=store.ActiveNpcAction(follow.ResidentId);
        Need(resumed!=null && resumed.Id==follow.Id && resumed.Phase==NpcActionPhase.Executing && resumed.FinishAt==follow.FinishAt,
            "same paid Follow resumes with original expiry");
        Need(NpcPaidCompanionAction.ContractId(resumed)==contract && store.ReadNpcServiceContracts().Revision==ledger &&
            store.ReadNpcInventory(follow.ResidentId).WalletGold==wallet,"no new contract or double money");
        restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Need(restored.ActiveNpcAction(follow.ResidentId).Id==follow.Id,"completed travel resumes after reload");

        store=Fixture(out follow);task=Accept(store,follow);move=Dispatch(store,task,103);
        task=store.ReadNpcTask(task.Id);Need(store.CancelNpcTask(task.Id,task.Revision,104,"Player cancelled the travel task."),"cancel task");
        Need(store.ReadNpcTask(task.Id).Phase==NpcTaskPhase.Cancelled && store.ReadNpcAction(move.Id).Phase==NpcActionPhase.Cancelled &&
            store.ReadNpcAction(task.Id+":step:1")==null && store.ActiveNpcAction(follow.ResidentId).Id==follow.Id,
            "cancel task stops child and resumes same Follow without later Wait");
        restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Need(restored.ActiveNpcAction(follow.ResidentId).Id==follow.Id,"cancelled travel resumes after reload");

        store=Fixture(out follow);task=Accept(store,follow,follow.FinishAt-40);
        Need(task.DeadlineAt==follow.FinishAt,"task cannot extend prepaid day");move=Dispatch(store,task,task.AcceptedAt+1);
        task=store.ReadNpcTask(task.Id);Need(store.ReconcileNpcTask(task.Id,task.Revision,follow.FinishAt),"reconcile expiry");
        Need(NpcActionExecutor.IsTerminal(store.ReadNpcAction(follow.Id)),"expired paid Follow cannot resume work");
        Need(store.ReadNpcServiceContracts().Read(NpcPaidCompanionAction.ContractId(follow)).BlockEndAt==follow.FinishAt,
            "paid absolute end unchanged");
        restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Need(NpcActionExecutor.IsTerminal(restored.ReadNpcAction(follow.Id)),"expired service remains terminal after reload");

        NpcTaskPlan refused;string reason;
        store=Fixture(out follow,false);Need(!store.TryBindPaidFollowTravelTask(Plan(follow),out refused,out reason),"unpaid action cannot be replaced");
        store=Fixture(out follow,true,false);Need(!store.TryBindPaidFollowTravelTask(Plan(follow),out refused,out reason),"non-travel service cannot be replaced");
        store=Fixture(out follow);var unsupported=Plan(follow);unsupported.Steps[0].Kind=NpcActionKind.Follow;
        Need(!store.TryBindPaidFollowTravelTask(unsupported,out refused,out reason),"uncovered work remains forbidden");
        return 4;
    }
}
