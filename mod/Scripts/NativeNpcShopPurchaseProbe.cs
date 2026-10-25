using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcShopPurchaseProbe
    {
        // Standalone fixture objects; no player, scene, money or save mutation.
        public static string Run()
        {
            Verify(false);Verify(true);VerifySplitRollback();
            return "PEOPLE_OF_THE_ILIAC_BAY_NPC_SHOP_PURCHASE_PASS full-uid split-uid stock buyer-wallet seller-wallet deny stale rollback replay native-save later-handover";
        }
        private static void Verify(bool partial)
        {
            var store=new WorldStateStore(91);var town=store.GetOrCreate(1,new[]{10,20,30},1);
            string buyerId=town.Households[0].Residents[0].Id,sellerId=town.Households[1].Residents[0].Id;
            NativeNpcInventoryBinding buyer,seller;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,buyerId,null,50,out buyer,out reason),reason);
            Require(NativeNpcInventoryBridge.TryOpen(store,sellerId,null,10,out seller,out reason),reason);
            var stock=new ItemCollection();var item=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);
            item.stackCount=5;stock.AddItem(item);int count=partial?2:5;
            var job=new NpcActionJob {Id="shop-fixture",ProposalId="shop-request",ResidentId=buyerId,Kind=NpcActionKind.Purchase,
                ServiceCode=NpcShopCommerce.ServiceCode,ServicePrice=9,ExpectedInventoryRevision=buyer.Revision,ExpectedTargetInventoryRevision=seller.Revision,
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,
                Target=new NpcActionTarget{Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,PersonId=sellerId,ContainerId="123",ItemId=item.UID.ToString(),Quantity=count,QuotedGold=9}};
            var task=new NpcTaskPlan {Id="shop-task",ProposalId="shop-task-request",ResidentId=buyerId,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200};
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Purchase,Target=NpcActionExecutor.CopyTarget(job.Target)});
            task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Transfer,ItemSourceStep=1,Target=new NpcActionTarget {
                Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=job.Target.ItemId,Quantity=count}});
            job.Id=NpcTaskContract.ActionId(task);job.ProposalId=job.Id;
            Require(NpcProbeCommandSource.TryAcceptDetachedTask(store,ref task,
                "Buy the fixture stock, then hand it over.",out reason),reason);
            job.Id=NpcTaskContract.ActionId(task);job.ProposalId=job.Id;
            Require(store.TryDispatchNpcTaskStep(task.Id,task.Revision,100,job,true,true,true,out reason),reason);
            Require(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Arrival()),"Shop arrival failed.");job=store.ReadNpcAction(job.Id);
            Require(!NativeNpcInventoryBridge.TryCompleteActionPurchase(store,job,102,buyer,stock,()=>false,out reason),"Denied stock access succeeded.");
            Require(item.stackCount==5 && buyer.Items.Count==0 && store.ReadNpcInventory(buyerId).WalletGold==50,"Denied purchase changed ownership or money.");
            var prior=store.Snapshot();
            Require(NativeNpcInventoryBridge.TryPayService(store,buyerId,1,"stale-fixture",buyer.Revision,out reason),reason);
            Require(!NativeNpcInventoryBridge.TryCompleteActionPurchase(store,job,102,buyer,stock,()=>true,out reason),"Stale buyer wallet charged.");
            store.Restore(prior);
            Require(NativeNpcInventoryBridge.TryOpen(store,buyerId,null,0,out buyer,out reason),reason);
            // An external source must not duplicate any persisted owner, even
            // when a partial native split would acquire a different UID.
            string third=town.Households[2].Residents[0].Id;
            {
                var conflicting=new ItemCollection();conflicting.AddItem(item,noStack:true);
                Require(store.InitializeNpcInventory(third,0,NativeNpcInventoryBridge.CaptureItems(conflicting)),"Conflict fixture failed.");
                Require(!NativeNpcInventoryBridge.TryCompleteActionPurchase(store,job,102,buyer,stock,()=>true,out reason),"Duplicate native owner accepted, including partial split.");
                Require(ReferenceEquals(stock.GetItem(item.UID),item) && item.stackCount==5 && buyer.Items.Count==0 &&
                    store.ReadNpcInventory(buyerId).WalletGold==50 && store.ReadNpcInventory(sellerId).WalletGold==10,"Purchase rollback was incomplete.");
                store.Restore(prior);Require(NativeNpcInventoryBridge.TryOpen(store,buyerId,null,0,out buyer,out reason),reason);
            }
            Require(NativeNpcInventoryBridge.TryCompleteActionPurchase(store,job,102,buyer,stock,()=>true,out reason),reason);
            var bought=buyer.Items.GetItem(0);ulong boughtUid=bought.UID;
            Require(store.ReadNpcAction(job.Id).AcquiredItemId==boughtUid.ToString(),"Purchase result lost the actual acquired UID.");
            Require(bought.stackCount==count && (partial?stock.GetItem(item.UID).stackCount==3 && boughtUid!=item.UID:
                stock.GetItem(item.UID)==null && boughtUid==item.UID),"Purchase changed source UID/count incorrectly.");
            Require(store.ReadNpcInventory(buyerId).WalletGold==41 && store.ReadNpcInventory(sellerId).WalletGold==19,"Purchase debited wrong wallet.");
            var restoredStock=new ItemCollection();restoredStock.DeserializeItems(stock.SerializeItems());
            store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            Require(NativeNpcInventoryBridge.TryOpen(store,buyerId,null,999,out buyer,out reason),reason);
            Require(buyer.Items.GetItem(boughtUid).stackCount==count && restoredStock.Count==(partial?1:0),"Native purchase save lost ownership.");
            Require(!NativeNpcInventoryBridge.TryCompleteActionPurchase(store,job,103,buyer,restoredStock,()=>true,out reason),"Restored purchase replay succeeded.");
            Require(store.ReconcileNpcTask(task.Id,2,103),"Restored task could not reconcile confirmed purchase.");
            task=store.ReadNpcTask(task.Id);
            var handover=new NpcActionJob {Id=NpcTaskContract.ActionId(task),ProposalId=NpcTaskContract.ActionId(task),ResidentId=buyerId,Kind=NpcActionKind.Transfer,
                ExpectedInventoryRevision=buyer.Revision,Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=104,UpdatedAt=104,DeadlineAt=200,
                Target=store.ResolveNpcTaskTarget(task,task.CurrentStepIndex)};
            Require(handover.Target!=null && handover.Target.ItemId==boughtUid.ToString(),"Task did not bind the acquired split/full UID.");
            Require(store.TryDispatchNpcTaskStep(task.Id,task.Revision,104,handover,true,true,true,out reason),reason);
            Require(store.ObserveNpcAction(handover.Id,1,105,NpcActionObservation.Arrival()),"Handover arrival failed.");
            var player=new ItemCollection();handover=store.ReadNpcAction(handover.Id);
            Require(NativeNpcInventoryBridge.TryCompleteActionHandover(store,handover,106,buyer,player,()=>true,out reason),reason);
            Require(buyer.Items.Count==0 && player.GetItem(boughtUid).stackCount==count && store.ReadNpcInventory(buyerId).WalletGold==41,
                "Later handover did not move the purchased item exactly once.");
            Require(store.ReconcileNpcTask(task.Id,4,106) && store.ReadNpcTask(task.Id).Phase==NpcTaskPhase.Completed,"Purchase/handover task did not finish.");
            store.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
            Require(store.ReadNpcTask(task.Id).Phase==NpcTaskPhase.Completed,"Completed task lost its result binding after native serialization.");
        }
        private static void Require(bool value,string message) {if(!value) throw new InvalidOperationException(message);}

        private static void VerifySplitRollback()
        {
            var store=new WorldStateStore(92);var town=store.GetOrCreate(1,new[]{10,20},1);
            var initial=new ItemCollection();
            for(int i=0;i<NpcInventoryLimits.MaximumItems;i++) initial.AddItem(ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Dagger),noStack:true);
            NativeNpcInventoryBinding buyer,seller;string reason;
            Require(NativeNpcInventoryBridge.TryOpen(store,town.Households[0].Residents[0].Id,initial,50,out buyer,out reason),reason);
            Require(NativeNpcInventoryBridge.TryOpen(store,town.Households[1].Residents[0].Id,null,0,out seller,out reason),reason);
            var preserved=buyer.Items.GetItem(0);var stock=new ItemCollection();
            var arrow=ItemBuilder.CreateItem(ItemGroups.Weapons,(int)Weapons.Arrow);arrow.stackCount=5;stock.AddItem(arrow);
            Require(!NativeNpcInventoryBridge.TryPurchase(store,buyer,stock,seller.PersonId,seller.Revision,arrow.UID,2,3,()=>true,"split-rollback",out reason),
                "Purchase exceeded inventory limit.");
            Require(stock.Count==1 && ReferenceEquals(stock.GetItem(arrow.UID),arrow) && arrow.stackCount==5 &&
                buyer.Items.Count==NpcInventoryLimits.MaximumItems && ReferenceEquals(buyer.Items.GetItem(preserved.UID),preserved) &&
                store.ReadNpcInventory(buyer.PersonId).WalletGold==50 && store.ReadNpcInventory(seller.PersonId).WalletGold==0,
                "Rejected split purchase lost source, changed unrelated object, or charged money.");
        }

        // Explicit disposable Player fixture only. Caller enters an open shop,
        // opens native stock, stands near an optional managed adult, then runs
        // this coroutine. It never seeds items/funds or writes a save.
        public static IEnumerator RunScene(StreetPopulation streets,string expectedCharacter,string exactItemName,string residentId=null,PeopleOfTheIliacBayMod host=null,bool taskFailureProbe=false)
        {
            var game=GameManager.Instance;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-commerce-probe")>=0 &&
                expectedCharacter!=null && expectedCharacter.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal) &&
                game.PlayerEntity.Name==expectedCharacter && streets && streets.Ready,"Shop probe requires its owned fixture and explicit flag.");
            Require(!taskFailureProbe || Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-task-failure-probe")>=0,
                "Task failure scene requires its explicit Player flag.");
            if(taskFailureProbe) ClearTaskFailureHudMessages();
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            ResidentActor actor=null;NpcActionJob accepted=null;string reason=null;
            int playerGold=game.PlayerEntity.GetGoldAmount();
            foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(residentId!=null && candidate.Resident.Id!=residentId) continue;
                foreach(var option in streets.ConversationActionOptions(candidate.Resident.Id,"Buy one "+exactItemName+"."))
                {
                    if(option.Kind!=NpcActionKind.Purchase || option.Target.ItemId==null) continue;
                    if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-commerce-chat-probe")>=0)
                    {
                        yield return AcceptChatTask(host,streets,store,candidate,option,exactItemName);
                        actor=candidate;accepted=store.ActiveNpcAction(actor.Resident.Id);break;
                    }
                    if(taskFailureProbe)
                    {
                        var handover=NpcActionSequenceChat.PurchasedHandover(0,new[]{option});
                        Require(handover!=null,"Purchase did not expose a dependent handover option.");
                        var follow=new NpcActionOption {Kind=NpcActionKind.Follow,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"}};
                        var decisions=new List<NpcActionDecision> {
                            new NpcActionDecision {DurationSeconds=300,Option=option},
                            new NpcActionDecision {DurationSeconds=300,Option=handover},
                            new NpcActionDecision {DurationSeconds=10,Option=follow}};
                        if(!NpcProbeCommandSource.TryAcceptTask(streets,candidate.Resident.Id,"native-task-failure-probe:"+Guid.NewGuid().ToString("N"),decisions,out reason)) continue;
                    }
                    else
                    {
                        var handover=NpcActionSequenceChat.PurchasedHandover(0,new[]{option});
                        Require(handover!=null,"Purchase did not expose a dependent handover option.");
                        var clauses=new[]{"Buy one "+exactItemName,"give it to me"};
                        var decisions=NpcActionSequenceChat.ParseSemantic(clauses,new List<IList<NpcActionOption>>{new[]{option},new[]{handover}},
                            new[]{"DIRECT|Purchase|"+option.Id+"|"+option.Target.Quantity+"|300","DIRECT|Transfer|1|"+handover.Target.Quantity+"|300"});
                        if(!NpcProbeCommandSource.TryAcceptTask(streets,candidate.Resident.Id,"shop-native:"+Guid.NewGuid().ToString("N"),decisions,out reason)) continue;
                    }
                    actor=candidate;accepted=store.ActiveNpcAction(actor.Resident.Id);break;
                }
                if(actor) break;
            }
            Require(actor && accepted!=null,"No exact affordable shop item and optional nearby buyer accepted: "+reason);
            int failureBuyerGoldBefore=store.ReadNpcInventory(actor.Resident.Id).WalletGold;
            int failureSellerGoldBefore=NativeNpcInventoryBridge.InitialWalletGold(store,accepted.Target.PersonId);
            int failurePlayerGoldBefore=game.PlayerEntity.GetGoldAmount();
            string failurePlayerItemsBefore=taskFailureProbe?EncodeProbeItems(game.PlayerEntity.Items):null;
            ulong uid=ulong.Parse(accepted.Target.ItemId);DaggerfallLoot shelf=null;
            foreach(var loot in game.PlayerEnterExit.Interior.GetComponentsInChildren<DaggerfallLoot>())
                if(loot.LoadID.ToString()==accepted.Target.ContainerId) shelf=loot;
            Require(shelf,"Accepted shelf disappeared.");int stockBefore=shelf.Items.GetItem(uid).stackCount;
            var inventory=store.ReadNpcInventory(actor.Resident.Id);int goldBefore=inventory.WalletGold;
            var ownedBefore=new HashSet<ulong>();foreach(var record in inventory.Items) ownedBefore.Add(record.Uid);
            Vector3 start=actor.transform.position;float walked=0,maxStep=0,until=Time.realtimeSinceStartup+90;
            while(Time.realtimeSinceStartup<until)
            {
                float step=Vector3.Distance(start,actor.transform.position);walked+=step;maxStep=Mathf.Max(maxStep,step);start=actor.transform.position;
                if(NpcActionExecutor.IsTerminal(store.ReadNpcAction(accepted.Id))) break;
                yield return null;
            }
            var complete=store.ReadNpcAction(accepted.Id);
            Require(complete.Phase==NpcActionPhase.Completed,"Shop purchase did not complete: "+complete.FailureReason);
            Require(walked>0.5f && maxStep<1.5f,"Shop purchase did not physically approach stock without teleporting.");
            var remaining=shelf.Items.GetItem(uid);var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            Require((remaining==null?0:remaining.stackCount)==stockBefore-1 && store.ReadNpcInventory(actor.Resident.Id).WalletGold==goldBefore-accepted.ServicePrice &&
                game.PlayerEntity.GetGoldAmount()==playerGold,"Scene purchase stock or wallets differ from exact outcome.");
            DaggerfallUnityItem bought=null;
            for(int i=0;i<owner.Binding.Items.Count;i++) {var item=owner.Binding.Items.GetItem(i);if(!ownedBefore.Contains(item.UID)) bought=item;}
            Require(bought!=null && bought.stackCount==1 && bought.LongName==exactItemName,"Purchase did not retain exact item in NPC inventory.");
            Require(!NativeNpcInventoryBridge.TryCompleteActionPurchase(store,accepted,complete.UpdatedAt,owner.Binding,shelf.Items,()=>true,out reason),"Scene purchase replay succeeded.");
            if(taskFailureProbe)
                yield return VerifyTaskFailureAfterPurchase(game,host,store,actor,complete,bought,owner,stockBefore,failureBuyerGoldBefore,
                    failureSellerGoldBefore,failurePlayerGoldBefore,failurePlayerItemsBefore);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_SCENE_PASS item="+exactItemName+" sourceUid="+uid+" ownedUid="+bought.UID+" price="+accepted.ServicePrice+" walked="+walked.ToString("F2"));
        }

        private static IEnumerator AcceptChatTask(PeopleOfTheIliacBayMod host,StreetPopulation streets,WorldStateStore store,
            ResidentActor actor,NpcActionOption purchase,string itemName)
        {
            const BindingFlags privateMember=BindingFlags.Instance|BindingFlags.NonPublic;
            var game=GameManager.Instance;
            Require(host && (bool)typeof(PeopleOfTheIliacBayMod).GetField("localDialogueEnabled",privateMember).GetValue(host),
                "Live shop chat requires the enabled actual local dialogue client.");
            var busy=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",privateMember);
            string request="Could you buy one "+itemName+", then give it to me?";
            Require(store.ActiveNpcTask(actor.Resident.Id)==null,"Shop chat fixture already has an active task.");
            var previous=store.LatestNpcAction(actor.Resident.Id);
            Require(previous==null || NpcActionExecutor.IsTerminal(previous),"Shop chat fixture has an unfinished prior action.");
            var before=store.ReadNpcInventory(actor.Resident.Id);
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            Require(owner && owner.Binding!=null,"Shop chat has no native inventory owner.");
            var room=game.PlayerEnterExit.Interior;DaggerfallLoot stock=null;
            foreach(var shelf in room.GetComponentsInChildren<DaggerfallLoot>())if(shelf.LoadID.ToString()==purchase.Target.ContainerId)stock=shelf;
            Require(stock,"Shop chat stock disappeared before request.");
            string buyerItems=EncodeProbeItems(owner.Binding.Items),stockItems=EncodeProbeItems(stock.Items),playerItems=EncodeProbeItems(game.PlayerEntity.Items);
            int playerGold=game.PlayerEntity.GetGoldAmount(),sellerGold=NativeNpcInventoryBridge.InitialWalletGold(store,purchase.Target.PersonId);
            var position=actor.transform.position;
            var observer=game.PlayerMotor.transform.position;
            try
            {
                game.TalkManager.TalkToMobileNPC(actor.Person);
                if(!(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow))
                {
                    DaggerfallUI.Instance.PopToHUD();
                    typeof(TalkManager).GetField("reactionToPlayer",privateMember).SetValue(game.TalkManager,90);
                    typeof(TalkManager).GetField("alreadyRejectedOnce",privateMember).SetValue(game.TalkManager,false);
                    typeof(TalkManager).GetMethod("TalkToNpc",privateMember).Invoke(game.TalkManager,null);
                }
                yield return null;yield return null;
                var window=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                Require(window!=null && (string)typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId",privateMember).GetValue(host)==actor.Resident.Id,
                    "Shop chat opened the wrong speaker.");
                // Controlled friendly fixture. Both model classifiers remain authoritative for willingness.
                typeof(TalkManager).GetField("reactionToPlayer",privateMember).SetValue(game.TalkManager,90);
                typeof(PeopleOfTheIliacBayMod).GetField("conversationNativeDisposition",privateMember).SetValue(host,90);
                store.SetConversationBaseline(actor.Resident.Id,90,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                float until=Time.realtimeSinceStartup+60;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<until)yield return null;
                Require(!(bool)busy.GetValue(host),"Shop speaker greeting timed out.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_CHAT_OBSERVER: planned="+observer.ToString("F3")+"; afterWindow="+game.PlayerMotor.transform.position.ToString("F3"));
                yield return null;
                Require(Vector3.Distance(game.PlayerMotor.transform.position,observer)<0.05f &&
                    Vector3.Distance(actor.transform.position,observer)<8,"Native chat fixture observer did not remain near the speaker.");
                typeof(PeopleOfTheIliacBayMod).GetMethod("SubmitFreeText",privateMember).Invoke(host,new object[]{window,request});
                until=Time.realtimeSinceStartup+90;
                while((bool)busy.GetValue(host) && Time.realtimeSinceStartup<until)yield return null;
                string transcript=(string)typeof(PeopleOfTheIliacBayMod).GetMethod("ReadConversationHistory",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{window});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_CHAT_RESPONSE: request="+request+"\n"+transcript);
                Require(!(bool)busy.GetValue(host),"Live model shop task response timed out.");
                Require(transcript.Contains(request),"Shop request was missing from native conversation history.");
                var task=store.ActiveNpcTask(actor.Resident.Id);
                Require(task!=null && task.Steps.Count==2 && task.CurrentStepIndex==0 && task.Steps[0].Kind==NpcActionKind.Purchase &&
                    NpcTaskContract.TargetMatches(task.Steps[0].Target,purchase.Target) && task.Steps[1].Kind==NpcActionKind.Transfer &&
                    task.Steps[1].ItemSourceStep==1 && task.Steps[1].Target.PersonId=="player",
                    "Live model did not accept the exact composed purchase/handover task.");
                var job=store.ReadNpcAction(task.Id+":step:0");
                Require(job!=null && (previous==null || job.Id!=previous.Id),"Shop task reused a prior action instead of new chat consent.");
                until=Time.realtimeSinceStartup+2;
                do
                {
                    Require(DaggerfallUI.UIManager.TopWindow==window && store.ReadNpcAction(job.Id).Phase==NpcActionPhase.Accepted &&
                        store.ReadNpcAction(task.Id+":step:1")==null && Vector3.Distance(position,actor.transform.position)<0.05f &&
                        store.ReadNpcInventory(actor.Resident.Id).WalletGold==before.WalletGold &&
                        store.ReadNpcInventory(purchase.Target.PersonId).WalletGold==sellerGold && game.PlayerEntity.GetGoldAmount()==playerGold &&
                        EncodeProbeItems(owner.Binding.Items)==buyerItems && EncodeProbeItems(stock.Items)==stockItems &&
                        EncodeProbeItems(game.PlayerEntity.Items)==playerItems,"Shop task changed movement, stock, wallets or ownership while chat remained open.");
                    yield return null;
                }while(Time.realtimeSinceStartup<until);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_COMMERCE_CHAT_PASS: native-window actual-local-model; new polite two-clause consent; exact stock and dependent result; two-second no-effect observation before close; task="+task.Id);
            }
            finally {DaggerfallUI.Instance.PopToHUD();}
        }
        private static string EncodeProbeItems(ItemCollection items)
        {var data=items.SerializeItems();return SaveLoadManager.Serialize(data.GetType(),data,false);}
    }
}
