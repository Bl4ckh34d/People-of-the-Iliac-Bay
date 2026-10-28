using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

// Controlled native item/scene host. Core transactions and extracted adapter methods run unchanged.
public static class DaggerfallUnity {public static ulong NextUID=>++next;private static ulong next=1000;}
namespace DaggerfallWorkshop {public static class DaggerfallUnity {public static ulong NextUID=>global::DaggerfallUnity.NextUID;}}
public sealed class ItemData {public ulong uid;public int stackCount;public string className;public int value=17;}
public class DaggerfallUnityItem
{
    public ulong UID;public int stackCount=1,value=17,TemplateIndex=1;public bool IsQuestItem,IsEquipped,IsSummoned;
    public string LongName="Potion";public virtual bool IsAStack()=>true;
    public ItemData GetSaveData()=>new ItemData{uid=UID,stackCount=stackCount,value=value};
}
public sealed class NativeMarketGoodsItem:DaggerfallUnityItem {public int GoodsArchive,GoodsRecord;public NativeMarketGoodsItem(int a,int r){GoodsArchive=a;GoodsRecord=r;UID=DaggerfallUnity.NextUID;}}
public sealed class HouseholdPropertyKeyItem:DaggerfallUnityItem {}
public static class ItemHelper {public const int LastDFTemplate=1000;}
public static class NativeItemReceiptLabel {public static string Capture(string value)=>value;}
public sealed class ItemCollection
{
    private readonly List<DaggerfallUnityItem> items=new List<DaggerfallUnityItem>();
    public int Count=>items.Count;public DaggerfallUnityItem GetItem(ulong uid)=>items.Find(i=>i.UID==uid);
    public DaggerfallUnityItem GetItem(int index)=>items[index];public bool Contains(DaggerfallUnityItem item)=>items.Contains(item);
    public void AddItem(DaggerfallUnityItem item,bool noStack=false){items.Add(item);}public void RemoveItem(DaggerfallUnityItem item){items.Remove(item);}
    public void Transfer(DaggerfallUnityItem item,ItemCollection source){source.RemoveItem(item);AddItem(item);}
    public DaggerfallUnityItem SplitStack(DaggerfallUnityItem item,int count){item.stackCount-=count;var split=new DaggerfallUnityItem{UID=DaggerfallUnity.NextUID,stackCount=count};AddItem(split);return split;}
    public void DeserializeItems(ItemData[] data){items.Clear();foreach(var row in data)AddItem(new DaggerfallUnityItem{UID=row.uid,stackCount=row.stackCount,value=row.value});}
    private DaggerfallUnityItem FindExistingStack(DaggerfallUnityItem item)=>null;
}
public sealed class NativeNpcInventoryBinding {public string PersonId;public long Revision;public ItemCollection Items=new ItemCollection();}
public static class NativeNpcInventoryBridge
{
    private static readonly MethodInfo findStack=typeof(ItemCollection).GetMethod("FindExistingStack",BindingFlags.NonPublic|BindingFlags.Instance);
    public static List<NpcInventoryItem> CaptureItems(ItemCollection items){var result=new List<NpcInventoryItem>();for(int i=0;i<items.Count;i++){var item=items.GetItem(i);result.Add(new NpcInventoryItem{Uid=item.UID,NativePayload=item.value+":"+item.stackCount});}return result;}
    private static void TryRecordExactPlayerGiftSource(params object[] ignored){}
    // PURCHASE_METHODS
}
public struct Vector3
{
    public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 up=>new Vector3(0,1,0);
    public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator*(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
}
public sealed class Transform {public Vector3 position;public Vector3 TransformPoint(Vector3 point)=>point;}
public sealed class BedAnchor {public Vector3 LocalCenter;}
public sealed class Interior {public Transform transform=new Transform();}
public sealed class SleepingArt
{
    public sealed class Atlas {public object Texture;public int[] Views;}
    public Atlas Available;public bool Throws;public Atlas ForResident(Resident resident){if(Throws)throw new Exception("Art unavailable");return Available;}
}
public sealed class ResidentActor {public Resident Resident=new Resident();public bool IsSleeping;public Transform transform=new Transform();public void Sleep(Transform parent,BedAnchor bed,SleepingArt.Atlas atlas){IsSleeping=true;transform.position=bed.LocalCenter+Vector3.up*0.18f;}}
public sealed class RestFixture
{
    private Interior interior=new Interior();private SleepingArt sleepingArt=new SleepingArt();private bool valid=true;private int ended;
    private bool TryValidateHealthRestTarget(NpcActionJob job,ResidentActor actor,double now,out BedAnchor bed,out string reason){bed=new BedAnchor();reason=null;return valid;}
    private void EndAction(NpcActionJob job,double now,string reason,bool failed){ended++;}
    // REST_METHODS
    public static void Run()
    {
        var f=new RestFixture();var actor=new ResidentActor{transform=new Transform{position=new Vector3(0,1,0)}};
        var job=new NpcActionJob{Kind=NpcActionKind.RestToHeal,Phase=NpcActionPhase.Executing};
        Fixture.Check(f.IsHealthRestPoseAtTarget(job,actor,10),"standing beside assigned bed permits native rest");
        f.UpdateHealthRestPose(job,actor,10);Fixture.Check(f.ended==0,"missing sleeping art never ends rest");
        f.sleepingArt.Throws=true;try{f.UpdateHealthRestPose(job,actor,11);Fixture.Check(f.ended==0,"unloadable sleeping art never ends rest");}catch{Fixture.Check(false,"unloadable sleeping art never ends rest");}
        f.valid=false;Fixture.Check(!f.IsHealthRestPoseAtTarget(job,actor,12),"lost bed access rejects rest");
        f.UpdateHealthRestPose(job,actor,12);Fixture.Check(f.ended==1,"lost bed access interrupts rest");
        f.valid=true;f.sleepingArt.Throws=false;f.sleepingArt.Available=new SleepingArt.Atlas{Texture=new object(),Views=new[]{1}};
        f.UpdateHealthRestPose(job,actor,13);Fixture.Check(actor.IsSleeping,"available sleeping art still renders native bed pose");
    }
}
public static class Fixture
{
    private static int failures,checks;
    public static void Check(bool value,string message){checks++;if(!value){failures++;Console.WriteLine("FAIL "+message);}else Console.WriteLine("PASS "+message);}
    public static int Run()
    {
        RestFixture.Run();
        var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20},1);
        string buyerId=town.Households[0].Residents[0].Id,sellerId=town.Households[1].Residents[0].Id;
        store.InitializeNpcInventory(buyerId,100,new NpcInventoryItem[0]);store.InitializeNpcInventory(sellerId,50,new NpcInventoryItem[0]);
        var buyer=new NativeNpcInventoryBinding{PersonId=buyerId};var stock=new ItemCollection();var item=new DaggerfallUnityItem{UID=77,stackCount=2};stock.AddItem(item);
        string reason;Check(NativeNpcInventoryBridge.TryPurchase(store,buyer,stock,sellerId,0,77,1,10,()=>true,"buy-1",out reason),"first native NPC purchase succeeds: "+reason);
        Check(stock.GetItem(77UL)==item && item.stackCount==2 && stock.Count==1,"NPC purchase preserves actual native stock object and quantity");
        Check(store.ReadNpcInventory(buyerId).WalletGold==90 && store.ReadNpcInventory(sellerId).WalletGold==60,"NPC purchase debits buyer and credits seller exactly once");
        Check(buyer.Items.Count==1 && buyer.Items.GetItem(0).UID!=77 && store.NpcInventoryItemOwner(buyer.Items.GetItem(0).UID)==buyerId,"purchased item has distinct persisted owned UID");
        Check(NativeNpcInventoryBridge.TryPurchase(store,buyer,stock,sellerId,1,77,1,10,()=>true,"buy-2",out reason),"second NPC can buy same native stock");
        Check(item.stackCount==2 && buyer.Items.Count==2 && buyer.Items.GetItem(0).UID!=buyer.Items.GetItem(1).UID,"repeated purchase preserves stock and distinct owned items");
        Check(!NativeNpcInventoryBridge.TryPurchase(store,buyer,stock,sellerId,2,77,1,10,()=>true,"buy-2",out reason),"replay never charges twice");
        Check(!NativeNpcInventoryBridge.TryPurchase(store,buyer,stock,sellerId,2,77,1,10,()=>false,"denied",out reason),"lost native access prevents purchase");
        int accessCalls=0;
        Check(!NativeNpcInventoryBridge.TryPurchase(store,buyer,stock,sellerId,2,77,1,10,()=>++accessCalls<4,"late-denied",out reason),"lost access at commit rolls back newly added item");
        Check(buyer.Items.Count==2 && store.ReadNpcInventory(buyerId).WalletGold==80,"late rejection restores live and saved inventory");
        Check(!NativeNpcInventoryBridge.TryPurchase(store,buyer,stock,sellerId,2,77,1,1000,()=>true,"poor",out reason),"insufficient wallet prevents purchase");
        item.IsQuestItem=true;Check(!NativeNpcInventoryBridge.TryPurchase(store,buyer,stock,sellerId,2,77,1,10,()=>true,"quest",out reason),"quest stock is protected");
        Check(store.ReadNpcInventory(buyerId).WalletGold==80 && buyer.Items.Count==2 && item.stackCount==2,"rejected purchases preserve money inventory and stock");
        var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
        Check(restored.ReadNpcInventory(buyerId).Items[0].Uid==buyer.Items.GetItem(0).UID,"current save restore preserves acquired UID");
        var provider=store.RegisterNativePerson(1,99,600,"Guild Healer",ResidentSex.Female,1,88,182,1);
        store.InitializeNpcInventory(provider.Id,50,new NpcInventoryItem[0]);
        var vitals=store.ReadNpcVitals(buyerId,NpcArchetype.Citizen);
        store.TryCaptureNpcVitals(buyerId,NpcArchetype.Citizen,Math.Max(1,vitals.MaximumHealth/2),vitals.CurrentMagicka,90,vitals.Revision);
        NpcGuildPotionOffer offer;
        Check(NpcGuildPotionSupply.TryCreateOffer(store,buyerId,NpcArchetype.Citizen,provider.Id,42,10,100,out offer),"native guild offer needs no generated provider stock");
        Check(NpcGuildPotionSupply.TryCreateOffer(store,buyerId,NpcArchetype.Citizen,provider.Id,42,10,100,out offer),"guild offer has no daily stock generation or exhaustion");
        Check(store.ReadNpcInventory(provider.Id).Items.Count==0 && store.ReadNpcInventory(provider.Id).Revision==0,"quoting guild potion never materializes provider inventory");
        var nativePotion=new DaggerfallUnityItem{UID=DaggerfallUnity.NextUID};
        Check(NativeNpcInventoryBridge.TryPurchaseCreatedItem(store,buyer,provider.Id,0,nativePotion,10,()=>true,"guild-buy",out reason),"guild supply buys one real native potion without seller stock");
        Check(buyer.Items.GetItem(nativePotion.UID)==nativePotion && store.NpcInventoryItemOwner(nativePotion.UID)==buyerId && store.ReadNpcInventory(buyerId).WalletGold==70 && store.ReadNpcInventory(provider.Id).WalletGold==60,"guild supply persists exact native item and both actual wallets");
        Check(store.ReadNpcInventory(provider.Id).Items.Count==0,"guild purchase never reserves or removes provider items");
        Check(!NativeNpcInventoryBridge.TryPurchaseCreatedItem(store,buyer,provider.Id,1,nativePotion,10,()=>true,"guild-duplicate",out reason),"owned potion UID cannot be bought twice");
        item.IsQuestItem=false;
        var action=new NpcActionJob{Id="action-buy",ProposalId="action-request",ResidentId=buyerId,Kind=NpcActionKind.Purchase,
            ServiceCode=NpcShopCommerce.ServiceCode,ServicePrice=10,ExpectedInventoryRevision=buyer.Revision,
            ExpectedTargetInventoryRevision=2,Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200,
            Target=new NpcActionTarget{Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,PersonId=sellerId,ContainerId="123",ItemId="77",Quantity=2,QuotedGold=10}};
        Check(Program.Accept(store,action,out reason),"purchase action accepts classified native intent: "+reason);
        Check(store.ObserveNpcAction(action.Id,1,101,NpcActionObservation.Arrival()),"purchase action observes real arrival");
        action=store.ReadNpcAction(action.Id);
        Check(NativeNpcInventoryBridge.TryCompleteActionPurchase(store,action,102,buyer,stock,()=>true,out reason),"purchase completion records native item outcome: "+reason);
        var completed=store.ReadNpcAction(action.Id);ulong acquired;
        Check(completed.Phase==NpcActionPhase.Completed && ulong.TryParse(completed.AcquiredItemId,out acquired) && acquired!=77 &&
            buyer.Items.GetItem(acquired).stackCount==2 && item.stackCount==2,"whole-stack action binds acquired UID while preserving native stock");
        Console.WriteLine((checks-failures)+"/"+checks+" passed");return failures==0?0:1;
    }
}
