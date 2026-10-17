using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public sealed partial class WorldStateStore
    {
        private SharedConversationArchive conversationArchive;

        public WorldStateStore(uint seed,string archiveRoot) : this(seed)
        {
            conversationArchive=new SharedConversationArchive(archiveRoot);
            state.ConversationArchive=new ConversationArchiveCheckpoint {ArchiveId=Guid.NewGuid().ToString("N")};
        }

        // Ordinary snapshots retain only uncommitted entries. Actual saves publish
        // them once and contain a fixed-size checkpoint. Failed writes leave the
        // prior checkpoint and pending records untouched for a safe retry.
        public WorldState SnapshotForSave()
        {
            if(conversationArchive==null) return Snapshot();
            var snapshot=Snapshot();
            var checkpoint=conversationArchive.Commit(state.ConversationArchive,state.ConversationMemories);
            snapshot.ConversationArchive=checkpoint;
            snapshot.ConversationMemories.Clear();
            state.ConversationArchive=ConversationArchiveCheckpoint.CopyValidated(checkpoint);
            state.ConversationMemories.Clear();
            return snapshot;
        }

        private Dictionary<string,NpcConversationMemory> PrepareMemoryIndex(WorldState candidate)
        {
            var index=new Dictionary<string,NpcConversationMemory>(StringComparer.Ordinal);
            if(candidate.ConversationArchive!=null && candidate.ConversationArchive.Head.Length>0)
            {
                if(conversationArchive==null) throw new InvalidOperationException("This save requires its shared PeopleMemory archive.");
                foreach(var journal in conversationArchive.Load(candidate.ConversationArchive)) index.Add(journal.PersonId,journal);
            }
            foreach(var pending in candidate.ConversationMemories)
            {
                NpcConversationMemory journal;
                if(!index.TryGetValue(pending.PersonId,out journal))
                {journal=new NpcConversationMemory {PersonId=pending.PersonId}; index.Add(pending.PersonId,journal);}
                journal.Entries.AddRange(pending.Entries);
            }
            return index;
        }

        private void AddPendingMemory(string personId,NpcMemoryEntry entry)
        {
            var pending=state.ConversationMemories.Find(j=>j.PersonId==personId);
            if(pending==null) {pending=new NpcConversationMemory {PersonId=personId}; state.ConversationMemories.Add(pending);}
            pending.Entries.Add(entry);
        }
    }
}
