using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    internal enum ConversationFeatureIndexCacheStatus
    {
        Building,
        Ready,
        UseScanFallback
    }

    internal sealed class ConversationFeatureIndexCacheResult
    {
        internal ConversationFeatureIndexCacheStatus Status { get; private set; }
        internal ConversationFeatureIndexBuilder Builder { get; private set; }
        internal ConversationFeatureIndex Index { get; private set; }

        internal ConversationFeatureIndexCacheResult(ConversationFeatureIndexCacheStatus status,
            ConversationFeatureIndexBuilder builder,ConversationFeatureIndex index)
        {Status=status;Builder=builder;Index=index;}

        internal static ConversationFeatureIndexCacheResult Building(ConversationFeatureIndexBuilder builder)
        {return new ConversationFeatureIndexCacheResult(ConversationFeatureIndexCacheStatus.Building,builder,null);}
        internal static ConversationFeatureIndexCacheResult Ready(ConversationFeatureIndexBuilder builder,ConversationFeatureIndex index)
        {return new ConversationFeatureIndexCacheResult(ConversationFeatureIndexCacheStatus.Ready,builder,index);}
        internal static ConversationFeatureIndexCacheResult Fallback()
        {return new ConversationFeatureIndexCacheResult(ConversationFeatureIndexCacheStatus.UseScanFallback,null,null);}
    }

    // Small state-scoped LRU for completed indexes and in-flight builders.
    // It is an optional Core owner; the live recall path does not use it yet.
    internal sealed class ConversationFeatureIndexCache
    {
        private sealed class Entry
        {
            internal string PersonId;
            internal IList<NpcMemoryEntry> Journal;
            internal int JournalCount;
            internal ConversationFeatureIndexBuilder Builder;
            internal LinkedListNode<string> LruNode;
        }

        // Rejected builds need only a small negative-cache record. Retaining
        // the journal reference and row count lets the owner distinguish an
        // unchanged unsupported journal from a changed one without copying it.
        private sealed class Rejection
        {
            internal string PersonId;
            internal IList<NpcMemoryEntry> Journal;
            internal int JournalCount;
            internal LinkedListNode<string> LruNode;
        }

        private readonly int maximumPeople;
        private readonly long maximumBytes;
        private readonly long maximumIndexBytes;
        private readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        private readonly LinkedList<string> lru=new LinkedList<string>();
        private readonly Dictionary<string,Rejection> rejections=new Dictionary<string,Rejection>(StringComparer.Ordinal);
        private readonly LinkedList<string> rejectionLru=new LinkedList<string>();
        private object stateIdentity;

        internal ConversationFeatureIndexCache(int maximumPeople=4,long maximumBytes=20000000,
            long maximumIndexBytes=5000000)
        {
            if(maximumPeople<1)throw new ArgumentOutOfRangeException("maximumPeople");
            if(maximumBytes<0)throw new ArgumentOutOfRangeException("maximumBytes");
            if(maximumIndexBytes<0)throw new ArgumentOutOfRangeException("maximumIndexBytes");
            this.maximumPeople=maximumPeople;this.maximumBytes=maximumBytes;this.maximumIndexBytes=maximumIndexBytes;
        }

        internal int PersonCount { get { return entries.Count; } }
        internal long OwnedBytes
        {
            get {long total=0;foreach(Entry entry in entries.Values)total+=entry.Builder.OwnedBytes;return total;}
        }
        internal bool ContainsPerson(string personId)
        {return personId!=null&&entries.ContainsKey(personId);}

        internal bool TryGetReady(string personId,IList<NpcMemoryEntry> journal,object currentStateIdentity,
            out ConversationFeatureIndex index)
        {
            index=null;
            if(string.IsNullOrWhiteSpace(personId)||journal==null||currentStateIdentity==null)return false;
            if(!ReferenceEquals(stateIdentity,currentStateIdentity)){EnterState(currentStateIdentity);return false;}
            Rejection rejection;
            if(rejections.TryGetValue(personId,out rejection))
            {
                if(ReferenceEquals(rejection.Journal,journal)&&rejection.JournalCount==journal.Count)
                {TouchRejection(rejection);return false;}
                RemoveRejection(rejection);
            }
            Entry entry;
            if(!entries.TryGetValue(personId,out entry))return false;
            if(!ReferenceEquals(entry.Journal,journal)||entry.JournalCount!=journal.Count)
            {Remove(entry);return false;}
            index=entry.Builder.GetCompletedIndex();
            if(index==null)return false;
            Touch(entry);return true;
        }

        internal void CancelIncompleteExcept(string activePerson,object currentStateIdentity)
        {
            if(currentStateIdentity==null)return;
            if(!ReferenceEquals(stateIdentity,currentStateIdentity)){EnterState(currentStateIdentity);return;}
            LinkedListNode<string> node=lru.First;
            while(node!=null)
            {
                LinkedListNode<string> next=node.Next;
                Entry entry=entries[node.Value];
                if(entry.PersonId!=activePerson&&!entry.Builder.IsComplete)Remove(entry);
                node=next;
            }
        }

        internal void CancelIncomplete(string personId,object currentStateIdentity)
        {
            if(string.IsNullOrWhiteSpace(personId)||currentStateIdentity==null)return;
            if(!ReferenceEquals(stateIdentity,currentStateIdentity)){EnterState(currentStateIdentity);return;}
            Entry entry;
            if(entries.TryGetValue(personId,out entry)&&!entry.Builder.IsComplete)Remove(entry);
        }

        // Journals are append-only in the owner store. If an existing row is
        // rewritten, callers must invalidate that person's detached snapshot
        // before requesting another indexed read.
        internal bool InvalidatePerson(string personId,object currentStateIdentity)
        {
            if(string.IsNullOrWhiteSpace(personId)||currentStateIdentity==null)return false;
            if(!ReferenceEquals(stateIdentity,currentStateIdentity))
            {EnterState(currentStateIdentity);return false;}
            Entry entry;
            bool removed=false;
            if(entries.TryGetValue(personId,out entry)){Remove(entry);removed=true;}
            Rejection rejection;
            if(rejections.TryGetValue(personId,out rejection)){RemoveRejection(rejection);removed=true;}
            return removed;
        }

        internal ConversationFeatureIndexCacheResult GetOrCreate(string personId,
            IList<NpcMemoryEntry> journal,object currentStateIdentity)
        {
            Validate(personId,journal,currentStateIdentity);EnterState(currentStateIdentity);
            Rejection rejection;
            if(rejections.TryGetValue(personId,out rejection))
            {
                if(ReferenceEquals(rejection.Journal,journal)&&rejection.JournalCount==journal.Count)
                {TouchRejection(rejection);return ConversationFeatureIndexCacheResult.Fallback();}
                RemoveRejection(rejection);
            }
            Entry entry;
            if(entries.TryGetValue(personId,out entry))
            {
                if(!ReferenceEquals(entry.Journal,journal)||entry.JournalCount!=journal.Count)
                    Remove(entry);
                else
                {Touch(entry);return ResultFor(entry);}
            }

            while(entries.Count+rejections.Count>=maximumPeople)
            {
                if(rejectionLru.First!=null)RemoveRejection(rejections[rejectionLru.First.Value]);
                else RemoveOldest();
            }
            var builder=new ConversationFeatureIndexBuilder(journal,currentStateIdentity,null,maximumIndexBytes);
            entry=new Entry{PersonId=personId,Journal=journal,JournalCount=journal.Count,Builder=builder};
            entry.LruNode=lru.AddLast(personId);entries.Add(personId,entry);
            EnforceByteLimit(personId);
            return entries.ContainsKey(personId)?ResultFor(entry):ConversationFeatureIndexCacheResult.Fallback();
        }

        internal ConversationFeatureIndexCacheResult Advance(string personId,object currentStateIdentity,
            int maximumEntries,long maximumElapsedTicks)
        {
            if(string.IsNullOrWhiteSpace(personId))throw new ArgumentException("Person ID is required.","personId");
            if(currentStateIdentity==null)throw new ArgumentNullException("currentStateIdentity");
            if(!ReferenceEquals(stateIdentity,currentStateIdentity))
            {EnterState(currentStateIdentity);return ConversationFeatureIndexCacheResult.Fallback();}
            Entry entry;
            if(!entries.TryGetValue(personId,out entry))return ConversationFeatureIndexCacheResult.Fallback();
            if(entry.Journal.Count!=entry.JournalCount)
            {Remove(entry);return ConversationFeatureIndexCacheResult.Fallback();}

            entry.Builder.Advance(currentStateIdentity,maximumEntries,maximumElapsedTicks);
            if(entry.Builder.IsRejected)
            {
                IList<NpcMemoryEntry> rejectedJournal=entry.Journal;int rejectedCount=entry.JournalCount;
                Remove(entry);AddRejection(personId,rejectedJournal,rejectedCount);
                return ConversationFeatureIndexCacheResult.Fallback();
            }
            if(entry.Builder.IsCancelled)
            {Remove(entry);return ConversationFeatureIndexCacheResult.Fallback();}
            Touch(entry);EnforceByteLimit(personId);
            return entries.ContainsKey(personId)?ResultFor(entry):ConversationFeatureIndexCacheResult.Fallback();
        }

        // Caller has appended the exact tail to the stable journal list. The
        // cache updates only that person's completed index; an in-flight build
        // or stale journal is discarded and asks the caller to use scan fallback.
        internal bool Append(string personId,NpcMemoryEntry appended,int journalIndex,object currentStateIdentity)
        {
            if(string.IsNullOrWhiteSpace(personId)||appended==null||currentStateIdentity==null)return false;
            if(!ReferenceEquals(stateIdentity,currentStateIdentity))
            {EnterState(currentStateIdentity);return false;}
            Entry entry;
            if(!entries.TryGetValue(personId,out entry))return false;
            if(!entry.Builder.IsComplete){Remove(entry);return false;}
            if(journalIndex!=entry.JournalCount||entry.Journal.Count!=entry.JournalCount+1||
                !ReferenceEquals(entry.Journal[journalIndex],appended))
            {Remove(entry);return false;}
            ConversationFeatureIndex index=entry.Builder.GetCompletedIndex();
            if(index==null)return false;
            try{index.Append(appended,journalIndex);}
            catch(ArgumentException){Remove(entry);return false;}
            entry.JournalCount=entry.Journal.Count;
            Touch(entry);EnforceByteLimit(personId);
            return entries.ContainsKey(personId);
        }

        private void Validate(string personId,IList<NpcMemoryEntry> journal,object currentStateIdentity)
        {
            if(string.IsNullOrWhiteSpace(personId))throw new ArgumentException("Person ID is required.","personId");
            if(journal==null)throw new ArgumentNullException("journal");
            if(currentStateIdentity==null)throw new ArgumentNullException("currentStateIdentity");
        }

        private void EnterState(object identity)
        {
            if(stateIdentity!=null&&ReferenceEquals(stateIdentity,identity))return;
            Clear();stateIdentity=identity;
        }

        private void Clear()
        {
            foreach(Entry entry in entries.Values)entry.Builder.Invalidate();
            entries.Clear();lru.Clear();rejections.Clear();rejectionLru.Clear();
        }

        private void EnforceByteLimit(string protectedPerson)
        {
            while(OwnedBytes>maximumBytes&&entries.Count>1)
            {
                Entry oldest=entries[lru.First.Value];
                if(oldest.PersonId==protectedPerson&&lru.Count>1)oldest=entries[lru.First.Next.Value];
                Remove(oldest);
            }
            if(OwnedBytes>maximumBytes)
            {
                Entry protectedEntry;
                if(entries.TryGetValue(protectedPerson,out protectedEntry))
                {
                    IList<NpcMemoryEntry> rejectedJournal=protectedEntry.Journal;int rejectedCount=protectedEntry.JournalCount;
                    Remove(protectedEntry);AddRejection(protectedPerson,rejectedJournal,rejectedCount);
                }
            }
        }

        private void RemoveOldest()
        {if(lru.First!=null)Remove(entries[lru.First.Value]);}

        private void Remove(Entry entry)
        {
            if(entry==null)return;
            entries.Remove(entry.PersonId);lru.Remove(entry.LruNode);entry.Builder.Invalidate();
        }

        private void Touch(Entry entry)
        {lru.Remove(entry.LruNode);lru.AddLast(entry.LruNode);}

        private void AddRejection(string personId,IList<NpcMemoryEntry> journal,int journalCount)
        {
            Rejection existing;
            if(rejections.TryGetValue(personId,out existing))RemoveRejection(existing);
            while(entries.Count+rejections.Count>=maximumPeople)
            {
                if(rejectionLru.First!=null)RemoveRejection(rejections[rejectionLru.First.Value]);
                else RemoveOldest();
            }
            var rejection=new Rejection{PersonId=personId,Journal=journal,JournalCount=journalCount};
            rejection.LruNode=rejectionLru.AddLast(personId);rejections.Add(personId,rejection);
        }

        private void RemoveRejection(Rejection rejection)
        {
            if(rejection==null)return;
            rejections.Remove(rejection.PersonId);rejectionLru.Remove(rejection.LruNode);
        }

        private void TouchRejection(Rejection rejection)
        {rejectionLru.Remove(rejection.LruNode);rejection.LruNode=rejectionLru.AddLast(rejection.PersonId);}

        private ConversationFeatureIndexCacheResult ResultFor(Entry entry)
        {
            ConversationFeatureIndex index=entry.Builder.GetCompletedIndex();
            return index==null?ConversationFeatureIndexCacheResult.Building(entry.Builder):
                ConversationFeatureIndexCacheResult.Ready(entry.Builder,index);
        }
    }
}
