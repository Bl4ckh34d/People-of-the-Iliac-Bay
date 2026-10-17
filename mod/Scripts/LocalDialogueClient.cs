using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // All network work stays on a worker. Update delivers only text to UI code.
    public sealed class LocalDialogueClient : MonoBehaviour
    {
        public const string ContextLimit = "__livingcities_context_limit__";
        public const string Unavailable = "__livingcities_model_unavailable__";
        private sealed class PendingResult
        {
            public int Version;
            public DialogueRequest Request;
            public Action<string> Complete;
            public string Body;
        }

        private readonly object completedGate = new object();
        private readonly Queue<PendingResult> completed = new Queue<PendingResult>();
        private readonly Dictionary<string, string> cache = new Dictionary<string, string>();
        private readonly Queue<string> cacheOrder = new Queue<string>();
        private readonly LocalDialogueDispatch dispatch = new LocalDialogueDispatch();
        private int warmupStarted;

        public bool Classify(LocalDialogueConnection connection, string prompt, string history, Action<string> complete)
        {
            if (connection == null || complete == null) return false;
            int version = dispatch.TryBegin();
            if (version == 0) return false;
            var completion = LocalDialogueCompletion.Classify(connection, prompt, history);
            ThreadPool.QueueUserWorkItem(ignored => Fetch(completion, null, complete, version));
            return true;
        }

        public bool Request(LocalDialogueConnection connection, DialogueRequest request, Action<string> complete)
        {
            if (connection == null || request == null || complete == null) return false;
            string cacheKey = LocalDialogue.CacheKey(request);
            if (cache.TryGetValue(cacheKey, out string cached))
            {
                complete(cached);
                return true;
            }
            int version = dispatch.TryBegin();
            if (version == 0) return false;
            DialogueCompletion completion = LocalDialogueCompletion.Build(connection, request);
            ThreadPool.QueueUserWorkItem(ignored => Fetch(completion, request, complete, version));
            return true;
        }

        public void Cancel()
        {
            dispatch.Cancel();
        }

        // Never occupies the chat request slot. This only asks llama.cpp to
        // load weights and initialise CUDA on a background worker.
        public bool Prewarm(LocalDialogueConnection connection)
        {
            if (connection == null || Interlocked.Exchange(ref warmupStarted, 1) != 0) return false;
            ThreadPool.QueueUserWorkItem(ignored => Warm(connection));
            return true;
        }

        private static void Warm(LocalDialogueConnection connection)
        {
            try
            {
                string json = "{\"prompt\":\"" + LocalDialogueWarmup.Prompt +
                    "\",\"n_predict\":1,\"temperature\":0,\"stop\":[\"\\n\"]}";
                byte[] data = Encoding.UTF8.GetBytes(json);
                var web = (HttpWebRequest)WebRequest.Create(connection.Endpoint);
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
        }

        private void Fetch(DialogueCompletion completion, DialogueRequest request, Action<string> complete, int version)
        {
            string body = null;
            try
            {
                string properties = Send(new Uri(completion.Endpoint, "/props"), null, completion.TimeoutMilliseconds);
                Match size = Regex.Match(properties, "\"n_ctx\"\\s*:\\s*(\\d+)");
                if (!size.Success) throw new InvalidOperationException("Missing server context size");
                int budget = int.Parse(size.Groups[1].Value) - completion.ResponseTokens - 32;
                string fitted = DialogueHistoryWindow.Fit(completion.Prompt, completion.History, budget, prompt => {
                    string reply = Send(new Uri(completion.Endpoint, "/tokenize"),
                        "{\"content\":\"" + LocalDialogueCompletion.EscapeJson(prompt) + "\",\"add_special\":true}", completion.TimeoutMilliseconds);
                    Match list = Regex.Match(reply, "\"tokens\"\\s*:\\s*\\[([^\\]]*)\\]");
                    if (!list.Success) throw new InvalidOperationException("Missing token count");
                    return Regex.Matches(list.Groups[1].Value, "-?[0-9]+").Count;
                }, completion.StructuredHistory);
                if (fitted == null) throw new InvalidOperationException("context limit");
                if (fitted.Length < completion.Prompt.Length)
                    Debug.Log("LIVING_CITIES_DIALOGUE_WINDOW: history trimmed; prompt characters=" + completion.Prompt.Length + " -> " + fitted.Length + "; token budget=" + budget);
                string payload = completion.JsonBody.Replace("\"prompt\":\"" + LocalDialogueCompletion.EscapeJson(completion.Prompt) + "\"",
                    "\"prompt\":\"" + LocalDialogueCompletion.EscapeJson(fitted) + "\"");
                byte[] data = Encoding.UTF8.GetBytes(payload);
                var web = (HttpWebRequest)WebRequest.Create(completion.Endpoint);
                web.Method = "POST";
                web.ContentType = "application/json";
                web.ContentLength = data.Length;
                web.Timeout = completion.TimeoutMilliseconds;
                web.ReadWriteTimeout = completion.TimeoutMilliseconds;
                using (Stream stream = web.GetRequestStream()) stream.Write(data, 0, data.Length);
                using (var response = (HttpWebResponse)web.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) body = reader.ReadToEnd();
            }
            catch (WebException error)
            {
                if (error.Response != null)
                    using (var response = error.Response)
                    using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                        body = reader.ReadToEnd();
            }
            catch (IOException) { }
            catch (InvalidOperationException) { body = "{\"error\":\"context budget unavailable or exceeded\"}"; }
            lock (completedGate)
                completed.Enqueue(new PendingResult { Version = version, Request = request, Complete = complete, Body = body });
        }

        private static string Send(Uri endpoint, string json, int timeout)
        {
            var web = (HttpWebRequest)WebRequest.Create(endpoint);
            web.Method = json == null ? "GET" : "POST";
            web.Timeout = timeout; web.ReadWriteTimeout = timeout;
            if (json != null)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                web.ContentType = "application/json"; web.ContentLength = bytes.Length;
                using (Stream stream = web.GetRequestStream()) stream.Write(bytes, 0, bytes.Length);
            }
            using (var response = web.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }

        private void Update()
        {
            PendingResult result = null;
            lock (completedGate)
            {
                if (completed.Count > 0) result = completed.Dequeue();
            }
            if (result == null || !dispatch.TryFinish(result.Version)) return;
            if (result.Body != null && (Regex.IsMatch(result.Body, "\"truncated\"\\s*:\\s*true") ||
                (result.Body.IndexOf("\"error\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 result.Body.IndexOf("context", StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                DaggerfallUI.AddHUDText("Local model context unavailable or too small for NPC/world data. Check server context size.");
                result.Complete(ContextLimit);
                return;
            }
            if (result.Request == null) { string classification = ExtractContent(result.Body);
                result.Complete(string.IsNullOrWhiteSpace(classification) ? Unavailable : classification); return; }
            string answer = LocalDialogue.SelectAnswer(result.Request, ExtractContent(result.Body));
            if (result.Request.SocialOnly && answer == result.Request.VanillaAnswer)
            { result.Complete(Unavailable); return; }
            string cacheKey = LocalDialogue.CacheKey(result.Request);
            if (answer != result.Request.VanillaAnswer) AddCache(cacheKey, answer);
            result.Complete(answer);
        }

        private void AddCache(string key, string value)
        {
            if (cache.ContainsKey(key)) return;
            while (cacheOrder.Count >= 64) cache.Remove(cacheOrder.Dequeue());
            cache.Add(key, value);
            cacheOrder.Enqueue(key);
        }

        private static string ExtractContent(string json)
        { return DialogueCompletionText.Extract(json); }
    }
}
