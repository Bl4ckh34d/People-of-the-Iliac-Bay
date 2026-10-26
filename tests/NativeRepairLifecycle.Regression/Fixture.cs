using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public struct Vector3
    {
        public float x, y, z;
        public static Vector3 up { get { return new Vector3 { y = 1 }; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3 { x=a.x-b.x,y=a.y-b.y,z=a.z-b.z }; }
        public static float Distance(Vector3 a, Vector3 b) { var d=a-b;return (float)Math.Sqrt(d.x*d.x+d.y*d.y+d.z*d.z); }
    }
    public sealed class Transform { public Vector3 position = Vector3.up; }
    public static class Mathf { public static float Ceil(float value) { return (float)Math.Ceiling(value); } }
}
namespace DaggerfallWorkshop.Utility { public static class DaggerfallDateTime { public const int SecondsPerDay=86400; } }
namespace DaggerfallWorkshop.Game.Entity { }
namespace DaggerfallWorkshop
{
    public sealed class DaggerfallUnity
    {
        public static readonly DaggerfallUnity Instance = new DaggerfallUnity();
        public static readonly SettingsData Settings = new SettingsData();
        public Clock WorldTime = new Clock();
        public sealed class SettingsData { public bool AllowMagicRepairs=true; }
        public sealed class Clock { public TimeData Now = new TimeData(); }
        public sealed class TimeData { public ulong Seconds=100;public ulong ToSeconds() { return Seconds; } }
    }
}
namespace DaggerfallWorkshop.Game
{
    public sealed class GameManager
    {
        public static readonly GameManager Instance=new GameManager();
        public EnterExit PlayerEnterExit=new EnterExit();
        public sealed class EnterExit { public Room Interior=new Room();public Discovery BuildingDiscoveryData=new Discovery(); }
        public sealed class Room { public string name="repair-shop-A"; }
        public sealed class Discovery { public int quality=10; }
    }
}
namespace DaggerfallWorkshop.Game.Serialization
{
    public sealed class ItemRepairData_v1 { public string sceneName;public ulong timeStarted;public int repairTime; }
}
namespace DaggerfallWorkshop.Game.Items
{
    public enum ItemGroups { Weapons,Armor,MensClothing,WomensClothing,Jewellery }
    public enum EquipSlots { None,Body }
    public sealed class DaggerfallUnityItem
    {
        public ulong UID=42;
        public int stackCount=1,currentCondition=10,maxCondition=100,value=90;
        public bool IsQuestItem=false,IsEquipped=false,IsSummoned=false,IsEnchanted=false;
        public Template ItemTemplate=new Template();public ItemRepairData RepairData=new ItemRepairData();
        public string LongName="Iron sword";public ItemGroups ItemGroup=ItemGroups.Armor;
        public sealed class Template { public bool isNotRepairable=false; }
    }
    public sealed class ItemCollection
    {
        public DaggerfallUnityItem Item=new DaggerfallUnityItem();
        public int Count {get{return Item==null?0:1;}}
        public DaggerfallUnityItem GetItem(int index) {return index==0?Item:null;}
        public DaggerfallUnityItem GetItem(ulong uid) { return Item != null && Item.UID==uid ? Item : null; }
    }
}
namespace DaggerfallWorkshop.Game.Formulas
{
    public static class FormulaHelper
    {
        public static int CalculateItemRepairCost(int value,int quality,int condition,int max,object guild) { return 9; }
        public static int CalculateItemRepairTime(int condition,int max) { return 86400; }
    }
}
namespace PeopleOfTheIliacBay.DFU
{
    public sealed class NativeNpcInventoryBinding { public string PersonId;public long Revision;public ItemCollection Items=new ItemCollection(); }
    public sealed class NativeNpcInventoryOwner : UnityEngine.Object
    {
        public NativeNpcInventoryBinding Binding;
        private FakeEntity entity=new FakeEntity();
        public sealed class FakeEntity {public ItemCollection Items=new ItemCollection();public FakeEquipment ItemEquipTable=new FakeEquipment();}
        public sealed class FakeEquipment
        {
            public EquipSlots GetEquipSlot(DaggerfallUnityItem item) {return EquipSlots.Body;}
            public DaggerfallUnityItem GetItem(EquipSlots slot) {return null;}
            public void EquipItem(DaggerfallUnityItem item,bool a,bool b) {item.IsEquipped=true;}
        }
        // PRODUCTION_WEARABLES
        public void RestoreWearables() {entity.Items=Binding.Items;EquipOwnedWearables();}
    }
    public sealed class ResidentActor : UnityEngine.Object
    {
        public Transform transform=new Transform();public NativeNpcInventoryOwner Owner;
        public T GetComponent<T>() where T:class { return Owner as T; }
    }
    public sealed class StaticNPC { public Transform transform=new Transform(); }
    public static class NativeNpcInventoryBridge
    {
        public static bool FailCapture;
        public sealed class Payload
        {
            public ulong Uid;public int Condition,Stack;
            public DaggerfallWorkshop.Game.Serialization.ItemRepairData_v1 Repair;
        }
        private static readonly JsonSerializerOptions Options=new JsonSerializerOptions { IncludeFields=true };
        public static List<NpcInventoryItem> CaptureItems(ItemCollection items)
        {
            if(FailCapture) throw new InvalidOperationException("controlled serialization failure");
            var item=items.Item;
            return new List<NpcInventoryItem> {new NpcInventoryItem {Uid=item.UID,NativePayload=JsonSerializer.Serialize(
                new Payload {Uid=item.UID,Condition=item.currentCondition,Stack=item.stackCount,Repair=item.RepairData.GetSaveData()},Options)}};
        }
        public static NativeNpcInventoryBinding Restore(WorldStateStore store,string id)
        {
            var saved=store.ReadNpcInventory(id);var data=JsonSerializer.Deserialize<Payload>(saved.Items[0].NativePayload,Options);
            var result=new NativeNpcInventoryBinding {PersonId=id,Revision=saved.Revision};
            result.Items.Item.UID=data.Uid;result.Items.Item.currentCondition=data.Condition;result.Items.Item.stackCount=data.Stack;
            result.Items.Item.RepairData.RestoreRepairData(data.Repair);return result;
        }
    }
    public sealed partial class StreetPopulation
    {
        private WorldStateStore worldStore;
        private bool shopAvailable=true;
        private bool TryNativeRepairKeeper(out NativePerson keeper,out StaticNPC npc)
        { keeper=new NativePerson {Id="keeper"};npc=new StaticNPC();return shopAvailable; }
        private static Vector3 ActionWorldPoint(NpcActionTarget target) { return new Vector3 {x=(float)target.X,y=(float)target.Y,z=(float)target.Z}; }
        // PRODUCTION_METHODS
        private static ConversationContextInventoryItem Project(DaggerfallUnityItem item) { return PRODUCTION_PROJECTION; }
        private static void Require(bool value,string message) { if(!value)throw new InvalidOperationException(message); }
        private static void Check(string name,Action body,ref int failures)
        { try {body();Console.WriteLine("PASS "+name);}catch(Exception error){failures++;Console.WriteLine("FAIL "+name+": "+error.Message);} }
        private static StreetPopulation Fresh(out ResidentActor actor)
        {
            DaggerfallUnity.Instance.WorldTime.Now.Seconds=100;GameManager.Instance.PlayerEnterExit.Interior=new GameManager.Room();
            NativeNpcInventoryBridge.FailCapture=false;
            var host=new StreetPopulation {worldStore=new WorldStateStore(723u)};
            var resident=host.worldStore.GetOrCreate(7,new[] {10},1).Households[0].Residents[0];
            actor=new ResidentActor {Owner=new NativeNpcInventoryOwner {Binding=new NativeNpcInventoryBinding {PersonId=resident.Id}}};
            Require(host.worldStore.InitializeNpcInventory(resident.Id,100,NativeNpcInventoryBridge.CaptureItems(actor.Owner.Binding.Items)),"initial inventory");
            actor.Owner.Binding.Revision=host.worldStore.ReadNpcInventory(resident.Id).Revision;return host;
        }
        private NpcActionJob Start(ResidentActor actor,string id,bool collect)
        {
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var job=new NpcActionJob {Id=id,ProposalId=id,ResidentId=actor.Owner.Binding.PersonId,Kind=NpcActionKind.Purchase,
                ServiceCode=NpcShopCommerce.RepairServiceCode,ServicePrice=collect?0:9,Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=now,UpdatedAt=now,DeadlineAt=now+300,ExpectedInventoryRevision=actor.Owner.Binding.Revision,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=7,BuildingKey=10,PersonId="keeper",
                    ContainerId=NpcShopCommerce.RepairTargetContainerId,ItemId="42",Quantity=1,QuotedGold=collect?0:9}};
            string reason;Require(Program.Accept(worldStore,job,out reason),reason);
            Require(worldStore.ObserveNpcAction(job.Id,1,now,NpcActionObservation.Arrival()),"observed approach");
            return worldStore.ReadNpcAction(job.Id);
        }
        private bool Execute(NpcActionJob job,ResidentActor actor) { string reason;return ExecuteNativeRepairAction(job,actor,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),out reason); }
        public static int Run()
        {
            int failures=0;
            Check("repair-held inventory describes custody, pending and ready status",()=>{
                ResidentActor actor;var host=Fresh(out actor);var item=actor.Owner.Binding.Items.Item;
                Require(host.Execute(host.Start(actor,"drop",false),actor),"drop-off");
                string pending=Project(item).Properties;
                Require(pending!=null && pending.Contains("unavailable") && pending.Contains("pending"),"pending repair falsely appears available");
                DaggerfallUnity.Instance.WorldTime.Now.Seconds=item.RepairData.GetTimeDone();
                string ready=Project(item).Properties;
                Require(ready!=null && ready.Contains("ready") && ready.Contains("unavailable"),"due repair falsely appears collected");
                GameManager.Instance.PlayerEnterExit.Interior=null;
                Require(Project(item).Properties.Contains("ready"),"repair facts must work outdoors without querying current shop");
            },ref failures);
            Check("collection rejects mutable stacks",()=>{
                ResidentActor actor;var host=Fresh(out actor);var item=actor.Owner.Binding.Items.Item;
                Require(host.Execute(host.Start(actor,"drop",false),actor),"drop-off");
                DaggerfallUnity.Instance.WorldTime.Now.Seconds=item.RepairData.GetTimeDone();item.stackCount=2;
                Require(!CollectableNativeRepairItem(item),"collection accepted more than one item for one UID receipt");
            },ref failures);
            Check("restored repair-held wearables stay unavailable until collected",()=>{
                ResidentActor actor;var host=Fresh(out actor);
                Require(host.Execute(host.Start(actor,"drop",false),actor),"drop-off");
                actor.Owner.Binding=NativeNpcInventoryBridge.Restore(host.worldStore,actor.Owner.Binding.PersonId);
                var item=actor.Owner.Binding.Items.Item;actor.Owner.RestoreWearables();
                Require(!item.IsEquipped,"restore equipped item still held at repair shop");
                DaggerfallUnity.Instance.WorldTime.Now.Seconds=item.RepairData.GetTimeDone();actor.Owner.RestoreWearables();
                Require(!item.IsEquipped,"finished but uncollected item equipped");
                Require(host.Execute(host.Start(actor,"collect",true),actor),"collection");actor.Owner.RestoreWearables();
                Require(item.IsEquipped,"collected wearable no longer equips normally");
            },ref failures);
            Check("exact UID survives restore; early/wrong-shop collection denied; one payment and no replay",()=>{
                ResidentActor actor;var host=Fresh(out actor);var drop=host.Start(actor,"drop",false);
                Require(host.Execute(drop,actor),"drop-off");Require(!host.Execute(drop,actor),"duplicate drop-off");
                var restored=new WorldStateStore(1);restored.Restore(host.worldStore.Snapshot());host.worldStore=restored;
                actor.Owner.Binding=NativeNpcInventoryBridge.Restore(restored,actor.Owner.Binding.PersonId);
                var item=actor.Owner.Binding.Items.Item;
                Require(item.UID==42 && item.currentCondition==10 && item.RepairData.RepairTime==86400,"exact native payload restore");
                Require(!CollectableNativeRepairItem(item),"early collection");
                DaggerfallUnity.Instance.WorldTime.Now.Seconds=item.RepairData.GetTimeDone();
                GameManager.Instance.PlayerEnterExit.Interior.name="repair-shop-B";
                Require(!CollectableNativeRepairItem(item),"wrong-shop collection");
                GameManager.Instance.PlayerEnterExit.Interior.name="repair-shop-A";
                var collect=host.Start(actor,"collect",true);Require(host.Execute(collect,actor),"collection");
                Require(!host.Execute(collect,actor),"duplicate collection");
                Require(item.currentCondition==100 && !item.RepairData.IsBeingRepaired(),"full condition only after pickup");
                Require(restored.ReadNpcInventory(actor.Owner.Binding.PersonId).WalletGold==91,"exact single NPC payment");
                var collectedBinding=NativeNpcInventoryBridge.Restore(restored,actor.Owner.Binding.PersonId);
                Require(collectedBinding.Items.Item.UID==42 && collectedBinding.Items.Item.currentCondition==100 &&
                    !collectedBinding.Items.Item.RepairData.IsBeingRepaired() && Project(collectedBinding.Items.Item).Properties==null,
                    "collected payload persists full condition without repair custody");
            },ref failures);
            Check("cancelled collection survives restore with item still at repair shop",()=>{
                ResidentActor actor;var host=Fresh(out actor);Require(host.Execute(host.Start(actor,"drop",false),actor),"drop-off");
                DaggerfallUnity.Instance.WorldTime.Now.Seconds=actor.Owner.Binding.Items.Item.RepairData.GetTimeDone();
                var collect=host.Start(actor,"collect",true);
                Require(host.worldStore.ObserveNpcAction(collect.Id,collect.Revision,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),NpcActionObservation.Cancelled("Stopped by player.")),"cancel");
                var restored=new WorldStateStore(1);restored.Restore(host.worldStore.Snapshot());host.worldStore=restored;
                actor.Owner.Binding=NativeNpcInventoryBridge.Restore(restored,actor.Owner.Binding.PersonId);
                Require(!host.Execute(collect,actor),"cancelled action replay");
                Require(actor.Owner.Binding.Items.Item.RepairData.IsBeingRepaired() && actor.Owner.Binding.Items.Item.currentCondition==10,"cancel retains custody");
                Require(host.Execute(host.Start(actor,"retry",true),actor),"new explicit collection after cancellation");
            },ref failures);
            Check("failed inventory commit rolls back native drop-off and collection",()=>{
                ResidentActor actor;var host=Fresh(out actor);var item=actor.Owner.Binding.Items.Item;var drop=host.Start(actor,"drop",false);
                NativeNpcInventoryBridge.FailCapture=true;Require(!host.Execute(drop,actor),"serialization failure accepted");
                Require(!item.RepairData.IsBeingRepaired() && item.currentCondition==10,"drop-off rollback");
                NativeNpcInventoryBridge.FailCapture=false;Require(host.Execute(drop,actor),"retry drop-off");
                ulong due=item.RepairData.GetTimeDone();DaggerfallUnity.Instance.WorldTime.Now.Seconds=due;
                var collect=host.Start(actor,"collect",true);NativeNpcInventoryBridge.FailCapture=true;
                Require(!host.Execute(collect,actor),"collection serialization failure accepted");
                Require(item.RepairData.IsBeingRepaired() && item.RepairData.GetTimeDone()==due && item.currentCondition==10,"collection rollback");
                Require(host.worldStore.ReadNpcInventory(actor.Owner.Binding.PersonId).WalletGold==91,"failed operation changed wallet");
                NativeNpcInventoryBridge.FailCapture=false;
            },ref failures);
            Console.WriteLine("Native repair regression: "+(6-failures)+"/6 scenarios passed.");return failures==0?0:1;
        }
    }
}
