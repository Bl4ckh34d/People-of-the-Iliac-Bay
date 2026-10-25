using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class ShadowConfigurationTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("shadow configuration is opt-in and requires exact model identity", OptIn));
            cases.Add(("shadow configuration accepts local endpoint and bounded timeout", LocalConnection));
            cases.Add(("shadow configuration rejects ambiguous or remote connection arguments", InvalidArguments));
            cases.Add(("routing configuration is explicit and accepts same bounded connection", RoutingOptIn));
            cases.Add(("routing and shadow modes cannot be enabled together", RoutingExclusion));
        }

        private static void OptIn()
        {
            LocalDecisionShadowConfiguration configuration;
            string diagnostic;
            Check.True(!LocalDecisionShadowConfiguration.TryParse(new string[0], out configuration, out diagnostic));
            Check.Equal(null, configuration);
            Check.Equal("disabled", diagnostic);
            Check.True(!LocalDecisionShadowConfiguration.TryParse(new[] { "-peopleoftheiliacbay-decision-shadow" }, out configuration, out diagnostic));
            Check.Equal("missing_model", diagnostic);
            Check.True(LocalDecisionShadowConfiguration.TryParse(new[] { "-peopleoftheiliacbay-decision-shadow", "-peopleoftheiliacbay-decision-model", "C:\\models\\Mini Q4.gguf" }, out configuration, out diagnostic));
            Check.Equal("C:\\models\\Mini Q4.gguf", configuration.ExpectedModelId);
            Check.Equal("http://127.0.0.1:8093/v1/systemone", configuration.Connection.Endpoint.AbsoluteUri);
            Check.Equal(5000, configuration.Connection.TimeoutMilliseconds);
        }

        private static void LocalConnection()
        {
            LocalDecisionShadowConfiguration configuration;
            string diagnostic;
            Check.True(LocalDecisionShadowConfiguration.TryParse(Arguments("http://localhost:8093/v1/systemone", "2400"), out configuration, out diagnostic));
            Check.Equal(2400, configuration.Connection.TimeoutMilliseconds);
            Check.Equal("", diagnostic);
        }

        private static void InvalidArguments()
        {
            foreach (string address in new[] { "https://127.0.0.1:8093/v1/systemone", "http://example.com/v1/systemone", "http://user:pass@localhost/v1/systemone", "http://127.0.0.1:8093/wrong", "http://127.0.0.1:8093/v1/systemone?x=1", "http://127.0.0.1:8093/v1/systemone#fragment" })
                Reject(Arguments(address, "5000"));
            foreach (string timeout in new[] { "0", "99", "10001", "1.5", "5000ms" })
                Reject(Arguments("http://127.0.0.1:8093/v1/systemone", timeout));
            Reject(new[] { "-peopleoftheiliacbay-decision-shadow", "-peopleoftheiliacbay-decision-model", "m", "-peopleoftheiliacbay-decision-model", "other" });
            Reject(new[] { "-peopleoftheiliacbay-decision-shadow", "-peopleoftheiliacbay-decision-model", "m", "-peopleoftheiliacbay-decision-endpoint" });
            Reject(new[] { "-peopleoftheiliacbay-decision-shadow", "-peopleoftheiliacbay-decision-shadow", "-peopleoftheiliacbay-decision-model", "m" });
            Reject(new[] { "-peopleoftheiliacbay-decision-shadow", "-peopleoftheiliacbay-decision-model", "model\nother" });
        }

        private static void RoutingOptIn()
        {
            LocalDecisionShadowConfiguration configuration;
            string diagnostic;
            Check.True(!LocalDecisionShadowConfiguration.TryParseRouting(new string[0], out configuration, out diagnostic));
            Check.Equal(null, configuration);
            Check.Equal("disabled", diagnostic);
            Check.True(!LocalDecisionShadowConfiguration.TryParseRouting(new[] { "-peopleoftheiliacbay-decision-routing" }, out configuration, out diagnostic));
            Check.Equal("missing_model", diagnostic);
            Check.True(LocalDecisionShadowConfiguration.TryParseRouting(RoutingArguments("http://localhost:8093/v1/systemone", "2400"), out configuration, out diagnostic));
            Check.Equal("C:\\models\\Mini Q4.gguf", configuration.ExpectedModelId);
            Check.Equal("http://localhost:8093/v1/systemone", configuration.Connection.Endpoint.AbsoluteUri);
            Check.Equal(2400, configuration.Connection.TimeoutMilliseconds);
            Check.Equal("", diagnostic);
        }

        private static void RoutingExclusion()
        {
            string[] shadowAndRouting = Arguments("http://127.0.0.1:8093/v1/systemone", "5000");
            var both = new string[shadowAndRouting.Length + 1];
            Array.Copy(shadowAndRouting, both, shadowAndRouting.Length);
            both[both.Length - 1] = "-peopleoftheiliacbay-decision-routing";
            Reject(both);

            string[] routingAndShadow = RoutingArguments("http://127.0.0.1:8093/v1/systemone", "5000");
            var reverse = new string[routingAndShadow.Length + 1];
            Array.Copy(routingAndShadow, reverse, routingAndShadow.Length);
            reverse[reverse.Length - 1] = "-peopleoftheiliacbay-decision-shadow";
            Reject(reverse);
        }

        private static string[] Arguments(string endpoint, string timeout) => new[] { "game.exe", "-peopleoftheiliacbay-decision-shadow", "-peopleoftheiliacbay-decision-model", "m", "-peopleoftheiliacbay-decision-endpoint", endpoint, "-peopleoftheiliacbay-decision-timeout", timeout };
        private static string[] RoutingArguments(string endpoint, string timeout) => new[] { "game.exe", "-peopleoftheiliacbay-decision-routing", "-peopleoftheiliacbay-decision-model", "C:\\models\\Mini Q4.gguf", "-peopleoftheiliacbay-decision-endpoint", endpoint, "-peopleoftheiliacbay-decision-timeout", timeout };
        private static void Reject(string[] args)
        {
            LocalDecisionShadowConfiguration configuration;
            string diagnostic;
            Check.True(!LocalDecisionShadowConfiguration.TryParse(args, out configuration, out diagnostic));
            Check.Equal(null, configuration);
            Check.True(diagnostic != "disabled" && diagnostic.Length > 0);
        }
    }
}
