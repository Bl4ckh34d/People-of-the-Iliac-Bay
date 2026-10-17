using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game;
using LivingCities.Core;

namespace LivingCities.DFU
{
    public sealed partial class LivingCitiesMod
    {
        private bool conversationHomeTopicAvailable;

        private bool TryConversationHome(out int homeSettlement, out int homeKey, out string homeName)
        {
            homeSettlement = -1;
            homeKey = -1;
            homeName = string.Empty;
            if (GameManager.Instance == null || !GameManager.Instance.PlayerGPS.HasCurrentLocation || store == null)
                return false;

            int currentSettlement = GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId;
            ResidentActor actor = conversationActor;
            if (actor && actor.Resident != null && actor.Resident.IsAlive && actor.Resident.Id == conversationPersonId)
            {
                ResidentTrip trip = store.GetResidentDialogueTrip(actor.Resident.Id);
                homeSettlement = trip == null ? currentSettlement : trip.HomeSettlement;
                homeKey = actor.Resident.HomeKey;
            }
            else
            {
                NativePerson native = FindConversationNative(currentSettlement);
                if (native == null || !native.IsAlive || native.Id != conversationPersonId) return false;
                homeSettlement = native.SettlementId;
                homeKey = native.HomeKey;
            }

            if (homeSettlement < 0 || homeKey < 0) return false;
            if (homeSettlement != currentSettlement) return true;
            var directory = GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            BuildingSummary summary;
            if (!directory || !directory.GetBuildingSummary(homeKey, out summary)) return false;
            homeName = ConversationBuildingName(homeKey);
            // Residence summaries are authoritative even when the native TalkManager
            // has no printable name for the house. The key still identifies an exact
            // building for directional hints and PlayerGPS discovery.
            return true;
        }

        private string ConversationHomeContext(int disposition)
        {
            if (!ConversationHomeRequests.CanShareHome(disposition))
                return "The NPC keeps their home address private. Do not disclose or invent a home location.";
            int settlement, building;
            string name;
            if (!TryConversationHome(out settlement, out building, out name))
                return "The NPC cannot point to a home location. Do not invent one.";
            string town = ConversationSettlementName(settlement);
            if (settlement == GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId && !string.IsNullOrWhiteSpace(name))
                return "Verified home: " + town + ", " + name + ". This is the exact building. Do not add road, gate, or river details.";
            if (settlement == GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId)
                return "Your actual home is an unnamed house in " + town + ". Verified direction from here: " +
                    ConversationHomeDirection(building) + ". If no direction is supplied, do not guess. Do not invent a name or landmark.";
            return string.IsNullOrWhiteSpace(town) ?
                "The NPC has a tracked home outside this settlement, but its location is not available here. Do not invent an address." :
                "The NPC's tracked home is in " + town + ", outside the current settlement. Do not invent a building or route.";
        }

        private bool TryHandleConversationHomeRequest(DaggerfallTalkWindow window, string playerText)
        {
            bool asksHome = ConversationHomeRequests.IsHomeInformationRequest(playerText);
            bool asksHomeMap = ConversationHomeRequests.IsExplicitHomeMapRequest(playerText) ||
                ConversationHomeRequests.IsFollowupHomeMapRequest(playerText, conversationHomeTopicAvailable);
            if (!asksHome && !asksHomeMap) return false;

            conversationBusy = false;
            int disposition = CurrentDialogueDisposition();
            if (!ConversationHomeRequests.CanShareHome(disposition))
            {
                AppendDialoguePair(window, playerText, "I keep my home address private.");
                return true;
            }

            int settlement, building;
            string name;
            if (!TryConversationHome(out settlement, out building, out name))
            {
                conversationHomeTopicAvailable = false;
                AppendDialoguePair(window, playerText, "I'm afraid I can't point you to my home.");
                return true;
            }
            conversationHomeTopicAvailable = true;

            int currentSettlement = GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId;
            if (settlement != currentSettlement || string.IsNullOrWhiteSpace(name))
            {
                if (settlement == currentSettlement && asksHomeMap)
                {
                    if (!conversationMapRequestsEnabled)
                    {
                        AppendDialoguePair(window, playerText, "My house has no sign anyone would know, and I can't mark it on your map.");
                        return true;
                    }
                    string direction = ConversationHomeDirection(building);
                    string marker = ConversationHomeMarkerLabel();
                    var gps=GameManager.Instance.PlayerGPS;
                    gps.DiscoverBuilding(building);
                    PlayerGPS.DiscoveredBuilding discovery;
                    // Preserve quest labels and player-written names. A custom map
                    // note identifies an unnamed home without renaming the building.
                    if(gps.GetDiscoveredBuilding(building,out discovery) && !discovery.isOverrideName &&
                        string.IsNullOrWhiteSpace(discovery.customUserDisplayName))
                        gps.SetDiscoveredBuildingCustomName(building,marker);
                    bool markedUnnamed = GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(building);
                    AppendDialoguePair(window, playerText, markedUnnamed ?
                        (string.IsNullOrWhiteSpace(direction) ? "I've marked my house on your map." : "I've marked my house " + direction + " of here on your map.") :
                        "I couldn't mark my house on your map.");
                    return true;
                }
                if (settlement == currentSettlement && !asksHomeMap)
                {
                    string homeDirection = ConversationHomeDirection(building);
                    string homeTownName = GameManager.Instance.PlayerGPS.CurrentLocation.Name;
                    string townPhrase = string.IsNullOrWhiteSpace(homeTownName) ? " in town" : " in " + homeTownName;
                    AppendDialoguePair(window, playerText, string.IsNullOrWhiteSpace(homeDirection) ?
                        "I live in a house" + townPhrase + "." :
                        "I live in a house " + homeDirection + " of here" + townPhrase + ".");
                    return true;
                }
                string town = ConversationSettlementName(settlement);
                AppendDialoguePair(window, playerText, string.IsNullOrWhiteSpace(town) ?
                    "My home is outside this settlement. I can't point it out from here." :
                    "My home is in " + town + ", but I can't point it out from here.");
                return true;
            }

            if (asksHomeMap)
            {
                if (!conversationMapRequestsEnabled)
                {
                    AppendDialoguePair(window, playerText, "I live at " + name + ", but I can't mark it on your map.");
                    return true;
                }
                GameManager.Instance.PlayerGPS.DiscoverBuilding(building);
                bool marked = GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(building);
                AppendDialoguePair(window, playerText, marked ? "I've marked my home on your map." :
                    "I couldn't mark my home on your map.");
                return true;
            }

            string currentTown = GameManager.Instance.PlayerGPS.CurrentLocation.Name;
            AppendDialoguePair(window, playerText, "I live at " + name +
                (string.IsNullOrWhiteSpace(currentTown) ? "." : " in " + currentTown + "."));
            return true;
        }

        private string ConversationHomeDirection(int buildingKey)
        {
            try { return GameManager.Instance.TalkManager.GetBuildingCompassDirection(buildingKey); }
            catch (Exception) { return string.Empty; }
        }

        private string ConversationHomeMarkerLabel()
        {
            string name = null;
            ResidentActor actor = conversationActor;
            if (actor && actor.Resident != null)
                name = !string.IsNullOrWhiteSpace(actor.Resident.FullName) ? actor.Resident.FullName : actor.Person.NameNPC;
            if (string.IsNullOrWhiteSpace(name))
            {
                NativePerson native = FindConversationNative(GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId);
                if (native != null) name = native.Name;
            }
            return string.IsNullOrWhiteSpace(name) ? "NPC home" : "Home of " + name;
        }
    }
}
