using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using PeopleOfTheIliacBay.Core;

internal sealed class PortablePackageManifest
{
    public int FormatVersion { get; set; }
    public string ArchiveId { get; set; }
    public string Head { get; set; }
    public List<string> Ancestry { get; set; }
    public string CueHead { get; set; }
    public List<string> CueAncestry { get; set; }
    public List<PortableFileManifest> Files { get; set; }
}

internal sealed class PortableFileManifest
{
    public string Path { get; set; }
    public long Length { get; set; }
    public string Sha256 { get; set; }
}

internal static class Program
{
    private const int ManifestVersion = 2;
    private const int ChunkMagic = 0x50494d31;
    private const int ChunkMagicWithProvenance = 0x50494d32;
    private const int CueChunkMagic = 0x50494331;
    private const long MaximumPackageBytes = 4L * 1024 * 1024 * 1024;
    private const int MaximumChunkBytes = 2 * 1024 * 1024;
    private const int MaximumCueChunkBytes = 16384;
    private const int MaximumManifestBytes = 1024 * 1024;
    private const string SaveDataFile = "mod_peopleoftheiliacbay.txt";

    private static int Main(string[] args)
    {
        try
        {
            if(System.Diagnostics.Process.GetProcessesByName("DaggerfallUnity").Length>0)
                throw new IOException("Close Daggerfall Unity before exporting or importing saves.");
            if(args.Length==3 && args[0]=="export") Export(args[1],args[2]);
            else if(args.Length==4 && args[0]=="import") Import(args[1],args[2],args[3]);
            else throw new ArgumentException("Usage: export <SAVE folder> <new zip> | import <zip> <Saves root> <new SAVE number, e.g. SAVE12>");
            return 0;
        }
        catch(Exception e)
        {
            Console.Error.WriteLine("Memory save transfer failed: "+e.Message);
            return 1;
        }
    }

