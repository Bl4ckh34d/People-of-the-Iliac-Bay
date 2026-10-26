using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.QuestHooks;

internal static class Program
{
    private static readonly BindingFlags PrivateStatic=BindingFlags.Static|BindingFlags.NonPublic;
    private static int passed;
    private static int Main()
    {
        try
        {
            Shop(true,true,false,false); Shop(false,true,false,false); Shop(true,false,false,false);
            Shop(true,true,true,false); Shop(true,true,false,true);
            PrivateTransfer(true,false,false,false); PrivateTransfer(false,false,false,false);
            PrivateTransfer(true,true,false,false); PrivateTransfer(true,false,true,false); PrivateTransfer(true,false,false,true);
            PrivateMerge();PrivateSplit();
            Scene(true,false,false);Scene(false,false,false);Scene(true,true,false);Scene(true,false,true);PrivateConsent();
            MissingNativeName(false);MissingNativeName(true);
            MissingSceneCapture();MissingScenePublication();
            Console.WriteLine("Native theft producer regression: "+passed+"/"+passed+" PASS (native dependencies stubbed)."); return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static object Begin(Type type,string method,params object[] arguments)
    { var call=type.GetMethod(method,PrivateStatic); if(call==null)return null;call.Invoke(null,arguments);return arguments[arguments.Length-1]; }
    private static void End(Type type,string method,object state)
    { var call=type.GetMethod(method,PrivateStatic); if(call!=null)call.Invoke(null,new[]{state}); }
    private static void Shop(bool transfer,bool seen,bool merge,bool replacePlayer)
    {
        GameManager.Instance=new GameManager();var game=GameManager.Instance;
        var item=new DaggerfallUnityItem {UID=41,stackCount=2};var window=new DaggerfallTradeWindow();window.Basket.AddItem(item);
        DaggerfallUnityItem existing=null;
        if(merge){existing=new DaggerfallUnityItem {UID=42,stackCount=3};game.PlayerEntity.Items.AddItem(existing);}
        var memories=new List<string>();int captures=0,receipts=0;bool currentSees=seen;
        Func<Action<string,int>> capture=()=>{captures++;bool witnessed=currentSees;return (name,count)=>{if(witnessed)memories.Add(count+" "+name);};};
        Action<string,string,ulong,int,int,int,int,int,bool> receipt=(a,b,c,d,e,f,g,h,i)=>receipts++;
        var type=typeof(NativePlayerShopProvenanceHooks);
        var constructor=type.GetConstructor(new[]{receipt.GetType(),capture.GetType()});
        using(var hook=(IDisposable)(constructor!=null?constructor.Invoke(new object[]{receipt,capture}):new NativePlayerShopProvenanceHooks(receipt)))
        {
            object state=Begin(type,"BeforeSteal",window,null);
            currentSees=!seen;item.LongName="changed after transfer";
            if(transfer){window.Basket.RemoveItem(item);if(merge)existing.stackCount+=2;else game.PlayerEntity.Items.AddItem(item);}
            if(replacePlayer)game.PlayerEntity.Items=new ItemCollection();
            End(type,"AfterSteal",state);End(type,"AfterSteal",state);
            int expected=transfer&&!replacePlayer?1:0;
            Require(captures==1,"Shop witnesses were not captured before native transfer.");
            Require(memories.Count==(seen?expected:0),"Shop memory lost pre-transfer sight, invented an unobserved outcome, or replayed.");
            if(memories.Count>0)Require(memories[0]=="2 Iron Dagger","Shop did not preserve supplied item name/quantity.");
            Require(receipts==expected,"Native shop legal provenance changed or replayed.");
        }
        passed++;
    }
    private static void PrivateTransfer(bool transfer,bool consent,bool gold,bool replacePlayer)
    {
        GameManager.Instance=new GameManager();var game=GameManager.Instance;
        var item=new DaggerfallUnityItem {UID=51,stackCount=2,ItemGroup=gold?ItemGroups.Currency:ItemGroups.Weapons};
        var loot=new DaggerfallLoot();loot.Items.AddItem(item);var window=new DaggerfallInventoryWindow {LootTarget=loot};
        var memories=new List<string>();int captures=0;
        Func<DaggerfallInventoryWindow,Action<string,int>> capture=w=>{captures++;return consent?null:(Action<string,int>)((name,count)=>memories.Add(count+" "+name));};
        var type=typeof(NativeTheftHooks);var constructor=type.GetConstructor(new[]{typeof(Func<bool?>),capture.GetType()});
        using(var hook=(IDisposable)(constructor!=null?constructor.Invoke(new object[]{new Func<bool?>(()=>false),capture}):new NativeTheftHooks(()=>false)))
        {
            object state=Begin(type,"BeforeTransfer",window,item,loot.Items,game.PlayerEntity.Items,null);
            item.LongName="changed after transfer";
            if(transfer){loot.Items.RemoveItem(item);if(gold)game.PlayerEntity.GoldPieces+=2;else game.PlayerEntity.Items.AddItem(item);}
            if(replacePlayer)game.PlayerEntity.Items=new ItemCollection();
            End(type,"AfterTransfer",state);End(type,"AfterTransfer",state);
            int expected=transfer&&!consent&&!replacePlayer?1:0;
            Require(captures==1,"Private theft witnesses were not captured at native transfer.");
            Require(memories.Count==expected,"Private theft memory missing, consent ignored, uncommitted transfer remembered, or replayed.");
            if(memories.Count>0)Require(memories[0]=="2 Iron Dagger","Private theft did not preserve supplied item name/quantity.");
        }
        passed++;
    }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidOperationException(message); }
    private static void PrivateMerge()
    {
        GameManager.Instance=new GameManager();var game=GameManager.Instance;
        var item=new DaggerfallUnityItem {UID=61,stackCount=2};var existing=new DaggerfallUnityItem {UID=62,stackCount=3};
        game.PlayerEntity.Items.AddItem(existing);var loot=new DaggerfallLoot();loot.Items.AddItem(item);
        var window=new DaggerfallInventoryWindow {LootTarget=loot};int remembered=0;
        using(var hook=new NativeTheftHooks(()=>false,w=>(name,count)=>remembered+=count))
        {
            object state=Begin(typeof(NativeTheftHooks),"BeforeTransfer",window,item,loot.Items,game.PlayerEntity.Items,null);
            loot.Items.RemoveItem(item);existing.stackCount=5;
            End(typeof(NativeTheftHooks),"AfterTransfer",state);
            Require(remembered==2,"Private theft into an existing native stack lost its actual transfer quantity.");
        }
        passed++;
    }
    private static void PrivateConsent()
    {
        GameManager.Instance=new GameManager();var streets=new PeopleOfTheIliacBay.DFU.StreetPopulation {Consent=true};
        var method=streets.GetType().GetMethod("CapturePrivateTheftMemory",BindingFlags.Instance|BindingFlags.NonPublic);
        var result=method.Invoke(streets,new object[]{new DaggerfallInventoryWindow {LootTarget=new DaggerfallLoot()}});
        Require(result==null,"Consensual household taking was admitted as witnessed theft.");passed++;
    }
    private static void PrivateSplit()
    {
        GameManager.Instance=new GameManager();var game=GameManager.Instance;var loot=new DaggerfallLoot();
        // Native SplitStack adds a new picked item to the source and reduces the
        // remainder before calling DoTransferItem. Supply that exact boundary state.
        var remainder=new DaggerfallUnityItem {UID=71,stackCount=3};var picked=new DaggerfallUnityItem {UID=72,stackCount=2};
        loot.Items.AddItem(remainder);loot.Items.AddItem(picked);var window=new DaggerfallInventoryWindow {LootTarget=loot};int remembered=0;
        using(var hook=new NativeTheftHooks(()=>false,w=>(name,count)=>remembered+=count))
        {
            object state=Begin(typeof(NativeTheftHooks),"BeforeTransfer",window,picked,loot.Items,game.PlayerEntity.Items,null);
            loot.Items.RemoveItem(picked);game.PlayerEntity.Items.AddItem(picked);
            End(typeof(NativeTheftHooks),"AfterTransfer",state);
            Require(remembered==2&&loot.Items.GetItem(71UL).stackCount==3,"Split theft remembered the untouched remainder instead of the actual picked quantity.");
        }
        passed++;
    }
    private static void MissingNativeName(bool privateTransfer)
    {
        GameManager.Instance=new GameManager();var game=GameManager.Instance;
        var item=new DaggerfallUnityItem {UID=81,NameUnavailable=true};int memories=0,receipts=0;
        Action<string,int> remember=(name,count)=>{Require(name==null&&count==1,"Unavailable native item name was invented.");memories++;};
        if(privateTransfer)
        {
            var loot=new DaggerfallLoot();loot.Items.AddItem(item);var window=new DaggerfallInventoryWindow {LootTarget=loot};
            using(var hook=new NativeTheftHooks(()=>false,w=>remember))
            {
                object state=Begin(typeof(NativeTheftHooks),"BeforeTransfer",window,item,loot.Items,game.PlayerEntity.Items,null);
                loot.Items.RemoveItem(item);game.PlayerEntity.Items.AddItem(item);End(typeof(NativeTheftHooks),"AfterTransfer",state);
            }
        }
        else
        {
            var window=new DaggerfallTradeWindow();window.Basket.AddItem(item);
            using(var hook=new NativePlayerShopProvenanceHooks((a,b,c,d,e,f,g,h,i)=>receipts++,()=>remember))
            {
                object state=Begin(typeof(NativePlayerShopProvenanceHooks),"BeforeSteal",window,null);
                window.Basket.RemoveItem(item);game.PlayerEntity.Items.AddItem(item);End(typeof(NativePlayerShopProvenanceHooks),"AfterSteal",state);
            }
            Require(receipts==1,"Unavailable item label suppressed valid native shop provenance.");
        }
        Require(memories==1,"Unavailable item label suppressed the witnessed transfer instead of remaining unknown.");passed++;
    }
    private static void MissingSceneCapture()
    {
        GameManager.Instance=new GameManager();var item=new DaggerfallUnityItem {UID=91};var window=new DaggerfallTradeWindow();window.Basket.AddItem(item);int receipts=0;
        using(var hook=new NativePlayerShopProvenanceHooks((a,b,c,d,e,f,g,h,i)=>receipts++,()=>{throw new InvalidOperationException("Scene unavailable.");}))
        {
            object state=Begin(typeof(NativePlayerShopProvenanceHooks),"BeforeSteal",window,null);
            window.Basket.RemoveItem(item);GameManager.Instance.PlayerEntity.Items.AddItem(item);End(typeof(NativePlayerShopProvenanceHooks),"AfterSteal",state);
        }
        Require(receipts==1,"Failed scene capture suppressed independent native item provenance.");passed++;
    }
    private static void MissingScenePublication()
    {
        GameManager.Instance=new GameManager();var first=new DaggerfallUnityItem {UID=101};var second=new DaggerfallUnityItem {UID=102,LongName="Shield"};
        var window=new DaggerfallTradeWindow();window.Basket.AddItem(first);window.Basket.AddItem(second);int receipts=0;
        using(var hook=new NativePlayerShopProvenanceHooks((a,b,c,d,e,f,g,h,i)=>receipts++,()=>((name,count)=>{throw new InvalidOperationException("Memory unavailable.");})))
        {
            object state=Begin(typeof(NativePlayerShopProvenanceHooks),"BeforeSteal",window,null);
            window.Basket.RemoveItem(first);window.Basket.RemoveItem(second);GameManager.Instance.PlayerEntity.Items.AddItem(first);GameManager.Instance.PlayerEntity.Items.AddItem(second);
            End(typeof(NativePlayerShopProvenanceHooks),"AfterSteal",state);
        }
        Require(receipts==2,"Failed memory publication suppressed another item's native provenance.");passed++;
    }
    private static void Scene(bool seen,bool invisible,bool restore)
    {
        GameManager.Instance=new GameManager();var store=new PeopleOfTheIliacBay.Core.WorldStateStore(13);
        var town=store.GetOrCreate(7,new[]{12},1);
        var person=store.RegisterNativePerson(7,12,24,"Shop witness",PeopleOfTheIliacBay.Core.ResidentSex.Female,0,0,182,1);
        var actor=new PeopleOfTheIliacBay.DFU.ResidentActor {Resident=town.Households[0].Residents[0]};actor.Observer.Visible=seen;
        var stationary=new PeopleOfTheIliacBay.DFU.StreetPopulation.StationaryParticipant {Person=person};stationary.Observer.Visible=seen;
        var streets=new PeopleOfTheIliacBay.DFU.StreetPopulation {worldStore=store,population=town};
        streets.actors.Add(actor.Resident.Id,actor);streets.stationaryParticipants.Add(person.Id,stationary);
        GameManager.Instance.PlayerEntity.IsInvisible=invisible;
        var method=streets.GetType().GetMethod("CaptureTheftMemory",BindingFlags.Instance|BindingFlags.NonPublic);
        var publish=method==null?null:(Action<string,int>)method.Invoke(streets,new object[]{null,"this shop"});
        actor.Observer.Visible=!seen;stationary.Observer.Visible=!seen;
        GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.buildingKey=999;
        if(restore)store.Restore(store.Snapshot());
        if(publish!=null)publish("Iron Dagger",2);
        var memories=store.Snapshot().SceneMemories;
        int expected=seen&&!invisible&&!restore?2:0;
        Require(memories.Count==expected,"Mobile/stationary producer did not preserve the event-time witness set or restore boundary.");
        foreach(var memory in memories)
        {
            Require(memory.BuildingKey==12&&memory.SettlementId==7,"Theft memory location changed after transfer.");
            Require(memory.ResponsibleId==PeopleOfTheIliacBay.Core.NpcDirectedRelationship.PlayerTargetId&&memory.SubjectId==null,"Theft invented a property owner or lost its seen actor.");
            Require(memory.Valence<0&&memory.ExpiresAt>memory.At,"Theft bypassed existing appraisal/retention.");
            Require(memory.Observation.Contains("2 Iron Dagger")&&memory.Observation.Contains("Copper House"),"Theft lost native item or location facts.");
        }
        passed++;
    }
}
