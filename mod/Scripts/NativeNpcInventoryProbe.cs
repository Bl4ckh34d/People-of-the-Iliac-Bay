using System;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Fixture-owned objects only. The editor/player harness can call Run without
    // touching the player's inventory, equipment, saves, gold or scene objects.
    public static class NativeNpcInventoryProbe
    {
        public static string Run()
        {
            VerifyTransferDirectionSerialization();
            string ownerProof=NativeNpcInventoryOwnerProbe.Run();
            string shopPurchaseProof=NativeNpcShopPurchaseProbe.Run();
            var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10,20},1);
            string a=town.Households[0].Residents[0].Id,b=town.Households[1].Residents[0].Id;
            var source=new ItemCollection();var target=new ItemCollection();
            var arrows=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);arrows.stackCount=7;source.AddItem(arrows);
            HouseholdPropertyKeyItem.Register();var key=new HouseholdPropertyKeyItem(100,10);source.AddItem(key);
            var existing=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);existing.stackCount=2;target.AddItem(existing);
            NativeNpcInventoryBinding first,second;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,a,source,50,out first,out reason),reason);
            Require(NativeNpcInventoryBridge.TryOpen(store,b,target,20,out second,out reason),reason);
            VerifyActionHandover(store,a);
            VerifyHomeContainerDeposit();
            VerifyPartialContainerDepositRefused();
            VerifyPlayerAcceptance(store.Snapshot(),a);
            VerifyFullPlayerAcceptance(store.Snapshot(),a);
            VerifyInsufficientPlayerSource(store.Snapshot(),a);
            VerifyNpcTransfer(store,a,b);
            ulong sourceUid=first.Items.GetItem(0).UID,targetUid=second.Items.GetItem(0).UID;
            Require(NativeNpcInventoryBridge.TryTransfer(store,first,second,sourceUid,3,"partial",out reason),reason);
            Require(first.Items.GetItem(sourceUid).stackCount==4 && second.Items.GetItem(targetUid).stackCount==5,"Native partial stack counts/UID changed.");
            Require(!NativeNpcInventoryBridge.TryTransfer(store,first,second,sourceUid,99,"too-many",out reason),"Invalid transfer succeeded.");
            var saved=store.Snapshot();var restored=new WorldStateStore(0);
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),saved)));
            NativeNpcInventoryBinding reopened;
            Require(NativeNpcInventoryBridge.TryOpen(restored,a,null,0,out reopened,out reason),reason);
            Require(reopened.Items.GetItem(sourceUid).stackCount==4,"Partial stack did not survive native save.");
            Require(HouseholdPropertyKeyItem.IsMatching(reopened.Items.GetItem(key.UID),100,10),"Custom class metadata or original UID did not survive native save.");
            var stock=new ItemCollection();var sword=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Longsword);stock.AddItem(sword);
            Require(NativeNpcInventoryBridge.TryPurchase(store,first,stock,b,second.Revision,sword.UID,1,9,()=>true,"purchase",out reason),reason);
            DaggerfallUnityItem purchased=null;
            for(int i=0;i<first.Items.Count;i++)
            {
                var item=first.Items.GetItem(i);
                if(item.ItemGroup==ItemGroups.Weapons && item.TemplateIndex==(int)Weapons.Longsword && item.UID!=sword.UID)
                {purchased=item;break;}
            }
            Require(stock.Count==1 && ReferenceEquals(stock.GetItem(sword.UID),sword) && purchased!=null &&
                store.ReadNpcInventory(a).WalletGold==41 && store.ReadNpcInventory(b).WalletGold==29,
                "Purchase did not preserve native stock, allocate owned stock and move money.");
            Require(!NativeNpcInventoryBridge.TryPayService(store,a,9,"purchase",first.Revision,out reason),"Duplicate receipt charged wallet.");
            Require(NativeNpcInventoryBridge.TryPayService(store,a,4,"drink",first.Revision,out reason),reason);
            Require(store.ReadNpcInventory(a).WalletGold==37,"Service charged wrong wallet.");
            Require(NativeNpcInventoryBridge.TryCapture(store,first,out reason),reason);
            Require(first.Revision==store.ReadNpcInventory(a).Revision,"Wallet-only change made live binding stale.");
            VerifyRejectedCorpseTransferRollback(store,first,a);
            var conflict=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Dagger);stock.AddItem(conflict);
            var third=store.GetOrCreate(200,new[]{30},1).Households[0].Residents[0].Id;
            var duplicate=new ItemCollection();duplicate.AddItem(conflict,noStack:true);
            Require(store.InitializeNpcInventory(third,0,NativeNpcInventoryBridge.CaptureItems(duplicate)),"Fixture duplicate owner initialization failed.");
            var retained=first.Items.GetItem(sourceUid);int beforeCount=retained.stackCount;
            Require(!NativeNpcInventoryBridge.TryPurchase(store,first,stock,b,store.ReadNpcInventory(b).Revision,conflict.UID,1,3,()=>true,"rollback",out reason),"UID conflict was accepted.");
            Require(ReferenceEquals(stock.GetItem(conflict.UID),conflict) && first.Items.GetItem(conflict.UID)==null &&
                ReferenceEquals(first.Items.GetItem(sourceUid),retained) && retained.stackCount==beforeCount && store.ReadNpcInventory(a).WalletGold==37,
                "Rollback changed unrelated references, stock, counts or wallet.");
            var opaque=store.Snapshot();opaque.NpcInventories.Find(i=>i.PersonId==a).Items[0].NativePayload="{\"uid\":"+sourceUid+",\"stackCount\":4,\"className\":\"Missing.Mod.Item\"}";
            restored.Restore(opaque);string opaquePayload=restored.ReadNpcInventory(a).Items[0].NativePayload;
            Require(!NativeNpcInventoryBridge.TryOpen(restored,a,null,0,out reopened,out reason),"Missing custom class silently fell back to vanilla.");
            Require(restored.ReadNpcInventory(a).Items[0].NativePayload==opaquePayload,"Missing custom payload was discarded.");
            Require(NativeNpcInventoryBridge.TryMaterializeCorpseWallet(store,first,out reason),reason);
            Require(store.ReadNpcInventory(a).WalletGold==0,"Corpse wallet was not debited with its native items.");
            int corpseItems=first.Items.Count;Require(NativeNpcInventoryBridge.TryMaterializeCorpseWallet(store,first,out reason),reason);
            Require(first.Items.Count==corpseItems,"Reopening a corpse regenerated gold.");
            var corpseBranch=new WorldStateStore(0);corpseBranch.Restore(store.Snapshot());
            Require(NativeNpcInventoryBridge.TryOpen(corpseBranch,a,null,999,out reopened,out reason),reason);
            Require(corpseBranch.ReadNpcInventory(a).WalletGold==0 && reopened.Items.Count==corpseItems,"Corpse reopen regenerated initial wealth.");
            return "PEOPLE_OF_THE_ILIAC_BAY_NPC_INVENTORY_PASS partial-stack native-save real-stock wallet receipt rollback opaque-custom corpse-wallet corpse-transfer-rollback action-handover player-acceptance bidirectional-quantity npc-transfer recipient-revisions old-quantity old-direction\n"+shopPurchaseProof+"\n"+ownerProof;
        }
        private static void VerifyTransferDirectionSerialization()
        {
            var state=new WorldState();
            NpcActionTransferDirection[] directions={NpcActionTransferDirection.NpcToCounterparty,
                NpcActionTransferDirection.PlayerToNpc,NpcActionTransferDirection.NpcToContainer,
                NpcActionTransferDirection.HomeContainerToNpc};
            for(int i=0;i<directions.Length;i++)
            {
                bool container=directions[i]==NpcActionTransferDirection.NpcToContainer ||
                    directions[i]==NpcActionTransferDirection.HomeContainerToNpc;
                state.NpcActions.Add(new NpcActionJob {Id="fixture-transfer-direction-"+i,ProposalId="fixture-transfer-direction-"+i,
                    ResidentId="fixture-resident",Kind=NpcActionKind.Transfer,Phase=NpcActionPhase.Accepted,Revision=1,
                    AcceptedAt=1,UpdatedAt=1,DeadlineAt=10,Target=new NpcActionTarget {
                        Kind=container?NpcActionTargetKind.IndoorPoint:NpcActionTargetKind.Person,
                        PersonId=container?null:(directions[i]==NpcActionTransferDirection.PlayerToNpc?"player":"fixture-recipient"),
                        SettlementId=container?100:-1,BuildingKey=container?10:-1,X=1,Y=2,Z=3,
                        ContainerId=container?"fixture-container":null,ItemId="184467",Quantity=7,
                        TransferDirection=directions[i]}});
            }
            var copy=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),state));
            Require(copy.NpcActions.Count==directions.Length,"Native serializer changed transfer-direction action count.");
            for(int i=0;i<directions.Length;i++)
            {
                var target=copy.NpcActions[i].Target;
                Require(target.TransferDirection==directions[i] && target.ItemId=="184467" && target.Quantity==7 &&
                    target.ContainerId==(i<2?null:"fixture-container") && target.PersonId==(i==0?"fixture-recipient":i==1?"player":null) &&
                    target.Kind==(i<2?NpcActionTargetKind.Person:NpcActionTargetKind.IndoorPoint) &&
                    target.SettlementId==(i<2?-1:100) && target.BuildingKey==(i<2?-1:10) && target.X==1 && target.Y==2 && target.Z==3,
                    "Native serializer changed exact transfer target payload for direction "+i+".");
            }
        }
        private static void VerifyActionHandover(WorldStateStore original,string owner)
        {
            var store=new WorldStateStore(0);store.Restore(original.Snapshot());
            NativeNpcInventoryBinding binding;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,null,0,out binding,out reason),reason);
            var item=binding.Items.GetItem(0);int before=item.stackCount;
            var recipient=new ItemCollection();var merged=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);merged.stackCount=2;recipient.AddItem(merged);
            var job=new NpcActionJob {Id="fixture-handover",ProposalId="fixture-offer",ResidentId=owner,Kind=NpcActionKind.Transfer,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=item.UID.ToString(),Quantity=3},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,ExpectedInventoryRevision=binding.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Handover arrival failed.");
            job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionHandover(store,job,102,binding,recipient,()=>false,out reason),"Invalid handover access succeeded.");
            Require(item.stackCount==before && merged.stackCount==2,"Denied handover changed native stacks.");
            Require(NativeNpcInventoryBridge.TryCompleteActionHandover(store,job,102,binding,recipient,()=>true,out reason),reason);
            Require(item.stackCount==before-3 && merged.stackCount==5 && store.ReadNpcAction(job.Id).Phase==NpcActionPhase.Completed,"Handover did not commit exact partial quantity and action completion.");
            store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            Require(!NativeNpcInventoryBridge.TryCompleteActionHandover(store,job,103,binding,recipient,()=>true,out reason),"Restored handover replay duplicated an item.");
            Require(merged.stackCount==5 && store.ReadNpcInventory(owner).WalletGold==50,"Handover replay changed items or wallet.");
            NativeNpcInventoryBinding reopened;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,null,999,out reopened,out reason),reason);
            Require(reopened.Items.GetItem(item.UID).stackCount==before-3,"Reopening handover source regenerated its item.");
            VerifyProtectedActionTransfer(store,binding,owner);
        }
        private static void VerifyHomeContainerDeposit()
        {
            var store=new WorldStateStore(77);var town=store.GetOrCreate(100,new[]{10},1);string owner=town.Households[0].Residents[0].Id;
            var nativeItems=new ItemCollection();var arrows=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);arrows.stackCount=5;nativeItems.AddItem(arrows);
            NativeNpcInventoryBinding source;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,nativeItems,0,out source,out reason),reason);
            ulong uid=source.Items.GetItem(0).UID;int before=source.Items.GetItem(uid).stackCount;
            var containerItems=new ItemCollection();var existingArrow=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);
            existingArrow.stackCount=4;containerItems.AddItem(existingArrow);
            var job=new NpcActionJob {Id="fixture-home-deposit",ProposalId="fixture-home-deposit",ResidentId=owner,Kind=NpcActionKind.Transfer,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,X=1,Y=0,Z=2,
                    ContainerId="4242",ItemId=uid.ToString(),Quantity=before,TransferDirection=NpcActionTransferDirection.NpcToContainer},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,
                LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:100",X=0,Z=0},
                ExpectedInventoryRevision=source.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Container deposit arrival failed.");job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,source,null,containerItems,()=>false,out reason),
                "Container transfer bypassed the fresh access check.");
            Require(source.Items.GetItem(uid).stackCount==before && containerItems.Count==1 && existingArrow.stackCount==4,"Rejected container transfer changed either collection.");
            Require(NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,source,null,containerItems,()=>true,out reason),reason);
            Require(source.Items.GetItem(uid)==null && containerItems.GetItem(uid)!=null &&
                containerItems.GetItem(uid).stackCount==before && existingArrow.stackCount==4 && containerItems.Count==2 &&
                store.ReadNpcAction(job.Id).Phase==NpcActionPhase.Completed,
                "Container deposit did not move exact UID/count and complete once.");
            var targetCopy=new ItemCollection();targetCopy.DeserializeItems(containerItems.SerializeItems());
            Require(targetCopy.GetItem(uid)!=null && targetCopy.GetItem(uid).stackCount==before && targetCopy.GetItem(existingArrow.UID).stackCount==4,
                "Deposited native item did not retain UID/count after collection serialization.");
            var restored=new WorldStateStore(0);restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(restored,job,103,source,null,containerItems,()=>true,out reason),
                "Restored container deposit replay duplicated a native item.");
            NativeNpcInventoryBinding reopened;
            Require(NativeNpcInventoryBridge.TryOpen(restored,owner,null,0,out reopened,out reason),reason);
            Require(reopened.Items.GetItem(uid)==null && containerItems.GetItem(uid).stackCount==before,
                "Saved NPC source restored an item already deposited to the container.");
        }
        private static void VerifyPartialContainerDepositRefused()
        {
            var store=new WorldStateStore(78);var town=store.GetOrCreate(100,new[]{10},1);string owner=town.Households[0].Residents[0].Id;
            var nativeItems=new ItemCollection();var arrows=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);arrows.stackCount=5;nativeItems.AddItem(arrows);
            NativeNpcInventoryBinding source;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,nativeItems,0,out source,out reason),reason);
            var item=source.Items.GetItem(0);var target=new ItemCollection();
            var job=new NpcActionJob {Id="fixture-partial-home-deposit",ProposalId="fixture-partial-home-deposit",ResidentId=owner,Kind=NpcActionKind.Transfer,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=100,BuildingKey=10,X=1,Y=0,Z=2,
                    ContainerId="4243",ItemId=item.UID.ToString(),Quantity=2,TransferDirection=NpcActionTransferDirection.NpcToContainer},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,
                LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:100",X=0,Z=0},
                ExpectedInventoryRevision=source.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Partial container deposit arrival failed.");job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,source,null,target,()=>true,out reason),
                "Partial container deposit silently created a replacement UID.");
            Require(source.Items.GetItem(item.UID).stackCount==5 && target.Count==0,"Rejected partial container deposit changed item ownership.");
        }
        private static void VerifyProtectedActionTransfer(WorldStateStore store,NativeNpcInventoryBinding source,string owner)
        {
            DaggerfallUnityItem key=null;
            for(int i=0;i<source.Items.Count;i++) if(source.Items.GetItem(i) is HouseholdPropertyKeyItem){key=source.Items.GetItem(i);break;}
            Require(key!=null,"Protected transfer fixture key missing.");
            var target=new ItemCollection();string reason;
            var job=new NpcActionJob {Id="fixture-protected-transfer",ProposalId="fixture-protected-offer",ResidentId=owner,
                Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=key.UID.ToString(),Quantity=1},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,ExpectedInventoryRevision=source.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Protected transfer action arrival failed.");
            job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,source,null,target,()=>true,out reason),
                "Protected household key was transferred through native action adapter.");
            Require(ReferenceEquals(source.Items.GetItem(key.UID),key) && HouseholdPropertyKeyItem.IsMatching(key,100,10) && target.Count==0,
                "Rejected protected transfer changed native key ownership or payload.");
        }
        private static void VerifyPlayerAcceptance(WorldState original,string owner)
        {
            var store=new WorldStateStore(0);store.Restore(original);
            NativeNpcInventoryBinding recipient;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,null,0,out recipient,out reason),reason);
            var receiverArrow=recipient.Items.GetItem(0);int receiverBefore=receiverArrow.stackCount;
            var playerItems=new ItemCollection();var playerArrow=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);
            playerArrow.stackCount=5;playerArrow.value=7;playerItems.AddItem(playerArrow);
            var job=new NpcActionJob {Id="fixture-player-accept",ProposalId="fixture-player-offer",ResidentId=owner,Kind=NpcActionKind.Transfer,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=playerArrow.UID.ToString(),Quantity=3,
                    TransferDirection=NpcActionTransferDirection.PlayerToNpc},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,
                ExpectedInventoryRevision=recipient.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Player item acceptance arrival failed.");
            job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,null,playerItems,recipient,null,()=>false,out reason),
                "NPC accepted player item without valid willingness/access.");
            Require(playerArrow.stackCount==5 && receiverArrow.stackCount==receiverBefore,"Denied player item acceptance changed native stacks.");
            Require(NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,null,playerItems,recipient,null,()=>true,out reason),reason);
            Require(playerArrow.stackCount==2 && receiverArrow.stackCount==receiverBefore+3 && recipient.Revision==1 &&
                store.ReadNpcAction(job.Id).Phase==NpcActionPhase.Completed,"NPC did not receive exact partial quantity into native inventory.");
            var receipt=store.ReadNpcAction(job.Id).ItemTransferReceipt;
            Require(receipt!=null && receipt.TransactionId==job.Id+":transfer" && receipt.SourceItemUid==playerArrow.UID &&
                receipt.MovedItemUid!=0 && receipt.MovedItemUid!=receiverArrow.UID &&
                receipt.RetainedDestinationItemUid==receiverArrow.UID && receipt.Quantity==3 &&
                receipt.RecipientId==owner && receipt.CounterpartyId=="player" &&
                receipt.NativeValuationStatus==NpcExperienceNativeValuationStatus.Known && receipt.NativeGoldValue==21,
                "Partial incoming stack receipt did not bind moved UID, retained merged UID and exact transfer quantity.");
            Require(store.DrainNpcItemTransferReceipts(32)==1 && store.DrainNpcItemTransferReceipts(32)==0 &&
                store.Snapshot().NpcExperienceInbox.Entries.Count==1 &&
                store.Snapshot().NpcExperienceInbox.Entries[0].EventKind==NpcExperienceEventKind.ItemReceived &&
                store.Snapshot().NpcExperienceInbox.Entries[0].ReceivedItemUid==receiverArrow.UID &&
                store.Snapshot().NpcExperienceInbox.Entries[0].TransferQuantity==3,
                "Partial incoming receipt did not project once as ItemReceived for retained merged UID and quantity.");
            store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,103,null,playerItems,recipient,null,()=>true,out reason),
                "Restored incoming transfer replay duplicated an item.");
            NativeNpcInventoryBinding reopened;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,null,0,out reopened,out reason),reason);
            var restoredAction=store.ReadNpcAction(job.Id);
            Require(reopened.Items.GetItem(receiverArrow.UID).stackCount==receiverBefore+3 &&
                restoredAction.ItemTransferReceipt!=null && restoredAction.ItemTransferReceipt.RetainedDestinationItemUid==receiverArrow.UID &&
                restoredAction.ItemTransferReceipt.MovedItemUid!=receiverArrow.UID &&
                restoredAction.ItemTransferReceipt.NativeValuationStatus==NpcExperienceNativeValuationStatus.Known &&
                restoredAction.ItemTransferReceipt.NativeGoldValue==21 &&
                restoredAction.ItemTransferReceipt.Quantity==3 && store.DrainNpcItemTransferReceipts(32)==0 &&
                store.Snapshot().NpcExperienceInbox.Entries.Count==1,
                "Partial incoming receipt, retained merged stack or no-replay state did not survive native save.");

            VerifyStalePlayerAcceptance(original,owner);
            VerifyMissingPlayerSource(original,owner);

            var legacy=new WorldStateStore(0);legacy.Restore(original);
            Require(NativeNpcInventoryBridge.TryOpen(legacy,owner,null,0,out recipient,out reason),reason);
            var legacyItem=recipient.Items.GetItem(0);
            var legacyJob=new NpcActionJob {Id="fixture-legacy-direction",ProposalId="fixture-legacy-direction-offer",ResidentId=owner,
                Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=legacyItem.UID.ToString()},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,ExpectedInventoryRevision=recipient.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(legacy, ref legacyJob, "Controlled legacy inventory fixture transfer.", out reason),reason);
            string json=SaveLoadManager.Serialize(typeof(WorldState),legacy.Snapshot());
            string withoutDirection=NativeProbeText.WithoutJsonProperty(json,"TransferDirection");
            var restoredLegacy=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),withoutDirection);
            Require(restoredLegacy.NpcActions[0].Target.TransferDirection==NpcActionTransferDirection.NpcToCounterparty,
                "Schema-28 action did not preserve legacy NPC-to-player direction.");
        }
        private static void VerifyFullPlayerAcceptance(WorldState original,string owner)
        {
            var store=new WorldStateStore(0);store.Restore(original);
            NativeNpcInventoryBinding recipient;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,null,0,out recipient,out reason),reason);
            var receiver=recipient.Items.GetItem(0);int before=receiver.stackCount;
            var playerItems=new ItemCollection();var item=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);item.stackCount=2;item.value=11;playerItems.AddItem(item);
            var job=new NpcActionJob {Id="fixture-full-player-receive",ProposalId="fixture-full-player-receive-offer",ResidentId=owner,
                Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=item.UID.ToString(),Quantity=2,
                    TransferDirection=NpcActionTransferDirection.PlayerToNpc},Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,ExpectedInventoryRevision=recipient.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Full incoming action arrival failed.");
            job=store.ReadNpcAction(job.Id);
            Require(job.Target.ItemId==item.UID.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                job.Target.Quantity==2 && job.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc,
                "Full-stack incoming action lost exact UID, quantity or direction.");
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,null,playerItems,recipient,null,()=>false,out reason),
                "Full-stack incoming transfer bypassed fresh access validation.");
            Require(playerItems.GetItem(item.UID)==item && item.stackCount==2 && receiver.stackCount==before,
                "Rejected full-stack item receive changed either inventory.");
            Require(store.ReadNpcAction(job.Id).ItemTransferReceipt==null,
                "Rejected incoming transfer recorded a durable receipt.");
            var wrongReceiptJob=NpcActionExecutor.Copy(job);
            wrongReceiptJob.Target.ItemId=(item.UID+1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            bool wrongReceiptRejected=false;
            try
            {
                NpcActionTransferReceipt.CreatePlayerToNpc(wrongReceiptJob,job.Id+":transfer",item.UID,
                    item.UID,receiver.UID,2,owner,"player",102,
                    NpcExperienceNativeValuationStatus.Unknown,0);
            }
            catch(ArgumentException) { wrongReceiptRejected=true; }
            Require(wrongReceiptRejected && playerItems.GetItem(item.UID)==item && item.stackCount==2 && receiver.stackCount==before,
                "A receipt bound to a different action item was not rejected before inventory commit.");
            Require(NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,null,playerItems,recipient,null,()=>true,out reason),reason);
            Require(playerItems.Count==0 && receiver.stackCount==before+2 && store.ReadNpcAction(job.Id).Phase==NpcActionPhase.Completed &&
                store.ReadNpcInventory(owner).LastTransactionId==job.Id+":transfer",
                "Full player stack transfer did not move all units and complete once.");
            var receipt=store.ReadNpcAction(job.Id).ItemTransferReceipt;
            Require(receipt!=null && receipt.TransactionId==job.Id+":transfer" && receipt.SourceItemUid==item.UID &&
                receipt.MovedItemUid==item.UID && receipt.RetainedDestinationItemUid==receiver.UID &&
                receipt.MovedItemUid!=receipt.RetainedDestinationItemUid && receipt.Quantity==2 &&
                receipt.RecipientId==owner && receipt.CounterpartyId=="player" &&
                receipt.NativeValuationStatus==NpcExperienceNativeValuationStatus.Known && receipt.NativeGoldValue==22,
                "Merged incoming item receipt did not preserve source, moved, and retained destination UIDs.");
            Require(store.DrainNpcItemTransferReceipts(32)==1 && store.DrainNpcItemTransferReceipts(32)==0 &&
                store.Snapshot().NpcExperienceInbox.Entries.Count==1 &&
                store.Snapshot().NpcExperienceInbox.Entries[0].EventKind==NpcExperienceEventKind.ItemReceived &&
                store.Snapshot().NpcExperienceInbox.Entries[0].ReceivedItemUid==receiver.UID,
                "Incoming item receipt did not drain once as neutral ItemReceived evidence.");
            var saved=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());var restored=new WorldStateStore(0);
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),saved));
            NativeNpcInventoryBinding reopened;
            Require(NativeNpcInventoryBridge.TryOpen(restored,owner,null,0,out reopened,out reason),reason);
            var completed=restored.ReadNpcAction(job.Id);
            Require(completed.Phase==NpcActionPhase.Completed && completed.Target.ItemId==item.UID.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                reopened.Items.GetItem(receiver.UID).stackCount==before+2 &&
                !NativeNpcInventoryBridge.TryCompleteActionTransfer(restored,completed,103,null,playerItems,reopened,null,()=>true,out reason) &&
                reopened.Items.GetItem(receiver.UID).stackCount==before+2 && playerItems.Count==0,
                "Restored full-stack item receipt did not preserve exact quantity or reject replay.");
        }
        private static void VerifyInsufficientPlayerSource(WorldState original,string owner)
        {
            var store=new WorldStateStore(0);store.Restore(original);
            NativeNpcInventoryBinding recipient;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,null,0,out recipient,out reason),reason);
            var receiver=recipient.Items.GetItem(0);int before=receiver.stackCount;
            var playerItems=new ItemCollection();var item=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);item.stackCount=2;playerItems.AddItem(item);
            var job=new NpcActionJob {Id="fixture-short-player-source",ProposalId="fixture-short-player-source-offer",ResidentId=owner,
                Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=item.UID.ToString(),Quantity=3,
                    TransferDirection=NpcActionTransferDirection.PlayerToNpc},Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,ExpectedInventoryRevision=recipient.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Insufficient-source action arrival failed.");
            job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,null,playerItems,recipient,null,()=>true,out reason),
                "Incoming transfer moved more units than player owned.");
            Require(item.stackCount==2 && receiver.stackCount==before,"Insufficient incoming stack changed either inventory.");
        }
        private static void VerifyStalePlayerAcceptance(WorldState original,string owner)
        {
            var store=new WorldStateStore(0);store.Restore(original);
            NativeNpcInventoryBinding recipient;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,null,0,out recipient,out reason),reason);
            var playerItems=new ItemCollection();var item=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);item.stackCount=4;playerItems.AddItem(item);
            var job=new NpcActionJob {Id="fixture-stale-player-receive",ProposalId="fixture-stale-player-receive-offer",ResidentId=owner,
                Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=item.UID.ToString(),Quantity=2,
                    TransferDirection=NpcActionTransferDirection.PlayerToNpc},Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,ExpectedInventoryRevision=recipient.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Stale incoming action arrival failed.");
            var saved=store.ReadNpcInventory(owner);
            Require(store.TryPayNpcService(owner,1,"fixture-stale-recipient-revision",saved.Revision),"Could not advance recipient revision fixture.");
            job=store.ReadNpcAction(job.Id);int count=item.stackCount,receiverCount=recipient.Items.GetItem(0).stackCount;
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,null,playerItems,recipient,null,()=>true,out reason),
                "Stale incoming recipient revision permitted transfer.");
            Require(item.stackCount==count && recipient.Items.GetItem(0).stackCount==receiverCount,"Stale incoming revision changed item ownership.");
        }
        private static void VerifyMissingPlayerSource(WorldState original,string owner)
        {
            var store=new WorldStateStore(0);store.Restore(original);
            NativeNpcInventoryBinding recipient;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,owner,null,0,out recipient,out reason),reason);
            var playerItems=new ItemCollection();var item=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);item.stackCount=4;playerItems.AddItem(item);
            var job=new NpcActionJob {Id="fixture-missing-player-source",ProposalId="fixture-missing-player-source-offer",ResidentId=owner,
                Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=item.UID.ToString(),Quantity=2,
                    TransferDirection=NpcActionTransferDirection.PlayerToNpc},Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,ExpectedInventoryRevision=recipient.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Missing-source action arrival failed.");
            playerItems.RemoveItem(item);
            var replacement=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);replacement.stackCount=4;playerItems.AddItem(replacement);
            int receiverCount=recipient.Items.GetItem(0).stackCount;
            job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,null,playerItems,recipient,null,()=>true,out reason),
                "A matching replacement item bypassed exact source UID validation.");
            Require(playerItems.Count==1 && playerItems.GetItem(replacement.UID)==replacement && replacement.stackCount==4 &&
                recipient.Items.GetItem(0).stackCount==receiverCount,
                "Rejected replacement UID changed player or NPC inventory.");
        }
        private static void VerifyNpcTransfer(WorldStateStore original,string sourceId,string recipientId)
        {
            var store=new WorldStateStore(0);store.Restore(original.Snapshot());
            NativeNpcInventoryBinding source,recipient;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,sourceId,null,0,out source,out reason),reason);
            Require(NativeNpcInventoryBridge.TryOpen(store,recipientId,null,0,out recipient,out reason),reason);
            var sourceItem=source.Items.GetItem(0);var recipientItem=recipient.Items.GetItem(0);
            int sourceBefore=sourceItem.stackCount,recipientBefore=recipientItem.stackCount;
            var job=new NpcActionJob {Id="fixture-npc-transfer",ProposalId="fixture-npc-transfer-offer",ResidentId=sourceId,
                Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId=recipientId,
                    ItemId=sourceItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture),Quantity=3},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,
                ExpectedInventoryRevision=source.Revision,ExpectedTargetInventoryRevision=recipient.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"NPC transfer arrival failed.");
            job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,source,recipient,null,()=>false,out reason),
                "Ineligible NPC recipient accepted an item transfer.");
            Require(sourceItem.stackCount==sourceBefore && recipientItem.stackCount==recipientBefore,"Denied NPC transfer changed native stacks.");
            Require(NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,source,recipient,null,()=>true,out reason),reason);
            Require(sourceItem.stackCount==sourceBefore-3 && recipientItem.stackCount==recipientBefore+3 &&
                source.Revision==1 && recipient.Revision==1 && store.ReadNpcAction(job.Id).Phase==NpcActionPhase.Completed &&
                store.ReadNpcAction(job.Id).ItemTransferReceipt!=null,
                "NPC transfer did not move exact partial quantity and commit both owner revisions.");
            var receipt=store.ReadNpcAction(job.Id).ItemTransferReceipt;
            Require(receipt.RecipientId==recipientId && receipt.CounterpartyId==sourceId &&
                receipt.SourceItemUid==sourceItem.UID && receipt.RetainedDestinationItemUid==recipientItem.UID &&
                receipt.NativeItemName==NativeItemReceiptLabel.Capture(sourceItem.LongName) &&
                receipt.Quantity==3 && store.DrainNpcItemTransferReceipts(32)==1 && store.DrainNpcItemTransferReceipts(32)==0,
                "NPC recipient did not receive exact merged-UID evidence once.");
            Require(store.TryRememberNpcActionOutcome(job.Id,store.ReadNpcAction(job.Id).Revision,"day 1","shop",null) &&
                store.Snapshot().ConversationMemories.Find(x=>x.PersonId==sourceId).Entries.Exists(x=>x.SourceActionId==job.Id) &&
                store.Snapshot().ConversationMemories.Find(x=>x.PersonId==recipientId).Entries.Exists(x=>x.SourceActionId==job.Id),
                "NPC delivery did not produce both participants' receipt-backed memories.");
            store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            Require(store.DrainNpcItemTransferReceipts(32)==0 &&
                store.ReadNpcAction(job.Id).ItemTransferReceipt.NativeItemName==NativeItemReceiptLabel.Capture(sourceItem.LongName) &&
                store.ReadNpcAction(job.Id).ProjectedItemTransferReceipt.NativeItemName==NativeItemReceiptLabel.Capture(sourceItem.LongName) &&
                !store.TryRememberNpcActionOutcome(job.Id,store.ReadNpcAction(job.Id).Revision,"day 1","shop",null),
                "NPC delivery receipt or memory replayed after native serialization.");
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,103,source,recipient,null,()=>true,out reason),
                "Restored NPC transfer replay duplicated items.");
            NativeNpcInventoryBinding reopened;
            Require(NativeNpcInventoryBridge.TryOpen(store,recipientId,null,0,out reopened,out reason),reason);
            Require(reopened.Items.GetItem(recipientItem.UID).stackCount==recipientBefore+3,
                "NPC recipient did not retain transferred partial stack after native save.");

            VerifyStaleNpcTransferRevision(original,sourceId,recipientId,true);
            VerifyStaleNpcTransferRevision(original,sourceId,recipientId,false);

            var legacy=new WorldStateStore(0);legacy.Restore(original.Snapshot());
            Require(NativeNpcInventoryBridge.TryOpen(legacy,sourceId,null,0,out source,out reason),reason);
            var legacyItem=source.Items.GetItem(0);
            var legacyJob=new NpcActionJob {Id="fixture-legacy-transfer",ProposalId="fixture-legacy-offer",ResidentId=sourceId,
                Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=legacyItem.UID.ToString(),Quantity=1},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,ExpectedInventoryRevision=source.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(legacy, ref legacyJob, "Controlled legacy inventory fixture transfer.", out reason),reason);
            string json=SaveLoadManager.Serialize(typeof(WorldState),legacy.Snapshot());
            string withoutQuantity=NativeProbeText.WithoutJsonProperty(json,"Quantity");
            Require(withoutQuantity!=json,"Could not form schema-28 action fixture without quantity field.");
            var restoredLegacy=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),withoutQuantity);
            Require(restoredLegacy.NpcActions[0].Target.Quantity==1,"Schema-28 action did not default missing quantity to one.");
        }
        private static void VerifyStaleNpcTransferRevision(WorldStateStore original,string sourceId,string recipientId,bool staleSource)
        {
            var store=new WorldStateStore(0);store.Restore(original.Snapshot());
            NativeNpcInventoryBinding source,recipient;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,sourceId,null,0,out source,out reason),reason);
            Require(NativeNpcInventoryBridge.TryOpen(store,recipientId,null,0,out recipient,out reason),reason);
            var sourceItem=source.Items.GetItem(0);var recipientItem=recipient.Items.GetItem(0);
            int sourceCount=sourceItem.stackCount,recipientCount=recipientItem.stackCount;
            var job=new NpcActionJob {Id=staleSource?"fixture-stale-source":"fixture-stale-recipient",
                ProposalId=staleSource?"fixture-stale-source-offer":"fixture-stale-recipient-offer",ResidentId=sourceId,
                Kind=NpcActionKind.Transfer,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId=recipientId,
                    ItemId=sourceItem.UID.ToString(System.Globalization.CultureInfo.InvariantCulture),Quantity=2},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,
                ExpectedInventoryRevision=source.Revision,ExpectedTargetInventoryRevision=recipient.Revision};
            Require(NpcProbeCommandSource.TryAcceptDetachedAction(store, ref job, "Controlled inventory fixture transfer.", out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Stale revision action arrival failed.");
            string changedOwner=staleSource?sourceId:recipientId;
            var changed=store.ReadNpcInventory(changedOwner);
            Require(store.TryPayNpcService(changedOwner,1,"fixture-revision-change",changed.Revision),"Could not advance inventory revision fixture.");
            job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionTransfer(store,job,102,source,recipient,null,()=>true,out reason),
                "Stale NPC inventory revision permitted transfer.");
            Require(sourceItem.stackCount==sourceCount && recipientItem.stackCount==recipientCount,
                "Stale NPC inventory revision changed native item counts.");
        }
        private static void VerifyRejectedCorpseTransferRollback(WorldStateStore store,NativeNpcInventoryBinding binding,string owner)
        {
            // Keep this destructive rejection fixture independent of later wallet
            // assertions in Run, and locate the custom key by identity, not order.
            var isolated=new WorldStateStore(0);isolated.Restore(store.Snapshot());
            string openReason;NativeNpcInventoryBinding isolatedBinding;
            Require(NativeNpcInventoryBridge.TryOpen(isolated,owner,null,0,out isolatedBinding,out openReason),openReason);
            store=isolated;binding=isolatedBinding;
            DaggerfallUnityItem key=null;
            for(int i=0;i<binding.Items.Count;i++)
                if(HouseholdPropertyKeyItem.IsMatching(binding.Items.GetItem(i),100,10)){key=binding.Items.GetItem(i);break;}
            Require(key!=null && HouseholdPropertyKeyItem.IsMatching(key,100,10),"Fixture corpse key missing before rejected transfer.");
            Require(store.TryPayNpcService(owner,1,"stale-corpse-transfer",binding.Revision),"Could not create stale corpse inventory lease.");
            var target=new ItemCollection();bool committed=false;string reason;
            var transferHook=OpenTransferHook(binding.Items,
                (item,sourceCollection,destination)=>NativeNpcInventoryBridge.TryCapture(store,binding,out reason),
                (item,sourceCollection,destination)=>committed=true,
                (item,sourceCollection,destination)=>{string captureReason;if(!NativeNpcInventoryBridge.TryCapture(store,binding,out captureReason))throw new InvalidOperationException(captureReason);});
            try {target.TransferAll(binding.Items);}
            finally {transferHook.Dispose();}
            var restored=binding.Items.GetItem(key.UID);var saved=store.ReadNpcInventory(owner);
            Require(!committed && target.Count==0 && ReferenceEquals(restored,key) && HouseholdPropertyKeyItem.IsMatching(restored,100,10),
                "Rejected corpse transfer did not restore exact native key object and ownership.");
            Require(saved.Items.Exists(item=>item.Uid==key.UID) && binding.Revision==saved.Revision,
                "Rejected corpse transfer left persistent owner ledger stale.");

            var partialSource=new ItemCollection();var mergeTarget=new ItemCollection();
            var arrows=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);arrows.stackCount=5;partialSource.AddItem(arrows);
            var existing=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);existing.stackCount=3;mergeTarget.AddItem(existing);
            var split=partialSource.SplitStack(arrows,2);ulong splitUid=split.UID;
            transferHook=OpenTransferHook(partialSource,(item,from,to)=>false,(item,from,to)=>{},(item,from,to)=>{});
            try {mergeTarget.Transfer(split,partialSource);}
            finally {transferHook.Dispose();}
            Require(partialSource.GetItem(arrows.UID)==arrows && arrows.stackCount==3 && ReferenceEquals(partialSource.GetItem(splitUid),split) &&
                split.stackCount==2 && mergeTarget.GetItem(existing.UID)==existing && existing.stackCount==3,
                "Rejected merged partial-stack transfer changed source/target UID or counts.");
        }
        private static IDisposable OpenTransferHook(ItemCollection observed,
            Func<DaggerfallUnityItem,ItemCollection,ItemCollection,bool> persist,
            Action<DaggerfallUnityItem,ItemCollection,ItemCollection> committed,
            Action<DaggerfallUnityItem,ItemCollection,ItemCollection> rolledBack)
        {
            var type=Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeItemTransferHooks, PeopleOfTheIliacBay.QuestHooks",true);
            return (IDisposable)Activator.CreateInstance(type,new object[]{
                new Func<DaggerfallUnityItem,ItemCollection,ItemCollection,bool>((item,source,target)=>ReferenceEquals(source,observed)||ReferenceEquals(target,observed)),
                persist,committed,rolledBack});
        }
        private static void Require(bool pass,string message){if(!pass)throw new InvalidOperationException(message);}
    }
}
