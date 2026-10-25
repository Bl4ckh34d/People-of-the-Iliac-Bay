using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationFeatureIndexTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Shared conversation feature extraction preserves words, stems, concepts and stop words",()=>{
                var features=ConversationFeatureExtractor.Extract("My mare hid the parcel in a tavern.");
                Check.True(features.Contains("w:mare") && features.Contains("s:mar") && features.Contains("c:horse"));
                Check.True(features.Contains("c:parcel") && features.Contains("c:inn"));
                Check.True(!features.Contains("w:my") && !features.Contains("w:the"));
            }));

            cases.Add(("Compact feature index matches scan ranking at historical cutoffs",()=>{
                var entries=Fixture();var index=new ConversationFeatureIndex(entries);
                foreach(double now in new[]{150d,250d,500d})
                    foreach(string query in new[]{"What was my mount's name?","What was it?","Where was the parcel hidden?","Was it in the tavern?"})
                        AssertScanParity(entries,index,query,now);
            }));

            cases.Add(("Unavailable fallback text is never indexed as NPC speech",()=>{
                var store=new WorldStateStore(19);
                store.RememberConversation("alice",100,"winter","Inn","I need some help.",
                    "A moonstone-whale is mentioned only by the unavailable fallback.","unavailable");
                store.RememberConversation("alice",200,"winter","Inn","The road is open.",
                    "The bridge is clear.","model");
                var entries=store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries;
                var index=new ConversationFeatureIndex(entries);
                int first=index.Rank("Tell me about the moonstone-whale.",500)[0];
                Check.True(index.EntryAt(first).Source=="model",
                    "A failure fallback must not outrank recorded NPC speech for words found only in that fallback.");
            }));

            cases.Add(("Indexed recall matches complete scan output across cutoffs, budgets, observations and paraphrases",()=>{
                var indexedMethod=typeof(WorldStateStore).GetMethod("RecallConversationIndexedForTesting",
                    System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                Check.True(indexedMethod!=null,"A full-output indexed recall oracle seam is missing.");
                var store=new WorldStateStore(19);
                store.RememberConversation("alice",10,"winter date","Black Wolf Inn",
                    "I lost my silver lute beneath the old mill. I hid my brass parcel in the tavern cellar. " +
                    "The lute has a cracked blue bridge and a small maker's mark near the tuning peg.",
                    "I will remember your lute and parcel. The blue bridge may help identify it later.","native");
                store.RememberConversation("alice",20,"spring date","Street",
                    "Today I saw a bird near the gate. What did you hear about the storm?",
                    "The northern road flooded after heavy rain. The water reached the market steps.","model");
                store.RememberObservation("alice",25,"spring date","Old Mill","Met and spoke with the player here.");
                store.RememberObservation("alice",26,"spring date","Old Mill","Conversation started.");
                store.RememberConversation("alice",40,"summer date","Tavern",
                    "The parcel was moved from the cellar to a locked room behind the kitchen. " +
                    "Its seal bears three stars, and the innkeeper saw the courier leave before dawn.",
                    "I saw the courier carry it away. I do not know who received it.","action");
                store.RememberConversation("alice",500,"future date","Future place",
                    "My silver lute is now called FutureSecret and it sits in the future archive.",
                    "This later fact must not alter an earlier recall.","model");
                var entries=store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries;
                var index=new ConversationFeatureIndex(entries);
                string[] queries={"Where did I put the small package?","What was its maker's mark?",
                    "What was it?","What happened to it?","What did I ask about the flood?","Where was the parcel after the cellar?"};
                double[] cutoffs={15,25,39,40,100,499,500,200000};
                int[] budgets={256,599,600,699,700,800,1000,1100,1400,1500,2000,2400,5000,200000};
                foreach(string query in queries)foreach(double now in cutoffs)foreach(int budget in budgets)
                {
                    string scan=store.RecallConversation("alice",query,now,budget);
                    string indexed=(string)indexedMethod.Invoke(null,new object[]{entries,query,now,budget,index});
                    Check.True(scan==indexed,"Full recalled text diverged at query='"+query+"', time="+now+", budget="+budget+".");
                }
                Check.True(store.RecallConversation("unknown","What did I say?",100,256).StartsWith("MEMORY ABSENT:"));
                string absent=(string)indexedMethod.Invoke(null,new object[]{new List<NpcMemoryEntry>(),"What did I say?",100,256,
                    new ConversationFeatureIndex(new List<NpcMemoryEntry>())});
                Check.Equal(store.RecallConversation("unknown","What did I say?",100,256),absent);
            }));

            cases.Add(("World-state recall uses only a ready active-person index and preserves the scan output",new Action(()=>{
                var store=new WorldStateStore(23);
                for(int i=0;i<9;i++)store.RememberConversation("alice",10+i,"winter","Inn",
                    i==2?"I hid the silver brooch beneath the old mill.":"I saw bird number "+i+" by the town wall.",
                    i==2?"I will remember the brooch was beneath the mill.":"The gate was blue.","model");
                store.RememberObservation("alice",25,"spring","Mill","Met and spoke with the player here.");
                string[] queries={"Where did I leave my silver pin?","What happened there?","What did I say about birds?"};
                string before=store.RecallConversation("alice",queries[0],30,1100);
                Check.True(!store.IsConversationRecallIndexReady("alice"),"A recall query created an index synchronously.");
                for(int attempt=0;attempt<200&&!store.IsConversationRecallIndexReady("alice");attempt++)
                    store.AdvanceConversationRecallWarmup("alice",2,100);
                Check.True(store.IsConversationRecallIndexReady("alice"),"Active-person cooperative warmup did not complete.");
                Check.True(before==store.RecallConversation("alice",queries[0],30,1100),"Ready index changed the full public recall string.");
                foreach(string query in queries.Skip(1))
                    Check.True(WorldStateStore.RecallConversationIndexedForTesting(
                        store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries,query,30,1100,
                        new ConversationFeatureIndex(store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries))==
                        store.RecallConversation("alice",query,30,1100),"Ready index changed the full public recall string.");
                Check.True(!store.IsConversationRecallIndexReady("bob"),"Recall warmup indexed a person who was not active.");
            })));

            cases.Add(("Ready recall index follows exact append and restore branch identity",new Action(()=>{
                var store=new WorldStateStore(29);
                store.RememberConversation("alice",10,"winter","Inn","My horse is Copper.","I will remember Copper.","model");
                WarmUntilReady(store,"alice");
                var checkpoint=store.Snapshot();
                store.RememberConversation("alice",20,"spring","Stable","My horse is now called Ash.","I will remember Ash.","native");
                Check.True(store.IsConversationRecallIndexReady("alice"),"Exact append discarded a completed index instead of updating its delta.");
                Check.Equal(store.RecallConversation("alice","What is my horse called?",30,1200),
                    WorldStateStore.RecallConversationIndexedForTesting(
                        store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries,
                        "What is my horse called?",30,1200,new ConversationFeatureIndex(
                            store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries)));
                WarmUntilReady(store,"alice");
                string branch=store.RecallConversation("alice","What is my horse called?",30,1200);
                Check.True(branch.Contains("Ash"));
                store.Restore(checkpoint);
                Check.True(!store.IsConversationRecallIndexReady("alice"),"Restore retained an index from the discarded branch.");
                WarmUntilReady(store,"alice");
                string restored=store.RecallConversation("alice","What is my horse called?",30,1200);
                Check.True(restored.Contains("Copper")&&!restored.Contains("Ash"),"Restored recall leaked a discarded branch fact.");
                Check.Equal(restored,WorldStateStore.RecallConversationIndexedForTesting(
                    store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries,
                    "What is my horse called?",30,1200,new ConversationFeatureIndex(
                        store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries)));
            })));

            cases.Add(("Ready public warmup performs no recurring cache or LRU allocations",new Action(()=>{
                var store=new WorldStateStore(31);
                for(int i=0;i<12;i++)store.RememberConversation("alice",10+i,"winter","Inn",
                    "I saw bird number "+i+" near the town wall.","The gate was blue.","model");
                WarmUntilReady(store,"alice");
                for(int i=0;i<128;i++)store.AdvanceConversationRecallWarmup("alice",32,2);
                long before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<1000;i++)store.AdvanceConversationRecallWarmup("alice",32,2);
                long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                Console.WriteLine("CONVERSATION_READY_WARMUP_ALLOCATION: calls=1000; allocated_bytes="+allocated);
                Check.True(allocated<=256,"Ready update hook allocated "+allocated+" bytes over 1,000 calls.");
            })));

            cases.Add(("Warm indexed recall preserves full output while measuring scan and index costs",new Action(()=>{
                var store=new WorldStateStore(21);
                for(int i=0;i<1000;i++)store.RememberConversation("alice",i+1,"winter date","Black Wolf Inn",
                    "I saw bird number "+i+" near the town wall; my silver parcel was beneath the old mill.",
                    "The bird found a quiet place by the blue gate; I will remember parcel "+i+".",i%2==0?"native":"model");
                var entries=store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries;
                var index=new ConversationFeatureIndex(entries);
                const string query="Where did I leave the silver package?";
                string scan=store.RecallConversation("alice",query,1001,2000);
                string indexed=WorldStateStore.RecallConversationIndexedForTesting(entries,query,1001,2000,index);
                Check.Equal(scan,indexed);
                long allocationBefore=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
                for(int i=0;i<8;i++)store.RecallConversation("alice",query,1001,2000);
                watch.Stop();double scanMs=watch.Elapsed.TotalMilliseconds;
                long scanAllocation=GC.GetAllocatedBytesForCurrentThread()-allocationBefore;
                allocationBefore=GC.GetAllocatedBytesForCurrentThread();watch.Restart();
                for(int i=0;i<8;i++)WorldStateStore.RecallConversationIndexedForTesting(entries,query,1001,2000,index);
                watch.Stop();double indexedMs=watch.Elapsed.TotalMilliseconds;
                long indexedAllocation=GC.GetAllocatedBytesForCurrentThread()-allocationBefore;
                Console.WriteLine("CONVERSATION_RECALL_PARITY_BENCH: entries=1000; repeats=8; scan_ms="+
                    scanMs.ToString("F3",CultureInfo.InvariantCulture)+"; indexed_ms="+
                    indexedMs.ToString("F3",CultureInfo.InvariantCulture)+"; scan_allocated_bytes="+
                    scanAllocation+"; indexed_allocated_bytes="+indexedAllocation+"; index_owned_bytes="+index.OwnedBytes);
            })));

            cases.Add(("Future-only entries cannot change indexed historical IDF ranking",()=>{
                var entries=new List<NpcMemoryEntry>{
                    Entry(10,"My parcel stayed in the tavern.","It was kept overnight.","Black Wolf Inn"),
                    Entry(20,"The hidden parcel was beneath the old mill.","I left it there.","Old Mill"),
                    Entry(200,"The hidden ruby parcel is in the tavern.","The hidden parcel remained there.","Tavern")};
                var index=new ConversationFeatureIndex(entries);
                AssertScanParity(entries,index,"Where is the hidden parcel?",100);
                var before=index.Rank("Where is the hidden parcel?",100);
                entries.Add(Entry(201,"The hidden parcel is in the tavern.","The parcel is safe.","Tavern"));
                index.Append(entries[3],3);
                var after=index.Rank("Where is the hidden parcel?",100);
                Check.True(before.SequenceEqual(after),"A future append changed an earlier query's IDF or ranking.");
            }));

            cases.Add(("Append deltas preserve ranking for same-time and out-of-order memories",()=>{
                var entries=Fixture().Where(e=>e.At<=250).ToList();var index=new ConversationFeatureIndex(entries);
                var appended=Entry(125,"My mare is called Juniper.","You named your steed Juniper.","Stable");
                entries.Add(appended);index.Append(appended,entries.Count-1);
                var oldDated=Entry(80,"My parcel was in the cellar.","I kept the package there.","Tavern");
                entries.Add(oldDated);index.Append(oldDated,entries.Count-1);
                AssertScanParity(entries,index,"What did I call my mount?",250);
                AssertScanParity(entries,index,"Where was the hidden parcel?",250);
            }));

            cases.Add(("Restored branch and another person's index remain isolated",()=>{
                var store=new WorldStateStore(17);
                store.RememberConversation("alice",100,"winter","inn","My horse is Copper.","I will remember Copper.","model");
                var checkpoint=store.Snapshot();
                var baseEntries=checkpoint.ConversationMemories.Single(x=>x.PersonId=="alice").Entries.ToList();
                var baseIndex=new ConversationFeatureIndex(baseEntries);
                store.RememberConversation("alice",110,"spring","inn","My horse is called Ash.","Ash is a fine name.","model");
                var branchEntries=store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries.ToList();
                var branchIndex=new ConversationFeatureIndex(branchEntries);
                Check.True(branchIndex.Rank("What is my mount called?",120).Contains(1));
                store.Restore(checkpoint);
                var restoredEntries=store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="alice").Entries.ToList();
                var restoredIndex=new ConversationFeatureIndex(restoredEntries);
                Check.True(restoredIndex.Rank("What is my mount called?",120).SequenceEqual(baseIndex.Rank("What is my mount called?",120)));
                Check.True(!restoredEntries.Any(x=>x.PlayerText.Contains("Ash")));
                store.RememberConversation("bob",100,"winter","inn","My horse is named Flint.","I will remember Flint.","model");
                var bobEntries=store.Snapshot().ConversationMemories.Single(x=>x.PersonId=="bob").Entries.ToList();
                var bobIndex=new ConversationFeatureIndex(bobEntries);
                Check.True(bobIndex.Rank("What was Alice's horse called?",120).All(i=>!bobEntries[i].PlayerText.Contains("Copper")));
                Check.True(!store.RecallConversation("bob","What was Alice's horse called?",120,1000).Contains("Copper"));
            }));

            cases.Add(("Exact feature comparison survives forced hash collisions",()=>{
                var entries=new List<NpcMemoryEntry>{
                    Entry(1,"My horse is called Copper.","I will remember Copper.","Inn"),
                    Entry(2,"My parcel was hidden in the mill.","I will remember the parcel.","Mill")};
                var colliding=new ConversationFeatureIndex(entries,_=>7UL);
                AssertScanParity(entries,colliding,"Where was my parcel hidden?",10);
                var horse=colliding.Rank("What is my horse called?",10);
                Check.True(horse.Count>0 && horse[0]==0,"Collision merged the horse features with unrelated parcel features.");
            }));

            cases.Add(("Exact compact feature index stays within the 10k owned-byte budget",()=>{
                var entries=new List<NpcMemoryEntry>(10000);
                for(int i=0;i<10000;i++)
                    entries.Add(Entry(i,"I saw bird number "+i+" near the town wall; entry="+i,
                        "The bird found a quiet place by the blue gate; note="+i,"The Black Wolf Inn"));
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();long heapBefore=GC.GetTotalMemory(true);
                long allocationBefore=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
                var index=new ConversationFeatureIndex(entries);
                watch.Stop();long allocation=GC.GetAllocatedBytesForCurrentThread()-allocationBefore;
                long retained=Math.Max(0,GC.GetTotalMemory(true)-heapBefore);
                index.Rank("Where did I hide the ruby parcel?",9999);
                Console.WriteLine("CONVERSATION_INDEX_BENCH: entries=10000; owned_bytes="+index.OwnedBytes+
                    "; retained_heap_delta="+retained+"; allocated_bytes="+allocation+"; cold_ms="+watch.Elapsed.TotalMilliseconds.ToString("F3"));
                Check.True(index.OwnedBytes<=5000000,"Index owns "+index.OwnedBytes+" bytes for 10,000 entries.");
                AssertScanParity(entries,index,"Where did I hide the ruby parcel?",9999);
            }));

            cases.Add(("Cooperative index builder resumes in bounded slices and exposes only complete indexes",new Action(()=>{
                var entries=Fixture().Where(e=>e.At<=250).ToList();object owner=new object();
                var builder=new ConversationFeatureIndexBuilder(entries,owner);int processed=0;
                Check.True(builder.GetCompletedIndex()==null,"Incomplete index became visible.");
                while(!builder.IsComplete&&!builder.IsCancelled&&!builder.IsRejected)
                {
                    int slice=builder.Advance(owner,2,long.MaxValue);
                    Check.True(slice>=0&&slice<=2,"Builder exceeded its per-call record limit.");
                    processed+=slice;
                    if(!builder.IsComplete)Check.True(builder.GetCompletedIndex()==null,"Partial index escaped during a resumed build.");
                }
                Check.True(builder.IsComplete&&!builder.IsCancelled&&!builder.IsRejected);
                Check.Equal(entries.Count,processed);Check.Equal(entries.Count,builder.NextEntryIndex);
                AssertScanParity(entries,builder.GetCompletedIndex(),"What was it?",250);
            })));

            cases.Add(("Cooperative index builder honors zero work budgets and cancellation on owner change",new Action(()=>{
                var entries=Fixture();object owner=new object();var builder=new ConversationFeatureIndexBuilder(entries,owner);
                Check.Equal(0,builder.Advance(owner,4,0));Check.Equal(0,builder.NextEntryIndex);
                Check.Equal(1,builder.Advance(owner,1,long.MaxValue));Check.Equal(1,builder.NextEntryIndex);
                Check.Equal(0,builder.Advance(new object(),4,long.MaxValue));
                Check.True(builder.IsCancelled&&!builder.IsComplete);
                Check.True(builder.GetCompletedIndex()==null,"Cancelled partial index remained exposed.");
            })));

            cases.Add(("Empty cooperative build completes and oversized build is discarded",new Action(()=>{
                object owner=new object();var empty=new ConversationFeatureIndexBuilder(new List<NpcMemoryEntry>(),owner);
                Check.Equal(0,empty.Advance(owner,4,long.MaxValue));Check.True(empty.IsComplete);
                Check.Equal(0,empty.GetCompletedIndex().Rank("anything",100).Count);

                var entries=Fixture();var oversized=new ConversationFeatureIndexBuilder(entries,new object(),null,1);
                oversized.Advance(oversized.StateIdentity,entries.Count,long.MaxValue);
                Check.True(oversized.IsRejected&&!oversized.IsComplete);
                Check.True(oversized.GetCompletedIndex()==null,"Over-budget partial index escaped to a caller.");
            })));

            cases.Add(("Completed cooperative index accepts an exact appended journal tail",new Action(()=>{
                var entries=Fixture().Where(e=>e.At<=250).ToList();object owner=new object();
                var builder=new ConversationFeatureIndexBuilder(entries,owner);
                while(!builder.IsComplete&&!builder.IsRejected)
                    builder.Advance(owner,3,long.MaxValue);
                Check.True(builder.IsComplete);
                var index=builder.GetCompletedIndex();
                var appended=Entry(125,"My mare is called Juniper.","You named your steed Juniper.","Stable");
                entries.Add(appended);index.Append(appended,entries.Count-1);
                AssertScanParity(entries,index,"What did I call my mount?",250);
            })));

            cases.Add(("Cooperative index builder stays within its slice record cap over 10k memories",new Action(()=>{
                var entries=new List<NpcMemoryEntry>(10000);
                for(int i=0;i<10000;i++)
                    entries.Add(Entry(i,"I saw bird number "+i+" near the town wall; entry="+i,
                        "The bird found a quiet place by the blue gate; note="+i,"The Black Wolf Inn"));
                object owner=new object();var builder=new ConversationFeatureIndexBuilder(entries,owner);
                long maxElapsed=0,maxAllocated=0,totalAllocated=0;int slices=0,processed=0;
                long budgetTicks=Math.Max(1,Stopwatch.Frequency*2/1000);
                while(!builder.IsComplete&&!builder.IsRejected&&!builder.IsCancelled)
                {
                    long allocBefore=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
                    int count=builder.Advance(owner,64,budgetTicks);watch.Stop();
                    long allocated=GC.GetAllocatedBytesForCurrentThread()-allocBefore;
                    Check.True(count>=0&&count<=64,"Builder exceeded 64 entries in a slice.");
                    if(count==0&&!builder.IsComplete&&builder.NextEntryIndex<builder.EntryCount)
                        throw new InvalidOperationException("Positive budget made no progress through journal entries.");
                    maxElapsed=Math.Max(maxElapsed,watch.ElapsedTicks);maxAllocated=Math.Max(maxAllocated,allocated);
                    totalAllocated+=allocated;processed+=count;slices++;
                }
                Check.True(builder.IsComplete&&!builder.IsRejected&&!builder.IsCancelled,
                    "Builder did not complete: rejected="+builder.IsRejected+" cancelled="+builder.IsCancelled+
                    " cursor="+builder.NextEntryIndex+"/"+builder.EntryCount);
                Check.Equal(entries.Count,processed);Check.Equal(entries.Count,builder.NextEntryIndex);
                Console.WriteLine("CONVERSATION_INDEX_SLICE_BENCH: entries=10000; slices="+slices+
                    "; budget_ms=2.000; max_slice_ms="+(maxElapsed*1000.0/Stopwatch.Frequency).ToString("F3",CultureInfo.InvariantCulture)+
                    "; max_slice_allocated_bytes="+maxAllocated+"; total_allocated_bytes="+totalAllocated+
                    "; owned_bytes="+builder.GetCompletedIndex().OwnedBytes);
                Check.True(builder.GetCompletedIndex().OwnedBytes<=5000000);
                AssertScanParity(entries,builder.GetCompletedIndex(),"Where did I hide the ruby parcel?",9999);
            })));

            cases.Add(("Deferred builder startup allocation is independent of journal length",()=>{
                var entries=LargeFixture(10000);long before=GC.GetAllocatedBytesForCurrentThread();
                var builder=new ConversationFeatureIndexBuilder(entries,new object());
                long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
                Console.WriteLine("CONVERSATION_INDEX_STARTUP_BENCH: entries=10000; owned_bytes="+builder.OwnedBytes+
                    "; allocated_bytes="+allocated);
                Check.True(builder.OwnedBytes<500000&&allocated<500000,
                    "Deferred warmup allocated storage proportional to the whole journal before its first slice.");
            }));

            cases.Add(("Conversation index cache enforces LRU people cap and cancels an evicted builder",new Action(()=>{
                var cache=new ConversationFeatureIndexCache(4,20000000);object state=new object();
                var journals=new List<NpcMemoryEntry>[4];
                ConversationFeatureIndexCacheResult firstResult=null;
                for(int i=0;i<4;i++)
                {journals[i]=Fixture();var built=BuildCached(cache,"person-"+i,journals[i],state);if(i==0)firstResult=built;}
                Check.True(cache.GetOrCreate("person-1",journals[1],state).Status==ConversationFeatureIndexCacheStatus.Ready);
                var fifth=BuildCached(cache,"person-4",Fixture(),state);
                Check.True(fifth.Status==ConversationFeatureIndexCacheStatus.Ready);
                Check.Equal(4,cache.PersonCount);Check.True(cache.ContainsPerson("person-1"));
                Check.True(!cache.ContainsPerson("person-0"),"Least recently used person was retained beyond the four-person cap.");
                Check.True(firstResult.Builder.IsCancelled&&!firstResult.Builder.IsComplete);
                AssertScanParity(journals[0],firstResult.Index,"What was my horse called?",100);

                var oneSlot=new ConversationFeatureIndexCache(1,20000000);object otherState=new object();
                var first=oneSlot.GetOrCreate("first",Fixture(),otherState);
                oneSlot.Advance("first",otherState,1,long.MaxValue);
                var second=oneSlot.GetOrCreate("second",Fixture(),otherState);
                Check.True(second.Status==ConversationFeatureIndexCacheStatus.Building);
                Check.True(first.Builder.IsCancelled&&!first.Builder.IsComplete,"Eviction left an incomplete builder live.");
                Check.True(first.Builder.GetCompletedIndex()==null);
            })));

            cases.Add(("Conversation index cache obeys aggregate byte cap and makes rejection explicit",new Action(()=>{
                var cache=new ConversationFeatureIndexCache(4,10000000);object state=new object();
                var large=LargeFixture(10000);var first=BuildCached(cache,"large-a",large,state);
                Check.True(first.Status==ConversationFeatureIndexCacheStatus.Ready);
                Check.True(cache.OwnedBytes<=10000000);
                var second=cache.GetOrCreate("large-b",LargeFixture(10000),state);
                while(second.Status==ConversationFeatureIndexCacheStatus.Building)
                {
                    second=cache.Advance("large-b",state,64,Stopwatch.Frequency*2/1000);
                    Check.True(cache.OwnedBytes<=10000000,"Cache byte cap was exceeded during a cooperative build slice.");
                }
                Check.True(second.Status==ConversationFeatureIndexCacheStatus.Ready);
                second.Index.Rank("Where did I hide the ruby parcel?",9999);
                Check.True(cache.OwnedBytes<=10000000,"Cache byte total exceeded its configured cap after a slice.");
                Check.True(!cache.ContainsPerson("large-a"),"Byte pressure did not evict the least recently used index.");

                var tinyLimit=new ConversationFeatureIndexCache(4,20000000,1);var tinyState=new object();
                var rejected=tinyLimit.GetOrCreate("too-large",Fixture(),tinyState);
                while(rejected.Status==ConversationFeatureIndexCacheStatus.Building)
                    rejected=tinyLimit.Advance("too-large",tinyState,20,long.MaxValue);
                Check.True(rejected.Status==ConversationFeatureIndexCacheStatus.UseScanFallback);
                Check.True(rejected.Index==null&&!tinyLimit.ContainsPerson("too-large"));
            })));

            cases.Add(("Rejected conversation index is not rebuilt until journal generation changes",new Action(()=>{
                var cache=new ConversationFeatureIndexCache(2,20000000,1);object state=new object();
                var entries=Fixture();var first=cache.GetOrCreate("alice",entries,state);
                while(first.Status==ConversationFeatureIndexCacheStatus.Building)
                    first=cache.Advance("alice",state,64,long.MaxValue);
                Check.True(first.Status==ConversationFeatureIndexCacheStatus.UseScanFallback);
                Check.True(!cache.ContainsPerson("alice"));
                var repeated=cache.GetOrCreate("alice",entries,state);
                Check.True(repeated.Status==ConversationFeatureIndexCacheStatus.UseScanFallback,
                    "Unchanged rejected journal recreated a builder instead of staying on scan fallback.");
                Check.True(repeated.Builder==null);

                var appended=Entry(999,"I found a new note at the fountain.","I will remember it.","Street");
                entries.Add(appended);
                Check.True(!cache.Append("alice",appended,entries.Count-1,state));
                var changedCount=cache.GetOrCreate("alice",entries,state);
                Check.True(changedCount.Status==ConversationFeatureIndexCacheStatus.Building,
                    "A real append did not invalidate rejection for the old journal generation.");

                var rejectedAgain=cache.Advance("alice",state,64,long.MaxValue);
                Check.True(rejectedAgain.Status==ConversationFeatureIndexCacheStatus.UseScanFallback);
                var replacement=Fixture();
                Check.True(cache.GetOrCreate("alice",replacement,state).Status==ConversationFeatureIndexCacheStatus.Building,
                    "A replacement journal with the same count inherited stale rejection.");
                var restoredState=new object();
                Check.True(cache.GetOrCreate("alice",Fixture(),restoredState).Status==ConversationFeatureIndexCacheStatus.Building,
                    "A restored state inherited rejection from the discarded branch.");
            })));

            cases.Add(("Rejected-index tombstones use a bounded LRU",new Action(()=>{
                var cache=new ConversationFeatureIndexCache(2,20000000,1);object state=new object();
                var a=Fixture();var b=Fixture();var c=Fixture();
                Check.True(BuildCached(cache,"a",a,state).Status==ConversationFeatureIndexCacheStatus.UseScanFallback);
                Check.True(BuildCached(cache,"b",b,state).Status==ConversationFeatureIndexCacheStatus.UseScanFallback);
                Check.True(cache.GetOrCreate("a",a,state).Status==ConversationFeatureIndexCacheStatus.UseScanFallback,
                    "Repeated fallback should refresh that tombstone's LRU position.");
                Check.True(BuildCached(cache,"c",c,state).Status==ConversationFeatureIndexCacheStatus.UseScanFallback);
                Check.True(cache.GetOrCreate("a",a,state).Status==ConversationFeatureIndexCacheStatus.UseScanFallback,
                    "Recently used rejection was evicted before the LRU entry.");
                Check.True(cache.GetOrCreate("b",b,state).Status==ConversationFeatureIndexCacheStatus.Building,
                    "Tombstone cache exceeded its configured two-person LRU cap.");
            })));

            cases.Add(("Conversation index cache restore identity clears every branch and prevents old-fact reuse",new Action(()=>{
                var cache=new ConversationFeatureIndexCache();object oldState=new object();
                var oldEntries=new List<NpcMemoryEntry>{Entry(1,"My horse is Copper.","I will remember Copper.","Inn")};
                var old=BuildCached(cache,"alice",oldEntries,oldState);
                Check.True(old.Status==ConversationFeatureIndexCacheStatus.Ready);
                var bob=cache.GetOrCreate("bob",Fixture(),oldState);
                object restoredState=new object();
                var restoredEntries=new List<NpcMemoryEntry>{Entry(2,"My horse is Ash.","I will remember Ash.","Inn")};
                var restored=cache.GetOrCreate("alice",restoredEntries,restoredState);
                Check.True(old.Builder.IsCancelled&&bob.Builder.IsCancelled,"Changing restore identity left stale cache builders published.");
                Check.Equal(1,cache.PersonCount);Check.True(!cache.ContainsPerson("bob"));
                while(restored.Status==ConversationFeatureIndexCacheStatus.Building)
                    restored=cache.Advance("alice",restoredState,10,long.MaxValue);
                Check.True(restored.Status==ConversationFeatureIndexCacheStatus.Ready);
                var ranked=restored.Index.Rank("What is my horse called?",10);
                Check.True(ranked.Count>0&&restoredEntries[ranked[0]].PlayerText.Contains("Ash"));
                Check.True(!restoredEntries.Any(x=>x.PlayerText.Contains("Copper")));
            })));

            cases.Add(("Appending a journal tail updates only that cached person",new Action(()=>{
                var cache=new ConversationFeatureIndexCache();object state=new object();
                var aliceEntries=new List<NpcMemoryEntry>{Entry(1,"My horse is Copper.","Copper.","Inn")};
                var bobEntries=new List<NpcMemoryEntry>{Entry(1,"My horse is Flint.","Flint.","Inn")};
                var alice=BuildCached(cache,"alice",aliceEntries,state);var bob=BuildCached(cache,"bob",bobEntries,state);
                var oldBob=bob.Index.Rank("What is my horse called?",10);
                var appended=Entry(2,"My horse is Juniper.","Juniper.","Stable");aliceEntries.Add(appended);
                Check.True(cache.Append("alice",appended,aliceEntries.Count-1,state));
                var updated=cache.GetOrCreate("alice",aliceEntries,state);
                Check.True(updated.Status==ConversationFeatureIndexCacheStatus.Ready);
                Check.True(updated.Index.Rank("What is my horse called?",10).Contains(1));
                Check.True(bobEntries.Count==1&&bob.Index.Rank("What is my horse called?",10).SequenceEqual(oldBob));

                var charlieEntries=Fixture();var pending=cache.GetOrCreate("charlie",charlieEntries,state);
                cache.Advance("charlie",state,1,long.MaxValue);
                var tail=Entry(300,"A late bird crossed the wall.","It flew east.","Street");charlieEntries.Add(tail);
                Check.True(!cache.Append("charlie",tail,charlieEntries.Count-1,state));
                Check.True(pending.Builder.IsCancelled&&pending.Builder.GetCompletedIndex()==null,
                    "Appending during a partial build left stale builder state live.");
                Check.True(cache.ContainsPerson("alice")&&cache.ContainsPerson("bob"),
                    "Cancelling one person's changed journal invalidated another person's cache.");
            })));

            cases.Add(("Conversation index keeps a stable source snapshot across prior-row edits",new Action(()=>{
                var source=Fixture();var expected=source.Select(CloneForTest).ToList();
                var index=new ConversationFeatureIndex(source);
                source[0].At=900;source[0].PlayerText="My horse is Jade.";source[0].NpcText="Jade.";
                AssertScanParity(expected,index,"What is my horse called?",15);
                AssertScanParity(expected,index,"What is the hidden parcel?",40);
                Check.Throws<ArgumentOutOfRangeException>(()=>index.Rank("anything",double.NaN));
                Check.Throws<ArgumentOutOfRangeException>(()=>index.Rank("anything",double.PositiveInfinity));
                Check.Throws<ArgumentOutOfRangeException>(()=>index.Rank("anything",-1));
            })));

            cases.Add(("Pending builder snapshots processed rows and cancels when journal shape drifts",new Action(()=>{
                var source=Fixture();var expected=source.Select(CloneForTest).ToList();object state=new object();
                var builder=new ConversationFeatureIndexBuilder(source,state);
                Check.Equal(1,builder.Advance(state,1,long.MaxValue));
                source[0].At=900;source[0].PlayerText="My horse is Jade.";
                source.Add(Entry(300,"I saw a later bird.","Blue wings.","Street"));
                Check.Equal(0,builder.Advance(state,1,long.MaxValue));
                Check.True(builder.IsCancelled&&builder.GetCompletedIndex()==null);

                source=Fixture();expected=source.Select(CloneForTest).ToList();builder=new ConversationFeatureIndexBuilder(source,state);
                Check.Equal(1,builder.Advance(state,1,long.MaxValue));
                source[0].At=900;source[0].PlayerText="My horse is Jade.";
                while(!builder.IsComplete&&!builder.IsRejected&&!builder.IsCancelled)builder.Advance(state,2,long.MaxValue);
                Check.True(builder.IsComplete);
                AssertScanParity(expected,builder.GetCompletedIndex(),"What was the horse called?",15);

                source=Fixture();expected=source.Select(CloneForTest).ToList();builder=new ConversationFeatureIndexBuilder(source,state);
                while(!builder.IsComplete&&!builder.IsRejected&&!builder.IsCancelled)builder.Advance(state,2,long.MaxValue);
                Check.True(builder.IsComplete);
                source[0].At=900;source[0].PlayerText="My horse is Jade.";
                AssertScanParity(expected,builder.GetCompletedIndex(),"What was the horse called?",15);
            })));

            cases.Add(("Conversation index rejects invalid timestamps and preserves cutoff validation",new Action(()=>{
                var invalid=new List<NpcMemoryEntry>{Entry(double.NaN,"My horse is Copper.","Copper.","Inn")};
                Check.Throws<ArgumentException>(()=>new ConversationFeatureIndex(invalid));
                object state=new object();var builder=new ConversationFeatureIndexBuilder(invalid,state);
                Check.Equal(0,builder.Advance(state,1,long.MaxValue));
                Check.True(builder.IsRejected&&builder.GetCompletedIndex()==null);
                var cache=new ConversationFeatureIndexCache();var rejected=cache.GetOrCreate("alice",invalid,new object());
                while(rejected.Status==ConversationFeatureIndexCacheStatus.Building)
                    rejected=cache.Advance("alice",rejected.Builder.StateIdentity,1,long.MaxValue);
                Check.True(rejected.Status==ConversationFeatureIndexCacheStatus.UseScanFallback);
            })));

            cases.Add(("Same-state replacement journal invalidates the prior cached source",new Action(()=>{
                var cache=new ConversationFeatureIndexCache();object state=new object();
                var first=BuildCached(cache,"alice",new List<NpcMemoryEntry>{Entry(1,"My horse is Copper.","Copper.","Inn")},state);
                var replacement=new List<NpcMemoryEntry>{Entry(2,"My horse is Ash.","Ash.","Inn")};
                var second=cache.GetOrCreate("alice",replacement,state);
                Check.True(first.Status==ConversationFeatureIndexCacheStatus.Ready);
                Check.True(first.Builder.IsCancelled&&!first.Builder.IsComplete);
                while(second.Status==ConversationFeatureIndexCacheStatus.Building)
                    second=cache.Advance("alice",state,10,long.MaxValue);
                Check.True(second.Status==ConversationFeatureIndexCacheStatus.Ready);
                var result=second.Index.Rank("What is my horse called?",10);
                Check.True(result.Count>0&&replacement[result[0]].PlayerText.Contains("Ash"));
            })));

            cases.Add(("Cached row edits require exact-person invalidation before rebuilding the snapshot",new Action(()=>{
                var cache=new ConversationFeatureIndexCache();object state=new object();
                var entries=new List<NpcMemoryEntry>{Entry(1,"My horse is Copper.","Copper.","Inn")};
                var original=BuildCached(cache,"alice",entries,state);
                entries[0].At=900;entries[0].PlayerText="My horse is Jade.";
                var stillSnapshot=cache.GetOrCreate("alice",entries,state);
                Check.True(stillSnapshot.Status==ConversationFeatureIndexCacheStatus.Ready);
                Check.True(original.Index.Rank("What is my horse called?",10).Contains(0),
                    "Mutating a source row silently changed the already-published index's time ordering.");
                Check.True(cache.InvalidatePerson("alice",state));
                Check.True(original.Builder.IsCancelled&&!original.Builder.IsComplete);
                Check.True(original.Index.Rank("What is my horse called?",10).Contains(0),
                    "Invalidating cache ownership mutated an already-published complete index.");
                var rebuilt=cache.GetOrCreate("alice",entries,state);
                while(rebuilt.Status==ConversationFeatureIndexCacheStatus.Building)
                    rebuilt=cache.Advance("alice",state,10,long.MaxValue);
                Check.True(rebuilt.Status==ConversationFeatureIndexCacheStatus.Ready);
                var ranked=rebuilt.Index.Rank("What is my horse called?",10);
                Check.True(ranked.Count==0,"Explicit invalidation rebuilt from an out-of-cutoff timestamp or old text.");
                Check.True(rebuilt.Index.Rank("What is my horse called?",1000).Count>0);

                var pendingEntries=Fixture();var pending=cache.GetOrCreate("bob",pendingEntries,state);
                cache.Advance("bob",state,1,long.MaxValue);
                pendingEntries[0].At=800;pendingEntries[0].PlayerText="My horse is Emerald.";
                Check.True(cache.InvalidatePerson("bob",state));
                Check.True(pending.Builder.IsCancelled&&pending.Builder.GetCompletedIndex()==null,
                    "Exact-person invalidation failed to cancel an in-flight stale snapshot.");
            })));
        }

        private static void AssertScanParity(IList<NpcMemoryEntry> entries,ConversationFeatureIndex index,string query,double now)
        {
            var eligible=entries.Select((entry,position)=>new{Entry=entry,Position=position})
                .Where(x=>x.Entry.At<=now && x.Entry.Observation!="Conversation started.").ToList();
            var scan=new ConversationRetrieval(eligible.Select(x=>x.Entry).ToList(),query);
            var expected=eligible.Select(x=>new{Index=x.Position,At=x.Entry.At,Score=scan.Score(x.Entry)})
                .OrderByDescending(x=>x.Score).ThenByDescending(x=>x.At).ThenByDescending(x=>x.Index)
                .Select(x=>x.Index).ToList();
            var actual=index.Rank(query,now);
            Check.True(expected.SequenceEqual(actual),"Scan/index rank differs for query '"+query+"' at "+now+". expected="+
                string.Join(",",expected.Take(12))+" actual="+string.Join(",",actual.Take(12)));
        }

        private static List<NpcMemoryEntry> Fixture()
        {
            return new List<NpcMemoryEntry>{
                Entry(10,"My horse is called Copper.","I will remember your mount.","Black Wolf Inn"),
                Entry(20,"Today I saw a bird by the wall.","The gate is blue.","Street"),
                Entry(30,"The hidden parcel stayed beneath the old mill.","I put it there.","Old Mill"),
                new NpcMemoryEntry{At=35,Date="winter",Place="Inn",PlayerText="",NpcText="",Observation="Met and spoke with the player here.",Source="observed"},
                new NpcMemoryEntry{At=40,Date="winter",Place="Inn",PlayerText="",NpcText="",Observation="Conversation started.",Source="observed"},
                Entry(200,"The hidden ruby parcel is in the tavern.","It is safe in the tavern.","Tavern")
            };
        }

        private static NpcMemoryEntry Entry(double at,string player,string npc,string place)
        {return new NpcMemoryEntry{At=at,Date="winter",Place=place,PlayerText=player,NpcText=npc,Observation="",Source="model"};}

        private static NpcMemoryEntry CloneForTest(NpcMemoryEntry entry)
        {return new NpcMemoryEntry{At=entry.At,Date=entry.Date,Place=entry.Place,PlayerText=entry.PlayerText,
            NpcText=entry.NpcText,Observation=entry.Observation,Source=entry.Source};}

        private static ConversationFeatureIndexCacheResult BuildCached(ConversationFeatureIndexCache cache,
            string person,List<NpcMemoryEntry> entries,object state)
        {
            var result=cache.GetOrCreate(person,entries,state);
            while(result.Status==ConversationFeatureIndexCacheStatus.Building)
                result=cache.Advance(person,state,64,long.MaxValue);
            return result;
        }

        private static void WarmUntilReady(WorldStateStore store,string person)
        {
            for(int attempt=0;attempt<100&&!store.IsConversationRecallIndexReady(person);attempt++)
                store.AdvanceConversationRecallWarmup(person,64,2);
            Check.True(store.IsConversationRecallIndexReady(person),"Index did not become ready within the bounded test warmup.");
        }

        private static List<NpcMemoryEntry> LargeFixture(int count)
        {
            var entries=new List<NpcMemoryEntry>(count);
            for(int i=0;i<count;i++)entries.Add(Entry(i,"I saw bird number "+i+" near the town wall; entry="+i,
                "The bird found a quiet place by the blue gate; note="+i,"The Black Wolf Inn"));
            return entries;
        }
    }
}
