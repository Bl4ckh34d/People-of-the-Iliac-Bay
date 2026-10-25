using System;
using System.Reflection;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

namespace PeopleOfTheIliacBay.QuestHooks
{
    // Capture the actual factory's service callbacks without installing the
    // separate guild UI patch collection. Their runtime policy remains real.
    public class NativeDispositionServiceHooks : IDisposable
    {
        public static NativeDispositionServiceHooks Active;
        public readonly Func<DaggerfallWorkshop.StaticNPC, bool> CanServe;
        public readonly Func<DaggerfallWorkshop.StaticNPC, int, int> Price;
        public NativeDispositionServiceHooks(Func<DaggerfallWorkshop.StaticNPC, bool> canServe,
            Func<DaggerfallWorkshop.StaticNPC, int, int> price)
        { CanServe = canServe; Price = price; Active = this; }
        public void Dispose() { Active = null; }
    }
}

namespace HarmonyLib
{
    // Patches are not installed in this CPU fixture. Tests invoke the real patch
    // callbacks directly with controlled windows, without a Unity process.
    public class Harmony { public Harmony(string owner) { } public void Patch(MethodInfo original, HarmonyMethod prefix = null, HarmonyMethod postfix = null, HarmonyMethod finalizer = null) { } public void UnpatchAll(string owner) { } }
    public class HarmonyMethod { public HarmonyMethod(MethodInfo method) { } public int priority; }
    public static class Priority { public const int First = 800; }
    public static class AccessTools
    {
        public const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public static MethodInfo Method(Type type, string name, Type[] parameters = null) { return typeof(AccessTools).GetMethod(nameof(PatchTarget)); }
        public static void PatchTarget() { }
        public static PropertyInfo Property(Type type, string name) { return type.GetProperty(name, all); }
        public static FieldInfo Field(Type type, string name) { return type.GetField(name, all); }
    }
}
namespace UnityEngine { public static class Debug { public static void LogWarning(string message) { throw new InvalidOperationException(message); } } }
namespace DaggerfallConnect.Arena2 { public class FactionFile { public enum GuildGroups { KnightlyOrder } } }
namespace DaggerfallWorkshop
{
    public class DaggerfallUnity
    {
        public static DaggerfallUnity Instance = new DaggerfallUnity();
        public WorldTime WorldTime = new WorldTime();
    }
    public class WorldTime
    {
        public DaggerfallWorkshop.Utility.DaggerfallDateTime DaggerfallDateTime = new DaggerfallWorkshop.Utility.DaggerfallDateTime();
        public DaggerfallWorkshop.Utility.DaggerfallDateTime Now = new DaggerfallWorkshop.Utility.DaggerfallDateTime();
    }
}
namespace DaggerfallWorkshop.Utility
{
    public class DaggerfallDateTime
    {
        public const int SecondsPerDay = 86400;
        public uint ToClassicDaggerfallTime() { return 0; }
        public ulong ToSeconds() { return 0; }
    }
}
namespace DaggerfallWorkshop.Game
{
    public static class DaggerfallUI
    {
        public static UserInterfaceManager UIManager = new UserInterfaceManager();
        public static int Messages;
        public static void MessageBox(string text) { Messages++; }
    }
    public class GuildManager
    {
        public Guild GetGuild(DaggerfallConnect.Arena2.FactionFile.GuildGroups group) { return null; }
    }
    public class Guild { public bool FreeTavernRooms() { return false; } }
}
namespace DaggerfallWorkshop.Game.Serialization { public class RoomRental_v1 { public ulong expiryTime; } }
namespace DaggerfallWorkshop.Game.Formulas
{
    public static class FormulaHelper
    {
        public static int CalculateTradePrice(int cost, int quality, bool selling) { return cost; }
        public static int GetHolidayId(uint minutes, int region) { return 0; }
        public static int CalculateRoomCost(int days) { return days * 10; }
    }
}
namespace DaggerfallWorkshop.Game.UserInterface
{
    public interface IUserInterfaceWindow { }
    public class UserInterfaceManager { public IUserInterfaceWindow TopWindow; }
}
namespace DaggerfallWorkshop.Game.UserInterfaceWindows
{
    public class DaggerfallPopupWindow : IUserInterfaceWindow
    {
        public IUserInterfaceWindow PreviousWindow;
        public bool Closed;
        public void CloseWindow() { Closed = true; }
    }
    public class DaggerfallTradeWindow : DaggerfallPopupWindow
    {
        public enum WindowModes { Buy, Sell, SellMagic, Repair, Identify, Other }
        public WindowModes WindowMode { get; set; }
    }
    public class DaggerfallTavernWindow : DaggerfallPopupWindow { }
    public class DaggerfallMessageBox : DaggerfallPopupWindow { public enum MessageBoxButtons { Yes, No } }
    public class DaggerfallMerchantServicePopupWindow : DaggerfallPopupWindow { public DaggerfallWorkshop.StaticNPC merchantNPC; }
    public class DaggerfallMerchantRepairPopupWindow : DaggerfallPopupWindow { public DaggerfallWorkshop.StaticNPC merchantNPC; }
    public class DaggerfallGuildServicePopupWindow : DaggerfallPopupWindow { public DaggerfallWorkshop.StaticNPC serviceNPC; }
}
