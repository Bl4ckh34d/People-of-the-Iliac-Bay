using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private ConversationContextSnapshot npcKnowledgeLookupAttemptedSnapshot;
        private bool npcKnowledgeLookupAttempted;

        /// <summary>
        /// Performs at most one bounded topic, inventory, memory or mood-cause read for this speaker snapshot.
        /// The callback receives renderer-safe context only while the captured conversation is still current.
        /// </summary>
        internal void BeginNpcKnowledgeLookup(ConversationContextSnapshot snapshot, DaggerfallTalkWindow window,
            string playerText, string history, Action<string> continueWithToolContext)
        {
            if (continueWithToolContext == null) return;
            if (snapshot == null || snapshot.Binding == null) return;
            if (!IsCurrentConversationContext(snapshot, window))
            { HandleRejectedConversationStage(snapshot, window, 0, playerText); return; }

            if (!ReferenceEquals(npcKnowledgeLookupAttemptedSnapshot, snapshot))
            {
                npcKnowledgeLookupAttemptedSnapshot = snapshot;
                npcKnowledgeLookupAttempted = false;
            }
            if (npcKnowledgeLookupAttempted)
            { continueWithToolContext(string.Empty); return; }
            npcKnowledgeLookupAttempted = true;

            string actorId = snapshot.Binding.PersonId;
            NpcKnowledgeSnapshot knowledge = null;
            if (!string.IsNullOrWhiteSpace(actorId) && store != null)
                store.TryCaptureNpcKnowledgeSnapshot(actorId, out knowledge);

            IList<NpcDisclosurePublicTopic> topics = ConversationContextTools.PublicKnowledgeTopics(knowledge);
            ConversationContextSourceHandle capturedInventorySource = null;
            foreach (var source in snapshot.ReadSources)
                if (source.Kind == "npc-inventory" && source.Owner == actorId)
                    capturedInventorySource = source;
            bool ownMemoryAvailable = CanSelectConversationMemoryRead(snapshot);
            if (topics.Count == 0 && capturedInventorySource == null && !ownMemoryAvailable)
            {
                // No eligible source can satisfy a read; do not spend a selector call.
                continueWithToolContext("Current personal inventory is unavailable for this speaker. Do not infer item possession.");
                return;
            }
            string prompt = NpcConversationLookup.Prompt(playerText, history, topics, ownMemoryAvailable, ownMemoryAvailable);
            if (prompt == null)
            {
                continueWithToolContext(ConversationContextTools.Format(
                    ConversationContextTools.Unavailable(ConversationContextToolId.ReadTopic, actorId)));
                return;
            }

            long stageToken = BeginConversationContextStage(snapshot, window);
            if (stageToken <= 0)
            { HandleRejectedConversationStage(snapshot, window, stageToken, playerText); return; }

            bool started = localDialogue != null && localDialogue.Classify(NewDialogueConnection(), prompt,
                string.Empty, output =>
                {
                    if (!TryConsumeConversationContextStage(snapshot, window, stageToken))
                    { HandleRejectedConversationStage(snapshot, window, stageToken, playerText); return; }
                    if (output == LocalDialogueClient.Unavailable || output == LocalDialogueClient.ContextLimit)
                    { continueWithToolContext(string.Empty); return; }

                    ConversationContextToolRequest request;
                    if (!NpcConversationLookup.TryParseSelection(output, topics, out request, ownMemoryAvailable, ownMemoryAvailable) || request == null)
                    { continueWithToolContext(string.Empty); return; }

                    if (request.Tool == ConversationContextToolId.RecallMemory || request.Tool == ConversationContextToolId.ReadMoodCauses)
                    {
                        continueWithToolContext(ReadSelectedConversationMemory(snapshot, request));
                        return;
                    }

                    if (request.Tool == ConversationContextToolId.ReadOwnInventory)
                    {
                        // Stage consumption checked speaker, request, scene, archive and save epoch.
                        // The runtime read independently validates the current managed native owner.
                        var inventory = capturedInventorySource == null ? ConversationContextTools.Unavailable(request.Tool, actorId) :
                            ExecuteConversationContextTool(request, capturedInventorySource);
                        if (capturedInventorySource == null || capturedInventorySource.Revision != inventory.Revision ||
                            !string.Equals(inventory.OwnerId, actorId, StringComparison.Ordinal))
                            inventory = ConversationContextTools.Unavailable(request.Tool, actorId);
                        if ((inventory.Status == ConversationContextToolStatus.Ok || inventory.Status == ConversationContextToolStatus.NoResults) &&
                            !TryBindConversationInventoryRead(snapshot, capturedInventorySource))
                            inventory = ConversationContextTools.Unavailable(request.Tool, actorId);
                        continueWithToolContext(ConversationContextTools.Format(inventory));
                        return;
                    }

                    NpcKnowledgeSnapshot currentKnowledge;
                    if (store == null || !store.TryCaptureNpcKnowledgeSnapshot(actorId, out currentKnowledge) ||
                        !NpcKnowledgeLookup.IsSameSnapshot(knowledge, currentKnowledge, actorId))
                    {
                        continueWithToolContext(ConversationContextTools.Format(
                            ConversationContextTools.Unavailable(ConversationContextToolId.ReadTopic, actorId)));
                        return;
                    }

                    ConversationContextToolResult read = ConversationContextTools.ReadNpcKnowledgeTopic(
                        currentKnowledge, actorId, NpcDisclosurePolicy.PlayerAudienceId, request);
                    continueWithToolContext(ConversationContextTools.Format(read));
                }, NpcConversationLookup.Grammar(topics, ownMemoryAvailable, ownMemoryAvailable), NpcConversationLookup.MaximumOutputTokens);

            if (!started)
            {
                if (TryConsumeConversationContextStage(snapshot, window, stageToken)) continueWithToolContext(string.Empty);
                else HandleRejectedConversationStage(snapshot, window, stageToken, playerText);
            }
        }
    }
}
