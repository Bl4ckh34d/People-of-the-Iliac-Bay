using System;
using DaggerfallConnect.Arena2;
using HarmonyLib;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;

namespace PeopleOfTheIliacBay.QuestHooks
{
    /// <summary>Applies personal disposition to paid guild services with a captured native service owner.</summary>
    public sealed class NativeDispositionServiceHooks : IDisposable
    {
        private const string Owner = "peopleoftheiliacbay.disposition-services";
        private static NativeDispositionServiceHooks active;
        private static StaticNPC currentServiceOwner;
        private static DaggerfallQuestPopupWindow currentDaedraServiceWindow;
        private static GuildServices currentService;
        private static StaticNPC currentCureOwner;
        private static DaggerfallMessageBox activeTrainingOffer;
        private static DaggerfallListPickerWindow activeTrainingPicker;
        private static bool trainingCallbackActive;
        private readonly Harmony harmony = new Harmony(Owner);
        private readonly Func<StaticNPC, bool> canServe;
        private readonly Func<StaticNPC, int, int> adjustServicePrice;

        public NativeDispositionServiceHooks(Func<StaticNPC, bool> canServe,
            Func<StaticNPC, int, int> adjustServicePrice)
        {
            this.canServe = canServe ?? throw new ArgumentNullException(nameof(canServe));
            this.adjustServicePrice = adjustServicePrice ?? throw new ArgumentNullException(nameof(adjustServicePrice));
            if (active != null) throw new InvalidOperationException("Disposition service hooks already installed.");
            active = this;
            try
            {
                Patch(typeof(DaggerfallGuildServicePopupWindow), "DoGuildService", nameof(BeforeGuildService));
                Patch(typeof(DaggerfallQuestPopupWindow), "DaedraSummoningService", nameof(BeforeDaedraSummoningService));
                Patch(typeof(FormulaHelper), nameof(FormulaHelper.CalculateDaedraSummoningCost), nameof(AfterDaedraSummoningCost), postfix: true,
                    parameters: new[] { typeof(int) });
                Patch(typeof(DaggerfallQuestPopupWindow), "ConfirmSummon_OnButtonClick", nameof(BeforeDaedraSummoningConfirm));
                Patch(typeof(Guild), "GetTrainingPrice", nameof(AfterTrainingPrice), postfix: true);
                Patch(typeof(DaggerfallGuildServiceTraining), "TrainingService", nameof(BeforeTrainingQuote));
                Patch(typeof(DaggerfallGuildServiceTraining), "TrainingService", nameof(AfterTrainingQuote), finalizer: true);
                Patch(typeof(DaggerfallGuildServiceTraining), "TrainingService", nameof(AfterTrainingOffer), postfix: true);
                Patch(typeof(DaggerfallGuildServiceTraining), "ConfirmTraining_OnButtonClick", nameof(BeforeTrainingConfirm));
                Patch(typeof(DaggerfallGuildServiceTraining), "ConfirmTraining_OnButtonClick", nameof(AfterTrainingConfirm), postfix: true);
                Patch(typeof(DaggerfallGuildServiceTraining), "TrainingSkill_OnItemPicked", nameof(BeforeTrainingPick));
                Patch(typeof(DaggerfallGuildServiceTraining), "TrainingSkill_OnItemPicked", nameof(AfterTrainingPick), postfix: true);
                Patch(typeof(DaggerfallMessageBox), "OnPop", nameof(AfterTrainingOfferPop), postfix: true);
                Patch(typeof(DaggerfallListPickerWindow), "OnPop", nameof(AfterTrainingPickerPop), postfix: true);
                Patch(typeof(DaggerfallGuildServiceCureDisease), "CureDiseaseService", nameof(BeforeCureQuote), outState: true);
                Patch(typeof(DaggerfallGuildServiceCureDisease), "CureDiseaseService", nameof(AfterCureQuote), finalizer: true, outState: true);
                Patch(typeof(DaggerfallGuildServiceCureDisease), "ConfirmCuring_OnButtonClick", nameof(BeforeCureConfirm));
                Patch(typeof(DaggerfallGuildServiceCureDisease), "ConfirmCuring_OnButtonClick", nameof(AfterCureConfirm), postfix: true);
                Patch(typeof(FormulaHelper), nameof(FormulaHelper.CalculateTradePrice), nameof(AfterCureTradePrice), postfix: true,
                    parameters: new[] { typeof(int), typeof(int), typeof(bool) });
                Patch(typeof(DaggerfallSpellBookWindow), "GetTradePrice", nameof(AfterSpellbookPrice), postfix: true);
                Patch(typeof(DaggerfallSpellBookWindow), "ConfirmTrade_OnButtonClick", nameof(BeforeSpellbookConfirm));
                Patch(typeof(DaggerfallItemMakerWindow), "GetTotalGoldCost", nameof(AfterItemMakerPrice), postfix: true);
                Patch(typeof(DaggerfallItemMakerWindow), "EnchantButton_OnMouseClick", nameof(BeforeItemMakerConfirm));
                Patch(typeof(DaggerfallItemMakerWindow), "OnPop", nameof(AfterMakerPop), postfix: true);
                Patch(typeof(FormulaHelper), nameof(FormulaHelper.CalculateTotalEffectCosts), nameof(AfterSpellMakerPrice), postfix: true,
                    parameters: new[] { typeof(EffectEntry[]), typeof(TargetTypes), typeof(DaggerfallWorkshop.Game.Entity.DaggerfallEntity), typeof(bool) });
                Patch(typeof(DaggerfallSpellMakerWindow), "BuyButton_OnMouseClick", nameof(BeforeSpellMakerConfirm));
                Patch(typeof(DaggerfallSpellMakerWindow), "OnPop", nameof(AfterMakerPop), postfix: true);
            }
            catch
            {
                active = null;
                currentServiceOwner = null;
                currentCureOwner = null;
                harmony.UnpatchAll(Owner);
                throw;
            }
        }

