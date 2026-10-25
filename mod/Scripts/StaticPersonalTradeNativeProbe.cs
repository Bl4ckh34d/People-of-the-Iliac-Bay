using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit disposable native fixture. It uses the native static-talk entry
    // and the exact detached production session; it never substitutes a mouse
    // event, a resident actor, a vendor shelf, or a service collection.
    internal static class StaticPersonalTradeNativeProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string FixtureCharacterPrefix = "PeopleOfTheIliacBayTest-";
        private const string FixtureLocation = "Gothway Garden";

        private sealed class ShopFixture
        {
            public Transform DoorOwner;
            public StaticDoor Door;
            public int BuildingKey;
            public NativePerson Person;
        }

        private sealed class InventorySnapshot
        {
            public ItemCollection Items;
            public List<ulong> Uids = new List<ulong>();
            public List<int> Counts = new List<int>();
        }

        internal static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            var game = GameManager.Instance;
            Require(Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE") == "static-personal-trade" && host && streets &&
                character != null && character.StartsWith(FixtureCharacterPrefix, StringComparison.Ordinal),
                "Static personal trade requires its explicit owned fixture.");
            Require(game != null && game.IsReady && game.PlayerEntity != null && game.PlayerGPS.HasCurrentLocation &&
                game.PlayerGPS.CurrentLocation.Name == FixtureLocation, "Static personal trade requires the Gothway Garden fixture.");
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((12*3600-clock.ToSeconds()%86400+86400)%86400));
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            if (game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior();
                yield return WaitForExterior(game);
            }
            Require(streets.Ready, "Static personal trade requires a ready street population.");
            var store = GetStore(streets);
            Require(store != null, "Static personal trade has no authoritative world store.");
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            Require(directory != null, "Gothway Garden has no loaded native building directory.");
            int settlement = game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            ShopFixture fixture = SelectOpenShop(directory);
            Require(fixture != null, "No currently open Gothway shop has a native entrance.");

            TownSmokeHarness.EnterInterior(fixture.DoorOwner, fixture.Door);
            float deadline = Time.realtimeSinceStartup + 45f;
            while ((!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey != fixture.BuildingKey) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            Require(game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior &&
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey == fixture.BuildingKey &&
                game.PlayerEnterExit.IsPlayerInsideOpenShop && PlayerActivate.IsBuildingOpen(game.PlayerEnterExit.Interior.BuildingData.BuildingType),
                "Actual entry did not reach the selected open native shop.");

            StaticNPC keeper = null;
            deadline = Time.realtimeSinceStartup + 30f;
            while (!keeper && Time.realtimeSinceStartup < deadline)
            {
                keeper = FindRegisteredKeeper(game, fixture, store, settlement);
                if (!keeper) yield return null;
            }
            Require(keeper != null && NativeStaticNpcInventoryOwner.Matches(keeper, fixture.Person) &&
                NativeBusinessServices.IsKeeper(keeper, game.PlayerEnterExit.Interior.BuildingData.BuildingType),
                "Actual shop keeper did not match the selected registered NativePerson.");
            var shelves = SnapshotShelves(game.PlayerEnterExit.Interior);
            var playerItemsBefore = Snapshot(game.PlayerEntity.Items);
            int originalGold = game.PlayerEntity.GoldPieces;
            short originalSkill = game.PlayerEntity.Skills.GetPermanentSkillValue(DFCareer.Skills.Mercantile);
            try
            {
                game.PlayerEntity.GoldPieces = 5000;
                game.PlayerEntity.Skills.SetPermanentSkillValue(DFCareer.Skills.Mercantile, 20);

                DaggerfallTalkWindow talk = null;
                yield return OpenNativeTalk(host, game, keeper, fixture.Person.Id, value => talk = value);
                PersonalTradeWindow window = Open(host, talk);
                var firstSession = Session(window);
                Require(firstSession.NpcId == fixture.Person.Id && firstSession.NpcItems != null &&
                    !ReferenceEquals(firstSession.NpcItems, game.PlayerEntity.Items),
                    "Static trade did not capture the exact static personal inventory.");

                // The production factory has already run its live hate/busy/quest
                // gates and established this real personal ledger. This item is
                // controlled disposable test stock, never generated shelf stock.
                NativeStaticNpcInventoryOwner owner;
                string reason;
                Require(NativeStaticNpcInventoryOwner.TryOpen(store, keeper, fixture.Person, NpcArchetype.Merchant,
                    out owner, out reason) && owner != null && owner.Binding != null, reason);
                var seeded = ItemBuilder.CreateItem(ItemGroups.UselessItems1, (int)UselessItems1.Glass_Jar);
                seeded.value = 100;
                seeded.stackCount = 2;
                owner.Binding.Items.AddItem(seeded, noStack: true);
                Require(NativeNpcInventoryBridge.TryCapture(store, owner.Binding, out reason), reason);
                ulong seededUid = seeded.UID;
                Call(window, "CloseUnchanged");
                yield return null;
                Require(!(DaggerfallUI.UIManager.TopWindow is PersonalTradeWindow),
                    "Fixture setup did not close the initial unchanged native trade window.");
                RequireShelvesUnchanged(shelves);

                yield return OpenNativeTalk(host, game, keeper, fixture.Person.Id, value => talk = value);
                window = Open(host, talk);
                var session = Session(window);
                var offered = session.NpcItems.GetItem(seededUid);
                Require(offered != null && offered.UID == seededUid && offered.stackCount == 2,
                    "Controlled item is absent from the actual static personal inventory.");
                int previewCount = offered.stackCount;
                int previewPlayerGold = game.PlayerEntity.GoldPieces;
                var previewSaved = store.ReadNpcInventory(fixture.Person.Id);
                Stage(window, PersonalTradeSide.Npc, offered, 1);
                Call(window, "RequestQuote");
                Require(session.NpcItems.GetItem(seededUid) == offered && offered.stackCount == previewCount &&
                    game.PlayerEntity.Items.GetItem(seededUid) == null && game.PlayerEntity.GoldPieces == previewPlayerGold &&
                    store.ReadNpcInventory(fixture.Person.Id).Revision == previewSaved.Revision,
                    "Static trade preview touched inventory, wallet, or store state.");
                Call(window, "CloseUnchanged");
                yield return null;
                Require(session.NpcItems.GetItem(seededUid) == offered && offered.stackCount == previewCount &&
                    game.PlayerEntity.GoldPieces == previewPlayerGold && store.ReadNpcInventory(fixture.Person.Id).Revision == previewSaved.Revision,
                    "Static trade cancel changed the owned item, gold, or inventory revision.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_PERSONAL_TRADE_CANCEL_PASS: nativeEntry=true; nativePerson="+
                    fixture.Person.Id+"; staticUid="+seededUid+"; shelfStockUntouched=true; playerStoreUntouched=true.");

                yield return OpenNativeTalk(host, game, keeper, fixture.Person.Id, value => talk = value);
                window = Open(host, talk);
                session = Session(window);
                offered = session.NpcItems.GetItem(seededUid);
                Require(offered != null && offered.stackCount == 2, "Static personal item changed before actual purchase.");
                Stage(window, PersonalTradeSide.Npc, offered, 1);
                Call(window, "RequestQuote");
                var quote = Quote(window);
                Require(quote != null && quote.NetGoldToNpc > 0 && !quote.CanSubmit,
                    "Static purchase did not produce a signed native baseline at Mercantile 20.");
                SetAcceptedOffer(window, quote.NetGoldToNpc);
                Call(window, "RequestQuote");
                quote = Quote(window);
                Require(quote != null && quote.CanSubmit && quote.NetGoldToNpc > 0,
                    "Static purchase did not accept the displayed native total.");
                int buyPrice = quote.NetGoldToNpc;
                int playerGoldBeforeBuy = game.PlayerEntity.GoldPieces;
                int npcGoldBeforeBuy = store.ReadNpcInventory(fixture.Person.Id).WalletGold;
                int skillUsesBeforeBuy = game.PlayerEntity.SkillUses[(int)DFCareer.Skills.Mercantile];
                var playerUidsBeforeBuy = Uids(game.PlayerEntity.Items);
                Call(window, "SubmitOffer");
                yield return null;
                yield return null;
                var bought = FindNewItem(game.PlayerEntity.Items, playerUidsBeforeBuy, offered.ItemGroup, offered.TemplateIndex);
                Require(bought != null && bought.UID != seededUid && bought.stackCount == 1 &&
                    session.NpcItems.GetItem(seededUid) == offered && offered.stackCount == 1,
                    "Static purchase did not transfer one real personal-stock UID.");
                Require(game.PlayerEntity.GoldPieces == playerGoldBeforeBuy - buyPrice &&
                    store.ReadNpcInventory(fixture.Person.Id).WalletGold == npcGoldBeforeBuy + buyPrice &&
                    game.PlayerEntity.SkillUses[(int)DFCareer.Skills.Mercantile] == skillUsesBeforeBuy + 1,
                    "Static purchase used incorrect native gold or Mercantile tally.");
                RequireShelvesUnchanged(shelves);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_PERSONAL_TRADE_BUY_PASS: nativeWindow=true; nativePerson="+
                    fixture.Person.Id+"; sourceUid="+seededUid+"; receivedUid="+bought.UID+"; gold="+buyPrice+"; lowMercantile20=true.");

                yield return OpenNativeTalk(host, game, keeper, fixture.Person.Id, value => talk = value);
                window = Open(host, talk);
                session = Session(window);
                Require(session.PlayerItems.GetItem(bought.UID) == bought, "Bought item did not remain the exact player-owned UID for sale.");
                Stage(window, PersonalTradeSide.Player, bought, 1);
                Call(window, "RequestQuote");
                quote = Quote(window);
                Require(quote != null && quote.NetGoldToNpc < 0 && !quote.CanSubmit,
                    "Static sale did not show the NPC payment baseline.");
                SetAcceptedOffer(window, quote.NetGoldToNpc);
                Call(window, "RequestQuote");
                quote = Quote(window);
                Require(quote != null && quote.CanSubmit && quote.NetGoldToNpc < 0,
                    "Static sale did not accept the displayed native payment.");
                int saleNet = quote.NetGoldToNpc;
                int playerGoldBeforeSale = game.PlayerEntity.GoldPieces;
                int npcGoldBeforeSale = store.ReadNpcInventory(fixture.Person.Id).WalletGold;
                int skillUsesBeforeSale = game.PlayerEntity.SkillUses[(int)DFCareer.Skills.Mercantile];
                Call(window, "SubmitOffer");
                yield return null;
                yield return null;
                Require(game.PlayerEntity.Items.GetItem(bought.UID) == null && session.NpcItems.GetItem(bought.UID) == bought &&
                    game.PlayerEntity.GoldPieces == playerGoldBeforeSale - saleNet &&
                    store.ReadNpcInventory(fixture.Person.Id).WalletGold == npcGoldBeforeSale + saleNet &&
                    game.PlayerEntity.SkillUses[(int)DFCareer.Skills.Mercantile] == skillUsesBeforeSale + 1,
                    "Static sale did not move the exact UID or settle native gold once.");
                RequireSameSnapshot(playerItemsBefore, game.PlayerEntity.Items, "Static trade left a fixture item in player inventory.");
                RequireShelvesUnchanged(shelves);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_PERSONAL_TRADE_SELL_PASS: nativeWindow=true; nativePerson="+
                    fixture.Person.Id+"; returnedUid="+bought.UID+"; net="+saleNet+"; shelfStockUntouched=true; playerStoreUntouched=true.");
            }
            finally
            {
                DaggerfallUI.Instance.PopToHUD();
                game.PlayerEntity.Skills.SetPermanentSkillValue(DFCareer.Skills.Mercantile, originalSkill);
                game.PlayerEntity.GoldPieces = originalGold;
            }
        }

        private static IEnumerator WaitForExterior(GameManager game)
        {
            float deadline = Time.realtimeSinceStartup + 45f;
            while ((game.PlayerEnterExit.IsPlayerInside || game.StreamingWorld.IsRepositioningPlayer || !game.StreamingWorld.IsReady) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            Require(!game.PlayerEnterExit.IsPlayerInside && !game.StreamingWorld.IsRepositioningPlayer && game.StreamingWorld.IsReady,
                "Static personal trade could not return to a ready Gothway exterior.");
        }

        private static ShopFixture SelectOpenShop(BuildingDirectory directory)
        {
            if (directory == null) return null;
            foreach (var building in directory.GetBuildingsOfType(DFLocation.BuildingTypes.GeneralStore))
            {
                if (!PlayerActivate.IsBuildingOpen(building.BuildingType)) continue;
                foreach (var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var door in doors.Doors)
                    if (door.doorType == DoorTypes.Building && door.buildingKey == building.buildingKey)
                        return new ShopFixture { DoorOwner = doors.transform, Door = door, BuildingKey = building.buildingKey };
            }
            return null;
        }

        private static StaticNPC FindRegisteredKeeper(GameManager game, ShopFixture fixture, WorldStateStore store, int settlement)
        {
            if (game.PlayerEnterExit.Interior == null || fixture == null) return null;
            var people = store.NativePeopleFor(settlement);
            foreach (var candidate in game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>(true))
            {
                if (!candidate || candidate.Data.buildingKey != fixture.BuildingKey ||
                    !NativeBusinessServices.IsKeeper(candidate, game.PlayerEnterExit.Interior.BuildingData.BuildingType)) continue;
                var person = people.Find(entry => NativeStaticNpcInventoryOwner.Matches(candidate, entry));
                if (person == null) continue;
                fixture.Person = person;
                return candidate;
            }
            return null;
        }

        private static IEnumerator OpenNativeTalk(PeopleOfTheIliacBayMod host, GameManager game, StaticNPC keeper,
            string expectedPersonId, Action<DaggerfallTalkWindow> completed)
        {
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToStaticNPC(keeper, false);
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if (talk != null && talk.IsSetup && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Static &&
                    game.TalkManager.StaticNPC == keeper && ConversationOpen(host) && !ConversationBusy(host) &&
                    ConversationPersonId(host) == expectedPersonId)
                { completed(talk); yield break; }
                yield return null;
            }
            throw new InvalidOperationException("Native static Talk did not bind the exact registered shop keeper.");
        }

        private static PersonalTradeWindow Open(PeopleOfTheIliacBayMod host, DaggerfallTalkWindow talk)
        {
            PersonalTradeNativeProbe.CaptureTradeContext(host, talk, "May I trade with you?");
            var method = typeof(PeopleOfTheIliacBayMod).GetMethod("TryOpenConversationPersonalTradeWindow", Private);
            Require(method != null, "Static conversation commerce handoff is unavailable.");
            var args = new object[] { talk, null };
            bool opened=(bool)method.Invoke(host,args);
            if(!opened)LogAdmissionState(host,talk);
            Require(opened, "Actual static Talk trade handoff refused: " + args[1]);
            var window = DaggerfallUI.UIManager.TopWindow as PersonalTradeWindow;
            Require(window != null && !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow),
                "Static Talk did not close into the owned native personal-trade window.");
            return window;
        }

        private static void LogAdmissionState(PeopleOfTheIliacBayMod host,DaggerfallTalkWindow talk)
        {
            var type=typeof(PeopleOfTheIliacBayMod);var game=GameManager.Instance;
            var streets=(StreetPopulation)type.GetField("streets",Private).GetValue(host);
            var store=GetStore(streets);var npc=game.TalkManager.StaticNPC;
            var party=new object[]{talk,null,null,NpcArchetype.Citizen};
            bool resolved=(bool)type.GetMethod("TryResolveStaticPersonalTradeParty",Private).Invoke(host,party);
            var person=party[2] as NativePerson;
            var role=new object[]{npc,NpcArchetype.Citizen};
            bool roleFound=(bool)type.GetMethod("StaticPersonalTradeRole",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,role);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_TRADE_ADMISSION: resolved="+resolved+
                "; sessionOpen="+ConversationOpen(host)+"; memoryWindow="+ReferenceEquals(type.GetField("memoryWindow",Private).GetValue(host),talk)+
                "; topWindow="+ReferenceEquals(DaggerfallUI.UIManager.TopWindow,talk)+"; saveFault="+type.GetField("saveFault",Private).GetValue(host)+
                "; person="+(person==null?"null":person.Id)+"; nativeMatch="+NativeStaticNpcInventoryOwner.Matches(npc,person)+
                "; role="+roleFound+":"+role[1]+"; streetsReady="+(streets && streets.Ready)+
                "; serviceBlocked="+(streets && streets.KeeperServiceBlocked(npc))+
                "; ledger="+(person!=null && store.HasNpcInventory(person.Id)));
            if(person==null || !store.HasNpcInventory(person.Id))return;
            NativeStaticNpcInventoryOwner owner;string reason;
            bool bound=NativeStaticNpcInventoryOwner.TryOpen(store,npc,person,(NpcArchetype)role[1],out owner,out reason);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_TRADE_BINDING: opened="+bound+"; reason="+reason);
            if(!bound)return;
            var saved=store.ReadNpcInventory(person.Id);var items=NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items);
            bool willing=(bool)type.GetMethod("StaticPersonalTradeWilling",Private).Invoke(host,
                new object[]{store,npc,person,role[1],owner,game.PlayerEntity});
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_TRADE_AUTHORITY: willing="+willing+
                "; savedRevision="+saved.Revision+"; bindingRevision="+owner.Binding.Revision+
                "; matches="+NpcInventoryReadAuthority.Matches(saved,person.Id,owner.Binding.Revision,items));
            foreach(var item in items)
            {
                var original=saved.Items.Find(row=>row.Uid==item.Uid);
                if(original!=null && original.NativePayload==item.NativePayload)continue;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STATIC_TRADE_PAYLOAD: saved="+(original==null?"missing":original.NativePayload)+"; live="+item.NativePayload);
                break;
            }
        }

        private static PersonalTradeWindowSession Session(PersonalTradeWindow window)
        { return (PersonalTradeWindowSession)Field(window, "session"); }

        private static PersonalTradeWindowQuote Quote(PersonalTradeWindow window)
        { return (PersonalTradeWindowQuote)Field(window, "quote"); }

        private static object Field(PersonalTradeWindow window, string name)
        { return typeof(PersonalTradeWindow).GetField(name, Private).GetValue(window); }

        private static void Stage(PersonalTradeWindow window, PersonalTradeSide side, DaggerfallUnityItem item, int quantity)
        {
            var method = typeof(PersonalTradeWindow).GetMethod("SetQuantity", Private, null,
                new[] { typeof(PersonalTradeSide), typeof(DaggerfallUnityItem), typeof(int) }, null);
            Require(method != null && (bool)method.Invoke(window, new object[] { side, item, quantity }),
                "Native static personal item cannot be staged.");
            Call(window, "RefreshView");
        }

        private static void SetAcceptedOffer(PersonalTradeWindow window, int offer)
        {
            typeof(PersonalTradeWindow).GetField("playerOfferGold", Private).SetValue(window, offer);
            typeof(PersonalTradeWindow).GetField("hasPlayerOffer", Private).SetValue(window, true);
        }

        private static void Call(PersonalTradeWindow window, string name)
        {
            var method = typeof(PersonalTradeWindow).GetMethod(name, Private);
            Require(method != null, "Native personal-trade window method is unavailable: " + name);
            method.Invoke(window, null);
        }

        private static InventorySnapshot Snapshot(ItemCollection items)
        {
            var result = new InventorySnapshot { Items = items };
            if (items == null) return result;
            for (int index = 0; index < items.Count; index++)
            {
                var item = items.GetItem(index);
                result.Uids.Add(item.UID);
                result.Counts.Add(item.stackCount);
            }
            return result;
        }

        private static List<InventorySnapshot> SnapshotShelves(DaggerfallInterior interior)
        {
            var snapshots = new List<InventorySnapshot>();
            foreach (var shelf in interior.GetComponentsInChildren<DaggerfallLoot>(true))
                if (shelf.ContainerType == LootContainerTypes.ShopShelves) snapshots.Add(Snapshot(shelf.Items));
            return snapshots;
        }

        private static void RequireShelvesUnchanged(List<InventorySnapshot> shelves)
        {
            foreach (var shelf in shelves) RequireSameSnapshot(shelf, shelf.Items, "Static personal trade touched native shelf stock.");
        }

        private static void RequireSameSnapshot(InventorySnapshot before, ItemCollection after, string failure)
        {
            if (before == null || after == null || before.Uids.Count != after.Count) throw new InvalidOperationException(failure);
            for (int index = 0; index < before.Uids.Count; index++)
            {
                var item = after.GetItem(index);
                if (item == null || item.UID != before.Uids[index] || item.stackCount != before.Counts[index])
                    throw new InvalidOperationException(failure);
            }
        }

        private static HashSet<ulong> Uids(ItemCollection items)
        {
            var result = new HashSet<ulong>();
            for (int index = 0; index < items.Count; index++) result.Add(items.GetItem(index).UID);
            return result;
        }

        private static DaggerfallUnityItem FindNewItem(ItemCollection items, HashSet<ulong> before, ItemGroups group, int template)
        {
            for (int index = 0; index < items.Count; index++)
            {
                var item = items.GetItem(index);
                if (!before.Contains(item.UID) && item.ItemGroup == group && item.TemplateIndex == template && item.stackCount == 1)
                    return item;
            }
            return null;
        }

        private static WorldStateStore GetStore(StreetPopulation streets)
        { return typeof(StreetPopulation).GetField("worldStore", Private).GetValue(streets) as WorldStateStore; }

        private static bool ConversationOpen(PeopleOfTheIliacBayMod host)
        { return (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", Private).GetValue(host); }

        private static bool ConversationBusy(PeopleOfTheIliacBayMod host)
        { return (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy", Private).GetValue(host); }

        private static string ConversationPersonId(PeopleOfTheIliacBayMod host)
        { return (string)typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId", Private).GetValue(host); }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
