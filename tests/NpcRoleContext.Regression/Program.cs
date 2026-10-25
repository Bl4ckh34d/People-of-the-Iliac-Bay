using System;
using PeopleOfTheIliacBay.Core;

static class Program
{
    static int assertions;
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    static void Main()
    {
        var original = Snapshot();
        var noble = new NpcRoleState { PersonId = "npc:observer", SocialRank = NpcSocialRank.Nobility,
            RankSourceId = "native-faction:365", Revision = 1 };
        var captured = original.WithRoleEvidence(noble);
        Check(captured.IsSuccessorOf(original) && original.RoleEvidence == null, "Role capture must create an immutable successor.");
        noble.SocialRank = NpcSocialRank.Commoner;
        Check(captured.RoleEvidence.SocialRank == NpcSocialRank.Nobility, "Caller mutation must not change captured rank.");
        var leaked = captured.RoleEvidence; leaked.SocialRank = NpcSocialRank.Commoner;
        Check(captured.RoleEvidence.SocialRank == NpcSocialRank.Nobility, "Role getter must not expose mutable snapshot state.");
        var changed = captured.WithCommittedDisposition(-20).WithFearContext(null, true);
        Check(changed.RoleEvidence.SocialRank == NpcSocialRank.Nobility, "Other successors must retain role authority.");
        string text = ConversationContextFormatter.ForSocialReply(changed, 2400);
        Check(text.Contains("subordinate") && text.Contains("do not fear"), "Noble rule must be in reply context without needing a relationship row.");
        Check(!ConversationContextFormatter.ForSocialReply(original, 2400).Contains("subordinate"), "Unknown rank must not invent social superiority.");
        var guard = new NpcRoleState { PersonId = "npc:observer", CombatRole = NpcCombatRole.Guard,
            CombatSourceId = "native-guard-duty:1", ServiceBinding = NpcServiceBinding.NobleKingdom,
            ServiceSourceId = "native-guard-duty:1", Revision = 1 };
        string guardText = ConversationContextFormatter.ForSocialReply(original.WithRoleEvidence(guard), 2400);
        Check(guardText.Contains("city wall quarters") && !guardText.Contains("fight to the death"), "City watch context must preserve the rare wall retreat exception.");
        guard.CombatRole = NpcCombatRole.PalaceGuard;
        string palaceText = ConversationContextFormatter.ForSocialReply(original.WithRoleEvidence(guard), 2400);
        Check(palaceText.Contains("fight to the death") && !palaceText.Contains("rare retreat"), "Palace guard duty must remain unconditional.");
        var rogue = new NpcRoleState { PersonId = "npc:observer", CombatRole = NpcCombatRole.Rogue,
            CombatSourceId = "native-career:8", ServiceBinding = NpcServiceBinding.Independent,
            ServiceSourceId = "native-spawn:unbound", Revision = 1 };
        string rogueText = ConversationContextFormatter.ForSocialReply(original.WithRoleEvidence(rogue), 2400);
        Check(rogueText.Contains("severely injured") && !rogueText.Contains("fight to the death"), "Independent rogue must not inherit guard death duty.");
        bool rejected = false;
        try { original.WithRoleEvidence(new NpcRoleState { PersonId = "npc:other", SocialRank = NpcSocialRank.Nobility, RankSourceId = "native:other", Revision = 1 }); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Foreign role must not attach to speaker.");
        Check(NpcNativeRoleProjection.SocialRank(true, 3, false) == NpcSocialRank.Nobility, "Native noble social group must resolve.");
        Check(NpcNativeRoleProjection.SocialRank(false, 3, false) == NpcSocialRank.Unknown, "Unresolved faction must not confer noble rank.");
        Check(NpcNativeRoleProjection.SocialRank(true, 4, false) == NpcSocialRank.Commoner, "Underworld affiliation does not confer noble rank.");
        Check(NpcNativeRoleProjection.SocialRank(true, 3, true) == NpcSocialRank.HighAuthority, "Verified royal binding has high authority.");
        Console.WriteLine("NpcRoleContext: " + assertions + " assertions passed.");
    }
    static ConversationContextSnapshot Snapshot()
    {
        var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, "npc:observer", null,
            "session:roles", 1, 2, "0123456789abcdef0123456789abcdef", "", "scene:town", 1);
        return new ConversationContextSnapshot(binding, 3600, Known("13:00"), Known("Mira"),
            ConversationContextFact<ConversationDialogueRole>.Unknown(), Known("Outside"), Known("scene:town"),
            ConversationContextFact<ConversationNpcPosition>.Unknown(), ConversationContextFact<ConversationNpcActivity>.Unknown(),
            ConversationContextFact<bool>.Unknown(), ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),
            Known("patient"), 0, 7, null, Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
    }
    static ConversationContextFact<string> Known(string text) => ConversationContextFact<string>.Verified(text, ConversationContextFactSource.NativeActor, 3600);
}
