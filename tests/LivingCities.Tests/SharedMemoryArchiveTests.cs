using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class SharedMemoryArchiveTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
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
            cases.Add(("Archive saves keep constant checkpoint size and do not rewrite committed conversations", () => InDirectory(root => {
                var store=new WorldStateStore(42,root); Say(store,"alice","First meeting.");
                var first=store.SnapshotForSave(); string small=JsonSerializer.Serialize(first,JsonOptions);
                int count=Directory.GetFiles(root,"*.pim",SearchOption.AllDirectories).Length;
                store.SnapshotForSave(); store.SnapshotForSave();
                Check.Equal(count,Directory.GetFiles(root,"*.pim",SearchOption.AllDirectories).Length);
                for(int n=0;n<1000;n++) Say(store,"alice","Memory number "+n+" "+new string('x',400));
                var last=store.SnapshotForSave();
                Check.Equal(0,last.ConversationMemories.Count);
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
        }
        private static readonly JsonSerializerOptions JsonOptions=new JsonSerializerOptions {IncludeFields=true};
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
