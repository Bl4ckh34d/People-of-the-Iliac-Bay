using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class LocalDialogueClient
    {
        // Gemma-only speech attribution uses the normal /completion transport, native
        // template, and the same dispatcher as foreground dialogue and background work.
        public bool ClassifySpeech(LocalDialogueConnection connection, NpcSpeechActRequest request,
            Action<NpcSpeechActClassification> complete, Action<string, string, string> trace = null)
        {
            if (connection == null || request == null || complete == null) return false;
            return Classify(connection, LocalSpeechActLabelWire.Prompt(request), string.Empty,
                SpeechLabelCallback(request, complete), LocalSpeechActLabelWire.Grammar(),
                LocalSpeechActLabelWire.ResponseTokens, trace);
        }

        // Background callers keep the integer ticket needed for cancellation and stale-result checks.
        public int TryClassifySpeechBackground(LocalDialogueConnection connection, NpcSpeechActRequest request,
            Action<NpcSpeechActClassification> complete, Action<string, string, string> trace = null)
        {
            if (connection == null || request == null || complete == null) return 0;
            return TryClassifyBackground(connection, LocalSpeechActLabelWire.Prompt(request),
                SpeechLabelCallback(request, complete), LocalSpeechActLabelWire.Grammar(),
                LocalSpeechActLabelWire.ResponseTokens, trace);
        }

        private static Action<string> SpeechLabelCallback(NpcSpeechActRequest request,
            Action<NpcSpeechActClassification> complete)
        {
            return response =>
            {
                NpcSpeechActClassification classification;
                LocalSpeechActLabelWire.TryParse(request, response, out classification);
                complete(classification);
            };
        }

    }
}
