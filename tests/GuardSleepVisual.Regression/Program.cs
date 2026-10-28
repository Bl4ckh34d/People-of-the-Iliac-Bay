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
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/SleepingPose.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        string Read(string path)=>File.ReadAllText(Path.Combine(root.FullName,path));
        var actor=CSharpSyntaxTree.ParseText(Read("mod/Scripts/GuardActor.cs")).GetRoot();
        var rest=actor.DescendantNodes().OfType<IfStatementSyntax>().Single(value=>
            value.Condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(name=>name.Identifier.ValueText=="atRestBed") &&
            value.Condition.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Any(name=>name.Identifier.ValueText=="indoors"));
        string died=actor.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="Died").ToFullString();
        string combat=actor.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="BeginCombat").ToFullString();
        string defense=CSharpSyntaxTree.ParseText(Read("mod/Scripts/GuardEnemyCombat.cs")).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="BeginDefenseCombat").ToFullString();
        string fixture=Read("tests/GuardSleepVisual.Regression/Fixture.cs").Replace("// REST_BLOCK",rest.ToFullString()).Replace("// DIED_METHOD",died).Replace("// COMBAT_METHODS",combat+defense);
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p));
        var sources=new[]{fixture,Read("mod/Scripts/SleepingPose.cs"),Read("../daggerfall-unity/Assets/Scripts/Game/Entities/EntityConcealmentBehaviour.cs")};
        var compilation=CSharpCompilation.Create("GuardSleepFixture",sources.Select(value=>CSharpSyntaxTree.ParseText(value)),references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
