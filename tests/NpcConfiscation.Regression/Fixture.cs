public static class CollectionExtensions{public static bool Contains(this ItemCollection items,ulong uid)=>items.GetItem(uid)!=null;}
public sealed class EquipTable
{public bool UnequipItem(DaggerfallUnityItem item){item.IsEquipped=false;return true;}public void EquipItem(DaggerfallUnityItem item,bool a,bool b){item.IsEquipped=true;}}
public sealed class NativeEntity{public ItemCollection Items;public EquipTable ItemEquipTable=new EquipTable();}
public sealed class DaggerfallEntityBehaviour:NativeComponent{public NativeEntity Entity;}
public sealed class DaggerfallLoot:NativeComponent
{public ulong LoadID=8;public LootContainerTypes ContainerType=LootContainerTypes.HouseContainers;public ItemCollection Items=new ItemCollection();public GameObject gameObject=new GameObject();public Transform transform=new Transform();}
public enum LootContainerTypes{HouseContainers,ShopShelves,CorpseMarker}
public static class DFLocation{public enum BuildingTypes{Town23,Shop}}
public sealed class BuildingSummary{public DFLocation.BuildingTypes BuildingType;}
public sealed class Door{public int buildingKey=7;}
public sealed class Room:NativeComponent{public Door EntryDoor=new Door();public Transform transform=new Transform();}
public sealed class EnterExit{public bool IsPlayerInsideBuilding=true;public Room Interior=new Room();}
public sealed class Directory:NativeComponent
{public bool Wall=true;public bool GetBuildingSummary(int key,out BuildingSummary building){building=new BuildingSummary{BuildingType=Wall?DFLocation.BuildingTypes.Town23:DFLocation.BuildingTypes.Shop};return true;}}
public sealed class Streaming{public Directory Directory=new Directory();public Directory GetCurrentBuildingDirectory()=>Directory;}
public sealed class GPS{public int CurrentMapID=1;}
public sealed class GameManager{public static GameManager Instance=new GameManager();public EnterExit PlayerEnterExit=new EnterExit();public Streaming StreamingWorld=new Streaming();public GPS PlayerGPS=new GPS();}
public static class Fixture
{
    static int checks;static void Need(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;Console.WriteLine("PASS "+name);}
    sealed class Case
    {
        public WorldStateStore Store=new WorldStateStore(55);public NativeNpcInventoryBinding Source,Owner;
        public DaggerfallEntityBehaviour Body;public DaggerfallLoot Container;
        public Case(bool weapon=true,bool equipped=false)
        {
            var town=Store.GetOrCreate(1,new[]{10,20},1);
            Source=new NativeNpcInventoryBinding{PersonId=town.Households[0].Residents[0].Id};
            Owner=new NativeNpcInventoryBinding{PersonId=town.Households[1].Residents[0].Id};
            Source.Items.AddItem(new DaggerfallUnityItem{UID=81,ItemGroup=weapon?ItemGroups.Weapons:ItemGroups.MiscItems,IsEquipped=equipped});
            Store.InitializeNpcInventory(Source.PersonId,73,NativeNpcInventoryBridge.CaptureItems(Source.Items));
            Store.InitializeNpcInventory(Owner.PersonId,19,NativeNpcInventoryBridge.CaptureItems(Owner.Items));
            Body=new DaggerfallEntityBehaviour{Entity=new NativeEntity{Items=Source.Items}};
            Container=new DaggerfallLoot();Container.transform.Parent=GameManager.Instance.PlayerEnterExit.Interior.transform;
        }
        public bool Apply(IDictionary<ulong,string> origins,DaggerfallLoot container,Func<bool> live,out NativeNpcConfiscationResult result)
        {string reason;return NativeNpcConfiscation.TryApply(Store,Source,Body,origins,id=>Owner,id=>Container,container,live,out result,out reason);}
    }
    public static int Run()
    {
        NativeNpcConfiscationResult result;var stored=new Case();
        Need(stored.Apply(null,stored.Container,()=>true,out result) && result.Stored==1 && stored.Container.Items.GetItem(81UL)!=null,
            "ordinary confiscated weapon enters existing wall container with exact UID");
        Need(stored.Store.NpcInventoryItemOwner(81)==null && stored.Store.ReadNpcInventory(stored.Source.PersonId).WalletGold==73,
            "wall deposit releases NPC ownership without touching wallet");
        var destroyed=new Case();Need(destroyed.Apply(null,null,()=>true,out result) && result.Destroyed==1 && destroyed.Source.Items.Count==0 && destroyed.Store.NpcInventoryItemOwner(81)==null,
            "missing actual wall container destroys weapon without guard inventory");
        var ordinary=new Case(false);Need(ordinary.Apply(null,null,()=>true,out result) && ordinary.Source.Items.Count==1 && result.Destroyed==0,
            "ordinary unproven nonweapon remains with NPC");
        var returned=new Case();Need(returned.Apply(new Dictionary<ulong,string>{{81,returned.Owner.PersonId}},returned.Container,()=>true,out result) &&
            result.Returned==1 && returned.Owner.Items.GetItem(81UL)!=null && returned.Container.Items.Count==0 && returned.Store.NpcInventoryItemOwner(81)==returned.Owner.PersonId,
            "proven stolen weapon returns to actual living NPC before wall disposal");
        var dead=new Case(false);dead.Store.ResolveResident(dead.Owner.PersonId).IsAlive=false;
        Need(dead.Apply(new Dictionary<ulong,string>{{81,dead.Owner.PersonId}},null,()=>true,out result) && result.Destroyed==1 && dead.Owner.Items.Count==0,
            "dead owner receives no item and missing container destroys proven stolen goods");
        var equipped=new Case(true,true);Need(equipped.Apply(null,equipped.Container,()=>true,out result) && !equipped.Container.Items.GetItem(81UL).IsEquipped,
            "actual equipped weapon is unequipped before confiscation");
        var lost=new Case();int calls=0;Need(!lost.Apply(null,lost.Container,()=>++calls==1,out result) && lost.Source.Items.Count==1 && lost.Container.Items.Count==0 && lost.Store.NpcInventoryItemOwner(81)==lost.Source.PersonId,
            "lost custody before transfer preserves exact source item and ownership");
        var invalid=new Case();invalid.Container.LoadID=0;
        Need(invalid.Apply(null,invalid.Container,()=>true,out result) && result.Destroyed==1 && invalid.Container.Items.Count==0,
            "fictitious container without native LoadID is never a disposal destination");
        var stale=new Case();stale.Store.CommitNpcInventoryTransaction("other-live-change",new[]{new NpcInventoryUpdate{
            PersonId=stale.Source.PersonId,ExpectedRevision=0,WalletGold=73,Items=new List<NpcInventoryItem>{new NpcInventoryItem{Uid=81,NativePayload="999:1"}}}});
        Need(!stale.Apply(null,stale.Container,()=>true,out result) && stale.Source.Items.Count==1 && stale.Container.Items.Count==0,
            "stale ownership cannot confiscate an item");
        var shop=new Case(false);shop.Container.ContainerType=LootContainerTypes.ShopShelves;
        Need(shop.Apply(new Dictionary<ulong,string>{{81,"native-shop:1:7"}},null,()=>true,out result) && result.Returned==1 && shop.Container.Items.GetItem(81UL)!=null,
            "known stolen shop item returns to actual matching loaded shop stock");
        Console.WriteLine(checks+" checks passed");return 0;
    }
}
