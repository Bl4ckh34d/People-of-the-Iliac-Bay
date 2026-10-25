using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;
using PeopleOfTheIliacBay.QuestHooks;
using DaggerfallWorkshop.Game.UserInterfaceWindows;

internal static class CommerceRegressionRunner
{
    private static int Main()
    {
        var cases = new (string Name, Action Run)[] {
            ("Disabled commerce preserves all callback prices and permits native service", DisabledCallbacks),
            ("Disabled commerce skips shelf resolution and disposition lookup", DisabledSkipsResolution),
            ("Default and reenabled commerce retain prices and refusal", DefaultsAndReenable),
            ("Commerce keeps existing identity and global gates", ExistingGates),
            ("No-op shelf gates preserve native service without resolving the scene", ShelfNoOpGatesSkipResolution),
            ("Commerce toggle preserves registered people and saved state", KeepsSavedState),
            ("Shipped commerce setting defaults to existing behavior", SettingDefault),
            ("Disabled native trade hook leaves unresolved owners to native rules", DisabledUnresolvedTrade),
            ("Disabled native food hook preserves zero price and refuses no service", DisabledNativeFood),
            ("Native trade and room hooks obey the live commerce switch", NativeHooksFollowLiveSwitch),
            ("Production reflection factory connects both commerce hook policies", ProductionFactoryConnectsPolicies),
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Run(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"{cases.Length - failed}/{cases.Length} passed");
        return failed == 0 ? 0 : 1;
    }

    private static PeopleOfTheIliacBayMod Host(out StaticNPC npc)
    {
        var host = new PeopleOfTheIliacBayMod();
        host.store.GetOrCreate(1, new[] { 5 });
        host.store.RegisterNativePerson(1, 5, 12, "Test Keeper", ResidentSex.Male, 1, 41, 182, 2);
        npc = new StaticNPC { Data = new StaticNPC.NpcData {
            context = StaticNPC.Context.Building, mapID = 1, buildingKey = 5, hash = 12, factionID = 41 } };
        GameManager.Instance = new GameManager();
        GameManager.Instance.PlayerEnterExit.Interior.Npcs = new[] { npc };
        return host;
    }

    private static void DisabledCallbacks()
    {
        var host = Host(out var npc);
        host.SetCommerce(false);
        foreach (int disposition in new[] { -100, -50, 100 })
        {
            host.Disposition = disposition;
            foreach (int price in new[] { -1, 0, 1, 100, int.MaxValue })
            {
                Equal(price, host.Purchase(npc, price));
                Equal(price, host.Sale(npc, price));
                Equal(price, host.Service(npc, price));
                Equal(price, host.Shelf(price));
                GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.buildingType = DFLocation.BuildingTypes.Tavern;
                Equal(price, host.Purchase(npc, price, true));
                GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.buildingType = DFLocation.BuildingTypes.Shop;
            }
            Equal(true, host.Serve(npc));
            Equal(true, host.ServeShelf());
        }
        Equal(0, host.Reads);
        Equal(true, host.conversationDispositionEnabled);
    }

    private static void DisabledSkipsResolution()
    {
        var host = Host(out var npc);
        host.SetCommerce(false);
        host.store = null;
        // A disabled callback must not inspect an incomplete scene or store.
        GameManager.Instance.PlayerEnterExit = null;
        Equal(100, host.Shelf(100));
        Equal(true, host.ServeShelf());
        Equal(100, host.Service(npc, 100));
        Equal(true, host.Serve(npc));
        Equal(0, host.Reads);
    }

    private static void DefaultsAndReenable()
    {
        var host = Host(out var npc);
        Equal(150, host.Purchase(npc, 100));
        Equal(50, host.Sale(npc, 100));
        Equal(150, host.Service(npc, 100));
        Equal(150, host.Shelf(100));
        Equal(false, host.Serve(npc));
        Equal(false, host.ServeShelf());
        GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.buildingType = DFLocation.BuildingTypes.Tavern;
        Equal(150, host.Purchase(npc, 100, true));
        host.SetCommerce(false);
        Equal(100, host.Service(npc, 100));
        host.SetCommerce(true);
        Equal(150, host.Service(npc, 100));
        host.Disposition = 100;
        Equal(85, host.Purchase(npc, 100));
        Equal(115, host.Sale(npc, 100));
        Equal(true, host.Serve(npc));
        host.Disposition = -50;
        Equal(false, host.Serve(npc));
        host.Disposition = -49;
        Equal(true, host.Serve(npc));
    }

    private static void ExistingGates()
    {
        var host = Host(out var npc);
        npc.Data.hash = 13;
        Equal(100, host.Purchase(npc, 100));
        Equal(true, host.Serve(npc));
        npc.Data.hash = 12;
        npc.gameObject.Component = new DaggerfallWorkshop.Game.Questing.QuestResourceBehaviour();
        Equal(100, host.Service(npc, 100));
        Equal(true, host.Serve(npc));
        npc.gameObject.Component = null;
        host.conversationDispositionEnabled = false;
        Equal(100, host.Service(npc, 100));
        Equal(true, host.Serve(npc));
        host.conversationDispositionEnabled = true;
        host.saveFault = true;
        Equal(100, host.Service(npc, 100));
        Equal(true, host.Serve(npc));
        host.saveFault = false;
        host.options = new RuntimeOptions(false, 6, 22, 1, true);
        Equal(100, host.Service(npc, 100));
        Equal(true, host.Serve(npc));
    }

    private static void ShelfNoOpGatesSkipResolution()
    {
        Action<PeopleOfTheIliacBayMod>[] disableGates = {
            host => host.options = new RuntimeOptions(false, 6, 22, 1, true),
            host => host.conversationDispositionEnabled = false,
            host => host.saveFault = true,
            host => host.store = null,
            host => host.SetCommerce(false)
        };

        foreach (var disableGate in disableGates)
        {
            var host = Host(out _);
            disableGate(host);
            GameManager.Instance.PlayerEnterExit = null;
            Equal(true, host.ServeShelf());
        }
    }

    private static void KeepsSavedState()
    {
        var host = Host(out var npc);
        var options = new JsonSerializerOptions { IncludeFields = true };
        string before = JsonSerializer.Serialize(host.store.Snapshot(), options);
        host.SetCommerce(false);
        host.Purchase(npc, 100);
        host.Serve(npc);
        host.SetCommerce(true);
        host.Service(npc, 100);
        Equal(before, JsonSerializer.Serialize(host.store.Snapshot(), options));
    }

    private static void SettingDefault()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "modsettings.json")));
        var dialogue = config.RootElement.GetProperty("Sections").EnumerateArray().Single(s => s.GetProperty("Name").GetString() == "Dialogue");
        var setting = dialogue.GetProperty("Keys").EnumerateArray().Where(k => k.GetProperty("Name").GetString() == "DispositionCommerce").ToArray();
        Equal(1, setting.Length);
        Equal(true, setting[0].GetProperty("Value").GetBoolean());
    }

    private static NativeDispositionPricingHooks Hooks(bool enabled)
    {
        var hooks = new NativeDispositionPricingHooks((npc, price) => price + 10, price => price + 10,
            (npc, price) => price - 10, (npc, price) => price + 10, npc => false, () => false,
            (npc, price) => price + 10, (input, rented) => 1, () => enabled);
        return hooks;
    }

    private static object Patch(string method, params object[] args)
    {
        return typeof(NativeDispositionPricingHooks).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    }

    private static void DisabledUnresolvedTrade()
    {
        using var hooks = Hooks(false);
        DaggerfallUI.Messages = 0;
        foreach (var mode in new[] { DaggerfallTradeWindow.WindowModes.Buy, DaggerfallTradeWindow.WindowModes.Sell,
            DaggerfallTradeWindow.WindowModes.Repair, DaggerfallTradeWindow.WindowModes.Identify })
        {
            var window = new DaggerfallTradeWindow { WindowMode = mode, PreviousWindow = new DaggerfallPopupWindow() };
            DaggerfallUI.UIManager.TopWindow = window;
            Equal(true, Patch("BeforeTradeConfirm", window, DaggerfallMessageBox.MessageBoxButtons.Yes));
            Equal(false, window.Closed);
            var priceArgs = new object[] { window, 100 };
            Patch("AfterShopTradePrice", priceArgs);
            Equal(100, priceArgs[1]);
        }
        Equal(0, DaggerfallUI.Messages);
    }

    private static void DisabledNativeFood()
    {
        using var hooks = Hooks(false);
        var window = new DaggerfallTavernWindow();
        byte[] prices = { 0, 10 };
        var args = new object[] { window, 0, new StaticNPC(), prices, null };
        Equal(true, Patch("BeforeFoodPurchase", args));
        Equal((byte)0, prices[0]);
        Equal(false, window.Closed);
        Equal(null, args[4]);
    }

    private static void NativeHooksFollowLiveSwitch()
    {
        bool enabled = true;
        using var hooks = new NativeDispositionPricingHooks((npc, price) => price + 10, price => price + 10,
            (npc, price) => price - 10, (npc, price) => price + 10, npc => false, () => false,
            (npc, price) => price + 10, (input, rented) => 1, () => enabled);
        var keeper = new StaticNPC();
        foreach (var mode in new[] { DaggerfallTradeWindow.WindowModes.Buy, DaggerfallTradeWindow.WindowModes.Sell,
            DaggerfallTradeWindow.WindowModes.SellMagic, DaggerfallTradeWindow.WindowModes.Repair, DaggerfallTradeWindow.WindowModes.Identify })
        {
            var window = new DaggerfallTradeWindow { WindowMode = mode,
                PreviousWindow = new DaggerfallMerchantServicePopupWindow { merchantNPC = keeper } };
            DaggerfallUI.UIManager.TopWindow = window;
            enabled = true;
            var args = new object[] { window, 100 };
            Patch("AfterShopTradePrice", args);
            Equal(mode == DaggerfallTradeWindow.WindowModes.Sell || mode == DaggerfallTradeWindow.WindowModes.SellMagic ? 90 : 110, args[1]);
            enabled = false;
            args[1] = 100;
            Patch("AfterShopTradePrice", args);
            Equal(100, args[1]);
            Equal(true, Patch("BeforeTradeConfirm", window, DaggerfallMessageBox.MessageBoxButtons.Yes));
            Equal(false, window.Closed);
        }

        var room = new DaggerfallTavernWindow();
        GameManager.Instance = new GameManager();
        enabled = true;
        var quote = new object[] { room, "1", keeper, null, null };
        Patch("BeforeRoomQuote", quote);
        var roomPrice = new object[] { 10, 10, false, 100 };
        Patch("AfterNativeRoomTradePrice", roomPrice);
        Equal(110, roomPrice[3]);
        enabled = false;
        roomPrice[3] = 100;
        Patch("AfterNativeRoomTradePrice", roomPrice);
        Equal(100, roomPrice[3]);
        Equal(true, Patch("BeforeRoomConfirm", room, DaggerfallMessageBox.MessageBoxButtons.Yes, keeper, 100));
        Equal(false, room.Closed);
        Patch("AfterRoomQuote", null, quote[4]);
        quote[4] = null;
        Patch("BeforeRoomQuote", quote);
        roomPrice[3] = 100;
        Patch("AfterNativeRoomTradePrice", roomPrice);
        Equal(100, roomPrice[3]);
        Patch("AfterRoomQuote", null, quote[4]);

        enabled = true;
        Equal(false, Patch("BeforeRoomConfirm", room, DaggerfallMessageBox.MessageBoxButtons.Yes, keeper, 100));
        Equal(true, room.Closed);
    }

    private static void ProductionFactoryConnectsPolicies()
    {
        var host = Host(out var keeper);
        using var hooks = host.InstallHooks();
        var window = new DaggerfallTradeWindow { WindowMode = DaggerfallTradeWindow.WindowModes.Buy,
            PreviousWindow = new DaggerfallMerchantServicePopupWindow { merchantNPC = keeper } };
        DaggerfallUI.UIManager.TopWindow = window;
        var price = new object[] { window, 100 };
        Patch("AfterShopTradePrice", price);
        Equal(150, price[1]);
        Equal(150, NativeDispositionServiceHooks.Active.Price(keeper, 100));
        Equal(false, NativeDispositionServiceHooks.Active.CanServe(keeper));
        host.SetCommerce(false);
        price[1] = 100;
        Patch("AfterShopTradePrice", price);
        Equal(100, price[1]);
        Equal(true, Patch("BeforeTradeConfirm", window, DaggerfallMessageBox.MessageBoxButtons.Yes));
        window.PreviousWindow = new DaggerfallPopupWindow();
        Equal(true, Patch("BeforeTradeConfirm", window, DaggerfallMessageBox.MessageBoxButtons.Yes));
        Equal(100, NativeDispositionServiceHooks.Active.Price(keeper, 100));
        Equal(true, NativeDispositionServiceHooks.Active.CanServe(keeper));
        host.SetCommerce(true);
        Equal(150, NativeDispositionServiceHooks.Active.Price(keeper, 100));
        Equal(false, NativeDispositionServiceHooks.Active.CanServe(keeper));
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
