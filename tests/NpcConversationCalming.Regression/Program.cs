using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const double FrozenGameTime = 100;

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("accepted safe turns cool strong fear and resentment by at most two and survive replay and restore", SafeTurnsAreSmallAndExactlyOnce),
            ("zero safe appraisal cools only an emotion that is still present", NeutralSafeTurnCoolsPresentEmotion),
            ("delayed friendly speech cannot cool a newer boundary at the same paused game time", OldSpeechCannotCoolNewBoundary),
            ("hostile speech does not receive conversational cooling", HostileTurnDoesNotCool),
            ("active refusal blocks cooling even for a repair attempt", RefusalBlocksCooling),
            ("protected rank retains stored player fear while resentment may cool", ProtectedRankRetainsFear),
            ("serious saved boundary limits positive chat mood gain until mood recovers", BoundaryMoodGainIsSlowAndScoped)
        };

        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void SafeTurnsAreSmallAndExactlyOnce()
    {
        WorldStateStore store = NewStore(out string personId);
        SeedRelationship(store, personId, 10, 10);
        Require(store.TryBeginNpcObservedSpeechSession(personId, "session:calming", out NpcObservedSpeechSession session),
            "safe-speech session did not open");

        NpcExperienceAppraisalTicket first = AcceptAndCapture(store, personId, session, 1,
            "We can speak again when you are ready.");
        BindClassification(store, first, "Other");
        string strongModelDecrease = Result(first.Request, resentment: -10, fear: -10, act: "Other");
        NpcDirectedRelationshipView beforeFailure = store.ReadNpcRelationship(personId, "player");
        Require(!store.TryCommitNpcExperienceAppraisal(first, "{}"), "failed model output was committed");
        Equal(10, beforeFailure.InterpersonalFear, "initial fear remains ten");
        Equal(10, beforeFailure.Resentment, "initial resentment remains ten");
        AssertRelationship(store, personId, 10, 10, "failed output caused no cooling");

        Require(store.TryCommitNpcExperienceAppraisal(first, strongModelDecrease), "valid safe appraisal did not commit");
        AssertRelationship(store, personId, 8, 8, "first accepted turn caps each decrease at two");
        Require(!store.TryCommitNpcExperienceAppraisal(first, strongModelDecrease), "appraisal replay was accepted");
        AssertRelationship(store, personId, 8, 8, "replay did not cool twice");

        var saved = store.Snapshot();
        store.Restore(saved);
        Require(!store.TryCommitNpcExperienceAppraisal(first, strongModelDecrease), "pre-restore ticket replayed after restore");
        AssertRelationship(store, personId, 8, 8, "restore retained the accepted cooling exactly once");

        Require(store.TryBeginNpcObservedSpeechSession(personId, "session:calming-restored", out session),
            "restored safe-speech session did not open");
        NpcExperienceAppraisalTicket second = AcceptAndCapture(store, personId, session, 2,
            "I will listen if you want to explain.");
        BindClassification(store, second, "Other");
        Require(store.TryCommitNpcExperienceAppraisal(second,
            Result(second.Request, resentment: -10, fear: -10, act: "Other")), "second safe appraisal did not commit");
        AssertRelationship(store, personId, 6, 6,
            "two accepted turns at unchanged game time leave strong fear and resentment intact");
    }

    private static void OldSpeechCannotCoolNewBoundary()
    {
        WorldStateStore store = NewStore(out string personId);
        Require(store.TryBeginNpcObservedSpeechSession(personId, "session:old-safe", out var session),
            "speech session did not open");
        AcceptAndCapture(store, personId, session, 1, "Good morning.");
        Require(store.TryIssueNpcObservedSpeech(session, 2, "I will hurt you.", FrozenGameTime,
            out var threat), "later threat was not issued");
        object source = new object();
        var request = NpcSpeechActRequest.Create(source, personId, "player", null, threat.ExactText, "");
        Require(NpcSpeechActClassification.TryAccept(request, "DirectThreat", out var classification),
            "threat classification was rejected");
        Require(store.TryCommitNpcConversationBoundary(threat, threat.Receipt, source, classification, out _),
            "later boundary did not commit");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(threat, threat.Receipt,
            NpcExperienceAppliedEffectsMask.None) != NpcObservedSpeechAcceptanceResult.Rejected,
            "threat speech was not accepted");
        store.Restore(store.Snapshot());
        Require(store.TryCaptureNpcExperienceAppraisal(personId, FrozenGameTime, out var delayed),
            "old accepted speech was not recaptured after restore");
        Equal("Good morning.", delayed.Request.ObservedUtterance, "old speech is still first in the inbox");
        BindClassification(store, delayed, "Other");
        string response = Result(delayed.Request, resentment: -10, fear: -10, act: "Other");
        Require(store.TryCommitNpcExperienceAppraisal(delayed, response), "old appraisal should drain normally");
        AssertRelationship(store, personId, 65, 65, "old greeting cannot ease a later threat");
        var applied = AppliedAppraisal(store, delayed.Request);
        Equal(0, applied.ResentmentDelta, "no pre-incident resentment recovery");
        Equal(0, applied.FearDelta, "no pre-incident fear recovery");
        Require(!store.TryCommitNpcExperienceAppraisal(delayed, response), "old appraisal cannot replay");
        store.Restore(store.Snapshot());
        AssertRelationship(store, personId, 65, 65, "new emotions survive restore without old-speech recovery");
    }

    private static void NeutralSafeTurnCoolsPresentEmotion()
    {
        WorldStateStore store = NewStore(out string personId);
        SeedRelationship(store, personId, 10, 10);
        Require(store.TryBeginNpcObservedSpeechSession(personId, "session:neutral", out NpcObservedSpeechSession session),
            "neutral-speech session did not open");
        NpcExperienceAppraisalTicket ticket = AcceptAndCapture(store, personId, session, 1,
            "Good morning.");
        BindClassification(store, ticket, "Other");
        int affinity = ticket.Request.PositiveAffinityCredit;
        Require(affinity > 0, "safe-turn positive affinity credit was not available");
        Require(store.TryCommitNpcExperienceAppraisal(ticket,
            Result(ticket.Request, affinity: affinity, act: "Other")),
            "neutral safe appraisal did not commit");
        AssertRelationship(store, personId, 9, 9, "zero model proposal cools one point per present dimension");
        NpcExperienceAppliedAppraisal saved = AppliedAppraisal(store, ticket.Request);
        Equal(affinity, saved.AffinityDelta, "positive affinity proposal is preserved in the applied receipt");
        Equal(-1, saved.ResentmentDelta, "safe zero resentment proposal records one-point cooling");
        Equal(-1, saved.FearDelta, "safe zero fear proposal records one-point cooling");
    }

    private static void HostileTurnDoesNotCool()
    {
        WorldStateStore store = NewStore(out string personId);
        SeedRelationship(store, personId, 10, 10);
        Require(store.TryBeginNpcObservedSpeechSession(personId, "session:hostile", out NpcObservedSpeechSession session),
            "hostile-speech session did not open");
        NpcExperienceAppraisalTicket ticket = AcceptAndCapture(store, personId, session, 1,
            "I will kill you.");
        BindClassification(store, ticket, "DirectThreat");
        Require(store.TryCommitNpcExperienceAppraisal(ticket,
            Result(ticket.Request, act: "DirectThreat")), "zero-delta hostile appraisal did not commit");
        AssertRelationship(store, personId, 10, 10, "direct hostile speech did not earn cooling");
    }

    private static void RefusalBlocksCooling()
    {
        WorldStateStore store = NewStore(out string personId);
        SeedRelationship(store, personId, 10, 10);
        Require(store.TryBeginNpcObservedSpeechSession(personId, "session:refusal", out NpcObservedSpeechSession session),
            "refusal-speech session did not open");
        NpcExperienceAppraisalTicket ticket = AcceptAndCapture(store, personId, session, 1,
            "I am sorry for what I said.");
        var request = ticket.Request;
        NpcSpeechActRequest speechRequest = NpcSpeechActRequest.Create(request, request.ObserverId,
            request.RelationshipTargetId, null, request.ObservedUtterance, string.Empty,
            listenerRefusesFurtherCommunication: true);
        Require(NpcSpeechActClassification.TryAccept(speechRequest, "RepairAttempt",
            out NpcSpeechActClassification classification), "repair classification was rejected");
        Require(store.TryBindNpcExperienceSpeechClassification(ticket, classification),
            "source-bound repair classification did not bind");
        Require(store.TryCommitNpcExperienceAppraisal(ticket,
            Result(ticket.Request, act: "RepairAttempt")), "repair appraisal did not commit");
        AssertRelationship(store, personId, 10, 10, "active refusal prevented conversational cooling");
    }

    private static void ProtectedRankRetainsFear()
    {
        WorldStateStore store = NewStore(out string personId, NpcSocialBackground.Noble);
        SeedRelationship(store, personId, 10, 10);
        Require(store.RecordNpcRoleEvidence(personId, NpcSocialRank.Nobility, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, "native:rank:calming-test", null, null), "noble role evidence was rejected");
        Require(store.TryBeginNpcObservedSpeechSession(personId, "session:protected", out NpcObservedSpeechSession session),
            "protected-speech session did not open");
        NpcExperienceAppraisalTicket ticket = AcceptAndCapture(store, personId, session, 1,
            "Good morning, my lord.");
        Require(ticket.Request.PlayerFearSuppressed, "noble speech request lacks protected-fear policy");
        BindClassification(store, ticket, "Other");
        Require(store.TryCommitNpcExperienceAppraisal(ticket,
            Result(ticket.Request, act: "Other")), "protected-rank safe appraisal did not commit");

        NpcDirectedRelationship raw = store.Snapshot().NpcRelationships.Relationships.Single(x =>
            x.ObserverId == personId && x.TargetId == NpcDirectedRelationship.PlayerTargetId);
        Equal(10, raw.InterpersonalFear, "stored noble player fear is retained");
        Equal(9, raw.Resentment, "safe turn may cool resentment by one");
        Equal(0, store.ReadNpcRelationship(personId, "player").ProjectedInterpersonalFear,
            "effective player fear remains suppressed");
    }

    private static void BoundaryMoodGainIsSlowAndScoped()
    {
        WorldStateStore store = NewStore(out string personId);
        Require(store.TryBeginNpcObservedSpeechSession(personId, "session:boundary-mood", out NpcObservedSpeechSession session),
            "boundary-speech session did not open");
        Require(store.TryIssueNpcObservedSpeech(session, 1, "You are a fool.", FrozenGameTime,
            out NpcObservedSpeechTicket ticket), "hostile boundary speech was not issued");
        object source = new object();
        NpcSpeechActRequest speechRequest = NpcSpeechActRequest.Create(source, personId,
            NpcDirectedRelationship.PlayerTargetId, null, ticket.ExactText, string.Empty);
        Require(NpcSpeechActClassification.TryAccept(speechRequest, "DirectInsult",
            out NpcSpeechActClassification classification), "hostile boundary classification failed");
        Require(store.TryCommitNpcConversationBoundary(ticket, ticket.Receipt, source, classification,
            out NpcConversationBoundaryRecord boundary), "serious boundary did not commit");
        Require(boundary != null, "saved serious boundary is missing");
        Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, ticket.Receipt,
            NpcExperienceAppliedEffectsMask.None) != NpcObservedSpeechAcceptanceResult.Rejected,
            "boundary speech was not accepted");

        ConversationMoodView mood = store.CaptureConversationMood(personId, FrozenGameTime);
        Require(mood.Value <= -40, "serious boundary did not establish strongly negative mood");
        Require(store.TryCommitAcceptedSocialExchange(personId, mood.AcceptedTurnSequence,
            mood.Revision, 10, FrozenGameTime, "day", "place", "I hope we can talk.",
            "The resident pauses.", out long sequence), "first positive exchange did not commit");
        Equal(mood.AcceptedTurnSequence + 1, sequence, "first accepted turn advanced once");
        Equal(mood.Value + 2, store.CaptureConversationMood(personId, FrozenGameTime).Value,
            "positive mood gain is capped at two while serious offense remains active");

        mood = store.CaptureConversationMood(personId, FrozenGameTime);
        Require(store.TryCommitAcceptedSocialExchange(personId, mood.AcceptedTurnSequence,
            mood.Revision, 10, FrozenGameTime, "day", "place", "I can make amends.",
            "The resident listens.", out sequence), "second positive exchange did not commit");
        Equal(-36, store.CaptureConversationMood(personId, FrozenGameTime).Value,
            "two friendly turns cannot clear the boundary anger");

        WorldStateStore ordinary = NewStore(out string ordinaryPerson);
        for (int index = 0; index < 4; index++)
        {
            ConversationMoodView ordinaryMoodStep = ordinary.CaptureConversationMood(ordinaryPerson, FrozenGameTime);
            Require(ordinary.TryCommitAcceptedSocialExchange(ordinaryPerson,
                ordinaryMoodStep.AcceptedTurnSequence, ordinaryMoodStep.Revision, -10,
                FrozenGameTime, "day", "place", "A serious loss.", "The resident grieves.", out sequence),
                "ordinary negative mood could not be seeded");
        }
        ConversationMoodView ordinaryMood = ordinary.CaptureConversationMood(ordinaryPerson, FrozenGameTime);
        Equal(-40, ordinaryMood.Value, "ordinary mood reached negative forty");
        Require(ordinary.TryCommitAcceptedSocialExchange(ordinaryPerson, ordinaryMood.AcceptedTurnSequence,
            ordinaryMood.Revision, 10, FrozenGameTime, "day", "place", "Good news.",
            "The resident smiles.", out sequence), "ordinary positive exchange did not commit");
        Equal(-30, ordinary.CaptureConversationMood(ordinaryPerson, FrozenGameTime).Value,
            "mood without a saved hostile boundary keeps its existing positive response");
    }

    private static WorldStateStore NewStore(out string personId,
        NpcSocialBackground background = NpcSocialBackground.Commoner)
    {
        var store = new WorldStateStore(109);
        personId = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        store.GetOrCreateNpcPersonality(personId, 109, background);
        return store;
    }

    private static void SeedRelationship(WorldStateStore store, string personId, int resentment, int fear)
    {
        NpcExperienceEvent evidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
            "receipt:seed:relationship", "episode:seed:relationship", "outcome:seed:relationship",
            NpcExperienceEventKind.Healing, FrozenGameTime, NpcResponsibleActorAttribution.Known, "player");
        var proposal = new NpcRelationshipLedgerUpdateProposal
        {
            ExpectedLedgerRevision = store.NpcRelationshipLedgerRevision,
            ObserverId = personId,
            TargetId = NpcDirectedRelationship.PlayerTargetId,
            RelationshipUpdate = new NpcRelationshipUpdateProposal
            {
                ExpectedRevision = 0,
                Evidence = evidence,
                ResentmentDelta = resentment,
                InterpersonalFearDelta = fear
            }
        };
        Require(store.TryCommitNpcRelationship(proposal, proposal.ExpectedLedgerRevision),
            "initial relationship emotion was rejected");
    }

    private static NpcExperienceAppraisalTicket AcceptAndCapture(WorldStateStore store,
        string personId, NpcObservedSpeechSession session, long requestRevision, string text)
    {
        ConversationMoodView mood = store.CaptureConversationMood(personId, FrozenGameTime);
        Require(store.TryIssueNpcObservedSpeech(session, requestRevision, text, FrozenGameTime,
            out NpcObservedSpeechTicket speechTicket), "observed speech was not issued");
        Require(store.TryCommitAcceptedSocialExchangeWithObservedSpeech(personId,
            mood.AcceptedTurnSequence, mood.Revision, 0, FrozenGameTime, "day", "place", text,
            "The resident replies.", speechTicket, speechTicket.Receipt,
            NpcExperienceAppliedEffectsMask.Mood, out long acceptedSequence),
            "speech did not commit as an accepted chat turn");
        Equal(mood.AcceptedTurnSequence + 1, acceptedSequence, "accepted chat sequence advanced once");
        Require(store.TryCaptureNpcExperienceAppraisal(personId, FrozenGameTime,
            out NpcExperienceAppraisalTicket ticket), "accepted speech appraisal was not captured");
        return ticket;
    }

    private static void BindClassification(WorldStateStore store,
        NpcExperienceAppraisalTicket ticket, string label)
    {
        NpcSpeechActRequest request = NpcSpeechActRequest.ForAppraisal(ticket.Request);
        Require(NpcSpeechActClassification.TryAccept(request, label,
            out NpcSpeechActClassification classification), "speech classification was rejected: " + label);
        Require(store.TryBindNpcExperienceSpeechClassification(ticket, classification),
            "speech classification was not bound: " + label);
    }

    private static string Result(NpcExperienceAppraisalRequest request, int affinity = 0,
        int resentment = 0, int fear = 0, string act = "NotApplicable")
    {
        return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
            ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
            ",\"saveBranchId\":\"" + request.SaveBranchId + "\",\"observerId\":\"" + request.ObserverId +
            "\",\"subjectPersonId\":\"" + request.SubjectPersonId +
            "\",\"evidenceDeduplicationKey\":\"" + request.EvidenceDeduplicationKey +
            "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId +
            "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? string.Empty) +
            "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision +
            ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision +
            ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision +
            ",\"moodDelta\":0,\"affinityDelta\":" + affinity + ",\"trustDelta\":0,\"resentmentDelta\":" + resentment +
            ",\"fearDelta\":" + fear + ",\"speechActKind\":\"" + act +
            "\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
    }

    private static void AssertRelationship(WorldStateStore store, string personId,
        int resentment, int fear, string message)
    {
        NpcDirectedRelationship raw = store.Snapshot().NpcRelationships.Relationships.Single(x =>
            x.ObserverId == personId && x.TargetId == NpcDirectedRelationship.PlayerTargetId);
        Equal(resentment, raw.Resentment, message + " (resentment)");
        Equal(fear, raw.InterpersonalFear, message + " (fear)");
    }

    private static NpcExperienceAppliedAppraisal AppliedAppraisal(WorldStateStore store,
        NpcExperienceAppraisalRequest request)
    {
        NpcExperienceInboxEntry entry = store.Snapshot().NpcExperienceInbox.Entries.Single(x =>
            x.ObserverId == request.ObserverId && x.DeduplicationKey == request.EvidenceDeduplicationKey);
        Require(entry.Status == NpcExperienceInboxStatus.Consumed && entry.Appraisal != null,
            "accepted appraisal receipt was not saved");
        return entry.Appraisal;
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
    }
}
