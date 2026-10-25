using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>Result for one frozen Qwen case. Ticket remains available in-process for production parsing.</summary>
    public sealed class SelectedAppraisalCapturedCase
    {
        public string CaseId { get; internal set; }
        public string PairId { get; internal set; }
        public string Status { get; internal set; }
        public string BlockReason { get; internal set; }
        public string ExportPath { get; internal set; }
        public string PromptSha256 { get; internal set; }
        public string CompletionRequestBody { get; internal set; }
        public string ExpectedModel { get; internal set; }
        public string Prompt { get; internal set; }
        public string SnapshotBeforeCapture { get; internal set; }
        public string SnapshotAfterCapture { get; internal set; }
        public NpcExperienceAppraisalRequest Request { get; internal set; }
        public NpcExperienceAppraisalTicket Ticket { get; internal set; }
        public WorldStateStore Store { get; internal set; }
    }

    public sealed class SelectedAppraisalCaptureReport
    {
        public string SuiteId { get; internal set; }
        public string OutputDirectory { get; internal set; }
        public int PlannedPairArms { get; internal set; }
        public int Exported { get; internal set; }
        public int Blocked { get; internal set; }
        public IReadOnlyList<SelectedAppraisalCapturedCase> Cases { get; internal set; }
        public string ReportPath { get; internal set; }
    }

    /// <summary>
    /// Exports frozen Qwen pair arms through fresh synthetic stores and the real capture ticket builder.
    /// Synthetic receipts are explicitly labelled; this exporter never runs a model or writes a game save.
    /// </summary>
    public static class SelectedAppraisalCapture
    {
        private const string ObserverId = "capture-suite-resident";
        private const string PlayerId = "player";
        private const double CapturedAt = 1000;
        private const uint WorldSeed = 20261022;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        { WriteIndented = true, IncludeFields = true };
        private static readonly JsonSerializerOptions JsonLineOptions = new JsonSerializerOptions
        { IncludeFields = true };

        public static SelectedAppraisalCaptureReport PreparePairs(string suitePath, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(suitePath)) throw new ArgumentException("Suite path is required.", nameof(suitePath));
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
            using (JsonDocument suite = JsonDocument.Parse(File.ReadAllText(suitePath)))
            {
                JsonElement root = suite.RootElement;
                string suiteId = RequiredString(root, "suiteId");
                string expectedModel = RequiredString(RequiredObject(root.GetProperty("selectedModels"), "qwenAppraisal"), "modelPath");
                JsonElement pairs = RequiredArray(root, "qwenAppraisalPairs");
                Directory.CreateDirectory(outputDirectory);
                var cases = new List<SelectedAppraisalCapturedCase>();
                var records = new List<string>();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                int expected = 0;
                foreach (JsonElement pair in pairs.EnumerateArray())
                {
                    string pairId = RequiredString(pair, "pairId");
                    foreach (string armName in new[] { "control", "treatment" })
                    {
                        JsonElement arm = RequiredObject(pair, armName);
                        string caseId = RequiredString(arm, "caseId");
                        if (!ids.Add(caseId)) throw new InvalidDataException("Duplicate selected appraisal case ID: " + caseId);
                        if (caseId == "QW-P21-T")
                        {
                            cases.Add(new SelectedAppraisalCapturedCase { CaseId = caseId, PairId = pairId,
                                Status = "blocked", BlockReason = "Frozen no-show negative-control arm lacks required wait or player-absence proof; exclude from inference." });
                            continue;
                        }
                        expected++;
                        SelectedAppraisalCapturedCase captured = CaptureArm(pair, arm, armName, pairId, outputDirectory, expectedModel, suitePath);
                        cases.Add(captured);
                        if (captured.Status == "exported")
                        {
                            Dictionary<string, string> sourceHashes = SourceHashes(suitePath);
                            records.Add(JsonSerializer.Serialize(new
                            {
                                caseId = captured.CaseId,
                                lane = "qwen",
                                requestBody = captured.CompletionRequestBody,
                                expectedModel,
                                sourceHashes,
                                promptSha256 = captured.PromptSha256,
                                request = captured.Request,
                                snapshotBeforeCapture = JsonDocument.Parse(captured.SnapshotBeforeCapture).RootElement,
                                snapshotAfterCapture = JsonDocument.Parse(captured.SnapshotAfterCapture).RootElement,
                                syntheticSource = true,
                                nativeGameEligibility = false
                            }, JsonLineOptions));
                        }
                    }
                }
                var report = new SelectedAppraisalCaptureReport
                {
                    SuiteId = suiteId,
                    OutputDirectory = Path.GetFullPath(outputDirectory),
                    PlannedPairArms = expected,
                    Exported = cases.Count(x => x.Status == "exported"),
                    Blocked = cases.Count(x => x.Status == "blocked"),
                    Cases = cases.AsReadOnly()
                };
                report.ReportPath = Path.Combine(outputDirectory, "capture-report.json");
                WriteNewLines(Path.Combine(outputDirectory, "requests.jsonl"), records);
                WriteNew(report.ReportPath, JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    suiteId,
                    syntheticSourceNotice = "Synthetic historical saved-inbox and typed-speech fixture; not a live DFU observation.",
                    nativeGameEligibility = false,
                    modelCalls = false,
                    plannedPairArms = report.PlannedPairArms,
                    exported = report.Exported,
                    blocked = report.Blocked,
                    cases = cases.Select(x => new { x.CaseId, x.PairId, x.Status, x.BlockReason, x.ExportPath, x.PromptSha256 }),
                    pairDiffs = cases.Where(x => x.PairId != null).GroupBy(x => x.PairId, StringComparer.Ordinal)
                        .Select(group => PairDiff(group.Key, group.ToList()))
                }, JsonOptions));
                return report;
            }
        }

        /// <summary>Offline structural check for capture-backed factors, duty ownership, and first-output preservation.</summary>
        public static void SelfTest(string suitePath, string outputDirectory)
        {
            SelectedAppraisalCaptureReport report = PreparePairs(suitePath, outputDirectory);
            if (report.PlannedPairArms != 41 || report.Cases.Count != 42 ||
                report.Cases.Count(x => x.CaseId == "QW-P21-T" && x.Status == "blocked") != 1 ||
                report.Cases.Count(x => x.Status == "exported") != 41)
                throw new InvalidOperationException("Frozen suite must yield 41 planned arms plus one held negative-control record.");
            Dictionary<string, SelectedAppraisalCapturedCase> byId = report.Cases.ToDictionary(x => x.CaseId, StringComparer.Ordinal);
            var activeTaskDuty = byId["QW-P07-T"].Request.MotivationContext.Duties.Single();
            if (!activeTaskDuty.EndsAtGameTime.HasValue || activeTaskDuty.EndsAtGameTime.Value <= byId["QW-P07-T"].Request.CapturedGameTime)
                throw new InvalidOperationException("Active-task contrast must not capture an already expired task.");
            if (!byId["QW-P15-C"].Prompt.Contains("nativeValuation=unknown") ||
                !byId["QW-P15-T"].Prompt.Contains("nativeValuation=known,nativeGoldValue=35"))
                throw new InvalidOperationException("Valuation contrast lost its production impact details.");
            AssertPairFactor(byId, "QW-P01-C", "QW-P01-T", (a, b) => a.Request.CurrentMood != b.Request.CurrentMood);
            AssertPairFactor(byId, "QW-P06-C", "QW-P06-T", (a, b) => a.Request.MotivationContext.Duties.Count == 0 && b.Request.MotivationContext.Duties.Count == 1);
            AssertPairFactor(byId, "QW-P07-C", "QW-P07-T", (a, b) => a.Request.MotivationContext.Duties.Count == 0 && b.Request.MotivationContext.Duties.Count == 1);
            AssertPairFactor(byId, "QW-P09-C", "QW-P09-T", (a, b) => a.Request.MotivationContext.Duties.Count == 0 && b.Request.MotivationContext.Duties.Count == 1);
            AssertPairFactor(byId, "QW-P18-C", "QW-P18-T", (a, b) => !a.Request.ImmediateAffinityDelta.HasValue &&
                b.Request.ImmediateAffinityDelta == 0 && a.Request.AlreadyAppliedEffectsMask == NpcExperienceAppliedEffectsMask.None &&
                b.Request.AlreadyAppliedEffectsMask == NpcExperienceAppliedEffectsMask.Affinity);
            AssertPairFactor(byId, "QW-P19-C", "QW-P19-T", (a, b) => a.Request.MotivationContext.Duties.Count == 0 && b.Request.MotivationContext.Duties.Count == 1);
            AssertPairFactor(byId, "QW-P20-C", "QW-P20-T", (a, b) => Personality(a).EmotionalStability != Personality(b).EmotionalStability);
            string firstOutput = File.ReadAllText(Path.Combine(outputDirectory, "requests.jsonl"));
            bool refusedOverwrite = false;
            try { PreparePairs(suitePath, outputDirectory); }
            catch (IOException) { refusedOverwrite = true; }
            if (!refusedOverwrite || File.ReadAllText(Path.Combine(outputDirectory, "requests.jsonl")) != firstOutput)
                throw new InvalidOperationException("Exporter did not preserve first-output bytes on rerun.");
        }

        private static void AssertPairFactor(Dictionary<string, SelectedAppraisalCapturedCase> cases,
            string controlId, string treatmentId,
            Func<SelectedAppraisalCapturedCase, SelectedAppraisalCapturedCase, bool> intendedFactor)
        {
            if (!cases.TryGetValue(controlId, out SelectedAppraisalCapturedCase control) ||
                !cases.TryGetValue(treatmentId, out SelectedAppraisalCapturedCase treatment) ||
                control.Status != "exported" || treatment.Status != "exported") return;
            NpcExperienceAppraisalRequest a = control.Request;
            NpcExperienceAppraisalRequest b = treatment.Request;
            if (!intendedFactor(control, treatment) || a.ObserverId != b.ObserverId || a.SubjectPersonId != b.SubjectPersonId ||
                a.CanonicalEvidenceId != b.CanonicalEvidenceId || a.CapturedGameTime != b.CapturedGameTime ||
                a.ExpectedMoodRevision != b.ExpectedMoodRevision || a.ExpectedAffinityRevision != b.ExpectedAffinityRevision ||
                a.ExpectedKnowledgeRevision != b.ExpectedKnowledgeRevision || a.ExpectedMotivationRevision != b.ExpectedMotivationRevision ||
                a.Prompt == b.Prompt)
                throw new InvalidOperationException("Pair projection changed outside intended factor or failed to change prompt: " + control.PairId);
        }

        private static NpcPersonalityProfile Personality(SelectedAppraisalCapturedCase value)
        {
            NpcPersonalityProfile result = value.Store.Snapshot().NpcPersonalities
                .FirstOrDefault(x => x.PersonId == value.Request.ObserverId);
            if (result == null) throw new InvalidOperationException("Capture fixture did not retain the personality profile.");
            return result;
        }

        private static object PairDiff(string pairId, List<SelectedAppraisalCapturedCase> pair)
        {
            SelectedAppraisalCapturedCase control = pair.FirstOrDefault(x => x.CaseId.EndsWith("-C", StringComparison.Ordinal));
            SelectedAppraisalCapturedCase treatment = pair.FirstOrDefault(x => x.CaseId.EndsWith("-T", StringComparison.Ordinal));
            return new
            {
                pairId,
                controlCaseId = control == null ? null : control.CaseId,
                treatmentCaseId = treatment == null ? null : treatment.CaseId,
                controlStatus = control == null ? "missing" : control.Status,
                treatmentStatus = treatment == null ? "missing" : treatment.Status,
                blockReasons = pair.Where(x => x.Status == "blocked").Select(x => new { x.CaseId, x.BlockReason }),
                nuisanceBindings = control != null && treatment != null && control.Request != null && treatment.Request != null
                    ? (object)new
                    {
                        saveBranchIdDiffers = control.Request.SaveBranchId != treatment.Request.SaveBranchId,
                        evidenceDeduplicationKeyDiffers = control.Request.EvidenceDeduplicationKey != treatment.Request.EvidenceDeduplicationKey,
                        canonicalEvidenceIdDiffers = control.Request.CanonicalEvidenceId != treatment.Request.CanonicalEvidenceId,
                        promptDiffers = control.Request.Prompt != treatment.Request.Prompt,
                        moodRevisionDiffers = control.Request.ExpectedMoodRevision != treatment.Request.ExpectedMoodRevision,
                        affinityRevisionDiffers = control.Request.ExpectedAffinityRevision != treatment.Request.ExpectedAffinityRevision,
                        motivationRevisionDiffers = control.Request.ExpectedMotivationRevision != treatment.Request.ExpectedMotivationRevision,
                        knowledgeRevisionDiffers = control.Request.ExpectedKnowledgeRevision != treatment.Request.ExpectedKnowledgeRevision
                    } : null,
                semanticProjection = control != null && treatment != null && control.Request != null && treatment.Request != null
                    ? (object)new
                    {
                        control = SemanticProjection(control),
                        treatment = SemanticProjection(treatment)
                    } : null
            };
        }

        private static object SemanticProjection(SelectedAppraisalCapturedCase value)
        {
            NpcExperienceAppraisalRequest request = value.Request;
            NpcDirectedRelationshipView relationship = value.Store.ReadNpcRelationship(request.ObserverId, PlayerId, request.CapturedGameTime);
            NpcPersonalityProfile personality = Personality(value);
            return new
            {
                observerId = request.ObserverId,
                subjectPersonId = request.SubjectPersonId,
                capturedGameTime = request.CapturedGameTime,
                currentMood = request.CurrentMood,
                currentAffinity = request.CurrentAffinity,
                immediateAffinityDelta = request.ImmediateAffinityDelta,
                alreadyAppliedEffectsMask = request.AlreadyAppliedEffectsMask.ToString(),
                expectedRelationshipRevision = request.ExpectedRelationshipRevision,
                relationship = relationship == null ? null : new
                {
                    relationship.TrustKnown, relationship.ReliabilityTrust, relationship.TrustConfidence,
                    relationship.ProjectedResentment, relationship.ProjectedInterpersonalFear
                },
                duties = request.MotivationContext == null ? Array.Empty<string>() : request.MotivationContext.Duties
                    .Select(x => string.Join("|", x.Id, x.Kind, x.Scope, x.Status, x.StartsAtGameTime.ToString("R", CultureInfo.InvariantCulture),
                        x.EndsAtGameTime.HasValue ? x.EndsAtGameTime.Value.ToString("R", CultureInfo.InvariantCulture) : "null")),
                personality = new { personality.EmotionalStability },
                knownFamilyIds = request.KnownFamilyIds,
                knownSpeakerRankId = request.KnownSpeakerRankId,
                conversationContext = request.ConversationContext
            };
        }

        private static SelectedAppraisalCapturedCase CaptureArm(JsonElement pair, JsonElement arm,
            string armName, string pairId, string outputDirectory, string expectedModel, string suitePath)
        {
            string caseId = RequiredString(arm, "caseId");
            var result = new SelectedAppraisalCapturedCase { CaseId = caseId, PairId = pairId, Status = "blocked" };
            try
            {
                var store = NewStore(out string observerId);
                double capturedAt = CaptureTime(pairId);
                ApplyPairFactor(store, pair, arm, armName, observerId, caseId);
                if (!TryQueueEvidence(store, pair, arm, armName, observerId, caseId, out string block))
                { result.BlockReason = block; return result; }

                string before = JsonSerializer.Serialize(store.Snapshot(), JsonOptions);
                if (!store.TryCaptureNpcExperienceAppraisal(observerId, capturedAt, out NpcExperienceAppraisalTicket ticket) ||
                    ticket == null || ticket.Request == null || string.IsNullOrWhiteSpace(ticket.Request.Prompt))
                { result.BlockReason = "Production TryCaptureNpcExperienceAppraisal rejected synthetic evidence or owner state."; return result; }
                string after = JsonSerializer.Serialize(store.Snapshot(), JsonOptions);
                string prompt = ticket.Request.Prompt;
                var connection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 300000);
                DialogueCompletion completion = LocalDialogueCompletion.BuildAppraisal(connection, ticket.Request);
                result.Status = "exported";
                result.Prompt = prompt;
                result.PromptSha256 = Sha256(prompt);
                result.CompletionRequestBody = completion.JsonBody;
                result.SnapshotBeforeCapture = before;
                result.SnapshotAfterCapture = after;
                result.Request = ticket.Request;
                result.Ticket = ticket;
                result.Store = store;
                string path = Path.Combine(outputDirectory, SafeFile(caseId) + ".json");
                result.ExportPath = path;
                WriteNew(path, JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    suitePairId = pairId,
                    caseId,
                    arm = armName,
                    source = "synthetic historical saved-inbox events, observed speech receipts and ledgers",
                    nativeGameEligibility = false,
                    modelResponse = (string)null,
                    prompt,
                    lane = "qwen",
                    requestBody = completion.JsonBody,
                    expectedModel,
                    sourceHashes = SourceHashes(suitePath),
                    promptSha256 = result.PromptSha256,
                    request = ticket.Request,
                    snapshotBeforeCapture = JsonDocument.Parse(before).RootElement,
                    snapshotAfterCapture = JsonDocument.Parse(after).RootElement
                }, JsonOptions));
                return result;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is InvalidDataException || ex is NotSupportedException)
            {
                result.BlockReason = ex.Message;
                return result;
            }
        }

        private static WorldStateStore NewStore(out string observerId)
        {
            var store = new WorldStateStore(WorldSeed);
            SettlementPopulation settlement = store.GetOrCreate(17, new[] { 101, 102, 103, 104 }, 3);
            Resident resident = settlement.Households.SelectMany(x => x.Residents)
                .FirstOrDefault(x => x != null && x.IsAlive && x.AgeGroup == ResidentAgeGroup.Adult && !x.IsQuestProtected);
            if (resident == null) throw new InvalidDataException("Synthetic world did not generate eligible adult resident.");
            observerId = resident.Id;
            store.GetOrCreateNpcPersonality(observerId, resident.AppearanceSeed, NpcSocialBackground.Unknown);
            return store;
        }

        private static void ApplyPairFactor(WorldStateStore store, JsonElement pair, JsonElement arm,
            string armName, string observerId, string caseId)
        {
            string pairId = RequiredString(pair, "pairId");
            if (pairId.StartsWith("QW-P01", StringComparison.Ordinal))
            {
                int delta = RequiredInt(arm, "value") < 0 ? -10 : 10;
                long sequence = 0;
                for (int i = 0; i < 5; i++)
                    if (!store.TryCommitAcceptedSocialExchange(observerId, sequence, delta, 10 + i,
                        "day", "place", "synthetic receipt", "synthetic reply", out sequence))
                        throw new InvalidDataException("Could not create accepted mood receipt through store API.");
            }
            else if (pairId.StartsWith("QW-P02", StringComparison.Ordinal) || pairId.StartsWith("QW-P03", StringComparison.Ordinal) ||
                     pairId.StartsWith("QW-P04", StringComparison.Ordinal) || pairId.StartsWith("QW-P05", StringComparison.Ordinal))
            {
                bool known = pairId.StartsWith("QW-P02", StringComparison.Ordinal)
                    ? armName == "treatment" : pairId.StartsWith("QW-P03", StringComparison.Ordinal) ||
                      pairId.StartsWith("QW-P04", StringComparison.Ordinal) || pairId.StartsWith("QW-P05", StringComparison.Ordinal);
                int trust = pairId.StartsWith("QW-P02", StringComparison.Ordinal) ? 70 : 50;
                int resentment = pairId.StartsWith("QW-P05", StringComparison.Ordinal) && armName == "treatment" ? 65 : 0;
                int fear = pairId.StartsWith("QW-P04", StringComparison.Ordinal) && armName == "treatment" ? 55 : 0;
                SeedRelationship(store, observerId, caseId, known, trust, resentment, fear);
                if (pairId.StartsWith("QW-P03", StringComparison.Ordinal))
                    store.SetConversationBaseline(observerId, armName == "control" ? 70 : -30, 10);
            }
            else if (pairId.StartsWith("QW-P20", StringComparison.Ordinal))
            {
                WorldState snapshot = store.Snapshot();
                int index = snapshot.NpcPersonalities.FindIndex(x => x.PersonId == observerId);
                NpcPersonalityProfile original = NpcPersonalityProfile.CopyValidated(snapshot.NpcPersonalities[index]);
                // Both arms use the same valid sparse V3 profile; only the tested axis differs.
                var replacement = new NpcPersonalityProfile { PersonId = original.PersonId, Version = 3,
                    Seed = original.Seed, Background = original.Background,
                    RandomTraitsSuppressed = original.RandomTraitsSuppressed,
                    Patience = "patient", Curiosity = "practical",
                    EmotionalStability = armName == "treatment" ? "volatile" : "resilient" };
                snapshot.NpcPersonalities[index] = NpcPersonalityProfile.CopyValidated(replacement);
                store.Restore(snapshot);
            }
        }

        private static void SeedRelationship(WorldStateStore store, string observerId, string caseId,
            bool knownTrust, int trust, int resentment, int fear)
        {
            int trustDelta = knownTrust ? Math.Max(1, trust - 50) : 0;
            int count = Math.Max(knownTrust ? (trustDelta + 9) / 10 : 1,
                Math.Max((resentment + 9) / 10, (fear + 9) / 10));
            if (trustDelta == 0 && resentment == 0 && fear == 0) return;
            for (int i = 0; i < count; i++)
            {
                NpcExperienceEvent basis = NpcExperienceEvent.FromNativeOutcome(observerId, observerId,
                    "fixture-rel-receipt:" + caseId + ":" + i.ToString(CultureInfo.InvariantCulture),
                    "fixture-rel-episode:" + caseId, "fixture-rel-outcome:" + caseId + ":" + i.ToString(CultureInfo.InvariantCulture),
                    NpcExperienceEventKind.CombatHelp, 10 + i, NpcResponsibleActorAttribution.Known, PlayerId);
                var update = new NpcRelationshipUpdateProposal { ExpectedRevision =
                    (store.ReadNpcRelationship(observerId, PlayerId)?.Revision ?? 0), Evidence = basis,
                    TrustDelta = Chunk(trustDelta, i), ResentmentDelta = Chunk(resentment, i),
                    InterpersonalFearDelta = Chunk(fear, i) };
                var proposal = new NpcRelationshipLedgerUpdateProposal { ExpectedLedgerRevision = store.NpcRelationshipLedgerRevision,
                    ObserverId = observerId, TargetId = PlayerId, RelationshipUpdate = update };
                if (!store.TryCommitNpcRelationship(proposal, proposal.ExpectedLedgerRevision))
                    throw new InvalidDataException("Could not seed directed relationship through production ledger API.");
            }
        }

        private static int Chunk(int value, int index)
        { return Math.Max(0, Math.Min(10, value - index * 10)); }

        private static bool TryQueueEvidence(WorldStateStore store, JsonElement pair, JsonElement arm,
            string armName, string observerId, string caseId, out string block)
        {
            block = null;
            string pairId = RequiredString(pair, "pairId");
            string eventName = RequiredString(pair, "event");
            if ((pairId.StartsWith("QW-P06", StringComparison.Ordinal) || pairId.StartsWith("QW-P07", StringComparison.Ordinal) ||
                 pairId.StartsWith("QW-P19", StringComparison.Ordinal)) &&
                !TryQueueDuty(store, pairId, arm, armName, observerId, pairId, out block)) return false;
            if (pairId.StartsWith("QW-P08", StringComparison.Ordinal) || pairId.StartsWith("QW-P09", StringComparison.Ordinal) ||
                pairId.StartsWith("QW-P10", StringComparison.Ordinal))
                if (!TryQueueServiceDuty(store, pairId, armName, observerId, caseId, out block)) return false;

            double at = 100;
            NpcExperienceEvent evidence;
            if (pairId.StartsWith("QW-P11", StringComparison.Ordinal) || pairId.StartsWith("QW-P12", StringComparison.Ordinal) ||
                pairId.StartsWith("QW-P13", StringComparison.Ordinal) || pairId.StartsWith("QW-P18", StringComparison.Ordinal))
            {
                string utterance = arm.TryGetProperty("utterance", out JsonElement text) ? text.GetString() : "I will hurt you.";
                evidence = NpcExperienceEvent.FromObservedSpeech(observerId, observerId, OpaqueId("speech-receipt", pairId),
                    OpaqueId("speech-episode", pairId), OpaqueId("speech-utterance", pairId), at, PlayerId);
                var turn = NpcObservedConversationTurn.Create(observerId, OpaqueId("speech-episode", pairId),
                    PlayerId, OpaqueId("speech-receipt", pairId), utterance, at);
                NpcObservedSpeechReceipt speech = NpcObservedSpeechReceipt.Create(evidence, turn);
                NpcExperienceAppliedEffectsMask mask = NpcExperienceAppliedEffectsMask.None;
                if (pairId.StartsWith("QW-P18", StringComparison.Ordinal) && armName == "treatment")
                {
                    // Historical saved-speech fixture: exact zero affinity was already
                    // applied in the synthetic past; this is not a live native source.
                    speech = speech.WithAppliedAffinity(0);
                    mask = NpcExperienceAppliedEffectsMask.Affinity;
                }
                if (!HistoricalNpcExperienceFixture.TryRestoreObservedSpeech(store, speech, mask))
                { block = "Historical synthetic saved-speech fixture failed validation."; return false; }
            }
            else if (pairId.StartsWith("QW-P17", StringComparison.Ordinal) || pairId.StartsWith("QW-P21", StringComparison.Ordinal))
            {
                bool noShow = pairId.StartsWith("QW-P17", StringComparison.Ordinal)
                    ? armName == "treatment" : armName == "control";
                if (!TryCreateAppointmentOutcome(store, observerId, OpaqueId("appointment-outcome", pairId), noShow, out evidence, out block)) return false;
                bool queued = store.ReadPendingNpcExperiences(observerId, 10)
                    .Any(x => x.DeduplicationKey == evidence.DeduplicationKey);
                if (!queued) { block = "Appointment observation did not queue its canonical outcome in the production inbox."; return false; }
            }
            else
            {
                NpcExperienceEventKind kind = eventName == "Healing" ? NpcExperienceEventKind.Healing :
                    eventName == "Injury" ? NpcExperienceEventKind.Injury : NpcExperienceEventKind.CombatHelp;
                NpcResponsibleActorAttribution attribution = NpcResponsibleActorAttribution.Known;
                string actor = PlayerId;
                if (pairId.StartsWith("QW-P14", StringComparison.Ordinal) && armName == "control")
                { attribution = NpcResponsibleActorAttribution.Unknown; actor = null; }
                else if (pairId.StartsWith("QW-P15", StringComparison.Ordinal)) kind = NpcExperienceEventKind.GiftReceived;
                else if (pairId.StartsWith("QW-P16", StringComparison.Ordinal)) kind = armName == "control" ? NpcExperienceEventKind.CombatHelp : NpcExperienceEventKind.Injury;
                NpcExperienceImpactDetails impact = pairId.StartsWith("QW-P15", StringComparison.Ordinal)
                    ? NpcExperienceImpactDetails.ForItemTransfer(1,
                    armName == "treatment" ? NpcExperienceNativeValuationStatus.Known : NpcExperienceNativeValuationStatus.Unknown,
                        armName == "treatment" ? 35 : 0) : null;
                evidence = NpcExperienceEvent.FromNativeOutcome(observerId, observerId,
                    OpaqueId("native-receipt", pairId), OpaqueId("native-episode", pairId),
                    OpaqueId("native-outcome", pairId), kind, at, attribution, actor, impact);
                if (!HistoricalNpcExperienceFixture.TryRestoreNativeOutcome(store, evidence))
                { block = "Historical synthetic saved-inbox fixture failed validation."; return false; }
            }
            return true;
        }

        private static bool TryQueueDuty(WorldStateStore store, string pairId, JsonElement arm,
            string armName, string observerId, string caseId, out string block)
        {
            block = null;
            if (pairId.StartsWith("QW-P06", StringComparison.Ordinal))
            {
                if (armName == "treatment")
                {
                    var appointment = new AppointmentRecord { Id = "fixture-appointment:" + caseId, ResidentId = observerId,
                        Purpose = "meet", Place = new AppointmentPlace { Kind = "building", SettlementId = 17, BuildingKey = 101 },
                        StartsAt = 1100, EndsAt = 1200, GraceSeconds = 0, Importance = 1 };
                    if (!store.TryReserveAppointment(appointment, 1000, out string reason))
                    { block = "Production appointment API rejected owner-bound synthetic appointment: " + reason; return false; }
                }
            }
            else if (pairId.StartsWith("QW-P07", StringComparison.Ordinal) && armName == "treatment")
            {
                var task = new NpcTaskPlan { Id = "fixture-task:" + caseId, ProposalId = "fixture-task-proposal:" + caseId,
                    ResidentId = observerId, Phase = NpcTaskPhase.Ready, Revision = 1, AcceptedAt = 10, UpdatedAt = 10, DeadlineAt = 1500 };
                task.Steps.Add(new NpcTaskStep { Kind = NpcActionKind.Wait, DurationSeconds = 30,
                    Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = PlayerId } });
                if (!SyntheticConversationCommandFixture.TryAcceptTask(store, task, out string reason))
                { block = "Production task API rejected owner-bound synthetic task: " + reason; return false; }
            }
            else if (pairId.StartsWith("QW-P19", StringComparison.Ordinal) && armName == "treatment")
            {
                var action = new NpcActionJob { Id = "fixture-action:" + caseId, ProposalId = "fixture-action-proposal:" + pairId,
                    ResidentId = observerId, Kind = NpcActionKind.Wait,
                    Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = PlayerId },
                    Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 10, UpdatedAt = 10, DeadlineAt = 1100 };
                if (!SyntheticConversationCommandFixture.TryAcceptAction(store, action, out string reason))
                { block = "Production action API rejected owner-bound synthetic action: " + reason; return false; }
            }
            return true;
        }

        private static bool TryQueueServiceDuty(WorldStateStore store, string pairId, string armName,
            string observerId, string caseId, out string block)
        {
            block = null;
            bool present = pairId.StartsWith("QW-P08", StringComparison.Ordinal)
                ? armName == "treatment"
                : pairId.StartsWith("QW-P09", StringComparison.Ordinal) ||
                  pairId.StartsWith("QW-P10", StringComparison.Ordinal);
            if (!present) return true;
            string owner = observerId;
            double startAt = 0;
            int fundedAmount = 120;
            int rate = 120;
            if (pairId.StartsWith("QW-P09", StringComparison.Ordinal) && armName == "control")
            {
                string foreign = store.Snapshot().Settlements.SelectMany(x => x.Households)
                    .SelectMany(x => x.Residents).Where(x => x != null && x.IsAlive && x.Id != observerId)
                    .Select(x => x.Id).FirstOrDefault();
                if (foreign == null) { block = "Blocked: synthetic world has no second known resident for foreign-owner contract."; return false; }
                owner = foreign;
            }
            if (pairId.StartsWith("QW-P10", StringComparison.Ordinal))
            {
                startAt = armName == "treatment" ? 90000 : 0;
                fundedAmount = 120;
                rate = 120;
            }
            string contractId = "fixture-contract:" + OpaqueId("service", pairId);
            string payer = PlayerId;
            if (!NpcServiceContractTerms.TryCreate(contractId, payer, owner, "fixture-home:" + owner,
                NpcServiceRole.Fighter, NpcServiceCoverage.Follow | NpcServiceCoverage.Combat, rate,
                false, NpcServiceRiskScope.OrdinaryCombat, Array.Empty<NpcServiceProtectedTarget>(),
                NpcServiceRestTreatment.RestNotIncluded, NpcServiceResupplyResponsibility.SeparatelyNegotiated,
                Array.Empty<NpcServiceReservedObligation>(), id => id == payer || id == owner,
                home => home == "fixture-home:" + owner, (id, category, revision) => true,
                (id, revision, kind, start, end) => true, out NpcServiceContractTerms terms))
            { block = "Production service terms rejected synthetic owner contract."; return false; }
            var funding = NpcServiceTransferReceipt.Create(OpaqueId("funding", pairId), contractId,
                NpcServiceTransferKind.Funding, payer, "contract:" + contractId + ":escrow", fundedAmount, 0);
            NpcServiceContractWriteTicket ticket = store.CaptureNpcServiceContractWriteTicket();
            string error = null;
            if (!NpcServiceContractLedger.TryReserve(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
                terms, funding, out NpcServiceContractLedger reserved) ||
                !store.TryCommitNpcServiceContracts(ticket, reserved, new[] { funding },
                    Array.Empty<NpcServiceRenewalAuthorization>(), out error))
            { block = "Production service reserve transition rejected synthetic funding receipt: " + error; return false; }
            ticket = store.CaptureNpcServiceContractWriteTicket();
            NpcServiceContract current = ticket.SourceLedger.Read(contractId);
            if (!current.TryStart(current.Revision, startAt, out NpcServiceContract active) ||
                !NpcServiceContractLedger.TryReplace(ticket.SourceLedger, ticket.ExpectedLedgerRevision, active,
                    out NpcServiceContractLedger started) ||
                !store.TryCommitNpcServiceContracts(ticket, started, Array.Empty<NpcServiceTransferReceipt>(),
                    Array.Empty<NpcServiceRenewalAuthorization>(), out error))
            { block = "Production service start transition rejected synthetic service state: " + error; return false; }
            return true;
        }

        private static bool TryCreateAppointmentOutcome(WorldStateStore store, string observerId, string caseId,
            bool noShow, out NpcExperienceEvent evidence, out string block)
        {
            evidence = null; block = null;
            string id = "fixture-outcome-appointment:" + caseId;
            var appointment = new AppointmentRecord { Id = id, ResidentId = observerId, Purpose = "meet",
                Place = new AppointmentPlace { Kind = "building", SettlementId = 17, BuildingKey = 101 },
                StartsAt = 100, EndsAt = 200, GraceSeconds = 0, PatienceSeconds = 600, Importance = 1 };
            if (!store.TryReserveAppointment(appointment, 0, out string reason))
            { block = "Production appointment API rejected fixture booking: " + reason; return false; }
            if (noShow)
            {
                double deadline = AppointmentTiming.ResolutionDeadline(store.FindAppointment(id));
                for (double time = deadline - 30; time < deadline; time += 10)
                    store.ObserveAppointment(id, time, true, false, false);
                store.ObserveAppointment(id, deadline, true, false, false);
                store.ObserveAppointment(id, deadline + 1, false, false, false);
            }
            else
            {
                store.ObserveAppointment(id, 100, true, false, false);
                store.ObserveAppointment(id, 110, true, true, false);
            }
            AppointmentRecord saved = store.FindAppointment(id);
            if (saved == null || !saved.OutcomeRecorded || !NpcAppointmentExperience.TryCreate(saved, out evidence))
            { block = "Production appointment observation API did not create a supported outcome receipt."; return false; }
            // Appointment observation queues its canonical receipt itself; do not enqueue a duplicate alias.
            return true;
        }

        private static JsonElement RequiredArray(JsonElement item, string name)
        { if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Missing suite array: " + name); return value; }
        private static JsonElement RequiredObject(JsonElement item, string name)
        { if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Missing suite object: " + name); return value; }
        private static string RequiredString(JsonElement item, string name)
        { if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Missing suite string: " + name); return value.GetString(); }
        private static int RequiredInt(JsonElement item, string name)
        { if (!item.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int number)) throw new InvalidDataException("Missing suite integer: " + name); return number; }
        private static string SafeFile(string value)
        { foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_'); return value; }
        private static string Sha256(string value)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", string.Empty).ToLowerInvariant(); }
        private static void WriteNew(string path, string contents)
        { using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(contents); }
        private static string OpaqueId(string kind, string pairId)
        { return "fixture-" + kind + ":" + Sha256(pairId).Substring(0, 20); }
        private static double CaptureTime(string pairId)
        { return pairId.StartsWith("QW-P10", StringComparison.Ordinal) ? 100000 : CapturedAt; }
        private static Dictionary<string, string> SourceHashes(string suitePath)
        {
            string root = FindRepositoryRoot(Path.GetFullPath(suitePath));
            var paths = new[]
            {
                "src/PeopleOfTheIliacBay.Core/NpcExperienceCommit.cs",
                "src/PeopleOfTheIliacBay.Core/NpcExperienceAppraisal.cs",
                "src/PeopleOfTheIliacBay.Core/NpcExperienceInbox.cs",
                "src/PeopleOfTheIliacBay.Core/NpcMotivationConversationProjection.cs",
                "src/PeopleOfTheIliacBay.Core/NpcMotivationContextFormatter.cs",
                "src/PeopleOfTheIliacBay.Core/NpcPersonality.cs",
                "src/PeopleOfTheIliacBay.Core/LocalDialogueCompletion.cs",
                "src/PeopleOfTheIliacBay.Core/NpcObservedSpeechReceipt.cs",
                "src/PeopleOfTheIliacBay.Core/NpcAppointmentExperience.cs",
                "src/PeopleOfTheIliacBay.Core/Appointments.cs",
                "src/PeopleOfTheIliacBay.Core/NpcActions.cs",
                "src/PeopleOfTheIliacBay.Core/NpcTasks.cs",
                "src/PeopleOfTheIliacBay.Core/NpcTaskStore.cs",
                "src/PeopleOfTheIliacBay.Core/NpcServiceContract.cs",
                "src/PeopleOfTheIliacBay.Core/NpcServiceContractState.cs",
                "src/PeopleOfTheIliacBay.Core/NpcServiceContractStore.cs",
                "tools/NpcFactorEvaluation/HistoricalNpcExperienceFixture.cs",
                "tools/NpcFactorEvaluation/SyntheticConversationCommandFixture.cs",
                "tools/NpcFactorEvaluation/SelectedAppraisalCapture.cs",
                "work/selected-framework-contrast-prep/suite-v1.json"
            };
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string relative in paths)
            {
                string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) throw new FileNotFoundException("Pinned capture source file is missing.", path);
                using (SHA256 sha = SHA256.Create())
                    result.Add(relative, BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", string.Empty).ToLowerInvariant());
            }
            return result;
        }
        private static string FindRepositoryRoot(string path)
        {
            DirectoryInfo directory = new DirectoryInfo(Path.GetDirectoryName(path));
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "src", "PeopleOfTheIliacBay.Core", "NpcExperienceCommit.cs")))
                    return directory.FullName;
                directory = directory.Parent;
            }
            throw new DirectoryNotFoundException("Could not locate LivingCities repository root from suite path.");
        }
        private static void WriteNewLines(string path, IReadOnlyList<string> lines)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                foreach (string line in lines) writer.WriteLine(line);
        }
    }
}
