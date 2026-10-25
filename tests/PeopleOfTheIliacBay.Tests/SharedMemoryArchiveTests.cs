using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SharedMemoryArchiveTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Archive preserves optional action provenance across old and new immutable chunks", () => InDirectory(root => {
                var archive=new SharedConversationArchive(root);
                var old=archive.Commit(null,new[]{Observed("alice",1,"Old observation.",null)});
                var oldCheckpoint=ConversationArchiveCheckpoint.CopyValidated(old);
                string oldPath=ChunkFile(root,oldCheckpoint);
                byte[] oldFile=File.ReadAllBytes(oldPath);
                Check.Equal(0x50494d31,BitConverter.ToInt32(Inflate(oldFile),0));
                const string actionId="action:observed-visit:17";
                var current=archive.Commit(old,new[]{Observed("alice",2,"Action observation.",actionId)});
                Check.True(oldFile.SequenceEqual(File.ReadAllBytes(oldPath)),"Publishing provenance changed immutable parent chunk");
                Check.Equal(0x50494d32,BitConverter.ToInt32(Inflate(File.ReadAllBytes(ChunkFile(root,current))),0));
                var loaded=archive.Load(current).Single().Entries;
                Check.Equal(2,loaded.Count);
                Check.Equal(null,loaded[0].SourceActionId);
                Check.Equal(actionId,loaded[1].SourceActionId);
                var rows=ReadAll(archive.OpenScan(current));
                Check.Equal(actionId,rows[0].Entry.SourceActionId);
                var detached=rows[0].Entry; detached.SourceActionId="forged";
                Check.Equal(actionId,rows[0].Entry.SourceActionId);
                Check.Equal(null,ReadAll(archive.OpenScan(oldCheckpoint)).Single().Entry.SourceActionId);
                var sibling=archive.Commit(oldCheckpoint,new[]{Observed("alice",3,"Sibling action.","action:sibling:18")});
                Check.Equal("action:sibling:18",ReadAll(archive.OpenScan(sibling))[0].Entry.SourceActionId);
                Check.Equal(actionId,ReadAll(archive.OpenScan(current))[0].Entry.SourceActionId);
                var mixed=archive.Commit(current,new[]{Observed("alice",4,"No action source.",null),
                    Observed("alice",5,"Another action.","action:observed-visit:19")});
                var mixedRows=ReadAll(archive.OpenScan(mixed));
                Check.Equal("action:observed-visit:19",mixedRows[0].Entry.SourceActionId);
                Check.Equal(null,mixedRows[1].Entry.SourceActionId);
            })));
            cases.Add(("Archive rejects malformed PIM2 provenance marker and length after valid checksum", () => InDirectory(root => {
                const string actionId="action:observed-visit:17";
                var archive=new SharedConversationArchive(root);
                var valid=archive.Commit(null,new[]{Observed("alice",2,"Action observation.",actionId)});
                byte[] raw=Inflate(File.ReadAllBytes(ChunkFile(root,valid)));
                int marker=raw.Length-Encoding.UTF8.GetByteCount(actionId)-sizeof(int)-1;
                Check.Equal(1,raw[marker]);
                var badMarker=PublishMutatedChunk(root,valid,bytes=>{bytes[marker]=2;return bytes;});
                Reject(()=>archive.Load(badMarker));
                Reject(()=>archive.OpenScan(badMarker).ReadNextBatch(1));
                var badLength=PublishMutatedChunk(root,valid,bytes=>{
                    BitConverter.GetBytes(881).CopyTo(bytes,marker+1);return bytes;});
                Reject(()=>archive.Load(badLength));
                Check.Equal(actionId,archive.Load(valid).Single().Entries.Single().SourceActionId);
            })));
            cases.Add(("Conversation compaction eligibility uses strict native game-month age and survives time skips", () => {
                const double month=30d*86400d;
                Check.True(!ConversationMemoryCompactionEligibility.IsEligible(100,100+month),
                    "Exactly one native month old is not older than one month");
                Check.True(ConversationMemoryCompactionEligibility.IsEligible(100,100+month+1),
                    "One game second beyond the month boundary is eligible");
                Check.True(!ConversationMemoryCompactionEligibility.IsEligible(1000,999),
                    "Future records are not eligible");
                Check.True(ConversationMemoryCompactionEligibility.IsEligible(100,100+month*12),
                    "A game-time jump can make old records eligible immediately");
                Reject(()=>ConversationMemoryCompactionEligibility.IsEligible(-1,100));
                Reject(()=>ConversationMemoryCompactionEligibility.IsEligible(1,double.NaN));
            }));
            cases.Add(("Conversation archive scan yields bounded reverse-archive records with stable source addresses", () => InDirectory(root => {
                var store=new WorldStateStore(42,root);
                for(int n=0;n<260;n++)
                    store.RememberConversation("alice",n,"date","place","turn-"+n,"reply-"+n,"model");
                var saved=store.SnapshotForSave();
                var archive=new SharedConversationArchive(root);
                var cursor=archive.OpenScan(saved.ConversationArchive);
                var all=new List<SharedConversationArchive.ScanRecord>();
                while(!cursor.IsComplete)
                {
                    var batch=cursor.ReadNextBatch(7);
                    Check.True(batch.Count<=7,"A scan batch must respect its record limit");
                    all.AddRange(batch);
                }
                Check.Equal(260,all.Count);
                Check.Equal("turn-259",all[0].Entry.PlayerText);
                Check.Equal("turn-0",all[259].Entry.PlayerText);
                Check.Equal(saved.ConversationArchive.ArchiveId,all[0].Address.ArchiveId);
                Check.Equal(saved.ConversationArchive.Head,all[0].Address.CheckpointHead);
                Check.Equal("alice",all[0].Address.PersonId);
                Check.True(all.Select(row=>row.Address.ChunkHash).Distinct().Count()>=3,
                    "The scan must cross multiple immutable chunks");
                Check.Equal(3,all[0].Address.RecordIndex);
                Check.Equal(127,all[4].Address.RecordIndex);
                Check.Equal(0,all[259].Address.RecordIndex);
            })));
            cases.Add(("Conversation archive scan preserves archive order across people and backdated times", () => InDirectory(root => {
                var store=new WorldStateStore(42,root);
                const double month=30d*86400d;
                double now=month+1000;
                store.RememberConversation("alice",100,"date","place","alice-old","reply","model");
                store.RememberConversation("alice",now-100,"date","place","alice-new","reply","model");
                store.RememberConversation("bob",50,"date","place","bob-old","reply","model");
                store.RememberConversation("bob",now-month,"date","place","bob-at-cutoff","reply","model");
                var saved=store.SnapshotForSave();
                var rows=ReadAll(new SharedConversationArchive(root).OpenScan(saved.ConversationArchive));
                Check.Equal(4,rows.Count);
                Check.Equal("bob-at-cutoff",rows[0].Entry.PlayerText);
                Check.Equal("bob-old",rows[1].Entry.PlayerText);
                Check.Equal("alice-new",rows[2].Entry.PlayerText);
                Check.Equal("alice-old",rows[3].Entry.PlayerText);
                Check.True(!ConversationMemoryCompactionEligibility.IsEligible(rows[0].Entry.At,now));
                Check.True(ConversationMemoryCompactionEligibility.IsEligible(rows[1].Entry.At,now),
                    "A younger record may precede an eligible record; caller must inspect every timestamp");
                Check.True(rows[1].Entry.At<rows[2].Entry.At,
                    "Archive scan is not globally sorted by game timestamp");
                Check.Equal(4,rows.Select(row=>row.Address.ArchiveId+":"+row.Address.ChunkHash+":"+
                    row.Address.RecordIndex+":"+row.Address.PersonId).Distinct().Count());
            })));
            cases.Add(("Conversation archive scans stay pinned to captured sibling checkpoints", () => InDirectory(root => {
                var origin=new WorldStateStore(42,root);
                for(int n=0;n<129;n++)origin.RememberConversation("alice",n,"date","place","base-"+n,"reply","model");
                var ancestor=origin.SnapshotForSave();
                var branchA=new WorldStateStore(1,root);branchA.Restore(ancestor);
                branchA.RememberConversation("alice",200,"date","place","branch-a","reply","model");
                var savedA=branchA.SnapshotForSave();
                var branchB=new WorldStateStore(2,root);branchB.Restore(ancestor);
                branchB.RememberConversation("alice",300,"date","place","branch-b","reply","model");
                var savedB=branchB.SnapshotForSave();
                var archive=new SharedConversationArchive(root);
                var scanA=archive.OpenScan(savedA.ConversationArchive);
                var scanB=archive.OpenScan(savedB.ConversationArchive);
                var rowsA=ReadAll(scanA);var rowsB=ReadAll(scanB);
                Check.True(rowsA.Any(row=>row.Entry.PlayerText=="branch-a") && !rowsA.Any(row=>row.Entry.PlayerText=="branch-b"));
                Check.True(rowsB.Any(row=>row.Entry.PlayerText=="branch-b") && !rowsB.Any(row=>row.Entry.PlayerText=="branch-a"));
                Check.Equal(savedA.ConversationArchive.Head,scanA.Checkpoint.Head);
                Check.Equal(savedB.ConversationArchive.Head,scanB.Checkpoint.Head);
            })));
            cases.Add(("Conversation archive scan validates missing and corrupted chunks lazily", () => InDirectory(root => {
                var store=new WorldStateStore(42,root);
                for(int n=0;n<129;n++)store.RememberConversation("alice",n,"date","place","turn-"+n,"reply","model");
                var saved=store.SnapshotForSave();
                var archive=new SharedConversationArchive(root);
                var cursor=archive.OpenScan(saved.ConversationArchive);
                string headPath=Directory.GetFiles(root,saved.ConversationArchive.Head+".pim",SearchOption.AllDirectories).Single();
                byte[] valid=File.ReadAllBytes(headPath);
                File.WriteAllBytes(headPath,new byte[]{1,2,3});
                Reject(()=>cursor.ReadNextBatch(1));
                File.WriteAllBytes(headPath,valid);
                Check.Equal(1,cursor.ReadNextBatch(1).Count);
                string parent=Directory.GetFiles(root,"*.pim",SearchOption.AllDirectories)
                    .Select(path=>new {Path=path,Name=Path.GetFileNameWithoutExtension(path)})
                    .Single(row=>row.Name!=saved.ConversationArchive.Head).Path;
                File.Delete(parent);
                Reject(()=>cursor.ReadNextBatch(1));
            })));
            cases.Add(("Shared memory checkpoints isolate abandoned futures and same-date branches", () => InDirectory(root => {
                var a=new WorldStateStore(42,root);
                Say(a,"alice","My sister is Mirabelle."); var first=a.SnapshotForSave();
                Say(a,"alice","The ruby is in Sentinel."); var future=a.SnapshotForSave();
                var b=new WorldStateStore(7,root); b.Restore(first);
                Check.True(Recall(b,"alice").Contains("Mirabelle") && !Recall(b,"alice").Contains("ruby"));
                Say(b,"alice","The emerald is in Wayrest."); var branch=b.SnapshotForSave();
                var reader=new WorldStateStore(9,root); reader.Restore(future);
                Check.True(Recall(reader,"alice").Contains("ruby") && !Recall(reader,"alice").Contains("emerald"));
                reader.Restore(branch);
                Check.True(Recall(reader,"alice").Contains("emerald") && !Recall(reader,"alice").Contains("ruby"));
                Check.True(!Recall(reader,"bob").Contains("Mirabelle"));
                reader.Restore(first); Check.True(!Recall(reader,"alice").Contains("emerald"));
            })));
            cases.Add(("Captured archive checkpoint views keep restored sibling heads independent", () => InDirectory(root => {
                var origin=new WorldStateStore(42,root);
                Say(origin,"alice","Shared ancestor."); var ancestor=origin.SnapshotForSave();
                Say(origin,"alice","Future branch only."); var future=origin.SnapshotForSave();
                var earlierView=origin.CaptureConversationArchiveCheckpoint();
                var siblingA=new WorldStateStore(3,root); siblingA.Restore(future);
                var siblingB=new WorldStateStore(5,root); siblingB.Restore(ancestor);
                var aBefore=siblingA.CaptureConversationArchiveCheckpoint();
                var bBefore=siblingB.CaptureConversationArchiveCheckpoint();
                Check.Equal(aBefore.ArchiveId,bBefore.ArchiveId);
                Check.True(aBefore.Head!=bBefore.Head,"Restored siblings must retain their selected ancestry heads");
                Say(siblingA,"alice","A branch continuation."); siblingA.SnapshotForSave();
                Say(siblingB,"alice","B branch continuation."); siblingB.SnapshotForSave();
                var aAfter=siblingA.CaptureConversationArchiveCheckpoint();
                var bAfter=siblingB.CaptureConversationArchiveCheckpoint();
                Check.Equal(aBefore.ArchiveId,aAfter.ArchiveId); Check.Equal(bBefore.ArchiveId,bAfter.ArchiveId);
                Check.True(aAfter.Head!=bAfter.Head,"Sibling writes must publish distinct captured heads in one namespace");
                Check.Equal(future.ConversationArchive.Head,aBefore.Head);
                Check.Equal(ancestor.ConversationArchive.Head,bBefore.Head);
                Check.Equal(future.ConversationArchive.ArchiveId,aAfter.ArchiveId);
                Check.Equal(ancestor.ConversationArchive.ArchiveId,bAfter.ArchiveId);
                Check.True(Recall(siblingA,"alice").Contains("Future branch only") && Recall(siblingA,"alice").Contains("A branch continuation") &&
                    !Recall(siblingA,"alice").Contains("B branch continuation"));
                Check.True(Recall(siblingB,"alice").Contains("Shared ancestor") && Recall(siblingB,"alice").Contains("B branch continuation") &&
                    !Recall(siblingB,"alice").Contains("Future branch only"));
                Check.Equal(future.ConversationArchive.Head,earlierView.Head);
                Check.Equal(future.ConversationArchive.ArchiveId,earlierView.ArchiveId);
            })));
            cases.Add(("Archive saves keep constant checkpoint size and do not rewrite committed conversations", () => InDirectory(root => {
                var store=new WorldStateStore(42,root); Say(store,"alice","First meeting.");
                var first=store.SnapshotForSave();
                int count=Directory.GetFiles(root,"*.pim",SearchOption.AllDirectories).Length;
                store.SnapshotForSave(); store.SnapshotForSave();
                Check.Equal(count,Directory.GetFiles(root,"*.pim",SearchOption.AllDirectories).Length);
                for(int n=0;n<1000;n++) Say(store,"alice","Memory number "+n+" "+new string('x',400));
                var last=store.SnapshotForSave();
                Check.Equal(0,last.ConversationMemories.Count);
                // Exchange-count metadata is proportional only to the current scalar
                // count; normalize it so this assertion measures archive payload size.
                foreach(var attitude in first.ConversationAttitudes)attitude.SharedExchangeCount=0;
                foreach(var attitude in last.ConversationAttitudes)attitude.SharedExchangeCount=0;
                string small=JsonSerializer.Serialize(first,JsonOptions);
                Check.Equal(small.Length,JsonSerializer.Serialize(last,JsonOptions).Length);
                var reader=new WorldStateStore(1,root); reader.Restore(last);
                Check.True(reader.RecallConversation("alice","First meeting",100,2000).Contains("First meeting"));
            })));
            cases.Add(("Legacy inline memories migrate without changing the old snapshot or duplicating repeated imports", () => InDirectory(root => {
                var old=new WorldStateStore(42); Say(old,"alice","My horse is Copper.");
                var legacy=old.Snapshot(); legacy.Version=22;
                var migrated=new WorldStateStore(1,root); migrated.Restore(legacy);
                var save=migrated.SnapshotForSave();
                Check.Equal(1,legacy.ConversationMemories.Count); Check.Equal(0,save.ConversationMemories.Count);
                int count=Directory.GetFiles(root,"*.pim",SearchOption.AllDirectories).Length;
                var again=new WorldStateStore(2,root); again.Restore(legacy); var second=again.SnapshotForSave();
                Check.Equal(save.ConversationArchive.Head,second.ConversationArchive.Head);
                Check.Equal(count,Directory.GetFiles(root,"*.pim",SearchOption.AllDirectories).Length);
                Check.True(Recall(again,"alice").Contains("Copper"));
            })));
            cases.Add(("Missing or damaged memory ancestry fails restore without replacing live memories", () => InDirectory(root => {
                var store=new WorldStateStore(42,root); Say(store,"alice","Keep this memory."); var saved=store.SnapshotForSave();
                Say(store,"alice","Pending survives failure.");
                string file=Directory.GetFiles(root,"*.pim",SearchOption.AllDirectories).Single(); byte[] data=File.ReadAllBytes(file);
                File.Delete(file);
                Reject(()=>store.Restore(saved)); Check.True(Recall(store,"alice").Contains("Pending survives"));
                File.WriteAllBytes(file,new byte[]{1,2,3,4}); Reject(()=>store.Restore(saved));
                Check.True(Recall(store,"alice").Contains("Keep this memory"));
                File.WriteAllBytes(file,data); store.Restore(saved);
                Check.True(!Recall(store,"alice").Contains("Pending survives"));
                var noArchive=new WorldStateStore(8); Reject(()=>noArchive.Restore(saved));
            })));
            cases.Add(("Failed archive publication retains pending memory for retry and never publishes a checkpoint", () => InDirectory(root => {
                string blocked=Path.Combine(root,"blocked"); File.WriteAllText(blocked,"not a directory");
                var store=new WorldStateStore(42,blocked); Say(store,"alice","Retry me.");
                Reject(()=>store.SnapshotForSave());
                Check.True(Recall(store,"alice").Contains("Retry me"));
                Check.Equal(1,store.Snapshot().ConversationMemories.Count);
                File.Delete(blocked); var saved=store.SnapshotForSave();
                var reader=new WorldStateStore(7,blocked); reader.Restore(saved);
                Check.True(Recall(reader,"alice").Contains("Retry me"));
            })));
            cases.Add(("Portable archive copy contains checkpoint ancestry and excludes sibling future", () => InDirectory(root => {
                string source=Path.Combine(root,"source"), target=Path.Combine(root,"target");
                var store=new WorldStateStore(42,source); Say(store,"alice","Known before save."); var saved=store.SnapshotForSave();
                Say(store,"alice","Secret future."); store.SnapshotForSave();
                var archive=new SharedConversationArchive(source); archive.CopyCheckpointTo(saved.ConversationArchive,target);
                var reader=new WorldStateStore(0,target); reader.Restore(saved);
                Check.True(Recall(reader,"alice").Contains("Known before") && !Recall(reader,"alice").Contains("Secret future"));
                Check.Equal(1,Directory.GetFiles(target,"*.pim",SearchOption.AllDirectories).Length);
                archive.CopyCheckpointTo(saved.ConversationArchive,target);
                Check.Equal(1,Directory.GetFiles(target,"*.pim",SearchOption.AllDirectories).Length);
            })));
            cases.Add(("Accepted-intent fallback survives shared archive save and transcript replay", () => InDirectory(root => {
                var store=new WorldStateStore(42,root);
                const string question="Wait here for 10 seconds, then follow me for 10 seconds.";
                string answer=NpcActionSequenceChat.AcceptedIntentFallback(new[]{
                    new NpcActionDecision {Option=new NpcActionOption {Kind=NpcActionKind.Wait}},
                    new NpcActionDecision {Option=new NpcActionOption {Kind=NpcActionKind.Follow}}});
                Check.Equal("I agreed to this ordered task. I will begin its steps after our conversation ends.",answer);
                store.RememberConversation("alice",100,"6 Morning Star 405","Black Wolf Inn",question,answer,"action");
                var saved=store.SnapshotForSave();
                Check.Equal(0,saved.ConversationMemories.Count);
                Check.True(saved.ConversationArchive!=null && saved.ConversationArchive.Head.Length==64);
                var decoded=JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(saved,JsonOptions),JsonOptions);
                var reader=new WorldStateStore(7,root); reader.Restore(decoded);
                var transcript=reader.ConversationTranscript("alice",200);
                Check.Equal(1,transcript.Count); Check.Equal(question,transcript[0].PlayerText);
                Check.Equal(answer,transcript[0].NpcText); Check.Equal("action",transcript[0].Source);
            })));
            cases.Add(("Archive checks every ancestor and ignores unreferenced chunks and temporary files", () => InDirectory(root => {
                var store=new WorldStateStore(42,root); Say(store,"alice","Original ancestor."); var parent=store.SnapshotForSave();
                Say(store,"alice","Later child."); var child=store.SnapshotForSave();
                string path=Directory.GetFiles(root,parent.ConversationArchive.Head+".pim",SearchOption.AllDirectories).Single();
                File.WriteAllText(Path.Combine(root,"unfinished.tmp"),"partial write");
                var reader=new WorldStateStore(1,root); reader.Restore(child); Check.True(Recall(reader,"alice").Contains("Original ancestor"));
                byte[] original=File.ReadAllBytes(path);
                using(var file=File.Create(path)) using(var gzip=new GZipStream(file,CompressionMode.Compress))
                {byte[] fake=new byte[]{1,2,3}; gzip.Write(fake,0,fake.Length);}
                Reject(()=>reader.Restore(child));
                Check.True(Recall(reader,"alice").Contains("Original ancestor"));
                File.WriteAllBytes(path,original); reader.Restore(child);
                var traversal=store.SnapshotForSave(); traversal.ConversationArchive.ArchiveId="../../escape";
                Reject(()=>reader.Restore(traversal));
            })));
            cases.Add(("New characters and pending runtime snapshots preserve separate memory histories", () => InDirectory(root => {
                var first=new WorldStateStore(42,root); Say(first,"alice","First character only."); var saved=first.SnapshotForSave();
                Say(first,"alice","Unsaved conversation."); var pending=first.Snapshot();
                var second=new WorldStateStore(42,root); Say(second,"alice","Second character only."); var other=second.SnapshotForSave();
                Check.True(saved.ConversationArchive.ArchiveId!=other.ConversationArchive.ArchiveId);
                second.Restore(pending); Check.True(Recall(second,"alice").Contains("Unsaved conversation") && !Recall(second,"alice").Contains("Second character"));
                var committed=second.SnapshotForSave(); var reader=new WorldStateStore(1,root); reader.Restore(committed);
                Check.True(Recall(reader,"alice").Contains("Unsaved conversation"));
                reader.Restore(saved); Check.True(!Recall(reader,"alice").Contains("Unsaved conversation"));
            })));
            cases.Add(("Restore reconciles shared-exchange counts from each pinned archive plus pending journal", () => InDirectory(root => {
                var origin=new WorldStateStore(42,root);
                origin.RememberConversation("alice",100,"day 1","inn","Base real exchange.","Base reply.","model");
                origin.RememberConversation("alice",101,"day 1","inn","Base failed turn.","I cannot settle that right now.","unavailable");
                var ancestor=origin.SnapshotForSave();

                var branchA=new WorldStateStore(1,root);branchA.Restore(ancestor);
                branchA.RememberConversation("alice",200,"day 1","inn","A failed turn.","I cannot settle that right now.","unavailable");
                branchA.RememberConversation("alice",201,"day 1","inn","A real exchange one.","A reply one.","native");
                branchA.RememberConversation("alice",202,"day 1","inn","A real exchange two.","A reply two.","action");
                var pendingA=branchA.Snapshot();
                pendingA.ConversationAttitudes.Single(item=>item.PersonId=="alice").SharedExchangeCount=99;

                var branchB=new WorldStateStore(2,root);branchB.Restore(ancestor);
                branchB.RememberConversation("alice",300,"day 1","inn","B real exchange.","B reply.","model");
                var savedB=branchB.SnapshotForSave();
                savedB.ConversationAttitudes.Single(item=>item.PersonId=="alice").SharedExchangeCount=88;
                Check.True(pendingA.ConversationArchive.Head!=savedB.ConversationArchive.Head,
                    "The test requires sibling selected heads in the same archive namespace.");
                Check.Equal(ancestor.ConversationArchive.ArchiveId,pendingA.ConversationArchive.ArchiveId);
                Check.Equal(ancestor.ConversationArchive.ArchiveId,savedB.ConversationArchive.ArchiveId);

                var restoredA=new WorldStateStore(3,root);restoredA.Restore(pendingA);
                var restoredB=new WorldStateStore(4,root);restoredB.Restore(savedB);
                Check.Equal(3,restoredA.Snapshot().ConversationAttitudes.Single(item=>item.PersonId=="alice").SharedExchangeCount);
                Check.Equal(2,restoredB.Snapshot().ConversationAttitudes.Single(item=>item.PersonId=="alice").SharedExchangeCount);
                Check.Equal(5,restoredA.ConversationTranscript("alice",500).FindAll(entry=>entry.Source!="observed").Count);
                Check.Equal(3,restoredB.ConversationTranscript("alice",500).FindAll(entry=>entry.Source!="observed").Count);
                string recallA=restoredA.RecallConversation("alice","A real exchange",500,5000);
                string recallB=restoredB.RecallConversation("alice","B real exchange",500,5000);
                Check.True(recallA.Contains("A real exchange")&&!recallA.Contains("B real exchange"));
                Check.True(recallB.Contains("B real exchange")&&!recallB.Contains("A real exchange"));
            })));
            cases.Add(("Restore recalculates legacy unavailable-only counts but preserves aggregates without a journal",()=>{
                var unavailable=new WorldStateStore(9);
                unavailable.RememberConversation("alice",10,"day 1","inn","Only failed turn.","I cannot settle that right now.","unavailable");
                var legacy=unavailable.Snapshot();
                legacy.ConversationAttitudes.Add(new ConversationAttitude {PersonId="alice",SharedExchangeCount=15,
                    Offset=2,EncounterCount=4});
                var restored=new WorldStateStore(10);restored.Restore(legacy);
                var reconciled=restored.Snapshot().ConversationAttitudes.Single(item=>item.PersonId=="alice");
                Check.Equal(0,reconciled.SharedExchangeCount);
                Check.Equal(2,reconciled.Offset);Check.Equal(4,reconciled.EncounterCount);

                var noJournal=new WorldState {Version=30,WorldSeed=9};
                noJournal.ConversationAttitudes.Add(new ConversationAttitude {PersonId="orphan",SharedExchangeCount=7});
                var preserved=new WorldStateStore(11);preserved.Restore(noJournal);
                Check.Equal(7,preserved.Snapshot().ConversationAttitudes.Single(item=>item.PersonId=="orphan").SharedExchangeCount);
            }));
        }
        private static readonly JsonSerializerOptions JsonOptions=new JsonSerializerOptions {IncludeFields=true};
        private static NpcConversationMemory Observed(string person,double at,string observation,string actionId)
        {return new NpcConversationMemory {PersonId=person,Entries=new List<NpcMemoryEntry> {
            new NpcMemoryEntry {At=at,Date="date",Place="place",PlayerText="",NpcText="",
                Observation=observation,Source="observed",SourceActionId=actionId}}};}
        private static string ChunkFile(string root,ConversationArchiveCheckpoint checkpoint)
        {return Path.Combine(root,checkpoint.ArchiveId,checkpoint.Head.Substring(0,2),checkpoint.Head+".pim");}
        private static byte[] Inflate(byte[] compressed)
        {using(var input=new MemoryStream(compressed))using(var gzip=new GZipStream(input,CompressionMode.Decompress))
            using(var output=new MemoryStream()){gzip.CopyTo(output);return output.ToArray();}}
        private static ConversationArchiveCheckpoint PublishMutatedChunk(string root,ConversationArchiveCheckpoint original,
            Func<byte[],byte[]> mutate)
        {
            byte[] bytes=mutate(Inflate(File.ReadAllBytes(ChunkFile(root,original))));
            string hash;
            using(var sha=SHA256.Create()) hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            var forged=new ConversationArchiveCheckpoint {ArchiveId=original.ArchiveId,Head=hash};
            string path=ChunkFile(root,forged); Directory.CreateDirectory(Path.GetDirectoryName(path));
            using(var file=File.Create(path))using(var gzip=new GZipStream(file,CompressionLevel.Optimal))
                gzip.Write(bytes,0,bytes.Length);
            return forged;
        }
        private static List<SharedConversationArchive.ScanRecord> ReadAll(SharedConversationArchive.Scan cursor)
        {var result=new List<SharedConversationArchive.ScanRecord>();while(!cursor.IsComplete)result.AddRange(cursor.ReadNextBatch(128));return result;}
        private static void Say(WorldStateStore store,string npc,string text)
        { store.RememberConversation(npc,100,"6 Morning Star 405","Black Wolf Inn",text,"I understand.","model"); }
        private static string Recall(WorldStateStore store,string npc) {return store.RecallConversation(npc,"",100,5000);}
        private static void Reject(Action action)
        {bool failed=false; try{action();}catch(Exception e) when(e is IOException || e is InvalidDataException || e is ArgumentException || e is InvalidOperationException){failed=true;} Check.True(failed);}
        private static void InDirectory(Action<string> action)
        { string root=Path.Combine(Path.GetTempPath(),"people-memory-test-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
          try{action(root);}finally{Directory.Delete(root,true);} }
    }
}