    private static (ConversationArchiveCheckpoint Memory, ConversationCueArchiveCheckpoint Cue) Checkpoints(string save)
    {
        string file=Path.Combine(save,SaveDataFile);
        using(var json=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions {MaxDepth=128}))
        {
            var root=json.RootElement;
            JsonElement value;
            int version=root.TryGetProperty("Version",out value) && value.ValueKind==JsonValueKind.Number
                ? value.GetInt32() : 0;
            var options=new JsonSerializerOptions {IncludeFields=true};
            ConversationArchiveCheckpoint memory=null;
            ConversationCueArchiveCheckpoint cue=null;
            if(root.TryGetProperty("ConversationArchive",out value) && value.ValueKind!=JsonValueKind.Null)
                memory=ConversationArchiveCheckpoint.CopyValidated(
                    JsonSerializer.Deserialize<ConversationArchiveCheckpoint>(value.GetRawText(),options));
            if(root.TryGetProperty("ConversationCueArchive",out value) && value.ValueKind!=JsonValueKind.Null)
                cue=ConversationCueArchiveCheckpoint.CopyValidated(
                    JsonSerializer.Deserialize<ConversationCueArchiveCheckpoint>(value.GetRawText(),options));
            if(version<54 && cue!=null || version>=54 &&
                ((memory==null)!=(cue==null) || memory!=null && memory.ArchiveId!=cue.ArchiveId))
                throw new InvalidDataException("Save has an invalid or mismatched conversation cue checkpoint.");
            return (memory,cue);
        }
    }

    private static void Export(string source,string output)
    {
        source=Path.GetFullPath(source); output=Path.GetFullPath(output);
        if(!Directory.Exists(source)) throw new DirectoryNotFoundException("Source SAVE folder does not exist.");
        if(File.Exists(output) || Directory.Exists(output)) throw new IOException("Export destination already exists.");
        if(string.IsNullOrWhiteSpace(Path.GetDirectoryName(output))) throw new IOException("Choose an export destination folder.");

        var checkpoints=Checkpoints(source);
        string saveRoot=Directory.GetParent(source)?.FullName;
        if(string.IsNullOrWhiteSpace(saveRoot)) throw new IOException("Could not locate the configured Saves folder beside this SAVE slot.");
        string stage=Path.Combine(Path.GetTempPath(),"people-export-"+Guid.NewGuid().ToString("N"));
        string temporary=output+"."+Guid.NewGuid().ToString("N")+".tmp";
        Directory.CreateDirectory(stage);
        try
        {
            CopySave(source,Path.Combine(stage,"Save"));
            new SharedConversationArchive(Path.Combine(saveRoot,"PeopleMemory"))
                .CopyCheckpointTo(checkpoints.Memory,Path.Combine(stage,"PeopleMemory"));
            new SharedConversationCueArchive(Path.Combine(saveRoot,"PeopleMemory"))
                .CopyCheckpointTo(checkpoints.Cue,Path.Combine(stage,"PeopleMemory"));

            var copied=Checkpoints(Path.Combine(stage,"Save"));
            if((copied.Memory?.ArchiveId)!=(checkpoints.Memory?.ArchiveId) ||
                (copied.Memory?.Head)!=(checkpoints.Memory?.Head) ||
                (copied.Cue?.ArchiveId)!=(checkpoints.Cue?.ArchiveId) ||
                (copied.Cue?.Head)!=(checkpoints.Cue?.Head))
                throw new IOException("Save changed during export. Close DFU and retry.");

            var manifest=CreateManifest(stage,checkpoints.Memory,checkpoints.Cue);
            File.WriteAllBytes(Path.Combine(stage,"manifest.json"),JsonSerializer.SerializeToUtf8Bytes(manifest,ManifestJsonOptions()));
            ValidatePackageDirectory(stage,manifest);

            Directory.CreateDirectory(Path.GetDirectoryName(output));
            ZipFile.CreateFromDirectory(stage,temporary,CompressionLevel.Optimal,false);
            File.Move(temporary,output);
            Console.WriteLine("Exported save and exact memory ancestry with verified manifest: "+output);
        }
        finally
        {
            if(Directory.Exists(stage)) Directory.Delete(stage,true);
            if(File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static PortablePackageManifest CreateManifest(string packageRoot,
        ConversationArchiveCheckpoint checkpoint,ConversationCueArchiveCheckpoint cueCheckpoint)
    {
        var ancestry=ReadAncestryHashes(Path.Combine(packageRoot,"PeopleMemory"),checkpoint);
        var cueAncestry=ReadCueAncestryHashes(Path.Combine(packageRoot,"PeopleMemory"),cueCheckpoint);
        var files=Directory.GetFiles(packageRoot,"*",SearchOption.AllDirectories)
            .Where(path=>!string.Equals(Path.GetFullPath(path),Path.GetFullPath(Path.Combine(packageRoot,"manifest.json")),StringComparison.OrdinalIgnoreCase))
            .Select(path=>ManifestFile(packageRoot,path))
            .OrderBy(file=>file.Path,StringComparer.Ordinal)
            .ToList();
        long total=0;
        foreach(var file in files)
        {
            total=checked(total+file.Length);
            if(total>MaximumPackageBytes) throw new InvalidDataException("Save exceeds the 4 GiB portable package limit.");
        }
        return new PortablePackageManifest {
            FormatVersion=ManifestVersion,
            ArchiveId=checkpoint?.ArchiveId??"",
            Head=checkpoint?.Head??"",
            Ancestry=ancestry,
            CueHead=cueCheckpoint?.Head??"",
            CueAncestry=cueAncestry,
            Files=files
        };
    }

    private static PortableFileManifest ManifestFile(string root,string path)
    {
        byte[] hash;
        long length;
        using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
        using(var sha=SHA256.Create())
        {
            hash=sha.ComputeHash(input);
            length=input.Length;
        }
        return new PortableFileManifest {
            Path=RelativePackagePath(root,path),
            Length=length,
            Sha256=ToHex(hash)
        };
    }

    private static void CopySave(string source,string target)
    {
        if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0) throw new IOException("Linked save directories are unsupported.");
        Directory.CreateDirectory(target);
        foreach(string entry in Directory.GetFileSystemEntries(source))
        {
            var attributes=File.GetAttributes(entry);
            if((attributes&FileAttributes.ReparsePoint)!=0) throw new IOException("Linked save entries are unsupported.");
            string destination=Path.Combine(target,Path.GetFileName(entry));
            if((attributes&FileAttributes.Directory)!=0) CopySave(entry,destination); else File.Copy(entry,destination,false);
        }
    }

    private static void Import(string package,string saves,string slot)
    {
        if((string.IsNullOrEmpty(slot) || !slot.StartsWith("SAVE",StringComparison.Ordinal) ||
            slot.Length==4 || slot.Skip(4).Any(c=>c<'0' || c>'9')))
            throw new ArgumentException("Destination must be a new SAVE number, such as SAVE12.");
        package=Path.GetFullPath(package); saves=Path.GetFullPath(saves);
        if(!File.Exists(package)) throw new FileNotFoundException("Portable save package does not exist.",package);
        string target=Path.Combine(saves,slot);
        if(Directory.Exists(target) || File.Exists(target)) throw new IOException("Destination save already exists; it will not be overwritten.");
        Directory.CreateDirectory(saves);
        string stage=Path.Combine(saves,".people-import-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            PortablePackageManifest manifest;
            bool legacy;
            ExtractAndValidatePackage(package,stage,out manifest,out legacy);
            var checkpoints=Checkpoints(Path.Combine(stage,"Save"));
            ValidateCheckpointMatchesManifest(checkpoints.Memory,checkpoints.Cue,manifest);

            var expectedAncestry=ReadAncestryHashes(Path.Combine(stage,"PeopleMemory"),checkpoints.Memory);
            var expectedCueAncestry=ReadCueAncestryHashes(Path.Combine(stage,"PeopleMemory"),checkpoints.Cue);
            if(manifest!=null && !expectedAncestry.SequenceEqual(manifest.Ancestry,StringComparer.Ordinal))
                throw new InvalidDataException("Manifest ancestry does not match this save's exact checkpoint chain.");
            if(manifest!=null && manifest.FormatVersion==2 &&
                !expectedCueAncestry.SequenceEqual(manifest.CueAncestry,StringComparer.Ordinal))
                throw new InvalidDataException("Manifest cue ancestry does not match this save's exact checkpoint chain.");
            ValidateExactArchiveFiles(Path.Combine(stage,"PeopleMemory"),checkpoints.Memory,expectedAncestry,
                checkpoints.Cue,expectedCueAncestry);
            ValidatePackageDirectory(stage,manifest);

            // The archive reader validates chunk encoding and full ancestry before destination writes.
            new SharedConversationArchive(Path.Combine(stage,"PeopleMemory"))
                .CopyCheckpointTo(checkpoints.Memory,Path.Combine(stage,"VerifiedPeopleMemory"));
            new SharedConversationCueArchive(Path.Combine(stage,"PeopleMemory"))
                .CopyCheckpointTo(checkpoints.Cue,Path.Combine(stage,"VerifiedPeopleMemory"));

            string memoryRoot=Path.Combine(saves,"PeopleMemory");
            PreflightExistingChunks(memoryRoot,checkpoints.Memory,expectedAncestry,
                checkpoints.Cue,expectedCueAncestry);
            if(Directory.Exists(target) || File.Exists(target)) throw new IOException("Destination save was created during import; it was not overwritten.");

            // Chunks are immutable and content-addressed. A crash here can leave only
            // unreferenced chunks; the save slot is published only after every chunk validates.
            new SharedConversationArchive(Path.Combine(stage,"PeopleMemory"))
                .CopyCheckpointTo(checkpoints.Memory,memoryRoot);
            new SharedConversationCueArchive(Path.Combine(stage,"PeopleMemory"))
                .CopyCheckpointTo(checkpoints.Cue,memoryRoot);
            Directory.Move(Path.Combine(stage,"Save"),target);

            if(legacy)
                Console.WriteLine("Imported legacy package without manifest. Archive ancestry passed SHA-256 validation, but save-file SHA-256 checks were unavailable.");
            else
                Console.WriteLine("Imported save and verified exact memory ancestry: "+target);
        }
        finally
        {
            if(Directory.Exists(stage)) Directory.Delete(stage,true);
        }
    }

    private static void ExtractAndValidatePackage(string package,string stage,out PortablePackageManifest manifest,out bool legacy)
    {
        manifest=null; legacy=false;
        using(var zip=ZipFile.OpenRead(package))
        {
            var entries=zip.Entries.ToList();
            if(entries.Count>100000) throw new InvalidDataException("Portable save package contains too many entries.");
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total=0,extractedTotal=0;
            int manifestCount=0;
            foreach(var entry in entries)
            {
                string name=NormalizeArchivePath(entry.FullName);
                bool directory=name.EndsWith("/",StringComparison.Ordinal);
                ValidatePackageEntryName(name,directory);
                if(!names.Add(name)) throw new InvalidDataException("Portable package contains duplicate paths.");
                if(IsSymbolicLink(entry)) throw new InvalidDataException("Linked files are unsupported in portable save packages.");
                if(!directory)
                {
                    if(name=="manifest.json") manifestCount++;
                    total=checked(total+entry.Length);
                    if(total>MaximumPackageBytes) throw new InvalidDataException("Portable save package exceeds 4 GiB limit.");
                }
            }
            if(manifestCount>1) throw new InvalidDataException("Portable package contains multiple manifests.");
            legacy=manifestCount==0;
            if(legacy) Console.WriteLine("Legacy package detected: verifying SAVE structure and exact archive ancestry; save-file manifest checks are unavailable.");
            else manifest=ReadManifest(zip.GetEntry("manifest.json"));

            Dictionary<string,PortableFileManifest> expectedFiles=null;
            if(manifest!=null)
            {
                ValidateManifestShape(manifest);
                expectedFiles=manifest.Files.ToDictionary(file=>file.Path,StringComparer.OrdinalIgnoreCase);
                var actualFiles=entries.Where(entry=>!NormalizeArchivePath(entry.FullName).EndsWith("/",StringComparison.Ordinal) &&
                    NormalizeArchivePath(entry.FullName)!="manifest.json").ToList();
                if(actualFiles.Count!=expectedFiles.Count || actualFiles.Any(entry=>!expectedFiles.ContainsKey(NormalizeArchivePath(entry.FullName))))
                    throw new InvalidDataException("Portable package has unlisted files or is missing manifest-listed files.");
            }
            ValidateManifestDirectories(entries,manifest);

            foreach(var entry in entries)
            {
                string name=NormalizeArchivePath(entry.FullName);
                string path=Path.GetFullPath(Path.Combine(stage,name.Replace('/',Path.DirectorySeparatorChar)));
                if(!IsWithin(stage,path)) throw new InvalidDataException("Portable package path escapes the staging folder.");
                if(name.EndsWith("/",StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(path);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var input=entry.Open())
                using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                using(var sha=SHA256.Create())
                {
                    long copied=CopyAndHash(input,output,sha,MaximumPackageBytes);
                    extractedTotal=checked(extractedTotal+copied);
                    if(extractedTotal>MaximumPackageBytes) throw new InvalidDataException("Portable save package exceeds 4 GiB limit.");
                    if(copied!=entry.Length) throw new InvalidDataException("Portable package entry length did not match its ZIP record.");
                    if(manifest!=null && name!="manifest.json")
                    {
                        var expected=expectedFiles[name];
                        if(copied!=expected.Length || ToHex(sha.Hash)!=expected.Sha256)
                            throw new InvalidDataException("Portable package checksum or length failed for "+name+".");
                    }
                }
            }
        }
        if(!Directory.Exists(Path.Combine(stage,"Save"))) throw new InvalidDataException("Portable package has no Save folder.");
        if(!File.Exists(Path.Combine(stage,"Save",SaveDataFile))) throw new InvalidDataException("Portable package is missing the People of the Iliac Bay save payload.");
        if(manifest!=null)
        {
            var checkpoints=Checkpoints(Path.Combine(stage,"Save"));
            ValidateCheckpointMatchesManifest(checkpoints.Memory,checkpoints.Cue,manifest);
        }
    }

    private static PortablePackageManifest ReadManifest(ZipArchiveEntry entry)
    {
        if(entry.Length>MaximumManifestBytes) throw new InvalidDataException("Portable package manifest is too large.");
        using(var input=entry.Open())
        using(var memory=new MemoryStream())
        {
            long copied=CopyAndHash(input,memory,null,MaximumManifestBytes);
            if(copied!=entry.Length) throw new InvalidDataException("Portable package manifest length did not match its ZIP record.");
            try {return JsonSerializer.Deserialize<PortablePackageManifest>(memory.ToArray(),ManifestJsonOptions());}
            catch(JsonException e) {throw new InvalidDataException("Portable package manifest is invalid JSON.",e);}
        }
    }

    private static void ValidateManifestShape(PortablePackageManifest manifest)
    {
        if(manifest==null || (manifest.FormatVersion!=1 && manifest.FormatVersion!=ManifestVersion) ||
            manifest.Ancestry==null || manifest.Files==null ||
            manifest.FormatVersion==1 && (manifest.CueHead!=null || manifest.CueAncestry!=null) ||
            manifest.FormatVersion==2 && (manifest.CueHead==null || manifest.CueAncestry==null))
            throw new InvalidDataException("Unsupported or incomplete portable package manifest.");
        if(manifest.ArchiveId==null || manifest.Head==null ||
            manifest.ArchiveId.Length!=0 && !IsLowerHex(manifest.ArchiveId,32) ||
            manifest.Head.Length!=0 && !IsLowerHex(manifest.Head,64))
            throw new InvalidDataException("Portable package manifest has an invalid checkpoint.");
        var seenHashes=new HashSet<string>(StringComparer.Ordinal);
        foreach(string hash in manifest.Ancestry)
            if(!IsLowerHex(hash,64) || !seenHashes.Add(hash)) throw new InvalidDataException("Portable package manifest ancestry is invalid.");
        if(manifest.FormatVersion==2)
        {
            if(manifest.CueHead.Length!=0 && !IsLowerHex(manifest.CueHead,64))
                throw new InvalidDataException("Portable package manifest cue head is invalid.");
            seenHashes.Clear();
            foreach(string hash in manifest.CueAncestry)
                if(!IsLowerHex(hash,64) || !seenHashes.Add(hash))
                    throw new InvalidDataException("Portable package manifest cue ancestry is invalid.");
            if((manifest.CueHead.Length==0)!=(manifest.CueAncestry.Count==0) ||
                manifest.CueAncestry.Count>0 && manifest.CueAncestry[0]!=manifest.CueHead)
                throw new InvalidDataException("Portable package manifest cue head and ancestry disagree.");
        }
        var seenPaths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in manifest.Files)
        {
            if(file==null || file.Length<0 || !IsLowerHex(file.Sha256,64))
                throw new InvalidDataException("Portable package manifest file entry is invalid.");
            ValidateManifestFilePath(file.Path);
            if(!seenPaths.Add(file.Path)) throw new InvalidDataException("Portable package manifest contains duplicate file paths.");
        }
        if(!seenPaths.Contains("Save/"+SaveDataFile)) throw new InvalidDataException("Portable package manifest omits the save payload.");
    }

    private static void ValidateManifestFilePath(string path)
    {
        if(string.IsNullOrWhiteSpace(path) || path=="manifest.json" || path.EndsWith("/",StringComparison.Ordinal) ||
            NormalizeArchivePath(path)!=path || !(path.StartsWith("Save/",StringComparison.Ordinal) || path.StartsWith("PeopleMemory/",StringComparison.Ordinal)))
            throw new InvalidDataException("Portable package manifest contains an unsafe file path.");
        var parts=path.Split('/');
        if(parts.Any(part=>part.Length==0 || part=="." || part==".." || part.Contains(":")))
            throw new InvalidDataException("Portable package manifest contains an unsafe file path.");
    }

    private static void ValidatePackageEntryName(string name,bool directory)
    {
        string trimmed=directory?name.Substring(0,name.Length-1):name;
        if(string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("/",StringComparison.Ordinal) || trimmed.Contains(":") ||
            trimmed.Split('/').Any(part=>part.Length==0 || part=="." || part==".."))
            throw new InvalidDataException("Unsafe portable package path.");
        if(trimmed!="manifest.json" && !(trimmed.StartsWith("Save/",StringComparison.Ordinal) || trimmed=="Save" ||
            trimmed.StartsWith("PeopleMemory/",StringComparison.Ordinal) || trimmed=="PeopleMemory"))
            throw new InvalidDataException("Unexpected path in portable save package.");
        if(directory && trimmed=="manifest.json") throw new InvalidDataException("Manifest path must name a file.");
    }

    private static void ValidateManifestDirectories(List<ZipArchiveEntry> entries,PortablePackageManifest manifest)
    {
        var expectedDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {"Save/"};
        var files=manifest==null
            ? entries.Where(entry=>!NormalizeArchivePath(entry.FullName).EndsWith("/",StringComparison.Ordinal) &&
                    NormalizeArchivePath(entry.FullName)!="manifest.json").Select(entry=>NormalizeArchivePath(entry.FullName))
            : manifest.Files.Select(file=>file.Path);
        foreach(string path in files)
        {
            string[] parts=path.Split('/');
            string current="";
            for(int i=0;i<parts.Length-1;i++)
            {
                current+=parts[i]+"/";
                expectedDirectories.Add(current);
            }
        }
        foreach(var entry in entries)
        {
            string name=NormalizeArchivePath(entry.FullName);
            if(name.EndsWith("/",StringComparison.Ordinal) && !expectedDirectories.Contains(name))
                throw new InvalidDataException("Portable package has an unlisted directory.");
        }
    }

    private static void ValidateCheckpointMatchesManifest(ConversationArchiveCheckpoint checkpoint,
        ConversationCueArchiveCheckpoint cueCheckpoint,PortablePackageManifest manifest)
    {
        if(manifest==null) return;
        if((checkpoint?.ArchiveId??"")!=manifest.ArchiveId || (checkpoint?.Head??"")!=manifest.Head ||
            (cueCheckpoint?.Head??"")!=(manifest.FormatVersion==2 ? manifest.CueHead : "") ||
            manifest.FormatVersion==1 && cueCheckpoint!=null && cueCheckpoint.Head.Length!=0)
            throw new InvalidDataException("Portable package manifest checkpoint does not match the save payload.");
    }

    private static void ValidatePackageDirectory(string packageRoot,PortablePackageManifest manifest)
    {
        var actual=Directory.GetFiles(packageRoot,"*",SearchOption.AllDirectories)
            .Where(path=>!string.Equals(Path.GetFullPath(path),Path.GetFullPath(Path.Combine(packageRoot,"manifest.json")),StringComparison.OrdinalIgnoreCase))
            .Select(path=>RelativePackagePath(packageRoot,path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var listed=manifest==null
            ? actual
            : new HashSet<string>(manifest.Files.Select(file=>file.Path),StringComparer.OrdinalIgnoreCase);
        if(!actual.SetEquals(listed)) throw new InvalidDataException("Portable package has unlisted files or is missing manifest-listed files.");
        if(manifest!=null)
        {
            foreach(var file in manifest.Files)
            {
                var actualFile=ManifestFile(packageRoot,Path.Combine(packageRoot,file.Path.Replace('/',Path.DirectorySeparatorChar)));
                if(actualFile.Length!=file.Length || actualFile.Sha256!=file.Sha256)
                    throw new InvalidDataException("Portable package checksum or length failed for "+file.Path+".");
            }
        }
    }

    private static List<string> ReadAncestryHashes(string memoryRoot,ConversationArchiveCheckpoint checkpoint)
    {
        // Walk only the hash-linked parent header; SharedConversationArchive still validates every full chunk.
        var result=new List<string>();
        if(checkpoint==null || checkpoint.Head.Length==0) return result;
        string hash=checkpoint.Head;
        var seen=new HashSet<string>(StringComparer.Ordinal);
        while(hash.Length>0)
        {
            if(!IsLowerHex(hash,64) || !seen.Add(hash)) throw new InvalidDataException("Conversation archive ancestry contains an invalid hash or cycle.");
            string path=Path.Combine(memoryRoot,checkpoint.ArchiveId,hash.Substring(0,2),hash+".pim");
            byte[] bytes=ReadChunk(path,hash,MaximumChunkBytes);
            result.Add(hash);
            hash=ReadChunkParent(bytes,ChunkMagic);
        }
        return result;
    }

    private static List<string> ReadCueAncestryHashes(string memoryRoot,ConversationCueArchiveCheckpoint checkpoint)
    {
        var result=new List<string>();
        if(checkpoint==null || checkpoint.Head.Length==0) return result;
        // Core validates every cue payload, not only its hash-linked parent header.
        new SharedConversationCueArchive(memoryRoot).Load(checkpoint);
        string hash=checkpoint.Head;
        var seen=new HashSet<string>(StringComparer.Ordinal);
        while(hash.Length>0)
        {
            if(!IsLowerHex(hash,64) || !seen.Add(hash))
                throw new InvalidDataException("Conversation cue ancestry contains an invalid hash or cycle.");
            string path=Path.Combine(memoryRoot,checkpoint.ArchiveId,hash.Substring(0,2),hash+".pic");
            byte[] bytes=ReadChunk(path,hash,MaximumCueChunkBytes);
            result.Add(hash);
            hash=ReadChunkParent(bytes,CueChunkMagic);
        }
        return result;
    }

    private static void ValidateExactArchiveFiles(string memoryRoot,ConversationArchiveCheckpoint checkpoint,
        List<string> ancestry,ConversationCueArchiveCheckpoint cueCheckpoint,List<string> cueAncestry)
    {
        var expected=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(checkpoint!=null)
            foreach(string hash in ancestry)
                expected.Add(RelativeChunkPath(checkpoint.ArchiveId,hash,".pim"));
        if(cueCheckpoint!=null)
            foreach(string hash in cueAncestry)
                expected.Add(RelativeChunkPath(cueCheckpoint.ArchiveId,hash,".pic"));
        var actual=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if(Directory.Exists(memoryRoot))
            foreach(string path in Directory.GetFiles(memoryRoot,"*",SearchOption.AllDirectories))
                actual.Add(RelativePackagePath(Path.GetDirectoryName(memoryRoot),path));
        if(!actual.SetEquals(expected))
            throw new InvalidDataException("Portable package has extra or missing PeopleMemory chunks outside this save's exact ancestry.");
    }

    private static string RelativeChunkPath(string archiveId,string hash,string extension)
    {return "PeopleMemory/"+archiveId+"/"+hash.Substring(0,2)+"/"+hash+extension;}

    private static byte[] ReadChunk(string path,string expectedHash,int maximumBytes)
    {
        if(!File.Exists(path)) throw new FileNotFoundException("NPC memory archive is missing. Restore PeopleMemory with this save; no blank history was substituted.",path);
        if(new FileInfo(path).Length>maximumBytes) throw new InvalidDataException("Conversation archive chunk is too large.");
        try
        {
            using(var file=File.OpenRead(path))
            using(var gzip=new GZipStream(file,CompressionMode.Decompress))
            using(var memory=new MemoryStream())
            {
                byte[] buffer=new byte[8192]; int count;
                while((count=gzip.Read(buffer,0,buffer.Length))>0)
                {
                    if(memory.Length+count>maximumBytes) throw new InvalidDataException("Expanded memory archive chunk is too large.");
                    memory.Write(buffer,0,count);
                }
                byte[] bytes=memory.ToArray();
                using(var sha=SHA256.Create())
                    if(ToHex(sha.ComputeHash(bytes))!=expectedHash) throw new InvalidDataException("NPC memory archive checksum failed; restore a backup of PeopleMemory.");
                return bytes;
            }
        }
        catch(FileNotFoundException) {throw;}
        catch(InvalidDataException) {throw;}
        catch(IOException e) {throw new InvalidDataException("Memory archive chunk is unreadable; restore a matching PeopleMemory backup.",e);}
    }

    private static string ReadChunkParent(byte[] bytes,int magic)
    {
        using(var input=new MemoryStream(bytes,false))
        using(var reader=new BinaryReader(input,new UTF8Encoding(false,true)))
        {
            int actualMagic=reader.ReadInt32();
            if(actualMagic!=magic && !(magic==ChunkMagic && actualMagic==ChunkMagicWithProvenance))
                throw new InvalidDataException("Unsupported conversation archive format.");
            int length=reader.ReadInt32();
            if(length<0 || length>64) throw new InvalidDataException("Invalid memory parent hash.");
            byte[] value=reader.ReadBytes(length);
            if(value.Length!=length) throw new EndOfStreamException("Incomplete memory parent hash.");
            string parent=new UTF8Encoding(false,true).GetString(value);
            if(parent.Length!=0 && !IsLowerHex(parent,64)) throw new InvalidDataException("Invalid memory parent hash.");
            return parent;
        }
    }

    private static void PreflightExistingChunks(string destinationRoot,ConversationArchiveCheckpoint checkpoint,
        List<string> ancestry,ConversationCueArchiveCheckpoint cueCheckpoint,List<string> cueAncestry)
    {
        if(ancestry.Count==0 && cueAncestry.Count==0) return;
        RejectReparsePath(destinationRoot);
        PreflightExistingChunkSet(destinationRoot,checkpoint?.ArchiveId,ancestry,".pim",MaximumChunkBytes);
        PreflightExistingChunkSet(destinationRoot,cueCheckpoint?.ArchiveId,cueAncestry,".pic",MaximumCueChunkBytes);
    }

    private static void PreflightExistingChunkSet(string destinationRoot,string archiveId,
        List<string> ancestry,string extension,int maximumBytes)
    {
        foreach(string hash in ancestry)
        {
            string relative=Path.Combine(archiveId,hash.Substring(0,2),hash+extension);
            string existing=Path.Combine(destinationRoot,relative);
            RejectReparsePath(existing);
            if(Directory.Exists(existing)) throw new IOException("A PeopleMemory chunk path is occupied by a directory; nothing was overwritten.");
            if(!File.Exists(existing)) continue;
            ReadChunk(existing,hash,maximumBytes);
        }
    }

    private static void RejectReparsePath(string path)
    {
        string full=Path.GetFullPath(path);
        string current=full;
        while(!string.IsNullOrEmpty(current))
        {
            if(File.Exists(current) || Directory.Exists(current))
                if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new IOException("Linked PeopleMemory paths are unsupported.");
            string parent=Path.GetDirectoryName(current);
            if(string.Equals(parent,current,StringComparison.OrdinalIgnoreCase)) break;
            current=parent;
        }
    }

    private static long CopyAndHash(Stream input,Stream output,HashAlgorithm sha,long maximum)
    {
        byte[] buffer=new byte[81920]; long total=0; int count;
        while((count=input.Read(buffer,0,buffer.Length))>0)
        {
            total=checked(total+count);
            if(total>maximum) throw new InvalidDataException("Portable package entry exceeds its size limit.");
            output.Write(buffer,0,count);
            sha?.TransformBlock(buffer,0,count,buffer,0);
        }
        sha?.TransformFinalBlock(Array.Empty<byte>(),0,0);
        return total;
    }

    private static string RelativePackagePath(string root,string path)
    {
        string relative=Path.GetRelativePath(root,path).Replace(Path.DirectorySeparatorChar,'/').Replace(Path.AltDirectorySeparatorChar,'/');
        if(relative.StartsWith("../",StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidDataException("Package file path escapes its root.");
        return relative;
    }

    private static string NormalizeArchivePath(string path)
    {
        if(path==null) throw new InvalidDataException("Portable package contains an empty path.");
        return path.Replace('\\','/');
    }

    private static bool IsWithin(string root,string path)
    {
        string fullRoot=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
        string fullPath=Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot,StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        int unixType=(entry.ExternalAttributes>>16)&0xf000;
        return unixType==0xa000;
    }

    private static bool IsLowerHex(string value,int length)
    {
        return value!=null && value.Length==length && value.All(c=>(c>='0' && c<='9') || (c>='a' && c<='f'));
    }

    private static string ToHex(byte[] value)
    {return BitConverter.ToString(value).Replace("-","").ToLowerInvariant();}

    private static JsonSerializerOptions ManifestJsonOptions()
    {
        return new JsonSerializerOptions {PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=false,MaxDepth=32};
    }
}
