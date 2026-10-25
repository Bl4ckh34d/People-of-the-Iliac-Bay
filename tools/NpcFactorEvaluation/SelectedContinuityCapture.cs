using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>
    /// Isolated, production-boundary continuity runner for the two frozen Qwen episodes.
    /// Keep one instance alive for the episode. Tickets are intentionally process-local; callers
    /// persist the exact prompt and response externally and must stop on every rejected commit.
    /// </summary>
    public sealed class SelectedContinuityCapture
    {
        public const string EpisodeOneId = "QW-C01-two-turn-help";
        public const string EpisodeTwoId = "QW-C02-three-turn-threat-and-reopen";

        private static readonly string[] EpisodeOneCases = { "QW-C01-T1", "QW-C01-T2" };
        private static readonly string[] EpisodeOneSpeech = { null, "Thank you for helping me." };
        private static readonly string[] EpisodeTwoCases = {
            "QW-C02-T1", "QW-C02-T2", "QW-C02-T3", "QW-C02-R1", "QW-C02-R2", "QW-C02-R3"
        };
        private static readonly string[] EpisodeTwoSpeech = {
            "I will hurt you.",
            "Someone said, 'I will hurt you.'",
            "If someone threatened you, what would you do?",
            "I will hurt you again.",
            "Someone said, 'I will hurt you again.'",
            "If someone threatened you again, what would you do?"
        };
        private static readonly JsonSerializerOptions SnapshotJson = new JsonSerializerOptions
        { IncludeFields = true };

        private readonly string episodeId;
        private readonly string[] caseIds;
        private readonly string[] speech;
        private readonly WorldStateStore store;
        private readonly string observerId;
        private NpcObservedSpeechSession speechSession;
        private NpcExperienceAppraisalTicket currentTicket;
        private SelectedContinuityPrompt currentPrompt;
        private int nextIndex;
        private bool stopped;
        private string stopReason;

        private SelectedContinuityCapture(string episode)
        {
            if (episode == EpisodeOneId)
            {
                episodeId = episode;
                caseIds = EpisodeOneCases;
                speech = EpisodeOneSpeech;
            }
            else if (episode == EpisodeTwoId)
            {
                episodeId = episode;
                caseIds = EpisodeTwoCases;
                speech = EpisodeTwoSpeech;
            }
            else throw new ArgumentException("Unknown selected continuity episode.", nameof(episode));

            // This store is synthetic and never binds to a live save or game runtime.
            store = new WorldStateStore(0x51EC7EDu);
            observerId = store.GetOrCreate(9101, new[] { 910101 }, 1).Households[0].Residents[0].Id;
            if (string.IsNullOrWhiteSpace(observerId)) throw new InvalidOperationException("Synthetic resident identity was not created.");
            if (!EnqueueNextReceipt()) throw new InvalidOperationException("Could not restore the first synthetic historical saved-inbox receipt.");
        }

        public static SelectedContinuityCapture Start(string episodeId)
        { return new SelectedContinuityCapture(episodeId); }

        public string EpisodeId { get { return episodeId; } }
        public string ObserverId { get { return observerId; } }
        public bool IsStopped { get { return stopped; } }
        public string StopReason { get { return stopReason; } }
        public int AcceptedCommitCount { get { return nextIndex; } }
        public bool IsComplete { get { return !stopped && nextIndex == caseIds.Length && currentTicket == null; } }
        /// <summary>Current production request while capture ticket remains open. Exposes no ticket or store.</summary>
        public NpcExperienceAppraisalRequest CurrentRequest
        { get { return currentTicket == null ? null : currentTicket.Request; } }

        public bool BindCurrentSpeechClassification(NpcSpeechActClassification classification)
        {
            if (stopped || currentTicket == null || !store.TryBindNpcExperienceSpeechClassification(currentTicket, classification)) return false;
            var request = currentTicket.Request;
            currentPrompt = new SelectedContinuityPrompt(episodeId, caseIds[nextIndex], nextIndex + 1,
                request.Prompt, Sha256(request.Prompt), request.SaveBranchId, request.ObserverId,
                request.EvidenceDeduplicationKey, request.CanonicalEvidenceId, request.ExpectedMoodRevision,
                request.ExpectedAffinityRevision, request.ExpectedRelationshipRevision, request.ExpectedMotivationRevision,
                request.ExpectedKnowledgeRevision, store.CaptureNpcExperienceInboxRevision(),
                store.Snapshot().NpcExperienceInbox.Entries.Count, observerId);
            return true;
        }

        /// <summary>Offline startup gate. Exercises prompt idempotence, six production commits, reopen, and exact-once rejection.</summary>
        public static bool RunSelfTests(out string diagnostic)
        {
            diagnostic = null;
            try
            {
                var historical = Start(EpisodeOneId);
                var savedEvidence = historical.store.ReadPendingNpcExperiences(historical.observerId, 10);
                if (savedEvidence.Count != 1 || savedEvidence[0].Origin != NpcExperienceEventOrigin.NativeOutcome ||
                    historical.store.Snapshot().NpcExperienceInbox.Revision != 1 ||
                    HistoricalNpcExperienceFixture.TryRestoreNativeOutcome(historical.store, savedEvidence[0]) ||
                    historical.store.Snapshot().NpcExperienceInbox.Revision != 1)
                    return Fail(out diagnostic, "historical synthetic saved-inbox fixture was not exact-once");
                var run = Start(EpisodeTwoId);
                string originalBranch = null;
                string originalObserver = null;
                string lastResponse = null;
                for (int index = 0; index < EpisodeTwoCases.Length; index++)
                {
                    string caseId = EpisodeTwoCases[index];
                    if (!run.TryPrepareNext(caseId, out SelectedContinuityPrompt captured) || captured == null)
                        return Fail(out diagnostic, "capture failed for " + caseId);
                    if (run.CurrentRequest == null || run.CurrentRequest.Prompt != captured.Prompt)
                        return Fail(out diagnostic, "current request binding missing for " + caseId);
                    if (index == 0)
                    {
                        originalBranch = captured.SaveBranchId;
                        originalObserver = captured.ObserverId;
                        if (!run.TryPrepareNext(caseId, out SelectedContinuityPrompt replay) || replay == null ||
                            replay.Prompt != captured.Prompt || replay.PromptSha256 != captured.PromptSha256)
                            return Fail(out diagnostic, "same-ticket replay changed prompt bytes");
                    }
                    if (captured.NativeGameEvidence || captured.ObserverId != originalObserver)
                        return Fail(out diagnostic, "synthetic source or observer binding changed");
                    if (index == 3 && captured.SaveBranchId == originalBranch)
                        return Fail(out diagnostic, "reopen did not rotate production branch");

                    string label = index == 0 || index == 3 ? "DirectThreat" : index == 1 || index == 4 ? "ReportedThreat" : "HypotheticalThreat";
                    if (!NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.ForAppraisal(run.CurrentRequest), label, out var classification) ||
                        !run.BindCurrentSpeechClassification(classification) || !run.TryPrepareNext(caseId, out captured))
                        return Fail(out diagnostic, "controlled speech classification binding failed");
                    lastResponse = SelfTestResponse(captured, index);
                    SelectedContinuityCommit result = run.CommitCurrent(lastResponse);
                    if (!result.ParserAccepted || !result.CommitAccepted || result.CaseId != caseId)
                        return Fail(out diagnostic, "production parse/commit failed for " + caseId + ": " + result.Rejection);
                    if (run.CommitCurrent(lastResponse).CommitAccepted)
                        return Fail(out diagnostic, "duplicate response committed for " + caseId);
                }
                if (!run.IsComplete || run.AcceptedCommitCount != EpisodeTwoCases.Length)
                    return Fail(out diagnostic, "episode did not complete exactly six commits");
                var speechSource = run.store.Snapshot().NpcObservedSpeechSources;
                if (speechSource.Count != 1 || speechSource[0].PersonId != run.observerId ||
                    speechSource[0].LastIssuedOrdinal != EpisodeTwoCases.Length ||
                    speechSource[0].LastAcceptedOrdinal != EpisodeTwoCases.Length ||
                    speechSource[0].LastProjectedOrdinal != EpisodeTwoCases.Length ||
                    speechSource[0].Pending != null)
                    return Fail(out diagnostic, "current synthetic speech source did not survive reopen and project once per turn");
                diagnostic = "passed: historical synthetic saved-inbox fixture exact-once; repeated capture preserved prompt/hash; six production commits accepted; duplicate commits rejected; reopen rotated branch and preserved identity/profile";
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = "failed: " + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        /// <summary>Capture one pending fixture receipt; native-shaped history is restored, observed speech uses its typed store API.</summary>
        public bool TryPrepareNext(out SelectedContinuityPrompt prompt)
        {
            return TryPrepareNext(nextIndex < caseIds.Length ? caseIds[nextIndex] : null, out prompt);
        }

        /// <summary>Idempotent while ticket remains open: repeated calls return same captured prompt and hash.</summary>
        public bool TryPrepareNext(string expectedCaseId, out SelectedContinuityPrompt prompt)
        {
            prompt = null;
            if (stopped || nextIndex >= caseIds.Length || !string.Equals(expectedCaseId, caseIds[nextIndex], StringComparison.Ordinal)) return false;
            if (currentTicket != null)
            {
                prompt = currentPrompt;
                return prompt != null;
            }
            NpcExperienceAppraisalTicket ticket;
            double now = EventTime(nextIndex);
            if (!store.TryCaptureNpcExperienceAppraisal(observerId, now, out ticket) || ticket == null || ticket.Request == null)
            {
                Stop("production-capture-rejected");
                return false;
            }

            string expectedSpeech = speech[nextIndex];
            if ((expectedSpeech == null) == ticket.Request.RequiresSpeechAppraisal ||
                expectedSpeech != null && !string.Equals(expectedSpeech, ticket.Request.ObservedUtterance, StringComparison.Ordinal))
            {
                Stop("captured-evidence-does-not-match-planned-receipt");
                return false;
            }

            currentTicket = ticket;
            NpcExperienceAppraisalRequest request = ticket.Request;
            prompt = new SelectedContinuityPrompt(episodeId, caseIds[nextIndex], nextIndex + 1,
                request.Prompt, Sha256(request.Prompt), request.SaveBranchId, request.ObserverId,
                request.EvidenceDeduplicationKey, request.CanonicalEvidenceId, request.ExpectedMoodRevision,
                request.ExpectedAffinityRevision, request.ExpectedRelationshipRevision,
                request.ExpectedMotivationRevision, request.ExpectedKnowledgeRevision,
                store.CaptureNpcExperienceInboxRevision(), store.Snapshot().NpcExperienceInbox.Entries.Count,
                observerId);
            currentPrompt = prompt;
            return true;
        }

        /// <summary>
        /// Parses the exact raw response, then forwards those same bytes to production commit.
        /// Any rejection stops the episode; the ticket cannot be replaced or retried.
        /// </summary>
        public SelectedContinuityCommit CommitCurrent(string rawResponse)
        {
            if (stopped) return SelectedContinuityCommit.Rejected(episodeId,
                nextIndex < caseIds.Length ? caseIds[nextIndex] : null, "episode-stopped", nextIndex);
            if (currentTicket == null) return SelectedContinuityCommit.Rejected(episodeId,
                nextIndex < caseIds.Length ? caseIds[nextIndex] : null, "no-open-capture-ticket", nextIndex);

            NpcExperienceAppraisalTicket ticket = currentTicket;
            string caseId = caseIds[nextIndex];
            NpcExperienceAppraisal parsed;
            bool parserAccepted = NpcExperienceAppraisalContract.TryParseResponse(ticket.Request, rawResponse, out parsed);
            if (!parserAccepted)
            {
                currentTicket = null;
                currentPrompt = null;
                Stop("production-parser-rejected");
                return SelectedContinuityCommit.Rejected(episodeId, caseId, "production-parser-rejected", nextIndex);
            }

            bool committed = store.TryCommitNpcExperienceAppraisal(ticket, rawResponse);
            currentTicket = null;
            currentPrompt = null;
            if (!committed)
            {
                Stop("production-commit-rejected");
                return SelectedContinuityCommit.Rejected(episodeId, caseId, true,
                    "production-commit-rejected", nextIndex);
            }

            int committedTurn = nextIndex;
            nextIndex++;
            if (episodeId == EpisodeTwoId && nextIndex == 3)
            {
                // This is the isolated equivalent of closing and reopening the synthetic store.
                // The Core restore intentionally starts a new private experience branch; state and
                // history persist. A new prompt is captured after restore and receives a new branch ID.
                string serializedSnapshot = JsonSerializer.Serialize(store.Snapshot(), SnapshotJson);
                WorldState snapshot = JsonSerializer.Deserialize<WorldState>(serializedSnapshot, SnapshotJson);
                if (snapshot == null)
                {
                    Stop("synthetic-snapshot-serialization-failed");
                    return new SelectedContinuityCommit(episodeId, caseId, true, true,
                        "synthetic-snapshot-serialization-failed", committedTurn + 1,
                        store.CaptureNpcExperienceInboxRevision(), store.CaptureConversationMood(observerId).Revision,
                        RelationshipRevision());
                }
                string profileBeforeRestore = store.ReadNpcPersonality(observerId)?.Description();
                store.Restore(snapshot);
                // Restore invalidates process-local source tickets. The persisted ordinal survives.
                speechSession = null;
                string profileAfterRestore = store.ReadNpcPersonality(observerId)?.Description();
                if (profileBeforeRestore == null || !string.Equals(profileBeforeRestore, profileAfterRestore, StringComparison.Ordinal))
                {
                    Stop("synthetic-profile-changed-across-reopen");
                    return new SelectedContinuityCommit(episodeId, caseId, true, true,
                        "synthetic-profile-changed-across-reopen", committedTurn + 1,
                        store.CaptureNpcExperienceInboxRevision(), store.CaptureConversationMood(observerId).Revision,
                        RelationshipRevision());
                }
            }

            if (nextIndex < caseIds.Length && !EnqueueNextReceipt())
            {
                Stop("supported-receipt-enqueue-rejected");
                return new SelectedContinuityCommit(episodeId, caseId, true, true,
                    "next-supported-receipt-enqueue-rejected", committedTurn + 1,
                    store.CaptureNpcExperienceInboxRevision(), store.CaptureConversationMood(observerId).Revision,
                    RelationshipRevision());
            }

            return new SelectedContinuityCommit(episodeId, caseId, true, true, null,
                committedTurn + 1, store.CaptureNpcExperienceInboxRevision(),
                store.CaptureConversationMood(observerId).Revision, RelationshipRevision());
        }

        public WorldState CaptureSyntheticSnapshot()
        { return store.Snapshot(); }

        private bool EnqueueNextReceipt()
        {
            if (nextIndex >= caseIds.Length) return true;
            double at = EventTime(nextIndex);
            if (episodeId == EpisodeOneId && nextIndex == 0)
            {
                string suffix = "selected-continuity-c01-help";
                var evidence = NpcExperienceEvent.FromNativeOutcome(observerId, observerId,
                    "receipt:" + suffix, episodeId, "outcome:" + suffix,
                    NpcExperienceEventKind.CombatHelp, at,
                    NpcResponsibleActorAttribution.Known, NpcDirectedRelationship.PlayerTargetId);
                return HistoricalNpcExperienceFixture.TryRestoreNativeOutcome(store, evidence);
            }

            if (speech[nextIndex] == null) return false;
            // This isolated synthetic session issues a fresh source ticket for each current
            // utterance. It does not claim that a native game observer heard the text.
            if (speechSession == null && !store.TryBeginNpcObservedSpeechSession(observerId,
                    "selected-continuity:" + episodeId + ":" + nextIndex.ToString(CultureInfo.InvariantCulture),
                    out speechSession)) return false;
            NpcObservedSpeechTicket ticket;
            if (!store.TryIssueNpcObservedSpeech(speechSession, nextIndex, speech[nextIndex], at,
                    out ticket)) return false;
            NpcObservedSpeechAcceptanceResult accepted = store.TryAcceptNpcObservedSpeechAfterAppend(
                ticket, ticket.Receipt, NpcExperienceAppliedEffectsMask.None);
            return accepted == NpcObservedSpeechAcceptanceResult.Projected ||
                accepted == NpcObservedSpeechAcceptanceResult.AcceptedPending &&
                store.TryProjectPendingNpcObservedSpeech(observerId);
        }

        private long RelationshipRevision()
        {
            NpcDirectedRelationshipView relationship = store.ReadNpcRelationship(observerId,
                NpcDirectedRelationship.PlayerTargetId);
            return relationship == null ? 0 : relationship.Revision;
        }

        private static double EventTime(int index)
        { return 1000d + index * 10d; }

        private static bool Fail(out string diagnostic, string reason)
        { diagnostic = "failed: " + reason; return false; }

        private static string SelfTestResponse(SelectedContinuityPrompt captured, int index)
        {
            bool directThreat = index == 0 || index == 3;
            NpcExperienceSpeechActKind speechAct = directThreat
                ? NpcExperienceSpeechActKind.DirectThreat
                : index == 1 || index == 4
                    ? NpcExperienceSpeechActKind.ReportedThreat
                    : NpcExperienceSpeechActKind.HypotheticalThreat;
            return JsonSerializer.Serialize(new
            {
                schemaVersion = NpcExperienceAppraisalContract.SchemaVersion,
                policyVersion = NpcExperienceAppraisalContract.PolicyVersion,
                saveBranchId = captured.SaveBranchId,
                observerId = captured.ObserverId,
                subjectPersonId = captured.ObserverId,
                evidenceDeduplicationKey = captured.EvidenceDeduplicationKey,
                canonicalEvidenceId = captured.CanonicalEvidenceId,
                relationshipTargetId = NpcDirectedRelationship.PlayerTargetId,
                expectedMoodRevision = captured.ExpectedMoodRevision,
                expectedAffinityRevision = captured.ExpectedAffinityRevision,
                expectedRelationshipRevision = captured.ExpectedRelationshipRevision,
                moodDelta = directThreat ? -2 : 0,
                affinityDelta = directThreat ? -50 : 0,
                trustDelta = directThreat ? -25 : 0,
                resentmentDelta = directThreat ? 25 : 0,
                fearDelta = directThreat ? 25 : 0,
                speechActKind = speechAct.ToString(),
                speechTargetPersonId = directThreat ? captured.ObserverId : string.Empty,
                conversationOption = NpcExperienceConversationOption.NoAction.ToString()
            });
        }

        private void Stop(string reason)
        { stopped = true; stopReason = reason; currentTicket = null; currentPrompt = null; }

        private static string Sha256(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (byte item in bytes) result.Append(item.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }

    public sealed class SelectedContinuityPrompt
    {
        internal SelectedContinuityPrompt(string episodeId, string caseId, int turnNumber,
            string prompt, string promptSha256, string saveBranchId, string observerId,
            string evidenceKey, string canonicalEvidenceId, long moodRevision, long affinityRevision,
            long relationshipRevision, long motivationRevision, long knowledgeRevision,
            long inboxRevision, int inboxEntryCount, string syntheticResidentId)
        {
            EpisodeId = episodeId; CaseId = caseId; TurnNumber = turnNumber;
            Prompt = prompt; PromptSha256 = promptSha256; SaveBranchId = saveBranchId;
            ObserverId = observerId; EvidenceDeduplicationKey = evidenceKey;
            CanonicalEvidenceId = canonicalEvidenceId; ExpectedMoodRevision = moodRevision;
            ExpectedAffinityRevision = affinityRevision; ExpectedRelationshipRevision = relationshipRevision;
            ExpectedMotivationRevision = motivationRevision; ExpectedKnowledgeRevision = knowledgeRevision;
            InboxRevision = inboxRevision; InboxEntryCount = inboxEntryCount;
            SyntheticResidentId = syntheticResidentId; NativeGameEvidence = false;
        }
        public string EpisodeId { get; private set; }
        public string CaseId { get; private set; }
        public int TurnNumber { get; private set; }
        public string Prompt { get; private set; }
        public string PromptSha256 { get; private set; }
        public string SaveBranchId { get; private set; }
        public string ObserverId { get; private set; }
        public string SyntheticResidentId { get; private set; }
        public string EvidenceDeduplicationKey { get; private set; }
        public string CanonicalEvidenceId { get; private set; }
        public long ExpectedMoodRevision { get; private set; }
        public long ExpectedAffinityRevision { get; private set; }
        public long ExpectedRelationshipRevision { get; private set; }
        public long ExpectedMotivationRevision { get; private set; }
        public long ExpectedKnowledgeRevision { get; private set; }
        public long InboxRevision { get; private set; }
        public int InboxEntryCount { get; private set; }
        public bool NativeGameEvidence { get; private set; }
    }

    public sealed class SelectedContinuityCommit
    {
        internal SelectedContinuityCommit(string episodeId, string caseId, bool parserAccepted,
            bool commitAccepted, string rejection, int acceptedCommitCount, long inboxRevision,
            long moodRevision, long relationshipRevision)
        {
            EpisodeId = episodeId; CaseId = caseId; ParserAccepted = parserAccepted;
            CommitAccepted = commitAccepted; Rejection = rejection;
            AcceptedCommitCount = acceptedCommitCount; InboxRevision = inboxRevision;
            MoodRevision = moodRevision; RelationshipRevision = relationshipRevision;
        }
        public string EpisodeId { get; private set; }
        public string CaseId { get; private set; }
        public bool ParserAccepted { get; private set; }
        public bool CommitAccepted { get; private set; }
        public string Rejection { get; private set; }
        public int AcceptedCommitCount { get; private set; }
        public long InboxRevision { get; private set; }
        public long MoodRevision { get; private set; }
        public long RelationshipRevision { get; private set; }

        internal static SelectedContinuityCommit Rejected(string episodeId, string caseId,
            string reason, int acceptedCommitCount)
        { return Rejected(episodeId, caseId, false, reason, acceptedCommitCount); }

        internal static SelectedContinuityCommit Rejected(string episodeId, string caseId,
            bool parserAccepted, string reason, int acceptedCommitCount)
        { return new SelectedContinuityCommit(episodeId, caseId, parserAccepted, false, reason,
            acceptedCommitCount, -1, -1, -1); }
    }
}
