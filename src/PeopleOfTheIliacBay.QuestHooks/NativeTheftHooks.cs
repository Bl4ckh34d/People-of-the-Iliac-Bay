using System;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using HarmonyLib;

namespace PeopleOfTheIliacBay.QuestHooks
{
    public sealed class NativeTheftHooks : IDisposable
    {
        private const string Owner = "local.peopleoftheiliacbay.theft-witnesses";
        private static NativeTheftHooks active;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<bool?> witnesses;
        private readonly Func<DaggerfallInventoryWindow, Action<string, int>> captureTransfer;
        private static readonly MethodInfo stack = typeof(ItemCollection).GetMethod("FindExistingStack", BindingFlags.Instance | BindingFlags.NonPublic);
        private sealed class TransferObservation
        {
            internal ItemCollection From, To;
            internal DaggerfallUnityItem Item, Destination;
            internal int Quantity, DestinationCount, GoldBefore;
            internal bool Gold, Published;
            internal string Name;
            internal Action<string, int> Remember;
        }
        public NativeTheftHooks(Func<bool?> witnesses)
            : this(witnesses, null) { }
        public NativeTheftHooks(Func<bool?> witnesses, Func<DaggerfallInventoryWindow, Action<string, int>> captureTransfer)
        {
            if (active != null) throw new InvalidOperationException("Theft hooks already installed.");
            this.witnesses = witnesses ?? throw new ArgumentNullException(nameof(witnesses));
            this.captureTransfer = captureTransfer;
            active = this;
            try
            {
                harmony.Patch(typeof(FormulaHelper).GetMethod("CalculateShopliftingChance"), prefix:
                    new HarmonyMethod(typeof(NativeTheftHooks).GetMethod("BeforeChance", BindingFlags.NonPublic | BindingFlags.Static)));
                if (captureTransfer != null)
                {
                    var transfer = typeof(DaggerfallInventoryWindow).GetMethod("DoTransferItem", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(DaggerfallUnityItem), typeof(ItemCollection), typeof(ItemCollection), typeof(bool) }, null);
                    if (transfer == null || stack == null) throw new MissingMethodException("Native inventory transfer API changed.");
                    harmony.Patch(transfer, prefix: new HarmonyMethod(typeof(NativeTheftHooks), nameof(BeforeTransfer)),
                        postfix: new HarmonyMethod(typeof(NativeTheftHooks), nameof(AfterTransfer)));
                }
            }
            catch { Dispose(); throw; }
        }
        // Capture at the committed transfer boundary, not inventory close. Split-stack
        // prompts may delay a transfer; cancelled prompts never reach this method.
        private static void BeforeTransfer(DaggerfallInventoryWindow __instance, DaggerfallUnityItem item,
            ItemCollection from, ItemCollection to, out TransferObservation __state)
        {
            __state = null;
            try
            {
                var self = active; var game = GameManager.Instance;
                var loot = __instance == null ? null : __instance.LootTarget;
                if (self == null || self.captureTransfer == null || loot == null || !loot.houseOwned || loot.playerOwned ||
                    item == null || item.UID == 0 || item.stackCount < 1 || from == null || to == null ||
                    !ReferenceEquals(from, loot.Items) || !ReferenceEquals(from.GetItem(item.UID), item) || game == null || game.PlayerEntity == null ||
                    !ReferenceEquals(to, game.PlayerEntity.Items)) return;
                var remember = self.captureTransfer(__instance);
                if (remember == null) return;
                var destination = (DaggerfallUnityItem)stack.Invoke(to, new object[] { item });
                __state = new TransferObservation { From = from, To = to, Item = item, Destination = destination,
                    Quantity = item.stackCount, DestinationCount = destination == null ? 0 : destination.stackCount,
                    Gold = item.IsOfTemplate(ItemGroups.Currency, (int)Currency.Gold_pieces),
                    GoldBefore = game.PlayerEntity.GoldPieces, Name = NativeItemName(item), Remember = remember };
            }
            catch (Exception error) { UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_THEFT_CAPTURE_FAILED: " + error.Message); }
        }
        private static void AfterTransfer(TransferObservation __state)
        {
            if (__state == null || __state.Published) return;
            __state.Published = true;
            try
            {
                var game = GameManager.Instance;
                if (active == null || game == null || game.PlayerEntity == null ||
                    !ReferenceEquals(game.PlayerEntity.Items, __state.To) || __state.From.Contains(__state.Item.UID)) return;
                if (__state.Gold)
                {
                    if ((long)game.PlayerEntity.GoldPieces - __state.GoldBefore != __state.Quantity) return;
                }
                else
                {
                    var expected = __state.Destination ?? __state.Item;
                    var actual = __state.To.GetItem(expected.UID);
                    if (!ReferenceEquals(actual, expected) || actual.stackCount != __state.DestinationCount + __state.Quantity) return;
                }
                __state.Remember(__state.Name, __state.Quantity);
            }
            catch (Exception error) { UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_THEFT_MEMORY_FAILED: " + error.Message); }
        }
        private static bool BeforeChance(ref int __result)
        {
            // Unknown coverage or any possible witness retains the native skill check.
            if (active == null || active.witnesses() != false) return true;
            __result = 0; return false;
        }
        private static string NativeItemName(DaggerfallUnityItem item)
        { try { return item.LongName; } catch { return null; } }
        public void Dispose()
        { if (active != this) return; harmony.UnpatchAll(Owner); active = null; }
    }
}
