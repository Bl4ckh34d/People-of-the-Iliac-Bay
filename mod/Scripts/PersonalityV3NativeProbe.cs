using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Detached native-data acceptance for V3 profile eligibility and save stability.</summary>
    internal static class PersonalityV3NativeProbe
    {
        internal static void Verify(WorldStateStore liveStore, ResidentActor actor)
        {
            Require(liveStore != null && actor != null && actor.Resident != null,
                "V3 personality probe requires the loaded store and a native managed resident.");
            string personId = actor.Resident.Id;
            double now = DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            uint seed;
            int nativeFaction;
            bool questProtected;
            Require(liveStore.TryReadNpcPersonalityIdentity(personId, out seed, out nativeFaction, out questProtected),
                "Selected native resident has no saved stable personality identity.");

            NpcSocialBackground ordinaryBackground;
            Require(PeopleOfTheIliacBayMod.TryResolveNpcPersonalityBackground(nativeFaction, out ordinaryBackground),
                "Selected resident faction did not resolve through native FactionData.");
            if (nativeFaction > 0)
            {
                FactionFile.FactionData actualFaction;
                Require(GameManager.Instance.PlayerEntity.FactionData.GetFactionData(nativeFaction, out actualFaction) &&
                    actualFaction.type != (int)FactionFile.FactionTypes.Individual,
                    "The ordinary-person fixture unexpectedly resolves to an authored Individual faction.");
            }

            string liveProfileBefore = ProfileFingerprint(liveStore.ReadNpcPersonality(personId));
            WorldState detached = Clone(liveStore.Snapshot());
            detached.NpcPersonalities.RemoveAll(profile => profile != null && profile.PersonId == personId);
            // The detached eligibility fixture must not mint an unauthorised
            // current-schema injury to make cleanup nonempty. Reset only detail
            // already present in the owned snapshot, preserving absolute v5 ordinals.
            int removed = detached.NpcExperienceInbox.Entries.RemoveAll(entry => entry.ObserverId == personId);
            if (removed > 0)
            {
                detached.NpcExperienceInbox.RetiredCount += removed;
                detached.NpcExperienceInbox.DetailRevision++;
            }
            detached.NpcRelationships = NpcRelationshipLedger.Create();
            var relationship = NpcDirectedRelationship.Create(personId, NpcDirectedRelationship.PlayerTargetId);
            relationship.Resentment = 27;
            relationship.InterpersonalFear = 13;
            detached.NpcRelationships.Relationships.Add(relationship);

            var isolated = new WorldStateStore(detached.WorldSeed);
            isolated.Restore(detached);
            Resident isolatedResident = isolated.ResolveResident(personId);
            Require(isolatedResident != null,
                "FullSerializer clone lost the controlled native resident identity.");

            // Persist exactly the deterministic profile that ordinary native presentation captures.
            NpcPersonalityProfile ordinary = isolated.GetOrCreateNpcPersonality(personId, seed, ordinaryBackground);
            string traits = ProfileFingerprint(ordinary);
            Require(ordinary.Version == 3 && ordinary.SelectedTraits.Count == 3,
                "Ordinary native identity did not save exactly three V3 traits.");
            var firstSave = Clone(isolated.Snapshot());
            var reloaded = new WorldStateStore(firstSave.WorldSeed);
            reloaded.Restore(firstSave);
            NpcPersonalityProfile afterReload = reloaded.ReadNpcPersonality(personId);
            Require(ProfileFingerprint(afterReload) == traits && afterReload.Version == 3 &&
                afterReload.SelectedTraits.Count == 3,
                "FullSerializer round-trip changed or expanded the three saved traits.");
            isolated = reloaded;
            isolatedResident = isolated.ResolveResident(personId);
            // Generated residents legitimately have no native-person record. Give only
            // this detached fixture a faction binding for the eligibility transitions.
            if (isolatedResident.NativeIdentity == null)
                isolatedResident.NativeIdentity = new NativePerson { Id = personId, FactionId = nativeFaction };

            // A generic quest-protected resident keeps both random traits and its directed relationship.
            NpcDirectedRelationshipView relationshipBefore = isolated.ReadNpcRelationship(personId,
                NpcDirectedRelationship.PlayerTargetId);
            string relationshipFingerprint = RelationshipFingerprint(relationshipBefore);
            isolatedResident.IsQuestProtected = true;
            NpcSocialBackground questBackground;
            bool questEligible = PeopleOfTheIliacBayMod.RefreshSavedRandomPersonalityEligibility(
                isolated, personId, out questBackground);
            NpcPersonalityProfile genericQuestProfile = isolated.ReadNpcPersonality(personId);
            NpcDirectedRelationshipView relationshipAfter = isolated.ReadNpcRelationship(personId,
                NpcDirectedRelationship.PlayerTargetId);
            Require(questEligible && ProfileFingerprint(genericQuestProfile) == traits &&
                genericQuestProfile.SelectedTraits.Count == 3 &&
                RelationshipFingerprint(relationshipAfter) == relationshipFingerprint,
                "Generic IsQuestProtected participation suppressed traits or changed the existing relationship.");
            Require(NpcPersonalityPolicy.CanGenerateRandom(true, true, false),
                "Generic IsQuestProtected/QuestResource flags must not suppress random personality.");
            isolatedResident.IsQuestProtected = false;

            int individualFactionId = FindNativeIndividualFaction();
            int ordinaryFactionId = isolatedResident.NativeIdentity.FactionId;
            isolatedResident.NativeIdentity.FactionId = individualFactionId;
            NpcSocialBackground authoredBackground;
            bool authoredEligible = PeopleOfTheIliacBayMod.RefreshSavedRandomPersonalityEligibility(
                isolated, personId, out authoredBackground);
            Require(!authoredEligible && isolated.ReadNpcPersonality(personId) == null,
                "Actual native Individual faction did not suppress and hide the saved random profile.");

            var presentation = NpcPresentationState.Capture(isolated, personId, now, true);
            Require(presentation != null && presentation.SavedTraits.Count == 0 &&
                presentation.CreateDeliveryContext() == null,
                "Suppressed authored profile leaked random traits into saved voice-delivery context.");

            string nonce = Guid.NewGuid().ToString("N");
            var evidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                "personality-probe-receipt-" + nonce, "personality-probe-episode-" + nonce,
                "personality-probe-outcome-" + nonce, NpcExperienceEventKind.Injury, now,
                NpcResponsibleActorAttribution.Unknown);
            NpcProbeLegacyExperienceFixture.Import(isolated, evidence);
            isolatedResident = isolated.ResolveResident(personId);
            Require(isolatedResident != null && isolatedResident.NativeIdentity != null &&
                isolatedResident.NativeIdentity.FactionId == individualFactionId,
                "Historical appraisal fixture lost the suppressed authored identity on restore.");
            NpcExperienceAppraisalTicket ticket;
            Require(!isolated.TryCaptureNpcExperienceAppraisal(personId, now, out ticket) && ticket == null,
                "Suppressed authored identity recreated a random profile through appraisal capture.");

            isolatedResident.NativeIdentity.FactionId = ordinaryFactionId;
            NpcSocialBackground restoredBackground;
            Require(PeopleOfTheIliacBayMod.RefreshSavedRandomPersonalityEligibility(
                isolated, personId, out restoredBackground),
                "Restored ordinary faction did not re-enable the existing random profile.");
            NpcPersonalityProfile restoredProfile = isolated.ReadNpcPersonality(personId);
            Require(ProfileFingerprint(restoredProfile) == traits && restoredProfile.SelectedTraits.Count == 3,
                "Temporary authored-individual suppression changed the ordinary profile on restoration.");
            Require(isolated.TryCaptureNpcExperienceAppraisal(personId, now, out ticket) && ticket != null &&
                ticket.Request.CanonicalEvidenceId == evidence.OutcomeId,
                "Ordinary positive control could not capture the same event after removing authored suppression.");

            VerifyNativeSocialGroupMappings();
            Require(ProfileFingerprint(liveStore.ReadNpcPersonality(personId)) == liveProfileBefore,
                "Detached V3 personality probe changed the live resident profile.");
            NpcRoleNativeProbe.Verify(liveStore, actor);

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERSONALITY_V3_NATIVE_PASS: npc=" + personId +
                "; profileVersion=3; savedTraits=3; fullSerializerStable=true; genericIsQuestProtectedTraitsAndRelationshipRetained=true" +
                "; questResourcePolicyRetained=true; authoredIndividualSuppressed=true; voiceDeliveryTraitsHidden=true" +
                "; appraisalRecreationRejected=true; originalFactionRestored=true; liveProfileUnchanged=true" +
                "; nativeSocialGroupMapping=0..4; actualNativeDialogueReaction=not-tested; questResourceComponentPath=not-tested.");
        }

        private static WorldState Clone(WorldState source)
        {
            string serialized = SaveLoadManager.Serialize(typeof(WorldState), source);
            Require(!string.IsNullOrEmpty(serialized), "FullSerializer returned empty detached personality state.");
            WorldState clone = SaveLoadManager.Deserialize(typeof(WorldState), serialized, true) as WorldState;
            Require(clone != null, "FullSerializer did not restore detached personality state.");
            return clone;
        }

        private static int FindNativeIndividualFaction()
        {
            var factions = GameManager.Instance.PlayerEntity.FactionData.FactionDict;
            foreach (KeyValuePair<int, FactionFile.FactionData> item in factions)
                if (item.Value.type == (int)FactionFile.FactionTypes.Individual)
                    return item.Key;
            throw new InvalidOperationException("Active native FactionData contains no Individual faction for the authored-person fixture.");
        }

        private static void VerifyNativeSocialGroupMappings()
        {
            var expected = new[] { NpcSocialBackground.Commoner, NpcSocialBackground.Merchant,
                NpcSocialBackground.Scholar, NpcSocialBackground.Noble, NpcSocialBackground.Underworld };
            var found = new bool[expected.Length];
            var factions = GameManager.Instance.PlayerEntity.FactionData.FactionDict;
            foreach (KeyValuePair<int, FactionFile.FactionData> item in factions)
            {
                int group = item.Value.sgroup;
                if (item.Value.type == (int)FactionFile.FactionTypes.Individual || group < 0 || group >= expected.Length || found[group])
                    continue;
                NpcSocialBackground actual;
                if (!PeopleOfTheIliacBayMod.TryResolveNpcPersonalityBackground(item.Key, out actual) || actual != expected[group])
                    throw new InvalidOperationException("Native social-group mapping changed for group " + group + ".");
                found[group] = true;
            }
            for (int group = 0; group < found.Length; group++)
                Require(found[group], "Active native FactionData has no non-Individual fixture for social group " + group + ".");
        }

        private static string ProfileFingerprint(NpcPersonalityProfile profile)
        {
            if (profile == null) return "<hidden-or-absent>";
            var values = new List<string>();
            foreach (KeyValuePair<string, string> trait in profile.SelectedTraits)
                values.Add(trait.Key + "=" + trait.Value);
            values.Sort(StringComparer.Ordinal);
            return profile.PersonId + "|" + profile.Version.ToString(CultureInfo.InvariantCulture) + "|" +
                profile.Seed.ToString(CultureInfo.InvariantCulture) + "|" + string.Join(";", values.ToArray());
        }

        private static string RelationshipFingerprint(NpcDirectedRelationshipView relationship)
        {
            if (relationship == null) return "<absent>";
            return string.Join("|", relationship.ObserverId, relationship.TargetId,
                relationship.Resentment.ToString(CultureInfo.InvariantCulture),
                relationship.InterpersonalFear.ToString(CultureInfo.InvariantCulture),
                relationship.TrustKnown.ToString(CultureInfo.InvariantCulture),
                relationship.ReliabilityTrust.ToString(CultureInfo.InvariantCulture),
                relationship.Revision.ToString(CultureInfo.InvariantCulture));
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
