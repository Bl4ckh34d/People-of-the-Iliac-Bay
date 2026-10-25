using System;

namespace PeopleOfTheIliacBay.Core
{
    // Facts are copied from the current native StaticNPC and FACTION/FLATS
    // metadata. No display-name or sprite-color interpretation belongs here.
    public struct NativeStaticRoleFacts
    {
        public readonly bool FactionResolved;
        public readonly bool CustomFaction;
        public readonly int FactionId;
        public readonly bool IndividualFaction;
        public readonly bool NobilitySocialGroup;
        public readonly bool BardsGuildGroup;
        public readonly int FactionFaceIndex;
        public readonly int BillboardArchive;
        public readonly int BillboardRecord;
        public readonly bool VerifiedShopkeeper;
        public readonly bool VerifiedInnkeeper;

        public NativeStaticRoleFacts(bool factionResolved, bool customFaction, int factionId,
            bool individualFaction, bool nobilitySocialGroup, bool bardsGuildGroup, int factionFaceIndex,
            int billboardArchive, int billboardRecord, bool verifiedShopkeeper, bool verifiedInnkeeper)
        {
            FactionResolved = factionResolved;
            CustomFaction = customFaction;
            FactionId = factionId;
            IndividualFaction = individualFaction;
            NobilitySocialGroup = nobilitySocialGroup;
            BardsGuildGroup = bardsGuildGroup;
            FactionFaceIndex = factionFaceIndex;
            BillboardArchive = billboardArchive;
            BillboardRecord = billboardRecord;
            VerifiedShopkeeper = verifiedShopkeeper;
            VerifiedInnkeeper = verifiedInnkeeper;
        }
    }

    public static class StaticNpcRoleProjection
    {
        // Verified against the local native FACTION.TXT records. Face index,
        // individual type, and Nobility group guard against recycled IDs.
        private static bool IsKnownRoyal(NativeStaticRoleFacts facts)
        {
            if (!facts.FactionResolved || facts.CustomFaction || !facts.IndividualFaction || !facts.NobilitySocialGroup)
                return false;
            return facts.FactionId == 364 && facts.FactionFaceIndex == 18 || // King Gothryd
                facts.FactionId == 365 && facts.FactionFaceIndex == 19 ||   // Queen Aubk-i
                facts.FactionId == 390 && facts.FactionFaceIndex == 42 ||   // King Eadwyre
                facts.FactionId == 391 && facts.FactionFaceIndex == 43;     // Queen Barenziah
        }

        public static ConversationDialogueRole? Resolve(NativeStaticRoleFacts facts)
        {
            // Existing building/service validation remains the source of keeper roles.
            if (facts.VerifiedInnkeeper) return ConversationDialogueRole.Innkeeper;
            if (facts.VerifiedShopkeeper) return ConversationDialogueRole.Shopkeeper;
            if (IsKnownRoyal(facts)) return ConversationDialogueRole.Royalty;

            // Native FLATS.CFG records 182/5 and 182/6 have exact "jester"
            // captions. This proves role identity, not active performance.
            if (facts.BillboardArchive == 182 && (facts.BillboardRecord == 5 || facts.BillboardRecord == 6))
                return ConversationDialogueRole.Jester;

            // Faction #511 is the native Group record for The Bards (ggroup 5).
            // Individual records with that guild group also prove affiliation.
            if (facts.FactionResolved && !facts.CustomFaction && facts.BardsGuildGroup &&
                (facts.IndividualFaction || facts.FactionId == 511))
                return ConversationDialogueRole.BardGuildMember;

            // A role such as generic performer, musician, server, noble, or
            // royalty remains unknown without a specific supported fact.
            return null;
        }

        public static string StaticActorContext(ConversationDialogueRole? role)
        {
            string roleText = role.HasValue ? RoleText(role.Value) : "unknown; do not infer one from appearance or name";
            return "Native static speaker role: " + roleText + ". This native role classification does not replace authored quest persona or explicit quest facts. Current placement does not prove an itinerary, walk, destination, food errand, or active performance. No personal daily schedule or current travel task is verified here. Describe travel or performance only when a separate verified task, quest fact, or observed activity supports it.";
        }

        public static string RoleText(ConversationDialogueRole role)
        {
            switch (role)
            {
                case ConversationDialogueRole.ManagedResident: return "managed resident";
                case ConversationDialogueRole.Guard: return "guard";
                case ConversationDialogueRole.Shopkeeper: return "shopkeeper";
                case ConversationDialogueRole.Innkeeper: return "innkeeper";
                case ConversationDialogueRole.Traveler: return "traveler";
                case ConversationDialogueRole.Jester: return "jester";
                case ConversationDialogueRole.BardGuildMember: return "member of the Bards guild";
                case ConversationDialogueRole.Royalty: return "royalty";
                default: return "unknown";
            }
        }
    }
}
