using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcActionExecutorTests
    {
        private static NpcActionJob Job() => new NpcActionJob {Id="job:1",ProposalId="turn:1",ResidentId="resident:1",
            Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=2,X=3,Z=4},
            Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200};
        private static bool TransferRequest(string text,string item,string recipient,int maximum,out int quantity)
        {
            quantity=0;
            var method=typeof(NpcActionChat).GetMethod("TryParseItemTransferRequest");
            if(method==null) return false;
            object[] args={text,item,recipient,maximum,0};
            bool result=(bool)method.Invoke(null,args);quantity=(int)args[4];return result;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            NpcMurderReportTests.Add(cases);
            cases.Add(("An anonymous murder report is a distinct executable action, not an approach",()=>{
                NpcActionKind report;
                Check.True(Enum.TryParse("ReportMurder",out report),"Missing semantic guard report action.");
                Check.True(NpcTaskContract.Supported(report));
                var job=Job();job.Kind=report;
                job.Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="guard:1",SettlementId=1};
                NpcActionExecutor.Validate(job);
                Check.Equal(report,NpcActionExecutor.Copy(job).Kind);
                Check.True(NpcActionFeedback.Describe(job,"Sara",null,null)==null,
                    "Accepting the request cannot claim delivery.");
            }));
            NpcFollowTransitionTests.Add(cases);
            cases.Add(("Only a bound paid Follow can last one full game day",()=>{
                var ordinary=Job();ordinary.Kind=NpcActionKind.Follow;
                ordinary.Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"};
                ordinary.FinishAt=ordinary.AcceptedAt+86400;
                ordinary.DeadlineAt=ordinary.FinishAt+60;
                bool rejected=false;
                try {NpcActionExecutor.Validate(ordinary);} catch(ArgumentException) {rejected=true;}
                Check.True(rejected,"Ordinary Follow must keep the existing duration limit.");
                ordinary.ServiceCode="paid-follow:contract:one";
                NpcActionExecutor.Validate(ordinary);
                ordinary.Kind=NpcActionKind.Wait;
                rejected=false;
                try {NpcActionExecutor.Validate(ordinary);} catch(ArgumentException) {rejected=true;}
                Check.True(rejected,"Paid Follow marker must not extend another action kind.");
            }));
            cases.Add(("Funded active service binds only its own accepted Follow",()=>{
                var store=new WorldStateStore(42);
                var resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                const string contractId="contract:paid-test";
                NpcServiceContractTerms terms;
                Check.True(NpcServiceContractTerms.TryCreate(contractId,"player",resident.Id,"home:100:10",
                    NpcServiceRole.Fighter,NpcServiceCoverage.Follow,120,false,NpcServiceRiskScope.None,
                    new NpcServiceProtectedTarget[0],NpcServiceRestTreatment.RestNotIncluded,
                    NpcServiceResupplyResponsibility.SeparatelyNegotiated,new NpcServiceReservedObligation[0],
                    id=>id=="player" || id==resident.Id,id=>id=="home:100:10",
                    (id,category,revision)=>false,(id,revision,kind,start,end)=>false,out terms));
                var funding=NpcServiceTransferReceipt.Create("fund:paid-test",contractId,
                    NpcServiceTransferKind.Funding,"player","contract:"+contractId+":escrow",120,100);
                var ticket=store.CaptureNpcServiceContractWriteTicket();
                NpcServiceContractLedger reserved;
                Check.True(NpcServiceContractLedger.TryReserve(ticket.SourceLedger,ticket.ExpectedLedgerRevision,
                    terms,funding,out reserved));
                string error;
                Check.True(store.TryCommitNpcServiceContracts(ticket,reserved,new[]{funding},
                    new NpcServiceRenewalAuthorization[0],out error),error);
                var action=Job();action.ResidentId=resident.Id;action.Kind=NpcActionKind.Follow;
                action.Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"};
                action.FinishAt=200;action.DeadlineAt=260;
                string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,action,true,true,true,out reason),reason);
                Check.True(!store.TryBindPaidFollowAction(action.Id,1,contractId,out reason),
                    "Reserved contract cannot extend Follow.");
                ticket=store.CaptureNpcServiceContractWriteTicket();
                NpcServiceContract started;
                Check.True(ticket.SourceLedger.Read(contractId).TryStart(1,100,out started));
                NpcServiceContractLedger active;
                Check.True(NpcServiceContractLedger.TryReplace(ticket.SourceLedger,ticket.ExpectedLedgerRevision,
                    started,out active));
                Check.True(store.TryCommitNpcServiceContracts(ticket,active,new NpcServiceTransferReceipt[0],
                    new NpcServiceRenewalAuthorization[0],out error),error);
                Check.True(store.TryBindPaidFollowAction(action.Id,1,contractId,out reason),reason);
                var bound=store.ReadNpcAction(action.Id);
                Check.Equal(86500d,bound.FinishAt);
                Check.Equal(86560d,bound.DeadlineAt);
                Check.Equal(2L,bound.Revision);
                Check.True(!store.TryBindPaidFollowAction(action.Id,1,contractId,out reason),
                    "Binding cannot repeat or replace an existing paid action.");
                var restored=new WorldStateStore(9);restored.Restore(store.Snapshot());
                Check.Equal(bound.ServiceCode,restored.ReadNpcAction(action.Id).ServiceCode);
                Check.Equal(contractId,NpcPaidCompanionAction.ContractId(restored.ReadNpcAction(action.Id)));
                Check.Equal(NpcServiceContractStatus.Active,
                    restored.ReadNpcServiceContracts().Read(contractId).Status);
                Check.True(store.ObserveNpcAction(action.Id,bound.Revision,101,NpcActionObservation.Arrival()));
                var executing=store.ReadNpcAction(action.Id);
                Check.True(!store.TrySuspendPaidFollowSettlement(executing.Id,executing.Revision,out reason),
                    "Live paid Follow cannot stop its own billing through the settlement seam.");
                Check.True(store.ObserveNpcAction(action.Id,executing.Revision,43300,
                    NpcActionObservation.Cancelled("The player dismissed the companion.")));
                var dismissed=store.ReadNpcAction(action.Id);
                Check.Equal(contractId,NpcPaidCompanionAction.ContractId(dismissed));
                Check.True(!store.TrySuspendPaidFollowSettlement(dismissed.Id,dismissed.Revision-1,out reason),
                    "Stale action evidence cannot stop billing.");
                Check.True(store.TrySuspendPaidFollowSettlement(dismissed.Id,dismissed.Revision,out reason),reason);
                var activeContract=store.ReadNpcServiceContracts().Read(contractId);
                Check.Equal(NpcServiceContractStatus.Suspended,activeContract.Status);
                Check.Equal(43300d,activeContract.LastServiceUpdateAt);
                Check.Equal(60,activeContract.EarnedGold);
                Check.True(store.TrySuspendPaidFollowSettlement(dismissed.Id,dismissed.Revision,out reason),
                    "Settlement suspension must be idempotent after a native payout refusal.");
                Check.True(store.SaveNpcActionPosition(dismissed.Id,dismissed.Revision,dismissed.LastPosition,false,44000));
                var retryStore=new WorldStateStore(1);retryStore.Restore(store.Snapshot());
                Check.Equal(1,retryStore.PendingPaidFollowSettlements().Count);
                var retryJob=retryStore.ReadNpcAction(dismissed.Id);
                Check.True(retryStore.TrySuspendPaidFollowSettlement(retryJob.Id,retryJob.Revision,out reason),reason);
                activeContract=retryStore.ReadNpcServiceContracts().Read(contractId);
                Check.Equal(43300d,activeContract.LastServiceUpdateAt);
                var wage=NpcServiceTransferReceipt.Create("wage:paid-test",contractId,
                    NpcServiceTransferKind.Earnings,"contract:"+contractId+":escrow",resident.Id,60,90000);
                var refund=NpcServiceTransferReceipt.Create("refund:paid-test",contractId,
                    NpcServiceTransferKind.Refund,"contract:"+contractId+":escrow","player",60,90000);
                NpcServiceContract terminated;
                Check.True(activeContract.TrySettle(activeContract.Revision,90000,
                    NpcServiceEndReason.NaturalExpiry,resident.Id,wage,refund,out terminated));
                Check.Equal(NpcServiceContractStatus.Terminated,terminated.Status);
                Check.Equal(NpcServiceEndReason.NaturalExpiry,terminated.EndReason.Value);
                Check.Equal(60,terminated.EarnedGold);
                Check.Equal(60,terminated.RefundedGold);
            }));
            cases.Add(("Terminal action memory follows the native result, actor, time, and save branch",()=>{
                var store=new WorldStateStore(42);
                var residents=store.GetOrCreate(100,new[]{10},2).Households[0].Residents;
                var job=Job();job.ResidentId=residents[0].Id;
                string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason),reason);
                Check.True(!store.TryRememberNpcActionOutcome(job.Id,1,"date","market","reached the market"));
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()));
                Check.True(store.ObserveNpcAction(job.Id,2,102,NpcActionObservation.Success(job.Id+":observed")));
                Check.True(!store.TryRememberNpcActionOutcome(job.Id,2,"date","market","reached the market"));
                Check.True(store.TryRememberNpcActionOutcome(job.Id,3,"date","market","physically reached the agreed market stall."));
                Check.True(!store.TryRememberNpcActionOutcome(job.Id,3,"date","market","duplicate"));
                var entries=store.Snapshot().ConversationMemories.Find(m=>m.PersonId==job.ResidentId).Entries;
                Check.Equal(1,entries.Count);
                Check.Equal(102d,entries[0].At);
                Check.Equal(job.Id,entries[0].SourceActionId);
                Check.True(entries[0].Observation.Contains(job.ResidentId));
                Check.True(entries[0].Observation.Contains("completed the accepted MoveTo action"));
                Check.True(store.RecallConversation(job.ResidentId,"market stall",103).Contains("market stall"));
                NpcMemoryReadSnapshot ownRead;
                Check.True(store.TryCaptureNpcMemoryRead(job.ResidentId,"market stall",103,2000,out ownRead));
                Check.True(ownRead.Text.Contains("market stall"));
                Check.True(store.IsNpcMemoryReadCurrent(ownRead,job.ResidentId,103));
                Check.True(!store.RecallConversation(residents[1].Id,"market stall",103).Contains("market stall"));
                var restored=new WorldStateStore(9);restored.Restore(store.Snapshot());
                Check.True(!restored.TryRememberNpcActionOutcome(job.Id,3,"date","market","duplicate after restore"));
                Check.Equal(1,restored.Snapshot().ConversationMemories.Find(m=>m.PersonId==job.ResidentId).Entries.Count);
            }));
            cases.Add(("Failed and stopped actions retain their actual distinct outcomes",()=>{
                var store=new WorldStateStore(42);
                var resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
                var failed=Job();failed.ResidentId=resident.Id;
                string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,failed,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(failed.Id,1,105,NpcActionObservation.Interrupted("The walking route remained blocked.")));
                Check.True(store.TryRememberNpcActionOutcome(failed.Id,2,"date","road",null));
                var cancelled=Job();cancelled.Id="job:stopped";cancelled.ProposalId="turn:stopped";
                cancelled.ResidentId=resident.Id;cancelled.AcceptedAt=cancelled.UpdatedAt=110;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,cancelled,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(cancelled.Id,1,111,NpcActionObservation.Cancelled("The player asked me to stop.")));
                Check.True(store.TryRememberNpcActionOutcome(cancelled.Id,2,"date","road",null));
                var entries=store.Snapshot().ConversationMemories.Find(m=>m.PersonId==resident.Id).Entries;
                Check.Equal(2,entries.Count);
                Check.Equal(105d,entries[0].At);Check.Equal(111d,entries[1].At);
                Check.True(entries[0].Observation.Contains("could not finish"));
                Check.True(entries[0].Observation.Contains("walking route remained blocked"));
                Check.True(entries[1].Observation.Contains("stopped"));
                Check.True(entries[1].Observation.Contains("player asked me to stop"));
                Check.True(!entries[1].Observation.Contains("could not finish"));
                Check.True(store.RecallConversation(resident.Id,"walking route",112).Contains("walking route"));
            }));
            cases.Add(("Action feedback describes only confirmed results and actual transfer quantity",()=>{
                var describe=typeof(NpcActionJob).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcActionFeedback")?.GetMethod("Describe");
                Check.True(describe!=null,"Confirmed NPC actions need player-facing outcome feedback.");
                var job=Job();job.Kind=NpcActionKind.Transfer;
                job.Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",Quantity=3};
                Func<string> message=()=> (string)describe.Invoke(null,new object[]{job,"Mara","Arrow","Celeste"});
                Check.True(message()==null,"An accepted promise is not a completed handover.");
                job.Phase=NpcActionPhase.Completed;
                Check.True(message()==null,"Completion without observed effect cannot claim success.");
                job.CompletedEffectId="transfer:1";
                Check.Equal("Mara hands you 3 x Arrow.",message());
                job.Target.PersonId="resident:2";
                Check.Equal("Mara hands Celeste 3 x Arrow.",message());
                job.Target.TransferDirection=NpcActionTransferDirection.PlayerToNpc;job.Target.PersonId="player";
                Check.Equal("Mara accepts 3 x Arrow from you.",message());
                job.Phase=NpcActionPhase.Failed;
                Check.True(!message().Contains("hands"),"Failed handover must never claim the item moved.");
                job.Phase=NpcActionPhase.Cancelled;
                Check.True(!message().Contains("hands"));
            }));
            cases.Add(("Action feedback keeps departure and interaction claims within observed scope",()=>{
                var describe=typeof(NpcActionJob).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcActionFeedback")?.GetMethod("Describe");
                Check.True(describe!=null);
                var job=Job();job.Phase=NpcActionPhase.Completed;job.CompletedEffectId="observed:1";
                job.Kind=NpcActionKind.ChangeSchedule;
                Check.Equal("Mara leaves to head home.",(string)describe.Invoke(null,new object[]{job,"Mara",null,null}));
                job.Kind=NpcActionKind.Interact;
                Check.Equal("Mara reaches the person you indicated.",(string)describe.Invoke(null,new object[]{job,"Mara",null,null}));
                job.Kind=NpcActionKind.Attack;
                Check.True(describe.Invoke(null,new object[]{job,"Mara",null,null})==null,"Unsupported adapters must not manufacture results.");
            }));
            cases.Add(("Semantic quantity binding preserves native item and direction: NPC item transfer binds exact recipient item and positive partial quantity",()=>{
                    var target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId="77",Quantity=1,
                        TransferDirection=NpcActionTransferDirection.NpcToCounterparty};
                    var option=new NpcActionOption {Id=2,Kind=NpcActionKind.Transfer,Target=target,MaximumQuantity=4};
                    var decision=NpcActionChat.ParseSemantic("DIRECT|Transfer|2|3|300",new[]{option});
                    Check.True(decision!=null);Check.Equal(3,decision.Option.Target.Quantity);Check.Equal("77",decision.Option.Target.ItemId);
                    Check.Equal(target.TransferDirection,decision.Option.Target.TransferDirection);
                    Check.True(NpcActionChat.ParseSemantic("DIRECT|Transfer|2|5|300",new[]{option})==null);
                    Check.True(NpcActionChat.ParseSemantic("REPORTED",new[]{option})==null);
                    Check.Equal(1,target.Quantity);
                }));
            cases.Add(("Semantic quantity binding preserves native item and direction: Player item offers require exact owned item and exact positive requested quantity",()=>{
                    var target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId="77",Quantity=1,
                        TransferDirection=NpcActionTransferDirection.PlayerToNpc};
                    var option=new NpcActionOption {Id=2,Kind=NpcActionKind.Transfer,Target=target,MaximumQuantity=4};
                    var decision=NpcActionChat.ParseSemantic("DIRECT|Transfer|2|3|300",new[]{option});
                    Check.True(decision!=null);Check.Equal(3,decision.Option.Target.Quantity);Check.Equal("77",decision.Option.Target.ItemId);
                    Check.Equal(target.TransferDirection,decision.Option.Target.TransferDirection);
                    Check.True(NpcActionChat.ParseSemantic("DIRECT|Transfer|2|5|300",new[]{option})==null);
                    Check.True(NpcActionChat.ParseSemantic("REPORTED",new[]{option})==null);
                    Check.Equal(1,target.Quantity);
                }));
            cases.Add(("NPC transfer target keeps exact quantity and recipient inventory revision",()=>{
                var target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="recipient:1",ItemId="77"};
                var quantity=typeof(NpcActionTarget).GetField("Quantity");
                Check.True(quantity!=null,"Transfer target needs persisted quantity with a legacy default of one.");
                Check.Equal(1,(int)quantity.GetValue(new NpcActionTarget()));
                var direction=typeof(NpcActionTarget).GetField("TransferDirection");
                Check.True(direction!=null,"Transfer target needs explicit backward-compatible direction.");
                Check.Equal(0,Convert.ToInt32(direction.GetValue(new NpcActionTarget())));
                quantity.SetValue(target,3);
                direction.SetValue(target,1);
                target.PersonId="player";
                var job=Job();job.Kind=NpcActionKind.Transfer;job.Target=target;
                job.ExpectedInventoryRevision=4;
                var recipientRevision=typeof(NpcActionJob).GetField("ExpectedTargetInventoryRevision");
                Check.True(recipientRevision!=null,"Action needs expected recipient inventory revision.");
                recipientRevision.SetValue(job,0L);
                NpcActionExecutor.Validate(job);
                var copy=NpcActionExecutor.Copy(job);
                Check.Equal(3,(int)quantity.GetValue(copy.Target));
                Check.Equal(1,Convert.ToInt32(direction.GetValue(copy.Target)));
                Check.Equal(4L,job.ExpectedInventoryRevision);
                Check.Equal(0L,(long)recipientRevision.GetValue(copy));
                Check.True(!ReferenceEquals(target,copy.Target));
                recipientRevision.SetValue(job,1L);
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(job));
                recipientRevision.SetValue(job,0L);
                direction.SetValue(target,99);
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(job));
                direction.SetValue(target,1);
                target.ItemId="not-a-native-uid";
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(job));
                target.ItemId="77";
                quantity.SetValue(target,0);
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(job));
            }));
            cases.Add(("Timed action cannot report completion before its accepted finish",()=>{
                var job=Job();job.Kind=NpcActionKind.Wait;job.FinishAt=150;NpcActionJob updated;
                Check.True(NpcActionExecutor.Advance(job,1,101,NpcActionObservation.Arrival(),out job));
                Check.True(!NpcActionExecutor.Advance(job,2,149,NpcActionObservation.Success("waited"),out updated));
                Check.True(NpcActionExecutor.Advance(job,2,150,NpcActionObservation.Success("waited"),out updated));
                Check.Equal(NpcActionPhase.Completed,updated.Phase);
            }));
            
            
            cases.Add(("Service effect cannot execute before payment or after actor death",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var resident=town.Households[0].Residents[0];
                var job=Job();job.ResidentId=resident.Id;job.Kind=NpcActionKind.Purchase;job.ServiceCode="tavern-beer";job.ServicePrice=1;
                store.InitializeNpcInventory(resident.Id,10,new NpcInventoryItem[0]);string reason;int effects=0;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason));
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()));
                Check.True(!store.TryCompleteNpcActionEffect(job.Id,2,102,()=>{effects++;return true;}));Check.Equal(0,effects);
                Check.True(store.TryPayNpcActionService(job.Id,2,103));
                Check.True(store.ObserveNpcAction(job.Id,3,104,NpcActionObservation.Arrival()));
                resident.IsAlive=false;
                Check.True(!store.TryCompleteNpcActionEffect(job.Id,4,105,()=>{effects++;return true;}));Check.Equal(0,effects);
            }));
            cases.Add(("Natural conversation proposals require current speaker assent and cannot replay",()=>{
                var options=new List<NpcActionOption>{new NpcActionOption {Id=1,Kind=NpcActionKind.Follow,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},Label="walk with you"}};
                Check.True(NpcActionChat.ParseProposal("PROPOSE|99|60",options)==null);
                Check.True(NpcActionChat.ParseProposal("ACCEPT|1|60",options)==null);
                var decision=NpcActionChat.ParseProposal("PROPOSE|1|60",options);Check.True(decision!=null);
                var offer=new NpcActionOffer("speaker",7,100,decision);NpcActionDecision accepted;
                Check.True(!offer.TryAcceptSemantic("other",7,101,decision,out accepted));
                Check.True(!offer.TryAcceptSemantic("speaker",8,101,decision,out accepted));
                Check.True(!offer.TryAcceptSemantic("speaker",7,101,null,out accepted));
                Check.True(!offer.TryAcceptSemantic("speaker",7,101,null,out accepted));
                Check.True(offer.TryAcceptSemantic("speaker",7,101,decision,out accepted));
                Check.Equal(NpcActionKind.Follow,accepted.Option.Kind);
                Check.True(!offer.TryAcceptSemantic("speaker",7,102,decision,out accepted));
                Check.True(new NpcActionOffer("speaker",7,100,decision).TryAcceptSemantic("speaker",7,221,decision,out accepted));
                Check.True(!new NpcActionOffer("speaker",7,100,decision).TryAcceptSemantic("speaker",7,99,decision,out accepted));
            }));
            
            cases.Add(("Native action effect is validated before execution and cannot repeat across restore",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var job=Job();job.ResidentId=town.Households[0].Residents[0].Id;
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason));int effects=0;
                Func<bool> effect=()=>{effects++;return true;};
                Check.True(!store.TryCompleteNpcActionEffect(job.Id,1,101,effect));Check.Equal(0,effects);
                Check.True(store.ObserveNpcAction(job.Id,1,102,NpcActionObservation.Arrival()));
                Check.True(!store.TryCompleteNpcActionEffect(job.Id,1,103,effect));Check.Equal(0,effects);
                Check.True(!store.TryCompleteNpcActionEffect(job.Id,2,200,effect));Check.Equal(0,effects);
                Check.True(!store.TryCompleteNpcActionEffect(job.Id,2,103,()=>false));Check.Equal(NpcActionPhase.Executing,store.ReadNpcAction(job.Id).Phase);
                Check.True(store.TryCompleteNpcActionEffect(job.Id,2,104,effect));Check.Equal(1,effects);
                store.Restore(store.Snapshot());
                Check.True(!store.TryCompleteNpcActionEffect(job.Id,3,105,effect));Check.Equal(1,effects);
                Check.Equal(NpcActionPhase.Completed,store.ReadNpcAction(job.Id).Phase);
            }));
            cases.Add(("Old terminal position capture cannot evict a newer active action",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var resident=town.Households[0].Residents[0];
                var old=Job();old.ResidentId=resident.Id;string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, old,true,true,true,out reason));
                Check.True(store.ObserveNpcAction(old.Id,1,101,NpcActionObservation.Cancelled("Stop.")));
                var next=Job();next.Id="next";next.ProposalId="next-turn";next.ResidentId=resident.Id;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, next,true,true,true,out reason));
                Check.True(store.SaveNpcActionPosition(old.Id,2,old.Target,false,102));
                Check.Equal(next.Id,store.ActiveNpcAction(resident.Id)?.Id);
                Check.True(!store.SaveNpcActionPosition(old.Id,3,old.Target,true,103));
                store.Restore(store.Snapshot());Check.Equal(next.Id,store.ActiveNpcAction(resident.Id).Id);
            }));
            cases.Add(("Action acceptance persists detached targets and rejects replay without replacing plans",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var resident=town.Households[0].Residents[0];
                var job=Job();job.ResidentId=resident.Id;string reason;
                Check.True(!ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,false,true,out reason));
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason));
                job.Target.X=99;Check.Equal(3d,store.ActiveNpcAction(resident.Id).Target.X);
                Check.True(!store.TryAcceptNpcAction(job,true,true,true,out reason));
                var save=store.Snapshot();store.Restore(save);
                Check.Equal(job.Id,store.ActiveNpcAction(resident.Id).Id);
                save.NpcActions[0].Target.X=100;Check.Equal(3d,store.ActiveNpcAction(resident.Id).Target.X);
                var corrupt=store.Snapshot();corrupt.NpcActions.Add(NpcActionExecutor.Copy(corrupt.NpcActions[0]));
                Check.Throws<ArgumentException>(()=>store.Restore(corrupt));
            }));
            cases.Add(("Action service payment and return stage commit together exactly once",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var resident=town.Households[0].Residents[0];
                store.InitializeNpcInventory(resident.Id,10,new NpcInventoryItem[0]);
                var job=Job();job.ResidentId=resident.Id;job.Kind=NpcActionKind.Purchase;job.ServiceCode="tavern-beer";job.ServicePrice=1;
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason));
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()));
                Check.True(store.TryPayNpcActionService(job.Id,2,102));
                Check.Equal(9,store.ReadNpcInventory(resident.Id).WalletGold);
                Check.Equal(1,store.ReadNpcAction(job.Id).ServiceStep);
                Check.True(!store.TryPayNpcActionService(job.Id,3,103));
                Check.Equal(9,store.ReadNpcInventory(resident.Id).WalletGold);
                var save=store.Snapshot();store.Restore(save);
                Check.Equal(1,store.ReadNpcAction(job.Id).ServiceStep);
                Check.True(!store.TryPayNpcActionService(job.Id,2,103));
            }));
            cases.Add(("Native temple training payment starts one receipt-bound three-hour NPC timer",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var resident=town.Households[0].Residents[0];
                store.InitializeNpcInventory(resident.Id,10,new NpcInventoryItem[0]);
                var job=Job();job.ResidentId=resident.Id;job.Kind=NpcActionKind.Purchase;job.ServiceCode=NpcShopCommerce.TrainingServiceCode;job.ServicePrice=1;
                job.DeadlineAt=12000;job.Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,
                    PersonId="trainer",ContainerId=NpcShopCommerce.TrainingTargetContainerId,ItemId="14",Quantity=1,QuotedGold=1};
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()));
                Check.True(store.TryPayNpcActionService(job.Id,2,102));
                var running=store.ReadNpcAction(job.Id);Check.Equal(1,running.ServiceStep);Check.Equal(NpcActionPhase.Executing,running.Phase);
                Check.Equal(10902d,running.FinishAt);Check.Equal(1L,running.ExpectedInventoryRevision);Check.Equal(9,store.ReadNpcInventory(resident.Id).WalletGold);
                Check.True(!store.TryPayNpcActionService(job.Id,running.Revision,103));Check.Equal(9,store.ReadNpcInventory(resident.Id).WalletGold);
                int effects=0;Check.True(!store.TryCompleteNpcActionEffect(job.Id,running.Revision,103,()=>{effects++;return true;}));Check.Equal(0,effects);
                Check.True(store.TryCompleteNpcActionEffect(job.Id,running.Revision,10902,()=>{effects++;return true;}));Check.Equal(1,effects);
                Check.True(!store.TryCompleteNpcActionEffect(job.Id,running.Revision,10903,()=>{effects++;return true;}));Check.Equal(1,effects);
            }));
            cases.Add(("Action acceptance protects quest ownership and accepted appointment windows",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var resident=town.Households[0].Residents[0];
                var job=Job();job.ResidentId=resident.Id;string reason;
                resident.IsQuestProtected=true;Check.True(!ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason));resident.IsQuestProtected=false;
                Check.True(store.TryReserveAppointment(new AppointmentRecord {Id="meeting",ResidentId=resident.Id,Purpose="meet",StartsAt=150,EndsAt=210,
                    GraceSeconds=30,Importance=1,Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=20}},50,out reason));
                Check.True(!ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason));
                Check.True(reason.Contains("appointment"));Check.Equal(0,store.Snapshot().NpcActions.Count);
            }));
            cases.Add(("Action acceptance protects captured appointment patience beyond meeting end",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var resident=town.Households[0].Residents[0];
                string reason;
                Check.True(store.TryReserveAppointment(new AppointmentRecord {Id="patient-meeting",ResidentId=resident.Id,Purpose="meet",
                    StartsAt=150,EndsAt=210,GraceSeconds=30,PatienceSeconds=9000,Importance=1,
                    Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=20}},50,out reason));
                var job=Job();job.ResidentId=resident.Id;job.AcceptedAt=job.UpdatedAt=300;job.DeadlineAt=1000;
                Check.True(!ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason),"An errand consumed the NPC's captured waiting window.");
                Check.True(reason.Contains("appointment"));Check.Equal(0,store.Snapshot().NpcActions.Count);
            }));
            cases.Add(("Cancelled action retains physical rejoin across save and permits a verified replacement",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var resident=town.Households[0].Residents[0];
                var job=Job();job.ResidentId=resident.Id;string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason));
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Cancelled("Stop requested.")));
                Check.True(store.SaveNpcActionPosition(job.Id,2,job.Target,true,101.5));
                var save=store.Snapshot();store.Restore(save);
                Check.True(store.ActiveNpcAction(resident.Id).ReturningToRoutine);
                var replacement=Job();replacement.Id="job:2";replacement.ProposalId="turn:2";replacement.ResidentId=resident.Id;
                replacement.AcceptedAt=replacement.UpdatedAt=102;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, replacement,true,true,true,out reason));
                Check.Equal(replacement.Id,store.ActiveNpcAction(resident.Id).Id);
                Check.True(!store.ReadNpcAction(job.Id).ReturningToRoutine);
                Check.Equal(2,store.Snapshot().NpcActions.Count);
            }));
            cases.Add(("Service failure leaves wallet and action stage unchanged",()=>{
                var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);var resident=town.Households[0].Residents[0];
                store.InitializeNpcInventory(resident.Id,0,new NpcInventoryItem[0]);
                var job=Job();job.ResidentId=resident.Id;job.Kind=NpcActionKind.Purchase;job.ServiceCode="tavern-beer";job.ServicePrice=1;
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason));
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()));
                Check.True(!store.TryPayNpcActionService(job.Id,2,102));Check.Equal(0,store.ReadNpcInventory(resident.Id).WalletGold);
                Check.Equal(0,store.ReadNpcAction(job.Id).ServiceStep);Check.Equal(2L,store.ReadNpcAction(job.Id).Revision);
                var bad=store.Snapshot();bad.NpcActions[0].ServiceStep=2;Check.Throws<ArgumentException>(()=>store.Restore(bad));
            }));
            cases.Add(("Action executor waits for physical arrival and observed execution",()=>{
                var job=Job();
                Check.True(NpcActionExecutor.Advance(job,1,101,NpcActionObservation.Approach(),out job));
                Check.Equal(NpcActionPhase.Approaching,job.Phase);
                Check.True(NpcActionExecutor.Advance(job,2,102,NpcActionObservation.Arrival(),out job));
                Check.Equal(NpcActionPhase.Executing,job.Phase);
                Check.True(NpcActionExecutor.Advance(job,3,103,NpcActionObservation.Success("job:1:effect"),out job));
                Check.Equal(NpcActionPhase.Completed,job.Phase);
                Check.True(!NpcActionExecutor.Advance(job,4,104,NpcActionObservation.Success("job:1:effect"),out job));
            }));
            cases.Add(("Action executor rejects stale revision and success before arrival",()=>{
                var job=Job();NpcActionJob changed;
                Check.True(!NpcActionExecutor.Advance(job,0,101,NpcActionObservation.Approach(),out changed));
                Check.True(!NpcActionExecutor.Advance(job,1,101,NpcActionObservation.Success("effect"),out changed));
                Check.Equal(NpcActionPhase.Accepted,job.Phase);
            }));
            cases.Add(("Action executor fails danger and timeout instead of finishing",()=>{
                var job=Job();NpcActionJob changed;
                Check.True(NpcActionExecutor.Advance(job,1,201,NpcActionObservation.Arrival(),out changed));
                Check.Equal(NpcActionPhase.Failed,changed.Phase);
                Check.True(changed.FailureReason.Contains("deadline"));
                Check.True(NpcActionExecutor.Advance(job,1,101,NpcActionObservation.Interrupted("Danger interrupted the action."),out changed));
                Check.Equal(NpcActionPhase.Failed,changed.Phase);
            }));
            cases.Add(("Action executor deep copies stable targets and rejects invalid persisted phases",()=>{
                var job=Job();var copy=NpcActionExecutor.Copy(job);copy.Target.X=40;
                Check.Equal(3d,job.Target.X);
                job.Phase=NpcActionPhase.Completed;
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(job));
                job.CompletedEffectId="effect";NpcActionExecutor.Validate(job);
            }));
            
        }
    }
}
