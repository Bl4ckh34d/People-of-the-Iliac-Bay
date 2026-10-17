using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using LivingCities.Core;

internal static class Program
{
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
        catch(Exception e) {Console.Error.WriteLine("Memory save transfer failed: "+e.Message); return 1;}
    }
    private static ConversationArchiveCheckpoint Checkpoint(string save)
    {
        string file=Path.Combine(save,"mod_livingcities.txt");
        using(var json=JsonDocument.Parse(File.ReadAllText(file),new JsonDocumentOptions {MaxDepth=128}))
        {
            JsonElement value;
            if(!json.RootElement.TryGetProperty("ConversationArchive",out value) || value.ValueKind==JsonValueKind.Null) return null;
            return ConversationArchiveCheckpoint.CopyValidated(JsonSerializer.Deserialize<ConversationArchiveCheckpoint>(value.GetRawText(),
                new JsonSerializerOptions {IncludeFields=true}));
        }
    }
    private static void Export(string source,string output)
    {
        source=Path.GetFullPath(source); output=Path.GetFullPath(output);
        if(File.Exists(output) || Directory.Exists(output)) throw new IOException("Export destination already exists.");
        var checkpoint=Checkpoint(source);
        string stage=Path.Combine(Path.GetTempPath(),"people-export-"+Guid.NewGuid().ToString("N"));
        string temporary=output+"."+Guid.NewGuid().ToString("N")+".tmp";
        Directory.CreateDirectory(stage);
        try
        {
            CopySave(source,Path.Combine(stage,"Save"));
            new SharedConversationArchive(Path.Combine(Directory.GetParent(source).FullName,"PeopleMemory"))
                .CopyCheckpointTo(checkpoint,Path.Combine(stage,"PeopleMemory"));
            // Refuse a save changed by a concurrent game save during copying.
            var copied=Checkpoint(Path.Combine(stage,"Save"));
            if((copied?.ArchiveId)!=(checkpoint?.ArchiveId) || (copied?.Head)!=(checkpoint?.Head))
                throw new IOException("Save changed during export. Close DFU and retry.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            ZipFile.CreateFromDirectory(stage,temporary,CompressionLevel.Optimal,false);
            File.Move(temporary,output);
            Console.WriteLine("Exported save and its exact memory ancestry: "+output);
        }
        finally {Directory.Delete(stage,true); if(File.Exists(temporary)) File.Delete(temporary);}
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
        if(!Regex.IsMatch(slot,@"^SAVE[0-9]+$")) throw new ArgumentException("Destination must be a new SAVE number.");
        saves=Path.GetFullPath(saves); string target=Path.Combine(saves,slot);
        if(Directory.Exists(target) || File.Exists(target)) throw new IOException("Destination save already exists; it will not be overwritten.");
        Directory.CreateDirectory(saves);
        string stage=Path.Combine(saves,".people-import-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            using(var zip=ZipFile.OpenRead(package))
            {
                long total=0;
                foreach(var entry in zip.Entries)
                {
                    string name=entry.FullName.Replace('\\','/');
                    if(!(name.StartsWith("Save/",StringComparison.Ordinal) || name.StartsWith("PeopleMemory/",StringComparison.Ordinal)) ||
                        name.Contains(":") || Array.Exists(name.Split('/'),part=>part==".." || part=="."))
                        throw new InvalidDataException("Unsafe or unexpected archive path.");
                    total=checked(total+entry.Length); if(total>4L*1024*1024*1024) throw new InvalidDataException("Save export exceeds 4 GiB import limit.");
                    string path=Path.GetFullPath(Path.Combine(stage,name));
                    if(!path.StartsWith(stage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archive path escapes import folder.");
                    if(name.EndsWith("/",StringComparison.Ordinal)) {Directory.CreateDirectory(path); continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var input=entry.Open()) using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)) input.CopyTo(output);
                }
            }
            var checkpoint=Checkpoint(Path.Combine(stage,"Save"));
            new SharedConversationArchive(Path.Combine(stage,"PeopleMemory")).CopyCheckpointTo(checkpoint,Path.Combine(saves,"PeopleMemory"));
            Directory.Move(Path.Combine(stage,"Save"),target);
            Console.WriteLine("Imported save and verified memory ancestry: "+target);
        }
        finally {Directory.Delete(stage,true);}
    }
}
