using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit disposable native disk/process fixture for a composed purchase
    // and dependent handover task.
    public static class NpcCommerceRestartProbe
    {
        private static readonly string ProcessToken=System.Diagnostics.Process.GetCurrentProcess().Id+":"+
            System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        private const string Character="PeopleOfTheIliacBayTest-9a5f197b";
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [Serializable] public sealed class Evidence
        {
            public string Process,Checkpoint,Job,Task,TaskProposal,Resident,Seller,Container,Source,Effect,Proposal,ItemName,AcquiredItemId;
            public string TaskPhase,TaskActiveActionId,PurchaseStepEffect,QueuedStepKind;
            public string BuyerItems,SellerItems,StockItems,PlayerItems;
            public int BuyerGold,SellerGold,PlayerGold,Price,SourceCount,TaskStepIndex,TaskStepCount,TaskItemSourceStep;
            public long TaskRevision;
            public ulong Bought;
        }
        private static WorldStateStore Store(StreetPopulation streets)
        {return (WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);}
        private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException("Commerce restart: "+why);}
        private static string Encode(object value){return SaveLoadManager.Serialize(value.GetType(),value,false);}
        private static string PathFor(string checkpoint)
        {return Path.Combine(Application.persistentDataPath,"PeopleOfTheIliacBay-commerce-restart-"+checkpoint+".json");}
        private static string Slot(string checkpoint){return "People - commerce restart "+checkpoint;}
        private static void Guard(string character)
        {
            Need(character==Character,"wrong disposable character");
            Need(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-commerce-restart-probe")>=0,"explicit restart flag missing");
        }
        private static DaggerfallLoot Stock(string container)
        {
            var room=GameManager.Instance.PlayerEnterExit.Interior;
            Need(room && room.EntryDoor.buildingKey==131343,"expected existing shop 131343");
            foreach(var shelf in room.GetComponentsInChildren<DaggerfallLoot>())if(shelf.LoadID.ToString()==container)return shelf;
            throw new InvalidOperationException("Commerce restart: saved stock container missing");
        }
        private static string NativeItems(ItemCollection items){return Encode(items.SerializeItems());}
        private static int Count(ItemCollection items,ulong uid){var item=items.GetItem(uid);return item==null?0:item.stackCount;}
        public static IEnumerator Prepare(StreetPopulation streets,string character,ResidentActor actor,NpcActionOption choice)
        {
            Guard(character);Need(GameManager.Instance.PlayerEntity.Name==character,"wrong loaded character");
            string reason;var store=Store(streets);
            var handover=NpcActionSequenceChat.PurchasedHandover(0,new[]{choice});
            Need(handover!=null,"purchase did not produce dependent handover option");
            Need(NpcProbeCommandSource.TryAcceptTask(streets,actor.Resident.Id,"commerce-restart:"+Guid.NewGuid().ToString("N"),
                new List<NpcActionDecision>{new NpcActionDecision{Option=choice,DurationSeconds=600},
                    new NpcActionDecision{Option=handover,DurationSeconds=300}},out reason),reason);
            var job=store.ActiveNpcAction(actor.Resident.Id);
            Need(job!=null && job.Phase==NpcActionPhase.Accepted,"purchase was not accepted before checkpoint");
            var shelf=Stock(job.Target.ContainerId);var source=shelf.Items.GetItem(ulong.Parse(job.Target.ItemId));
            Need(source!=null,"real source item missing");
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();owner.Capture();
            var task=store.ActiveNpcTask(actor.Resident.Id);
            Need(task!=null && task.Steps.Count==2 && task.Steps[0].Kind==NpcActionKind.Purchase &&
                task.Steps[1].Kind==NpcActionKind.Transfer && task.Steps[1].ItemSourceStep==1,"composed task or queued dependency missing");
            var evidence=new Evidence {Process=ProcessToken,Job=job.Id,Task=task.Id,TaskProposal=task.ProposalId,Resident=job.ResidentId,Seller=job.Target.PersonId,
                Container=job.Target.ContainerId,Source=job.Target.ItemId,Proposal=job.ProposalId,Price=job.ServicePrice,
                SourceCount=source.stackCount,ItemName=source.LongName};
            var before=new HashSet<ulong>();foreach(var item in store.ReadNpcInventory(job.ResidentId).Items)before.Add(item.Uid);
            yield return Save(streets,evidence,"accepted");
            yield return Finish(streets,job.Id);
            job=store.ReadNpcAction(job.Id);evidence.Effect=job.CompletedEffectId;evidence.AcquiredItemId=job.AcquiredItemId;
            Need(evidence.AcquiredItemId!=null,"completed purchase did not persist acquired item result");
            Need(store.ReadNpcInventory(evidence.Resident).WalletGold==evidence.BuyerGold-evidence.Price &&
                store.ReadNpcInventory(evidence.Seller).WalletGold==evidence.SellerGold+evidence.Price &&
                GameManager.Instance.PlayerEntity.GetGoldAmount()==evidence.PlayerGold,"prepared purchase wallets not conserved");
            for(int i=0;i<owner.Binding.Items.Count;i++)
            {var item=owner.Binding.Items.GetItem(i);if(!before.Contains(item.UID)){Need(evidence.Bought==0,"multiple purchased items");evidence.Bought=item.UID;}}
            Need(evidence.Bought!=0,"purchase did not produce exactly one owned item");
            Need(Count(shelf.Items,ulong.Parse(evidence.Source))==evidence.SourceCount-1,"stock decrement differs");
            var beforeNextDispatch=store.ReadNpcTask(evidence.Task);
            Need(beforeNextDispatch!=null &&
                (beforeNextDispatch.Phase==NpcTaskPhase.Running && beforeNextDispatch.CurrentStepIndex==0 && beforeNextDispatch.ActiveActionId==evidence.Job ||
                 beforeNextDispatch.Phase==NpcTaskPhase.Ready && beforeNextDispatch.CurrentStepIndex==1 && beforeNextDispatch.ActiveActionId==null) &&
                store.ReadNpcAction(evidence.Task+":step:1")==null,
                "queued transfer dispatched before completed-purchase checkpoint could be saved");
            yield return Save(streets,evidence,"completed");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMMERCE_RESTART_PREPARED: accepted and completed native disk slots; process="+ProcessToken);
        }
        private static IEnumerator Save(StreetPopulation streets,Evidence e,string checkpoint)
        {
            bool enabled=streets.enabled;streets.enabled=false;
            bool saved=false;SaveLoadManager.OnSaveEventHandler handler=ignored=>saved=true;SaveLoadManager.OnSave+=handler;
            try
            {
                var store=Store(streets);var actor=streets.FindActor(e.Resident);actor.GetComponent<NativeNpcInventoryOwner>().Capture();
                var buyer=store.ReadNpcInventory(e.Resident);var seller=store.ReadNpcInventory(e.Seller);
                var task=store.ReadNpcTask(e.Task);Need(task!=null && task.Steps.Count==2,"task missing at disk checkpoint");
                e.Checkpoint=checkpoint;e.BuyerGold=buyer.WalletGold;e.SellerGold=seller.WalletGold;
                e.TaskPhase=task.Phase.ToString();e.TaskActiveActionId=task.ActiveActionId;e.TaskRevision=task.Revision;
                e.TaskStepIndex=task.CurrentStepIndex;e.TaskStepCount=task.Steps.Count;e.QueuedStepKind=task.Steps[1].Kind.ToString();
                e.TaskItemSourceStep=task.Steps[1].ItemSourceStep;e.PurchaseStepEffect=task.Steps[0].CompletedEffectId;
                e.AcquiredItemId=store.ReadNpcAction(e.Job).AcquiredItemId;
                e.PlayerGold=GameManager.Instance.PlayerEntity.GetGoldAmount();e.BuyerItems=Encode(buyer.Items);e.SellerItems=Encode(seller.Items);
                e.StockItems=NativeItems(Stock(e.Container).Items);e.PlayerItems=NativeItems(GameManager.Instance.PlayerEntity.Items);
                GameManager.Instance.SaveLoadManager.Save(Character,Slot(checkpoint));
                float until=Time.realtimeSinceStartup+30;while(!saved && Time.realtimeSinceStartup<until)yield return null;
                Need(saved,"disk save timeout");
                File.WriteAllText(PathFor(checkpoint),Encode(e));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMMERCE_RESTART_SAVE: checkpoint="+checkpoint+"; job="+e.Job+"; evidence="+PathFor(checkpoint));
            }
            finally {SaveLoadManager.OnSave-=handler;streets.enabled=enabled;}
        }
        private static IEnumerator Finish(StreetPopulation streets,string id)
        {
            float until=Time.realtimeSinceStartup+150;var store=Store(streets);
            while(!NpcActionExecutor.IsTerminal(store.ReadNpcAction(id)) && Time.realtimeSinceStartup<until)yield return null;
            var job=store.ReadNpcAction(id);Need(job!=null && job.Phase==NpcActionPhase.Completed,"original action failed/expired: "+(job==null?"missing":job.FailureReason));
        }
        public static IEnumerator Resume(StreetPopulation streets,string character,string checkpoint)
        {
            Guard(character);Need(checkpoint=="accepted" || checkpoint=="completed","unknown checkpoint");
            Need(File.Exists(PathFor(checkpoint)),"prepare evidence missing");
            var e=(Evidence)SaveLoadManager.Deserialize(typeof(Evidence),File.ReadAllText(PathFor(checkpoint)));
            Need(e.Process!=ProcessToken && e.Checkpoint==checkpoint,"resume must run in an independent process");
            var game=GameManager.Instance;game.SaveLoadManager.EnumerateSaves();
            int slot=game.SaveLoadManager.FindSaveFolderByNames(character,Slot(checkpoint));Need(slot>=0,"disk slot missing");
            bool loaded=false;SaveLoadManager.OnLoadEventHandler handler=ignored=>loaded=true;SaveLoadManager.OnLoad+=handler;
            try
            {
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                float until=Time.realtimeSinceStartup+120;while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<until)yield return null;
                Need(loaded && streets.Ready && game.PlayerEntity.Name==character,"disk load failed");
                // Ready describes the schedule build; actors materialize on the
                // next population update. Initial scene setup requires unpaused updates.
                until=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<until)
                {
                    var body=streets.FindActor(e.Resident);
                    if(body && body.gameObject.activeInHierarchy && body.GetComponent<NativeNpcInventoryOwner>() &&
                        body.GetComponent<NativeNpcInventoryOwner>().Binding!=null){game.PauseGame(true);break;}
                    yield return null;
                }
            }
            finally {SaveLoadManager.OnLoad-=handler;}
            var store=Store(streets);var job=store.ReadNpcAction(e.Job);var buyer=store.ReadNpcInventory(e.Resident);var seller=store.ReadNpcInventory(e.Seller);
            var task=store.ReadNpcTask(e.Task);
            Need(job!=null && job.ProposalId==e.Proposal && job.ResidentId==e.Resident && job.Target.ItemId==e.Source &&
                job.Target.PersonId==e.Seller && job.Target.ContainerId==e.Container && job.ServicePrice==e.Price,"original purchase identity changed");
            Need(task!=null && task.Id==e.Task && task.ProposalId==e.TaskProposal && task.ResidentId==e.Resident && task.Steps.Count==e.TaskStepCount &&
                task.Steps.Count==2 && task.Steps[0].Kind==NpcActionKind.Purchase && task.Steps[1].Kind==NpcActionKind.Transfer &&
                task.Steps[1].ItemSourceStep==e.TaskItemSourceStep && task.Steps[1].ItemSourceStep==1 &&
                task.Steps[1].Target.PersonId=="player","restored task identity or queued dependency changed");
            Need(buyer.WalletGold==e.BuyerGold && seller.WalletGold==e.SellerGold && game.PlayerEntity.GetGoldAmount()==e.PlayerGold,"restored wallets differ");
            var shelf=Stock(e.Container);
            Need(Encode(buyer.Items)==e.BuyerItems && Encode(seller.Items)==e.SellerItems && NativeItems(shelf.Items)==e.StockItems &&
                NativeItems(game.PlayerEntity.Items)==e.PlayerItems,"restored exact inventory payloads differ");
            var actor=streets.FindActor(e.Resident);Need(actor && actor.gameObject.activeInHierarchy,"restored buyer body missing");
            int bodies=0;foreach(var body in UnityEngine.Object.FindObjectsOfType<ResidentActor>())if(body.gameObject.activeInHierarchy && body.Resident.Id==e.Resident)bodies++;
            Need(bodies==1,"restored buyer body duplicated");
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();Need(owner && owner.Binding!=null,"restored inventory binding missing");
            game.PlayerEntity.GodMode=true;game.PauseGame(true);
            var resolved=store.ResolveNpcTaskTarget(task,1);
            Need((e.TaskPhase=="Running" && e.TaskStepIndex==0 && e.TaskActiveActionId==e.Job ||
                checkpoint=="completed" && e.TaskPhase=="Ready" && e.TaskStepIndex==1 && e.TaskActiveActionId==null) &&
                e.QueuedStepKind==NpcActionKind.Transfer.ToString() && e.TaskItemSourceStep==1,
                "saved task progress or queued-step evidence differs");
            if(checkpoint=="accepted")Need(resolved==null && job.AcquiredItemId==null,"accepted checkpoint fabricated purchase result");
            else Need(resolved!=null && resolved.Kind==NpcActionTargetKind.Person && resolved.PersonId=="player" &&
                resolved.ItemId==e.AcquiredItemId && resolved.Quantity==1,"restored queued transfer did not resolve to actual purchase result");
            Need(job.AcquiredItemId==null && checkpoint=="accepted" || job.AcquiredItemId==e.AcquiredItemId && e.AcquiredItemId==e.Bought.ToString(),
                "restored purchase acquired-item result differs");
            if(checkpoint=="accepted")
                Need(!NpcActionExecutor.IsTerminal(job) && job.CompletedEffectId==null,"accepted checkpoint already committed before inspection");
            else
            {
                bool taskProgressValid=(task.CurrentStepIndex==0 && task.Phase==NpcTaskPhase.Running && task.ActiveActionId==e.Job &&
                    task.Steps[0].CompletedEffectId==null && e.TaskStepIndex==0 && e.PurchaseStepEffect==null) ||
                    (task.CurrentStepIndex==1 && (task.Phase==NpcTaskPhase.Ready || task.Phase==NpcTaskPhase.Running) &&
                    task.Steps[0].CompletedEffectId==job.CompletedEffectId &&
                    (e.TaskStepIndex==0 || e.TaskStepIndex==1 && e.PurchaseStepEffect==task.Steps[0].CompletedEffectId) &&
                    task.Steps[1].CompletedEffectId==null);
                var queuedAction=store.ReadNpcAction(e.Task+":step:1");
                string restored="phase="+job.Phase+"; effect="+job.CompletedEffectId+"; expectedEffect="+e.Effect+"; bought="+e.Bought+
                    "; nativeCount="+Count(owner.Binding.Items,e.Bought)+"; bindingOwner="+owner.Binding.PersonId+"; bindingRevision="+owner.Binding.Revision+
                    "; storeRevision="+buyer.Revision+"; storeItems="+Encode(buyer.Items)+"; nativeItems="+NativeItems(owner.Binding.Items)+
                    "; savedTask="+e.TaskPhase+"/"+e.TaskStepIndex+"/"+e.PurchaseStepEffect+"; restoredTask="+task.Phase+"/"+
                    task.CurrentStepIndex+"/"+task.Steps[0].CompletedEffectId+"/"+task.Steps[1].CompletedEffectId+
                    "; queuedAction="+(queuedAction==null?"missing":queuedAction.Phase.ToString());
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMMERCE_RESTART_COMPLETED_RESTORE: "+restored);
                Need(job.Phase==NpcActionPhase.Completed && job.CompletedEffectId==e.Effect && job.AcquiredItemId==e.AcquiredItemId &&
                    Count(owner.Binding.Items,e.Bought)==1 && taskProgressValid,
                    "completed effect, acquired result, or task progress lost: "+restored);
            }
            Need(task.Id==e.Task && task.Steps[0].Kind==NpcActionKind.Purchase && task.Steps[1].Kind==NpcActionKind.Transfer &&
                task.Steps[1].ItemSourceStep==1 && task.Steps[1].Target.ItemId==e.Source,
                "restored task definition or queued step changed");
            if(task.CurrentStepIndex==0)
                Need(task.Phase==NpcTaskPhase.Running && task.ActiveActionId==e.Job,"restored purchase task progress changed");
            else Need(task.CurrentStepIndex==1 && (task.Phase==NpcTaskPhase.Ready || task.Phase==NpcTaskPhase.Running) &&
                task.Steps[0].CompletedEffectId==job.CompletedEffectId,"restored task did not advance from completed purchase");
            job=store.ReadNpcAction(e.Job);string reason;
            string stockNow=NativeItems(shelf.Items),buyerNow=Encode(store.ReadNpcInventory(e.Resident)),sellerNow=Encode(store.ReadNpcInventory(e.Seller));
            Need(!NativeNpcInventoryBridge.TryCompleteActionPurchase(store,job,job.UpdatedAt,owner.Binding,shelf.Items,()=>true,out reason),"purchase replay accepted");
            Need(stockNow==NativeItems(shelf.Items) && buyerNow==Encode(store.ReadNpcInventory(e.Resident)) && sellerNow==Encode(store.ReadNpcInventory(e.Seller)),"replay mutated stock/wallets");
            game.PauseGame(false);
            yield return ContinueComposedTask(streets,actor,e);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMMERCE_RESTART_PASS: checkpoint="+checkpoint+"; originalTask="+e.Task+"; purchase="+e.Job+
                "; acquired="+e.Bought+"; independent-process task-progress queued-transfer physical exactly-once");
        }        private static void DiagnoseAccess(StreetPopulation streets,NpcActionJob job,ResidentActor actor,DaggerfallLoot shelf)
        {
            var type=typeof(StreetPopulation);var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;
            object[] sellerArgs={null};bool seller=(bool)type.GetMethod("TryShopSeller",Private).Invoke(streets,sellerArgs);
            object[] quoteArgs={actor,shelf.Items.GetItem(ulong.Parse(job.Target.ItemId)),job.Target.Quantity,0};
            bool quote=(bool)type.GetMethod("QuoteShopItem",Private).Invoke(streets,quoteArgs);
            bool visible=(bool)type.GetMethod("ShopShelfVisible",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{actor.transform.position,shelf});
            bool verified=(bool)type.GetMethod("VerifiedShopStock",Private).Invoke(streets,new object[]{shelf});
            bool quest=(bool)type.GetMethod("QuestOwned",Private).Invoke(streets,new object[]{actor.Resident});
            bool alarm=streets.IsAlarmed(job.ResidentId);
            var target=room.transform.TransformPoint(new Vector3((float)job.Target.X,(float)job.Target.Y,(float)job.Target.Z));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMMERCE_RESTART_ACCESS: alive="+actor.Resident.IsAlive+"; quest="+quest+"; alarm="+alarm+
                "; stock="+verified+"; seller="+seller+"; sellerId="+(sellerArgs[0]==null?"null":((NativePerson)sellerArgs[0]).Id)+"; expectedSeller="+job.Target.PersonId+
                "; discoveryBuilding="+game.PlayerEnterExit.BuildingDiscoveryData.buildingKey+"; entryBuilding="+room.EntryDoor.buildingKey+
                "; quality="+game.PlayerEnterExit.BuildingDiscoveryData.quality+"; shelfDistance="+Vector3.Distance(actor.transform.position,shelf.transform.position)+
                "; targetDistance="+Vector3.Distance(actor.transform.position-Vector3.up,target)+"; visible="+visible+"; quote="+quote+"; price="+quoteArgs[3]+"; expectedPrice="+job.ServicePrice+
                "; interiorGeometry="+(type.GetField("indoorGeometry",Private).GetValue(streets)!=null)+"; shopGeometry="+(type.GetField("shopGeometry",Private).GetValue(streets)!=null));
        }
        private static IEnumerator ContinueComposedTask(StreetPopulation streets,ResidentActor actor,Evidence e)
        {
            var game=GameManager.Instance;var store=Store(streets);var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            Vector3 previous=actor.transform.position;float walked=0,maxStep=0,until=Time.realtimeSinceStartup+240;
            var task=store.ReadNpcTask(e.Task);
            while(task!=null && task.Phase!=NpcTaskPhase.Completed && task.Phase!=NpcTaskPhase.Failed && Time.realtimeSinceStartup<until)
            {
                yield return null;
                float step=Vector3.Distance(previous,actor.transform.position);walked+=step;maxStep=Mathf.Max(maxStep,step);previous=actor.transform.position;
                task=store.ReadNpcTask(e.Task);
            }
            var purchase=store.ReadNpcAction(e.Job);var handover=store.ReadNpcAction(e.Task+":step:1");task=store.ReadNpcTask(e.Task);
            Need(purchase!=null && purchase.AcquiredItemId!=null,"resumed purchase has no confirmed acquired result");
            ulong acquired=ulong.Parse(purchase.AcquiredItemId);
            if(e.Bought==0)e.Bought=acquired;
            Need(e.Bought==acquired,"resumed purchase UID differs from prepared evidence");
            Need(purchase!=null && handover!=null && task!=null && task.Phase==NpcTaskPhase.Completed && task.CurrentStepIndex==2 && task.ActiveActionId==null &&
                task.Steps[0].CompletedEffectId==purchase.CompletedEffectId && task.Steps[1].CompletedEffectId==handover.CompletedEffectId,
                "original task did not reconcile both effects: "+(task==null?"missing":task.Phase+"/"+task.CurrentStepIndex));
            Need(purchase!=null && purchase.Phase==NpcActionPhase.Completed && purchase.AcquiredItemId==e.Bought.ToString() &&
                handover!=null && handover.Kind==NpcActionKind.Transfer && handover.Phase==NpcActionPhase.Completed &&
                handover.Target.ItemId==e.Bought.ToString() && walked>0.5f && maxStep<1.5f &&
                Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)<=1.5f,
                "queued transfer did not complete with physical approach or exact acquired UID");
            Need(Count(owner.Binding.Items,e.Bought)==0 && Count(game.PlayerEntity.Items,e.Bought)==1 &&
                game.PlayerEntity.GetGoldAmount()==e.PlayerGold &&
                store.ReadNpcInventory(e.Resident).WalletGold==e.BuyerGold-(e.Checkpoint=="accepted"?e.Price:0) &&
                store.ReadNpcInventory(e.Seller).WalletGold==e.SellerGold+(e.Checkpoint=="accepted"?e.Price:0) &&
                Count(Stock(e.Container).Items,ulong.Parse(e.Source))==e.SourceCount-1,
                "composed task stock, ownership or wallet invariants failed");
            string reason;
            Need(!NativeNpcInventoryBridge.TryCompleteActionHandover(store,handover,handover.UpdatedAt,owner.Binding,game.PlayerEntity.Items,()=>true,out reason),
                "handover replay accepted");
            Need(Count(owner.Binding.Items,e.Bought)==0 && Count(game.PlayerEntity.Items,e.Bought)==1 &&
                store.ReadNpcInventory(e.Resident).WalletGold==e.BuyerGold-(e.Checkpoint=="accepted"?e.Price:0) &&
                store.ReadNpcInventory(e.Seller).WalletGold==e.SellerGold+(e.Checkpoint=="accepted"?e.Price:0),
                "handover replay mutated ownership or wallets");
        }
    }
}
