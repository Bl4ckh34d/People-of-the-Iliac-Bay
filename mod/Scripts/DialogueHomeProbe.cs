using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Player;
using DaggerfallWorkshop.Game.UserInterface;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    internal static class DialogueHomeProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static IEnumerator Run(LivingCitiesMod adapter, DaggerfallTalkWindow window,
            MethodInfo submit, ListBox transcript, string personId)
        {
            var game = GameManager.Instance;
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Home dialogue probe requires the outdoor town fixture before its outdoor map checks.");
            var store = typeof(LivingCitiesMod).GetField("store", Private).GetValue(adapter) as WorldStateStore;
            var actor = UnityEngine.Object.FindObjectsOfType<ResidentActor>().FirstOrDefault(candidate =>
                candidate && candidate.Resident != null && candidate.Resident.Id == personId && candidate.Resident.IsAlive);
            if (!actor) throw new InvalidOperationException("Home dialogue probe needs the active tracked resident.");

            int settlement = game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            var town = store.Snapshot().Settlements.Find(item => item.SettlementId == settlement);
            var household = town == null ? null : town.Households.Find(item =>
                item.Residents.Exists(resident => resident.Id == personId));
            if (household == null || household.BuildingKey != actor.Resident.HomeKey)
                throw new InvalidOperationException("Dialogue resident HomeKey does not match saved household.");
            int homeKey = household.BuildingKey;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            BuildingSummary homeSummary;
            if (!directory || !directory.GetBuildingSummary(homeKey, out homeSummary))
                throw new InvalidOperationException("Current native BuildingDirectory does not contain tracked residential HomeKey " + homeKey + ".");
            string homeName = string.Empty;
            string nameLookup = "blank";
            try
            {
                homeName = game.TalkManager.GetBuildingNameForBuildingKey(homeKey);
                nameLookup = string.IsNullOrWhiteSpace(homeName) ? "blank" : "named";
            }
            catch (Exception e)
            {
                nameLookup = "missing/ambiguous: " + e.Message;
            }

            var mapField = typeof(PlayerGPS).GetField("discoveredLocations", Private);
            var reactionField = typeof(TalkManager).GetField("reactionToPlayer", Private);
            var mapToggle = typeof(LivingCitiesMod).GetField("conversationMapRequestsEnabled", Private);
            var homeTopic = typeof(LivingCitiesMod).GetField("conversationHomeTopicAvailable", Private);
            var actorField = typeof(LivingCitiesMod).GetField("conversationActor", Private);
            var busyField = typeof(LivingCitiesMod).GetField("conversationBusy", Private);
            if (mapField == null || reactionField == null || mapToggle == null || homeTopic == null || actorField == null || busyField == null)
                throw new InvalidOperationException("Home dialogue probe could not find required runtime state.");

            object originalMap = mapField.GetValue(game.PlayerGPS);
            object originalReaction = reactionField.GetValue(game.TalkManager);
            object originalToggle = mapToggle.GetValue(adapter);
            object originalTopic = homeTopic.GetValue(adapter);
            object originalActor = actorField.GetValue(adapter);
            object originalBusy = busyField.GetValue(adapter);
            try
            {
                mapField.SetValue(game.PlayerGPS, CloneDiscoveryMap(originalMap as Dictionary<int, PlayerGPS.DiscoveredLocation>));
                game.PlayerGPS.UndiscoverBuilding(homeKey);
                reactionField.SetValue(game.TalkManager, 19);
                mapToggle.SetValue(adapter, true);
                homeTopic.SetValue(adapter, false);
                busyField.SetValue(adapter, false);
                string lowTrust = Submit(adapter, window, submit, transcript, "Mark your house on my map");
                if (game.PlayerGPS.HasDiscoveredBuilding(homeKey) || !lowTrust.Contains("private"))
                    throw new InvalidOperationException("Low disposition disclosed or marked the tracked home: " + lowTrust);

                reactionField.SetValue(game.TalkManager, 20);
                mapToggle.SetValue(adapter, false);
                string disabled = Submit(adapter, window, submit, transcript, "Mark your house on my map");
                if (game.PlayerGPS.HasDiscoveredBuilding(homeKey) || !disabled.Contains("can't mark"))
                    throw new InvalidOperationException("Disabled map requests still marked the tracked home: " + disabled);

                mapToggle.SetValue(adapter, true);
                homeTopic.SetValue(adapter, false);
                string answer = Submit(adapter, window, submit, transcript, "Where do you live?");
                if (string.IsNullOrWhiteSpace(homeName) ?
                    !answer.Contains("house") : !answer.Contains(homeName))
                    throw new InvalidOperationException("Trusted home answer did not use the saved HomeKey's available description: " + answer + " (native name lookup: " + nameLookup + ")");
                string marked = Submit(adapter, window, submit, transcript, "Mark it on my map");
                string markedPhrase = string.IsNullOrWhiteSpace(homeName) ? "marked my house" : "marked my home";
                if (!game.PlayerGPS.HasDiscoveredBuilding(homeKey) || !marked.Contains(markedPhrase))
                    throw new InvalidOperationException("Trusted follow-up did not mark exact native residential key: " + marked);
                var discovery = (Dictionary<int, PlayerGPS.DiscoveredLocation>)mapField.GetValue(game.PlayerGPS);
                bool foundMarker = false;
                string markerName = string.Empty;
                foreach (var location in discovery.Values)
                {
                    if (location.discoveredBuildings == null || !location.discoveredBuildings.ContainsKey(homeKey)) continue;
                    foundMarker = true;
                    var markedBuilding=location.discoveredBuildings[homeKey];
                    markerName = string.IsNullOrWhiteSpace(markedBuilding.customUserDisplayName) ?
                        markedBuilding.displayName : markedBuilding.customUserDisplayName;
                    break;
                }
                if (!foundMarker || string.IsNullOrWhiteSpace(markerName))
                    throw new InvalidOperationException("Native discovery did not create a named marker for tracked HomeKey " + homeKey + ".");
                game.PlayerGPS.UndiscoverBuilding(homeKey);

                if (game.PlayerEnterExit.IsPlayerInsideBuilding)
                    throw new InvalidOperationException("Home dialogue probe unexpectedly entered an interior.");
                Debug.Log("LIVING_CITIES_HOME_DIALOGUE_PASS: resident=" + personId + "; home=" + homeKey +
                    "; building=" + (string.IsNullOrWhiteSpace(homeName) ? "unnamed " + homeSummary.BuildingType : homeName) +
                    "; talk-name=" + nameLookup + "; marker=" + markerName + "; disposition=20; map-toggle and outdoor discovery verified.");
            }
            finally
            {
                mapField.SetValue(game.PlayerGPS, originalMap);
                reactionField.SetValue(game.TalkManager, originalReaction);
                mapToggle.SetValue(adapter, originalToggle);
                homeTopic.SetValue(adapter, originalTopic);
                actorField.SetValue(adapter, originalActor);
                busyField.SetValue(adapter, originalBusy);
            }
            yield break;
        }

        private static string Submit(LivingCitiesMod adapter, DaggerfallTalkWindow window, MethodInfo submit,
            ListBox transcript, string text)
        {
            int before = transcript.Count;
            submit.Invoke(adapter, new object[] { window, text });
            if (transcript.Count != before + 2)
                throw new InvalidOperationException("Home dialogue handler did not reply synchronously to '" + text + "'.");
            return transcript.GetItem(before + 1).textLabel.Text;
        }

        private static Dictionary<int, PlayerGPS.DiscoveredLocation> CloneDiscoveryMap(
            Dictionary<int, PlayerGPS.DiscoveredLocation> source)
        {
            var copy = new Dictionary<int, PlayerGPS.DiscoveredLocation>();
            if (source == null) return copy;
            foreach (var pair in source)
            {
                var location = pair.Value;
                location.discoveredBuildings = location.discoveredBuildings == null ? null :
                    new Dictionary<int, PlayerGPS.DiscoveredBuilding>(location.discoveredBuildings);
                copy.Add(pair.Key, location);
            }
            return copy;
        }
    }
}
