using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class QuestOfferProbe
    {
        // Expose native protected entry without replacing its implementation or event wiring.
        private sealed class GuildOffer : DaggerfallGuildServicePopupWindow
        {
            public GuildOffer(StaticNPC npc) : base(DaggerfallUI.UIManager, npc, FactionFile.GuildGroups.FightersGuild, FightersGuild.FactionId) { }
            public void Offer(Quest quest) { offeredQuest = quest; questPool = new List<QuestData>(); OfferQuest(); }
        }

        public static IEnumerator Run(WorldStateStore store, Quest quest, QuestResidentBinding candidate)
        {
            var game = GameManager.Instance;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var halls = directory.GetBuildingsOfType(DFLocation.BuildingTypes.GuildHall);
            var hall = halls.Find(b => b.FactionId == FightersGuild.FactionId);
            Require(hall.FactionId == FightersGuild.FactionId, "No Fighters Guild in fixture town.");
            var now = DaggerfallUnity.Instance.WorldTime.Now;
            now.RaiseTime((86400 + 12 * 3600 - (int)(now.ToSeconds() % 86400)) % 86400);
            yield return Enter(hall.buildingKey);
            game.PlayerEnterExit.Interior.UpdateNpcPresence(); yield return null;
            StaticNPC questor = null;
            foreach (var npc in game.PlayerEnterExit.Interior.GetComponentsInChildren<StaticNPC>(true))
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUILD_NPC: faction=" + npc.Data.factionID + "; active=" + npc.gameObject.activeInHierarchy + "; hour=" + DaggerfallUnity.Instance.WorldTime.Now.Hour);
                if (npc.gameObject.activeInHierarchy && npc.Data.factionID == (int)GuildNpcServices.FG_Quests) questor = npc;
            }
            Require(questor, "Actual Fighters Guild quest service NPC missing.");
            QuestMachine.Instance.LastNPCClicked = questor;
            var offers = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>().QuestOffers;
            {
                string before = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
                offers.Prepare(quest, new[] { candidate });
                yield return ShowAndClick(questor, quest, DaggerfallMessageBox.MessageBoxButtons.No, candidate);
                Require(QuestMachine.Instance.GetQuest(quest.UID) == null &&
                    before == SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot()), "Refused offer committed a quest or household change.");

                offers.Prepare(quest, new[] { candidate });
                var town = store.GetOrCreate(candidate.SettlementId, new[] { candidate.HomeKey });
                var resident = town.Households.Find(h => h.BuildingKey == candidate.HomeKey).Residents.Find(r => r.Id == candidate.ResidentId);
                resident.IsAlive = false;
                yield return ShowAndClick(questor, quest, DaggerfallMessageBox.MessageBoxButtons.Yes, candidate);
                Require(QuestMachine.Instance.GetQuest(quest.UID) == null && store.Snapshot().QuestBindings.Count == 0,
                    "Stale offer started after recipient died.");
                resident.IsAlive = true;

                offers.Prepare(quest, new[] { candidate });
                yield return ShowAndClick(questor, quest, DaggerfallMessageBox.MessageBoxButtons.Yes, candidate, true);
                Require(QuestMachine.Instance.GetQuest(quest.UID) == quest && store.Snapshot().QuestBindings.Exists(b =>
                    b.QuestUid == quest.UID && b.ResidentId == candidate.ResidentId), "Native acceptance did not commit the staged resident binding.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_QUEST_OFFER_PASS: actual guild OfferQuest and message-button handlers; refusal; dead-recipient rejection; acceptance and committed binding.");
            yield return Enter(candidate.HomeKey);
            // At night this resident's normal schedule is also home: expose duplicate representations.
            now.RaiseTime((86400 + 23 * 3600 - (int)(now.ToSeconds() % 86400)) % 86400);
            yield return new WaitForSecondsRealtime(2);
            float deadline = Time.realtimeSinceStartup + 15;
            QuestResourceBehaviour target = null;
            while (!target && Time.realtimeSinceStartup < deadline)
            {
                foreach (var resource in UnityEngine.Object.FindObjectsOfType<QuestResourceBehaviour>())
                    if (resource.QuestUID == quest.UID && resource.TargetSymbol.Name == new Symbol(candidate.PersonSymbol).Name) { target = resource; break; }
                yield return null;
            }
            Require(target && target.GetComponent<StaticNPC>(), "Native quest recipient did not appear at the published home.");
            Require(!UnityEngine.Object.FindObjectOfType<StreetPopulation>().FindActor(candidate.ResidentId),
                "Bound resident appears twice: household actor and native quest recipient.");
            Require(target.GetComponent<StaticNPC>().DisplayName == candidate.PersonName, "Native quest recipient name disagrees with offer: " + target.GetComponent<StaticNPC>().DisplayName + " / " + candidate.PersonName);
            var click = typeof(PlayerActivate).GetMethod("StaticNPCClick", BindingFlags.Instance | BindingFlags.NonPublic);
            quest.GetPerson(new Symbol(candidate.PersonSymbol)).RearmPlayerClick();
            quest.GetTask(new Symbol("_clicked_")).IsTriggered = false;
            click.Invoke(game.PlayerActivate, new object[] { target.GetComponent<StaticNPC>() }); quest.Update();
            Require(quest.GetTask(new Symbol("_clicked_")).IsTriggered, "Native recipient activation failed to reach quest task.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_QUEST_RECIPIENT_PASS: native placement and activation at bound household; name=" + candidate.PersonName);
        }

        private static IEnumerator ShowAndClick(StaticNPC questor, Quest quest, DaggerfallMessageBox.MessageBoxButtons choice, QuestResidentBinding expected, bool expectStarted = false)
        {
            var window = new GuildOffer(questor);
            DaggerfallUI.UIManager.PushWindow(window); yield return null;
            window.Offer(quest); yield return null;
            var box = DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
            Require(box != null, "Native guild offer prompt missing.");
            var label = (MultiFormatTextLabel)typeof(DaggerfallMessageBox).GetField("label", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(box);
            string text = ""; foreach (var part in label.TextLabels) text += part.Text + " ";
            Require(text.Contains(expected.PersonName) && text.Contains(expected.HomeName), "Guild offer labels do not match published contract.");
            var buttons = (List<Button>)typeof(DaggerfallMessageBox).GetField("buttons", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(box);
            var button = buttons.Find(b => (DaggerfallMessageBox.MessageBoxButtons)b.Tag == choice);
            Require(button != null, "Native choice button missing.");
            typeof(DaggerfallMessageBox).GetMethod("ActivateButton", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(box, new object[] { button });
            yield return null;
            var response = DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
            Require(response != null, "Native accept/refuse response missing.");
            var responseLabel = (MultiFormatTextLabel)typeof(DaggerfallMessageBox).GetField("label", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(response);
            string responseText = ""; foreach (var part in responseLabel.TextLabels) responseText += part.Text + " ";
            Require(expectStarted ? responseText.Contains("Accepted:") && responseText.Contains(expected.PersonName) :
                responseText.Contains("Offer refused.") && !responseText.Contains("Accepted:"), "Native response text does not match acceptance decision.");
            DaggerfallUI.Instance.PopToHUD(); yield return null;
        }
        internal static IEnumerator Enter(int buildingKey)
        {
            var game = GameManager.Instance;
            if (game.PlayerEnterExit.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            DaggerfallStaticDoors owner = null; StaticDoor selected = default(StaticDoor);
            foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach (var door in candidate.Doors)
                    if (door.doorType == DoorTypes.Building && door.buildingKey == buildingKey) { owner = candidate; selected = door; break; }
            Require(owner, "Building entrance missing.");
            TownSmokeHarness.EnterInterior(owner.transform, selected);
            yield return new WaitForSecondsRealtime(2); DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
        }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException("Quest offer probe: " + message); }
    }
}
