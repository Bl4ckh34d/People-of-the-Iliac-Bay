using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcActionContractTests
    {
        private static bool OwnHomeDeposit(Resident resident,int settlement,Household home,int containerBuilding,
            bool houseContainer=true,bool playerOwned=false,bool shop=false,bool inn=false,
            bool factionOwned=false,bool rentableRoom=false,bool publicInventory=false,bool customDrop=false,bool corpse=false,bool questAction=false)
        {
            var type=typeof(NpcActionContract).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcContainerAccessPolicy");
            var method=type==null?null:type.GetMethod("AllowsOwnPermanentHomeDeposit");
            if(method==null)return false;
            return (bool)method.Invoke(null,new object[]{resident,settlement,home,containerBuilding,houseContainer,
                playerOwned,shop,inn,factionOwned,rentableRoom,publicInventory,customDrop,corpse,questAction});
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            NpcActionExecutorTests.Add(cases);
            NpcTaskTests.Add(cases);
            cases.Add(("Confirmed NPC approach records reciprocal factual memories once across restore",()=>{
                var store=new WorldStateStore(71);
                var residents=store.GetOrCreate(1,new[]{42},2).Households[0].Residents;
                Check.True(residents.Count>=2);
                string source=residents[0].Id,target=residents[1].Id,reason;
                var job=new NpcActionJob {Id="interact-receipt",ProposalId="interact-receipt",ResidentId=source,
                    Kind=NpcActionKind.Interact,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId=target},
                    Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=10,UpdatedAt=10,DeadlineAt=100,
                    LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",X=0,Z=0}};
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason),reason);
                var accepted=store.ReadNpcAction(job.Id);
                Check.True(store.ObserveNpcAction(job.Id,accepted.Revision,11,NpcActionObservation.Arrival()));
                accepted=store.ReadNpcAction(job.Id);
                Check.True(store.ObserveNpcAction(job.Id,accepted.Revision,12,NpcActionObservation.Success(job.Id+":observed")));
                accepted=store.ReadNpcAction(job.Id);
                var physical=NpcInteractionPhysicalEvidence.Create(job.Id,accepted.Revision,source,target,12,
                    "settlement:1",0,0,0,"settlement:1",0.5,0,0,true);
                Check.True(store.TryRecordNpcInteractionMemory(job.Id,accepted.Revision,12,"date","settlement 1",
                    physical,out var completed));
                Check.Equal(NpcActionPhase.Completed,completed.Phase);
                var snapshot=store.Snapshot();
                var first=snapshot.ConversationMemories.Find(j=>j.PersonId==source).Entries;
                var second=snapshot.ConversationMemories.Find(j=>j.PersonId==target).Entries;
                Check.Equal(1,first.Count);Check.Equal(1,second.Count);
                Check.Equal("observed",first[0].Source);Check.Equal("observed",second[0].Source);
                Check.Equal(job.Id,first[0].SourceActionId);Check.Equal(job.Id,second[0].SourceActionId);
                Check.Equal("",first[0].PlayerText);Check.Equal("",first[0].NpcText);
                Check.Equal("",second[0].PlayerText);Check.Equal("",second[0].NpcText);
                Check.True(first[0].Observation.Contains(target) && first[0].Observation.Contains("No spoken exchange was observed."));
                Check.True(second[0].Observation.Contains(source) && second[0].Observation.Contains("No spoken exchange was observed."));
                if(!string.IsNullOrWhiteSpace(residents[1].FullName))Check.True(first[0].Observation.Contains(residents[1].FullName));
                if(!string.IsNullOrWhiteSpace(residents[0].FullName))Check.True(second[0].Observation.Contains(residents[0].FullName));
                var json=JsonSerializer.Serialize(snapshot,new JsonSerializerOptions {IncludeFields=true});
                var restored=new WorldStateStore(0);restored.Restore(JsonSerializer.Deserialize<WorldState>(json,
                    new JsonSerializerOptions {IncludeFields=true}));
                var saved=restored.ReadNpcAction(job.Id);
                Check.True(saved.InteractionMemoryRecorded);
                Check.True(!restored.TryRecordNpcInteractionMemory(job.Id,saved.Revision,12,"date","settlement 1",out _));
                var after=restored.Snapshot().ConversationMemories;
                Check.Equal(1,after.Find(j=>j.PersonId==source).Entries.Count);
                Check.Equal(1,after.Find(j=>j.PersonId==target).Entries.Count);
            }));
            cases.Add(("NPC interaction memory requires confirmed person-target receipt",()=>{
                var store=new WorldStateStore(72);var resident=store.GetOrCreate(1,new[]{42},1).Households[0].Residents[0];
                var job=new NpcActionJob {Id="not-interact",ProposalId="not-interact",ResidentId=resident.Id,
                    Kind=NpcActionKind.Wait,Target=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",X=0,Z=0},
                    Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=10,UpdatedAt=10,DeadlineAt=100,
                    LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",X=0,Z=0}};
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason),reason);
                var current=store.ReadNpcAction(job.Id);
                Check.True(store.ObserveNpcAction(job.Id,current.Revision,11,NpcActionObservation.Arrival()));
                current=store.ReadNpcAction(job.Id);
                Check.True(!store.TryRecordNpcInteractionMemory(job.Id,current.Revision,12,"date","settlement 1",out _));
                Check.Equal(0,store.Snapshot().ConversationMemories.Count);
            }));
            cases.Add(("NPC interaction memory requires known live unprotected identities",()=>{
                bool Refused(bool unknownTarget,bool sourceDead,bool sourceQuest,bool targetDead,bool targetQuest)
                {
                    var store=new WorldStateStore(73);
                    var households=store.GetOrCreate(1,new[]{42,43},2).Households;
                    var source=households[0].Residents[0];var target=households[1].Residents[0];string reason;
                    var job=new NpcActionJob {Id="interact-identity",ProposalId="interact-identity",ResidentId=source.Id,
                        Kind=NpcActionKind.Interact,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,
                            PersonId=unknownTarget?"resident:missing":target.Id},
                        Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=10,UpdatedAt=10,DeadlineAt=100,
                        LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",X=0,Z=0}};
                    if(!ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason))return false;
                    var accepted=store.ReadNpcAction(job.Id);
                    if(!store.ObserveNpcAction(job.Id,accepted.Revision,11,NpcActionObservation.Arrival()))return false;
                    accepted=store.ReadNpcAction(job.Id);
                    if(!store.ObserveNpcAction(job.Id,accepted.Revision,12,NpcActionObservation.Success(job.Id+":observed")))return false;
                    source.IsAlive=!sourceDead;source.IsQuestProtected=sourceQuest;
                    target.IsAlive=!targetDead;target.IsQuestProtected=targetQuest;
                    accepted=store.ReadNpcAction(job.Id);
                    var physical=NpcInteractionPhysicalEvidence.Create(job.Id,accepted.Revision,source.Id,target.Id,12,
                        "settlement:1",0,0,0,"settlement:1",0.5,0,0,true);
                    bool recorded=store.TryRecordNpcInteractionMemory(job.Id,accepted.Revision,12,"date","settlement 1",
                        physical,out _);
                    return !recorded && store.Snapshot().ConversationMemories.Count==0;
                }
                Check.True(Refused(true,false,false,false,false),"unknown recipient");
                Check.True(Refused(false,true,false,false,false),"dead initiator");
                Check.True(Refused(false,false,true,false,false),"protected initiator");
                Check.True(Refused(false,false,false,true,false),"dead recipient");
                Check.True(Refused(false,false,false,false,true),"protected recipient");
            }));
            cases.Add(("NPC actions cannot complete from prose acceptance or an unconfirmed attempt",()=>{
                Check.True(!NpcActionContract.CanTransition(NpcActionPhase.Proposed,NpcActionPhase.Completed,true));
                Check.True(!NpcActionContract.CanTransition(NpcActionPhase.Executing,NpcActionPhase.Completed,false));
                Check.True(NpcActionContract.CanTransition(NpcActionPhase.Executing,NpcActionPhase.Completed,true));
                Check.True(!NpcActionContract.CanTransition(NpcActionPhase.Completed,NpcActionPhase.Executing,true));
                Check.True(NpcActionContract.CanTransition(NpcActionPhase.Approaching,NpcActionPhase.Failed,false));
            }));
            cases.Add(("NPC action eligibility requires adapter consent availability and native ownership",()=>{
                string reason;
                Check.True(!NpcActionContract.CanAccept(true,false,false,true,true,out reason));
                Check.True(!NpcActionContract.CanAccept(true,false,true,false,true,out reason));
                Check.True(!NpcActionContract.CanAccept(true,true,true,true,true,out reason));
                Check.True(!NpcActionContract.CanAccept(true,false,true,true,false,out reason));
                Check.True(NpcActionContract.CanAccept(true,false,true,true,true,out reason));
            }));
            cases.Add(("NPC action targets retain stable identity and reject incomplete spatial targets",()=>{
                var person=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="npc:one"};
                NpcActionContract.ValidateTarget(person);
                Check.Throws<ArgumentException>(()=>NpcActionContract.ValidateTarget(new NpcActionTarget {Kind=NpcActionTargetKind.Person}));
                Check.Throws<ArgumentException>(()=>NpcActionContract.ValidateTarget(new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=-1}));
                Check.Throws<ArgumentException>(()=>NpcActionContract.ValidateTarget(new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=1,X=double.NaN}));
                NpcActionContract.ValidateTarget(new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=42,X=1,Z=2});
            }));
            cases.Add(("NPC container deposit requires stable container identity and an interior approach",()=>{
                var deposit=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=42,
                    X=1,Y=0,Z=2,ContainerId="987",ItemId="1234",Quantity=1,TransferDirection=(NpcActionTransferDirection)2};
                NpcActionContract.ValidateTarget(deposit);
                var noContainer=NpcActionExecutor.CopyTarget(deposit);noContainer.ContainerId=null;
                Check.Throws<ArgumentException>(()=>NpcActionContract.ValidateTarget(noContainer));
                var noBuilding=NpcActionExecutor.CopyTarget(deposit);noBuilding.BuildingKey=-1;
                Check.Throws<ArgumentException>(()=>NpcActionContract.ValidateTarget(noBuilding));
                var noPoint=NpcActionExecutor.CopyTarget(deposit);noPoint.X=double.NaN;
                Check.Throws<ArgumentException>(()=>NpcActionContract.ValidateTarget(noPoint));
                var wrongSpace=NpcActionExecutor.CopyTarget(deposit);wrongSpace.Kind=NpcActionTargetKind.WorldPoint;wrongSpace.WorldSpaceId="settlement:1";
                Check.Throws<ArgumentException>(()=>NpcActionContract.ValidateTarget(wrongSpace));
                NpcActionExecutor.Validate(new NpcActionJob {Id="deposit",ProposalId="deposit",ResidentId="resident",Kind=NpcActionKind.Transfer,
                    Target=deposit,Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=10,UpdatedAt=10,DeadlineAt=100,
                    LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",X=0,Z=0}});
            }));
            cases.Add(("NPC home retrieval direction persists as an indoor transfer into own inventory",()=>{
                var retrieval=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=42,
                    X=1,Y=0,Z=2,ContainerId="987",ItemId="1234",Quantity=3,TransferDirection=(NpcActionTransferDirection)3};
                NpcActionContract.ValidateTarget(retrieval);
                var store=new WorldStateStore(5);var resident=store.GetOrCreate(1,new[]{42},1).Households[0].Residents[0];string reason;
                var job=new NpcActionJob {Id="saved-retrieval",ProposalId="saved-retrieval",ResidentId=resident.Id,Kind=NpcActionKind.Transfer,
                    Target=retrieval,Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=10,UpdatedAt=10,DeadlineAt=100,
                    ExpectedInventoryRevision=0,ExpectedTargetInventoryRevision=4,
                    LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",X=0,Z=0}};
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason),reason);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());var saved=restored.ReadNpcAction(job.Id);
                Check.Equal((NpcActionTransferDirection)3,saved.Target.TransferDirection);
                Check.Equal("987",saved.Target.ContainerId);Check.Equal("1234",saved.Target.ItemId);Check.Equal(3,saved.Target.Quantity);
                Check.Equal(4L,saved.ExpectedTargetInventoryRevision);
                var wrongSource=NpcActionExecutor.CopyTarget(retrieval);wrongSource.Kind=NpcActionTargetKind.Person;wrongSource.PersonId="player";
                Check.Throws<ArgumentException>(()=>NpcActionContract.ValidateTarget(wrongSource));
                var wrongRevision=NpcActionExecutor.Copy(saved);wrongRevision.ExpectedInventoryRevision=1;
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(wrongRevision));
            }));
            cases.Add(("Semantic quantity binding preserves native item and direction: Home-container retrieval request names exact ordinary item and storage source",()=>{
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
            cases.Add(("NPC container access is limited to its own permanent private home",()=>{
                var resident=new Resident {Id="store-owner",HomeKey=42,IsAlive=true};
                var household=new Household {BuildingKey=42,Residents=new List<Resident>{resident}};
                bool restoredTransientHouseOwned=false;
                Check.True(!restoredTransientHouseOwned,"DFU does not serialize the houseOwned UI marker.");
                Check.True(OwnHomeDeposit(resident,1,household,42));
                Check.True(!OwnHomeDeposit(resident,1,household,43));
                Check.True(!OwnHomeDeposit(new Resident {Id="other",HomeKey=42,IsAlive=true},1,household,42));
                Check.True(!OwnHomeDeposit(resident,1,household,42,shop:true));
                Check.True(!OwnHomeDeposit(resident,1,household,42,playerOwned:true));
                Check.True(!OwnHomeDeposit(resident,1,household,42,customDrop:true));
                Check.True(!OwnHomeDeposit(resident,1,household,42,corpse:true));
                Check.True(!OwnHomeDeposit(resident,1,household,42,questAction:true));
                Check.True(NpcContainerAccessPolicy.AllowsOwnPermanentHomeContainerAccess(resident,1,household,42,
                    true,false,false,false,false,false,false,false,false,false));
                Check.True(!NpcContainerAccessPolicy.AllowsOwnPermanentHomeContainerAccess(new Resident {Id="other",HomeKey=42,IsAlive=true},
                    1,household,42,true,false,false,false,false,false,false,false,false,false));
            }));
            cases.Add(("Native home-container stock preserves same-day depletion and existing payload",()=>{
                Check.True(NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,0,0,4200101));
                Check.True(!NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,0,4200101,4200101),
                    "An empty container stocked today must not be refilled again today.");
                Check.True(NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,0,4200101,4200102),
                    "An untouched empty native chest may still receive its next-day stock.");
                Check.True(!NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,1,0,4200101),
                    "Nonempty unstocked payload must never be cleared by native stock generation.");
                Check.True(!NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,1,4200001,4200101),
                    "A stale native stock date must never permit clearing existing home contents.");
                Check.True(NpcHomeContainerLifecycle.ShouldProtectExistingContents(1,4200001,4200101),
                    "NPC access must persist a stale nonempty chest before PlayerActivate can clear it.");
                Check.True(NpcHomeContainerLifecycle.ShouldProtectExistingContents(1,0,4200101),
                    "NPC access must persist opaque zero-date contents.");
                Check.True(!NpcHomeContainerLifecycle.ShouldProtectExistingContents(0,4200001,4200101),
                    "An empty chest has no contents to protect.");
                Check.True(NpcHomeContainerLifecycle.ShouldProtectExistingContents(1,4200101,4200101),
                    "Stock exposed to an NPC today must survive tomorrow's native reset.");
                Check.True(NpcHomeContainerLifecycle.ShouldProtectExistingContents(1,4200200,4200101),
                    "Future-dated contents exposed to an NPC must remain stable when the clock catches up.");
                Check.True(!NpcHomeContainerLifecycle.ShouldProtectExistingContents(1,int.MaxValue,4200101),
                    "Persistent storage is already protected.");
                Check.True(!NpcHomeContainerLifecycle.ShouldNativeStock(true,true,true,false,false,false,0,0,4200101));
                Check.True(!NpcHomeContainerLifecycle.ShouldNativeStock(false,true,false,false,false,false,0,0,4200101));
                int usedDate=NpcHomeContainerLifecycle.MarkUsed(4200101,4200101);
                Check.Equal(NpcHomeContainerLifecycle.PersistentStorageStockedDate,usedDate);
                Check.Equal(usedDate,NpcHomeContainerLifecycle.MarkUsed(0,4200101));
                Check.Equal(usedDate,NpcHomeContainerLifecycle.MarkUsed(4200200,4200101));
                Check.True(!NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,1,usedDate,4200102),
                    "Taking one item must not clear remaining contents the next day.");
                Check.True(!NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,0,usedDate,4200102),
                    "Depleting a used chest must not grant fresh free stock on later days.");
            }));
            cases.Add(("Successful NPC home deposits remain stored across native next-day restock and save restore",()=>{
                var mark=typeof(NpcHomeContainerLifecycle).GetMethod("MarkPersistentStorage");
                var isProtected=typeof(NpcHomeContainerLifecycle).GetMethod("IsPersistentStorage");
                var restore=typeof(NpcHomeContainerLifecycle).GetMethod("RestoreStockDateAfterFailedDeposit");
                Check.True(mark!=null && isProtected!=null && restore!=null,"Deposit-specific persistent storage lifecycle API is required.");
                int protectedDate=(int)mark.Invoke(null,null);
                Check.Equal(int.MaxValue,protectedDate);
                int restoredDate=protectedDate; // DaggerfallLoot.stockedDate is serialized as an integer.
                Check.True((bool)isProtected.Invoke(null,new object[]{restoredDate}));
                Check.True(!NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,1,restoredDate,4200102),
                    "The next-day PlayerActivate condition must not invoke the clearing stocker for NPC storage.");
                Check.True(!NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,0,restoredDate,4200103),
                    "An emptied storage chest must not be refilled on later days.");
                Check.True(NpcHomeContainerLifecycle.ShouldNativeStock(true,true,false,false,false,false,0,0,4200102),
                    "Untouched native home chests must keep DFU's first-open stock policy.");
                Check.Equal(4200101,(int)restore.Invoke(null,new object[]{4200101,protectedDate,false}));
                Check.Equal(int.MaxValue,(int)restore.Invoke(null,new object[]{4200101,protectedDate,true}));
            }));
            cases.Add(("Home-container dialogue candidates do not authorize stocking or mutation",()=>{
                    var target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,
                        ContainerId="42",ItemId="77",Quantity=1,TransferDirection=NpcActionTransferDirection.HomeContainerToNpc};
                    var option=new NpcActionOption {Id=2,Kind=NpcActionKind.Transfer,Target=target};
                    var decision=NpcActionChat.ParseSemantic("DIRECT|Transfer|2|1|300",new[]{option});
                    Check.True(decision!=null);decision.Option.Target.Quantity=5;
                    Check.Equal(1,target.Quantity);
                }));
            cases.Add(("Home retrieval feedback describes household storage without claiming personal ownership",()=>{
                var job=new NpcActionJob {Kind=NpcActionKind.Transfer,Phase=NpcActionPhase.Completed,CompletedEffectId="retrieve:observed",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,ContainerId="4242",ItemId="99",Quantity=2,
                        TransferDirection=(NpcActionTransferDirection)3}};
                string result=NpcActionFeedback.Describe(job,"Mara","Iron Arrows",null);
                Check.True(result.Contains("household storage") && result.Contains("retrieves 2 x Iron Arrows"),result);
                Check.True(!result.Contains("your Iron Arrows") && !result.Contains("personal"),result);
            }));
            cases.Add(("Home retrieval awareness identifies household contents as the source",()=>{
                var job=new NpcActionJob {Kind=NpcActionKind.Transfer,Phase=NpcActionPhase.Accepted,
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,ContainerId="4242",ItemId="99",Quantity=2,
                        TransferDirection=(NpcActionTransferDirection)3}};
                string context=ConversationActionAwareness.Format(job,null,"inside own home","household storage",null);
                Check.True(context.Contains("from household storage") && !context.Contains("from the NPC's belongings"),context);
            }));
            cases.Add(("Confirmed NPC container deposit reports a saved home-container transfer",()=>{
                var job=new NpcActionJob {Kind=NpcActionKind.Transfer,Phase=NpcActionPhase.Completed,CompletedEffectId="deposit:transfer",
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,ContainerId="4242",ItemId="99",Quantity=2,
                        TransferDirection=NpcActionTransferDirection.NpcToContainer}};
                string result=NpcActionFeedback.Describe(job,"Mara","arrows",null);
                Check.True(result.Contains("stores 2 x arrows") && result.Contains("home container 4242"),result);
            }));
            cases.Add(("Accepted home-container deposit retains its exact target through save restore",()=>{
                var store=new WorldStateStore(5);var resident=store.GetOrCreate(1,new[]{42},1).Households[0].Residents[0];string reason;
                var job=new NpcActionJob {Id="saved-deposit",ProposalId="saved-deposit",ResidentId=resident.Id,Kind=NpcActionKind.Transfer,
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=42,X=1,Y=0,Z=2,
                        ContainerId="987",ItemId="1234",Quantity=3,TransferDirection=NpcActionTransferDirection.NpcToContainer},
                    Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=10,UpdatedAt=10,DeadlineAt=100,
                    LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",X=0,Z=0}};
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason),reason);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());var saved=restored.ReadNpcAction(job.Id);
                Check.Equal(NpcActionTransferDirection.NpcToContainer,saved.Target.TransferDirection);
                Check.Equal("987",saved.Target.ContainerId);Check.Equal("1234",saved.Target.ItemId);Check.Equal(3,saved.Target.Quantity);
            }));
        }
    }
}
