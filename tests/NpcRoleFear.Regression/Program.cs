using PeopleOfTheIliacBay.Tests;
using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("protected rank suppresses effective player fear but preserves history and acute fear", ProtectedProjection),
            ("commoner player fear remains visible and may increase", CommonerControl),
            ("protected rank rejects positive native player fear writes", NativeWriteRejected),
            ("protected rank request grammar and parser reject positive player fear", AppraisalFearRejected),
            ("role revision change makes a captured appraisal stale", RoleChangeRejectsStaleAppraisal)
        };

        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void ProtectedProjection()
    {
        WorldStateStore store = NewStore(out string personId);
        store.GetOrCreateNpcPersonality(personId, 17, NpcSocialBackground.Noble);
        CommitFear(store, personId, "legacy-fear", 100, 24);
        store.TryObserveNpcAcuteFear(personId, "wolf:1", NpcFearSeverity.Moderate, 100);
        SetRole(store, personId, NpcSocialRank.Nobility);

        NpcDirectedRelationshipView raw = store.ReadNpcRelationship(personId, "player");
        Equal(24, raw.StoredInterpersonalFear, "persisted fear remains available for audit");
        Equal(0, raw.InterpersonalFear, "raw read exposes effective player fear zero");
        Equal(0, raw.ProjectedInterpersonalFear, "raw read exposes projected player fear zero");
        True(raw.PlayerFearSuppressed, "view identifies rank-based player fear suppression");
        Equal(store.ReadNpcRole(personId).Revision, raw.RoleRevision, "view records current role revision");

        NpcDirectedRelationshipView recovered = store.ReadNpcRelationship(personId, "player", 10000)
            .ProjectRecovery(20000, store.ReadNpcPersonality(personId));
        Equal(24, recovered.StoredInterpersonalFear, "repeated recovery projection retains stored history");
        Equal(0, recovered.InterpersonalFear, "repeated projection cannot rehydrate protected fear");
        Equal(0, recovered.ProjectedInterpersonalFear, "timed projection remains suppressed");

        NpcPresentationState presentation = NpcPresentationState.Capture(store, personId, 10000, true);
        Equal(0, presentation.PlayerFear, "portrait/contact fear stays zero for nobility");
        True(presentation.AcuteFearKnown && presentation.AcuteFearIntensity > 0,
            "separate witnessed-danger fear remains visible");
        Equal((int)Math.Round(presentation.AcuteFearIntensity, MidpointRounding.AwayFromZero),
            presentation.DeliveryFear, "voice delivery still receives acute danger fear");
        Equal(24, store.Snapshot().NpcRelationships.Relationships[0].InterpersonalFear,
            "presentation does not rewrite historical ledger state");
    }

    private static void CommonerControl()
    {
        WorldStateStore store = NewStore(out string personId);
        store.GetOrCreateNpcPersonality(personId, 17, NpcSocialBackground.Commoner);
        SetRole(store, personId, NpcSocialRank.Commoner);
        CommitFear(store, personId, "commoner-fear", 100, 8);

        NpcDirectedRelationshipView view = store.ReadNpcRelationship(personId, "player");
        True(!view.PlayerFearSuppressed, "commoner player fear is not protected");
        Equal(store.ReadNpcRole(personId).Revision, view.RoleRevision, "commoner view records current role revision");
        Equal(8, view.StoredInterpersonalFear, "stored commoner fear remains eight");
        Equal(8, view.InterpersonalFear, "effective commoner fear remains eight");
        Equal(8, view.ProjectedInterpersonalFear, "projected commoner fear remains eight");

        EnqueueNative(store, personId, "commoner-appraisal", 200);
        True(store.TryCaptureNpcExperienceAppraisal(personId, 200, out NpcExperienceAppraisalTicket ticket), "commoner appraisal is capturable");
        True(store.TryCommitNpcExperienceAppraisal(ticket, Result(ticket.Request, fear: 2)),
            "commoner may receive bounded positive fear proposal");
        Equal(10, store.ReadNpcRelationship(personId, "player").StoredInterpersonalFear,
            "commoner appraisal fear is committed");
    }

    private static void NativeWriteRejected()
    {
        WorldStateStore store = NewStore(out string personId);
        store.GetOrCreateNpcPersonality(personId, 17, NpcSocialBackground.Noble);
        SetRole(store, personId, NpcSocialRank.HighAuthority);
        var target = store.GetOrCreate(21, new[] { 201 }, 1).Households[0].Residents[0].Id;
        long ledgerRevision = store.NpcRelationshipLedgerRevision;
        True(!CommitFear(store, personId, "protected-write", 100, 5),
            "authoritative store rejects positive player fear delta");
        Equal(ledgerRevision, store.NpcRelationshipLedgerRevision, "rejected write leaves ledger revision unchanged");
        True(store.ReadNpcRelationship(personId, "player") == null, "rejected write does not create row or history");

        NpcExperienceEvent npcEvidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
            "receipt:npc-target-fear", "episode:role-fear", "npc-target-fear", NpcExperienceEventKind.Healing,
            101, NpcResponsibleActorAttribution.Known, target);
        var npcProposal = new NpcRelationshipLedgerUpdateProposal
        {
            ExpectedLedgerRevision = store.NpcRelationshipLedgerRevision,
            ObserverId = personId,
            TargetId = target,
            RelationshipUpdate = new NpcRelationshipUpdateProposal
            {
                ExpectedRevision = 0,
                Evidence = npcEvidence,
                InterpersonalFearDelta = 5
            }
        };
        True(store.TryCommitNpcRelationship(npcProposal, npcProposal.ExpectedLedgerRevision),
            "rank policy does not suppress fear directed at another NPC");
        Equal(5, store.ReadNpcRelationship(personId, target).InterpersonalFear,
            "NPC-directed fear remains intact");
    }

    private static void AppraisalFearRejected()
    {
        WorldStateStore store = NewStore(out string personId);
        store.GetOrCreateNpcPersonality(personId, 17, NpcSocialBackground.Noble);
        SetRole(store, personId, NpcSocialRank.Nobility);
        EnqueueNative(store, personId, "protected-appraisal", 100);
        True(store.TryCaptureNpcExperienceAppraisal(personId, 100, out NpcExperienceAppraisalTicket ticket), "protected appraisal is capturable");
        True(ticket.Request.PlayerFearSuppressed, "appraisal request carries protected player fear policy internally");
        Equal(store.ReadNpcRole(personId).Revision, ticket.Request.ExpectedRoleRevision,
            "appraisal request captures role revision without changing model JSON schema");

        string grammar = NpcExperienceAppraisalContract.ResponseGrammar(ticket.Request);
        True(grammar.Contains("fear-delta ::= \"0\""), "protected-rank grammar allows only fear delta zero");
        NpcExperienceAppraisal parsed;
        True(!NpcExperienceAppraisalContract.TryParseResponse(ticket.Request,
            Result(ticket.Request, fear: 1), out parsed), "parser rejects positive fear even if grammar is bypassed");
        True(!store.TryCommitNpcExperienceAppraisal(ticket, Result(ticket.Request, fear: 1)),
            "commit rejects direct positive fear response");
        Equal(1, store.ReadPendingNpcExperiences(personId, 100).Count, "rejected appraisal leaves event pending");
        True(store.TryCommitNpcExperienceAppraisal(ticket, Result(ticket.Request)),
            "protected-rank zero fear appraisal remains valid");
        True(store.ReadNpcRelationship(personId, "player") == null,
            "zero-fear protected appraisal does not create a relationship row");
    }

    private static void RoleChangeRejectsStaleAppraisal()
    {
        WorldStateStore store = NewStore(out string personId);
        store.GetOrCreateNpcPersonality(personId, 17, NpcSocialBackground.Noble);
        SetRole(store, personId, NpcSocialRank.Nobility);
        EnqueueNative(store, personId, "stale-role", 100);
        True(store.TryCaptureNpcExperienceAppraisal(personId, 100, out NpcExperienceAppraisalTicket ticket), "appraisal is captured with current role");
        long capturedRevision = ticket.Request.ExpectedRoleRevision;
        True(store.RecordNpcRoleEvidence(personId, NpcSocialRank.Commoner, NpcCombatRole.Unknown,
            NpcServiceBinding.Unknown, "native:rank:changed", null, null), "role rank changes with sourced evidence");
        True(store.ReadNpcRole(personId).Revision > capturedRevision, "new role revision is greater than captured revision");

        True(!store.TryCommitNpcExperienceAppraisal(ticket, Result(ticket.Request)),
            "role change invalidates even a zero-fear response bound to earlier role facts");
        Equal(1, store.ReadPendingNpcExperiences(personId, 100).Count, "stale response leaves event pending");
        Equal(0L, store.NpcRelationshipLedgerRevision, "stale response does not mutate relationship ledger");
    }

    private static WorldStateStore NewStore(out string personId)
    {
        WorldStateStore store = new WorldStateStore(109);
        personId = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static void SetRole(WorldStateStore store, string personId, NpcSocialRank rank)
    {
        True(store.RecordNpcRoleEvidence(personId, rank, NpcCombatRole.Unknown, NpcServiceBinding.Unknown,
            "native:rank:test", null, null), "native rank evidence recorded");
    }

    private static bool CommitFear(WorldStateStore store, string personId, string outcomeId, double at, int fear)
    {
        int remaining = fear;
        int index = 0;
        while (remaining > 0)
        {
            int delta = Math.Min(10, remaining);
            string id = outcomeId + ":" + index;
            NpcExperienceEvent evidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                "receipt:" + id, "episode:role-fear", id, NpcExperienceEventKind.Healing, at + index,
                NpcResponsibleActorAttribution.Known, "player");
            var proposal = new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = store.NpcRelationshipLedgerRevision,
                ObserverId = personId,
                TargetId = "player",
                RelationshipUpdate = new NpcRelationshipUpdateProposal
                {
                    ExpectedRevision = store.ReadNpcRelationship(personId, "player") == null ? 0 :
                        store.ReadNpcRelationship(personId, "player").Revision,
                    Evidence = evidence,
                    InterpersonalFearDelta = delta
                }
            };
            if (!store.TryCommitNpcRelationship(proposal, proposal.ExpectedLedgerRevision)) return false;
            remaining -= delta;
            index++;
        }
        return fear >= 0;
    }

    private static void EnqueueNative(WorldStateStore store, string personId, string outcomeId, double at)
    {
        var evidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
            "receipt:" + outcomeId, "episode:role-fear", outcomeId, NpcExperienceEventKind.Healing, at,
            NpcResponsibleActorAttribution.Known, "player");
        True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence), "historical native event imported");
    }

    private static string Result(NpcExperienceAppraisalRequest request, int fear = 0)
    {
        return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
            ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
            ",\"saveBranchId\":\"" + request.SaveBranchId + "\",\"observerId\":\"" + request.ObserverId +
            "\",\"subjectPersonId\":\"" + request.SubjectPersonId + "\",\"evidenceDeduplicationKey\":\"" +
            request.EvidenceDeduplicationKey + "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId +
            "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? "") +
            "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision +
            ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision +
            ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision +
            ",\"moodDelta\":0,\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":" + fear +
            ",\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
    }

    private static void True(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
    }
}
