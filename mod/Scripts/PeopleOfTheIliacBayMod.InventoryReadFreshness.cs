using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // One bounded lease, attached only after a successful model-selected inventory read.
        private NpcInventoryReadEvidence conversationInventoryReadEvidence;
        private NativeNpcInventoryBinding conversationInventoryReadNativeBinding;

        private bool HasSelectedConversationInventoryRead(ConversationContextSnapshot snapshot)
        {
            return snapshot != null && conversationInventoryReadEvidence != null &&
                conversationInventoryReadEvidence.AppliesTo(snapshot.Binding);
        }

        private bool TryBindConversationInventoryRead(ConversationContextSnapshot snapshot, ConversationContextSourceHandle source)
        {
            if (snapshot == null || snapshot.Binding == null || source == null || store == null ||
                !conversationActor || conversationActor.Resident == null || !conversationActor.Resident.IsAlive ||
                conversationActor.Resident.IsQuestProtected || conversationActor.Resident.Id != snapshot.Binding.PersonId) return false;
            var owner = conversationActor.GetComponent<NativeNpcInventoryOwner>();
            var native = owner == null ? null : owner.Binding;
            if (native == null || native.Items == null || native.PersonId != snapshot.Binding.PersonId) return false;
            try
            {
                NpcInventoryReadEvidence evidence;
                if (!NpcInventoryReadEvidence.TryCapture(snapshot.Binding, source, store.ReadNpcInventory(native.PersonId),
                    native.Revision, NativeNpcInventoryBridge.CaptureItems(native.Items), out evidence)) return false;
                conversationInventoryReadEvidence = evidence;
                conversationInventoryReadNativeBinding = native;
                return true;
            }
            catch (Exception) { return false; }
        }

        private bool IsSelectedConversationInventoryCurrent(ConversationContextSnapshot snapshot)
        {
            if (!HasSelectedConversationInventoryRead(snapshot)) return true;
            // The stage gate has already validated this snapshot against the live request,
            // speaker, scene, save epoch and archive. Resolve the current native lease again.
            ConversationSpeakerCapture speaker;
            if (!TryResolveConversationSpeaker(out speaker)) return false;
            if (speaker == null || !speaker.ManagedActor || speaker.ManagedActor.Resident == null ||
                !speaker.ManagedActor.Resident.IsAlive || speaker.ManagedActor.Resident.IsQuestProtected || store == null) return false;
            var owner = speaker.ManagedActor.GetComponent<NativeNpcInventoryOwner>();
            var native = owner == null ? null : owner.Binding;
            if (native == null || !ReferenceEquals(native, conversationInventoryReadNativeBinding) || native.Items == null ||
                native.PersonId != snapshot.Binding.PersonId) return false;
            try
            {
                return conversationInventoryReadEvidence.Matches(snapshot.Binding, store.ReadNpcInventory(native.PersonId),
                    native.Revision, NativeNpcInventoryBridge.CaptureItems(native.Items));
            }
            catch (Exception) { return false; }
        }
    }
}
