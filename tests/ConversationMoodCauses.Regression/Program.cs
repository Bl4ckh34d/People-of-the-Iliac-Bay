using PeopleOfTheIliacBay.Tests;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int assertions;
    private static readonly MethodInfo DescribeMethod = typeof(WorldStateStore).GetMethod(
        "DescribeRecentConversationMoodCauses", BindingFlags.Public | BindingFlags.Instance, null,
        new[] { typeof(string), typeof(double), typeof(int) }, null);

    private static int Main()
    {
        if (DescribeMethod == null)
        {
            Console.Error.WriteLine("RED: the public recorded-mood-cause read API is not implemented.");
            return 1;
        }

        var cases = new List<(string Name, Action Body)>
        {
            ("Recorded injury and healing report their real signed effects without leaking actor identity", InjuryAndHealingAreBoundedAndPrivate),
            ("Mood cause entries identify the subject, responsible actor category and native event time", SubjectActorAndEventTimeAreExplicit),
            ("Typed poison onset uses its native kind but never exposes the internal effect key", PoisonOnsetIsTypedAndPrivate),
            ("Pending and deferred native events are not reported as accepted mood influences", PendingAndDeferredRowsAreExcluded),
            ("A saturated zero applied effect is excluded even when the appraisal proposed a delta", SaturatedZeroEffectIsExcluded),
            ("Future event and update times cannot explain an earlier mood read", FutureEvidenceIsExcluded),
            ("Unknown, unrelated and dead observers cannot read another mood journal", ObserverMustBeKnownAndAlive),
            ("Restoring another save branch replaces the reported source history", CurrentRestoredBranchOwnsResults),
            ("Expired effects disappear and output stays within complete entry and character bounds", OutputIsBoundedAndWhole),
            ("Mood-cause reads leave the persisted inbox and mood unchanged", ReadDoesNotMutate),
            ("Invalid time and character bounds are rejected", InputsAreBounded)
        };

        int failed = 0;
        foreach (var test in cases)
        {
            try
            {
                test.Body();
                Console.WriteLine("PASS " + test.Name);
            }
            catch (Exception error)
            {
                failed++;
                Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message);
            }
        }

        Console.WriteLine("Conversation mood cause regressions: " + (cases.Count - failed) + "/" + cases.Count +
            " cases, " + assertions + " assertions.");
        return failed == 0 ? 0 : 1;
    }

    private static void InjuryAndHealingAreBoundedAndPrivate()
    {
        var store = CreateWorld(out string observerId, out string otherActorId);
        Commit(store, observerId, "injury", NpcExperienceEventKind.Injury, 100, 100, -6,
            NpcResponsibleActorAttribution.Known, otherActorId,
            NpcExperienceImpactDetails.ForHealthEpisode(90, 78, 100, 12, 0, 1));
        Commit(store, observerId, "healing", NpcExperienceEventKind.Healing, 110, 110, 5,
            NpcResponsibleActorAttribution.Unknown, null,
            NpcExperienceImpactDetails.ForHealthEpisode(40, 50, 100, 0, 10, 1));

        string context = Describe(store, observerId, 110);
        Check.True(context.Contains("injury", StringComparison.Ordinal), "the native injury kind is described");
        Check.True(context.Contains("healing/recovery", StringComparison.Ordinal), "healing is described as recovery");
        Check.True(context.Contains("native damage 12", StringComparison.Ordinal), "the observer's verified damage is included");
        Check.True(context.Contains("native recovery 10", StringComparison.Ordinal), "the observer's verified healing is included");
        Check.True(context.Contains("mood effect -6", StringComparison.Ordinal), "the accepted negative applied effect is included");
        Check.True(context.Contains("mood effect +5", StringComparison.Ordinal), "the accepted positive applied effect is included");
        Check.True(context.Contains("responsible actor unknown", StringComparison.Ordinal), "unknown responsibility stays explicit");
        Check.True(context.Contains("responsible actor: another person", StringComparison.Ordinal),
            "known non-player responsibility is distinguished without revealing identity");
        Check.True(!context.Contains(otherActorId, StringComparison.Ordinal), "internal actor identity is not exposed");
        Check.True(!context.Contains("player", StringComparison.OrdinalIgnoreCase), "unknown responsibility is never guessed to be the player");
        Check.True(context.IndexOf("healing/recovery", StringComparison.Ordinal) < context.IndexOf("injury", StringComparison.Ordinal),
            "the most recently applied influence appears first");
    }

    private static void PoisonOnsetIsTypedAndPrivate()
    {
        var store = CreateWorld(out string observerId, out _);
        Commit(store, observerId, "poison", NpcExperienceEventKind.StatusOnset, 200, 200, -3,
            NpcResponsibleActorAttribution.Unknown, null,
            NpcExperienceImpactDetails.ForStatusOnset(NpcExperienceStatusKind.Poison, "internal-poison-effect-key", 7));

        string context = Describe(store, observerId, 200);
        Check.True(context.Contains("you were poisoned", StringComparison.Ordinal), "known poison event is expressed as the observer's past experience");
        Check.True(context.Contains("responsible actor unknown", StringComparison.Ordinal), "the poisoner remains unknown");
        Check.True(!context.Contains("internal-poison-effect-key", StringComparison.Ordinal), "the native effect key stays private");
    }

    private static void SubjectActorAndEventTimeAreExplicit()
    {
        var otherSubjectStore = CreateWorld(out string observerId, out string otherPersonId);
        Commit(otherSubjectStore, observerId, "other-injury", NpcExperienceEventKind.Injury, 100, 100, -3,
            NpcResponsibleActorAttribution.Known, otherPersonId,
            NpcExperienceImpactDetails.ForHealthEpisode(90, 78, 100, 12, 0, 1), otherPersonId);
        string otherSubjectContext = Describe(otherSubjectStore, observerId, 100);
        Check.True(otherSubjectContext.Contains("another person: injury", StringComparison.Ordinal),
            "witnessed injury names the injured subject as another person");
        Check.True(!otherSubjectContext.Contains("you: injury", StringComparison.Ordinal),
            "another person's injury cannot be mistaken for the observer's injury");
        Check.True(!otherSubjectContext.Contains("native damage", StringComparison.Ordinal),
            "another person's private health details are not exposed");
        Check.True(otherSubjectContext.Contains("responsible actor: another person", StringComparison.Ordinal),
            "known non-player actor is distinguished without revealing identity");
        Check.True(otherSubjectContext.Contains("game-second 100", StringComparison.Ordinal),
            "native observation time is included");

        var playerStore = CreateWorld(out string playerObserverId, out _);
        Commit(playerStore, playerObserverId, "player-actor", NpcExperienceEventKind.Injury, 200, 200, -2,
            NpcResponsibleActorAttribution.Known, NpcDirectedRelationship.PlayerTargetId, null);
        string playerContext = Describe(playerStore, playerObserverId, 200);
        Check.True(playerContext.Contains("responsible actor: player", StringComparison.Ordinal),
            "known player responsibility is explicit");

        var selfStore = CreateWorld(out string selfObserverId, out _);
        Commit(selfStore, selfObserverId, "self-actor", NpcExperienceEventKind.Injury, 300, 300, -2,
            NpcResponsibleActorAttribution.Known, selfObserverId, null);
        string selfContext = Describe(selfStore, selfObserverId, 300);
        Check.True(selfContext.Contains("responsible actor: self", StringComparison.Ordinal),
            "known observer responsibility is explicit");

        var unknownStore = CreateWorld(out string unknownObserverId, out _);
        Commit(unknownStore, unknownObserverId, "unknown-actor", NpcExperienceEventKind.Injury, 400, 400, -2,
            NpcResponsibleActorAttribution.Unknown, null, null);
        Commit(unknownStore, unknownObserverId, "not-applicable-actor", NpcExperienceEventKind.Healing, 401, 401, 2,
            NpcResponsibleActorAttribution.NotApplicable, null, null);
        string unknownContext = Describe(unknownStore, unknownObserverId, 401);
        Check.True(unknownContext.Contains("responsible actor unknown", StringComparison.Ordinal),
            "unknown responsibility remains explicit");
        Check.True(unknownContext.Contains("responsibility not applicable", StringComparison.Ordinal),
            "inapplicable responsibility is distinct from unknown");
    }

    private static void PendingAndDeferredRowsAreExcluded()
    {
        var store = CreateWorld(out string observerId, out _);
        Enqueue(store, observerId, "pending", NpcExperienceEventKind.Healing, 300,
            NpcResponsibleActorAttribution.Unknown, null, null);
        Check.Equal(string.Empty, Describe(store, observerId, 300), "pending evidence has no accepted mood effect");

        var saved = store.Snapshot();
        var deferred = saved.NpcExperienceInbox.Entries.Single();
        deferred.Status = NpcExperienceInboxStatus.Deferred;
        deferred.Revision = 2;
        deferred.UpdatedAt = 301;
        deferred.DeferralReason = NpcExperienceDeferralReason.AppraisalAttemptLimit;
        deferred.DeferralAttempts = 5;
        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        Check.Equal(string.Empty, Describe(restored, observerId, 301), "terminally deferred evidence has no applied appraisal");
    }

    private static void SaturatedZeroEffectIsExcluded()
    {
        var store = CreateWorld(out string observerId, out _);
        var saturatedMood = store.Snapshot();
        saturatedMood.ConversationMoods.Add(new ConversationMood
        {
            PersonId = observerId,
            Value = 100,
            AcceptedTurnSequence = 0,
            HasTimeAnchor = false,
            UpdatedAt = 0,
            Revision = 0,
            RecoveryHalfLifeSeconds = 0
        });
        store.Restore(saturatedMood);
        Commit(store, observerId, "saturated-healing", NpcExperienceEventKind.Healing, 400, 400, 5,
            NpcResponsibleActorAttribution.Unknown, null, null);

        var row = store.Snapshot().NpcExperienceInbox.Entries.Single();
        Check.Equal(5, row.Appraisal.MoodDelta, "the appraiser proposed a positive change");
        Check.Equal(0, row.Appraisal.AppliedMoodDelta, "saturation accepted no actual change");
        Check.Equal(string.Empty, Describe(store, observerId, 400), "a zero applied effect is not a mood cause");
    }

    private static void FutureEvidenceIsExcluded()
    {
        var futureUpdateStore = CreateWorld(out string observerId, out _);
        Commit(futureUpdateStore, observerId, "future-update", NpcExperienceEventKind.Injury, 500, 510, -4,
            NpcResponsibleActorAttribution.Unknown, null, null);
        Check.Equal(string.Empty, Describe(futureUpdateStore, observerId, 509), "an application timestamp after now is excluded");
        Check.True(Describe(futureUpdateStore, observerId, 510).Contains("injury", StringComparison.Ordinal),
            "the same application is visible at its recorded time");

        var futureObservedSave = futureUpdateStore.Snapshot();
        var futureObserved = futureObservedSave.NpcExperienceInbox.Entries.Single();
        futureObserved.ObservedGameTime = 511;
        futureObserved.UpdatedAt = 512;
        var futureObservedStore = new WorldStateStore(0);
        futureObservedStore.Restore(futureObservedSave);
        Check.Equal(string.Empty, Describe(futureObservedStore, observerId, 510), "a future observed native outcome is excluded");
    }

    private static void ObserverMustBeKnownAndAlive()
    {
        var store = CreateWorld(out string observerId, out string otherPersonId);
        Check.Equal(string.Empty, Describe(store, "unknown-observer", 600), "unknown identities have no recall");
        Check.Equal(string.Empty, Describe(store, otherPersonId, 600), "another observer has no access to this journal");

        Commit(store, observerId, "dead-owner", NpcExperienceEventKind.Injury, 590, 590, -2,
            NpcResponsibleActorAttribution.Unknown, null, null);
        var saved = store.Snapshot();
        saved.Settlements.SelectMany(x => x.Households).SelectMany(x => x.Residents)
            .Single(x => x.Id == observerId).IsAlive = false;
        var deadStore = new WorldStateStore(0);
        deadStore.Restore(saved);
        Check.Equal(string.Empty, Describe(deadStore, observerId, 600), "dead observers cannot receive current conversation context");
    }

    private static void CurrentRestoredBranchOwnsResults()
    {
        var store = CreateWorld(out string observerId, out _);
        var beforeOutcome = store.Snapshot();
        Commit(store, observerId, "branch-injury", NpcExperienceEventKind.Injury, 700, 700, -4,
            NpcResponsibleActorAttribution.Unknown, null, null);
        Check.True(Describe(store, observerId, 700).Contains("injury", StringComparison.Ordinal), "the current branch reports its saved outcome");

        store.Restore(beforeOutcome);
        Check.Equal(string.Empty, Describe(store, observerId, 700), "restoring a save without that outcome cannot reuse stale recall");
    }

    private static void OutputIsBoundedAndWhole()
    {
        var store = CreateWorld(out string observerId, out _);
        Commit(store, observerId, "cause-1", NpcExperienceEventKind.Injury, 800, 800, -1,
            NpcResponsibleActorAttribution.Unknown, null, null);
        Commit(store, observerId, "cause-2", NpcExperienceEventKind.Healing, 801, 801, 1,
            NpcResponsibleActorAttribution.Unknown, null, null);
        Commit(store, observerId, "cause-3", NpcExperienceEventKind.CombatHelp, 802, 802, 1,
            NpcResponsibleActorAttribution.Unknown, null, null);
        Commit(store, observerId, "cause-4", NpcExperienceEventKind.GiftReceived, 803, 803, 1,
            NpcResponsibleActorAttribution.Unknown, null, null);

        string full = Describe(store, observerId, 803, 600);
        Check.True(full.Length <= 600, "default maximum prevents oversized context");
        Check.Equal(3, Count(full, "estimated remaining mood effect"), "at most three whole native outcomes are included");
        int separator = full.IndexOf(" | ", StringComparison.Ordinal);
        Check.True(separator > 0, "the complete first record has an independently testable boundary");
        string firstRecord = full.Substring(0, separator);
        Check.Equal(firstRecord, Describe(store, observerId, 803, firstRecord.Length), "a character limit emits complete entries only");
        Check.Equal(string.Empty, Describe(store, observerId, 803, firstRecord.Length - 1), "a record that does not fit is omitted whole");
        Check.Equal(string.Empty, Describe(store, observerId, 803, 1), "a tiny bound yields no partial prefix");

        double halfLife = store.ConversationMoodHalfLifeSeconds(observerId);
        Check.Equal(string.Empty, Describe(store, observerId, 803 + 20 * halfLife), "expired residual effects do not linger indefinitely");
    }

    private static void ReadDoesNotMutate()
    {
        var store = CreateWorld(out string observerId, out _);
        Commit(store, observerId, "pure-read", NpcExperienceEventKind.Injury, 900, 900, -3,
            NpcResponsibleActorAttribution.Unknown, null, null);
        var before = store.Snapshot();
        var beforeMood = store.CaptureConversationMood(observerId, 900);

        string first = Describe(store, observerId, 900);
        string second = Describe(store, observerId, 900);
        var after = store.Snapshot();
        var afterMood = store.CaptureConversationMood(observerId, 900);

        Check.Equal(first, second, "repeated reads are stable");
        Check.Equal(before.NpcExperienceInbox.Revision, after.NpcExperienceInbox.Revision, "inbox revision remains unchanged");
        Check.Equal(before.NpcExperienceInbox.Entries.Single().Status, after.NpcExperienceInbox.Entries.Single().Status,
            "the consumed receipt remains unchanged");
        Check.Equal(before.NpcExperienceInbox.Entries.Single().Appraisal.AppliedMoodDelta,
            after.NpcExperienceInbox.Entries.Single().Appraisal.AppliedMoodDelta, "the applied effect remains unchanged");
        Check.Equal(beforeMood.Value, afterMood.Value, "reading causes does not change mood");
        Check.Equal(beforeMood.Revision, afterMood.Revision, "reading causes does not advance the mood revision");
    }

    private static void InputsAreBounded()
    {
        var store = CreateWorld(out string observerId, out _);
        Check.Throws<ArgumentOutOfRangeException>(() => Describe(store, observerId, double.NaN), "NaN time is rejected");
        Check.Throws<ArgumentOutOfRangeException>(() => Describe(store, observerId, double.PositiveInfinity), "infinite time is rejected");
        Check.Throws<ArgumentOutOfRangeException>(() => Describe(store, observerId, -1), "negative time is rejected");
        Check.Throws<ArgumentOutOfRangeException>(() => Describe(store, observerId, 1, 0), "zero character bound is rejected");
        Check.Throws<ArgumentOutOfRangeException>(() => Describe(store, observerId, 1, 601), "caller cannot raise the hard output bound");
    }

    private static WorldStateStore CreateWorld(out string observerId, out string otherPersonId)
    {
        var store = new WorldStateStore(4201);
        observerId = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        otherPersonId = store.GetOrCreate(21, new[] { 201 }, 1).Households[0].Residents[0].Id;
        Check.True(!string.Equals(observerId, otherPersonId, StringComparison.Ordinal), "fixture residents have distinct native identities");
        return store;
    }

    private static void Enqueue(WorldStateStore store, string observerId, string outcome,
        NpcExperienceEventKind eventKind, double observedAt, NpcResponsibleActorAttribution actorAttribution,
        string actorId, NpcExperienceImpactDetails details, string subjectPersonId = null)
    {
        var evidence = NpcExperienceEvent.FromNativeOutcome(observerId, subjectPersonId ?? observerId,
            "receipt:" + outcome, "episode:" + outcome, "outcome:" + outcome, eventKind, observedAt,
            actorAttribution, actorId, details);
        Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence), "historical native evidence enters the save-owned inbox");
    }

    private static NpcExperienceInboxEntry Commit(WorldStateStore store, string observerId, string outcome,
        NpcExperienceEventKind eventKind, double observedAt, double capturedAt, int moodDelta,
        NpcResponsibleActorAttribution actorAttribution, string actorId, NpcExperienceImpactDetails details,
        string subjectPersonId = null)
    {
        Enqueue(store, observerId, outcome, eventKind, observedAt, actorAttribution, actorId, details, subjectPersonId);
        Check.True(store.TryCaptureNpcExperienceAppraisal(observerId, capturedAt, out var ticket), "actual inbox row can be captured");
        Check.True(store.TryCommitNpcExperienceAppraisal(ticket, Response(ticket.Request, moodDelta)), "actual appraisal commits once");
        return store.Snapshot().NpcExperienceInbox.Entries.Single(x => x.OutcomeId == "outcome:" + outcome);
    }

    private static string Response(NpcExperienceAppraisalRequest request, int moodDelta)
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
            ",\"moodDelta\":" + moodDelta + ",\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0," +
            "\"fearDelta\":0,\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\"," +
            "\"conversationOption\":\"NoAction\"}";
    }

    private static string Describe(WorldStateStore store, string observerId, double now, int maxChars = 600)
    {
        try
        {
            return (string)DescribeMethod.Invoke(store, new object[] { observerId, now, maxChars });
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static class Check
    {
        public static void True(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            assertions++;
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(message + " (expected " + expected + ", got " + actual + ")");
        }

        public static void Throws<TException>(Action action, string message) where TException : Exception
        {
            assertions++;
            try { action(); }
            catch (TException) { return; }
            throw new InvalidOperationException(message + " (expected " + typeof(TException).Name + ")");
        }
    }
}
