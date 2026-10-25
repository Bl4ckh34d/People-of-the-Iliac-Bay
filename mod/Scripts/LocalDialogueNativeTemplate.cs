using System;
using System.Collections.Generic;
using FullSerializer;

namespace PeopleOfTheIliacBay.DFU
{
    // Translate the existing internal message framing, never interpret dialogue semantics.
    // The serving GGUF owns the actual template and special generation tokens.
    internal static class LocalDialogueNativeTemplate
    {
        private const string Start = "<|im_start|>";
        private const string End = "<|im_end|>";
        private const string Assistant = "<|im_start|>assistant\n";

        internal static string Request(string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) throw new InvalidOperationException("Missing dialogue prompt.");
            var messages = new List<fsData>();
            if (!prompt.StartsWith(Start, StringComparison.Ordinal))
            {
                if (prompt.Contains(Start) || prompt.Contains(End))
                    throw new InvalidOperationException("Ambiguous dialogue framing.");
                messages.Add(Message("user", prompt));
            }
            else
            {
                int offset = 0;
                while (true)
                {
                    if (prompt.Substring(offset) == Assistant)
                    {
                        if (messages.Count == 0) throw new InvalidOperationException("Missing dialogue messages.");
                        break;
                    }
                    if (prompt.IndexOf(Start, offset, StringComparison.Ordinal) != offset)
                        throw new InvalidOperationException("Malformed dialogue boundary.");
                    int roleStart = offset + Start.Length;
                    int contentStart = prompt.IndexOf('\n', roleStart);
                    if (contentStart < 0) throw new InvalidOperationException("Missing dialogue role.");
                    string role = prompt.Substring(roleStart, contentStart - roleStart);
                    if (role != "system" && role != "user" && role != "assistant")
                        throw new InvalidOperationException("Unsupported dialogue role.");
                    contentStart++;
                    int end = prompt.IndexOf(End, contentStart, StringComparison.Ordinal);
                    if (end < 0) throw new InvalidOperationException("Unterminated dialogue message.");
                    string content = prompt.Substring(contentStart, end - contentStart);
                    if (content.Contains(Start)) throw new InvalidOperationException("Ambiguous dialogue message.");
                    messages.Add(Message(role, content));
                    offset = end + End.Length;
                    if (offset < prompt.Length && prompt[offset] == '\n') offset++;
                    if (offset >= prompt.Length) throw new InvalidOperationException("Missing assistant generation prefix.");
                }
            }
            return fsJsonPrinter.CompressedJson(new fsData(new Dictionary<string, fsData> {
                { "messages", new fsData(messages) },
                { "add_generation_prompt", new fsData(true) },
                { "chat_template_kwargs", new fsData(new Dictionary<string, fsData> { { "enable_thinking", new fsData(false) } }) }
            }));
        }

        internal static string ReadPrompt(string response)
        {
            var root = Object(response);
            fsData prompt;
            if (!root.TryGetValue("prompt", out prompt) || !prompt.IsString || string.IsNullOrWhiteSpace(prompt.AsString))
                throw new InvalidOperationException("Missing server-native prompt.");
            return prompt.AsString;
        }

        internal static string Payload(string original, string renderedPrompt)
        {
            var root = Object(original);
            if (!root.ContainsKey("prompt") || !root["prompt"].IsString || string.IsNullOrEmpty(renderedPrompt))
                throw new InvalidOperationException("Missing completion prompt.");
            root["prompt"] = new fsData(renderedPrompt);
            fsData stops;
            if (root.TryGetValue("stop", out stops))
            {
                if (!stops.IsList) throw new InvalidOperationException("Invalid completion stops.");
                var retained = new List<fsData>();
                foreach (fsData stop in stops.AsList)
                {
                    if (!stop.IsString) throw new InvalidOperationException("Invalid completion stop.");
                    if (stop.AsString != Start && stop.AsString != End) retained.Add(stop);
                }
                root["stop"] = new fsData(retained);
            }
            return fsJsonPrinter.CompressedJson(new fsData(root));
        }

        internal static int ContextSize(string json)
        {
            fsData value;
            var properties=Object(json);
            fsData settings;
            if(properties.TryGetValue("default_generation_settings",out settings))
            {
                if(!settings.IsDictionary)throw new InvalidOperationException("Invalid server generation settings.");
                properties=settings.AsDictionary;
            }
            if(!properties.TryGetValue("n_ctx",out value) || !value.IsInt64 ||
                value.AsInt64<1 || value.AsInt64>int.MaxValue)
                throw new InvalidOperationException("Missing or invalid server context size.");
            return (int)value.AsInt64;
        }

        internal static int TokenCount(string json)
        {
            fsData value;
            if(!Object(json).TryGetValue("tokens",out value) || !value.IsList)
                throw new InvalidOperationException("Missing token list.");
            foreach(var token in value.AsList)
                if(!token.IsInt64 || token.AsInt64<0 || token.AsInt64>int.MaxValue)
                    throw new InvalidOperationException("Invalid token ID.");
            return value.AsList.Count;
        }

        internal static bool IsTruncated(string json)
        {
            fsData root,value;
            return !string.IsNullOrEmpty(json) && !fsJsonParser.Parse(json,out root).Failed &&
                root.IsDictionary && root.AsDictionary.TryGetValue("truncated",out value) && value.IsBool && value.AsBool;
        }

        private static fsData Message(string role, string content)
        {
            return new fsData(new Dictionary<string, fsData> {
                { "role", new fsData(role) }, { "content", new fsData(content) }
            });
        }

        private static Dictionary<string, fsData> Object(string json)
        {
            fsData root;
            if (string.IsNullOrEmpty(json) || fsJsonParser.Parse(json, out root).Failed || !root.IsDictionary)
                throw new InvalidOperationException("Invalid native template JSON object.");
            return root.AsDictionary;
        }
    }
}
