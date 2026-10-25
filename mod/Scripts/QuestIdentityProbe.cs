using System;
using System.Collections;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in disposable-save probe, not gameplay or a quest replacement system.
    public static class QuestIdentityProbe
    {
        private const string SaveName = "People of the Iliac Bay - quest identity probe";
        private static readonly Symbol Owner = new Symbol("_owner_");
        private static readonly Symbol Clicked = new Symbol("_clicked_");

        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            Require(game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal), "Disposable character required.");
            // Generated family homes may contain no original NPC at all. Use a
            // real tavern, whose vanilla occupants can own a Questor resource.
            if (game.PlayerEnterExit.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var taverns = directory.GetBuildingsOfType(DFLocation.BuildingTypes.Tavern);
            Require(taverns.Count > 0, "Fixture has no tavern for an original NPC.");
            DaggerfallStaticDoors doorOwner = null; StaticDoor entrance = default(StaticDoor);
            foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var door in candidate.Doors)
                    if (door.doorType == DoorTypes.Building && door.buildingKey == taverns[0].buildingKey)
                    { doorOwner = candidate; entrance = door; break; }
            Require(doorOwner != null, "Tavern entrance missing.");
            TownSmokeHarness.EnterInterior(doorOwner.transform, entrance);
            yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            Require(game.PlayerEnterExit.IsPlayerInsideBuilding, "Interior required for quest probe.");
            StaticNPC npc = null;
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<StaticNPC>())
                if (!candidate.IsChildNPC && candidate.Data.context == StaticNPC.Context.Building &&
                    !candidate.GetComponent<QuestResourceBehaviour>()) { npc = candidate; break; }
            Require(npc != null, "No unbound original adult NPC in fixture interior.");
            var machine = QuestMachine.Instance;
            var originalData = npc.Data;
            string originalJson = JsonUtility.ToJson(originalData), originalName = npc.DisplayName;
            Vector3 originalPosition = npc.transform.position;
            machine.LastNPCClicked = npc;
            var quest = machine.ParseQuest("_LC_ID_PROBE", new[] {
                "Quest: _LC_ID_PROBE", "DisplayName: People of the Iliac Bay identity probe", "QRC:",
                "Message: 1000", "Disposable integration probe.", "", "QBN:",
                "Person _owner_ group Questor", "", "_clicked_ task:", " clicked npc _owner_" });
            Require(quest != null, "Probe quest did not parse.");
            machine.StartQuest(quest);
            ulong uid = quest.UID;
            yield return null;
            RequireBinding(npc, quest, originalJson, originalName);
            CheckNativeClick(npc, quest);

            // A transform move must not rewrite the immutable layout identity.
            npc.transform.position += Vector3.up * 0.5f;
            yield return null;
            RequireBinding(npc, quest, originalJson, originalName);
            CheckNativeClick(npc, quest);
            npc.transform.position = originalPosition;

            // Negative controls prove all four key fields matter to native matching.
            var changed = originalData; changed.hash ^= 1;
            Require(!machine.IsNPCDataEqual(originalData, changed), "Hash change did not break identity.");
            changed = originalData; changed.mapID ^= 1;
            Require(!machine.IsNPCDataEqual(originalData, changed), "Map change did not break identity.");
            changed = originalData; changed.nameSeed ^= 1;
            Require(!machine.IsNPCDataEqual(originalData, changed), "Name seed change did not break identity.");
            changed = originalData; changed.buildingKey ^= 1;
            Require(!machine.IsNPCDataEqual(originalData, changed), "Building change did not break identity.");

            var person = quest.GetPerson(Owner);
            person.IsHidden = true; quest.Update();
            yield return null;
            Require(!npc.gameObject.activeSelf, "Quest hide did not suppress bound NPC.");
            person.IsHidden = false; quest.Update();
            yield return null;
            Require(npc.gameObject.activeSelf, "Quest show did not restore bound NPC.");
            RequireBinding(npc, quest, originalJson, originalName);
            CheckNativeClick(npc, quest);

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
                Require(saved, "Quest fixture save timed out.");
                yield return null;
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, SaveName);
                Require(key >= 0, "Quest fixture save missing.");
                int previousInstance = npc.GetInstanceID();
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 90;
                while ((!loaded || game.SaveLoadManager.LoadInProgress) && Time.realtimeSinceStartup < deadline) yield return null;
                Require(loaded && !game.SaveLoadManager.LoadInProgress, "Quest fixture load timed out.");
                yield return new WaitForSecondsRealtime(2);
                quest = machine.GetQuest(uid);
                Require(quest != null && quest.GetTask(Clicked).IsTriggered, "Quest identity or clicked task lost on reload.");
                npc = null; int matches = 0;
                foreach (var candidate in UnityEngine.Object.FindObjectsOfType<StaticNPC>())
                    if (machine.IsNPCDataEqual(originalData, candidate.Data)) { npc = candidate; matches++; }
                Require(matches == 1 && npc.GetInstanceID() != previousInstance, "Reload did not reconstruct one original NPC.");
                RequireBinding(npc, quest, originalJson, originalName);
                CheckNativeClick(npc, quest);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_QUEST_PASS: transform, native click task, hide/show, reconstructed identity and save/load; quest=" +
                    uid + "; name=" + originalName + "; building=" + originalData.buildingKey + "; save=" + game.SaveLoadManager.GetSaveFolder(key));
            }
            finally
            {
                SaveLoadManager.OnSave -= onSave;
                SaveLoadManager.OnLoad -= onLoad;
            }
        }

        private static void RequireBinding(StaticNPC npc, Quest quest, string data, string name)
        {
            var binding = npc.GetComponent<QuestResourceBehaviour>();
            Require(JsonUtility.ToJson(npc.Data) == data && npc.DisplayName == name, "Original NPC data or name changed.");
            Require(binding != null && binding.QuestUID == quest.UID && binding.TargetSymbol.Name == Owner.Name &&
                binding.TargetResource == quest.GetPerson(Owner), "Quest resource binding missing or changed.");
            Require(QuestMachine.Instance.ActiveQuestor(npc.Data) == quest.GetPerson(Owner), "Questor no longer recognized.");
        }

        private static void CheckNativeClick(StaticNPC npc, Quest quest)
        {
            quest.GetPerson(Owner).RearmPlayerClick();
            quest.GetTask(Clicked).IsTriggered = false;
            var click = typeof(PlayerActivate).GetMethod("StaticNPCClick", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(click != null, "Native static activation boundary missing.");
            // Invoke the actual native activation handler; this does not test mouse raycasting.
            click.Invoke(GameManager.Instance.PlayerActivate, new object[] { npc });
            quest.Update();
            Require(quest.GetTask(Clicked).IsTriggered, "Native click did not reach quest task.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Quest identity probe: " + message);
        }
    }
}
