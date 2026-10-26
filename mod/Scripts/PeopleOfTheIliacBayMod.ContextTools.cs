using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // Tool data is captured only from the current, still-open native talk
        // session. The Core layer owns bounds, filtering, paging and formatting.
        private ConversationContextToolResult ExecuteConversationContextTool(ConversationContextToolRequest request,
            ConversationContextSourceHandle capturedSource = null)
        {
            if (request == null) return ConversationContextTools.Unavailable(ConversationContextToolId.BrowseTopics, null);
            if (!TryGetCurrentContextSpeaker(out var manager, out var ownerId, out var sessionRevision))
                return ConversationContextTools.Unavailable(request.Tool, null);

            switch (request.Tool)
            {
                case ConversationContextToolId.BrowseTopics:
                {
                    var topics = new List<ConversationTopic>();
                    var native = new Dictionary<int, TalkManager.ListItem>();
                    var seen = new HashSet<TalkManager.ListItem>();
                    AddConversationTopics(manager.ListTopicLocation, "Where is / Location", topics, native, seen);
                    AddConversationTopics(manager.ListTopicPerson, "Where is / People", topics, native, seen);
                    AddConversationTopics(manager.ListTopicThings, "Where is / Things", topics, native, seen);
                    AddConversationTopics(manager.ListTopicTellMeAbout, "Tell me about", topics, native, seen);
                    return ConversationContextTools.BrowseTopics(topics, ownerId, sessionRevision, request);
                }
                case ConversationContextToolId.ReadOwnInventory:
                    return ReadCurrentSpeakerInventory(request, manager, capturedSource);
                case ConversationContextToolId.ReadTopic:
                case ConversationContextToolId.ReadPlan:
                case ConversationContextToolId.RecallMemory:
                default:
                    return ConversationContextTools.Unavailable(request.Tool, ownerId);
            }
        }

        private bool TryGetCurrentContextSpeaker(out TalkManager manager, out string ownerId, out long revision)
        {
            manager = null;
            ownerId = null;
            revision = 0;
            if (saveFault || !conversationSessionOpen || store == null || memoryWindow == null ||
                DaggerfallUI.UIManager == null || DaggerfallUI.UIManager.TopWindow != memoryWindow ||
                !DaggerfallUI.UIManager.ContainsWindow(memoryWindow)) return false;

            var game = GameManager.Instance;
            if (game == null || game.TalkManager == null) return false;
            manager = game.TalkManager;
            if (manager.CurrentNPCType == TalkManager.NPCType.Mobile)
            {
                var mobile = manager.MobileNPC;
                if (!mobile || !conversationActor || conversationActor.Person != mobile ||
                    conversationActor.Resident == null || !conversationActor.Resident.IsAlive ||
                    conversationActor.Resident.Id != conversationPersonId) return false;
            }
            else if (manager.CurrentNPCType == TalkManager.NPCType.Static)
            {
                if (!manager.StaticNPC || string.IsNullOrEmpty(conversationPersonId)) return false;
            }
            else return false;

            ownerId = string.IsNullOrEmpty(memoryPersonId) ? conversationPersonId : memoryPersonId;
            if (string.IsNullOrEmpty(ownerId)) ownerId = "conversation:" + memorySession;
            revision = Math.Max(0, memorySession);
            return true;
        }

        private ConversationContextToolResult ReadCurrentSpeakerInventory(ConversationContextToolRequest request, TalkManager manager,
            ConversationContextSourceHandle capturedSource)
        {
            if (manager == null || manager.CurrentNPCType != TalkManager.NPCType.Mobile ||
                !manager.MobileNPC || !conversationActor || conversationActor.Person != manager.MobileNPC ||
                conversationActor.Resident == null || !conversationActor.Resident.IsAlive ||
                conversationActor.Resident.IsQuestProtected ||
                conversationActor.Resident.Id != conversationPersonId)
                return ConversationContextTools.Unavailable(ConversationContextToolId.ReadOwnInventory, null);

            var owner = conversationActor.GetComponent<NativeNpcInventoryOwner>();
            var binding = owner == null ? null : owner.Binding;
            if (store == null || binding == null || binding.PersonId != conversationPersonId || binding.Items == null)
                return ConversationContextTools.Unavailable(ConversationContextToolId.ReadOwnInventory, conversationPersonId);
            if (capturedSource != null && (capturedSource.Kind != "npc-inventory" || capturedSource.Owner != conversationPersonId ||
                capturedSource.Revision != binding.Revision))
                return ConversationContextTools.Unavailable(ConversationContextToolId.ReadOwnInventory, conversationPersonId);

            // TryCapture is a write path: it can commit items or refresh the lease.
            // A dialogue read must only compare detached snapshots and reject a stale binding.
            try
            {
                if (!NpcInventoryReadAuthority.Matches(store.ReadNpcInventory(conversationPersonId),
                    conversationPersonId, binding.Revision, NativeNpcInventoryBridge.CaptureItems(binding.Items)))
                    return ConversationContextTools.Unavailable(ConversationContextToolId.ReadOwnInventory, conversationPersonId);
                var items = new List<ConversationContextInventoryItem>(binding.Items.Count);
                for (int i = 0; i < binding.Items.Count; i++)
                {
                    DaggerfallUnityItem item = binding.Items.GetItem(i);
                    if (item == null || item.stackCount < 1) continue;
                    items.Add(new ConversationContextInventoryItem
                    {
                        Name = item.LongName,
                        Category = item.ItemGroup.ToString(),
                        Quantity = item.stackCount,
                        Properties = NativeNpcRepairContext.Describe(item)
                    });
                }
                return ConversationContextTools.PageInventory(conversationPersonId, binding.Revision, items, request);
            }
            catch (Exception)
            {
                return ConversationContextTools.Unavailable(ConversationContextToolId.ReadOwnInventory, conversationPersonId);
            }
        }
    }
}
