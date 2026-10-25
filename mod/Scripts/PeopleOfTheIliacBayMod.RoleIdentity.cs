using System;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        internal static void RefreshSavedNpcRole(WorldStateStore owner, string personId)
        {
            if (owner == null || string.IsNullOrEmpty(personId)) return;
            int factionId;
            if (owner.TryReadNpcPersonalityFaction(personId, out factionId))
                CaptureNpcFactionRank(owner, personId, factionId);
            var archetype = owner.ResolveNpcArchetype(personId,NpcArchetype.Citizen);
            var existingRole = owner.ReadNpcRole(personId);
            // A generic saved archetype must not downgrade a more specific native duty.
            if (existingRole != null && existingRole.CombatRole != NpcCombatRole.Unknown &&
                existingRole.CombatSourceId != null &&
                !existingRole.CombatSourceId.StartsWith("saved-archetype:", StringComparison.Ordinal)) return;
            NpcCombatRole role = NpcCombatRole.Unknown;
            if (archetype == NpcArchetype.Guard) role = NpcCombatRole.Guard;
            else if (archetype == NpcArchetype.Warrior) role = NpcCombatRole.Warrior;
            else if (archetype == NpcArchetype.Thief) role = NpcCombatRole.Rogue;
            else if (archetype == NpcArchetype.Mage) role = NpcCombatRole.Combatant;
            if (role != NpcCombatRole.Unknown)
            {
                bool independent=role!=NpcCombatRole.Guard &&
                    (existingRole==null || existingRole.ServiceBinding==NpcServiceBinding.Unknown);
                string source="saved-archetype:"+archetype;
                owner.RecordNpcRoleEvidence(personId,NpcSocialRank.Unknown,role,
                    independent?NpcServiceBinding.Independent:NpcServiceBinding.Unknown,
                    null,source,independent?source:null);
            }
        }

        internal static void RefreshNativeNpcRole(WorldStateStore owner, string personId, UnityEngine.Object nativeObject)
        {
            RefreshSavedNpcRole(owner, personId);
            var component = nativeObject as Component;
            if (owner == null || string.IsNullOrEmpty(personId) || !component) return;
            var stationary = component as StaticNPC;
            if (stationary)
            {
                CaptureNpcFactionRank(owner, personId, stationary.Data.factionID);
                if (IsPalaceStationed(stationary))
                {
                    string source = "native-palace-layout:" + stationary.Data.mapID + ":" + stationary.Data.buildingKey;
                    var known = owner.ReadNpcRole(personId);
                    bool guardDuty = known != null && (known.CombatRole == NpcCombatRole.Guard ||
                        known.CombatRole == NpcCombatRole.PalaceGuard);
                    owner.RecordNpcRoleEvidence(personId, NpcSocialRank.Unknown,
                        guardDuty ? NpcCombatRole.PalaceGuard : NpcCombatRole.Unknown,
                        NpcServiceBinding.NobleKingdom, null, guardDuty ? source : null, source);
                }
            }
            var guard = component.GetComponent<GuardActor>();
            if (guard && guard.GuardId == personId && !(stationary && IsPalaceStationed(stationary)))
                owner.RecordNpcRoleEvidence(personId, NpcSocialRank.Unknown, NpcCombatRole.Guard,
                    NpcServiceBinding.NobleKingdom, null, "native-guard-duty:" + personId,
                    "native-guard-duty:" + personId);
        }

        private static void CaptureNpcFactionRank(WorldStateStore owner, string personId, int factionId)
        {
            string source;
            var rank = ReadNativeFactionRank(factionId, out source);
            if (rank != NpcSocialRank.Unknown)
                owner.RecordNpcRoleEvidence(personId, rank, NpcCombatRole.Unknown, NpcServiceBinding.Unknown,
                    source, null, null);
        }

        private static NpcSocialRank ReadNativeFactionRank(int factionId, out string source)
        {
            source = null;
            var game = GameManager.Instance;
            FactionFile.FactionData faction;
            if (factionId <= 0 || game == null || game.PlayerEntity == null ||
                game.PlayerEntity.FactionData == null || !game.PlayerEntity.FactionData.GetFactionData(factionId, out faction)) return NpcSocialRank.Unknown;
            bool royal = StaticNpcRoleProjection.Resolve(new NativeStaticRoleFacts(true,
                FactionFile.CustomFactions.ContainsKey(factionId), factionId,
                faction.type == (int)FactionFile.FactionTypes.Individual,
                faction.sgroup == (int)FactionFile.SocialGroups.Nobility, false, faction.face,
                -1, -1, false, false)) == ConversationDialogueRole.Royalty;
            var rank = NpcNativeRoleProjection.SocialRank(true, faction.sgroup, royal);
            if (rank != NpcSocialRank.Unknown)
                source = "native-faction-social-group:" + factionId + ":" + faction.sgroup;
            return rank;
        }

        internal static NpcRoleState CaptureConversationRole(WorldStateStore owner, string personId, UnityEngine.Object nativeObject)
        {
            if (owner == null || string.IsNullOrEmpty(personId)) return null;
            var saved = owner.ReadNpcRole(personId);
            var npc = nativeObject as StaticNPC;
            if (!npc) return saved;
            string nativeId = StaticConversationPersonId(npc);
            if (!string.Equals(personId, nativeId, StringComparison.Ordinal)) return null;
            string rankSource;
            var rank = ReadNativeFactionRank(npc.Data.factionID, out rankSource);
            bool palace = IsPalaceStationed(npc);
            if (saved != null)
            {
                // Revalidate live faction authority even before another persistence refresh.
                // This detached overlay also rejects stale replies when a native faction changes.
                saved.SocialRank = rank;
                saved.RankSourceId = rankSource;
                return rank != NpcSocialRank.Unknown || saved.CombatRole != NpcCombatRole.Unknown ||
                    saved.ServiceBinding != NpcServiceBinding.Unknown ? saved : null;
            }
            if (rank == NpcSocialRank.Unknown && !palace) return null;
            // Story actors deliberately remain outside NativePeople's civilian simulation.
            // The conversation binds this ephemeral evidence to the exact native speaker.
            return new NpcRoleState
            {
                PersonId = personId, SocialRank = rank, RankSourceId = rankSource,
                ServiceBinding = palace ? NpcServiceBinding.NobleKingdom : NpcServiceBinding.Unknown,
                ServiceSourceId = palace ? "native-palace-layout:" + npc.Data.mapID + ":" + npc.Data.buildingKey : null,
                Revision = 1
            };
        }

        internal static bool SameConversationRole(NpcRoleState captured, NpcRoleState current)
        {
            if (captured == null || current == null) return captured == null && current == null;
            return captured.PersonId == current.PersonId && captured.Revision == current.Revision &&
                captured.SocialRank == current.SocialRank && captured.CombatRole == current.CombatRole &&
                captured.ServiceBinding == current.ServiceBinding && captured.RankSourceId == current.RankSourceId &&
                captured.CombatSourceId == current.CombatSourceId && captured.ServiceSourceId == current.ServiceSourceId;
        }

        internal static bool IsPalaceStationed(StaticNPC npc)
        {
            string castleKey;
            if(TryNativeCastleLayoutKey(npc,out castleKey))return true;
            var game = GameManager.Instance;
            if (!npc || npc.Data.context != StaticNPC.Context.Building || game == null ||
                !game.PlayerGPS.HasCurrentLocation || !game.PlayerEnterExit.IsPlayerInsideBuilding ||
                game.PlayerGPS.CurrentLocation.MapTableData.MapId != npc.Data.mapID) return false;
            var interior = npc.GetComponentInParent<DaggerfallWorkshop.DaggerfallInterior>();
            if (!interior || interior.EntryDoor.buildingKey != npc.Data.buildingKey) return false;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            BuildingSummary summary;
            return directory && directory.GetBuildingSummary(npc.Data.buildingKey, out summary) &&
                summary.BuildingType == DFLocation.BuildingTypes.Palace;
        }
    }
}