        private void Patch(Type type, string originalName, string hookName, bool prefix = true,
            bool postfix = false, bool finalizer = false, bool outState = false, Type[] parameters = null)
        {
            var original = parameters == null
                ? AccessTools.Method(type, originalName)
                : AccessTools.Method(type, originalName, parameters);
            if (original == null) throw new MissingMethodException(type.FullName, originalName);
            // Mono can return an inherited MethodInfo with the requested reflected
            // type. Harmony requires the actual declaration for inherited methods.
            if (original.ReflectedType != original.DeclaringType)
                original = AccessTools.DeclaredMethod(original.DeclaringType, original.Name,
                    Array.ConvertAll(original.GetParameters(), parameter => parameter.ParameterType));
            var method = new HarmonyMethod(typeof(NativeDispositionServiceHooks).GetMethod(hookName, AccessTools.all));
            if (prefix && !postfix && !finalizer) harmony.Patch(original, prefix: method);
            else if (postfix) harmony.Patch(original, postfix: method);
            else if (finalizer) harmony.Patch(original, finalizer: method);
        }

        private static bool BeforeGuildService(DaggerfallGuildServicePopupWindow __instance,
            StaticNPC ___serviceNPC, GuildServices ___service)
        {
            var self = active;
            if (self == null || __instance == null) return true;
            currentServiceOwner = null;
            currentDaedraServiceWindow = null;
            currentService = ___service;
            currentCureOwner = null;
            if (!___serviceNPC || !IsDispositionService(___service)) return true;
            try
            {
                if (!self.canServe(___serviceNPC))
                {
                    DaggerfallUI.MessageBox("The guild refuses to provide this service.");
                    return false;
                }
                currentServiceOwner = ___serviceNPC;
                return true;
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_SERVICE_GATE_FAILED: " + error);
                currentServiceOwner = null;
                return true;
            }
        }

        private static bool IsDispositionService(GuildServices service)
        {
            switch (service.ToString())
            {
                case "Training":
                case "CureDisease":
                case "BuySpells":
                case "BuySpellsMages":
                case "MakeSpells":
                case "MakeMagicItems":
                case "DaedraSummoning":
                    return true;
                default:
                    return false;
            }
        }

        private static void AfterDaedraSummoningCost(int npcRep, ref int __result)
        {
            var self = active;
            if (self == null || __result <= 0 || !currentServiceOwner ||
                currentService != GuildServices.DaedraSummoning || !IsCurrentDaedraServiceActive()) return;
            Adjust(self, currentServiceOwner, ref __result, "DAEDRA_SUMMONING");
        }

