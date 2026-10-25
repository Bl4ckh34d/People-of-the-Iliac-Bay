// Only the Unity/DFU host is substituted. The linked production partial and Core
// store implement the commerce callbacks, identity matching, and pricing rules.
using System;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;

namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object value) { return value != null; }
        public static T FindObjectOfType<T>() { return default(T); }
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : Object { return gameObject.Component as T; }
    }
    public class GameObject
    {
        public bool activeInHierarchy = true;
        public Object Component;
        public T AddComponent<T>() where T : new() { return new T(); }
    }
}
namespace DaggerfallConnect
{
    public class DFLocation { public enum BuildingTypes { Shop, Tavern, GuildHall } public enum Holidays { New_Life = 1 } }
}
namespace DaggerfallConnect.Arena2 { }
namespace DaggerfallWorkshop
{
    public class StaticNPC : UnityEngine.MonoBehaviour
    {
        public enum Context { Building, Outside }
        public NpcData Data = new NpcData();
        public class NpcData { public Context context; public int mapID, buildingKey, hash, factionID; }
    }
    public class DaggerfallAction : UnityEngine.Object { }
    public class Interior : UnityEngine.Object
    {
        public StaticNPC[] Npcs = Array.Empty<StaticNPC>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return (T[])(object)Npcs; }
    }
}
namespace DaggerfallWorkshop.Game
{
    public class StateManager { public enum StateTypes { Start } }
    public class GameManager
    {
        public static GameManager Instance;
        public EnterExit PlayerEnterExit = new EnterExit();
        public GPS PlayerGPS = new GPS();
        public GuildManager GuildManager = new GuildManager();
    }
    public class EnterExit
    {
        public bool IsPlayerInside = true, IsPlayerInsideOpenShop = true;
        public Interior Interior = new Interior();
        public Building BuildingDiscoveryData = new Building();
    }
    public class Building { public int buildingKey = 5, quality = 10; public DFLocation.BuildingTypes buildingType; }
    public class GPS { public Location CurrentLocation = new Location(); }
    public class Location { public MapData MapTableData = new MapData(); }
    public class MapData { public int MapId = 1; }
}
namespace DaggerfallWorkshop.Game.Questing { public class QuestResourceBehaviour : UnityEngine.Object { } }
namespace DaggerfallWorkshop.Game.UserInterfaceWindows { }
namespace DaggerfallWorkshop.Game.Utility.ModSupport
{
    public class InvokeAttribute : Attribute { public InvokeAttribute(StateManager.StateTypes state, int order) { } }
    public class InitParams { }
}
namespace DaggerfallWorkshop.Utility
{
    public static class RMBLayout { public static bool IsShop(DFLocation.BuildingTypes type) { return type == DFLocation.BuildingTypes.Shop; } }
}
namespace PeopleOfTheIliacBay.DFU
{
    public static class NativeBusinessServices { public static bool IsKeeper(StaticNPC npc, DFLocation.BuildingTypes type) { return true; } }
    public sealed partial class PeopleOfTheIliacBayMod : UnityEngine.MonoBehaviour
    {
        internal bool saveFault, conversationDispositionEnabled = true;
        internal RuntimeOptions options = new RuntimeOptions(true, 6, 22, 1, true);
        internal WorldStateStore store = new WorldStateStore(42);
        internal int Disposition = -100, Reads;
        internal bool TryGetFreshConversationDisposition(string personId, out int disposition)
        { Reads++; disposition = Disposition; return true; }
        internal void SetCommerce(bool enabled)
        {
            typeof(PeopleOfTheIliacBayMod).GetField("dispositionCommerceEnabled", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(this, enabled);
        }
        internal int Purchase(StaticNPC npc, int price, bool room = false) { return AdjustNativeKeeperPurchasePrice(npc, price, room); }
        internal int Sale(StaticNPC npc, int price) { return AdjustNativeKeeperSalePrice(npc, price); }
        internal int Service(StaticNPC npc, int price) { return AdjustNativeKeeperServicePrice(npc, price); }
        internal int Shelf(int price) { return AdjustCurrentShelfPurchasePrice(price); }
        internal bool Serve(StaticNPC npc) { return CanServeNativeKeeper(npc); }
        internal bool ServeShelf() { return CanServeAtCurrentShelf(); }
        internal IDisposable InstallHooks() { return CreateDispositionPricingHooks(); }
    }
}
