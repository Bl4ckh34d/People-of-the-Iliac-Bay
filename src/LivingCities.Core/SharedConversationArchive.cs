using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class ConversationArchiveCheckpoint
    {
        public int Version=1;
        public string ArchiveId;
        public string Head="";

        public static ConversationArchiveCheckpoint CopyValidated(ConversationArchiveCheckpoint source)
        {
            if(source==null) return null;
            if(source.Version!=1 || !Hex(source.ArchiveId,32) ||
                source.Head==null || (source.Head.Length!=0 && !Hex(source.Head,64)))
                throw new ArgumentException("Invalid conversation archive checkpoint.");
            return new ConversationArchiveCheckpoint { ArchiveId=source.ArchiveId,Head=source.Head };
        }
        internal static bool Hex(string value,int size)
        { return value!=null && value.Length==size && value.All(c=>(c>='0' && c<='9') || (c>='a' && c<='f')); }
    }

    // Immutable parent-linked chunks let saves share ancestry without sharing future
    // state. No timestamp, mutable 'latest' file or directory listing selects a branch.
    public sealed class SharedConversationArchive
    {
        private const int Magic=0x50494d31, BatchSize=128, MaximumBytes=2*1024*1024;
        private readonly string root;
        private sealed class Record { public string Person; public NpcMemoryEntry Entry; }
        private sealed class Chunk { public string Hash,Parent; public byte[] Bytes; public List<Record> Records; }

        public SharedConversationArchive(string directory)
        {
            if(string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Memory archive directory is required.");
            root=Path.GetFullPath(directory);
        }

        internal static ConversationArchiveCheckpoint LegacyCheckpoint(uint seed)
        {
            // The ancestry hash, rather than this namespace, controls membership.
            // Stable namespaces let repeated imports of the same old save deduplicate.
            return new ConversationArchiveCheckpoint {ArchiveId=Hash(Encoding.UTF8.GetBytes("legacy-world:"+seed.ToString(System.Globalization.CultureInfo.InvariantCulture))).Substring(0,32)};
        }

        public ConversationArchiveCheckpoint Commit(ConversationArchiveCheckpoint checkpoint,IEnumerable<NpcConversationMemory> pending)
        {
            var next=ConversationArchiveCheckpoint.CopyValidated(checkpoint) ??
                new ConversationArchiveCheckpoint {ArchiveId=Guid.NewGuid().ToString("N")};
            // Verify the parent before publishing anything that references it.
            if(next.Head.Length>0) Read(next.ArchiveId,next.Head);
            var batch=new List<Record>();
            foreach(var journal in pending.OrderBy(j=>j.PersonId,StringComparer.Ordinal))
            {
                ValidatePerson(journal.PersonId);
                foreach(var entry in journal.Entries)
                {
                    WorldStateStore.ValidateMemoryEntry(entry);
                    batch.Add(new Record {Person=journal.PersonId,Entry=entry});
                    if(batch.Count==BatchSize) {CommitBatch(next,batch); batch.Clear();}
                }
            }
            if(batch.Count>0) CommitBatch(next,batch);
            return next;
        }

        private void CommitBatch(ConversationArchiveCheckpoint next,List<Record> records)
        {
            byte[] bytes=Encode(next.Head,records); string hash=Hash(bytes);
            Publish(next.ArchiveId,hash,bytes);
            next.Head=hash;
        }

        public List<NpcConversationMemory> Load(ConversationArchiveCheckpoint checkpoint)
        {
            var chunks=ReadAncestry(checkpoint);
            var journals=new Dictionary<string,NpcConversationMemory>(StringComparer.Ordinal);
            for(int n=chunks.Count-1;n>=0;n--)
                foreach(var record in chunks[n].Records)
                {
                    NpcConversationMemory journal;
                    if(!journals.TryGetValue(record.Person,out journal))
                    {journal=new NpcConversationMemory {PersonId=record.Person}; journals.Add(record.Person,journal);}
                    journal.Entries.Add(record.Entry);
                }
            return journals.Values.ToList();
        }

        public void CopyCheckpointTo(ConversationArchiveCheckpoint checkpoint,string destination)
        {
            var validated=ConversationArchiveCheckpoint.CopyValidated(checkpoint);
            var chunks=ReadAncestry(validated); // Fully validate before publishing the export.
            if(validated==null) return;
            var target=new SharedConversationArchive(destination);
            foreach(var chunk in chunks) target.Publish(validated.ArchiveId,chunk.Hash,chunk.Bytes);
        }

        private List<Chunk> ReadAncestry(ConversationArchiveCheckpoint checkpoint)
        {
            var saved=ConversationArchiveCheckpoint.CopyValidated(checkpoint);
            var result=new List<Chunk>();
            if(saved==null) return result;
            string hash=saved.Head; var seen=new HashSet<string>(StringComparer.Ordinal);
            while(hash.Length>0)
            {
                if(!seen.Add(hash)) throw new InvalidDataException("Cycle in conversation archive ancestry.");
                var chunk=Read(saved.ArchiveId,hash); result.Add(chunk); hash=chunk.Parent;
            }
            return result;
        }

        private string ChunkPath(string archiveId,string hash)
        {
            if(!ConversationArchiveCheckpoint.Hex(archiveId,32) || !ConversationArchiveCheckpoint.Hex(hash,64))
                throw new ArgumentException("Invalid memory archive path identifier.");
            return Path.Combine(root,archiveId,hash.Substring(0,2),hash+".pim");
        }

        private Chunk Read(string archiveId,string hash)
        {
            string path=ChunkPath(archiveId,hash);
            if(!File.Exists(path)) throw new FileNotFoundException("NPC memory archive is missing. Restore PeopleMemory with this save; no blank history was substituted.",path);
            if(new FileInfo(path).Length>MaximumBytes) throw new InvalidDataException("Conversation archive chunk is too large.");
            byte[] bytes;
            using(var file=File.OpenRead(path))
            using(var gzip=new GZipStream(file,CompressionMode.Decompress))
            using(var buffer=new MemoryStream())
            {
                byte[] block=new byte[8192]; int count;
                while((count=gzip.Read(block,0,block.Length))>0)
                {if(buffer.Length+count>MaximumBytes) throw new InvalidDataException("Expanded memory archive chunk is too large."); buffer.Write(block,0,count);}
                bytes=buffer.ToArray();
            }
            if(Hash(bytes)!=hash) throw new InvalidDataException("NPC memory archive checksum failed; restore a backup of PeopleMemory.");
            return Decode(hash,bytes);
        }

        private void Publish(string archiveId,string hash,byte[] bytes)
        {
            string path=ChunkPath(archiveId,hash);
            if(File.Exists(path)) {Read(archiveId,hash); return;}
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    using(var gzip=new GZipStream(file,CompressionLevel.Optimal,true)) gzip.Write(bytes,0,bytes.Length);
                    file.Flush(true);
                }
                try {File.Move(temporary,path);}
                catch(IOException) {if(!File.Exists(path)) throw; Read(archiveId,hash);}
            }
            finally {if(File.Exists(temporary)) File.Delete(temporary);}
        }

        private static byte[] Encode(string parent,List<Record> records)
        {
            using(var stream=new MemoryStream())
            {
                using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
                {
                    writer.Write(Magic); WriteString(writer,parent); writer.Write(records.Count);
                    foreach(var record in records)
                    {
                        var e=record.Entry; WriteString(writer,record.Person); writer.Write(e.At);
                        WriteString(writer,e.Date); WriteString(writer,e.Place); WriteString(writer,e.PlayerText);
                        WriteString(writer,e.NpcText); WriteString(writer,e.Observation); WriteString(writer,e.Source);
                    }
                }
                if(stream.Length>MaximumBytes) throw new InvalidDataException("Conversation archive batch is too large.");
                return stream.ToArray();
            }
        }
        private static Chunk Decode(string hash,byte[] bytes)
        {
            using(var stream=new MemoryStream(bytes,false))
            using(var reader=new BinaryReader(stream,Encoding.UTF8))
            {
                if(reader.ReadInt32()!=Magic) throw new InvalidDataException("Unsupported conversation archive format.");
                var chunk=new Chunk {Hash=hash,Bytes=bytes,Parent=ReadString(reader,64),Records=new List<Record>()};
                if(chunk.Parent.Length>0 && !ConversationArchiveCheckpoint.Hex(chunk.Parent,64)) throw new InvalidDataException("Invalid memory parent.");
                int count=reader.ReadInt32();
                if(count<1 || count>BatchSize) throw new InvalidDataException("Invalid memory batch length.");
                for(int n=0;n<count;n++)
                {
                    string person=ReadString(reader,240); ValidatePerson(person);
                    var entry=new NpcMemoryEntry {At=reader.ReadDouble(),Date=ReadString(reader,120),Place=ReadString(reader,300),
                        PlayerText=ReadString(reader,600),NpcText=ReadString(reader,1600),Observation=ReadString(reader,600),Source=ReadString(reader,16)};
                    WorldStateStore.ValidateMemoryEntry(entry);
                    chunk.Records.Add(new Record {Person=person,Entry=entry});
                }
                if(stream.Position!=stream.Length) throw new InvalidDataException("Trailing memory archive data.");
                return chunk;
            }
        }
        private static void ValidatePerson(string person)
        {if(string.IsNullOrWhiteSpace(person) || person.Length>240) throw new ArgumentException("Invalid memory identity.");}
        private static void WriteString(BinaryWriter writer,string value)
        {byte[] bytes=Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);}
        private static string ReadString(BinaryReader reader,int limit)
        {
            int length=reader.ReadInt32(); if(length<0 || length>limit*4) throw new InvalidDataException("Invalid memory string length.");
            byte[] bytes=reader.ReadBytes(length); if(bytes.Length!=length) throw new EndOfStreamException("Incomplete memory entry.");
            string value=new UTF8Encoding(false,true).GetString(bytes);
            if(value.Length>limit) throw new InvalidDataException("Memory string exceeds limit."); return value;
        }
        private static string Hash(byte[] bytes)
        {using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
    }
}