        private static bool BeforeDaedraSummoningService(DaggerfallQuestPopupWindow __instance, int npcFactionId)
        {
            var self = active;
            if (self == null || __instance == null) return true;

            StaticNPC owner = null;
            if (__instance is DaggerfallGuildServicePopupWindow guild)
            {
                var service = (GuildServices)AccessTools.Field(typeof(DaggerfallGuildServicePopupWindow), "service").GetValue(guild);
                if (service != GuildServices.DaedraSummoning) return true;
                owner = (StaticNPC)AccessTools.Field(typeof(DaggerfallGuildServicePopupWindow), "serviceNPC").GetValue(guild);
            }
            else if (__instance is DaggerfallWitchesCovenPopupWindow coven)
            {
                owner = (StaticNPC)AccessTools.Field(typeof(DaggerfallWitchesCovenPopupWindow), "witchNPC").GetValue(coven);
            }
            else return true;

            if (!owner || owner.Data.factionID != npcFactionId) return true;
            currentServiceOwner = null;
            currentDaedraServiceWindow = null;
            if (!CanContinue(self, owner, "DAEDRA_SUMMONING"))
            {
                DaggerfallUI.MessageBox("The guild refuses to summon for you.");
                return false;
            }
            currentServiceOwner = owner;
            currentService = GuildServices.DaedraSummoning;
            currentDaedraServiceWindow = __instance;
            return true;
        }

        private static bool BeforeDaedraSummoningConfirm(DaggerfallMessageBox sender,
            DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            var self = active;
            if (self == null || messageBoxButton != DaggerfallMessageBox.MessageBoxButtons.Yes ||
                !currentServiceOwner || currentService != GuildServices.DaedraSummoning || !IsCurrentDaedraServiceActive()) return true;
            if (CanContinue(self, currentServiceOwner, "DAEDRA_SUMMONING")) return true;
            if (sender != null) sender.CloseWindow();
            DaggerfallUI.MessageBox("The guild refuses to summon for you.");
            return false;
        }

        private static bool IsCurrentDaedraServiceActive()
        {
            if (currentDaedraServiceWindow == null || DaggerfallUI.UIManager == null) return false;
            IUserInterfaceWindow window = DaggerfallUI.UIManager.TopWindow;
            for (int depth = 0; window != null && depth < 8; depth++)
            {
                if (ReferenceEquals(window, currentDaedraServiceWindow))
                {
                    if (currentDaedraServiceWindow is DaggerfallGuildServicePopupWindow guild)
                    {
                        var owner = (StaticNPC)AccessTools.Field(typeof(DaggerfallGuildServicePopupWindow), "serviceNPC").GetValue(guild);
                        var service = (GuildServices)AccessTools.Field(typeof(DaggerfallGuildServicePopupWindow), "service").GetValue(guild);
                        return owner == currentServiceOwner && service == GuildServices.DaedraSummoning;
                    }
                    if (currentDaedraServiceWindow is DaggerfallWitchesCovenPopupWindow coven)
                    {
                        var owner = (StaticNPC)AccessTools.Field(typeof(DaggerfallWitchesCovenPopupWindow), "witchNPC").GetValue(coven);
                        return owner == currentServiceOwner;
                    }
                    return false;
                }
                var popup = window as DaggerfallPopupWindow;
                if (popup == null) return false;
                window = popup.PreviousWindow;
            }
            return false;
        }


        private static void AfterTrainingPrice(ref int __result)
        {
            var self = active;
            var top = DaggerfallUI.UIManager == null ? null : DaggerfallUI.UIManager.TopWindow;
            if (self == null || !currentServiceOwner || currentService != GuildServices.Training || __result <= 0 ||
                (!trainingCallbackActive && !ReferenceEquals(top, activeTrainingOffer) && !ReferenceEquals(top, activeTrainingPicker))) return;
            Adjust(self, currentServiceOwner, ref __result, "TRAINING");
        }

        // The offer's macro expands before the message box is pushed. Payment
        // likewise runs after its picker closes. Scope those native callbacks,
        // instead of relying only on which window is currently at the top.
        private static void BeforeTrainingQuote(out bool __state)
        {
            __state = trainingCallbackActive;
            trainingCallbackActive = true;
        }

        private static Exception AfterTrainingQuote(Exception __exception, bool __state)
        {
            trainingCallbackActive = __state;
            return __exception;
        }

        private static void AfterTrainingOffer(DaggerfallGuildServiceTraining __instance)
        {
            activeTrainingOffer = null;
            activeTrainingPicker = null;
            if (!currentServiceOwner || currentService != GuildServices.Training || DaggerfallUI.UIManager == null) return;
            var messageBox = DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
            if (messageBox == null || !HasEventTarget(messageBox, "OnButtonClick", __instance))
            {
                ClearTrainingContext();
                return;
            }
            activeTrainingOffer = messageBox;
        }

