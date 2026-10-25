using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Isolated no-coordinator regression for production TTS request retention.</summary>
    internal static class BreezeTtsTrialCapacityProbe
    {
        public const string RunFlag = "-peopleoftheiliacbay-breeze-tts-capacity-probe";
        private const int TrialLimit = 256;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable]
        private sealed class Config
        {
            public int schemaVersion = 1;
            public string bridgeRoot;
            public string serverEndpoint = string.Empty;
        }

        public static IEnumerator Run()
        {
            string sandbox = Path.Combine(Path.GetTempPath(), "PeopleOfTheIliacBay-TtsCapacity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            try
            {
                VerifyConfiguredBridgeDoesNotInheritTrialLimit(Path.Combine(sandbox, "production"));
                VerifyTrialBridgeRetainsItsLimit(Path.Combine(sandbox, "trial"));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_CAPACITY_PASS: production=257-closed-conversations; trial=256-limit; no-coordinator-or-audio");
            }
            finally
            {
                try { Directory.Delete(sandbox, true); }
                catch (Exception error) { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_BREEZE_TTS_CAPACITY_CLEANUP: " + error.GetType().Name); }
            }
            yield break;
        }

        private static void VerifyConfiguredBridgeDoesNotInheritTrialLimit(string sandbox)
        {
            BreezeTtsTrialBridge bridge = CreateConfiguredBridge(sandbox);
            try
            {
                for (int index = 0; index < TrialLimit + 1; index++)
                {
                    string token = "capacity-production-" + index.ToString("D3");
                    string requestId;
                    Require(bridge.NotifyFinalizedNpcText("capacity-npc", "Test NPC", "A finalized test line.",
                        token, "unknown", out requestId), "configured production bridge rejected request " + index);
                    Require(!string.IsNullOrEmpty(requestId), "configured production request ID missing at " + index);
                    bridge.EndConversation(token);
                }
                Require(!bridge.IsTrialMode, "configured bridge was marked trial mode");
                Require(bridge.PublishedRequestCount == TrialLimit + 1,
                    "configured request counter did not cross the trial threshold");
                VerifyCancelledRegistryCleanup(bridge);
            }
            finally { DestroyBridge(bridge); }
        }

        private static void VerifyTrialBridgeRetainsItsLimit(string sandbox)
        {
            GameObject owner = new GameObject("Breeze TTS isolated trial capacity probe");
            BreezeTtsTrialBridge bridge = null;
            try
            {
                string diagnostic;
                bridge = BreezeTtsTrialBridge.TryCreateFromCommandLine(owner,
                    new[] { BreezeTtsTrialBridge.RunFlag, Path.Combine(sandbox, "bridge") }, out diagnostic);
                Require(bridge != null, "trial bridge creation failed: " + diagnostic);
                for (int index = 0; index < TrialLimit; index++)
                {
                    string token = "capacity-trial-" + index.ToString("D3");
                    string requestId;
                    Require(bridge.NotifyFinalizedNpcText("capacity-npc", "Test NPC", "A finalized test line.",
                        token, "unknown", out requestId), "trial bridge rejected request before limit at " + index);
                    bridge.EndConversation(token);
                }
                string rejectedId;
                Require(!bridge.NotifyFinalizedNpcText("capacity-npc", "Test NPC", "A finalized test line.",
                    "capacity-trial-over-limit", "unknown", out rejectedId), "trial bridge accepted request 257");
                Require(rejectedId == null && bridge.PublishedRequestCount == TrialLimit,
                    "trial request count or rejected request ID was inconsistent");
            }
            finally
            {
                if (bridge != null) bridge.Shutdown();
                if (owner != null) UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static BreezeTtsTrialBridge CreateConfiguredBridge(string sandbox)
        {
            string configDirectory = Path.Combine(sandbox, "config");
            string bridgeRoot = Path.Combine(sandbox, "bridge");
            Directory.CreateDirectory(configDirectory);
            File.WriteAllText(Path.Combine(configDirectory, BreezeTtsTrialBridge.DefaultConfigFileName),
                JsonUtility.ToJson(new Config { bridgeRoot = bridgeRoot }));

            GameObject owner = new GameObject("Breeze TTS isolated production capacity probe");
            string diagnostic;
            BreezeTtsTrialBridge bridge = BreezeTtsTrialBridge.TryCreateConfigured(owner, new string[0],
                configDirectory, true, false, out diagnostic);
            if (bridge == null)
            {
                UnityEngine.Object.DestroyImmediate(owner);
                throw new InvalidOperationException("configured bridge creation failed: " + diagnostic);
            }
            return bridge;
        }

        private static void VerifyCancelledRegistryCleanup(BreezeTtsTrialBridge bridge)
        {
            const string token = "capacity-open-replay-token";
            string requestId;
            Require(bridge.NotifyFinalizedNpcText("capacity-npc", "Test NPC", "Keep this open transcript binding.",
                token, "unknown", out requestId), "open-replay cleanup fixture request failed");
            object entry = RequestRegistry(bridge)[requestId];
            Type entryType = entry.GetType();
            FieldInfo waveField = entryType.GetField("Wave");
            FieldInfo cancelledAtField = entryType.GetField("CancelledAtTicks");
            Require(waveField != null && cancelledAtField != null, "request retention fields missing");
            var retainedWave = new BreezeTtsTrialWave();
            typeof(BreezeTtsTrialWave).GetProperty("Samples").SetValue(retainedWave, new float[8192], null);
            waveField.SetValue(entry, retainedWave);
            bridge.SetFeatureOptions(false, false);
            bridge.SetFeatureOptions(true, false);
            cancelledAtField.SetValue(entry, Stopwatch.GetTimestamp() - Stopwatch.Frequency * 301L);
            Sweep(bridge);
            Require(waveField.GetValue(entry) == null, "cancelled request retained its decoded wave buffer");
            string boundPerson, boundToken, boundText;
            Require(bridge.TryReadRequestBinding(requestId, out boundPerson, out boundToken, out boundText),
                "sweep removed an open transcript replay binding");
            Require(bridge.GetRequestState(requestId) == BreezeTtsTrialBridge.RequestState.Cancelled,
                "sweep removed a cancelled request before conversation close");

            bridge.EndConversation(token);
            cancelledAtField.SetValue(entry, Stopwatch.GetTimestamp() - Stopwatch.Frequency * 301L);
            Sweep(bridge);
            Require(bridge.GetRequestState(requestId) == BreezeTtsTrialBridge.RequestState.Unknown,
                "expired closed conversation entry remained in the request registry");
            Require(!bridge.TryReadRequestBinding(requestId, out boundPerson, out boundToken, out boundText),
                "expired closed conversation still exposed a replay binding");
        }

        private static IDictionary RequestRegistry(BreezeTtsTrialBridge bridge)
        {
            FieldInfo field = typeof(BreezeTtsTrialBridge).GetField("requestsById", PrivateInstance);
            IDictionary registry = field == null ? null : field.GetValue(bridge) as IDictionary;
            Require(registry != null, "request registry unavailable to isolated fixture");
            return registry;
        }

        private static void Sweep(BreezeTtsTrialBridge bridge)
        {
            MethodInfo method = typeof(BreezeTtsTrialBridge).GetMethod("CleanupClosedArtifacts", PrivateInstance);
            Require(method != null, "closed-artifact sweep unavailable to isolated fixture");
            method.Invoke(bridge, null);
        }

        private static void DestroyBridge(BreezeTtsTrialBridge bridge)
        {
            if (bridge == null) return;
            bridge.Shutdown();
            UnityEngine.Object.DestroyImmediate(bridge.gameObject);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Breeze TTS capacity probe: " + message);
        }
    }
}
