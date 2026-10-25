using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var tests = new List<(string, Action)>
        {
            ("steal requires exact owner and transferable item", StealAdmission),
            ("protected targets, missing adapter and consent refuse", ProtectedAndConsent),
            ("locks require current skill and exact source revision", LockAndRevision),
            ("relationship and witnessed crime risk are bounded", RiskAndWitness),
            ("steal completes only after exact transfer and crime receipt", StealReceipt),
            ("search and investigation require distinct observed effects", KnowledgeReceipts),
            ("wrong target, action, witness or receipt cannot complete", WrongReceipt),
            ("completed operation and receipt cannot replay", Replay),
            ("persisted replay entries reject reused operation and receipt", ReloadReplay),
            ("illicit search records exact crime; legal search does not", SearchCrime),
            ("steal requires observed item ownership transfer", ItemOwnership)
        };
        int failed = 0;
        foreach (var test in tests)
        {
            try { test.Item2(); Console.WriteLine("PASS " + test.Item1); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Item1 + ": " + ex.Message); }
        }
        Console.WriteLine((tests.Count - failed) + "/" + tests.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static NpcCovertRequest Request(NpcCovertActionKind kind = NpcCovertActionKind.Steal)
    { return new NpcCovertRequest { OperationId = "op:1", ActorId = "npc:rogue", TargetId = "chest:1",
        OwnerId = "npc:owner", ItemId = kind == NpcCovertActionKind.Steal ? "item:1" : null,
        Kind = kind, TargetRevision = 7, CrimeRevision = 3, WitnessRevision = 2 }; }

    private static NpcCovertFacts Facts()
    { return new NpcCovertFacts { ActorAlive = true, TargetExists = true, OwnerId = "npc:owner",
        ItemExists = true, ItemTransferable = true, TargetRevision = 7, CrimeRevision = 3,
        WitnessRevision = 2, AdapterAvailable = true, DutyAvailable = true, ActorConsents = true,
        Relationship = 50, MinimumRelationship = 10, LockDifficulty = 20, LockSkill = 30,
        Risk = 4, MaximumRisk = 8, WitnessIds = new[] { "npc:watcher" } }; }

    private static NpcCovertAuthorization Admit(NpcCovertRequest request, NpcCovertFacts facts)
    { NpcCovertAuthorization auth; True(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "admission refused"); return auth; }

    private static NpcCovertOutcome Outcome(NpcCovertRequest request)
    { return new NpcCovertOutcome { OperationId = request.OperationId, ReceiptId = "receipt:1", ActorId = request.ActorId,
        TargetId = request.TargetId, OwnerId = request.OwnerId, ItemId = request.ItemId, Kind = request.Kind,
        TargetRevisionBefore = 7, TargetRevisionAfter = 8, CrimeRevisionBefore = 3, CrimeRevisionAfter = 4,
        WitnessRevision = 2, WitnessIds = new[] { "npc:watcher" }, NativeAdapterExecuted = true,
        EffectKind = NpcCovertEffectKind.ItemTransferred, EffectId = "transfer:1", SourceQuantityBefore = 1,
        SourceQuantityAfter = 0, ActorQuantityBefore = 0, ActorQuantityAfter = 1,
        CrimeIncidentId = "crime:1", ItemOwnerBefore = "npc:owner", ItemOwnerAfter = "npc:rogue" }; }

    private static void StealAdmission()
    { var request = Request(); var facts = Facts(); Admit(request, facts); NpcCovertAuthorization auth;
      facts.OwnerId = "npc:other"; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "wrong owner admitted");
      facts = Facts(); facts.ItemTransferable = false; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "untransferable item admitted");
      facts = Facts(); request.ItemId = null; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "missing item admitted"); }

    private static void ProtectedAndConsent()
    { var request = Request(); var facts = Facts(); NpcCovertAuthorization auth;
      facts.QuestProtected = true; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "quest target admitted");
      facts = Facts(); facts.AdapterAvailable = false; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "no adapter admitted");
      facts = Facts(); facts.ActorConsents = false; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "no actor consent admitted");
      facts = Facts(); facts.OwnerConsents = true; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "consensual taking admitted as theft"); }

    private static void LockAndRevision()
    { var request = Request(); var facts = Facts(); NpcCovertAuthorization auth;
      facts.LockSkill = 19; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "insufficient lock skill admitted");
      facts = Facts(); facts.TargetRevision = 8; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "stale target admitted");
      facts = Facts(); facts.WitnessRevision = 3; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "stale witnesses admitted"); }

    private static void RiskAndWitness()
    { var request = Request(); var facts = Facts(); NpcCovertAuthorization auth;
      facts.Relationship = 9; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "relationship bound bypassed");
      facts = Facts(); facts.Risk = 9; False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "risk bound bypassed");
      facts = Facts(); facts.WitnessIds = new[] { "npc:watcher", "npc:watcher" };
      False(NpcCovertActionContract.TryAuthorize(request, facts, out auth), "duplicate witnesses admitted"); }

    private static void StealReceipt()
    { var request = Request(); var auth = Admit(request, Facts()); var outcome = Outcome(request);
      True(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "exact transfer refused");
      outcome.ActorQuantityAfter = 0; False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "no acquisition completed");
      outcome = Outcome(request); outcome.CrimeIncidentId = null;
      False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "crime consequence absent"); }

    private static void KnowledgeReceipts()
    { foreach (var kind in new[] { NpcCovertActionKind.Search, NpcCovertActionKind.Investigate })
      { var request = Request(kind); var facts = Facts(); facts.OwnerConsents = true; var auth = Admit(request, facts);
        var outcome = Outcome(request); outcome.ItemId = null; outcome.ItemOwnerBefore = null;
        outcome.ItemOwnerAfter = null; outcome.EffectKind = kind == NpcCovertActionKind.Search
            ? NpcCovertEffectKind.SearchResultRecorded : NpcCovertEffectKind.ClueRecorded;
        outcome.EffectId = kind == NpcCovertActionKind.Search ? "search:1" : "clue:1";
        outcome.SourceQuantityBefore = outcome.SourceQuantityAfter = outcome.ActorQuantityBefore = outcome.ActorQuantityAfter = 0;
        outcome.CrimeRevisionAfter = 3; outcome.CrimeIncidentId = null;
        True(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "knowledge effect refused");
        outcome.EffectId = null; False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "empty effect admitted"); } }

    private static void WrongReceipt()
    { var request = Request(); var auth = Admit(request, Facts()); var outcome = Outcome(request);
      outcome.NativeAdapterExecuted = false; False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "generic success admitted");
      outcome = Outcome(request); outcome.TargetId = "chest:other"; False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "wrong target admitted");
      outcome = Outcome(request); outcome.WitnessIds = new[] { "npc:other" }; False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "wrong witness admitted");
      outcome = Outcome(request); outcome.Kind = NpcCovertActionKind.Search; False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "wrong action admitted"); }

    private static void Replay()
    { var request = Request(); var auth = Admit(request, Facts()); var outcome = Outcome(request);
      var ledger = new NpcCovertReceiptLedger();
      True(ledger.TryComplete(auth, outcome), "first receipt refused");
      False(ledger.TryComplete(auth, outcome), "same receipt replayed");
      var other = Request(); other.OperationId = "op:2"; var auth2 = Admit(other, Facts());
      outcome = Outcome(other); False(ledger.TryComplete(auth2, outcome), "receipt reused for another operation");
      True(ledger.HasCompleted("op:1"), "completion not retained"); }

    private static void ReloadReplay()
    { var request = Request(); var auth = Admit(request, Facts()); var outcome = Outcome(request);
      var ledger = new NpcCovertReceiptLedger(); True(ledger.TryComplete(auth, outcome), "initial receipt refused");
      var restored = new NpcCovertReceiptLedger(ledger.Snapshot());
      False(restored.TryComplete(auth, outcome), "restored operation replayed");
      var other = Request(); other.OperationId = "op:2"; var auth2 = Admit(other, Facts());
      False(restored.TryComplete(auth2, Outcome(other)), "restored receipt reused"); }

    private static void SearchCrime()
    { var request = Request(NpcCovertActionKind.Search); var facts = Facts();
      var auth = Admit(request, facts); var outcome = Outcome(request);
      outcome.EffectKind = NpcCovertEffectKind.SearchResultRecorded; outcome.ItemId = null;
      outcome.ItemOwnerBefore = null; outcome.ItemOwnerAfter = null;
      outcome.SourceQuantityBefore = outcome.SourceQuantityAfter = outcome.ActorQuantityBefore = outcome.ActorQuantityAfter = 0;
      True(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "illicit search receipt refused");
      facts.OwnerConsents = true; auth = Admit(request, facts);
      False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "legal search recorded crime"); }

    private static void ItemOwnership()
    { var request = Request(); var auth = Admit(request, Facts()); var outcome = Outcome(request);
      outcome.ItemOwnerBefore = "npc:other";
      False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "wrong source owner transferred");
      outcome = Outcome(request); outcome.ItemOwnerAfter = "npc:other";
      False(NpcCovertActionContract.MatchesObservedOutcome(auth, outcome), "item did not reach actor"); }

    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void False(bool value, string message) { if (value) throw new Exception(message); }
}
