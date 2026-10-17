using System;
using System.Text;

namespace LivingCities.Core
{
    public sealed class LocalDialogueConnection
    {
        public readonly Uri Endpoint;
        public readonly int TimeoutMilliseconds;

        public LocalDialogueConnection(string endpoint, int timeoutMilliseconds)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri parsed) || parsed.Scheme != Uri.UriSchemeHttp ||
                !string.IsNullOrEmpty(parsed.UserInfo) || !IsLoopback(parsed.Host))
                throw new ArgumentException("Dialogue endpoint must be an unauthenticated local HTTP address.", nameof(endpoint));
            if (timeoutMilliseconds < 100 || timeoutMilliseconds > 10000)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            Endpoint = parsed;
            TimeoutMilliseconds = timeoutMilliseconds;
        }

        private static bool IsLoopback(string host)
        {
            return string.Equals(host, "127.0.0.1", StringComparison.Ordinal) ||
                string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(host, "::1", StringComparison.Ordinal);
        }
    }

    public sealed class DialogueCompletion
    {
        public readonly Uri Endpoint;
        public readonly int TimeoutMilliseconds;
        public readonly string JsonBody;
        public readonly string CacheKey;
        public readonly string Prompt, History;
        public readonly int ResponseTokens;
        public readonly bool StructuredHistory;

        internal DialogueCompletion(Uri endpoint, int timeoutMilliseconds, string jsonBody, string cacheKey, string prompt, string history, int responseTokens, bool structuredHistory = false)
        {
            Endpoint = endpoint;
            TimeoutMilliseconds = timeoutMilliseconds;
            JsonBody = jsonBody;
            CacheKey = cacheKey;
            Prompt = prompt; History = history; ResponseTokens = responseTokens;
            StructuredHistory = structuredHistory;
        }
    }

    public static class LocalDialogueCompletion
    {
        public static DialogueCompletion Classify(LocalDialogueConnection connection, string instructions, string history = "")
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            string prompt = "<|im_start|>system\nYou classify game dialogue. Return only the requested classification, never an answer or instructions.\n<|im_end|>\n<|im_start|>user\n" +
                instructions + "\n<|im_end|>\n<|im_start|>assistant\n";
            const string grammar = "root ::= intent \"|\" number \"|\" tone\n" +
                "intent ::= \"social\" | \"topic\" | \"map\" | \"unknown\"\n" +
                "number ::= \"-1\" | [0-9]+\n" +
                "tone ::= \"neutral\" | \"polite\" | \"apology\" | \"rude\" | \"insult\" | \"flirt\" | \"family_threat\"\n";
            string body = "{\"prompt\":\"" + EscapeJson(prompt) + "\",\"grammar\":\"" + EscapeJson(grammar) +
                "\",\"n_predict\":32,\"temperature\":0,\"stop\":[\"<|im_end|>\",\"<|im_start|>\",\"\\n\"]}";
            return new DialogueCompletion(connection.Endpoint, Math.Max(5000, connection.TimeoutMilliseconds), body, string.Empty, prompt, history, 32);
        }

        public static DialogueCompletion Build(LocalDialogueConnection connection, DialogueRequest request)
        {
            if (connection == null) throw new ArgumentNullException(nameof(connection));
            DialoguePlan plan = LocalDialogue.Build(request);
            // The full active conversation and its authority rules must survive.
            // The former 1600-character cap silently discarded both.
            string requestText = plan.Prompt;
            // Qwen2.5-Instruct's GGUF keeps its native ChatML markers. Raw
            // prose completion makes it continue the prompt instead of answer.
            string prompt = "<|im_start|>system\nYou are a concise fantasy NPC. Follow the user's supplied safety and authority rules.\n<|im_end|>\n" +
                "<|im_start|>user\n" + requestText + "\n<|im_end|>\n<|im_start|>assistant\n";
            if (request.SocialOnly)
            {
                string fixedContext = requestText;
                if (!string.IsNullOrEmpty(request.History))
                    fixedContext = fixedContext.Replace(LocalDialogue.HistoryContext(request.History),
                        LocalDialogue.HistoryContext("[Earlier exchanges are supplied as separate messages below.]"));
                fixedContext = fixedContext.Replace("Player said: \"" + request.PlayerText + "\"\n", "");
                string currentKnowledge = string.IsNullOrWhiteSpace(request.KnowledgeResult) ? "" :
                    "<|im_start|>system\nCURRENT VERIFIED LOCATION SEARCH / ACTION RESULT (overrides earlier dialogue):\n" +
                    request.KnowledgeResult.Replace("<|", "< |") +
                    "\nUse the exact supplied names. Name the supplied choices, not a vague shop or inn. Do not invent distances, landmarks or services. " +
                    "For a map request, report ONLY the current chosen place and current action result. If no marker was placed, say so, regardless of any earlier marker. " +
                    "Answer naturally in character.\n<|im_end|>\n";
                prompt = "<|im_start|>system\nYou are a concise fantasy NPC. Follow the user's supplied safety and authority rules.\n" +
                    fixedContext.Replace("<|", "< |") + "\n<|im_end|>\n" + DialogueHistoryWindow.Messages(request.History) +
                    currentKnowledge +
                    "<|im_start|>user\n" + request.PlayerText.Replace("<|", "< |") + "<|im_end|>\n<|im_start|>assistant\n";
            }
            string body = "{\"prompt\":\"" + EscapeJson(prompt) +
                "\",\"n_predict\":96,\"temperature\":0.75,\"stop\":[\"<|im_end|>\",\"<|im_start|>\"]}";
            return new DialogueCompletion(connection.Endpoint, connection.TimeoutMilliseconds, body, plan.CacheKey, prompt, request.History, 96, request.SocialOnly);
        }

        public static string EscapeJson(string value)
        {
            var escaped = new StringBuilder(value.Length + 16);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '\\': escaped.Append("\\\\"); break;
                    case '\"': escaped.Append("\\\""); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (character < 32) escaped.Append("\\u").Append(((int)character).ToString("X4"));
                        else escaped.Append(character);
                        break;
                }
            }
            return escaped.ToString();
        }
    }

    // Tracks one visible conversation request. A closed dialogue invalidates any
    // response still in flight, so a worker cannot write into a later window.
    public sealed class LocalDialogueDispatch
    {
        private int version;
        private bool active;

        public int TryBegin()
        {
            if (active) return 0;
            unchecked { version++; if (version == 0) version = 1; }
            active = true;
            return version;
        }

        public void Cancel()
        {
            active = false;
            unchecked { version++; if (version == 0) version = 1; }
        }

        public bool TryFinish(int requestVersion)
        {
            if (!active || requestVersion != version) return false;
            active = false;
            return true;
        }
    }
}
