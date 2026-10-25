using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit local test opt-in. Parsing never probes endpoints or starts services.
    internal sealed class LocalDecisionShadowConfiguration
    {
        public string ExpectedModelId { get; private set; }
        public LocalDialogueConnection Connection { get; private set; }

        public static bool TryParse(string[] args, out LocalDecisionShadowConfiguration configuration,
            out string diagnostic)
        {
            return TryParseMode(args, "-peopleoftheiliacbay-decision-shadow",
                "-peopleoftheiliacbay-decision-routing", out configuration, out diagnostic);
        }

        public static bool TryParseRouting(string[] args, out LocalDecisionShadowConfiguration configuration,
            out string diagnostic)
        {
            return TryParseMode(args, "-peopleoftheiliacbay-decision-routing",
                "-peopleoftheiliacbay-decision-shadow", out configuration, out diagnostic);
        }

        private static bool TryParseMode(string[] args, string modeFlag, string opposingModeFlag,
            out LocalDecisionShadowConfiguration configuration, out string diagnostic)
        {
            configuration = null;
            diagnostic = "disabled";
            if (args == null || !args.Contains(modeFlag)) return false;
            if (args.Contains(opposingModeFlag))
            { diagnostic = "conflicting_modes"; return false; }
            if (args.Count(value => value == modeFlag) != 1)
            { diagnostic = "duplicate_flag"; return false; }
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in new[] { "-peopleoftheiliacbay-decision-model", "-peopleoftheiliacbay-decision-endpoint", "-peopleoftheiliacbay-decision-timeout" })
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] != key) continue;
                    if (values.ContainsKey(key) || i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("-peopleoftheiliacbay-", StringComparison.Ordinal))
                    { diagnostic = "invalid_arguments"; return false; }
                    values.Add(key, args[i + 1]);
                }
            }
            string model;
            if (!values.TryGetValue("-peopleoftheiliacbay-decision-model", out model))
            { diagnostic = "missing_model"; return false; }
            if (model.Length > 256 || model.Any(char.IsControl))
            { diagnostic = "invalid_model"; return false; }
            string address;
            if (!values.TryGetValue("-peopleoftheiliacbay-decision-endpoint", out address))
                address = "http://127.0.0.1:8093/v1/systemone";
            int timeout = 5000;
            string timeoutText;
            if (values.TryGetValue("-peopleoftheiliacbay-decision-timeout", out timeoutText) &&
                (!int.TryParse(timeoutText, NumberStyles.None, CultureInfo.InvariantCulture, out timeout) || timeout < 100 || timeout > 10000))
            { diagnostic = "invalid_timeout"; return false; }
            LocalDialogueConnection connection;
            try { connection = new LocalDialogueConnection(address, timeout); }
            catch (ArgumentException) { diagnostic = "invalid_endpoint"; return false; }
            if (connection.Endpoint.AbsolutePath != "/v1/systemone" || connection.Endpoint.Query.Length != 0 || connection.Endpoint.Fragment.Length != 0)
            { diagnostic = "invalid_endpoint"; return false; }
            configuration = new LocalDecisionShadowConfiguration { ExpectedModelId = model, Connection = connection };
            diagnostic = "";
            return true;
        }
    }
}
