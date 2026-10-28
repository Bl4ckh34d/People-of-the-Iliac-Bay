using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // All network work stays on a worker. Update delivers only text to UI code.
    public sealed partial class LocalDialogueClient : MonoBehaviour
    {
        public const string ContextLimit = "__peopleoftheiliacbay_context_limit__";
        public const string Unavailable = "__peopleoftheiliacbay_model_unavailable__";
        private sealed class PendingResult
        {
            public int Version;
            public DialogueRequest Request;
            public Action<string> Complete;
            public string Body;
            public string RequestJson;
            public Action<string,string,string> ClassificationTrace;
            public bool TransportFailed;
            public string TransportFailureStage;
            public bool SuppressContextLimitHud;
            public bool IsStreamSentence;
            public string StreamSentence;
            public Func<string, bool> StreamSentenceCallback;
            public Action<SocialDialogueStreamResult> StreamComplete;
            public SocialDialogueStreamResult StreamResult;
            public List<string> AcceptedStreamSegments;
        }

        private readonly object completedGate = new object();
        private readonly Queue<PendingResult> completed = new Queue<PendingResult>();
        private readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        private readonly Queue<string> cacheOrder = new Queue<string>();
        private readonly LocalDialogueDispatch dispatch = new LocalDialogueDispatch();
        private readonly object warmupGate = new object();
        private HttpWebRequest warmupRequest;
        private bool shuttingDown;
        private int warmupStarted;
        internal long ReplyRequestCount { get; private set; }
        // Counts fresh nonempty backend replies, excluding cache/fallback delivery.
        internal long ReplyCompletionCount { get; private set; }
        // Optional passive diagnostics. Normal gameplay keeps this unset.
        // Main-thread observation cannot change the request or suppress its callback.
        internal Action<DialogueRequest, string, string, string> ForegroundReplyTrace { get; set; }
        internal Action<string, string, string> ForegroundClassificationTrace { get; set; }
        public bool IsBusy { get { return dispatch.IsBusy; } }

        public bool Classify(LocalDialogueConnection connection, string prompt, string history, Action<string> complete, string grammar=null,
            int responseTokens=32,Action<string,string,string> classificationTrace=null)
        {
            if (connection == null || complete == null) return false;
            var completion = LocalDialogueCompletion.Classify(connection, prompt, history, grammar, responseTokens);
            int version = dispatch.TryBegin();
            if (version == 0) return false;
            var observer = ForegroundClassificationTrace;
            Action<string,string,string> trace = classificationTrace;
            if(observer != null)
                trace = (request,response,classification) =>
                {
                    try { observer(request,response,classification); }
                    catch(Exception error)
                    { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_CLASSIFIER_TRACE_FAILURE: " + error.GetType().Name); }
                    if(classificationTrace != null)classificationTrace(request,response,classification);
                };
            ThreadPool.QueueUserWorkItem(ignored => Fetch(completion, null, complete, version,trace));
            return true;
        }

        // Low-priority classification shares the same dialogue client and transport.
        // A later foreground operation takes the dispatch slot and invalidates this ticket.
        public int TryClassifyBackground(LocalDialogueConnection connection, string prompt, Action<string> complete,
            string grammar = null, int responseTokens = 32, Action<string, string, string> classificationTrace = null)
        {
            if (connection == null || complete == null) return 0;
            var completion = LocalDialogueCompletion.Classify(connection, prompt, string.Empty, grammar, responseTokens);
            return TryQueueBackground(completion, complete, classificationTrace);
        }

        // Appraisal is a distinct structured contract, not a generic classifier.
        // Keep its evidence-bound grammar and token budget from Core unchanged.
        public int TryAppraiseNpcExperienceBackground(LocalDialogueConnection connection,
            NpcExperienceAppraisalRequest request, Action<string> complete,
            Action<string, string, string> completionTrace = null)
        {
            if (connection == null || request == null || complete == null) return 0;
            DialogueCompletion completion = LocalDialogueCompletion.BuildAppraisal(connection, request);
            return TryQueueBackground(completion, complete, completionTrace);
        }

        private int TryQueueBackground(DialogueCompletion completion, Action<string> complete,
            Action<string, string, string> classificationTrace)
        {
            int version;
            if (!dispatch.TryBeginBackground(out version)) return 0;
            bool queued;
            try { queued = ThreadPool.QueueUserWorkItem(ignored => Fetch(completion, null, complete, version, classificationTrace, true)); }
            catch (NotSupportedException) { queued = false; }
            catch (InvalidOperationException) { queued = false; }
            if (queued) return version;
            dispatch.CancelBackground(version);
            return 0;
        }

        public bool CancelBackground(int ticket)
        { return dispatch.CancelBackground(ticket); }

        public bool IsBackgroundCurrent(int ticket)
        { return dispatch.IsBackgroundCurrent(ticket); }

        public bool PendingText(LocalDialogueConnection connection, string instructions, Action<string> complete)
        {
            if (connection == null || complete == null) return false;
            // Build before taking dispatch ownership so invalid text cannot
            // strand this auxiliary client in a busy state.
            DialogueCompletion completion = ConversationPendingText.Completion(connection, instructions);
            int version = dispatch.TryBegin();
            if (version == 0) return false;
            ThreadPool.QueueUserWorkItem(ignored => Fetch(completion, null, complete, version, null));
            return true;
        }

        public bool Extract(LocalDialogueConnection connection, string prompt, string history, string grammar,
            Action<string> complete, int responseTokens=1024, Action<string,string,string> trace=null)
        {
            if (connection == null || complete == null) return false;
            // Construct before owning dispatch so invalid inputs cannot strand it.
            var completion = LocalDialogueCompletion.Extract(connection, prompt, history, grammar, responseTokens);
            int version = dispatch.TryBegin();
            if (version == 0) return false;
            ThreadPool.QueueUserWorkItem(ignored => Fetch(completion, null, complete, version, trace));
            return true;
        }

        public bool Request(LocalDialogueConnection connection, DialogueRequest request, Action<string> complete)
        {
            if (connection == null || request == null || complete == null) return false;
            if (ReplyRequestCount < long.MaxValue) ReplyRequestCount++;
            LocalDialogueConnection injectedConnection=DialogueAvailabilityProbe.ConnectionFor(connection,request);
            bool injected=!ReferenceEquals(injectedConnection,connection);
            connection=injectedConnection;
            string cacheKey = LocalDialogue.CacheKey(request);
            if (!request.MoodEnabled && !injected && cache.TryGetValue(cacheKey, out string cached))
            {
                // No transport is needed, but foreground work still invalidates a background ticket.
                int foregroundVersion = dispatch.TryBegin();
                if (foregroundVersion != 0) dispatch.TryFinish(foregroundVersion);
                complete(cached);
                return true;
            }
            DialogueCompletion completion = LocalDialogueCompletion.Build(connection, request);
            int version = dispatch.TryBegin();
            if (version == 0) return false;
            ThreadPool.QueueUserWorkItem(ignored => Fetch(completion, request, complete, version,null));
            return true;
        }

        // Opt-in sentence stream for a fully validated social dialogue request.
        // Sentence and final callbacks run from Update on Unity's main thread.
        public bool RequestStreaming(LocalDialogueConnection connection, DialogueRequest request,
            Func<string, bool> onSentence, Action<SocialDialogueStreamResult> complete)
        {
            if (connection == null || request == null || onSentence == null || complete == null ||
                !request.SocialOnly || !request.MoodEnabled || request.ActionReply != null) return false;
            if (ReplyRequestCount < long.MaxValue) ReplyRequestCount++;
            connection = DialogueAvailabilityProbe.ConnectionFor(connection, request);
            DialogueCompletion completion = LocalDialogueCompletion.BuildStreaming(connection, request);
            int version = dispatch.TryBegin();
            if (version == 0) return false;
            var acceptedSegments = new List<string>();
            bool queued;
            try
            {
                queued = ThreadPool.QueueUserWorkItem(ignored => Fetch(completion, request, null, version, null,
                    false, onSentence, complete, acceptedSegments));
            }
            catch (NotSupportedException) { queued = false; }
            catch (InvalidOperationException) { queued = false; }
            if (queued) return true;
            dispatch.Cancel();
            return false;
        }

        public void Cancel()
        {
            dispatch.Cancel();
        }

        private void OnDestroy()
        {
            Cancel();
            HttpWebRequest activeWarmup;
            lock (warmupGate)
            {
                shuttingDown = true;
                activeWarmup = warmupRequest;
                warmupRequest = null;
            }
            if (activeWarmup != null)
            {
                try { activeWarmup.Abort(); }
                catch { }
            }
        }

        // Never occupies the chat request slot. This only asks llama.cpp to
        // load weights and initialise CUDA on a background worker.
        public bool Prewarm(LocalDialogueConnection connection)
        {
            if (connection == null || Interlocked.Exchange(ref warmupStarted, 1) != 0) return false;
            ThreadPool.QueueUserWorkItem(ignored => Warm(connection));
            return true;
        }

        private void Warm(LocalDialogueConnection connection)
        {
            HttpWebRequest web = null;
            try
            {
                string json = "{\"prompt\":\"" + LocalDialogueWarmup.Prompt +
                    "\",\"n_predict\":1,\"temperature\":0,\"stop\":[\"\\n\"]}";
                byte[] data = Encoding.UTF8.GetBytes(json);
                web = LocalDialogueHttp.Create(connection.Endpoint);
                lock (warmupGate)
                {
                    if (shuttingDown) return;
                    warmupRequest = web;
                }
                web.Method = "POST";
                web.ContentType = "application/json";
                web.ContentLength = data.Length;
                web.Timeout = Math.Max(connection.TimeoutMilliseconds, 10000);
                web.ReadWriteTimeout = Math.Max(connection.TimeoutMilliseconds, 10000);
                using (Stream stream = web.GetRequestStream()) stream.Write(data, 0, data.Length);
                using (var response = (HttpWebResponse)web.GetResponse()) { }
            }
            catch (WebException) { }
            catch (IOException) { }
            catch (InvalidOperationException) { }
            finally
            {
                if (web != null)
                {
                    lock (warmupGate)
                    {
                        if (ReferenceEquals(warmupRequest, web)) warmupRequest = null;
                    }
                }
            }
        }

        private void Fetch(DialogueCompletion completion, DialogueRequest request, Action<string> complete, int version,
            Action<string,string,string> classificationTrace, bool suppressContextLimitHud = false,
            Func<string, bool> streamSentence = null, Action<SocialDialogueStreamResult> streamComplete = null,
            List<string> acceptedStreamSegments = null)
        {
            if (!dispatch.IsCurrent(version)) return;
            string body = null;
            string requestJson=null;
            string stage="props";
            bool transportFailed=false;
            string transportFailureStage=null;
            try
            {
                string properties = Send(dispatch,version,new Uri(completion.Endpoint, "/props"), null, completion.TimeoutMilliseconds);
                int budget = LocalDialogueNativeTemplate.ContextSize(properties) - completion.ResponseTokens - 32;
                // Cache each rendered candidate so token budgeting and generation use
                // exactly the same server-native prompt, including special tokens.
                var nativePrompts = new Dictionary<string, string>(StringComparer.Ordinal);
                string fitted = DialogueHistoryWindow.Fit(completion.Prompt, completion.History, budget, prompt => {
                    stage="apply-template";
                    string rendered = LocalDialogueNativeTemplate.ReadPrompt(Send(dispatch, version,
                        new Uri(completion.Endpoint, "/apply-template"), LocalDialogueNativeTemplate.Request(prompt),
                        completion.TimeoutMilliseconds));
                    nativePrompts[prompt] = rendered;
                    stage="tokenize";
                    string reply = Send(dispatch,version,new Uri(completion.Endpoint, "/tokenize"),
                        "{\"content\":\"" + LocalDialogueCompletion.EscapeJson(rendered) + "\",\"add_special\":true}", completion.TimeoutMilliseconds);
                    return LocalDialogueNativeTemplate.TokenCount(reply);
                }, completion.StructuredHistory);
                stage="context-fit";
                if (fitted == null) throw new InvalidOperationException("context limit");
                stage="payload";
                if (fitted.Length < completion.Prompt.Length)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_WINDOW: history trimmed; prompt characters=" + completion.Prompt.Length + " -> " + fitted.Length + "; token budget=" + budget);
                string payload = LocalDialogueNativeTemplate.Payload(completion.JsonBody, nativePrompts[fitted]);
                requestJson=payload;
                stage="completion";
                if (streamComplete != null)
                {
                    SocialDialogueStreamResult streamResult = SendStreaming(dispatch, version, completion.Endpoint,
                        payload, completion.TimeoutMilliseconds, streamSentence, streamComplete, acceptedStreamSegments);
                    QueueStreamComplete(version, streamSentence, streamComplete, streamResult);
                    return;
                }
                body = Send(dispatch,version,completion.Endpoint,payload,completion.TimeoutMilliseconds);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebException error)
            {
                if (!dispatch.IsCurrent(version))
                {
                    if (error.Response != null)
                    {
                        try { error.Response.Close(); }
                        catch { }
                    }
                    return;
                }
                var http=error.Response as HttpWebResponse;
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage="+stage+"; transport="+error.Status+
                    "; http="+(http==null?"none":((int)http.StatusCode).ToString())+".");
                if (error.Response != null)
                {
                    try
                    {
                    using (var response = error.Response)
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        body = reader.ReadToEnd();
                    }
                    catch (IOException) { body=null; transportFailed=true; transportFailureStage=stage; Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage="+stage+"; reason=error-response-io."); }
                    catch (InvalidOperationException) { body=null; Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage="+stage+"; reason=error-response-protocol."); }
                }
                else { transportFailed=true; transportFailureStage=stage; }
            }
            catch (IOException)
            {
                if (!dispatch.IsCurrent(version)) return;
                transportFailed=true; transportFailureStage=stage;
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage="+stage+"; reason=io.");
            }
            catch (InvalidOperationException)
            {
                if (!dispatch.IsCurrent(version)) return;
                bool contextExceeded=stage=="context-fit";
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage="+stage+"; reason="+
                    (contextExceeded?"context-limit":"protocol")+".");
                body = contextExceeded?"{\"error\":\"context budget exceeded\"}":null;
            }
            if (!dispatch.IsCurrent(version)) return;
            if (streamComplete != null)
            {
                string failure = transportFailed ? "transport-unavailable" :
                    stage == "context-fit" ? "context-limit" : "protocol-error";
                QueueStreamComplete(version, streamSentence, streamComplete, SocialDialogueStreamResult.Failed(failure));
                return;
            }
            lock (completedGate)
            {
                if (!dispatch.IsCurrent(version)) return;
                completed.Enqueue(new PendingResult { Version = version, Request = request, Complete = complete, Body = body,
                    RequestJson=requestJson,ClassificationTrace=classificationTrace,TransportFailed=transportFailed,
                    TransportFailureStage=transportFailureStage, SuppressContextLimitHud=suppressContextLimitHud });
            }
        }

        private static string Send(LocalDialogueDispatch dispatch,int version,Uri endpoint,string json,int timeout)
        {
            var web = LocalDialogueHttp.Create(endpoint);
            Action abort=web.Abort;
            if(!dispatch.TryRegisterAbort(version,abort))throw new OperationCanceledException();
            try
            {
                if(!dispatch.IsCurrent(version))throw new OperationCanceledException();
                web.Method = json == null ? "GET" : "POST";
                web.Timeout = timeout; web.ReadWriteTimeout = timeout;
                if (json != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(json);
                    web.ContentType = "application/json"; web.ContentLength = bytes.Length;
                    using (Stream stream = web.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
                }
                if(!dispatch.IsCurrent(version))throw new OperationCanceledException();
                using (var response = web.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
            }
            finally { dispatch.ClearAbort(version,abort); }
        }

        private SocialDialogueStreamResult SendStreaming(LocalDialogueDispatch activeDispatch, int version,
            Uri endpoint, string json, int timeout, Func<string, bool> sentenceCallback,
            Action<SocialDialogueStreamResult> completeCallback, List<string> acceptedSegments)
        {
            var parser = new SocialDialogueStreamParser();
            parser.SentenceReady += sentence => QueueStreamSentence(version, sentenceCallback, completeCallback,
                acceptedSegments, sentence);
            var web = LocalDialogueHttp.Create(endpoint);
            Action abort = web.Abort;
            if (!activeDispatch.TryRegisterAbort(version, abort))
                return parser.Finish("cancelled");
            try
            {
                if (!activeDispatch.IsCurrent(version)) return parser.Finish("cancelled");
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                web.Method = "POST";
                web.ContentType = "application/json";
                web.ContentLength = bytes.Length;
                web.Timeout = timeout;
                web.ReadWriteTimeout = timeout;
                using (Stream request = web.GetRequestStream()) request.Write(bytes, 0, bytes.Length);
                if (!activeDispatch.IsCurrent(version)) return parser.Finish("cancelled");
                using (var response = (HttpWebResponse)web.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), new UTF8Encoding(false, true), false))
                {
                    while (activeDispatch.IsCurrent(version))
                    {
                        string line = ReadBoundedLine(reader, SocialDialogueStreamParser.DefaultMaximumFrameCharacters);
                        if (line == null) break;
                        if (!parser.FeedSseLine(line)) break;
                    }
                }
                return parser.Finish();
            }
            catch (OperationCanceledException)
            {
                return parser.Finish("cancelled");
            }
            catch (WebException)
            {
                return parser.Finish(activeDispatch.IsCurrent(version) ? "transport-error" : "cancelled");
            }
            catch (IOException)
            {
                return parser.Finish(activeDispatch.IsCurrent(version) ? "transport-error" : "cancelled");
            }
            catch (DecoderFallbackException)
            {
                return parser.Finish(activeDispatch.IsCurrent(version) ? "protocol-error" : "cancelled");
            }
            catch (InvalidOperationException)
            {
                return parser.Finish(activeDispatch.IsCurrent(version) ? "protocol-error" : "cancelled");
            }
            finally { activeDispatch.ClearAbort(version, abort); }
        }

        private static string ReadBoundedLine(StreamReader reader, int maximumCharacters)
        {
            var line = new StringBuilder();
            while (true)
            {
                int next = reader.Read();
                if (next < 0) return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
                if (next == '\n') return line.ToString().TrimEnd('\r');
                if (line.Length >= maximumCharacters) throw new InvalidOperationException("SSE frame exceeded limit.");
                line.Append((char)next);
            }
        }

        private void QueueStreamSentence(int version, Func<string, bool> callback,
            Action<SocialDialogueStreamResult> completeCallback, List<string> acceptedSegments, string sentence)
        {
            if (callback == null || !dispatch.IsCurrent(version)) return;
            lock (completedGate)
            {
                if (!dispatch.IsCurrent(version)) return;
                if (completed.Count >= 64) throw new InvalidOperationException("Dialogue stream callback queue exceeded limit.");
                completed.Enqueue(new PendingResult { Version = version, IsStreamSentence = true,
                    StreamSentence = sentence, StreamSentenceCallback = callback,
                    StreamComplete = completeCallback, AcceptedStreamSegments = acceptedSegments });
            }
        }

        private void QueueStreamComplete(int version, Func<string, bool> sentenceCallback,
            Action<SocialDialogueStreamResult> complete, SocialDialogueStreamResult result)
        {
            if (!dispatch.IsCurrent(version)) return;
            lock (completedGate)
            {
                if (!dispatch.IsCurrent(version)) return;
                completed.Enqueue(new PendingResult { Version = version, StreamSentenceCallback = sentenceCallback,
                    StreamComplete = complete, StreamResult = result });
            }
        }

        private void Update()
        {
            PendingResult result = null;
            lock (completedGate)
            {
                if (completed.Count > 0) result = completed.Dequeue();
            }
            if (result == null) return;
            if (result.IsStreamSentence)
            {
                if (!dispatch.IsCurrent(result.Version)) return;
                bool accepted = false;
                try { accepted = result.StreamSentenceCallback != null && result.StreamSentenceCallback(result.StreamSentence); }
                catch (Exception error)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_STREAM_CALLBACK_FAIL: " + error.GetType().Name); }
                if (!accepted)
                {
                    dispatch.Cancel();
                    if (result.StreamComplete != null)
                    {
                        try { result.StreamComplete(SocialDialogueStreamResult.Failed("consumer-rejected", result.AcceptedStreamSegments)); }
                        catch (Exception error)
                        { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_STREAM_COMPLETE_CALLBACK_FAIL: " + error.GetType().Name); }
                    }
                }
                else if (result.AcceptedStreamSegments != null)
                    result.AcceptedStreamSegments.Add(result.StreamSentence);
                return;
            }
            if (result.StreamComplete != null)
            {
                if (!dispatch.TryFinish(result.Version)) return;
                SocialDialogueStreamResult streamResult = result.StreamResult ?? SocialDialogueStreamResult.Failed("protocol-error");
                if (streamResult.Complete && ReplyCompletionCount < long.MaxValue) ReplyCompletionCount++;
                try { result.StreamComplete(streamResult); }
                catch (Exception error)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_STREAM_COMPLETE_CALLBACK_FAIL: " + error.GetType().Name); }
                return;
            }
            if (!dispatch.TryFinish(result.Version)) return;
            if (result.TransportFailed)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage="+
                    (result.TransportFailureStage??"unknown")+"; reason=transport-unavailable.");
                if (result.ClassificationTrace != null)
                    result.ClassificationTrace(result.RequestJson??string.Empty,result.Body??string.Empty,Unavailable);
                TraceForegroundReply(result, Unavailable);
                result.Complete(Unavailable);
                return;
            }
            if (result.Body != null && (LocalDialogueNativeTemplate.IsTruncated(result.Body) ||
                LocalDialogueNativeTemplate.HasContextLimitError(result.Body)))
            {
                if (!result.SuppressContextLimitHud)
                    DaggerfallUI.AddHUDText("Local model context unavailable or too small for NPC/world data. Check server context size.");
                if(result.ClassificationTrace!=null)result.ClassificationTrace(result.RequestJson??string.Empty,result.Body,ContextLimit);
                TraceForegroundReply(result, ContextLimit);
                result.Complete(ContextLimit);
                return;
            }
            if (result.Request == null) { string classification = ExtractContent(result.Body);
                if(string.IsNullOrWhiteSpace(classification))
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage=classification; reason=empty-or-invalid-response.");
                if(result.ClassificationTrace!=null)result.ClassificationTrace(result.RequestJson??string.Empty,result.Body??string.Empty,
                    string.IsNullOrWhiteSpace(classification)?Unavailable:classification);
                result.Complete(string.IsNullOrWhiteSpace(classification) ? Unavailable : classification); return; }
            string extracted=ExtractContent(result.Body);
            if (!string.IsNullOrWhiteSpace(extracted) && ReplyCompletionCount < long.MaxValue) ReplyCompletionCount++;
            TraceForegroundReply(result, extracted);
            if (result.Request.MoodEnabled)
            {
                SocialDialogueResult socialResult;
                if (!SocialDialogueResult.TryParse(extracted, out socialResult))
                {
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage=social-result; reason=empty-or-rejected-response.");
                    result.Complete(Unavailable);
                    return;
                }
                result.Complete(socialResult.ToJson());
                return;
            }
            string answer;
            bool validReply = LocalDialogue.TrySelectAnswer(result.Request, extracted, out answer);
            if (result.Request.ActionReply != null && !validReply)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage=action-wording; reason=empty-or-rejected-response.");
                result.Complete(Unavailable);
                return;
            }
            if (!validReply) answer = result.Request.VanillaAnswer;
            if (result.Request.ActionReply != null &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-action-wording-probe") >= 0)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_WORDING_MODEL_RESULT: status=" + result.Request.ActionReply.Status +
                    "; content=" + (extracted ?? "<missing>") + "; selected=" + answer);
            if (result.Request.SocialOnly && answer == result.Request.VanillaAnswer)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FAILURE: stage=reply; reason=empty-or-rejected-response; characters="+
                    (extracted==null?0:extracted.Length)+".");
                result.Complete(Unavailable); return;
            }
            string cacheKey = LocalDialogue.CacheKey(result.Request);
            if (!result.Request.MoodEnabled && answer != result.Request.VanillaAnswer) AddCache(cacheKey, answer);
            result.Complete(answer);
        }

        private void AddCache(string key, string value)
        {
            if (cache.ContainsKey(key)) return;
            while (cacheOrder.Count >= 64) cache.Remove(cacheOrder.Dequeue());
            cache.Add(key, value);
            cacheOrder.Enqueue(key);
        }

        private void TraceForegroundReply(PendingResult result, string extracted)
        {
            var observer = ForegroundReplyTrace;
            if (observer == null || result.Request == null) return;
            try { observer(result.Request, result.RequestJson ?? string.Empty, result.Body ?? string.Empty, extracted); }
            catch (Exception error)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_FOREGROUND_REPLY_TRACE_FAILED: " + error.GetType().Name);
            }
        }

        private static string ExtractContent(string json)
        { return LocalDialogueNativeTemplate.ReadCompletionContent(json); }
    }
}
