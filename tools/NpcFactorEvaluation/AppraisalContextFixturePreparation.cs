using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>
    /// Builds the additive v3 context-pair appraisal cases. Inputs are synthetic fixture data;
    /// these requests are not native captures and cannot commit world state.
    /// </summary>
    public static class AppraisalContextFixturePreparation
    {
        private const string FixtureId = "npc-experience-appraisal-context-pairs-v3";
        private const string BaseFixtureId = "npc-experience-appraisal-live-scenarios-v2";
        private const string CorpusFixtureId = "npc-experience-appraisal-live-scenarios-v3";
        private const string BranchId = "offline-appraisal-context-v3";
        private const string PlayerId = "player";
        private const string PublicTopicId = "topic-74d23d05";
        private const string BaseFixtureFile = "appraisal-live-scenarios-v2.json";
        private const string ContextPairFixtureFile = "appraisal-context-pairs-v3.json";
        private const int CorpusCaseCount = 38;

        /// <summary>Loads the pinned v3 manifest and expands its frozen v2 base plus six context cases.</summary>
        public static IReadOnlyList<AppraisalFixtureCase> BuildCorpusCases(string manifestPath)
        {
            V3Manifest manifest = ReadManifest(manifestPath);
            IReadOnlyList<AppraisalFixtureCase> baseCases = AppraisalFixturePreparation.BuildCases(manifest.BaseFixturePath);
            IReadOnlyList<AppraisalFixtureCase> contextCases = BuildCases(manifest.BaseFixturePath, manifest.ContextPairFixturePath);
            var result = new List<AppraisalFixtureCase>(CorpusCaseCount);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            AddUnique(baseCases, result, ids);
            AddUnique(contextCases, result, ids);
            if (result.Count != CorpusCaseCount || result.Count != manifest.ExpectedCaseCount)
                throw new InvalidDataException("Pinned v3 appraisal corpus must contain exactly 38 unique cases.");
            return result.AsReadOnly();
        }

        /// <summary>Returns the exact manifest byte hash for prepared-row metadata after validating child pins.</summary>
        public static string GetCorpusManifestSha256(string manifestPath)
        {
            V3Manifest manifest = ReadManifest(manifestPath);
            return manifest.ManifestSha256;
        }

        /// <summary>Returns exactly six cases in three matched pairs, without changing the frozen v1/v2 builder.</summary>
        public static IReadOnlyList<AppraisalFixtureCase> BuildCases(string baseFixturePath, string contextPairFixturePath)
        {
            if (string.IsNullOrWhiteSpace(baseFixturePath)) throw new ArgumentException("Base fixture path is required.", nameof(baseFixturePath));
            if (string.IsNullOrWhiteSpace(contextPairFixturePath)) throw new ArgumentException("Context-pair fixture path is required.", nameof(contextPairFixturePath));

            using (JsonDocument baseDocument = JsonDocument.Parse(File.ReadAllText(baseFixturePath)))
            using (JsonDocument pairDocument = JsonDocument.Parse(File.ReadAllText(contextPairFixturePath)))
            {
                JsonElement baseRoot = baseDocument.RootElement;
                if (RequiredString(baseRoot, "fixtureId") != BaseFixtureId || RequiredInt(baseRoot, "fixtureVersion") != 2 ||
                    RequiredInt(baseRoot, "profileTraitCount") != 3)
                    throw new InvalidDataException("Context pairs require the current three-trait appraisal fixture v2.");

                JsonElement root = pairDocument.RootElement;
                if (RequiredString(root, "fixtureId") != FixtureId || RequiredInt(root, "fixtureVersion") != 3 ||
                    RequiredString(root, "baseFixtureId") != BaseFixtureId)
                    throw new InvalidDataException("Unsupported appraisal context-pair fixture identity or version.");

                Dictionary<string, JsonElement> profiles = Index(RequiredArray(baseRoot, "profiles"), "personId");
                Dictionary<string, JsonElement> scenarios = Index(RequiredArray(baseRoot, "scenarios"), "id");
                JsonElement pairs = RequiredArray(root, "pairs");
                if (pairs.GetArrayLength() != 3) throw new InvalidDataException("Appraisal context fixture must contain exactly three matched pairs.");

                var cases = new List<AppraisalFixtureCase>(6);
                var caseIds = new HashSet<string>(StringComparer.Ordinal);
                var pairIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement pair in pairs.EnumerateArray())
                {
                    string pairId = RequiredString(pair, "pairId");
                    if (!pairIds.Add(pairId)) throw new InvalidDataException("Duplicate context pair ID: " + pairId);
                    string contrast = RequiredString(pair, "contrast");
                    string profileId = RequiredString(pair, "profileId");
                    string scenarioId = RequiredString(pair, "scenarioId");
                    if (!profiles.TryGetValue(profileId, out JsonElement profileSpec))
                        throw new InvalidDataException("Context pair references an unknown base profile: " + profileId);
                    if (!scenarios.TryGetValue(scenarioId, out JsonElement scenarioSpec))
                        throw new InvalidDataException("Context pair references an unknown base scenario: " + scenarioId);
                    JsonElement arms = RequiredArray(pair, "arms");
                    if (arms.GetArrayLength() != 2) throw new InvalidDataException("Each context pair must contain exactly two arms: " + pairId);
                    ValidatePairSpec(pair, contrast, scenarioSpec, arms);

                    string observerId = RequiredString(profileSpec, "personId");
                    uint seed = checked((uint)RequiredInt(profileSpec, "seed"));
                    NpcSocialBackground background = ParseEnum<NpcSocialBackground>(RequiredString(profileSpec, "background"));
                    NpcPersonalityProfile personality = NpcPersonalityGenerator.Generate(observerId, seed, background);
                    if (personality.Version != 3 || personality.SelectedTraits.Count != 3)
                        throw new InvalidDataException("Context pair did not produce exactly three saved traits: " + pairId);

                    NpcExperienceEvent evidence = BuildEvidence(scenarioSpec, observerId, pairId);
                    NpcDirectedRelationshipView relationship = evidence.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Known
                        ? CreateRelationship(profileSpec, observerId) : null;

                    foreach (JsonElement arm in arms.EnumerateArray())
                    {
                        string caseId = RequiredString(arm, "caseId");
                        string label = RequiredString(arm, "reviewLabel");
                        if (!caseIds.Add(caseId)) throw new InvalidDataException("Duplicate context case ID: " + caseId);
                        NpcMotivationContextSnapshot motivation = BuildMotivationContext(profileSpec, personality, observerId,
                            contrast, arm, pairId, relationship != null);
                        string hiddenValue = pair.TryGetProperty("hiddenValue", out JsonElement hidden) ? hidden.GetString() : null;
                        string topicId = pair.TryGetProperty("publicTopicId", out JsonElement topic) ? topic.GetString() : null;
                        NpcKnowledgeSnapshot knowledge = BuildKnowledgeSnapshot(observerId, contrast, arm, pairId, topicId, hiddenValue);
                        bool waited = evidence.EventKind == NpcExperienceEventKind.AppointmentNoShow;

                        NpcExperienceAppraisalRequest request = NpcExperienceAppraisalContract.CreateRequest(
                            BranchId, evidence, personality,
                            RequiredInt(profileSpec, "currentMood"), RequiredLong(profileSpec, "moodRevision"),
                            RequiredDouble(profileSpec, "moodGameTime"), RequiredInt(profileSpec, "currentAffinity"),
                            RequiredLong(profileSpec, "affinityRevision"), RequiredInt(profileSpec, "positiveAffinityCredit"),
                            RequiredDouble(profileSpec, "capturedGameTime"), relationship, null, waited,
                            null, null, null, null, null, NpcExperienceAppliedEffectsMask.None, motivation, knowledge);

                        EnsurePromptIsolation(request, pairId, caseId, label, hiddenValue, topicId, contrast, arm);
                        cases.Add(new AppraisalFixtureCase(caseId, label, request));
                    }

                    ValidateMatchedPair(cases[cases.Count - 2], cases[cases.Count - 1], contrast);
                }

                if (cases.Count != 6 || pairIds.Count != 3 || caseIds.Count != 6)
                    throw new InvalidDataException("Appraisal context fixture must expand to six unique cases.");
                return cases.AsReadOnly();
            }
        }

        private static void ValidatePairSpec(JsonElement pair, string contrast, JsonElement scenario, JsonElement arms)
        {
            string expectedScenario = contrast == "healthNeed" ? "injury-health-episode" :
                contrast == "appointmentDuty" ? "confirmed-no-show" :
                contrast == "knowledgeEpistemicState" ? "status-onset" : null;
            if (expectedScenario == null || RequiredString(scenario, "type") != expectedScenario)
                throw new InvalidDataException("Context pair contrast does not match its typed base event.");
            JsonElement first = arms[0];
            JsonElement second = arms[1];
            if (contrast == "healthNeed")
            {
                int a = RequiredInt(first, "needIntensity"), b = RequiredInt(second, "needIntensity");
                if (!((a == 75 && b == 20) || (a == 20 && b == 75)))
                    throw new InvalidDataException("Health need pair must contain the authored high/low values 75 and 20.");
            }
            else if (contrast == "appointmentDuty")
            {
                if (RequiredBool(first, "dutyPresent") == RequiredBool(second, "dutyPresent"))
                    throw new InvalidDataException("Appointment duty pair must have one present and one absent arm.");
            }
            else
            {
                if (RequiredString(pair, "publicTopicId") != PublicTopicId || string.IsNullOrWhiteSpace(RequiredString(pair, "hiddenValue")))
                    throw new InvalidDataException("Knowledge pair requires the authored public topic and hidden value.");
                bool validVerified = HasKnowledgeTreatment(first, "VerifiedTruth", "NativeObservation", false);
                bool validRumor = HasKnowledgeTreatment(first, "Rumor", "AttributedReport", true);
                bool validVerifiedSecond = HasKnowledgeTreatment(second, "VerifiedTruth", "NativeObservation", false);
                bool validRumorSecond = HasKnowledgeTreatment(second, "Rumor", "AttributedReport", true);
                if (!(validVerified && validRumorSecond || validRumor && validVerifiedSecond))
                    throw new InvalidDataException("Knowledge pair must contain one verified observation and one attributed rumor.");
            }
        }

        private static V3Manifest ReadManifest(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath)) throw new ArgumentException("V3 manifest path is required.", nameof(manifestPath));
            string fullManifestPath = Path.GetFullPath(manifestPath);
            if (!string.Equals(Path.GetFileName(fullManifestPath), "appraisal-live-scenarios-v3.json", StringComparison.Ordinal))
                throw new InvalidDataException("V3 manifest must use the literal appraisal-live-scenarios-v3.json filename.");
            byte[] manifestBytes = ReadBounded(fullManifestPath, 16 * 1024);
            using (JsonDocument document = JsonDocument.Parse(manifestBytes))
            {
                JsonElement root = document.RootElement;
                if (RequiredString(root, "fixtureId") != CorpusFixtureId || RequiredInt(root, "fixtureVersion") != 3 ||
                    RequiredInt(root, "expectedCaseCount") != CorpusCaseCount)
                    throw new InvalidDataException("Unsupported v3 appraisal corpus manifest.");
                string baseName = RequiredString(root, "baseFixtureFile");
                string contextName = RequiredString(root, "contextPairFixtureFile");
                if (baseName != BaseFixtureFile || contextName != ContextPairFixtureFile)
                    throw new InvalidDataException("V3 manifest child paths must match the two literal fixture filenames.");
                string directory = Path.GetDirectoryName(fullManifestPath);
                string basePath = Path.Combine(directory, BaseFixtureFile);
                string contextPath = Path.Combine(directory, ContextPairFixtureFile);
                string baseHash = RequiredString(root, "baseFixtureSha256");
                string contextHash = RequiredString(root, "contextPairFixtureSha256");
                ValidateHashPin(basePath, baseHash);
                ValidateHashPin(contextPath, contextHash);
                return new V3Manifest
                {
                    BaseFixturePath = basePath,
                    ContextPairFixturePath = contextPath,
                    ExpectedCaseCount = RequiredInt(root, "expectedCaseCount"),
                    ManifestSha256 = Sha256(manifestBytes)
                };
            }
        }

        private static void AddUnique(IReadOnlyList<AppraisalFixtureCase> source, List<AppraisalFixtureCase> target,
            HashSet<string> ids)
        {
            foreach (AppraisalFixtureCase item in source)
            {
                if (item == null || item.Request == null || !ids.Add(item.CaseId))
                    throw new InvalidDataException("V3 appraisal corpus contains a null or duplicate case.");
                target.Add(item);
            }
        }

        private static void ValidateHashPin(string path, string expectedHash)
        {
            if (expectedHash == null || expectedHash.Length != 64)
                throw new InvalidDataException("V3 manifest child hash must be a 64-character SHA-256.");
            for (int i = 0; i < expectedHash.Length; i++)
                if (!Uri.IsHexDigit(expectedHash[i])) throw new InvalidDataException("V3 manifest child hash is not hexadecimal.");
            string actual = Sha256(ReadBounded(path, 1024 * 1024));
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("V3 manifest child fixture SHA-256 mismatch: " + Path.GetFileName(path));
        }

        private static byte[] ReadBounded(string path, int maximumBytes)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 1 || info.Length > maximumBytes)
                throw new InvalidDataException("Fixture file is missing, empty or exceeds its size bound: " + Path.GetFileName(path));
            return File.ReadAllBytes(path);
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte item in hash) result.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private sealed class V3Manifest
        {
            public string BaseFixturePath;
            public string ContextPairFixturePath;
            public string ManifestSha256;
            public int ExpectedCaseCount;
        }

        private static bool HasKnowledgeTreatment(JsonElement arm, string kind, string origin, bool needsAttribution)
        {
            if (RequiredString(arm, "epistemicKind") != kind || RequiredString(arm, "evidenceOrigin") != origin) return false;
            if (needsAttribution) return arm.TryGetProperty("attributionId", out JsonElement attribution) &&
                attribution.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(attribution.GetString());
            return !arm.TryGetProperty("attributionId", out _);
        }

        private static NpcExperienceEvent BuildEvidence(JsonElement scenario, string observerId, string pairId)
        {
            string type = RequiredString(scenario, "type");
            double time = RequiredDouble(scenario, "observedGameTime");
            string receipt = OpaqueId("receipt", pairId);
            string episode = OpaqueId("episode", pairId);
            string outcome = OpaqueId("outcome", pairId);
            if (type == "injury-health-episode")
                return NpcExperienceEvent.FromNativeOutcome(observerId, observerId, receipt, episode, outcome,
                    NpcExperienceEventKind.Injury, time, NpcResponsibleActorAttribution.Unknown, null,
                    NpcExperienceImpactDetails.ForHealthEpisode(80, 40, 100, 40, 0, 1));
            if (type == "confirmed-no-show")
            {
                var appointment = new AppointmentRecord
                {
                    Id = OpaqueId("appointment", pairId), ResidentId = observerId, Purpose = "shared appointment",
                    StartsAt = 1600, EndsAt = 1800, GraceSeconds = 0, PatienceSeconds = 600,
                    Status = "player-absent", NpcArrivedAt = 1600, PlayerArrivedAt = -1,
                    PlayerAtVenueAt = -1, ResolvedAt = 2201, NpcLastSeenAt = 2190,
                    NpcWaitedSeconds = 30, OutcomeRecorded = true, NoShowPenalty = 1
                };
                if (!NpcAppointmentExperience.TryCreate(appointment, out NpcExperienceEvent noShow))
                    throw new InvalidDataException("Core rejected synthetic confirmed no-show evidence.");
                return noShow;
            }
            if (type == "status-onset")
                return NpcExperienceEvent.FromNativeOutcome(observerId, observerId, receipt, episode, outcome,
                    NpcExperienceEventKind.StatusOnset, time, NpcResponsibleActorAttribution.Unknown);
            throw new InvalidDataException("Unsupported context-pair scenario type: " + type);
        }

        private static NpcMotivationContextSnapshot BuildMotivationContext(JsonElement profileSpec, NpcPersonalityProfile personality, string observerId,
            string contrast, JsonElement arm, string pairId, bool includeRelationship)
        {
            double captured = RequiredDouble(profileSpec, "capturedGameTime");
            long clockRevision = 1;
            var clockEvidence = EvidenceRef(observerId, "clock", NpcMotivationEvidenceKind.WorldClock, clockRevision, captured);
            var clock = new NpcMotivationGameClock(captured, clockRevision, clockEvidence);
            long profileRevision = personality.Version;
            var profileRef = new NpcMotivationProfileReference(observerId, profileRevision,
                EvidenceRef(observerId, "profile", NpcMotivationEvidenceKind.PersonalityProfile, profileRevision, 0));
            NpcMotivationRelationshipReference relationRef = null;
            if (includeRelationship)
            {
                long relationRevision = 1;
                relationRef = new NpcMotivationRelationshipReference(observerId, PlayerId, relationRevision,
                    EvidenceRef(observerId, "relationship", NpcMotivationEvidenceKind.RelationshipLedger,
                        relationRevision, RequiredDouble(profileSpec, "relationshipGameTime")));
            }

            var needs = new List<NpcMotivationNeed>();
            var duties = new List<NpcMotivationDuty>();
            if (contrast == "healthNeed")
            {
                int intensity = RequiredInt(arm, "needIntensity");
                needs.Add(new NpcMotivationNeed(OpaqueId("need", pairId), NpcMotivationNeedKind.Health,
                    NpcMotivationKnowledgeState.Known, intensity, 1,
                    EvidenceRef(observerId, "need-source", NpcMotivationEvidenceKind.ResourceLedger, 1,
                        RequiredDouble(profileSpec, "moodGameTime"))));
            }
            else if (contrast == "appointmentDuty" && RequiredBool(arm, "dutyPresent"))
            {
                duties.Add(new NpcMotivationDuty(OpaqueId("duty", pairId), NpcMotivationDutyKind.Appointment,
                    NpcMotivationDutyScope.Schedule, NpcMotivationDutyStatus.Active, 2100, 2400, 1,
                    EvidenceRef(observerId, "duty-source", NpcMotivationEvidenceKind.CommitmentLedger, 1, 2200)));
            }
            else if (contrast != "appointmentDuty" && contrast != "knowledgeEpistemicState")
                throw new InvalidDataException("Unsupported motivation context contrast: " + contrast);

            return NpcMotivationContextSnapshot.Capture(BranchId, observerId, 1, clock, profileRef, relationRef,
                needs, null, null, null, null, null, duties);
        }

        private static NpcKnowledgeSnapshot BuildKnowledgeSnapshot(string observerId, string contrast, JsonElement arm,
            string pairId, string topicId, string hiddenValue)
        {
            NpcKnowledgeState state = NpcKnowledgeLedger.Create(observerId, BranchId);
            if (contrast == "knowledgeEpistemicState")
            {
                NpcKnowledgeUpdate update;
                string sourceId = OpaqueId("source", pairId);
                string updateReceipt = OpaqueId("update", pairId);
                double observed = 1623;
                NpcDisclosureEpistemicKind kind = ParseEnum<NpcDisclosureEpistemicKind>(RequiredString(arm, "epistemicKind"));
                NpcKnowledgeEvidenceOrigin origin = ParseEnum<NpcKnowledgeEvidenceOrigin>(RequiredString(arm, "evidenceOrigin"));
                if (kind == NpcDisclosureEpistemicKind.VerifiedTruth && origin == NpcKnowledgeEvidenceOrigin.NativeObservation)
                    update = NpcKnowledgeUpdate.FromNativeEvidence(observerId, OpaqueId("entry", pairId), topicId,
                        hiddenValue, NpcDisclosureProtection.Public, origin, sourceId, updateReceipt, 1, observed);
                else if (kind == NpcDisclosureEpistemicKind.Rumor && origin == NpcKnowledgeEvidenceOrigin.AttributedReport)
                    update = NpcKnowledgeUpdate.FromAttributedClaim(observerId, OpaqueId("entry", pairId), topicId,
                        hiddenValue, kind, NpcDisclosureProtection.Public, origin, sourceId, updateReceipt, 1, observed,
                        RequiredString(arm, "attributionId"));
                else throw new InvalidDataException("Unsupported typed knowledge treatment package.");

                if (!NpcKnowledgeLedger.TryApply(state, update, observerId, BranchId, 0, out state))
                    throw new InvalidDataException("Core rejected synthetic typed knowledge update.");
            }
            else if (contrast != "healthNeed" && contrast != "appointmentDuty")
                throw new InvalidDataException("Unsupported knowledge context contrast: " + contrast);

            if (!NpcKnowledgeLedger.TryCapture(state, observerId, BranchId, out NpcKnowledgeSnapshot snapshot) || snapshot == null)
                throw new InvalidDataException("Core rejected synthetic knowledge snapshot.");
            return snapshot;
        }

        private static NpcMotivationEvidenceRef EvidenceRef(string observerId, string purpose,
            NpcMotivationEvidenceKind kind, long revision, double time)
        { return new NpcMotivationEvidenceRef(BranchId, observerId, OpaqueId(purpose, observerId), kind, revision, time); }

        private static NpcDirectedRelationshipView CreateRelationship(JsonElement spec, string observerId)
        {
            return new NpcDirectedRelationship
            {
                ObserverId = observerId, TargetId = PlayerId, Revision = 1,
                LastGameTime = RequiredDouble(spec, "relationshipGameTime"), TrustKnown = RequiredBool(spec, "trustKnown"),
                ReliabilityTrust = RequiredInt(spec, "reliabilityTrust"), TrustConfidence = RequiredInt(spec, "trustConfidence"),
                TrustEvidenceCount = RequiredInt(spec, "trustEvidenceCount"), Resentment = RequiredInt(spec, "resentment"),
                InterpersonalFear = RequiredInt(spec, "fear")
            }.ToView();
        }

        private static void EnsurePromptIsolation(NpcExperienceAppraisalRequest request, string pairId, string caseId,
            string label, string hiddenValue, string topicId, string contrast, JsonElement arm)
        {
            string prompt = request.Prompt;
            if (prompt.IndexOf(pairId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                prompt.IndexOf(caseId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                prompt.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (!string.IsNullOrEmpty(hiddenValue) && prompt.IndexOf(hiddenValue, StringComparison.OrdinalIgnoreCase) >= 0))
                throw new InvalidDataException("Review-only context labels or hidden test values leaked into the prompt: " + caseId);

            if (contrast == "healthNeed" && (request.MotivationContext.Needs.Count != 1 ||
                !prompt.Contains("intensity=" + RequiredInt(arm, "needIntensity"), StringComparison.Ordinal)))
                throw new InvalidDataException("Health need did not reach the prompt as a typed fact: " + caseId);
            if (contrast == "appointmentDuty" && request.MotivationContext.Duties.Count != (RequiredBool(arm, "dutyPresent") ? 1 : 0))
                throw new InvalidDataException("Appointment duty arm does not match its typed projection: " + caseId);
            if (contrast == "knowledgeEpistemicState")
            {
                string topicLabel = topicId + ":" + RequiredString(arm, "epistemicKind");
                if (!prompt.Contains(topicLabel, StringComparison.Ordinal) ||
                    !request.KnowledgeSnapshot.TryGet(topicId, out NpcKnowledgeFactView fact) ||
                    fact.Statement.EpistemicKind != ParseEnum<NpcDisclosureEpistemicKind>(RequiredString(arm, "epistemicKind")) ||
                    fact.Statement.Protection != NpcDisclosureProtection.Public)
                    throw new InvalidDataException("Public topic epistemic metadata is missing from the prompt: " + caseId);
            }
        }

        private static void ValidateMatchedPair(AppraisalFixtureCase first, AppraisalFixtureCase second, string contrast)
        {
            NpcExperienceAppraisalRequest a = first.Request;
            NpcExperienceAppraisalRequest b = second.Request;
            if (a.SaveBranchId != b.SaveBranchId || a.ObserverId != b.ObserverId || a.SubjectPersonId != b.SubjectPersonId ||
                a.CanonicalEvidenceId != b.CanonicalEvidenceId || a.EvidenceDeduplicationKey != b.EvidenceDeduplicationKey ||
                a.CurrentMood != b.CurrentMood || a.CurrentAffinity != b.CurrentAffinity || a.CapturedGameTime != b.CapturedGameTime ||
                a.ExpectedMoodRevision != b.ExpectedMoodRevision || a.ExpectedAffinityRevision != b.ExpectedAffinityRevision ||
                a.ExpectedMotivationRevision != b.ExpectedMotivationRevision || a.ExpectedKnowledgeRevision != b.ExpectedKnowledgeRevision)
                throw new InvalidDataException("Matched context pair differs in a shared event/profile/state input: " + contrast);
            if (string.Equals(a.Prompt, b.Prompt, StringComparison.Ordinal))
                throw new InvalidDataException("Matched context pair did not produce distinct prompts: " + contrast);
            if (contrast == "healthNeed" && (a.MotivationContext.Needs[0].Intensity == b.MotivationContext.Needs[0].Intensity ||
                a.MotivationContext.Duties.Count != b.MotivationContext.Duties.Count || a.KnowledgeSnapshot.Revision != b.KnowledgeSnapshot.Revision))
                throw new InvalidDataException("Health pair contains an unintended context mismatch.");
            if (contrast == "appointmentDuty" && (a.MotivationContext.Duties.Count == b.MotivationContext.Duties.Count ||
                a.MotivationContext.Needs.Count != b.MotivationContext.Needs.Count || a.KnowledgeSnapshot.Revision != b.KnowledgeSnapshot.Revision))
                throw new InvalidDataException("Duty pair contains an unintended context mismatch.");
            if (contrast == "knowledgeEpistemicState" && (a.KnowledgeSnapshot.Revision != b.KnowledgeSnapshot.Revision ||
                a.MotivationContext.Needs.Count != b.MotivationContext.Needs.Count || a.MotivationContext.Duties.Count != b.MotivationContext.Duties.Count))
                throw new InvalidDataException("Knowledge pair contains an unintended context mismatch.");
        }

        private static string OpaqueId(string purpose, string pairId)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(FixtureId + ":" + purpose + ":" + pairId));
                var text = new StringBuilder(24);
                for (int i = 0; i < 12; i++) text.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return "fixture-v3:" + purpose + ":" + text;
            }
        }

        private static Dictionary<string, JsonElement> Index(JsonElement array, string keyName)
        {
            var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonElement item in array.EnumerateArray())
                if (!result.TryAdd(RequiredString(item, keyName), item)) throw new InvalidDataException("Duplicate fixture key: " + keyName);
            return result;
        }

        private static JsonElement RequiredArray(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                throw new InvalidDataException("Missing or empty fixture array: " + name);
            return value;
        }
        private static string RequiredString(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidDataException("Missing fixture string: " + name);
            return value.GetString();
        }
        private static int RequiredInt(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out JsonElement value) || !value.TryGetInt32(out int result)) throw new InvalidDataException("Missing fixture integer: " + name);
            return result;
        }
        private static long RequiredLong(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out JsonElement value) || !value.TryGetInt64(out long result)) throw new InvalidDataException("Missing fixture integer: " + name);
            return result;
        }
        private static double RequiredDouble(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out JsonElement value) || !value.TryGetDouble(out double result) || double.IsNaN(result) || double.IsInfinity(result))
                throw new InvalidDataException("Missing finite fixture number: " + name);
            return result;
        }
        private static bool RequiredBool(JsonElement item, string name)
        {
            if (!item.TryGetProperty(name, out JsonElement value) || (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
                throw new InvalidDataException("Missing fixture boolean: " + name);
            return value.GetBoolean();
        }
        private static T ParseEnum<T>(string value) where T : struct
        {
            if (!Enum.TryParse(value, false, out T result) || !Enum.IsDefined(typeof(T), result) || Enum.GetName(typeof(T), result) != value)
                throw new InvalidDataException("Invalid fixture enum value: " + value);
            return result;
        }
    }
}
