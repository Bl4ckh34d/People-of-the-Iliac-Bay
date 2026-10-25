using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private bool decisionShadowInitialized;
        private LocalDecisionSession decisionShadowSession;
        private LocalDecisionShadow decisionShadow;
        private LocalDecisionSession decisionRoutingSession;
        private LocalDialogueConnection decisionRoutingConnection;

        private void InitializeDecisionShadow()
        {
            if (decisionShadowInitialized) return;
            decisionShadowInitialized = true;
            LocalDecisionShadowConfiguration configuration;
            string diagnosticCode;
            if (decisionPlannerEnabled && localDialogueEnabled && LocalDecisionShadowConfiguration.TryParseRouting(Environment.GetCommandLineArgs(), out configuration, out diagnosticCode))
            {
                decisionRoutingSession = new LocalDecisionSession();
                decisionRoutingSession.ExpectedModelId = configuration.ExpectedModelId;
                decisionRoutingSession.Enabled = true;
                decisionRoutingConnection = configuration.Connection;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_READY: purpose=" + LocalDecisionRouting.Purpose +
                    "; refinement=Qwen; nativeValidation=required");
                return;
            }
            if (!LocalDecisionShadowConfiguration.TryParse(Environment.GetCommandLineArgs(), out configuration, out diagnosticCode))
            {
                if (diagnosticCode != "disabled")
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DECISION_SHADOW_CONFIG: " + diagnosticCode);
                return;
            }
            decisionShadowSession = new LocalDecisionSession();
            decisionShadowSession.ExpectedModelId = configuration.ExpectedModelId;
            decisionShadowSession.Enabled = true;
            decisionShadow = new LocalDecisionShadow(decisionShadowSession, configuration.Connection, RecordDecisionShadow);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_SHADOW_READY: wireProtocol=" + NpcDecisionRequest.CurrentProtocolVersion +
                "; purpose=" + LocalDecisionRouting.Purpose + "; nativeDispatch=false");
        }

        // Called exactly once after the normal player-turn snapshot capture. This
        // observer has no stage token, dialogue callback or action authority.
        private void ObserveDecisionShadow(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window, string playerText)
        {
            if (decisionShadow == null || decisionShadowSession == null || !decisionShadowSession.Enabled ||
                !isActiveAndEnabled || !options.Enabled || !localDialogueEnabled || saveFault) return;
            if (!IsCurrentConversationContext(snapshot, window) ||
                !string.Equals(playerText, conversationContextCapturedPlayerText, StringComparison.Ordinal)) return;
            try
            {
                var request = BuildConversationDecisionRequest(snapshot, window, playerText);
                decisionShadow.Observe(request, () => IsCurrentConversationContext(snapshot, window)
                    ? new LocalDecisionFreshness(snapshot.Binding, snapshot.Revision, request.CandidateSetRevision, request.Options)
                    : null);
            }
            catch (ArgumentException)
            {
                // An oversized/unsupported observation must not change the live
                // chat or silently submit a clipped version of the player's turn.
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_SHADOW_SKIPPED: invalid_or_oversized_context");
            }
            catch (InvalidOperationException)
            {
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_SHADOW_SKIPPED: context_unavailable");
            }
        }

        private NpcDecisionRequest BuildConversationDecisionRequest(ConversationContextSnapshot snapshot,
            DaggerfallTalkWindow window, string playerText)
        {
            NpcActionOption offered = null;
            int duration = 0;
            if (pendingConversationAction != null && conversationActor && streets &&
                string.Equals(pendingConversationActionHistory, ReadConversationHistory(window), StringComparison.Ordinal))
                pendingConversationAction.TryGetSemanticCandidate(conversationPersonId, memorySession,
                    ConversationActionTime(), out offered, out duration);
            return LocalDecisionRouting.Build(snapshot, playerText, ReadDecisionShadowHistory(window),
                ReadCapturedConversationMemory(snapshot, window), offered, duration);
        }

        // D3 selects a route, never an effect. Qwen and the existing native
        // validators still resolve details and willingness before commitment.
        private bool TryBeginDecisionRouting(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window,
            string playerText, Action<string> complete)
        {
            if (!decisionPlannerEnabled || decisionRoutingSession == null || !decisionRoutingSession.Enabled || decisionRoutingConnection == null ||
                !isActiveAndEnabled || !options.Enabled || !localDialogueEnabled || saveFault || complete == null)
                return false;
            if (!IsCurrentConversationContext(snapshot, window)) return false;
            NpcDecisionRequest request;
            try { request = BuildConversationDecisionRequest(snapshot, window, playerText); }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            int session = memorySession;
            string person = conversationPersonId;
            long stage = BeginConversationContextStage(snapshot, window);
            if (stage <= 0)
            {
                HandleRejectedConversationStage(snapshot, window, stage, playerText);
                return true;
            }
            bool submitted = decisionRoutingSession.TryRequest(decisionRoutingConnection, request,
                () =>
                {
                    if (!IsCurrentConversationContext(snapshot, window)) return null;
                    var currentRequest = BuildConversationDecisionRequest(snapshot, window, playerText);
                    return new LocalDecisionFreshness(snapshot.Binding, snapshot.Revision,
                        currentRequest.CandidateSetRevision, currentRequest.Options);
                },
                result =>
                {
                    if (!CurrentMemorySession(window, session) || person != conversationPersonId) return;
                    if (!TryConsumeConversationContextStage(snapshot, window, stage))
                    { HandleRejectedConversationStage(snapshot, window, stage, playerText); return; }
                    string route = result.Status == NpcDecisionStatus.Ok ? result.SelectedOptionId : null;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_RESULT: status=" + result.Status +
                        "; route=" + (route ?? "Qwen-fallback") + "; latencyMs=" +
                        result.LatencyMilliseconds.ToString("F2", CultureInfo.InvariantCulture) + "; nativeDispatch=false");
                    complete(route);
                });
            if (submitted) return true;
            if (!TryConsumeConversationContextStage(snapshot, window, stage)) return true;
            return false;
        }

        private static IEnumerable<ConversationContextTurn> ReadDecisionShadowHistory(DaggerfallTalkWindow window)
        {
            var transcript = window == null || conversationListField == null ? null : conversationListField.GetValue(window) as ListBox;
            var turns = new List<ConversationContextTurn>();
            if (transcript == null) return turns;
            for (int i = 0; i < transcript.Count; i++)
            {
                var row = transcript.GetItem(i);
                if (row == null || row.textLabel == null || string.IsNullOrWhiteSpace(row.textLabel.Text) ||
                    row.textLabel.Name == "PeopleOfTheIliacBayHistoryDay" || row.textLabel.Name == "PeopleOfTheIliacBayHistoryRule" ||
                    row.textLabel.Name == UnavailableTranscriptRowName) continue;
                turns.Add(new ConversationContextTurn(row.textColor == DaggerfallUI.DaggerfallQuestionTextColor
                    ? ConversationTextSpeaker.Player : ConversationTextSpeaker.Npc, row.textLabel.Text));
            }
            return turns;
        }

        private static void RecordDecisionShadow(LocalDecisionShadowObservation observation)
        {
            // Deliberately no player/NPC text, names, model paths or raw responses.
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_SHADOW: request=" + observation.RequestId.ToString("D") +
                "; requestRevision=" + observation.Binding.RequestRevision.ToString(CultureInfo.InvariantCulture) +
                "; snapshot=" + observation.SnapshotRevision.ToString(CultureInfo.InvariantCulture) +
                "; candidates=" + observation.CandidateSetRevision.ToString(CultureInfo.InvariantCulture) +
                "; wireProtocol=" + NpcDecisionRequest.CurrentProtocolVersion + "; purpose=" + LocalDecisionRouting.Purpose + "; status=" + observation.Status +
                "; failure=" + observation.FailureKind + "; choice=" + (observation.SelectedOptionId ?? "none") +
                "; sha256=" + observation.RequestSha256 +
                "; latencyMs=" + observation.LatencyMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
                "; diagnostic=" + observation.DiagnosticCode + "; nativeDispatch=false");
        }

        private void UpdateDecisionShadow()
        {
            if (!options.Enabled || !localDialogueEnabled || saveFault) { CancelDecisionShadow(); return; }
            if (decisionPlannerEnabled && decisionRoutingSession != null) decisionRoutingSession.Pump();
            if (decisionShadow != null) decisionShadow.Pump();
        }

        private void CancelDecisionShadow()
        {
            CancelNpcExperienceAppraisal();
            if (decisionShadow != null) decisionShadow.CancelAll();
            if (decisionRoutingSession != null) decisionRoutingSession.CancelAll();
        }

        private void DisposeDecisionShadow()
        {
            if (decisionShadow != null) decisionShadow.Dispose();
            decisionShadow = null;
            if (decisionShadowSession != null) decisionShadowSession.Dispose();
            decisionShadowSession = null;
            if (decisionRoutingSession != null) decisionRoutingSession.Dispose();
            decisionRoutingSession = null;
            decisionRoutingConnection = null;
        }

        private void OnDisable()
        {
            if (splitPortraitOverlay) splitPortraitOverlay.DetachAndRestore();
            ResetPortraitPresentation();
            CancelDecisionShadow();
            if (breezeTtsTrial) breezeTtsTrial.EndConversation(conversationContextSessionId);
        }

        private void InstallDecisionRoutingForProbe(LocalDecisionSession session, LocalDialogueConnection connection)
        {
            if ((Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-routing-probe") < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-routing-appraisal-reopen-probe") < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-willingness-probe") < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-binding-probe") < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-sequence-probe") < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-goal-cancellation-probe") < 0 &&
                Array.IndexOf(Environment.GetCommandLineArgs(), GoalChainLengthProbe.Switch) < 0) ||
                session == null || connection == null)
                throw new InvalidOperationException("Decision routing injection requires the owned native probe.");
            DisposeDecisionShadow();
            decisionShadowInitialized = true;
            decisionRoutingSession = session;
            decisionRoutingConnection = connection;
        }

        // Only the owned native probe can inject a non-network transport. Normal
        // games cannot replace the configured session through this entry point.
        private void InstallDecisionShadowForProbe(LocalDecisionSession session, LocalDecisionShadow shadow)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-decision-shadow-probe") < 0 || session == null || shadow == null)
                throw new InvalidOperationException("Decision shadow injection requires the owned native probe.");
            DisposeDecisionShadow();
            decisionShadowInitialized = true;
            decisionShadowSession = session;
            decisionShadow = shadow;
        }
    }
}
