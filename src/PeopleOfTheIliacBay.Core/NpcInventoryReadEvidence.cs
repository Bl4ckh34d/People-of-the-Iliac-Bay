using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Detached evidence for one selected read, scoped to its immutable request binding.</summary>
    public sealed class NpcInventoryReadEvidence
    {
        private readonly ConversationContextBinding binding;
        private readonly NpcInventoryState captured;

        private NpcInventoryReadEvidence(ConversationContextBinding binding, NpcInventoryState saved)
        {
            this.binding = binding;
            captured = new NpcInventoryState { PersonId = saved.PersonId, Revision = saved.Revision };
            foreach (var item in saved.Items)
                captured.Items.Add(new NpcInventoryItem { Uid = item.Uid, NativePayload = item.NativePayload });
        }

        public static bool TryCapture(ConversationContextBinding binding, ConversationContextSourceHandle source,
            NpcInventoryState saved, long nativeRevision, IList<NpcInventoryItem> nativeItems, out NpcInventoryReadEvidence evidence)
        {
            evidence = null;
            if (binding == null || source == null || source.Kind != "npc-inventory" || source.Owner != binding.PersonId ||
                saved == null || source.Revision != saved.Revision ||
                !NpcInventoryReadAuthority.Matches(saved, binding.PersonId, nativeRevision, nativeItems)) return false;
            evidence = new NpcInventoryReadEvidence(binding, saved);
            return true;
        }

        // Binding reference is snapshot provenance. Successor snapshots retain this exact binding.
        // Other turns must not inherit an inventory dependency merely because the speaker matches.
        public bool AppliesTo(ConversationContextBinding candidate)
        { return ReferenceEquals(binding, candidate); }

        public bool Matches(ConversationContextBinding current, NpcInventoryState saved,
            long nativeRevision, IList<NpcInventoryItem> nativeItems)
        {
            return binding.HasSameRequestBinding(current) &&
                NpcInventoryReadAuthority.Matches(captured, current.PersonId, nativeRevision, nativeItems) &&
                NpcInventoryReadAuthority.Matches(saved, current.PersonId, captured.Revision, captured.Items);
        }
    }
}
