using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        try
        {
            DirectPayment(); Console.WriteLine("PASS direct daily payment");
            WalletPayment(); Console.WriteLine("PASS atomic wallet payment and rollback");
            DeferredRest(); Console.WriteLine("PASS deferred guard rest");
            LateRestCandidate(); Console.WriteLine("PASS late rest rejection retains shift and permits closer placement");
            ApproachingRestCandidate(); Console.WriteLine("PASS approaching rest keeps physical position and rejects late return");
            DeferredRoster(); Console.WriteLine("PASS deferred rest preserves finite identities");
            var cases=new List<(string Name,Action Body)>();
            PeopleOfTheIliacBay.Tests.GuardRefugeTests.Add(cases);
            PeopleOfTheIliacBay.Tests.NpcCompanionWageTests.Add(cases);
            foreach(var check in cases){check.Body();Console.WriteLine("PASS "+check.Name);}
            return 0;
        }
        catch(Exception error) { Console.Error.WriteLine("FAIL " + error.Message); return 1; }
    }
    private static void DeferredRoster()
    {
        var roster=GuardRoster.Create(1,42,2,new[]{new SavedRoutePoint()});
        foreach(var guard in roster.Guards)
        {
            guard.DayRestUntil=1000;
            var plan=GuardRefugeJourney.Create(17,new[]{new GuardPoint3(),new GuardPoint3{X=13}},null,100,1.3,12);
            roster.TakeRefuge(guard.Id,plan,100);guard.Refuge.ReleaseToReturnBy(1000);
        }
        string first=roster.Guards[0].Id,second=roster.Guards[1].Id;
        var copy=GuardRoster.CopyValidated(roster);
        Assert(copy.Guards.Count==2 && copy.Guards[0].Id==first && copy.Guards[1].Id==second,"same finite guards");
        Assert(copy.Guards.TrueForAll(g=>g.Refuge.RestPending),"shared entrance needs no exclusive unseen bed");
        copy.Advance(1000);
        Assert(copy.Guards.TrueForAll(g=>g.Refuge==null && g.Duty==GuardDuty.Posted),"both guards resume posts");
    }
    private static void DeferredRest()
    {
        var outside=new[]{new GuardPoint3{X=0,Z=0},new GuardPoint3{X=13,Z=0}};
        var plan=GuardRefugeJourney.Create(17,outside,null,100,1.3,12);
        Assert(plan.WallDuty && !plan.RestBedRecord.HasValue && plan.Interior==null,"rest has no unseen bed or indoor route");
        Assert(plan.ReleaseToReturnBy(1000),"deferred rest still honors shift");
        Assert(plan.Evaluate(219).Phase==RefugePhase.Approaching,"physical outside approach retained");
        Assert(plan.Evaluate(221).Phase==RefugePhase.Sheltered,"cross actual entrance before resting");
        Assert(plan.Evaluate(999).Phase==RefugePhase.Returning && plan.Evaluate(1000).Phase==RefugePhase.Complete,"return by shift");
        var saved=GuardRefugeJourney.CopyValidated(plan);
        Assert(saved.Interior==null && saved.Evaluate(500).Phase==RefugePhase.Sheltered,"save keeps deferred placement");
        saved.MaterializeRest(new[]{new GuardPoint3{X=1,Y=1},new GuardPoint3{X=3,Y=1}},7,500,1000);
        Assert(!saved.RestPending && saved.RestBedRecord==7 && saved.Evaluate(500).Phase==RefugePhase.Sheltered,"entering player materializes rest");
        Assert(saved.Evaluate(500).Position.X==3 && saved.Evaluate(1000).Phase==RefugePhase.Complete,"bed placement keeps next shift");
    }
    private static void LateRestCandidate()
    {
        var rest=GuardRefugeJourney.Create(17,new[]{new GuardPoint3(),new GuardPoint3{X=13}},null,100,1.3,12);
        Assert(rest.ReleaseToReturnBy(1000),"coarse return schedule");
        double starts=rest.StartsAt,release=rest.ReleasedAt.Value;
        rest.MaterializeRest(new[]{new GuardPoint3{X=1},new GuardPoint3{X=3}},7,870,1000);
        Assert(rest.RestPending && rest.StartsAt==starts && rest.ReleasedAt==release && !rest.RestBedRecord.HasValue,
            "a late bed candidate must leave the coarse rest unchanged");
        Assert(rest.Evaluate(1000).Phase==RefugePhase.Complete,"observation cannot delay the existing shift");
        rest.MaterializeRest(new[]{new GuardPoint3{X=1},new GuardPoint3{X=1.5}},8,870,1000);
        Assert(!rest.RestPending && rest.RestBedRecord==8 && rest.Evaluate(870).Phase==RefugePhase.Sheltered,
            "a closer free destination must still be tried");
        Assert(rest.Evaluate(999).Phase==RefugePhase.Returning && rest.Evaluate(1000).Phase==RefugePhase.Complete,
            "closer placement preserves physical return and shift deadline");
        GuardRefugeJourney.CopyValidated(rest);
    }
    private static void ApproachingRestCandidate()
    {
        var rest=GuardRefugeJourney.Create(17,new[]{new GuardPoint3(),new GuardPoint3{X=13}},null,100,1.3,12);
        Assert(rest.ReleaseToReturnBy(350),"approaching coarse return schedule");
        var before=rest.Evaluate(150);double release=rest.ReleasedAt.Value;
        rest.MaterializeRest(new[]{new GuardPoint3{X=1},new GuardPoint3{X=3}},7,150,350);
        Assert(rest.RestPending && rest.StartsAt==100 && rest.ReleasedAt==release,
            "unreachable observed bed must not alter an approaching guard");
        rest.MaterializeRest(new[]{new GuardPoint3{X=1},new GuardPoint3{X=1.5}},8,150,350);
        var after=rest.Evaluate(150);
        Assert(!rest.RestPending && after.Phase==RefugePhase.Approaching && rest.StartsAt==100 &&
            Math.Abs(before.Position.X-after.Position.X)<.000001,"placement must preserve actual exterior position");
        Assert(Math.Abs(rest.Evaluate(350).Position.X)<.000001 && rest.Evaluate(350.000001).Phase==RefugePhase.Complete,
            "approaching placement must meet its original shift within floating-point clock precision");
        GuardRefugeJourney.CopyValidated(rest);
    }
    private static void WalletPayment()
    {
        var store=new WorldStateStore(9);
        string npc=store.GetOrCreateFamilies(3,new[]{new HomeProfile(17,1,60)}).Households[0].Residents[0].Id;
        Assert(store.InitializeNpcInventory(npc,7,new[]{new NpcInventoryItem {Uid=70001,NativePayload="owned"}}),"inventory");
        Assert(NpcServiceContractTerms.TryCreate("wallet","player",npc,"home",NpcServiceRole.Fighter,
            NpcServiceCoverage.Follow,120,false,NpcServiceRiskScope.None,new NpcServiceProtectedTarget[0],
            NpcServiceRestTreatment.RestNotIncluded,NpcServiceResupplyResponsibility.SeparatelyNegotiated,
            new NpcServiceReservedObligation[0],id=>true,id=>true,(id,kind,revision)=>false,
            (id,revision,kind,start,end)=>false,out var terms),"wallet terms");
        var ticket=store.CaptureNpcServiceContractWriteTicket();
        var proof=NpcServiceTransferReceipt.Create("wallet-payment","wallet",NpcServiceTransferKind.Funding,"player",npc,120,0);
        Assert(NpcServiceContractLedger.TryReserve(ticket.SourceLedger,ticket.ExpectedLedgerRevision,terms,proof,out var candidate),"wallet reserve");
        Assert(!store.TryCommitNpcServiceContracts(ticket,candidate,new[]{proof},new NpcServiceRenewalAuthorization[0],out _),"no unpaid ledger bypass");
        var inventory=store.ReadNpcInventory(npc);
        var credit=new NpcInventoryUpdate{PersonId=npc,ExpectedRevision=inventory.Revision,WalletGold=127,Items=inventory.Items};
        Assert(store.TryCommitNpcServiceSettlement(ticket,candidate,credit,new[]{proof},out var error),"payment commit: "+error);
        Assert(store.ReadNpcInventory(npc).WalletGold==127,"wallet credit");
        Assert(!store.TryCommitNpcServiceSettlement(ticket,candidate,credit,new[]{proof},out _),"duplicate fee rejected");
        ticket=store.CaptureNpcServiceContractWriteTicket();
        var current=ticket.SourceLedger.Read("wallet");
        var refund=NpcServiceTransferReceipt.Create("rollback","wallet",NpcServiceTransferKind.Refund,npc,"player",120,1);
        Assert(current.TrySettle(current.Revision,1,NpcServiceEndReason.CancelledBeforeService,npc,null,refund,out var closed),"failed-start rollback");
        Assert(NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,closed,out candidate),"rollback ledger");
        inventory=store.ReadNpcInventory(npc);
        credit=new NpcInventoryUpdate{PersonId=npc,ExpectedRevision=inventory.Revision,WalletGold=7,Items=inventory.Items};
        credit.ExpectedRevision--;
        Assert(!store.TryCommitNpcServiceSettlement(ticket,candidate,credit,new[]{refund},out _),"stale rollback rejected");
        Assert(store.ReadNpcInventory(npc).WalletGold==127 && store.ReadNpcServiceContracts().Read("wallet").Status==NpcServiceContractStatus.Reserved,"rollback rejection atomic");
        credit.ExpectedRevision++;
        Assert(store.TryCommitNpcServiceSettlement(ticket,candidate,credit,new[]{refund},out error),"rollback commit: "+error);
        Assert(store.ReadNpcInventory(npc).WalletGold==7,"rollback exact wallet");
        var restored=new WorldStateStore(9);restored.Restore(store.Snapshot());
        Assert(restored.ReadNpcInventory(npc).WalletGold==7 && restored.ReadNpcServiceContracts().Read("wallet").RefundedGold==120,"current save roundtrip");
    }

    private static void DirectPayment()
    {
        var ledger=NpcServiceContractLedger.Create();
        Assert(NpcServiceContractTerms.TryCreate("day","player","npc","home",NpcServiceRole.Fighter,
            NpcServiceCoverage.Follow,120,false,NpcServiceRiskScope.None,new NpcServiceProtectedTarget[0],
            NpcServiceRestTreatment.RestNotIncluded,NpcServiceResupplyResponsibility.SeparatelyNegotiated,
            new NpcServiceReservedObligation[0],id=>id=="player"||id=="npc",id=>id=="home",
            (id,kind,revision)=>false,(id,revision,kind,start,end)=>false,out var terms),"terms");
        var paid=NpcServiceTransferReceipt.Create("payment","day",NpcServiceTransferKind.Funding,"player","npc",120,0);
        Assert(NpcServiceContractLedger.TryReserve(ledger,ledger.Revision,terms,paid,out var reserved),
            "direct player-to-NPC payment must reserve service");
        var contract=reserved.Read("day");
        Assert(contract.EscrowGold==0,"no escrow account");
        Assert(contract.TryStart(contract.Revision,1,out var started),"start");
        Assert(started.TrySuspend(started.Revision,400,out var paused),"pause");
        Assert(paused.TryResume(paused.Revision,500,out var resumed),"resume");
        Assert(resumed.TrySettle(resumed.Revision,600,NpcServiceEndReason.PlayerDismissal,"npc",null,null,out var ended),
            "dismissal needs no second payment");
        Assert(ended.TransferReceipts.Count==1 && ended.ReleasedGold==120 && ended.RefundedGold==0,"one full day payment");
        Assert(!ended.TrySettle(ended.Revision,600,NpcServiceEndReason.PlayerDismissal,"npc",null,null,out _),"duplicate termination");
        Assert(!started.TryRenew(started.Revision,started.BlockEndAt,paid,null,null,out _),"no automatic renewal");
    }
    private static void Assert(bool value,string text) {if(!value)throw new InvalidOperationException(text);}
}