        private static bool BeforeTrainingConfirm(DaggerfallGuildServiceTraining __instance,
            DaggerfallMessageBox sender, DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            trainingCallbackActive = true;
            var self = active;
            if (messageBoxButton != DaggerfallMessageBox.MessageBoxButtons.Yes)
                return true;
            if (self == null || !currentServiceOwner)
                return true;
            if (CanContinue(self, currentServiceOwner, "TRAINING")) return true;
            trainingCallbackActive = false;
            activeTrainingOffer = null;
            currentServiceOwner = null;
            currentDaedraServiceWindow = null;
            currentService = default(GuildServices);
            DaggerfallUI.MessageBox("The guild refuses to continue training you.");
            return false;
        }

        private static void AfterTrainingConfirm(DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            trainingCallbackActive = false;
            activeTrainingOffer = null;
            activeTrainingPicker = messageBoxButton == DaggerfallMessageBox.MessageBoxButtons.Yes && DaggerfallUI.UIManager != null
                ? DaggerfallUI.UIManager.TopWindow as DaggerfallListPickerWindow : null;
            if (activeTrainingPicker == null) ClearTrainingContext();
        }

        private static void AfterTrainingPick()
        {
            trainingCallbackActive = false;
            ClearTrainingContext();
        }

        private static bool BeforeTrainingPick()
        {
            trainingCallbackActive = true;
            var self = active;
            if (self == null || !currentServiceOwner || CanContinue(self, currentServiceOwner, "TRAINING")) return true;
            trainingCallbackActive = false;
            currentServiceOwner = null;
            currentService = default(GuildServices);
            activeTrainingPicker = null;
            DaggerfallUI.MessageBox("The guild refuses to continue training you.");
            return false;
        }

        private static void AfterTrainingPickerPop(IUserInterfaceWindow __instance)
        {
            if (!trainingCallbackActive && ReferenceEquals(activeTrainingPicker, __instance)) ClearTrainingContext();
        }

        private static void AfterTrainingOfferPop(DaggerfallMessageBox __instance)
        {
            if (!trainingCallbackActive && ReferenceEquals(activeTrainingOffer, __instance)) ClearTrainingContext();
        }

        private static void ClearTrainingContext()
        {
            activeTrainingOffer = null;
            activeTrainingPicker = null;
            currentServiceOwner = null;
            currentService = default(GuildServices);
        }

        private static bool HasEventTarget(object instance, string eventName, object target)
        {
            var field = AccessTools.Field(instance.GetType(), eventName);
            var handlers = field == null ? null : field.GetValue(instance) as Delegate;
            if (handlers == null) return false;
            foreach (var handler in handlers.GetInvocationList())
                if (ReferenceEquals(handler.Target, target)) return true;
            return false;
        }

        private static void BeforeCureQuote(out StaticNPC __state)
        {
            __state = currentCureOwner;
            currentCureOwner = currentServiceOwner;
        }

        private static Exception AfterCureQuote(Exception __exception, StaticNPC __state)
        {
            currentCureOwner = __state;
            return __exception;
        }

        private static bool BeforeCureConfirm(DaggerfallGuildServiceCureDisease __instance,
            DaggerfallMessageBox sender, DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            var self = active;
            if (self == null || messageBoxButton != DaggerfallMessageBox.MessageBoxButtons.Yes || !currentServiceOwner)
                return true;
            if (CanContinue(self, currentServiceOwner, "CURE")) return true;
            currentServiceOwner = null;
            DaggerfallUI.MessageBox("The temple refuses to cure you.");
            return false;
        }

        private static void AfterCureConfirm()
        {
            currentServiceOwner = null;
            currentService = default(GuildServices);
        }

        private static void AfterCureTradePrice(int cost, int shopQuality, bool selling, ref int __result)
        {
            var self = active;
            if (self == null || selling || !currentCureOwner || currentService != GuildServices.CureDisease || cost <= 0 || __result <= 0) return;
            Adjust(self, currentCureOwner, ref __result, "CURE");
        }

        private static void AfterSpellbookPrice(ref int __result, DaggerfallSpellBookWindow __instance)
        {
            var self = active;
            var owner = ResolveGuildOwner(__instance == null ? null : __instance.PreviousWindow);
            if (self == null || !owner || __result <= 0) return;
            Adjust(self, owner, ref __result, "SPELLBOOK");
        }

