using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Opt-in native regression for terminal receipt delivery during voice cancellation.
    /// Seeds bridge-owned state by reflection; never submits speech or generates audio.
    /// </summary>
    internal static class BreezeTtsMixerReceiptProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-breeze-tts-mixer-receipt-probe";
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string RequestId = "mixer-receipt-probe-request";
        private const string ConversationToken = "mixer-receipt-probe-conversation";
        private const string PersonId = "mixer-receipt-probe-person";
        private const string ExactText = "Controlled cancellation receipt probe.";

        private sealed class Observation
        {
            public int Starts;
            public int Terminals;
            public BreezeTtsTrialBridge.MixerPlaybackStartReceipt Start;
            public BreezeTtsTrialBridge.PrePlaybackTerminalReceipt Terminal;
        }

        internal static IEnumerator Run(string character, string saveName)
        {
            if (character != "PeopleOfTheIliacBayTest-9a5f197b" || saveName != "People - Gothway Garden")
                throw new InvalidOperationException("Mixer receipt probe requires the exact owned character and save.");

            RunScenario("counted-before-update", true, false, true);
            RunScenario("zero-output", false, false, true);
            RunScenario("onset-published", true, true, false);
            Debug.Log("BREEZE_TTS_MIXER_RECEIPT_PROBE_PASS: cancellation event delivery only; no audio or speaker output tested.");
            yield return null;
        }

        private static void RunScenario(string scenario, bool nonZeroSamples, bool publishOnset, bool expectTerminal)
        {
            string root = Path.Combine(Path.GetTempPath(), "PeopleOfTheIliacBay-MixerReceipt-" + Guid.NewGuid().ToString("N"));
            string requestDirectory = Path.Combine(root, "requests");
            string responseDirectory = Path.Combine(root, "responses");
            string measurementDirectory = Path.Combine(root, "measurements");
            Directory.CreateDirectory(requestDirectory);
            Directory.CreateDirectory(responseDirectory);
            Directory.CreateDirectory(measurementDirectory);

            var owner = new GameObject("Disposable Breeze mixer receipt probe");
            owner.hideFlags = HideFlags.HideAndDontSave;
            BreezeTtsTrialBridge bridge = owner.AddComponent<BreezeTtsTrialBridge>();
            Observation observation = new Observation();
            bridge.MixerPlaybackStarted += receipt => { observation.Starts++; observation.Start = receipt; };
            bridge.PrePlaybackTerminated += receipt => { observation.Terminals++; observation.Terminal = receipt; };

            try
            {
                object seededEntry = SeedBridge(bridge, requestDirectory, responseDirectory, measurementDirectory, nonZeroSamples);
                if (publishOnset)
                {
                    Method(bridge, "PublishMixerPlaybackStarted").Invoke(bridge,
                        new[] { Field(bridge, "activeEntry").GetValue(bridge) });
                    Require(observation.Starts == 1 && observation.Start != null && observation.Start.RequestId == RequestId &&
                        observation.Start.ConversationToken == ConversationToken && observation.Start.PersonId == PersonId &&
                        observation.Start.ExactText == ExactText,
                        scenario + " did not publish bound mixer onset receipt.");
                }
                else
                    Require(observation.Starts == 0, scenario + " unexpectedly published onset receipt.");

                bridge.SetFeatureOptions(false, false);
                bridge.SetFeatureOptions(false, false);

                if (expectTerminal)
                {
                    Require(observation.Terminals == 1, scenario + " did not deliver exactly one terminal receipt.");
                    Require(observation.Terminal != null && observation.Terminal.RequestId == RequestId &&
                        observation.Terminal.ConversationToken == ConversationToken &&
                        observation.Terminal.PersonId == PersonId && observation.Terminal.ExactText == ExactText &&
                        observation.Terminal.State == BreezeTtsTrialBridge.RequestState.Cancelled &&
                        observation.Terminal.Reason == "tts-disabled",
                        scenario + " delivered terminal receipt with wrong request binding or outcome.");
                    Method(bridge, "PublishPrePlaybackTerminal").Invoke(bridge, new[] { seededEntry, "duplicate-probe" });
                    Require(observation.Terminals == 1, scenario + " delivered duplicate terminal receipt.");
                }
                else
                {
                    Method(bridge, "PublishPrePlaybackTerminal").Invoke(bridge, new[] { seededEntry, "duplicate-probe" });
                    Require(observation.Terminals == 0 && observation.Starts == 1,
                        scenario + " did not preserve onset as sole published outcome.");
                }

                Debug.Log("BREEZE_TTS_MIXER_RECEIPT_PROBE_CASE_PASS: case=" + scenario +
                    "; starts=" + observation.Starts + "; terminals=" + observation.Terminals +
                    "; outcome=" + (expectTerminal ? "pre-playback-terminal" : "onset-receipt"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                try { Directory.Delete(root, true); }
                catch (Exception error) { Debug.LogWarning("BREEZE_TTS_MIXER_RECEIPT_PROBE_CLEANUP: " + error.GetType().Name); }
            }
        }

        private static object SeedBridge(BreezeTtsTrialBridge bridge, string requests, string responses,
            string measurements, bool nonZeroSamples)
        {
            Type entryType = typeof(BreezeTtsTrialBridge).GetNestedType("RequestEntry", BindingFlags.NonPublic);
            Type countersType = typeof(BreezeTtsTrialBridge).GetNestedType("PlaybackCounters", BindingFlags.NonPublic);
            if (entryType == null || countersType == null)
                throw new InvalidOperationException("Bridge request or playback counter type missing.");
            object entry = Activator.CreateInstance(entryType, true);
            object counters = Activator.CreateInstance(countersType, true);
            Set(entry, "RequestId", RequestId);
            Set(entry, "ConversationToken", ConversationToken);
            Set(entry, "PersonId", PersonId);
            Set(entry, "Text", ExactText);
            Set(entry, "State", BreezeTtsTrialBridge.RequestState.Playing);
            Set(entry, "Counters", counters);
            Set(entry, "MixerPlaybackStartPublished", false);
            Set(entry, "PrePlaybackTerminalPublished", false);
            Set(counters, "NonZeroSamples", nonZeroSamples ? 12L : 0L);
            Set(counters, "FirstNonZeroMixerTicks", nonZeroSamples ? 123L : 0L);
            Set(counters, "FirstNonZeroMixerFrame", nonZeroSamples ? 7L : -1L);

            Field(bridge, "voiceEnabled").SetValue(bridge, true);
            Field(bridge, "activeConversationToken").SetValue(bridge, ConversationToken);
            Field(bridge, "activePersonId").SetValue(bridge, PersonId);
            Field(bridge, "activeEntry").SetValue(bridge, entry);
            Field(bridge, "mixerCounters").SetValue(bridge, counters);
            Field(bridge, "requestDirectory").SetValue(bridge, requests);
            Field(bridge, "responseDirectory").SetValue(bridge, responses);
            Field(bridge, "measurementDirectory").SetValue(bridge, measurements);
            IDictionary requestsById = (IDictionary)Field(bridge, "requestsById").GetValue(bridge);
            if (requestsById == null) throw new InvalidOperationException("Bridge request map is not initialized.");
            requestsById.Add(RequestId, entry);
            return entry;
        }

        private static FieldInfo Field(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, InstanceFields);
            if (field == null) throw new InvalidOperationException("Bridge field missing: " + name);
            return field;
        }

        private static void Set(object target, string name, object value) { Field(target, name).SetValue(target, value); }

        private static MethodInfo Method(object target, string name)
        {
            MethodInfo method = target.GetType().GetMethod(name, InstanceMethods);
            if (method == null) throw new InvalidOperationException("Bridge method missing: " + name);
            return method;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
