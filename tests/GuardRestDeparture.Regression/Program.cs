using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
static class Program
{
    static int Main()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/GuardDayRest.cs")))root=root.Parent;
        var method=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/GuardDayRest.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="PrepareDayRest");
        string source=File.ReadAllText(Path.Combine(root.FullName,"tests/GuardRestDeparture.Regression/Fixture.cs"))
            .Replace("// PRODUCTION_METHOD",method.ToFullString());
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("GuardRestFixture",new[]{CSharpSyntaxTree.ParseText(source)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(output.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
