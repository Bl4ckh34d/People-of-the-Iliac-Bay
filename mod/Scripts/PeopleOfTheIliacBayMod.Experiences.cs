using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private const int ExperienceObserverScanLimit = 32;
        private const int ExperienceRetryLimit = 5;
        private const int ExperienceRetryStateLimit = 128;
        private sealed class ExperienceRetryState
        {
            public int Attempts;
            public float RetryAt;
        }

        private readonly Dictionary<string, ExperienceRetryState> experienceRetries =
            new Dictionary<string, ExperienceRetryState>(StringComparer.Ordinal);
        private WorldStateStore experienceConsumerStore;
        private long experienceConsumerSaveEpoch = -1;
        private string experienceObserverCursor;
        private NpcExperienceAppraisalTicket activeExperienceTicket;
        private int activeExperienceBackgroundTicket;
        private float nextExperienceScanAt;
        private bool experienceAppraisalsSuspended;

        // Optional diagnostics use the existing transport trace without changing the request or result.
        // Normal gameplay leaves this unset. A failing observer must never block the appraisal callback.
        internal Action<string, string, string> ExperienceAppraisalTrace { get; set; }

        // Parent calls this from Update. It never owns dialogue state or produces visible text.
        private void TickNpcExperienceAppraisals()
        {
            if (experienceAppraisalsSuspended) return;

            if (!ReferenceEquals(experienceConsumerStore, store) || experienceConsumerSaveEpoch != conversationSaveEpoch)
            {
                CancelNpcExperienceAppraisal();
                experienceRetries.Clear();
                experienceObserverCursor = null;
                experienceConsumerStore = store;
                experienceConsumerSaveEpoch = conversationSaveEpoch;
            }

            if (activeExperienceBackgroundTicket != 0)
            {
                if (!ExperienceAppraisalRuntimeReady(experienceConsumerStore))
                {
                    CancelActiveExperienceAppraisal();
                    return;
                }
                if (localDialogue != null && localDialogue.IsBackgroundCurrent(activeExperienceBackgroundTicket))
                    return;

                // A foreground request preempted this exact background ticket.
                // Do not cancel the foreground request or treat preemption as a model failure.
                nextExperienceScanAt = Time.unscaledTime + 1f;
                ClearActiveExperienceAppraisal();
                return;
            }

            if (!ExperienceAppraisalRuntimeReady(experienceConsumerStore) || localDialogue.IsBusy ||
                Time.unscaledTime < nextExperienceScanAt)
                return;

            nextExperienceScanAt = Time.unscaledTime + 0.5f;
            // Completed native item transfers remain durable on their action record;
            // this bounded projection is optional and runs only when appraisal is ready.
            try { experienceConsumerStore.DrainNpcItemTransferReceipts(32); }
            catch (ArgumentException) { return; }
            catch (InvalidOperationException) { return; }
            List<string> observers;
            try { observers = experienceConsumerStore.ReadPendingNpcExperienceObservers(ExperienceObserverScanLimit, experienceObserverCursor); }
            catch (ArgumentException) { return; }
            catch (InvalidOperationException) { return; }
            if (observers == null) return;
            if (observers.Count == 0)
            {
                experienceObserverCursor = null;
                return;
            }

            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach (string observer in observers)
            {
                if (string.IsNullOrWhiteSpace(observer)) continue;
                experienceObserverCursor = observer;
                List<NpcExperienceEvent> pending;
                try { pending = experienceConsumerStore.ReadPendingNpcExperiences(observer, 1); }
                catch (ArgumentException) { continue; }
                catch (InvalidOperationException) { continue; }
                if (pending == null || pending.Count == 0 || pending[0] == null) continue;

                string key = ExperienceAppraisalKey(observer, pending[0].DeduplicationKey);
                ExperienceRetryState retry;
                bool hasRetry = experienceRetries.TryGetValue(key, out retry);
                if (hasRetry && Time.unscaledTime < retry.RetryAt) continue;
                if (!hasRetry && experienceRetries.Count >= ExperienceRetryStateLimit) continue;

                NpcExperienceAppraisalTicket captured;
                NpcSocialBackground background;
                if (!RefreshSavedRandomPersonalityEligibility(experienceConsumerStore, observer, out background))
                    continue;
                NpcPersonalityProfile savedProfile = experienceConsumerStore.ReadNpcPersonality(observer);
                bool captureSucceeded;
                if (savedProfile == null)
                {
                    uint seed;
                    int factionId;
                    bool questProtected;
                    // Profile creation is limited to saved residents whose stable seed and native faction
                    // are known. An unresolved native faction leaves the event pending for a later scan.
                    if (!experienceConsumerStore.TryReadNpcPersonalityIdentity(observer, out seed, out factionId,
                            out questProtected))
                        continue;
                    captureSucceeded = experienceConsumerStore.TryCaptureNpcExperienceAppraisal(observer, now,
                        background, out captured);
                }
                else
                {
                    captureSucceeded = experienceConsumerStore.TryCaptureNpcExperienceAppraisal(observer, now,
                        out captured);
                }
                if (!captureSucceeded || captured == null)
                    continue;

                // Capture always chooses the oldest native event. Guard against a queue change between the
                // detached read and capture before submitting any model work.
                string capturedKey = ExperienceAppraisalKey(observer, captured.Request.EvidenceDeduplicationKey);
                if (!string.Equals(key, capturedKey, StringComparison.Ordinal)) continue;
                StartExperienceAppraisal(experienceConsumerStore, captured, key);
                return;
            }
        }

        // TownSmokeHarness invokes this only after loading its exact disposable
        // character and save. Suspend normal background appraisals during probe.
        internal IEnumerator RunNpcExperienceAppraisalTransportProbe(string character, string fixtureSave)
        {
            bool priorSuspension = experienceAppraisalsSuspended;
            experienceAppraisalsSuspended = true;
            CancelNpcExperienceAppraisal();
            try
            {
                yield return NpcExperienceCommitProbe.RunTransportProbe(character, fixtureSave,
                    localDialogue, NewDialogueConnection());
            }
            finally
            {
                experienceAppraisalsSuspended = priorSuspension;
            }
        }

        private bool ExperienceAppraisalRuntimeReady(WorldStateStore owner)
        {
            var game = DaggerfallUnity.Instance;
            var gameManager = GameManager.Instance;
            // Native events use the normal LLM appraisal. Observed speech first requires a bound classification.
            if (owner == null || !ReferenceEquals(owner, store) || !isActiveAndEnabled || !options.Enabled ||
                saveFault || !localDialogueEnabled || !localDialogue || game == null || !game.IsReady || gameManager == null ||
                !gameManager.IsReady || !gameManager.IsPlayingGame() || gameManager.PlayerEntity == null ||
                game.WorldTime == null || GameManager.IsGamePaused || conversationBusy || dialogueWasOpen)
                return false;

            // DFU's hostile-enemy alert is only a conservative workload deferral signal,
            // not proof that combat has started or ended. Never appraise during that alert.
            if (gameManager.PlayerEntity.EnemyAlertActive || gameManager.PlayerEntity.CurrentHealth <= 0)
                return false;
            // The opt-in voice trial owns the GPU switch until its queued speech finishes.
            if (breezeTtsTrial && breezeTtsTrial.HasPendingSpeech)
                return false;

            // An open native talk window is foreground work, even before its first setup/update.
            return !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow);
        }

        private void StartExperienceAppraisal(WorldStateStore owner, NpcExperienceAppraisalTicket captured, string key)
        {
            if (captured.Request.RequiresSpeechAppraisal && captured.Request.SpeechClassification == null)
            {
                StartExperienceSpeechClassification(owner, captured, key);
                return;
            }
            int backgroundTicket = 0;
            try
            {
                backgroundTicket = localDialogue.TryAppraiseNpcExperienceBackground(NewDialogueConnection(), captured.Request,
                    response => CompleteExperienceAppraisal(owner, captured, key, backgroundTicket, response),
                    TraceExperienceAppraisal);
            }
            catch (ArgumentException)
            {
                DeferExperienceAppraisalScan("invalid_request");
                return;
            }
            catch (InvalidOperationException)
            {
                DeferExperienceAppraisalScan("request_unavailable");
                return;
            }

            if (backgroundTicket == 0)
            {
                DeferExperienceAppraisalScan("client_busy");
                return;
            }
            activeExperienceTicket = captured;
            activeExperienceBackgroundTicket = backgroundTicket;
        }

        private void StartExperienceSpeechClassification(WorldStateStore owner, NpcExperienceAppraisalTicket captured, string key)
        {
            int backgroundTicket = 0;
            var speech = NpcSpeechActRequest.ForAppraisal(captured.Request);
            backgroundTicket = localDialogue.TryClassifySpeechBackground(NewDialogueConnection(), speech, classification =>
                {
                    if (backgroundTicket == 0 || activeExperienceBackgroundTicket != backgroundTicket ||
                        !ReferenceEquals(activeExperienceTicket, captured) || !ReferenceEquals(owner, experienceConsumerStore)) return;
                    ClearActiveExperienceAppraisal();
                    if (!ExperienceAppraisalRuntimeReady(owner)) return;
                    if (classification == null || !owner.TryBindNpcExperienceSpeechClassification(captured, classification))
                    {
                        RegisterExperienceAppraisalFailure(owner, captured, key, "speech_classification_rejected");
                        return;
                    }
                    StartExperienceAppraisal(owner, captured, key);
                }, TraceExperienceAppraisal);
            if (backgroundTicket == 0) { DeferExperienceAppraisalScan("speech_classifier_busy"); return; }
            activeExperienceTicket = captured;
            activeExperienceBackgroundTicket = backgroundTicket;
        }

        private void TraceExperienceAppraisal(string requestJson, string rawResponse, string parsedResponse)
        {
            var observer = ExperienceAppraisalTrace;
            if (observer == null) return;
            try { observer(requestJson, rawResponse, parsedResponse); }
            catch (Exception error)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_EXPERIENCE_APPRAISAL_TRACE_FAILED: " + error.GetType().Name);
            }
        }

        private void CompleteExperienceAppraisal(WorldStateStore owner, NpcExperienceAppraisalTicket captured,
            string key, int backgroundTicket, string response)
        {
            if (backgroundTicket == 0 || activeExperienceBackgroundTicket != backgroundTicket ||
                !ReferenceEquals(activeExperienceTicket, captured) || !ReferenceEquals(owner, experienceConsumerStore))
                return;

            ClearActiveExperienceAppraisal();
            if (!ExperienceAppraisalRuntimeReady(owner))
            {
                nextExperienceScanAt = Time.unscaledTime + 1f;
                return;
            }
            if (string.IsNullOrWhiteSpace(response) || response == LocalDialogueClient.Unavailable ||
                response == LocalDialogueClient.ContextLimit)
            {
                RegisterExperienceAppraisalFailure(owner, captured, key, "model_unavailable");
                return;
            }

            NpcExperienceAppraisal appraisal;
            if (!NpcExperienceAppraisalContract.TryParseResponse(captured.Request, response, out appraisal) ||
                appraisal == null || appraisal.ConversationOption != NpcExperienceConversationOption.NoAction)
            {
                RegisterExperienceAppraisalFailure(owner, captured, key, "invalid_or_unsupported_appraisal");
                return;
            }

            if (!owner.TryCommitNpcExperienceAppraisal(captured, response))
            {
                RegisterExperienceAppraisalFailure(owner, captured, key, "stale_or_rejected_appraisal");
                return;
            }

            experienceRetries.Remove(key);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_EXPERIENCE_APPRAISAL: status=committed; moodDelta=" +
                appraisal.MoodDelta + "; affinityDelta=" + appraisal.AffinityDelta + "; relationshipDelta=" +
                appraisal.TrustDelta + "/" + appraisal.ResentmentDelta + "/" + appraisal.FearDelta + ".");
        }

        private void RegisterExperienceAppraisalFailure(WorldStateStore owner,
            NpcExperienceAppraisalTicket captured, string key, string reason)
        {
            ExperienceRetryState retry;
            if (!experienceRetries.TryGetValue(key, out retry))
            {
                retry = new ExperienceRetryState();
                experienceRetries.Add(key, retry);
            }
            retry.Attempts++;
            if (retry.Attempts >= ExperienceRetryLimit)
            {
                double deferredAt = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                if (owner != null && captured != null && owner.TryDeferNpcExperienceAppraisal(
                    captured.Request.ObserverId, captured, deferredAt))
                {
                    experienceRetries.Remove(key);
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_EXPERIENCE_APPRAISAL: status=deferred; reason=" + reason +
                        "; completedAttempts=" + retry.Attempts + "; appraisalApplied=false; laterPendingEventsRemainEligible=true.");
                    return;
                }

                // A failed CAS must not mark the current head exhausted in memory. A fresh scan may
                // capture a new ticket for the same still-pending event or discover that it changed.
                experienceRetries.Remove(key);
                nextExperienceScanAt = Time.unscaledTime + 1f;
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_EXPERIENCE_APPRAISAL: status=defer_cas_rejected; reason=" +
                    reason + "; completedAttempts=" + retry.Attempts + "; permanentInMemoryExhaustion=false.");
                return;
            }

            float[] delays = { 3f, 6f, 12f, 24f };
            retry.RetryAt = Time.unscaledTime + delays[Mathf.Clamp(retry.Attempts - 1, 0, delays.Length - 1)];
            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_EXPERIENCE_APPRAISAL: status=retry; reason=" + reason +
                "; attempt=" + retry.Attempts + "; nativeEventRemainsPending=true.");
        }

        private void DeferExperienceAppraisalScan(string reason)
        {
            // No Qwen request completed, so this is scheduling pressure, not an appraisal attempt.
            nextExperienceScanAt = Time.unscaledTime + 1f;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_EXPERIENCE_APPRAISAL: status=not_dispatched; reason=" + reason +
                "; genuineAttemptsUnchanged=true.");
        }

        private void CancelActiveExperienceAppraisal()
        {
            if (activeExperienceBackgroundTicket != 0 && localDialogue)
                localDialogue.CancelBackground(activeExperienceBackgroundTicket);
            ClearActiveExperienceAppraisal();
        }

        private void ClearActiveExperienceAppraisal()
        {
            activeExperienceBackgroundTicket = 0;
            activeExperienceTicket = null;
        }

        private void CancelNpcExperienceAppraisal()
        {
            CancelActiveExperienceAppraisal();
            experienceConsumerStore = store;
            nextExperienceScanAt = Time.unscaledTime + 0.5f;
        }

        private static string ExperienceAppraisalKey(string observer, string evidenceKey)
        {
            return observer.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + observer +
                (evidenceKey == null ? "0:" : evidenceKey.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + evidenceKey);
        }
    }
}
