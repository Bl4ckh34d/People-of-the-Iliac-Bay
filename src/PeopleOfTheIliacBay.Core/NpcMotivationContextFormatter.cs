using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Builds the single bounded motivation projection shared by model providers. This text is context only; it never grants an action or permission.</summary>
    public static class NpcMotivationContextFormatter
    {
        public const int DefaultMaximumCharacters = 6000;
        public const int MaximumAllowedCharacters = 16000;
        private const int ReservedOmissionMarkerCharacters = 240;

        public static string ForModels(NpcMotivationContextSnapshot snapshot, int maximumCharacters = DefaultMaximumCharacters)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (maximumCharacters < 1024 || maximumCharacters > MaximumAllowedCharacters)
                throw new ArgumentOutOfRangeException(nameof(maximumCharacters), "Projection budget must be 1024–16000 characters.");

            var header = new StringBuilder();
            header.Append("Observer-known motivation snapshot (context only; not action authorization).\n");
            header.Append("branch=").Append(snapshot.SaveBranchId).Append(" observer=").Append(snapshot.ObserverId)
                .Append(" revision=").Append(snapshot.Revision.ToString(CultureInfo.InvariantCulture))
                .Append(" capturedGameTime=").Append(Num(snapshot.CapturedGameTime)).Append('\n');
            header.Append("clock=").Append(Evidence(snapshot.Clock.Evidence)).Append('\n');
            header.Append("personalityReference person=").Append(snapshot.Profile.PersonId).Append(" revision=")
                .Append(snapshot.Profile.Revision.ToString(CultureInfo.InvariantCulture)).Append(" source=").Append(Evidence(snapshot.Profile.Evidence)).Append('\n');
            if (snapshot.Relationship == null)
                header.Append("relationshipReference=not-supplied\n");
            else
                header.Append("relationshipReference direction=").Append(snapshot.Relationship.ObserverId).Append("->").Append(snapshot.Relationship.TargetId)
                    .Append(" revision=").Append(snapshot.Relationship.Revision.ToString(CultureInfo.InvariantCulture))
                    .Append(" source=").Append(Evidence(snapshot.Relationship.Evidence)).Append('\n');
            header.Append("Knowledge labels: known means source-backed; attributedClaim is only what the observer heard; unknown has no value.\n");
            if (header.Length > maximumCharacters) throw new ArgumentException("Budget is too small for the mandatory identity and provenance header.", nameof(maximumCharacters));

            var result = new StringBuilder(header.ToString());
            var omitted = new List<string>();
            AppendGroup(result, snapshot.Needs, maximumCharacters, "needs", Format, omitted);
            AppendGroup(result, snapshot.Goals, maximumCharacters, "goals", Format, omitted);
            AppendGroup(result, snapshot.Priorities, maximumCharacters, "priorities", Format, omitted);
            AppendGroup(result, snapshot.Capabilities, maximumCharacters, "capabilities", Format, omitted);
            AppendGroup(result, snapshot.Resources, maximumCharacters, "resources", Format, omitted);
            AppendGroup(result, snapshot.Threats, maximumCharacters, "observerKnownThreats", Format, omitted);
            AppendGroup(result, snapshot.Duties, maximumCharacters, "commitmentsAndDuties", Format, omitted);

            if (omitted.Count > 0)
            {
                string marker = "omittedFacts=" + string.Join(",", omitted) + " (whole facts omitted; no fact was truncated).\n";
                if (result.Length + marker.Length > maximumCharacters)
                    throw new ArgumentException("Budget cannot fit the mandatory omission marker.", nameof(maximumCharacters));
                result.Append(marker);
            }
            return result.ToString();
        }

        private static void AppendGroup<T>(StringBuilder output, IReadOnlyList<T> items, int budget, string group,
            Func<T, string> formatter, List<string> omitted) where T : INpcMotivationFact
        {
            if (items == null || items.Count == 0) return;
            int skipped = 0;
            foreach (T item in items.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                string line = group + ": " + formatter(item) + "\n";
                if (output.Length + line.Length + ReservedOmissionMarkerCharacters <= budget) output.Append(line);
                else skipped++;
            }
            if (skipped > 0) omitted.Add(group + "=" + skipped.ToString(CultureInfo.InvariantCulture));
        }

        private static string Format(NpcMotivationNeed x) { return FactHead(x.Id, x.Kind, x.KnowledgeState, x.Revision, x.Evidence) + " intensity=" + Maybe(x.Intensity); }
        private static string Format(NpcMotivationGoal x) { return FactHead(x.Id, x.Kind, x.KnowledgeState, x.Revision, x.Evidence) + " status=" + x.Status + " priority=" + (x.KnowledgeState == NpcMotivationKnowledgeState.Unknown ? "unknown" : x.Priority.ToString()); }
        private static string Format(NpcMotivationPriority x) { return FactHead(x.Id, x.Kind, x.KnowledgeState, x.Revision, x.Evidence) + " subject=" + x.SubjectId + " rank=" + (x.KnowledgeState == NpcMotivationKnowledgeState.Unknown ? "unknown" : x.Rank.ToString(CultureInfo.InvariantCulture)); }
        private static string Format(NpcMotivationCapabilityConstraint x) { return FactHead(x.Id, x.Kind, x.KnowledgeState, x.Revision, x.Evidence) + " availability=" + (x.KnowledgeState == NpcMotivationKnowledgeState.Unknown ? "unknown" : x.Availability.ToString()); }
        private static string Format(NpcMotivationResourceConstraint x) { return FactHead(x.Id, x.Kind, x.KnowledgeState, x.Revision, x.Evidence) + " current=" + Maybe(x.CurrentAmount) + " required=" + Maybe(x.RequiredAmount); }
        private static string Format(NpcMotivationThreatObservation x) { return FactHead(x.Id, x.Kind, x.KnowledgeState, x.Revision, x.Evidence) + " perceivedSeverity=" + Maybe(x.Severity); }
        private static string Format(NpcMotivationDuty x) { return FactHead(x.Id, x.Kind, x.KnowledgeState, x.Revision, x.Evidence) + " scope=" + x.Scope + " status=" + x.Status + " starts=" + Num(x.StartsAtGameTime) + " ends=" + (x.EndsAtGameTime.HasValue ? Num(x.EndsAtGameTime.Value) : "open"); }

        private static string FactHead(string id, object kind, NpcMotivationKnowledgeState state, long revision, NpcMotivationEvidenceRef evidence)
        {
            return "id=" + id + " kind=" + kind + " state=" + (state == NpcMotivationKnowledgeState.AttributedClaim ? "attributedClaim" : state.ToString()) +
                " revision=" + revision.ToString(CultureInfo.InvariantCulture) + " source=" + (evidence == null ? "none" : Evidence(evidence));
        }

        private static string Evidence(NpcMotivationEvidenceRef x)
        {
            return x.Kind + "(" + x.EvidenceId + ",rev=" + x.Revision.ToString(CultureInfo.InvariantCulture) +
                ",t=" + Num(x.ObservedGameTime) + ",observer=" + x.ObserverId + ")";
        }
        private static string Maybe(int? value) { return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "unknown"; }
        private static string Num(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
    }
}
