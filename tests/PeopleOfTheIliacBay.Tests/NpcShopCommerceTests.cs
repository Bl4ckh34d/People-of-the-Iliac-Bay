using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcShopCommerceTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Shop buyer quotes use seeded profile and survive actor recreation and save restore",()=>{
                var store=new WorldStateStore(42);string id=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
                int quote,expected,recreated;
                var view=store.ReadNpcActor(id,NpcArchetype.Citizen);
                Check.True(NpcShopCommerce.TryPrice(1000,1,12,view.Skill(14),view.Profile.Attribute(5),false,out expected));
                Check.True(NpcShopCommerce.TryPriceForBuyer(store,id,1000,1,12,false,out quote));Check.Equal(expected,quote);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.True(NpcShopCommerce.TryPriceForBuyer(restored,id,1000,1,12,false,out recreated));Check.Equal(quote,recreated);
                Check.Equal(0,restored.Snapshot().NpcActors.Count);
            }));
            cases.Add(("Shop quotes honor pinned archetype and earned mercantile instead of a frozen quote",()=>{
                var store=new WorldStateStore(55);string id=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
                Check.True(store.AwardNpcSkillExperience(id,NpcArchetype.Merchant,0,1,0));
                int before,after,expected;var view=store.ReadNpcActor(id,NpcArchetype.Merchant);
                Check.True(NpcShopCommerce.TryPrice(1000,1,12,view.Skill(14),view.Profile.Attribute(5),false,out expected));
                Check.True(NpcShopCommerce.TryPriceForBuyer(store,id,1000,1,12,false,out before));Check.Equal(expected,before);
                Check.True(store.AwardNpcSkillExperience(id,NpcArchetype.Merchant,14,100000,1));
                Check.True(NpcShopCommerce.TryPriceForBuyer(store,id,1000,1,12,false,out after));Check.True(after<before);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());int copied;
                Check.True(NpcShopCommerce.TryPriceForBuyer(restored,id,1000,1,12,false,out copied));Check.Equal(after,copied);
            }));
            cases.Add(("Person approach falls back from blocked radial point to verified in-range route",()=>{
                var method=typeof(NpcActionJob).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcActionApproach")?.GetMethod("FindRoute");
                Check.True(method!=null,"A reachable person must not fail because a radial standoff point falls in furniture.");
                var target=new GuardPoint3{X=3};var blocked=new GuardPoint3{X=2};var clear=new GuardPoint3{X=3,Z=1};
                Func<GuardPoint3,List<GuardPoint3>> route=p=>p.Z==0?new List<GuardPoint3>():new List<GuardPoint3>{new GuardPoint3(),p};
                var result=(List<GuardPoint3>)method.Invoke(null,new object[]{target,1.35,new[]{blocked,clear},route,new Func<GuardPoint3,bool>(p=>true)});
                Check.True(result!=null);Check.Near(1,result[result.Count-1].Z);
                result=(List<GuardPoint3>)method.Invoke(null,new object[]{target,1.35,new[]{new GuardPoint3{X=3,Z=2},clear},route,new Func<GuardPoint3,bool>(p=>false)});
                Check.True(result==null,"Out-of-range or wall-occluded approach cannot satisfy handover.");
            }));
            cases.Add(("Shop source ownership lookup follows inventory transfer and restored branch",()=>{
                var lookup=typeof(WorldStateStore).GetMethod("NpcInventoryItemOwner");
                Check.True(lookup!=null,"Purchases must reject an already-owned source UID before native splitting.");
                var store=new WorldStateStore(11);var town=store.GetOrCreate(1,new[]{10,20},1);
                string a=town.Households[0].Residents[0].Id,b=town.Households[1].Residents[0].Id;
                store.InitializeNpcInventory(a,0,new[]{new NpcInventoryItem{Uid=7,NativePayload="native"}});
                store.InitializeNpcInventory(b,0,new NpcInventoryItem[0]);
                var before=store.Snapshot();
                Check.Equal(a,(string)lookup.Invoke(store,new object[]{(ulong)7}));
                Check.True(store.CommitNpcInventoryTransaction("move",new[]{new NpcInventoryUpdate{PersonId=a,ExpectedRevision=0},
                    new NpcInventoryUpdate{PersonId=b,ExpectedRevision=0,Items=new List<NpcInventoryItem>{new NpcInventoryItem{Uid=7,NativePayload="native"}}}}));
                Check.Equal(b,(string)lookup.Invoke(store,new object[]{(ulong)7}));
                store.Restore(before);Check.Equal(a,(string)lookup.Invoke(store,new object[]{(ulong)7}));
                Check.True(lookup.Invoke(store,new object[]{(ulong)8})==null);
            }));
            
            cases.Add(("Shop pricing uses buyer stats and rejects arithmetic overflow",()=>{
                int ordinary,skilled;
                Check.True(NpcShopCommerce.TryPrice(100,1,10,50,50,false,out ordinary));
                Check.Equal(56,ordinary);
                Check.True(NpcShopCommerce.TryPrice(100,1,10,100,100,false,out skilled));
                Check.True(skilled<ordinary);
                int sale;Check.True(NpcShopCommerce.TryPrice(100,2,10,50,50,true,out sale));Check.Equal(ordinary,sale);
                Check.True(!NpcShopCommerce.TryPrice(int.MaxValue,1000000,20,0,0,false,out sale));
                Check.True(!NpcShopCommerce.TryPrice(10,0,10,50,50,false,out sale));
            }));
            cases.Add(("Owned-loot sale contract binds one UID keeper and native sell price",()=>{
                int novice,expert;
                Check.True(NpcShopLootSale.TryPrice(100,12,0,0,out novice));
                Check.True(NpcShopLootSale.TryPrice(100,12,100,100,out expert));
                Check.True(expert>novice,"Native sell quote must use the seller's own Mercantile.");
                var sale=SaleJob();NpcActionExecutor.Validate(sale);
                var malformed=NpcActionExecutor.Copy(sale);malformed.Target.Quantity=2;
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
                malformed=NpcActionExecutor.Copy(sale);malformed.Target.ContainerId="shop-stock";
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
                malformed=NpcActionExecutor.Copy(sale);malformed.Target.ItemId="0";
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
                malformed=NpcActionExecutor.Copy(sale);malformed.ServicePrice=0;
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
            }));
            
            cases.Add(("Semantic quantity binding preserves native item and direction: Owned-loot sale parser keeps exact item binding with current-shop wording",()=>{
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
            cases.Add(("Owned-loot sale chooses lowest verified merchant shelf without binding a shelf contract",()=>{
                var select=typeof(NpcShopLootSale).GetMethod("TrySelectMerchantShelfLoadId");
                Check.True(select!=null,"Owned-loot sale needs deterministic merchant shelf selection.");
                object[] args={new ulong[]{91,7,32},(ulong)0};
                Check.True((bool)select.Invoke(null,args));
                Check.Equal((ulong)7,(ulong)args[1]);
                args=new object[]{new ulong[0],(ulong)0};
                Check.True(!(bool)select.Invoke(null,args));
            }));
            cases.Add(("Native training contract permits only paid running or completed stage one",()=>{
                var training=TrainingJob();NpcActionExecutor.Validate(training);
                training=NpcActionExecutor.Copy(training);training.ServiceStep=1;training.Phase=NpcActionPhase.Executing;training.FinishAt=10900;
                NpcActionExecutor.Validate(training);
                training=NpcActionExecutor.Copy(training);training.Phase=NpcActionPhase.Completed;training.CompletedEffectId=training.Id+":trained";NpcActionExecutor.Validate(training);
                training=TrainingJob();training.ServiceStep=1;training.Phase=NpcActionPhase.Failed;training.FinishAt=10900;training.FailureReason="Training interrupted.";
                NpcActionExecutor.Validate(training);
                training=TrainingJob();training.ServiceStep=1;training.Phase=NpcActionPhase.Cancelled;training.FinishAt=10900;training.FailureReason="Training cancelled.";
                NpcActionExecutor.Validate(training);
                training=TrainingJob();training.ServiceStep=1;
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(training));
                training=TrainingJob();training.Phase=NpcActionPhase.Completed;training.CompletedEffectId=training.Id+":trained";
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(training));
            }));
            cases.Add(("Shop action contract binds stock seller UID quantity and price",()=>{
                var job=Job();NpcActionExecutor.Validate(job);
                var malformed=NpcActionExecutor.Copy(job);malformed.Target.ItemId="0";
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
                malformed=NpcActionExecutor.Copy(job);malformed.Target.Quantity=0;
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
                malformed=NpcActionExecutor.Copy(job);malformed.Target.ContainerId="private-chest";
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
                malformed=NpcActionExecutor.Copy(job);malformed.ServicePrice=0;
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
                malformed=NpcActionExecutor.Copy(job);malformed.ServiceStep=1;
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
                malformed=NpcActionExecutor.Copy(job);malformed.ServicePrice=10;
                Check.Throws<ArgumentException>(()=>NpcActionExecutor.Validate(malformed));
                Check.Equal(9,NpcActionExecutor.CopyTarget(job.Target).QuotedGold);
            }));
            cases.Add(("Shop purchase completion is atomic replay-safe and survives save branch restore",()=>{
                var store=new WorldStateStore(41);var town=store.GetOrCreate(1,new[]{10,20},1);
                var job=Job();job.ResidentId=town.Households[0].Residents[0].Id;job.Target.PersonId=town.Households[1].Residents[0].Id;
                store.InitializeNpcInventory(job.ResidentId,30,new NpcInventoryItem[0]);
                store.InitializeNpcInventory(job.Target.PersonId,0,new NpcInventoryItem[0]);
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()));job=store.ReadNpcAction(job.Id);
                int calls=0;
                Check.True(!store.TryCompleteNpcActionEffect(job.Id,job.Revision,102,()=>{calls++;return false;}));
                Check.Equal(NpcActionPhase.Executing,store.ReadNpcAction(job.Id).Phase);
                Check.True(store.TryCompleteNpcActionEffect(job.Id,job.Revision,102,()=>{
                    calls++;return store.CommitNpcInventoryTransaction(job.Id+":purchase",new[]{
                        new NpcInventoryUpdate{PersonId=job.ResidentId,ExpectedRevision=0,WalletGold=21,Items=new List<NpcInventoryItem>{new NpcInventoryItem{Uid=42,NativePayload="native item"}}},
                        new NpcInventoryUpdate{PersonId=job.Target.PersonId,ExpectedRevision=0,WalletGold=9}});
                }));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.True(!restored.TryCompleteNpcActionEffect(job.Id,job.Revision,103,()=>{calls++;return true;}));
                Check.Equal(2,calls);Check.Equal(21,restored.ReadNpcInventory(job.ResidentId).WalletGold);
                Check.Equal((ulong)42,restored.ReadNpcInventory(job.ResidentId).Items[0].Uid);
                Check.Equal(NpcActionPhase.Completed,restored.ReadNpcAction(job.Id).Phase);
            }));
        }
        private static NpcActionJob Job() {return new NpcActionJob {Id="purchase:1",ProposalId="request:1",ResidentId="buyer",
            Kind=NpcActionKind.Purchase,ServiceCode="shop-stock",ServicePrice=9,Phase=NpcActionPhase.Accepted,
            Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,
            Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,PersonId="seller",ContainerId="123",ItemId="42",Quantity=1,QuotedGold=9}};}
        private static NpcActionJob SaleJob() {return new NpcActionJob {Id="sale:1",ProposalId="request:sale",ResidentId="seller",
            Kind=NpcActionKind.Purchase,ServiceCode=NpcShopLootSale.ServiceCode,ServicePrice=9,Phase=NpcActionPhase.Accepted,
            Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,
            Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,PersonId="keeper",ContainerId=NpcShopLootSale.TargetContainerId,ItemId="42",Quantity=1,QuotedGold=9}};}
        private static NpcActionJob TrainingJob() {return new NpcActionJob {Id="training:1",ProposalId="request:training",ResidentId="student",
            Kind=NpcActionKind.Purchase,ServiceCode=NpcShopCommerce.TrainingServiceCode,ServicePrice=9,Phase=NpcActionPhase.Accepted,
            Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=20000,FinishAt=0,
            Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,PersonId="trainer",ContainerId=NpcShopCommerce.TrainingTargetContainerId,ItemId="14",Quantity=1,QuotedGold=9}};}
    }
}
