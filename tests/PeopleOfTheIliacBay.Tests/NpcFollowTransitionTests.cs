using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcFollowTransitionTests
    {
        private static NpcActionTarget Outside(double x=0) => new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=100,WorldSpaceId="settlement:100",X=x};
        private static NpcActionTarget Inside(int building=10) => new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=building};
        private static NpcActionTarget Dungeon(double x=0) => new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=100,WorldSpaceId="dungeon:100",X=x};
        private static WorldStateStore Fixture(out NpcActionJob job)
        {
            var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);
            job=new NpcActionJob {Id="follow:1",ProposalId="turn:1",ResidentId=town.Households[0].Residents[0].Id,
                Kind=NpcActionKind.Follow,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=300,FinishAt=250,LastPosition=Outside()};
            string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason),reason);return store;
        }
        private static bool Call(WorldStateStore store,string name,params object[] args)
        {
            var method=typeof(WorldStateStore).GetMethod(name);
            Check.True(method!=null,"FOLLOW needs persisted, revision-checked doorway transit.");
            try{return (bool)method.Invoke(store,args);}catch(TargetInvocationException e){throw e.InnerException;}
        }
        private static bool Begin(WorldStateStore store,NpcActionJob job,NpcActionTarget target=null,IList<NpcActionTarget> route=null)
        {return Call(store,"TryBeginNpcFollowTransition",job.Id,job.Revision,110d,target??Inside(),route??new List<NpcActionTarget>{Outside(),Outside(10)},2d);}
        private static WorldStateStore PaidFixture(out NpcActionJob job)
        {
            var store=Fixture(out job);const string id="contract:dungeon-follow";NpcServiceContractTerms terms;
            string residentId=job.ResidentId;
            Check.True(NpcServiceContractTerms.TryCreate(id,"player",residentId,"home:100:10",NpcServiceRole.Fighter,
                NpcServiceCoverage.Follow|NpcServiceCoverage.Travel,120,false,NpcServiceRiskScope.None,
                new NpcServiceProtectedTarget[0],NpcServiceRestTreatment.RestNotIncluded,
                NpcServiceResupplyResponsibility.SeparatelyNegotiated,new NpcServiceReservedObligation[0],
                value=>value=="player" || value==residentId,value=>value=="home:100:10",
                (value,category,revision)=>false,(value,revision,kind,start,end)=>false,out terms));
            var funding=NpcServiceTransferReceipt.Create("fund:dungeon-follow",id,NpcServiceTransferKind.Funding,
                "player",residentId,120,100);
            var ticket=store.CaptureNpcServiceContractWriteTicket();NpcServiceContractLedger reserved;string reason;
            Check.True(NpcServiceContractLedger.TryReserve(ticket.SourceLedger,ticket.ExpectedLedgerRevision,terms,funding,out reserved));
            Check.True(NpcDailyPaymentFixture.Commit(store,ticket,reserved,funding,out reason));
            Check.True(store.TryBindPaidFollowAction(job.Id,job.Revision,id,out reason));job=store.ReadNpcAction(job.Id);return store;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Dungeon Follow does not arrive across a short disconnected gap", () => {
                Check.True(!NpcFollowArrival.CanHold(1.2, false, 0));
            }));
            cases.Add(("Dungeon Follow does not stop through a wall with a long walking detour", () => {
                Check.True(!NpcFollowArrival.CanHold(1.2, true, 4.5));
            }));
            cases.Add(("Dungeon Follow holds after an actual short walking connection", () => {
                Check.True(NpcFollowArrival.CanHold(1.2, true, 1.4));
                Check.True(NpcFollowArrival.CanHold(1.8, true, 1.8));
                Check.True(!NpcFollowArrival.CanHold(2, true, 2));
                Check.True(!NpcFollowArrival.CanHold(1.2, true, double.NaN));
                Check.True(!NpcFollowArrival.CanHold(double.NaN, true, 1.2));
                Check.True(!NpcFollowArrival.CanHold(1.2, true, -1));
            }));
            cases.Add(("FOLLOW dungeon paid expiry cannot resume its old exterior owner",()=>{
                NpcActionJob job;var store=PaidFixture(out job);string contract=NpcPaidCompanionAction.ContractId(job);
                Check.True(store.TryDeferPaidTravel(job.Id,job.Revision,105));job=store.ReadNpcAction(job.Id);
                Check.True(Begin(store,job,Dungeon()));job=store.ReadNpcAction(job.Id);
                Check.True(store.TryResumePaidTravel(job.Id,job.Revision,110));job=store.ReadNpcAction(job.Id);
                Check.True(Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Dungeon()));job=store.ReadNpcAction(job.Id);
                Check.True(store.ObserveNpcAction(job.Id,job.Revision,job.FinishAt,NpcActionObservation.Interrupted("Agreed time ended.")));
                job=store.ReadNpcAction(job.Id);string reason;
                Check.True(store.TrySuspendPaidFollowSettlement(job.Id,job.Revision,out reason));
                long revision=store.ReadNpcServiceContracts().Read(contract).Revision;
                Check.True(store.TrySuspendPaidFollowSettlement(job.Id,job.Revision,out reason));
                Check.Equal(revision,store.ReadNpcServiceContracts().Read(contract).Revision);
                Check.True(!store.TryDeferPaidTravel(job.Id,job.Revision,job.FinishAt));
                Check.True(!store.TryResumePaidTravel(job.Id,job.Revision,job.FinishAt));
            }));
            cases.Add(("FOLLOW dungeon door releases exterior handoff without changing paid contract or double suspension",()=>{
                NpcActionJob job;var store=PaidFixture(out job);string contract=NpcPaidCompanionAction.ContractId(job);
                Check.True(store.TryDeferPaidTravel(job.Id,job.Revision,105));job=store.ReadNpcAction(job.Id);
                Check.True(Begin(store,job,Dungeon()));job=store.ReadNpcAction(job.Id);
                Check.True(store.TryResumePaidTravel(job.Id,job.Revision,110));job=store.ReadNpcAction(job.Id);
                Check.Equal(contract,NpcPaidCompanionAction.ContractId(job));Check.True(job.PaidTravelHandoff==null);
                Check.Equal(NpcServiceContractStatus.Active,store.ReadNpcServiceContracts().Read(contract).Status);
                Check.True(Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Dungeon()));job=store.ReadNpcAction(job.Id);
                Check.True(store.ObserveNpcAction(job.Id,job.Revision,140,NpcActionObservation.Cancelled("Stop.")));
                job=store.ReadNpcAction(job.Id);string reason;
                Check.True(store.TrySuspendPaidFollowSettlement(job.Id,job.Revision,out reason));
                long revision=store.ReadNpcServiceContracts().Read(contract).Revision;
                Check.True(store.TrySuspendPaidFollowSettlement(job.Id,job.Revision,out reason));
                Check.Equal(revision,store.ReadNpcServiceContracts().Read(contract).Revision);
                store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,150d,Dungeon()));
                Check.True(!store.TryDeferPaidTravel(job.Id,job.Revision,150));
            }));
            cases.Add(("FOLLOW native dungeon round trip preserves identity and inventory",()=>{
                NpcActionJob job;var store=Fixture(out job);
                Check.True(store.InitializeNpcInventory(job.ResidentId,17,new[]{new NpcInventoryItem {Uid=77,NativePayload="native item"}}));
                Check.True(Begin(store,job,Dungeon()));job=store.ReadNpcAction(job.Id);
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,129d,Dungeon()));
                store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
                Check.True(Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Dungeon()));job=store.ReadNpcAction(job.Id);
                Check.True(Call(store,"TryBeginNpcFollowTransition",job.Id,job.Revision,140d,Outside(10),new List<NpcActionTarget>{Dungeon(),Dungeon(10)},2d));
                job=store.ReadNpcAction(job.Id);
                Check.True(Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,160d,Outside(10)));
                var arrived=store.ReadNpcAction(job.Id);Check.Equal(job.ResidentId,arrived.ResidentId);
                Check.Equal(250d,arrived.FinishAt);Check.Equal(300d,arrived.DeadlineAt);
                Check.Equal(77ul,store.ReadNpcInventory(job.ResidentId).Items[0].Uid);
                Check.Equal(17,store.ReadNpcInventory(job.ResidentId).WalletGold);
            }));
            cases.Add(("FOLLOW dungeon handoff refuses mismatched map and cancellation survives restore",()=>{
                NpcActionJob job;var store=Fixture(out job);var wrong=Dungeon();wrong.WorldSpaceId="dungeon:101";
                Check.True(!Begin(store,job,wrong));wrong=Dungeon();wrong.SettlementId=101;Check.True(!Begin(store,job,wrong));
                Check.True(Begin(store,job,Dungeon()));job=store.ReadNpcAction(job.Id);
                Check.True(store.ObserveNpcAction(job.Id,job.Revision,120,NpcActionObservation.Cancelled("Stop following.")));
                store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
                Check.Equal(NpcActionPhase.Cancelled,job.Phase);Check.Near(5,job.LastPosition.X);
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Dungeon()));
            }));
            cases.Add(("FOLLOW doorway arrival waits for full verified route and keeps action identity",()=>{
                NpcActionJob job;var store=Fixture(out job);Check.True(Begin(store,job));
                var pending=store.ReadNpcAction(job.Id);Check.Equal(NpcActionPhase.Approaching,pending.Phase);
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,pending.Revision,129d,Inside()));
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,pending.Revision,130d,Inside(11)));
                Check.True(Call(store,"TryArriveNpcFollowTransition",job.Id,pending.Revision,130d,Inside()));
                var arrived=store.ReadNpcAction(job.Id);Check.Equal(job.ResidentId,arrived.ResidentId);Check.Equal("player",arrived.Target.PersonId);
                Check.Equal(NpcActionTargetKind.IndoorPoint,arrived.LastPosition.Kind);Check.Equal(10,arrived.LastPosition.BuildingKey);
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,arrived.Revision,131d,Inside()),"Consumed transit cannot duplicate a body.");
            }));
            cases.Add(("FOLLOW snapshot restore retains transit and detached route data",()=>{
                NpcActionJob job;var store=Fixture(out job);var route=new List<NpcActionTarget>{Outside(),Outside(10)};
                Check.True(Begin(store,job,route:route));route[1].X=999;
                var snapshot=store.Snapshot();var restored=new WorldStateStore(1);restored.Restore(snapshot);
                var pending=restored.ReadNpcAction(job.Id);
                Check.True(Call(restored,"TryArriveNpcFollowTransition",job.Id,pending.Revision,130d,Inside()));
                Check.Equal(NpcActionTargetKind.WorldPoint,store.ReadNpcAction(job.Id).LastPosition.Kind);
            }));
            cases.Add(("FOLLOW stop during doorway transit prevents arrival after restore",()=>{
                NpcActionJob job;var store=Fixture(out job);Check.True(Begin(store,job));job=store.ReadNpcAction(job.Id);
                Check.True(store.ObserveNpcAction(job.Id,job.Revision,120,NpcActionObservation.Cancelled("Stop following.")));
                store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
                Check.Equal(NpcActionPhase.Cancelled,job.Phase);Check.Near(5,job.LastPosition.X);
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Inside()));
            }));
            cases.Add(("FOLLOW refuses foreign spaces inaccessible routes and expired consent",()=>{
                NpcActionJob job;var store=Fixture(out job);var foreign=Inside();foreign.SettlementId=101;
                Check.True(!Begin(store,job,foreign));Check.True(!Begin(store,job,route:new List<NpcActionTarget>{Outside()}));
                Check.True(!Begin(store,job,Outside(10)));Check.True(!Begin(store,job,route:new List<NpcActionTarget>{Outside(3),Outside(10)}));
                Check.True(Begin(store,job));job=store.ReadNpcAction(job.Id);
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,250d,Inside()));
                Check.True(store.ObserveNpcAction(job.Id,job.Revision,300,NpcActionObservation.Interrupted("Deadline.")));
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,store.ReadNpcAction(job.Id).Revision,301d,Inside()));
            }));
            cases.Add(("FOLLOW failed native transition resumes source without moving ahead",()=>{
                NpcActionJob job;var store=Fixture(out job);Check.True(Begin(store,job));job=store.ReadNpcAction(job.Id);
                Check.True(Call(store,"TryResumeNpcFollowSource",job.Id,job.Revision,110d));
                job=store.ReadNpcAction(job.Id);Check.Near(0,job.LastPosition.X);Check.Equal(NpcActionPhase.Approaching,job.Phase);
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Inside()));
            }));
            cases.Add(("FOLLOW source return resumes at elapsed route position and stale revisions fail",()=>{
                NpcActionJob job;var store=Fixture(out job);Check.True(Begin(store,job));
                Check.True(!Call(store,"TryResumeNpcFollowSource",job.Id,job.Revision,120d));job=store.ReadNpcAction(job.Id);
                Check.True(Call(store,"TryResumeNpcFollowSource",job.Id,job.Revision,120d));Check.Near(5,store.ReadNpcAction(job.Id).LastPosition.X);
            }));
            cases.Add(("FOLLOW round trip keeps the same inventory and original consent deadline",()=>{
                NpcActionJob job;var store=Fixture(out job);
                Check.True(store.InitializeNpcInventory(job.ResidentId,17,new[]{new NpcInventoryItem {Uid=77,NativePayload="opaque native item"}}));
                Check.True(Begin(store,job));job=store.ReadNpcAction(job.Id);
                Check.True(Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Inside()));job=store.ReadNpcAction(job.Id);
                var end=Inside();end.X=10;
                Check.True(Call(store,"TryBeginNpcFollowTransition",job.Id,job.Revision,140d,Outside(10),new List<NpcActionTarget>{Inside(),end},2d));
                store.Restore(store.Snapshot());job=store.ReadNpcAction(job.Id);
                Check.True(Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,160d,Outside(10)));
                job=store.ReadNpcAction(job.Id);Check.Equal(250d,job.FinishAt);Check.Equal(300d,job.DeadlineAt);
                var inventory=store.ReadNpcInventory(job.ResidentId);Check.Equal(17,inventory.WalletGold);Check.Equal(77ul,inventory.Items[0].Uid);
                Check.Equal("opaque native item",inventory.Items[0].NativePayload);
            }));
            cases.Add(("FOLLOW position capture cannot overwrite pending route ownership",()=>{
                NpcActionJob job;var store=Fixture(out job);Check.True(Begin(store,job));job=store.ReadNpcAction(job.Id);
                Check.True(!store.SaveNpcActionPosition(job.Id,job.Revision,Inside(),false,120));
                Check.True(!store.SaveNpcActionPosition(job.Id,job.Revision,Outside(9),false,120));
                Check.True(Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Inside()));
            }));
            cases.Add(("FOLLOW dead or quest protected resident cannot cross doorway",()=>{
                NpcActionJob job;var store=Fixture(out job);Check.True(Begin(store,job));job=store.ReadNpcAction(job.Id);
                var resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];resident.IsQuestProtected=true;
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Inside()));
                resident.IsQuestProtected=false;resident.IsAlive=false;
                Check.True(!Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Inside()));
            }));
            cases.Add(("FOLLOW route length and point budgets reject unbounded transition work",()=>{
                NpcActionJob job;var store=Fixture(out job);
                Check.True(!Begin(store,job,route:new List<NpcActionTarget>{Outside(),Outside(65)}));
                var points=new List<NpcActionTarget>();for(int i=0;i<129;i++)points.Add(Outside());
                Check.True(!Begin(store,job,route:points));Check.Equal(1L,store.ReadNpcAction(job.Id).Revision);
            }));
            cases.Add(("FOLLOW ordinary arrival observation cannot bypass a pending doorway route",()=>{
                NpcActionJob job;var store=Fixture(out job);Check.True(Begin(store,job));job=store.ReadNpcAction(job.Id);
                Check.True(!store.ObserveNpcAction(job.Id,job.Revision,111,NpcActionObservation.Arrival()));
                Check.True(!store.ObserveNpcAction(job.Id,job.Revision,111,NpcActionObservation.Success("premature")));
                Check.Equal(job.Revision,store.ReadNpcAction(job.Id).Revision);
                Check.True(Call(store,"TryArriveNpcFollowTransition",job.Id,job.Revision,130d,Inside()));
            }));
        }
    }
}
