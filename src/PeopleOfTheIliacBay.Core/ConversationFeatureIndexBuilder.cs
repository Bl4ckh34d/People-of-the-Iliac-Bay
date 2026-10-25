using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeopleOfTheIliacBay.Core
{
    // Resumable, per-owner builder. The mutable index is never returned until
    // every captured journal entry has been indexed and the flat postings are
    // finalized. Callers provide both a record cap and a Stopwatch-tick budget.
    internal sealed class ConversationFeatureIndexBuilder
    {
        private readonly IList<NpcMemoryEntry> journal;
        private readonly int expectedCount;
        private readonly object stateIdentity;
        private readonly long maximumOwnedBytes;
        private readonly long maximumBuildBytes;
        private ConversationFeatureIndex index;
        private int nextEntryIndex;

        internal ConversationFeatureIndexBuilder(IList<NpcMemoryEntry> journal,object stateIdentity,
            Func<string,ulong> hashForTesting=null,long maximumOwnedBytes=5000000)
        {
            if(journal==null)throw new ArgumentNullException("journal");
            if(stateIdentity==null)throw new ArgumentNullException("stateIdentity");
            if(maximumOwnedBytes<0)throw new ArgumentOutOfRangeException("maximumOwnedBytes");
            this.journal=journal;this.stateIdentity=stateIdentity;this.maximumOwnedBytes=maximumOwnedBytes;
            maximumBuildBytes=maximumOwnedBytes>long.MaxValue/2?long.MaxValue:maximumOwnedBytes*2L;
            expectedCount=journal.Count;
            index=new ConversationFeatureIndex(journal,hashForTesting,true,expectedCount);
        }

        internal object StateIdentity { get { return stateIdentity; } }
        internal int NextEntryIndex { get { return nextEntryIndex; } }
        internal int EntryCount { get { return expectedCount; } }
        internal long OwnedBytes { get { return index==null?0:index.OwnedBytes; } }
        internal bool IsComplete { get; private set; }
        internal bool IsCancelled { get; private set; }
        internal bool IsRejected { get; private set; }

        internal ConversationFeatureIndex GetCompletedIndex()
        {return IsComplete&&!IsCancelled&&!IsRejected?index:null;}

        // Elapsed budget is in Stopwatch ticks. One journal row is the smallest
        // preemptible unit, so one row may exceed the time budget by itself.
        internal int Advance(object currentStateIdentity,int maximumEntries,long maximumElapsedTicks)
        {
            if(IsComplete||IsCancelled||IsRejected)return 0;
            if(!ReferenceEquals(currentStateIdentity,stateIdentity)||journal.Count!=expectedCount)
            {Cancel();return 0;}
            if(maximumEntries<=0||maximumElapsedTicks<=0)return 0;

            long started=Stopwatch.GetTimestamp();int processed=0;
            while(nextEntryIndex<expectedCount&&processed<maximumEntries)
            {
                try{index.AddJournalEntry(nextEntryIndex);}
                catch(ArgumentException){Reject();return processed;}
                nextEntryIndex++;processed++;
                // Construction holds temporary per-feature lists alongside the
                // packed final postings. Allow bounded scratch headroom, then
                // enforce the retained-index cap after finalization.
                if(index.OwnedBytes>maximumBuildBytes)
                {Reject();return processed;}
                if(Stopwatch.GetTimestamp()-started>=maximumElapsedTicks)break;
            }
            if(nextEntryIndex==expectedCount)
            {
                long remaining=maximumElapsedTicks-(Stopwatch.GetTimestamp()-started);
                if(remaining>0)index.FinishBuildStep(maximumEntries,remaining);
                if(index.IsFinalized)CompleteOrReject();
            }
            return processed;
        }

        private void CompleteOrReject()
        {
            if(index.OwnedBytes>maximumOwnedBytes){Reject();return;}
            IsComplete=true;
        }

        private void Cancel()
        {
            if(index!=null)index.DiscardBuild();
            index=null;IsCancelled=true;
        }

        private void Reject()
        {
            if(index!=null)index.DiscardBuild();
            index=null;IsRejected=true;
        }

        internal void Invalidate()
        {
            // A completed index may already have been published to a caller.
            // Drop our reference without mutating that snapshot; unfinished
            // scratch is private and can be released immediately.
            if(index!=null&&!IsComplete)index.DiscardBuild();
            index=null;IsComplete=false;IsCancelled=true;IsRejected=false;
        }
    }
}
