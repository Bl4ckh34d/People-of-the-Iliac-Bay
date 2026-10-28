using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NativeNpcCompanionExperienceTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Successful bound Follow use awards only its native skill", BoundFollowAwardsNativeSkill));
            cases.Add(("Ordinary resident actions and invalid native skills gain no companion experience", UnboundOrInvalidUseDoesNotAward));
            cases.Add(("Cancelled Follow returning to routine grants no companion experience", CancelledFollowReturnDoesNotAward));
            cases.Add(("Expired Follow grants no companion experience", ExpiredFollowDoesNotAward));
            cases.Add(("Follow return allowance is not companion service time", FinishedFollowDoesNotAward));
            cases.Add(("Cancelled paid Follow cannot award before settlement", CancelledPaidFollowDoesNotAward));
            cases.Add(("Paid Follow expiry cannot use the physical return allowance", ExpiredPaidFollowDoesNotAward));
            cases.Add(("Suspended paid Follow cannot award through ordinary Follow eligibility", SuspendedPaidFollowDoesNotAward));
            cases.Add(("Paid companion awards reject timestamps older than contract state", StalePaidFollowDoesNotAward));
            cases.Add(("Explicit adventurer assistance earns successful-use experience", AdventurerAssistanceAwards));
            cases.Add(("Ordinary household and traveler assistance keep seeded skills", OrdinaryAssistanceDoesNotAward));
            cases.Add(("Adventurer assistance requires a current live action", EndedAdventurerAssistanceDoesNotAward));
            cases.Add(("Follow-only paid service cannot bypass combat coverage through adventurer identity", PaidCoverageDoesNotFallBack));
        }

        private static void BoundFollowAwardsNativeSkill()
        {
            string resident;var store=CreateStore(out resident);
            var follow=Job(resident,NpcActionKind.Follow,100);
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,follow,true,true,true,out reason),reason);
            int before=store.ReadNpcActor(resident,store.ResolveNpcArchetype(resident)).Skill(20),gain;
            Check.True(NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,20,101,out gain));
            Check.Equal(store.ReadNpcActor(resident,store.ResolveNpcArchetype(resident)).Skill(20)-before,gain);
            var progress=store.ReadNpcProgress(resident);
            Check.Equal(1,progress.Skills.Count);
            Check.Equal(20,progress.Skills[0].Skill);
            Check.Equal(1L,progress.Skills[0].Experience);
        }

        private static void UnboundOrInvalidUseDoesNotAward()
        {
            string resident;var store=CreateStore(out resident);
            var wait=Job(resident,NpcActionKind.Wait,100);
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,wait,true,true,true,out reason),reason);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,20,101));
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,-1,101));
            Check.Equal(null,store.ReadNpcProgress(resident));
        }

        private static void CancelledFollowReturnDoesNotAward()
        {
            string resident;var store=CreateStore(out resident);
            var follow=Job(resident,NpcActionKind.Follow,100);
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,follow,true,true,true,out reason),reason);
            Check.True(store.ObserveNpcAction(follow.Id,1,101,NpcActionObservation.Cancelled("Stop requested.")),"Follow cancellation failed.");
            Check.True(store.SaveNpcActionPosition(follow.Id,2,ReturnPosition(),true,102),"Follow return position save failed.");
            var returning=store.ActiveNpcAction(resident);
            Check.True(returning!=null && returning.ReturningToRoutine && NpcActionExecutor.IsTerminal(returning),"Terminal Follow return did not remain action-index active.");
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,20,102));
            Check.Equal(null,store.ReadNpcProgress(resident));
        }

        private static void ExpiredFollowDoesNotAward()
        {
            string resident;var store=CreateStore(out resident);
            var follow=Job(resident,NpcActionKind.Follow,100);
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,follow,true,true,true,out reason),reason);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,20,200));
            Check.Equal(null,store.ReadNpcProgress(resident));
        }

        private static void FinishedFollowDoesNotAward()
        {
            string resident;var store=CreateStore(out resident);
            var follow=Job(resident,NpcActionKind.Follow,100);follow.FinishAt=150;
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,follow,true,true,true,out reason),reason);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,20,150));
            Check.Equal(null,store.ReadNpcProgress(resident));
        }

        private static void CancelledPaidFollowDoesNotAward()
        {
            NpcActionJob follow;var store=PaidFollow(out follow);
            Check.True(store.ObserveNpcAction(follow.Id,follow.Revision,101,NpcActionObservation.Cancelled("Dismissed.")));
            Check.Equal(NpcServiceContractStatus.Active,store.ReadNpcServiceContracts().Read(NpcPaidCompanionAction.ContractId(follow)).Status);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,follow.ResidentId,20,102));
            Check.Equal(null,store.ReadNpcProgress(follow.ResidentId));
        }

        private static void ExpiredPaidFollowDoesNotAward()
        {
            NpcActionJob follow;var store=PaidFollow(out follow);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,follow.ResidentId,20,follow.FinishAt));
            Check.Equal(null,store.ReadNpcProgress(follow.ResidentId));
        }

        private static void SuspendedPaidFollowDoesNotAward()
        {
            NpcActionJob follow;var store=PaidFollow(out follow);
            var ticket=store.CaptureNpcServiceContractWriteTicket();
            var contract=ticket.SourceLedger.Read(NpcPaidCompanionAction.ContractId(follow));
            NpcServiceContract suspended;NpcServiceContractLedger ledger;string reason;
            Check.True(contract.TrySuspend(contract.Revision,110,out suspended));
            Check.True(NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,suspended,out ledger));
            Check.True(store.TryCommitNpcServiceContracts(ticket,ledger,new NpcServiceTransferReceipt[0],new NpcServiceRenewalAuthorization[0],out reason),reason);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,follow.ResidentId,20,110));
            Check.Equal(null,store.ReadNpcProgress(follow.ResidentId));
        }

        private static void StalePaidFollowDoesNotAward()
        {
            NpcActionJob follow;var store=PaidFollow(out follow);
            var ticket=store.CaptureNpcServiceContractWriteTicket();
            var contract=ticket.SourceLedger.Read(NpcPaidCompanionAction.ContractId(follow));
            NpcServiceContract accrued;NpcServiceContractLedger ledger;string reason;
            Check.True(contract.TrySuspend(contract.Revision,105,out accrued));
            Check.True(NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,accrued,out ledger));
            Check.True(store.TryCommitNpcServiceContracts(ticket,ledger,new NpcServiceTransferReceipt[0],new NpcServiceRenewalAuthorization[0],out reason),reason);
            ticket=store.CaptureNpcServiceContractWriteTicket();
            contract=ticket.SourceLedger.Read(NpcPaidCompanionAction.ContractId(follow));
            Check.True(contract.TryResume(contract.Revision,110,out accrued));
            Check.True(NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,accrued,out ledger));
            Check.True(store.TryCommitNpcServiceContracts(ticket,ledger,new NpcServiceTransferReceipt[0],new NpcServiceRenewalAuthorization[0],out reason),reason);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,follow.ResidentId,20,109));
            Check.True(NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,follow.ResidentId,20,110));
            Check.Equal(1L,store.ReadNpcProgress(follow.ResidentId).Skills[0].Experience);
        }

        private static WorldStateStore PaidFollow(out NpcActionJob follow)
        {
            string resident;var store=CreateStore(out resident);
            follow=Job(resident,NpcActionKind.Follow,100);
            const string contractId="contract:skill-use";NpcServiceContractTerms terms;
            Check.True(NpcServiceContractTerms.TryCreate(contractId,"player",resident,"home:100:10",
                NpcServiceRole.Fighter,NpcServiceCoverage.Follow,120,false,NpcServiceRiskScope.None,
                new NpcServiceProtectedTarget[0],NpcServiceRestTreatment.RestNotIncluded,
                NpcServiceResupplyResponsibility.SeparatelyNegotiated,new NpcServiceReservedObligation[0],
                id=>id=="player" || id==resident,id=>id=="home:100:10",
                (id,category,revision)=>false,(id,revision,kind,start,end)=>false,out terms));
            var funding=NpcServiceTransferReceipt.Create("fund:skill-use",contractId,NpcServiceTransferKind.Funding,
                "player",resident,120,100);
            var ticket=store.CaptureNpcServiceContractWriteTicket();NpcServiceContractLedger reserved;string reason;
            Check.True(NpcServiceContractLedger.TryReserve(ticket.SourceLedger,ticket.ExpectedLedgerRevision,terms,funding,out reserved));
            Check.True(NpcDailyPaymentFixture.Commit(store,ticket,reserved,funding,out reason),reason);
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,follow,true,true,true,out reason),reason);
            Check.True(store.TryBindPaidFollowAction(follow.Id,follow.Revision,contractId,out reason),reason);
            follow=store.ReadNpcAction(follow.Id);return store;
        }

        private static void AdventurerAssistanceAwards()
        {
            foreach(var kind in new[]{NpcActionKind.Attack,NpcActionKind.Defend,NpcActionKind.CastSpell})
            {
                NpcActionJob job;var store=AssistanceFixture(kind,true,true,out job);
                Check.True(NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,job.ResidentId,29,102));
                Check.Equal(1L,store.ReadNpcProgress(job.ResidentId).Skills[0].Experience);
            }
        }

        private static void OrdinaryAssistanceDoesNotAward()
        {
            foreach(bool traveler in new[]{false,true})
                foreach(var kind in new[]{NpcActionKind.Attack,NpcActionKind.Defend,NpcActionKind.CastSpell})
                {
                    NpcActionJob job;var store=AssistanceFixture(kind,traveler,false,out job);
                    Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,job.ResidentId,29,102));
                    Check.Equal(null,store.ReadNpcProgress(job.ResidentId));
                }
        }

        private static void EndedAdventurerAssistanceDoesNotAward()
        {
            foreach(string end in new[]{"cancelled","expired","dead"})
            {
                NpcActionJob job;var store=AssistanceFixture(NpcActionKind.Attack,true,true,out job);
                if(end=="cancelled")Check.True(store.ObserveNpcAction(job.Id,job.Revision,102,NpcActionObservation.Cancelled("Stop.")));
                if(end=="dead")Check.True(store.KillTraveler(job.ResidentId,102));
                Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,job.ResidentId,29,end=="expired"?200:102));
                Check.Equal(null,store.ReadNpcProgress(job.ResidentId));
            }
        }

        private static void PaidCoverageDoesNotFallBack()
        {
            NpcActionJob job;var store=AssistanceFixture(NpcActionKind.Attack,true,true,out job);
            const string id="contract:uncovered-combat";NpcServiceContractTerms terms;
            string resident=job.ResidentId;
            Check.True(NpcServiceContractTerms.TryCreate(id,"player",resident,"home:100:10",NpcServiceRole.Fighter,
                NpcServiceCoverage.Follow,120,false,NpcServiceRiskScope.None,new NpcServiceProtectedTarget[0],
                NpcServiceRestTreatment.RestNotIncluded,NpcServiceResupplyResponsibility.SeparatelyNegotiated,
                new NpcServiceReservedObligation[0],value=>value=="player" || value==resident,value=>value=="home:100:10",
                (value,category,revision)=>false,(value,revision,kind,start,end)=>false,out terms));
            var ticket=store.CaptureNpcServiceContractWriteTicket();NpcServiceContractLedger ledger;string reason;
            var funding=NpcServiceTransferReceipt.Create("fund:uncovered-combat",id,NpcServiceTransferKind.Funding,
                "player",resident,120,100);
            Check.True(NpcServiceContractLedger.TryReserve(ticket.SourceLedger,ticket.ExpectedLedgerRevision,terms,funding,out ledger));
            Check.True(NpcDailyPaymentFixture.Commit(store,ticket,ledger,funding,out reason),reason);
            ticket=store.CaptureNpcServiceContractWriteTicket();NpcServiceContract started;
            var contract=ticket.SourceLedger.Read(id);
            Check.True(contract.TryStart(contract.Revision,100,out started));
            Check.True(NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,started,out ledger));
            Check.True(store.TryCommitNpcServiceContracts(ticket,ledger,new NpcServiceTransferReceipt[0],new NpcServiceRenewalAuthorization[0],out reason),reason);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,job.ResidentId,29,102));
            Check.Equal(null,store.ReadNpcProgress(job.ResidentId));
        }

        internal static WorldStateStore AssistanceFixture(NpcActionKind kind,bool traveler,bool adventurer,out NpcActionJob job)
        {
            string resident;var store=CreateStore(out resident);
            if(traveler)
            {
                uint seed=1;
                while(TravelerAdventurerPolicy.IsAdventurer(TravelerAdventurerPolicy.Archetype(seed))!=adventurer)seed++;
                resident="traveler:skill-use";
                store.RegisterTraveler(new TravelerPerson {Id=resident,Name="Skill-use traveler",HomeSettlement=2,AppearanceSeed=seed});
                store.RegisterTravelerVisit(new TravelerVisit {Id="visit:skill-use",PersonId=resident,SettlementId=100,TavernKey=10,
                    DesiredArrival=43200,Nights=1,CommuteSeconds=600,
                    Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=30}}});
            }
            job=Job(resident,kind,100);
            if(kind==NpcActionKind.Attack || kind==NpcActionKind.Defend)job.Target.PersonId="native:1";
            if(kind==NpcActionKind.CastSpell)job.CompanionCast=new NpcCompanionCastSpec {SpellId="native:spellbook:0:fixture",
                EffectId="HealHealth",TargetId="player",EffectKind=NpcMagicEffectKind.Health,
                Delivery=NpcCompanionCastDelivery.PlayerTouch,MagickaCost=5};
            string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason),reason);
            Check.True(store.ObserveNpcAction(job.Id,job.Revision,101,NpcActionObservation.Arrival()));
            job=store.ReadNpcAction(job.Id);return store;
        }

        private static WorldStateStore CreateStore(out string resident)
        {
            var store=new WorldStateStore(42);
            resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
            return store;
        }

        private static NpcActionJob Job(string resident,NpcActionKind kind,double at)
        {
            return new NpcActionJob {Id="companion-experience:"+kind,ProposalId="companion-experience:"+kind,
                ResidentId=resident,Kind=kind,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=at,UpdatedAt=at,DeadlineAt=at+100};
        }

        private static NpcActionTarget ReturnPosition()
        {return new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=100,WorldSpaceId="settlement:100"};}
    }
}
