using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>One synthetic, production-Core appraisal request for offline or live-model evaluation.</summary>
    public sealed class AppraisalFixtureCase
    {
        internal AppraisalFixtureCase(string caseId, string label, NpcExperienceAppraisalRequest request)
        {
            CaseId = caseId;
            Label = label;
            Request = request;
            InputSha256 = AppraisalFixturePreparation.Sha256(request.Prompt);
        }

        public string CaseId { get; private set; }
        /// <summary>Human-review grouping only. Never append this label to the model payload.</summary>
        public string Label { get; private set; }
        public string InputSha256 { get; private set; }
        public NpcExperienceAppraisalRequest Request { get; private set; }
    }

    /// <summary>
    /// Expands the bounded authored appraisal fixture through production Core factories. It creates no world
    /// state and does not imply that its synthetic evidence came from a running game.
    /// </summary>
    public static class AppraisalFixturePreparation
    {
        private const string FixtureIdV1 = "npc-experience-appraisal-live-scenarios-v1";
        private const string FixtureIdV2 = "npc-experience-appraisal-live-scenarios-v2";
        private const string PlayerId = "player";

        public static IReadOnlyList<AppraisalFixtureCase> BuildCases(string fixturePath)
        {
            if (string.IsNullOrWhiteSpace(fixturePath)) throw new ArgumentException("Fixture path is required.", nameof(fixturePath));
            using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(fixturePath), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            }))
            {
                JsonElement root = document.RootElement;
                string fixtureId = RequiredString(root, "fixtureId");
                int fixtureVersion = RequiredInt(root, "fixtureVersion");
                if ((fixtureVersion == 1 && fixtureId != FixtureIdV1) ||
                    (fixtureVersion == 2 && fixtureId != FixtureIdV2) ||
                    (fixtureVersion != 1 && fixtureVersion != 2))
                    throw new InvalidDataException("Unsupported appraisal live-scenario fixture identity or version.");
                if (RequiredInt(root, "profileTraitCount") != 3)
                    throw new InvalidDataException("Fixture contract must use exactly three current saved personality traits.");
                JsonElement profiles = RequiredArray(root, "profiles");
                JsonElement scenarios = RequiredArray(root, "scenarios");
                var cases = new List<AppraisalFixtureCase>();
                var ids = new HashSet<string>(StringComparer.Ordinal);

                foreach (JsonElement profileSpec in profiles.EnumerateArray())
                {
                    string observerId = RequiredString(profileSpec, "personId");
                    uint seed = checked((uint)RequiredInt(profileSpec, "seed"));
                    NpcSocialBackground background = Enum.Parse<NpcSocialBackground>(RequiredString(profileSpec, "background"), false);
                    NpcPersonalityProfile personality = NpcPersonalityGenerator.Generate(observerId, seed, background);
                    if (personality.Version != 3 || personality.SelectedTraits.Count != 3)
                        throw new InvalidDataException("Fixture profile did not produce exactly three current saved personality traits.");

                    int mood = RequiredInt(profileSpec, "currentMood");
                    int affinity = RequiredInt(profileSpec, "currentAffinity");
                    int positiveCredit = RequiredInt(profileSpec, "positiveAffinityCredit");
                    NpcDirectedRelationshipView relationship = CreateRelationship(profileSpec, observerId);

                    foreach (JsonElement scenario in scenarios.EnumerateArray())
                    {
                        string scenarioId = RequiredString(scenario, "id");
                        string caseId = observerId + "/" + scenarioId;
                        if (!ids.Add(caseId)) throw new InvalidDataException("Duplicate appraisal case ID: " + caseId);
                        string label = RequiredString(scenario, "reviewLabel");
                        NpcExperienceEvent evidence;
                        NpcObservedConversationTurn observedTurn;
                        bool waited;
                        BuildEvidence(scenario, observerId, caseId, fixtureId, fixtureVersion,
                            out evidence, out observedTurn, out waited);
                        NpcDirectedRelationshipView requestRelationship =
                            evidence.Origin == NpcExperienceEventOrigin.NativeOutcome &&
                            evidence.ResponsibleActorAttribution == NpcResponsibleActorAttribution.Unknown
                                ? null : relationship;

                        NpcExperienceAppraisalRequest request = NpcExperienceAppraisalContract.CreateRequest(
                            "offline-appraisal-fixture-v" + fixtureVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            evidence,
                            personality,
                            mood,
                            RequiredLong(profileSpec, "moodRevision"),
                            RequiredDouble(profileSpec, "moodGameTime"),
                            affinity,
                            RequiredLong(profileSpec, "affinityRevision"),
                            positiveCredit,
                            RequiredDouble(profileSpec, "capturedGameTime"),
                            requestRelationship,
                            null,
                            waited,
                            observedTurn);
                        if (request == null || string.IsNullOrWhiteSpace(request.Prompt))
                            throw new InvalidDataException("Core did not construct appraisal request: " + caseId);
                        cases.Add(new AppraisalFixtureCase(caseId, label, request));
                    }
                }

                if (cases.Count < 24 || cases.Count > 40)
                    throw new InvalidDataException("Appraisal live-scenario fixture must expand to 24–40 bounded cases.");
                return cases.AsReadOnly();
            }
        }

        /// <summary>Use the exact Core parser on a raw model response. No fixture oracle is applied.</summary>
        public static bool TryParseResponse(AppraisalFixtureCase fixtureCase, string rawResponse,
            out NpcExperienceAppraisal appraisal)
        {
            if (fixtureCase == null) throw new ArgumentNullException(nameof(fixtureCase));
            return NpcExperienceAppraisalContract.TryParseResponse(fixtureCase.Request, rawResponse, out appraisal);
        }

        /// <summary>
        /// Forward an actual native capture ticket and exact raw response to the atomic store transaction.
        /// Synthetic fixture cases cannot supply tickets and cannot commit state.
        /// </summary>
        public static bool TryCommitNativeTicket(WorldStateStore store, NpcExperienceAppraisalTicket ticket,
            string rawResponse)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (ticket == null) throw new ArgumentNullException(nameof(ticket));
            return store.TryCommitNpcExperienceAppraisal(ticket, rawResponse);
        }

        private static NpcDirectedRelationshipView CreateRelationship(JsonElement spec, string observerId)
        {
            var relationship = new NpcDirectedRelationship
            {
                ObserverId = observerId,
                TargetId = PlayerId,
                Revision = 1,
                LastGameTime = RequiredDouble(spec, "relationshipGameTime"),
                TrustKnown = RequiredBool(spec, "trustKnown"),
                ReliabilityTrust = RequiredInt(spec, "reliabilityTrust"),
                TrustConfidence = RequiredInt(spec, "trustConfidence"),
                TrustEvidenceCount = RequiredInt(spec, "trustEvidenceCount"),
                Resentment = RequiredInt(spec, "resentment"),
                InterpersonalFear = RequiredInt(spec, "fear")
            };
            return relationship.ToView();
        }

        private static void BuildEvidence(JsonElement scenario, string observerId, string caseId,
            string fixtureId, int fixtureVersion,
            out NpcExperienceEvent evidence, out NpcObservedConversationTurn observedTurn, out bool waited)
        {
            string type = RequiredString(scenario, "type");
            double observedTime = RequiredDouble(scenario, "observedGameTime");
            // Keep the human-readable scenario identity out of Core prompts. Core includes these
            // IDs in canonical evidence and deduplication metadata sent to the model.
            string receipt = OpaqueFixtureId(fixtureId, fixtureVersion, "receipt", caseId);
            string episode = OpaqueFixtureId(fixtureId, fixtureVersion, "episode", caseId);
            string outcome = OpaqueFixtureId(fixtureId, fixtureVersion, "outcome", caseId);
            observedTurn = null;
            waited = false;

            switch (type)
            {
                case "known-gift":
                    evidence = NpcExperienceEvent.FromNativeOutcome(observerId, observerId, receipt, episode,
                        outcome, NpcExperienceEventKind.GiftReceived, observedTime,
                        NativeAttribution(scenario, fixtureVersion, NpcResponsibleActorAttribution.Known),
                        NativeActor(scenario, fixtureVersion),
                        NpcExperienceImpactDetails.ForItemTransfer(1, NpcExperienceNativeValuationStatus.Unknown, 0));
                    return;
                case "confirmed-help":
                    evidence = NpcExperienceEvent.FromNativeOutcome(observerId, observerId, receipt, episode,
                        outcome, NpcExperienceEventKind.CombatHelp, observedTime,
                        NativeAttribution(scenario, fixtureVersion, NpcResponsibleActorAttribution.Known),
                        NativeActor(scenario, fixtureVersion));
                    return;
                case "injury-health-episode":
                    evidence = NpcExperienceEvent.FromNativeOutcome(observerId, observerId, receipt, episode,
                        outcome, NpcExperienceEventKind.Injury, observedTime,
                        NativeAttribution(scenario, fixtureVersion, NpcResponsibleActorAttribution.Known),
                        NativeActor(scenario, fixtureVersion),
                        NpcExperienceImpactDetails.ForHealthEpisode(80, 40, 100, 40, 0, 1));
                    return;
                case "status-onset":
                    evidence = NpcExperienceEvent.FromNativeOutcome(observerId, observerId, receipt, episode,
                        outcome, NpcExperienceEventKind.StatusOnset, observedTime,
                        NativeAttribution(scenario, fixtureVersion, NpcResponsibleActorAttribution.Known),
                        NativeActor(scenario, fixtureVersion));
                    return;
                case "confirmed-no-show":
                    if (fixtureVersion == 2 &&
                        (NativeAttribution(scenario, fixtureVersion, NpcResponsibleActorAttribution.Known) != NpcResponsibleActorAttribution.Known ||
                         NativeActor(scenario, fixtureVersion) != PlayerId))
                        throw new InvalidDataException("Confirmed no-show evidence must preserve the appointment's known player-absence attribution.");
                    var appointment = new AppointmentRecord
                    {
                        Id = OpaqueFixtureId(fixtureId, fixtureVersion, "appointment", caseId),
                        ResidentId = observerId,
                        Purpose = "shared appointment",
                        StartsAt = 1600,
                        EndsAt = 1800,
                        GraceSeconds = 0,
                        PatienceSeconds = 600,
                        Status = "player-absent",
                        NpcArrivedAt = 1600,
                        PlayerArrivedAt = -1,
                        PlayerAtVenueAt = -1,
                        ResolvedAt = 2201,
                        NpcLastSeenAt = 2190,
                        NpcWaitedSeconds = 30,
                        OutcomeRecorded = true,
                        NoShowPenalty = 1
                    };
                    if (!NpcAppointmentExperience.TryCreate(appointment, out evidence))
                        throw new InvalidDataException("Core rejected confirmed no-show fixture evidence: " + caseId);
                    waited = true;
                    return;
                case "direct-threat":
                case "quoted-threat":
                case "hypothetical-threat":
                    string utterance = RequiredString(scenario, "observedUtterance");
                    evidence = NpcExperienceEvent.FromObservedSpeech(observerId, observerId, receipt, episode,
                        OpaqueFixtureId(fixtureId, fixtureVersion, "utterance", caseId), observedTime, PlayerId);
                    observedTurn = NpcObservedConversationTurn.Create(observerId, episode, PlayerId,
                        receipt, utterance, observedTime);
                    return;
                default:
                    throw new InvalidDataException("Unsupported appraisal scenario type: " + type);
            }
        }

        private static JsonElement RequiredArray(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
                throw new InvalidDataException("Missing or empty fixture array: " + name);
            return value;
        }

        private static string RequiredString(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidDataException("Missing fixture string: " + name);
            return value.GetString();
        }

        private static int RequiredInt(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value) || !value.TryGetInt32(out int result))
                throw new InvalidDataException("Missing fixture integer: " + name);
            return result;
        }

        private static long RequiredLong(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value) || !value.TryGetInt64(out long result))
                throw new InvalidDataException("Missing fixture integer: " + name);
            return result;
        }

        private static double RequiredDouble(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value) || !value.TryGetDouble(out double result) ||
                double.IsNaN(result) || double.IsInfinity(result))
                throw new InvalidDataException("Missing finite fixture number: " + name);
            return result;
        }

        private static bool RequiredBool(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value) ||
                (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False))
                throw new InvalidDataException("Missing fixture boolean: " + name);
            return value.GetBoolean();
        }

        internal static string Sha256(string text)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte item in hash) result.Append(item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static NpcResponsibleActorAttribution NativeAttribution(JsonElement scenario, int fixtureVersion,
            NpcResponsibleActorAttribution v1Value)
        {
            if (fixtureVersion == 1) return v1Value;
            string value = RequiredString(scenario, "responsibleActorAttribution");
            if (!Enum.TryParse(value, false, out NpcResponsibleActorAttribution attribution) ||
                !Enum.IsDefined(typeof(NpcResponsibleActorAttribution), attribution))
                throw new InvalidDataException("Invalid responsibleActorAttribution for " + RequiredString(scenario, "type") + ".");
            return attribution;
        }

        private static string NativeActor(JsonElement scenario, int fixtureVersion)
        {
            if (fixtureVersion == 1) return PlayerId;
            string value = RequiredString(scenario, "responsibleActorAttribution");
            if (string.Equals(value, NpcResponsibleActorAttribution.Unknown.ToString(), StringComparison.Ordinal))
            {
                if (scenario.TryGetProperty("responsibleActorId", out _))
                    throw new InvalidDataException("Unknown actor attribution must not contain a responsibleActorId.");
                return null;
            }
            return RequiredString(scenario, "responsibleActorId");
        }

        private static string OpaqueFixtureId(string fixtureId, int fixtureVersion, string kind, string caseId)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(fixtureId + ":" + kind + ":" + caseId));
                var result = new StringBuilder(24);
                for (int index = 0; index < 12; index++)
                    result.Append(bytes[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return "fixture-v" + fixtureVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + kind + ":" + result;
            }
        }
    }
}
