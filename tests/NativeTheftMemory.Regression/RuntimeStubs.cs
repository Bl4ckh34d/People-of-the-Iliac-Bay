// Only native engine/framework dependencies are substituted. Tests execute the production hook methods.
using System;
using System.Collections.Generic;
using System.Reflection;
namespace HarmonyLib
{
    public sealed class Harmony { public Harmony(string id) {} public void Patch(MethodInfo method,HarmonyMethod prefix=null,HarmonyMethod postfix=null) {} public void UnpatchAll(string owner) {} }
    public sealed class HarmonyMethod { public HarmonyMethod(MethodInfo method) {} public HarmonyMethod(Type type,string name) {} }
}
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value!=null; } }
    public struct Vector3 { public float x,y,z; }
    public class Transform:Object { public Vector3 position; }
    public class GameObject:Object { public bool activeInHierarchy=true; }
    public class MonoBehaviour:Object { public T GetComponent<T>() where T:class { return null; } }
    public static class Debug { public static void LogWarning(object value) { Console.WriteLine(value); } }
}
namespace DaggerfallWorkshop
{
    public sealed class DaggerfallUnity { public static DaggerfallUnity Instance=new DaggerfallUnity();public Clock WorldTime=new Clock(); }
    public sealed class Clock { public Moment Now=new Moment(); }
    public sealed class Moment { public double At=100; public double ToSeconds() { return At; } }
}
namespace DaggerfallWorkshop.Game.Items
{
    public enum ItemGroups { Currency, Weapons }
    public enum Currency { Gold_pieces }
    public sealed class DaggerfallUnityItem
    {
        public ulong UID; public int stackCount=1; public bool NameUnavailable; private string name="Iron Dagger";
        public string LongName { get { if(NameUnavailable)throw new InvalidOperationException("Native item label unavailable.");return name; } set { name=value; } }
        public ItemGroups ItemGroup=ItemGroups.Weapons;
        public bool IsOfTemplate(ItemGroups group,int index) { return ItemGroup==group; }
    }
    public sealed class ItemCollection
    {
        private readonly List<DaggerfallUnityItem> items=new List<DaggerfallUnityItem>();
        public int Count { get { return items.Count; } }
        public void AddItem(DaggerfallUnityItem item) { items.Add(item); }
        public void RemoveItem(DaggerfallUnityItem item) { items.Remove(item); }
        public bool Contains(ulong uid) { return GetItem(uid)!=null; }
        public DaggerfallUnityItem GetItem(int index) { return items[index]; }
        public DaggerfallUnityItem GetItem(ulong uid) { return items.Find(item=>item.UID==uid); }
        private DaggerfallUnityItem FindExistingStack(DaggerfallUnityItem item) { return items.Find(other=>other.LongName==item.LongName); }
    }
}
namespace DaggerfallWorkshop.Game
{
    using DaggerfallWorkshop.Game.Items;
    public sealed class DaggerfallLoot:UnityEngine.Object { public bool houseOwned=true,playerOwned; public ItemCollection Items=new ItemCollection();public UnityEngine.Transform transform=new UnityEngine.Transform(); }
    public sealed class Player { public ItemCollection Items=new ItemCollection(); public bool IsInvisible;public int GoldPieces=100; public int GetGoldAmount() { return GoldPieces; } }
    public sealed class EnterExit { public Discovery BuildingDiscoveryData=new Discovery();public bool IsPlayerInsideBuilding=true; }
    public sealed class Discovery { public int buildingKey=12; }
    public sealed class GPS { public bool HasCurrentLocation=true; public Location CurrentLocation=new Location(); }
    public sealed class Location { public Map MapTableData=new Map(); }
    public sealed class Map { public int MapId=7; }
    public sealed class Motor { public UnityEngine.Transform transform=new UnityEngine.Transform(); }
    public sealed class Talk { public string GetBuildingNameForBuildingKey(int key) { return "Copper House"; } }
    public sealed class GameManager
    { public static GameManager Instance=new GameManager(); public bool IsReady=true;public Player PlayerEntity=new Player(); public EnterExit PlayerEnterExit=new EnterExit(); public GPS PlayerGPS=new GPS();public Motor PlayerMotor=new Motor();public Talk TalkManager=new Talk(); }
}
namespace PeopleOfTheIliacBay.DFU
{
    using PeopleOfTheIliacBay.Core;
    using DaggerfallWorkshop.Game.UserInterfaceWindows;
    public sealed class NativeNpcSceneObserver:UnityEngine.Object
    { public bool Visible=true; public bool Sees(UnityEngine.Vector3 position,float range,UnityEngine.Transform observedObject=null) { return Visible; } }
    public sealed class ResidentActor:UnityEngine.Object
    { public Resident Resident;public NativeNpcSceneObserver Observer=new NativeNpcSceneObserver();public T GetComponent<T>() where T:class { return Observer as T; } }
    public sealed class PeopleOfTheIliacBayMod:UnityEngine.Object { }
    public sealed partial class StreetPopulation:UnityEngine.MonoBehaviour
    {
        internal bool Ready=true,Consent=false;
        internal SettlementPopulation population;
        internal WorldStateStore worldStore;
        internal Dictionary<string,ResidentActor> actors=new Dictionary<string,ResidentActor>();
        internal sealed class StationaryParticipant { internal NativePerson Person;internal NativeNpcSceneObserver Observer=new NativeNpcSceneObserver(); }
        internal Dictionary<string,StationaryParticipant> stationaryParticipants=new Dictionary<string,StationaryParticipant>();
        private bool CanTakeHouseholdLoot(PeopleOfTheIliacBayMod host,DaggerfallInventoryWindow window) { return Consent; }
    }
}
namespace DaggerfallWorkshop.Game.Formulas
{ public static class FormulaHelper { public static int CalculateShopliftingChance() { return 50; } } }
namespace DaggerfallWorkshop.Game.UserInterfaceWindows
{
    using DaggerfallWorkshop.Game.Items;
    public sealed class DaggerfallMessageBox { public enum MessageBoxButtons { Yes,No } }
    public class DaggerfallTradeWindow
    {
        public enum WindowModes { Buy,Sell }
        protected WindowModes WindowMode { get; set; }=WindowModes.Buy;
        protected ItemCollection basketItems=new ItemCollection();
        protected int GetTradePrice() { return 10; }
        protected virtual void ConfirmTrade_OnButtonClick(DaggerfallMessageBox box,DaggerfallMessageBox.MessageBoxButtons button) {}
        private void DoSteal() {}
        public ItemCollection Basket { get { return basketItems; } }
    }
    public class DaggerfallInventoryWindow
    {
        public DaggerfallLoot LootTarget { get; set; }
        protected void DoTransferItem(DaggerfallUnityItem item,ItemCollection from,ItemCollection to,bool equip) {}
    }
}
