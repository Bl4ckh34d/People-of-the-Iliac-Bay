using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int checks;

    private static void Equal<T>(T expected, T actual, string caseName)
    {
        checks++;
        if (!object.Equals(expected, actual))
            throw new InvalidOperationException(caseName + ": expected " + expected + ", got " + actual);
    }

    private static NpcSpeechActClassification Classify(object source, string label, string history = "")
    {
        var request = NpcSpeechActRequest.Create(source, "npc:observer", "player", null,
            "Current player speech", history);
        NpcSpeechActClassification classification;
        if (!NpcSpeechActClassification.TryAccept(request, label, out classification))
            throw new InvalidOperationException("Classification label rejected: " + label);
        return classification;
    }

    private static NpcSpeechActClassification ClassifyWithRefusal(object source, string label)
    {
        var request = NpcSpeechActRequest.Create(source, "npc:observer", "player", null,
            "Current player speech", "Earlier NPC speech", true);
        Equal(true, request.ListenerRefusesFurtherCommunication, "authoritative refusal flag retained");
        NpcSpeechActClassification classification;
        if (!NpcSpeechActClassification.TryAccept(request, label, out classification))
            throw new InvalidOperationException("Classification label rejected: " + label);
        return classification;
    }

    public static void Main()
    {
        object source = new object();
        Equal(false, ConversationSpeechBoundaryPolicy.ToleratesOrdinaryInsult(null, false, 90, true, 90, 0, 0),
            "high disposition alone does not establish familiarity");
        Equal(true, ConversationSpeechBoundaryPolicy.ToleratesOrdinaryInsult(null, true, 90, false, 0, 0, 0),
            "a familiar well-liked friend need not have reliability evidence to tolerate a mild insult");
        Equal(false, ConversationSpeechBoundaryPolicy.ToleratesOrdinaryInsult(null, true, 20, false, 0, 0, 0),
            "familiarity alone with an ordinary disposition is insufficient");
        Equal(false, ConversationSpeechBoundaryPolicy.ToleratesOrdinaryInsult(null, true, 90, true, 20, 0, 0),
            "known unreliability lowers tolerance despite a pleasant disposition");
        Equal(true, ConversationSpeechBoundaryPolicy.ToleratesOrdinaryInsult(null, true, 80, true, 80, 0, 0),
            "trusted friend can vent without automatic conversation closure");
        Equal(false, ConversationSpeechBoundaryPolicy.ToleratesOrdinaryInsult(null, true, 80, true, 80, 35, 0),
            "accumulated resentment consumes tolerance");
        var patient = new NpcPersonalityProfile { Patience = "patient", Sensitivity = "thick-skinned", Forgiveness = "forgiving" };
        var sensitive = new NpcPersonalityProfile { Patience = "impatient", Sensitivity = "highly sensitive", Forgiveness = "vindictive" };
        Equal(true, ConversationSpeechBoundaryPolicy.ToleratesOrdinaryInsult(patient, true, 65, true, 65, 0, 0),
            "patient friend has a lower threshold");
        Equal(false, ConversationSpeechBoundaryPolicy.ToleratesOrdinaryInsult(sensitive, true, 65, true, 65, 0, 0),
            "sensitive friend has a higher threshold");
        var friend = FriendContext();
        Equal(ConversationSpeechBoundaryAction.Continue,
            ConversationSpeechBoundaryPolicy.Decide(friend, Classify(friend,"DirectInsult")), "captured friend context tolerates ordinary insult");
        var familiar=FriendContext(false);
        Equal(ConversationSpeechBoundaryAction.Continue,
            ConversationSpeechBoundaryPolicy.Decide(familiar,Classify(familiar,"DirectInsult")), "familiar affinity works before a reliability ledger exists");
        Equal(ConversationSpeechBoundaryAction.EndConversation,
            ConversationSpeechBoundaryPolicy.Decide(friend, ClassifyWithRefusal(friend,"DirectInsult")), "friendship cannot override an active refusal");
        foreach(string label in new[]{"DirectThreat","SevereAbuse","PersistentAbuse","DirectHarassment"})
            Equal(label=="DirectThreat"?ConversationSpeechBoundaryAction.EndAndReport:ConversationSpeechBoundaryAction.EndConversation,
                ConversationSpeechBoundaryPolicy.Decide(friend, Classify(friend,label)), "friendship does not excuse "+label);
        Equal(11, ConversationBrowsing.Pick(new[]{11,12},new[]{11},count=>0), "remembered selection revalidates native eligibility");
        Equal(-1, ConversationBrowsing.Pick(new[]{11},new[]{12},count=>0), "unknown or ineligible destination cannot bind");
        object wrongSource = new object();
        var action = new ConversationSelection("action", -1, "neutral", "none");

        var threat = Classify(source, "DirectThreat");
        Equal(ConversationSpeechBoundaryAction.EndAndReport,
            ConversationSpeechBoundaryPolicy.Decide(source, threat), "direct threat reports");
        Equal(ConversationSpeechBoundaryAction.Continue,
            ConversationSpeechBoundaryPolicy.Decide(wrongSource, threat), "wrong source cannot punish");
        Equal(ConversationSpeechBoundaryAction.Continue,
            ConversationSpeechBoundaryPolicy.Decide(source, null), "missing classification cannot punish");
        var threatSelection = NpcSpeechActClassification.GuardSelection(source, action, threat);
        Equal("social", threatSelection.Intent, "threat overrides action misroute");
        Equal(-1, threatSelection.TopicId, "hostile route has no native topic");
        Equal("insult", threatSelection.Tone, "threat carries hostile tone");
        Equal("none", threatSelection.SocialIntent, "listener threat does not invent family target");

        var expected = new[] {
            new { Label = "DirectInsult", Action = ConversationSpeechBoundaryAction.EndConversation, Tone = "insult" },
            new { Label = "DirectHarassment", Action = ConversationSpeechBoundaryAction.EndConversation, Tone = "rude" },
            new { Label = "SevereAbuse", Action = ConversationSpeechBoundaryAction.EndConversation, Tone = "insult" },
            new { Label = "PersistentAbuse", Action = ConversationSpeechBoundaryAction.EndConversation, Tone = "insult" }
        };
        foreach (var row in expected)
        {
            var classification = Classify(source, row.Label, "NPC previously set a boundary.");
            Equal(true, classification.IsDirectHostility, row.Label + " counts as direct hostility");
            Equal(row.Action, ConversationSpeechBoundaryPolicy.Decide(source, classification), row.Label + " boundary");
            var selection = NpcSpeechActClassification.GuardSelection(source, action, classification);
            Equal("social", selection.Intent, row.Label + " overrides action misroute");
            Equal(row.Tone, selection.Tone, row.Label + " tone");
        }

        foreach (string label in new[] { "ReportedThreat", "HypotheticalThreat", "AmbiguousThreat", "Other" })
        {
            var classification = Classify(source, label);
            Equal(false, classification.IsDirectHostility, label + " is not direct hostility");
            Equal(ConversationSpeechBoundaryAction.Continue,
                ConversationSpeechBoundaryPolicy.Decide(source, classification), label + " does not end chat");
            Equal("action", NpcSpeechActClassification.GuardSelection(source, action, classification).Intent,
                label + " preserves normal action route");
        }

        var family = new ConversationSelection("social", -1, "neutral", "family_threat");
        Equal("family_threat", NpcSpeechActClassification.GuardSelection(source, family, threat).SocialIntent,
            "bound direct threat preserves existing family route");
        Equal("none", NpcSpeechActClassification.GuardSelection(source, family,
            Classify(source, "DirectInsult")).SocialIntent, "insult cannot become family threat");
        Equal("action", NpcSpeechActClassification.GuardSelection(wrongSource, action, threat).Intent,
            "wrong source cannot change action route");
        var repair = ClassifyWithRefusal(source, "RepairAttempt");
        Equal(ConversationSpeechBoundaryAction.Continue,
            ConversationSpeechBoundaryPolicy.Decide(source, repair), "genuine repair may continue");
        Equal("social", NpcSpeechActClassification.GuardSelection(source, action, repair).Intent,
            "repair cannot become an action request");
        var unrelated = ClassifyWithRefusal(source, "Other");
        Equal(ConversationSpeechBoundaryAction.EndConversation,
            ConversationSpeechBoundaryPolicy.Decide(source, unrelated), "ordinary follow-up keeps refusal without reporting");
        Equal(ConversationSpeechBoundaryAction.Continue,
            ConversationSpeechBoundaryPolicy.Decide(wrongSource, unrelated), "wrong source cannot enforce refusal");
        Equal(ConversationSpeechBoundaryAction.Continue,
            ConversationSpeechBoundaryPolicy.Decide(source, Classify(source, "Other")),
            "unrelated reply without refusal remains ordinary");
        BoundaryLedger();
        ConsumedBoundaryAppraisal();
        Console.WriteLine("ConversationSpeechBoundary regression: " + checks + " checks passed.");
    }

    private static ConversationContextSnapshot FriendContext(bool reliabilityKnown=true)
    {
        var binding=new ConversationContextBinding(ConversationActorCategory.Mobile,"npc:observer",null,
            "session:friend",1,7,"0123456789abcdef0123456789abcdef",
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","scene:town",3);
        var personal=new ConversationContextPersonalState(ConversationContextFact<string>.Unknown(),
            ConversationContextFact<string>.Unknown(),ConversationContextFact<bool>.Verified(true,ConversationContextFactSource.Save,10),
            ConversationContextFact<string>.Unknown(),ConversationContextFact<bool>.Unknown(),ConversationContextFact<bool>.Unknown());
        var context=new ConversationContextSnapshot(binding,10,ConversationContextFact<string>.Unknown(),
            ConversationContextFact<string>.Unknown(),ConversationContextFact<ConversationDialogueRole>.Unknown(),
            ConversationContextFact<string>.Unknown(),ConversationContextFact<string>.Unknown(),
            ConversationContextFact<ConversationNpcPosition>.Unknown(),ConversationContextFact<ConversationNpcActivity>.Unknown(),
            ConversationContextFact<bool>.Unknown(),ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),
            ConversationContextFact<string>.Unknown(),80,7,null,Array.Empty<ConversationContextCommitment>(),
            Array.Empty<ConversationContextSourceHandle>(),personal);
        var relation=NpcDirectedRelationship.Create("npc:observer","player");
        if(!reliabilityKnown)return context;
        relation.TrustKnown=true;relation.ReliabilityTrust=80;relation.TrustEvidenceCount=1;relation.TrustConfidence=10;
        return context.WithDirectedRelationship(relation.ToView());
    }

    private static void BoundaryLedger()
    {
        var store = new WorldStateStore(42);
        string person = store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0].Id;
        if (!store.TryBeginNpcObservedSpeechSession(person, "boundary-ledger-test", out var session) ||
            !store.TryIssueNpcObservedSpeech(session, 1, "You are a fool.", 10, out var ticket))
            throw new InvalidOperationException("Could not issue boundary ledger ticket.");
        object source = new object();
        var request = NpcSpeechActRequest.Create(source, person, "player", null, ticket.ExactText, "");
        if (!NpcSpeechActClassification.TryAccept(request, "DirectInsult", out var classification))
            throw new InvalidOperationException("Could not classify boundary ledger test.");
        var empty = NpcRelationshipLedger.Create();
        Func<string, bool> known = id => id == person;
        Equal(false, NpcRelationshipLedger.TryStageConversationBoundary(empty, 0, ticket, ticket.Receipt,
            new object(), classification, ticket.Ordinal, 0, null, null, known, out _, out _),
            "foreign source cannot stage resentment");
        Equal(true, NpcRelationshipLedger.TryStageConversationBoundary(empty, 0, ticket, ticket.Receipt,
            source, classification, ticket.Ordinal, 0, null, null, known, out var staged, out var view),
            "one issued boundary stages resentment");
        Equal(65, view.ProjectedResentment, "single boundary reaches high resentment");
        Equal(0L, empty.Revision, "staging leaves input detached");
        Equal(1L, staged.Revision, "one boundary is one accepted outcome");
        Equal(ticket.Ordinal, staged.AcceptedOutcomes[0].ConversationBoundaryOrdinal,
            "accepted outcome retains boundary ordinal");
        Equal(ticket.Receipt.Evidence.ReceiptId, staged.AcceptedOutcomes[0].ReceiptId,
            "accepted outcome retains exact issued receipt");
        Equal(0, view.ProjectedInterpersonalFear, "boundary does not invent fear for protected roles");
        Equal(true, view.ResentmentRecoveryHalfLifeSeconds > 86400,
            "high resentment recovers over days");
        Equal(false, NpcRelationshipLedger.TryStageConversationBoundary(staged, 1, ticket, ticket.Receipt,
            source, classification, ticket.Ordinal, 0, null, null, known, out _, out _),
            "same receipt cannot stage twice");
        var restored = NpcRelationshipLedger.CopyValidated(staged, known);
        Equal(65, restored.Read(person, "player").ProjectedResentment,
            "receipt-bound high resentment survives validation");
        restored.AcceptedOutcomes[0].ConversationBoundaryOrdinal = 0;
        bool rejected = false;
        try { NpcRelationshipLedger.CopyValidated(restored, known); }
        catch (ArgumentException) { rejected = true; }
        Equal(true, rejected, "large outcome cannot shed its boundary marker");
    }

    private static void ConsumedBoundaryAppraisal()
    {
        var store = new WorldStateStore(43);
        string person = store.GetOrCreate(11, new[] { 23 }, 1).Households[0].Residents[0].Id;
        if (!store.TryBeginNpcObservedSpeechSession(person, "appraisal-consume-test", out var session) ||
            !store.TryIssueNpcObservedSpeech(session, 1, "You are a fool.", 10, out var ticket))
            throw new InvalidOperationException("Could not issue appraisal consume ticket.");
        object source = new object();
        var request = NpcSpeechActRequest.Create(source, person, "player", null, ticket.ExactText, "");
        if (!NpcSpeechActClassification.TryAccept(request, "DirectInsult", out var classification))
            throw new InvalidOperationException("Could not classify appraisal consume test.");
        Equal(true, store.TryCommitNpcConversationBoundary(ticket, ticket.Receipt, source,
            classification, out _), "boundary applied before background appraisal");
        Equal(NpcObservedSpeechAcceptanceResult.Projected,
            store.TryAcceptNpcObservedSpeechAfterAppend(ticket, ticket.Receipt,
                NpcExperienceAppliedEffectsMask.None), "offending speech queued");
        Equal(true, store.TryCaptureNpcExperienceAppraisal(person, 10, out var appraisalTicket),
            "background appraisal ticket captured");
        long before = store.Snapshot().NpcRelationships.Revision;
        Equal(true, store.TryCommitNpcExperienceAppraisal(appraisalTicket, null),
            "already applied boundary receipt drains without model effects");
        Equal(before, store.Snapshot().NpcRelationships.Revision,
            "background drain does not apply duplicate relationship outcome");
        Equal(0, store.ReadPendingNpcExperiences(person, 10).Count,
            "boundary speech is not pending forever");
        Equal(false, store.TryCommitNpcExperienceAppraisal(appraisalTicket, null),
            "drained boundary speech cannot replay");
        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Equal(0, restored.ReadPendingNpcExperiences(person, 10).Count,
            "consumed boundary speech stays drained after restore");
    }
}
