using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Detached native-data acceptance for saved NPC role evidence and player-fear projection.</summary>
    internal static class NpcRoleNativeProbe
    {
        private const int NobilitySocialGroup = 3;
        private const int UnderworldSocialGroup = 4;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static void Verify(WorldStateStore liveStore, ResidentActor actor)
        {
            Require(liveStore != null && actor != null && actor.Resident != null,
                "NPC role probe requires the loaded store and native managed resident.");
            string actorId = actor.Resident.Id;
            WorldState liveSnapshot = liveStore.Snapshot();
            Require(liveSnapshot.Version == WorldState.CurrentVersion, "NPC role probe requires the current WorldState version.");

            int nobilityFactionId = FindNativeFaction(NobilitySocialGroup);
            int underworldFactionId = FindNativeFaction(UnderworldSocialGroup);
            string nobleId = FindNativePerson(liveSnapshot, NobilitySocialGroup) ?? actorId;
            string underworldId = FindNativePerson(liveSnapshot, UnderworldSocialGroup, nobleId) ??
                FindOtherIdentity(liveSnapshot, nobleId);
            Require(!string.IsNullOrWhiteSpace(nobleId) && !string.IsNullOrWhiteSpace(underworldId) && nobleId != underworldId,
                "NPC role probe requires two known native-world identities.");

            string liveBefore = LiveFingerprint(liveStore, nobleId, underworldId);
            NativePerson actorNativeBefore = actor.Resident.NativeIdentity;
            int actorFactionBefore = actorNativeBefore == null ? -1 : actorNativeBefore.FactionId;

            WorldState detached = Clone(liveSnapshot);
            ClearFixtureHistory(detached, nobleId, underworldId);
            var isolated = new WorldStateStore(detached.WorldSeed);
            isolated.Restore(detached);
            BindNativeFaction(isolated, nobleId, nobilityFactionId);
            BindNativeFaction(isolated, underworldId, underworldFactionId);
            Require(HasFaction(isolated, nobleId, nobilityFactionId) && HasFaction(isolated, underworldId, underworldFactionId),
                "Detached native role fixtures did not retain their controlled FactionData bindings.");

            string nonce = Guid.NewGuid().ToString("N");
            double now = DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            CommitFear(isolated, nobleId, NpcDirectedRelationship.PlayerTargetId,
                "npc-role-native-legacy-player-" + nonce, now, 60);
            CommitFear(isolated, nobleId, underworldId,
                "npc-role-native-other-npc-" + nonce, now, 23);
            CommitFear(isolated, underworldId, NpcDirectedRelationship.PlayerTargetId,
                "npc-role-native-underworld-player-" + nonce, now, 17);

            NpcDirectedRelationshipView otherFearBefore = RequiredRelationship(isolated, nobleId, underworldId);
            Require(otherFearBefore.StoredInterpersonalFear == 23 && otherFearBefore.InterpersonalFear == 23,
                "Detached NPC-to-NPC fear fixture did not begin at 23.");

            PeopleOfTheIliacBayMod.RefreshSavedNpcRole(isolated, nobleId);
            PeopleOfTheIliacBayMod.RefreshSavedNpcRole(isolated, underworldId);

            NpcRoleState nobleRole = isolated.ReadNpcRole(nobleId);
            NpcRoleState underworldRole = isolated.ReadNpcRole(underworldId);
            Require(nobleRole != null && nobleRole.SocialRank == NpcSocialRank.Nobility &&
                nobleRole.RankSourceId == RankSource(nobilityFactionId, NobilitySocialGroup) && nobleRole.Revision > 0,
                "RefreshSavedNpcRole did not persist nobility from actual native social group 3.");
            Require(underworldRole != null && underworldRole.SocialRank == NpcSocialRank.Commoner &&
                underworldRole.RankSourceId == RankSource(underworldFactionId, UnderworldSocialGroup) && underworldRole.Revision > 0,
                "RefreshSavedNpcRole did not persist underworld control as commoner from actual native social group 4.");

            NpcDirectedRelationshipView noblePlayerFear = RequiredRelationship(isolated, nobleId,
                NpcDirectedRelationship.PlayerTargetId);
            Require(noblePlayerFear.PlayerFearSuppressed && noblePlayerFear.StoredInterpersonalFear == 60 &&
                noblePlayerFear.InterpersonalFear == 0 && noblePlayerFear.ProjectedInterpersonalFear == 0 &&
                noblePlayerFear.RoleRevision == nobleRole.Revision,
                "Nobility did not preserve stored player fear 60 while suppressing its effective projection.");
            NpcDirectedRelationshipView underworldPlayerFear = RequiredRelationship(isolated, underworldId,
                NpcDirectedRelationship.PlayerTargetId);
            Require(!underworldPlayerFear.PlayerFearSuppressed && underworldPlayerFear.StoredInterpersonalFear == 17 &&
                underworldPlayerFear.InterpersonalFear > 0 && underworldPlayerFear.ProjectedInterpersonalFear > 0,
                "Underworld/commoner control lost positive effective player fear.");
            NpcDirectedRelationshipView otherFearAfter = RequiredRelationship(isolated, nobleId, underworldId);
            Require(RelationshipFingerprint(otherFearAfter) == RelationshipFingerprint(otherFearBefore),
                "Nobility player-fear suppression changed fear directed at another NPC.");

            NpcPresentationState noblePresentation = NpcPresentationState.Capture(isolated, nobleId, now, true);
            Require(noblePresentation != null && noblePresentation.PlayerFearKnown && noblePresentation.PlayerFear == 0,
                "NPC presentation exposed stored nobility player fear.");

            WorldState beforeRoundTrip = isolated.Snapshot();
            WorldState roundTrip = Clone(beforeRoundTrip);
            Require(roundTrip.Version == beforeRoundTrip.Version, "FullSerializer changed the role probe WorldState version.");
            var reloaded = new WorldStateStore(roundTrip.WorldSeed);
            reloaded.Restore(roundTrip);
            NpcRoleState reloadedNobleRole = reloaded.ReadNpcRole(nobleId);
            NpcRoleState reloadedUnderworldRole = reloaded.ReadNpcRole(underworldId);
            Require(RoleFingerprint(reloadedNobleRole) == RoleFingerprint(nobleRole) &&
                RoleFingerprint(reloadedUnderworldRole) == RoleFingerprint(underworldRole),
                "FullSerializer changed saved native role facts, sources, or revisions.");
            NpcDirectedRelationshipView reloadedNobleFear = RequiredRelationship(reloaded, nobleId,
                NpcDirectedRelationship.PlayerTargetId);
            NpcDirectedRelationshipView reloadedUnderworldFear = RequiredRelationship(reloaded, underworldId,
                NpcDirectedRelationship.PlayerTargetId);
            Require(reloadedNobleFear.StoredInterpersonalFear == 60 && reloadedNobleFear.InterpersonalFear == 0 &&
                reloadedNobleFear.RoleRevision == reloadedNobleRole.Revision &&
                reloadedUnderworldFear.StoredInterpersonalFear == 17 && reloadedUnderworldFear.InterpersonalFear > 0,
                "FullSerializer changed raw player fear or its role-aware projection.");
            NpcDirectedRelationshipView reloadedOtherFear = RequiredRelationship(reloaded, nobleId, underworldId);
            Require(RelationshipFingerprint(reloadedOtherFear) == RelationshipFingerprint(otherFearBefore),
                "FullSerializer changed NPC-to-NPC fear while saving role evidence.");
            NpcPresentationState reloadedPresentation = NpcPresentationState.Capture(reloaded, nobleId, now, true);
            Require(reloadedPresentation != null && reloadedPresentation.PlayerFear == 0,
                "FullSerializer round-trip re-exposed nobility player fear in presentation.");

            VerifyConversationRoleSnapshotEquality(reloaded, nobilityFactionId, underworldFactionId);
            Require(LiveFingerprint(liveStore, nobleId, underworldId) == liveBefore &&
                ReferenceEquals(actorNativeBefore, actor.Resident.NativeIdentity) &&
                actorFactionBefore == (actor.Resident.NativeIdentity == null ? -1 : actor.Resident.NativeIdentity.FactionId),
                "Detached NPC role probe changed the live NPC identity, role, or relationship data.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ROLE_NATIVE_PASS: nobleFaction=" + nobilityFactionId +
                "; nobleSocialGroup=3; storedPlayerFear=60; effectivePlayerFear=0; npcTargetFearUnchanged=true" +
                "; underworldFaction=" + underworldFactionId + "; underworldSocialGroup=4; controlPlayerFear=" +
                underworldPlayerFear.InterpersonalFear.ToString(CultureInfo.InvariantCulture) +
                "; fullSerializerVersion=" + roundTrip.Version + "; roleSourcesAndRevisionsStable=true; rawFearStable=true" +
                "; presentationPlayerFear=0; sameConversationRoleValues=true; changedRankAndSourceRejected=true" +
                "; detachedStaticRoleCapture=true; nativePeopleRegistration=false; liveNpcAndStoreUnchanged=true" +
                "; palacePlacement=not-tested; nativeCombatBinding=not-tested.");
        }

        private static void VerifyConversationRoleSnapshotEquality(WorldStateStore owner,
            int nobilityFactionId, int underworldFactionId)
        {
            var game = GameManager.Instance;
            Require(game != null && game.PlayerGPS != null,
                "Native role probe requires current GPS context for a detached StaticNPC carrier.");
            var data = new StaticNPC.NPCData
            {
                mapID = game.PlayerGPS.CurrentMapID,
                buildingKey = -1,
                factionID = nobilityFactionId,
                context = StaticNPC.Context.Custom
            };
            WorldState before = owner.Snapshot();
            string personId = null;
            bool unique = false;
            for (int hash = 100001; hash < 110001; hash++)
            {
                data.hash = hash;
                personId = CanonicalStaticNpcId(data);
                bool nativeIdentityExists = before.NativePeople.Exists(item => item != null && item.Id == personId);
                if (!nativeIdentityExists && owner.ReadNpcRole(personId) == null)
                { unique = true; break; }
            }
            Require(unique, "Could not create an unused canonical StaticNPC identity for detached role capture.");

            var carrierObject = new GameObject("Detached native NPC role probe carrier");
            try
            {
                carrierObject.SetActive(false);
                StaticNPC carrier = carrierObject.AddComponent<StaticNPC>();
                FieldInfo npcData = typeof(StaticNPC).GetField("npcData", PrivateInstance);
                Require(npcData != null, "StaticNPC native data field is unavailable for detached role capture.");
                npcData.SetValue(carrier, data);
                int nativePeopleBefore = before.NativePeople.Count;
                NpcRoleState captured = PeopleOfTheIliacBayMod.CaptureConversationRole(owner, personId, carrier);
                Require(captured != null && captured.PersonId == personId &&
                    captured.SocialRank == NpcSocialRank.Nobility &&
                    captured.RankSourceId == RankSource(nobilityFactionId, NobilitySocialGroup),
                    "Detached StaticNPC did not provide canonical nobility evidence from actual FactionData.");
                Require(owner.ReadNpcRole(personId) == null && owner.Snapshot().NativePeople.Count == nativePeopleBefore &&
                    !owner.Snapshot().NativePeople.Exists(item => item != null && item.Id == personId),
                    "Ephemeral StaticNPC role capture registered native identity or saved role state.");

                NpcRoleState sameValues = NpcRoleState.CopyValidated(captured);
                Require(PeopleOfTheIliacBayMod.SameConversationRole(captured, sameValues),
                    "SameConversationRole rejected identical role values and revision.");
                NpcRoleState changedRank = NpcRoleState.CopyValidated(captured);
                changedRank.SocialRank = NpcSocialRank.Commoner;
                changedRank.RankSourceId = RankSource(underworldFactionId, UnderworldSocialGroup);
                Require(changedRank.Revision == captured.Revision &&
                    !PeopleOfTheIliacBayMod.SameConversationRole(captured, changedRank),
                    "SameConversationRole accepted changed rank/source at the same revision.");
                NpcRoleState changedSource = NpcRoleState.CopyValidated(captured);
                changedSource.RankSourceId += ":changed";
                Require(changedSource.Revision == captured.Revision &&
                    !PeopleOfTheIliacBayMod.SameConversationRole(captured, changedSource),
                    "SameConversationRole accepted changed source at the same revision.");
            }
            finally { UnityEngine.Object.DestroyImmediate(carrierObject); }
        }

        private static int FindNativeFaction(int socialGroup)
        {
            var game = GameManager.Instance;
            Require(game != null && game.PlayerEntity != null && game.PlayerEntity.FactionData != null,
                "Active native FactionData is unavailable to NPC role probe.");
            foreach (KeyValuePair<int, FactionFile.FactionData> item in game.PlayerEntity.FactionData.FactionDict)
                if (item.Key > 0 && item.Value.sgroup == socialGroup &&
                    item.Value.type != (int)FactionFile.FactionTypes.Individual &&
                    !FactionFile.CustomFactions.ContainsKey(item.Key))
                    return item.Key;
            throw new InvalidOperationException("Active native FactionData has no non-Individual faction for social group " +
                socialGroup.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static string FindNativePerson(WorldState world, int socialGroup, string excludedId = null)
        {
            var factions = GameManager.Instance.PlayerEntity.FactionData;
            foreach (NativePerson person in world.NativePeople)
            {
                if (person == null || !person.IsAlive || person.Id == excludedId || person.FactionId <= 0) continue;
                FactionFile.FactionData faction;
                if (factions.GetFactionData(person.FactionId, out faction) && faction.sgroup == socialGroup &&
                    faction.type != (int)FactionFile.FactionTypes.Individual &&
                    !FactionFile.CustomFactions.ContainsKey(person.FactionId))
                    return person.Id;
            }
            return null;
        }

        private static string FindOtherIdentity(WorldState world, string excludedId)
        {
            foreach (NativePerson person in world.NativePeople)
                if (person != null && person.IsAlive && person.Id != excludedId) return person.Id;
            foreach (SettlementPopulation settlement in world.Settlements)
                foreach (Household household in settlement.Households)
                    foreach (Resident resident in household.Residents)
                        if (resident != null && resident.IsAlive && resident.Id != excludedId) return resident.Id;
            return null;
        }

        private static void BindNativeFaction(WorldStateStore store, string personId, int factionId)
        {
            Resident resident = store.ResolveResident(personId);
            if (resident != null)
            {
                if (resident.NativeIdentity == null)
                    resident.NativeIdentity = new NativePerson { Id = personId, FactionId = factionId };
                else
                    resident.NativeIdentity.FactionId = factionId;
                return;
            }

            NativePerson saved = null;
            foreach (NativePerson person in store.Snapshot().NativePeople)
                if (person != null && person.Id == personId) { saved = person; break; }
            Require(saved != null, "Detached role fixture identity is not a saved resident or native person.");
            NativePerson current = store.NativePersonInSettlement(saved.SettlementId, personId);
            Require(current != null, "Detached role fixture native record disappeared after restore.");
            current.FactionId = factionId;
        }

        private static bool HasFaction(WorldStateStore store, string personId, int expectedFactionId)
        {
            int actualFactionId;
            return store.TryReadNpcPersonalityFaction(personId, out actualFactionId) &&
                actualFactionId == expectedFactionId;
        }

        private static void ClearFixtureHistory(WorldState state, string nobleId, string underworldId)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal) { nobleId, underworldId };
            state.NpcRoles.RemoveAll(role => role != null && identities.Contains(role.PersonId));
            state.NpcRelationships.Relationships.RemoveAll(row => row != null &&
                (identities.Contains(row.ObserverId) || identities.Contains(row.TargetId)));
            state.NpcRelationships.AcceptedOutcomes.RemoveAll(outcome => outcome != null &&
                (identities.Contains(outcome.ObserverId) || identities.Contains(outcome.TargetId)));
            state.NpcRelationships.Revision = state.NpcRelationships.AcceptedOutcomes.Count;
        }

        private static void CommitFear(WorldStateStore store, string observerId, string targetId,
            string prefix, double now, int amount)
        {
            int remaining = amount;
            int index = 0;
            while (remaining > 0)
            {
                int delta = Math.Min(NpcDirectedRelationship.MaximumUpdateDelta, remaining);
                string outcomeId = prefix + ":" + index.ToString(CultureInfo.InvariantCulture);
                NpcExperienceEvent evidence = NpcExperienceEvent.FromNativeOutcome(observerId, observerId,
                    prefix + ":receipt:" + index.ToString(CultureInfo.InvariantCulture), prefix + ":episode",
                    outcomeId, NpcExperienceEventKind.Injury, now,
                    NpcResponsibleActorAttribution.Known, targetId);
                NpcDirectedRelationshipView current = store.ReadNpcRelationship(observerId, targetId);
                var proposal = new NpcRelationshipLedgerUpdateProposal
                {
                    ExpectedLedgerRevision = store.NpcRelationshipLedgerRevision,
                    ObserverId = observerId,
                    TargetId = targetId,
                    RelationshipUpdate = new NpcRelationshipUpdateProposal
                    {
                        ExpectedRevision = current == null ? 0 : current.Revision,
                        Evidence = evidence,
                        InterpersonalFearDelta = delta
                    }
                };
                Require(store.TryCommitNpcRelationship(proposal, proposal.ExpectedLedgerRevision),
                    "Detached native role probe could not save pre-role fear evidence.");
                remaining -= delta;
                index++;
            }
        }

        private static string RankSource(int factionId, int socialGroup)
        {
            return "native-faction-social-group:" + factionId.ToString(CultureInfo.InvariantCulture) + ":" +
                socialGroup.ToString(CultureInfo.InvariantCulture);
        }

        private static string CanonicalStaticNpcId(StaticNPC.NPCData data)
        {
            return "npc:" + data.mapID.ToString(CultureInfo.InvariantCulture) + ":" +
                data.buildingKey.ToString(CultureInfo.InvariantCulture) + ":" + data.hash.ToString(CultureInfo.InvariantCulture);
        }

        private static NpcDirectedRelationshipView RequiredRelationship(WorldStateStore store, string observerId,
            string targetId)
        {
            NpcDirectedRelationshipView relationship = store.ReadNpcRelationship(observerId, targetId);
            Require(relationship != null, "Detached native role probe is missing relationship " + observerId + " -> " + targetId + ".");
            return relationship;
        }

        private static string LiveFingerprint(WorldStateStore store, string nobleId, string underworldId)
        {
            return RoleFingerprint(store.ReadNpcRole(nobleId)) + "|" +
                RoleFingerprint(store.ReadNpcRole(underworldId)) + "|" +
                RelationshipFingerprint(store.ReadNpcRelationship(nobleId, NpcDirectedRelationship.PlayerTargetId)) + "|" +
                RelationshipFingerprint(store.ReadNpcRelationship(nobleId, underworldId)) + "|" +
                RelationshipFingerprint(store.ReadNpcRelationship(underworldId, NpcDirectedRelationship.PlayerTargetId));
        }

        private static string RoleFingerprint(NpcRoleState role)
        {
            if (role == null) return "<absent>";
            return role.PersonId + "|" + role.SocialRank + "|" + role.CombatRole + "|" + role.ServiceBinding + "|" +
                role.RankSourceId + "|" + role.CombatSourceId + "|" + role.ServiceSourceId + "|" +
                role.Revision.ToString(CultureInfo.InvariantCulture);
        }

        private static string RelationshipFingerprint(NpcDirectedRelationshipView relationship)
        {
            if (relationship == null) return "<absent>";
            return string.Join("|", relationship.ObserverId, relationship.TargetId,
                relationship.Revision.ToString(CultureInfo.InvariantCulture),
                relationship.LastGameTime.ToString("R", CultureInfo.InvariantCulture),
                relationship.StoredInterpersonalFear.ToString(CultureInfo.InvariantCulture),
                relationship.InterpersonalFear.ToString(CultureInfo.InvariantCulture),
                relationship.ProjectedInterpersonalFear.ToString(CultureInfo.InvariantCulture),
                relationship.PlayerFearSuppressed.ToString(CultureInfo.InvariantCulture),
                relationship.RoleRevision.ToString(CultureInfo.InvariantCulture));
        }

        private static WorldState Clone(WorldState source)
        {
            string serialized = SaveLoadManager.Serialize(typeof(WorldState), source);
            Require(!string.IsNullOrEmpty(serialized), "FullSerializer returned empty detached NPC role state.");
            WorldState clone = SaveLoadManager.Deserialize(typeof(WorldState), serialized, true) as WorldState;
            Require(clone != null, "FullSerializer did not restore detached NPC role state.");
            return clone;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
