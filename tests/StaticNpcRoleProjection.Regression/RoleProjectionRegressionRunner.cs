using System;
using PeopleOfTheIliacBay.Core;

internal static class RoleProjectionRegressionRunner
{
    private static int assertions;

    private static void Equal<T>(T expected, T actual, string label)
    {
        assertions++;
        if (!Equals(expected, actual)) throw new InvalidOperationException(label + ": expected " + expected + ", got " + actual);
    }

    private static NativeStaticRoleFacts Facts(int faction = -1, bool resolved = false, bool custom = false,
        bool individual = false, bool noble = false, bool bardsGuild = false, int face = -1,
        int archive = -1, int record = -1, bool shopkeeper = false, bool innkeeper = false)
    {
        return new NativeStaticRoleFacts(resolved, custom, faction, individual, noble, bardsGuild, face,
            archive, record, shopkeeper, innkeeper);
    }

    private static void Main()
    {
        Equal(ConversationDialogueRole.Jester, StaticNpcRoleProjection.Resolve(Facts(archive: 182, record: 5)), "FLATS 182/5 jester");
        Equal(ConversationDialogueRole.Jester, StaticNpcRoleProjection.Resolve(Facts(archive: 182, record: 6)), "FLATS 182/6 jester");
        Equal(null, StaticNpcRoleProjection.Resolve(Facts(archive: 182, record: 7)), "adjacent FLATS record unknown");
        Equal(null, StaticNpcRoleProjection.Resolve(Facts(archive: 183, record: 5)), "same record in a different archive unknown");
        Equal(ConversationDialogueRole.Royalty, StaticNpcRoleProjection.Resolve(Facts(364, true, false, true, true, false, 18)), "Gothryd native faction identity");
        Equal(ConversationDialogueRole.Royalty, StaticNpcRoleProjection.Resolve(Facts(365, true, false, true, true, false, 19)), "Aubk-i native faction identity");
        Equal(ConversationDialogueRole.Royalty, StaticNpcRoleProjection.Resolve(Facts(390, true, false, true, true, false, 42)), "Eadwyre native faction identity");
        Equal(ConversationDialogueRole.Royalty, StaticNpcRoleProjection.Resolve(Facts(391, true, false, true, true, false, 43)), "Barenziah native faction identity");
        Equal(null, StaticNpcRoleProjection.Resolve(Facts(364, true, false, true, false, false, 18)), "royalty requires native nobility group");
        Equal(null, StaticNpcRoleProjection.Resolve(Facts(364, true, false, true, true, false, 19)), "royalty requires exact native face");
        Equal(null, StaticNpcRoleProjection.Resolve(Facts(364, true, true, true, true, false, 18)), "custom faction never inherits a base royal identity");
        Equal(ConversationDialogueRole.BardGuildMember, StaticNpcRoleProjection.Resolve(Facts(511, true, false, false, false, true)), "native Bards guild group");
        Equal(null, StaticNpcRoleProjection.Resolve(Facts(511, false, false, false, false, true)), "unresolved Bards group unknown");
        Equal(null, StaticNpcRoleProjection.Resolve(Facts(511, true, true, false, false, true)), "custom Bards-like faction unknown");
        Equal(null, StaticNpcRoleProjection.Resolve(Facts(700, true, false, true, false, false)), "other guild affiliation unknown");
        Equal(ConversationDialogueRole.Shopkeeper, StaticNpcRoleProjection.Resolve(Facts(shopkeeper: true)), "existing verified keeper role retained");
        Equal(null, StaticNpcRoleProjection.Resolve(Facts(archive: 182, record: 7)), "unsupported profession remains unknown");
        Equal(ConversationDialogueRole.Merchant, StaticNpcRoleProjection.Resolve(new NativeStaticRoleFacts(
            true,false,469,false,false,false,-1,184,0,false,false,true)), "native merchant group establishes profession");
        Equal(null, StaticNpcRoleProjection.Resolve(new NativeStaticRoleFacts(
            false,false,469,false,false,false,-1,184,0,false,false,true)), "unresolved merchant group does not establish profession");
        Equal(null, StaticNpcRoleProjection.Resolve(new NativeStaticRoleFacts(
            true,true,469,false,false,false,-1,184,0,false,false,true)), "custom recycled merchant faction requires its own adapter");
        Equal(ConversationDialogueRole.Innkeeper, StaticNpcRoleProjection.Resolve(new NativeStaticRoleFacts(
            true,false,469,false,false,false,-1,184,0,false,true,true)), "actual inn service takes priority over merchant group");
        MerchantStock();

        string unknown = StaticNpcRoleProjection.StaticActorContext(null);
        if (unknown.IndexOf("unknown", StringComparison.Ordinal) < 0 || unknown.IndexOf("quest persona", StringComparison.Ordinal) < 0 ||
            unknown.IndexOf("does not prove", StringComparison.Ordinal) < 0)
            throw new InvalidOperationException("Static context must preserve unknown roles and authored quest persona while rejecting inferred itineraries.");
        assertions++;
        string nativeDuty = ConversationLifeContext.NativeStaticDuty(null);
        if (nativeDuty.IndexOf("authored quest persona", StringComparison.Ordinal) < 0 ||
            nativeDuty.IndexOf("walking", StringComparison.Ordinal) < 0 || nativeDuty.IndexOf("looking for food", StringComparison.Ordinal) < 0)
            throw new InvalidOperationException("Static life context must preserve quest facts and reject invented movement or errands.");
        assertions++;
        string staticProjection = ConversationContextFormatter.ForRouting(Snapshot(ConversationActorCategory.Static,
            ConversationContextFact<ConversationDialogueRole>.Verified(ConversationDialogueRole.Jester,
                ConversationContextFactSource.NativeActor, 0)), 500);
        if (staticProjection.IndexOf("Native static speaker role: jester", StringComparison.Ordinal) < 0 ||
            staticProjection.IndexOf("authored quest persona", StringComparison.Ordinal) < 0)
            throw new InvalidOperationException("Static speaker formatting must carry verified role and preserve quest persona.");
        assertions++;
        string mobileProjection = ConversationContextFormatter.ForRouting(Snapshot(ConversationActorCategory.Mobile,
            ConversationContextFact<ConversationDialogueRole>.Unknown()), 500);
        if (mobileProjection.IndexOf("Native static speaker role:", StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Static role policy must not bleed into mobile speakers.");
        assertions++;
        Console.WriteLine("Production-linked static role regressions passed: " + assertions + " assertions.");
    }

    private static void MerchantStock()
    {
        var variants=new System.Collections.Generic.HashSet<string>();
        for(int identity=0;identity<100;identity++)
        {
            string id="native-market:"+identity;
            var first=NpcMerchantStock.Generate(123,id);
            var again=NpcMerchantStock.Generate(123,id);
            if(first.Count<10 || first.Count>14 || first.Count<=NpcStartingBelongings.MaximumDistinctItems*3)
                throw new InvalidOperationException("Merchant must have substantially more goods than a resident.");
            var seen=new System.Collections.Generic.HashSet<int>();
            string signature="";
            for(int i=0;i<first.Count;i++)
            {
                if(!seen.Add(first[i].TemplateIndex) || first[i].Count!=1 || first[i].TemplateIndex==276)
                    throw new InvalidOperationException("Stock duplicated a template or generated currency.");
                Equal(first[i].TemplateIndex,again[i].TemplateIndex,"repeatable merchant stock");
                signature+=first[i].TemplateIndex+",";
            }
            variants.Add(signature);
        }
        if(variants.Count<50)throw new InvalidOperationException("Merchant stocks lack identity-specific variety.");
        assertions++;
    }

    private static ConversationContextSnapshot Snapshot(ConversationActorCategory category,
        ConversationContextFact<ConversationDialogueRole> role)
    {
        var binding = new ConversationContextBinding(category, null, "speaker-1", "session-1", 0, 0,
            "0123456789abcdef0123456789abcdef", "", "scene-1", 0);
        return new ConversationContextSnapshot(binding, 0,
            ConversationContextFact<string>.Unknown(), ConversationContextFact<string>.Unknown(), role,
            ConversationContextFact<string>.Unknown(), ConversationContextFact<string>.Unknown(),
            ConversationContextFact<ConversationNpcPosition>.Unknown(), ConversationContextFact<ConversationNpcActivity>.Unknown(),
            ConversationContextFact<bool>.Unknown(), ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),
            ConversationContextFact<string>.Unknown(), 0, 0, null,
            Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>());
    }
}
