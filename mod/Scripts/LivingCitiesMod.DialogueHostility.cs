using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class LivingCitiesMod
    {
        private bool IsConversationAtOwnedPlace(out string place)
        {
            place = null;
            var game = GameManager.Instance;
            int building, settlement;
            BuildingSummary summary;
            if (!TryConversationBuilding(out settlement, out building, out summary)) return false;

            // The public floor belongs only to the exact working keeper. A resident
            // visiting, lodging, or working a different shift there is not its owner.
            if (summary.BuildingType == DFLocation.BuildingTypes.Tavern || RMBLayout.IsShop(summary.BuildingType))
            {
                NativePerson keeper = FindConversationNative(settlement);
                if (keeper == null || !keeper.IsAlive || keeper.WorkKey != building) return false;
                place = summary.BuildingType == DFLocation.BuildingTypes.Tavern ? "the innkeeper's inn" : "the shopkeeper's shop";
                return true;
            }

            NativePerson native = FindConversationNative(settlement);
            if (native != null && native.IsAlive && native.HomeKey == building)
            {
                place = "the NPC's home";
                return true;
            }
            var resident = conversationActor && conversationActor.Resident != null ? conversationActor.Resident : null;
            if (resident != null && resident.IsAlive && resident.HomeKey == building && resident.Id == conversationPersonId)
            {
                place = "the NPC's home";
                return true;
            }
            return false;
        }

        private string ConversationPlaceContext()
        {
            string owned;
            if (IsConversationAtOwnedPlace(out owned)) return "You are in " + owned + ".";
            if (!GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding)
                return "You are outdoors in a public area.";
            int settlement, building;
            BuildingSummary summary;
            if (!TryConversationBuilding(out settlement, out building, out summary))
                return "You are inside a building; its ownership context is unavailable.";
            if (summary.BuildingType == DFLocation.BuildingTypes.Tavern)
                return "You are on a public inn floor; only its actual keeper is treated as the owner.";
            if (RMBLayout.IsShop(summary.BuildingType))
                return "You are on a public shop floor; only its actual keeper is treated as the owner.";
            return "You are inside another building, not the NPC's own home or business.";
        }

        private bool TryConversationBuilding(out int settlement, out int building, out BuildingSummary summary)
        {
            settlement = -1; building = -1; summary = default(BuildingSummary);
            var game = GameManager.Instance;
            if (game == null || !game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                !game.PlayerGPS.HasCurrentLocation || current == null) return false;
            building = game.PlayerEnterExit.Interior.EntryDoor.buildingKey;
            settlement = game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            if (current.SettlementId != settlement) return false;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            return directory && directory.GetBuildingSummary(building, out summary);
        }

        private NativePerson FindConversationNative(int settlement)
        {
            if (string.IsNullOrEmpty(conversationPersonId)) return null;
            return store.NativePeopleFor(settlement).Find(person => person.Id == conversationPersonId);
        }

        private bool HandleConversationHostility(DaggerfallTalkWindow window, string playerText,
            string effectiveTone, bool isFactualRequest)
        {
            int disposition = conversationDispositionBeforeMessage <= -100 ? -100 : CurrentDialogueDisposition();
            string place;
            bool ownPlace = IsConversationAtOwnedPlace(out place);
            ConversationHostilityAction action = ConversationHostility.Decide(disposition, ownPlace, isFactualRequest);
            if (action == ConversationHostilityAction.None) return false;

            conversationBusy = false;
            if (action == ConversationHostilityAction.EndAndReport)
            {
                var game = GameManager.Instance;
                bool alerted = false;
                if (game.PlayerEnterExit.IsPlayerInsideBuilding)
                    alerted = ReportInsideDialogueTrespassing();
                else
                {
                    var streets = GetComponent<StreetPopulation>();
                    alerted = streets && conversationActor &&
                        streets.StartDialogueComplaint(conversationActor, conversationPersonId);
                }
                string terminal = alerted ? game.PlayerEnterExit.IsPlayerInsideBuilding ?
                    "Leave now. The guards have been alerted." : "Leave now. They are running to fetch a guard." :
                    "Leave now.";
                AppendDialoguePair(window, playerText, terminal);
                DaggerfallUI.Instance.PopToHUD();
                DaggerfallUI.AddHUDText(terminal);
                return true;
            }

            string addressedPlace = ConversationHostility.FirstPersonPlace(place);
            string response = effectiveTone == "apology" ? "I accept your apology, but you still need to leave " + addressedPlace + "." :
                action == ConversationHostilityAction.RefuseAndWarn ?
                "I will not help you. Leave " + addressedPlace + "." :
                "You are not welcome here. Leave " + addressedPlace + ".";
            AppendDialoguePair(window, playerText, response);
            return true;
        }

        private bool ReportInsideDialogueTrespassing()
        {
            var player = GameManager.Instance.PlayerEntity;
            if (player.CrimeCommitted == PlayerEntity.Crimes.None)
                player.CrimeCommitted = PlayerEntity.Crimes.Trespassing;
            var guardPopulation = GetComponent<GuardPopulation>();
            if (!guardPopulation || !guardPopulation.HasAvailableDialogueGuards()) return false;
            guardPopulation.ReportDialogueOffense();
            return true;
        }

        private string NativeConversationAnswerOverride()
        {
            if (!conversationSessionOpen || !conversationDispositionEnabled || saveFault) return null;
            int disposition = CurrentDialogueDisposition();
            if (disposition > -50) return null;
            string place;
            if (disposition <= -100)
            {
                var game = GameManager.Instance;
                bool alerted = false;
                if (game.PlayerEnterExit.IsPlayerInsideBuilding) alerted = ReportInsideDialogueTrespassing();
                else
                {
                    var streets = GetComponent<StreetPopulation>();
                    if (streets && conversationActor)
                        alerted = streets.StartDialogueComplaint(conversationActor, conversationPersonId);
                }
                string response = alerted ? game.PlayerEnterExit.IsPlayerInsideBuilding ?
                    "Leave now. The guards have been alerted." : "Leave now. They are running to fetch a guard." :
                    "Leave now.";
                DaggerfallUI.Instance.PopToHUD();
                DaggerfallUI.AddHUDText(response);
                return response;
            }
            if (!IsConversationAtOwnedPlace(out place)) return null;
            return "I will not provide further help. Please leave " + ConversationHostility.FirstPersonPlace(place) + ".";
        }
    }
}
