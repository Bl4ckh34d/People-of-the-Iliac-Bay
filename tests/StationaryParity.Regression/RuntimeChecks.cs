using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class RuntimeChecks
{
    internal static int Run()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/NativeStationaryLife.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/NativeStationaryLife.cs")));
        string methods=string.Concat(tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method=>new[]{"BeginPlayerHit","Died"}.Contains(method.Identifier.ValueText)).Select(method=>method.ToFullString()));
        string fixture=File.ReadAllText(Path.Combine(root.FullName,"tests/StationaryParity.Regression/Fixture.cs")).Replace("// ACTUAL_METHODS",methods);
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("StationaryLifeFixture",new[]{CSharpSyntaxTree.ParseText(fixture)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
