using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public NpcPersonalityProfile ReadNpcPersonality(string personId)
        {
            if (string.IsNullOrWhiteSpace(personId)) return null;
            var profile = ActiveNpcPersonality(state, personId);
            return profile == null ? null : NpcPersonalityProfile.CopyValidated(profile);
        }

        // Native authored-character binding owns this flag; generic quest assignment does not. Never an LLM tool.
        public void SetNpcRandomPersonalitySuppressed(string personId, bool suppressed)
        {
            var profile = state.NpcPersonalities.Find(item => item.PersonId == personId);
            if (profile != null) profile.RandomTraitsSuppressed = suppressed;
        }

        private static NpcPersonalityProfile ActiveNpcPersonality(WorldState world, string personId)
        {
            var profile = world.NpcPersonalities.Find(item => item.PersonId == personId);
            if (profile == null || profile.RandomTraitsSuppressed) return null;
            return profile;
        }

        /// <summary>
        /// Reads stable identity inputs for event-time profile creation. This is read-only. Only saved household
        /// residents expose quest-protection state here; actors without that native eligibility fact are not
        /// eligible for implicit profile creation. Appearance seed zero is a valid seed, not a missing sentinel.
        /// </summary>
        public bool TryReadNpcPersonalityIdentity(string personId, out uint seed, out int factionId,
            out bool questProtected)
        {
            seed = 0;
            factionId = -1;
            questProtected = false;
            if (string.IsNullOrWhiteSpace(personId)) return false;

            Resident resident = null;
            foreach (var settlement in state.Settlements)
            {
                foreach (var household in settlement.Households)
                {
                    resident = household.Residents.Find(item => item != null && item.Id == personId);
                    if (resident != null) break;
                }
                if (resident != null) break;
            }
            if (resident == null)
            {
                foreach (var retired in state.HouseholdHistory)
                {
                    if (retired == null || retired.Household == null) continue;
                    resident = retired.Household.Residents.Find(item => item != null && item.Id == personId);
                    if (resident != null) break;
                }
            }
            if (resident == null) return false;

            seed = resident.AppearanceSeed;
            factionId = resident.NativeIdentity == null ? -1 : resident.NativeIdentity.FactionId;
            questProtected = resident.IsQuestProtected;
            return true;
        }

        public NpcPersonalityProfile GetOrCreateNpcPersonality(string personId, uint seed,
            NpcSocialBackground background)
        {
            if (string.IsNullOrWhiteSpace(personId)) throw new ArgumentException("A known NPC identity is required.", nameof(personId));
            bool alive;
            Require(KnownNpcIdentity(state, personId, out alive), "NPC personality requires a known NPC identity.");

            var existing = state.NpcPersonalities.Find(item => item.PersonId == personId);
            if (existing != null)
            {
                Require(!existing.RandomTraitsSuppressed, "Story-controlled NPCs cannot activate a random personality.");
                var migrated = NpcPersonalityProfile.ToThreeTraits(existing);
                migrated.Background = background;
                state.NpcPersonalities[state.NpcPersonalities.IndexOf(existing)] = migrated;
                return NpcPersonalityProfile.CopyValidated(migrated);
            }

            var generated = NpcPersonalityGenerator.Generate(personId, seed, background);
            state.NpcPersonalities.Add(NpcPersonalityProfile.CopyValidated(generated));
            return NpcPersonalityProfile.CopyValidated(generated);
        }

        /// <summary>Reads saved native faction evidence without creating or inventing an identity.</summary>
        public bool TryReadNpcPersonalityFaction(string personId, out int factionId)
        {
            uint seed;
            bool questProtected;
            if (TryReadNpcPersonalityIdentity(personId, out seed, out factionId, out questProtected)) return true;
            var native = state.NativePeople.Find(item => item != null && item.Id == personId);
            if (native == null) return false;
            factionId = native.FactionId;
            return true;
        }

        private static List<NpcPersonalityProfile> CopyNpcPersonalities(WorldState source, WorldState validated)
        {
            var result = new List<NpcPersonalityProfile>();
            if (source.Version < 33) return result;
            Require(source.NpcPersonalities != null, "Missing NPC personality ledger.");
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var saved in source.NpcPersonalities)
            {
                Require(saved != null, "Null NPC personality profile.");
                var profile = NpcPersonalityProfile.ToThreeTraits(saved);
                Require(identities.Add(profile.PersonId), "Duplicate NPC personality identity.");
                bool alive;
                Require(KnownNpcIdentity(validated, profile.PersonId, out alive),
                    "NPC personality references an unknown NPC identity.");
                result.Add(profile);
            }
            return result;
        }
    }
}
