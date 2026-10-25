using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private static void RefreshRandomPersonalityEligibility(WorldStateStore activeStore, string personId,
            UnityEngine.Object speakerObject)
        {
            RefreshNativeNpcRole(activeStore, personId, speakerObject);
            var component = speakerObject as Component;
            if (activeStore == null || string.IsNullOrEmpty(personId) || !component) return;
            var resident = component.GetComponent<ResidentActor>();
            var stationary = component as StaticNPC;
            bool protectedResident = resident && resident.Resident != null && resident.Resident.IsQuestProtected;
            bool questResource = component.GetComponent<QuestResourceBehaviour>();
            int factionId = stationary ? stationary.Data.factionID : resident && resident.Resident != null &&
                resident.Resident.NativeIdentity != null ? resident.Resident.NativeIdentity.FactionId : -1;
            bool authored = IsAuthoredNativeIndividual(factionId);
            NpcSocialBackground ignored;
            bool resolved = TryResolveNpcPersonalityBackground(factionId, out ignored);
            activeStore.SetNpcRandomPersonalitySuppressed(personId,
                !resolved || !NpcPersonalityPolicy.CanGenerateRandom(protectedResident, questResource, authored));
        }

        /// <summary>
        /// Rechecks saved native identity even when a random profile already exists.
        /// Generic sidequest participation never suppresses the profile.
        /// </summary>
        internal static bool RefreshSavedRandomPersonalityEligibility(WorldStateStore activeStore, string personId,
            out NpcSocialBackground background)
        {
            RefreshSavedNpcRole(activeStore, personId);
            background = NpcSocialBackground.Unknown;
            int factionId;
            if (activeStore == null || !activeStore.TryReadNpcPersonalityFaction(personId, out factionId)) return false;
            bool eligible = TryResolveNpcPersonalityBackground(factionId, out background);
            activeStore.SetNpcRandomPersonalitySuppressed(personId, !eligible);
            return eligible;
        }

        /// <summary>
        /// Resolves a native faction to its social background for stable profile generation.
        /// False means the faction is unavailable or a known authored Individual; callers must defer random creation.
        /// </summary>
        internal static bool TryResolveNpcPersonalityBackground(int factionId, out NpcSocialBackground background)
        {
            background = NpcSocialBackground.Unknown;
            // DFU uses zero as its native no-faction sentinel. Positive IDs still
            // require an actual lookup; unavailable faction data must not become nobility.
            if (factionId <= 0) return true;

            var game = GameManager.Instance;
            if (game == null || game.PlayerEntity == null || game.PlayerEntity.FactionData == null)
                return false;

            DaggerfallConnect.Arena2.FactionFile.FactionData faction;
            if (!game.PlayerEntity.FactionData.GetFactionData(factionId, out faction))
                return false;

            // Named individual factions can be authored story actors even without an active quest component.
            if (faction.type == (int)DaggerfallConnect.Arena2.FactionFile.FactionTypes.Individual)
                return false;

            background = NpcPersonalityPolicy.BackgroundForNativeSocialGroup(faction.sgroup);
            return true;
        }

        internal static bool IsAuthoredNativeIndividual(int factionId)
        {
            var game = GameManager.Instance;
            DaggerfallConnect.Arena2.FactionFile.FactionData faction;
            return factionId > 0 && game != null && game.PlayerEntity != null &&
                game.PlayerEntity.FactionData != null && game.PlayerEntity.FactionData.GetFactionData(factionId, out faction) &&
                faction.type == (int)DaggerfallConnect.Arena2.FactionFile.FactionTypes.Individual;
        }
    }
}
