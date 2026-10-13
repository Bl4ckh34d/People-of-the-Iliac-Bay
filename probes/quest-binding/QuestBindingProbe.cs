using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using HarmonyLib;
using UnityEngine;

namespace LivingCities.Probes
{
    // Disposable feasibility probe. No general-purpose quest rewriting is enabled.
    public static class QuestBindingProbe
    {
        private const string Owner = "local.livingcities.quest-binding-probe";
        private const string QuestName = "_LC_BIND_PROBE";
        private static readonly Symbol Recipient = new Symbol("_recipient_");
        private static IDictionary<int, string[]> homes;
        private static int mapId;
        private static int calls;
        public static string BoundQuestJson { get; private set; }
        [Invoke(StateManager.StateTypes.Start, 100)]
        public static void Init(InitParams p) { p.Mod.IsReady = true; }
        public static IEnumerator Run(int settlementId, IDictionary<int, string[]> householdResidents)
        {
            homes = householdResidents; calls = 0; mapId = settlementId; BoundQuestJson = null;
            var harmony = new Harmony(Owner);
            var method = typeof(Parser).GetMethod("Parse", new[] { typeof(string[]), typeof(int), typeof(bool) });
            try
            {
                Require(method != null, "Native parser boundary missing.");
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(QuestBindingProbe).GetMethod("Parsed")));
                var machine = QuestMachine.Instance;
                var source = new[] { "Quest: " + QuestName, "DisplayName: Household binding probe", "QRC:",
                    "Message: 1000", "Deliver to _recipient_ at __recipient_.", "", "QBN:",
                    "Person _recipient_ group Resident1 male local", "Person _fixed_ named Meridia", "",
                    "_clicked_ task:", " clicked npc _recipient_" };
                var partial = machine.ParseQuest(QuestName, source, 0, true);
                Require(partial != null && partial.GetAllResources().Length == 0 && calls == 0, "Partial preview mutated binding.");
                Quest quest = null; string[] expected = null;
                for (int attempt = 0; attempt < 20 && expected == null; attempt++)
                {
                    quest = machine.ParseQuest(QuestName, source);
                    Require(quest != null, "Probe parse failed.");
                    var home = quest.GetPerson(Recipient).GetHomePlace();
                    if (home != null && home.SiteDetails.mapId == mapId) homes.TryGetValue(home.SiteDetails.buildingKey, out expected);
                }
                Require(expected != null, "No generated quest destination matched an existing household.");
                var person = quest.GetPerson(Recipient);
                Require(machine.GetQuest(quest.UID) == null, "Probe quest started before its offer.");
                Require(person.DisplayName == expected[0], "Recipient name was not bound before offer expansion.");
                Require(person.HomeBuildingName == expected[1], "Residence name was not bound before offer expansion.");
                var fixedPerson = quest.GetPerson(new Symbol("_fixed_"));
                Require(fixedPerson.IsIndividualNPC && fixedPerson.DisplayName == "Meridia", "Fixed named character changed.");
                string text = string.Join(" ", quest.GetMessage(1000).GetTextTokens().Select(t => t.text ?? ""));
                Require(text.Contains(expected[0]) && text.Contains(expected[1]), "Native offer macro expansion disagrees with bound resources: " + text);
                var prompt = machine.CreateMessagePrompt(quest, 1000);
                var label = (MultiFormatTextLabel)prompt.GetType().GetField("label", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(prompt);
                string promptText = string.Join(" ", label.TextLabels.Select(t => t.Text));
                Require(promptText.Contains(expected[0]) && promptText.Contains(expected[1]), "Native prompt labels disagree with bound names.");
                BoundQuestJson = SaveLoadManager.Serialize(typeof(Quest.QuestSaveData_v1), quest.GetSaveData());
                var destination = new Place(quest, "Place _next_ local tavern"); quest.AddResource(destination);
                person.SetAssignedPlaceSymbol(destination.Symbol);
                string movedText = string.Join(" ", quest.GetMessage(1000).GetTextTokens().Select(t => t.text ?? ""));
                Require(movedText.Contains(expected[0]) && movedText.Contains(destination.SiteDetails.buildingName) &&
                    person.HomeBuildingName == expected[1], "Assigned destination did not override dialog place while preserving home/name.");
                var serialized = SaveLoadManager.Serialize(typeof(Quest.QuestSaveData_v1), quest.GetSaveData());
                var restored = new Quest();
                restored.RestoreSaveData((Quest.QuestSaveData_v1)SaveLoadManager.Deserialize(typeof(Quest.QuestSaveData_v1), serialized));
                Require(restored.UID == quest.UID && restored.GetPerson(Recipient).DisplayName == expected[0] &&
                    restored.GetPerson(Recipient).HomeBuildingName == person.HomeBuildingName,
                    "Native quest serialization lost binding.");
                Require(restored.GetPerson(Recipient).GetDialogPlace().Symbol.Name == destination.Symbol.Name,
                    "Native serialization lost changed dialog destination.");
                int beforeExcluded = calls;
                var fixedSource = source.Select(line => line.StartsWith("Person _recipient_ ") ?
                    "Person _recipient_ named Meridia" : line).ToArray();
                var fixedQuest = machine.ParseQuest(QuestName, fixedSource);
                Require(fixedQuest != null && fixedQuest.GetPerson(Recipient).DisplayName == "Meridia" && calls == beforeExcluded,
                    "Fixed recipient was not excluded from the binding hook.");
                var unrelatedSource = source.Select(line => line == "Quest: " + QuestName ? "Quest: _LC_UNRELATED_PROBE" : line).ToArray();
                Require(machine.ParseQuest("_LC_UNRELATED_PROBE", unrelatedSource) != null && calls == beforeExcluded,
                    "Probe modified an unrelated quest.");
                harmony.UnpatchAll(Owner);
                var info = Harmony.GetPatchInfo(method);
                Require(info == null || !info.Owners.Contains(Owner), "Probe patch remained installed.");
                Require(machine.ParseQuest(QuestName, source) != null && calls == beforeExcluded,
                    "Interception continued after unpatching.");
                Debug.Log("LIVING_CITIES_QUEST_BINDING_PASS: parser interception before offer; partial preview; fixed name; native prompt labels; changed dialog destination; FullSerializer; patches removed; recipient=" + expected[0]);
            }
            finally { harmony.UnpatchAll(Owner); homes = null; }
            yield break;
        }
        public static void Parsed(bool partialParse, Quest __result)
        {
            if (partialParse || __result == null || __result.QuestName != QuestName) return;
            var person = __result.GetPerson(Recipient);
            if (person == null || person.IsIndividualNPC || person.IsQuestor || person.GetHomePlace() == null) return;
            var home = person.GetHomePlace(); var site = home.SiteDetails;
            string[] names;
            if (site.mapId != mapId || !homes.TryGetValue(site.buildingKey, out names)) return;
            var saved = (Person.SaveData_v1)person.GetSaveData();
            string originalPerson = SaveLoadManager.Serialize(typeof(Person.SaveData_v1), saved);
            string originalName = saved.displayName;
            string originalSite = SaveLoadManager.Serialize(site.GetType(), site); string originalBuildingName = site.buildingName;
            saved.displayName = names[0];
            person.RestoreSaveData(saved);
            site.buildingName = names[1]; home.SiteDetails = site;
            var checkedPerson = (Person.SaveData_v1)person.GetSaveData(); checkedPerson.displayName = originalName;
            var checkedSite = home.SiteDetails; checkedSite.buildingName = originalBuildingName;
            Require(originalPerson == SaveLoadManager.Serialize(typeof(Person.SaveData_v1), checkedPerson) &&
                originalSite == SaveLoadManager.Serialize(checkedSite.GetType(), checkedSite), "Name binding changed non-name resource identity or site fields.");
            calls++;
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
