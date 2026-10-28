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
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/StreetPopulation.Actions.cs")))root=root.Parent;
        var method=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/StreetPopulation.Actions.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="ApplyNpcActionOverlay");
        // Execute the actual motion-owner dispatch before ordinary action/routine
        // handling. Native boundary doubles observe which owner got this body.
        var prefix=method.Body.Statements.TakeWhile(statement=>!statement.DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>().Any(value=>value.Name.Identifier.ValueText=="OwnsDungeonFollow"));
        var source=File.ReadAllText(Path.Combine(root.FullName,"tests/ComplaintActorOwnership.Regression/Fixture.cs"))
            .Replace("// ACTUAL_OWNER_DISPATCH",string.Join(Environment.NewLine,prefix.Select(value=>value.ToFullString())));
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path)).ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(Resident).Assembly.Location));
        var compilation=CSharpCompilation.Create("ComplaintOwnerFixture",new[]{CSharpSyntaxTree.ParseText(source)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
