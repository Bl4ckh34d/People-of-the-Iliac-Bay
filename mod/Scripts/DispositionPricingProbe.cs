using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Live acceptance probe for displayed and charged native inn-room prices.</summary>
    public static class DispositionPricingProbe
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        public static IEnumerator Run()
        {
            var host = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            if (!host) throw new InvalidOperationException("People of the Iliac Bay mod is unavailable.");
            var game = GameManager.Instance;
            if (game == null || !game.IsReady || !game.PlayerGPS.HasCurrentLocation)
                throw new InvalidOperationException("Probe requires a ready player inside a town.");

            // Keep one before-state and restore it only after all NPC/window references are done.
            var before = (WorldState)host.GetSaveData();
            var player = game.PlayerEntity;
            int originalGold = player.GoldPieces;
            var originalReceipts = new List<RoomRental_v1>(player.RentedRooms);
            short[] originalGroupReputation = (short[])player.SGroupReputations.Clone();
            int map = game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            string scene = null;
            bool sceneWasPermanent = false;
            try
            {
                DaggerfallUI.Instance.PopToHUD();
                if (game.PlayerEnterExit.IsPlayerInside)
                {
                    game.PlayerEnterExit.TransitionExterior();
                    yield return WaitForExterior(game);
                }

                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                if (directory == null) throw new InvalidOperationException("Current town has no loaded building directory.");
                StaticDoor entry;
                Transform owner;
                int buildingKey;
                if (!SelectTavern(directory, out owner, out entry, out buildingKey))
                    throw new InvalidOperationException("Current town has no tavern door.");

                TownSmokeHarness.EnterInterior(owner, entry);
                float transitionDeadline = Time.realtimeSinceStartup + 45f;
                while ((!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey != buildingKey) &&
                    Time.realtimeSinceStartup < transitionDeadline)
                    yield return null;
                if (!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey != buildingKey)
                    throw new InvalidOperationException("Native transition into the selected tavern timed out.");

                var interior = game.PlayerEnterExit.Interior;
                scene = DaggerfallInterior.GetSceneName(map, buildingKey);
                sceneWasPermanent = SaveLoadManager.StateManager.ContainsPermanentScene(scene);
                StaticNPC keeper = null;
                WorldStateStore store = null;
                float registrationDeadline = Time.realtimeSinceStartup + 30f;
                while (!keeper && Time.realtimeSinceStartup < registrationDeadline)
                {
                    store = GetStore(host);
                    if (store != null) keeper = SelectKeeper(interior, map, buildingKey, store, player);
                    if (!keeper) yield return null;
                }
                if (!keeper) throw new InvalidOperationException("Selected tavern has no matching loaded registered keeper.");

                string personId = NativePersonId(keeper);
                var faction = GetMerchantFaction(player, keeper.Data.factionID);
                if (faction.sgroup < 0 || faction.sgroup >= player.SGroupReputations.Length)
                    throw new InvalidOperationException("Keeper merchant social group is outside the player's reputation array.");

                var order = game.GuildManager.GetGuild(FactionFile.GuildGroups.KnightlyOrder);
                if (order != null && order.FreeTavernRooms())
                    throw new InvalidOperationException("Probe requires a paid inn-room quote; Knightly Order rooms are free.");

                player.RentedRooms.RemoveAll(r => r.mapID == map && r.buildingKey == buildingKey);
                player.GoldPieces = 5000;
                Dictionary<int, string> markers;
                var allBeds = TavernBedSurvey.Read(interior, out markers);
                if (markers.Count == 0) throw new InvalidOperationException("Tavern has no native rental bed.");
                var stateField = typeof(WorldStateStore).GetField("state", InstancePrivate);
                var state = stateField == null ? null : stateField.GetValue(store) as WorldState;
                if (state == null) throw new InvalidOperationException("Unable to stage temporary conversation baseline.");
                // The outer save snapshot is restored in finally. Start the test inn
                // with clean bookings so native keeper/occupant reconciliation can
                // claim genuine beds instead of competing with probe-created holds.
                state.Lodgings.RemoveAll(t => t.SettlementId == map && t.BuildingKey == buildingKey);
                store.RegisterLodgingBeds(map, buildingKey, allBeds);
                int nativePrice = FormulaHelper.CalculateTradePrice(FormulaHelper.CalculateRoomCost(1),
                    game.PlayerEnterExit.BuildingDiscoveryData.quality, false);
                if (nativePrice <= 0) throw new InvalidOperationException("Native room price is not positive; discount charge cannot be proven.");

                foreach (int expectedDisposition in new[] { -49, 0, 100 })
                {
                    CaptureSyntheticNativeBaseline(player, game, faction, personId, expectedDisposition, store, state);
                    int freshDisposition;
                    if (!host.TryGetFreshConversationDisposition(personId, out freshDisposition) ||
                        freshDisposition != expectedDisposition)
                        throw new InvalidOperationException("Fresh conversation disposition failed the " + expectedDisposition + " fixture.");

                    int expectedPrice = DispositionPricing.ApplyPurchaseDispositionPrice(nativePrice, freshDisposition);
                    var window = new DaggerfallTavernWindow(DaggerfallUI.UIManager, keeper);
                    var daysField = typeof(DaggerfallTavernWindow).GetField("daysToRent", InstancePrivate);
                    if (daysField == null || (int)daysField.GetValue(window) != 0)
                        throw new InvalidOperationException("Fresh native tavern quote did not begin with zero days.");
                    DaggerfallUI.UIManager.PushWindow(window);
                    yield return null;
                    typeof(DaggerfallTavernWindow).GetMethod("InputMessageBox_OnGotUserInput", InstancePrivate)
                        .Invoke(window, new object[] { null, "1" });
                    yield return null;

                    int quoted = (int)typeof(DaggerfallTavernWindow).GetField("tradePrice", InstancePrivate).GetValue(window);
                    var messageBox = DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
                    string renderedText = messageBox == null ? "" : RenderedMessageText(messageBox);
                    if (quoted != expectedPrice || window.GetMacroDataSource().Amount() != expectedPrice.ToString(CultureInfo.InvariantCulture) ||
                        !NativeProbeText.ContainsNumber(renderedText, expectedPrice))
                        throw new InvalidOperationException("Inn quote did not render the exact adjusted amount. tradePrice=" + quoted +
                            "; amountMacro=" + window.GetMacroDataSource().Amount() + "; rendered=" + renderedText);

                    int goldBeforePurchase = player.GetGoldAmount();
                    typeof(DaggerfallTavernWindow).GetMethod("ConfirmRenting_OnButtonClick", InstancePrivate)
                        .Invoke(window, new object[] { null, DaggerfallMessageBox.MessageBoxButtons.Yes });
                    int goldAfterPurchase = player.GetGoldAmount();
                    int charged = goldBeforePurchase - goldAfterPurchase;
                    var receipt = player.GetRentedRoom(map, buildingKey);
                    if (charged != expectedPrice || receipt == null)
                    {
                        var ledger = store.Snapshot().Lodgings.Find(t => t.SettlementId == map && t.BuildingKey == buildingKey);
                        int bookingCount = ledger == null ? -1 : ledger.Bookings.Count;
                        throw new InvalidOperationException("Inn confirmation mismatch. disposition=" + freshDisposition +
                            "; nativeBase=" + nativePrice + "; expected=" + expectedPrice + "; nativeTradePrice=" + quoted +
                            "; chargedFromTotalGold=" + charged + "; goldBefore=" + goldBeforePurchase +
                            "; goldAfter=" + goldAfterPurchase + "; receipt=" + (receipt != null) +
                            "; receiptExpiry=" + (receipt == null ? "none" : receipt.expiryTime.ToString(CultureInfo.InvariantCulture)) +
                            "; ledgerBookingCount=" + bookingCount + "; topWindow=" +
                            (DaggerfallUI.UIManager.TopWindow == null ? "none" : DaggerfallUI.UIManager.TopWindow.GetType().Name) +
                            "; renderedQuote=" + renderedText);
                    }

                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_ROOM_PRICE_CASE_PASS: keeper=" + personId +
                        "; disposition=" + freshDisposition + "; native=" + nativePrice +
                        "; quoted=" + quoted + "; charged=" + charged + ".");
                    DaggerfallUI.Instance.PopToHUD();
                    player.RentedRooms.RemoveAll(r => r.mapID == map && r.buildingKey == buildingKey);
                    // Each price case is a fresh rental. Remove its mod reservation
                    // together with the native receipt, without touching NPC claims.
                    state.Lodgings.Find(t => t.SettlementId == map && t.BuildingKey == buildingKey)
                        .Bookings.RemoveAll(b => b.Player);
                    player.GoldPieces = 5000;
                }

                foreach (int deniedDisposition in new[] { -50, -100 })
                {
                    CaptureSyntheticNativeBaseline(player, game, faction, personId, deniedDisposition, store, state);
                    int freshDisposition;
                    if (!host.TryGetFreshConversationDisposition(personId, out freshDisposition) ||
                        freshDisposition != deniedDisposition)
                        throw new InvalidOperationException("Fresh conversation disposition failed the refusal fixture " + deniedDisposition + ".");

                    var window = new DaggerfallTavernWindow(DaggerfallUI.UIManager, keeper);
                    DaggerfallUI.UIManager.PushWindow(window);
                    yield return null;
                    typeof(DaggerfallTavernWindow).GetMethod("InputMessageBox_OnGotUserInput", InstancePrivate)
                        .Invoke(window, new object[] { null, "1" });
                    yield return null;
                    int goldBeforeRefusal = player.GetGoldAmount();
                    typeof(DaggerfallTavernWindow).GetMethod("ConfirmRenting_OnButtonClick", InstancePrivate)
                        .Invoke(window, new object[] { null, DaggerfallMessageBox.MessageBoxButtons.Yes });
                    int refusedCharge = goldBeforeRefusal - player.GetGoldAmount();
                    var deniedReceipt = player.GetRentedRoom(map, buildingKey);
                    var denialBox = DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
                    string denialText = denialBox == null ? "" : RenderedMessageText(denialBox);
                    if (refusedCharge != 0 || deniedReceipt != null ||
                        !denialText.Contains("The keeper refuses to rent you a room."))
                        throw new InvalidOperationException("Low-disposition room service was not refused. disposition=" +
                            freshDisposition + "; charge=" + refusedCharge + "; receipt=" + (deniedReceipt != null) +
                            "; topWindow=" + (DaggerfallUI.UIManager.TopWindow == null ? "none" : DaggerfallUI.UIManager.TopWindow.GetType().Name) +
                            "; rendered=" + denialText);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_ROOM_REFUSAL_PASS: disposition=" + freshDisposition +
                        "; charge=0; receipt=none; refusalText=visible.");
                    DaggerfallUI.Instance.PopToHUD();
                }

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_ROOM_PRICE_PASS: quote and payment match at -49, 0, and 100; paid room refused at -50 and -100.");
            }
            finally
            {
                player.SGroupReputations = originalGroupReputation;
                player.GoldPieces = originalGold;
                player.RentedRooms.Clear();
                player.RentedRooms.AddRange(originalReceipts);
                if (scene != null && !sceneWasPermanent) SaveLoadManager.StateManager.RemovePermanentScene(scene);
                host.RestoreSaveData(before);
                DaggerfallUI.Instance.PopToHUD();
            }
        }

        private static IEnumerator WaitForExterior(GameManager game)
        {
            float deadline = Time.realtimeSinceStartup + 45f;
            while ((game.PlayerEnterExit.IsPlayerInside || game.StreamingWorld.IsRepositioningPlayer ||
                !game.StreamingWorld.IsReady) && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (game.PlayerEnterExit.IsPlayerInside || game.StreamingWorld.IsRepositioningPlayer || !game.StreamingWorld.IsReady)
                throw new InvalidOperationException("Transition to current town exterior timed out.");
        }

        private static bool SelectTavern(BuildingDirectory directory, out Transform owner,
            out StaticDoor entry, out int buildingKey)
        {
            owner = null; entry = default(StaticDoor); buildingKey = -1;
            foreach (var summary in directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern))
            {
                foreach (var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var door in doors.Doors)
                {
                    if (door.doorType != DoorTypes.Building || door.buildingKey != summary.buildingKey) continue;
                    owner = doors.transform; entry = door; buildingKey = summary.buildingKey;
                    return true;
                }
            }
            return false;
        }

        private static StaticNPC SelectKeeper(DaggerfallInterior interior, int map, int buildingKey,
            WorldStateStore store, PlayerEntity player)
        {
            foreach (var npc in interior.GetComponentsInChildren<StaticNPC>(true))
            {
                if (!npc || !npc.gameObject.activeInHierarchy || npc.Data.context != StaticNPC.Context.Building ||
                    npc.Data.mapID != map || npc.Data.buildingKey != buildingKey || npc.IsChildNPC ||
                    npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>()) continue;
                if (!IsMerchantFaction(player, npc.Data.factionID)) continue;
                string id = NativePersonId(npc);
                if (store.NativePeopleFor(map).Exists(p => p.Id == id && p.WorkKey == buildingKey &&
                    p.NativeHash == npc.Data.hash && p.FactionId == npc.Data.factionID && p.IsAlive)) return npc;
            }
            return null;
        }

        private static void CaptureSyntheticNativeBaseline(PlayerEntity player, GameManager game,
            FactionFile.FactionData faction, string personId, int expected, WorldStateStore store, WorldState state)
        {
            int group = faction.sgroup;
            int otherModifiers = faction.rep + player.BiographyReactionMod + player.GetReactionMod((FactionFile.SocialGroups)group);
            player.SGroupReputations[group] = checked((short)(expected - otherModifiers));
            int baseline = game.TalkManager.GetReactionToPlayer(faction);
            if (baseline != expected)
                throw new InvalidOperationException("Temporary player reputation did not produce exact native reaction " + expected + ".");

            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            store.SetConversationBaseline(personId, baseline, now, faction.id);
            var attitude = state.ConversationAttitudes.Find(a => a.PersonId == personId);
            if (attitude == null) throw new InvalidOperationException("Conversation baseline was not captured into player state.");
            attitude.Offset = 0;
        }

        private static FactionFile.FactionData GetMerchantFaction(PlayerEntity player, int factionId)
        {
            FactionFile.FactionData faction;
            if (!player.FactionData.GetFactionData(factionId, out faction) ||
                faction.sgroup != (int)FactionFile.SocialGroups.Merchants)
                throw new InvalidOperationException("Keeper no longer belongs to the merchant social group.");
            return faction;
        }

        private static bool IsMerchantFaction(PlayerEntity player, int factionId)
        {
            FactionFile.FactionData faction;
            return player.FactionData.GetFactionData(factionId, out faction) &&
                faction.sgroup == (int)FactionFile.SocialGroups.Merchants;
        }

        private static string NativePersonId(StaticNPC keeper)
        {
            return string.Format(CultureInfo.InvariantCulture, "npc:{0}:{1}:{2}",
                keeper.Data.mapID, keeper.Data.buildingKey, keeper.Data.hash);
        }

        private static WorldStateStore GetStore(PeopleOfTheIliacBayMod host)
        {
            return (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store", InstancePrivate).GetValue(host);
        }

        private static string RenderedMessageText(DaggerfallMessageBox messageBox)
        {
            var labelField = typeof(DaggerfallMessageBox).GetField("label", InstancePrivate);
            var label = labelField == null ? null : labelField.GetValue(messageBox);
            if (label == null) return "";
            var linesField = label.GetType().GetField("labels", InstancePrivate);
            var lines = linesField == null ? null : linesField.GetValue(label) as System.Collections.IEnumerable;
            if (lines == null) return "";
            var text = new StringBuilder();
            foreach (object line in lines)
            {
                var property = line.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
                if (property != null) text.Append(property.GetValue(line, null));
            }
            return text.ToString();
        }
    }
}