        private static bool BeforeSpellbookConfirm(DaggerfallSpellBookWindow __instance,
            DaggerfallMessageBox sender, DaggerfallMessageBox.MessageBoxButtons messageBoxButton)
        {
            var self = active;
            var owner = ResolveGuildOwner(__instance == null ? null : __instance.PreviousWindow);
            if (self == null || messageBoxButton != DaggerfallMessageBox.MessageBoxButtons.Yes || !owner) return true;
            if (CanContinue(self, owner, "SPELLBOOK")) return true;
            __instance.CloseWindow();
            DaggerfallUI.MessageBox("The guild refuses to sell you spells.");
            return false;
        }

        private static void AfterItemMakerPrice(ref int __result)
        {
            var self = active;
            if (self == null || !currentServiceOwner || currentService != GuildServices.MakeMagicItems || __result <= 0 ||
                !IsWindowInActiveChain(typeof(DaggerfallItemMakerWindow))) return;
            Adjust(self, currentServiceOwner, ref __result, "ITEM_MAKER");
        }

        private static bool BeforeItemMakerConfirm(DaggerfallItemMakerWindow __instance)
        {
            return BeforeMakerConfirm(__instance, "ITEM_MAKER", "The enchanter refuses to work for you.");
        }

        private static void AfterSpellMakerPrice(ref FormulaHelper.SpellCost __result)
        {
            var self = active;
            if (self == null || !currentServiceOwner || currentService != GuildServices.MakeSpells || __result.goldCost <= 0 ||
                !IsWindowInActiveChain(typeof(DaggerfallSpellMakerWindow))) return;
            try { __result.goldCost = self.adjustServicePrice(currentServiceOwner, __result.goldCost); }
            catch (Exception error) { UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_SPELL_MAKER_PRICE_FAILED: " + error); }
        }

        private static bool BeforeSpellMakerConfirm(DaggerfallSpellMakerWindow __instance)
        {
            return BeforeMakerConfirm(__instance, "SPELL_MAKER", "The guild refuses to help create your spell.");
        }

        private static bool BeforeMakerConfirm(DaggerfallBaseWindow window, string label, string refusal)
        {
            var self = active;
            if (self == null || !currentServiceOwner) return true;
            if (CanContinue(self, currentServiceOwner, label)) return true;
            currentServiceOwner = null;
            DaggerfallUI.MessageBox(refusal);
            return false;
        }

        private static void AfterMakerPop(IUserInterfaceWindow __instance)
        {
            if (__instance is DaggerfallItemMakerWindow || __instance is DaggerfallSpellMakerWindow)
            {
                currentServiceOwner = null;
                currentService = default(GuildServices);
            }
        }

        private static StaticNPC ResolveGuildOwner(IUserInterfaceWindow window)
        {
            for (int depth = 0; window != null && depth < 8; depth++)
            {
                var guild = window as DaggerfallGuildServicePopupWindow;
                if (guild != null)
                    return (StaticNPC)AccessTools.Field(typeof(DaggerfallGuildServicePopupWindow), "serviceNPC").GetValue(guild);
                var popup = window as DaggerfallPopupWindow;
                window = popup == null ? null : popup.PreviousWindow;
            }
            return null;
        }

        private static bool IsWindowInActiveChain(Type type)
        {
            IUserInterfaceWindow window = DaggerfallUI.UIManager == null ? null : DaggerfallUI.UIManager.TopWindow;
            for (int depth = 0; window != null && depth < 8; depth++)
            {
                if (type.IsInstanceOfType(window)) return true;
                var popup = window as DaggerfallPopupWindow;
                if (popup == null) return false;
                window = popup.PreviousWindow;
            }
            return false;
        }

        private static bool CanContinue(NativeDispositionServiceHooks self, StaticNPC owner, string label)
        {
            try { return self.canServe(owner); }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_" + label + "_GATE_FAILED: " + error);
                return true;
            }
        }

        private static void Adjust(NativeDispositionServiceHooks self, StaticNPC owner, ref int price, string label)
        {
            try
            {
                price = self.adjustServicePrice(owner, price);
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DISPOSITION_" + label + "_PRICE_FAILED: " + error);
            }
        }

        public void Dispose()
        {
            if (active == this) active = null;
            currentServiceOwner = null;
            currentDaedraServiceWindow = null;
            currentService = default(GuildServices);
            currentCureOwner = null;
            activeTrainingOffer = null;
            activeTrainingPicker = null;
            trainingCallbackActive = false;
            harmony.UnpatchAll(Owner);
        }
    }
}
