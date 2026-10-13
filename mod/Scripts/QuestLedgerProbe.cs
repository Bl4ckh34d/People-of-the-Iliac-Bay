using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Opt-in fixture only. This does not install an offer-acceptance adapter.
    public static class QuestLedgerProbe
    {
        private const string SaveName = "Living Cities - quest binding ledger";
        public static IEnumerator Run(string questJson, IDictionary<int, string[]> homes)
        {
            var game = GameManager.Instance;
            Require(game.PlayerEntity.Name.StartsWith("LivingCitiesTest-", StringComparison.Ordinal), "Disposable character required.");
            Require(!string.IsNullOrEmpty(questJson), "Parser probe did not return its bound quest.");
            var quest = new Quest();
            quest.RestoreSaveData((Quest.QuestSaveData_v1)SaveLoadManager.Deserialize(typeof(Quest.QuestSaveData_v1), questJson));
            var person = quest.GetPerson(new Symbol("_recipient_"));
            var site = person.GetHomePlace().SiteDetails;
            string[] expected = homes[site.buildingKey];
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            // Access the actual store only from the opt-in harness, without a test-only runtime API.
            var field = typeof(LivingCitiesMod).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance);
            var store = (WorldStateStore)field.GetValue(adapter);
            var candidate = new QuestResidentBinding { QuestUid = quest.UID, QuestName = quest.QuestName,
                PersonSymbol = "_recipient_", SettlementId = site.mapId, HomeKey = site.buildingKey,
                ResidentId = expected[2], PersonName = expected[0], HomeName = expected[1],
                RuleId = "probe/explicit-permanent-resident/v1" };
            string before = SaveLoadManager.Serialize(typeof(WorldState), adapter.GetSaveData());
            var rejectedOffer = store.PrepareQuestBindings(new[] { candidate });
            Require(before == SaveLoadManager.Serialize(typeof(WorldState), adapter.GetSaveData()), "Unaccepted offer changed saved state.");
            // Native startup, not a simulated guild UI click. The production acceptance hook is still pending.
            QuestMachine.Instance.StartQuest(quest);
            Require(QuestMachine.Instance.GetQuest(quest.UID) == quest, "Native startup failed.");
            store.CommitQuestBindings(rejectedOffer);
            ulong uid = quest.UID;
            bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave;
            SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Save(game.PlayerEntity.Name, SaveName);
                float deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                Require(saved, "Binding fixture save timed out.");
                yield return null;
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, SaveName);
                Require(key >= 0, "Binding fixture save missing.");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 90;
                while ((!loaded || game.SaveLoadManager.LoadInProgress) && Time.realtimeSinceStartup < deadline) yield return null;
                Require(loaded && !game.SaveLoadManager.LoadInProgress, "Binding fixture load timed out.");
                yield return new WaitForSecondsRealtime(2);
                var state = (WorldState)adapter.GetSaveData();
                var binding = state.QuestBindings.Find(b => b.QuestUid == uid && b.PersonSymbol == "_recipient_");
                var restored = QuestMachine.Instance.GetQuest(uid);
                Require(restored != null && binding != null && restored != quest, "Quest or binding was not reconstructed.");
                var restoredPerson = restored.GetPerson(new Symbol("_recipient_"));
                Require(binding.ResidentId == expected[2] && binding.PersonName == restoredPerson.DisplayName &&
                    binding.HomeName == restoredPerson.HomeBuildingName && binding.PersonName == expected[0] &&
                    binding.HomeName == expected[1] && binding.HomeKey == site.buildingKey && binding.SettlementId == site.mapId &&
                    binding.QuestName == restored.QuestName && binding.RuleId == candidate.RuleId,
                    "Native quest and persistent resident contract disagree after save/load.");
                var home = state.Settlements.Find(t => t.SettlementId == site.mapId).Households.Find(h => h.BuildingKey == site.buildingKey);
                Require(home.Residents.Exists(r => r.Id == binding.ResidentId), "Bound household resident was lost.");
                Debug.Log("LIVING_CITIES_TOWN_QUEST_LEDGER_PASS: native quest startup; mod sidecar and quest reconstructed; uid=" + uid +
                    "; resident=" + binding.ResidentId + "; name=" + binding.PersonName + "; schema=" + state.Version +
                    "; save=" + game.SaveLoadManager.GetSaveFolder(key));
            }
            finally
            {
                SaveLoadManager.OnSave -= onSave;
                SaveLoadManager.OnLoad -= onLoad;
            }
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("Quest ledger probe: " + message); }
    }
}
