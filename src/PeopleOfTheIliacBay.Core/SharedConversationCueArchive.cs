using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class ConversationCueArchiveCheckpoint
    {
        public int Version = 1;
        public string ArchiveId;
        public string Head = "";

        public static ConversationCueArchiveCheckpoint CopyValidated(ConversationCueArchiveCheckpoint source)
        {
            if (source == null) return null;
            if (source.Version != 1 || !ConversationArchiveCheckpoint.Hex(source.ArchiveId, 32) ||
                source.Head == null || source.Head.Length != 0 &&
                !ConversationArchiveCheckpoint.Hex(source.Head, 64))
                throw new ArgumentException("Invalid conversation cue archive checkpoint.");
            return new ConversationCueArchiveCheckpoint { ArchiveId = source.ArchiveId, Head = source.Head };
        }
    }

    [Serializable]
    public sealed class ConversationCueArchiveRecord
    {
        public string SourceChunkHash;
        public int SourceRecordIndex;
        public string PersonId;
        public string ReceiptId, EpisodeId, UtteranceId, SpeakerId, ObservedText;
        public double ObservedAt;
        public NpcExperienceAppliedAppraisal Appraisal;

        internal static ConversationCueArchiveRecord CopyValidated(ConversationCueArchiveRecord source)
        {
            if (source == null || !ConversationArchiveCheckpoint.Hex(source.SourceChunkHash, 64) ||
                source.SourceRecordIndex < 0 || source.SourceRecordIndex >= 128 ||
                !NpcExperienceAppraisalContract.ValidIdentity(source.PersonId, 240) ||
                !NpcExperienceAppraisalContract.ValidIdentity(source.ReceiptId, 240) ||
                !NpcExperienceAppraisalContract.ValidIdentity(source.EpisodeId, 240) ||
                !NpcExperienceAppraisalContract.ValidIdentity(source.UtteranceId, 240) ||
                !NpcExperienceAppraisalContract.ValidIdentity(source.SpeakerId, 240) ||
                string.IsNullOrEmpty(source.ObservedText) || source.ObservedText.Length > 1024 ||
                double.IsNaN(source.ObservedAt) || double.IsInfinity(source.ObservedAt) ||
                source.ObservedAt < 0)
                throw new ArgumentException("Invalid archived conversation emotion cue.");
            return new ConversationCueArchiveRecord {
                SourceChunkHash = source.SourceChunkHash, SourceRecordIndex = source.SourceRecordIndex,
                PersonId = source.PersonId, ReceiptId = source.ReceiptId,
                EpisodeId = source.EpisodeId, UtteranceId = source.UtteranceId,
                SpeakerId = source.SpeakerId, ObservedText = source.ObservedText,
                ObservedAt = source.ObservedAt,
                Appraisal = NpcExperienceAppliedAppraisal.CopyValidated(source.Appraisal) };
        }

        internal ConversationMemoryCompactionEmotionCue ToCue(string archiveId, string checkpointHead)
        {
            return new ConversationMemoryCompactionEmotionCue(
                new SharedConversationArchive.RecordAddress(archiveId, checkpointHead,
                    SourceChunkHash, SourceRecordIndex, PersonId), ReceiptId, EpisodeId,
                UtteranceId, PersonId, SpeakerId, ObservedAt, ObservedText, Appraisal);
        }
    }

    /// <summary>One immutable, content-addressed cue per chunk. The save head selects exact branch ancestry.</summary>
    public sealed class SharedConversationCueArchive
    {
        private const int Magic = 0x50494331;
        private const int MaximumBytes = 16384;
        private readonly string root;
        private sealed class Chunk
        {
            public string Hash, Parent;
            public byte[] Bytes;
            public ConversationCueArchiveRecord Cue;
        }

        public SharedConversationCueArchive(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Cue archive directory is required.");
            root = Path.GetFullPath(directory);
        }

        public ConversationCueArchiveCheckpoint Commit(ConversationCueArchiveCheckpoint checkpoint,
            ConversationCueArchiveRecord record)
        {
            var next = ConversationCueArchiveCheckpoint.CopyValidated(checkpoint);
            if (next == null) throw new ArgumentException("Cue checkpoint is required.", nameof(checkpoint));
            var cue = ConversationCueArchiveRecord.CopyValidated(record);
            if (next.Head.Length != 0) Read(next.ArchiveId, next.Head);
            byte[] bytes = Encode(next.Head, cue);
            string hash = Hash(bytes);
            Publish(next.ArchiveId, hash, bytes);
            next.Head = hash;
            return next;
        }

        public List<ConversationCueArchiveRecord> Load(ConversationCueArchiveCheckpoint checkpoint)
        { return ReadAncestry(checkpoint).Select(x => x.Cue).ToList(); }

        public void CopyCheckpointTo(ConversationCueArchiveCheckpoint checkpoint, string destination)
        {
            var checkedCheckpoint = ConversationCueArchiveCheckpoint.CopyValidated(checkpoint);
            var ancestry = ReadAncestry(checkedCheckpoint);
            if (checkedCheckpoint == null) return;
            var target = new SharedConversationCueArchive(destination);
            foreach (var chunk in ancestry)
                target.Publish(checkedCheckpoint.ArchiveId, chunk.Hash, chunk.Bytes);
        }

        private List<Chunk> ReadAncestry(ConversationCueArchiveCheckpoint checkpoint)
        {
            var checkedCheckpoint = ConversationCueArchiveCheckpoint.CopyValidated(checkpoint);
            var result = new List<Chunk>();
            if (checkedCheckpoint == null) return result;
            string next = checkedCheckpoint.Head;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (next.Length != 0)
            {
                if (!seen.Add(next)) throw new InvalidDataException("Cycle in conversation cue ancestry.");
                var chunk = Read(checkedCheckpoint.ArchiveId, next);
                result.Add(chunk);
                next = chunk.Parent;
            }
            return result;
        }

        private string ChunkPath(string archiveId, string hash)
        {
            if (!ConversationArchiveCheckpoint.Hex(archiveId, 32) ||
                !ConversationArchiveCheckpoint.Hex(hash, 64))
                throw new ArgumentException("Invalid cue archive path identifier.");
            return Path.Combine(root, archiveId, hash.Substring(0, 2), hash + ".pic");
        }

        private Chunk Read(string archiveId, string hash)
        {
            string path = ChunkPath(archiveId, hash);
            if (!File.Exists(path)) throw new FileNotFoundException(
                "NPC conversation cue archive is missing; restore PeopleMemory with this save.", path);
            if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("Cue chunk is too large.");
            byte[] bytes;
            using (var file = File.OpenRead(path))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var buffer = new MemoryStream())
            {
                byte[] block = new byte[4096]; int count;
                while ((count = gzip.Read(block, 0, block.Length)) > 0)
                {
                    if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("Expanded cue chunk is too large.");
                    buffer.Write(block, 0, count);
                }
                bytes = buffer.ToArray();
            }
            if (Hash(bytes) != hash) throw new InvalidDataException("NPC conversation cue checksum failed.");
            return Decode(hash, bytes);
        }

        private void Publish(string archiveId, string hash, byte[] bytes)
        {
            string path = ChunkPath(archiveId, hash);
            if (File.Exists(path)) { Read(archiveId, hash); return; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (var gzip = new GZipStream(file, CompressionLevel.Optimal, true))
                        gzip.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                try { File.Move(temporary, path); }
                catch (IOException) { if (!File.Exists(path)) throw; Read(archiveId, hash); }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static byte[] Encode(string parent, ConversationCueArchiveRecord cue)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Magic); Write(writer, parent);
                Write(writer, cue.SourceChunkHash); writer.Write(cue.SourceRecordIndex);
                Write(writer, cue.PersonId); Write(writer, cue.ReceiptId); Write(writer, cue.EpisodeId);
                Write(writer, cue.UtteranceId); Write(writer, cue.SpeakerId);
                Write(writer, cue.ObservedText); writer.Write(cue.ObservedAt);
                var a = cue.Appraisal;
                writer.Write(a.Version); writer.Write(a.MoodDelta); writer.Write(a.AffinityDelta);
                writer.Write(a.TrustDelta); writer.Write(a.ResentmentDelta); writer.Write(a.FearDelta);
                writer.Write(a.AppliedMoodDelta); writer.Write(a.AppliedAffinityDelta);
                writer.Write((int)a.SpeechActKind); WriteNullable(writer, a.SpeechTargetPersonId);
                if (stream.Length > MaximumBytes) throw new InvalidDataException("Cue payload is too large.");
                return stream.ToArray();
            }
        }

        private static Chunk Decode(string hash, byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (reader.ReadInt32() != Magic) throw new InvalidDataException("Unsupported cue chunk format.");
                string parent = ReadString(reader, 64);
                if (parent.Length != 0 && !ConversationArchiveCheckpoint.Hex(parent, 64))
                    throw new InvalidDataException("Invalid cue parent.");
                var cue = new ConversationCueArchiveRecord {
                    SourceChunkHash = ReadString(reader, 64), SourceRecordIndex = reader.ReadInt32(),
                    PersonId = ReadString(reader, 240), ReceiptId = ReadString(reader, 240),
                    EpisodeId = ReadString(reader, 240), UtteranceId = ReadString(reader, 240),
                    SpeakerId = ReadString(reader, 240), ObservedText = ReadString(reader, 1024),
                    ObservedAt = reader.ReadDouble(),
                    Appraisal = new NpcExperienceAppliedAppraisal {
                        Version = reader.ReadInt32(), MoodDelta = reader.ReadInt32(),
                        AffinityDelta = reader.ReadInt32(), TrustDelta = reader.ReadInt32(),
                        ResentmentDelta = reader.ReadInt32(), FearDelta = reader.ReadInt32(),
                        AppliedMoodDelta = reader.ReadInt32(), AppliedAffinityDelta = reader.ReadInt32(),
                        SpeechActKind = (NpcExperienceSpeechActKind)reader.ReadInt32(),
                        SpeechTargetPersonId = ReadNullable(reader, 240) }
                };
                if (stream.Position != stream.Length) throw new InvalidDataException("Trailing cue data.");
                return new Chunk { Hash = hash, Parent = parent, Bytes = bytes,
                    Cue = ConversationCueArchiveRecord.CopyValidated(cue) };
            }
        }

        private static void Write(BinaryWriter writer, string value)
        { var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); }
        private static void WriteNullable(BinaryWriter writer, string value)
        { writer.Write(value != null); if (value != null) Write(writer, value); }
        private static string ReadNullable(BinaryReader reader, int maximum)
        { return reader.ReadBoolean() ? ReadString(reader, maximum) : null; }
        private static string ReadString(BinaryReader reader, int maximum)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maximum * 4) throw new InvalidDataException("Invalid cue string length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException("Incomplete cue string.");
            string value = new UTF8Encoding(false, true).GetString(bytes);
            if (value.Length > maximum) throw new InvalidDataException("Cue string exceeds limit.");
            return value;
        }
        private static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
