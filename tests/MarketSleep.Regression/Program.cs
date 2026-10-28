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
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/StreetPopulation.MarketCommuters.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/StreetPopulation.MarketCommuters.cs")));
        var arrival=tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single(value=>
            value.Condition is InvocationExpressionSyntax call && call.Expression is IdentifierNameSyntax name && name.Identifier.ValueText=="MarketMove" &&
            call.ArgumentList.Arguments.Count>1 && call.ArgumentList.Arguments[1].Expression is IdentifierNameSyntax target && target.Identifier.ValueText=="beside");
        string source=File.ReadAllText(Path.Combine(root.FullName,"tests/MarketSleep.Regression/Fixture.cs")).Replace("// ARRIVAL_BLOCK",arrival.ToFullString());
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("MarketSleepFixture",new[]{CSharpSyntaxTree.ParseText(source)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
