using System;
using System.IO;
using System.Net;
using System.Threading;
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

        // The existing dispatch owns both stages: foreground preemption aborts the exact HTTP request.
        // Keep this systemone probability-vector adapter isolated for legacy callers and tests.
        public int TryClassifySpeech(LocalDialogueConnection connection, string expectedModel,
            NpcSpeechActRequest request, bool background, Action<NpcSpeechActClassification> complete,
            Action<string, string, string> trace = null)
        {
            if (connection == null || request == null || complete == null || string.IsNullOrWhiteSpace(expectedModel) ||
                connection.Endpoint.AbsolutePath != "/v1/systemone" || connection.Endpoint.Query.Length != 0) return 0;
            string payload = LocalSpeechActWire.Build(request);
            int version;
            if (background) { if (!dispatch.TryBeginBackground(out version)) return 0; }
            else { version = dispatch.TryBegin(); if (version == 0) return 0; }
            bool queued;
            try
            {
                queued = ThreadPool.QueueUserWorkItem(ignored =>
                {
                    string body = null;
                    NpcSpeechActClassification classification = null;
                    try
                    {
                        body = Send(dispatch, version, connection.Endpoint, payload, connection.TimeoutMilliseconds);
                        LocalSpeechActWire.TryParse(request, expectedModel, body, out classification);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (WebException error) { if (error.Response != null) error.Response.Close(); }
                    catch (IOException) { }
                    catch (InvalidOperationException) { }
                    if (!dispatch.IsCurrent(version)) return;
                    lock (completedGate)
                    {
                        if (!dispatch.IsCurrent(version)) return;
                        completed.Enqueue(new PendingResult { Version = version, SpeechComplete = complete,
                            SpeechClassification = classification, Body = body, RequestJson = payload, ClassificationTrace = trace });
                    }
                });
            }
            catch (NotSupportedException) { queued = false; }
            catch (InvalidOperationException) { queued = false; }
            if (queued) return version;
            if (background) dispatch.CancelBackground(version); else dispatch.TryFinish(version);
            return 0;
        }
    }
}
