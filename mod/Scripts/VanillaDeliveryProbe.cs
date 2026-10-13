using System;
using System.Collections;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Opt-in validation of an unchanged shipped quest. This is not an automatic eligibility rule.
    public static class VanillaDeliveryProbe
    {
        private static readonly string[] Packages = { "_book_", "_womensclothing_", "_potion_", "_mensclothing_",
            "_painting_", "_weapon_", "_ingredient_", "_jewelry_" };
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            Require(game.PlayerEntity.Name.StartsWith("LivingCitiesTest-", StringComparison.Ordinal), "Disposable character required.");
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var town = ((WorldState)adapter.GetSaveData()).Settlements.Find(t => t.SettlementId == game.PlayerGPS.CurrentMapID);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var taverns = directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern);
            Require(taverns.Count > 0, "Fixture tavern missing.");
            yield return QuestOfferProbe.Enter(taverns[0].buildingKey);
            var npcs = game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>();
            Require(npcs.Length > 0, "Native quest giver context missing.");
            QuestMachine.Instance.LastNPCClicked = npcs[0];
            Quest quest = null; Household home = null; Resident resident = null;
            for (int attempt = 0; attempt < 40 && resident == null; attempt++)
            {
                quest = game.QuestListsManager.GetQuest("A0C00Y14");
                Require(quest != null, "Shipped delivery quest did not load.");
                var person = quest.GetPerson(new Symbol("_pickuplocal_"));
                var place = person.GetHomePlace();
                if (place == null || place.SiteDetails.mapId != town.SettlementId) continue;
                home = town.Households.Find(h => h.BuildingKey == place.SiteDetails.buildingKey);
                if (home == null) continue;
                resident = home.Residents.Find(r => r.IsAlive && r.AgeGroup == ResidentAgeGroup.Adult &&
                    (r.Sex == ResidentSex.Male) == (person.Gender == Genders.Male));
            }
            Require(resident != null, "No compatible household recipient in fixture town.");
            string first, surname;
            uint oldSeed = DFRandom.Seed;
            try
            {
                DFRandom.Seed = resident.AppearanceSeed;
                first = DaggerfallUnity.Instance.NameHelper.FirstName(NameHelper.BankTypes.Breton,
                    resident.Sex == ResidentSex.Male ? Genders.Male : Genders.Female);
                DFRandom.Seed = home.SurnameSeed;
                surname = DaggerfallUnity.Instance.NameHelper.Surname(NameHelper.BankTypes.Breton);
            }
            finally { DFRandom.Seed = oldSeed; }
            var binding = new QuestResidentBinding { QuestUid = quest.UID, QuestName = quest.QuestName,
                PersonSymbol = "_pickuplocal_", SettlementId = town.SettlementId, HomeKey = home.BuildingKey,
                ResidentId = resident.Id, PersonName = first + " " + surname,
                HomeName = TextManager.Instance.GetLocalizedText("theNamedResidence").Replace("%s", surname),
                RuleId = "probe/shipped-delivery/v1" };
            adapter.QuestOffers.Prepare(quest, new[] { binding });
            Require(MessageText(quest, 1002).Contains(binding.PersonName), "Native acceptance expansion disagrees with recipient.");
            QuestMachine.Instance.StartQuest(quest);
            string package = null;
            float deadline = Time.realtimeSinceStartup + 15;
            while (package == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (string symbol in Packages)
                    if (game.PlayerEntity.Items.Contains(quest.GetItem(new Symbol(symbol)))) { package = symbol; break; }
                DaggerfallUI.Instance.PopToHUD(); yield return null;
            }
            Require(package != null, "Quest script did not give its delivery item.");
            yield return QuestOfferProbe.Enter(home.BuildingKey);
            yield return ReloadFixture("Living Cities - vanilla delivery");
            quest = QuestMachine.Instance.GetQuest(binding.QuestUid);
            Require(quest != null && game.PlayerEntity.Items.Contains(quest.GetItem(new Symbol(package))),
                "Actual quest or delivery item lost across reload.");
            Require(Array.Exists(quest.GetLogMessages(), entry => entry.messageID == 1010), "Native journal step did not survive reload.");
            quest.CurrentLogMessageId = 1010;
            Require(MessageText(quest, 1010).Contains(binding.PersonName), "Reloaded journal disagrees with published recipient.");
            QuestResourceBehaviour recipient = null;
            foreach (var target in UnityEngine.Object.FindObjectsOfType<QuestResourceBehaviour>())
                if (target.QuestUID == quest.UID && target.TargetSymbol.Name == "pickuplocal") recipient = target;
            Require(recipient && recipient.GetComponent<StaticNPC>().DisplayName == binding.PersonName,
                "Reloaded native recipient differs from published name.");
            int goldBefore = game.PlayerEntity.GoldPieces;
            int reward = quest.GetItem(new Symbol("_gold_")).DaggerfallUnityItem.stackCount;
            typeof(PlayerActivate).GetMethod("StaticNPCClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
                game.PlayerActivate, new object[] { recipient.GetComponent<StaticNPC>() });
            deadline = Time.realtimeSinceStartup + 10;
            while (!quest.QuestSuccess && Time.realtimeSinceStartup < deadline) yield return null;
            Require(quest.QuestSuccess && !game.PlayerEntity.Items.Contains(quest.GetItem(new Symbol(package))),
                "Native delivery did not consume package and award success.");
            var message = DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
            Require(message != null, "Native completion message missing.");
            message.CloseWindow(); yield return null; yield return null;
            var inventory = DaggerfallUI.Instance.InventoryWindow;
            Require(DaggerfallUI.UIManager.TopWindow == inventory && inventory.LootTarget != null,
                "Native reward loot window missing.");
            var loot = inventory.LootTarget;
            Require(loot.Items.Count == 1 && loot.Items.GetItem(0).stackCount == reward, "Native gold reward differs from quest promise.");
            typeof(DaggerfallInventoryWindow).GetMethod("DoTransferItem", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(
                inventory, new object[] { loot.Items.GetItem(0), loot.Items, game.PlayerEntity.Items, false });
            Require(game.PlayerEntity.GoldPieces == goldBefore + reward && loot.Items.Count == 0, "Native reward collection failed.");
            DaggerfallUI.Instance.PopToHUD(); yield return new WaitForSecondsRealtime(2);
            Require(quest.QuestComplete && !adapter.QuestOffers.OwnsResident(resident.Id), "Completed delivery kept resident ownership.");
            Require(((WorldState)adapter.GetSaveData()).QuestBindings.Exists(b => b.QuestUid == binding.QuestUid),
                "Quest completion erased historical resident contract.");
            Debug.Log("LIVING_CITIES_TOWN_DELIVERY_PASS: unchanged A0C00Y14 loaded by QuestListsManager; native package; real save/load; native recipient hand-in; collected gold=" +
                reward + "; recipient=" + binding.PersonName + "; resident=" + resident.Id + "; uid=" + binding.QuestUid);
        }
        private static string MessageText(Quest quest, int id)
        {
            string result = "";
            foreach (var token in quest.GetMessage(id).GetTextTokens()) result += token.text + " ";
            return result;
        }
        private static IEnumerator ReloadFixture(string name)
        {
            var game = GameManager.Instance; bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Save(game.PlayerEntity.Name, name);
                float deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                Require(saved, "Delivery fixture save timed out."); yield return null;
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, name);
                Require(key >= 0, "Delivery fixture slot missing.");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || game.SaveLoadManager.LoadInProgress) && Time.realtimeSinceStartup < deadline) yield return null;
                Require(loaded && !game.SaveLoadManager.LoadInProgress, "Delivery fixture reload timed out.");
                yield return new WaitForSecondsRealtime(2);
            }
            finally { SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad; }
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("Vanilla delivery probe: " + message); }
    }
}
