using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

public static class DaggerfallUnity {private static ulong next=1000;public static ulong NextUID=>++next;}
namespace DaggerfallWorkshop {public static class DaggerfallUnity {public static ulong NextUID=>global::DaggerfallUnity.NextUID;}}
public sealed class ItemData {public ulong uid;public int stackCount,value;public string className;public int archive,record;}
public class DaggerfallUnityItem
{
    public ulong UID;public int stackCount=1,value=17,TemplateIndex=1;public bool IsQuestItem,IsEquipped,IsSummoned,IsArtifact;
    public ItemGroups ItemGroup;public RepairData RepairData=new RepairData();
    public string LongName="Goods";public bool IsAStack()=>true;
    public ItemData GetSaveData(){var g=this as NativeMarketGoodsItem;return new ItemData{uid=UID,stackCount=stackCount,value=value,className=g==null?null:"market",archive=g==null?0:g.GoodsArchive,record=g==null?0:g.GoodsRecord};}
}
public enum ItemGroups {Weapons,MiscItems}
public sealed class RepairData {public bool Held;public bool IsBeingRepaired()=>Held;}
public sealed class NativeMarketGoodsItem:DaggerfallUnityItem {public int GoodsArchive,GoodsRecord;public NativeMarketGoodsItem(int a,int r){GoodsArchive=a;GoodsRecord=r;UID=DaggerfallUnity.NextUID;}}
public sealed class HouseholdPropertyKeyItem:DaggerfallUnityItem {}
public static class ItemHelper {public const int LastDFTemplate=1000;}
public static class NativeItemReceiptLabel {public static string Capture(string value)=>value;}
public sealed class ItemCollection
{
    private readonly List<DaggerfallUnityItem> items=new List<DaggerfallUnityItem>();public int Count=>items.Count;
    public DaggerfallUnityItem GetItem(ulong uid)=>items.Find(i=>i.UID==uid);public DaggerfallUnityItem GetItem(int i)=>items[i];
    public bool Contains(DaggerfallUnityItem item)=>items.Contains(item);public void AddItem(DaggerfallUnityItem item,bool noStack=false)=>items.Add(item);public void RemoveItem(DaggerfallUnityItem item)=>items.Remove(item);
    public void Transfer(DaggerfallUnityItem item,ItemCollection source){source.RemoveItem(item);AddItem(item);}
    public DaggerfallUnityItem SplitStack(DaggerfallUnityItem item,int count){item.stackCount-=count;var split=new DaggerfallUnityItem{UID=DaggerfallUnity.NextUID,stackCount=count};AddItem(split);return split;}
    public void DeserializeItems(ItemData[] data){foreach(var row in data){DaggerfallUnityItem i=row.className=="market"?new NativeMarketGoodsItem(row.archive,row.record):new DaggerfallUnityItem();i.UID=row.uid;i.stackCount=row.stackCount;i.value=row.value;AddItem(i);}}
    private DaggerfallUnityItem FindExistingStack(DaggerfallUnityItem item)=>null;
}
public sealed class NativeNpcInventoryBinding {public string PersonId;public long Revision;public ItemCollection Items=new ItemCollection();}
public static class NativeNpcInventoryBridge
{
    private static readonly MethodInfo findStack=typeof(ItemCollection).GetMethod("FindExistingStack",BindingFlags.NonPublic|BindingFlags.Instance);
    public static List<NpcInventoryItem> CaptureItems(ItemCollection items){var result=new List<NpcInventoryItem>();for(int i=0;i<items.Count;i++){var item=items.GetItem(i);result.Add(new NpcInventoryItem{Uid=item.UID,NativePayload=item.value+":"+item.stackCount});}return result;}
    private static void TryRecordExactPlayerGiftSource(params object[] ignored){}
    // BRIDGE_METHODS
}
public class NativeComponent {public static implicit operator bool(NativeComponent value)=>value!=null;}
public struct Vector3 {public float x;public static float Distance(Vector3 a,Vector3 b)=>Math.Abs(a.x-b.x);}
public sealed class Transform {public Vector3 position;}
public sealed class GameObject {public bool activeInHierarchy=true;}
public sealed class ResidentActor:NativeComponent {public Resident Resident=new Resident();public bool IsSleeping;}
public sealed class SleepingPose:NativeComponent {public bool Active;}
public sealed class NativeMarketCommuter:NativeComponent {public NativePerson Person;public bool AtPost=true,InsideHome;public string Phase="work";}
public sealed class StaticNPC:NativeComponent
{
    public Transform transform=new Transform();public GameObject gameObject=new GameObject();
    public ResidentActor Actor;public SleepingPose Pose;public NativeMarketCommuter Commuter;
    public T GetComponent<T>() where T:class {if(typeof(T)==typeof(ResidentActor))return Actor as T;if(typeof(T)==typeof(SleepingPose))return Pose as T;return Commuter as T;}
}
public sealed class NativeMarketGoodsEvidence {public int Archive,Record;}
public sealed class NativeMarketPost {public StaticNPC Seller=new StaticNPC();public MarketPostRole Role;public List<NativeMarketGoodsEvidence> Goods=new List<NativeMarketGoodsEvidence>();public Vector3 PostWorldPosition=>new Vector3(); // AVAILABILITY_PROPERTY
}
public static class NativeMarketStock {// SALE_ITEM_METHOD
}
public sealed class NativeVendorTradeStock
{public NativePerson Person;public NativeNpcInventoryBinding Seller;public NativeMarketPost Post=new NativeMarketPost();public ItemCollection Items=>Seller.Items;public bool IsSaleItem(DaggerfallUnityItem item)=>item!=null && !item.IsEquipped && !item.IsQuestItem;}
public sealed class NativeVendorPurchaseReceipt {public string TransactionId;public ulong SourceItemUid,RecipientItemUid;public int Quantity,Price;}
public static class NativeVendorTradeAdapter {// ADAPTER_METHOD
}
public static class Fixture
{
    static int checks,failures;static void Check(bool value,string message){checks++;if(!value){failures++;Console.WriteLine("FAIL "+message);}else Console.WriteLine("PASS "+message);}
    public static int Run()
    {
        var post=new NativeMarketPost();Check(post.Available,"real present native person provides market activity");
        post.Seller=null;Check(!post.Available,"empty stage cannot provide audience activity");post.Seller=new StaticNPC();
        post.Seller.gameObject.activeInHierarchy=false;Check(!post.Available,"inactive performer cannot provide audience activity");post.Seller.gameObject.activeInHierarchy=true;
        post.Seller.Actor=new ResidentActor{IsSleeping=true};Check(!post.Available,"sleeping performer cannot provide audience activity");post.Seller.Actor.IsSleeping=false;
        post.Seller.Actor.Resident.IsAlive=false;Check(!post.Available,"dead performer cannot provide audience activity");post.Seller.Actor.Resident.IsAlive=true;
        post.Seller.Commuter=new NativeMarketCommuter{AtPost=false,Phase="to-home"};Check(!post.Available,"departing performer cannot continue show near post");post.Seller.Commuter=null;
        var store=new WorldStateStore(42);var town=store.GetOrCreate(1,new[]{10,20},1);string buyerId=town.Households[0].Residents[0].Id;
        var seller=store.RegisterNativePerson(1,-1,99,"Vendor",ResidentSex.Female,1,88,182,1);
        var buyer=new NativeNpcInventoryBinding{PersonId=buyerId};var sellerBinding=new NativeNpcInventoryBinding{PersonId=seller.Id};
        var goods=new NativeMarketGoodsItem(211,31){stackCount=4};sellerBinding.Items.AddItem(goods);
        store.InitializeNpcInventory(buyerId,100,NativeNpcInventoryBridge.CaptureItems(buyer.Items));store.InitializeNpcInventory(seller.Id,50,NativeNpcInventoryBridge.CaptureItems(sellerBinding.Items));
        var stock=new NativeVendorTradeStock{Seller=sellerBinding,Person=seller};NativeVendorPurchaseReceipt receipt;string reason;
        bool Buy(string id,int count=1,int price=10,Func<bool> live=null)=>NativeVendorTradeAdapter.TryNpcPurchase(store,stock,buyer,goods.UID,count,price,live??(()=>true),id,out receipt,out reason);
        Check(Buy("first"),"commercial market purchase succeeds");
        Check(ReferenceEquals(stock.Items.GetItem(goods.UID),goods) && goods.stackCount==4 && stock.Items.Count==1,"commercial purchase preserves vendor exact stock");
        Check(buyer.Items.Count==1 && buyer.Items.GetItem(0).UID!=goods.UID && buyer.Items.GetItem(0) is NativeMarketGoodsItem,"commercial purchase creates own UID and preserves custom type");
        Check(store.ReadNpcInventory(buyerId).WalletGold==90 && store.ReadNpcInventory(seller.Id).WalletGold==60,"commercial purchase pays both real wallets");
        Check(store.NpcInventoryItemOwner(goods.UID)==seller.Id && store.NpcInventoryItemOwner(buyer.Items.GetItem(0).UID)==buyerId,"source and received ownership remain distinct");
        Check(Buy("second",4),"whole-stock quantity can be bought again");
        Check(goods.stackCount==4 && buyer.Items.Count==2 && buyer.Items.GetItem(1).stackCount==4,"whole-stock purchase also preserves source");
        Check(!Buy("second"),"duplicate transaction cannot charge twice");
        Check(!Buy("poor",1,1000),"insufficient funds reject commercial purchase");
        Check(!Buy("closed",1,10,()=>false),"lost physical access rejects purchase");
        int calls=0;Check(!Buy("late",1,10,()=>++calls<4),"lost access before commit rejects purchase");
        Check(buyer.Items.Count==2 && goods.stackCount==4 && store.ReadNpcInventory(buyerId).WalletGold==80,"failed commercial purchase rolls back exact state");
        var stale=new NativeNpcInventoryBinding{PersonId=seller.Id,Revision=0,Items=sellerBinding.Items};stock.Seller=stale;
        Check(!Buy("stale"),"stale vendor lease rejects commercial purchase");stock.Seller=sellerBinding;
        Check(NativeNpcInventoryBridge.TryPurchaseOwnedStock(store,buyer,sellerBinding,goods.UID,1,10,()=>true,"private",out reason),"private owned sale retains transfer path");
        Check(goods.stackCount==3,"private owned sale depletes exact seller stack");
        ulong ordinaryUid=buyer.Items.GetItem(0).UID;
        Check(store.RecordNpcOrdinaryPurchase(buyerId,ordinaryUid) && Buy("third"),"ordinary food provenance survives subsequent real commerce");
        Check(store.ReadNpcInventory(buyerId).Items.Find(item=>item.Uid==ordinaryUid).OrdinaryPurchase,"same-owner native capture preserves ordinary purchase marker");
        Check(NativeNpcInventoryBridge.TryTransfer(store,buyer,sellerBinding,ordinaryUid,1,"gift-food",out reason),"ordinary food can transfer through actual native inventory bridge");
        Check(!store.ReadNpcInventory(seller.Id).Items.Find(item=>item.Uid==ordinaryUid).OrdinaryPurchase && store.ConsumeNpcOrdinaryPurchasesAtHome(seller.Id)==0 && sellerBinding.Items.GetItem(ordinaryUid)!=null,"gift recipient retains food because ownership transfer clears ordinary purchase marker");
        var repairStore=new WorldStateStore(43);var repairTown=repairStore.GetOrCreate(2,new[]{30,40},1);
        var repairBuyer=new NativeNpcInventoryBinding{PersonId=repairTown.Households[0].Residents[0].Id};
        var repairSeller=new NativeNpcInventoryBinding{PersonId=repairTown.Households[1].Residents[0].Id};
        var repairItem=new DaggerfallUnityItem{UID=700,ItemGroup=ItemGroups.Weapons,RepairData=new RepairData{Held=true}};
        repairSeller.Items.AddItem(repairItem);repairStore.InitializeNpcInventory(repairSeller.PersonId,50,NativeNpcInventoryBridge.CaptureItems(repairSeller.Items));
        repairStore.InitializeNpcInventory(repairBuyer.PersonId,100,NativeNpcInventoryBridge.CaptureItems(repairBuyer.Items));
        var weaponPost=new NativeMarketPost{Role=MarketPostRole.Weapons};
        Check(!NativeMarketStock.IsSaleItem(repairItem,weaponPost),"repair custody weapon is excluded from commercial offers");
        Check(!NativeNpcInventoryBridge.TryPurchaseCommercialStock(repairStore,repairBuyer,repairSeller,700,1,10,()=>true,"repair",out reason),"commercial helper cannot clone repair custody item even without offer filter");
        Check(repairBuyer.Items.Count==0 && repairItem.stackCount==1 && repairStore.ReadNpcInventory(repairBuyer.PersonId).WalletGold==100 && repairStore.ReadNpcInventory(repairSeller.PersonId).WalletGold==50,"rejected repair custody sale preserves receipt ownership and both wallets");
        Console.WriteLine((checks-failures)+"/"+checks+" passed");return failures==0?0:1;
    }
}
