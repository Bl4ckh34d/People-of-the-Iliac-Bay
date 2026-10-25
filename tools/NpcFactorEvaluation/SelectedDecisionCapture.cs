using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>Captured first-output result for one offered-choice response.</summary>
    public sealed class SelectedDecisionParseResult
    {
        public bool Accepted { get; internal set; }
        public string Status { get; internal set; }
        public string DiagnosticCode { get; internal set; }
        public string SelectedOptionId { get; internal set; }
        public string ModelId { get; internal set; }
        public string RequestSha256 { get; internal set; }
        public string CoreBindingStatus { get; internal set; }
    }

    /// <summary>
    /// A single prepared decision request. Blocked cases have no request and cannot be parsed.
    /// The request and choices exist only when copied from a production builder.
    /// </summary>
    public sealed class SelectedDecisionCaptureCase
    {
        internal SelectedDecisionCaptureCase(string caseId, string pairId, string factor,
            string oracle, string status, string blockedReason, NpcDecisionRequest request,
            string requestJson, string requestSha256)
        {
            CaseId = caseId;
            PairId = pairId;
            Factor = factor;
            Oracle = oracle;
            Status = status;
            BlockedReason = blockedReason;
            Request = request;
            RequestJson = requestJson;
            RequestSha256 = requestSha256;
        }

        public string CaseId { get; }
        public string PairId { get; }
        public string Factor { get; }
        public string Oracle { get; }
        public string Status { get; }
        public string BlockedReason { get; }
        public NpcDecisionRequest Request { get; }
        public string RequestJson { get; }
        public string RequestSha256 { get; }
        public bool NativeGameEvidence => false;
        public bool NativeGameAcceptance => false;
        public IReadOnlyList<NpcDecisionOption> AllowedChoices => Request?.Options;
    }

    /// <summary>
    /// Preparation boundary for the frozen D3 binary lane. The frozen manifest does not contain
    /// production snapshots, current option offers, or captured turns. Therefore current rows
    /// remain blocked until a caller can supply those through production Core builders.
    /// This class never creates option IDs or claims eligibility from manifest prose.
    /// </summary>
    public static class SelectedDecisionCapture
    {
        private const string LaneName = "d3nano";
        private const string ExpectedSuiteId = "selected-framework-factor-contrast-v1";
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Exports exactly the 16 frozen rows. Rows are blocked when the suite lacks a production
        /// snapshot and a proven two-choice offer. No model, server, or game process is started.
        /// </summary>
        public static IReadOnlyList<SelectedDecisionCaptureCase> PrepareDecisions(string suitePath,
            string outDirectory)
            => PrepareDecisions(suitePath, outDirectory, BuildFrozenProductionRequest);

        /// <summary>
        /// Variant for a caller that owns production fixture builders. Factory must return the
        /// current LocalDecisionWillingness request for each frozen row, with its validated proposal.
        /// </summary>
        internal static IReadOnlyList<SelectedDecisionCaptureCase> PrepareDecisions(string suitePath,
            string outDirectory, Func<string, string, string, string, NpcDecisionRequest> productionRequestFactory)
        {
            if (string.IsNullOrWhiteSpace(suitePath)) throw new ArgumentException("Suite path is required.", nameof(suitePath));
            if (string.IsNullOrWhiteSpace(outDirectory)) throw new ArgumentException("Output directory is required.", nameof(outDirectory));
            byte[] suiteBytes = File.ReadAllBytes(suitePath);
            string suiteHash = Sha256(suiteBytes);
            using (JsonDocument document = JsonDocument.Parse(suiteBytes))
            {
                JsonElement root = document.RootElement;
                if (RequiredString(root, "suiteId") != ExpectedSuiteId)
                    throw new InvalidDataException("Unsupported selected-decision suite identity.");
                JsonElement lane = RequiredProperty(root, "d3BinaryCases");
                JsonElement pairs = RequiredArray(lane, "cases");
                if (pairs.GetArrayLength() != 8)
                    throw new InvalidDataException("Frozen D3 binary lane must contain exactly eight pairs.");

                var result = new List<SelectedDecisionCaptureCase>(16);
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement pair in pairs.EnumerateArray())
                {
                    string pairId = RequiredString(pair, "pairId");
                    string factor = RequiredString(pair, "factor");
                    string oracle = RequiredString(pair, "oracle");
                    JsonElement caseIds = RequiredArray(pair, "caseIds");
                    if (caseIds.GetArrayLength() != 2)
                        throw new InvalidDataException("Frozen D3 pair must contain exactly two case IDs: " + pairId);
                    foreach (JsonElement caseNode in caseIds.EnumerateArray())
                    {
                        if (caseNode.ValueKind != JsonValueKind.String)
                            throw new InvalidDataException("Frozen D3 case ID must be a string: " + pairId);
                        string caseId = caseNode.GetString();
                        if (string.IsNullOrWhiteSpace(caseId) || !ids.Add(caseId))
                            throw new InvalidDataException("Frozen D3 case IDs must be unique and nonempty.");
                        NpcDecisionRequest request = productionRequestFactory == null
                            ? null : productionRequestFactory(caseId, pairId, factor, oracle);
                        if (request == null)
                        {
                            result.Add(new SelectedDecisionCaptureCase(caseId, pairId, factor, oracle,
                                "blocked", BlockedReason(pairId),
                                null, null, null));
                        }
                        else
                        {
                            result.Add(FromProductionRequest(caseId, pairId, factor, oracle, request));
                        }
                    }
                }
                if (result.Count != 16)
                    throw new InvalidDataException("Frozen D3 binary lane must contain exactly 16 unique cases.");
                AssertFrozenPairInvariants(result);

                string expectedModel = ReadExpectedModel(root);
                Dictionary<string, string> sourceHashes = ReadSourceHashes(root, suitePath);
                var export = new ExportDocument
                {
                    SchemaVersion = 1,
                    Lane = LaneName,
                    SuiteId = ExpectedSuiteId,
                    SuiteSha256 = suiteHash,
                    Status = result.All(item => item.Status == "ready") ? "ready" :
                        result.Any(item => item.Status == "ready") ? "partial-production-blocks" : "blocked-production-inputs-required",
                    GeneratedPrompts = false,
                    ModelResponses = false,
                    GameRun = false,
                    NativeGameEvidence = false,
                    NativeGameAcceptance = false,
                    ModelProfile = ReadModelProfile(root),
                    ExpectedModel = expectedModel,
                    SourceHashes = sourceHashes,
                    Cases = result.Select(item => ToExportRow(item, expectedModel, sourceHashes)).ToArray()
                };
                Directory.CreateDirectory(outDirectory);
                File.WriteAllText(Path.Combine(outDirectory, "selected-decision-requests.json"),
                    JsonSerializer.Serialize(export, JsonOptions), new UTF8Encoding(false));
                return result.AsReadOnly();
            }
        }

        /// <summary>
        /// Parses one recorded raw response using production LocalDecisionWire and Core binding
        /// validation. Invalid or unoffered choices remain rejected. No semantic grading occurs.
        /// </summary>
        public static SelectedDecisionParseResult ParseRecorded(SelectedDecisionCaptureCase captureCase,
            string response)
        {
            if (captureCase == null) throw new ArgumentNullException(nameof(captureCase));
            if (captureCase.Request == null || string.IsNullOrEmpty(captureCase.RequestSha256))
                throw new InvalidOperationException("Blocked decision case has no production request to parse.");

            LocalDecisionParsedReply parsed = LocalDecisionWire.Parse(response);
            if (!parsed.IsValid)
                return new SelectedDecisionParseResult
                {
                    Accepted = false,
                    Status = "invalid",
                    DiagnosticCode = parsed.DiagnosticCode,
                    RequestSha256 = captureCase.RequestSha256,
                    CoreBindingStatus = "not-run"
                };

            NpcDecisionRequest request = captureCase.Request;
            NpcDecisionResult core = NpcDecisionResult.ValidateResponse(request,
                parsed.SelectedOptionId, parsed.Probabilities, parsed.ReportedConfidence,
                captureCase.RequestSha256, parsed.ModelId, 0, 200,
                request.Binding, request.SnapshotRevision, request.CandidateSetRevision,
                request.Options);
            return new SelectedDecisionParseResult
            {
                Accepted = core.Status == NpcDecisionStatus.Ok,
                Status = core.Status.ToString().ToLowerInvariant(),
                DiagnosticCode = core.DiagnosticCode,
                SelectedOptionId = core.SelectedOptionId,
                ModelId = parsed.ModelId,
                RequestSha256 = captureCase.RequestSha256,
                CoreBindingStatus = core.Status.ToString()
            };
        }

        /// <summary>Builds a capture only from an existing production decision request.</summary>
        internal static SelectedDecisionCaptureCase FromProductionRequest(string caseId, string pairId,
            string factor, string oracle, NpcDecisionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.Purpose != LocalDecisionWillingness.Purpose || request.Options.Count != 2 ||
                request.Options[0].Id != "ACCEPT" || request.Options[1].Id != "DECLINE" ||
                request.Options[0].Description != "The NPC agrees to the exact listed action proposal." ||
                request.Options[1].Description != "The NPC does not agree to the exact listed action proposal." ||
                request.Options[0].IsEffectful || request.Options[1].IsEffectful)
                throw new InvalidDataException("D3 binary request must retain current production willingness options ACCEPT and DECLINE.");
            LocalDecisionPayload payload = LocalDecisionWire.Build(request);
            return new SelectedDecisionCaptureCase(caseId, pairId, factor, oracle, "ready", null,
                request, new UTF8Encoding(false, true).GetString(payload.Bytes), payload.Sha256);
        }

        private static NpcDecisionRequest BuildFrozenProductionRequest(string caseId, string pairId,
            string factor, string oracle)
        {
            // Current willingness projection has no responsible-actor event receipt field.
            if (pairId == "D3-B05-unknown-attribution") return null;

            const double capturedAt = 1000;
            const double acceptedAt = 900;
            const string personIdSeed = "selected-d3-resident";
            bool treatment = caseId.EndsWith("-T", StringComparison.Ordinal);
            var store = new WorldStateStore(17);
            SettlementPopulation population = store.GetOrCreate(10, new[] { 20 }, 1);
            if (population.Households.Count == 0 || population.Households[0].Residents.Count == 0)
                throw new InvalidDataException("Synthetic decision fixture has no resident identity.");
            string owner = population.Households[0].Residents[0].Id;
            if (string.IsNullOrWhiteSpace(owner) || owner == personIdSeed)
                throw new InvalidDataException("Synthetic decision fixture returned invalid resident identity.");

            var commitments = new List<ConversationContextCommitment>();
            int disposition = pairId == "D3-B04-affinity-trust" ? (treatment ? 20 : 55) : 35;
            if (pairId == "D3-B01-duty-conflict" && treatment)
            {
                var appointment = new AppointmentRecord
                {
                    Id = "selected-d3-appointment",
                    ResidentId = owner,
                    Purpose = "meet",
                    StartsAt = 1100,
                    EndsAt = 1180,
                    GraceSeconds = 30,
                    Importance = 1,
                    Place = new AppointmentPlace { Kind = "building", SettlementId = 10, BuildingKey = 20 }
                };
                if (!store.TryReserveAppointment(appointment, acceptedAt, out string reason))
                    throw new InvalidDataException("Synthetic appointment duty was rejected: " + reason);
                AppointmentRecord saved = store.AppointmentsFor(owner).Single();
                commitments.Add(new ConversationContextCommitment(saved.Id, "appointment", saved.Status,
                    store.AppointmentRevision, null, saved.StartsAt, saved.EndsAt));
            }
            else if (pairId == "D3-B02-active-task" && treatment)
            {
                var task = new NpcTaskPlan
                {
                    Id = "selected-d3-task",
                    ProposalId = "selected-d3-task-proposal",
                    ResidentId = owner,
                    Phase = NpcTaskPhase.Ready,
                    CurrentStepIndex = 0,
                    AcceptedAt = acceptedAt,
                    UpdatedAt = acceptedAt,
                    DeadlineAt = 1600
                };
                task.Steps.Add(new NpcTaskStep
                {
                    Kind = NpcActionKind.Wait,
                    DurationSeconds = 600,
                    Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player" }
                });
                if (!store.TryBeginNpcConversationCommandSession(owner,"selected-d3-task-fixture",out var session))
                    throw new InvalidDataException("Synthetic task duty session was rejected.");
                try
                {
                    var taskBinding = new ConversationContextBinding(ConversationActorCategory.Mobile,owner,null,
                        "selected-d3-task-fixture",1,1,store.NpcFrameworkScopeId,"","scene:fixture",1);
                    if (!store.TryCaptureNpcConversationCommandTurn(session,taskBinding,"Controlled fixture task.",out var taskTurn) ||
                        !store.TryPrepareNpcConversationTask(taskTurn,task,out var prepared) ||
                        !store.TryIssueNpcConversationTask(taskTurn,prepared,out var capability))
                        throw new InvalidDataException("Synthetic task duty command could not be issued.");
                    if (!store.TryAcceptNpcTaskFromConversation(capability,prepared,true,out string reason))
                        throw new InvalidDataException("Synthetic task duty was rejected: " + reason);
                    task = prepared;
                }
                finally { store.TryCloseNpcConversationCommandSession(session); }
                NpcTaskPlan saved = store.ReadNpcTask(task.Id);
                commitments.Add(new ConversationContextCommitment(saved.Id, "action-task", "ready",
                    saved.Revision, saved.Steps[0].Kind, saved.AcceptedAt, saved.DeadlineAt));
            }
            else if (pairId == "D3-B03-funded-service" && treatment)
            {
                AddFundedActiveService(store, owner, "selected-d3-service", acceptedAt, 950);
            }

            var binding = new ConversationContextBinding(ConversationActorCategory.NativeMobile, owner, null,
                "selected-d3-" + pairId, 1, 1, "0123456789abcdef0123456789abcdef",
                new string('a', 64), "scene:selected-d3", 1);
            ConversationContextFact<string> clock = ConversationContextFact<string>.Verified("10:00, Midyear",
                ConversationContextFactSource.Derived, capturedAt);
            ConversationContextFact<string> name = ConversationContextFact<string>.Verified("Mira",
                ConversationContextFactSource.NativeActor, capturedAt);
            ConversationContextFact<string> scene = ConversationContextFact<string>.Verified("Daggerfall",
                ConversationContextFactSource.NpcSceneObservation, capturedAt);
            ConversationContextFact<ConversationDialogueRole> role = ConversationContextFact<ConversationDialogueRole>.Verified(
                ConversationDialogueRole.ManagedResident, ConversationContextFactSource.NativeActor, capturedAt);
            ConversationContextFact<string> traits = ConversationContextFact<string>.Verified("patient and careful",
                ConversationContextFactSource.Derived, capturedAt);
            var snapshot = new ConversationContextSnapshot(binding, capturedAt, clock, name, role, scene, scene,
                ConversationContextFact<ConversationNpcPosition>.Unknown(),
                ConversationContextFact<ConversationNpcActivity>.Verified(ConversationNpcActivity.Stationary,
                    ConversationContextFactSource.LiveObservation, capturedAt),
                ConversationContextFact<bool>.Unknown(), ConversationContextFact<ConversationNpcWeatherObservation>.Unknown(),
                traits, disposition, 771, null, commitments, new ConversationContextSourceHandle[0]);

            var relationship = NpcDirectedRelationship.Create(owner, NpcDirectedRelationship.PlayerTargetId);
            if (pairId == "D3-B04-affinity-trust" || pairId == "D3-B08-known-unknown-trust" && treatment)
            {
                relationship.Revision = 1;
                relationship.LastGameTime = acceptedAt;
                relationship.TrustKnown = true;
                relationship.ReliabilityTrust = 55;
                relationship.TrustConfidence = 10;
                relationship.TrustEvidenceCount = 1;
            }
            snapshot = snapshot.WithDirectedRelationship(relationship.ToView());
            NpcPersonalityProfile profile = NpcPersonalityGenerator.Generate(owner, 771, NpcSocialBackground.Commoner);
            if (!NpcMotivationConversationProjection.TryCapture(snapshot, profile, store, out NpcMotivationContextSnapshot motivation))
                throw new InvalidDataException("Synthetic motivation snapshot failed Core projection: " + caseId);
            snapshot = snapshot.WithMotivationContext(motivation);

            string turn = "Could you wait here for ten minutes?";
            if (pairId == "D3-B06-quoted-threat")
                turn = treatment
                    ? "Wait here for ten minutes; someone else said, 'I will hurt you.'"
                    : "Wait here for ten minutes, or I will hurt you.";
            else if (pairId == "D3-B07-hypothetical-threat")
                turn = treatment
                    ? "If someone threatened you to wait here for ten minutes, what would you do?"
                    : "Wait here for ten minutes, or I will hurt you.";

            var proposal = new LocalDecisionWillingnessProposal(new[]
            {
                new NpcActionDecision
                {
                    DurationSeconds = 600,
                    Option = new NpcActionOption
                    {
                        Id = 9,
                        Kind = NpcActionKind.Wait,
                        Label = "wait ten minutes",
                        Target = new NpcActionTarget
                        {
                            Kind = NpcActionTargetKind.WorldPoint,
                            WorldSpaceId = "settlement:selected-d3",
                            Quantity = 1,
                            SettlementId = -1,
                            BuildingKey = -1,
                            X = 12.5,
                            Y = 0,
                            Z = 45.25
                        }
                    }
                }
            });
            return LocalDecisionWillingness.Build(snapshot, turn, new ConversationContextTurn[0], null, proposal);
        }

        private static void AddFundedActiveService(WorldStateStore store, string owner, string contractId,
            double acceptedAt, double startAt)
        {
            const string payer = NpcDirectedRelationship.PlayerTargetId;
            NpcServiceContractWriteTicket ticket = store.CaptureNpcServiceContractWriteTicket();
            if (!NpcServiceContractTerms.TryCreate(contractId, payer, owner, "home:selected-d3",
                NpcServiceRole.Fighter, NpcServiceCoverage.Follow | NpcServiceCoverage.Combat,
                120, false, NpcServiceRiskScope.OrdinaryCombat, Array.Empty<NpcServiceProtectedTarget>(),
                NpcServiceRestTreatment.RestNotIncluded, NpcServiceResupplyResponsibility.SeparatelyNegotiated,
                Array.Empty<NpcServiceReservedObligation>(), id => id == payer || id == owner,
                home => home == "home:selected-d3", (id, category, revision) => true,
                (id, revision, kind, start, end) => true, out NpcServiceContractTerms terms))
                throw new InvalidDataException("Synthetic funded service terms failed validation.");
            var funding = NpcServiceTransferReceipt.Create("receipt:" + contractId, contractId,
                NpcServiceTransferKind.Funding, payer, "contract:" + contractId + ":escrow", 120, acceptedAt);
            if (!NpcServiceContractLedger.TryReserve(ticket.SourceLedger, ticket.ExpectedLedgerRevision,
                terms, funding, out NpcServiceContractLedger reserved))
                throw new InvalidDataException("Synthetic funded service reservation failed.");
            if (!store.TryCommitNpcServiceContracts(ticket, reserved, new List<NpcServiceTransferReceipt> { funding },
                new List<NpcServiceRenewalAuthorization>(), out string error))
                throw new InvalidDataException("Synthetic funded service ledger commit failed: " + error);
            ticket = store.CaptureNpcServiceContractWriteTicket();
            NpcServiceContract activeSource = ticket.SourceLedger.Read(contractId);
            if (activeSource == null || !activeSource.TryStart(activeSource.Revision, startAt, out NpcServiceContract active) ||
                !NpcServiceContractLedger.TryReplace(ticket.SourceLedger, ticket.ExpectedLedgerRevision, active,
                    out NpcServiceContractLedger updated) ||
                !store.TryCommitNpcServiceContracts(ticket, updated, new List<NpcServiceTransferReceipt>(),
                    new List<NpcServiceRenewalAuthorization>(), out error))
                throw new InvalidDataException("Synthetic funded service activation failed: " + error);
        }

        private static string BlockedReason(string pairId)
        {
            if (pairId == "D3-B05-unknown-attribution")
                return "LocalDecisionWillingness receives current turn, attributed conversation history, and captured snapshot, but no event-source attribution receipt. Pair requires unknown/known event attribution and cannot be represented without invented evidence.";
            return "Production inputs are unavailable for this frozen case.";
        }

        private static void AssertFrozenPairInvariants(IReadOnlyList<SelectedDecisionCaptureCase> cases)
        {
            foreach (IGrouping<string, SelectedDecisionCaptureCase> pair in cases.GroupBy(item => item.PairId, StringComparer.Ordinal))
            {
                SelectedDecisionCaptureCase control = pair.Single(item => item.CaseId.EndsWith("-C", StringComparison.Ordinal));
                SelectedDecisionCaptureCase treatment = pair.Single(item => item.CaseId.EndsWith("-T", StringComparison.Ordinal));
                if (control.Request == null || treatment.Request == null) continue;
                NpcDecisionRequest left = control.Request;
                NpcDecisionRequest right = treatment.Request;
                if (!left.Binding.HasSameRequestBinding(right.Binding) ||
                    left.CandidateSetRevision != right.CandidateSetRevision || left.Purpose != right.Purpose ||
                    left.Instructions != right.Instructions || left.Options.Count != right.Options.Count ||
                    left.Options.Where((option, index) => option.Id != right.Options[index].Id ||
                        option.Description != right.Options[index].Description || option.IsEffectful != right.Options[index].IsEffectful).Any())
                    throw new InvalidDataException("Frozen decision pair changed binding or offered choices: " + pair.Key);

                bool speechTreatment = pair.Key == "D3-B06-quoted-threat" || pair.Key == "D3-B07-hypothetical-threat";
                if (speechTreatment)
                {
                    if (left.Context != right.Context || left.OriginalTurn == right.OriginalTurn)
                        throw new InvalidDataException("Speech-framing pair must change exact turn only: " + pair.Key);
                }
                else if (left.OriginalTurn != right.OriginalTurn || left.Context == right.Context)
                    throw new InvalidDataException("Context-treatment pair must change captured context only: " + pair.Key);

                string leftProposal = ProposalSignature(left.Context);
                string rightProposal = ProposalSignature(right.Context);
                if (!string.Equals(leftProposal, rightProposal, StringComparison.Ordinal))
                    throw new InvalidDataException("Frozen decision pair changed the action proposal: " + pair.Key);
            }
        }

        private static string ProposalSignature(string context)
        {
            using (JsonDocument document = JsonDocument.Parse(context))
                return RequiredString(document.RootElement, "proposalSignature");
        }

        private static object ReadModelProfile(JsonElement root)
        {
            if (!root.TryGetProperty("selectedModels", out JsonElement models) ||
                !models.TryGetProperty("d3BinaryDecision", out JsonElement d3))
                throw new InvalidDataException("Frozen D3 model profile pin is missing.");
            return JsonSerializer.Deserialize<JsonElement>(d3.GetRawText());
        }

        private static Dictionary<string, string> ReadSourceHashes(JsonElement root, string suitePath)
        {
            JsonElement pins = RequiredArray(root, "sourcePins");
            string repoRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(suitePath)), "..", ".."));
            var result = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Path.GetFileName(suitePath).Equals("suite-v1.json", StringComparison.Ordinal)
                    ? "work/selected-framework-contrast-prep/suite-v1.json"
                    : Path.GetFullPath(suitePath)] = Sha256(File.ReadAllBytes(suitePath))
            };
            foreach (JsonElement pin in pins.EnumerateArray())
            {
                string relativePath = RequiredString(pin, "path");
                string expectedHash = RequiredString(pin, "sha256");
                string fullPath = Path.GetFullPath(Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(fullPath)) throw new FileNotFoundException("Pinned source file is missing: " + relativePath, fullPath);
                string actualHash = Sha256(File.ReadAllBytes(fullPath));
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Pinned source hash changed: " + relativePath);
                result[relativePath] = actualHash;
            }
            AddCurrentHash(result, repoRoot, "tools/NpcFactorEvaluation/SelectedDecisionCapture.cs");
            AddCurrentHash(result, repoRoot, "tools/NpcFactorEvaluation/NpcFactorEvaluation.csproj");
            AddCurrentHash(result, repoRoot, "mod/Scripts/LocalDecisionWillingness.cs");
            AddCurrentHash(result, repoRoot, "mod/Scripts/LocalDecisionWire.cs");
            return result;
        }

        private static void AddCurrentHash(IDictionary<string, string> hashes, string repoRoot, string relativePath)
        {
            string fullPath = Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            hashes[relativePath] = Sha256(File.ReadAllBytes(fullPath));
        }

        private static string ReadExpectedModel(JsonElement root)
        {
            JsonElement models = RequiredProperty(root, "selectedModels");
            JsonElement d3 = RequiredProperty(models, "d3BinaryDecision");
            return RequiredString(d3, "exactModel");
        }

        private static ExportRow ToExportRow(SelectedDecisionCaptureCase item, string expectedModel,
            Dictionary<string, string> sourceHashes)
        {
            return new ExportRow
            {
                CaseId = item.CaseId,
                Lane = "d3nano",
                PairId = item.PairId,
                Factor = item.Factor,
                Oracle = item.Oracle,
                Status = item.Status,
                BlockedReason = item.BlockedReason,
                RequestBody = item.RequestJson,
                RequestSha256 = item.RequestSha256,
                ExpectedModel = expectedModel,
                SourceHashes = sourceHashes,
                ProjectionLimit = item.PairId == "D3-B04-affinity-trust"
                    ? "EffectiveDisposition is used as the available directed-affinity context. Core snapshot has no separate affinity field, so review results cannot establish a distinct affinity projection."
                    : null,
                RequestId = item.Request?.RequestId.ToString("D"),
                Purpose = item.Request?.Purpose,
                OriginalTurn = item.Request?.OriginalTurn,
                Context = item.Request?.Context,
                SnapshotRevision = item.Request?.SnapshotRevision,
                CandidateSetRevision = item.Request?.CandidateSetRevision,
                ExpectedCoreBinding = item.Request == null ? null : new BindingMetadata
                {
                    PersonId = item.Request.Binding.PersonId,
                    SessionId = item.Request.Binding.SessionId,
                    RequestRevision = item.Request.Binding.RequestRevision,
                    SaveEpoch = item.Request.Binding.SaveEpoch,
                    SceneId = item.Request.Binding.SceneId,
                    SceneRevision = item.Request.Binding.SceneRevision
                },
                AllowedChoices = item.Request?.Options.Select(option => new ChoiceMetadata
                {
                    Id = option.Id,
                    Description = option.Description,
                    IsEffectful = option.IsEffectful
                }).ToArray(),
                NativeGameEvidence = false,
                NativeGameAcceptance = false
            };
        }

        private static JsonElement RequiredProperty(JsonElement parent, string name)
        {
            if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out JsonElement value))
                throw new InvalidDataException("Required suite property is missing: " + name);
            return value;
        }

        private static JsonElement RequiredArray(JsonElement parent, string name)
        {
            JsonElement value = RequiredProperty(parent, name);
            if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Suite property must be an array: " + name);
            return value;
        }

        private static string RequiredString(JsonElement parent, string name)
        {
            JsonElement value = RequiredProperty(parent, name);
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidDataException("Suite property must be a nonempty string: " + name);
            return value.GetString();
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
                return string.Concat(algorithm.ComputeHash(bytes).Select(value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        }

        private sealed class ExportDocument
        {
            public int SchemaVersion { get; set; }
            public string Lane { get; set; }
            public string SuiteId { get; set; }
            public string SuiteSha256 { get; set; }
            public string Status { get; set; }
            public bool GeneratedPrompts { get; set; }
            public bool ModelResponses { get; set; }
            public bool GameRun { get; set; }
            public bool NativeGameEvidence { get; set; }
            public bool NativeGameAcceptance { get; set; }
            public object ModelProfile { get; set; }
            public string ExpectedModel { get; set; }
            public Dictionary<string, string> SourceHashes { get; set; }
            public ExportRow[] Cases { get; set; }
        }

        private sealed class ExportRow
        {
            public string CaseId { get; set; }
            public string Lane { get; set; }
            public string PairId { get; set; }
            public string Factor { get; set; }
            public string Oracle { get; set; }
            public string Status { get; set; }
            public string BlockedReason { get; set; }
            public string RequestBody { get; set; }
            public string RequestSha256 { get; set; }
            public string ExpectedModel { get; set; }
            public Dictionary<string, string> SourceHashes { get; set; }
            public string ProjectionLimit { get; set; }
            public string RequestId { get; set; }
            public string Purpose { get; set; }
            public string OriginalTurn { get; set; }
            public string Context { get; set; }
            public long? SnapshotRevision { get; set; }
            public long? CandidateSetRevision { get; set; }
            public BindingMetadata ExpectedCoreBinding { get; set; }
            public ChoiceMetadata[] AllowedChoices { get; set; }
            public bool NativeGameEvidence { get; set; }
            public bool NativeGameAcceptance { get; set; }
        }

        private sealed class BindingMetadata
        {
            public string PersonId { get; set; }
            public string SessionId { get; set; }
            public long RequestRevision { get; set; }
            public long SaveEpoch { get; set; }
            public string SceneId { get; set; }
            public long SceneRevision { get; set; }
        }

        private sealed class ChoiceMetadata
        {
            public string Id { get; set; }
            public string Description { get; set; }
            public bool IsEffectful { get; set; }
        }
    }
}
