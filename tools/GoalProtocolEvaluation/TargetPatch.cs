using System.Text.Json;
using System.Text.Json.Nodes;
using PeopleOfTheIliacBay.Core;

// Experimental evaluator only. No language parsing or gameplay execution.
internal sealed class TargetPatch
{
    private readonly string anchor;
    private readonly int[] indices;
    public string Prompt { get; }
    public string Grammar { get; }

    private TargetPatch(string anchor, int[] indices, NpcGoalProposal proposal)
    {
        this.anchor = anchor; this.indices = indices;
        Prompt = "Fill only the missing target of each listed step, in this exact order. " +
            "For Purchase or Transfer, target is the object. For MoveTo, target is the destination. " +
            "Copy the target exactly from THAT step's source quote, preserving singular/plural spelling. " +
            "If absent or unclear, return an empty string. Do not infer a target from a different step. " +
            "Return only {\"targets\":[\"target for first listed step\",...]} with exactly " + indices.Length + " strings.\n" +
            "Untrusted step data:\n" + JsonSerializer.Serialize(indices.Select(i => new { index = i, kind = proposal.Steps[i].Kind, source = proposal.Steps[i].Source.Text }));
        Grammar = "root ::= " + JsonSerializer.Serialize("{\"targets\":[") + " " +
            string.Join(" ", indices.Select((_, i) => (i == 0 ? "" : JsonSerializer.Serialize(",") + " ") + "string")) +
            " " + JsonSerializer.Serialize("]}") + "\n" +
            "string ::= \"\\\"\" ( [^\"\\\\\\x00-\\x1f] | \"\\\\\" ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F]{4}) )* \"\\\"\"\n";
    }

    public static TargetPatch Prepare(string anchor, NpcGoalDecomposition decomposition)
    {
        var parsed = NpcGoalTwoPassChat.ParseParameters(anchor, decomposition);
        if (parsed.Status != NpcGoalExtractionStatus.Valid) return null;
        var indices = parsed.Proposal.Steps.Select((step, i) => (step, i)).Where(x =>
            x.step.TargetText.Length == 0 && (x.step.Kind == "MoveTo" || x.step.Kind == "Purchase" || x.step.Kind == "Transfer")).Select(x => x.i).ToArray();
        return indices.Length == 0 ? null : new TargetPatch(anchor, indices, parsed.Proposal);
    }

    public string Merge(string delta)
    {
        try
        {
            using var parsed = JsonDocument.Parse(delta);
            var root = parsed.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Array ||
                targets.GetArrayLength() != indices.Length || targets.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String)) return "{}";
            var merged = JsonNode.Parse(anchor).AsObject();
            for (int j = 0; j < indices.Length; j++)
            {
                string target = targets[j].GetString();
                if (string.IsNullOrWhiteSpace(target)) continue;
                var step = merged["steps"][indices[j]].AsObject();
                step["target"] = target;
                RemoveHint(step["missing"].AsArray(), "target");
                RemoveHint(merged["missing"].AsArray(), "steps[" + indices[j] + "].target");
            }
            // The existing Core validates source containment and immutable safe-fill constraints next.
            return merged.ToJsonString();
        }
        catch (JsonException) { return "{}"; }
    }

    private static void RemoveHint(JsonArray hints, string exact)
    {
        for (int i = hints.Count - 1; i >= 0; i--)
            if (hints[i].GetValue<string>() == exact) hints.RemoveAt(i);
    }
}
