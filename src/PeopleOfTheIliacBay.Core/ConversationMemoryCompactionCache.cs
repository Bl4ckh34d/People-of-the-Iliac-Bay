using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class ConversationMemoryCompactionCacheGroup
    {
        internal string ArchiveId, Head, PersonId, SourceFingerprint, ContextFingerprint;
        public long MonthIndex { get; internal set; }
        internal int SourceCount;
        internal List<SharedConversationArchive.ScanRecord> Candidates;
        internal List<SharedConversationArchive.ScanRecord> Selected;
        public IReadOnlyList<SharedConversationArchive.ScanRecord> CandidateSources { get { return Candidates.AsReadOnly(); } }
        public IReadOnlyList<SharedConversationArchive.ScanRecord> Sources { get { return Selected.AsReadOnly(); } }
    }

    /// <summary>
    /// Non-authoritative, source-addressed extractive cache. It lives outside the
    /// immutable .pim archive and is ignored whenever its checkpoint or context
    /// does not match.
    /// </summary>
    public sealed class ConversationMemoryCompactionCache
    {
        private const int IndexMagic = 0x434D4931;
        private const int GroupMagic = 0x434D4731;
        // The cache is derived and may be rebuilt; v2 keeps branch/person identity
        // once per manifest instead of repeating it in every monthly entry.
        private const int Version = 2;
        private const int MaximumIndexEntries = 4096;
        private const int MaximumGroupFileBytes = 65536;
        private const int MaximumIndexFileBytes = 2 * 1024 * 1024;
        private const int MaximumCandidates = 8;
        private const int MaximumSelected = 4;
        public const string NoContextFingerprint = "0000000000000000000000000000000000000000000000000000000000000000";

        private enum IndexStatus { Complete = 1, Skipped = 2 }

        private sealed class IndexEntry
        {
            internal string ArchiveId, Head, PersonId, SourceFingerprint, ContextFingerprint, GroupFile;
            internal long MonthIndex;
            internal IndexStatus Status;
        }

        private readonly string root;
        private readonly object gate = new object();

        public ConversationMemoryCompactionCache(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Compaction cache directory is required.", nameof(directory));
            root = Path.GetFullPath(directory);
        }

        public bool HasCompletedGroup(ConversationArchiveCheckpointView checkpoint, string personId, long monthIndex)
        { return HasCompletedGroup(checkpoint, personId, monthIndex, NoContextFingerprint); }

        public bool HasCompletedGroup(ConversationArchiveCheckpointView checkpoint, string personId,
            long monthIndex, string contextFingerprint)
        {
            if (!ValidKey(checkpoint, personId, monthIndex, contextFingerprint)) return false;
            List<IndexEntry> entries;
            if (!TryReadIndex(checkpoint.ArchiveId, checkpoint.Head, personId, out entries)) return false;
            return entries.Any(entry => entry.Status == IndexStatus.Complete && entry.MonthIndex == monthIndex &&
                entry.ContextFingerprint == contextFingerprint);
        }

        /// <summary>Loads only groups explicitly named by this person's branch index; no directory scan occurs.</summary>
        public bool TryReadGroups(ConversationArchiveCheckpointView checkpoint, string personId, double now,
            out List<ConversationMemoryCompactionCacheGroup> groups)
        {
            groups = new List<ConversationMemoryCompactionCacheGroup>();
            if (!ValidCheckpoint(checkpoint) || string.IsNullOrWhiteSpace(personId) || !FiniteNonnegative(now)) return false;
            List<IndexEntry> entries;
            if (!TryReadIndex(checkpoint.ArchiveId, checkpoint.Head, personId, out entries)) return false;
            long currentMonth = ConversationMemoryNativeMonth.IndexAt(now);
            var loaded = new HashSet<string>(StringComparer.Ordinal);
            int readAttempts = 0;
            foreach (IndexEntry entry in entries.OrderByDescending(item => item.MonthIndex))
            {
                if (entry.Status != IndexStatus.Complete || entry.MonthIndex > currentMonth ||
                    entry.ContextFingerprint != NoContextFingerprint) continue;
                if (readAttempts >= 24) break;
                readAttempts++;
                ConversationMemoryCompactionCacheGroup group;
                if (!TryReadGroup(entry, out group) || !GroupEligible(group, now)) continue;
                groups.Add(group);
                loaded.Add(entry.GroupFile);
            }
            return true;
        }

        internal bool TryReadIndexedGroups(ConversationArchiveCheckpointView checkpoint, string personId, double now,
            Func<string, IReadOnlyList<SharedConversationArchive.ScanRecord>, ConversationMemoryCompactionContextSnapshot> contextProvider,
            Func<ConversationMemoryCompactionCacheGroup, bool> sourceValidator,
            out List<ConversationMemoryCompactionCacheGroup> groups)
        {
            groups = new List<ConversationMemoryCompactionCacheGroup>();
            if (!ValidCheckpoint(checkpoint) || string.IsNullOrWhiteSpace(personId) || !FiniteNonnegative(now)) return false;
            List<IndexEntry> entries;
            if (!TryReadIndex(checkpoint.ArchiveId, checkpoint.Head, personId, out entries)) return false;
            long currentMonth = ConversationMemoryNativeMonth.IndexAt(now);
            var loaded = new HashSet<string>(StringComparer.Ordinal);
            int readAttempts = 0;
            foreach (IndexEntry entry in entries.OrderByDescending(item => item.MonthIndex))
            {
                if (entry.Status != IndexStatus.Complete || entry.MonthIndex > currentMonth) continue;
                if (readAttempts >= 8) break;
                readAttempts++;
                ConversationMemoryCompactionCacheGroup group;
                if (!TryReadGroup(entry, out group) || !GroupEligible(group, now)) continue;
                if (sourceValidator != null && !sourceValidator(group)) continue;
                if (contextProvider == null)
                {
                    if (group.ContextFingerprint != NoContextFingerprint) continue;
                }
                else
                {
                    ConversationMemoryCompactionContextSnapshot snapshot;
                    try { snapshot = contextProvider(personId, group.Candidates.AsReadOnly()); }
                    catch { continue; }
                    if (snapshot == null || snapshot.PersonId != personId ||
                        snapshot.ContextFingerprint != group.ContextFingerprint) continue;
                }
                groups.Add(group);
                loaded.Add(entry.GroupFile);
            }
            return true;
        }

        internal bool IsSkipped(ConversationArchiveCheckpointView checkpoint, string personId,
            long monthIndex, string sourceFingerprint)
        {
            if (!ValidKey(checkpoint, personId, monthIndex, sourceFingerprint)) return false;
            List<IndexEntry> entries;
            if (!TryReadIndex(checkpoint.ArchiveId, checkpoint.Head, personId, out entries)) return false;
            return entries.Any(entry => entry.Status == IndexStatus.Skipped && entry.MonthIndex == monthIndex &&
                entry.SourceFingerprint == sourceFingerprint);
        }

        internal bool Publish(ConversationArchiveCheckpointView checkpoint, string personId, long monthIndex,
            string sourceFingerprint, string contextFingerprint, IList<SharedConversationArchive.ScanRecord> candidates,
            IList<SharedConversationArchive.ScanRecord> selected, int sourceCount)
        {
            if (!ValidKey(checkpoint, personId, monthIndex, sourceFingerprint) ||
                !ValidFingerprint(contextFingerprint) || candidates == null || selected == null ||
                candidates.Count < 1 || candidates.Count > MaximumCandidates || selected.Count < 1 ||
                selected.Count > MaximumSelected || sourceCount != candidates.Count) return false;
            var group = new ConversationMemoryCompactionCacheGroup
            {
                ArchiveId = checkpoint.ArchiveId,
                Head = checkpoint.Head,
                PersonId = personId,
                MonthIndex = monthIndex,
                SourceFingerprint = sourceFingerprint,
                ContextFingerprint = contextFingerprint,
                SourceCount = sourceCount,
                Candidates = CopyRecords(candidates),
                Selected = CopyRecords(selected)
            };
            if (!ValidateGroup(group)) return false;

            string indexPath = IndexPath(checkpoint.ArchiveId, checkpoint.Head, personId);
            lock (gate)
            {
                List<IndexEntry> entries;
                if (!TryReadIndex(checkpoint.ArchiveId, checkpoint.Head, personId, out entries)) entries = new List<IndexEntry>();
                if (entries.Any(entry => entry.Status == IndexStatus.Complete && entry.MonthIndex == monthIndex &&
                    entry.SourceFingerprint == sourceFingerprint && entry.ContextFingerprint == contextFingerprint)) return true;
                entries.RemoveAll(entry => entry.Status == IndexStatus.Skipped && entry.MonthIndex == monthIndex &&
                    entry.SourceFingerprint == sourceFingerprint);
                // A changed salience snapshot replaces this month's derived view.
                // Free the replaceable index slot before rejecting a full manifest.
                entries.RemoveAll(entry => entry.Status == IndexStatus.Complete && entry.MonthIndex == monthIndex);
                if (entries.Count >= MaximumIndexEntries) return false;
                string groupFile = HashKey(checkpoint.ArchiveId, checkpoint.Head, personId, monthIndex.ToString(),
                    sourceFingerprint, contextFingerprint) + ".cmp";
                string groupPath = Path.Combine(root, "groups", groupFile);
                if (!WriteGroup(groupPath, group)) return false;
                entries.Add(new IndexEntry
                {
                    ArchiveId = checkpoint.ArchiveId, Head = checkpoint.Head, PersonId = personId,
                    MonthIndex = monthIndex, SourceFingerprint = sourceFingerprint,
                    ContextFingerprint = contextFingerprint, GroupFile = groupFile, Status = IndexStatus.Complete
                });
                return WriteIndex(indexPath, entries);
            }
        }

        internal bool MarkSkipped(ConversationArchiveCheckpointView checkpoint, string personId,
            long monthIndex, string sourceFingerprint)
        {
            if (!ValidKey(checkpoint, personId, monthIndex, sourceFingerprint)) return false;
            string indexPath = IndexPath(checkpoint.ArchiveId, checkpoint.Head, personId);
            lock (gate)
            {
                List<IndexEntry> entries;
                if (!TryReadIndex(checkpoint.ArchiveId, checkpoint.Head, personId, out entries)) entries = new List<IndexEntry>();
                if (entries.Any(entry => entry.Status == IndexStatus.Skipped && entry.MonthIndex == monthIndex &&
                    entry.SourceFingerprint == sourceFingerprint)) return true;
                if (entries.Count >= MaximumIndexEntries) return false;
                entries.Add(new IndexEntry
                {
                    ArchiveId = checkpoint.ArchiveId, Head = checkpoint.Head, PersonId = personId,
                    MonthIndex = monthIndex, SourceFingerprint = sourceFingerprint,
                    ContextFingerprint = "", GroupFile = "", Status = IndexStatus.Skipped
                });
                return WriteIndex(indexPath, entries);
            }
        }

        internal static string SourceFingerprint(IList<SharedConversationArchive.ScanRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(records.Count);
                foreach (SharedConversationArchive.ScanRecord record in records)
                {
                    if (record == null || record.Address == null || record.Entry == null)
                        throw new ArgumentException("Invalid source record.", nameof(records));
                    WriteAddress(writer, record.Address);
                    WriteEntry(writer, record.Entry);
                }
                writer.Flush();
                return Hash(stream.ToArray());
            }
        }

        internal static string EntryFingerprint(NpcMemoryEntry entry)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                WriteEntry(writer, entry);
                writer.Flush();
                return Hash(stream.ToArray());
            }
        }

        internal static NpcMemoryEntry CopyEntry(NpcMemoryEntry source)
        {
            return new NpcMemoryEntry { At = source.At, Date = source.Date, Place = source.Place,
                PlayerText = source.PlayerText, NpcText = source.NpcText,
                Observation = source.Observation, Source = source.Source };
        }

        private bool TryReadIndex(string archiveId, string head, string personId, out List<IndexEntry> entries)
        {
            entries = new List<IndexEntry>();
            string path = IndexPath(archiveId, head, personId);
            if (!File.Exists(path)) return true;
            try
            {
                if (new FileInfo(path).Length > MaximumIndexFileBytes) return false;
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadInt32() != IndexMagic || reader.ReadInt32() != Version) return false;
                    string savedArchive = ReadString(reader, 32), savedHead = ReadString(reader, 64), savedPerson = ReadString(reader, 240);
                    if (savedArchive != archiveId || savedHead != head || savedPerson != personId) return false;
                    int count = reader.ReadInt32();
                    if (count < 0 || count > MaximumIndexEntries) return false;
                    for (int i = 0; i < count; i++)
                    {
                        var entry = new IndexEntry
                        {
                            ArchiveId = archiveId, Head = head, PersonId = personId, MonthIndex = reader.ReadInt64(),
                            SourceFingerprint = ReadString(reader, 64), ContextFingerprint = ReadString(reader, 64),
                            Status = (IndexStatus)reader.ReadByte(), GroupFile = ReadString(reader, 72)
                        };
                        if (entry.ArchiveId != archiveId || entry.Head != head || entry.PersonId != personId ||
                            entry.MonthIndex < 0 || !ValidFingerprint(entry.SourceFingerprint)) return false;
                        if (entry.Status == IndexStatus.Complete)
                        {
                            if ((entry.ContextFingerprint != NoContextFingerprint && !ValidFingerprint(entry.ContextFingerprint)) ||
                                entry.GroupFile.Length != 68 || !entry.GroupFile.EndsWith(".cmp", StringComparison.Ordinal) ||
                                !ConversationArchiveCheckpoint.Hex(entry.GroupFile.Substring(0, 64), 64)) return false;
                        }
                        else if (entry.Status == IndexStatus.Skipped)
                        {
                            if (entry.GroupFile.Length != 0 || entry.ContextFingerprint.Length != 0) return false;
                        }
                        else return false;
                        entries.Add(entry);
                    }
                    return stream.Position == stream.Length;
                }
            }
            catch { entries = new List<IndexEntry>(); return false; }
        }

        private bool TryReadGroup(IndexEntry index, out ConversationMemoryCompactionCacheGroup group)
        {
            group = null;
            try
            {
                string path = Path.Combine(root, "groups", index.GroupFile);
                if (!File.Exists(path) || new FileInfo(path).Length > MaximumGroupFileBytes) return false;
                using (var stream = File.OpenRead(path))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    if (reader.ReadInt32() != GroupMagic || reader.ReadInt32() != Version) return false;
                    var found = new ConversationMemoryCompactionCacheGroup
                    {
                        ArchiveId = ReadString(reader, 32), Head = ReadString(reader, 64),
                        PersonId = ReadString(reader, 240), MonthIndex = reader.ReadInt64(),
                        SourceFingerprint = ReadString(reader, 64), ContextFingerprint = ReadString(reader, 64),
                        SourceCount = reader.ReadInt32()
                    };
                    int candidateCount = reader.ReadInt32();
                    if (candidateCount < 1 || candidateCount > MaximumCandidates) return false;
                    found.Candidates = ReadRecords(reader, found.ArchiveId, found.Head, found.PersonId, candidateCount);
                    int selectedCount = reader.ReadInt32();
                    if (selectedCount < 1 || selectedCount > MaximumSelected) return false;
                    found.Selected = ReadRecords(reader, found.ArchiveId, found.Head, found.PersonId, selectedCount);
                    if (stream.Position != stream.Length || index.Status != IndexStatus.Complete ||
                        found.ArchiveId != index.ArchiveId || found.Head != index.Head || found.PersonId != index.PersonId ||
                        found.MonthIndex != index.MonthIndex || found.SourceFingerprint != index.SourceFingerprint ||
                        found.ContextFingerprint != index.ContextFingerprint || !ValidateGroup(found)) return false;
                    group = found;
                    return true;
                }
            }
            catch { return false; }
        }

        private bool WriteGroup(string path, ConversationMemoryCompactionCacheGroup group)
        {
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write(GroupMagic); writer.Write(Version);
                    WriteString(writer, group.ArchiveId); WriteString(writer, group.Head); WriteString(writer, group.PersonId);
                    writer.Write(group.MonthIndex); WriteString(writer, group.SourceFingerprint);
                    WriteString(writer, group.ContextFingerprint); writer.Write(group.SourceCount);
                    writer.Write(group.Candidates.Count); foreach (var record in group.Candidates) WriteRecord(writer, record);
                    writer.Write(group.Selected.Count); foreach (var record in group.Selected) WriteRecord(writer, record);
                    writer.Flush();
                    if (stream.Length > MaximumGroupFileBytes) return false;
                    return AtomicWrite(path, stream.ToArray());
                }
            }
            catch { return false; }
        }

        private bool WriteIndex(string path, List<IndexEntry> entries)
        {
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write(IndexMagic); writer.Write(Version);
                    // The filename is a hash, so persist the branch identity from the entries.
                    if (entries.Count == 0) return false;
                    WriteString(writer, entries[0].ArchiveId); WriteString(writer, entries[0].Head); WriteString(writer, entries[0].PersonId);
                    writer.Write(entries.Count);
                    foreach (IndexEntry entry in entries)
                    {
                        writer.Write(entry.MonthIndex); WriteString(writer, entry.SourceFingerprint);
                        WriteString(writer, entry.ContextFingerprint); writer.Write((byte)entry.Status); WriteString(writer, entry.GroupFile);
                    }
                    writer.Flush();
                    if (stream.Length > MaximumIndexFileBytes) return false;
                    return AtomicWrite(path, stream.ToArray());
                }
            }
            catch { return false; }
        }

        private bool AtomicWrite(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                if (!File.Exists(path)) { File.Move(temp, path); return true; }
                try { File.Replace(temp, path, null); return true; }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
                File.Delete(path);
                File.Move(temp, path);
                return true;
            }
            catch { return false; }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static List<SharedConversationArchive.ScanRecord> ReadRecords(BinaryReader reader,
            string archiveId, string head, string personId, int count)
        {
            var records = new List<SharedConversationArchive.ScanRecord>(count);
            for (int i = 0; i < count; i++)
            {
                string savedArchive = ReadString(reader, 32), savedHead = ReadString(reader, 64);
                string chunkHash = ReadString(reader, 64); int index = reader.ReadInt32();
                string savedPerson = ReadString(reader, 240);
                if (savedArchive != archiveId || savedHead != head || savedPerson != personId ||
                    !ConversationArchiveCheckpoint.Hex(chunkHash, 64) || index < 0 || index >= 128)
                    throw new InvalidDataException("Invalid cached source address.");
                var address = new SharedConversationArchive.RecordAddress(savedArchive, savedHead, chunkHash, index, savedPerson);
                var entry = ReadEntry(reader);
                records.Add(new SharedConversationArchive.ScanRecord(address, entry));
            }
            return records;
        }

        private static void WriteRecord(BinaryWriter writer, SharedConversationArchive.ScanRecord record)
        { WriteAddress(writer, record.Address); WriteEntry(writer, record.Entry); }

        private static void WriteAddress(BinaryWriter writer, SharedConversationArchive.RecordAddress address)
        {
            WriteString(writer, address.ArchiveId); WriteString(writer, address.CheckpointHead);
            WriteString(writer, address.ChunkHash); writer.Write(address.RecordIndex); WriteString(writer, address.PersonId);
        }

        private static void WriteEntry(BinaryWriter writer, NpcMemoryEntry entry)
        {
            writer.Write(BitConverter.DoubleToInt64Bits(entry.At));
            WriteString(writer, entry.Date); WriteString(writer, entry.Place); WriteString(writer, entry.PlayerText);
            WriteString(writer, entry.NpcText); WriteString(writer, entry.Observation); WriteString(writer, entry.Source);
        }

        private static NpcMemoryEntry ReadEntry(BinaryReader reader)
        {
            var entry = new NpcMemoryEntry { At = BitConverter.Int64BitsToDouble(reader.ReadInt64()),
                Date = ReadString(reader, 120), Place = ReadString(reader, 300), PlayerText = ReadString(reader, 600),
                NpcText = ReadString(reader, 1600), Observation = ReadString(reader, 600), Source = ReadString(reader, 16) };
            WorldStateStore.ValidateMemoryEntry(entry);
            return entry;
        }

        private bool ValidateGroup(ConversationMemoryCompactionCacheGroup group)
        {
            if (group == null || !ConversationArchiveCheckpoint.Hex(group.ArchiveId, 32) ||
                group.Head == null || group.Head.Length != 0 && !ConversationArchiveCheckpoint.Hex(group.Head, 64) ||
                string.IsNullOrWhiteSpace(group.PersonId) || group.PersonId.Length > 240 || group.MonthIndex < 0 ||
                !ValidFingerprint(group.SourceFingerprint) || group.ContextFingerprint != NoContextFingerprint && !ValidFingerprint(group.ContextFingerprint) ||
                group.SourceCount < 1 || group.SourceCount != group.Candidates.Count || group.Candidates.Count > MaximumCandidates ||
                group.Selected.Count < 1 || group.Selected.Count > MaximumSelected) return false;
            if (SourceFingerprint(group.Candidates) != group.SourceFingerprint) return false;
            double start = ConversationMemoryNativeMonth.StartAt(group.MonthIndex);
            double end = start + ConversationMemoryNativeMonth.SecondsPerMonth;
            var candidateAddresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (SharedConversationArchive.ScanRecord record in group.Candidates)
                if (!ValidRecord(record, group, start, end) || !candidateAddresses.Add(AddressKey(record.Address))) return false;
            var selectedAddresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (SharedConversationArchive.ScanRecord selected in group.Selected)
            {
                if (!ValidRecord(selected, group, start, end) || !selectedAddresses.Add(AddressKey(selected.Address))) return false;
                SharedConversationArchive.ScanRecord candidate = group.Candidates.FirstOrDefault(item =>
                    AddressKey(item.Address) == AddressKey(selected.Address));
                if (candidate == null || EntryFingerprint(candidate.Entry) != EntryFingerprint(selected.Entry)) return false;
            }
            return true;
        }

        private static bool ValidRecord(SharedConversationArchive.ScanRecord record,
            ConversationMemoryCompactionCacheGroup group, double start, double end)
        {
            if (record == null || record.Address == null || record.Entry == null) return false;
            SharedConversationArchive.RecordAddress address = record.Address;
            if (address.ArchiveId != group.ArchiveId || address.CheckpointHead != group.Head ||
                address.PersonId != group.PersonId || !ConversationArchiveCheckpoint.Hex(address.ChunkHash, 64) ||
                address.RecordIndex < 0 || address.RecordIndex >= 128 || !(record.Entry.At >= start && record.Entry.At < end)) return false;
            try { WorldStateStore.ValidateMemoryEntry(record.Entry); return true; }
            catch { return false; }
        }

        private static List<SharedConversationArchive.ScanRecord> CopyRecords(IList<SharedConversationArchive.ScanRecord> source)
        {
            var result = new List<SharedConversationArchive.ScanRecord>(source.Count);
            foreach (SharedConversationArchive.ScanRecord record in source)
            {
                var address = record.Address;
                result.Add(new SharedConversationArchive.ScanRecord(new SharedConversationArchive.RecordAddress(
                    address.ArchiveId, address.CheckpointHead, address.ChunkHash, address.RecordIndex, address.PersonId),
                    CopyEntry(record.Entry)));
            }
            return result;
        }

        private static bool GroupEligible(ConversationMemoryCompactionCacheGroup group, double now)
        {
            foreach (SharedConversationArchive.ScanRecord record in group.Candidates)
                if (record.Entry.At > now || !ConversationMemoryCompactionEligibility.IsEligible(record.Entry.At, now)) return false;
            return true;
        }

        private string IndexPath(string archiveId, string head, string personId)
        { return Path.Combine(root, "manifests", HashKey(archiveId, head, personId) + ".idx"); }

        private static string AddressKey(SharedConversationArchive.RecordAddress address)
        { return address.ArchiveId + ":" + address.CheckpointHead + ":" + address.ChunkHash + ":" + address.RecordIndex + ":" + address.PersonId; }

        private static bool ValidKey(ConversationArchiveCheckpointView checkpoint, string personId,
            long monthIndex, string fingerprint)
        { return ValidCheckpoint(checkpoint) && !string.IsNullOrWhiteSpace(personId) && personId.Length <= 240 && monthIndex >= 0 &&
            (fingerprint == "" || ValidFingerprint(fingerprint)); }

        private static bool ValidCheckpoint(ConversationArchiveCheckpointView checkpoint)
        { return checkpoint != null && ConversationArchiveCheckpoint.Hex(checkpoint.ArchiveId, 32) &&
            (checkpoint.Head.Length == 0 || ConversationArchiveCheckpoint.Hex(checkpoint.Head, 64)); }

        private static bool ValidFingerprint(string fingerprint)
        { return ConversationArchiveCheckpoint.Hex(fingerprint, 64); }

        private static bool FiniteNonnegative(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }

        private static string HashKey(params string[] values)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            { foreach (string value in values) WriteString(writer, value); writer.Flush(); return Hash(stream.ToArray()); }
        }

        private static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
            writer.Write(bytes.Length); writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader, int maximumCharacters)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maximumCharacters * 4) throw new InvalidDataException("Invalid cache string length.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length) throw new EndOfStreamException("Incomplete cache string.");
            string value = new UTF8Encoding(false, true).GetString(bytes);
            if (value.Length > maximumCharacters) throw new InvalidDataException("Cache string exceeds its limit.");
            return value;
        }
    }
}
