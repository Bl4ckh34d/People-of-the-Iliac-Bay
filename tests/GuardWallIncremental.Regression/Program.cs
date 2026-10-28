using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PeopleOfTheIliacBay.Core;
static class Program
{
    static int Main()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/GuardWallWatch.cs")))root=root.Parent;
        string Method(string file,string name)=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,file))).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText==name).ToFullString();
        var source=File.ReadAllText(Path.Combine(root.FullName,"tests/GuardWallIncremental.Regression/Fixture.cs"))
            .Replace("// ACTUAL_ASSIGNMENT",Method("mod/Scripts/GuardWallWatch.cs","AssignReadyWallWatch"))
            .Replace("// ACTUAL_GAP",Method("mod/Scripts/GuardNightPatrols.cs","Gap")+Method("mod/Scripts/GuardGateDuty.cs","AtPoint")+
                Method("mod/Scripts/GuardWallWatch.cs","WallPostGap"));
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path)).ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(GuardRoster).Assembly.Location));
        var compilation=CSharpCompilation.Create("IncrementalWallFixture",new[]{CSharpSyntaxTree.ParseText(source)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
