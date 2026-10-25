using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class ConversationArchiveCheckpointView
    {
        public string ArchiveId { get; }
        public string Head { get; }

        internal ConversationArchiveCheckpointView(ConversationArchiveCheckpoint checkpoint)
        {
            var copy = ConversationArchiveCheckpoint.CopyValidated(checkpoint);
            if (copy == null) throw new InvalidOperationException("Conversation archive checkpoint is unavailable.");
            ArchiveId = copy.ArchiveId;
            Head = copy.Head;
        }
    }

    public sealed partial class WorldStateStore
    {
        private SharedConversationArchive conversationArchive;
        private SharedConversationCueArchive conversationCueArchive;
        private object pendingConversationMemoryEpoch = new object();
        private Dictionary<string,List<ConversationCueArchiveRecord>> conversationCueIndex;
        private string conversationCueIndexHead;

        // ArchiveId names the storage namespace; Head pins ancestry. Return only
        // this detached pair so asynchronous reads cannot follow a later head.
        public ConversationArchiveCheckpointView CaptureConversationArchiveCheckpoint()
        {
            return new ConversationArchiveCheckpointView(state.ConversationArchive);
        }

        public WorldStateStore(uint seed,string archiveRoot) : this(seed)
        {
            conversationArchive=new SharedConversationArchive(archiveRoot);
            conversationCueArchive=new SharedConversationCueArchive(archiveRoot);
            state.ConversationArchive=new ConversationArchiveCheckpoint {ArchiveId=Guid.NewGuid().ToString("N")};
            state.ConversationCueArchive=new ConversationCueArchiveCheckpoint {
                ArchiveId=state.ConversationArchive.ArchiveId};
        }

        // Ordinary snapshots retain only uncommitted entries. Actual saves publish
        // them once and contain a fixed-size checkpoint. Failed writes leave the
        // prior checkpoint and pending records untouched for a safe retry.
        public WorldState SnapshotForSave()
        {
            if(conversationArchive==null) return Snapshot();
            var snapshot=Snapshot();
            EnsureConversationCueIndex(state.ConversationCueArchive);
            var checkpoint=conversationArchive.Commit(state.ConversationArchive,state.ConversationMemories);
            snapshot.ConversationArchive=checkpoint;
            snapshot.ConversationMemories.Clear();
            state.ConversationArchive=ConversationArchiveCheckpoint.CopyValidated(checkpoint);
            state.ConversationMemories.Clear();
            pendingConversationMemoryEpoch = new object();
            return snapshot;
        }

        private Dictionary<string,NpcConversationMemory> PrepareMemoryIndex(WorldState candidate)
        {
            if(candidate.ConversationCueArchive!=null && candidate.ConversationCueArchive.Head.Length>0)
            {
                if(conversationCueArchive==null)
                    throw new InvalidOperationException("This save requires its shared conversation cue archive.");
                EnsureConversationCueIndex(candidate.ConversationCueArchive);
            }
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
            pendingConversationMemoryEpoch = new object();
        }

        private static string CueAddressKey(string chunkHash,int recordIndex,string personId)
        { return chunkHash+":"+recordIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+personId; }

        private void EnsureConversationCueIndex(ConversationCueArchiveCheckpoint checkpoint)
        {
            string head=checkpoint==null ? null : checkpoint.ArchiveId+":"+checkpoint.Head;
            if(conversationCueIndex!=null && conversationCueIndexHead==head)return;
            var index=new Dictionary<string,List<ConversationCueArchiveRecord>>(StringComparer.Ordinal);
            if(checkpoint!=null && checkpoint.Head.Length>0)
            {
                if(conversationCueArchive==null)
                    throw new InvalidOperationException("This save requires its shared conversation cue archive.");
                foreach(var cue in conversationCueArchive.Load(checkpoint))
                {
                    string key=CueAddressKey(cue.SourceChunkHash,cue.SourceRecordIndex,cue.PersonId);
                    List<ConversationCueArchiveRecord> values;
                    if(!index.TryGetValue(key,out values))
                    {values=new List<ConversationCueArchiveRecord>();index.Add(key,values);}
                    values.Add(cue);
                }
            }
            conversationCueIndex=index;
            conversationCueIndexHead=head;
        }

        private List<ConversationCueArchiveRecord> ReadConversationCuesAt(
            SharedConversationArchive.RecordAddress address)
        {
            EnsureConversationCueIndex(state.ConversationCueArchive);
            List<ConversationCueArchiveRecord> values;
            return conversationCueIndex.TryGetValue(CueAddressKey(address.ChunkHash,
                address.RecordIndex,address.PersonId),out values)
                ? values : new List<ConversationCueArchiveRecord>();
        }

        private void PublishConversationCueIndex(ConversationCueArchiveCheckpoint checkpoint,
            ConversationCueArchiveRecord cue)
        {
            EnsureConversationCueIndex(state.ConversationCueArchive);
            string key=CueAddressKey(cue.SourceChunkHash,cue.SourceRecordIndex,cue.PersonId);
            List<ConversationCueArchiveRecord> values;
            if(!conversationCueIndex.TryGetValue(key,out values))
            {values=new List<ConversationCueArchiveRecord>();conversationCueIndex.Add(key,values);}
            values.Add(ConversationCueArchiveRecord.CopyValidated(cue));
            conversationCueIndexHead=checkpoint.ArchiveId+":"+checkpoint.Head;
        }
    }
}
