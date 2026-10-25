using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public static class ConversationContextFormatter
    {
        private sealed class ProjectionLine
        {
            public string Text;
            public int Priority;
            public bool Mandatory;
        }

        private const string OmittedMarker = "context omitted to fit the character budget.";

        /// <summary>Intent-only projection; stable traits, relationship and motivation belong to later decisions.</summary>
        public static string ForRouting(ConversationContextSnapshot snapshot, IEnumerable<ConversationContextTurn> history,
            string currentPlayerText, int maxChars)
        {
            Require(snapshot, history, currentPlayerText, maxChars);
            return Fit(RoutingRows(snapshot), history, currentPlayerText, maxChars);
        }

        public static string ForRouting(ConversationContextSnapshot snapshot, int maxChars)
        {
            RequireFacts(snapshot, maxChars);
            return Fit(RoutingRows(snapshot), Enumerable.Empty<ConversationContextTurn>(), null, maxChars);
        }

        public static string ForSemanticClassifier(ConversationContextSnapshot snapshot, IEnumerable<ConversationContextTurn> history,
            string currentPlayerText, int maxChars)
        {
            Require(snapshot, history, currentPlayerText, maxChars);
            var prefix = SemanticRows(snapshot, maxChars);
            return Fit(prefix, history, currentPlayerText, maxChars);
        }

        public static string ForSemanticClassifier(ConversationContextSnapshot snapshot, int maxChars)
        {
            RequireFacts(snapshot, maxChars);
            return Fit(SemanticRows(snapshot, maxChars), Enumerable.Empty<ConversationContextTurn>(), null, maxChars);
        }

        private static List<ProjectionLine> RoutingRows(ConversationContextSnapshot snapshot)
        {
            var prefix = new List<ProjectionLine>();
            Add(prefix, "Speaker: " + Fact(snapshot.Name, "unknown") + "; role=" + Fact(snapshot.Role, "unknown") + ".", 3);
            AppendStaticActorContext(prefix, snapshot);
            AppendCommitments(prefix, snapshot.Commitments, currentOnly: true);
            AppendHouseholdOuting(prefix, snapshot, 0);
            return prefix;
        }

        private static List<ProjectionLine> SemanticRows(ConversationContextSnapshot snapshot, int maxChars)
        {
            var prefix = new List<ProjectionLine>();
            AddEssential(prefix, "Speaker: " + Fact(snapshot.Name, "unknown") + "; role=" + Fact(snapshot.Role, "unknown") + ".", 3, maxChars);
            AppendStaticActorContext(prefix, snapshot);
            AddEssential(prefix, "Captured game clock: " + Fact(snapshot.ClockContext, "unknown") + ".", 4, maxChars);
            AddEssential(prefix, "Disposition: " + snapshot.EffectiveDisposition.ToString(CultureInfo.InvariantCulture) + ".", 2, maxChars);
            AppendDirectedRelationship(prefix, snapshot, maxChars);
            AppendPersonality(prefix, snapshot, maxChars);
            AppendCommitments(prefix, snapshot.Commitments, currentOnly: false);
            AppendDerivedMood(prefix, snapshot, maxChars);
            AppendMotivationContext(prefix, snapshot, maxChars);
            AppendHouseholdOuting(prefix, snapshot, maxChars);
            return prefix;
        }

        private static void AppendPersonalStateAndMood(List<ProjectionLine> lines, ConversationContextSnapshot snapshot, int maxChars)
        {
            string body = Fact(snapshot.PersonalState.BodyContext, null);
            if (body != null) AddEssential(lines, body, 5, maxChars);
            string traveler = Fact(snapshot.PersonalState.TravelerContext, null);
            if (traveler != null) AddEssential(lines, traveler, 5, maxChars);
            string race = Fact(snapshot.PersonalState.Race, null);
            string gender = Fact(snapshot.PersonalState.Gender, null);
            if (race != null) Add(lines, "Race: " + race + ".", 2);
            if (gender != null) Add(lines, "Gender: " + gender + ".", 2);
            AddEssential(lines, snapshot.PersonalState.HouseholdKnowledge == null
                ? "Family/household: unknown; do not invent."
                : snapshot.PersonalState.HouseholdKnowledge.ToContext(), 0, maxChars);

            bool familiar;
            bool knowsFamiliarity = snapshot.PersonalState.FamiliarWithPlayer.TryGetVerified(out familiar);
            if (knowsFamiliarity)
            {
                Add(lines, "Player familiarity: " + (familiar ? "yes" : "no") + ".", 3);
                string playerName = Fact(snapshot.PersonalState.KnownPlayerName, null);
                if (familiar && playerName != null)
                    Add(lines, "You already know the player as " + playerName + ".", 3);
            }

            AppendDerivedMood(lines, snapshot, maxChars);
        }

        private static void AppendDerivedMood(List<ProjectionLine> lines, ConversationContextSnapshot snapshot, int maxChars)
        {
            if (snapshot.PersistentMood != null)
            {
                AddEssential(lines, ConversationLifeContext.PersistentMood(snapshot.PersistentMood.Value), 5, maxChars);
                AppendPersistentMoodFacts(lines, snapshot, maxChars);
                return;
            }
            if (Fact(snapshot.PersonalityTraits, null) == null || snapshot.CapturedGameTime / 86400d > int.MaxValue)
            {
                AddEssential(lines, "Current mood: unknown (no saved mood or bounded derived inputs).", 5, maxChars);
                return;
            }
            bool grieving, alarmed, outdoors, familiar;
            bool knowsGrieving = snapshot.PersonalState.Grieving.TryGetVerified(out grieving);
            bool knowsAlarmed = snapshot.PersonalState.Alarmed.TryGetVerified(out alarmed);
            bool knowsFamiliarity = snapshot.PersonalState.FamiliarWithPlayer.TryGetVerified(out familiar);
            bool knowsOutdoors = snapshot.NpcOutdoors.TryGetVerified(out outdoors);
            bool knownActivity;
            ConversationNpcActivity activityValue;
            knownActivity = snapshot.NpcActivity.TryGetVerified(out activityValue);
            int day = (int)Math.Floor(snapshot.CapturedGameTime / 86400d);
            int hour = (int)(snapshot.CapturedGameTime % 86400d / 3600d);
            bool afterDark = hour >= 20 || hour < 5;
            bool outdoorsForMood = knowsOutdoors && outdoors && (!afterDark || knowsFamiliarity);
            string weather = outdoorsForMood ? NpcWeather(snapshot) : null;
            var inputs = new ConversationMoodInputs
            {
                Disposition = snapshot.EffectiveDisposition,
                Hour = hour,
                Grieving = knowsGrieving && grieving,
                Alarmed = knowsAlarmed && alarmed,
                Outdoors = outdoorsForMood,
                Familiar = knowsFamiliarity && familiar,
                Weather = weather,
                Activity = knownActivity ? activityValue.ToString().ToLowerInvariant() : null
            };
            string mood = ConversationLifeContext.Mood(snapshot.PersonalitySeed, day, inputs);
            const string currentMood = "Current mood: ";
            if (mood.StartsWith(currentMood, StringComparison.Ordinal)) mood = mood.Substring(currentMood.Length);
            var caveats = new List<string>();
            if (!knowsGrieving || !knowsAlarmed) caveats.Add("grieving/alarmed status is untracked");
            if (!knowsOutdoors) caveats.Add("NPC outdoor status is unknown, so outdoor/weather modifiers are omitted");
            else if (outdoors && afterDark && !knowsFamiliarity)
                caveats.Add("familiarity is untracked, so outdoor/night modifiers are omitted");
            AddEssential(lines, "Derived mood from tracked inputs: " + mood +
                (caveats.Count == 0 ? "" : " (" + string.Join("; ", caveats) + ")"), 5, maxChars);
        }

        private static void AppendPersistentMoodFacts(List<ProjectionLine> lines, ConversationContextSnapshot snapshot, int maxChars)
        {
            bool grieving, alarmed, outdoors;
            if (snapshot.PersonalState.Grieving.TryGetVerified(out grieving))
            {
                if (grieving) AddEssential(lines, "Current verified condition: grieving.", 4, maxChars);
            }
            else AddEssential(lines, "Grieving status is untracked.", 2, maxChars);

            if (snapshot.PersonalState.Alarmed.TryGetVerified(out alarmed))
            {
                if (alarmed) AddEssential(lines, "Current verified condition: alarmed.", 4, maxChars);
            }
            else AddEssential(lines, "Alarmed status is untracked.", 2, maxChars);

            if (snapshot.NpcOutdoors.TryGetVerified(out outdoors) && outdoors)
            {
                string weather = NpcWeather(snapshot);
                if (weather != null && !lines.Any(line => line.Text.StartsWith("NPC weather:", StringComparison.Ordinal)))
                    AddEssential(lines, "NPC weather: " + weather + ".", 4, maxChars);
            }
        }

        public static string ForSocialReply(ConversationContextSnapshot snapshot, IEnumerable<ConversationContextTurn> history,
            string currentPlayerText, int maxChars)
        {
            Require(snapshot, history, currentPlayerText, maxChars);
            return Fit(SocialRows(snapshot, maxChars), history, currentPlayerText, maxChars);
        }

        public static string ForSocialReply(ConversationContextSnapshot snapshot, int maxChars)
        {
            RequireFacts(snapshot, maxChars);
            return Fit(SocialRows(snapshot, maxChars), Enumerable.Empty<ConversationContextTurn>(), null, maxChars);
        }

        public static string ForActionVoice(ConversationContextSnapshot snapshot, int maxChars)
        {
            RequireFacts(snapshot, maxChars);
            return Fit(ActionVoiceRows(snapshot, maxChars), Enumerable.Empty<ConversationContextTurn>(), null, maxChars);
        }

        private static List<ProjectionLine> ActionVoiceRows(ConversationContextSnapshot snapshot, int maxChars)
        {
            var prefix = new List<ProjectionLine>();
            AddEssential(prefix, "Speaker: " + Fact(snapshot.Name, "unknown") + "; role=" + Fact(snapshot.Role, "unknown") + ".", 3, maxChars);
            AppendStaticActorContext(prefix, snapshot);
            AddEssential(prefix, "Captured game clock: " + Fact(snapshot.ClockContext, "unknown") + ".", 4, maxChars);
            AddEssential(prefix, "Disposition: " + snapshot.EffectiveDisposition.ToString(CultureInfo.InvariantCulture) + ".", 2, maxChars);
            AppendDirectedRelationship(prefix, snapshot, maxChars);
            AppendPersonality(prefix, snapshot, maxChars);
            AppendDerivedMood(prefix, snapshot, maxChars);
            AppendMotivationContext(prefix, snapshot, maxChars);
            AppendHouseholdOuting(prefix, snapshot, maxChars);
            return prefix;
        }

        private static List<ProjectionLine> SocialRows(ConversationContextSnapshot snapshot, int maxChars)
        {
            var prefix = new List<ProjectionLine>();
            AddEssential(prefix, "Speaker: " + Fact(snapshot.Name, "unknown") + "; role=" + Fact(snapshot.Role, "unknown") + ".", 3, maxChars);
            AppendStaticActorContext(prefix, snapshot);
            AddEssential(prefix, "Captured game clock: " + Fact(snapshot.ClockContext, "unknown") + ".", 4, maxChars);
            AddEssential(prefix, "Disposition: " + snapshot.EffectiveDisposition.ToString(CultureInfo.InvariantCulture) + ".", 1, maxChars);
            AppendDirectedRelationship(prefix, snapshot, maxChars);
            Add(prefix, "Player scene at submission: " + Fact(snapshot.PlayerScene, "unknown") + ".", 2);
            Add(prefix, FormatHere(snapshot.PlayerLocationAtSubmission), 5);
            Add(prefix, "NPC scene: " + Fact(snapshot.NpcScene, "unknown") + ".", 2);
            Add(prefix, "NPC position: " + Fact(snapshot.NpcPosition, "unknown") + ".", 1);
            Add(prefix, "NPC activity: " + Fact(snapshot.NpcActivity, "unknown") + ".", 1);
            Add(prefix, "NPC outdoors: " + Fact(snapshot.NpcOutdoors, "unknown") + ".", 1);
            bool outdoors;
            if (snapshot.NpcOutdoors.TryGetVerified(out outdoors))
                AddEssential(prefix, outdoors
                    ? "Current physical location: outdoors, not inside your home. Home ownership and scheduled stops are not your current location. This live observation overrides earlier dialogue."
                    : "Current physical location: indoors. Do not infer that this building is your home from home ownership alone.", 6, maxChars);
            var weather = NpcWeather(snapshot);
            string journey;
            if(snapshot.CurrentJourney.TryGetVerified(out journey))
            {
                string detail=Escape(journey);
                int limit=maxChars>=2048?256:80;
                if(detail.Length>limit)detail=detail.Substring(0,limit)+"…";
                AddEssential(prefix,"Journey when this conversation began: "+detail+". Destination does not prove arrival.",5,maxChars);
            }
            if (weather != null) Add(prefix, "NPC weather: " + weather + ".", 1);
            AppendPersonality(prefix, snapshot, maxChars);
            AppendCommitments(prefix, snapshot.Commitments, currentOnly: false);
            AppendPersonalStateAndMood(prefix, snapshot, maxChars);
            AppendMotivationContext(prefix, snapshot, maxChars);
            AppendHouseholdOuting(prefix, snapshot, maxChars);
            return prefix;
        }

        private static void AppendHouseholdOuting(List<ProjectionLine> lines, ConversationContextSnapshot snapshot, int maxChars)
        {
            if (snapshot.HouseholdOuting != null)
                AddEssential(lines, snapshot.HouseholdOuting.ToContext(), 9, maxChars);
        }

        private static void AppendMotivationContext(List<ProjectionLine> lines, ConversationContextSnapshot snapshot, int maxChars)
        {
            int commitmentsWithoutTiming = 0;
            foreach (ConversationContextCommitment commitment in snapshot.Commitments)
                if (commitment != null && !commitment.StartsAtGameTime.HasValue) commitmentsWithoutTiming++;
            if (commitmentsWithoutTiming > 0)
                Add(lines, commitmentsWithoutTiming.ToString(CultureInfo.InvariantCulture) +
                    " verified commitment(s) lack a captured native start time; treat their duty timing as unknown.", 1);
            if (snapshot.MotivationContext == null)
            {
                AddEssential(lines, "Agenda and paid duties: unknown (no observer-bound motivation snapshot supplied).", 8, maxChars);
                return;
            }
            if (maxChars < 1024)
            {
                Add(lines, "Agenda and paid duties omitted: the native projection budget is below 1024 characters.", 0);
                return;
            }
            int motivationBudget = Math.Min(NpcMotivationContextFormatter.DefaultMaximumCharacters, Math.Max(1024, maxChars / 2));
            string projection = NpcMotivationContextFormatter.ForModels(snapshot.MotivationContext, motivationBudget);
            // Preserve the whole bounded actor-known projection; never truncate a fact inside it.
            // It is useful context, but a small action-voice budget must not make speech fail
            // merely because the complete motivation block cannot fit alongside required facts.
            Add(lines, "Shared actor-known motivation context:\n" + projection, 8);
        }

        private static void AppendPersonality(List<ProjectionLine> lines, ConversationContextSnapshot snapshot, int maxChars)
        {
            string traits = Fact(snapshot.PersonalityTraits, null);
            AddEssential(lines, traits == null
                ? "Stable personality: unknown (no stable native profile captured)."
                : "Known personality: " + traits + ".", 1, maxChars);
        }

        private static void AppendStaticActorContext(List<ProjectionLine> lines, ConversationContextSnapshot snapshot)
        {
            if (snapshot.Binding.ActorCategory != ConversationActorCategory.Static) return;
            ConversationDialogueRole role;
            ConversationDialogueRole? verifiedRole = snapshot.Role.TryGetVerified(out role) ? (ConversationDialogueRole?)role : null;
            Add(lines, StaticNpcRoleProjection.StaticActorContext(verifiedRole), 5);
        }

        private static void AppendDirectedRelationship(List<ProjectionLine> lines, ConversationContextSnapshot snapshot, int maxChars)
        {
            AppendRoleAuthority(lines, snapshot, maxChars);
            var relationship = snapshot.DirectedRelationship;
            if (relationship == null)
            {
                AddEssential(lines, "Relationship toward player: unknown (no current directed record).", 4, maxChars);
                AppendAcuteFear(lines, snapshot, maxChars);
                return;
            }

            string trust = relationship.TrustKnown
                ? "reliability trust=" + relationship.ReliabilityTrust.ToString(CultureInfo.InvariantCulture) +
                    "/100 (support=" + relationship.TrustConfidence.ToString(CultureInfo.InvariantCulture) +
                    "/100 from " + relationship.TrustEvidenceCount.ToString(CultureInfo.InvariantCulture) +
                    " observed outcomes; not a probability)"
                : "reliability trust=unknown (no supporting evidence; not evidence of unreliability)";
            string line = "Relationship toward player: liking/disposition=" +
                snapshot.EffectiveDisposition.ToString(CultureInfo.InvariantCulture) + "/100; " + trust +
                "; resentment=" + relationship.ProjectedResentment.ToString(CultureInfo.InvariantCulture) +
                "/100; interpersonal fear=" + relationship.ProjectedInterpersonalFear.ToString(CultureInfo.InvariantCulture) +
                "/100.";
            int contactFear = NpcPlayerContactFearPolicy.Project(relationship.ProjectedInterpersonalFear,
                snapshot.VerifiedPlayerContact);
            if (snapshot.VerifiedPlayerContact)
                line += " Verified player-contact fear projection=" + contactFear.ToString(CultureInfo.InvariantCulture) + "/100.";
            line += " These factors do not prove intent.";
            AddEssential(lines, line, 4, maxChars);
            AppendAcuteFear(lines, snapshot, maxChars);
        }

        private static void AppendRoleAuthority(List<ProjectionLine> lines, ConversationContextSnapshot snapshot, int maxChars)
        {
            var role = snapshot.RoleEvidence;
            if (role == null) return;
            if (NpcRolePolicy.SuppressPlayerFear(role))
                AddEssential(lines, "Verified social authority: the player is your subordinate, an agent serving nobility. You do not fear or submit to the player. Unrelated dangers are separate.", 5, maxChars);
            if (!NpcRolePolicy.IsCombatExperienced(role)) return;
            string duty = NpcRolePolicy.FightsToDeath(role)
                ? "You fight to the death for nobility and kingdom; injury does not make you flee."
                : role.CombatRole == NpcCombatRole.Guard
                    ? "As a city watch guard you normally stand and fight. A rare retreat when severely injured leads into the city wall quarters; do not invent a retreat decision or a reachable route."
                : role.ServiceBinding == NpcServiceBinding.Independent
                    ? "As an independent combatant you may retreat when severely injured; this is not automatic panic."
                    : "Follow your verified duty; do not invent independent status or a retreat order.";
            AddEssential(lines, "Combat role: " + role.CombatRole + "; service=" + role.ServiceBinding +
                ". Very low danger fear. " + duty + " These facts do not authorize attacking neutral actors.", 5, maxChars);
        }

        private static void AppendAcuteFear(List<ProjectionLine> lines, ConversationContextSnapshot snapshot, int maxChars)
        {
            NpcAcuteFearView fear = snapshot.AcuteFear;
            if (fear == null) return;
            string current = fear.CurrentFear.ToString("0.##", CultureInfo.InvariantCulture);
            string observed = fear.ObservedGameTime.ToString("0.##", CultureInfo.InvariantCulture);
            string line = "Recent non-player danger fear: " + current + "/100; severity=" + fear.Severity +
                "; last observed at game time " + observed + "; source=" + fear.SourceId +
                ". This observation does not establish current threat status.";
            AddEssential(lines, line, 4, maxChars);
        }

        private static void AppendCommitments(List<ProjectionLine> lines, IReadOnlyList<ConversationContextCommitment> commitments, bool currentOnly)
        {
            foreach (var commitment in commitments)
            {
                string kind = commitment.Kind.ToLowerInvariant();
                string status = commitment.Status.ToLowerInvariant();
                if (!AllowedCommitmentKinds.Contains(kind) || !AllowedCommitmentStatus(kind, status) ||
                    currentOnly && !CurrentCommitmentStatus(kind, status)) continue;
                string temporal = CurrentCommitmentStatus(kind, status) ? "Current" : "Previous";
                if (kind == "action-task")
                {
                    var action = commitment.ActionKind.HasValue ? commitment.ActionKind.Value.ToString() : "unknown action";
                    Add(lines, temporal + " action: " + action + " (" + status + ").", 5);
                }
                else if (kind == "task")
                {
                    Add(lines, temporal + " task: " + status + ".", 5);
                }
                else
                {
                    string appointmentState = kind == "appointment" && status == "attended"
                        ? "attended (in progress)" : status;
                    Add(lines, temporal + " " + (kind == "shared-activity" ? "shared activity" : "appointment") + ": " + appointmentState + ".", 2);
                }
            }
        }

        private static readonly HashSet<string> AllowedCommitmentKinds = new HashSet<string>(StringComparer.Ordinal)
            { "shared-activity", "appointment", "action-task", "task" };
        private static readonly HashSet<string> SharedActivityStatuses = new HashSet<string>(StringComparer.Ordinal)
            { "planned", "active", "paused", "completed", "cancelled", "failed" };
        private static readonly HashSet<string> AppointmentStatuses = new HashSet<string>(StringComparer.Ordinal)
            { "accepted", "waiting", "attended" };
        private static readonly HashSet<string> ActionTaskStatuses = new HashSet<string>(StringComparer.Ordinal)
            { "proposed", "accepted", "approaching", "executing", "completed", "failed", "cancelled" };
        private static readonly HashSet<string> TaskStatuses = new HashSet<string>(StringComparer.Ordinal)
            { "ready", "running", "completed", "failed", "cancelled" };

        private static bool AllowedCommitmentStatus(string kind, string status)
        {
            switch (kind)
            {
                case "shared-activity": return SharedActivityStatuses.Contains(status);
                case "appointment": return AppointmentStatuses.Contains(status);
                case "action-task": return ActionTaskStatuses.Contains(status);
                case "task": return TaskStatuses.Contains(status);
                default: return false;
            }
        }

        private static bool CurrentCommitmentStatus(string kind, string status)
        {
            switch (kind)
            {
                case "shared-activity": return status == "planned" || status == "active" || status == "paused";
                case "appointment": return status == "accepted" || status == "waiting" || status == "attended";
                case "action-task": return status == "proposed" || status == "accepted" || status == "approaching" || status == "executing";
                case "task": return status == "ready" || status == "running";
                default: return false;
            }
        }

        private static string NpcWeather(ConversationContextSnapshot snapshot)
        {
            bool outdoors;
            ConversationNpcWeatherObservation observation;
            string npcScene;
            if (!snapshot.NpcOutdoors.TryGetVerified(out outdoors) || !outdoors ||
                !snapshot.Weather.TryGetVerified(out observation) ||
                !snapshot.NpcScene.TryGetVerified(out npcScene) ||
                !string.Equals(npcScene, snapshot.Binding.SceneId, StringComparison.Ordinal) ||
                !string.Equals(observation.SceneId, snapshot.Binding.SceneId, StringComparison.Ordinal) ||
                observation.SceneRevision != snapshot.Binding.SceneRevision)
                return null;
            return Escape(observation.Weather);
        }

        private static string Fact<T>(ConversationContextFact<T> fact, string fallback)
        {
            T value;
            if (fact != null && fact.TryGetVerified(out value))
            {
                if (value is ConversationNpcPosition)
                {
                    var position = (ConversationNpcPosition)(object)value;
                    return position.X.ToString("R", CultureInfo.InvariantCulture) + "," + position.Y.ToString("R", CultureInfo.InvariantCulture) + "," + position.Z.ToString("R", CultureInfo.InvariantCulture);
                }
                return Escape(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            return fallback;
        }

        private static string FormatHere(ConversationLocationSnapshot snapshot)
        {
            if (snapshot == null) return "Player appointment HERE at submission: unknown.";
            var place = snapshot.PlaceCopy();
            return "Player appointment HERE at submission: " + Escape(place.Kind) + ", settlement " +
                place.SettlementId.ToString(CultureInfo.InvariantCulture) + ", building " +
                place.BuildingKey.ToString(CultureInfo.InvariantCulture) + " (fixed).";
        }

        private static string Escape(string value)
        {
            if (value == null) return "";
            var result = new StringBuilder(value.Length);
            foreach (char ch in value) result.Append(char.IsControl(ch) ? ' ' : ch);
            return result.ToString();
        }

        private static string Fit(List<ProjectionLine> prefix, IEnumerable<ConversationContextTurn> turns, string current, int maxChars)
        {
            var history = turns.ToList();
            if (history.Any(turn => turn == null)) throw new ArgumentException("Conversation history contains a null turn.");
            var keptPrefix = new List<ProjectionLine>(prefix);
            bool omitted = false;
            while (true)
            {
                var rows = keptPrefix.Select(line => line.Text).ToList();
                if (omitted) rows.Add(OmittedMarker);
                rows.AddRange(history.Select(turn => (turn.Speaker == ConversationTextSpeaker.Player ? "Player: " : "NPC: ") + Escape(turn.Text)));
                if (current != null) rows.Add("Current player message: " + current);
                var result = string.Join("\n", rows);
                if (result.Length <= maxChars) return result;
                if (history.Count > 0)
                {
                    history.RemoveAt(0);
                    omitted = true;
                    continue;
                }
                if (keptPrefix.Count > 0)
                {
                    var removable = keptPrefix.Where(line => !line.Mandatory).ToList();
                    if (removable.Count == 0)
                        throw new ArgumentOutOfRangeException(nameof(maxChars), "Mandatory actor context and the current message cannot fit the projection budget.");
                    var remove = removable.OrderBy(line => line.Priority).ThenByDescending(line => keptPrefix.IndexOf(line)).First();
                    keptPrefix.Remove(remove);
                    omitted = true;
                    continue;
                }
                if (omitted)
                    throw new ArgumentOutOfRangeException(nameof(maxChars), "The budget cannot retain the current message and context-omission marker.");
                throw new ArgumentOutOfRangeException(nameof(maxChars), "The current message does not fit the projection budget.");
            }
        }

        private static void Require(ConversationContextSnapshot snapshot, IEnumerable<ConversationContextTurn> history,
            string current, int maxChars)
        {
            if (snapshot == null || history == null || current == null || current.Length == 0 || current.Length > 8192 || maxChars < 1)
                throw new ArgumentException("Invalid context projection request.");
        }

        private static void RequireFacts(ConversationContextSnapshot snapshot, int maxChars)
        {
            if (snapshot == null || maxChars < 1)
                throw new ArgumentException("Invalid context projection request.");
        }

        private static void Add(List<ProjectionLine> rows, string text, int priority)
        { rows.Add(new ProjectionLine { Text = text, Priority = priority }); }

        private static void AddEssential(List<ProjectionLine> rows, string text, int priority, int maxChars)
        { rows.Add(new ProjectionLine { Text = text, Priority = priority, Mandatory = maxChars >= 1024 }); }
    }
}
