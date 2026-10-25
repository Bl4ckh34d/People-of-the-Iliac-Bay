using System;
using System.Linq;
using System.Text;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class LocalSpeechActWire
    {
        private static string Quote(string value) { return "\"" + LocalDialogueCompletion.EscapeJson(value) + "\""; }
        internal static string Build(NpcSpeechActRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var b = new StringBuilder("{\"state\":");
            b.Append(Quote(request.State)).Append(",\"questions\":{\"decision\":{\"type\":\"choice\",\"instructions\":")
                .Append(Quote(NpcSpeechActRequest.Instructions)).Append(",\"criteria\":{");
            for (int i = 0; i < NpcSpeechActRequest.Options.Count; i++)
            {
                if (i > 0) b.Append(',');
                var option = NpcSpeechActRequest.Options[i];
                b.Append(Quote(option.Id)).Append(':').Append(Quote(option.Description));
            }
            return b.Append("}}}}").ToString();
        }

        internal static bool TryParse(NpcSpeechActRequest request, string expectedModel, string response,
            out NpcSpeechActClassification classification)
        {
            classification = null;
            var parsed = LocalDecisionWire.Parse(response);
            if (!parsed.IsValid || string.IsNullOrWhiteSpace(expectedModel) || parsed.ModelId != expectedModel ||
                parsed.Probabilities == null || parsed.Probabilities.Count != NpcSpeechActRequest.Options.Count ||
                NpcSpeechActRequest.Options.Any(x => !parsed.Probabilities.ContainsKey(x.Id))) return false;
            double sum = 0, max = 0, selected;
            foreach (double probability in parsed.Probabilities.Values)
            {
                if (double.IsNaN(probability) || double.IsInfinity(probability) || probability < 0 || probability > 1) return false;
                sum += probability; max = Math.Max(max, probability);
            }
            if (Math.Abs(sum - 1) > 0.001 || !parsed.Probabilities.TryGetValue(parsed.SelectedOptionId, out selected) ||
                selected + 0.000001 < max) return false;
            return NpcSpeechActClassification.TryAccept(request, parsed.SelectedOptionId, out classification);
        }
    }
}
