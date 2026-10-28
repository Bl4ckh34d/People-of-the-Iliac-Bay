using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class RendererHandoff
{
    public static void Verify()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory!=null&&!File.Exists(Path.Combine(directory.FullName,"mod/Scripts/CompletedNpcPose.cs")))directory=directory.Parent;
        if(directory==null)throw new Exception("Source root missing");
        var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(directory.FullName,"mod/Scripts/CompletedNpcPose.cs")));
        string release=tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m=>m.Identifier.ValueText=="Release").ToFullString();
        string fixture=@"
using System;
public class Native {public bool enabled=true,forceRenderingOff; public static implicit operator bool(Native x){return x!=null;}}
public class Visual {public bool active=true;public void SetActive(bool v){active=v;}public static implicit operator bool(Visual x){return x!=null;}}
public class Keeper {public bool active;public void SetStanding(bool v){active=v;}public static implicit operator bool(Keeper x){return x!=null;}}
public class Fixture {
Native original=new Native();Visual visual=new Visual();Keeper keeper;
bool ownsRenderer=true,originalForceOff,keeperWasActive;
// RELEASE
static void Check(bool value,string name){if(!value)throw new Exception(name);}
public static void Run(){
var pose=new Fixture();pose.original.forceRenderingOff=true;
pose.original.enabled=false; // SleepingPose has just acquired the native body.
pose.Release();Check(!pose.original.enabled,""Release must not undo sleep/concealment visibility"");
Check(!pose.original.forceRenderingOff&&!pose.visual.active,""Release removes own draw suppression and own visual"");
pose.Release();Check(!pose.original.enabled,""Repeated release preserves native owner"");
pose=new Fixture();pose.originalForceOff=true;pose.original.forceRenderingOff=true;
pose.Release();Check(pose.original.forceRenderingOff,""Previously suppressed native renderer stays suppressed"");
pose=new Fixture();pose.keeper=new Keeper();pose.keeperWasActive=true;pose.Release();
Check(pose.keeper.active,""Prior keeper pose restored"");
pose=new Fixture();pose.keeper=new Keeper();pose.keeperWasActive=false;pose.Release();
Check(!pose.keeper.active,""Inactive keeper not spuriously started"");
Console.WriteLine(""Completed NPC actual renderer release: 6 passed"");
}}
".Replace("// RELEASE",release);
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("CompletedNpcRendererHandoff",new[]{CSharpSyntaxTree.ParseText(fixture)},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new Exception(string.Join(Environment.NewLine,result.Diagnostics));
        Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
