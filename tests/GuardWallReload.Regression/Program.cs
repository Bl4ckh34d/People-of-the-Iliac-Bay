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
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/GuardWallTrespass.cs")))root=root.Parent;
        var method=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/GuardWallTrespass.cs"))).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="IsWallIntruder");
        var text=File.ReadAllText(Path.Combine(root.FullName,"tests/GuardWallReload.Regression/Fixture.cs")).Replace("// ACTUAL_METHOD",method.ToFullString());
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path)).ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(GuardRoster).Assembly.Location));
        var compilation=CSharpCompilation.Create("WallReloadFixture",new[]{CSharpSyntaxTree.ParseText(text)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(output.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
